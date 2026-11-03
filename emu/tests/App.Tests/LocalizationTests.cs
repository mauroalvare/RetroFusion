using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class LocalizationTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Test]
    public void TheCompactPromptUsesEnglishWithoutChangingItsInternalLocation()
    {
        UiText.Language = "en";
        var terminal = new ProgramTerminal(() => []);
        Assert.That(terminal.PromptForColumns(30), Is.EqualTo("/library>"));
        Assert.That(terminal.Location, Is.EqualTo("biblioteca"));
        terminal.Location = "favoritos";
        Assert.That(terminal.PromptForColumns(30), Is.EqualTo("/favorites>"));
        UiText.Language = "es";
        Assert.That(terminal.PromptForColumns(30), Is.EqualTo("/favoritos>"));
    }

    [Test]
    public void EnglishChangesLabelsAndKeepsUserTextAndNumericFormats()
    {
        UiText.Language = "en";
        Assert.That(UiText.Get("Volver a la partida"), Is.EqualTo("Return to game"));
        Assert.That(UiText.Format($"Máquina: {"Crear"} · {UiText.Get("en pausa")}"), Is.EqualTo("Machine: Crear · paused"));
        Assert.That(UiText.Format($"Dirección ${0xABCD:X4}: {{braces}}"), Is.EqualTo("Dirección $ABCD: {braces}"), "An uncatalogued technical format must keep its data and literal braces.");
        Assert.That(UiText.Format($"Ventana: {1280}x{840} · escala {1.75f:F2} · texto {175} %"), Is.EqualTo("Window: 1280x840 · scale 1.75 · text 175 %"));
        Assert.That(UiText.Label((string?)null), Is.Null, "Null clears ImGui window focus.");
        Assert.That(UiText.Label("Preferencias###preferencias"), Is.EqualTo("Preferences###preferencias"));
        Assert.That(UiText.Label("Preparar##propio"), Is.EqualTo("Prepare###Preparar##propio"));
        var game = new Retro.Ai.CreatedGame { Title = "Crear", Folder = "datos propios", Machine = "c64" };
        Assert.That(game.ProgramPath, Is.EqualTo(Path.Combine("datos propios", "juego.prg")));
        Assert.That(game.SourcePath, Is.EqualTo(Path.Combine("datos propios", "juego.s")));
        Assert.That(UiText.Format($"Juego: {game.Title}"), Is.EqualTo("Game: Crear"));
        UiText.Language = "es";
        Assert.That(UiText.Label("Preparar##propio"), Is.EqualTo("Preparar###Preparar##propio"));
        Assert.That(UiText.Get("Volver a la partida"), Is.EqualTo("Volver a la partida"));
    }

    private static IReadOnlyList<CommandSpec> Catalog()
    {
        var app = (RetroApp)RuntimeHelpers.GetUninitializedObject(typeof(RetroApp));
        var orders = (IReadOnlyList<CommandSpec>)typeof(RetroApp).GetProperty("TerminalCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        return new ProgramTerminal(() => orders).Commands;
    }

    [Test]
    public void IncludedGuidesFollowLanguageEvenWhenPreviouslyOpenedInSpanish()
    {
        UiText.Language = "es";
        Assert.That(Retro.Ai.GameDesignGuide.Text, Does.StartWith("# Contrato"));
        Assert.That(Retro.Asm.Templates.For("c64")!.Guide, Does.Contain("Cómo arranca"));
        Assert.That(Retro.Studio.StudioDesigner.Method, Does.StartWith("# Método"));
        UiText.Language = "en";
        Assert.That(Retro.Ai.GameDesignGuide.Text, Does.StartWith("# Design"));
        Assert.That(Retro.Ai.GameDesignGuide.Compact, Does.StartWith("# Design"));
        Assert.That(Retro.Asm.Templates.For("c64")!.Guide, Does.Contain("## Boot"));
        Assert.That(Retro.Asm.Templates.For("zx48")!.Guide, Does.Contain("## Boot"));
        Assert.That(Retro.Studio.StudioDesigner.ApiDoc, Does.Contain("## Lifecycle"));
        Assert.That(Retro.Studio.StudioDesigner.Method, Does.StartWith("# Fusing"));
        var request = new Retro.Ai.AiRequest(Retro.Ai.AiPlayAdvisor.Guide.Replace("\r\n", "\n"), [], null);
        Assert.That(request.System, Does.StartWith("You are an expert retro gamer"), "Resources must translate with both Linux and Windows newlines.");
    }

    [TestCase("sub-claude"), TestCase("sub-codex"), TestCase("sub-google")]
    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void DisabledProvidersCannotBeConstructedEvenIfTheirSelectionChanges(string provider)
    {
        var settings = new Retro.Ai.AiSettings { Provider = provider, ProvidersDisabled = true };
        Assert.That(settings.CreateProvider(), Is.Null);
        Assert.That(settings.CreateProvider(TimeSpan.FromSeconds(1)), Is.Null);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(settings), Does.Not.Contain("ProvidersDisabled"));
    }

    [Test]
    public void EnglishHasUniqueCommandsAndSpanishScriptsStillResolve()
    {
        UiText.Language = "en";
        var orders = Catalog();
        string[] expected = ["games", "play", "open", "save", "load", "help", "options", "option", "interface", "tasks", "cancel", "prepare", "projects", "quit"];
        Assert.That(expected.Where(name => orders.All(order => order.Name != name)), Is.Empty);
        Assert.That(orders.SelectMany(order => order.Aliases.Prepend(order.Name)).Select(CommandLine.Fold), Is.Unique);
        Assert.That(orders.Single(order => order.Id == "play.open-game").Answers("jugar"), Is.True);
        var terminal = new ProgramTerminal(() => orders);
        Assert.That(terminal.Submit("help play"), Is.True);
        Assert.That(terminal.Output.AllText(), Does.Contain("play").And.Contain("Usage:").And.Not.Contain("Uso:"));
        Assert.That(terminal.Complete("ga", 2).Items.Select(item => item.Value), Has.Member("games"));
    }

    [Test]
    public void LanguageAndManualUpdateVersionSurviveASettingsRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-language-" + Guid.NewGuid().ToString("N"), "preferences.json");
        try
        {
            var settings = new AppPreferences(path) { Language = "en", LastStartedVersion = "0.3.8", Interface = InterfaceStyle.Hardcore };
            Assert.That(settings.Save(), Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("\"Interface\":\"hardcore\""), "Older versions must retain the command interface after rollback.");
            var again = new AppPreferences(path);
            Assert.That(again.Language, Is.EqualTo("en")); Assert.That(again.LastStartedVersion, Is.EqualTo("0.3.8"));
            Assert.That(again.Interface, Is.EqualTo(InterfaceStyle.Hardcore));
            Assert.That(InterfaceStyles.Describe(again.Interface).Id, Is.EqualTo("real"));
            Assert.That(InterfaceStyles.TryParse("hardcore", out var legacy), Is.True); Assert.That(legacy, Is.EqualTo(again.Interface));
        }
        finally { if (File.Exists(path)) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [TestCase(new string[] { }, true)]
    [TestCase(new[] { "--no-update" }, false)]
    [TestCase(new[] { "--autotest", "1", "shot.png" }, false)]
    [TestCase(new[] { "--update-ready", "plan.json" }, false)]
    [TestCase(new[] { "--hidden", "--mute" }, true)]
    public void EmergencyUpdatesRespectTestAndUpdaterModes(string[] args, bool allowed) =>
        Assert.That(StartupLauncher.CanOfferUpdate(args), Is.EqualTo(allowed));
}
