using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Fusion;

public sealed class RecipeGame
{
    /// <summary>Short name used in rules: a, b, c.</summary>
    public string Slot { get; set; } = "a";
    public string Profile { get; set; } = "";
}

public sealed class RecipeLayout
{
    /// <summary>side (side by side), overlay (one on top of the other), pip (picture in picture).</summary>
    public string Mode { get; set; } = "side";
    /// <summary>Slot drawn full size in overlay/pip modes.</summary>
    public string? Base { get; set; }
    /// <summary>Opacity of the games drawn over the base (overlay).</summary>
    public double Opacity { get; set; } = 0.6;
}

public sealed class RecipeRule
{
    /// <summary>"a.aliens decreases", "b.air below 40", "a.ships equals 0", "b.score increases".</summary>
    public string? When { get; set; }
    /// <summary>Instead of When: fire every N ticks (60 per second).</summary>
    public int? Every { get; set; }
    /// <summary>"b.air += 3", "a.ships -= 1", "a.score = b.score", "press b.jump 10", "pause a 120", "swap".</summary>
    public List<string> Do { get; set; } = new();
    public string? Message { get; set; }
    /// <summary>Minimum ticks between two firings of this rule.</summary>
    public int Cooldown { get; set; }
}

/// <summary>
/// A fusion: two or three games running at the same time, drawn together, sharing
/// controls, and linked by rules that read and write each game's RAM.
/// </summary>
public sealed class FusionRecipe
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public List<RecipeGame> Games { get; set; } = new();
    public RecipeLayout Layout { get; set; } = new();
    /// <summary>Player input (left, right, up, down, fire, jump, start, coin) -> "slot.control" list.</summary>
    public Dictionary<string, List<string>> Controls { get; set; } = new();
    public List<RecipeRule> Rules { get; set; } = new();

    public static readonly string[] Inputs = { "left", "right", "up", "down", "fire", "jump", "start", "coin" };

    public static FusionRecipe Parse(string json)
    {
        // Accept a JSON object embedded in prose or a ```json block (AI answers).
        int start = json.IndexOf('{'), end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidDataException("No encontré un objeto JSON en la respuesta.");
        return JsonSerializer.Deserialize<FusionRecipe>(json[start..(end + 1)], GameProfile.Json)
               ?? throw new InvalidDataException("Receta vacía.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, GameProfile.Json);

    /// <summary>Every problem, in words an AI (or a person) can act on. Empty = valid.</summary>
    public List<string> Validate(Func<string, GameProfile?> findProfile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Falta \"name\".");
        if (Games.Count is < 2 or > 3)
            errors.Add("\"games\" debe tener 2 o 3 juegos.");
        var slots = new Dictionary<string, GameProfile>();
        foreach (var g in Games)
        {
            var p = findProfile(g.Profile);
            if (p == null)
                errors.Add($"El perfil \"{g.Profile}\" no existe. Perfiles disponibles: los de la lista que te pasé.");
            else if (!slots.TryAdd(g.Slot, p))
                errors.Add($"El slot \"{g.Slot}\" está repetido.");
        }
        if (Layout.Mode is not ("side" or "overlay" or "pip"))
            errors.Add("\"layout.mode\" debe ser side, overlay o pip.");
        if (Layout.Mode != "side" && Layout.Base != null && !slots.ContainsKey(Layout.Base))
            errors.Add($"\"layout.base\" = \"{Layout.Base}\" no es un slot de la receta.");
        if (Layout.Opacity is < 0.1 or > 1)
            errors.Add("\"layout.opacity\" debe estar entre 0.1 y 1.");

        foreach (var (input, targets) in Controls)
        {
            if (!Inputs.Contains(input))
                errors.Add($"Control \"{input}\" desconocido. Usá: {string.Join(", ", Inputs)}.");
            foreach (var t in targets)
                CheckControl(t, slots, errors, $"controls.{input}");
        }
        if (!Controls.ContainsKey("left") || !Controls.ContainsKey("right"))
            errors.Add("Faltan los controles \"left\" y \"right\".");

        for (int i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            string where = $"rules[{i}]";
            if (r.When == null && r.Every == null)
                errors.Add($"{where}: necesita \"when\" o \"every\".");
            if (r.When != null)
            {
                try
                {
                    var c = RuleCondition.Parse(r.When);
                    CheckVariable(c.Slot, c.Variable, slots, errors, where, write: false);
                }
                catch (FormatException e)
                {
                    errors.Add($"{where}.when: {e.Message}");
                }
            }
            if (r.Every is < 10)
                errors.Add($"{where}.every: mínimo 10 ticks.");
            if (r.Do.Count == 0)
                errors.Add($"{where}: \"do\" está vacío.");
            foreach (var d in r.Do)
            {
                try
                {
                    var a = RuleAction.Parse(d);
                    switch (a.Kind)
                    {
                        case ActionKind.Set:
                        case ActionKind.Add:
                            CheckVariable(a.Slot!, a.Variable!, slots, errors, where, write: true);
                            if (a.SourceSlot != null)
                                CheckVariable(a.SourceSlot, a.SourceVariable!, slots, errors, where, write: false);
                            break;
                        case ActionKind.Press:
                            CheckControl($"{a.Slot}.{a.Variable}", slots, errors, where);
                            break;
                        case ActionKind.Pause:
                            if (!slots.ContainsKey(a.Slot!))
                                errors.Add($"{where}: \"pause {a.Slot}\": el slot no existe.");
                            break;
                    }
                }
                catch (FormatException e)
                {
                    errors.Add($"{where}.do \"{d}\": {e.Message}");
                }
            }
        }
        return errors;
    }

    private static void CheckVariable(string slot, string variable, Dictionary<string, GameProfile> slots, List<string> errors, string where, bool write)
    {
        if (!slots.TryGetValue(slot, out var p))
        {
            errors.Add($"{where}: el slot \"{slot}\" no existe.");
            return;
        }
        if (!p.Variables.TryGetValue(variable, out var v))
        {
            errors.Add($"{where}: {p.Id} no tiene la variable \"{variable}\". Tiene: {string.Join(", ", p.Variables.Keys)}.");
            return;
        }
        if (write && v.ReadOnly)
            errors.Add($"{where}: {p.Id}.{variable} es de solo lectura (no se puede escribir).");
    }

    private static void CheckControl(string target, Dictionary<string, GameProfile> slots, List<string> errors, string where)
    {
        var parts = target.Split('.');
        if (parts.Length != 2 || !slots.TryGetValue(parts[0], out var p))
        {
            errors.Add($"{where}: \"{target}\" debe ser slot.control (ej. a.left).");
            return;
        }
        if (!p.Controls.ContainsKey(parts[1]))
            errors.Add($"{where}: {p.Id} no tiene el control \"{parts[1]}\". Tiene: {string.Join(", ", p.Controls.Keys)}.");
    }
}

