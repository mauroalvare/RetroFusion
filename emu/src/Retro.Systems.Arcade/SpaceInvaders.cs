using Retro.Core;
using Retro.Cpu.I8080;

namespace Retro.Systems.Arcade;

/// <summary>
/// Taito/Midway "8080" board running Space Invaders (1978).
///
/// Intel 8080 at 19.968 MHz / 10 = 1.9968 MHz. 8 KB ROM at $0000, 1 KB work RAM at
/// $2000 and 7 KB of 1-bit video RAM at $2400 (224 lines x 256 pixels). The video
/// timing is 320 x 262 pixel clocks at 4.992 MHz (59.54 Hz): 128 CPU cycles per line.
/// The board requests RST 1 when the beam reaches line 96 and RST 2 at line 224 (start
/// of vertical blank). Ports: inputs 1-2, a hardware barrel shifter (OUT 2/4, IN 3),
/// sound triggers (OUT 3/5) and the watchdog (OUT 6). The monitor is mounted rotated
/// 90 degrees counter-clockwise behind a coloured cellophane overlay.
/// </summary>
public sealed class SpaceInvaders : IMachine, I8080Bus, IMemoryView, ICodePatch
{
    public const int CpuClock = 1_996_800;
    public const int CyclesPerLine = 128;
    public const int TotalLines = 262;
    public const int CyclesPerFrame = CyclesPerLine * TotalLines;   // 33536
    public const int VisibleLines = 224;
    public const int MidScreenLine = 96;
    public const int ScreenWidth = 224, ScreenHeight = 256;          // after rotation
    public const int SampleRate = 44100;

    private const int White = unchecked((int)0xFFF4F4F4);
    private const int Red = unchecked((int)0xFFFF3030);
    private const int Green = unchecked((int)0xFF30FF50);
    private const int Black = unchecked((int)0xFF000000);

    private readonly byte[] _rom;
    private readonly byte[] _ram = new byte[0x2000];
    private readonly I8080Cpu _cpu;
    private readonly I8080Disassembler _disassembler = new();
    private readonly KeySequencer _keys = new();
    private long _frameStart;
    private int _renderedLine;
    private byte? _pendingInterrupt;
    private bool _midFired, _vblankFired;
    private ushort _shift;
    private int _shiftOffset;
    private bool _coin, _p1Start, _p2Start, _p1Left, _p1Right, _p1Fire, _p2Left, _p2Right, _p2Fire, _tilt;
    private bool _skipBreakpointOnce;
    private int _watchdogWrites;

    public SpaceInvaders(byte[] rom)
    {
        if (rom.Length != 0x2000)
            throw new ArgumentException("La ROM de Space Invaders debe tener 8192 bytes (invaders.h, .g, .f, .e).");
        _rom = (byte[])rom.Clone();
        _cpu = new I8080Cpu(this);
        _disassembler.Symbols = a => SymbolFor(a);
        Video = new VideoFrame(ScreenWidth, ScreenHeight);
        Audio = new AudioBuffer(SampleRate, 4096);
        Sound = new SpaceInvadersSound(SampleRate);
        Reset(true);
    }

    public string Id => "invaders";
    public string DisplayName => "Space Invaders (Taito/Midway, 1978)";
    public I8080Cpu I8080 => _cpu;
    public ICpuDebugView Cpu => _cpu;
    public IDisassembler Disassembler => _disassembler;
    public IMemoryView Memory => this;
    public VideoFrame Video { get; }
    public AudioBuffer Audio { get; }
    public SpaceInvadersSound Sound { get; }
    public double FramesPerSecond => (double)CpuClock / CyclesPerFrame;
    public long FrameCount { get; private set; }
    public long FrameCycle => _cpu.Cycles - _frameStart;

    // DIP switches (bank on the board)
    /// <summary>Lives per game: 3, 4, 5 or 6.</summary>
    public int Ships { get; set; } = 3;
    /// <summary>Extra base at 1000 points instead of 1500.</summary>
    public bool BonusAt1000 { get; set; }
    /// <summary>Show the coin information on the attract screen.</summary>
    public bool ShowCoinInfo { get; set; } = true;

