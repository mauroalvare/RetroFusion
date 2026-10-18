using System.Diagnostics;
using Retro.Core;
using Retro.Systems.Arcade;
using Retro.Systems.Spectrum;

// Headless runner: loads real software into an emulated machine, runs it for a number
// of frames (as fast as possible), presses keys on given frames and saves screenshots
// and audio. Used by automated tests and to check compatibility without a window.

static string NesInfo(byte[] data)
{
    try { return "NES: " + Retro.Systems.Nes.NesCartridge.Load(data).Describe(); }
    catch (InvalidDataException e) { return "NES, no se puede cargar: " + e.Message; }
}

static int Usage()
{
    Console.WriteLine("""
        RetroEmu CLI

          retro-cli info <archivo>
          retro-cli run <archivo> [opciones]
          retro-cli fuse <receta> --games <carpeta> [--ticks n] [--screenshot png]
          retro-cli studio <juego.js|juego.json|id> --games <carpeta> [--seconds n] [--shots prefijo]
          retro-cli compat <carpeta-guiones|guion.json> --juegos <carpeta> [--salida r.json] [--md tabla.md]
                    [--capturas carpeta] [--solo id1,id2] [--descubrir] [--dos-veces] [--paralelo n]
          retro-cli lab <guion.json> [--juegos carpeta] [--cuadros n] [--salida hallazgos.json]
                    [--hipotesis tipo:dirección[:codificación:bytes]]... [--sin-verificar]
          retro-cli juegos <carpeta>   todos los juegos que se pueden fusionar, con el id de su perfil
          retro-cli preparar <juego> [--ia sub-claude|sub-codex|sub-google|claude|ollama|agent|ninguna]
                    [--carpeta carpeta-del-agente] [--rondas n] [--perfiles carpeta] [--sin-guardar]
                    prepara un juego para las fusiones: llega a la partida (prueba de teclas y la IA
                    elegida en la app), encuentra y verifica sus variables y guarda un perfil por hash

        Máquinas: ZX Spectrum 48K (.tap .tzx .z80 .sna), Space Invaders (invaders.zip de MAME),
        Commodore 64 (.t64 .prg) y NES (.nes con mapper 0 o 3; teclas P1_A, P1_START...).

        Opciones de run:
          --rom <ruta>             ROM del Spectrum 48K (por defecto roms/48.rom)
          --frames <n>             cuadros a emular. Por defecto 500
          --autoload               (Spectrum) escribe LOAD "" ENTER al arrancar
          --until-tape-end         (Spectrum) sigue hasta que la cinta termina (+ --frames extra)
          --press <c>:<teclas>:<n> en el cuadro c mantiene teclas n cuadros. Teclas unidas con +,
                                   espacios como _ (ej. 300:COIN:5  400:P1_START:5  500:SYMBOL_SHIFT+P:4)
          --screenshot <png>       guarda la pantalla final
          --shot-every <n> --shot-prefix <p>   capturas periódicas
          --wav <archivo>          guarda el audio emulado
          --samples <zip|carpeta>  (Space Invaders) sonidos grabados de MAME (samples/invaders.zip)
          --trace <n>              traza las primeras n instrucciones
          --pick <n>               elige el archivo n dentro de un zip
          --poke <c>:<hex>=<n>     en el cuadro c escribe el valor n en la dirección (probar hipótesis)
          --disasm <hex>:<n>       al final, desensambla n instrucciones desde la dirección
          --dump <hex>:<n>         al final, muestra n bytes de memoria
          --text                   (C64) al final, muestra el texto de la pantalla
        """);
    return 1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length < 2)
    return Usage();

string command = args[0];
string mediaPath = args[1];
var opts = new Dictionary<string, List<string>>();
for (int i = 2; i < args.Length; i++)
{
    if (!args[i].StartsWith("--"))
        continue;
    string key = args[i][2..];
    string value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
    if (!opts.TryGetValue(key, out var list))
        opts[key] = list = new List<string>();
    list.Add(value);
}
string? Opt(string k) => opts.TryGetValue(k, out var v) ? v[^1] : null;

if (command == "studio")
    return RunStudio(mediaPath, Opt("games") ?? ".", Opt("seconds"), Opt("shots"));

if (command == "compat")
    return RunCompat(mediaPath, Opt("juegos") ?? @"C:\Users\mauro\Downloads\ROMs", Opt("salida"), Opt("md"), Opt("capturas") ?? "out/compat",
        Opt("solo"), Opt("descubrir") != null, Opt("dos-veces") != null, int.Parse(Opt("paralelo") ?? "4"));

