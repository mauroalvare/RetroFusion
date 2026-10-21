using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Fusion;

namespace Retro.Studio;

/// <summary>
/// A fusion game: an idea that mixes 2 or 3 games into one, and the JavaScript that makes
/// it happen on top of the real emulated games. Every change asked by the player is a new
/// version; the old ones are kept.
/// </summary>
public sealed class StudioGame
{
    /// <summary>Same for every version of the game.</summary>
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The idea in one paragraph.</summary>
    public string Pitch { get; set; } = "";
    /// <summary>How the games are mixed (invasion, crossed powers, alternating worlds, remix...).</summary>
    public string Approach { get; set; } = "";
    public string HowToPlay { get; set; } = "";
    /// <summary>Mechanic link, distinct rule, decisions, progression and presentation; see Docs/GUIA-FUSION.md.</summary>
    public string Design { get; set; } = "";
    /// <summary>The player's initial request, retained across proposals and revisions.</summary>
    public string PlayerRequest { get; set; } = "";
    public List<RecipeGame> Games { get; set; } = new();
    /// <summary>Original whose world and presentation are retained; null preserves legacy free reinterpretations.</summary>
    public string? BaseSlot { get; set; }
    public string Script { get; set; } = "";
    public int Version { get; set; } = 1;
    /// <summary>What the player asked for to get this version (null for the first one).</summary>
    public string? Request { get; set; }
    /// <summary>What changed compared to the previous version.</summary>
    public string? Changes { get; set; }
    public string Author { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;

    public static StudioGame Parse(string json)
    {
        var game = JsonSerializer.Deserialize<StudioGame>(json, GameProfile.Json)
                   ?? throw new InvalidDataException("Juego vacío.");
        game.Id ??= "";
        game.Title = string.IsNullOrWhiteSpace(game.Title) ? game.Id : game.Title;
        game.Pitch ??= "";
        game.Approach ??= "";
        game.HowToPlay ??= "";
        game.Design ??= "";
        game.PlayerRequest ??= "";
        game.Author ??= "";
        game.Script ??= "";
        game.Games ??= new();
        if (game.Games.Any(g => g == null || string.IsNullOrWhiteSpace(g.Slot) || string.IsNullOrWhiteSpace(g.Profile)))
            throw new InvalidDataException("La lista de originales contiene un juego incompleto.");
        if (game.BaseSlot != null && !game.Games.Any(g => g.Slot == game.BaseSlot))
            throw new InvalidDataException("El juego base no pertenece a los originales de esta fusión.");
        return game;
    }

    public string ToJson() => JsonSerializer.Serialize(this, GameProfile.Json);

    public StudioGame Copy() => Parse(ToJson());

    public IEnumerable<string> ProfileIds => Games.Select(g => g.Profile);
}

/// <summary>An idea the AI proposes before writing any code.</summary>
public sealed record GameConcept(string Title, string Approach, string Pitch, string HowToPlay, string Design = "", FusionDirection? Direction = null,
    string PlayerRequest = "", string MissingData = "");

/// <summary>The player's direction, independent of whatever the model writes in its answer.</summary>
public sealed record FusionDirection(string? BaseSlot = null, bool Reimagine = false)
{
    public string? Resolve(IReadOnlyList<RecipeGame> slots)
    {
        if (Reimagine) return null;
        string? selected = BaseSlot ?? slots.FirstOrDefault()?.Slot;
        if (selected == null || !slots.Any(s => s.Slot == selected))
            throw new ArgumentException("Elegí un juego base entre los originales seleccionados.");
        return selected;
    }
    public static FusionDirection From(StudioGame game) => new(game.BaseSlot, game.BaseSlot == null);
}

/// <summary>Built-in fusion games plus the ones the player made (every version on disk).</summary>
public static class StudioLibrary
{
    private static List<StudioGame>? _builtIn;

