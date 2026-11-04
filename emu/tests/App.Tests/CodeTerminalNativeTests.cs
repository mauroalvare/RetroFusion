using System.Reflection;
using System.Text.RegularExpressions;
using Retro.App;
using Retro.Core;

namespace App.Tests;

/// <summary>
/// References, patches, exports and variables typed in the real terminal over an own NES program,
/// with a hidden window, muted audio and every file in the harness folders.
/// </summary>
[NonParallelizable]
public sealed class CodeTerminalNativeTests
{
    private const string Reason = "Requiere gráficos nativos: ventana oculta, audio silenciado y datos propios.";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    private static string Flat(ProgramTerminal console) => Regex.Replace(console.Output.AllText(), @"\s+", " ");
    private static string Since(ProgramTerminal console, int start) => Regex.Replace(console.Output.AllText()[start..], @"\s+", " ");

    [Explicit(Reason)]
    [Test]
    public void ReferencesPatchesExportsAndVariablesWorkByOrders()
    {
        using var ui = new NativeHarness(1280, 1300, 100, "real", games: 4);
        string game = Path.Combine(ui.Games, "Taller_de_parches.nes");
        File.WriteAllBytes(game, CodeTerminalServiceTests.Rom());
        ui.Call("OpenFile", game); ui.Frames(4);
        var host = (EmulatorHost)ui.Get("_host"); var m = host.Machine!;
        ui.Call("OpenTerminal"); ui.Frames(3);
        var console = (ProgramTerminal)ui.Property("Terminal");
        string Run(string order) { int start = console.Output.AllText().Length; ui.Enter(order); ui.Frames(2); return Since(console, start); }

        string refs = Run("referencias 8011");
        Assert.That(refs, Does.Contain("sub_8010 ($8010-").And.Contain("La llaman: sub_8000"));
        Assert.That(Run("referencias 00D5"), Does.Contain("Instrucciones que nombran $00D5: 1").And.Contain("$8010 sub_8010 · LDA"));

        string prepared = Run("parche 8010 EA EA EA");
        Assert.That(prepared, Does.Contain("Antes").And.Contain("LDA").And.Contain("Después").And.Contain("NOP").And.Contain("Nada cambió todavía"));
        Assert.That(m.Memory.Peek(0x8010), Is.EqualTo(0xAD), "Preparing does not write.");
        Assert.That(Run("parche 8010 EA EA EA aplicar"), Does.Contain("Cambiado $8010"));
        Assert.That(m.Memory.Peek(0x8010), Is.EqualTo(0xEA));
        Assert.That(Run("parches"), Does.Contain("$8010 AD D5 00 EA EA EA aplicado"));
        Assert.That(Run("parche 8011 00 aplicar"), Does.Contain("se superpone"));
        Assert.That(m.Memory.Peek(0x8011), Is.EqualTo(0xEA));
        Assert.That(Run("parche 8010 ZZ"), Does.Contain("Bytes inválidos"));
        Assert.That(Run("deshacer-parche 8010"), Does.Contain("se restauraron los bytes originales"));
        Assert.That(m.Memory.Peek(0x8010), Is.EqualTo(0xAD));

        string exported = Run("exportar-asm");
        string asm = Directory.GetFiles(Path.Combine(ui.Folder, "out", "autotest-codigo"), "*.asm").Single();
        Assert.That(exported, Does.Contain("Código exportado: " + asm));
        Assert.That(File.ReadAllText(asm), Does.Contain("sub_8010").And.Contain("LDA"));
        File.WriteAllText(asm, "editado a mano");
        Assert.That(Run("exportar-asm \"" + asm + "\""), Does.Contain("no se reemplaza"));
        Assert.That(File.ReadAllText(asm), Is.EqualTo("editado a mano"));

        Assert.That(Run("agregar-variable 00D5 vidas baja al perder"), Does.Contain("\"vidas\" ($00D5) agregada"));
        Assert.That(Run("variables"), Does.Contain("vidas").And.Contain("00D5"));
        byte before = m.Memory.Peek(0xD5);
        Assert.That(Run("cambiar-variable vidas 7"), Does.Contain("Cambio aplicado: $00D5"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(7));
        Assert.That(Run("cambiar-variable vidas 9 congelar"), Does.Contain("congelada"));
        Assert.That(Run("cambiar-variable nadie 1"), Does.Contain("No existe la variable nadie"));
        Assert.That(Run("deshacer-variable"), Does.Contain("deshecho"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(7));
        Assert.That(Run("deshacer-variable todo"), Does.Contain("restaurada"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(before));
        string profile = Path.Combine(ui.Folder, "perfil exportado.json");
        Assert.That(Run("exportar-perfil \"" + profile + "\""), Does.Contain("Perfil exportado: " + profile));
        Assert.That(File.ReadAllText(profile), Does.Contain("\"vidas\""));

        // The visual instrument prepares the same patch before Cambiar writes it.
        Run("hacer code"); ui.Frames(4);
        var code = (CodeWindow)ui.Get("_code");
        typeof(CodeWindow).GetMethod("Analyze", Private)!.Invoke(code, [m]);
        typeof(CodeWindow).GetField("_selected", Private)!.SetValue(code, (uint?)0x8010u);
        typeof(CodeWindow).GetField("_line", Private)!.SetValue(code, (uint?)0x8010u);
        typeof(CodeWindow).GetField("_patchHex", Private)!.SetValue(code, "EA");
        ui.Frames(6); ui.Capture("codigo-vista-previa-parche");
        Assert.That(m.Memory.Peek(0x8010), Is.EqualTo(0xAD), "Drawing the preview changes nothing.");
        Assert.That(console.Output.AllText(), Does.Not.Contain("orden desconocida"));
    }

    [Explicit(Reason)]
    [TestCase(100), TestCase(175)]
    public void EnglishNamesHelpAndFailuresForTheCodeOrders(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "real", games: 4, "--language", "en");
        string game = Path.Combine(ui.Games, "Patch_shop.nes");
        File.WriteAllBytes(game, CodeTerminalServiceTests.Rom());
        ui.Call("OpenFile", game); ui.Frames(4);
        ui.Call("OpenTerminal"); ui.Frames(3);
        var console = (ProgramTerminal)ui.Property("Terminal");
        ui.Enter("help patch"); ui.Frames(2);
        Assert.That(Flat(console), Does.Contain("Usage: patch").And.Not.Contain("Uso:"));
        ui.Enter("references 8000"); ui.Frames(2);
        Assert.That(Flat(console), Does.Contain("Calls: sub_8010"));
        ui.Enter("patch 8010 EA"); ui.Frames(2);
        Assert.That(Flat(console), Does.Contain("Before").And.Contain("After").And.Contain("Nothing changed yet"));
        Assert.That(Flat(console), Does.Contain("$8011 D5 00 CMP"), "The leftover bytes of the old instruction are shown as they will be read.");
        ui.Enter("export-profile x.json"); ui.Frames(2);
        Assert.That(Flat(console), Does.Contain("does not have a profile yet"));
        ui.Capture("codigo-ordenes-en-" + text);
        Assert.That(Flat(console), Does.Not.Contain("unknown command"));
    }
}
