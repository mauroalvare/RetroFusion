using System.Reflection;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Retro.App;
using Retro.Core;
using Retro.Studio;

namespace App.Tests;

[NonParallelizable]
public sealed class TerminalNativeTests
{
    [TestCase(100), TestCase(200)]
    [Explicit("Requiere gráficos nativos: usa una ventana oculta, audio silenciado y un cartucho propio.")]
    public void PlayingAndContinuingFromTheActualInputWidgetReleaseTheGuestKeyboard(int textPercent)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-terminal-native-" + Guid.NewGuid().ToString("N"))).FullName;
        string previousDirectory = Environment.CurrentDirectory, previousFusion = StudioLibrary.UserFolder, previousCreated = Retro.Ai.CreatedGames.UserFolder;
        string? previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        RetroApp? app = null; bool initialized = false;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        object Get(string field) => typeof(RetroApp).GetField(field, Private)!.GetValue(app)!;
        object? Call(string method) => typeof(RetroApp).GetMethod(method, Private)!.Invoke(app, null);
        try
        {
            Environment.CurrentDirectory = folder; StudioLibrary.UserFolder = Path.Combine(folder, "fusions"); Retro.Ai.CreatedGames.UserFolder = Path.Combine(folder, "created");
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(folder, "profiles"));
            string games = Directory.CreateDirectory(Path.Combine(folder, "games")).FullName;
            byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1; Array.Fill(rom, (byte)0xEA, 16, 16384); rom[^3] = 0x80;
            string path = Path.Combine(games, "own.nes"); File.WriteAllBytes(path, rom);
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning); Raylib.SetConfigFlags(ConfigFlags.HiddenWindow | ConfigFlags.HighDpiWindow);
            Raylib.InitWindow(textPercent == 200 ? 640 : 1280, textPercent == 200 ? 480 : 840, "RetroEmu hidden terminal regression");
            Raylib.InitAudioDevice(); rlImGui.Setup(true, true); initialized = true;
            app = new RetroApp(["--autotest", "10000", Path.Combine(folder, "unused.png"), "--hidden", "--mute", "--no-update", "--no-organize", "--interface", "hardcore", "--text-size", textPercent.ToString(), "--games-folder", games]);
            var library = (GameLibrary)Get("_library");
            Assert.That(SpinWait.SpinUntil(() => { library.Poll(); return !library.Scanning; }, TimeSpan.FromSeconds(8)), Is.True);
            for (int i = 0; i < 4; i++) app.Frame(1f / 60);
            var terminal = (ProgramTerminal)typeof(RetroApp).GetProperty("Terminal", Private)!.GetValue(app)!;
            void InputFrame() { ImGui.GetIO().DeltaTime = 1f / 60; ImGui.NewFrame(); Call("DrawTerminal"); ImGui.Render(); }
            void Submit(string command)
            {
                Call("OpenTerminal"); InputFrame(); InputFrame(); ImGui.GetIO().AddInputCharactersUTF8(command); InputFrame();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, true); InputFrame(); ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, false); InputFrame();
                Assert.That(terminal.History.Last(), Is.EqualTo(command));
            }
            Submit("cd originales"); Submit("juegos"); Submit("opcion volumen 37");
            void Key(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); InputFrame(); ImGui.GetIO().AddKeyEvent(key, false); InputFrame(); }
            Key(ImGuiKey.UpArrow); Assert.That(Get("_terminalInput"), Is.EqualTo("opcion volumen 37"));
            Key(ImGuiKey.DownArrow); Assert.That(Get("_terminalInput"), Is.EqualTo(""));
            ImGui.GetIO().AddInputCharactersUTF8("jue"); InputFrame(); Key(ImGuiKey.Tab); Assert.That(Get("_terminalInput"), Is.EqualTo("juegos "));
            Key(ImGuiKey.Enter); Submit("jugar 1");
            app.Frame(1f / 60); app.Frame(1f / 60);
            Assert.That(((EmulatorHost)Get("_host")).LoadedMediaSource, Is.EqualTo(path));
            Assert.That(Call("CaptureGuiKeyboard"), Is.EqualTo(false), "The command bar must not reclaim keyboard focus after playing.");
            Submit("estado"); Assert.That(((EmulatorHost)Get("_host")).Paused, Is.True);
            Submit("continuar"); app.Frame(1f / 60); app.Frame(1f / 60);
            Assert.That(Call("CaptureGuiKeyboard"), Is.EqualTo(false)); Assert.That(((EmulatorHost)Get("_host")).Paused, Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(rom));
        }
        finally
        {
            app?.Shutdown(); if (initialized) { rlImGui.Shutdown(); Raylib.CloseAudioDevice(); Raylib.CloseWindow(); }
            Environment.CurrentDirectory = previousDirectory; StudioLibrary.UserFolder = previousFusion; Retro.Ai.CreatedGames.UserFolder = previousCreated;
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", previousProfiles);
            Directory.Delete(folder, true);
        }
    }
}
