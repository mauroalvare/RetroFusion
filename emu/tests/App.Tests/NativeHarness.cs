using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Retro.App;
using Retro.Studio;

namespace App.Tests;

/// <summary>
/// A hidden, muted RetroApp over isolated folders and invented media. Used only by explicit native tests:
/// it drives the real frame loop with real ImGui events and never touches the user's library or settings.
/// </summary>
internal sealed class NativeHarness : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly string _previousDirectory = Environment.CurrentDirectory, _previousFusion = StudioLibrary.UserFolder, _previousCreated = Retro.Ai.CreatedGames.UserFolder;
    private readonly string? _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
    // Creating the window and the audio device again inside one process can block the native backends.
    private static bool s_windowReady;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ClipboardReader(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ClipboardWriter(nint context, nint text);
    private static nint s_clipboard;
    private static readonly ClipboardReader ReadClipboard = _ => s_clipboard;
    private static readonly ClipboardWriter WriteClipboard = (_, text) =>
    {
        string value = text == 0 ? "" : Marshal.PtrToStringUTF8(text) ?? "";
        Marshal.FreeCoTaskMem(s_clipboard); s_clipboard = Marshal.StringToCoTaskMemUTF8(value);
    };
    public string Clipboard => Marshal.PtrToStringUTF8(s_clipboard) ?? "";
    public string Folder { get; }
    public string Games { get; }
    public RetroApp App { get; private set; } = null!;
    public Dictionary<string, (bool Visible, Vector2 Min, Vector2 Max)> Bounds { get; } = new();

    public NativeHarness(int width, int height, int textPercent, string style, int games = 12, params string[] arguments)
    {
        Folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-native-" + Guid.NewGuid().ToString("N"))).FullName;
        Games = Directory.CreateDirectory(Path.Combine(Folder, "games")).FullName;
        Environment.CurrentDirectory = Folder;
        StudioLibrary.UserFolder = Path.Combine(Folder, "fusions"); Retro.Ai.CreatedGames.UserFolder = Path.Combine(Folder, "created");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(Folder, "profiles"));
        WriteDemoLibrary(Games, games);
        if (!s_windowReady)
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
            Raylib.SetConfigFlags(ConfigFlags.HiddenWindow | ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow);
            Raylib.InitWindow(width, height, "RetroEmu hidden QA"); Raylib.SetExitKey(KeyboardKey.Null);
            // Set by the case that proves a computer without a sound device; the backend reports it just like a failed start.
            if (Environment.GetEnvironmentVariable("RETROEMU_QA_NO_AUDIO") != "1" && StartupRequirements.WantsAudio(arguments)) Raylib.InitAudioDevice();
            rlImGui.Setup(true, true); s_windowReady = true;
            unsafe { ImGui.GetIO().NativePtr->IniFilename = null; }

        }
        else Raylib.SetWindowSize(width, height);
        // All UI copy/paste paths, including workshop buttons, stay inside this test process.
        var platform = ImGui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(ReadClipboard);
        platform.Platform_SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(WriteClipboard);
        WriteClipboard(0, 0);
        string[] all = ["--autotest", "100000", Path.Combine(Folder, "unused.png"), "--hidden", "--mute", "--no-update", "--no-organize",
            "--interface", style, "--text-size", textPercent.ToString(), "--games-folder", Games, .. arguments];
        App = new RetroApp(all);
        var library = (GameLibrary)Get("_library");
        Assert.That(SpinWait.SpinUntil(() => { library.Poll(); return !library.Scanning; }, TimeSpan.FromSeconds(10)), Is.True, "The demo library did not finish scanning.");
        library.Poll();
        App.InterfaceControlBounds = (name, min, max) =>
        {
            var clipMin = ImGui.GetWindowDrawList().GetClipRectMin(); var clipMax = ImGui.GetWindowDrawList().GetClipRectMax();
            Bounds[name] = (min.X >= clipMin.X && max.X <= clipMax.X && min.Y >= clipMin.Y && max.Y <= clipMax.Y, min, max);
        };
        ((HomeScreen)Get("_home")).LibraryControlBounds = App.InterfaceControlBounds;
        Frames(5);
    }

    /// <summary>Own invented titles. One cartridge is a real iNES image of NOPs; the rest only need a recognized format.</summary>
    public static void WriteDemoLibrary(string folder, int count)
    {
        string[] names = ["Faro del taller", "Ruedas de cobre", "Cuadrícula nocturna", "Bajo el puente", "La mina de Ohm", "Sonda 4", "Tres fusibles", "Túnel de señales",
            "Oficina de cintas", "Pulso de Marte", "Relé y cerrojo", "Vía muerta", "Molino binario", "La última válvula", "Cartucho perdido", "Mar de bytes"];
        string[] genres = ["Plataformas", "Carreras", "Puzles", "Acción", "Aventura"];
        var list = new System.Text.StringBuilder("<gameList>");
        for (int i = 0; i < count; i++)
        {
            string title = i < names.Length ? names[i] : $"{names[i % names.Length]} {i / names.Length + 1}";
            string file = title.Replace(' ', '_');
            if (i % 3 == 0)
            {
                byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
                Array.Fill(rom, (byte)0xEA, 16, 16384); rom[^3] = 0x80; rom[17] = (byte)i;
                File.WriteAllBytes(Path.Combine(folder, file + ".nes"), rom); file += ".nes";
            }
            else if (i % 3 == 1) { File.WriteAllBytes(Path.Combine(folder, file + ".prg"), [1, 8, 0x60, (byte)i]); file += ".prg"; }
            else { File.WriteAllBytes(Path.Combine(folder, file + ".tap"), [3, 0, 255, (byte)i, (byte)(255 ^ i)]); file += ".tap"; }
            if (i % 4 != 3)
                list.Append($"<game><path>./{file}</path><name>{title}</name><genre>{genres[i % genres.Length]}</genre><releasedate>{1982 + i % 10}0101T000000</releasedate><publisher>Taller propio</publisher></game>");
        }
        File.WriteAllText(Path.Combine(folder, "gamelist.xml"), list.Append("</gameList>").ToString());
    }

    public object Get(string field) => App.GetType().GetField(field, Private)!.GetValue(App)!;
    public void Set(string field, object? value) => App.GetType().GetField(field, Private)!.SetValue(App, value);
    public object Property(string name) => App.GetType().GetProperty(name, Private)!.GetValue(App)!;
    public object? Call(string method, params object[] parameters) => App.GetType().GetMethod(method, Private)!.Invoke(App, parameters);

    public void Frames(int count = 1) { for (int i = 0; i < count; i++) App.Frame(1f / 60); }
    public void Key(ImGuiKey key, bool ctrl = false, bool shift = false)
    {
        var io = ImGui.GetIO();
        if (ctrl) io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        if (shift) io.AddKeyEvent(ImGuiKey.ModShift, true);
        io.AddKeyEvent(key, true); Frames(); io.AddKeyEvent(key, false); Frames();
        if (ctrl) io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        if (shift) io.AddKeyEvent(ImGuiKey.ModShift, false);
        Frames();
    }
    public void Type(string text) { ImGui.GetIO().AddInputCharactersUTF8(text); Frames(2); }
    public void Enter(string text) { Type(text); Key(ImGuiKey.Enter); }
    /// <summary>The backend reads the position from raylib every frame. A hidden window has no focus, so the real cursor stays where it is.</summary>
    public void MoveMouse(Vector2 at) => Raylib.SetMousePosition((int)at.X, (int)at.Y);
    public void Click(Vector2 at)
    {
        var io = ImGui.GetIO();
        MoveMouse(at); Frames(2); io.AddMouseButtonEvent(0, true); Frames(2); io.AddMouseButtonEvent(0, false); Frames(2);
    }
    public void Click(string control)
    {
        Assert.That(Bounds.TryGetValue(control, out var area), Is.True, "Control not drawn: " + control);
        Assert.That(area.Visible, Is.True, "Control not fully visible: " + control);
        Click((area.Min + area.Max) / 2);
    }

    /// <summary>Writes the PNG next to the other evidence when RETROEMU_QA_SHOTS names a folder.</summary>
    public void Capture(string name)
    {
        string? folder = Environment.GetEnvironmentVariable("RETROEMU_QA_SHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); Frames(3);
        typeof(RetroApp).Assembly.GetType("Retro.App.FramebufferCapture")!.GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [Path.Combine(folder, name + ".png")]);
    }

    public void Dispose()
    {
        App?.Shutdown();
        WriteClipboard(0, 0);
        Environment.CurrentDirectory = _previousDirectory; StudioLibrary.UserFolder = _previousFusion; Retro.Ai.CreatedGames.UserFolder = _previousCreated;
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        try { Directory.Delete(Folder, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* a leftover temporary folder is harmless */ }
    }
}
