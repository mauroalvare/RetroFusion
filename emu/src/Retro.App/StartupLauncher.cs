using System.Diagnostics;
using System.Runtime.InteropServices;
using Retro.Update;

namespace Retro.App;

/// <summary>The parent never loads the graphical or audio backends. It can recover a failed or stuck initialization.</summary>
internal static class StartupLauncher
{
    internal static bool CanOfferUpdate(string[] args) => !args.Any(arg => arg is "--no-update" or "--autotest" or "--update-ready" or "--apply-update" or "--recover-update");

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--repair"))
        {
            if (args.Contains("--autotest")) return 2;
            return await StartupRecovery.OfferAsync("Se abrió la recuperación del programa.", CanOfferUpdate(args));
        }
        string folder = Path.Combine(Path.GetTempPath(), "RetroEmu-startup-" + Guid.NewGuid().ToString("N"));
        string ready = Path.Combine(folder, "ready");
        try
        {
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = args.Contains("--hidden") ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
            // dotnet run and framework-dependent diagnostics also use the same isolated entry point.
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(StartupLauncher).Assembly.Location);
            foreach (string arg in args) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--startup-child"); start.ArgumentList.Add("--startup-ready"); start.ArgumentList.Add(ready);
            using var child = Process.Start(start) ?? throw new IOException("No se pudo iniciar el programa.");
            var clock = Stopwatch.StartNew();
            while (!child.HasExited && !File.Exists(ready) && clock.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(50);
            if (File.Exists(ready)) return 0;
            bool hung = !child.HasExited;
            if (hung) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            string problem = hung ? "El programa no terminó de abrir en un minuto." : UiText.Format($"El programa terminó antes de abrir (código {child.ExitCode}).");
            string? detail = null;
            try { if (File.Exists(ready + ".error")) detail = File.ReadAllText(ready + ".error"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            bool canUpdate = CanOfferUpdate(args);
            if (!canUpdate && args.Any(arg => arg is "--autotest" or "--update-ready" or "--apply-update" or "--recover-update"))
                return child.ExitCode == 0 ? 2 : child.ExitCode;
            return await StartupRecovery.OfferAsync(problem, canUpdate, detail);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!CanOfferUpdate(args)) { Console.Error.WriteLine(error.Message); return 2; }
            return await StartupRecovery.OfferAsync(UiText.Get("No se pudo iniciar el programa: ") + error.Message, allowUpdate: true);
        }
        finally
        {
            // This path was generated here; no directory supplied on the command line is removed.
            try { File.Delete(ready); File.Delete(ready + ".error"); Directory.Delete(folder, recursive: false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Native recovery UI: no OpenGL, raylib, sound device, CLI subscription or web browser is required.</summary>
internal static partial class StartupRecovery
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NativeMessageBox(nint window, string text, string caption, uint type);
    private static int MessageBox(nint window, string text, string caption, uint type) =>
        NativeMessageBox(window, UiText.Message(text), UiText.Get(caption), type);

    internal static async Task<int> OfferAsync(string problem, bool allowUpdate, string? detail = null)
    {
        StartupReport.Record(detail ?? problem);
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(problem + UiText.Get("\nUsá --repair en Windows para buscar una actualización sin abrir la interfaz.")); return 2; }
        if (await RepairRequirementsAsync(AppContext.BaseDirectory)) return 2;
        if (!allowUpdate) { MessageBox(0, problem + UiText.Get("\n\nLa búsqueda de actualizaciones está desactivada para este inicio."), "RetroEmu · Recuperación", 0x10); return 2; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await new StartupUpdater(new ReleaseClient(http)).RunAsync(AppContext.BaseDirectory,
                () => MessageBox(0, problem + UiText.Get("\n\n¿Querés buscar una versión nueva sin abrir la interfaz? No se instalará nada sin tu confirmación.\n\nTus juegos y partidas permanecen en su carpeta."), "RetroEmu · Recuperación", 0x24) == 6,
                asset => MessageBox(0, UiText.Format($"Está disponible RetroEmu {asset.Version.ToString(3)}.\n\n¿Descargarla, verificarla e instalarla en esta misma carpeta? Se conservan juegos, trabajos, partidas y ajustes."), "RetroEmu · Actualización", 0x24) == 6,
                UpdateRunner.StartHelper, null, timeout.Token);
            if (result == StartupUpdateResult.UpToDate)
                MessageBox(0, UiText.Get("No hay una versión más nueva. El detalle del problema está en:\n") + StartupReport.LogPath, "RetroEmu · Recuperación", 0x40);
            return result == StartupUpdateResult.Started ? 0 : 2;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            StartupReport.Record(problem, error);
            MessageBox(0, UiText.Get("No se pudo completar la actualización: ") + error.Message + UiText.Get("\n\nPodés volver a abrir RetroEmu con --repair para reintentar. Tus juegos y partidas se conservan.\n\nDetalle: ") + StartupReport.LogPath, "RetroEmu · Recuperación", 0x10);
            return 2;
        }
    }

    /// <summary>Called only by an interactive recovery or an explicit requirements action, never by a test startup.</summary>
    internal static async Task<bool> RepairRequirementsAsync(string root, nint owner = default, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var missing = OfficialComponent.MissingVisualCpp(root);
        if (missing.Count == 0) return false;
        try
        {
            using var http = OfficialComponent.CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var result = await OfficialComponent.InstallVisualCppAsync(
                () => !cancellation.IsCancellationRequested && MessageBox(owner, UiText.Get("Falta Microsoft Visual C++ x64, requerido por una biblioteca del programa:\n") + string.Join(", ", missing)
                    + UiText.Get("\n\n¿Permitís descargar el instalador oficial desde Microsoft? Todavía no se instalará nada.\n\nFuente: ") + OfficialComponent.VisualCppSource,
                    "RetroEmu · Requisitos", 0x24) == 6,
                () => !cancellation.IsCancellationRequested && MessageBox(owner, UiText.Get("La firma de Microsoft del instalador es válida.\n\n¿Permitís ejecutarlo para instalar Visual C++ x64? Windows puede pedir permiso de administrador. El instalador oficial muestra su licencia; no se reiniciará automáticamente."),
                    "RetroEmu · Requisitos", 0x24) == 6, http, cancellation: timeout.Token);
            if (result == ComponentInstallResult.Declined) return false;
            if (cancellation.IsCancellationRequested) return true;
            bool present = OfficialComponent.MissingVisualCpp(root).Count == 0;
            string message = result == ComponentInstallResult.RestartRequired
                ? "El instalador indicó que hace falta reiniciar Windows. Guardá tu trabajo y reiniciá cuando puedas."
                : present ? "El componente ya está disponible. Volvé a abrir RetroEmu para comprobar el arranque."
                : "El instalador terminó, pero la biblioteca sigue sin estar disponible. No se comprobó una reparación. Revisá el paquete y la guía oficial.";
            MessageBox(owner, UiText.Get(message), "RetroEmu · Requisitos", present ? 0x40u : 0x30u);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            StartupReport.Record("Official component repair", error);
            if (cancellation.IsCancellationRequested) return false;
            MessageBox(owner, UiText.Get("No se pudo completar la reparación del componente: ") + error.Message
                + "\n\n" + OfficialComponent.VisualCppGuide, "RetroEmu · Requisitos", 0x10);
            return false;
        }
    }
}
