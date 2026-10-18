using Retro.Core;
using Retro.Systems.Spectrum.Tape;

namespace Retro.Systems.Spectrum;

public enum SpectrumMediaKind { Tape, Snapshot }

public sealed record SpectrumLoadResult(SpectrumMediaKind Kind, string Description, IReadOnlyList<string> Warnings);

/// <summary>Recognises and loads ZX Spectrum files: TAP, TZX (tapes), Z80, SNA (snapshots).</summary>
public static class SpectrumMedia
{
    public static readonly string[] Extensions = { ".tap", ".tzx", ".z80", ".sna" };

    /// <summary>0 = not a Spectrum file, 100 = certain.</summary>
    public static int Probe(MediaFile f)
    {
        var d = f.Data;
        return f.Extension switch
        {
            ".tzx" => TzxLoader.Probe(d) ? 100 : 0,
            ".tap" => TapLoader.Probe(d) ? 95 : 0,       // C64/C16 .tap files fail the block chain check
            ".z80" => Z80Snapshot.Probe(d) ? 90 : 0,
            ".sna" => SnaSnapshot.Probe(d) ? 90 : 0,
            _ => TzxLoader.Probe(d) ? 80 : 0,
        };
    }

    public static SpectrumLoadResult Load(Spectrum48 machine, MediaFile f)
    {
        var warnings = new List<string>();
        switch (f.Extension)
        {
            case ".tap":
            {
                var tape = TapLoader.Load(f.Data, f.Name);
                machine.InsertTape(tape);
                return new SpectrumLoadResult(SpectrumMediaKind.Tape, $"Cinta TAP con {tape.Blocks.Count} bloques", warnings);
            }
            case ".tzx":
            {
                var tape = TzxLoader.Load(f.Data, f.Name, warnings);
                machine.InsertTape(tape);
                return new SpectrumLoadResult(SpectrumMediaKind.Tape, $"Cinta TZX con {tape.Blocks.Count} bloques", warnings);
            }
            case ".z80":
                Z80Snapshot.Load(machine, f.Data);
                return new SpectrumLoadResult(SpectrumMediaKind.Snapshot, "Snapshot Z80", warnings);
            case ".sna":
                SnaSnapshot.Load(machine, f.Data);
                return new SpectrumLoadResult(SpectrumMediaKind.Snapshot, "Snapshot SNA", warnings);
            default:
                if (TzxLoader.Probe(f.Data))
                    goto case ".tzx";
                throw new InvalidDataException($"Formato no reconocido para ZX Spectrum: {f.Name}");
        }
    }
}
