namespace Retro.Core;

/// <summary>One register as shown by the debugger.</summary>
public readonly record struct RegisterValue(string Name, int Bits, uint Value);

/// <summary>One status flag as shown by the debugger.</summary>
public readonly record struct FlagValue(string Name, bool Set, string Description);

/// <summary>A decoded instruction for disassembly views.</summary>
public readonly record struct DisassembledInstruction(uint Address, int Length, string Text, byte[] Bytes);

/// <summary>
/// CPU state access for the debugger. Every core exposes its registers, flags and
/// interrupt state through this interface so the debugger UI is CPU independent.
/// </summary>
public interface ICpuDebugView
{
    string CpuName { get; }
    uint ProgramCounter { get; set; }
    uint StackPointer { get; }
    /// <summary>Total clock cycles (T-states) executed since power on.</summary>
    long Cycles { get; }
    IReadOnlyList<RegisterValue> GetRegisters();
    bool TrySetRegister(string name, uint value);
    IReadOnlyList<FlagValue> GetFlags();
    bool TrySetFlag(string name, bool value);
    IReadOnlyList<string> GetInterruptState();
}

public interface IDisassembler
{
    DisassembledInstruction Disassemble(Func<uint, byte> peek, uint address);
}

/// <summary>Side-effect free memory access for debuggers (no contention, no I/O).</summary>
public interface IMemoryView
{
    int AddressBits { get; }
    byte Peek(uint address);
    void Poke(uint address, byte value);
}

/// <summary>Frame buffer produced by a machine: 32-bit ARGB pixels.</summary>
public sealed class VideoFrame
{
    public VideoFrame(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new int[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }
}

/// <summary>Mono audio produced during one emulated frame.</summary>
public sealed class AudioBuffer
{
    public AudioBuffer(int sampleRate, int capacity)
    {
        SampleRate = sampleRate;
        Samples = new short[capacity];
    }

    public int SampleRate { get; }
    public short[] Samples { get; }
    public int Count { get; set; }
}

public enum RunStopReason
{
    FrameComplete,
    Breakpoint,
    StepComplete,
}

/// <summary>Breakpoints and stepping requests shared between the UI and a machine.</summary>
public sealed class DebugController
{
    private readonly HashSet<uint> _breakpoints = new();

    public IReadOnlyCollection<uint> Breakpoints => _breakpoints;
    public bool Enabled => _breakpoints.Count > 0 || Temporary != null;

    /// <summary>One-shot stop address used by "run to cursor" and "step over".</summary>
    public uint? Temporary { get; set; }

    public void Toggle(uint address)
    {
        if (!_breakpoints.Remove(address))
            _breakpoints.Add(address);
    }

    public void Add(uint address) => _breakpoints.Add(address);
    public void Remove(uint address) => _breakpoints.Remove(address);
    public void Clear() => _breakpoints.Clear();
    public bool IsBreakpoint(uint address) => _breakpoints.Contains(address) || Temporary == address;
}

/// <summary>A key on the emulated machine, identified by a machine specific name.</summary>
public readonly record struct MachineKey(string Name, string Label);

/// <summary>
/// An emulated computer or console. Implementations own their CPU, memory map,
/// video, audio and input hardware and advance them in lockstep.
/// </summary>
public interface IMachine
{
    string Id { get; }
    string DisplayName { get; }
    ICpuDebugView Cpu { get; }
    IDisassembler Disassembler { get; }
    IMemoryView Memory { get; }
    VideoFrame Video { get; }
    AudioBuffer Audio { get; }
    double FramesPerSecond { get; }
    long FrameCount { get; }

    void Reset(bool hard);

    /// <summary>Runs until the end of the current frame or until a breakpoint is hit.</summary>
    RunStopReason RunFrame(DebugController? debug);

    /// <summary>Executes exactly one instruction (or one interrupt acceptance).</summary>
    void StepInstruction();

    IReadOnlyList<MachineKey> Keys { get; }
    void SetKey(string name, bool pressed);
    void ReleaseAllKeys();
    /// <summary>Presses keys for a number of frames (scripted input, typed commands).</summary>
    void QueueKeys(int framesHeld, params string[] keys);

    byte[] SaveState();
    void LoadState(byte[] state);

    /// <summary>Takes the audio produced since the last call (44.1 kHz mono).</summary>
    int DrainAudio(short[] destination);

    /// <summary>Call after a breakpoint stop so the next run does not stop at the same PC.</summary>
    void ResumeFromBreakpoint();

    /// <summary>Timing details for the debugger (frame, beam position, interrupts).</summary>
    IReadOnlyList<string> GetMachineInfo();

    /// <summary>Known name for an address (ROM routine, hardware register), or null.</summary>
    string? SymbolFor(uint address);

    /// <summary>One-line summary of the memory map for the memory viewer.</summary>
    string MemoryMapDescription { get; }

    /// <summary>Control-flow classifier of this machine's CPU (routine analysis).</summary>
    IFlowAnalyzer Flow { get; }

    /// <summary>Where execution starts: reset and interrupt vectors.</summary>
    IReadOnlyList<uint> CodeEntryPoints { get; }

    /// <summary>When set (65536 entries), every executed instruction address is marked.</summary>
    bool[]? ExecutedMap { get; set; }
}

/// <summary>
/// Machines whose program lives in ROM let the code editor change it. The change is
/// made to the copy in memory only: the user's file is never modified.
/// </summary>
public interface ICodePatch
{
    void PatchCode(uint address, byte value);
}

/// <summary>Plays scripted key presses: each step holds some keys for N frames.</summary>
public sealed class KeySequencer
{
    private readonly Queue<(string[] keys, int frames)> _queue = new();
    private string[]? _held;
    private long _until = -1;

    public bool Busy => _queue.Count > 0 || _held != null;

    public void Enqueue(int frames, string[] keys) => _queue.Enqueue((keys, frames));

    /// <summary>Call once per emulated frame.</summary>
    public void Tick(long frame, Action<string, bool> setKey)
    {
        if (frame < _until)
            return;
        if (_held != null)
            foreach (var k in _held)
                setKey(k, false);
        _held = null;
        if (_queue.Count == 0)
            return;
        var (keys, frames) = _queue.Dequeue();
        foreach (var k in keys)
            setKey(k, true);
        _held = keys;
        _until = frame + frames;
    }
}

/// <summary>Something the user can load into a machine: tape, disk, snapshot, cartridge.</summary>
public interface IMediaLoader
{
    /// <summary>Human readable format name, e.g. "Cinta TAP".</summary>
    string FormatName { get; }
    IReadOnlyList<string> Extensions { get; }
    /// <summary>0 = not this format, 100 = certain.</summary>
    int Probe(string fileName, ReadOnlySpan<byte> data);
}

/// <summary>
/// Optional for a machine: reports every CPU write to memory with the address of the
/// instruction that made it. Used by the reverse engineering lab and watchpoints; when
/// nobody listens (null) it costs nothing.
/// </summary>
public interface IWriteWatch
{
    /// <summary>address written, value, address of the instruction that wrote it.</summary>
    Action<ushort, byte, ushort>? WriteWatch { get; set; }
}

/// <summary>
/// Reports the reads and writes a program makes to the hardware (Z80 ports, or the 6502's
/// memory-mapped I/O registers that are read): how the routine that reads the keyboard or
/// makes the sound is found.
/// </summary>
public interface IIoWatch
{
    /// <summary>port or register, value, true for a write, address of the instruction.</summary>
    Action<ushort, byte, bool, ushort>? IoWatch { get; set; }
}
