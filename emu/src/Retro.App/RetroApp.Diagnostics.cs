using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    /// <summary>
    /// A technical summary for reporting a problem. It says what the program is doing, never whose it is:
    /// no game titles, paths, order history, account names or settings of AI providers.
    /// </summary>
    internal string DiagnosticReport()
    {
        var text = new StringBuilder();
        text.AppendLine("RetroEmu " + Retro.Update.UpdateRunner.AppVersion.ToString(3));
        text.AppendLine($"Sistema: {RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture} · .NET {Environment.Version}");
        text.AppendLine($"Ventana: {Raylib.GetScreenWidth()}x{Raylib.GetScreenHeight()} · escala {Raylib.GetWindowScaleDPI().X:F2} · texto {_prefs.TextPercent} %");
        text.AppendLine($"Interfaz: {InterfaceStyles.Describe(_prefs.Interface).Id} · contraste {(_prefs.HighContrast ? "alto" : "normal")}");
        text.AppendLine($"Máquina: {_host.Machine?.Id ?? "ninguna"} · {(_host.Paused ? "en pausa" : "en ejecución")} · medio {(_host.LoadedMedia != null ? "cargado" : "sin cargar")}");
        text.AppendLine("Audio: " + (AudioReady ? "salida activa" : Raylib.IsAudioDeviceReady() ? "dispositivo sin canal de salida" : "sin dispositivo"));
        text.AppendLine($"Biblioteca: {_library.Entries.Count} archivos · {(_library.Scanning ? "actualizando" : "al día")}");
        text.AppendLine($"Memoria administrada: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        foreach (var task in _tasks.Active) text.AppendLine($"Tarea: {task.Kind} · {AppTaskCenter.StateName(task.State)} · {AppTaskCenter.Duration(task.Elapsed)}");
        foreach (string problem in new[] { _prefs.Problem, _sound.Problem, _gameMetadata.Problem, _library.OrganizationError }.OfType<string>())
            text.AppendLine("Aviso: " + Anonymize(problem));
        return text.ToString().TrimEnd();
    }

    /// <summary>Problems can quote a file; the user's folder is replaced before they leave the program.</summary>
    internal static string Anonymize(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        // A service account can have no name, and replacing an empty text is an error.
        return Environment.UserName.Length == 0 ? text : text.Replace(Environment.UserName, "<usuario>", StringComparison.OrdinalIgnoreCase);
    }
}