if (command == "lab")
    return RunLab(mediaPath, Opt("juegos") ?? @"C:\Users\mauro\Downloads\ROMs", Opt("salida"), int.Parse(Opt("cuadros") ?? "3000"),
        Opt("sin-verificar") == null, opts.GetValueOrDefault("hipotesis") ?? new List<string>());

if (command == "juegos")
    return ListGames(mediaPath);

if (command == "preparar")
    return await RunPrepare(mediaPath, Opt("ia"), Opt("perfiles"), int.Parse(Opt("rondas") ?? "24"), Opt("sin-guardar") == null, Opt("carpeta"));

if (command == "fuse")
    return RunFusion(mediaPath, Opt("games") ?? ".", Opt("ticks"), Opt("screenshot"), Opt("shot-every"), Opt("shot-prefix"), opts.GetValueOrDefault("press"), Opt("layout"));

var files = MediaArchive.Open(mediaPath);
bool isInvaders = SpaceInvadersRom.Probe(files) >= 80;

if (SpaceInvadersSound.IsSampleSet(files))
{
    Console.WriteLine($"{Path.GetFileName(mediaPath)}: sonidos grabados de Space Invaders (samples/ de MAME, {files.Count} archivos WAV).");
    Console.WriteLine("No contiene el programa del juego: hace falta el invaders.zip de roms/ (invaders.e, .f, .g, .h).");
    Console.WriteLine("Para usar estos sonidos: retro-cli run <roms/invaders.zip> --samples " + mediaPath);
    return command == "info" ? 0 : 2;
}

if (command == "info")
{
    if (isInvaders)
        Console.WriteLine($"Placa arcade Space Invaders: {SpaceInvadersRom.Build(files).description}");
    foreach (var c in files.Select((f, i) => (f, i)))
    {
        int score = SpectrumMedia.Probe(c.f);
        int c64Score = Retro.Systems.C64.C64Media.Probe(c.f);
        string what = score > 0 ? $"ZX Spectrum (confianza {score})"
            : c64Score > 0 ? $"Commodore 64 (confianza {c64Score})"
            : Retro.Systems.Nes.NesCartridge.IsINes(c.f.Data) ? NesInfo(c.f.Data)
            : isInvaders ? "chip de Space Invaders" : "no reconocido por las máquinas disponibles";
        Console.WriteLine($"[{c.i}] {c.f.Name} ({c.f.Data.Length} bytes) -> {what}");
    }
    return 0;
}
if (command != "run")
    return Usage();

IMachine machine;
Spectrum48? zx = null;
bool isTape = false;
var c64Media = files.Where(f => Retro.Systems.C64.C64Media.Probe(f) > 0).OrderBy(f => MediaArchive.DumpScore(f.Name)).FirstOrDefault();
var nesMedia = files.Where(f => Retro.Systems.Nes.NesCartridge.IsINes(f.Data)).OrderBy(f => MediaArchive.DumpScore(f.Name)).FirstOrDefault();
if (nesMedia != null && !isInvaders)
{
    Retro.Systems.Nes.NesCartridge cart;
    try { cart = Retro.Systems.Nes.NesCartridge.Load(nesMedia.Data); }
    catch (InvalidDataException e)
    {
        Console.Error.WriteLine("No se pudo cargar: " + e.Message);
        return 3;
    }
    machine = new Retro.Systems.Nes.Nes(cart);
    Console.WriteLine($"Máquina: {machine.DisplayName}");
    Console.WriteLine($"Archivo: {nesMedia.Name}");
    Console.WriteLine(cart.Describe());
}
else if (c64Media != null && !isInvaders)
{
    var c64 = new Retro.Systems.C64.C64(Retro.Systems.C64.C64Firmware.Find(Path.GetDirectoryName(FindRom("48.rom"))!));
    machine = c64;
    Console.WriteLine($"Máquina: {c64.DisplayName} ({c64.FirmwareDescription})");
    Console.WriteLine($"Archivo: {c64Media.Name}");
    var result = Retro.Systems.C64.C64Media.Load(c64, c64Media);
    Console.WriteLine(result.Description);
    foreach (var w in result.Warnings)
        Console.WriteLine("  aviso: " + w);
}
else if (isInvaders)
{
    var (rom, description) = SpaceInvadersRom.Build(files);
    var invaders = new SpaceInvaders(rom);
    machine = invaders;
    Console.WriteLine($"Máquina: {machine.DisplayName}");
    Console.WriteLine(description);
    string? samples = Opt("samples") ?? SpaceInvadersSound.FindSamples(Path.GetDirectoryName(Path.GetFullPath(mediaPath)), null);
    int loadedSamples = samples != null ? invaders.Sound.LoadSamples(samples) : 0;
    Console.WriteLine(loadedSamples > 0 ? $"Sonidos: {loadedSamples} grabaciones de {samples}" : "Sonidos: sintetizados");
}
else
{
    var candidates = files.Select((f, i) => (f, i, score: SpectrumMedia.Probe(f)))
        .OrderByDescending(c => c.score).ThenBy(c => MediaArchive.DumpScore(c.f.Name)).ToList();
    MediaFile media;
    if (Opt("pick") is { } pick)
        media = files[int.Parse(pick)];
    else if (candidates.Count > 0 && candidates[0].score > 0)
        media = candidates[0].f;
    else
    {
        Console.Error.WriteLine("Ningún archivo reconocido por las máquinas disponibles.");
        return 2;
    }
    string romPath = Opt("rom") ?? FindRom("48.rom");
    zx = new Spectrum48(File.ReadAllBytes(romPath));
    machine = zx;
    Console.WriteLine($"Máquina: {zx.DisplayName}  ROM: {romPath}");
    Console.WriteLine($"Archivo: {media.Name}  ({media.Source})");
    try
    {
        var result = SpectrumMedia.Load(zx, media);
        isTape = result.Kind == SpectrumMediaKind.Tape;
        Console.WriteLine(result.Description);
        foreach (var w in result.Warnings)
            Console.WriteLine("  aviso: " + w);
        if (isTape && zx.Tape.Tape != null)
            foreach (var b in zx.Tape.Tape.Blocks)
                Console.WriteLine($"  bloque {b.Index}: {b.Description}");
    }
    catch (Exception e)
    {
        Console.Error.WriteLine("No se pudo cargar: " + e.Message);
        return 3;
    }
    if (isTape && Opt("autoload") != null)
    {
        // Let the ROM boot to the copyright screen first (~3 s), then type LOAD "".
        for (int i = 0; i < 150; i++)
            zx.RunFrame(null);
        zx.TypeLoadCommand();
    }
}

