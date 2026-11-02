using System.Globalization;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Compat;

/// <summary>Observe RAM near known fields on a private research machine; never infer a purpose from adjacency.</summary>
public static class PreparationNeighborhoods
{
    public static List<LabFinding> Observe(IMachine machine, GameProfile? previous, int frames = 900, CancellationToken ct = default)
    {
        if (frames is < 1 or > 1800) throw new ArgumentOutOfRangeException(nameof(frames));
        if (machine.Id is not ("zx48" or "c64" or "nes" or "invaders") || previous == null || previous.Variables.Count == 0) return [];
        ct.ThrowIfCancellationRequested();
        var range = MemoryLab.RangeFor(machine.Id);
        var known = new HashSet<uint>(); var addresses = new Dictionary<uint,string>();
        foreach (var pair in previous.Variables.Take(24))
            if (uint.TryParse(pair.Value.Address,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint address)) known.Add(address);
        foreach (var pair in previous.Variables.OrderByDescending(p=>p.Key.Contains("timer",StringComparison.OrdinalIgnoreCase) || p.Key.Contains("clock",StringComparison.OrdinalIgnoreCase)).Take(12))
        {
            if (!uint.TryParse(pair.Value.Address,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint address)) continue;
            for (int offset=-2;offset<=2;offset++)
            {
                long at=(long)address+offset;
                if (at<range.start || at>range.end || known.Contains((uint)at)) continue;
                addresses.TryAdd((uint)at,pair.Key);
            }
        }
        if (addresses.Count==0) return [];
        byte[] before=machine.SaveState(); var picture=(int[])machine.Video.Pixels.Clone(); var sink=new short[8192];
        var samples=addresses.Keys.ToDictionary(a=>a,a=>new List<byte>{machine.Memory.Peek(a)});
        try
        {
            for (int frame=0;frame<frames;frame++)
            {
                ct.ThrowIfCancellationRequested(); machine.RunFrame(null); machine.DrainAudio(sink);
                if (frame%6==0) foreach (var pair in samples) pair.Value.Add(machine.Memory.Peek(pair.Key));
            }
        }
        finally { machine.LoadState(before); picture.CopyTo(machine.Video.Pixels,0); }
        return samples.Where(pair=>pair.Value.Distinct().Count()>1).Select(pair=>new LabFinding {
            Address=pair.Key.ToString("X4"),Kind="counter",Encoding="uint8",Bytes=1,Confidence=0.7,
            Hypothesis="Byte cambiante junto a un dato conocido; función aún sin identificar.",
            Evidence=[Retro.Core.UiText.Format($"Vecino de {addresses[pair.Key]}, observado durante {frames} cuadros; {pair.Value.Distinct().Count()} valores distintos, mínimo {pair.Value.Min()}, máximo {pair.Value.Max()}.")],
            ExpectedEffect="Una escritura puede distinguir almacenamiento de una copia recalculada; no prueba su significado.",
            HowToVerify="Comparar con el original desde el mismo estado, comprobar la escritura y observar cruces de cifras o transiciones antes de asignarle una función."
        }).Take(6).ToList();
    }
}
