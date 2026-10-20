using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Retro.Asm;
using Retro.Compat;
using Retro.Core;

namespace Retro.Ai;

/// <summary>
/// A game made from scratch, kept as a folder the player can open, edit and share:
/// juego.s (the source), juego.prg or juego.tap (ready to load), juego.png (how it looked in
/// its last trial) and juego.json (this).
/// </summary>
public sealed class CreatedGame
{
    public string Title { get; set; } = "";
    public string Machine { get; set; } = "c64";
    public string Pitch { get; set; } = "";
    public string HowToPlay { get; set; } = "";
    public string Request { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public bool Passed { get; set; }
    /// <summary>The last trial, as text.</summary>
    public string Trial { get; set; } = "";

    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public string SourcePath => Path.Combine(Folder, "juego.s");
    [JsonIgnore] public string ProgramPath => Path.Combine(Folder, "juego" + (Templates.For(Machine)?.Output ?? ".prg"));
    [JsonIgnore] public string PicturePath => Path.Combine(Folder, "juego.png");
    [JsonIgnore] public string MachineName => Templates.For(Machine)?.Name ?? Machine;
}

/// <summary>Where created games live and how they are saved, listed and tried again after an edit.</summary>
public static class CreatedGames
{
    public const string FolderName = "Juegos creados";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// A "Juegos creados" folder next to the program or in a folder above it (like "Juegos
    /// fusionados"), or else Documents/RetroEmu/Juegos creados.
    /// </summary>
    public static string UserFolder { get; set; } = DefaultFolder();

    public static string DefaultFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, FolderName);
            if (Directory.Exists(p))
                return p;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", FolderName);
    }

    /// <summary>Every created game, newest first.</summary>
    public static IReadOnlyList<CreatedGame> All()
    {
        var list = new List<CreatedGame>();
        if (!Directory.Exists(UserFolder))
            return list;
        foreach (string dir in Directory.GetDirectories(UserFolder))
        {
            string path = Path.Combine(dir, "juego.json");
            if (!File.Exists(path))
                continue;
            try
            {
                var g = JsonSerializer.Deserialize<CreatedGame>(File.ReadAllText(path), Json);
                if (g == null || Templates.For(g.Machine ?? "") == null)
                    continue;
                g.Title = string.IsNullOrWhiteSpace(g.Title) ? Path.GetFileName(dir) : g.Title;
                g.Pitch ??= "";
                g.HowToPlay ??= "";
                g.Request ??= "";
                g.Author ??= "";
                g.Trial ??= "";
                g.Folder = dir;
                list.Add(g);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                // Broken or temporarily unreadable metadata does not hide the other games.
            }
        }
        return list.OrderByDescending(g => g.Created).ToList();
    }

    /// <summary>Saves what an AI wrote (and how its trial went) in a new folder.</summary>
    public static CreatedGame Save(GameDraft draft, TrialReport? report, string machine, string request, string author)
    {
        var g = new CreatedGame
        {
            Title = draft.Title,
            Machine = machine,
            Pitch = draft.Pitch,
            HowToPlay = draft.HowToPlay,
            Request = request.Trim(),
            Author = author,
            Folder = NewFolder(draft.Title, machine),
        };
        File.WriteAllText(g.SourcePath, draft.Source);
        Store(g, report);
        return g;
    }

    /// <summary>A new game from the machine's template, to write by hand (already tried).</summary>
    public static CreatedGame FromTemplate(string machine, string firmwareDirectory)
    {
        var t = Templates.For(machine) ?? throw new ArgumentException($"no hay plantilla para {machine}");
        var g = new CreatedGame
        {
            Title = "Mi juego",
            Machine = t.Machine,
            Pitch = "La plantilla de la máquina: arranca, dibuja, lee los controles y suena. Editá juego.s y probalo de nuevo.",
            HowToPlay = t.Machine == "c64" ? "Joystick en el puerto 2; el disparo suena." : "Q A O P o el Kempston; ESPACIO o el disparo suena.",
            Author = "plantilla",
            Folder = NewFolder("mi juego", t.Machine),
        };
        File.WriteAllText(g.SourcePath, t.Source);
        Store(g, HomebrewTrial.Run(t.Source, t.Machine, firmwareDirectory));
        return g;
    }

    public const string ExampleAuthor = "ejemplo de RetroEmu";

    /// <summary>
    /// Copies the example games into the folder (each one once, tried in the emulator so it has
    /// its program and its picture) and leaves a LEEME.txt. A folder already there is left alone.
    /// </summary>
    public static int ExportExamples(string firmwareDirectory)
    {
        int added = 0;
        Directory.CreateDirectory(UserFolder);
        foreach (var e in Templates.Examples)
        {
            string dir = Path.Combine(UserFolder, $"{Slug(e.Title)}-{e.Machine}");
            if (Directory.Exists(dir))
                continue;
            Directory.CreateDirectory(dir);
            var g = new CreatedGame
            {
                Title = e.Title,
                Machine = e.Machine,
                Pitch = e.Pitch,
                HowToPlay = e.HowToPlay,
                Author = ExampleAuthor,
                Folder = dir,
            };
            File.WriteAllText(g.SourcePath, e.Source);
            Store(g, HomebrewTrial.Run(e.Source, e.Machine, firmwareDirectory));
            added++;
        }
        string readme = Path.Combine(UserFolder, "LEEME.txt");
        if (!File.Exists(readme))
            File.WriteAllText(readme,
                "Juegos creados con RetroEmu\r\n" +
                "===========================\r\n\r\n" +
                "Cada carpeta es un juego de 8 bits escrito desde cero (por una IA o a mano):\r\n" +
                "  juego.s            el programa en ensamblador (editable)\r\n" +
                "  juego.prg o .tap   listo para cargar en el C64 o el Spectrum (también en otros emuladores)\r\n" +
                "  juego.png          cómo se veía en su última prueba\r\n" +
                "  juego.json         título, cómo se juega y el informe de la prueba\r\n\r\n" +
                "Después de editar juego.s, en RetroEmu: Crear > Juego de 8 bits nuevo > Probar de nuevo.\r\n");
        return added;
    }

    /// <summary>Tries the game again from its juego.s (after the player or an agent edited it).</summary>
    public static TrialReport TryAgain(CreatedGame g, string firmwareDirectory)
    {
        var report = HomebrewTrial.Run(File.ReadAllText(g.SourcePath), g.Machine, firmwareDirectory);
        Store(g, report);
        return report;
    }

    private static void Store(CreatedGame g, TrialReport? report)
    {
        if (report != null)
        {
            g.Passed = report.Ok;
            g.Trial = report.Summary;
            if (report.File != null)
                File.WriteAllBytes(g.ProgramPath, report.File);
            if (report.LastPicture != null)
                PngWriter.Write(g.PicturePath, report.LastPicture, 2);
        }
        File.WriteAllText(Path.Combine(g.Folder, "juego.json"), JsonSerializer.Serialize(g, Json));
    }

    private static string Slug(string title)
    {
        string slug = Regex.Replace(title.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "");
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "juego" : slug;
    }

    private static string NewFolder(string title, string machine)
    {
        string baseName = $"{Slug(title)}-{machine}", path = Path.Combine(UserFolder, baseName);
        for (int n = 2; Directory.Exists(path); n++)
            path = Path.Combine(UserFolder, $"{baseName}-{n}");
        Directory.CreateDirectory(path);
        return path;
    }
}
