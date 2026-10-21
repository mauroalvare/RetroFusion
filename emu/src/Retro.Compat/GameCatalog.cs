using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace Retro.Compat;

/// <summary>A game of the user's folder that this emulator can run, with the profile the fusions use.</summary>
public sealed record CatalogGame(string Path, GameSource Source, bool Prepared)
{
    public GameProfile Profile => Source.Profile;
}

/// <summary>
/// Every game of the user's folder (subfolders too) that an implemented machine can run,
/// for the fusions. A game with a verified profile (made by hand or by "Preparar" for
/// that copy) uses it; the others get a basic profile on the spot: machine, controls and
/// boot, no verified variables yet. Preparation can discover some variables; this does
/// not imply that maps, graphics or the hooks for a particular mechanic are available.
/// </summary>
public static class GameCatalog
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".json", ".png", ".py", ".url", ".nfo", ".pdf", ".jpg" };

    public static List<CatalogGame> Scan(string folder)
    {
        var list = new List<CatalogGame>();
        if (!Directory.Exists(folder))
            return list;
        var ids = new HashSet<string>();
        foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string rel = System.IO.Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (Skip.Contains(System.IO.Path.GetExtension(path)) || rel.StartsWith("revision/", StringComparison.OrdinalIgnoreCase) || CompatDiscovery.IsExcluded(rel))
                continue;
            List<MediaFile> files;
            try { files = MediaArchive.Open(path); }
            catch (Exception) { continue; }
            if (files.Count == 0)
                continue;
            var profile = ProfileLibrary.ForFiles(files);
            bool prepared = profile != null;
            if (profile == null)
            {
                var (machine, media, _) = GamePreparer.Choose(files);
                // Spectrum 128K games (in a 128K folder) need the machine that is not done yet
                if (machine == null || media == null || (machine == "zx48" && rel.Contains("128", StringComparison.OrdinalIgnoreCase)))
                    continue;
                profile = BasicProfile(path, files, machine, media);
                ProfileLibrary.Remember(profile);
            }
            if (ids.Add(profile.Id))
                list.Add(new CatalogGame(path, new GameSource(profile, files, System.IO.Path.GetDirectoryName(path)), prepared));
        }
        return list;
    }

    /// <summary>Machine, controls and boot of a game nobody prepared yet; the same id "Preparar" gives it later.</summary>
    public static GameProfile BasicProfile(string path, IReadOnlyList<MediaFile> files, string machine, MediaFile media)
    {
        string name = GamePreparer.GameNameOf(path, machine);
        string sha = GamePreparer.Sha256Of(media);
        string machineName = MachineCatalog.Get(machine).Name;
        var p = new GameProfile
        {
            Id = $"{GamePreparer.Slug(name)}-{sha[..8]}",
            Name = $"{name} ({machineName})",
            Machine = machine,
            Match = new ProfileMatch { Sha256 = { sha }, Member = files.Count > 1 ? media.Name : null },
            Description = $"{name} para {machineName} (copia {System.IO.Path.GetFileName(path)}). Sin preparar: todavía no tiene variables verificadas. " +
                          "Podés jugar el original. Para integrarlo en una fusión hay que comprobar los datos y recursos que requiere la mecánica; " +
                          "Preparar puede encontrar variables, pero no verifica automáticamente mapas, gráficos ni todas sus reglas.",
        };
        if (machine == "zx48" && media.Extension is ".tap" or ".tzx")
            p.Boot.Add("load-tape");
        if (PlayDetector.SetsFor(machine).FirstOrDefault() is { } c)
        {
            p.Controls["left"] = c.Left;
            p.Controls["right"] = c.Right;
            if (c.Up != c.Fire) p.Controls["up"] = c.Up;
            if (c.Down != c.Fire) p.Controls["down"] = c.Down;
            p.Controls["fire"] = c.Fire;
        }
        if (machine == "nes")
            p.Controls["start"] = "P1 START";
        return p;
    }
}
