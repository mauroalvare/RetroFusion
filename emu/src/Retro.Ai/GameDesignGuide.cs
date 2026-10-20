namespace Retro.Ai;

/// <summary>Shared design contract for every provider, manual prompt and agent workspace.</summary>
public static class GameDesignGuide
{
    public const string FileName = "DISENO-JUEGOS.md";
    public static string Text { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(GameDesignGuide).Assembly.GetManifestResourceStream("Retro.Ai.Docs." + FileName)
            ?? throw new InvalidOperationException("Falta el contrato de diseño de juegos.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
