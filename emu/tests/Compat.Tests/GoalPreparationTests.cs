using Retro.Asm;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Nes;

namespace Compatibility.Tests;

internal static class PreparationNesFixture
{
    public static byte[] Rom() => Build(false);
    public static byte[] RomWithDelayedOam() => Build(true);
    private static byte[] Build(bool delayed)
    {
        string source = """
            .org $C000
            sei
            cld
            ldx #$FF
            txs
            lda #0
            sta $2000
            sta $2001
            sta $00
            sta $01
            sta $02
            sta $14
            sta $15
            sta $16
            lda #80
            sta $10
            lda #160
            sta $11
            lda #3
            sta $12
            ldx #0
            lda #$FF
            clear:
            sta $0200,x
            inx
            bne clear
            lda #$3F
            sta $2006
            lda #$00
            sta $2006
            lda #$0F
            sta $2007
            lda #$3F
            sta $2006
            lda #$11
            sta $2006
            lda #$30
            sta $2007
            lda #$80
            sta $2000
            lda #$14
            sta $2001
            frame:
            lda $00
            cmp $01
            beq frame
            sta $01
            lda #1
            sta $4016
            lda #0
            sta $4016
            sta $02
            ldx #8
            pad:
            lda $4016
            lsr a
            rol $02
            dex
            bne pad
            lda $15
            bne play
            lda $02
            and #$10
            beq display
            lda #1
            sta $15
            jmp display
            play:
            lda $02
            and #2
            beq right
            dec $10
            right:
            lda $02
            and #1
            beq action
            inc $10
            action:
            lda $02
            and #$80
            beq jump
            lda $16
            bne jump
            lda #32
            sta $16
            jump:
            lda $16
            beq ground
            cmp #16
            bcc falling
            eor #$FF
            clc
            adc #33
            falling:
            asl a
            sta $03
            lda #160
            sec
            sbc $03
            sta $11
            dec $16
            jmp score
            ground:
            lda #160
            sta $11
            score:
            lda $02
            and #$40
            beq death
            inc $14
            death:
            lda $10
            cmp #220
            bcc display
            dec $12
            lda #80
            sta $10
            display:
            lda $15
            beq title
            lda $11
            sta $0200
            lda #0
            sta $0201
            sta $0202
            lda $10
            sta $0203
            title:
            lda #0
            sta $2003
            lda #2
            sta $4014
            lda #$3F
            sta $2006
            lda #$11
            sta $2006
            lda $14
            and #3
            ora #$20
            sta $2007
            jmp frame
            nmi:
            inc $00
            rti
            """;
        if (delayed)
        {
            source = source.Replace("sta $16", "sta $16\nlda #80\nsta $19\nlda #160\nsta $18")
                .Replace("lda $11\nsta $0200", "lda $18\nsta $0200")
                .Replace("lda $10\nsta $0203", "lda $19\nsta $0203\nlda $10\nsta $19\nlda $11\nsta $18");
        }
        var result = Asm6502.Assemble(source, 0xC000);
        Assert.That(result.Ok, Is.True, string.Join("; ", result.Errors));
        var rom = new byte[16 + 0x4000 + 0x2000];
        "NES\x1A"u8.CopyTo(rom); rom[4] = rom[5] = 1;
        result.Bytes.CopyTo(rom, 16);
        int nmi = result.Symbols["nmi"];
        for (int i = 0; i < 3; i++)
        {
            int vector = i == 1 ? 0xC000 : nmi;
            rom[16 + 0x3FFA + i * 2] = (byte)vector;
            rom[16 + 0x3FFB + i * 2] = (byte)(vector >> 8);
        }
        for (int i = 0; i < 8; i++) rom[16 + 0x4000 + i] = 0xFF;
        return rom;
    }
}