int frames = int.Parse(Opt("frames") ?? "500");
var presses = new List<(int frame, string[] keys, int dur)>();
if (opts.TryGetValue("press", out var pressList))
    foreach (var p in pressList)
    {
        var parts = p.Split(':');
        presses.Add((int.Parse(parts[0]), parts[1].Replace('_', ' ').Split('+'), parts.Length > 2 ? int.Parse(parts[2]) : 5));
    }

if (Opt("trace") is { } traceCount)
{
    // Instruction trace: address, bytes, disassembly and registers before each instruction.
    int n = int.Parse(traceCount);
    var cpu = machine.Cpu;
    for (int i = 0; i < n; i++)
    {
        var ins = machine.Disassembler.Disassemble(a => machine.Memory.Peek(a), cpu.ProgramCounter);
        string regs = string.Join(" ", cpu.GetRegisters().Take(6).Select(r => $"{r.Name}={r.Value:X4}"));
        Console.WriteLine($"{cpu.ProgramCounter:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2"))),-12} {ins.Text,-22} {regs} ciclos={cpu.Cycles}");
        machine.StepInstruction();
    }
}

string? wavPath = Opt("wav");
var wav = new List<short>();
var frameAudio = new short[4096];
var sw = Stopwatch.StartNew();
int shotEvery = int.Parse(Opt("shot-every") ?? "0");
string shotPrefix = Opt("shot-prefix") ?? "shot";
bool untilTapeEnd = zx != null && Opt("until-tape-end") != null;
int lastBlock = -1;
long frame = 0;
int extraAfterTape = frames;
// --poke <frame>:<hex address>=<value>: writes memory on that frame (to test a hypothesis)
var pokes = (opts.GetValueOrDefault("poke") ?? new List<string>()).Select(p =>
{
    var (f, rest) = (p.Split(':')[0], p.Split(':')[1]);
    var kv = rest.Split('=');
    return (frame: long.Parse(f), address: Convert.ToUInt32(kv[0].TrimStart('$'), 16), value: (byte)int.Parse(kv[1]));
}).ToList();
while (true)
{
    foreach (var p in presses.Where(p => p.frame == frame))
        machine.QueueKeys(p.dur, p.keys);
    foreach (var pk in pokes.Where(pk => pk.frame == frame))
        machine.Memory.Poke(pk.address, pk.value);
    machine.RunFrame(null);
    frame++;
    int got = machine.DrainAudio(frameAudio);
    if (wavPath != null)
        wav.AddRange(frameAudio.AsSpan(0, got).ToArray());
    if (zx != null && zx.Tape.IsPlaying && zx.Tape.CurrentBlock != lastBlock)
    {
        lastBlock = zx.Tape.CurrentBlock;
        Console.WriteLine($"  cuadro {zx.FrameCount}: cinta en bloque {lastBlock}");
    }
    if (shotEvery > 0 && frame % shotEvery == 0)
        PngWriter.Write($"{shotPrefix}_{frame:D6}.png", machine.Video, 2);
    if (untilTapeEnd)
    {
        if (zx!.Tape.AtEnd || (!zx.Tape.IsPlaying && frame > 200 && lastBlock >= 0))
        {
            if (--extraAfterTape <= 0)
                break;
        }
        if (frame > 200_000)
            break;
    }
    else if (frame >= frames)
        break;
}
sw.Stop();
double emulated = machine.FrameCount / machine.FramesPerSecond;
Console.WriteLine($"Emulados {machine.FrameCount} cuadros = {emulated:F1} s en {sw.Elapsed.TotalSeconds:F2} s reales ({emulated / Math.Max(sw.Elapsed.TotalSeconds, 0.001):F0}x)");
Console.WriteLine($"PC={machine.Cpu.ProgramCounter:X4} SP={machine.Cpu.StackPointer:X4}" +
                  (zx != null ? $" cinta: {(zx.Tape.IsPlaying ? "reproduciendo" : zx.Tape.AtEnd ? "terminada" : "detenida")}" : ""));

