using Retro.Core;

namespace Retro.Cpu.I8088;

public sealed partial class I8088Cpu : ICpuDebugView
{
    public string CpuName => WideBus ? "Intel 8086" : "Intel 8088";
    private string StateSignature => WideBus ? "RETRO-8086" : "RETRO-8088";
    public bool HasCycleTiming => TimingEnabled;
    public long Cycles => TimingEnabled ? _cycles : 0;
    public uint ProgramCounter
    {
        get => PhysicalPC;
        set
        {
            if (value > 0xFFFFF) throw new ArgumentOutOfRangeException(nameof(value));
            uint offset = (value - ((uint)CS << 4)) & 0xFFFFF;
            if (offset <= ushort.MaxValue) IP = (ushort)offset;
            else { CS = (ushort)(value >> 4); IP = (ushort)(value & 15); }
        }
    }
    public uint StackPointer => Physical(SS, SP);

    public IReadOnlyList<RegisterValue> GetRegisters() =>
    [
        new("AX", 16, AX), new("BX", 16, BX), new("CX", 16, CX), new("DX", 16, DX),
        new("SP", 16, SP), new("BP", 16, BP), new("SI", 16, SI), new("DI", 16, DI),
        new("CS", 16, CS), new("DS", 16, DS), new("SS", 16, SS), new("ES", 16, ES),
        new("IP", 16, IP), new("FLAGS", 16, Flags), new("PC", 20, PhysicalPC)
    ];
    public bool TrySetRegister(string name, uint value)
    {
        if (name.Equals("PC", StringComparison.OrdinalIgnoreCase))
        { if (value > 0xFFFFF) return false; ProgramCounter = value; return true; }
        if (value > ushort.MaxValue) return false;
        ushort word = (ushort)value;
        switch (name.ToUpperInvariant())
        {
            case "AX": AX = word; break; case "BX": BX = word; break;
            case "CX": CX = word; break; case "DX": DX = word; break;
            case "SP": SP = word; break; case "BP": BP = word; break;
            case "SI": SI = word; break; case "DI": DI = word; break;
            case "CS": CS = word; break; case "DS": DS = word; break;
            case "SS": SS = word; break; case "ES": ES = word; break;
            case "IP": IP = word; break; case "FLAGS": Flags = word; break;
            default: return false;
        }
        return true;
    }
    public IReadOnlyList<FlagValue> GetFlags() =>
    [
        new("CF", Flag(Carry), "Acarreo"), new("PF", Flag(Parity), "Paridad par"),
        new("AF", Flag(Auxiliary), "Acarreo auxiliar"), new("ZF", Flag(Zero), "Cero"),
        new("SF", Flag(Sign), "Signo"), new("TF", Flag(Trap), "Paso a paso"),
        new("IF", Flag(InterruptEnable), "Interrupciones"), new("DF", Flag(Direction), "Dirección"),
        new("OF", Flag(Overflow), "Desbordamiento")
    ];
    public bool TrySetFlag(string name, bool value)
    {
        ushort bit = name.ToUpperInvariant() switch
        { "CF" => Carry, "PF" => Parity, "AF" => Auxiliary, "ZF" => Zero, "SF" => Sign, "TF" => Trap,
          "IF" => InterruptEnable, "DF" => Direction, "OF" => Overflow, _ => 0 };
        if (bit == 0) return false;
        SetFlag(bit, value); return true;
    }
    public IReadOnlyList<string> GetInterruptState() =>
    [
        $"CS:IP={CS:X4}:{IP:X4} · físico={PhysicalPC:X5}",
        $"IF={(Flag(InterruptEnable) ? 1 : 0)} · TF={(Flag(Trap) ? 1 : 0)} · NMI={(_nmi ? "pendiente" : "-")}",
        $"IRQ={(_irq is byte vector ? vector.ToString("X2") : "-")} · sombra={(_interruptShadow || _segmentShadow ? "activa" : "-")}",
        Halted ? "CPU detenida en HLT" : _waitingForTest ? "WAIT: espera TEST" : "CPU ejecutando",
        $"Instrucciones/iteraciones: {Instructions:N0}. " + (TimingEnabled ? $"Reloj: {_cycles:N0} T; modelo EU aproximado." : "Modo funcional sin reloj.")
    ];

    /// <summary>CPU-only snapshot at an instruction boundary. Memory and devices belong to the machine.</summary>
    public void Save(BinaryWriter writer)
    {
        writer.Write(StateSignature); writer.Write(2);
        foreach (ushort value in _r) writer.Write(value);
        foreach (ushort value in _s) writer.Write(value);
        writer.Write(IP); writer.Write(Flags); writer.Write(Halted); writer.Write(Instructions);
        writer.Write(_interruptShadow); writer.Write(_segmentShadow); writer.Write(_nmi); writer.Write(_trapPending);
        writer.Write(_irq.HasValue); if (_irq is byte vector) writer.Write(vector);
        writer.Write(_waitingForTest);
        SaveTiming(writer);
    }
    public void Load(BinaryReader reader)
    {
        if (reader.ReadString() != StateSignature) throw new InvalidDataException($"El estado no corresponde a la CPU {CpuName}.");
        int version = reader.ReadInt32();
        if (version != 2 && (WideBus || version != 1)) throw new InvalidDataException($"No es un estado de CPU {CpuName} compatible.");
        // Parse and validate before mutating any live state.
        ushort[] registers = new ushort[8], segments = new ushort[4];
        for (int i = 0; i < registers.Length; i++) registers[i] = reader.ReadUInt16();
        for (int i = 0; i < segments.Length; i++) segments[i] = reader.ReadUInt16();
        ushort ip = reader.ReadUInt16(), flags = reader.ReadUInt16();
        bool halted = reader.ReadBoolean(); long instructions = reader.ReadInt64();
        bool shadow = reader.ReadBoolean(), segmentShadow = reader.ReadBoolean(), nmi = reader.ReadBoolean(), trap = reader.ReadBoolean();
        byte? irq = reader.ReadBoolean() ? reader.ReadByte() : null;
        bool waiting = reader.ReadBoolean();
        var timing = version == 2 ? ReadTiming(reader) : null;
        if (instructions < 0 || (flags & 0xF02A) != 0xF002) throw new InvalidDataException("El estado 8088 tiene datos inválidos.");
        registers.CopyTo(_r, 0); segments.CopyTo(_s, 0);
        IP = ip; Flags = flags; Halted = halted; Instructions = instructions;
        _interruptShadow = shadow; _segmentShadow = segmentShadow; _nmi = nmi; _trapPending = trap; _irq = irq;
        _waitingForTest = waiting; _segmentOverride = -1;
        _startCs = CS; _startIp = IP;
        _busLocked = false; bus.SetLock(false);
        if (timing != null) RestoreTiming(timing);
        else { TimingEnabled = false; _cycles = 0; _repeatCode.Clear(); FlushQueue(); }
    }
}