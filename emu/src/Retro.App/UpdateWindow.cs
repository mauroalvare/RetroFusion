using System.Collections.Concurrent;
using System.Numerics;
using ImGuiNET;
using Retro.Update;

namespace Retro.App;

/// <summary>Startup updates happen before the library can start a game; manual checks wait for a click.</summary>
public sealed class UpdateWindow : IDisposable
{
    private readonly string _root, _settings;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Action<UpdatePlan> _install;
    private readonly ConcurrentQueue<UpdateProgress> _messages = new();
    private readonly UpdatePreferences _preferences;
    private readonly bool _packaged;
    private CancellationTokenSource? _cancel;
    private Task<UpdatePlan?>? _task;
    private UpdatePlan? _ready;
    private bool _open, _startup, _cancelled;
    private string _status = "", _error = "";
    private double? _fraction;
    public bool RestartRequested { get; private set; }
    public bool Blocking => _open;

    public UpdateWindow(string[] args, HttpClient? http = null, string? root = null, string? settings = null, Action<UpdatePlan>? install = null)
    {
        _root = Path.GetFullPath(root ?? AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        _settings = settings ?? UpdatePreferences.Folder;
        _preferences = UpdatePreferences.Load(_settings);
        _ownsHttp = http == null; _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _install = install ?? UpdateRunner.StartHelper;
        _packaged = ReleaseClient.Runtime != null && File.Exists(Path.Combine(_root, PackageManifest.FileName))
            && !Path.GetFullPath(_root).Split(Path.DirectorySeparatorChar).Contains(".retroemu-updates");
        bool testing = args.Any(a => a is "--hidden" or "--autotest" or "--no-update" or "--update-ready");
        bool openingFile = args.Any(File.Exists);
        if (_packaged && !testing && !openingFile && _preferences.Automatic) Begin(startup: true);
    }

    public void Open() { if (_task == null && _ready == null) Begin(startup: false); else { _startup = false; _open = true; } }
    public void Skip() { _cancelled = true; _cancel?.Cancel(); _open = false; }

    private void Begin(bool startup)
    {
        _open = true; _startup = startup; _cancelled = false; _error = ""; _status = "Buscando actualizaciones..."; _fraction = null;
        if (!_packaged) { _status = "Las actualizaciones automáticas están disponibles en el paquete instalado."; return; }
        _cancel?.Dispose(); _cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = _cancel.Token;
        _task = Task.Run(async () =>
        {
            var current = PackageManifest.Read(_root);
            if (current.Runtime != ReleaseClient.Runtime || current.Version != UpdateRunner.AppVersion.ToString(3)) throw new InvalidDataException("El registro no corresponde al programa instalado.");
            var client = new ReleaseClient(_http);
            using var check = CancellationTokenSource.CreateLinkedTokenSource(ct); check.CancelAfter(TimeSpan.FromSeconds(6));
            var asset = await client.CheckAsync(ReleaseClient.ParseVersion(current.Version), current.Runtime, check.Token);
            if (asset == null) return null;
            if (startup && _preferences.FailedVersion == asset.Version.ToString(3)) throw new IOException("La última actualización no se pudo completar. Podés reintentar desde Ayuda > Actualizaciones.");
            _messages.Enqueue(new($"Preparando RetroEmu {asset.Version.ToString(3)}..."));
            return await UpdatePreparation.PrepareAsync(client, asset, _root, new QueueProgress(_messages), ct);
        }, ct);
    }

    private sealed class QueueProgress(ConcurrentQueue<UpdateProgress> queue) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => queue.Enqueue(value); }

    public void Poll()
    {
        while (_messages.TryDequeue(out var message)) { _status = message.Message; _fraction = message.Fraction; }
        if (_task is not { IsCompleted: true }) return;
        var task = _task; _task = null;
        try
        {
            _ready = task.GetAwaiter().GetResult();
            if (_cancelled) { _ready = null; return; }
            _status = _ready == null ? "Ya tenés la versión más reciente." : $"RetroEmu {_ready.ToVersion} está listo para instalar.";
            if (_startup)
            {
                if (_ready == null) _open = false;
                else Install();
            }
        }
        catch (Exception error)
        {
            if (!_cancelled) { _status = "No se pudo actualizar. Podés seguir usando esta versión."; _error = error.GetBaseException().Message; }
            if (_startup) _open = false;
        }
    }

    private void Install()
    {
        if (_ready == null) return;
        try { _install(_ready); RestartRequested = true; _status = "Cerrando para instalar y volver a abrir..."; }
        catch (Exception error) { _status = "No se pudo iniciar la actualización."; _error = error.Message; _startup = false; }
    }

    public void Draw()
    {
        const string title = "Actualizaciones de RetroEmu";
        if (!_open && !ImGui.IsPopupOpen(title)) return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(280, 180), vp.WorkSize), Vector2.Min(UiTheme.Px(520, 400), vp.WorkSize));
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(480, 230), vp.WorkSize), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Appearing, new(.5f));
        if (_open && !ImGui.IsPopupOpen(title)) ImGui.OpenPopup(title);
        bool shown = ImGui.BeginPopupModal(title, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        if (shown)
        {
            if (!_open) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
            ImGui.TextWrapped(_status);
            if (_task != null)
            {
                ImGui.ProgressBar((float)(_fraction ?? 0), new(-1, 0), _fraction is double p ? $"{p:P0}" : "");
                if (ImGui.Button("Continuar sin actualizar")) { Skip(); ImGui.CloseCurrentPopup(); }
            }
            else if (_ready != null && !RestartRequested && ImGui.Button("Instalar y reiniciar")) Install();
            bool automatic = _preferences.Automatic;
            if (ImGui.Checkbox("Buscar e instalar al abrir", ref automatic))
            {
                _preferences.Automatic = automatic;
                try { _preferences.Save(_settings); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _error = "No pude guardar esta preferencia."; }
                if (!automatic && _startup) { Skip(); ImGui.CloseCurrentPopup(); }
            }
            if (_error.Length > 0 && ImGui.CollapsingHeader("Detalles")) ImGui.TextWrapped(_error);
            if (_task == null && !RestartRequested)
            {
                if (ImGui.Button("Volver a buscar")) { _ready = null; Begin(startup: false); }
                ImGui.SameLine(); if (ImGui.Button("Cerrar")) { _open = false; ImGui.CloseCurrentPopup(); }
            }
        }
        if (shown) ImGui.EndPopup();
    }

    public void Dispose()
    {
        _cancelled = true; _cancel?.Cancel();
        if (_ownsHttp) _http.Dispose(); _cancel?.Dispose();
    }
}
