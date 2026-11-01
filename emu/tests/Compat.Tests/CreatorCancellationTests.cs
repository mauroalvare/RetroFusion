using System.Text.Json;
using Retro.Ai;
using Retro.Asm;
using Retro.Compat;

namespace Compat.Tests;

public sealed class CreatorCancellationTests
{
    private string _firmware = "";
    [OneTimeSetUp]
    public void LocateFirmware()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom"))) dir = dir.Parent;
        if (dir == null) Assert.Ignore("Falta el firmware local de prueba.");
        _firmware = Path.Combine(dir!.FullName, "roms");
    }

    [TestCase("Carga", 0)] [TestCase("Arranque", 60)] [TestCase("Dibujo", 0)]
    [TestCase("Controles", 0)] [TestCase("Partida robot", 60)]
    public void CancellationDuringAnActualTrialIsNotConvertedIntoAFailedLoad(string stage, int atFrame)
    {
        using var stop = new CancellationTokenSource();
        var seen = new List<TrialProgress>();
        Assert.Throws<OperationCanceledException>(() => HomebrewTrial.Run(Templates.For("c64")!.Source, "c64", _firmware, stop.Token, p =>
        {
            seen.Add(p);
            if (p.Stage == stage && p.Frames == atFrame) stop.Cancel();
        }));
        Assert.That(seen.Any(p => p.Stage == stage && p.Frames == atFrame), Is.True);
    }

    private sealed class OneAnswer : IAiProvider
    {
        public string Name => "Respuesta local para cancelación";
        public int Calls;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(JsonSerializer.Serialize(new { title = "Propio", pitch = "Prueba propia", howToPlay = "Joystick", changes = "", source = Templates.For("c64")!.Source }));
        }
    }

    [Test]
    public void CancellingTheRobotKeepsTheCompleteAnswerForAContinuationWithoutAnotherRequest()
    {
        using var stop = new CancellationTokenSource(); var provider = new OneAnswer(); WriterCheckpoint? saved = null;
        var writer = new GameWriter { Checkpoint = s => saved = s };
        Assert.ThrowsAsync<OperationCanceledException>(() => writer.WriteAsync(provider, "c64", "Mantener el pedido", _firmware, stop.Token,
            progress: text => { if (text.StartsWith("Partida robot: 60 ", StringComparison.Ordinal)) stop.Cancel(); }));
        Assert.That(saved!.Draft!.Source, Is.EqualTo(Templates.For("c64")!.Source.Replace("\r\n", "\n")));
        Assert.That(saved.Calls, Is.EqualTo(1));
        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That(saved.Wish, Is.EqualTo("Mantener el pedido"));
    }
}
