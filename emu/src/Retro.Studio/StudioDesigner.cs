using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Ai;
using Retro.Fusion;

namespace Retro.Studio;

public sealed record DesignOutcome(StudioGame? Game, ValidationReport? Report, int Attempts, string? Error, StudioGame? Draft = null);

/// <summary>
/// The AI side of fusion games. Three steps, each one a conversation with the player's AI:
/// 1. ideas: given the games (and maybe the player's wish), propose 3 different concepts;
/// 2. write: turn a concept into a script, try it without a window and send the errors
///    back until it runs (up to 3 attempts);
/// 3. revise: the player asks for a change ("the invaders are too fast", "now make it
///    co-op") and the AI writes the next version, tried the same way.
/// The same prompts work by copy and paste with any chat, and as a folder for agents.
/// </summary>
public sealed class StudioDesigner
{
    public const int MaxAttempts = 3;
    public const string MissingDataPrefix = "Faltan datos del original: ";
    public Action<DesignCheckpoint>? Checkpoint { get; init; }

    public static string ApiDoc { get; } = Resource("Docs.API.md");
    /// <summary>Mechanic-led design, individual presentation and playtesting; no universal game template.</summary>
    public static string Method { get; } = Resource("Docs.GUIA-FUSION.md");
    private static StudioGame? Example => StudioLibrary.BuiltIn.FirstOrDefault(g => g.Id == "manic-invaders") ?? StudioLibrary.BuiltIn.FirstOrDefault();
    public static string ExampleScript { get; } = Example?.Script ?? "";
    public static string ExampleDesign { get; } = Example?.Design ?? "";

    private static string Resource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("Retro.Studio." + name);
        if (s == null)
            return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static string CoreGuide => GameDesignGuide.Compact + "\n\n" + """
        ## Entorno de fusión
        JavaScript ES2020 conecta 2 o 3 máquinas emuladas, sin archivos, red, módulos ni .NET.
        La base indicada por el jugador es inmutable. El motor dibuja su imagen antes de
        draw(); agregá los aportes, sin screen.clear ni otra resolución. screen.base repone
        esa imagen. screen.game está retirado; no pegues pantallas completas secundarias.
        En modo libre, el guion dibuja una escena nueva con los recursos disponibles.

        Antes del código, concretá en design: acción/objetivo, regla transferida, consecuencias
        de quitar un original, datos usados y faltantes, diferencia frente a ideas previas,
        progreso/final, controles y presentación. No uses un juego previo como molde.
        Si falta algo indispensable, devolvé missingData con el dato concreto y no un juego
        genérico que lo disimule. Una captura no prueba conocer mapas, memoria ni rutinas.

        Informá game.state (title/playing/gameover cuando corresponda), puntos, muertes y
        progreso reales. La base puede empezar jugando sin título ni puntaje artificial.
        El robot prueba quieto, movimiento y disparo; no adaptes el género para conformarlo.
        Con la fuente de texto del Spectrum usá caracteres sin tildes ni eñes; ui sí las admite.
        """;

    public static string Guide => CoreGuide + "\n\n" + ApiDoc;

    public static string ConceptGuide => CoreGuide + "\n\n" + """
        Capacidades para evaluar ideas: g.get/set usan variables verificadas (respetá solo
        lectura); controles y ayudas específicas se enumeran en cada perfil. g.sprite usa
        gráficos de un bit; g.capture guarda recursos con colores, máximo 32x32 y 4096
        píxeles por original/partida. El guion integra choques/reglas y puede controlar,
        pausar, reiniciar, ajustar velocidad/volumen y emitir sonidos. No hay editor universal
        de mapas: ampliar niveles requiere su formato comprobado. La API completa se entrega
        al implementar; no escribas código en esta etapa.
        """;

