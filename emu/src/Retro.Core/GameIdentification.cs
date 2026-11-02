using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Retro.Core;

public sealed record IdentifiedGame(string Title, string[] Genres, int? ReleaseYear, string? Publisher,
    string Source, string Method, int? FirstReleaseYear = null);

public sealed record GameLookup(string Key, string Name, string Machine, string Path = "");

/// <summary>Filename hints are explicitly distinguished from catalog facts. No gameplay guesses.</summary>
public static partial class GameIdentification
{
    [GeneratedRegex(@"\s*[\[(](19\d{2}|20\d{2}|2100)[\])]", RegexOptions.CultureInvariant)]
    private static partial Regex YearTag();
    [GeneratedRegex(@"\[[^\]]*\]|\((?:USA|Europe|Japan|World|U|E|J|EU|US|UK|ES|En|Fr|De|Es|It|Rev[ .][^)]*|v\d[^)]*|19\d{2}|20\d{2}|2100)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DumpTags();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    public static string TitleFromName(string name)
    {
        string title = DumpTags().Replace(name.Replace('_', ' '), " ");
        // TOSEC puts year and publisher after the title; do not retain them as part of the query.
        var year = YearTag().Match(name);
        if (year.Success && year.Index > 1) title = DumpTags().Replace(name[..year.Index].Replace('_', ' '), " ");
        title = Spaces().Replace(title, " ").Trim(' ', '-', '.');
        if (title.EndsWith(", The", StringComparison.OrdinalIgnoreCase)) title = Retro.Core.UiText.Get("The ") + title[..^5];
        return title.Length is > 0 and <= 160 ? title : name[..Math.Min(name.Length, 160)];
    }

    public static string MatchTitle(string name) => string.Concat(LibraryMetadata.Normalize(TitleFromName(name)).Where(char.IsLetterOrDigit));

    public static string[] NormalizeGenres(IEnumerable<string> genres) => genres.SelectMany(value => LibraryMetadata.Normalize(value.Trim()) switch
    {
        "PLATFORM GAME" or "PLATFORMER" or "VIDEOJUEGO DE PLATAFORMAS" => new[] { "Plataformas" },
        "PUZZLE-PLATFORMER" or "PUZZLE PLATFORMER" => ["Puzles", "Plataformas"],
        "ACTION GAME" or "VIDEOJUEGO DE ACCION" => ["Acción"],
        "ADVENTURE GAME" or "VIDEOJUEGO DE AVENTURA" or "VIDEOJUEGO DE AVENTURAS" => ["Aventura"],
        "SHOOTER GAME" or "SHOOT 'EM UP" or "FIXED SHOOTER" or "MATAMARCIANOS" or "VIDEOJUEGO DE DISPAROS" => ["Disparos"],
        "PUZZLE VIDEO GAME" or "PUZZLE GAME" or "VIDEOJUEGO DE LOGICA" or "VIDEOJUEGO DE ROMPECABEZAS" => ["Puzles"],
        "ROLE-PLAYING VIDEO GAME" or "VIDEOJUEGO DE ROL" => ["Rol"],
        "STRATEGY VIDEO GAME" or "VIDEOJUEGO DE ESTRATEGIA" => ["Estrategia"],
        "SPORTS VIDEO GAME" or "VIDEOJUEGO DE DEPORTES" => ["Deportes"],
        "RACING VIDEO GAME" or "RACING GAME" or "VIDEOJUEGO DE CARRERAS" => ["Carreras"],
        "FIGHTING GAME" or "VIDEOJUEGO DE LUCHA" => ["Lucha"],
        "SIMULATION VIDEO GAME" or "VIDEOJUEGO DE SIMULACION" => ["Simulación"],
        "EDUCATIONAL VIDEO GAME" or "VIDEOJUEGO EDUCATIVO" => ["Educativo"],
        _ => [value.Trim()]
    }).Where(value => value.Length is > 0 and <= 40).DistinctBy(LibraryMetadata.Normalize).Take(8).ToArray();

    public static IdentifiedGame FromFilename(string name)
    {
        var years = YearTag().Matches(name).Select(match => int.Parse(match.Groups[1].Value)).Distinct().ToArray();
        return new(TitleFromName(name), [], years.Length == 1 ? years[0] : null, null,
            "Nombre del archivo", "Indicio del nombre; edición sin verificar");
    }

    public static LibraryGameInfo Merge(LibraryGameInfo personal, IdentifiedGame? detected) => new(
        personal.Genres.Count > 0 ? personal.Genres : Array.AsReadOnly(detected?.Genres.ToArray() ?? []),
        personal.ReleaseYear ?? detected?.ReleaseYear ?? detected?.FirstReleaseYear, personal.AddedAt,
        personal.ReleaseYear == null && detected?.ReleaseYear == null && detected?.FirstReleaseYear != null);

    /// <summary>Read an existing EmulationStation list; no external entities, path traversal or large documents.</summary>
    public static IReadOnlyDictionary<string, IdentifiedGame> ReadGameList(string file, string libraryRoot)
    {
        var result = new Dictionary<string, IdentifiedGame>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) return result;
            string root = Path.GetFullPath(libraryRoot);
            if (!Path.EndsInDirectorySeparator(root)) root += Path.DirectorySeparatorChar;
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            var document = XDocument.Load(reader);
            foreach (var game in document.Root?.Elements("game").Take(16384) ?? [])
            {
                string relative = (string?)game.Element("path") ?? "";
                if (relative.Length is 0 or > 1024) continue;
                string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, relative));
                if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                string title = ((string?)game.Element("name") ?? "").Trim();
                if (title.Length is 0 or > 160) continue;
                string date = (string?)game.Element("releasedate") ?? "";
                int? year = date.Length >= 4 && int.TryParse(date[..4], out int y) && y is >= 1900 and <= 2100 ? y : null;
                string[] genres = ((string?)game.Element("genre") ?? "").Split([',', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(value => value.Length <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
                string? publisher = ((string?)game.Element("publisher"))?.Trim();
                if (publisher?.Length > 160) publisher = null;
                result.TryAdd(path, new(title, NormalizeGenres(genres), year, publisher, "gamelist.xml local", "Ficha de la colección; asociada por ruta"));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException or NotSupportedException) { }
        return result;
    }
}