[TestFixture, NonParallelizable]
public sealed class GoalPreparationTests
{
    private string _folder = "", _previousFolder = "";
    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-goal-" + Guid.NewGuid())).FullName;
        _previousFolder = Environment.GetEnvironmentVariable("RETROEMU_PROFILES") ?? "";
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousFolder.Length == 0 ? null : _previousFolder);
        Directory.Delete(_folder, true);
    }
    private string WriteRom()
    {
        string file = Path.Combine(_folder, "own.nes"); File.WriteAllBytes(file, PreparationNesFixture.Rom()); return file;
    }
    private PrepareOptions Options(IPlayAdvisor? advisor = null) => new()
    {
        Goal = "Observar posición vertical y salto; faltan hitbox, cámara y daño.",
        Advisor = advisor, MaxRounds = 4, TrialRounds = 0, LabFrames = 120,
        ProfilesFolder = ProfileLibrary.UserFolder,
    };

    [Test]
    public async Task NesPreparationStartsAndObservesJumpWithoutManualPlay()
    {
        string file = WriteRom(); byte[] original = File.ReadAllBytes(file);
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem + "\n" + string.Join("\n", result.Log));
        Assert.That(result.Boot, Does.Contain("press P1 START 6"));
        Assert.That(result.Profile!.Controls["start"], Is.EqualTo("P1 START"));
        Assert.That(result.Profile.Controls["jump"], Is.EqualTo("P1 A"));
        Assert.That(result.Lab!.Findings, Has.Some.Matches<LabFinding>(f => f.Kind == "position-y" && f.Address == "0011"));
        Assert.That(result.Profile.Variables, Does.ContainKey("y"));
        Assert.That(result.CheckedData, Has.Some.Contains("jump"));
        Assert.That(result.UnresolvedData, Has.Some.Contains("hitboxes"));
        Assert.That(result.Goal, Does.Contain("cámara"));
        Assert.That(File.ReadAllBytes(file), Is.EqualTo(original));
    }

    private sealed class PlanningAdvisor : IPlayAdvisor, IPreparationExperimentAdvisor
    {
        public PreparationExperimentQuestion? Question;
        public PlayObservation? Observation;
        public string Name => "IA simulada";
        public Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct)
        {
            Observation = observation;
            return Task.FromResult(observation.PlayCheck.Contains("Si esto ya es la partida")
                ? new PlayAdvice { Playing = true }
                : new PlayAdvice { Actions = { new("P1 START", 6, 150) } });
        }
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion question, CancellationToken ct)
        {
            Question = question;
            return Task.FromResult(new List<LabExperiment>
            {
                new() { Kind = "score", Description = "Mantener la segunda acción", Frames = 120, Steps = { new() { Frame = 0, Keys = "P1 B", Hold = 100 } } },
                new() { Kind = "change", Description = "Plan inválido", Frames = int.MaxValue, Steps = { new() { Frame = 0, Keys = "host-command", Hold = 1 } } },
            });
        }
    }

    [Test]
    public async Task GoalReachesTheAdvisorAndOnlyBoundedInputExperimentsRun()
    {
        var advisor = new PlanningAdvisor();
        var result = await GamePreparer.PrepareAsync(WriteRom(), Options(advisor), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(advisor.Observation!.Goal, Is.EqualTo(result.Goal));
        Assert.That(advisor.Question!.Goal, Is.EqualTo(result.Goal));
        Assert.That(result.CheckedData, Has.Some.Contains("Mantener la segunda acción"));
        Assert.That(result.UnresolvedData, Has.Some.Contains("límites"));
        Assert.That(result.Lab!.Findings, Has.Some.Matches<LabFinding>(f => f.Address == "0014" && f.Status == "verified"));
        Assert.That(result.UnresolvedData, Has.Some.Contains("hitboxes"));
    }

    [Test]
    public async Task ExactEditionKeepsPreviouslyVerifiedHelpersAndVariables()
    {
        string file = WriteRom();
        var profile = new GameProfile
        {
            Id = "known-edition", Name = "Edición propia", Machine = "nes",
            Match = new() { Sha256 = { GamePreparer.Sha256Of(new("own.nes", File.ReadAllBytes(file), file)) } },
            Helpers = { "g.known = function(){ return 1; };" }, HelpersDoc = { "known: comprobado antes." },
            Variables = { ["knownCounter"] = new() { Address = "0014", Description = "Dato previamente observado." } },
            Sprites = { ["known"] = new() { Address = "0010", Width = 8, Height = 8 } },
        };
        ProfileLibrary.Save(profile);
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Id, Is.EqualTo(profile.Id));
        Assert.That(result.Profile.Helpers, Is.EqualTo(profile.Helpers));
        Assert.That(result.Profile.HelpersDoc, Is.EqualTo(profile.HelpersDoc));
        Assert.That(result.Profile.Sprites, Does.ContainKey("known"));
        Assert.That(result.Profile.Variables, Does.ContainKey("knownCounter"));
        Assert.That(result.Profile.Match.Sha256, Is.EqualTo(profile.Match.Sha256));
    }

    [Test]
    public void InvalidOrCancelledPreparationDoesNotWriteAProfile()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await GamePreparer.PrepareAsync(WriteRom(), Options(), null, cancelled.Token));
        Assert.That(Directory.Exists(ProfileLibrary.UserFolder), Is.False);
    }

    [Test]
    public async Task ASecondEditionDoesNotInheritHelpersFromANameMatch()
    {
        string file = WriteRom();
        var previous = new GameProfile
        {
            Id = "other-edition", Name = "Otra edición", Machine = "nes",
            Match = new() { Files = { "own.nes" }, Sha256 = { GamePreparer.Sha256Of(new("own.nes", File.ReadAllBytes(file), file)) } },
            Helpers = { "g.wrongEdition = function(){ return 1; };" },
        };
        ProfileLibrary.Save(previous);
        byte[] changed = File.ReadAllBytes(file); changed[^1] = 1; File.WriteAllBytes(file, changed);
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Id, Is.Not.EqualTo(previous.Id));
        Assert.That(result.Profile.Helpers, Is.Empty);
        Assert.That(result.Profile.Match.Sha256, Is.Not.EqualTo(previous.Match.Sha256));
    }

    [Test]
    public async Task JumpControlComesFromObservedMotionRatherThanAnAssumedAButton()
    {
        string file = WriteRom(); byte[] rom = File.ReadAllBytes(file);
        int mask = Enumerable.Range(16, 0x4000 - 4).First(i => rom[i] == 0xA5 && rom[i + 1] == 2 && rom[i + 2] == 0x29 && rom[i + 3] == 0x80);
        rom[mask + 3] = 0x40; File.WriteAllBytes(file, rom);
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Controls["jump"], Is.EqualTo("P1 B"));
    }

    [Test]
    public void CancellingDuringAnObservationRestoresItsPrivateMachine()
    {
        var machine = new Nes(NesCartridge.Load(PreparationNesFixture.Rom()));
        var sink = new short[8192];
        for (int i = 0; i < 10; i++) { machine.RunFrame(null); machine.DrainAudio(sink); }
        machine.SetKey("P1 START", true);
        for (int i = 0; i < 6; i++) { machine.RunFrame(null); machine.DrainAudio(sink); }
        machine.ReleaseAllKeys();
        byte[] state = machine.SaveState();
        using var cancellation = new CancellationTokenSource();
        using var observer = CpuWriteObservers.Subscribe(machine, (_, _, _) => cancellation.Cancel());
        Assert.Throws<OperationCanceledException>(() =>
            MemoryLab.Analyze(machine, "nes", new(IdleFrames: 120), ct: cancellation.Token));
        Assert.That(machine.SaveState(), Is.EqualTo(state));
    }

    [Test]
    public async Task AProfileInAnotherZipMemberIsNotCopiedToTheSelectedRom()
    {
        byte[] first = PreparationNesFixture.Rom(), second = (byte[])first.Clone(); second[^1] = 1;
        string file = Path.Combine(_folder, "copies.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var stream = archive.CreateEntry("A.nes").Open()) stream.Write(first);
            using (var stream = archive.CreateEntry("B.nes").Open()) stream.Write(second);
        }
        var other = new GameProfile
        {
            Id = "second-member", Name = "Otro miembro", Machine = "nes",
            Match = new() { Sha256 = { GamePreparer.Sha256Of(new("B.nes", second, "")) } },
            Helpers = { "g.belongsToB = function(){ return true; };" },
        };
        ProfileLibrary.Save(other);
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Sha256, Is.EqualTo(GamePreparer.Sha256Of(new("A.nes", first, ""))));
        Assert.That(result.Profile!.Helpers, Is.Empty);
        Assert.That(result.Profile.Id, Is.Not.EqualTo(other.Id));
    }

    [Test]
    public async Task ObservedPositionsTolerateOneFrameOfRenderingPipelineWithoutInventingAnOffset()
    {
        string file = Path.Combine(_folder, "delayed.nes");
        File.WriteAllBytes(file, PreparationNesFixture.RomWithDelayedOam());
        var result = await GamePreparer.PrepareAsync(file, Options(), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Controls["jump"], Is.EqualTo("P1 A"));
        Assert.That(result.Lab!.Findings, Has.Some.Matches<LabFinding>(f => f.Kind == "position-y" && f.Address == "0011"));
        Assert.That(result.Lab.Findings, Has.Some.Matches<LabFinding>(f => f.Kind == "position-x" && f.Address == "0010"));
    }

    private sealed class RejectingNames : IPlayAdvisor, IVariableNamer
    {
        public string Name => "IA simulada que no identifica los datos";
        public Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct) =>
            Task.FromResult(observation.PlayCheck.Contains("Si esto ya es la partida")
                ? new PlayAdvice { Playing = true }
                : new PlayAdvice { Actions = { new("P1 START", 6, 150) } });
        public Task<List<VariableName>> NameVariablesAsync(VariableQuestion question, CancellationToken ct) =>
            Task.FromResult(question.Candidates.Select(c => new VariableName(c.Address, c.Guess, "No se conoce su significado.", false)).ToList());
    }

    [Test]
    public async Task AReproducibleBootWithNoAcceptedVariablesRemainsExplicitlyPartial()
    {
        var result = await GamePreparer.PrepareAsync(WriteRom(), Options(new RejectingNames()), null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Variables, Is.Empty, "A rejection must not silently resurrect heuristic variable names.");
        Assert.That(result.UnresolvedData, Has.Some.Contains("no se identificaron variables"));
        Assert.That(result.UnresolvedData, Has.Some.Contains("hitboxes"));
        Assert.That(result.CheckedData, Has.Some.Contains("arranque guardado"));
    }
}