    /// <summary>Everything the AI must know about each game: picture, variables, controls, sprites, helpers.</summary>
    public static string GamesText(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Alcance: solo están comprobados los datos y recursos descritos. Un perfil no equivale a conocer todos sus mapas o reglas. Si falta un dato esencial, indicá cuál; no lo sustituyas por decoración.");
        foreach (var slot in slots)
        {
            var p = find(slot.Profile);
            if (p == null)
                continue;
            sb.AppendLine($"### games.{slot.Slot}: {p.Name} (perfil \"{p.Id}\", máquina {p.Machine})");
            sb.AppendLine(p.Description);
            sb.AppendLine(p.Match.Sha256.Count > 0
                ? "Huellas reconocidas por este perfil (SHA-256): " + string.Join(", ", p.Match.Sha256)
                : "Este perfil no declara huellas SHA-256; no asumas que sus datos sirven para cualquier edición.");
            sb.AppendLine(p.Machine switch
            {
                "zx48" => "Imagen: 256x192 (ZX Spectrum, g.area dentro de un cuadro de 352x296 con borde).",
                "invaders" => "Imagen: 224x256, vertical (placa arcade).",
                "c64" => "Imagen: 320x200 (Commodore 64, g.area dentro de un cuadro de 384x272 con borde).",
                _ => "",
            });
            sb.AppendLine("Variables (g.get / g.set):");
            if (p.Variables.Count == 0)
                sb.AppendLine("No hay variables verificadas en este perfil. No inventes datos de memoria; indicá si el diseño necesita preparación adicional.");
            foreach (var (name, v) in p.Variables)
                sb.AppendLine($"- {name}{(v.ReadOnly ? " (solo lectura)" : "")}: {v.Description} [{v.Min}..{v.Max}]");
            sb.AppendLine("Controles (g.control): " + string.Join(", ", p.Controls.Select(c => $"{c.Key} = tecla {c.Value}")));
            if (p.Sprites.Count > 0)
            {
                sb.AppendLine("Sprites (g.sprite):");
                foreach (var (name, s) in p.Sprites)
                    sb.AppendLine($"- {name} ({s.Width}x{s.Height}): {s.Description}");
            }
            if (p.HelpersDoc.Count > 0)
            {
                sb.AppendLine("Ayudas del perfil:");
                foreach (var h in p.HelpersDoc)
                    sb.AppendLine("- " + h);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ 1. ideas
    /// <summary>The parts of the design document (see the method), in the order they are written.</summary>
    public static readonly (string Key, string Label)[] DesignFields =
    {
        ("baseGame", "Juego base"),
        ("hero", "Héroe"),
        ("contributions", "Aportes de cada original y decisiones que cambian"),
        ("mechanicLink", "Unión mecánica inseparable (qué cambia al quitar cualquiera de los juegos)"),
        ("uniqueRule", "Interacción incorporada y diez segundos concretos de partida"),
        ("difference", "Diferencia mecánica frente a las otras ideas y fusiones previas"),
        ("discarded", "Se descarta"),
        ("goal", "Objetivo"),
        ("lose", "Derrota"),
        ("story", "Historia"),
        ("levels", "Mapas y progresión conservados; ampliaciones compatibles si se pidieron"),
        ("controls", "Controles"),
        ("presentation", "Identidad visual, interfaz y sonido conservados o transformados según el modo elegido"),
        ("readability", "Legibilidad"),
    };

    private static readonly (string Key, string Label)[] ConceptFields = DesignFields
        .Where(f => f.Key is "contributions" or "mechanicLink" or "uniqueRule" or "difference" or "presentation").ToArray();

    public static JsonElement ConceptSchema()
    {
        var props = new Dictionary<string, object>
        {
            ["title"] = new { type = "string", maxLength = 60 },
            ["approach"] = new { type = "string", maxLength = 180 },
            ["pitch"] = new { type = "string", maxLength = 240 },
            ["howToPlay"] = new { type = "string", maxLength = 320 },
        };
        foreach (var (key, _) in ConceptFields)
            props[key] = new { type = "string", maxLength = 600 };
        props["missingData"] = new { type = "string", maxLength = 400 };
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "ideas" },
            properties = new
            {
                ideas = new
                {
                    type = "array",
                    items = new { type = "object", additionalProperties = false, required = props.Keys.ToArray(), properties = props },
                },
            },
        });
    }

