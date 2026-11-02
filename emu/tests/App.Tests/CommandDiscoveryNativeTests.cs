using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class CommandDiscoveryNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit("Ventana oculta, audio silenciado y datos propios; sin IA ni consultas en línea.")]
    [TestCase(125), TestCase(175)]
    public void SearchPreparesAnEnglishOrderAndReturnsToTheSameGraphicalStyle(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "retro", games: 12, "--language", "en");
        var prefs = (AppPreferences)ui.Get("_prefs");
        var sound = (SoundSettings)ui.Get("_sound");
        ui.Click("Buscar acciones"); ui.Frames(3);
        ui.Type("order:system.option"); ui.Frames(2); ui.Capture("buscar-orden-" + text);
        ui.Key(ImGuiKey.Enter); ui.Frames(4);
        Assert.That(ui.Get("_terminalOpen"), Is.EqualTo(true));
        var input = (TerminalInput)ui.Get("_terminalInput");
        Assert.That(input.Editor.Text, Is.EqualTo("option "));
        Assert.That(sound.VolumePercent, Is.EqualTo(100), "Selecting only prepares the command.");
        Assert.That(((ProgramTerminal)ui.Property("Terminal")).Output.AllText(), Does.Contain("Usage: option"));
        ui.Enter("volume 40"); Assert.That(sound.VolumePercent, Is.EqualTo(40));
        ui.Capture("orden-preparada-" + text);
        ui.Enter("do library"); ui.Frames(3);
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Retro));
        Assert.That(ui.Get("_terminalOpen"), Is.EqualTo(false));
        Assert.That(((HomeScreen)ui.Get("_home")).IsOpen, Is.True);
    }
}