if (machine is Retro.Systems.C64.C64 c64m && c64m.KernalInternalCalls.Count > 0)
    Console.WriteLine("Saltos del programa a rutinas internas del KERNAL: " + string.Join(", ", c64m.KernalInternalCalls.Select(a => $"${a:X4}")));
if (Opt("text") != null && machine is Retro.Systems.C64.C64 c64t)
    Console.Write(c64t.ScreenText());
if (opts.TryGetValue("disasm", out var disasmList))
    foreach (var d in disasmList)
    {
        // --disasm <hex>:<n>: n instructions from the address, as the memory is now
        var parts = d.Split(':');
        uint at = Convert.ToUInt32(parts[0].TrimStart('$'), 16);
        int count = parts.Length > 1 ? int.Parse(parts[1]) : 16;
        for (int i = 0; i < count; i++)
        {
            var ins = machine.Disassembler.Disassemble(a => machine.Memory.Peek(a), at);
            Console.WriteLine($"{at:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2"))),-9} {ins.Text}");
            at += (uint)Math.Max(1, ins.Bytes.Length);
        }
    }
if (opts.TryGetValue("dump", out var dumpList))
    foreach (var d in dumpList)
    {
        // --dump <hex>:<n>: n bytes from the address
        var parts = d.Split(':');
        uint at = Convert.ToUInt32(parts[0].TrimStart('$'), 16);
        int count = parts.Length > 1 ? int.Parse(parts[1]) : 64;
        for (int i = 0; i < count; i += 16)
            Console.WriteLine($"{at + i:X4}  " + string.Join(" ", Enumerable.Range(i, Math.Min(16, count - i)).Select(k => machine.Memory.Peek((uint)(at + k)).ToString("X2"))));
    }
if (Opt("screenshot") is { } shot)
{
    PngWriter.Write(shot, machine.Video, 2);
    Console.WriteLine("Captura: " + shot);
}
if (wavPath != null)
{
    WriteWav(wavPath, wav, 44100);
    Console.WriteLine($"Audio: {wavPath} ({wav.Count / 44100.0:F1} s)");
}
return 0;

