using System.Globalization;

namespace Retro.App;

public enum AppOptionKind { Toggle, Choice, Number }
/// <summary>How far a change reaches, shown next to the setting.</summary>
public enum OptionScope { Saved, Session }
public sealed record OptionChoice(string Value, string Label);

/// <summary>One live setting contract for the graphical form and the internal terminal.</summary>
public sealed record AppOption(string Id, string Title, string Group, string Description, AppOptionKind Kind,
    Func<string> Read, Func<string, string?> Write, IReadOnlyList<OptionChoice>? Choices = null, int Min = 0, int Max = 100)
{
    /// <summary>Value of a fresh installation; a setting without one depends on the equipment.</summary>
    public string? Default { get; init; }
    public OptionScope Scope { get; init; } = OptionScope.Saved;
    public string ScopeName => Scope == OptionScope.Saved ? "se guarda para todos los estilos" : "solo esta sesión";
    public string Values => Kind switch
    {
        AppOptionKind.Toggle => "on, off",
        AppOptionKind.Choice => string.Join(", ", Choices!.Select(choice => choice.Value)),
        _ => $"{Min}..{Max}"
    };
    public bool Modified => Default != null && Read() != Default;
    public IEnumerable<CompletionItem> Completions()
    {
        IEnumerable<CompletionItem> values = Kind switch
        {
            AppOptionKind.Toggle => [new("on", "activar"), new("off", "desactivar")],
            AppOptionKind.Choice => Choices!.Select(choice => new CompletionItem(choice.Value, choice.Label)),
            _ => []
        };
        return Default == null ? values : values.Append(new("restablecer", "volver a " + Default));
    }

    public static AppOption Toggle(string id, string title, string group, string description, Func<bool> read, Action<bool> write) =>
        new(id, title, group, description, AppOptionKind.Toggle, () => read() ? "on" : "off", value =>
        {
            bool? parsed = value.ToLowerInvariant() switch { "on" or "si" or "sí" or "true" or "1" => true, "off" or "no" or "false" or "0" => false, _ => null };
            if (parsed == null) return "Usá on u off.";
            write(parsed.Value); return null;
        });
    public static AppOption Choice(string id, string title, string group, string description, Func<string> read, Action<string> write, params OptionChoice[] choices) =>
        new(id, title, group, description, AppOptionKind.Choice, read, value =>
        {
            var choice = choices.FirstOrDefault(choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (choice == null) return "Valores: " + string.Join(", ", choices.Select(choice => choice.Value));
            write(choice.Value); return null;
        }, Array.AsReadOnly(choices));
    public static AppOption Number(string id, string title, string group, string description, Func<int> read, Action<int> write, int min, int max) =>
        new(id, title, group, description, AppOptionKind.Number, () => read().ToString(CultureInfo.InvariantCulture), value =>
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < min || number > max) return $"Usá un número entre {min} y {max}.";
            write(number); return null;
        }, Min: min, Max: max);
}
