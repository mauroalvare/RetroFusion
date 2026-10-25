namespace Retro.Systems.Pc;

// Device states are loaded only into a fresh runtime. No event callbacks run while restoring;
// the PIC input latches and timer/DMA phases already describe the saved physical levels.
internal static class PcDeviceState
{
    internal static void Check(bool valid, string device)
    { if (!valid) throw new InvalidDataException($"El estado de {device} no es válido."); }
    internal static int Count(BinaryReader reader, int max)
    { int n = reader.ReadInt32(); Check(n >= 0 && n <= max, "cantidad de datos"); return n; }
}

public sealed partial class PcMemory
{
    internal void Save(BinaryWriter w)
    {
        foreach (byte value in _ram) w.Write(value);
        foreach (byte value in _video) w.Write(value);
        
    }
    internal void Load(BinaryReader r)
    {
        for (int i = 0; i < _ram.Length; i++) _ram[i] = r.ReadByte();
        for (int i = 0; i < _video.Length; i++) _video[i] = r.ReadByte();
        
    }
}

public sealed partial class Pic8259A
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_inputs);
        w.Write(_requests);
        w.Write(_service);
        w.Write(_mask);
        w.Write(VectorBase);
        w.Write(_initStep);
        w.Write(_lowest);
        w.Write(_polledLine);
        w.Write(_autoEoi);
        w.Write(_rotateAuto);
        w.Write(_specialMask);
        w.Write(_readService);
        w.Write(_poll);
        w.Write(Initialized);
        w.Write(BufferedMode);
        w.Write(BufferedMaster);
        
    }
    internal void Load(BinaryReader r)
    {
        _inputs = r.ReadByte();
        _requests = r.ReadByte();
        _service = r.ReadByte();
        _mask = r.ReadByte();
        VectorBase = r.ReadByte();
        _initStep = r.ReadInt32();
        _lowest = r.ReadInt32();
        _polledLine = r.ReadInt32();
        _autoEoi = r.ReadBoolean();
        _rotateAuto = r.ReadBoolean();
        _specialMask = r.ReadBoolean();
        _readService = r.ReadBoolean();
        _poll = r.ReadBoolean();
        Initialized = r.ReadBoolean();
        BufferedMode = r.ReadBoolean();
        BufferedMaster = r.ReadBoolean();
        PcDeviceState.Check(_initStep is 0 or 2 or 4 && _lowest is >= 0 and < 8 && _polledLine is >= -1 and < 8 && (VectorBase & 7) == 0, "8259");
    }
}

public sealed partial class Ppi8255
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_a);
        w.Write(_b);
        w.Write(_c);
        w.Write(_control);
        
    }
    internal void Load(BinaryReader r)
    {
        _a = r.ReadByte();
        _b = r.ReadByte();
        _c = r.ReadByte();
        _control = r.ReadByte();
        PcDeviceState.Check((_control & 0xE4) == 0x80, "8255");
    }
}

public sealed partial class PcKeyboard
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_inhibitClocks);
        w.Write(_transferClocks);
        w.Write(_batClocks);
        w.Write(_clockEnabled);
        w.Write(_clear);
        w.Write(_latched);
        w.Write(_data); w.Write(_repeatKey); w.Write(_repeatTimer);
        w.Write(_queue.Count); foreach (byte value in _queue) w.Write(value);
    }
    internal void Load(BinaryReader r)
    {
        _inhibitClocks = r.ReadInt64();
        _transferClocks = r.ReadInt32();
        _batClocks = r.ReadInt32();
        _clockEnabled = r.ReadBoolean();
        _clear = r.ReadBoolean();
        _latched = r.ReadBoolean();
        _data = r.ReadByte(); _repeatKey = r.ReadInt32(); _repeatTimer = r.ReadInt32();
        _queue.Clear(); int count = PcDeviceState.Count(r, 33);
        for (int i = 0; i < count; i++) _queue.Enqueue(r.ReadByte());
        PcDeviceState.Check(_repeatKey is >= -1 and <= 127 && _repeatTimer is >= 0 and <= 2386364 && _inhibitClocks >= 0 && _transferClocks is >= 0 and < 4773 && _batClocks is >= 0 and <= 2386364, "teclado");
    }
}