// Compatibility matrix: runs the scripts of emu/compat/juegos against the games folder and
// writes the results (JSON with evidence) and the Markdown table.
static int RunCompat(string scriptsPath, string gamesDir, string? jsonOut, string? mdOut, string shotsDir, string? only,
                     bool discover, bool twice, int parallel)
{
    string firmware = Path.GetDirectoryName(FindRom("48.rom"))!;
    if (!Directory.Exists(gamesDir))
    {
        Console.Error.WriteLine($"No existe la carpeta de juegos {gamesDir} (--juegos).");
        return 2;
    }
    if (discover)
    {
        var created = Retro.Compat.CompatDiscovery.Discover(gamesDir, scriptsPath);
        Console.WriteLine($"Guiones nuevos: {created.Count}");
        foreach (var s in created)
            Console.WriteLine($"  {s.Id}: {s.Machine} {s.File}");
    }
    var scripts = Retro.Compat.CompatScript.LoadAll(scriptsPath);
    if (only != null)
    {
        var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries);
        scripts = scripts.Where(s => wanted.Any(w => s.Id.StartsWith(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }
    Console.WriteLine($"Probando {scripts.Count} guiones con {gamesDir} (firmware de {firmware})...");
    var results = new System.Collections.Concurrent.ConcurrentBag<Retro.Compat.CompatResult>();
    var sw = Stopwatch.StartNew();
    Parallel.ForEach(scripts, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, s =>
    {
        var runner = new Retro.Compat.CompatRunner(gamesDir, firmware, shotsDir);
        var r = runner.Run(s);
        if (twice && r.ScreenHash != null)
        {
            var again = new Retro.Compat.CompatRunner(gamesDir, firmware, null).Run(s);
            if (again.ScreenHash != r.ScreenHash)
                r.Warnings.Add($"no determinista: la segunda corrida dio otra imagen final ({again.ScreenHash} contra {r.ScreenHash})");
            else
                r.Warnings.Add("determinista: dos corridas dieron la misma imagen final");
        }
        results.Add(r);
        Console.WriteLine($"  {r.Id,-48} {(r.Highest == "" ? "ninguno" : Retro.Compat.CompatLevels.Spanish(r.Highest))}");
    });
    var report = jsonOut != null && File.Exists(jsonOut) && only != null ? Retro.Compat.CompatReport.LoadJson(jsonOut) : new Retro.Compat.CompatReport();
    report.Merge(results);
    report.Generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
    report.Commit = GitHead();
    report.GamesFolder = gamesDir.Replace('\\', '/');
    if (jsonOut != null)
    {
        report.SaveJson(jsonOut);
        Console.WriteLine("Resultados: " + jsonOut);
    }
    if (mdOut != null)
    {
        File.WriteAllText(mdOut, report.ToMarkdown());
        Console.WriteLine("Tabla: " + mdOut);
    }
    Console.WriteLine($"Listo en {sw.Elapsed.TotalSeconds:F0} s.");
    foreach (var g in results.GroupBy(r => r.Machine).OrderBy(g => g.Key))
        Console.WriteLine($"  {g.Key}: {g.Count()} archivos; " + string.Join(", ",
            Retro.Compat.CompatLevels.All.Select(l => $"{Retro.Compat.CompatLevels.Spanish(l)} {g.Count(r => r.Levels[l].Status == "ok")}")));
    return 0;
}

// Reverse engineering lab: plays a compatibility script to reach the game, then looks
// for lives, timers, positions and entity tables by experiment, plays the script's
// experiments (score, level...), verifies the best hypotheses by writing test values and
// writes the findings with evidence.
static int RunLab(string scriptPath, string gamesDir, string? jsonOut, int idleFrames, bool verify, List<string> givenHypotheses)
{
    var script = Retro.Compat.CompatScript.Load(scriptPath);
    var runner = new Retro.Compat.CompatRunner(gamesDir, Path.GetDirectoryName(FindRom("48.rom"))!, null);
    var (m, sha, problem) = runner.Start(script);
    if (m == null)
    {
        Console.Error.WriteLine($"No se pudo llegar al juego: {problem}");
        return 3;
    }
    Console.WriteLine($"{script.Game} ({script.Machine}), estado al final del guion {script.Id}; observando {idleFrames} cuadros...");
    var sw = Stopwatch.StartNew();
    var report = Retro.Compat.MemoryLab.Analyze(m, script.Machine, new Retro.Compat.MemoryLab.Options(IdleFrames: idleFrames));
    report.Game = script.Game;
    report.Script = script.Id;
    report.Sha256 = sha;
    Console.WriteLine($"Observación lista en {sw.Elapsed.TotalSeconds:F1} s. {report.Method}");
    var replays = new Dictionary<Retro.Compat.LabFinding, Retro.Compat.LabExperiment>();
    foreach (var e in script.Experiments ?? new())
    {
        var found = Retro.Compat.LabExperiments.Analyze(m, script.Machine, e);
        foreach (var f in found)
            replays[f] = e;
        report.Findings.AddRange(found);
        report.Method += $" Experimento {e.Kind} \"{e.Description}\": {e.Frames} cuadros con sus entradas contra los mismos sin tocar nada.";
        Console.WriteLine($"Experimento {e.Kind} \"{e.Description}\": {found.Count} candidato(s).");
    }
    var given = new List<Retro.Compat.LabFinding>();
    foreach (var h in givenHypotheses)
    {
        // kind:address[:encoding:bytes], e.g. score:20F8:bcd:2
        var p = h.Split(':');
        var f = new Retro.Compat.LabFinding
        {
            Kind = p[0], Address = p[1].TrimStart('$').ToUpperInvariant(), Encoding = p.Length > 2 ? p[2] : "uint8",
            Bytes = p.Length > 3 ? int.Parse(p[3]) : 1, Confidence = 0.5,
            Hypothesis = $"Hipótesis dada: {p[0]} en ${p[1].TrimStart('$')}.",
        };
        var experiment = script.Experiments?.FirstOrDefault(e => e.Kind == f.Kind);
        if (experiment != null)
            replays[f] = experiment;
        given.Add(f);
        report.Findings.Add(f);
    }
    if (verify)
    {
        sw.Restart();
        foreach (var f in given)
            Retro.Compat.LabVerifier.Verify(m, f, replays.GetValueOrDefault(f));
        Retro.Compat.LabVerifier.VerifyBest(m, report, replays);
        Console.WriteLine($"Verificación lista en {sw.Elapsed.TotalSeconds:F1} s.");
    }
    foreach (var f in report.Findings.Take(30))
    {
        string where = f.Kind == "entity-table" ? $"${f.Address} x{f.Count} de {f.Stride} bytes" : f.Bytes > 1 ? $"${f.Address} {f.Encoding} {f.Bytes}" : $"${f.Address}";
        Console.WriteLine($"  {where} {f.Kind,-12} {Retro.Compat.LabReportText.Status(f.Status),-12} confianza {f.ConfidenceLabel} ({f.Confidence:F2}) {f.Hypothesis}");
        foreach (var e in f.Evidence)
            Console.WriteLine($"      evidencia: {e}");
        foreach (var field in f.Fields ?? new())
            Console.WriteLine($"      campo {field}");
        if (f.Writers.Count > 0)
            Console.WriteLine($"      lo escriben: {string.Join(", ", f.Writers)}");
        if (f.Verification != null)
        {
            Console.WriteLine($"      experimento: {f.Verification.Experiment}");
            foreach (var o in f.Verification.Observations)
                Console.WriteLine($"        - {o}");
        }
    }
    foreach (var n in report.Notes)
        Console.WriteLine("  nota: " + n);
    if (jsonOut != null)
    {
        report.Save(jsonOut);
        Console.WriteLine("Hallazgos: " + jsonOut);
    }
    return 0;
}

// Every game of a folder this emulator can run, with the profile id recipes use.
static int ListGames(string folder)
{
    var games = Retro.Compat.GameCatalog.Scan(folder);
    if (games.Count == 0)
    {
        Console.Error.WriteLine($"No hay juegos que este emulador pueda correr en {folder}.");
        return 2;
    }
    Console.WriteLine($"{games.Count} juegos en {folder} (el id es el que va en \"profile\" de una receta):");
    foreach (var g in games.OrderBy(g => g.Profile.Machine).ThenBy(g => g.Profile.Name))
        Console.WriteLine($"  {g.Profile.Id,-34} {g.Profile.Machine,-9} {(g.Prepared ? "preparado" : "sin preparar"),-12} " +
                          $"{(g.Profile.Variables.Count > 0 ? string.Join(", ", g.Profile.Variables.Keys) : "sin variables")}  ({Path.GetRelativePath(folder, g.Path)})");
    Console.WriteLine("Los que no están preparados se fusionan con controles compartidos y cualquier vista; para reglas: retro-cli preparar <juego>.");
    return 0;
}

// Prepares any game for the fusions: reaches the game in progress (key trial, then the
// user's AI), finds and verifies its variables with the lab and saves a profile tied to
// the SHA-256 of the copy.
static async Task<int> RunPrepare(string gamePath, string? ia, string? profilesFolder, int rounds, bool save, string? agentFolder)
{
    var settings = Retro.Ai.AiSettings.Load();
    if (ia != null && ia != "ninguna")
        settings.Provider = ia;
    var provider = ia == "ninguna" ? null : settings.CreateProvider();
    Retro.Compat.IPlayAdvisor? advisor = provider != null ? new Retro.Ai.AiPlayAdvisor(provider)
        : settings.Provider == "agent" && ia != "ninguna"
            ? new Retro.Ai.FolderPlayAdvisor(agentFolder ?? Path.Combine(Path.GetTempPath(), "RetroEmu-preparar", Path.GetFileNameWithoutExtension(gamePath)))
            : null;
    var options = new Retro.Compat.PrepareOptions
    {
        FirmwareDirectory = Path.GetDirectoryName(FindRom("48.rom"))!,
        Advisor = advisor,
        MaxRounds = rounds,
        ProfilesFolder = profilesFolder,
        Save = save,
    };
    Console.WriteLine($"Preparando {Path.GetFileName(gamePath)} para las fusiones" +
                      (advisor != null ? $" (prueba de teclas y después {advisor.Name})." : " (solo prueba de teclas, sin IA)."));
    var sw = Stopwatch.StartNew();
    var r = await Retro.Compat.GamePreparer.PrepareAsync(gamePath, options, line => Console.WriteLine("  " + line), CancellationToken.None);
    Console.WriteLine($"({sw.Elapsed.TotalSeconds:F0} s)");
    if (r.Lab != null)
        foreach (var f in r.Lab.Findings.Where(f => f.Status == "verified" || f.Confidence >= 0.45).Take(14))
        {
            string where = f.Kind == "entity-table" ? $"${f.Address} x{f.Count} de {f.Stride} bytes" : f.Bytes > 1 ? $"${f.Address} {f.Encoding} {f.Bytes}" : $"${f.Address}";
            Console.WriteLine($"  {where,-22} {f.Kind,-12} {Retro.Compat.LabReportText.Status(f.Status),-11} {f.Hypothesis}");
        }
    if (r.Profile != null)
    {
        Console.WriteLine($"Perfil {r.Profile.Id}: arranque {string.Join(", ", r.Profile.Boot)}");
        foreach (var (name, v) in r.Profile.Variables)
            Console.WriteLine($"  {name} = ${v.Address} {v.Encoding}{(v.Bytes > 1 ? " " + v.Bytes : "")}{(v.ReadOnly ? " (solo lectura)" : "")}");
        Console.WriteLine("  controles: " + string.Join(", ", r.Profile.Controls.Select(c => $"{c.Key} = {c.Value}")));
    }
    if (!r.Ok)
    {
        Console.Error.WriteLine("No se pudo preparar: " + r.Problem);
        return 6;
    }
    Console.WriteLine(r.ProfilePath != null ? "Listo: " + r.ProfilePath : "Listo (sin guardar).");
    return 0;
}

static string GitHead()
{
    try
    {
        var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false });
        string head = p!.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        var d = Process.Start(new ProcessStartInfo("git", "status --porcelain --untracked-files=no") { RedirectStandardOutput = true, UseShellExecute = false });
        bool dirty = d!.StandardOutput.ReadToEnd().Trim().Length > 0;
        d.WaitForExit();
        return head + (dirty ? "+cambios" : "");
    }
    catch (Exception)
    {
        return "?";
    }
}

