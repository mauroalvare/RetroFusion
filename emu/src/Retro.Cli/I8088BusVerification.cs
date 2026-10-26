using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Retro.Cpu.I8088;

/// <summary>Compares data access order/value and relative spacing; not absolute EU or prefetch timing.</summary>
internal static class I8088BusVerification
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Ram = new byte[1 << 20];
        public byte ReadMemory(uint address) => Ram[address];
        public void WriteMemory(uint address, byte value) => Ram[address] = value;
        public byte In(ushort port) => 255;
        public void Out(ushort port, byte value) { }
    }
    internal static List<I8088BusAccess> ReadTransfers(JsonElement cycles)
    {
        var rows = cycles.EnumerateArray().ToArray();
        var result = new List<I8088BusAccess>();
        uint address = 0; string? kind = null;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.GetArrayLength() != 11) throw new InvalidDataException("Una fila de bus debe tener once campos.");
            if ((row[0].GetInt32() & 1) != 0)
            {
                address = row[1].GetUInt32(); kind = row[7].GetString();
                if (address > 0xFFFFF) throw new InvalidDataException("Dirección de bus fuera de 20 bits.");
            }
            string? phase = row[8].GetString();
            if (phase is not ("T3" or "Tw") || i + 1 < rows.Length && rows[i + 1][8].GetString() == "Tw") continue;
            string memory = row[3].GetString() ?? "", ports = row[4].GetString() ?? "";
            if (memory.Length != 3 || ports.Length != 3) throw new InvalidDataException("Señales de bus incompletas.");
            I8088BusAccessKind? access = kind switch
            {
                "MEMR" when memory[0] == 'R' => I8088BusAccessKind.MemoryRead,
                "MEMW" when memory[1] == 'A' => I8088BusAccessKind.MemoryWrite,
                "IOR" when ports[0] == 'R' => I8088BusAccessKind.Input,
                "IOW" when ports[1] == 'A' => I8088BusAccessKind.Output,
                _ => null
            };
            if (access is { } value) result.Add(new(i + 1, value, address, row[6].GetByte()));
            else if (kind == null && (memory != "---" || ports != "---"))
                throw new InvalidDataException("Transferencia sin dirección retenida por ALE.");
        }
        return result;
    }

    public static int Run(string folder, string? report, string? opcodes, string? cases)
    {
        if (!int.TryParse(cases ?? "1000", out int limit) || limit is < 1 or > 10000)
        { Console.Error.WriteLine("--casos debe estar entre 1 y 10000 por archivo."); return 1; }
        try
        {
            string[] selected = (opcodes ?? "88,89,8A,8B,A0,A1,A2,A3,E4,E5,E6,E7,EC,ED,EE,EF")
                .Split(',', StringSplitOptions.TrimEntries).Select(v => v.ToUpperInvariant()).Distinct().ToArray();
            if (selected.Length == 0 || selected.Any(v => v.Length != 2 || !byte.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException("--opcodes requiere bytes hexadecimales separados por coma.");
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Indicá --suite con la carpeta v2 de SingleStepTests/8088.");
            string root = Path.GetFullPath(folder);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "metadata.json")));
            if (metadata.RootElement.GetProperty("syntax_version").GetInt32() != 2 || metadata.RootElement.GetProperty("cpu").GetString() != "8088")
                throw new InvalidDataException("Se requiere SingleStepTests/8088 formato 2.");
            var properties = typeof(I8088Cpu).GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(ushort)).ToDictionary(p => p.Name.ToLowerInvariant());
            var bus = new Bus(); var files = new List<object>();
            int checkedCases = 0, sequenceFailures = 0, timingFailures = 0, timingCompared = 0, pairs = 0, excluded = 0, noPairs = 0;
            foreach (string opcode in selected)
            {
                using var input = File.OpenRead(Path.Combine(root, opcode + ".json.gz"));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var document = JsonDocument.Parse(gzip);
                var tests = document.RootElement; int take = Math.Min(limit, tests.GetArrayLength());
                if (take == 0) throw new InvalidDataException("El archivo no contiene casos.");
                var info = metadata.RootElement.GetProperty("opcodes").GetProperty(opcode);
                int fileChecked = 0, fileSequenceFailures = 0, fileTimingFailures = 0;
                var differences = new List<object>();
                for (int n = 0; n < take; n++)
                {
                    // Spread samples over the file so both initially empty/full queues are exercised.
                    var test = tests[n * tests.GetArrayLength() / take];
                    byte[] bytes = test.GetProperty("bytes").EnumerateArray().Select(v => v.GetByte()).ToArray();
                    int operation = 0;
                    while (operation < bytes.Length && bytes[operation] is 0x26 or 0x2E or 0x36 or 0x3E or 0xF0 or 0xF2 or 0xF3) operation++;
                    if (operation == bytes.Length) throw new InvalidDataException("Caso sin opcode.");
                    var detail = info;
                    if (info.TryGetProperty("reg", out var groups)) detail = groups.GetProperty(((bytes[operation + 1] >> 3) & 7).ToString());
                    if (detail.TryGetProperty("status", out var status) && status.GetString() != "normal") { excluded++; continue; }
                    Array.Fill(bus.Ram, (byte)0x90);
                    var cpu = new I8088Cpu(bus);
                    var initial = test.GetProperty("initial");
                    foreach (var reg in initial.GetProperty("regs").EnumerateObject()) properties[reg.Name].SetValue(cpu, reg.Value.GetUInt16());
                    foreach (var cell in initial.GetProperty("ram").EnumerateArray()) bus.Ram[cell[0].GetInt32()] = cell[1].GetByte();
                    for (int i = 0; i < bytes.Length; i++) bus.Ram[I8088Cpu.Physical(cpu.CS, (ushort)(cpu.IP + i))] = bytes[i];
                    cpu.TimingEnabled = true;
                    cpu.InitializePrefetchQueue(initial.GetProperty("queue").EnumerateArray().Select(v => v.GetByte()).ToArray());
                    var actual = new List<I8088BusAccess>();
                    cpu.BusAccessed += access => { if (access.Kind != I8088BusAccessKind.Code) actual.Add(access); };
                    var expected = ReadTransfers(test.GetProperty("cycles"));
                    string? error = null;
                    try
                    {
                        ushort start = cpu.IP; int steps = 0;
                        do
                        {
                            cpu.Step();
                            if (++steps > 65536) throw new InvalidDataException("REP supera el límite de pasos.");
                        } while (cpu.IP == start && bytes[operation] is 0xA4 or 0xA5 or 0xA6 or 0xA7 or 0xAA or 0xAB or 0xAC or 0xAD or 0xAE or 0xAF);
                    }
                    catch (Exception e) when (e is NotSupportedException or InvalidDataException) { error = e.Message; }
                    bool sequence = error == null && actual.Count == expected.Count && actual.Zip(expected)
                        .All(pair => pair.First.Kind == pair.Second.Kind && pair.First.Address == pair.Second.Address && pair.First.Value == pair.Second.Value);
                    bool? timing = null;
                    if (!sequence) { sequenceFailures++; fileSequenceFailures++; }
                    else if (expected.Count > 1)
                    {
                        timingCompared++; pairs += expected.Count - 1;
                        timing = actual.Zip(expected).All(pair => pair.First.Clock - actual[0].Clock == pair.Second.Clock - expected[0].Clock);
                        if (timing == false) { timingFailures++; fileTimingFailures++; }
                    }
                    else noPairs++;
                    checkedCases++; fileChecked++;
                    if ((!sequence || timing == false) && differences.Count < 5)
                        differences.Add(new { Index = test.GetProperty("idx").GetInt32(), Name = test.GetProperty("name").GetString(),
                            SequenceMatches = sequence, RelativeTimingMatches = timing, Error = error, Expected = expected, Actual = actual });
                }
                files.Add(new { Opcode = opcode, Checked = fileChecked, SequenceFailures = fileSequenceFailures, RelativeTimingFailures = fileTimingFailures, Differences = differences });
                Console.WriteLine($"{opcode}: {fileChecked} casos; diferencias de accesos={fileSequenceFailures}, de intervalos={fileTimingFailures}.");
            }
            if (checkedCases == 0) throw new InvalidDataException("No se comparó ningún caso documentado.");
            var result = new { Checked = checkedCases, SequenceFailures = sequenceFailures, RelativeTimingFailures = timingFailures,
                TimingComparedCases = timingCompared, TimingComparedPairs = pairs, NoTimingPairs = noPairs, Excluded = excluded,
                Scope = "Accesos de datos: orden, dirección, valor e intervalos relativos al primer acceso. Sin tiempos absolutos EU, prelectura, INTA, señales eléctricas ni estado final.", Files = files };
            if (report != null)
            {
                string path = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var options = new JsonSerializerOptions { WriteIndented = true }; options.Converters.Add(new JsonStringEnumConverter());
                File.WriteAllText(path, JsonSerializer.Serialize(result, options));
            }
            Console.WriteLine($"Casos: {checkedCases}; diferencias de accesos: {sequenceFailures}; de intervalos: {timingFailures}; pares contrastados: {pairs}.");
            Console.WriteLine(result.Scope);
            return sequenceFailures == 0 && timingFailures == 0 ? 0 : 2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException)
        { Console.Error.WriteLine("No se pudo contrastar el bus: " + e.Message); return 1; }
    }
}
