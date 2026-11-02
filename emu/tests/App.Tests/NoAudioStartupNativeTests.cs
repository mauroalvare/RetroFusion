using Raylib_cs;
using Retro.App;

namespace App.Tests;

[NonParallelizable]
public sealed class NoAudioStartupNativeTests
{
    [Test, Explicit("Ejecutar sola: ventana oculta y --no-audio, sin inicializar el dispositivo.")]
    public void NoAudioStartsAndPlaysWithoutTheDeviceAndOnlyAnExplicitRetryEnablesIt()
    {
        using var ui = new NativeHarness(1280, 840, 125, "real", games: 1, "--no-audio");
        Assert.That((bool)Raylib.IsAudioDeviceReady(), Is.False);
        ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(60);
        var host = (EmulatorHost)ui.Get("_host");
        Assert.That(host.Machine!.FrameCount, Is.GreaterThan(0)); Assert.That(host.SoundDeviceMissing, Is.True);
        Assert.That((bool)Raylib.IsAudioDeviceReady(), Is.False, "Playing must not initialize audio implicitly.");
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("requisitos"); ui.Enter("diagnostico");
        Assert.That(((ProgramTerminal)ui.Property("Terminal")).Output.AllText(), Does.Contain("desactivado para este inicio (--no-audio)"));
        ui.Capture("inicio-sin-dispositivo");
        ui.Enter("hacer audio-retry"); ui.Frames(5);
        Assert.That((bool)Raylib.IsAudioDeviceReady(), Is.True); Assert.That(host.SoundDeviceMissing, Is.False);
        Assert.That(ui.Get("_audioDisabled"), Is.EqualTo(false));
    }
}
