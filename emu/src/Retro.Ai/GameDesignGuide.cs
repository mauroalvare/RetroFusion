namespace Retro.Ai;

/// <summary>Shared design contract for every provider, manual prompt and agent workspace.</summary>
public static class GameDesignGuide
{
    public const string FileName = "DISENO-JUEGOS.md";
    public static string Text { get; } = Read(FileName);
    public static string Compact { get; } = Read("CONTRATO-DISENO.md");

    private static string Read(string name)
    {
        using var stream = typeof(GameDesignGuide).Assembly.GetManifestResourceStream("Retro.Ai.Docs." + name)
            ?? throw new InvalidOperationException("Falta el contrato de diseño de juegos.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