    /// <summary>The design document as text, from the fields of an idea.</summary>
    public static string DesignText(JsonElement idea, string? chosenBase = null)
    {
        var sb = new StringBuilder();
        if (chosenBase != null) sb.AppendLine("- Juego base: " + chosenBase);
        foreach (var (key, label) in DesignFields)
        {
            if (chosenBase != null && key == "baseGame") continue;
            string v = Str(idea, key);
            if (v.Length > 0)
                sb.AppendLine($"- {label}: {v}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ConceptPrompt(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, string wish, FusionDirection? direction = null)
    {
        direction ??= new FusionDirection();
        bool keepBase = direction.Resolve(slots) != null;
        var sb = new StringBuilder();
        sb.AppendLine("Quiero unir estos originales en una fusión:");
        sb.AppendLine(DirectionText(slots, find, direction));
        sb.AppendLine();
        sb.Append(GamesText(slots, find));
        sb.AppendLine(string.IsNullOrWhiteSpace(wish)
            ? keepBase ? "Proponé 3 formas distintas de incorporar mecánicas de los aportantes dentro de la base elegida."
                : "Proponé 3 reinterpretaciones distintas entre sí (enfoques, géneros y presentaciones diferentes)."
            : $"Lo que quiere el jugador: \"{wish.Trim()}\". Proponé 3 ideas que respeten ese pedido, con enfoques distintos.");
        var existing = StudioLibrary.For(slots.Select(s => s.Profile)).Select(g => $"\"{g.Title}\" ({g.Approach})");
        sb.Append(keepBase ? CreativeSeeds.BaseBrief(existing)
            : CreativeSeeds.Brief(Random.Shared, 3, existing, !string.IsNullOrWhiteSpace(wish)));
        sb.Append(PreviousDesigns(slots));
        sb.AppendLine("Cada idea resume una partida completa con: title (nombre), approach " +
                      "(el enfoque de la mezcla en una frase), pitch (la premisa en una oración), howToPlay (objetivo y teclas en dos o tres líneas) y el " +
                      "ficha breve: " + string.Join(", ", ConceptFields.Select(f => $"{f.Key} ({f.Label.ToLowerInvariant()})")) + ". " +
                      "missingData queda vacío si es realizable; si requiere información ausente, indicá el dato concreto. Esa idea no se implementará hasta prepararlo. " +
                      "Tienen que ser realizables con la API y las ayudas de los perfiles. Conservá la base elegida; solo el modo libre permite reinventar la presentación. " +
                      "Antes de responder, revisá cada idea con la lista de control y con los anti-patrones, y descartá las que no la pasen.");
        return sb.ToString();
    }

    public async Task<List<GameConcept>> ConceptsAsync(IAiProvider ai, IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find,
        string wish, CancellationToken ct, FusionDirection? direction = null)
    {
        direction ??= new FusionDirection();
        direction = direction with { BaseSlot = direction.Resolve(slots) };
        string answer = await ai.CompleteAsync(new AiRequest(ConceptGuide, new[] { new AiTurn(true, ConceptPrompt(slots, find, wish, direction)) }, ConceptSchema()), ct);
        return ParseConcepts(answer, direction, wish);
    }

    public static List<GameConcept> ParseConcepts(string answer, FusionDirection? direction = null, string playerRequest = "")
    {
        var json = FirstJson(answer) ?? throw new AiException("La respuesta no tiene un JSON con las ideas.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("ideas", out var ideas) ? ideas : default;
        if (array.ValueKind != JsonValueKind.Array)
            throw new AiException("El JSON no tiene la lista \"ideas\".");
        var list = new List<GameConcept>();
        foreach (var e in array.EnumerateArray())
            list.Add(new GameConcept(Str(e, "title"), Str(e, "approach"), Str(e, "pitch"), Str(e, "howToPlay"),
                DesignText(e, direction?.BaseSlot is { } chosen ? $"Original elegido (games.{chosen})" : null), direction, playerRequest, Str(e, "missingData")));
        if (list.Count == 0)
            throw new AiException("La IA no propuso ninguna idea.");
        return list;
    }

    // ------------------------------------------------------------------ 2. write and 3. revise
    public static JsonElement GameSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "title", "approach", "pitch", "howToPlay", "design", "changes", "script", "missingData" },
        properties = new
        {
            title = new { type = "string", maxLength = 60 },
            approach = new { type = "string", maxLength = 180 },
            pitch = new { type = "string", maxLength = 240 },
            howToPlay = new { type = "string", maxLength = 320 },
            design = new { type = "string" },
            changes = new { type = "string", maxLength = 400 },
            script = new { type = "string" },
            missingData = new { type = "string", maxLength = 400 },
        },
    });

    private const string AnswerShape =
        "Devolvé title (hasta 60 caracteres), approach (hasta 180), pitch (una oración, hasta 240), howToPlay (objetivo y teclas, hasta 320), " +
        "design (el documento de diseño completo y actualizado, una línea por " +
        "parte: unión mecánica, regla única, diferencia, aportes, se descarta, objetivo, final, progresión, controles, presentación, legibilidad), changes (qué " +
        "hiciste o cambiaste, hasta 400 caracteres) y script (el guion de JavaScript COMPLETO, no solo los cambios). " +
        "missingData queda vacío si se puede implementar; si falta información indispensable, explicá qué dato se necesita y dejá script vacío. " +
        "El documento de diseño no debe aparecer como un tutorial extenso dentro del juego.";

    public static string WritePrompt(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, GameConcept concept)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Escribí el juego fusionado con estos juegos:");
        sb.AppendLine(DirectionText(slots, find, concept.Direction ?? new FusionDirection()));
        sb.AppendLine();
        sb.Append(GamesText(slots, find));
        if (!string.IsNullOrWhiteSpace(concept.PlayerRequest))
            sb.AppendLine("Pedido original del jugador: " + concept.PlayerRequest.Trim());
        sb.Append(PreviousDesigns(slots));
        if (string.IsNullOrWhiteSpace(concept.Title))
            sb.AppendLine("La idea del jugador (inventale un buen título y completá lo que falte): " + concept.Pitch);
        else
        {
            sb.AppendLine($"La idea elegida: \"{concept.Title}\" ({concept.Approach}).");
            sb.AppendLine(concept.Pitch);
        }
        if (concept.HowToPlay.Length > 0)
            sb.AppendLine("Cómo se juega: " + concept.HowToPlay);
        if (concept.Design.Length > 0)
        {
            sb.AppendLine("Diseño:");
            sb.AppendLine(concept.Design);
        }
        else
            sb.AppendLine("Concretá el diseño antes de escribir el guion, siguiendo el contrato.");
        sb.AppendLine();
        sb.AppendLine("Escribí el guion completo, jugable de punta a punta, con la regla única y una progresión adecuada a su género. " +
                      "Aplicá la prueba de quitar un original: ambos deben cambiar decisiones centrales. Informá game.state y los puntos, muertes " +
                      "o progreso reales cuando existan. En modo base heredá presentación, avisos, mapas y progresión; en modo libre seguí la " +
                      "presentación del diseño. No impongas título, HUD ni puntaje artificial. Antes de responder, revisá la lista de control.");
        sb.AppendLine(AnswerShape);
        return sb.ToString();
    }