    // Hide only an unchanged exported copy of the withdrawn example. Keep the files
    // and any player rewrite; newer versions with their own scene still belong here.
    private static bool IsRetiredExample(StudioGame g) => g.Id == "pacman-mina" &&
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(g.Script.Replace("\r\n", "\n")))) ==
        "1617759419c541d34ff319d74f234ad1b455a7e1dccfed7df44353357fa8a9f8";

    public static IReadOnlyList<StudioGame> BuiltIn
    {
        get
        {
            if (_builtIn != null)
                return _builtIn;
            var asm = Assembly.GetExecutingAssembly();
            _builtIn = asm.GetManifestResourceNames()
                .Where(n => n.Contains(".Games.") && n.EndsWith(".json"))
                .Select(n =>
                {
                    var game = StudioGame.Parse(ReadResource(asm, n)!);
                    // the script can live next to the JSON, as a .js file that is easier to edit
                    if (game.Script.Length == 0)
                        game.Script = ReadResource(asm, n[..^5] + ".js") ?? "";
                    return game;
                })
                .ToList();
            return _builtIn;
        }
    }

    private static string? ReadResource(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name);
        if (s == null)
            return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public const string FolderName = "Juegos fusionados";

    /// <summary>
    /// Where the player's fusion games live: a "Juegos fusionados" folder next to the
    /// program or in one of the folders above it (like the ROMs folder), or else
    /// Documents/RetroEmu/Juegos fusionados. Each version is two files the player can
    /// open, share or edit: vN.json (the idea, the design, how to play) and vN.js (the script).
    /// </summary>
    public static string UserFolder { get; set; } = DefaultFolder();

    /// <summary>Where earlier versions of RetroEmu kept them (still read).</summary>
    public static string LegacyFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "juegos");

    public static string DefaultFolder() => Retro.Core.PlayerFolders.Find(FolderName,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", FolderName))!;

    /// <summary>Every version of every game: the built-in ones, then the stored ones (a stored copy wins over a built-in one with the same version).</summary>
    public static IReadOnlyList<StudioGame> AllVersions()
    {
        var byKey = new Dictionary<(string, int), StudioGame>();
        foreach (var g in BuiltIn)
            byKey[(g.Id, g.Version)] = g;
        foreach (var folder in new[] { LegacyFolder, UserFolder })
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var dir in Directory.GetDirectories(folder))
            {
                string[] files;
                try { files = Directory.GetFiles(dir, "v*.json"); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var f in files)
                {
                    try
                    {
                        var g = StudioGame.Parse(File.ReadAllText(f));
                        string js = Path.ChangeExtension(f, ".js");
                        if (File.Exists(js))
                            g.Script = File.ReadAllText(js);
                        if (g.Script.Length > 0 && !IsRetiredExample(g))
                            byKey[(g.Id, g.Version)] = g;
                    }
                    catch (JsonException) { }
                    catch (IOException) { }
                    catch (InvalidDataException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        return byKey.Values.ToList();
    }

    /// <summary>Copies the built-in games into the player's folder (once), so they can be seen, edited and shared like the rest.</summary>
    public static void ExportBuiltIns()
    {
        try
        {
            Directory.CreateDirectory(UserFolder);
            foreach (var g in BuiltIn)
            {
                string dir = Path.Combine(UserFolder, g.Id);
                if (File.Exists(Path.Combine(dir, $"v{g.Version}.json")))
                    continue;
                Write(g, dir);
            }
            string readme = Path.Combine(UserFolder, "LEEME.txt");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    "Juegos fusionados de RetroEmu\r\n" +
                    "=============================\r\n\r\n" +
                    "Cada carpeta es un juego; cada versión son dos archivos:\r\n" +
                    "  vN.json  la idea, el diseño, cómo se juega y qué juegos usa\r\n" +
                    "  vN.js    el guion que los fusiona\r\n\r\n" +
                    "Para compartir un juego, pasá su carpeta: quien la reciba la copia acá y necesita\r\n" +
                    "sus propias ROMs de los juegos que usa. Las ROMs nunca van en estos archivos.\r\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Write(StudioGame game, string dir)
    {
        Directory.CreateDirectory(dir);
        var meta = game.Copy();
        meta.Script = "";
        string path = Path.Combine(dir, $"v{game.Version}.json");
        File.WriteAllText(path, meta.ToJson());
        File.WriteAllText(Path.ChangeExtension(path, ".js"), game.Script);
        return path;
    }

    /// <summary>The latest version of each game made with exactly these games (in any slot order).</summary>
    public static IEnumerable<StudioGame> For(IEnumerable<string> profileIds)
    {
        var want = profileIds.OrderBy(x => x).ToList();
        return AllVersions()
            .Where(g => g.ProfileIds.OrderBy(x => x).SequenceEqual(want))
            .GroupBy(g => g.Id)
            .Select(grp => grp.OrderByDescending(v => v.Version).First());
    }

    public static IReadOnlyList<StudioGame> Versions(string id) =>
        AllVersions().Where(g => g.Id == id).OrderBy(g => g.Version).ToList();

    /// <summary>Saves the game as a new version (the number after the highest one stored).</summary>
    public static string Save(StudioGame game)
    {
        if (string.IsNullOrWhiteSpace(game.Id))
            game.Id = Slug(game.Title);
        int highest = Versions(game.Id).Select(v => v.Version).DefaultIfEmpty(0).Max();
        if (game.Version <= highest)
            game.Version = highest + 1;
        return Write(game, Path.Combine(UserFolder, game.Id));
    }

    /// <summary>An id no stored game uses yet (for the first version of a new game).</summary>
    public static string UniqueId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            id = "juego";
        var used = AllVersions().Select(g => g.Id).ToHashSet();
        if (!used.Contains(id))
            return id;
        for (int i = 2; ; i++)
            if (!used.Contains($"{id}-{i}"))
                return $"{id}-{i}";
    }

    public static string Slug(string text)
    {
        string s = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        s = new string(s.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        s = Regex.Replace(s, "[^a-z0-9]+", "-").Trim('-');
        return s.Length == 0 ? "juego-" + DateTime.Now.ToString("yyyyMMddHHmmss") : s;
    }
}
