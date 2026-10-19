using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

internal static class TestGames
{
    internal static (Dictionary<string, GameSource> sources, string romDir)? Find()
    {
        string? romDir = null;
        var sources = new Dictionary<string, GameSource>();
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (romDir == null && File.Exists(Path.Combine(d.FullName, "roms", "48.rom")))
                romDir = Path.Combine(d.FullName, "roms");
            string games = Path.Combine(d.FullName, "ROMs");
            if (!Directory.Exists(games)) continue;
            foreach (var zip in Directory.GetFiles(games, "*.zip"))
            {
                List<MediaFile> files;
                try { files = MediaArchive.Open(zip); }
                catch (InvalidDataException) { continue; }
                var p = ProfileLibrary.ForFiles(files);
                if (p != null && !sources.ContainsKey(p.Id))
                    sources[p.Id] = new GameSource(p, files, games);
            }
        }
        return romDir != null && sources.ContainsKey("manicminer") ? (sources, romDir) : null;
    }
}
