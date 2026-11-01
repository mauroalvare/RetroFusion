using System.Numerics;
using System.Text.Json;
using ImGuiNET;
using Retro.Ai;
using Retro.App;
using Retro.Asm;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class CreatorInteractionTests
{
    [TestCase(1280, 840, 1)] [TestCase(640, 480, 2)]
    public async Task AManualAnswerCanBeTestedAndCancelledByClickWithoutStartingTheGame(int width, int height, int scale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom"))) dir = dir.Parent;
        if (dir == null) Assert.Ignore("Falta el firmware local de prueba.");
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-creator-click-" + Guid.NewGuid().ToString("N"))).FullName;
        string previous = CreatedGames.UserFolder; CreatedGames.UserFolder = folder;
        nint context = ImGui.CreateContext(); CreatorWindow? window = null;
        var played = new List<string>();
        try
        {
            var io = ImGui.GetIO(); io.DisplaySize = new(width, height); io.DeltaTime = 1f / 60;
            unsafe { io.NativePtr->IniFilename = null; }
            io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out nint _, out int _, out int _, out int _);
            UiTheme.Apply(scale, false);
            window = new CreatorWindow(Path.Combine(dir!.FullName, "roms"), new AiSettings { Provider = "manual" }, played.Add);
            var bounds = new Dictionary<string, (Vector2 Min, Vector2 Max)>();
            var active = new Dictionary<string, bool>();
            Vector2 windowMin = default, windowMax = default;
            window.ControlBounds = (name, min, max) =>
            {
                if (name == "Panel") { windowMin = min; windowMax = max; return; }
                bounds[name] = (min, max); active[name] = ImGui.IsItemActive();
            };
            window.Open();
            void Frame() { bounds.Clear(); active.Clear(); ImGui.NewFrame(); window.Draw(); ImGui.Render(); }
            void Click(string name)
            {
                for (int attempt = 0; attempt < 45; attempt++)
                {
                    Frame();
                    if (!bounds.ContainsKey(name))
                    {
                        string? error = typeof(CreatorWindow).GetField("_problem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) as string;
                        Assert.Fail(name + ": " + error);
                    }
                    var b = bounds[name]; var point = (b.Min + b.Max) / 2;
                    if (point.Y > windowMin.Y + UiTheme.Px(15) && point.Y < windowMax.Y - UiTheme.Px(15) && point.X > 0 && point.X < width)
                    {
                        io.AddMousePosEvent(point.X, point.Y); Frame();
                        io.AddMouseButtonEvent(0, true); Frame(); io.AddMouseButtonEvent(0, false); Frame(); return;
                    }
                    // Scroll the parent margin rather than the multiline editor's own child window.
                    io.AddMousePosEvent(windowMin.X + 5, (windowMin.Y + windowMax.Y) / 2);
                    io.AddMouseWheelEvent(0, point.Y >= windowMax.Y - UiTheme.Px(15) ? -3 : 3); Frame();
                }
                Assert.Fail("Control fuera del área accesible: " + name);
            }
            Frame(); Frame(); Click("Preparar"); Click("Respuesta");
            Assert.That(active["Respuesta"], Is.True, "The response editor must actually receive input");
            io.AddInputCharactersUTF8(JsonSerializer.Serialize(new { title = "Propio", pitch = "Prueba", howToPlay = "Controles", source = Templates.All[0].Source }));
            Frame(); Click("Probar"); Click("Cancelar");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (bounds.ContainsKey("Cancelar") && clock.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Delay(5); Frame(); }
            Assert.That(bounds.ContainsKey("Cancelar"), Is.False, "The click must stop the trial without invoking CancelWork as a shortcut");
            Assert.That(played, Is.Empty, "Cancellation must not save/start a partially tested answer");
            Assert.That(CreatedGames.All(), Is.Empty);
        }
        finally
        {
            if (window != null) { try { await window.CancelWork(); } catch (OperationCanceledException) { } }
            UiTheme.Apply(1, false); ImGui.DestroyContext(context); CreatedGames.UserFolder = previous;
            Assert.That(Path.GetFullPath(folder), Does.StartWith(Path.GetTempPath())); Directory.Delete(folder, true);
        }
    }
}
