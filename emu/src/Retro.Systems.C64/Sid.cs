namespace Retro.Systems.C64;

/// <summary>
/// MOS 6581 SID: three voices (triangle, sawtooth, pulse, noise and their combinations,
/// ring modulation and hard sync), an ADSR envelope per voice and a multimode filter. The
/// oscillators are stepped with the exact number of chip cycles per output sample; the
/// envelope and filter follow the datasheet timings with simplified curves.
/// </summary>
public sealed class Sid
{
    private readonly byte[] _regs = new byte[32];
    private readonly Voice[] _voices = { new(), new(), new() };
    private readonly double _cyclesPerSample;
    private double _cycleCarry;
    private double _lp, _bp;
    private int _lastWave3;

    // attack times in ms (datasheet); decay and release take three times longer
    private static readonly double[] AttackMs = { 2, 8, 16, 24, 38, 56, 68, 80, 100, 250, 500, 800, 1000, 3000, 5000, 8000 };

    private sealed class Voice
    {
        public long Acc;
        public uint Lfsr = 0x7FFFF8;
        public double Env;
        public int State;            // 0 attack, 1 decay/sustain, 2 release
        public bool Gate;
        public bool Overflowed;      // the accumulator wrapped during this sample (hard sync)
        public int PulseWidth;
    }

    public Sid(double clock, int sampleRate = 44100) => _cyclesPerSample = clock / sampleRate;

    public void Reset()
    {
        Array.Clear(_regs);
        foreach (var v in _voices)
        {
            v.Acc = 0;
            v.Lfsr = 0x7FFFF8;
            v.Env = 0;
            v.State = 2;
            v.Gate = false;
        }
        _lp = _bp = 0;
    }

    public byte Read(int reg) => (reg & 0x1F) switch
    {
        0x19 or 0x1A => 0xFF,                         // paddles: nothing connected
        0x1B => (byte)(_lastWave3 >> 4),              // OSC3: voice 3 waveform
        0x1C => (byte)_voices[2].Env,                 // ENV3: voice 3 envelope
        _ => 0,                                       // write-only registers read as 0 here
    };

    public byte Peek(int reg) => _regs[reg & 0x1F];

    public void Write(int reg, byte value)
    {
        reg &= 0x1F;
        if (reg > 0x18)
            return;
        _regs[reg] = value;
        if (reg % 7 == 4 && reg < 21)
        {
            var v = _voices[reg / 7];
            bool gate = (value & 1) != 0;
            if (gate && !v.Gate) v.State = 0;
            else if (!gate && v.Gate) v.State = 2;
            v.Gate = gate;
            if ((value & 0x08) != 0)
            {
                v.Acc = 0;
                v.Lfsr = 0x7FFFF8;
            }
        }
    }

    /// <summary>Produces the next 44.1 kHz output sample.</summary>
    public short NextSample()
    {
        _cycleCarry += _cyclesPerSample;
        int cycles = (int)_cycleCarry;
        _cycleCarry -= cycles;

        for (int i = 0; i < 3; i++)
        {
            var v = _voices[i];
            int b = i * 7;
            byte ctrl = _regs[b + 4];
            if ((ctrl & 0x08) != 0)
            {
                v.Overflowed = false;
                continue;
            }
            long freq = _regs[b] | (_regs[b + 1] << 8);
            long before = v.Acc;
            long after = before + freq * cycles;
            // noise is clocked by every rising edge of accumulator bit 19
            long rises = ((after - 0x80000) >> 20) - ((before - 0x80000) >> 20);
            for (long n = 0; n < rises && n < 64; n++)
            {
                uint bit = ((v.Lfsr >> 22) ^ (v.Lfsr >> 17)) & 1;
                v.Lfsr = ((v.Lfsr << 1) | bit) & 0x7FFFFF;
            }
            v.Overflowed = after > 0xFFFFFF;
            v.Acc = after & 0xFFFFFF;
        }
        // hard sync: a voice restarts when the previous one wraps
        for (int i = 0; i < 3; i++)
        {
            int src = (i + 2) % 3;
            if ((_regs[i * 7 + 4] & 0x02) != 0 && _voices[src].Overflowed)
                _voices[i].Acc = 0;
        }

        double filterIn = 0, direct = 0;
        byte route = _regs[0x17];
        byte mode = _regs[0x18];
        for (int i = 0; i < 3; i++)
        {
            var v = _voices[i];
            int b = i * 7;
            byte ctrl = _regs[b + 4];
            v.PulseWidth = _regs[b + 2] | ((_regs[b + 3] & 0x0F) << 8);
            int wave = Waveform(v, ctrl, _voices[(i + 2) % 3]);
            if (i == 2)
                _lastWave3 = wave;
            UpdateEnvelope(v, b);
            double outV = (wave - 0x800) * v.Env / 255.0;
            if ((route & (1 << i)) != 0)
                filterIn += outV;
            else if (!(i == 2 && (mode & 0x80) != 0))
                direct += outV;
        }

        // Chamberlin state-variable filter
        int cutoff = (_regs[0x15] & 7) | (_regs[0x16] << 3);
        double fc = 30 + cutoff * 5.8;
        double f = 2 * Math.Sin(Math.PI * Math.Min(fc, 12000) / 44100);
        double q = 1.4 - (_regs[0x17] >> 4) * 0.075;
        double hp = filterIn - _lp - q * _bp;
        _bp += f * hp;
        _lp += f * _bp;
        double filtered = ((mode & 0x10) != 0 ? _lp : 0) + ((mode & 0x20) != 0 ? _bp : 0) + ((mode & 0x40) != 0 ? hp : 0);
        double mix = (filtered + direct) * (mode & 0x0F) / 15.0;
        return (short)Math.Clamp(mix * 3.2, -32767, 32767);
    }

