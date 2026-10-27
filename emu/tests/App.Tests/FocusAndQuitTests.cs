using Retro.App;

namespace App.Tests;

public sealed class FocusAndQuitTests
{
    [Test]
    public void BackgroundPauseResumesOnlyThePauseItMade()
    {
        var pause = new FocusPause();
        Assert.That(pause.Update(enabled: true, focused: true, running: true, paused: false), Is.EqualTo(FocusPause.Step.None));
        Assert.That(pause.Update(true, focused: false, running: true, paused: false), Is.EqualTo(FocusPause.Step.Pause));
        Assert.That(pause.PausedByFocus, Is.True);
        Assert.That(pause.Update(true, focused: false, running: true, paused: true), Is.EqualTo(FocusPause.Step.None));
        Assert.That(pause.Update(true, focused: true, running: true, paused: true), Is.EqualTo(FocusPause.Step.Resume));
        Assert.That(pause.PausedByFocus, Is.False);
        Assert.That(pause.Update(true, focused: true, running: true, paused: false), Is.EqualTo(FocusPause.Step.None));
    }

    [Test]
    public void APlayerPauseOrResumeIsNeverOverridden()
    {
        var pause = new FocusPause();
        Assert.That(pause.Update(true, focused: false, running: true, paused: true), Is.EqualTo(FocusPause.Step.None), "Ya estaba en pausa.");
        Assert.That(pause.Update(true, focused: true, running: true, paused: true), Is.EqualTo(FocusPause.Step.None), "No reanuda una pausa del jugador.");

        Assert.That(pause.Update(true, focused: false, running: true, paused: false), Is.EqualTo(FocusPause.Step.Pause));
        Assert.That(pause.Update(true, focused: false, running: true, paused: false), Is.EqualTo(FocusPause.Step.None),
            "El jugador la reanudó sin volver: no se vuelve a pausar en ese mismo cuadro.");
        Assert.That(pause.PausedByFocus, Is.False);
        Assert.That(pause.Update(true, focused: true, running: true, paused: false), Is.EqualTo(FocusPause.Step.None));
    }

    [Test]
    public void DisabledOrIdleWindowsAreNotPaused()
    {
        var pause = new FocusPause();
        Assert.That(pause.Update(enabled: false, focused: false, running: true, paused: false), Is.EqualTo(FocusPause.Step.None));
        Assert.That(pause.Update(enabled: true, focused: false, running: false, paused: false), Is.EqualTo(FocusPause.Step.None));
        Assert.That(pause.Update(true, focused: false, running: true, paused: false), Is.EqualTo(FocusPause.Step.Pause));
        Assert.That(pause.Update(true, focused: true, running: false, paused: true), Is.EqualTo(FocusPause.Step.None),
            "Si se abrió la biblioteca o cambió el juego, no se reanuda.");
    }

    [Test]
    public void QuittingAsksOnlyWhenWorkWouldBeCancelled()
    {
        Assert.That(QuitGuard.Notice(false, false, false), Is.Null);
        Assert.That(QuitGuard.Notice(true, false, false), Does.Contain("una fusión o creación con originales").And.Contain("se cancela"));
        string all = QuitGuard.Notice(true, true, true)!;
        Assert.That(all, Does.Contain("con originales, un juego nuevo desde cero y un análisis o una modificación"));
        Assert.That(all, Does.Contain("Se conserva lo que ya estaba guardado"));
    }

    [Test]
    public void BackgroundPausePersistsAndOlderFilesWithoutItStillLoad()
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "preferencias.json");
        try
        {
            File.WriteAllText(path, "{\"Version\":1,\"TextPercent\":125,\"HighContrast\":true,\"IntegerScale\":true,\"Fullscreen\":false," +
                "\"PowerSaving\":true,\"ShowFps\":false,\"SpectrumJoystick\":\"Cursor\",\"C64Port\":2,\"TypeLoad\":true,\"TurboWhileLoading\":true}");
            var older = new AppPreferences(path);
            Assert.That(older.Problem, Is.Null);
            Assert.That((older.TextPercent, older.SpectrumJoystick, older.PauseInBackground), Is.EqualTo((125, JoystickMode.Cursor, false)));
            older.PauseInBackground = true;
            Assert.That(older.Save(), Is.True);
            Assert.That(new AppPreferences(path).PauseInBackground, Is.True);
            older.Defaults();
            Assert.That(older.PauseInBackground, Is.False);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