    public static string DirectionText(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, FusionDirection direction)
    {
        string? selected = direction.Resolve(slots);
        if (selected == null)
            return "MODO ELEGIDO: reinterpretación libre. Podés diseñar una escena y presentación nuevas a partir de los originales. " +
                "No hay una base nativa declarada y screen.base no está disponible.";
        var slot = slots.Single(s => s.Slot == selected);
        string name = find(slot.Profile)?.Name ?? slot.Profile;
        return $"MODO ELEGIDO: conservar el original base. BASE ELEGIDA: games.{selected} = {name} (perfil {slot.Profile}).\n" +
            "Heredá mapas, paleta, escala, animaciones, interfaz, sonidos y reglas principales. Los aportantes interactúan dentro de ese mundo. " +
            $"screen.base(games.{selected}) repone la imagen nativa cuando haga falta. La respuesta no puede cambiar esta elección.";
    }

    private static string PreviousDesigns(IReadOnlyList<RecipeGame> slots)
    {
        var previous = StudioLibrary.For(slots.Select(s => s.Profile)).OrderByDescending(g => g.Created).Take(5).ToList();
        if (previous.Count == 0) return "";
        static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
        var sb = new StringBuilder("\nFusiones anteriores de esta combinación, solo para compararlas y NO copiarlas:\n");
        foreach (var game in previous)
            sb.AppendLine($"- {game.Title}: {Limit(game.Pitch, 400)}\n  Diseño: {Limit(game.Design, 1400)}");
        sb.AppendLine("Explicá qué decisión central cambia en tu propuesta. No alcanza con otro nombre, cartel o HUD.\n");
        return sb.ToString();
    }

