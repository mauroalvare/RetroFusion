using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class LibraryJourneyNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";
    [Test, Explicit("Ventana oculta, audio silenciado y archivos sintéticos propios.")]
    public void RenamingMovingDuplicatingAndRemovingAFavoriteKeepsItsIdentityAndAWayBack()
    {
        using var ui = new NativeHarness(1280, 840, 125, "real");
        var home = (HomeScreen)ui.Get("_home");
        var library = (GameLibrary)ui.Get("_library");
        var terminal = (ProgramTerminal)ui.Property("Terminal");
        ui.Enter("juegos");
        var original = home.TerminalGames().Single(game => game.Title == "Faro del taller");
        ui.Enter("favorito \"Faro del taller\" on"); ui.Enter("cd favoritos");
        Assert.That(home.TerminalGames().Select(game => game.Key), Is.EqualTo(new[] { original.Key }));
        string moved = Path.Combine(Directory.CreateDirectory(Path.Combine(ui.Games, "movidos")).FullName, "Renombrado.nes");
        File.Move(original.Path!, moved);
        Rescan();
        Assert.That(home.TerminalGames().Single().Key, Is.EqualTo(original.Key));
        Assert.That(home.TerminalGames().Single().Path, Is.EqualTo(moved));
        string duplicate = Path.Combine(ui.Games, "Duplicado.nes"); File.Copy(moved, duplicate);
        Rescan(); Assert.That(home.TerminalGames(), Has.Count.EqualTo(1));
        File.Delete(moved); File.Delete(duplicate);
        Rescan();
        Assert.That(home.TerminalGames(), Has.Count.EqualTo(1), "A missing favorite remains visible and removable.");
        var missing = home.TerminalGames().Single();
        Assert.That(missing.Key, Is.EqualTo(original.Key)); Assert.That(missing.Openable, Is.False);
        Assert.That(missing.Hint, Does.Contain("escanear"));
        string unknown = LibraryKey.Named("original", "favorito de un catálogo anterior sin identificación");
        home.TerminalFavorite(unknown, true);
        ui.Enter("opcion idioma en");
        Assert.That(home.TerminalGames().Single(game => game.Key == unknown).Title, Does.StartWith("Missing game"));
        Assert.That(home.TerminalGames().Single(game => game.Key == original.Key).Title, Is.EqualTo("Faro del taller"), "Known personal titles are not translated.");
        ui.Enter("option language es");
        Assert.That(home.TerminalGames().Single(game => game.Key == unknown).Title, Does.StartWith("Juego ausente"));
        home.TerminalFavorite(unknown, false);
        ui.Enter("jugar 1");
        Assert.That(terminal.Output.AllText(), Does.Contain("No se puede abrir"));
        Assert.That(ui.Get("_terminalOpen"), Is.EqualTo(true));
        ui.Capture("favorito-ausente");
        NativeHarness.WriteDemoLibrary(ui.Games, 1); Rescan();
        Assert.That(home.TerminalGames().Single().Key, Is.EqualTo(original.Key));
        Assert.That(home.TerminalGames().Single().Openable, Is.True);
        foreach (string section in new[] { "originales", "biblioteca", "favoritos", "nes", "recientes" })
        {
            ui.Enter("cd " + section); ui.Enter("juegos");
            ui.Enter("jugar \"Faro del taller\""); ui.Frames(3);
            Assert.That(((EmulatorHost)ui.Get("_host")).LoadedMediaSource, Is.EqualTo(original.Path));
            ui.Call("OpenTerminal"); ui.Frames(3);
            Assert.That(terminal.Location, Is.EqualTo(section));
            Assert.That(home.TerminalGames().Any(game => game.Key == original.Key), Is.True);
        }
        void Rescan()
        {
            ui.Enter("escanear");
            Assert.That(SpinWait.SpinUntil(() => { ui.Frames(); return !library.Scanning; }, TimeSpan.FromSeconds(10)), Is.True);
            ui.Enter("juegos");
        }
    }
}