    public void Reset(bool hard)
    {
        if (hard)
            Array.Clear(_ram);
        _cpu.Reset();
        _cpu.Cycles = 0;
        _frameStart = 0;
        _renderedLine = 0;
        _pendingInterrupt = null;
        _midFired = _vblankFired = false;
        _shift = 0;
        _shiftOffset = 0;
        FrameCount = 0;
        Sound.Reset();
        Audio.Count = 0;
        ReleaseAllKeys();
    }

    // ------------------------------------------------------------------ bus
    public byte ReadMemory(ushort address)
    {
        int a = address & 0x3FFF;
        return a < 0x2000 ? _rom[a] : _ram[a - 0x2000];
    }

    public void WriteMemory(ushort address, byte value)
    {
        int a = address & 0x3FFF;
        if (a < 0x2000)
            return;                                   // ROM
        if (a >= 0x2400)
            RenderTo(CurrentLine());
        _ram[a - 0x2000] = value;
    }

    public byte In(byte port) => port switch
    {
        0 => 0x0E,
        1 => (byte)((_coin ? 0x01 : 0) | (_p2Start ? 0x02 : 0) | (_p1Start ? 0x04 : 0) | 0x08
                    | (_p1Fire ? 0x10 : 0) | (_p1Left ? 0x20 : 0) | (_p1Right ? 0x40 : 0)),
        2 => (byte)(((Ships - 3) & 3) | (_tilt ? 0x04 : 0) | (BonusAt1000 ? 0x08 : 0)
                    | (_p2Fire ? 0x10 : 0) | (_p2Left ? 0x20 : 0) | (_p2Right ? 0x40 : 0) | (ShowCoinInfo ? 0 : 0x80)),
        3 => (byte)(_shift >> (8 - _shiftOffset)),
        _ => 0x00,
    };

    public void Out(byte port, byte value)
    {
        switch (port)
        {
            case 2: _shiftOffset = value & 7; break;
            case 4: _shift = (ushort)((value << 8) | (_shift >> 8)); break;
            case 3: Sound.WritePort3(value, PositionInFrame()); break;
            case 5: Sound.WritePort5(value, PositionInFrame()); break;
            case 6: _watchdogWrites++; break;
        }
    }

    private double PositionInFrame() => (double)FrameCycle / CyclesPerFrame;

    private int CurrentLine() => (int)Math.Min(FrameCycle / CyclesPerLine, VisibleLines);

    // ------------------------------------------------------------------ video
    /// <summary>Draws the hardware lines that the beam has passed (up to, not including, <paramref name="line"/>).</summary>
    private void RenderTo(int line)
    {
        var px = Video.Pixels;
        for (; _renderedLine < line && _renderedLine < VisibleLines; _renderedLine++)
        {
            int y = _renderedLine;                    // becomes the screen column
            int src = 0x0400 + y * 32;
            for (int bx = 0; bx < 32; bx++)
            {
                byte bits = _ram[src + bx];
                for (int bit = 0; bit < 8; bit++)
                {
                    int x = bx * 8 + bit;             // hardware pixel, LSB first
                    int row = 255 - x;
                    px[row * ScreenWidth + y] = (bits & (1 << bit)) != 0 ? OverlayColor(y, row) : Black;
                }
            }
        }
    }

    /// <summary>The cabinet's cellophane: red band for the UFO, green for the bases.</summary>
    private static int OverlayColor(int col, int row)
    {
        if (row >= 32 && row < 64)
            return Red;
        if (row >= 184 && row < 240)
            return Green;
        if (row >= 240 && col >= 16 && col < 134)
            return Green;
        return White;
    }

    // ------------------------------------------------------------------ execution
    public RunStopReason RunFrame(DebugController? debug)
    {
        long frameEnd = _frameStart + CyclesPerFrame;
        while (_cpu.Cycles < frameEnd)
        {
            if (debug != null && debug.Enabled && !_skipBreakpointOnce && debug.IsBreakpoint(_cpu.PC))
                return RunStopReason.Breakpoint;
            _skipBreakpointOnce = false;
            ExecuteOne();
        }
        EndFrame();
        return RunStopReason.FrameComplete;
    }

