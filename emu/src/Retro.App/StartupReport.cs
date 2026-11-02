using System.Runtime.InteropServices;
using System.Text;

namespace Retro.App;

/// <summary>
/// When the program cannot go on, whoever is in front of it gets a sentence and a file, not a window that
/// vanishes. It runs before and after the graphical interface exists, so it uses nothing from it.
/// </summary>
internal static partial class StartupReport
{
    public const string NoGraphics = "RetroEmu no pudo abrir su ventana porque este equipo no ofrece OpenGL 3.3.\n\n" +
        "Actualizá el controlador de la placa de video. En una máquina virtual o un escritorio remoto, activá la aceleración de gráficos o abrilo directamente en el equipo.\n\n" +
        "Tus juegos y partidas no se modificaron.";

    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroEmu", "fallo.txt");

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string caption, uint type);

    /// <summary>The text that reaches the file: what failed and where, without the user's folders.</summary>
    internal static string Describe(string message, Exception? error)
    {
        var text = new StringBuilder();
        text.AppendLine(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
        text.AppendLine("RetroEmu " + typeof(StartupReport).Assembly.GetName().Version?.ToString(3));
        text.AppendLine(RuntimeInformation.OSDescription + " · " + RuntimeInformation.ProcessArchitecture);
        text.AppendLine(message);
        if (error != null) text.AppendLine(error.ToString());
        return RetroApp.Anonymize(text.ToString());
    }

    /// <summary>Largest window that fits a screen, leaving room for the title bar and the taskbar. All sizes are in pixels.</summary>
    internal static (int Width, int Height) FittedSize(int width, int height, int screenWidth, int screenHeight) =>
        (Math.Max(Math.Min(640, width), Math.Min(width, screenWidth - 40)), Math.Max(Math.Min(480, height), Math.Min(height, screenHeight - 100)));

    /// <summary>
    /// A small or scaled screen must not get a window taller than itself, with its status bar out of reach:
    /// 1280×840 at 150 % is 1260 pixels high. The render size counts that scale; the monitor size is already in pixels.
    /// </summary>
    public static void FitWindowToMonitor()
    {
        int monitor = Raylib_cs.Raylib.GetCurrentMonitor();
        int screenWidth = Raylib_cs.Raylib.GetMonitorWidth(monitor), screenHeight = Raylib_cs.Raylib.GetMonitorHeight(monitor);
        int current = Raylib_cs.Raylib.GetRenderWidth(), currentHeight = Raylib_cs.Raylib.GetRenderHeight();
        if (screenWidth <= 0 || screenHeight <= 0 || current <= 0 || currentHeight <= 0) return;
        var (width, height) = FittedSize(current, currentHeight, screenWidth, screenHeight);
        if (width == current && height == currentHeight) return;
        Raylib_cs.Raylib.SetWindowSize(width, height);
        Raylib_cs.Raylib.SetWindowPosition(Math.Max(0, (screenWidth - width) / 2), Math.Max(30, (screenHeight - height) / 2 - 20));
    }

    public static void Fatal(string message, Exception? error = null)
    {
        string saved = "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, Describe(message, error));
            saved = "\n\nEl detalle quedó en " + LogPath;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        if (OperatingSystem.IsWindows()) MessageBox(0, message + saved, "RetroEmu", 0x10);
        else Console.Error.WriteLine(message + saved);
    }
}
