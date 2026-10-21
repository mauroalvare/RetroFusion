using System.Collections.Concurrent;
using Jint;
using Jint.Runtime;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Arcade;
using Retro.Systems.Spectrum;

namespace Retro.Studio;

/// <summary>
/// Runs a fusion game: the original games keep running on their emulated machines, and
/// the game's JavaScript (sandboxed with Jint: no files, no network, limited time and
/// memory) reads and writes their memory, presses their keys, pauses them and draws one
/// scene from their sprites, tiles and variables. 60 ticks per second.
/// </summary>
public sealed class StudioSession : IFusionRun
{
    public const int TicksPerSecond = 60;
    public static readonly string[] InputNames = { "left", "right", "up", "down", "fire", "jump", "start", "coin" };

    internal sealed class SlotState
    {
        public required FusionSlot Slot;
        public required byte[] BootState;
        public required VideoFrame BootPicture;
        public double Accumulator;
        public bool Paused;
        public double Speed = 1;
        public double Volume = 1;
        public readonly Queue<short> Audio = new();
    }

    private sealed record BootSnapshot(byte[] State, VideoFrame Picture);
    private static readonly ConcurrentDictionary<string, BootSnapshot> BootCache = new();

    private readonly List<SlotState> _slots;
    private readonly Canvas _canvas;
    private readonly ScriptHost _host;
    private readonly short[] _drain = new short[8192];
    private readonly List<short> _mixed = new();
    private readonly List<Tone> _tones = new();
    private double _audioCarry;
    private int _inputMask;
    private Engine? _engine;

    public StudioGame Game { get; }
    public string Title => Game.Title;
    public string Description => Game.Pitch;
    public IReadOnlyList<FusionSlot> Slots { get; }
    public VideoFrame Video => _canvas.Frame;
    public List<FusionEvent> Events { get; } = new();
    public long Tick { get; private set; }
    /// <summary>Status line the script shows next to the game (lives, level...).</summary>
    public string Hud { get; internal set; } = "";
    /// <summary>Script error: when set, the script is no longer called and the games are paused.</summary>
    public string? Error { get; private set; }
    /// <summary>Lines written by the script with log(), most recent last.</summary>
    public List<string> Log { get; } = new();

    // What the script reports about the game (game.state / score / death / level), used by
    // the automatic playtest and shown to the player.
    /// <summary>Last state reported: title, playing, gameover... (empty if the script never said).</summary>
    public string State { get; private set; } = "";
    public bool ReportsState { get; private set; }
    internal bool SceneViolation { get; set; }
    internal string? SceneProblem { get; set; }
    internal HashSet<string> ReferencedSlots { get; } = new();
    public bool ReportsScore { get; private set; }
    public long Score { get; private set; }
    public int Level { get; private set; }
    public List<(long Tick, string Cause)> Deaths { get; } = new();

    internal void SetState(string state)
    {
        State = state.Trim().ToLowerInvariant();
        ReportsState = true;
    }

    internal void AddScore(int points)
    {
        Score += points;
        ReportsScore = true;
    }

    internal void AddDeath(string cause) => Deaths.Add((Tick, cause.Length > 40 ? cause[..40] : cause));
    internal void SetLevel(int level) => Level = level;

    private sealed class Tone
    {
        public double Frequency, Volume, Phase;
        public int Samples;
        public bool Noise;
    }

    private StudioSession(StudioGame game, List<SlotState> slots, byte[]? font)
    {
        Game = game;
        _slots = slots;
        Slots = slots.Select(s => s.Slot).ToList();
        _canvas = new Canvas(256, 192, font);
        _host = new ScriptHost(this);
        StartScript();
    }