    private static int Waveform(Voice v, byte ctrl, Voice ringSource)
    {
        if ((ctrl & 0xF0) == 0)
            return 0x800;
        int result = 0xFFF;
        if ((ctrl & 0x10) != 0)
        {
            bool msb = (v.Acc & 0x800000) != 0;
            if ((ctrl & 0x04) != 0)
                msb ^= (ringSource.Acc & 0x800000) != 0;
            int tri = (int)((v.Acc >> 11) & 0xFFF);
            if (msb) tri ^= 0xFFF;
            result &= tri;
        }
        if ((ctrl & 0x20) != 0)
            result &= (int)(v.Acc >> 12);
        if ((ctrl & 0x40) != 0)
        {
            result &= (v.Acc >> 12) >= v.PulseWidth ? 0xFFF : 0;
        }
        if ((ctrl & 0x80) != 0)
        {
            uint l = v.Lfsr;
            int noise = (int)(((l >> 20) & 1) << 11 | ((l >> 18) & 1) << 10 | ((l >> 14) & 1) << 9 | ((l >> 11) & 1) << 8 |
                              ((l >> 9) & 1) << 7 | ((l >> 5) & 1) << 6 | ((l >> 2) & 1) << 5 | (l & 1) << 4);
            result &= noise;
        }
        return result;
    }

    private void UpdateEnvelope(Voice v, int b)
    {
        byte ad = _regs[b + 5], sr = _regs[b + 6];
        switch (v.State)
        {
            case 0:
                v.Env += 255.0 / (AttackMs[ad >> 4] * 44.1);
                if (v.Env >= 255)
                {
                    v.Env = 255;
                    v.State = 1;
                }
                break;
            case 1:
            {
                double sustain = (sr >> 4) * 17;
                if (v.Env > sustain)
                {
                    v.Env -= 255.0 / (AttackMs[ad & 0x0F] * 3 * 44.1);
                    if (v.Env < sustain) v.Env = sustain;
                }
                break;
            }
            default:
                if (v.Env > 0)
                {
                    v.Env -= 255.0 / (AttackMs[sr & 0x0F] * 3 * 44.1);
                    if (v.Env < 0) v.Env = 0;
                }
                break;
        }
    }

    public void Save(BinaryWriter w)
    {
        w.Write(_regs);
        foreach (var v in _voices)
        {
            w.Write(v.Acc); w.Write(v.Lfsr); w.Write(v.Env); w.Write(v.State); w.Write(v.Gate);
        }
        w.Write(_lp); w.Write(_bp);
    }

    public void Load(BinaryReader r)
    {
        r.Read(_regs, 0, 32);
        foreach (var v in _voices)
        {
            v.Acc = r.ReadInt64(); v.Lfsr = r.ReadUInt32(); v.Env = r.ReadDouble(); v.State = r.ReadInt32(); v.Gate = r.ReadBoolean();
        }
        _lp = r.ReadDouble(); _bp = r.ReadDouble();
    }
}