public sealed partial class Dma8237
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_command);
        w.Write(_mask);
        w.Write(_requests);
        w.Write(_softwareRequests);
        w.Write(_terminal);
        w.Write(_highByte);
        w.Write(_active);
        w.Write(_phase);
        w.Write(_releaseClocks);
        w.Write(_priority);
        foreach (ushort value in _address) w.Write(value);
        foreach (ushort value in _count) w.Write(value);
        foreach (ushort value in _baseAddress) w.Write(value);
        foreach (ushort value in _baseCount) w.Write(value);
        foreach (byte value in _mode) w.Write(value);
        
    }
    internal void Load(BinaryReader r)
    {
        _command = r.ReadByte();
        _mask = r.ReadByte();
        _requests = r.ReadByte();
        _softwareRequests = r.ReadByte();
        _terminal = r.ReadByte();
        _highByte = r.ReadBoolean();
        _active = r.ReadInt32();
        _phase = r.ReadInt32();
        _releaseClocks = r.ReadInt32();
        _priority = r.ReadInt32();
        for (int i = 0; i < _address.Length; i++) _address[i] = r.ReadUInt16();
        for (int i = 0; i < _count.Length; i++) _count[i] = r.ReadUInt16();
        for (int i = 0; i < _baseAddress.Length; i++) _baseAddress[i] = r.ReadUInt16();
        for (int i = 0; i < _baseCount.Length; i++) _baseCount[i] = r.ReadUInt16();
        for (int i = 0; i < _mode.Length; i++) _mode[i] = r.ReadByte();
        PcDeviceState.Check(_active is >= -1 and < 4 && _phase is >= 0 and < 4 && _releaseClocks is >= 0 and <= 4 && _priority is >= 0 and < 4 && _mask <= 15 && _requests <= 15 && _softwareRequests <= 15 && _terminal <= 15 && (_command & 0xE9) == 0, "8237");
        PcDeviceState.Check(_mode.All(m => (m & 0xC0) != 0xC0 && (m & 12) != 12), "modo DMA");
    }
}

public sealed partial class Cga6845
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_index);
        w.Write(_oscillator);
        w.Write(_character);
        w.Write(_raster);
        w.Write(_row);
        w.Write(_adjust);
        w.Write(_vsyncRemaining);
        w.Write(_inAdjust);
        w.Write(_lightPen);
        w.Write(FrameReady);
        w.Write(Mode);
        w.Write(Color);
        w.Write(FrameCount);
        foreach (byte value in _registers) w.Write(value);
        foreach (int value in Frame.Pixels) w.Write(value);
        
    }
    internal void Load(BinaryReader r)
    {
        _index = r.ReadInt32();
        _oscillator = r.ReadInt32();
        _character = r.ReadInt32();
        _raster = r.ReadInt32();
        _row = r.ReadInt32();
        _adjust = r.ReadInt32();
        _vsyncRemaining = r.ReadInt32();
        _inAdjust = r.ReadBoolean();
        _lightPen = r.ReadBoolean();
        FrameReady = r.ReadBoolean();
        Mode = r.ReadByte();
        Color = r.ReadByte();
        FrameCount = r.ReadInt64();
        for (int i = 0; i < _registers.Length; i++) _registers[i] = r.ReadByte();
        for (int i = 0; i < Frame.Pixels.Length; i++) Frame.Pixels[i] = r.ReadInt32();
        PcDeviceState.Check(_index is >= 0 and < 32 && _oscillator is >= 0 and < 16 && _character is >= 0 and < 256 && _raster is >= 0 and < 32 && _row is >= 0 and <= 128 && _adjust is >= 0 and < 32 && _vsyncRemaining is >= 0 and <= 16 && FrameCount >= 0, "6845");
    }
}

