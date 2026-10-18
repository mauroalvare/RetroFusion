using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retro.Compat;

/// <summary>A key press of a script: on <see cref="Frame"/> (counted after the boot) hold <see cref="Keys"/>.</summary>
public sealed class CompatStep
{
    public int Frame { get; set; }
    /// <summary>Machine key names joined with '+' ("SPACE", "JOY2 FIRE", "CAPS SHIFT+5").</summary>
    public string Keys { get; set; } = "";
    public int Hold { get; set; } = 6;
}

/// <summary>Something that has to be true on a frame for a level to count as reached.</summary>
public sealed class CompatCheck
{
    /// <summary>title, controls or playable.</summary>
    public string Level { get; set; } = "title";
    /// <summary>Frame after the boot when it is evaluated; 0 = at the end of the run.</summary>
    public int Frame { get; set; }
    /// <summary>text, notText, memory, colors, changed.</summary>
    public string Type { get; set; } = "text";
    /// <summary>Text to find (text, notText) or value to compare (memory, decimal or $hex).</summary>
    public string? Value { get; set; }
    /// <summary>Memory address in hex (memory).</summary>
    public string? Address { get; set; }
    /// <summary>eq, ne, gt, lt, ge, le (memory).</summary>
    public string Op { get; set; } = "eq";
    /// <summary>Minimum number of distinct colours (colors).</summary>
    public int Min { get; set; }
    /// <summary>The picture must differ from the one of this frame (changed).</summary>
    public int Since { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// How to test one edition of one game: the file, how it boots, which keys to press and
/// what to check. Scripts live in emu/compat/juegos/*.json; the runner turns them into
/// results with evidence (hash, firmware, levels reached, screenshot).
/// </summary>
public sealed class CompatScript
{
    public string Id { get; set; } = "";
    public string Game { get; set; } = "";
    /// <summary>Machine id of the catalog (zx48, c64, invaders, zx128, nes...).</summary>
    public string Machine { get; set; } = "";
    /// <summary>Path relative to the games folder.</summary>
    public string File { get; set; } = "";
    /// <summary>File inside the zip; empty = the best one for the machine.</summary>
    public string? Member { get; set; }
    /// <summary>SHA-256 of the edition the script was written for (of the member).</summary>
    public string? Sha256 { get; set; }
    public string Edition { get; set; } = "";
    public string Controls { get; set; } = "";
    /// <summary>auto (tapes are loaded with LOAD ""), tape or none.</summary>
    public string Boot { get; set; } = "auto";
    /// <summary>Frames to run after the boot.</summary>
    public int Frames { get; set; } = 1500;
    /// <summary>Most frames the tape load may take; for multiload tapes without a stop block, the end of the first part.</summary>
    public int BootFrames { get; set; } = 30000;
    /// <summary>When BootFrames ends the load with the tape still playing, count it as loaded (multiload games).</summary>
    public bool LoadEndsByTime { get; set; }
    public List<CompatStep> Steps { get; set; } = new();
    public List<CompatCheck> Checks { get; set; } = new();
    /// <summary>Inputs tried by the controls probe; empty = the machine's defaults.</summary>
    public List<string> ProbeInputs { get; set; } = new();
    /// <summary>Things that belong to the game, not to the emulator (code card, multiload, trainer).</summary>
    public List<string> GameQuirks { get; set; } = new();
    public string Notes { get; set; } = "";
    /// <summary>Experiments of the reverse engineering lab, played from the end of the script (shooting adds points...).</summary>
    public List<LabExperiment>? Experiments { get; set; }
    /// <summary>True when a person wrote the steps and checks (not just discovered).</summary>
    public bool Curated { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CompatScript Load(string path)
    {
        var s = JsonSerializer.Deserialize<CompatScript>(System.IO.File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path}: guion vacío");
        if (string.IsNullOrEmpty(s.Id))
            s.Id = Path.GetFileNameWithoutExtension(path);
        return s;
    }

    public void Save(string path) => System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + "\n");

    /// <summary>All the scripts of a folder (or one file), sorted by id.</summary>
    public static List<CompatScript> LoadAll(string folderOrFile) =>
        Directory.Exists(folderOrFile)
            ? Directory.GetFiles(folderOrFile, "*.json").Select(Load).OrderBy(s => s.Machine).ThenBy(s => s.Id).ToList()
            : new List<CompatScript> { Load(folderOrFile) };
}
