using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;
using Retro.Studio;
using Retro.Systems.Arcade;
using Retro.Systems.C64;
using Retro.Systems.Nes;
using Retro.Systems.Spectrum;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class IntegralReviewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _folder = "";
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private | BindingFlags.Public)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-integral-" + Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllBytes(Path.Combine(_folder, "48.rom"), new byte[16384]);
    }

    [TearDown]
    public void Cleanup()
    {
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_folder)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-integral-"));
        Directory.Delete(_folder, recursive: true);
    }

    private RetroApp Controller(EmulatorHost host)
    {
        var app = Empty<RetroApp>();
        Set(app, "_host", host); Set(app, "_home", Empty<HomeScreen>());
        Set(app, "_creator", Empty<CreatorWindow>()); Set(app, "_studio", Empty<StudioWindow>());
        Set(app, "_code", Empty<CodeWindow>()); Set(app, "_updates", Empty<UpdateWindow>());
        Set(app, "_commands", new CommandPalette()); Set(app, "_browser", new FileBrowser(_folder));
        return app;
    }

    private StudioSession Session()
    {
        var sources = new Dictionary<string, GameSource>();
        foreach (string slot in new[] { "a", "b" })
        {
            byte[] snapshot = new byte[30 + 49152]; snapshot[7] = 0x80; snapshot[8] = 0xFE; snapshot[9] = 0xFF;
            var profile = new GameProfile { Id = "review-" + slot, Name = "Original propio " + slot, Machine = "zx48", Boot = ["wait 1"] };
            sources[profile.Id] = new(profile, [new MediaFile(slot + ".z80", snapshot, "own-review")], null);
        }
        var game = new StudioGame
        {
            Id = "review", Title = "Prueba de reinicio",
            Games = sources.Select((entry, i) => new RecipeGame { Slot = i == 0 ? "a" : "b", Profile = entry.Key }).ToList(),
            Script = "function init(){games.a.pause();games.b.pause();game.state('playing');ui.hud('inicio');} " +
                "function tick(input){game.score(1);ui.hud(input.fire?'disparo':'sigue');} function draw(){}"
        };
        return StudioSession.Start(game, sources, _folder);
    }

    [TestCase(false), TestCase(true)]
    public void RestartFromTheGameMenuRestartsTheWholeFusionAndItsScript(bool hard)
    {
        var session = Session();
        byte[][] initial = session.Slots.Select(slot => slot.Machine.SaveState()).ToArray();
        var host = new EmulatorHost(); host.StartFusion(session);
        session.SetInput("fire", true); session.Update(1.0 / 60); session.Update(1.0 / 60);
        foreach (var slot in session.Slots) slot.Machine.Memory.Poke(0x4100, 99);
        host.Reset(hard);
        Assert.That(session.Tick, Is.Zero, "The menu must reset the scene and JavaScript heap, as game.restart does.");
        Assert.That(session.Score, Is.Zero); Assert.That(session.Hud, Is.EqualTo("inicio"));
        for (int i = 0; i < initial.Length; i++) Assert.That(session.Slots[i].Machine.SaveState(), Is.EqualTo(initial[i]), session.Slots[i].Id);
        session.Update(1.0 / 60);
        Assert.That(session.Hud, Is.EqualTo("sigue"), "A held input from the abandoned run must not survive restart.");
        Assert.That(session.Error, Is.Null);
    }

    [TestCase("_preferencesOpen"), TestCase("_soundWindowOpen"), TestCase("ai")]
    public void ConfigurationWindowsOwnKeyboardAndGamepadNavigationWhileTheGameRuns(string window)
    {
        nint context = ImGui.CreateContext();
        var aiOpen = typeof(AiConnectionWindow).GetField("s_open", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            var app = Controller(new EmulatorHost());
            var machine = new Spectrum48(new byte[16384]);
            if (window == "ai") aiOpen.SetValue(null, true); else Set(app, window, true);
            var io = ImGui.GetIO(); io.ConfigFlags = ImGuiConfigFlags.None;
            Call(app, "SetKeyboardNavigation", machine);
            Assert.That(io.ConfigFlags.HasFlag(ImGuiConfigFlags.NavEnableKeyboard), Is.True, "Tab and arrows must reach the settings.");
            Assert.That(io.ConfigFlags.HasFlag(ImGuiConfigFlags.NavEnableGamepad), Is.True);
            Assert.That(Call(app, "CaptureGuiKeyboard"), Is.EqualTo(true), "Settings input must not also control the game.");
        }
        finally { aiOpen.SetValue(null, false); ImGui.DestroyContext(context); }
    }

    [Test]
    public void CommandShortcutsFollowFusionModeEvenWhenItsMachineHasTheSameId()
    {
        var host = new EmulatorHost(); host.Start(MachineCatalog.Get("zx48"), _folder);
        var app = Controller(host);
        IReadOnlyList<AppCommand> Commands() => (IReadOnlyList<AppCommand>)typeof(RetroApp).GetProperty("Commands", Private)!.GetValue(app)!;
        Assert.That(Commands().Single(command => command.Id == "library").Shortcut, Is.EqualTo("Ctrl+Shift+L"));
        host.StartFusion(Session());
        Assert.That(Commands().Single(command => command.Id == "library").Shortcut, Is.EqualTo("Ctrl+L"));
        host.StopFusion();
        Assert.That(Commands().Single(command => command.Id == "library").Shortcut, Is.EqualTo("Ctrl+Shift+L"));
    }

    private static IMachine Machine(string id) => id switch
    {
        "zx48" => new Spectrum48(new byte[16384]),
        "c64" => new C64(new C64Firmware(new byte[8192], new byte[8192], new byte[4096], "own fixture")),
        "invaders" => new SpaceInvaders(new byte[8192]),
        "nes" => new Nes(NesCartridge.Load(Nrom())),
        _ => throw new ArgumentException(id)
    };
    private static byte[] Nrom()
    {
        byte[] data = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(data); data[4] = 1;
        Array.Fill(data, (byte)0xEA, 16, 16384); data[^4] = 0; data[^3] = 0x80;
        return data;
    }
    private static EmulatorHost Host(IMachine machine)
    {
        var host = new EmulatorHost(); Call(host, "SetMachine", machine, MachineCatalog.Get(machine.Id), "own fixture");
        return host;
    }
    private void RejectWithoutChangingTheGame(IMachine machine, byte[] invalid)
    {
        byte[] previous = machine.SaveState(); int[] picture = machine.Video.Pixels.ToArray();
        string path = Path.Combine(_folder, "invalid.rfstate"); File.WriteAllBytes(path, invalid);
        var error = Assert.Throws<InvalidDataException>(() => Host(machine).LoadStateFromFile(path));
        Assert.That(error!.Message, Does.Contain("Se conservó la partida"));
        Assert.That(machine.SaveState(), Is.EqualTo(previous));
        Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
    }

    [TestCase("zx48"), TestCase("c64"), TestCase("invaders"), TestCase("nes")]
    public void InvalidStringLengthsInAStateProduceARecoverableError(string id) =>
        RejectWithoutChangingTheGame(Machine(id), [0x80, 0x80, 0x80, 0x80, 0x80, 0]);

    [TestCase("zx48"), TestCase("c64"), TestCase("invaders"), TestCase("nes")]
    public void UnexpectedBytesAfterAStateAreRejectedAndTheCurrentRunIsPreserved(string id)
    {
        var machine = Machine(id); RejectWithoutChangingTheGame(machine, [..machine.SaveState(), 0xFF]);
    }

    [TestCase("nes", "Line", -1), TestCase("nes", "Dot", 341)]
    [TestCase("c64", "Raster", 312), TestCase("c64", "Cycle", 63)]
    [TestCase("zx48", "IM", 3), TestCase("invaders", "_shiftOffset", 8)]
    public void InvalidDevicePositionsCannotBeAcceptedAsAPlayableState(string id, string field, int invalidValue)
    {
        var machine = Machine(id); byte[] previous = machine.SaveState();
        object component = machine switch { Nes nes => nes.Ppu, C64 c64 => c64.Vic, Spectrum48 zx => zx.Z80, _ => machine };
        var property = component.GetType().GetProperty(field);
        if (property != null) property.SetValue(component, invalidValue); else Set(component, field, invalidValue);
        byte[] invalid = machine.SaveState(); machine.LoadState(previous);
        RejectWithoutChangingTheGame(machine, invalid);
    }

    [TestCase("nes"), TestCase("c64")]
    public void ARejectedStateDoesNotForgetAControlThatThePlayerIsStillHolding(string id)
    {
        var machine = Machine(id); var host = Host(machine);
        string key = id == "nes" ? "P1 A" : "JOY2 FIRE";
        bool Pressed() => machine switch
        {
            Nes nes => (nes.Read(0x4016) & 1) != 0,
            C64 c64 => (c64.Read(0xDC00) & 0x10) == 0,
            _ => false
        };
        host.SetInput(key, true);
        if (machine is Nes controller) controller.Write(0x4016, 1);
        Assert.That(Pressed(), Is.True);
        string path = Path.Combine(_folder, "truncated.rfstate"); File.WriteAllBytes(path, machine.SaveState()[..^1]);
        Assert.Throws<InvalidDataException>(() => host.LoadStateFromFile(path));
        Assert.That(Pressed(), Is.True, "The host must restore the logical keys as well as memory after rejecting the state.");
        host.SetInput(key, false); Assert.That(Pressed(), Is.False);
    }

    [Test]
    public void ARejectedPcStateDoesNotEnqueueExtraKeyboardEventsByRestoringAnUnchangedMachine()
    {
        var machine = new Retro.Systems.Pc.PcXt(new byte[8192]); var host = Host(machine);
        host.SetInput("A", true); byte[] previous = machine.SaveState(); byte[] invalid = previous.ToArray(); invalid[0] ^= 1;
        string path = Path.Combine(_folder, "invalid-pc.rfstate"); File.WriteAllBytes(path, invalid);
        Assert.Throws<InvalidDataException>(() => host.LoadStateFromFile(path));
        Assert.That(machine.SaveState(), Is.EqualTo(previous), "PC validation already uses a fresh board; rolling back an untouched board would release keys and enqueue scan codes.");
    }

    private sealed class Controls : IFusionRun
    {
        public Dictionary<string, bool> Held { get; } = new();
        public int Calls { get; private set; }
        public string Title => "Own controls";
        public string Description => "";
        public IReadOnlyList<FusionSlot> Slots { get; init; } = [];
        public VideoFrame Video { get; } = new(2, 2);
        public List<FusionEvent> Events { get; } = [];
        public long Tick => 0;
        public void SetInput(string input, bool pressed) { Held[input] = pressed; Calls++; }
        public void Update(double seconds) { }
        public int DrainAudio(short[] destination) => 0;
    }

    [Test]
    public void HoldingAControlAcrossFusionChangesReachesTheNewSessionExactlyOnce()
    {
        var mapper = new InputMapper(); var first = new Controls(); var second = new Controls();
        bool[] frame = [true, false, false, false, true, false, false, false];
        mapper.ApplyFusion(first, frame); int firstCalls = first.Calls;
        mapper.ApplyFusion(first, frame); Assert.That(first.Calls, Is.EqualTo(firstCalls));
        mapper.ApplyFusion(second, frame);
        Assert.That(second.Held["left"], Is.True); Assert.That(second.Held["fire"], Is.True);
        Assert.That(second.Held["start"], Is.False);
        int secondCalls = second.Calls; mapper.ApplyFusion(second, frame); Assert.That(second.Calls, Is.EqualTo(secondCalls));
        mapper.ForgetKeys(); mapper.ApplyFusion(second, frame);
        Assert.That(second.Calls, Is.GreaterThan(secondCalls), "A reset invalidates cached controls even if the session object is reused.");
        Assert.That(second.Held["left"], Is.True);
        mapper.ApplyFusion(second, new bool[8]); Assert.That(second.Held.Values.All(pressed => !pressed), Is.True);
    }

    [Test]
    public void IncompleteInputFramesCannotPartiallyChangeTheCurrentControls()
    {
        var mapper = new InputMapper(); var controls = new Controls();
        mapper.ApplyFusion(controls, [true, false, false, false, false, false, false, false]);
        int calls = controls.Calls;
        Assert.Throws<ArgumentException>(() => mapper.ApplyFusion(controls, new bool[7]));
        Assert.That(controls.Calls, Is.EqualTo(calls)); Assert.That(controls.Held["left"], Is.True);
    }

    [Test]
    public void OversizedStateFilesAreRejectedBeforeReadingTheirPayload()
    {
        var machine = Machine("nes"); var host = Host(machine); byte[] previous = machine.SaveState();
        string path = Path.Combine(_folder, "oversized.rfstate");
        using (var file = new FileStream(path, FileMode.CreateNew)) file.SetLength(16 * 1024 * 1024 + 1);
        var error = Assert.Throws<InvalidDataException>(() => host.LoadStateFromFile(path));
        Assert.That(error!.Message, Does.Contain("16 MB")); Assert.That(machine.SaveState(), Is.EqualTo(previous));
    }

    [Test]
    public void FusionResetDisplaysItsShortcutEvenWhenTheFocusedOriginalUsesTheC64Keyboard()
    {
        var machine = Machine("c64"); var host = Host(machine); var app = Controller(host);
        Assert.That(Call(app, "Shortcut", "F5"), Is.EqualTo(""));
        host.StartFusion(new Controls { Slots = [new FusionSlot { Id = "a", Machine = machine, Profile = new GameProfile { Id = "own-c64", Machine = "c64" } }] });
        Assert.That(Call(app, "Shortcut", "F5"), Is.EqualTo("F5"));
        Assert.That(Call(app, "Shortcut", "Shift+F5"), Is.EqualTo("Shift+F5"));
    }
}