    /// <summary>
    /// Creates the machines and boots each game to playing. The booted state is kept in
    /// memory, so restarting the game or trying a new version of the script is instant.
    /// </summary>
    public static StudioSession Start(StudioGame game, IReadOnlyDictionary<string, GameSource> sources, string romDirectory, Action<string>? log = null)
    {
        var slots = new List<SlotState>();
        foreach (var g in game.Games)
        {
            if (!sources.TryGetValue(g.Profile, out var src))
                throw new InvalidOperationException($"Falta el juego \"{g.Profile}\" en la carpeta de juegos.");
            var m = MachineFactory.Create(src, romDirectory);
            string key = g.Profile + "|" + string.Join(",", src.Files.Select(f => f.Name + ":" + f.Data.Length));
            if (BootCache.TryGetValue(key, out var snapshot))
            {
                m.LoadState(snapshot.State);
                snapshot.Picture.Pixels.CopyTo(m.Video.Pixels, 0);
            }
            else
            {
                MachineFactory.Boot(m, src.Profile, log);
                var picture = new VideoFrame(m.Video.Width, m.Video.Height);
                m.Video.Pixels.CopyTo(picture.Pixels, 0);
                snapshot = new BootSnapshot(m.SaveState(), picture);
                BootCache[key] = snapshot;
            }
            m.ExecutedMap ??= new bool[0x10000];
            slots.Add(new SlotState { Slot = new FusionSlot { Id = g.Slot, Profile = src.Profile, Machine = m },
                BootState = snapshot.State, BootPicture = snapshot.Picture });
        }
        return new StudioSession(game, slots, Canvas.LoadFont(romDirectory));
    }

    internal SlotState SlotFor(string id) =>
        _slots.FirstOrDefault(s => s.Slot.Id == id) ?? throw new ArgumentException($"No hay un juego en la ranura \"{id}\".");

    internal IEnumerable<SlotState> SlotStates => _slots;
    internal Canvas Canvas => _canvas;

    /// <summary>Where the game's own picture is inside its frame (without the Spectrum or C64 border).</summary>
    internal static (int x, int y, int w, int h) PictureArea(IMachine m) => m switch
    {
        Spectrum48 => (Spectrum48.BorderSide, Spectrum48.BorderTop, 256, 192),
        SpaceInvaders => (0, 0, SpaceInvaders.ScreenWidth, SpaceInvaders.ScreenHeight),
        // 25-row display: VIC X 24 (32 border pixels in) and raster line 51 (36 lines down)
        Retro.Systems.C64.C64 => (32, 51 - Retro.Systems.C64.Vic.FirstVisibleLine, 320, 200),
        _ => (0, 0, m.Video.Width, m.Video.Height),
    };

