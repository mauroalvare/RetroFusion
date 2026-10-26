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
          retro-cli cpu8088 verificar --suite <carpeta-v2> [--salida informe.json]
          retro-cli cpu8088 <programa.bin|demo|irq-demo|timer-demo> [--steps n]   diagnóstico del núcleo; todavía sin PC/BIOS/DOS
          retro-cli run <archivo> [opciones]
          retro-cli studio <juego.js|juego.json|id> --games <carpeta> [--seconds n] [--shots prefijo]
          retro-cli compat <carpeta-guiones|guion.json> --juegos <carpeta> [--salida r.json] [--md tabla.md]
                    [--capturas carpeta] [--solo id1,id2] [--descubrir] [--dos-veces] [--paralelo n]
          retro-cli lab <guion.json> [--juegos carpeta] [--cuadros n] [--salida hallazgos.json]
                    [--hipotesis tipo:dirección[:codificación:bytes]]... [--sin-verificar]
          retro-cli juegos <carpeta>   todos los juegos que se pueden fusionar, con el id de su perfil
          retro-cli asm <fuente.s> [--cpu 6502|z80] [--salida x.prg|x.bin|x.tap] [--lista]   ensamblador
          retro-cli crear <c64|zx48> [juego.s] [--ia sub-claude|sub-codex|sub-google|claude|ollama...]
                    [--pedido "..."] [--intentos n]
                    sin --ia: empieza un juego desde la plantilla de la máquina (arranca, dibuja, lee los
                    controles y suena). Con --ia: la IA escribe un juego original, se prueba en el
                    emulador con un jugador robot y lo corrige hasta que pasa. Deja el .s, el .prg o .tap y una captura.
                    Con --ia agent: arma una carpeta (la del argumento, o una nueva) para un agente que escribe juego.s
                    y lo prueba con retro-cli probar
          Con --ia sub-claude, sub-codex o sub-google, --modelo <nombre> elige el modelo de esa herramienta
          (si no, el guardado en la app o el que usa la herramienta).
          retro-cli probar <juego.s> [--maquina c64|zx48]   prueba un juego escrito a mano como lo prueba la IA
          retro-cli ideas <perfil1,perfil2> [--ia ...] [--pedido "..."] [--juegos carpeta]   3 ideas de fusión del estudio
          retro-cli preparar <juego> [--ia sub-claude|sub-codex|sub-google|claude|ollama|agent|ninguna]
                    [--carpeta carpeta-del-agente] [--rondas n] [--perfiles carpeta] [--sin-guardar]
                    prepara un juego para las fusiones: llega a la partida (prueba de teclas y la IA
                    elegida en la app), encuentra y verifica sus variables y guarda un perfil por hash

        Máquinas: ZX Spectrum 48K (.tap .tzx .z80 .sna), Space Invaders (invaders.zip de MAME),
        Commodore 64 (.t64 .prg), NES (.nes mappers 0/1/2/3/4/7) y PC/XT (.img .ima).

        Opciones de run:
          --rom <ruta>             ROM del Spectrum 48K (por defecto roms/48.rom)
          --bios <ruta>            BIOS PC/XT opcional; se usa el libre incluido por defecto
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
if (Opt("modelo") is { } cliModel)
    Environment.SetEnvironmentVariable("RETROEMU_MODELO", cliModel);   // the model of the subscription tool, for this run

if (command == "cpu8088")
    return mediaPath == "verificar" ? I8088Verification.Run(Opt("suite") ?? "", Opt("salida")) : I8088Diagnostic.Run(mediaPath, Opt("steps"));

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

if (command == "asm")
    return Assemble(mediaPath, Opt("salida"), Opt("lista") != null, Opt("cpu") ?? "6502");

if (command == "crear")
{
    string? source = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null;
    return Opt("ia") != null
        ? await WriteGame(mediaPath, source, Opt("ia")!, Opt("pedido") ?? "", int.Parse(Opt("intentos") ?? "0"))
        : CreateGame(mediaPath, source);
}

