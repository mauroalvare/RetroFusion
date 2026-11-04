using System.Reflection;
using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

/// <summary>The code workshop services the terminal uses, over an own NES program: JSR $8010 / LDA $00D5.</summary>
[NonParallelizable]
public sealed class CodeTerminalServiceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _scope = "";
    private string? _previousProfiles;

    [SetUp]
    public void Isolate()
    {
        _scope = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-code-orders-" + Guid.NewGuid().ToString("N"))).FullName;
        _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_scope, "profiles"));
    }

    [TearDown]
    public void Restore()
    {
        UiText.Language = "es";
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        Assert.That(Path.GetFileName(_scope), Does.StartWith("retro-code-orders-"));
        foreach (string file in Directory.GetFiles(_scope, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_scope, true);
    }

    /// <summary>$8000: JSR $8010; JMP $8000. $8010: LDA $00D5; RTS. Reset vector $8000.</summary>
    internal static byte[] Rom()
    {
        byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
        Array.Fill(rom, (byte)0xEA, 16, 16384);
        byte[] main = [0x20, 0x10, 0x80, 0x4C, 0x00, 0x80];
        byte[] sub = [0xAD, 0xD5, 0x00, 0x60];
        main.CopyTo(rom, 16); sub.CopyTo(rom, 16 + 0x10);
        rom[16 + 0x3FFC] = 0x00; rom[16 + 0x3FFD] = 0x80;
        return rom;
    }

    private (EmulatorHost Host, CodeWindow Code) Open(string? exports = null)
    {
        var host = new EmulatorHost();
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile("propio.nes", Rom(), "propio.nes"));
        var code = new CodeWindow(host, new AiSettings { Provider = "manual", ProvidersDisabled = true }, _ => { }, history: new ModificationHistory(persist: false),
            annotationsDirectory: Path.Combine(_scope, "notes"), exportDirectory: exports ?? Path.Combine(_scope, "codigo"));
        return (host, code);
    }

    private static bool Write(IMachine machine, uint address, byte[] bytes) =>
        (bool)typeof(CodeWindow).GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [machine, address, bytes])!;

    [Test]
    public void PatchesArePreparedAppliedRejectedUndoneAndReapplied()
    {
        var (host, code) = Open(); using var window = code; var m = host.Machine!;
        byte[] state = m.SaveState();
        var preview = code.PreparePatch(0x8010, "EA $ea 0xEA");
        Assert.That((preview.Before, preview.After), Is.EqualTo((new byte[] { 0xAD, 0xD5, 0x00 }, new byte[] { 0xEA, 0xEA, 0xEA })));
        Assert.That(preview.BeforeCode[0], Does.Contain("LDA"));
        Assert.That(preview.AfterCode, Has.Count.EqualTo(3).And.All.Contain("NOP"));
        Assert.That(m.SaveState(), Is.EqualTo(state), "Preparing changes nothing.");
        var shorter = code.PreparePatch(0x8010, "EA");
        Assert.That(shorter.AfterCode, Has.Count.EqualTo(2), "NOP, then the old operand bytes read as a new instruction.");
        Assert.That(shorter.AfterCode[1], Does.StartWith("$8011  D5 00"));
        Assert.That(shorter.BeforeCode, Has.Count.EqualTo(1));
        Assert.That(() => code.PreparePatch(0x8010, "ZZ"), Throws.ArgumentException.With.Message.Contains("Bytes inválidos"));
        Assert.That(() => code.PreparePatch(0x8010, ""), Throws.ArgumentException);
        Assert.That(() => code.PreparePatch(0xFFFF, "EA EA"), Throws.InvalidOperationException.With.Message.Contains("espacio de direcciones"));
        Assert.That(() => code.PreparePatch(0x8010, string.Join(' ', Enumerable.Repeat("EA", CodeWindow.MaxPatchBytes + 1))), Throws.ArgumentException);

        Assert.That(code.ApplyPatch(0x8010, "EA EA EA"), Does.Contain("$8010"));
        Assert.That(Enumerable.Range(0, 3).Select(i => m.Memory.Peek(0x8010 + (uint)i)), Is.All.EqualTo(0xEA));
        var saved = code.SavedPatches(out int damaged).Single();
        Assert.That((saved.Address, saved.Original, saved.Applied, damaged), Is.EqualTo((0x8010u, new byte[] { 0xAD, 0xD5, 0x00 }, true, 0)));
        Assert.That(() => code.ApplyPatch(0x8011, "00"), Throws.InvalidOperationException.With.Message.Contains("superpone"));
        Assert.That(m.Memory.Peek(0x8011), Is.EqualTo(0xEA), "A rejected patch leaves the machine as it was.");

        // The record survives a machine that lost the bytes; reapplying writes them again.
        Assert.That(Write(m, 0x8010, saved.Original), Is.True);
        Assert.That(code.SavedPatches(out _).Single().Applied, Is.False);
        Assert.That(code.ReapplyPatches(), Is.EqualTo((1, 1)));
        Assert.That(code.SavedPatches(out _).Single().Applied, Is.True);

        Assert.That(code.UndoPatch(0x8010), Does.Contain("restauraron"));
        Assert.That(new[] { m.Memory.Peek(0x8010), m.Memory.Peek(0x8011), m.Memory.Peek(0x8012) }, Is.EqualTo(new byte[] { 0xAD, 0xD5, 0x00 }));
        Assert.That(code.SavedPatches(out _), Is.Empty);
        Assert.That(() => code.UndoPatch(0x8010), Throws.InvalidOperationException.With.Message.Contains("No hay un parche"));

        // A damaged backup is listed as damaged, never applied nor removed as if it were complete.
        var notes = (CodeAnnotations)typeof(CodeWindow).GetField("_notes", Private)!.GetValue(code)!;
        notes.Patches["9000"] = "AA|BB CC";
        Assert.That(code.SavedPatches(out damaged), Is.Empty); Assert.That(damaged, Is.EqualTo(1));
        Assert.That(code.ReapplyPatches(), Is.EqualTo((0, 1)));
        Assert.That(() => code.UndoPatch(0x9000), Throws.InstanceOf<InvalidDataException>());
        Assert.That(notes.Patches, Contains.Key("9000"));
    }

    [Test]
    public void ReferencesShowTheRoutineItsCallsAndTheInstructionsThatNameAnAddress()
    {
        var (_, code) = Open(); using var window = code;
        var sub = code.References(0x8011);
        Assert.That(sub.Routine?.Address, Is.EqualTo(0x8010u));
        Assert.That(sub.Callers.Select(link => link.Address), Does.Contain(0x8000u));
        Assert.That(sub.Calls, Is.Empty);
        Assert.That(code.References(0x8000).Calls.Select(link => link.Address), Does.Contain(0x8010u));
        var data = code.References(0x00D5);
        Assert.That((data.Routine, data.MentionCount), Is.EqualTo(((CodeLink?)null, 1)));
        Assert.That(data.Mentions.Single().Address, Is.EqualTo(0x8010u));
        Assert.That(data.Mentions.Single().Name, Does.Contain("LDA"));
        Assert.That(() => code.References(0x10000), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(code.WriteAnnotation(0x8010, "lee vidas", label: true), Is.Null);
        Assert.That(code.References(0x8000).Calls.Single(link => link.Address == 0x8010).Name, Is.EqualTo("lee_vidas"), "Names come from the saved labels.");
    }

    [Test]
    public void ExportsNeverReplaceFilesNorLeavePartialOnes()
    {
        var (host, code) = Open(); using var window = code;
        string first = code.ExportListing(null), second = code.ExportListing(null);
        Assert.That(Path.GetFileName(first), Is.EqualTo("propio.asm"));
        Assert.That(Path.GetFileName(second), Is.EqualTo("propio-2.asm"));
        Assert.That(File.ReadAllText(first), Does.Contain("sub_8010").And.Contain("LDA"));
        string mine = Path.Combine(_scope, "mio.asm");
        File.WriteAllText(mine, "editado a mano");
        Assert.That(() => code.ExportListing(mine), Throws.InstanceOf<IOException>().With.Message.Contains("no se reemplaza"));
        Assert.That(File.ReadAllText(mine), Is.EqualTo("editado a mano"));
        Assert.That(code.ExportListing(Path.Combine(_scope, "nueva", "copia.asm")), Does.EndWith("copia.asm"), "A missing folder is created.");

        // The export folder cannot be created: the button reports it instead of closing the program.
        string blocked = Path.Combine(_scope, "bloqueado");
        File.WriteAllBytes(blocked, [1]);
        var (_, other) = Open(blocked); using var otherWindow = other;
        Assert.That(() => other.ExportListing(null), Throws.InstanceOf<IOException>());
        object?[] arguments = [host.Machine, null, null];
        Assert.That((bool)typeof(CodeWindow).GetMethod("TrySaveListing", Private)!.Invoke(other, arguments)!, Is.False);
        Assert.That((string)arguments[2]!, Does.StartWith("No se pudo exportar el código").And.Contain("a medias"));
        Assert.That(File.ReadAllBytes(blocked), Is.EqualTo(new byte[] { 1 }));

        string profileCopy = Path.Combine(_scope, "perfil.json");
        Assert.That(() => code.ExportProfile(profileCopy), Throws.InvalidOperationException.With.Message.Contains("no tiene perfil"));
        code.AddVariable(0x00D5, "vidas", "baja al perder");
        Assert.That(code.ExportProfile(profileCopy), Is.EqualTo(profileCopy));
        Assert.That(File.ReadAllText(profileCopy), Does.Contain("\"vidas\"").And.Contain("00D5"));
        string exported = File.ReadAllText(profileCopy);
        Assert.That(() => code.ExportProfile(profileCopy), Throws.InstanceOf<IOException>());
        Assert.That(File.ReadAllText(profileCopy), Is.EqualTo(exported));
    }

    [Test]
    public void VariablesAreAddedChangedFrozenAndUndone()
    {
        var (host, code) = Open(); using var window = code; var m = host.Machine!;
        Assert.That(() => code.SetVariable("vidas", 3, null), Throws.InvalidOperationException.With.Message.Contains("No existe"));
        Assert.That(code.AddVariable(0x00D5, "Vidas", "baja al perder"), Does.Contain("vidas").And.Contain("$00D5"));
        Assert.That(Directory.GetFiles(Path.Combine(_scope, "profiles"), "*.json").Single(), Does.EndWith("propio.json"));
        Assert.That(() => code.AddVariable(0x00D6, "vidas", ""), Throws.InvalidOperationException.With.Message.Contains("ya existe"));
        Assert.That(() => code.AddVariable(0x00D6, "___", ""), Throws.InvalidOperationException.With.Message.Contains("nombre"));
        Assert.That(() => code.AddVariable(0x10000, "otra", ""), Throws.InstanceOf<ArgumentOutOfRangeException>());

        byte before = m.Memory.Peek(0xD5);
        Assert.That(code.SetVariable("vidas", 7, null), Does.Contain("$00D5").And.Contain("→ 07"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(7));
        Assert.That(code.SetVariable("vidas", 9, true), Does.Contain("congelada"));
        Assert.That(() => code.SetVariable("vidas", 300, null), Throws.InstanceOf<Exception>());
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(9), "An invalid value leaves the game as it was.");
        Assert.That(code.UndoVariables(all: false), Does.Contain("deshecho"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(7));
        Assert.That(code.UndoVariables(all: true), Does.Contain("restaurada"));
        Assert.That(m.Memory.Peek(0xD5), Is.EqualTo(before));
        Assert.That(() => code.UndoVariables(all: false), Throws.InvalidOperationException);
    }
}