public enum ConditionKind { Increases, Decreases, Changes, Equals, Above, Below }

public sealed record RuleCondition(string Slot, string Variable, ConditionKind Kind, int Value)
{
    private static readonly Regex Pattern = new(
        @"^\s*([a-z])\.([a-zA-Z_][a-zA-Z0-9_]*)\s+(increases|decreases|changes|equals|above|below)\s*(-?\d+)?\s*$",
        RegexOptions.IgnoreCase);

    public static RuleCondition Parse(string text)
    {
        var m = Pattern.Match(text);
        if (!m.Success)
            throw new FormatException("formato: \"slot.variable increases|decreases|changes|equals N|above N|below N\".");
        var kind = Enum.Parse<ConditionKind>(m.Groups[3].Value, true);
        bool needsValue = kind is ConditionKind.Equals or ConditionKind.Above or ConditionKind.Below;
        if (needsValue && !m.Groups[4].Success)
            throw new FormatException($"\"{m.Groups[3].Value}\" necesita un número.");
        int value = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        return new RuleCondition(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, kind, value);
    }

    /// <summary>Edge triggered: true when the change between ticks matches.</summary>
    public bool Fires(int before, int now) => Kind switch
    {
        ConditionKind.Increases => now > before,
        ConditionKind.Decreases => now < before,
        ConditionKind.Changes => now != before,
        ConditionKind.Equals => now == Value && before != Value,
        ConditionKind.Above => now > Value && before <= Value,
        _ => now < Value && before >= Value,
    };
}

public enum ActionKind { Set, Add, Press, Pause, Message, Swap }