    public static string RevisePrompt(StudioGame current, IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, string request, bool includeScript = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Este es el juego fusionado \"{current.Title}\", versión {current.Version}, con estos juegos:");
        sb.AppendLine(DirectionText(slots, find, FusionDirection.From(current)));
        sb.AppendLine();
        sb.Append(GamesText(slots, find));
        if (!string.IsNullOrWhiteSpace(current.PlayerRequest))
            sb.AppendLine("Pedido original del jugador: " + current.PlayerRequest);
        sb.AppendLine($"Idea: {current.Pitch}");
        sb.AppendLine($"Cómo se juega: {current.HowToPlay}");
        if (current.Design.Length > 0)
        {
            sb.AppendLine("Diseño actual:");
            sb.AppendLine(current.Design);
        }
        sb.AppendLine();
        if (includeScript)
        {
            sb.AppendLine("Guion actual:");
            sb.AppendLine("```javascript");
            sb.AppendLine(current.Script);
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine($"El jugador lo probó y pide: \"{request.Trim()}\"");
        sb.AppendLine("Hacé la versión siguiente con ese pedido. Conservá la dirección y la base elegidas. Si pide otra visión total, podés cambiar título, idea, diseño y " +
                      "guion siguiendo el método. Si es una corrección, conservá lo que funciona. En los dos casos, el resultado tiene que " +
                      "seguir pasando la lista de control.");
        sb.AppendLine(AnswerShape);
        return sb.ToString();
    }

    public Task<DesignOutcome> WriteAsync(IAiProvider ai, IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, GameConcept concept,
        Func<StudioGame, ValidationReport> validate, CancellationToken ct, Action<string>? progress = null, OriginalReferences? references = null)
    {
        if (!string.IsNullOrWhiteSpace(concept.MissingData))
            return Task.FromResult(new DesignOutcome(null, null, 0, MissingDataPrefix + concept.MissingData));
        var template = new StudioGame
        {
            Id = string.IsNullOrWhiteSpace(concept.Title) ? "" : StudioLibrary.Slug(concept.Title),
            Title = concept.Title,
            Approach = concept.Approach,
            Pitch = concept.Pitch,
            HowToPlay = concept.HowToPlay,
            Design = concept.Design,
            PlayerRequest = concept.PlayerRequest,
            Games = slots.ToList(),
            BaseSlot = (concept.Direction ?? new FusionDirection()).Resolve(slots),
            Version = 1,
            Author = ai.Name,
        };
        string brief = WritePrompt(slots, find, concept);
        return LoopAsync(ai, brief, brief, template, validate, ct, progress, references);
    }

    public Task<DesignOutcome> ReviseAsync(IAiProvider ai, StudioGame current, Func<string, GameProfile?> find, string request,
        Func<StudioGame, ValidationReport> validate, CancellationToken ct, Action<string>? progress = null, OriginalReferences? references = null)
    {
        var template = current.Copy();
        template.Version = current.Version + 1;
        template.Request = request.Trim();
        template.Author = ai.Name;
        template.Created = DateTime.Now;
        return LoopAsync(ai, RevisePrompt(current, current.Games, find, request),
            RevisePrompt(current, current.Games, find, request, includeScript: false), template, validate, ct, progress, references);
    }

    public Task<DesignOutcome> ResumeAsync(IAiProvider ai, DesignCheckpoint state, Func<StudioGame, ValidationReport> validate,
        CancellationToken ct, Action<string>? progress = null, OriginalReferences? references = null) =>
        LoopAsync(ai, state.FirstPrompt, state.Brief, state.Template.Copy(), validate, ct, progress, references, state);

