using Retro.Core;

namespace Retro.Studio;

/// <summary>Rejects a clearly erased base or a near-exact copy of another complete screen, not a creative-quality judge.</summary>
internal static class BaseSceneCheck
{
    private sealed record Match(int Pixels, int Exact, int Foreground, int ForegroundExact, int Intersection, int Union, int OutputColours)
    {
        public bool NearlyExact => Exact >= Pixels * 0.99 && Foreground >= 128 && ForegroundExact >= Foreground * 0.98;
        public bool SameGeometry => Foreground >= 256 && Intersection >= Union * 0.98;
        public bool Erased => Foreground >= 256 && (OutputColours == 1 || ForegroundExact < Foreground * 0.25 && Intersection < Union * 0.25);
    }

    public static string? Problem(StudioSession session)
    {
        if (session.Game.BaseSlot is not { } id) return null;
        var original = session.Slots.Single(s => s.Id == id);
        var own = Compare(session.Video, original.Machine);
        if (own == null) return "La fusión cambió la resolución de la base. Conservá su escena y agregá los aportes dentro de sus coordenadas.";
        if (own.NearlyExact || own.SameGeometry) return null;
        foreach (var donor in session.Slots.Where(s => s.Id != id))
            if (Compare(session.Video, donor.Machine)?.NearlyExact == true)
                return $"Se está mostrando casi toda la pantalla de {donor.Profile.Name} en lugar del original base. " +
                    $"Conservá games.{id} y transferí elementos y comportamientos del aportante; no pegues otra pantalla completa.";
        return own.Erased
            ? "La escena tapa casi toda la imagen reconocible del original base. Conservá sus mapas, gráficos e interfaz y dibujá solo los aportes; una escena nueva corresponde a creación libre."
            : null;
    }

    private static Dictionary<int, int> Colours(VideoFrame frame, int x, int y, int w, int h)
    {
        var counts = new Dictionary<int, int>();
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                int color = frame.Pixels[(y + row) * frame.Width + x + col] & 0xFFFFFF;
                counts[color] = counts.GetValueOrDefault(color) + 1;
            }
        return counts;
    }

    private static Match? Compare(VideoFrame output, IMachine machine)
    {
        var area = StudioSession.PictureArea(machine);
        if (output.Width != area.w || output.Height != area.h) return null;
        var source = machine.Video;
        var sourceColors = Colours(source, area.x, area.y, area.w, area.h);
        var outputColors = Colours(output, 0, 0, output.Width, output.Height);
        int background = sourceColors.OrderByDescending(c => c.Value).ThenBy(c => c.Key).First().Key;
        int outputBackground = outputColors.GetValueOrDefault(background) >= output.Pixels.Length / 8
            ? background : outputColors.OrderByDescending(c => c.Value).ThenBy(c => c.Key).First().Key;
        int exact = 0, foreground = 0, foregroundExact = 0, intersection = 0, union = 0;
        for (int y = 0; y < area.h; y++)
            for (int x = 0; x < area.w; x++)
            {
                int a = source.Pixels[(area.y + y) * source.Width + area.x + x] & 0xFFFFFF;
                int b = output.Pixels[y * output.Width + x] & 0xFFFFFF;
                bool fgA = a != background, fgB = b != outputBackground;
                if (a == b) { exact++; if (fgA) foregroundExact++; }
                if (fgA) foreground++;
                if (fgA && fgB) intersection++;
                if (fgA || fgB) union++;
            }
        return new Match(output.Pixels.Length, exact, foreground, foregroundExact, intersection, union, outputColors.Count);
    }
}