if (command == "probar")
    return TryGame(mediaPath, Opt("maquina"));

if (command == "ideas")
    return await RunIdeas(mediaPath, Opt("ia"), Opt("pedido") ?? "", Opt("juegos"));

if (command == "preparar")
    return await RunPrepare(mediaPath, Opt("ia"), Opt("perfiles"), int.Parse(Opt("rondas") ?? "24"), Opt("sin-guardar") == null, Opt("carpeta"));

if (command == "fuse")
{
    Console.Error.WriteLine("La fusión clásica se retiró. Usá retro-cli studio para probar juegos nuevos con escena propia.");
    return 2;
}

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
            : isInvaders ? "chip de Space Invaders" : Retro.Machines.MachineCatalog.Identify(c.f).FirstOrDefault() is { } candidate
                ? candidate.Machine.Name + ": " + candidate.Reason + (Retro.Machines.MachineCatalog.NotLoadableYet(candidate) is { } why ? "; " + why : "")
                : "no reconocido por las máquinas disponibles";
        Console.WriteLine($"[{c.i}] {Retro.Machines.PcDos.DisplayPath(c.f)} ({c.f.Data.Length} bytes) -> {what}");
    }
    return 0;
}
if (command != "run")
    return Usage();

IReadOnlyList<MediaFile> selectedFiles = files;
if (Opt("pick") is { } requestedPick)
{
    if (!int.TryParse(requestedPick, out int index) || index < 0 || index >= files.Count)
    {
        Console.Error.WriteLine("Índice --pick inválido. Usá info para ver los archivos disponibles.");
        return 2;
    }
    selectedFiles = new[] { files[index] };
    isInvaders = false;
}

