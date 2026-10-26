namespace Retro.Cpu.I8088;

public sealed partial class I8088Cpu
{
    private bool _timingEnabled, _timingStep, _replaying, _repeatAgain;
    private readonly byte[] _queue = new byte[model == I8088Model.Intel8086 ? 6 : 4];
    private readonly List<byte> _repeatCode = [];
    private int _queueHead, _queueCount, _prefetchTicks, _replayIndex, _eaClocks, _modRm = -1;
    private ushort _queueCs, _queueIp, _fetchIp, _sequentialIp, _repeatCs, _repeatIp;
    private long _cycles;
    private bool _prefetchLatched;
    private ushort _prefetchValue;

    /// <summary>Optional diagnostic observation; no event is emitted in functional mode.</summary>
    public event Action<I8088BusAccess>? BusAccessed;

    private void TraceAccess(I8088BusAccessKind kind, uint address, byte value)
    {
        if (TimingEnabled && BusAccessed is { } observer) observer(new(_cycles, kind, address, value));
    }
    private byte MemoryRead(uint address, bool code = false)
    {
        byte value = bus.ReadMemory(address);
        TraceAccess(code ? I8088BusAccessKind.Code : I8088BusAccessKind.MemoryRead, address, value);
        return value;
    }
    private void MemoryWrite(uint address, byte value)
    {
        bus.WriteMemory(address, value); TraceAccess(I8088BusAccessKind.MemoryWrite, address, value);
    }