    public void ResumeFromBreakpoint() => _skipBreakpointOnce = true;

    public void StepInstruction()
    {
        ExecuteOne();
        if (_cpu.Cycles >= _frameStart + CyclesPerFrame)
            EndFrame();
    }

    public IFlowAnalyzer Flow { get; } = new I8080Flow();
    public IReadOnlyList<uint> CodeEntryPoints { get; } = new uint[] { 0x0000, 0x0008, 0x0010 };
    public bool[]? ExecutedMap { get; set; }

    private void ExecuteOne()
    {
        if (ExecutedMap != null)
            ExecutedMap[_cpu.PC] = true;
        long cycle = FrameCycle;
        if (!_midFired && cycle >= MidScreenLine * CyclesPerLine)
        {
            _midFired = true;
            _pendingInterrupt = 0xCF;                 // RST 1
        }
        if (!_vblankFired && cycle >= VisibleLines * CyclesPerLine)
        {
            _vblankFired = true;
            _pendingInterrupt = 0xD7;                 // RST 2
        }
        if (_pendingInterrupt is byte rst && _cpu.InterruptsAccepted)
        {
            _cpu.Interrupt(rst);
            _pendingInterrupt = null;
            return;
        }
        _cpu.Step();
    }

    private void EndFrame()
    {
        RenderTo(VisibleLines);
        _renderedLine = 0;
        Sound.EndFrame(Audio, FramesPerSecond);
        _frameStart += CyclesPerFrame;
        _midFired = _vblankFired = false;
        FrameCount++;
        _keys.Tick(FrameCount, SetKey);
    }

    public int DrainAudio(short[] destination)
    {
        int n = Math.Min(destination.Length, Audio.Count);
        Array.Copy(Audio.Samples, destination, n);
        Audio.Count = 0;
        return n;
    }

    // ------------------------------------------------------------------ input
    public IReadOnlyList<MachineKey> Keys { get; } = new[]
    {
        new MachineKey("COIN", "Moneda"), new MachineKey("P1 START", "1 jugador"), new MachineKey("P2 START", "2 jugadores"),
        new MachineKey("P1 LEFT", "J1 izquierda"), new MachineKey("P1 RIGHT", "J1 derecha"), new MachineKey("P1 FIRE", "J1 disparo"),
        new MachineKey("P2 LEFT", "J2 izquierda"), new MachineKey("P2 RIGHT", "J2 derecha"), new MachineKey("P2 FIRE", "J2 disparo"),
        new MachineKey("TILT", "Tilt"),
    };

    public void SetKey(string name, bool pressed)
    {
        switch (name.ToUpperInvariant())
        {
            case "COIN": _coin = pressed; break;
            case "P1 START": _p1Start = pressed; break;
            case "P2 START": _p2Start = pressed; break;
            case "P1 LEFT": _p1Left = pressed; break;
            case "P1 RIGHT": _p1Right = pressed; break;
            case "P1 FIRE": _p1Fire = pressed; break;
            case "P2 LEFT": _p2Left = pressed; break;
            case "P2 RIGHT": _p2Right = pressed; break;
            case "P2 FIRE": _p2Fire = pressed; break;
            case "TILT": _tilt = pressed; break;
        }
    }

    public void ReleaseAllKeys()
    {
        _coin = _p1Start = _p2Start = _p1Left = _p1Right = _p1Fire = _p2Left = _p2Right = _p2Fire = _tilt = false;
    }

    public void QueueKeys(int framesHeld, params string[] keys) => _keys.Enqueue(framesHeld, keys);

    public bool TypingInProgress => _keys.Busy;