IMachine machine;
Spectrum48? zx = null;
bool isTape = false;
var c64Media = selectedFiles.Where(f => Retro.Systems.C64.C64Media.Probe(f) > 0).OrderBy(f => MediaArchive.DumpScore(f.Name)).FirstOrDefault();
var nesMedia = selectedFiles.Where(f => Retro.Systems.Nes.NesCartridge.IsINes(f.Data)).OrderBy(f => MediaArchive.DumpScore(f.Name)).FirstOrDefault();
var pcMedia = selectedFiles.SelectMany(Retro.Machines.MachineCatalog.Identify).Where(c => c.Machine.Id == "pcxt" && c.Confidence >= 50).OrderBy(c => Retro.Machines.PcDos.ProgramPriority(c.File.Name)).ThenByDescending(c => c.Confidence).FirstOrDefault();
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
else if (pcMedia != null && !isInvaders)
{
    try
    {
        if (Retro.Machines.MachineCatalog.NotLoadableYet(pcMedia) is { } problem) throw new InvalidDataException(problem);
        string bios = Opt("bios") ?? FindPcBios();
        bool program = pcMedia.File.Extension is ".exe" or ".com";
        byte[] disk = program ? Retro.Machines.PcDos.Prepare(files, pcMedia.File) : pcMedia.File.Data;
        machine = new Retro.Systems.Pc.PcXt(File.ReadAllBytes(bios), disk);
        Console.WriteLine($"Máquina: {machine.DisplayName}; CGA, teclado y altavoz");
        Console.WriteLine($"{(program ? "Programa con FreeDOS integrado" : "Disquete")}: {Retro.Machines.PcDos.DisplayPath(pcMedia.File)}; BIOS: {bios}");
    }
    catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
    { Console.Error.WriteLine("No se pudo cargar PC/XT: " + e.Message); return 3; }
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
    var candidates = selectedFiles.Select((f, i) => (f, i, score: SpectrumMedia.Probe(f)))
        .OrderByDescending(c => c.score).ThenBy(c => MediaArchive.DumpScore(c.f.Name)).ToList();
    MediaFile media;
    if (candidates.Count > 0 && candidates[0].score > 0)
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

// Asks the chosen AI for 3 fusion ideas, as the studio does (to compare AIs and see the variety).
static async Task<int> RunIdeas(string profileList, string? ia, string wish, string? gamesDir)
{
    if (gamesDir != null)
        Retro.Compat.GameCatalog.Scan(gamesDir);   // basic profiles of games nobody prepared
    var settings = Retro.Ai.AiSettings.Load();
    if (ia != null)
        settings.Provider = ia;
    var provider = settings.CreateProvider();
    if (provider == null)
    {
        Console.Error.WriteLine("Esa IA no se puede llamar desde acá (copiar y pegar o carpeta de agentes).");
        return 2;
    }
    var slots = profileList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select((id, i) => new Retro.Fusion.RecipeGame { Slot = ((char)('a' + i)).ToString(), Profile = id }).ToList();
    var missing = slots.Where(s => Retro.Fusion.ProfileLibrary.Find(s.Profile) == null).Select(s => s.Profile).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine("No conozco estos perfiles: " + string.Join(", ", missing) + " (retro-cli juegos <carpeta> los lista; --juegos <carpeta> suma los no preparados).");
        return 2;
    }
    Console.WriteLine($"Pidiendo 3 ideas a {provider.Name} para {string.Join(" + ", slots.Select(s => s.Profile))}" + (wish.Length > 0 ? $", con el pedido \"{wish}\"" : ", sin pedido") + "...");
    var operation = settings.Operation(provider, 1);
    ShowEstimate(new(Retro.Studio.StudioDesigner.ConceptGuide,
        [new(true, Retro.Studio.StudioDesigner.ConceptPrompt(slots, Retro.Fusion.ProfileLibrary.Find, wish))], null));
    var sw = Stopwatch.StartNew();
    var ideas = await new Retro.Studio.StudioDesigner().ConceptsAsync(operation, slots, Retro.Fusion.ProfileLibrary.Find, wish, CancellationToken.None);
    ShowUsage(operation);
    Console.WriteLine($"({sw.Elapsed.TotalSeconds:F0} s)");
    foreach (var idea in ideas)
    {
        Console.WriteLine();
        Console.WriteLine($"## {idea.Title} — {idea.Approach}");
        Console.WriteLine(idea.Pitch);
        Console.WriteLine("Cómo se juega: " + idea.HowToPlay);
        Console.WriteLine(idea.Design);
    }
    return 0;
}

// Assembles a source file: a .prg (load address first, for the C64) or a raw binary.
static int Assemble(string sourcePath, string? output, bool listing, string cpu)
{
    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"No existe {sourcePath}.");
        return 2;
    }
    var r = cpu.ToLowerInvariant() switch
    {
        "z80" => Retro.Asm.AsmZ80.Assemble(File.ReadAllText(sourcePath)),
        _ => Retro.Asm.Asm6502.Assemble(File.ReadAllText(sourcePath)),
    };
    foreach (var e in r.Errors)
        Console.Error.WriteLine($"{Path.GetFileName(sourcePath)}: {e}");
    if (!r.Ok)
        return 3;
    if (listing)
        foreach (var l in r.Listing)
            Console.WriteLine(l);
    output ??= Path.ChangeExtension(sourcePath, cpu.Equals("z80", StringComparison.OrdinalIgnoreCase) ? ".bin" : ".prg");
    var bytes = Path.GetExtension(output).ToLowerInvariant() switch
    {
        ".prg" => new[] { (byte)r.Origin, (byte)(r.Origin >> 8) }.Concat(r.Bytes).ToArray(),
        // a Spectrum tape with a BASIC loader that runs the code by itself
        ".tap" => Retro.Asm.SpectrumTap.Build(r.Bytes, r.Origin, Path.GetFileNameWithoutExtension(output)),
        _ => r.Bytes,
    };
    File.WriteAllBytes(output, bytes);
    Console.WriteLine($"{r.Bytes.Length} bytes desde ${r.Origin:X4} ({r.Symbols.Count} símbolos) -> {output}");
    return 0;
}

