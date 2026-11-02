using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Retro.App;

namespace App.Tests;

[NonParallelizable]
public sealed class WorkshopJourneyNativeTests
{
    [Explicit("Ventana oculta, audio silenciado, respuesta inventada y sin proveedores de IA.")]
    [TestCase(125), TestCase(175)]
    public void ALongIdeaAndAnInvalidAnswerRemainEditableWithoutStartingOver(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "real");
        ui.Enter("crear");
        var creator = (CreatorWindow)ui.Get("_creator");
        var bounds = new Dictionary<string, (Vector2 Min, Vector2 Max, Vector2 ClipMin, Vector2 ClipMax)>();
        Vector2 panelMin = default, panelMax = default;
        creator.ControlBounds = (name, min, max) =>
        {
            if (name == "Panel") { panelMin = min; panelMax = max; return; }
            bounds[name] = (min, max, ImGui.GetWindowDrawList().GetClipRectMin(), ImGui.GetWindowDrawList().GetClipRectMax());
        };
        ui.Frames(4);
        string wish = string.Concat(Enumerable.Repeat("Guiar una sonda por un taller con controles claros y objetivos propios. ", 12));
        Click("Idea"); ui.Type(wish); ui.Key(ImGuiKey.Tab);
        Click("Preparar"); ui.Frames(3);
        string prompt = Value("_manualPrompt")!;
        Assert.That(prompt, Does.Contain(wish.TrimEnd()));
        Click("Respuesta"); ui.Type("Respuesta incompleta sin ningún bloque de código."); ui.Key(ImGuiKey.Tab);
        Click("Probar"); ui.Frames(3);
        Assert.That(Value("_problem"), Does.Contain("No pude leer la respuesta"));
        Assert.That(Value("_manualPrompt"), Is.EqualTo(prompt));
        Assert.That(Value("_wish"), Is.EqualTo(wish));
        Assert.That(Value("_pasted"), Does.Contain("Respuesta incompleta"));
        ui.Capture("creacion-respuesta-invalida-" + text);
        Click("Cancelar respuesta"); ui.Frames(2);
        Assert.That(Value("_manualPrompt"), Is.Null);
        Assert.That(Value("_wish"), Is.EqualTo(wish));
        Assert.That(Retro.Ai.CreatedGames.All(), Is.Empty);

        string? Value(string name) => (string?)typeof(CreatorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator);
        void Click(string name)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                ui.Frames(2);
                Assert.That(bounds.TryGetValue(name, out var box), Is.True, name);
                if (box.Min.X >= box.ClipMin.X && box.Max.X <= box.ClipMax.X && box.Min.Y >= box.ClipMin.Y && box.Max.Y <= box.ClipMax.Y)
                { ui.Click((box.Min + box.Max) / 2); return; }
                // The left margin scrolls the form, rather than either multiline input's private child.
                ui.MoveMouse(new(panelMin.X + 5, (panelMin.Y + panelMax.Y) / 2));
                ImGui.GetIO().AddMouseWheelEvent(0, box.Max.Y > box.ClipMax.Y ? -2 : 2); ui.Frames(2);
            }
            Assert.Fail("No se pudo alcanzar " + name);
        }
    }
}
