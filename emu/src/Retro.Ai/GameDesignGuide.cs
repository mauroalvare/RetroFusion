namespace Retro.Ai;

/// <summary>Shared design contract for every provider, manual prompt and agent workspace.</summary>
public static class GameDesignGuide
{
    public const string FileName = "DISENO-JUEGOS.md";
    public static string Text => Read(FileName);
    public static string Compact => Read("CONTRATO-DISENO.md");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Texts = new();

    private static string Read(string name)
    {
        if (Retro.Core.UiText.English) name = name.Replace(".md", "-EN.md", StringComparison.Ordinal);
        return Texts.GetOrAdd(name, ReadResource);
    }
    private static string ReadResource(string name)
    {
        using var stream = typeof(GameDesignGuide).Assembly.GetManifestResourceStream("Retro.Ai.Docs." + name)
            ?? throw new InvalidOperationException("Falta el contrato de diseño de juegos.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
