using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Retro.App;
using Retro.Core;

namespace App.Tests;

/// <summary>Rules every order must follow, checked on the real catalog. A new order that breaks one fails here.</summary>
[NonParallelizable]
public sealed class TerminalCatalogTests
{
    private static IReadOnlyList<CommandSpec> Catalog()
    {
        // The definitions are data: reading them needs no window, only the instance their handlers will use.
        var app = (RetroApp)RuntimeHelpers.GetUninitializedObject(typeof(RetroApp));
        var own = (IReadOnlyList<CommandSpec>)typeof(RetroApp).GetProperty("TerminalCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        return new ProgramTerminal(() => own).Commands;
    }

    private static string DocumentPath() => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "docs", "CONSOLA.md"));

    [Test]
    public void IdentifiersNamesAndAliasesNeverCollide()
    {
        var all = Catalog();
        Assert.That(all.Select(order => order.Id), Is.Unique);
        var words = all.SelectMany(order => order.Aliases.Prepend(order.Name)).Select(CommandLine.Fold).ToArray();
        Assert.That(words.GroupBy(word => word).Where(group => group.Count() > 1).Select(group => group.Key), Is.Empty);
    }

    [Test]
    public void EveryOrderExplainsItselfAndItsExamplesAreValid()
    {
        foreach (var order in Catalog())
        {
            Assert.That(order.Summary, Is.Not.Empty.And.EndWith("."), order.Name);
            Assert.That(order.Run != null || order.Planned != null, Is.True, order.Name);
            Assert.That(order.Arguments.Take(Math.Max(0, order.Arguments.Count - 1)).Any(argument => argument.Rest), Is.False, order.Name + ": only the last argument can take the rest of the line.");
            bool optional = false;
            foreach (var argument in order.Arguments)
            {
                Assert.That(!optional || argument.Optional, Is.True, order.Name + ": a required argument cannot follow an optional one.");
                optional |= argument.Optional;
            }
            foreach (string example in order.Examples)
            {
                Assert.That(CommandLine.TryParse(example, out var tokens, out string? problem), Is.True, example + ": " + problem);
                if (order.Answers(tokens[0].Text))
                    foreach (var (argument, token) in order.Arguments.Zip(tokens.Skip(1)))
                        Assert.That(argument.Validate(argument.Rest ? string.Join(' ', tokens.Skip(1).Select(item => item.Text)) : token.Text), Is.Null, example);
            }
        }
    }

    [Test]
    public void TheCommonOrdersOfTheFirstVersionStillAnswer()
    {
        string[] previous = ["cd", "pwd", "ls", "juegos", "buscar", "filtrar", "ficha", "favorito", "jugar", "abrir", "continuar", "interfaz", "opciones", "opcion", "acciones", "hacer", "estado",
            "pausa", "reiniciar", "captura", "ranuras", "guardar", "cargar", "registros", "memoria", "codigo", "paso", "maquinas", "maquina", "actualizar", "copiar", "salir", "ayuda", "limpiar", "historial", "clear", "help"];
        var all = Catalog();
        Assert.That(previous.Where(word => !all.Any(order => order.Answers(word))), Is.Empty);
    }

    [Test]
    public void ThePublishedReferenceMatchesTheCatalog()
    {
        string reference = Reference(), path = DocumentPath(), text = File.ReadAllText(path);
        const string Start = "<!-- referencia-inicio -->", End = "<!-- referencia-fin -->";
        int from = text.IndexOf(Start, StringComparison.Ordinal) + Start.Length, to = text.IndexOf(End, StringComparison.Ordinal);
        Assert.That(from, Is.GreaterThan(Start.Length - 1)); Assert.That(to, Is.GreaterThan(from));
        string current = text[from..to].Replace("\r\n", "\n");
        if (Environment.GetEnvironmentVariable("RETROEMU_WRITE_REFERENCE") == "1") { File.WriteAllText(path, text[..from] + reference + text[to..]); return; }
        Assert.That(current, Is.EqualTo(reference), "Run this test with RETROEMU_WRITE_REFERENCE=1 after changing an order.");
    }

    [Test]
    public void TheEnglishReferenceMatchesTheSameCatalog()
    {
        string previous = UiText.Language;
        try
        {
            UiText.Language = "en";
            string path = Path.Combine(Path.GetDirectoryName(DocumentPath())!, "CONSOLE-EN.md");
            string reference = ("# Real terminal command reference\n\nChoose English in Preferences. Existing Spanish aliases remain accepted.\nUse help <command> for examples, requirements and errors.\n" + Reference()).TrimEnd() + "\n";
            if (Environment.GetEnvironmentVariable("RETROEMU_WRITE_REFERENCE") == "1") { File.WriteAllText(path, reference); return; }
            Assert.That(File.ReadAllText(path).Replace("\r\n", "\n"), Is.EqualTo(reference));
        }
        finally { UiText.Language = previous; }
    }

    private static string Reference()
    {
        var text = new StringBuilder("\n");
        foreach (var group in Catalog().GroupBy(order => order.Group).OrderBy(group => Array.IndexOf<string>(["Biblioteca", "Partida", "Análisis", "Proyectos", "Tareas", "Sistema", "Consola"], group.Key)))
        {
            text.Append("\n### ").Append(group.Key).Append(UiText.English ? "\n\n| Command | Function |\n| --- | --- |\n" : "\n\n| Orden | Función |\n| --- | --- |\n");
            foreach (var order in group)
                text.Append("| `").Append(order.Usage.Replace("|", "\\|")).Append('`').Append(order.Aliases.Count > 0 ? " (" + string.Join(", ", order.Aliases.Select(alias => "`" + alias + "`")) + ")" : "").Append(" | ").Append(order.Summary).Append(" |\n");
        }
        return text.Append('\n').ToString();
    }
}
