using System.Runtime.InteropServices;
using System.Text.Json;

namespace Retro.App;

internal sealed record StartupCheck(string Id, string Status, string Detail);

/// <summary>Read-only requirements report; callable before loading graphics, audio, preferences or providers.</summary>
internal static class StartupRequirements
{
    internal static bool WantsAudio(string[] args) => !args.Contains("--no-audio");

    public static IReadOnlyList<StartupCheck> Inspect(string root, bool graphicsReady = false, bool audioDisabled = false)
    {
        bool windows = OperatingSystem.IsWindows();
        string native = Path.Combine(root, "runtimes", windows ? "win-x64" : "linux-x64", "native");
        bool Present(params string[] names) => names.Any(name => File.Exists(Path.Combine(root, name)) || File.Exists(Path.Combine(native, name)));
        bool bundled = string.Equals(Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        return
        [
            new("system", windows || OperatingSystem.IsLinux() ? "ok" : "missing",
                UiText.Get("Hay paquetes para Windows x64 y Linux x64. El soporte de cada versión del sistema depende de .NET 10 y del controlador gráfico; consultá la guía oficial.")),
            new("architecture", RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "ok" : "missing",
                UiText.Format($"Proceso: {RuntimeInformation.ProcessArchitecture}. Los paquetes se preparan para x64.")),
            new("runtime", "ok", bundled ? UiText.Get(".NET incluido en esta carpeta; no necesitás instalar el SDK.")
                : UiText.Get(".NET ya cargado. El paquete autocontenido incluye su runtime; no necesitás el SDK para jugar.")),
            new("raylib", Present(windows ? ["raylib.dll"] : ["libraylib.so", "raylib.so"]) ? "ok" : "missing",
                UiText.Get("Biblioteca gráfica raylib del paquete. Su carga se comprueba al abrir la ventana.")),
            new("imgui", Present(windows ? ["cimgui.dll"] : ["cimgui.so", "libcimgui.so"]) ? "ok" : "missing",
                UiText.Get("Biblioteca de interfaz cimgui del paquete. Su carga se comprueba al abrir la ventana.")),
            new("graphics", graphicsReady ? "ok" : "needs-check", UiText.Get(graphicsReady
                ? "Ventana gráfica inicializada. Se necesita un controlador con OpenGL 3.3."
                : "Se necesita un controlador con OpenGL 3.3. Este informe sin ventana no comprueba el controlador.")),
            new("system-libraries", windows && OfficialComponent.MissingVisualCpp(root).Count > 0 ? "missing" : windows ? "ok" : "needs-check",
                UiText.Get(windows ? "Visual C++ sólo se ofrece si una biblioteca del paquete lo requiere y falta en el sistema. No se instala por un aviso de audio."
                    : "Estas bibliotecas gráficas de Linux x64 requieren glibc 2.38 o posterior y X11/XWayland. El runtime incluido no reemplaza ICU, OpenSSL ni las bibliotecas del sistema. RetroEmu.sh --check permite revisar el paquete antes del runtime.")),
            new("audio", "optional", UiText.Get(audioDisabled ? "Audio desactivado para este inicio (--no-audio). Podés activarlo buscando la salida desde Sonido."
                : "La salida de audio es opcional. --no-audio evita inicializar el dispositivo; --mute solo silencia.")),
            new("tools", "optional", UiText.Get("Para abrir y jugar no se necesitan Python, SDK, cuenta de IA ni herramientas de IA.")),
            new("firmware", "optional", UiText.Get("El firmware depende de la máquina elegida; su ausencia no impide abrir la biblioteca ni jugar un cartucho NES."))
        ];
    }

    internal static string StatusName(string status) => UiText.Get(status switch
    { "ok" => "Disponible", "missing" => "Falta", "needs-check" => "Por comprobar", _ => "Opcional" });

    public static int Write(string path, string root, bool audioDisabled = false)
    {
        string? temporary = null;
        try
        {
            string destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var checks = Inspect(root, audioDisabled: audioDisabled);
            temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                version = typeof(StartupRequirements).Assembly.GetName().Version?.ToString(3),
                system = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                checks, graphicsDriverChecked = false,
                note = UiText.Get("Un archivo presente no certifica su carga. Abrir una ventana y probar un juego completan la comprobación.")
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, overwrite: true);
            return checks.Any(check => check.Status == "missing") ? 2 : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Console.Error.WriteLine(UiText.Get("No se pudo guardar la comprobación de arranque: ") + error.Message); return 2; }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