    // ------------------------------------------------------------------ script
    private void StartScript()
    {
        Error = null;
        _host.ResetSprites();
        _engine = new Engine(o =>
        {
            o.LimitMemory(64_000_000);
            o.TimeoutInterval(TimeSpan.FromMilliseconds(500));
            o.MaxStatements(3_000_000);
            o.LimitRecursion(256);
        });
        try
        {
            _engine.SetValue("__h", _host);
            _engine.Execute(ScriptHost.Prelude, "api-fusion.js");
            foreach (var s in _slots.Where(s => s.Slot.Profile.Helpers.Count > 0))
                _engine.Execute($"(function (g) {{\n{string.Join("\n", s.Slot.Profile.Helpers)}\n}})(games['{s.Slot.Id}']);", $"ayudas-{s.Slot.Profile.Id}.js");
            _engine.Execute(Game.Script, "juego.js");
            Call("__init");
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private void Call(string function, params object[] args)
    {
        _engine!.Constraints.Reset();
        _engine.Invoke(function, args);
    }

    private void Fail(Exception e)
    {
        Error = Describe(e);
        foreach (var s in _slots)
            s.Paused = true;
    }

    /// <summary>A script error as the player (and the AI that has to fix it) should read it.</summary>
    public static string Describe(Exception e)
    {
        switch (e)
        {
            case JavaScriptException js:
            {
                var loc = js.Location;
                string where = loc.SourceFile is { Length: > 0 } f ? $" ({f}, línea {loc.Start.Line}, columna {loc.Start.Column + 1})" : "";
                return $"Error de JavaScript{where}: {js.Message}";
            }
            case Acornima.ParseErrorException pe:
                return $"Error de sintaxis (juego.js, línea {pe.LineNumber}, columna {pe.Column}): {pe.Description}";
            case TimeoutException:
                return "El guion tardó demasiado en un tick (más de medio segundo): probablemente hay un bucle infinito.";
            case StatementsCountOverflowException:
                return "El guion ejecutó demasiadas instrucciones en un tick (¿un bucle sin fin?).";
            case MemoryLimitExceededException:
                return "El guion usó demasiada memoria (más de 64 MB).";
            case RecursionDepthOverflowException:
                return "El guion se llamó a sí mismo demasiadas veces (recursión sin fin).";
            default:
                return (e.InnerException != null && e is System.Reflection.TargetInvocationException ? e.InnerException.Message : e.Message);
        }
    }

    /// <summary>Starts the fusion over: every game back to its booted state and the script from init().</summary>
    public void Restart()
    {
        foreach (var s in _slots)
        {
            s.Slot.Machine.LoadState(s.BootState);
            s.BootPicture.Pixels.CopyTo(s.Slot.Machine.Video.Pixels, 0);
            s.Slot.Machine.ReleaseAllKeys();
            s.Paused = false;
            s.Speed = 1;
            s.Volume = 1;
            s.Accumulator = 0;
            s.Audio.Clear();
        }
        Tick = 0;
        Hud = "";
        Events.Clear();
        State = "";
        ReportsState = false;
        ReportsScore = false;
        SceneViolation = false;
        SceneProblem = null;
        ReferencedSlots.Clear();
        _inputMask = 0;
        Score = 0;
        Level = 0;
        Deaths.Clear();
        _tones.Clear();
        _canvas.Resize(256, 192);
        StartScript();
    }

    // ------------------------------------------------------------------ IFusionRun
    public void SetInput(string input, bool pressed)
    {
        int bit = Array.IndexOf(InputNames, input);
        if (bit < 0)
            return;
        if (pressed) _inputMask |= 1 << bit;
        else _inputMask &= ~(1 << bit);
    }

    public void Update(double seconds)
    {
        Tick++;
        foreach (var s in _slots)
        {
            if (s.Paused)
                continue;
            s.Accumulator += seconds * s.Slot.Machine.FramesPerSecond * s.Speed;
            int guard = 0;
            while (s.Accumulator >= 1 && guard++ < 8)
            {
                s.Accumulator -= 1;
                s.Slot.Machine.RunFrame(null);
                int n = s.Slot.Machine.DrainAudio(_drain);
                for (int i = 0; i < n; i++)
                    s.Audio.Enqueue(_drain[i]);
                while (s.Audio.Count > 8820)
                    s.Audio.Dequeue();
            }
        }
        if (Error == null)
        {
            try
            {
                Call("__tick", _inputMask);
                Call("__draw");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }
        MixAudio(seconds);
    }

    public int DrainAudio(short[] destination)
    {
        int n = Math.Min(destination.Length, _mixed.Count);
        _mixed.CopyTo(0, destination, 0, n);
        _mixed.RemoveRange(0, n);
        return n;
    }

    // ------------------------------------------------------------------ sound
    internal void AddTone(double frequency, double milliseconds, double volume, bool noise)
    {
        if (_tones.Count >= 16)
            return;
        _tones.Add(new Tone
        {
            Frequency = Math.Clamp(frequency, 20, 12000),
            Samples = (int)(Math.Clamp(milliseconds, 1, 3000) * 44.1),
            Volume = Math.Clamp(volume, 0, 1),
            Noise = noise,
        });
    }

    private void MixAudio(double seconds)
    {
        _audioCarry += seconds * 44100;
        int n = (int)_audioCarry;
        _audioCarry -= n;
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            foreach (var s in _slots)
                if (s.Audio.Count > 0)
                {
                    short v = s.Audio.Dequeue();
                    if (!s.Paused)
                        sum += v * s.Volume;
                }
            foreach (var t in _tones)
            {
                if (t.Samples <= 0)
                    continue;
                t.Samples--;
                t.Phase += t.Frequency / 44100;
                double wave = t.Noise ? (Random.Shared.NextDouble() * 2 - 1) : ((t.Phase % 1) < 0.5 ? 1 : -1);
                sum += wave * 9000 * t.Volume;
            }
            _mixed.Add((short)Math.Clamp(sum * 0.8, -32767, 32767));
        }
        _tones.RemoveAll(t => t.Samples <= 0);
        if (_mixed.Count > 44100)
            _mixed.RemoveRange(0, _mixed.Count - 44100);
    }

    internal void AddMessage(string text) => Events.Add(new FusionEvent(Tick, text));

    internal void AddLog(string text)
    {
        Log.Add(text.Length > 300 ? text[..300] : text);
        if (Log.Count > 200)
            Log.RemoveAt(0);
    }
}
