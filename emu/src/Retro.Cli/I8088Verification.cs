using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Retro.Cpu.I8088;
/// <summary>Functional state/memory oracle for the separately obtained SingleStepTests 8088 v2 files.</summary>
internal static class I8088Verification
{
    public static int Run(string folder, string? report)
    {
        if (string.IsNullOrWhiteSpace(folder)) { Console.Error.WriteLine("Indicá --suite con la carpeta v2 de SingleStepTests/8088."); return 1; }
        try
        {
            var root = Path.GetFullPath(folder);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "metadata.json")));
            if (metadata.RootElement.GetProperty("syntax_version").GetInt32() != 2 || metadata.RootElement.GetProperty("cpu").GetString() != "8088")
                throw new InvalidDataException("Se requiere SingleStepTests/8088 formato 2.");
            var files = Directory.GetFiles(root, "*.json.gz").Order().ToArray();
            if (files.Length == 0) throw new InvalidDataException("La carpeta no contiene pruebas .json.gz.");
            var props = typeof(I8088Cpu).GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(ushort)).ToDictionary(p => p.Name.ToLowerInvariant());
            var bus = new Bus(); var results = new List<object>(); int totalPassed = 0, totalFailed = 0, totalSkipped = 0;
            foreach (var file in files)
            {
                using var zipped = File.OpenRead(file); using var gzip = new GZipStream(zipped, CompressionMode.Decompress); using var document = JsonDocument.Parse(gzip);
                string opcode = Path.GetFileName(file).Split('.')[0]; var info = metadata.RootElement.GetProperty("opcodes").GetProperty(opcode);
                int passed = 0, failed = 0, skipped = 0; var errors = new List<object>();
                foreach (var test in document.RootElement.EnumerateArray())
                {
                    var bytes = test.GetProperty("bytes").EnumerateArray().Select(v => v.GetByte()).ToArray(); int actualOpcode = 0;
                    while (actualOpcode < bytes.Length && bytes[actualOpcode] is 0x26 or 0x2E or 0x36 or 0x3E or 0xF0 or 0xF2 or 0xF3) actualOpcode++;
                    var detail = info;
                    if (info.TryGetProperty("reg", out var group) && group.TryGetProperty(((bytes[actualOpcode + 1] >> 3) & 7).ToString(), out var member)) detail = member;
                    if (detail.TryGetProperty("status", out var status) && status.GetString() is not "normal") { skipped++; continue; }
                    Array.Fill(bus.Ram, (byte)0x90); var cpu = new I8088Cpu(bus);
                    var initial = test.GetProperty("initial");
                    foreach (var reg in initial.GetProperty("regs").EnumerateObject()) props[reg.Name].SetValue(cpu, reg.Value.GetUInt16());
                    foreach (var cell in initial.GetProperty("ram").EnumerateArray()) bus.Ram[cell[0].GetInt32()] = cell[1].GetByte();
                    for (int i = 0; i < bytes.Length; i++) bus.Ram[I8088Cpu.Physical(cpu.CS, (ushort)(cpu.IP + i))] = bytes[i];
                    string? problem = null;
                    try
                    {
                        ushort startIp = cpu.IP; int steps = 0;
                        do { cpu.Step(); if (++steps > 65536) throw new Exception("REP sin límite"); } while (cpu.IP == startIp && bytes[actualOpcode] is 0xA4 or 0xA5 or 0xA6 or 0xA7 or 0xAA or 0xAB or 0xAC or 0xAD or 0xAE or 0xAF);
                        var final = test.GetProperty("final"); var expected = initial.GetProperty("regs").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
                        foreach (var reg in final.GetProperty("regs").EnumerateObject()) expected[reg.Name] = reg.Value.GetInt32();
                        int mask = detail.TryGetProperty("flags-mask", out var m) ? m.GetInt32() : 65535;
                        // OF is architecturally undefined for counts greater than one, regardless of metadata.
                        if (bytes[actualOpcode] is 0xD2 or 0xD3 && (initial.GetProperty("regs").GetProperty("cx").GetInt32() & 255) > 1) mask &= ~I8088Cpu.Overflow;
                        foreach (var (name, value) in expected) { int actual = (ushort)props[name].GetValue(cpu)!; int compare = name == "flags" ? mask : 65535; if ((actual & compare) != (value & compare)) { problem = $"{name} esperado {value:X4}, actual {actual:X4}, máscara {compare:X4}"; break; } }
                        if (problem == null) foreach (var cell in final.GetProperty("ram").EnumerateArray())
                        {
                            int a = cell[0].GetInt32(), expectedByte = cell[1].GetByte(), byteMask = 255;
                            bool arithmeticFault = cpu.CS == 0 && cpu.IP == 0x400 && bytes[actualOpcode] is 0xD4 or 0xF6 or 0xF7;
                            int stackFlags = (int)I8088Cpu.Physical(initial.GetProperty("regs").GetProperty("ss").GetUInt16(), (ushort)(initial.GetProperty("regs").GetProperty("sp").GetUInt16() - 2));
                            if (arithmeticFault && a == stackFlags) byteMask = mask & 255;
                            if (arithmeticFault && a == ((stackFlags + 1) & 0xFFFFF)) byteMask = (mask >> 8) & 255;
                            if ((bus.Ram[a] & byteMask) != (expectedByte & byteMask)) { problem = $"RAM {cell[0]} esperada {cell[1]}, actual {bus.Ram[cell[0].GetInt32()]}"; break; }
                        }
                    }
                    catch (Exception error) { problem = error.Message; }
                    if (problem == null) passed++; else { failed++; if (errors.Count < 5) errors.Add(new { Test = test.GetProperty("name").GetString(), Index = test.GetProperty("idx").GetInt32(), Problem = problem, Initial = initial.ToString(), Bytes = bytes }); }
                }
                totalPassed += passed; totalFailed += failed; totalSkipped += skipped;
                results.Add(new { File = Path.GetFileName(file), Opcode = opcode, Passed = passed, Failed = failed, Skipped = skipped, Errors = errors }); Console.WriteLine($"{opcode}: {passed} PASS, {failed} FAIL, {skipped} no documentadas");
            }
            if (report != null) { string path = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(new { Passed = totalPassed, Failed = totalFailed, Excluded = totalSkipped, Scope = "Registros y memoria; sin validar ciclos, cola de prelectura ni flags indefinidos.", Files = results }, new JsonSerializerOptions { WriteIndented = true })); }
            Console.WriteLine($"Aprobados: {totalPassed}; fallidos: {totalFailed}; excluidos por metadatos: {totalSkipped}.");
            Console.WriteLine("Se contrastaron registros y memoria. Esta prueba no valida ciclos ni la cola de prelectura.");
            return totalFailed == 0 ? 0 : 2;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { Console.Error.WriteLine("No se pudo leer la suite: " + error.Message); return 1; }
    }
    private sealed class Bus : I8088Bus
    {
        public byte[] Ram = new byte[1 << 20]; public byte ReadMemory(uint a) => Ram[a]; public void WriteMemory(uint a, byte v) => Ram[a] = v; public byte In(ushort p) => 255; public void Out(ushort p, byte v) { }
    }
}
