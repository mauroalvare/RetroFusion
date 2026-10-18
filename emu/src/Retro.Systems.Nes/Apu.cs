namespace Retro.Systems.Nes;

/// <summary>
/// The 2A03's sound unit (NTSC): two pulse channels with envelope and sweep, triangle,
/// noise and the delta modulation channel (DMC), plus the frame counter that clocks
/// envelopes, lengths and sweeps and can raise an IRQ. Advanced once per CPU cycle; the
/// mixer uses the nonlinear formula of the real chip and the machine averages it into
/// 44.1 kHz samples.
/// </summary>
public sealed class Apu
{
    private static readonly byte[] LengthTable =
    {
        10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30,
    };

    private static readonly byte[][] DutyTable =
    {
        new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0, 1, 1, 0, 0, 0, 0, 0 },
        new byte[] { 0, 1, 1, 1, 1, 0, 0, 0 },
        new byte[] { 1, 0, 0, 1, 1, 1, 1, 1 },
    };

    private static readonly byte[] TriangleTable =
    {
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    };

    private static readonly ushort[] NoisePeriods = { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };
    private static readonly ushort[] DmcRates = { 428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54 };

    private static readonly float[] PulseMix = BuildPulseMix();
    private static readonly float[] TndMix = BuildTndMix();

    private static float[] BuildPulseMix()
    {
        var t = new float[31];
        for (int i = 1; i < t.Length; i++)
            t[i] = (float)(95.52 / (8128.0 / i + 100));
        return t;
    }

    private static float[] BuildTndMix()
    {
        var t = new float[203];
        for (int i = 1; i < t.Length; i++)
            t[i] = (float)(163.67 / (24329.0 / i + 100));
        return t;
    }

    /// <summary>Where the DMC fetches its samples (CPU address space, $8000-$FFFF).</summary>
    public Func<ushort, byte> ReadMemory { get; set; } = _ => 0;

    private readonly Pulse _pulse1 = new(onesComplement: true), _pulse2 = new(onesComplement: false);
    private readonly Triangle _triangle = new();
    private readonly Noise _noise = new();
    private readonly Dmc _dmc = new();
    private bool _fiveStep, _irqInhibit, _frameIrq;
    private int _frameCycle;
    private int _pendingFrameWrite = -1, _frameWriteDelay;
    private bool _evenCycle;

    /// <summary>IRQ output: the frame counter or the DMC.</summary>
    public bool Irq => _frameIrq || _dmc.Irq;

    public void Reset()
    {
        foreach (var r in Enumerable.Range(0x4000, 0x14))
            Write((ushort)r, 0);
        Write(0x4015, 0);
        _fiveStep = false;
        _irqInhibit = false;
        _frameIrq = false;
        _frameCycle = 0;
        _pendingFrameWrite = -1;
        _noise.Shift = 1;
    }

    // ---------------------------------------------------------------- registers

    public void Write(ushort address, byte v)
    {
        switch (address)
        {
            case 0x4000: case 0x4001: case 0x4002: case 0x4003:
                _pulse1.Write(address & 3, v);
                break;
            case 0x4004: case 0x4005: case 0x4006: case 0x4007:
                _pulse2.Write(address & 3, v);
                break;
            case 0x4008:
                _triangle.Control = (v & 0x80) != 0;
                _triangle.LinearReload = v & 0x7F;
                break;
            case 0x400A:
                _triangle.Period = _triangle.Period & 0x700 | v;
                break;
            case 0x400B:
                _triangle.Period = _triangle.Period & 0xFF | (v & 7) << 8;
                if (_triangle.Enabled) _triangle.Length = LengthTable[v >> 3];
                _triangle.LinearReloadFlag = true;
                break;
            case 0x400C:
                _noise.Envelope.Write(v);
                _noise.Halt = (v & 0x20) != 0;
                break;
            case 0x400E:
                _noise.Mode = (v & 0x80) != 0;
                _noise.Period = NoisePeriods[v & 0x0F];
                break;
            case 0x400F:
                if (_noise.Enabled) _noise.Length = LengthTable[v >> 3];
                _noise.Envelope.Start = true;
                break;
            case 0x4010:
                _dmc.IrqEnabled = (v & 0x80) != 0;
                _dmc.Loop = (v & 0x40) != 0;
                _dmc.Rate = DmcRates[v & 0x0F];
                if (!_dmc.IrqEnabled) _dmc.Irq = false;
                break;
            case 0x4011:
                _dmc.Output = v & 0x7F;
                break;
            case 0x4012:
                _dmc.SampleAddress = (ushort)(0xC000 | v << 6);
                break;
            case 0x4013:
                _dmc.SampleLength = v << 4 | 1;
                break;
            case 0x4015:
                _pulse1.Enabled = (v & 1) != 0; if (!_pulse1.Enabled) _pulse1.Length = 0;
                _pulse2.Enabled = (v & 2) != 0; if (!_pulse2.Enabled) _pulse2.Length = 0;
                _triangle.Enabled = (v & 4) != 0; if (!_triangle.Enabled) _triangle.Length = 0;
                _noise.Enabled = (v & 8) != 0; if (!_noise.Enabled) _noise.Length = 0;
                _dmc.Irq = false;
                if ((v & 0x10) == 0) _dmc.BytesLeft = 0;
                else if (_dmc.BytesLeft == 0) _dmc.Restart();
                break;
            case 0x4017:
                _irqInhibit = (v & 0x40) != 0;
                if (_irqInhibit) _frameIrq = false;
                // the new mode takes effect 3 or 4 CPU cycles later
                _pendingFrameWrite = v;
                _frameWriteDelay = _evenCycle ? 3 : 4;
                break;
        }
    }

    /// <summary>$4015: which channels still have length (and DMC bytes), frame IRQ (cleared by the read) and DMC IRQ.</summary>
    public byte ReadStatus()
    {
        byte s = Status;
        _frameIrq = false;
        return s;
    }

    /// <summary>$4015 without the side effect (debugger).</summary>
    public byte Status => (byte)(
        (_pulse1.Length > 0 ? 1 : 0) | (_pulse2.Length > 0 ? 2 : 0) | (_triangle.Length > 0 ? 4 : 0) |
        (_noise.Length > 0 ? 8 : 0) | (_dmc.BytesLeft > 0 ? 0x10 : 0) | (_frameIrq ? 0x40 : 0) | (_dmc.Irq ? 0x80 : 0));

    // ---------------------------------------------------------------- timing

    /// <summary>Advances one CPU cycle.</summary>
    public void Step()
    {
        _evenCycle = !_evenCycle;
        _triangle.Clock();
        if (_evenCycle)
        {
            _pulse1.Clock();
            _pulse2.Clock();
            _noise.Clock();
        }
        _dmc.Clock(ReadMemory);
        StepFrameCounter();
    }

    private void StepFrameCounter()
    {
        if (_pendingFrameWrite >= 0 && --_frameWriteDelay == 0)
        {
            _fiveStep = (_pendingFrameWrite & 0x80) != 0;
            _pendingFrameWrite = -1;
            _frameCycle = 0;
            if (_fiveStep)
            {
                QuarterFrame();
                HalfFrame();
            }
            return;
        }
        _frameCycle++;
        switch (_frameCycle)
        {
            case 7457: QuarterFrame(); break;
            case 14913: QuarterFrame(); HalfFrame(); break;
            case 22371: QuarterFrame(); break;
            case 29828: if (!_fiveStep && !_irqInhibit) _frameIrq = true; break;
            case 29829:
                if (!_fiveStep)
                {
                    QuarterFrame();
                    HalfFrame();
                    if (!_irqInhibit) _frameIrq = true;
                }
                break;
            case 29830:
                if (!_fiveStep)
                {
                    if (!_irqInhibit) _frameIrq = true;
                    _frameCycle = 0;
                }
                break;
            case 37281: QuarterFrame(); HalfFrame(); break;
            case 37282: _frameCycle = 0; break;
        }
    }

    private void QuarterFrame()
    {
        _pulse1.Envelope.Clock();
        _pulse2.Envelope.Clock();
        _noise.Envelope.Clock();
        _triangle.ClockLinear();
    }

    private void HalfFrame()
    {
        _pulse1.ClockLengthAndSweep();
        _pulse2.ClockLengthAndSweep();
        if (!_triangle.Control && _triangle.Length > 0) _triangle.Length--;
        if (!_noise.Halt && _noise.Length > 0) _noise.Length--;
    }

    /// <summary>Mixed output of the five channels, 0 to about 1.</summary>
    public float Output =>
        PulseMix[_pulse1.Output + _pulse2.Output] + TndMix[3 * _triangle.Output + 2 * _noise.Output + _dmc.Output];

    public string Describe() =>
        $"pulsos {(_pulse1.Length > 0 ? "1" : "-")}{(_pulse2.Length > 0 ? "2" : "-")}, triángulo {(_triangle.Length > 0 ? "sí" : "-")}, " +
        $"ruido {(_noise.Length > 0 ? "sí" : "-")}, DMC {(_dmc.BytesLeft > 0 ? "sí" : "-")} | contador de cuadros de {(_fiveStep ? 5 : 4)} pasos" +
        (_frameIrq ? ", IRQ pedida" : "");

    // ---------------------------------------------------------------- save states

    public void Save(BinaryWriter w)
    {
        _pulse1.Save(w); _pulse2.Save(w); _triangle.Save(w); _noise.Save(w); _dmc.Save(w);
        w.Write(_fiveStep); w.Write(_irqInhibit); w.Write(_frameIrq); w.Write(_frameCycle);
        w.Write(_pendingFrameWrite); w.Write(_frameWriteDelay); w.Write(_evenCycle);
    }

    public void Load(BinaryReader r)
    {
        _pulse1.Load(r); _pulse2.Load(r); _triangle.Load(r); _noise.Load(r); _dmc.Load(r);
        _fiveStep = r.ReadBoolean(); _irqInhibit = r.ReadBoolean(); _frameIrq = r.ReadBoolean(); _frameCycle = r.ReadInt32();
        _pendingFrameWrite = r.ReadInt32(); _frameWriteDelay = r.ReadInt32(); _evenCycle = r.ReadBoolean();
    }

    // ---------------------------------------------------------------- channels

    private sealed class Envelope
    {
        public bool Start, Loop, Constant;
        public int Volume, Divider, Decay;

        public void Write(byte v)
        {
            Loop = (v & 0x20) != 0;
            Constant = (v & 0x10) != 0;
            Volume = v & 0x0F;
        }

        public void Clock()
        {
            if (Start)
            {
                Start = false;
                Decay = 15;
                Divider = Volume;
                return;
            }
            if (Divider > 0)
            {
                Divider--;
                return;
            }
            Divider = Volume;
            if (Decay > 0) Decay--;
            else if (Loop) Decay = 15;
        }

        public int Output => Constant ? Volume : Decay;

        public void Save(BinaryWriter w) { w.Write(Start); w.Write(Loop); w.Write(Constant); w.Write(Volume); w.Write(Divider); w.Write(Decay); }
        public void Load(BinaryReader r) { Start = r.ReadBoolean(); Loop = r.ReadBoolean(); Constant = r.ReadBoolean(); Volume = r.ReadInt32(); Divider = r.ReadInt32(); Decay = r.ReadInt32(); }
    }

    private sealed class Pulse
    {
        private readonly bool _onesComplement;   // pulse 1 negates with one's complement
        public readonly Envelope Envelope = new();
        public bool Enabled;
        public int Length, Duty, Step, Period, Timer;
        private bool _sweepEnabled, _sweepNegate, _sweepReload;
        private int _sweepPeriod, _sweepShift, _sweepDivider;

        public Pulse(bool onesComplement) => _onesComplement = onesComplement;

        public void Write(int reg, byte v)
        {
            switch (reg)
            {
                case 0:
                    Duty = v >> 6;
                    Envelope.Write(v);
                    break;
                case 1:
                    _sweepEnabled = (v & 0x80) != 0;
                    _sweepPeriod = v >> 4 & 7;
                    _sweepNegate = (v & 0x08) != 0;
                    _sweepShift = v & 7;
                    _sweepReload = true;
                    break;
                case 2:
                    Period = Period & 0x700 | v;
                    break;
                case 3:
                    Period = Period & 0xFF | (v & 7) << 8;
                    if (Enabled) Length = LengthTable[v >> 3];
                    Step = 0;
                    Envelope.Start = true;
                    break;
            }
        }

        public void Clock()
        {
            if (Timer == 0)
            {
                Timer = Period;
                Step = (Step + 1) & 7;
            }
            else
                Timer--;
        }

        private int SweepTarget
        {
            get
            {
                int change = Period >> _sweepShift;
                if (!_sweepNegate) return Period + change;
                return Period - change - (_onesComplement ? 1 : 0);
            }
        }

        private bool Muted => Period < 8 || SweepTarget > 0x7FF;

        public void ClockLengthAndSweep()
        {
            if (!Envelope.Loop && Length > 0) Length--;   // the envelope's loop flag is the length halt
            if (_sweepDivider == 0 && _sweepEnabled && _sweepShift > 0 && !Muted)
                Period = Math.Max(0, SweepTarget);
            if (_sweepDivider == 0 || _sweepReload)
            {
                _sweepDivider = _sweepPeriod;
                _sweepReload = false;
            }
            else
                _sweepDivider--;
        }

        public int Output => Length == 0 || Muted || DutyTable[Duty][Step] == 0 ? 0 : Envelope.Output;

        public void Save(BinaryWriter w)
        {
            Envelope.Save(w);
            w.Write(Enabled); w.Write(Length); w.Write(Duty); w.Write(Step); w.Write(Period); w.Write(Timer);
            w.Write(_sweepEnabled); w.Write(_sweepNegate); w.Write(_sweepReload); w.Write(_sweepPeriod); w.Write(_sweepShift); w.Write(_sweepDivider);
        }

        public void Load(BinaryReader r)
        {
            Envelope.Load(r);
            Enabled = r.ReadBoolean(); Length = r.ReadInt32(); Duty = r.ReadInt32(); Step = r.ReadInt32(); Period = r.ReadInt32(); Timer = r.ReadInt32();
            _sweepEnabled = r.ReadBoolean(); _sweepNegate = r.ReadBoolean(); _sweepReload = r.ReadBoolean(); _sweepPeriod = r.ReadInt32(); _sweepShift = r.ReadInt32(); _sweepDivider = r.ReadInt32();
        }
    }

    private sealed class Triangle
    {
        public bool Enabled, Control, LinearReloadFlag;
        public int Length, Period, Timer, Step, Linear, LinearReload;

        public void Clock()
        {
            if (Timer == 0)
            {
                Timer = Period;
                // periods below 2 are ultrasonic: games use them to silence the channel
                if (Length > 0 && Linear > 0 && Period >= 2)
                    Step = (Step + 1) & 31;
            }
            else
                Timer--;
        }

        public void ClockLinear()
        {
            if (LinearReloadFlag) Linear = LinearReload;
            else if (Linear > 0) Linear--;
            if (!Control) LinearReloadFlag = false;
        }

        public int Output => TriangleTable[Step];

        public void Save(BinaryWriter w) { w.Write(Enabled); w.Write(Control); w.Write(LinearReloadFlag); w.Write(Length); w.Write(Period); w.Write(Timer); w.Write(Step); w.Write(Linear); w.Write(LinearReload); }
        public void Load(BinaryReader r) { Enabled = r.ReadBoolean(); Control = r.ReadBoolean(); LinearReloadFlag = r.ReadBoolean(); Length = r.ReadInt32(); Period = r.ReadInt32(); Timer = r.ReadInt32(); Step = r.ReadInt32(); Linear = r.ReadInt32(); LinearReload = r.ReadInt32(); }
    }

    private sealed class Noise
    {
        public readonly Envelope Envelope = new();
        public bool Enabled, Halt, Mode;
        public int Length, Period = 4, Timer, Shift = 1;

        public void Clock()
        {
            if (Timer == 0)
            {
                Timer = Period / 2 - 1;   // the table is in CPU cycles; this runs every other one
                int feedback = (Shift ^ (Shift >> (Mode ? 6 : 1))) & 1;
                Shift = Shift >> 1 | feedback << 14;
            }
            else
                Timer--;
        }

        public int Output => Length == 0 || (Shift & 1) != 0 ? 0 : Envelope.Output;

        public void Save(BinaryWriter w) { Envelope.Save(w); w.Write(Enabled); w.Write(Halt); w.Write(Mode); w.Write(Length); w.Write(Period); w.Write(Timer); w.Write(Shift); }
        public void Load(BinaryReader r) { Envelope.Load(r); Enabled = r.ReadBoolean(); Halt = r.ReadBoolean(); Mode = r.ReadBoolean(); Length = r.ReadInt32(); Period = r.ReadInt32(); Timer = r.ReadInt32(); Shift = r.ReadInt32(); }
    }

    private sealed class Dmc
    {
        public bool IrqEnabled, Loop, Irq;
        public int Rate = 428, Timer, Output, SampleLength = 1, BytesLeft;
        public ushort SampleAddress = 0xC000, Address;
        private int _buffer = -1, _shift, _bitsLeft = 8;
        private bool _silent = true;

        public void Restart()
        {
            Address = SampleAddress;
            BytesLeft = SampleLength;
        }

        public void Clock(Func<ushort, byte> read)
        {
            // memory reader: fills the one-byte buffer as soon as it is empty
            if (_buffer < 0 && BytesLeft > 0)
            {
                _buffer = read(Address);
                Address = Address == 0xFFFF ? (ushort)0x8000 : (ushort)(Address + 1);
                if (--BytesLeft == 0)
                {
                    if (Loop) Restart();
                    else if (IrqEnabled) Irq = true;
                }
            }
            if (Timer > 0)
            {
                Timer--;
                return;
            }
            Timer = Rate - 1;
            if (!_silent)
            {
                if ((_shift & 1) != 0) { if (Output <= 125) Output += 2; }
                else if (Output >= 2) Output -= 2;
            }
            _shift >>= 1;
            if (--_bitsLeft == 0)
            {
                _bitsLeft = 8;
                _silent = _buffer < 0;
                if (!_silent)
                {
                    _shift = _buffer;
                    _buffer = -1;
                }
            }
        }

        public void Save(BinaryWriter w)
        {
            w.Write(IrqEnabled); w.Write(Loop); w.Write(Irq); w.Write(Rate); w.Write(Timer); w.Write(Output); w.Write(SampleLength); w.Write(BytesLeft);
            w.Write(SampleAddress); w.Write(Address); w.Write(_buffer); w.Write(_shift); w.Write(_bitsLeft); w.Write(_silent);
        }

        public void Load(BinaryReader r)
        {
            IrqEnabled = r.ReadBoolean(); Loop = r.ReadBoolean(); Irq = r.ReadBoolean(); Rate = r.ReadInt32(); Timer = r.ReadInt32(); Output = r.ReadInt32(); SampleLength = r.ReadInt32(); BytesLeft = r.ReadInt32();
            SampleAddress = r.ReadUInt16(); Address = r.ReadUInt16(); _buffer = r.ReadInt32(); _shift = r.ReadInt32(); _bitsLeft = r.ReadInt32(); _silent = r.ReadBoolean();
        }
    }
}
