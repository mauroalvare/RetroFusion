using System.Security.Cryptography;
using Retro.Core;
using Retro.Cpu.I8088;

namespace Retro.Systems.Pc;

/// <summary>8088 PC/XT with CGA, PIT/PIC/PPI, keyboard, DMA, speaker and one sector-image floppy.</summary>
public sealed class PcXt : IMachine, IMemoryView
{
    public const double Clock = 14_318_181.818181818 / 3;
    public const int CyclesPerFrame = 79_648;
    private readonly byte[] _bios, _disk;
    private readonly bool _pc5150, _writeProtected;
    private readonly int _ramKilobytes;
    private Runtime _runtime;
    private readonly HashSet<byte> _held = [];
    private KeySequencer _sequence = new();
    private bool _skipBreakpoint;
    private readonly I8088Disassembler _disassembler;

    public PcXt(byte[] bios, byte[]? floppy = null, int ramKilobytes = 640, bool? pc5150 = null, bool writeProtected = false)
    {
        ArgumentNullException.ThrowIfNull(bios);
        _bios = bios.ToArray(); _disk = floppy?.ToArray() ?? []; _ramKilobytes = ramKilobytes;
        _pc5150 = pc5150 ?? (bios.Length >= 2 && bios[^2] == 0xFF); _writeProtected = writeProtected;
        _runtime = NewRuntime(); _disassembler = new I8088Disassembler(() => Cpu8088.CS); Flow = new I8088Flow(_disassembler);
    }
    private Runtime NewRuntime() => new(_bios, _disk, _ramKilobytes, _pc5150, _writeProtected);
    public PcBus Board => _runtime.Board;
    public I8088Cpu Cpu8088 => _runtime.Cpu;
    public string Id => "pcxt";
    public string DisplayName => "IBM PC / XT";
    public ICpuDebugView Cpu => Cpu8088;
    public IDisassembler Disassembler => _disassembler;
    public IMemoryView Memory => this;
    public int AddressBits => 20;
    public VideoFrame Video => _runtime.Presented;
    public AudioBuffer Audio => _runtime.Audio;
    public double FramesPerSecond => Clock / CyclesPerFrame;
    public long FrameCount => Cpu8088.Cycles / CyclesPerFrame;
    public IFlowAnalyzer Flow { get; }
    public bool[]? ExecutedMap { get; set; }
    public string MemoryMapDescription => "00000-9FFFF RAM instalada | B8000-BFFFF CGA | BIOS al final de FFFFF | huecos: FF";
    public IReadOnlyList<uint> CodeEntryPoints => new uint[] { 0xFFFF0 }.Concat(Enumerable.Range(0, 256)
        .Select(n => I8088Cpu.Physical((ushort)(Peek((uint)(n * 4 + 2)) | Peek((uint)(n * 4 + 3)) << 8),
            (ushort)(Peek((uint)(n * 4)) | Peek((uint)(n * 4 + 1)) << 8))).Where(a => a != 0)).Distinct().ToArray();
    public byte Peek(uint address) => Board.Memory.Read(address);
    public void Poke(uint address, byte value) => Board.Memory.Write(address, value);
    public void Reset(bool hard)
    {
        ReleaseAllKeys(); _sequence = new(); Board.Reset(hard); Cpu8088.Reset(); _runtime.ResetAudio(); _runtime.Present(); _skipBreakpoint = false;
    }
    public RunStopReason RunFrame(DebugController? debug)
    {
        _sequence.Tick(FrameCount, SetKey);
        long end = checked((FrameCount + 1) * CyclesPerFrame);
        while (Cpu8088.Cycles < end)
        {
            uint pc = Cpu8088.PhysicalPC;
            if (!_skipBreakpoint && !Cpu8088.Halted && debug?.IsBreakpoint(pc) == true)
            { debug.Temporary = null; _runtime.Present(); return RunStopReason.Breakpoint; }
            Step(); _skipBreakpoint = false;
        }
        _runtime.Present(); return RunStopReason.FrameComplete;
    }
    private void Step()
    {
        uint pc = Cpu8088.PhysicalPC;
        if (!Cpu8088.Halted && ExecutedMap is { } map && pc < map.Length) map[pc] = true;
        Cpu8088.Step();
    }
    public void StepInstruction() { Step(); _runtime.Present(); }
    public void ResumeFromBreakpoint() => _skipBreakpoint = true;
    public void QueueKeys(int framesHeld, params string[] keys) => _sequence.Enqueue(framesHeld, keys);
    public IReadOnlyList<MachineKey> Keys => PcKeys.All;
    public void SetKey(string name, bool pressed)
    {
        if (!PcKeys.Codes.TryGetValue(name, out byte scan)) return;
        if (pressed ? _held.Add(scan) : _held.Remove(scan)) Board.Keyboard.SendKey(scan, pressed);
    }
    public void ReleaseAllKeys()
    {
        foreach (byte scan in _held) Board.Keyboard.SendKey(scan, false);
        _held.Clear(); Board.Keyboard.StopRepeat();
    }
    public int DrainAudio(short[] destination)
    {
        int count = Math.Min(destination.Length, Audio.Count); Array.Copy(Audio.Samples, destination, count);
        if (count < Audio.Count) Array.Copy(Audio.Samples, count, Audio.Samples, 0, Audio.Count - count);
        Audio.Count -= count; return count;
    }
    public string? SymbolFor(uint address) => address == 0xFFFF0 ? "Vector de reinicio" : address switch
    { >= 0x400 and < 0x500 => "Datos del BIOS", >= 0xB8000 and < 0xC0000 => "Memoria CGA", _ => null };
    public IReadOnlyList<string> GetMachineInfo() =>
    [
        $"{(_pc5150 ? "PC 5150" : "XT 5160")} · 8088 · {Board.Memory.RamKilobytes} KB · CGA",
        $"Reloj {Cpu8088.Cycles:N0} T · cuadro {FrameCount} · vídeo {Board.Cga.FrameCount}",
        $"PIC solicitudes {Board.Pic.RequestRegister:X2} · servicio {Board.Pic.InServiceRegister:X2} · máscara {Board.Pic.Mask:X2}",
        $"Disquete: {(Board.Fdc.Disk == null ? "sin medio" : $"{Board.Fdc.Disk.Cylinders} cilindros, {Board.Fdc.Disk.Heads} caras")}",
        "Escrituras de disquete en capa privada; el archivo original permanece intacto."
    ];