    // ------------------------------------------------------------------ debugger
    public IReadOnlyList<string> GetMachineInfo()
    {
        long c = FrameCycle;
        long line = c / CyclesPerLine;
        return new[]
        {
            $"Ciclos totales: {_cpu.Cycles:N0}",
            $"Cuadro {FrameCount}, ciclo {c} de {CyclesPerFrame}",
            $"Haz: línea {line} de {TotalLines}{(line >= VisibleLines ? " (retrazado vertical)" : "")}",
            $"Interrupción pendiente: {(_pendingInterrupt is byte p ? (p == 0xCF ? "RST 1 (mitad de pantalla)" : "RST 2 (VBLANK)") : "ninguna")}",
            $"Shifter: {_shift:X4} desplazamiento {_shiftOffset}  -> IN 3 = {(byte)(_shift >> (8 - _shiftOffset)):X2}",
            $"Sonido: puerto 3 = {Sound.Port3:X2}, puerto 5 = {Sound.Port5:X2}",
        };
    }

    public string? SymbolFor(uint address) => (address & 0xFFFF) switch
    {
        0x0000 => "RESET",
        0x0008 => "RST1_MIDSCREEN",
        0x0010 => "RST2_VBLANK",
        _ => null,
    };

    public string MemoryMapDescription => "0000-1FFF ROM | 2000-23FF RAM de trabajo | 2400-3FFF video (224 líneas x 32 bytes)";

    // ------------------------------------------------------------------ IMemoryView
    public int AddressBits => 16;
    public byte Peek(uint address) => ReadMemory((ushort)address);

    public void Poke(uint address, byte value)
    {
        int a = (int)(address & 0x3FFF);
        if (a >= 0x2000)
            _ram[a - 0x2000] = value;
    }

    /// <summary>Changes the game program (the ROM copy in memory) for the code editor.</summary>
    public void PatchCode(uint address, byte value)
    {
        int a = (int)(address & 0x3FFF);
        if (a < 0x2000)
            _rom[a] = value;
        else
            _ram[a - 0x2000] = value;
    }

    // ------------------------------------------------------------------ save states
    private const string StateMagic = "RETRO-INVADERS";

    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(StateMagic);
        w.Write(1);
        w.Write(_cpu.PSW); w.Write(_cpu.BC); w.Write(_cpu.DE); w.Write(_cpu.HL); w.Write(_cpu.SP); w.Write(_cpu.PC);
        w.Write(_cpu.INTE); w.Write(_cpu.Halted);
        w.Write(FrameCycle);
        w.Write(_ram);
        w.Write(_shift); w.Write(_shiftOffset);
        w.Write(_pendingInterrupt ?? 0); w.Write(_pendingInterrupt.HasValue);
        w.Write(_midFired); w.Write(_vblankFired);
        w.Write(FrameCount);
        w.Write(Sound.Port3); w.Write(Sound.Port5);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var r = new BinaryReader(new MemoryStream(state));
        if (r.ReadString() != StateMagic || r.ReadInt32() != 1)
            throw new InvalidDataException("No es un estado guardado de Space Invaders.");
        _cpu.PSW = r.ReadUInt16(); _cpu.BC = r.ReadUInt16(); _cpu.DE = r.ReadUInt16(); _cpu.HL = r.ReadUInt16();
        _cpu.SP = r.ReadUInt16(); _cpu.PC = r.ReadUInt16();
        _cpu.INTE = r.ReadBoolean(); _cpu.Halted = r.ReadBoolean();
        long cycle = r.ReadInt64();
        _frameStart = 0;
        _cpu.Cycles = cycle;
        r.Read(_ram, 0, _ram.Length);
        _shift = r.ReadUInt16(); _shiftOffset = r.ReadInt32();
        byte pending = r.ReadByte(); bool hasPending = r.ReadBoolean();
        _pendingInterrupt = hasPending ? pending : null;
        _midFired = r.ReadBoolean(); _vblankFired = r.ReadBoolean();
        FrameCount = r.ReadInt64();
        byte p3 = r.ReadByte(), p5 = r.ReadByte();
        Sound.Reset();
        Sound.WritePort3(p3, 0);
        Sound.WritePort5(p5, 0);
        _renderedLine = 0;
        Audio.Count = 0;
    }
}
