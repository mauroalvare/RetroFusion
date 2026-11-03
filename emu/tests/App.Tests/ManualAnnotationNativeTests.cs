using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class ManualAnnotationNativeTests
{
    private const string Reason = "Órdenes y entrada reales; ventana oculta, audio deshabilitado, IA bloqueada y anotaciones propias.";
    [TearDown] public void SpanishAgain() => UiText.Language = "es";
    private static EmulatorHost Host(NativeHarness ui) => (EmulatorHost)ui.Get("_host");
    private static CodeWindow Code(NativeHarness ui) => (CodeWindow)ui.Get("_code");
    private static ProgramTerminal Terminal(NativeHarness ui) => (ProgramTerminal)ui.Property("Terminal");

    [TestCase(1280, 840, 100, "es"), TestCase(640, 480, 200, "en"), Explicit(Reason)]
    public void NotesUseTheEditorStorePreserveUserLanguageAndReloadAfterRenaming(int width, int height, int text, string language)
    {
        using var ui = new NativeHarness(width, height, text, "real", games: 1, "--no-audio", "--language", language);
        bool en = language == "en";
        string media = Path.Combine(ui.Folder, "Faro QA.nes"); File.WriteAllBytes(media, NesBatteryTests.Image());
        ui.Enter((en ? "open" : "abrir") + " \"" + media + "\""); ui.Call("OpenTerminal"); ui.Frames(3);
        byte[] before = Host(ui).Machine!.SaveState();
        ui.Enter((en ? "NoTe" : "AnOtAr") + " C000 Guardar estado rápido");
        ui.Enter((en ? "label" : "etiquetar") + " C000 entrada principal");
        ui.Enter(en ? "notes C000" : "anotaciones C000");
        Assert.That(Terminal(ui).Output.AllText(), Does.Contain(en ? "Comment: Guardar estado rápido" : "Comentario: Guardar estado rápido"));
        Assert.That(Code(ui).ReadAnnotation(0xc000).Label, Is.EqualTo("entrada_principal"));
        Assert.That(Code(ui).ReadAnnotation(0xc000).Comment, Is.EqualTo("Guardar estado rápido"));
        Assert.That(Host(ui).Machine!.SaveState(), Is.EqualTo(before));
        string folder = Path.Combine(ui.Folder, "out", "autotest-annotations");
        Assert.That(Directory.GetFiles(folder, "edition-*.json"), Has.Length.EqualTo(1));
        string renamed = Path.Combine(ui.Folder, "Otro nombre.nes"); File.Copy(media, renamed);
        ui.RestartApp(); ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter((en ? "open" : "abrir") + " \"" + renamed + "\""); ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter(en ? "notes C000" : "anotaciones C000");
        Assert.That(Terminal(ui).Output.AllText(), Does.Contain("Guardar estado rápido").And.Contain("entrada_principal"));
        ui.Capture("anotaciones-" + text + "-" + language);
        Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).ProvidersDisabled, Is.True);
    }

    [Test, Explicit(Reason)]
    public void ImportingLegacyNotesRequiresConfirmationAndKeepsTheSourceAndProgramUntouched()
    {
        using var ui = new NativeHarness(640, 480, 200, "real", games: 1, "--no-audio", "--language", "en");
        string folder = Path.Combine(ui.Folder, "out", "autotest-annotations");
        var legacy = new CodeAnnotations(); legacy.Comments["C000"] = "Own legacy text"; legacy.Patches["C002"] = "60|EA";
        legacy.Save("faro-qa", folder); string source = Path.Combine(folder, "faro-qa.json"); byte[] saved = File.ReadAllBytes(source);
        string media = Path.Combine(ui.Folder, "Faro QA.nes"); File.WriteAllBytes(media, NesBatteryTests.Image());
        ui.Enter("open \"" + media + "\""); ui.Call("OpenTerminal"); ui.Frames(3);
        byte[] state = Host(ui).Machine!.SaveState();
        ui.Enter("notes C000"); Assert.That(Code(ui).ReadAnnotation(0xc000).Comment, Is.Empty);
        ui.Enter("import-notes"); Assert.That(Code(ui).ReadAnnotation(0xc000).Comment, Is.Empty);
        ui.Enter("import-notes confirm"); ui.Enter("notes C000");
        Assert.That(Terminal(ui).Output.AllText(), Does.Contain("Comment: Own legacy text"));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(saved)); Assert.That(Host(ui).Machine!.SaveState(), Is.EqualTo(state));
        Assert.That(Code(ui).HasLegacyAnnotations, Is.False);
        ui.Capture("anotaciones-importar-200-en");
    }

    [Test, Explicit(Reason)]
    public void AFailedAnnotationWriteReportsSessionOnlyAndCanBeRetriedFromRealInput()
    {
        using var ui = new NativeHarness(1280, 840, 125, "real", games: 1, "--no-audio");
        string media = Path.Combine(ui.Folder, "Faro QA.nes"); File.WriteAllBytes(media, NesBatteryTests.Image());
        ui.Enter("abrir \"" + media + "\""); ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("anotar C000 Antes");
        string path = Directory.GetFiles(Path.Combine(ui.Folder, "out", "autotest-annotations"), "edition-*.json").Single();
        byte[] before = File.ReadAllBytes(path); int marker = Terminal(ui).Output.AllText().Length;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ui.Enter("anotar C000 Después");
            string reply = Terminal(ui).Output.AllText()[marker..];
            Assert.That(reply, Does.Contain("No pude guardar las anotaciones").And.Not.Contain("Comentario guardado."));
            Assert.That(Code(ui).ReadAnnotation(0xc000).Comment, Is.EqualTo("Después"));
            ui.Enter("hacer debugger"); ui.Frames(4);
            byte[] state = Host(ui).Machine!.SaveState();
            ui.Click("Reiniciar depurador"); ui.Frames(3);
            Assert.That(ui.Get("_errorPopup"), Does.Contain("No pude guardar las anotaciones"));
            Assert.That(Host(ui).Machine!.SaveState(), Is.EqualTo(state));
            ui.Click("Cerrar aviso"); ui.Call("OpenTerminal"); ui.Frames(3);
            ui.App.RequestQuit(); ui.Frames(3);
            Assert.That(ui.App.QuitRequested, Is.False); Assert.That(ui.Get("_quitNotice"), Does.Contain("anotaciones sin guardar"));
            ui.Key(ImGuiKey.Escape); ui.Call("OpenTerminal"); ui.Frames(3);
            string copy = Path.Combine(ui.Folder, "Copia de notas.json");
            ui.Enter("exportar-anotaciones \"" + copy + "\"");
            Assert.That(File.Exists(copy), Is.True); Assert.That(Code(ui).TryPreserveAnnotations(), Is.True);
            ui.Enter("hacer code"); ui.Frames(4);
            Assert.That(ui.Bounds["Exportar notas"].Visible, Is.True);
            ui.Click("Exportar notas");
            Assert.That(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path)!, "recovery"), "*.json"), Has.Length.EqualTo(1));
            ui.Capture("anotaciones-error-exportar-125-es");
        }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter("anotar C000 Después"); Assert.That(Code(ui).AnnotationProblem, Is.Empty);
        Assert.That(File.ReadAllBytes(path), Is.Not.EqualTo(before));
    }

    [Test, Explicit(Reason)]
    public void ExplicitDiscardDoesNotSaveDuringShutdownEvenIfTheStorageBecomesWritable()
    {
        using var ui = new NativeHarness(640, 480, 200, "real", games: 1, "--no-audio", "--language", "en");
        string media = Path.Combine(ui.Folder, "Faro QA.nes"); File.WriteAllBytes(media, NesBatteryTests.Image());
        ui.Enter("open \"" + media + "\""); ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("note C000 Before");
        string path = Directory.GetFiles(Path.Combine(ui.Folder, "out", "autotest-annotations"), "edition-*.json").Single();
        byte[] before = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ui.Enter("note C000 Discard this edit"); ui.Enter("continue");
            long frame = Host(ui).Machine!.FrameCount;
            ui.App.RequestQuit(); ui.Frames(4);
            Assert.That(Host(ui).Machine!.FrameCount, Is.EqualTo(frame), "The quit notice must pause the running game.");
            Assert.That(ui.App.QuitRequested, Is.False); Assert.That(ui.Bounds["Confirmar salida"].Visible, Is.True);
            ui.Capture("anotaciones-salida-200-en");
            ui.Click("Confirmar salida"); Assert.That(ui.App.QuitRequested, Is.True);
        }
        ui.RestartApp();
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "The explicit discard must survive the final shutdown retry.");
    }
}
