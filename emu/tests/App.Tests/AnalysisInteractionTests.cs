using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Retro.Ai;
using Retro.App;
using Retro.Compat;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

/// <summary>Real ImGui mouse events on rendered controls; no physical-input claim.</summary>
[TestFixture, NonParallelizable]
public sealed class AnalysisInteractionTests
{
    [TestCase(1280, 840, 1)]
    [TestCase(640, 480, 2)]
    public async Task StartPauseAndContinueAreReachableByClickAndPreserveTheSameWork(int width, int height, int scale)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-interaction-" + Guid.NewGuid())).FullName;
        string? previous = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(folder, "profiles"));
        nint context = ImGui.CreateContext();
        var host = new EmulatorHost(); CodeWindow? window = null;
        try
        {
            var io = ImGui.GetIO(); io.DisplaySize = new(width, height); io.DeltaTime = 1f / 60;
            unsafe { io.NativePtr->IniFilename = null; }
            io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out nint _, out int _, out int _, out int _);
            UiTheme.Apply(scale, false);
            byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
            Array.Fill(rom, (byte)0xEA, 16, 16384); rom[^4] = 0; rom[^3] = 0x80;
            string path = Path.Combine(folder, "own.nes"); File.WriteAllBytes(path, rom);
            host.StartNes(MachineCatalog.Get("nes"), new MediaFile("own.nes", rom, path));
            window = new CodeWindow(host, new AiSettings { Provider = "manual" }, _ => { }, firmwareDirectory: folder);
            void Set(string name, object value) => typeof(CodeWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
            Set("_autonomousCalls", 0); Set("_autonomousFrames", 9000); Set("_autonomousPasses", 24);
            window.OpenAutomaticAnalysis();
            var bounds = new Dictionary<string, (Vector2 Min, Vector2 Max)>();
            window.AnalysisControlBounds = (name, min, max) => bounds[name] = (min, max);
            void Frame() { io.DeltaTime = 1f / 60; ImGui.NewFrame(); window.Draw(); ImGui.Render(); }
            void Click(string label)
            {
                for (int retry = 0; retry < 35; retry++)
                {
                    Frame();
                    Assert.That(bounds.ContainsKey(label), Is.True, label);
                    var b = bounds[label]; Vector2 point = (b.Min + b.Max) / 2;
                    if (point.Y > 35 && point.Y < height - 25)
                    {
                        io.AddMousePosEvent(point.X, point.Y); Frame();
                        io.AddMouseButtonEvent(0, true); Frame();
                        io.AddMouseButtonEvent(0, false); Frame(); return;
                    }
                    io.AddMousePosEvent(width / 2, height / 2); io.AddMouseWheelEvent(0, point.Y > height - 25 ? -3 : 3); Frame();
                }
                Assert.Fail("Control fuera del área accesible: " + label);
            }
            Frame(); Frame();
            byte[] state = host.Machine!.SaveState();
            Click("Iniciar investigación");
            Assert.That(window.IsWorking, Is.True);
            Click("Pausar análisis");
            await window.CancelWork().WaitAsync(TimeSpan.FromSeconds(10)); Frame();
            var store = new AutonomousResearchStore(Path.Combine(folder, ".retroemu", "analisis"));
            var first = store.Read().Single();
            Assert.That(first.Status, Is.EqualTo("paused"));
            Click("Continuar análisis");
            Assert.That(window.IsWorking, Is.True);
            Click("Pausar análisis");
            await window.CancelWork().WaitAsync(TimeSpan.FromSeconds(10)); Frame();
            Assert.That(store.Read().Single().Id, Is.EqualTo(first.Id));
            Assert.That(host.Machine.SaveState(), Is.EqualTo(state));
            Assert.That(store.Read().Single().AiCallsUsed, Is.Zero);
        }
        finally
        {
            if (window != null) { await window.CancelWork(); window.Dispose(); }
            UiTheme.Apply(1, false); ImGui.DestroyContext(context);
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", previous);
            Directory.Delete(folder, true);
        }
    }
}