static Dictionary<string, Retro.Fusion.GameSource> FindGameSources(string gamesDir)
{
    var sources = Retro.Compat.GameCatalog.Scan(gamesDir).ToDictionary(g => g.Profile.Id, g => g.Source);
    return sources;
}

// Tries a fusion game the way the app does before showing it (robot player, script errors,
// blank or frozen picture) and prints the report: agents use it to check their juego.js.
static int RunStudio(string gameArg, string gamesDir, string? secondsOpt, string? shotsPrefix)
{
    Retro.Studio.StudioGame? game;
    if (gameArg.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && File.Exists(gameArg))
    {
        string meta = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameArg))!, Retro.Studio.StudioWorkspace.GameFile);
        if (!File.Exists(meta))
        {
            Console.Error.WriteLine($"Falta {Retro.Studio.StudioWorkspace.GameFile} junto a {gameArg} (dice qué juegos usa).");
            return 2;
        }
        game = Retro.Studio.StudioGame.Parse(File.ReadAllText(meta));
        game.Script = File.ReadAllText(gameArg);
    }
    else if (File.Exists(gameArg))
        game = Retro.Studio.StudioGame.Parse(File.ReadAllText(gameArg));
    else
        game = Retro.Studio.StudioLibrary.AllVersions().Where(g => g.Id == gameArg).OrderByDescending(g => g.Version).FirstOrDefault();
    if (game == null)
    {
        Console.Error.WriteLine("Juego no encontrado. Disponibles: " + string.Join(", ", Retro.Studio.StudioLibrary.AllVersions().Select(g => g.Id).Distinct()));
        return 2;
    }
    var sources = FindGameSources(gamesDir);
    var missing = game.Games.Where(g => !sources.ContainsKey(g.Profile)).Select(g => g.Profile).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"Faltan juegos en {gamesDir}: {string.Join(", ", missing)}");
        return 4;
    }
    Console.WriteLine($"Juego fusionado: {game.Title} (versión {game.Version})");
    var sw = Stopwatch.StartNew();
    var session = Retro.Studio.StudioSession.Start(game, sources, Path.GetDirectoryName(FindRom("48.rom"))!);
    Console.WriteLine($"Juegos listos en {sw.Elapsed.TotalSeconds:F1} s. Probando con tres jugadores robot...");
    sw.Restart();
    var report = Retro.Studio.StudioValidator.Run(session, int.Parse(secondsOpt ?? Retro.Studio.StudioValidator.DefaultSecondsPerRobot.ToString()));
    Console.WriteLine($"(prueba en {sw.Elapsed.TotalSeconds:F1} s)");
    Console.WriteLine(report.Summary);
    if (shotsPrefix != null && report.Shots != null)
        foreach (var (label, frame) in report.Shots)
        {
            string file = $"{shotsPrefix}_{label}.png";
            PngWriter.Write(file, frame, 2);
            Console.WriteLine("Captura: " + file);
        }
    Console.WriteLine(report.Ok ? "Resultado: LISTO" : "Resultado: HAY QUE CORREGIR");
    return report.Ok ? 0 : 5;
}