    public byte[] SaveState()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write("RETRO-PCXT"); w.Write(1); w.Write(Board.Memory.BiosIdentity); w.Write(Board.Fdc.Disk?.Identity ?? "");
        w.Write(_pc5150); w.Write(_ramKilobytes); w.Write(_writeProtected);
        Cpu8088.Save(w); Board.Save(w); _runtime.SaveAudio(w);
        w.Write(_held.Count); foreach (byte key in _held.Order()) w.Write(key);
        w.Flush(); byte[] payload = stream.ToArray(); byte[] digest = SHA256.HashData(payload);
        return [.. payload, .. digest];
    }
    public void LoadState(byte[] state)
    {
        if (state.Length is < 64 or > 4 * 1024 * 1024 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(state.AsSpan(0, state.Length - 32)), state.AsSpan(state.Length - 32)))
            throw new InvalidDataException("El estado PC está incompleto o dañado.");
        using var stream = new MemoryStream(state, 0, state.Length - 32, false); using var r = new BinaryReader(stream);
        if (r.ReadString() != "RETRO-PCXT" || r.ReadInt32() != 1) throw new InvalidDataException("La versión del estado PC no es compatible.");
        if (r.ReadString() != Board.Memory.BiosIdentity || r.ReadString() != (Board.Fdc.Disk?.Identity ?? "") ||
            r.ReadBoolean() != _pc5150 || r.ReadInt32() != _ramKilobytes || r.ReadBoolean() != _writeProtected)
            throw new InvalidDataException("El estado pertenece a otro firmware, disquete o configuración de PC.");
        var next = NewRuntime(); next.Cpu.Load(r); next.Board.Load(r); next.LoadAudio(r);
        int count = PcDeviceState.Count(r, 83); byte[] held = r.ReadBytes(count);
        PcDeviceState.Check(held.Length == count && held.All(k => k is > 0 and <= 0x53) && held.Distinct().Count() == count &&
            next.Cpu.TimingEnabled && next.Cpu.Cycles == next.Board.CpuClocks && stream.Position == stream.Length, "máquina PC");
        // All parsing and validation ran on a fresh board. The live machine is replaced only now.
        _runtime = next; _held.Clear(); foreach (byte key in held) _held.Add(key);
        ReleaseAllKeys(); _sequence = new(); _runtime.Present(); _skipBreakpoint = false;
    }

    private sealed class Runtime
    {
        public PcBus Board { get; }
        public I8088Cpu Cpu { get; }
        public VideoFrame Presented { get; } = new(640, 400);
        public AudioBuffer Audio { get; } = new(44100, 8192);
        private double _samplePhase;
        private float _sum, _dcIn, _dcOut;
        private int _samples;
        public Runtime(byte[] bios, byte[] disk, int ram, bool pc5150, bool protectedDisk)
        {
            Board = new PcBus(new PcMemory(bios, ram), pc5150);
            if (disk.Length > 0) Board.Fdc.Mount(new FloppyDiskImage(disk, protectedDisk));
            Cpu = new I8088Cpu(Board, Board.Pic) { TimingEnabled = true }; Cpu.Reset();
            Board.Clocked += SampleClock; Present();
        }
        public void Present()
        {
            int[] source = Board.Cga.Frame.Pixels, target = Presented.Pixels;
            for (int y = 0; y < 200; y++) { Array.Copy(source, y * 640, target, y * 1280, 640); Array.Copy(source, y * 640, target, y * 1280 + 640, 640); }
        }
        private void SampleClock()
        {
            _sum += Board.SpeakerOutput ? 1 : 0; _samples++; _samplePhase++;
            if (_samplePhase < Clock / 44100) return;
            _samplePhase -= Clock / 44100; float value = _sum / _samples; _sum = 0; _samples = 0;
            _dcOut = value - _dcIn + 0.996f * _dcOut; _dcIn = value;
            if (Audio.Count < Audio.Samples.Length) Audio.Samples[Audio.Count++] = (short)Math.Clamp(_dcOut * 12000, short.MinValue, short.MaxValue);
        }
        public void ResetAudio() { _samplePhase = 0; _sum = _dcIn = _dcOut = 0; _samples = Audio.Count = 0; }
        public void SaveAudio(BinaryWriter w)
        {
            w.Write(_samplePhase); w.Write(_sum); w.Write(_dcIn); w.Write(_dcOut); w.Write(_samples); w.Write(Audio.Count);
            for (int i = 0; i < Audio.Count; i++) w.Write(Audio.Samples[i]);
        }
        public void LoadAudio(BinaryReader r)
        {
            _samplePhase = r.ReadDouble(); _sum = r.ReadSingle(); _dcIn = r.ReadSingle(); _dcOut = r.ReadSingle(); _samples = r.ReadInt32();
            PcDeviceState.Check(double.IsFinite(_samplePhase) && _samplePhase >= 0 && _samplePhase < Clock / 44100 && float.IsFinite(_sum) &&
                float.IsFinite(_dcIn) && float.IsFinite(_dcOut) && _samples is >= 0 and <= 109, "audio PC");
            Audio.Count = PcDeviceState.Count(r, Audio.Samples.Length); for (int i = 0; i < Audio.Count; i++) Audio.Samples[i] = r.ReadInt16();
        }
    }
}

internal static class PcKeys
{
    private static readonly string[] Names =
    ["", "ESC", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "BACKSPACE", "TAB",
     "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "ENTER", "CTRL", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "APOSTROPHE", "GRAVE", "LSHIFT", "BACKSLASH",
     "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "RSHIFT", "KP*", "ALT", "SPACE", "CAPS LOCK",
     "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "NUM LOCK", "SCROLL LOCK", "HOME", "UP", "PAGE UP", "KP-", "LEFT", "KP5", "RIGHT", "KP+", "END", "DOWN", "PAGE DOWN", "INSERT", "DELETE"];
    public static readonly Dictionary<string, byte> Codes = Names.Select((n, i) => (n, i)).Skip(1).ToDictionary(p => p.n, p => (byte)p.i, StringComparer.OrdinalIgnoreCase);
    public static readonly IReadOnlyList<MachineKey> All = Names.Skip(1).Select(n => new MachineKey(n, n)).ToArray();
}