    private async Task<DesignOutcome> LoopAsync(IAiProvider ai, string prompt, string brief, StudioGame template,
        Func<StudioGame, ValidationReport> validate, CancellationToken ct, Action<string>? progress, OriginalReferences? references,
        DesignCheckpoint? resume = null)
    {
        using var pictures = ai.SupportsImages ? references?.ExportTemporary() : null;
        if (resume?.ParseError?.StartsWith(MissingDataPrefix, StringComparison.Ordinal) == true)
            return new(null, resume.Report, 0, resume.ParseError, resume.Draft);
        ValidationReport? report = resume?.Report;
        StudioGame? draft = resume?.Draft?.Copy();
        if (draft != null)
        {
            if (string.IsNullOrWhiteSpace(draft.Script)) throw new AiException("El borrador guardado no tiene un guion completo.");
            draft.Games = template.Copy().Games;
            draft.BaseSlot = template.BaseSlot;
            draft.PlayerRequest = template.PlayerRequest;
            draft.Request = template.Request;
            draft.Version = template.Version;
        }
        string? error = resume?.ParseError;
        int calls = resume?.Calls ?? 0;
        var triedScripts = new HashSet<string>(resume?.TriedScripts ?? [], StringComparer.Ordinal);
        string firstPrompt = prompt;
        void Save()
        {
            if (Checkpoint == null) return;
            var metadata = template.Copy();
            metadata.Script = ""; // Initial source lives in FirstPrompt; once generated, only Draft keeps source.
            Checkpoint.Invoke(new DesignCheckpoint { Template = metadata, FirstPrompt = draft != null ? "" : firstPrompt, Brief = brief,
                Draft = draft?.Copy(), Report = report is { } r ? r with { Picture = null, Shots = null, Robots = null,
                    Summary = r.Summary.Length > 16000 ? r.Summary[..16000] + "\n[Salida adicional omitida]" : r.Summary } : null,
                ParseError = error, TriedScripts = triedScripts.TakeLast(32).ToList(), Calls = calls });
        }
        if (draft != null)
        {
            progress?.Invoke("Volviendo a probar el borrador guardado, sin llamar a la IA...");
            report = await Task.Run(() => validate(draft), ct);
            Save();
            if (report.Ok) return new(draft, report, 0, null);
            prompt = brief + "\n\n" + CurrentDraft(draft) + "\n\n" + FixRequest(report);
        }
        else if (error != null)
            prompt = brief + $"\nNo pude leer la respuesta anterior: {error}. {AnswerShape}";
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(attempt == 1 ? $"{ai.Name} está escribiendo el juego..." : $"{ai.Name} está corrigiendo (intento {attempt} de {MaxAttempts})...");
            // Syntax failures need the current source and error, not the same pictures again.
            bool attach = pictures != null && (attempt == 1 || report is { Error: null });
            string context = prompt + (references != null ? "\n\n" + references.Describe(template.BaseSlot, attach) : "");
            calls++;
            Save(); // Before calling the provider, so a limit or cancellation keeps the task.
            string answer = await ai.CompleteAsync(new AiRequest(Guide, [new(true, context)], GameSchema(), attach ? pictures!.Paths : null), ct);
            var game = ParseGame(answer, template, out error);
            if (game == null)
            {
                Save();
                if (error?.StartsWith(MissingDataPrefix, StringComparison.Ordinal) == true)
                    return new(null, report, attempt, error, draft);
                prompt = brief + "\n\n" + (draft != null ? CurrentDraft(draft) : "") + $"\nNo pude leer tu respuesta: {error}. {AnswerShape}";
                continue;
            }
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.Script.Replace("\r\n", "\n").Trim())));
            if (!triedScripts.Add(hash))
                return new(null, report, attempt, "La IA devolvió el mismo guion que ya falló. Conservamos el borrador para continuar con otro cambio.", draft);
            draft = game;
            draft.Author = ai.Name;
            error = null;
            report = null;
            Save(); // A complete answer survives even when validation is interrupted.
            progress?.Invoke("Probando el juego con tres jugadores robot...");
            report = await Task.Run(() => validate(game), ct);
            Save();
            if (report.Ok)
                return new DesignOutcome(game, report, attempt, null);
            error = report.Error ?? string.Join(" ", report.Problems);
            prompt = brief + "\n\n" + CurrentDraft(draft) + "\n\n" + FixRequest(report);
        }
        return new DesignOutcome(null, report, MaxAttempts, error, draft);
    }

    private static string CurrentDraft(StudioGame game) =>
        "Último borrador (reemplaza los anteriores; conservá lo que funciona):\n" +
        $"Nombre: {game.Title}\nIdea: {game.Pitch}\nCómo se juega: {game.HowToPlay}\nDiseño: {game.Design}\n```javascript\n{game.Script}\n```";

    public static string FixRequest(ValidationReport report) =>
        "Probé el guion en el emulador y falló:\n" + report.Summary +
        "\nCorregilo y devolvé el juego completo de nuevo. " + AnswerShape;

    /// <summary>
    /// Reads an answer: the JSON of the schema, or (from a chat) a JSON with the texts plus a
    /// ```javascript block with the script. Fields that are missing keep the template's value.
    /// </summary>
    public static StudioGame? ParseGame(string answer, StudioGame template, out string? error)
    {
        error = null;
        var game = template.Copy();
        string? script = null;
        var fence = Regex.Match(answer, "```(?:javascript|js)\\s*\\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fence.Success)
            script = fence.Groups[1].Value.Trim();
        string withoutCode = fence.Success ? answer.Remove(fence.Index, fence.Length) : answer;
        var json = FirstJson(withoutCode);
        if (json != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var e = doc.RootElement;
                if (Str(e, "missingData").Trim() is { Length: > 0 } missing)
                {
                    error = MissingDataPrefix + missing;
                    return null;
                }
                game.Title = Str(e, "title", game.Title);
                game.Approach = Str(e, "approach", game.Approach);
                game.Pitch = Str(e, "pitch", game.Pitch);
                game.HowToPlay = Str(e, "howToPlay", game.HowToPlay);
                game.Design = Str(e, "design", game.Design);
                game.Changes = Str(e, "changes", game.Changes ?? "");
                if (script == null && e.TryGetProperty("script", out var sc) && sc.ValueKind == JsonValueKind.String)
                    script = sc.GetString();
            }
            catch (JsonException ex)
            {
                if (script == null)
                {
                    error = "el JSON no es válido (" + ex.Message + ")";
                    return null;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(script))
        {
            error = "falta el guion (script)";
            return null;
        }
        game.Script = script;
        if (string.IsNullOrWhiteSpace(game.Id))
            game.Id = StudioLibrary.Slug(game.Title);
        return game;
    }

    // ------------------------------------------------------------------ copy and paste
    public static string ManualConceptPrompt(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, string wish, FusionDirection? direction = null) =>
        ConceptGuide + "\n\n" + ConceptPrompt(slots, find, wish, direction) +
        "\nRespondé SOLO con un JSON así: {\"ideas\": [{\"title\": \"...\", \"approach\": \"...\", \"pitch\": \"...\", \"howToPlay\": \"...\", " +
        string.Join(", ", ConceptFields.Select(f => $"\"{f.Key}\": \"...\"")) + ", \"missingData\": \"\"}]}";

    private const string ManualShape =
        "\nRespondé con DOS bloques: primero un bloque ```json con {\"title\", \"approach\", \"pitch\", \"howToPlay\", \"design\", \"changes\", \"missingData\"} " +
        "y después un bloque ```javascript con el guion completo.";

    public static string ManualWritePrompt(IReadOnlyList<RecipeGame> slots, Func<string, GameProfile?> find, GameConcept concept) =>
        Guide + "\n\n" + WritePrompt(slots, find, concept) + ManualShape;

    public static string ManualRevisePrompt(StudioGame current, Func<string, GameProfile?> find, string request) =>
        Guide + "\n\n" + RevisePrompt(current, current.Games, find, request) + ManualShape;

    public static string ManualFixPrompt(ValidationReport report, StudioGame? current = null) =>
        Guide + "\n\n" + (current != null ?
            DirectionText(current.Games, ProfileLibrary.Find, FusionDirection.From(current)) + "\n" + GamesText(current.Games, ProfileLibrary.Find) +
            "\nPedido original del jugador: " + current.PlayerRequest + "\nCambio solicitado: " + current.Request + "\n" + CurrentDraft(current) + "\n\n" : "") +
        FixRequest(report) + ManualShape;

    // ------------------------------------------------------------------ helpers
    private static string Str(JsonElement e, string name, string fallback = "") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    /// <summary>The first balanced {...} or [...] in the text that parses as JSON.</summary>
    internal static string? FirstJson(string text)
    {
        for (int start = 0; start < text.Length; start++)
        {
            char open = text[start];
            if (open != '{' && open != '[')
                continue;
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == open) depth++;
                else if (c == close && --depth == 0)
                {
                    string candidate = text[start..(i + 1)];
                    try
                    {
                        using var _ = JsonDocument.Parse(candidate);
                        return candidate;
                    }
                    catch (JsonException)
                    {
                        break;
                    }
                }
            }
        }
        return null;
    }
}
