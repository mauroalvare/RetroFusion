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
    /// <summary>Fusion is the legacy default; inspired games are a separate creation experience.</summary>
    public string Kind { get; set; } = "fusion";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsInspired => Kind == "inspired";
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
    /// <summary>Optional directed input replays checked against original memory by the host.</summary>
    public List<StudioPlaytest> Playtests { get; set; } = [];
    public int Version { get; set; } = 1;
    /// <summary>Local operation that saved this version; prevents duplicate saves after interrupted cleanup.</summary>
    public string? WorkId { get; set; }
    public Retro.Ai.AiOperationStats? Usage { get; set; }
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
        game.Kind ??= "fusion";
        if (game.Kind is not ("fusion" or "inspired")) throw new InvalidDataException("Modalidad de juego desconocida.");
        game.Title = string.IsNullOrWhiteSpace(game.Title) ? game.Id : game.Title;
        game.Pitch ??= "";
        game.Approach ??= "";
        game.HowToPlay ??= "";
        game.Design ??= "";
        game.PlayerRequest ??= "";
        game.Author ??= "";
        game.Script ??= "";
        game.Games ??= new();
        game.Playtests ??= [];
        if (StudioPlaytests.CheckShape(game.Playtests) is { } problem) throw new InvalidDataException(problem);
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
    private static readonly object SaveGate = new();
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
                        game.Script = ReadResource(asm, n[..^5] + Retro.Core.UiText.Get(".js")) ?? "";
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
    public static IReadOnlyList<StudioGame> AllVersions() => StoredGames().Select(entry => entry.Game).ToList();

    private sealed record StoredGame(StudioGame Game, string? Path);

    private static IReadOnlyList<StoredGame> StoredGames()
    {
        var byKey = new Dictionary<(string, int), StoredGame>();
        foreach (var g in BuiltIn)
            byKey[(g.Id, g.Version)] = new(g, null);
        foreach (var folder in new[] { LegacyFolder, UserFolder, Retro.Ai.CreatedGames.UserFolder }.Distinct())
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
                            byKey[(g.Id, g.Version)] = new(g, f);
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
                if (File.Exists(Path.Combine(dir, Retro.Core.UiText.Format($"v{g.Version}.json"))) || File.Exists(Path.Combine(dir, Retro.Core.UiText.Format($"v{g.Version}.js"))))
                    continue;
                Write(g, dir);
            }
            string readme = Path.Combine(UserFolder, "LEEME.txt");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    Retro.Core.UiText.Get("Juegos fusionados de RetroEmu\r\n") +
                    "=============================\r\n\r\n" +
                    Retro.Core.UiText.Get("Cada carpeta es un juego; cada versión son dos archivos:\r\n") +
                    Retro.Core.UiText.Get("  vN.json  la idea, el diseño, cómo se juega y qué juegos usa\r\n") +
                    Retro.Core.UiText.Get("  vN.js    el guion que los fusiona\r\n\r\n") +
                    Retro.Core.UiText.Get("Para compartir un juego, pasá su carpeta: quien la reciba la copia acá y necesita\r\n") +
                    Retro.Core.UiText.Get("sus propias ROMs de los juegos que usa. Las ROMs nunca van en estos archivos.\r\n"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Write(StudioGame game, string dir)
    {
        Directory.CreateDirectory(dir);
        var meta = game.Copy();
        meta.Script = "";
        string path = Path.Combine(dir, Retro.Core.UiText.Format($"v{game.Version}.json"));
        byte[] metadata = Encoding.UTF8.GetBytes(meta.ToJson());
        // The card is the commit marker: never list a version before its source exists.
        WriteNewFile(Path.ChangeExtension(path, ".js"), Encoding.UTF8.GetBytes(game.Script));
        WriteNewFile(path, metadata);
        return path;
    }

    private static void WriteNewFile(string path, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + Retro.Core.UiText.Get(".tmp"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
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
    public static string Save(StudioGame game, Retro.Core.VideoFrame? picture = null)
    {
        lock (SaveGate) return SaveVersion(game, picture);
    }

    private static string ContentOf(StudioGame game, string fallbackTitle)
    {
        var content = JsonSerializer.SerializeToNode(game, GameProfile.Json)!.AsObject();
        foreach (string field in new[] { "id", "version", "workId", "usage", "created" }) content.Remove(field);
        content["title"] = string.IsNullOrWhiteSpace(game.Title) ? fallbackTitle : game.Title;
        return content.ToJsonString(GameProfile.Json);
    }

    private static int NextVersion(int version) => version < int.MaxValue ? version + 1
        : throw new InvalidDataException("No quedan números de versión disponibles para este juego.");

    private static bool ValidStorageId(string id) => !string.IsNullOrWhiteSpace(id) && id is not ("." or "..") &&
        !Path.IsPathRooted(id) && id == Path.GetFileName(id) && !id.Contains('\\') && !id.Contains('/') &&
        id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string SaveVersion(StudioGame game, Retro.Core.VideoFrame? picture)
    {
        if (string.IsNullOrWhiteSpace(game.Id)) game.Id = Slug(game.Title ?? "");
        if (!ValidStorageId(game.Id))
            throw new InvalidDataException("El identificador del juego no puede ser una ruta ni salir de su biblioteca.");
        if (string.IsNullOrWhiteSpace(game.Script)) throw new InvalidDataException("El juego no tiene un guion completo para guardar.");
        var previous = string.IsNullOrWhiteSpace(game.WorkId) ? [] : StoredGames()
            .Where(entry => entry.Game.WorkId == game.WorkId && entry.Game.Kind == game.Kind && ValidStorageId(entry.Game.Id))
            .OrderByDescending(entry => entry.Game.Version).ToList();
        if (previous.FirstOrDefault(entry => entry.Path != null && ContentOf(entry.Game, entry.Game.Title) == ContentOf(game, entry.Game.Title)) is { } saved)
        {
            game.Id = saved.Game.Id;
            game.Version = saved.Game.Version;
            return saved.Path!;
        }
        // A recovered edit belongs to the original game, even if its provisional slug changed.
        if (previous.Count > 0) game.Id = previous[0].Game.Id;
        int highest = Versions(game.Id).Select(v => v.Version).DefaultIfEmpty(0).Max();
        if (game.Version <= highest)
            game.Version = NextVersion(highest);
        string dir = Path.Combine(FolderFor(game), game.Id);
        while (File.Exists(Path.Combine(dir, Retro.Core.UiText.Format($"v{game.Version}.json"))) || File.Exists(Path.Combine(dir, Retro.Core.UiText.Format($"v{game.Version}.js"))))
            game.Version = NextVersion(game.Version);
        string path = Write(game, dir);
        if (picture != null)
            try { Retro.Core.PngWriter.Write(Path.ChangeExtension(path, ".png"), picture, 1); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* A thumbnail must not prevent playing a saved game. */ }
        return path;
    }

    public static string? PicturePath(StudioGame game)
    {
        if (string.IsNullOrWhiteSpace(game.Id) || game.Id is "." or ".." || game.Id != Path.GetFileName(game.Id) ||
            game.Id.Contains('\\') || game.Id.Contains('/') || game.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        foreach (string folder in new[] { FolderFor(game), UserFolder, LegacyFolder })
        {
            string path = Path.Combine(folder, game.Id, Retro.Core.UiText.Format($"v{game.Version}.png"));
            if (File.Exists(path)) return path;
        }
        string included = Path.Combine(AppContext.BaseDirectory, "docs", "img", "fusiones", game.Id + Retro.Core.UiText.Get(".png"));
        return File.Exists(included) ? included : null;
    }

    public static string FolderFor(StudioGame game) => game.IsInspired ? Retro.Ai.CreatedGames.UserFolder : UserFolder;

    /// <summary>An id no stored game uses yet (for the first version of a new game).</summary>
    public static string UniqueId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            id = "juego";
        var used = AllVersions().Select(g => g.Id).ToHashSet();
        if (!used.Contains(id))
            return id;
        for (int i = 2; ; i++)
            if (!used.Contains(Retro.Core.UiText.Format($"{id}-{i}")))
                return Retro.Core.UiText.Format($"{id}-{i}");
    }

    public static string Slug(string text)
    {
        string s = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        s = new string(s.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        s = Regex.Replace(s, "[^a-z0-9]+", "-").Trim('-');
        return s.Length == 0 ? Retro.Core.UiText.Get("juego-") + DateTime.Now.ToString("yyyyMMddHHmmss") : s;
    }
}