// A new game from the template of a machine: writes the source and leaves it assembled.
static int CreateGame(string machine, string? sourcePath)
{
    var t = Retro.Asm.Templates.For(machine);
    if (t == null)
    {
        Console.Error.WriteLine($"No hay plantilla para {machine}. Máquinas: {string.Join(", ", Retro.Asm.Templates.All.Select(x => x.Machine))}.");
        return 2;
    }
    sourcePath ??= "juego.s";
    if (File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"{sourcePath} ya existe: elegí otro nombre para no pisarlo.");
        return 2;
    }
    File.WriteAllText(sourcePath, t.Source);
    var (r, file) = Retro.Asm.Templates.Build(t.Source, t.Machine);
    foreach (var e in r.Errors)
        Console.Error.WriteLine($"{Path.GetFileName(sourcePath)}: {e}");
    if (file == null)
        return 3;
    string output = Path.ChangeExtension(sourcePath, t.Output);
    File.WriteAllBytes(output, file);
    Console.WriteLine($"{t.Name}: {sourcePath} ({r.Bytes.Length} bytes desde ${r.Origin:X4}) -> {output}");
    Console.WriteLine($"Probalo:  retro-cli run {output} --frames 300 --screenshot juego.png{(t.Machine == "zx48" ? " --autoload --until-tape-end" : "")}");
    Console.WriteLine($"Después de editarlo:  retro-cli asm {sourcePath} --cpu {t.Cpu} --salida {output}");
    return 0;
}

static void ShowEstimate(Retro.Ai.AiRequest request)
{
    var estimate = Retro.Ai.AiEstimate.For(request);
    Console.WriteLine($"Entrada orientativa: {estimate.Characters:N0} caracteres, {estimate.LowTokens:N0} a {estimate.HighTokens:N0} tokens de texto. No incluye imágenes, salida ni contexto propio del CLI.");
}

static void ShowUsage(Retro.Ai.AiOperation operation)
{
    var stats = operation.Stats;
    Console.WriteLine($"Pedidos: {stats.Calls}; texto enviado: {stats.TextCharacters:N0} caracteres.");
    Console.WriteLine(stats.CallsWithUsage > 0
        ? $"Tokens informados{(stats.CallsWithUsage < stats.Calls ? " (parcial)" : "")}: {stats.InputTokens:N0} entrada, {stats.OutputTokens:N0} salida."
        : "La herramienta no informó consumo de tokens.");
}

