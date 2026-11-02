using Retro.Core;

namespace Retro.Cli;

internal static class CliLanguage
{
    public static string[] Normalize(string[] args)
    {
        UiText.Language = UiText.StartupLanguage(args);
        var result = args.ToList();
        int language = result.IndexOf("--language");
        if (language >= 0 && language + 1 < result.Count) result.RemoveRange(language, 2);
        if (result.Count == 0) return [];
        var commands = new Dictionary<string, string> { ["create"]="crear", ["test"]="probar", ["prepare"]="preparar", ["games"]="juegos" };
        result[0] = commands.GetValueOrDefault(result[0], result[0]);
        if (result.Count > 1 && result[0] is "nes" or "cpu8088" or "cpu8086")
            result[1] = result[1] switch { "verify" => "verificar", "verify-bus" => "verificar-bus", _ => result[1] };
        var flags = new Dictionary<string, string>
        {
            ["--ai"]="--ia", ["--request"]="--pedido", ["--attempts"]="--intentos", ["--machine"]="--maquina", ["--model"]="--modelo",
            ["--output"]="--salida", ["--log"]="--registro", ["--cases"]="--casos", ["--rounds"]="--rondas", ["--profiles"]="--perfiles",
            ["--no-save"]="--sin-guardar", ["--no-verify"]="--sin-verificar", ["--hypothesis"]="--hipotesis", ["--folder"]="--carpeta",
            ["--discover"]="--descubrir", ["--twice"]="--dos-veces", ["--parallel"]="--paralelo", ["--only"]="--solo", ["--captures"]="--capturas",
            ["--list"]="--lista", ["--routines"]="--rutinas", ["--routine-steps"]="--pasos-rutinas"
        };
        for (int i = 2; i < result.Count; i++)
        {
            if (result[i] == "none" && result[i - 1] == "--ia") { result[i] = "ninguna"; continue; }
            if (result[i] == "--games" && result[0] is "compat" or "lab" or "ideas") result[i] = "--juegos";
            else if (result[i] == "--frames" && result[0] == "lab") result[i] = "--cuadros";
            else result[i] = flags.GetValueOrDefault(result[i], result[i]);
        }
        return result.ToArray();
    }

    public const string EnglishUsage = """
        RetroEmu CLI · --language en|es selects the language

          retro-cli info <file>
          retro-cli run <file> [options]
          retro-cli studio <game.js|game.json|id> --games <folder> [--seconds n] [--shots prefix]
          retro-cli games <folder>                 games with a fusion profile
          retro-cli asm <source.s> [--cpu 6502|z80] [--output game.prg|game.bin|game.tap] [--list]
          retro-cli create <c64|zx48> [game.s] [--ai provider] [--request "..."] [--attempts n]
          retro-cli test <game.s> [--machine c64|zx48]
          retro-cli ideas <profile1,profile2> [--ai provider] [--request "..."] [--games folder]
          retro-cli prepare <game> [--ai provider|agent|none] [--folder agent-folder]
                    [--rounds n] [--profiles folder] [--no-save]
          retro-cli compat <scripts-folder|script.json> --games folder [--output report.json]
                    [--md table.md] [--captures folder] [--only id1,id2] [--discover] [--twice] [--parallel n]
          retro-cli lab <script.json> [--games folder] [--frames n] [--output findings.json]
                    [--hypothesis type:address[:encoding:bytes]]... [--no-verify]
          retro-cli nes verify --rom diagnostic.nes --log nestest.log [--output report.json]
          retro-cli cpu8088 verify --suite folder-v2 [--output report.json]
          retro-cli cpu8086 verify --suite folder-v1 [--output report.json]
          retro-cli cpu8088 verify-bus --suite folder-v2 [--opcodes A1,A3,80.0] [--cases 1000]
          retro-cli cpu8086 verify-bus --suite folder-v1 [--opcodes A1,A3] [--cases 1000]
          retro-cli cpu8088 <program.bin|demo|timed-demo|irq-demo|timer-demo> [--steps n]
          retro-cli cpu8086 <program.bin|demo|timed-demo> [--steps n]

        Without --ai, create starts from an editable template. Automatic creation assembles,
        runs and tests a new game with bots. Passing does not certify creativity or fun.
        Available providers: sub-claude, sub-codex, sub-google, claude, openai, gemini,
        ollama, agent. --model selects a subscription tool's model for this run.
        prepare investigates startup, controls and variables; it may use the selected AI.

        Machines: ZX Spectrum 48K (.tap/.tzx/.z80/.sna), C64 (.t64/.prg), NES (.nes,
        mappers 0/1/2/3/4/7), arcade board ROM sets (.zip), PC/XT (.exe/.com/.zip,
        included FreeDOS, or bootable .img/.ima). Compatibility depends on the edition.

        run options:
          --rom <path>             Spectrum system ROM (default roms/48.rom)
          --bios <path>            optional PC/XT BIOS; defaults to included free BIOS
          --frames <n>             emulated frames (default 500)
          --autoload               type LOAD "" ENTER on Spectrum startup
          --until-tape-end         run to tape end, then --frames extra
          --press <frame>:<keys>:<hold>  hold keys for frames; join with +, replace spaces with _
          --screenshot <png>       save final screen
          --shot-every <n> --shot-prefix <prefix>   periodic screenshots
          --wav <file>             save emulated audio
          --samples <zip|folder>   optional recorded arcade sounds
          --trace <n>              trace the first n instructions
          --pick <n>               select an archive entry shown by info
          --poke <frame>:<hex>=<n> write a test value to memory
          --disasm <hex>:<n>       disassemble at the end
          --dump <hex>:<n>         display memory bytes at the end
          --text                   show final C64 screen text
          --routines <report.json> observe routines in a diagnostic machine
          --routine-steps <n>      bounded sample, 1–50000 steps (default 12000)

        Existing Spanish commands and flags remain accepted. Files and identifiers are unchanged.
        """;
}
