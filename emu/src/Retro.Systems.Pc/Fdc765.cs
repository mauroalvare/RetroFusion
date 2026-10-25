namespace Retro.Systems.Pc;

/// <summary>uPD765 command/execution/result phases, sector-level MFM and DMA/non-DMA byte transfer.</summary>
public sealed partial class Fdc765
{
    private readonly List<byte> _command = [];
    private readonly Queue<byte> _result = new();
    private readonly Queue<(byte Status, byte Cylinder)> _sense = new();
    private readonly int[] _cylinder = new int[4], _seekTarget = new int[4], _seekTimer = new int[4];
    private readonly byte[] _buffer = new byte[512];
    private int _expected, _timer, _position, _drive, _head, _sector, _track, _size, _eot, _stepRate = 6;
    private byte _dor, _operation, _specify, _seekBusy;
    private bool _executing, _byteReady, _write, _multi, _nonDma, _irq, _drq, _irqPending, _drqPending, _endOfTrack, _needSector;
    public event Action<bool>? IrqChanged;
    public event Action<bool>? DmaRequestChanged;
    public FloppyDiskImage? Disk { get; private set; }
    public bool DmaRequest => _drq;
    public long BytesTransferred { get; private set; }
    public long SectorsTransferred { get; private set; }
    public byte MainStatus
    {
        get
        {
            int status = _result.Count > 0 ? 0xD0 : _executing ? 0x10 | (_nonDma ? 0x20 : 0) | (_nonDma && _byteReady ? 0x80 | (!_write ? 0x40 : 0) : 0) : 0x80 | (_command.Count > 0 ? 0x10 : 0);
            status |= _seekBusy;
            return (_dor & 4) == 0 ? (byte)0 : (byte)status;
        }
    }
    public void Mount(FloppyDiskImage? disk)
    {
        if (_executing) throw new InvalidOperationException("Esperá a que termine el acceso al disquete.");
        if (disk != null && disk.SectorsPerTrack > 9) throw new NotSupportedException("La controladora XT admite por ahora disquetes de 160 a 720 KB, a 250 kbit/s.");
        Disk = disk;
    }
    public void Reset()
    {
        _command.Clear(); _result.Clear(); _sense.Clear(); Array.Clear(_cylinder); Array.Clear(_seekTimer);
        _dor = _operation = _specify = _seekBusy = 0; _endOfTrack = _needSector = false; _executing = _byteReady = _write = _multi = _nonDma = false;
        _expected = _timer = _position = _drive = _head = _sector = _track = _size = _eot = 0;
        _stepRate = 6; BytesTransferred = SectorsTransferred = 0; SetIrq(false); SetDrq(false);
    }
    public void WriteControl(byte value)
    {
        bool wasReset = (_dor & 4) == 0;
        _dor = value;
        if ((value & 4) == 0)
        {
            _command.Clear(); _result.Clear(); _sense.Clear(); _executing = _byteReady = false;
            Array.Clear(_seekTimer); _seekBusy = 0; _timer = 0; SetIrq(false); SetDrq(false);
        }
        else if (wasReset)
        {
            _sense.Enqueue((0xC0, (byte)_cylinder[0])); // XT ties READY high: one reset report, no AT drive polling.
            _timer = 16; // Reset release completes asynchronously.
        }
        SetIrq(_irqPending); SetDrq(_drqPending);
        if (_executing && !Ready(_drive)) Finish(0x48, 0, 0);
    }
    private void SetIrq(bool value) { _irqPending = value; value &= (_dor & 8) != 0; if (_irq == value) return; _irq = value; IrqChanged?.Invoke(value); }
    private void SetDrq(bool value) { _drqPending = value; value &= (_dor & 8) != 0; if (_drq == value) return; _drq = value; DmaRequestChanged?.Invoke(value); }
    private bool Ready(int drive) => drive == 0 && (_dor & 3) == drive && Disk != null && (_dor & 0x10) != 0;
    public void WriteData(byte value)
    {
        if ((_dor & 4) == 0 || _result.Count > 0) return;
        if (_executing) { if (_nonDma && _write && _byteReady) TransferWrite(value); return; }
        if (_command.Count == 0)
        {
            _operation = (byte)(value & 31); _expected = _operation switch { 3 => 3, 4 or 7 or 10 => 2, 8 => 1, 15 => 3, 5 or 6 => 9, 13 => 6, _ => 1 };
        }
        _command.Add(value); if (_command.Count < _expected) return;
        ExecuteCommand(); _command.Clear();
    }
    public byte ReadData()
    {
        if (_result.Count > 0)
        {
            SetIrq(false); return _result.Dequeue();
        }
        if (_executing && !_write && _byteReady && _nonDma) return TransferRead();
        return 255;
    }
    private void ExecuteCommand()
    {
        switch (_operation)
        {
            case 3:
                _stepRate = Math.Max(1, 16 - (_command[1] >> 4)); _specify = _command[2]; _nonDma = (_specify & 1) != 0; break;
            case 4:
            {
                int d = _command[1] & 3; bool ready = Ready(d);
                _result.Enqueue((byte)((_command[1] & 7) | (d == 0 && Disk?.Heads == 2 ? 8 : 0) | (_cylinder[d] == 0 ? 16 : 0) | (ready ? 32 : 0) | (Disk?.WriteProtected == true ? 64 : 0))); break;
            }
            case 7: case 15:
            {
                int d = _command[1] & 3, target = _operation == 7 ? 0 : _command[2];
                _seekBusy |= (byte)(1 << d); _seekTarget[d] = target; _seekTimer[d] = Math.Max(1, Math.Abs(target - _cylinder[d])) * _stepRate * 4773; SetIrq(false); break;
            }
            case 8:
                SetIrq(false);
                if (_sense.TryDequeue(out var pending)) { _seekBusy &= (byte)~(1 << (pending.Status & 3)); _result.Enqueue(pending.Status); _result.Enqueue(pending.Cylinder); }
                else _result.Enqueue(0x80);
                break;
            case 5: case 6:
                _drive = _command[1] & 3; _head = _command[3]; _track = _command[2]; _sector = _command[4];
                _size = _command[5]; _eot = _command[6]; _write = _operation == 5; _multi = (_command[0] & 0x80) != 0;
                SetIrq(false);
                if (!Ready(_drive)) { Finish(0x48, 0, 0); break; }
                if ((_command[0] & 0x40) == 0 || _size != 2 || _head != ((_command[1] >> 2) & 1) || _track != _cylinder[_drive] || _sector < 1 || _eot < 1)
                { Finish(0x40, 4, _track != _cylinder[_drive] ? (byte)0x10 : (byte)0); break; }
                if (_write && Disk!.WriteProtected) { Finish(0x40, 2, 0); break; }
                if (!Disk!.ReadSector(_track, _head, _sector, _buffer)) { Finish(0x40, 4, 0); break; }
                _position = 0; _endOfTrack = _needSector = false; _executing = true; _byteReady = false; _timer = 4773; break;
            case 10:
                _drive = _command[1] & 3; _head = (_command[1] >> 2) & 1; _track = _cylinder[_drive]; _sector = 1; _size = 2;
                if (Ready(_drive)) Finish(0, 0, 0); else Finish(0x48, 0, 0); break;
            case 13: throw new NotSupportedException("FDC765: el formateo de pistas todavía no está implementado.");
            default: _result.Enqueue(0x80); break;
        }
    }
    private void QueueSense(byte status, byte cylinder)
    {
        var others = _sense.Where(s => (s.Status & 3) != (status & 3)).ToArray();
        _sense.Clear(); foreach (var entry in others) _sense.Enqueue(entry);
        _sense.Enqueue((status, cylinder));
    }
    public void Clock()
    {
        for (int i = 0; i < 4; i++)
        {
            if (_seekTimer[i] <= 0 || --_seekTimer[i] != 0) continue;
            _cylinder[i] = _seekTarget[i]; QueueSense((byte)((Ready(i) ? 0x20 : 0x68) | i), (byte)_cylinder[i]); SetIrq(true);
        }
        if (_timer <= 0 || --_timer != 0) return;
        if (!_executing) { if (_sense.Count > 0) SetIrq(true); return; }
        if (_endOfTrack) { Finish(0x40, 0x80, 0); return; }
        if (_byteReady) { Finish(0x40, 0x10, 0); return; } // Overrun: the previous byte was not serviced.
        if (_needSector)
        {
            _needSector = false;
            if (!Disk!.ReadSector(_track, _head, _sector, _buffer)) { Finish(0x40, 4, 0); return; }
        }
        _byteReady = true; _timer = 153; // One byte at 250 kbit/s (32 us).
        if (_nonDma) SetIrq(true); else SetDrq(true);
    }
    public byte DmaRead() => _executing && !_write && _byteReady ? TransferRead() : (byte)255;
    public void DmaWrite(byte value) { if (_executing && _write && _byteReady) TransferWrite(value); }
    private byte TransferRead() { byte value = _buffer[_position]; ByteTransferred(); return value; }
    private void TransferWrite(byte value) { _buffer[_position] = value; ByteTransferred(); }
    private void ByteTransferred()
    {
        SetDrq(false); if (_nonDma) SetIrq(false); _byteReady = false; BytesTransferred++; _position++;
        if (_position < 512) return;
        SectorsTransferred++;
        if (_write && !Disk!.WriteSector(_track, _head, _sector, _buffer)) { Finish(0x40, 2, 0); return; }
        _position = 0;
        if (_sector != _eot) _sector++;
        else if (_multi && _head == 0 && Disk!.Heads == 2) { _head = 1; _sector = 1; }
        else { _endOfTrack = true; return; }
        _needSector = true;
    }
    public void TerminalCount()
    {
        if (_executing) Finish(0, 0, 0);
    }
    private void Finish(byte status0, byte status1, byte status2)
    {
        _executing = _byteReady = false; _timer = 0; SetDrq(false); _result.Clear();
        foreach (byte value in new[] { (byte)(status0 | _drive | (_head << 2)), status1, status2, (byte)_track, (byte)_head, (byte)_sector, (byte)_size }) _result.Enqueue(value);
        SetIrq(true);
    }
}