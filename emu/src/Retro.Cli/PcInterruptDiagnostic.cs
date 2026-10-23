using Retro.Cpu.I8088;
using Retro.Systems.Pc;

/// <summary>Own guest program, real CPU/PIC interaction. No BIOS, DOS or invented clocks.</summary>
internal static class PcInterruptDiagnostic
{
    private sealed class Board : I8088Bus, I8088InterruptSource
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public readonly Pic8259A Pic = new();
        public readonly List<byte> Accepted = [];
        public bool InterruptRequested => Pic.InterruptRequested;
        public byte Acknowledge() { byte vector = Pic.Acknowledge(); Accepted.Add(vector); return vector; }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => port switch { 0x20 => Pic.ReadCommand(), 0x21 => Pic.ReadData(),
            _ => throw new NotSupportedException($"Puerto no conectado: {port:X4}.") };
        public void Out(ushort port, byte value)
        {
            if (port == 0x20) Pic.WriteCommand(value);
            else if (port == 0x21) Pic.WriteData(value);
            else throw new NotSupportedException($"Puerto no conectado: {port:X4}.");
        }
    }

    public static int Run(int limit)
    {
        var board = new Board();
        var cpu = new I8088Cpu(board, board) { CS = 0x1000, DS = 0x1000, ES = 0x1000, SS = 0x1000, SP = 0xFFFE, IP = 0x100 };
        byte[] program = [
            0xB0,0x13,0xE6,0x20, 0xB0,8,0xE6,0x21, 0xB0,1,0xE6,0x21,
            0xB0,0xFC,0xE6,0x21, 0xFB,0x90,0xF4, // Enable IRQ0/1, STI; NOP; HLT.
            0xB0,0xDC,0xE6,0x21, 0xFA,0xF4]; // Unmask queued IRQ5, then CLI; HLT.
        program.CopyTo(board.Memory, 0x10100);
        for (int line = 0; line < 8; line++)
        {
            ushort entry = (ushort)(0x200 + line * 16), counter = (ushort)(0x300 + line);
            cpu.WriteWord(0, (ushort)((8 + line) * 4), entry);
            cpu.WriteWord(0, (ushort)((8 + line) * 4 + 2), 0x1000);
            byte[] handler = [0xFE,0x06,(byte)counter,(byte)(counter >> 8),0xB0,0x20,0xE6,0x20,0xCF];
            handler.CopyTo(board.Memory, 0x10000 + entry);
        }
        bool injected = false;
        int steps = 0;
        for (; steps < limit; steps++)
        {
            if (cpu.Halted)
            {
                if (injected) break;
                // Hold each line through acknowledge, as required by the edge-input protocol.
                board.Pic.SetLine(5, true); board.Pic.SetLine(1, true); board.Pic.SetLine(0, true);
                injected = true;
            }
            cpu.Step();
        }
        bool complete = injected && cpu.Halted && !cpu.Flag(I8088Cpu.InterruptEnable);
        Console.WriteLine("Diagnóstico funcional 8088 + 8259A. Sin BIOS ni DOS; no mide tiempos de PC.");
        Console.WriteLine("Vectores atendidos: " + string.Join(" ", board.Accepted.Select(v => v.ToString("X2"))));
        Console.WriteLine($"Contadores IRQ0={board.Memory[0x10300]}, IRQ1={board.Memory[0x10301]}, IRQ5={board.Memory[0x10305]}; pendientes={board.Pic.RequestRegister:X2}; en servicio={board.Pic.InServiceRegister:X2}.");
        Console.WriteLine($"CS:IP={cpu.CS:X4}:{cpu.IP:X4}; SP={cpu.SP:X4}; instrucciones={cpu.Instructions}; pasos={steps}; HLT={cpu.Halted}.");
        if (!complete) { Console.Error.WriteLine("Se alcanzó el límite de pasos antes de completar el diagnóstico."); return 2; }
        bool correct = board.Accepted.SequenceEqual(new byte[] {8,9,13}) && cpu.SP == 0xFFFE &&
            board.Memory[0x10300] == 1 && board.Memory[0x10301] == 1 && board.Memory[0x10305] == 1 &&
            board.Pic.RequestRegister == 0 && board.Pic.InServiceRegister == 0;
        if (!correct) { Console.Error.WriteLine("El diagnóstico no coincide con el resultado esperado."); return 1; }
        return 0;
    }
}
