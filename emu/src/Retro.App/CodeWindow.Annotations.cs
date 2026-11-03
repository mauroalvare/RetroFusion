using System.Globalization;
using ImGuiNET;
using Retro.Core;

namespace Retro.App;

public sealed record ManualAnnotation(uint Address, string Label, string Comment, string Summary, string Pseudocode);

public sealed partial class CodeWindow
{
    private readonly string? _annotationsDirectory;
    private string _annotationId = "";
    private CodeAnnotations? _legacyNotes;
    private bool _confirmLegacy;
    private string _annotationExportPath = "";
    internal Action<string, System.Numerics.Vector2, System.Numerics.Vector2>? AnnotationControlBounds { get; set; }
    private string AnnotationId => _annotationId.Length > 0 ? _annotationId : _gameId;
    private string AnnotationDirectory => _annotationsDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "anotaciones");
    public string AnnotationProblem => _notes.Problem ?? "";
    public bool HasLegacyAnnotations => _legacyNotes != null;
    public bool TryPreserveAnnotations()
    {
        if (!_notes.HasUnsavedChanges) return true;
        _notes.Save(AnnotationId);
        return !_notes.HasUnsavedChanges || _notes.HasVerifiedExport;
    }
    public void EnsureAnnotationsPreserved()
    {
        if (!TryPreserveAnnotations()) throw new IOException(UiText.Message(_notes.Problem ?? "No se pudieron conservar las anotaciones de esta sesión."));
    }
    public string ExportAnnotations(string path)
    {
        OriginalAnnotations();
        return _notes.Export(AnnotationId, path);
    }

    private void LoadAnnotations()
    {
        _legacyNotes = null; _confirmLegacy = false;
        // Original media has an exact content identity. Fusion annotations retain their existing profile scope.
        string nextIdentity = _host.Fusion == null
            ? "edition-" + StateSlotStore.GameKey(_machine!.Id, _host.LoadedMediaHashes, _host.FirmwareIdentity)
            : _gameId;
        bool keepPending = _annotationId == nextIdentity && _notes.HasUnsavedChanges;
        _annotationId = nextIdentity;
        if (!keepPending) _notes = CodeAnnotations.Load(AnnotationId, AnnotationDirectory, requireIdentity: _host.Fusion == null);
        _annotationExportPath = Path.Combine(AnnotationDirectory, "recovery", AnnotationId + "." + Guid.NewGuid().ToString("N") + ".json");
        if (_host.Fusion != null || _gameId.Length is 0 or > 180 || _gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        if (File.Exists(Path.Combine(AnnotationDirectory, _gameId + ".json")))
        {
            var legacy = CodeAnnotations.Load(_gameId, AnnotationDirectory);
            if (legacy.Problem != null || legacy.SourceFingerprint != _notes.LegacySourceHash) _legacyNotes = legacy;
        }
    }

    private void OriginalAnnotations()
    {
        if (_host.Fusion != null || _host.Machine == null || _host.LoadedMedia == null)
            throw new InvalidOperationException("Las anotaciones por órdenes pertenecen a un original cargado.");
        Update();
    }

    private void AnnotationAddress(uint address)
    {
        int bits = _machine!.Memory.AddressBits;
        if (bits < 32 && address >= 1u << bits) throw new ArgumentOutOfRangeException(nameof(address), "La dirección de la anotación está fuera de esta máquina.");
    }

    public ManualAnnotation ReadAnnotation(uint address)
    {
        OriginalAnnotations(); AnnotationAddress(address);
        string key = CodeAnnotations.Key(address);
        return new(address, _notes.Label(address) ?? "", _notes.Comment(address) ?? "",
            _notes.Summaries.GetValueOrDefault(key, ""), _notes.Pseudocode.GetValueOrDefault(key, ""));
    }

    public IReadOnlyList<uint> AnnotationAddresses()
    {
        OriginalAnnotations();
        int bits = _machine!.Memory.AddressBits;
        return _notes.Labels.Keys.Concat(_notes.Comments.Keys).Concat(_notes.Summaries.Keys).Concat(_notes.Pseudocode.Keys)
            .Select(key => uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint a) && (bits >= 32 || a < 1u << bits) ? (uint?)a : null)
            .Where(a => a.HasValue).Select(a => a!.Value).Distinct().Order().ToArray();
    }

    /// <returns>A storage problem, retaining the edit in this session, or null after a confirmed save.</returns>
    public string? WriteAnnotation(uint address, string text, bool label = false)
    {
        OriginalAnnotations(); AnnotationAddress(address);
        if (string.IsNullOrWhiteSpace(text) || text.Length > (label ? 128 : 4096) || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("La anotación está vacía, contiene controles o supera el tamaño permitido.");
        if (label) SetLabel(address, text); else SetComment(address, text);
        return _notes.Problem;
    }

    /// <summary>Explicit, additive migration. The name-scoped source remains untouched and existing edition notes win.</summary>
    public string? ImportLegacyAnnotations(bool confirmed)
    {
        OriginalAnnotations();
        return ImportLegacy(confirmed);
    }

    private string? ImportLegacy(bool confirmed)
    {
        if (!confirmed) throw new InvalidOperationException("Confirmá que las anotaciones anteriores corresponden a esta edición.");
        if (_legacyNotes == null) throw new InvalidOperationException("No hay anotaciones anteriores por nombre para este juego.");
        if (_legacyNotes.Problem != null) throw new InvalidDataException(_legacyNotes.Problem);
        var sources = new[] { _legacyNotes.Labels, _legacyNotes.Comments, _legacyNotes.Summaries, _legacyNotes.Pseudocode, _legacyNotes.Patches };
        var targets = new[] { _notes.Labels, _notes.Comments, _notes.Summaries, _notes.Pseudocode, _notes.Patches };
        // Validate all addresses before adding anything. Invalid legacy files stay available for manual recovery.
        foreach (var source in sources)
            foreach (string key in source.Keys)
            {
                if (!uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address))
                    throw new InvalidDataException("Las anotaciones anteriores tienen direcciones inválidas. El archivo se conservó.");
                AnnotationAddress(address);
            }
        for (int i = 0; i < sources.Length; i++)
            foreach (var pair in sources[i])
                targets[i].TryAdd(CodeAnnotations.Key(uint.Parse(pair.Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), pair.Value);
        _notes.LegacySourceHash = _legacyNotes.SourceFingerprint;
        _notes.Save(AnnotationId);
        if (_notes.Problem == null) _legacyNotes = null;
        return _notes.Problem;
    }

    private void DrawLegacyAnnotations()
    {
        if (_legacyNotes == null) return;
        UiTheme.Hint("Hay anotaciones anteriores por nombre. Confirmá su edición antes de importarlas; el archivo anterior se conserva.");
        ImGui.Checkbox(UiText.Label("Confirmar edición"), ref _confirmLegacy);
        ImGui.BeginDisabled(!_confirmLegacy);
        if (ImGui.Button(UiText.Label("Importar anteriores")))
        {
            try
            {
                string? problem = ImportLegacy(_confirmLegacy);
                _status = problem ?? "Anotaciones importadas sin reemplazar las actuales. No se aplicaron parches a la memoria.";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { _status = error.Message; }
            _confirmLegacy = false;
        }
        ImGui.EndDisabled();
    }

    private void DrawUnsavedAnnotations()
    {
        if (!_notes.HasUnsavedChanges) return;
        ImGui.TextWrapped(UiText.Get("No se confirmó el guardado en el archivo habitual. Podés reintentar o exportar una copia JSON nueva."));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(UiText.Label("##annotation-export-path"), ref _annotationExportPath, 2048);
        AnnotationControlBounds?.Invoke("Ruta exportar notas", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.Button(UiText.Label("Exportar notas")))
        {
            try { _status = UiText.Get("Copia de anotaciones guardada: ") + _notes.Export(AnnotationId, _annotationExportPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException) { _status = UiText.Message(error.Message); }
        }
        AnnotationControlBounds?.Invoke("Exportar notas", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }
}
