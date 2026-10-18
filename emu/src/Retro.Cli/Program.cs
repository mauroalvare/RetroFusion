using System.Diagnostics;
using Retro.Core;
using Retro.Systems.Arcade;
using Retro.Systems.Spectrum;

// Headless runner: loads real software into an emulated machine, runs it for a number
// of frames (as fast as possible), presses keys on given frames and saves screenshots
// and audio. Used by automated tests and to check compatibility without a window.

static int Usage()
{
    Console.WriteLine("""
        RetroEmu CLI

          retro-cli info <archivo>
          retro-cli run <archivo> [opciones]
          retro-cli fuse <receta> --games <carpeta> [--ticks n] [--screenshot png]
          retro-cli studio <juego.js|juego.json|id> --games <carpeta> [--seconds n] [--shots prefijo]

        Máquinas: ZX Spectrum 48K (.tap .tzx .z80 .sna) y Space Invaders (invaders.zip de MAME).

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
        Console.WriteLine($"[{c.i}] {c.f.Name} ({c.f.Data.Length} bytes) -> {(score > 0 ? $"ZX Spectrum (confianza {score})" : isInvaders ? "chip de Space Invaders" : "no reconocido por las máquinas disponibles")}");
    }
    return 0;
}
if (command != "run")
    return Usage();

IMachine machine;
Spectrum48? zx = null;
bool isTape = false;
if (isInvaders)
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
while (true)
{
    foreach (var p in presses.Where(p => p.frame == frame))
        machine.QueueKeys(p.dur, p.keys);
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

static Dictionary<string, Retro.Fusion.GameSource> FindGameSources(string gamesDir)
{
    var sources = new Dictionary<string, Retro.Fusion.GameSource>();
    foreach (var zip in Directory.GetFiles(gamesDir).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || SpectrumMedia.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
    {
        List<MediaFile> fs;
        try { fs = MediaArchive.Open(zip); }
        catch (InvalidDataException) { continue; }
        var p = Retro.Fusion.ProfileLibrary.ForFiles(fs);
        if (p != null && !sources.ContainsKey(p.Id))
            sources[p.Id] = new Retro.Fusion.GameSource(p, fs, gamesDir);
    }
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
    var errors = recipe.Validate(Retro.Fusion.ProfileLibrary.Find);
    if (errors.Count > 0)
    {
        Console.Error.WriteLine("Receta inválida:\n  " + string.Join("\n  ", errors));
        return 3;
    }
    var sources = new Dictionary<string, Retro.Fusion.GameSource>();
    foreach (var zip in Directory.GetFiles(gamesDir).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || SpectrumMedia.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
    {
        List<MediaFile> fs;
        try { fs = MediaArchive.Open(zip); }
        catch (InvalidDataException) { continue; }
        var p = Retro.Fusion.ProfileLibrary.ForFiles(fs);
        if (p != null && !sources.ContainsKey(p.Id))
            sources[p.Id] = new Retro.Fusion.GameSource(p, fs, gamesDir);
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
        var p = Path.Combine(dir.FullName, "roms", name);
        if (File.Exists(p))
            return p;
        dir = dir.Parent;
    }
    throw new FileNotFoundException($"No encuentro roms/{name}. Pasá --rom <ruta>.");
}