    /// <summary>Seed a hardware diagnostic's initial queue before any clock or instruction executes.</summary>
    public void InitializePrefetchQueue(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > _queue.Length) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (!TimingEnabled || _cycles != 0 || Instructions != 0)
            throw new InvalidOperationException("La cola inicial requiere una CPU temporizada sin ejecutar.");
        FlushQueue(); _repeatCode.Clear(); bytes.CopyTo(_queue);
        _queueCount = bytes.Length; _fetchIp = (ushort)(IP + bytes.Length);
    }

    /// <summary>Enables the model's clocked bus and prefetch queue. Not pin-exact microcode timing.</summary>
    public bool TimingEnabled
    {
        get => _timingEnabled;
        set { if (_timingEnabled != value) { _timingEnabled = value; FlushQueue(); _repeatCode.Clear(); } }
    }

    private void Clock() { _cycles++; bus.Clock(); }
    private void FlushQueue()
    {
        _queueHead = _queueCount = _prefetchTicks = 0; _prefetchLatched = false; _prefetchValue = 0;
        _queueCs = CS; _queueIp = _fetchIp = IP;
    }
    private void IdleClocks(int count)
    {
        if (!TimingEnabled) return;
        for (int n = 0; n < count; n++)
        {
            bool grant = _prefetchTicks == 0 && bus.HoldRequested && !_busLocked;
            bus.SetBusGranted(grant);
            if (_prefetchTicks == 0 && _queueCount <= _queue.Length - (WideBus ? 2 : 1) && !grant) _prefetchTicks = 1;
            Clock();
            if (_prefetchTicks == 0) continue;
            if (_prefetchTicks == 3) LatchPrefetch();
            if (_prefetchTicks++ < 4) continue;
            // Old format-2 snapshots did not latch until T4. Finish those once, without rereading new states.
            if (!_prefetchLatched) LatchPrefetch();
            int width = WideBus && (_fetchIp & 1) == 0 ? 2 : 1;
            for (int b = 0; b < width; b++)
            {
                _queue[(_queueHead + _queueCount++) % _queue.Length] = (byte)(_prefetchValue >> (b * 8));
                _fetchIp++;
            }
            _prefetchTicks = 0; _prefetchLatched = false; _prefetchValue = 0;
        }
    }
    private void LatchPrefetch()
    {
        _prefetchValue = MemoryRead(Physical(_queueCs, _fetchIp), code: true);
        if (WideBus && (_fetchIp & 1) == 0)
            _prefetchValue |= (ushort)(MemoryRead(Physical(_queueCs, (ushort)(_fetchIp + 1)), code: true) << 8);
        _prefetchLatched = true;
    }
    private void DataBusCycle(bool continuation = false)
    {
        if (!TimingEnabled) return;
        // A pending EU transfer wins over the next prefetch, after an active cycle finishes.
        while (_prefetchTicks != 0) IdleClocks(1);
        // An 8086 split word keeps ownership until its high byte has transferred.
        while (bus.HoldRequested && !_busLocked && !continuation) { bus.SetBusGranted(true); Clock(); }
        bus.SetBusGranted(false);
        for (int n = 0; n < 3; n++) Clock();
    }
    private void EndDataBusCycle() { if (TimingEnabled) Clock(); }
    private byte FetchTimed()
    {
        byte value;
        if (_replaying)
        {
            value = _repeatCode[_replayIndex++]; IP++; _sequentialIp = IP; return value;
        }
        if (_queueCs != CS || _queueIp != IP) FlushQueue();
        while (_queueCount == 0) IdleClocks(1);
        value = _queue[_queueHead]; _queueHead = (_queueHead + 1) % _queue.Length; _queueCount--; _queueIp++; IP++;
        _sequentialIp = IP;
        if (_timingStep) _repeatCode.Add(value);
        return value;
    }
    private void StartTimingStep()
    {
        _eaClocks = 0; _modRm = -1; _repeatAgain = false;
        if (!TimingEnabled) return;
        _replaying = _repeatCode.Count > 0 && CS == _repeatCs && IP == _repeatIp;
        if (!_replaying) { _repeatCode.Clear(); _repeatCs = CS; _repeatIp = IP; }
        _replayIndex = 0; _sequentialIp = IP; _timingStep = true;
    }
    private void EndTimingStep(byte op, byte repeat, int prefixes, ushort oldFlags, ushort oldCx)
    {
        if (!TimingEnabled) return;
        bool jump = CS != _startCs || IP != _sequentialIp;
        if (op is >= 0x70 and <= 0x7F) jump = Condition(op & 15);
        if (op is 0xE8 or 0xE9 or 0xEA or 0xEB or 0x9A or 0xC2 or 0xC3 or 0xCA or 0xCB or 0xCF) jump = true;
        if (op == 0xFF && ((_modRm >> 3) & 7) is >= 2 and <= 5) jump = true;
        if (jump && !_repeatAgain) FlushQueue();
        int clocks = ExecutionClocks(op, repeat, oldFlags, oldCx, jump) + _eaClocks + prefixes * 2;
        IdleClocks(clocks);
        _timingStep = false;
        if (!_repeatAgain) _repeatCode.Clear();
    }

    // EU work only: transfers add four clocks per byte (8088) or aligned word (8086).
    // Fetches overlap this work. Variable multiply/divide use a conservative upper bound.
    private int ExecutionClocks(byte op, byte repeat, ushort flags, ushort cx, bool jump)
    {
        bool memory = _modRm >= 0 && _modRm < 0xC0;
        int group = (_modRm >> 3) & 7;
        if (op <= 0x3D && (op & 7) <= 5)
            return (op & 7) >= 4 ? 4 : !memory ? 3 : (op & 2) != 0 || op >> 3 == 7 ? 5 : 8;
        if (op is >= 0x40 and <= 0x4F) return 3;
        if (op is >= 0x50 and <= 0x57) return 7;
        if (op is >= 0x58 and <= 0x5F) return 4;
        if (op is >= 0x70 and <= 0x7F) return jump ? 16 : 4;
        if (op is >= 0xB0 and <= 0xBF) return 4;
        if (op is >= 0x91 and <= 0x97) return 3;
        return op switch
        {
            0x06 or 0x0E or 0x16 or 0x1E => 6,
            0x07 or 0x17 or 0x1F => 4,
            0x27 or 0x2F or 0x37 or 0x3F => 4,
            0x80 or 0x81 or 0x82 or 0x83 => !memory ? 4 : group == 7 ? 6 : 9,
            0x84 or 0x85 => memory ? 5 : 3,
            0x86 or 0x87 => memory ? 9 : 4,
            0x88 or 0x89 => memory ? 5 : 2,
            0x8A or 0x8B or 0x8C or 0x8E => memory ? 4 : 2,
            0x8D => 2, 0x8F => memory ? 9 : 4,
            0x90 => 3, 0x98 => 2, 0x99 => 5,
            0x9A => 20, 0x9B => _waitingForTest ? 5 : 3,
            0x9C => 6, 0x9D => 4, 0x9E or 0x9F => 4,
            >= 0xA0 and <= 0xA3 => 6,
            0xA8 or 0xA9 => 4,
            0xA4 or 0xA5 => repeat == 0 ? 10 : 9,
            0xA6 or 0xA7 => repeat == 0 ? 14 : 14,
            0xAA or 0xAB => repeat == 0 ? 7 : 6,
            0xAC or 0xAD => repeat == 0 ? 8 : 9,
            0xAE or 0xAF => 11,
            0xC2 => 12, 0xC3 => 4, 0xC4 or 0xC5 => 8,
            0xC6 or 0xC7 => memory ? 6 : 4,
            0xCA => 9, 0xCB => 10,
            0xCC or 0xCD => 0, 0xCE => (flags & Overflow) != 0 ? 0 : 4, 0xCF => 12,
            0xD0 or 0xD1 => memory ? 7 : 2,
            0xD2 or 0xD3 => (memory ? 12 : 8) + 4 * (cx & 255),
            0xD4 => 83, 0xD5 => 60, 0xD7 => 7,
            >= 0xD8 and <= 0xDF => 2,
            0xE0 or 0xE1 => jump ? 18 : 6,
            0xE2 => jump ? 17 : 5, 0xE3 => jump ? 18 : 6,
            >= 0xE4 and <= 0xE7 => 6,
            0xE8 => 15, 0xE9 or 0xEA or 0xEB => 15,
            >= 0xEC and <= 0xEF => 4,
            0xF4 or 0xF5 or >= 0xF8 and <= 0xFD => 2,
            0xF6 or 0xF7 => group switch
            { 0 => memory ? 7 : 5, 2 or 3 => memory ? 8 : 3, 4 => (op == 0xF7 ? 133 : 77),
              5 => (op == 0xF7 ? 154 : 98), 6 => (op == 0xF7 ? 162 : 90), _ => (op == 0xF7 ? 184 : 112) },
            0xFE or 0xFF => group switch { 0 or 1 => memory ? 7 : 3, 2 => memory ? 13 : 12,
                3 => 21, 4 => memory ? 14 : 11, 5 => 16, _ => memory ? 8 : 7 },
            _ => throw new NotSupportedException($"8088: falta el tiempo EU de {op:X2}.")
        };
    }
    private sealed record TimingState(bool Enabled, long Cycles, byte[] Queue, int Head, int Count, int Pending,
        ushort Cs, ushort Ip, ushort FetchIp, ushort RepeatCs, ushort RepeatIp, byte[] RepeatCode, bool Latched, ushort Value);
    private void SaveTiming(BinaryWriter w)
    {
        w.Write(TimingEnabled); w.Write(_cycles); w.Write(_queue); w.Write(_queueHead); w.Write(_queueCount); w.Write(_prefetchTicks);
        w.Write(_queueCs); w.Write(_queueIp); w.Write(_fetchIp); w.Write(_repeatCs); w.Write(_repeatIp);
        w.Write(_repeatCode.Count); w.Write(_repeatCode.ToArray()); w.Write(_prefetchLatched); w.Write(_prefetchValue);
    }
    private TimingState ReadTiming(BinaryReader r, int version)
    {
        bool enabled = r.ReadBoolean(); long cycles = r.ReadInt64(); byte[] queue = r.ReadBytes(_queue.Length);
        int head = r.ReadInt32(), count = r.ReadInt32(), pending = r.ReadInt32();
        ushort cs = r.ReadUInt16(), ip = r.ReadUInt16(), fetchIp = r.ReadUInt16(), repeatCs = r.ReadUInt16(), repeatIp = r.ReadUInt16();
        int length = r.ReadInt32();
        if (queue.Length != _queue.Length || cycles < 0 || head < 0 || head >= _queue.Length || count < 0 || count > _queue.Length ||
            pending is < 0 or > 4 || pending != 0 && count > _queue.Length - (WideBus ? 2 : 1) ||
            (ushort)(ip + count) != fetchIp || length is < 0 or > 65536)
            throw new InvalidDataException("El reloj o la cola 8088 del estado no son válidos.");
        byte[] code = r.ReadBytes(length); if (code.Length != length) throw new EndOfStreamException();
        bool latched = version >= 3 && r.ReadBoolean();
        ushort value = version >= 3 ? r.ReadUInt16() : (ushort)0;
        if (latched && pending != 4 || !latched && value != 0)
            throw new InvalidDataException("La lectura pendiente del estado no es válida.");
        return new(enabled, cycles, queue, head, count, pending, cs, ip, fetchIp, repeatCs, repeatIp, code, latched, value);
    }
    private void RestoreTiming(TimingState t)
    {
        _timingEnabled = t.Enabled; _cycles = t.Cycles; t.Queue.CopyTo(_queue, 0);
        _queueHead = t.Head; _queueCount = t.Count; _prefetchTicks = t.Pending; _prefetchLatched = t.Latched; _prefetchValue = t.Value;
        _queueCs = t.Cs; _queueIp = t.Ip; _fetchIp = t.FetchIp; _repeatCs = t.RepeatCs; _repeatIp = t.RepeatIp;
        _repeatCode.Clear(); _repeatCode.AddRange(t.RepeatCode); _timingStep = _replaying = _repeatAgain = false;
    }

}