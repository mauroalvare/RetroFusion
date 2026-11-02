using Retro.App;
using Retro.Machines;

namespace App.Tests;

/// <summary>A computer without a working sound output must still run games at their real speed.</summary>
public sealed class MissingAudioDeviceTests
{
    private static double SpeedWithoutConsumption(bool deviceMissing)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-audio-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "48.rom"), new byte[16384]);
            var host = new EmulatorHost { SoundDeviceMissing = deviceMissing };
            host.Start(MachineCatalog.Get("zx48"), folder);
            var machine = host.Machine!;
            const int Updates = 600; const double Step = 1.0 / 60;
            long before = machine.FrameCount;
            // Nothing pops the queue: the audio callback only runs on a real device.
            for (int i = 0; i < Updates; i++) host.Update(Step);
            return (machine.FrameCount - before) / (Updates * Step * machine.FramesPerSecond);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public void TheQueueOnlyPacesTheGameWhileADeviceConsumesIt()
    {
        double stalled = SpeedWithoutConsumption(deviceMissing: false), missing = SpeedWithoutConsumption(deviceMissing: true);
        TestContext.Out.WriteLine($"sin consumo: {stalled:P0}; sin dispositivo declarado: {missing:P0}");
        Assert.That(missing, Is.EqualTo(1).Within(0.02), "Without a device the game keeps real time.");
        Assert.That(stalled, Is.LessThan(0.9), "A stalled device slows the machine down: that is what must not happen when none exists.");
    }

    [TestCase(3840, 2160, 1280, 840), TestCase(1920, 1080, 1280, 840), TestCase(1366, 768, 1280, 668), TestCase(1280, 720, 1240, 620), TestCase(1024, 600, 984, 500), TestCase(800, 480, 760, 480)]
    public void TheWindowNeverStartsLargerThanTheScreen(int screenWidth, int screenHeight, int width, int height) =>
        Assert.That(StartupReport.FittedSize(screenWidth, screenHeight), Is.EqualTo((width, height)));

    [Test]
    public void AFailureReportSaysWhatHappenedWithoutPersonalFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string report = StartupReport.Describe("No abrió " + profile + "\\juego.nes", new IOException("disco"));
        Assert.That(report, Does.Contain("disco").And.Contain("RetroEmu").And.Not.Contain(profile));
    }

    [Test]
    public void ASummaryOfTheProblemNeverExposesThePersonalFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string text = RetroAppDiagnostics(profile + "\\juegos\\roto.nes");
        Assert.That(text, Does.Not.Contain(profile)); Assert.That(text, Does.Contain("%USERPROFILE%"));
    }

    private static string RetroAppDiagnostics(string message) => (string)typeof(RetroApp)
        .GetMethod("Anonymize", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [message])!;
}
