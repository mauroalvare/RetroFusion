using Retro.App;
using Raylib_cs;

namespace App.Tests;

public sealed class CommandSearchTests
{
    private static AppCommand Command(string id, string title, string group = "", string keywords = "") =>
        new(id, title, group, keywords, "", () => Assert.Fail("Buscar no debe ejecutar acciones."));

    [Test]
    public void OpeningThePaletteKeepsTheFullChordOutOfTheEmulatedKeyboard()
    {
        Assert.That(InputMapper.ToolChordHeld(new[] { KeyboardKey.LeftControl, KeyboardKey.LeftShift, KeyboardKey.P }.Contains), Is.True);
        Assert.That(InputMapper.ToolChordHeld(new[] { KeyboardKey.LeftControl, KeyboardKey.P }.Contains), Is.False);
    }

    [Test]
    public void SearchAcceptsAccentsCaseAndMultipleWordsAcrossMetadata()
    {
        var commands = new[] { Command("code", "Código y rutinas", "Herramientas", "desensamblador"), Command("games", "Biblioteca") };
        Assert.That(CommandPalette.Search(commands, "  HERRAMIENTAS codigo  ").Select(c => c.Id), Is.EqualTo(new[] { "code" }));
        Assert.That(CommandPalette.Search(commands, "código desensamblador").Select(c => c.Id), Is.EqualTo(new[] { "code" }));
    }

    [Test]
    public void MissingWordsDoNotReturnUnrelatedCommands()
    {
        Assert.That(CommandPalette.Search([Command("sound", "Sonido", keywords: "volumen")], "volumen guardar"), Is.Empty);
    }

    [Test]
    public void EmptySearchPreservesTheCuratedOrder()
    {
        var commands = new[] { Command("library", "Biblioteca"), Command("updates", "Actualizaciones") };
        Assert.That(CommandPalette.Search(commands, "  "), Is.EqualTo(commands));
    }

    [Test]
    public void TitlePrefixComesBeforeKeywordMatches()
    {
        var commands = new[] { Command("recent", "Recientes", keywords: "guardar"), Command("save", "Guardar estado") };
        Assert.That(CommandPalette.Search(commands, "guardar").Select(c => c.Id), Is.EqualTo(new[] { "save", "recent" }));
    }

    [Test]
    public void UnavailableCommandsRemainDiscoverableAndUseCurrentContext()
    {
        bool loaded = false;
        var command = new AppCommand("save", "Guardar estado", "Partida", "", "", () => { }, () => loaded, "Abrí un original.");
        var found = CommandPalette.Search([command], "guardar").Single();
        Assert.That(found.Enabled, Is.False);
        Assert.That(found.Unavailable, Is.EqualTo("Abrí un original."));
        loaded = true;
        Assert.That(found.Enabled, Is.True);
    }
}
