using Retro.Cpu.I8088;
using Retro.Systems.Pc;

/// <summary>Own guest configures PIT/PIC via OUT and handles timer IRQ0. No BIOS, DOS or CPU clock ratio.</summary>
internal static class PcTimerDiagnostic
{
    private sealed class Board : I8088Bus, I8088InterruptSource
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public readonly Pic8259A Pic = new();
        public readonly Pit8253 Pit = new();
        public readonly List<byte> Accepted = [];
        public int Pulses { get; private set; }
        public Board() => Pit.OutputChanged += (channel, high) => { if (channel == 0) Pic.SetLine(0, high); };
        public bool InterruptRequested => Pic.InterruptRequested;
        public byte Acknowledge() { byte vector = Pic.Acknowledge(); Accepted.Add(vector); return vector; }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => port switch
        {
            0x20 => Pic.ReadCommand(), 0x21 => Pic.ReadData(),
            >= 0x40 and <= 0x42 => Pit.ReadCounter(port - 0x40),
            _ => throw new NotSupportedException($"Puerto no conectado: {port:X4}.")
        };
        public void Out(ushort port, byte value)
        {
            if (port == 0x20) Pic.WriteCommand(value);
            else if (port == 0x21) Pic.WriteData(value);
            else if (port is >= 0x40 and <= 0x42) Pit.WriteCounter(port - 0x40, value);
            else if (port == 0x43) Pit.WriteControl(value);
            else throw new NotSupportedException($"Puerto no conectado: {port:X4}.");
        }
        public void Pulse() { Pit.Tick(0); Pulses++; }
    }

    public static int Run(int limit)
    {
        var board = new Board();
        board.Pit.SetGate(0, true);
        var cpu = new I8088Cpu(board, board) { CS = 0x1000, DS = 0x1000, ES = 0x1000, SS = 0x1000, SP = 0xFFFE, IP = 0x100 };
        byte[] program = [
            0xB0,0x34,0xE6,0x43, 0xB0,4,0xE6,0x40, 0xB0,0,0xE6,0x40, // PIT0: mode 2, divisor 4.
            0xB0,0x13,0xE6,0x20, 0xB0,8,0xE6,0x21, 0xB0,1,0xE6,0x21,
            0xB0,0xFE,0xE6,0x21, 0xFB,0x90, // Initialize PIC after PIT programming sets OUT high.
            0xF4,0x80,0x3E,0,3,3,0x72,0xF8, // HLT; CMP byte [0300],3; JB HLT.
            0xFA,0xB0,0xFF,0xE6,0x21, // CLI; mask IRQs.
            0xB0,0,0xE6,0x43,0xE4,0x40,0xA2,2,3,0xE4,0x40,0xA2,3,3,0xF4]; // Latch/read PIT0, HLT.
        program.CopyTo(board.Memory, 0x10100);
        cpu.WriteWord(0, 8 * 4, 0x200); cpu.WriteWord(0, 8 * 4 + 2, 0x1000);
        byte[] handler = [0x50,0xFE,0x06,0,3,0xB0,0x20,0xE6,0x20,0x58,0xCF]; // Preserve AX; INC [0300]; EOI; IRET.
        handler.CopyTo(board.Memory, 0x10200);
        int actions = 0;
        while (actions < limit && !(cpu.Halted && !cpu.Flag(I8088Cpu.InterruptEnable)))
        {
            // Diagnostic-only clock schedule: pulses while HLT waits, never an invented instruction/cycle ratio.
            if (cpu.Halted && !board.InterruptRequested) board.Pulse();
            else cpu.Step();
            actions++;
        }
        bool complete = cpu.Halted && !cpu.Flag(I8088Cpu.InterruptEnable);
        ushort count = (ushort)(board.Memory[0x10302] | (board.Memory[0x10303] << 8));
        Console.WriteLine("Diagnóstico funcional 8088 + 8259A + 8253. Reloj externo; sin BIOS ni DOS.");
        Console.WriteLine($"IRQ0 atendidas={board.Memory[0x10300]}; vectores={string.Join(" ", board.Accepted.Select(v => v.ToString("X2")))}; pulsos PIT={board.Pulses}; contador leído={count:X4}.");
        Console.WriteLine($"Pendientes={board.Pic.RequestRegister:X2}; en servicio={board.Pic.InServiceRegister:X2}; CS:IP={cpu.CS:X4}:{cpu.IP:X4}; SP={cpu.SP:X4}; instrucciones={cpu.Instructions}; acciones={actions}; HLT={cpu.Halted}.");
        if (!complete) { Console.Error.WriteLine("Se alcanzó el límite de acciones antes de completar el diagnóstico."); return 2; }
        bool correct = board.Accepted.SequenceEqual(new byte[] {8,8,8}) && board.Memory[0x10300] == 3 &&
            board.Pulses == 13 && count == 4 && cpu.SP == 0xFFFE && cpu.CS == 0x1000 && cpu.IP == 0x13A &&
            board.Pic.RequestRegister == 0 && board.Pic.InServiceRegister == 0 && board.Pic.Mask == 0xFF;
        if (!correct) { Console.Error.WriteLine("El diagnóstico no coincide con el resultado esperado."); return 1; }
        return 0;
    }
}
