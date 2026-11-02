using Retro.App;
using Retro.Update;

int requirementsIndex = Array.FindIndex(args, arg => arg is "--check-startup" or "--comprobar-arranque");
if (requirementsIndex >= 0)
{
    int languageIndex = Array.IndexOf(args, "--language");
    UiText.Language = languageIndex >= 0 && languageIndex + 1 < args.Length && args[languageIndex + 1] is "es" or "en" ? args[languageIndex + 1] : "es";
    Environment.ExitCode = requirementsIndex + 1 < args.Length && !args[requirementsIndex + 1].StartsWith("--", StringComparison.Ordinal)
        ? StartupRequirements.Write(args[requirementsIndex + 1], AppContext.BaseDirectory, !StartupRequirements.WantsAudio(args)) : 2;
    return;
}

UiText.Language = UiText.StartupLanguage(args);

if (args.Length == 2 && args[0] == "--apply-update")
{
    Environment.ExitCode = await UpdateRunner.RunHelperAsync(args[1]);
    return;
}
if (args.Length == 4 && args[0] == "--recover-update" &&
    int.TryParse(args[2], out int recoveryParent) && long.TryParse(args[3], out long recoveryStarted))
{
    Environment.ExitCode = await UpdateRunner.RecoverAsync(args[1], recoveryParent, recoveryStarted);
    return;
}
string? ready = null;
int readyIndex = Array.IndexOf(args, "--update-ready");
if (readyIndex >= 0 && readyIndex + 1 < args.Length) ready = args[readyIndex + 1];
if (ready == null && UpdateRunner.RecoverBeforeStart()) return;

if (!args.Contains("--startup-child"))
{
    Environment.ExitCode = await StartupLauncher.RunAsync(args);
    return;
}
try { AppStartup.Run(args, ready, readyIndex); }
catch (Exception error)
{
    int markerIndex = Array.IndexOf(args, "--startup-ready");
    if (markerIndex >= 0 && markerIndex + 1 < args.Length)
    {
        // A WinExe has no console. Report to the supervisor through its private startup folder.
        try { File.WriteAllText(args[markerIndex + 1] + ".error", StartupReport.Describe("RetroEmu no pudo terminar de abrir.", error)); }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
    }
    else if (!args.Contains("--autotest")) StartupReport.Record("RetroEmu no pudo terminar de abrir.", error);
    Console.Error.WriteLine(error);
    Environment.ExitCode = 2;
}