public sealed record RuleAction(ActionKind Kind, string? Slot, string? Variable, int Amount, string? SourceSlot, string? SourceVariable, string? Text)
{
    private static readonly Regex Assign = new(@"^\s*([a-z])\.([a-zA-Z_]\w*)\s*(=|\+=|-=)\s*(?:(-?\d+)|([a-z])\.([a-zA-Z_]\w*))\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Press = new(@"^\s*press\s+([a-z])\.([a-zA-Z_]\w*)\s+(\d+)\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex PauseRe = new(@"^\s*pause\s+([a-z])\s+(\d+)\s*$", RegexOptions.IgnoreCase);

    public static RuleAction Parse(string text)
    {
        var m = Assign.Match(text);
        if (m.Success)
        {
            string op = m.Groups[3].Value;
            var kind = op == "=" ? ActionKind.Set : ActionKind.Add;
            int amount = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
            if (op == "-=")
                amount = -amount;
            if (m.Groups[5].Success && op != "=")
                throw new FormatException("solo se puede copiar con \"=\" (ej. a.score = b.score).");
            return new RuleAction(kind, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, amount,
                m.Groups[5].Success ? m.Groups[5].Value.ToLowerInvariant() : null, m.Groups[6].Success ? m.Groups[6].Value : null, null);
        }
        m = Press.Match(text);
        if (m.Success)
            return new RuleAction(ActionKind.Press, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, int.Parse(m.Groups[3].Value), null, null, null);
        m = PauseRe.Match(text);
        if (m.Success)
            return new RuleAction(ActionKind.Pause, m.Groups[1].Value.ToLowerInvariant(), null, int.Parse(m.Groups[2].Value), null, null, null);
        if (text.Trim().Equals("swap", StringComparison.OrdinalIgnoreCase))
            return new RuleAction(ActionKind.Swap, null, null, 0, null, null, null);
        if (text.TrimStart().StartsWith("message ", StringComparison.OrdinalIgnoreCase))
            return new RuleAction(ActionKind.Message, null, null, 0, null, null, text.Trim()[8..].Trim().Trim('"'));
        throw new FormatException("acciones válidas: \"s.var = N\", \"s.var += N\", \"s.var -= N\", \"s.var = t.var\", \"press s.control N\", \"pause s N\", \"swap\", \"message texto\".");
    }
}

/// <summary>Built-in recipes shipped with the emulator, plus the saved ones.</summary>
public static class RecipeLibrary
{
    private static List<FusionRecipe>? _builtIn;

    public static IReadOnlyList<FusionRecipe> BuiltIn
    {
        get
        {
            if (_builtIn != null)
                return _builtIn;
            var asm = Assembly.GetExecutingAssembly();
            _builtIn = asm.GetManifestResourceNames()
                .Where(n => n.Contains(".Recipes.") && n.EndsWith(".json"))
                .Select(n =>
                {
                    using var s = asm.GetManifestResourceStream(n)!;
                    using var r = new StreamReader(s);
                    return FusionRecipe.Parse(r.ReadToEnd());
                })
                .ToList();
            return _builtIn;
        }
    }

    public static string UserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "recipes");

    public static IReadOnlyList<FusionRecipe> All()
    {
        var list = new List<FusionRecipe>(BuiltIn);
        if (Directory.Exists(UserFolder))
            foreach (var f in Directory.GetFiles(UserFolder, "*.json"))
            {
                try
                {
                    var r = FusionRecipe.Parse(File.ReadAllText(f));
                    list.RemoveAll(x => x.Id == r.Id);
                    list.Add(r);
                }
                catch (Exception e) when (e is JsonException or InvalidDataException)
                {
                }
            }
        return list;
    }

    /// <summary>Recipes whose games are exactly these profiles (any order).</summary>
    public static IEnumerable<FusionRecipe> For(IEnumerable<string> profileIds)
    {
        var set = profileIds.OrderBy(x => x).ToList();
        return All().Where(r => r.Games.Select(g => g.Profile).OrderBy(x => x).SequenceEqual(set));
    }

    public static string Save(FusionRecipe recipe)
    {
        Directory.CreateDirectory(UserFolder);
        if (string.IsNullOrWhiteSpace(recipe.Id))
            recipe.Id = Regex.Replace(recipe.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        string path = Path.Combine(UserFolder, recipe.Id + ".json");
        File.WriteAllText(path, recipe.ToJson());
        return path;
    }
}
