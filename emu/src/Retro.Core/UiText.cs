using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Core;

/// <summary>Offline UI catalog. Technical identifiers, paths and persisted documents keep their original values.</summary>
public static class UiText
{
    private static readonly AsyncLocal<string?> Current = new();
    public static string Language { get => Current.Value ?? "es"; set => Current.Value = value == "en" ? "en" : "es"; }
    public static bool English => Language == "en";
    private sealed record RawValue(object? Value)
    {
        public override string ToString() => Value?.ToString() ?? "";
    }
    public static object Raw(object? value) => new RawValue(value);
    private static readonly Regex Placeholders = new(@"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}", RegexOptions.CultureInvariant);
    private static string Key(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
    private static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("Retro.Core.Localization.en.json");
        var original = stream == null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, english) in original) catalog.TryAdd(Key(source), Key(english));
        foreach (var (source, english) in catalog.ToArray())
        {
            int hidden = source.IndexOf("##", StringComparison.Ordinal);
            int translatedHidden = english.IndexOf("##", StringComparison.Ordinal);
            if (hidden > 0) catalog.TryAdd(source[..hidden], translatedHidden < 0 ? english : english[..translatedHidden]);
        }
        return catalog;
    });
    private sealed record DiagnosticPattern(string Source, string Translation, string Prefix, string Required, Regex? Pattern);
    private static readonly Lazy<DiagnosticPattern[]> Diagnostics = new(() => Catalog.Value
        .Where(pair => pair.Key != pair.Value && pair.Key.Length >= 8)
        .OrderByDescending(pair => pair.Key.Length)
        .Select(pair =>
        {
            var parts = Regex.Split(pair.Key, @"\{\d+\}");
            if (parts.Length == 1) return new DiagnosticPattern(pair.Key, pair.Value, pair.Key, pair.Key, null);
            string pattern = ""; int offset = 0;
            foreach (Match placeholder in Regex.Matches(pair.Key, @"\{(\d+)\}"))
            {
                pattern += Regex.Escape(pair.Key[offset..placeholder.Index]) + "(?<p" + placeholder.Groups[1].Value + ">.*?)";
                offset = placeholder.Index + placeholder.Length;
            }
            pattern += Regex.Escape(pair.Key[offset..]);
            return new DiagnosticPattern(pair.Key, pair.Value, parts[0], parts.MaxBy(part => part.Length)!,
                new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20)));
        }).ToArray());
    public static string Get(string? source)
    {
        if (source == null) return "";
        if (!English || source.Length == 0) return source;
        return Catalog.Value.GetValueOrDefault(Key(source), source);
    }

    /// <summary>Only for program-generated diagnostics, never titles, paths, source code or other user data.</summary>
    public static string Message(string source)
    {
        if (!English) return source;
        source = Key(source);
        string direct = Get(source);
        if (direct != source) return direct;
        foreach (var item in Diagnostics.Value)
        {
            if (!source.StartsWith(item.Prefix, StringComparison.Ordinal) || !source.Contains(item.Required, StringComparison.Ordinal)) continue;
            if (item.Pattern != null)
            {
                Match match;
                try { match = item.Pattern.Match(source); }
                catch (RegexMatchTimeoutException) { continue; }
                if (match.Success) return Regex.Replace(item.Translation, @"\{(\d+)\}", placeholder => match.Groups["p" + placeholder.Groups[1].Value].Value);
            }
            else return item.Translation + Message(source[item.Source.Length..]);
        }
        if (source.Contains('\n')) return string.Join('\n', source.Split('\n').Select(line => Get(line)));
        return source;
    }

    public static string Format(FormattableString source)
    {
        if (!English) return source.ToString(CultureInfo.InvariantCulture);
        var placeholders = Placeholders.Matches(source.Format);
        string key = Placeholders.Replace(source.Format, match => "{" + match.Groups[1].Value + "}");
        object?[] arguments = source.GetArguments();
        if (!Catalog.Value.TryGetValue(Key(key), out string? translated))
        {
            var format = new System.Text.StringBuilder(); int offset = 0;
            foreach (Match placeholder in placeholders)
            {
                format.Append(Get(source.Format[offset..placeholder.Index])).Append(placeholder.Value);
                offset = placeholder.Index + placeholder.Length;
            }
            format.Append(Get(source.Format[offset..]));
            return string.Format(CultureInfo.InvariantCulture, format.ToString(), arguments);
        }
        var formats = placeholders.GroupBy(match => match.Groups[1].Value).ToDictionary(group => group.Key, group => group.First().Value);
        return Regex.Replace(translated, @"\{(\d+)\}", match =>
        {
            string id = match.Groups[1].Value;
            return string.Format(CultureInfo.InvariantCulture, formats.GetValueOrDefault(id, match.Value), arguments);
        });
    }

    /// <summary>Visible labels change language; their ImGui identity does not.</summary>
    public static string Label(string? source)
    {
        if (source == null) return null!;
        if (source.StartsWith("##", StringComparison.Ordinal)) return source;
        int identity = source.IndexOf("###", StringComparison.Ordinal);
        int hidden = source.IndexOf("##", StringComparison.Ordinal);
        string visible = hidden < 0 ? source : source[..hidden];
        string translated = Catalog.Value.GetValueOrDefault(Key(visible), visible);
        if (translated == visible) return source;
        return (English ? translated : visible) + (identity < 0 ? "###" + source : source[identity..]);
    }

    public static string StartupLanguage(string[] args)
    {
        int index = Array.IndexOf(args, "--language");
        if (index >= 0 && index + 1 < args.Length && args[index + 1] is "es" or "en") return args[index + 1];
        if (args.Contains("--autotest")) return "es";
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "preferencias.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return "es";
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.TryGetProperty("Language", out var language) && language.GetString() == "en" ? "en" : "es";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return "es"; }
    }
}
