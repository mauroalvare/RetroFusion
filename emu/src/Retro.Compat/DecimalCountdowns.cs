namespace Retro.Compat;

/// <summary>Observes whole decimal fields, including carry/borrow, rather than naming an isolated digit.</summary>
public static class DecimalCountdowns
{
    public static IReadOnlyList<LabFinding> Find(IReadOnlyList<byte[]> samples, int start, int sampleEvery)
    {
        if (samples.Count < 6 || sampleEvery < 1) return [];
        int length = samples.Min(s => s.Length);
        var found = new List<LabFinding>();
        for (int offset = 0; offset < length - 1; offset++)
            for (int width = 2; width <= 4 && offset + width <= length; width++)
            {
                if (samples.All(s => s[offset] == 0)) continue;
                if (samples.Any(s => Enumerable.Range(offset, width).Any(i => s[i] > 9))) continue;
                int Read(byte[] s) { int n = 0; for (int i = 0; i < width; i++) n = n * 10 + s[offset + i]; return n; }
                var values = samples.Select(Read).ToArray();
                var changes = new List<int>(); bool valid = true;
                for (int i = 1; i < values.Length; i++)
                    if (values[i] != values[i - 1])
                    { if (values[i] != values[i - 1] - 1) { valid = false; break; } changes.Add(i * sampleEvery); }
                if (!valid || changes.Count < 5) continue;
                double cadence = (changes[^1] - changes[0]) / (double)(changes.Count - 1);
                if (cadence < sampleEvery || changes.Zip(changes.Skip(1), (a, b) => b - a).Any(d => Math.Abs(d - cadence) > sampleEvery)) continue;
                found.Add(new() { Kind = "timer", Address = $"{start + offset:X4}", Encoding = "digits", Bytes = width, Confidence = 0.72,
                    Hypothesis = $"Cuenta atrás decimal de {width} dígitos; la unidad y el propósito de juego requieren contraste.",
                    Evidence = [$"El campo completo baja de {values[0]} a {values[^1]} en {changes.Count} pasos unitarios. Intervalo observado aproximado: {cadence:F2} cuadros por unidad; muestreo cada {sampleEvery} cuadros."],
                    ExpectedEffect = "Escribir el número completo desplaza la cuenta sin corromper los dígitos; comprobar límites, acarreo/préstamo y uso por el juego.",
                    HowToVerify = "Comparar la cuenta completa frente a un control y comprobar cambios de cifra, cadencia y vencimiento. No inferir segundos reales a partir del número mostrado." });
            }
        return found.Where(f => !found.Any(other => other != f && Convert.ToInt32(other.Address, 16) <= Convert.ToInt32(f.Address, 16)
            && Convert.ToInt32(other.Address, 16) + other.Bytes >= Convert.ToInt32(f.Address, 16) + f.Bytes)).ToList();
    }
}
