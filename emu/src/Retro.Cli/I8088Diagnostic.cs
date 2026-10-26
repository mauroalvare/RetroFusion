using Retro.Cpu.I8088;

/// <summary>Bounded raw-program diagnostic; deliberately supplies neither BIOS nor DOS services.</summary>
internal static class I8088Diagnostic
{
    private sealed class Bus : I8088Bus
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => throw new NotSupportedException($"El diagnóstico no tiene un dispositivo en el puerto {port:X4}.");
        public void Out(ushort port, byte value) => throw new NotSupportedException($"El diagnóstico no tiene un dispositivo en el puerto {port:X4}.");
    }

    public static int Run(string path, string? steps, I8088Model model = I8088Model.Intel8088)
    {
        if (!int.TryParse(steps ?? "100000", out int limit) || limit is < 1 or > 10_000_000)
        { Console.Error.WriteLine("--steps debe estar entre 1 y 10000000."); return 1; }
        try
        {
            if (model == I8088Model.Intel8086 && path is "irq-demo" or "timer-demo" or "verificar")
                throw new NotSupportedException("Ese diagnóstico corresponde al 8088 del PC/XT. Para 8086 usá demo, timed-demo o un programa crudo.");
            if (path == "irq-demo") return PcInterruptDiagnostic.Run(limit);
            if (path == "timer-demo") return PcTimerDiagnostic.Run(limit);
            bool timed = path == "timed-demo";
            if (path is not ("demo" or "timed-demo") && new FileInfo(path).Length is 0 or > 65280)
                throw new InvalidDataException("El programa crudo debe tener entre 1 y 65280 bytes.");
            // Original tiny program: sum 1..10, store at DS:0200, halt. No external firmware or game.
            byte[] program = path is "demo" or "timed-demo" ? [0xB9, 10, 0, 0x31, 0xC0, 0x31, 0xDB, 0x43, 0x01, 0xD8, 0xE2, 0xFB, 0xA3, 0, 2, 0xF4]
                : File.ReadAllBytes(path);
            if (program.Length is 0 or > 65280) throw new InvalidDataException("El programa crudo debe tener entre 1 y 65280 bytes.");
            var bus = new Bus();
            program.CopyTo(bus.Memory, 0x10100);
            var cpu = new I8088Cpu(bus, model) { CS = 0x1000, DS = 0x1000, ES = 0x1000, SS = 0x1000, IP = 0x100, SP = 0xFFFE, TimingEnabled = timed };
            Console.WriteLine($"Diagnóstico {cpu.CpuName}: programa crudo en 1000:0100. Sin BIOS, DOS ni temporización de PC.");
            for (int i = 0; i < limit && !cpu.Halted; i++) cpu.Step();
            Console.WriteLine($"AX={cpu.AX:X4} BX={cpu.BX:X4} CX={cpu.CX:X4} DX={cpu.DX:X4} SI={cpu.SI:X4} DI={cpu.DI:X4}");
            Console.WriteLine($"CS:IP={cpu.CS:X4}:{cpu.IP:X4} SS:SP={cpu.SS:X4}:{cpu.SP:X4} FLAGS={cpu.Flags:X4}; pasos={cpu.Instructions}; HLT={cpu.Halted}");
            if (timed) Console.WriteLine($"Reloj: {cpu.Cycles} T; bus de {(model == I8088Model.Intel8086 ? "palabra y cola de 6" : "byte y cola de 4")} bytes, tiempos EU aproximados.");
            if (!cpu.Halted) Console.Error.WriteLine("Se alcanzó el límite de pasos sin HLT.");
            return cpu.Halted ? 0 : 2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { Console.Error.WriteLine(e.Message); return 1; }
    }
}
