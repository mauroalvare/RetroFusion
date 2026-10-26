using Retro.Systems.Nes;

namespace Retro.Compat;

/// <summary>Observes action-driven vertical motion of a sprite already controlled horizontally.</summary>
internal static class NesActionProbe
{
    private sealed record Sample(byte[] Ram, byte[] PreviousRam, byte[] Oam);
    private static int Delta(int a, int b) => (sbyte)(b - a);

    public static void Observe(Nes machine, List<LabFinding> findings, LabReport report, CancellationToken ct)
    {
        byte[] state = machine.SaveState();
        var sink = new short[8192];
        Sample Capture(byte[] previous) => new((byte[])machine.Ram.Clone(), previous, (byte[])machine.Ppu.Oam.Clone());
        List<Sample> Run(string? key, int frames, int[] checkpoints, int release)
        {
            machine.LoadState(state); machine.ReleaseAllKeys();
            if (key != null) machine.SetKey(key, true);
            var samples = new List<Sample> { Capture((byte[])machine.Ram.Clone()) };
            for (int frame = 1; frame <= frames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                if (frame == release && key != null) machine.SetKey(key, false);
                byte[] previous = (byte[])machine.Ram.Clone();
                machine.RunFrame(null); machine.DrainAudio(sink);
                if (checkpoints.Contains(frame)) samples.Add(Capture(previous));
            }
            return samples;
        }
        try
        {
            var horizontalIdle = Run(null, 16, [8, 16], 17);
            var left = Run("P1 LEFT", 16, [8, 16], 17);
            var right = Run("P1 RIGHT", 16, [8, 16], 17);
            var controlledSprites = new List<int>();
            for (int sprite = 0; sprite < 64; sprite++)
            {
                int field = sprite * 4 + 3;
                if (left[^1].Oam[field - 3] >= 239 || right[^1].Oam[field - 3] >= 239) continue;
                int dxLeft = Delta(horizontalIdle[^1].Oam[field], left[^1].Oam[field]);
                int dxRight = Delta(horizontalIdle[^1].Oam[field], right[^1].Oam[field]);
                if (Math.Abs(dxRight - dxLeft) < 2 || dxLeft != 0 && dxRight != 0 && Math.Sign(dxLeft) == Math.Sign(dxRight)) continue;
                bool Matches(int address, bool previous) => Enumerable.Range(1, 2).All(i =>
                {
                    var baseline = previous ? horizontalIdle[i].PreviousRam : horizontalIdle[i].Ram;
                    var minus = previous ? left[i].PreviousRam : left[i].Ram;
                    var plus = previous ? right[i].PreviousRam : right[i].Ram;
                    return Delta(baseline[address], minus[address]) == Delta(horizontalIdle[i].Oam[field], left[i].Oam[field]) &&
                        Delta(baseline[address], plus[address]) == Delta(horizontalIdle[i].Oam[field], right[i].Oam[field]);
                });
                int address = Enumerable.Range(0, 0x800).Where(a => a is not (>= 0x100 and < 0x300))
                    .FirstOrDefault(a => Matches(a, false) || Matches(a, true), -1);
                if (address < 0) continue;
                controlledSprites.Add(sprite);
                string location = address.ToString("X4");
                string evidence = $"Su movimiento con LEFT/RIGHT coincide con la X del registro OAM {sprite} en tres muestras de cada corrida y frente a la corrida sin entradas, con retardo constante de cero o un cuadro.";
                var existing = findings.FirstOrDefault(f => f.Kind == "position-x" && f.Address == location);
                if (existing != null)
                {
                    existing.Confidence = Math.Max(0.9, existing.Confidence);
                    if (!existing.Evidence.Contains(evidence)) existing.Evidence.Add(evidence);
                }
                else findings.Add(new LabFinding
                {
                    Kind = "position-x", Address = location, Confidence = 0.9,
                    Hypothesis = "Coordenada horizontal asociada a una entidad controlada; no acredita posición de mundo ni cámara.",
                    Evidence = { evidence },
                    ExpectedEffect = "Leerla sigue el desplazamiento observado de esa entidad.",
                    HowToVerify = "Comparar su trayectoria en otros tramos y con desplazamiento de cámara.",
                });
            }
            var controlled = controlledSprites.ToArray();
            if (controlled.Length == 0)
            {
                report.Notes.Add("El sondeo de acciones NES no pudo vincular una entidad OAM con el desplazamiento horizontal observado.");
                return;
            }
            int[] checkpoints = [4, 8, 12, 16, 24, 36, 60, 90, 120];
            var idle = Run(null, 120, checkpoints, 9);
            foreach (string key in new[] { "P1 A", "P1 B" })
            {
                var action = Run(key, 120, checkpoints, 9);
                foreach (int sprite in controlled)
                {
                    int oam = sprite * 4;
                    int[] motion = Enumerable.Range(0, idle.Count).Select(i => Delta(idle[i].Oam[oam], action[i].Oam[oam])).ToArray();
                    if (motion.Count(d => Math.Abs(d) >= 2) < 2) continue;
                    bool returns = Math.Abs(motion[^1]) <= 2;
                    bool rises = motion.Count(d => d <= -2) >= 2;
                    if (rises && returns && !report.CheckedControls.ContainsKey("jump"))
                    {
                        report.CheckedControls["jump"] = key;
                        report.Notes.Add($"{key} produjo una excursión vertical y regreso de una entidad OAM controlada horizontalmente (registro {sprite}); se conserva como salto observado.");
                    }
                    int added = 0;
                    for (int address = 0; address < 0x800 && added < 4; address++)
                    {
                        if (address is >= 0x100 and < 0x200) continue;
                        bool Matches(bool previous) => Enumerable.Range(0, idle.Count).All(i =>
                            Delta((previous ? idle[i].PreviousRam : idle[i].Ram)[address],
                                (previous ? action[i].PreviousRam : action[i].Ram)[address]) == motion[i]);
                        bool matches = Matches(false) || Matches(true);
                        if (!matches || findings.Any(f => f.Kind == "position-y" && f.Address == address.ToString("X4"))) continue;
                        findings.Add(new LabFinding
                        {
                            Kind = "position-y", Address = address.ToString("X4"), Confidence = 0.85,
                            Hypothesis = "Coordenada vertical de una entidad controlada; falta confirmar su significado en el juego.",
                            Evidence = { $"{key}: su diferencia con la corrida sin entradas coincide con la Y del registro OAM {sprite} en {idle.Count} muestras, con retardo constante de cero o un cuadro; LEFT/RIGHT ya mueven esa entidad." },
                            ExpectedEffect = "Modificarla puede desplazar la entidad si no es una copia recalculada.",
                            HowToVerify = "Repetir desde el mismo estado con otro valor y comparar imagen y escrituras.",
                        });
                        added++;
                    }
                }
            }
        }
        finally { machine.LoadState(state); machine.ReleaseAllKeys(); }
    }
}