// An AI writes an original game for a machine, tries it in the emulator and fixes it until it passes.
static async Task<int> WriteGame(string machine, string? sourcePath, string ia, string wish, int attempts)
{
    var t = Retro.Asm.Templates.For(machine);
    if (t == null)
    {
        Console.Error.WriteLine($"No hay plantilla para {machine}. Máquinas: {string.Join(", ", Retro.Asm.Templates.All.Select(x => x.Machine))}.");
        return 2;
    }
    if (ia == "agent")
    {
        // a folder for a coding agent: the task, the guide and juego.s; it tries the game with "probar"
        string cli = Environment.ProcessPath ?? "retro-cli";
        string dir = Retro.Ai.GameWorkspace.Create(t.Machine, wish, Random.Shared, $"\"{cli}\" probar {Retro.Ai.GameWorkspace.SourceFile}",
            sourcePath != null ? Path.GetFullPath(sourcePath) : null);
        Console.WriteLine($"Carpeta para el agente: {dir}");
        Console.WriteLine($"Decile a tu agente: \"Trabajá en la carpeta {dir}: leé TAREA.md y seguí sus instrucciones.\"");
        return 0;
    }
    sourcePath ??= "juego.s";
    if (File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"{sourcePath} ya existe: elegí otro nombre para no pisarlo.");
        return 2;
    }
    var settings = Retro.Ai.AiSettings.Load();
    settings.Provider = ia;
    var provider = settings.CreateProvider();
    if (provider == null)
    {
        Console.Error.WriteLine("Esa IA no se puede llamar desde acá (copiar y pegar o carpeta de agentes).");
        return 2;
    }
    Console.WriteLine($"{provider.Name} escribe un juego para {t.Name}" + (wish.Length > 0 ? $" con el pedido \"{wish}\"" : " sin pedido") + "...");
    attempts = attempts > 0 ? attempts : settings.DesignAttempts;
    var operation = settings.Operation(provider, attempts);
    ShowEstimate(new(Retro.Ai.GameWriter.Guide(t), [new(true, Retro.Ai.GameWriter.FirstPrompt(t, wish, new Random(0)))], null));
    var sw = Stopwatch.StartNew();
    var outcome = await new Retro.Ai.GameWriter { MaxAttempts = attempts }.WriteAsync(operation, t.Machine, wish,
        Path.GetDirectoryName(FindRom("48.rom"))!, CancellationToken.None, line => Console.WriteLine("  " + line));
    ShowUsage(operation);
    Console.WriteLine($"({sw.Elapsed.TotalSeconds:F0} s, {outcome.Attempts} intento{(outcome.Attempts == 1 ? "" : "s")})");
    if (outcome.Game == null)
    {
        Console.Error.WriteLine("No hubo juego: " + outcome.Problem);
        return 6;
    }
    var g = outcome.Game;
    File.WriteAllText(sourcePath, g.Source);
    Console.WriteLine();
    Console.WriteLine($"## {g.Title}");
    Console.WriteLine(g.Pitch);
    Console.WriteLine("Cómo se juega: " + g.HowToPlay);
    if (outcome.Report != null)
    {
        Console.WriteLine();
        Console.WriteLine(outcome.Report.Summary.TrimEnd());
        SaveTrial(outcome.Report, sourcePath, t.Output);
    }
    if (!outcome.Ok)
    {
        Console.Error.WriteLine($"No pasó la prueba en {outcome.Attempts} intentos ({outcome.Problem}). El último intento quedó en {sourcePath}.");
        return 6;
    }
    return 0;
}

// The trial of a game written by hand: the same one an AI goes through.
static int TryGame(string sourcePath, string? machine)
{
    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"No existe {sourcePath}.");
        return 2;
    }
    string source = File.ReadAllText(sourcePath);
    machine ??= System.Text.RegularExpressions.Regex.IsMatch(source, @"^\s*\.?org\s+(\$8000|8000h|32768)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "zx48" : "c64";
    var t = Retro.Asm.Templates.For(machine);
    if (t == null)
    {
        Console.Error.WriteLine($"No hay plantilla para {machine}.");
        return 2;
    }
    var r = Retro.Compat.HomebrewTrial.Run(source, t.Machine, Path.GetDirectoryName(FindRom("48.rom"))!);
    Console.WriteLine($"{t.Name}: {sourcePath}");
    Console.WriteLine(r.Summary.TrimEnd());
    SaveTrial(r, sourcePath, t.Output);
    return r.Ok ? 0 : 6;
}

static void SaveTrial(Retro.Compat.TrialReport r, string sourcePath, string extension)
{
    if (r.File != null)
    {
        string output = Path.ChangeExtension(sourcePath, extension);
        File.WriteAllBytes(output, r.File);
        Console.WriteLine("Programa: " + output);
    }
    if (r.LastPicture != null)
    {
        string png = Path.ChangeExtension(sourcePath, ".png");
        PngWriter.Write(png, r.LastPicture, 2);
        Console.WriteLine("Captura: " + png);
    }
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
    Console.WriteLine("El estudio usa las piezas de estos juegos para una escena propia; para verificar sus variables: retro-cli preparar <juego>.");
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

static string FindPcBios()
{
    try { return FindRom("pcxt/pcxt-bios.rom"); }
    catch (FileNotFoundException)
    {
        try { return FindRom("pcxt-bios.rom"); }
        catch (FileNotFoundException) { return Retro.Systems.Pc.PcFirmware.FindPath(""); }
    }
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
