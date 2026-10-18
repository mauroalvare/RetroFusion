using System.Text.RegularExpressions;
using Retro.Core;
using Retro.Machines;

namespace Retro.Compat;

/// <summary>
/// Writes a starting script for every game file of the games folder that has none, so the
/// matrix covers the whole collection. A person (or an agent) then adds steps and checks.
/// Content the user excluded is skipped.
/// </summary>
public static class CompatDiscovery
{
    /// <summary>Titles the user asked to keep out (adult content, and others by caution).</summary>
    public static readonly string[] Excluded =
    {
        "fundamentally loathsome", "fundamentally-loathsome", "cheril", "phantomasa", "sila tsikl", "sila-tsikl",
        "nanako descends", "nanako-descends",
        // Bootee (NES) stars Cheril: left out until the user decides
        "bootee",
    };

    public static bool IsExcluded(string path) =>
        Excluded.Any(x => path.Replace('_', ' ').Contains(x.Replace('-', ' '), StringComparison.OrdinalIgnoreCase)
                          || path.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".json", ".png", ".py", ".url", ".nfo", ".pdf" };

    /// <summary>Returns the scripts created (existing ids are never overwritten).</summary>
    public static List<CompatScript> Discover(string gamesRoot, string scriptsFolder)
    {
        Directory.CreateDirectory(scriptsFolder);
        var existing = new List<CompatScript>();
        foreach (var file in Directory.GetFiles(scriptsFolder, "*.json"))
        {
            var s = CompatScript.Load(file);
            existing.Add(s);
            // discovered scripts follow the naming rules; curated ones keep what a person wrote
            string base_ = Path.GetFileNameWithoutExtension(s.File);
            if (base_.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase) || base_.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
                base_ = base_[..^4];
            if (!s.Curated && s.Machine != "invaders" && GameName(base_) is var g && g != s.Game)
            {
                s.Game = g;
                s.Save(file);
            }
        }
        var knownFiles = existing.Select(s => s.File.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = existing.Select(s => s.Id).ToHashSet();
        var created = new List<CompatScript>();
        foreach (var path in Directory.GetFiles(gamesRoot, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(gamesRoot, path).Replace('\\', '/');
            if (Skip.Contains(Path.GetExtension(path)) || rel.StartsWith("revision/", StringComparison.OrdinalIgnoreCase)
                || IsExcluded(rel) || knownFiles.Contains(rel))
                continue;
            List<MediaFile> files;
            try { files = MediaArchive.Open(path); }
            catch (Exception) { continue; }
            if (files.Count == 0)
                continue;
            var script = ScriptFor(rel, files);
            if (script == null)
                continue;
            string id = script.Id;
            for (int n = 2; ids.Contains(id); n++)
                id = $"{script.Id}-{n}";
            script.Id = id;
            ids.Add(id);
            script.Save(Path.Combine(scriptsFolder, id + ".json"));
            created.Add(script);
        }
        return created;
    }

    private static CompatScript? ScriptFor(string rel, List<MediaFile> files)
    {
        string name = Path.GetFileNameWithoutExtension(rel);
        if (name.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        string game = GameName(name);
        if (MachineCatalog.IdentifySet(files) is { } board)
        {
            return new CompatScript
            {
                Id = Slug($"{board.Machine.Id}-{name}"), Game = "Space Invaders", Machine = board.Machine.Id, File = rel,
                Edition = board.Reason, Controls = "5 o C moneda, 1 empezar, flechas y espacio",
                Frames = 1200, Steps = { new CompatStep { Frame = 300, Keys = "COIN" }, new CompatStep { Frame = 420, Keys = "P1 START" } },
            };
        }
        var best = files.Where(f => !Skip.Contains(f.Extension))
            .SelectMany(MachineCatalog.Identify)
            .Where(c => c.Confidence >= 50)
            .OrderByDescending(c => MachineLauncher.IsImplemented(c.Machine.Id) && MachineCatalog.NotLoadableYet(c) == null)
            .ThenByDescending(c => c.Confidence)
            .ThenBy(c => MediaArchive.DumpScore(c.File.Name))
            .FirstOrDefault();
        if (best == null)
            return null;
        string machine = best.Machine.Id;
        bool needs128 = machine == "zx48" && rel.Contains("128", StringComparison.OrdinalIgnoreCase);
        if (needs128)
            machine = "zx128";
        var s = new CompatScript
        {
            Id = Slug($"{machine}-{name}"), Game = game, Machine = machine, File = rel,
            Member = files.Count > 1 ? best.File.Name : null,
            Edition = best.Reason + (files.Count > 1 ? $"; el paquete trae {files.Count} archivos, se eligió el volcado más limpio" : ""),
            Frames = machine switch { "c64" => 2500, _ => 1500 },
            Notes = needs128 ? "Está en la carpeta de 128K: necesita el Spectrum 128K (T-030)." : "",
        };
        return s;
    }

    /// <summary>A readable title from a file name: "Mojon-Twins--Sir-Ababol--C64" → "Sir Ababol", "boulder-dash" → "Boulder Dash".</summary>
    public static string GameName(string fileName)
    {
        string n = fileName.Replace('_', ' ');
        n = Regex.Replace(n, @"^(Inside|Mojon[- ]Twins)\s*-+\s*", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"\s*-*\s*(The[- ]Mojon[- ]Twins)$", "", RegexOptions.IgnoreCase);
        // platform and dump suffixes, repeated: "Pac_Man_ZX-Spectrum_ROM_EN" has two
        for (string before = ""; before != n;)
        {
            before = n;
            n = Regex.Replace(n, @"(?:[\s-]+(?:ROM|EN)|[\s-]*(?:\(1\)|C64|CPC|ZX[\s-]*Spectrum|Atari[\s-]*8[\s-]*bit|Atari8bit|AtariST|Amiga|Apple[\s-]*II|AppleII|PC[\s-]*88|PC8800|VIC[\s-]*20|Amstrad|Commodore\s*Plus4|msx[\s-]*gdx))\s*$", "", RegexOptions.IgnoreCase);
        }
        n = Regex.Replace(n.Replace("--", " - ").Replace('-', ' '), @"\s+", " ").Trim();
        if (n.Length > 0 && n == n.ToLowerInvariant())
            n = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(n);
        return n;
    }

    private static string Slug(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant().Replace("_rom_en", "").Replace("_en", ""), @"[^a-z0-9]+", "-"), "-+", "-").Trim('-');
}