static int RunFusion(string recipeArg, string gamesDir, string? ticksOpt, string? shot, string? shotEvery, string? shotPrefix, List<string>? presses, string? layout)
{
    var recipe = File.Exists(recipeArg)
        ? Retro.Fusion.FusionRecipe.Parse(File.ReadAllText(recipeArg))
        : Retro.Fusion.RecipeLibrary.All().FirstOrDefault(r => r.Id == recipeArg);
    if (recipe == null)
    {
        Console.Error.WriteLine("Receta no encontrada. Disponibles: " + string.Join(", ", Retro.Fusion.RecipeLibrary.All().Select(r => r.Id)));
        return 2;
    }
    if (layout != null)
        recipe.Layout.Mode = layout;
    // the folder first: games nobody prepared get their basic profile, which recipes can name
    var sources = FindGameSources(gamesDir);
    var errors = recipe.Validate(Retro.Fusion.ProfileLibrary.Find);
    if (errors.Count > 0)
    {
        Console.Error.WriteLine("Receta inválida:\n  " + string.Join("\n  ", errors));
        return 3;
    }
    var missing = recipe.Games.Where(g => !sources.ContainsKey(g.Profile)).Select(g => g.Profile).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"Faltan juegos en {gamesDir}: {string.Join(", ", missing)}");
        return 4;
    }
    Console.WriteLine($"Fusión: {recipe.Name} — {recipe.Description}");
    var sw = Stopwatch.StartNew();
    var session = Retro.Fusion.FusionSession.Start(recipe, sources, Path.GetDirectoryName(FindRom("48.rom"))!, s => Console.WriteLine("  arranque: " + s));
    Console.WriteLine($"Juegos listos en {sw.Elapsed.TotalSeconds:F1} s");
    int ticks = int.Parse(ticksOpt ?? "600");
    int every = int.Parse(shotEvery ?? "0");
    var scheduled = (presses ?? new()).Select(p => p.Split(':')).Select(p => (tick: int.Parse(p[0]), input: p[1], dur: int.Parse(p[2]))).ToList();
    var active = new List<(string input, int until)>();
    int printed = 0;
    for (int t = 0; t < ticks; t++)
    {
        foreach (var p in scheduled.Where(p => p.tick == t))
        {
            session.SetInput(p.input, true);
            active.Add((p.input, t + p.dur));
        }
        foreach (var a in active.Where(a => a.until == t).ToList())
        {
            session.SetInput(a.input, false);
            active.Remove(a);
        }
        session.Update(1.0 / 60);
        for (; printed < session.Events.Count; printed++)
            Console.WriteLine($"  tick {session.Events[printed].Tick}: {session.Events[printed].Text}");
        if (every > 0 && t % every == 0)
            PngWriter.Write($"{shotPrefix ?? "fusion"}_{t:D5}.png", session.Video, 2);
    }
    Console.WriteLine(string.Join("  ", session.Variables().Select(v => $"{v.slot}.{v.variable}={v.value}")));
    if (shot != null)
    {
        PngWriter.Write(shot, session.Video, 2);
        Console.WriteLine("Captura: " + shot);
    }
    return 0;
}

static void WriteWav(string path, List<short> samples, int rate)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write("RIFF"u8.ToArray());
    w.Write(36 + samples.Count * 2);
    w.Write("WAVEfmt "u8.ToArray());
    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8.ToArray());
    w.Write(samples.Count * 2);
    foreach (var s in samples)
        w.Write(s);
}

static string FindRom(string name)
{
    var env = Environment.GetEnvironmentVariable("RETRO_ROMS");
    if (env != null && File.Exists(Path.Combine(env, name)))
        return Path.Combine(env, name);
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        foreach (var folder in new[] { "firmware", "roms" })
        {
            var p = Path.Combine(dir.FullName, folder, name);
            if (File.Exists(p))
                return p;
        }
        dir = dir.Parent;
    }
    throw new FileNotFoundException($"No encuentro roms/{name}. Pasá --rom <ruta>.");
}
