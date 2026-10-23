using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Retro.Ai;
using Retro.Fusion;

namespace Retro.App;

public sealed record SavedModificationReport(string Provider, DateTimeOffset At, long Frame, string Wish,
    string Analysis, IReadOnlyList<ModificationIdea> Ideas);
public sealed record SavedModifications(int Version, string Key, string Machine,
    Dictionary<string, GameVariable> Variables, Dictionary<string, string> Labels,
    IReadOnlyList<SavedModificationReport> Reports, IReadOnlyList<VariableEdit> Changes, bool Reapply);

/// <summary>Private, opt-in experiments for exact media and firmware. Never a verified fusion profile.</summary>
public sealed class ModificationHistory(string? folder = null, bool persist = true)
{
    public const int ReportLimit = 12;
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly string _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "modificaciones");
    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);
    public string? Problem { get; private set; }

    public static string? Key(string machine, IEnumerable<string> mediaHashes, string firmware)
    {
        var hashes = mediaHashes.Select(h => h.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (hashes.Length == 0) return null;
        if (string.IsNullOrWhiteSpace(machine) || hashes.Any(h => !ValidKey(h))) throw new ArgumentException("Identidad de juego inválida.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machine + "\0" + firmware + "\0" + string.Join("", hashes))));
    }

    private static bool ValidKey(string key) => key is { Length: 64 } && key.All(char.IsAsciiHexDigit);
    private string FileFor(string key) => ValidKey(key) ? Path.Combine(_folder, key + ".json") : throw new ArgumentException("Identidad de historial inválida.");

    public SavedModifications? Read(string key, string machine, int addressBits)
    {
        string path = FileFor(key); Problem = null;
        if (!persist) return null;
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException();
            var saved = JsonSerializer.Deserialize<SavedModifications>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            Validate(saved, key, machine, addressBits);
            _unreadable.Remove(key);
            return saved;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            _unreadable.Add(key);
            Problem = "No se pudo leer el historial de este juego. Se conserva el archivo; podés borrarlo desde Historial guardado.";
            return null;
        }
    }

    public bool Write(SavedModifications saved, int addressBits)
    {
        if (!persist) return true;
        string path = FileFor(saved.Key), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        if (_unreadable.Contains(saved.Key)) return false;
        try
        {
            Validate(saved, saved.Key, saved.Machine, addressBits);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(saved);
            if (bytes.Length > MaxBytes) throw new InvalidDataException();
            Directory.CreateDirectory(_folder);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
            Problem = null;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        { Problem = "No se pudo guardar el historial. Los cambios siguen disponibles en esta sesión."; return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public bool Delete(string key)
    {
        try
        {
            if (persist) File.Delete(FileFor(key));
            _unreadable.Remove(key); Problem = null; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No se pudo borrar el historial guardado. El archivo permanece en su sitio."; return false; }
    }

    private static void Validate(SavedModifications saved, string key, string machine, int bits)
    {
        if (saved.Version != 1 || saved.Key != key || saved.Machine != machine ||
            saved.Variables == null || saved.Variables.Count > 256 || saved.Labels == null ||
            saved.Reports == null || saved.Reports.Count > ReportLimit || saved.Changes == null || saved.Changes.Count > 256)
            throw new InvalidDataException();
        var profile = new GameProfile { Machine = machine, Variables = saved.Variables };
        foreach (var (name, variable) in saved.Variables)
        {
            if (name.Length is < 1 or > 100 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || variable == null ||
                variable.Description == null || variable.Description.Length > 4000) throw new InvalidDataException();
            _ = VariableMemory.Cells(variable, bits);
            var copy = JsonSerializer.Deserialize<GameVariable>(JsonSerializer.Serialize(variable))!;
            copy.ReadOnly = false;
            GameModifications.Validate(new GameProfile { Variables = new() { [name] = copy } }, [new(name, copy.Min, false)], bits);
        }
        if (saved.Labels.Any(l => !saved.Variables.ContainsKey(l.Key) || l.Value == null || l.Value.Length > 160)) throw new InvalidDataException();
        foreach (var report in saved.Reports)
        {
            if (report == null || report.Provider == null || report.Provider.Length > 300 || report.At == default || report.Frame < 0 ||
                report.Wish == null || report.Wish.Length > 2000 || report.Analysis == null || report.Analysis.Length > 8000 ||
                report.Ideas == null || report.Ideas.Count > 4) throw new InvalidDataException();
            foreach (var idea in report.Ideas)
            {
                if (idea == null || idea.Title == null || idea.Title.Length > 100 || idea.Effect == null || idea.Effect.Length > 1600 ||
                    idea.Changes == null || idea.Changes.Any(c => c == null)) throw new InvalidDataException();
                GameModifications.Validate(profile, idea.Changes, bits);
            }
        }
        if (saved.Changes.Any(c => c == null)) throw new InvalidDataException();
        foreach (var batch in saved.Changes.Chunk(8)) GameModifications.Validate(profile, batch, bits);
        VariableMemory.ValidateOverlap(profile, saved.Changes.Select(c => c.Variable), machine);
    }
}