public sealed partial class Fdc765
{
    internal void Save(BinaryWriter w)
    {
        w.Write(_expected);
        w.Write(_timer);
        w.Write(_position);
        w.Write(_drive);
        w.Write(_head);
        w.Write(_sector);
        w.Write(_track);
        w.Write(_size);
        w.Write(_eot);
        w.Write(_stepRate);
        w.Write(_dor);
        w.Write(_operation);
        w.Write(_specify);
        w.Write(_seekBusy);
        w.Write(_executing);
        w.Write(_byteReady);
        w.Write(_write);
        w.Write(_multi);
        w.Write(_nonDma);
        w.Write(_irq);
        w.Write(_drq);
        w.Write(_irqPending);
        w.Write(_drqPending);
        w.Write(_endOfTrack);
        w.Write(_needSector);
        w.Write(BytesTransferred);
        w.Write(SectorsTransferred);
        foreach (byte value in _buffer) w.Write(value);
        foreach (int value in _cylinder) w.Write(value);
        foreach (int value in _seekTarget) w.Write(value);
        foreach (int value in _seekTimer) w.Write(value);
        w.Write(_command.Count); foreach (byte value in _command) w.Write(value);
        w.Write(_result.Count); foreach (byte value in _result) w.Write(value);
        w.Write(_sense.Count); foreach (var entry in _sense) { w.Write(entry.Status); w.Write(entry.Cylinder); }
        w.Write(Disk != null); Disk?.SaveOverlay(w);
    }
    internal void Load(BinaryReader r)
    {
        _expected = r.ReadInt32();
        _timer = r.ReadInt32();
        _position = r.ReadInt32();
        _drive = r.ReadInt32();
        _head = r.ReadInt32();
        _sector = r.ReadInt32();
        _track = r.ReadInt32();
        _size = r.ReadInt32();
        _eot = r.ReadInt32();
        _stepRate = r.ReadInt32();
        _dor = r.ReadByte();
        _operation = r.ReadByte();
        _specify = r.ReadByte();
        _seekBusy = r.ReadByte();
        _executing = r.ReadBoolean();
        _byteReady = r.ReadBoolean();
        _write = r.ReadBoolean();
        _multi = r.ReadBoolean();
        _nonDma = r.ReadBoolean();
        _irq = r.ReadBoolean();
        _drq = r.ReadBoolean();
        _irqPending = r.ReadBoolean();
        _drqPending = r.ReadBoolean();
        _endOfTrack = r.ReadBoolean();
        _needSector = r.ReadBoolean();
        BytesTransferred = r.ReadInt64();
        SectorsTransferred = r.ReadInt64();
        for (int i = 0; i < _buffer.Length; i++) _buffer[i] = r.ReadByte();
        for (int i = 0; i < _cylinder.Length; i++) _cylinder[i] = r.ReadInt32();
        for (int i = 0; i < _seekTarget.Length; i++) _seekTarget[i] = r.ReadInt32();
        for (int i = 0; i < _seekTimer.Length; i++) _seekTimer[i] = r.ReadInt32();
        _command.Clear(); int command = PcDeviceState.Count(r, 8); for (int i = 0; i < command; i++) _command.Add(r.ReadByte());
        _result.Clear(); int result = PcDeviceState.Count(r, 7); for (int i = 0; i < result; i++) _result.Enqueue(r.ReadByte());
        _sense.Clear(); int sense = PcDeviceState.Count(r, 4); for (int i = 0; i < sense; i++) _sense.Enqueue((r.ReadByte(), r.ReadByte()));
        PcDeviceState.Check(r.ReadBoolean() == (Disk != null), "medio FDC"); Disk?.LoadOverlay(r);
        PcDeviceState.Check(_expected is >= 0 and <= 9 && _timer is >= 0 and <= 4773 && _position is >= 0 and < 512 && _drive is >= 0 and < 4 && _head is >= 0 and <= 255 && _track is >= 0 and <= 255 && _sector is >= 0 and <= 255 && _size is >= 0 and <= 255 && _eot is >= 0 and <= 255 && _stepRate is >= 1 and <= 16 && BytesTransferred >= 0 && SectorsTransferred >= 0 && _seekBusy <= 15, "765");
        PcDeviceState.Check(_cylinder.All(v => v is >= 0 and <= 255) && _seekTarget.All(v => v is >= 0 and <= 255) && _seekTimer.All(v => v is >= 0 and <= 19473840), "seek FDC");
    }
}

public sealed partial class PcBus
{
    internal void Save(BinaryWriter w)
    {
        w.Write(CpuClocks);
        w.Write(NmiEnabled);
        w.Write(BusLocked);
        w.Write(TestSignal);
        w.Write(_busGranted);
        foreach (byte value in _dmaPages) w.Write(value);
        Memory.Save(w); Pic.Save(w); Pit.Save(w); Ppi.Save(w); Keyboard.Save(w); Dma.Save(w); Cga.Save(w); Fdc.Save(w);
    }
    internal void Load(BinaryReader r)
    {
        CpuClocks = r.ReadInt64();
        NmiEnabled = r.ReadBoolean();
        BusLocked = r.ReadBoolean();
        TestSignal = r.ReadBoolean();
        _busGranted = r.ReadBoolean();
        for (int i = 0; i < _dmaPages.Length; i++) _dmaPages[i] = r.ReadByte();
        PcDeviceState.Check(CpuClocks >= 0 && !BusLocked, "placa PC");
        Memory.Load(r); Pic.Load(r); Pit.Load(r); Ppi.Load(r); Keyboard.Load(r); Dma.Load(r); Cga.Load(r); Fdc.Load(r);
    }
}

public sealed partial class Pit8253
{
    internal void Save(BinaryWriter w)
    {
        foreach (var c in _channels) { w.Write(c.Access); w.Write(c.Mode); w.Write(c.Reload); w.Write(c.Count); w.Write(c.Latch); w.Write(c.Low); w.Write(c.Configured); w.Write(c.HasReload); w.Write(c.Loaded); w.Write(c.LoadPending); w.Write(c.Counting); w.Write(c.Gate); w.Write(c.Output); w.Write(c.WriteHigh); w.Write(c.ReadHigh); w.Write(c.Latched); }
    }
    internal void Load(BinaryReader r)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = new Channel(); c.Access = r.ReadInt32(); c.Mode = r.ReadInt32(); c.Reload = r.ReadUInt16(); c.Count = r.ReadUInt16(); c.Latch = r.ReadUInt16(); c.Low = r.ReadByte(); c.Configured = r.ReadBoolean(); c.HasReload = r.ReadBoolean(); c.Loaded = r.ReadBoolean(); c.LoadPending = r.ReadBoolean(); c.Counting = r.ReadBoolean(); c.Gate = r.ReadBoolean(); c.Output = r.ReadBoolean(); c.WriteHigh = r.ReadBoolean(); c.ReadHigh = r.ReadBoolean(); c.Latched = r.ReadBoolean();
            PcDeviceState.Check(c.Access is >= 0 and <= 3 && c.Mode is 0 or 2 or 3 && (!c.Configured || c.Access != 0), "8253");
            _channels[i] = c;
        }
    }
}
