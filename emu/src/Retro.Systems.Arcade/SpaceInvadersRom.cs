using Retro.Core;

namespace Retro.Systems.Arcade;

/// <summary>
/// Builds the 8 KB program from the four 2 KB chips (MAME set "invaders":
/// invaders.h at $0000, .g at $0800, .f at $1000, .e at $1800) or from a single
/// 8 KB dump. The CRCs of the known good dump are checked to tell the user.
/// </summary>
public static class SpaceInvadersRom
{
    private static readonly (string name, int offset, uint crc)[] Chips =
    {
        ("invaders.h", 0x0000, 0x734F5AD8),
        ("invaders.g", 0x0800, 0x6BFACA4A),
        ("invaders.f", 0x1000, 0x0CCEAD96),
        ("invaders.e", 0x1800, 0x14E538B0),
    };

    /// <summary>0 = not this game, 100 = all four chips present.</summary>
    public static int Probe(IReadOnlyList<MediaFile> files)
    {
        int found = Chips.Count(c => files.Any(f => f.Name.Equals(c.name, StringComparison.OrdinalIgnoreCase) && f.Data.Length == 0x800));
        if (found == 4)
            return 100;
        if (files.Any(f => f.Data.Length == 0x2000 && f.Name.Contains("invaders", StringComparison.OrdinalIgnoreCase)))
            return 80;
        return found > 0 ? 30 : 0;
    }

    public static (byte[] rom, string description) Build(IReadOnlyList<MediaFile> files)
    {
        var rom = new byte[0x2000];
        bool allChips = true, verified = true;
        foreach (var (name, offset, crc) in Chips)
        {
            var f = files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Data.Length == 0x800);
            if (f == null)
            {
                allChips = false;
                break;
            }
            f.Data.CopyTo(rom, offset);
            if (Crc32.Compute(f.Data) != crc)
                verified = false;
        }
        if (!allChips)
        {
            var single = files.FirstOrDefault(x => x.Data.Length == 0x2000)
                ?? throw new InvalidDataException("Faltan chips de Space Invaders: se necesitan invaders.h, .g, .f y .e (2 KB cada uno).");
            single.Data.CopyTo(rom, 0);
            verified = Chips.All(c => Crc32.Compute(rom.AsSpan(c.offset, 0x800)) == c.crc);
        }
        return (rom, verified ? "ROM de Space Invaders verificada (CRC de la versión original de Midway)."
                              : "ROM de Space Invaders cargada (no coincide con los CRC conocidos: puede ser otra versión).");
    }
}
