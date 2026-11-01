using System.Globalization;

namespace Retro.App;

public enum AppOptionKind { Toggle, Choice, Number }
public sealed record OptionChoice(string Value, string Label);

/// <summary>One live setting contract for the graphical form and the internal terminal.</summary>
public sealed record AppOption(string Id, string Title, string Group, string Description, AppOptionKind Kind,
    Func<string> Read, Func<string, string?> Write, IReadOnlyList<OptionChoice>? Choices = null, int Min = 0, int Max = 100)
{
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
