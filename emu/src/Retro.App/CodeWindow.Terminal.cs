using System.Globalization;
using System.Text;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

public sealed record CodeLink(uint Address, string Name);
/// <summary>A saved patch: the bytes kept for undo, the bytes written and whether the machine runs them now.</summary>
public sealed record SavedPatch(uint Address, byte[] Original, byte[] Replacement, bool Applied, string Routine);
/// <summary>What a patch would change, prepared without touching the machine.</summary>
public sealed record PatchPreview(uint Address, byte[] Before, byte[] After, IReadOnlyList<string> BeforeCode, IReadOnlyList<string> AfterCode, bool ReplacesSaved);
/// <summary>Where an address sits in the reconstructed code and which instructions call or mention it.</summary>
public sealed record CodeReferences(uint Address, string Name, CodeLink? Routine, uint End, int Instructions, bool Executed,
    IReadOnlyList<CodeLink> Calls, IReadOnlyList<CodeLink> Callers, IReadOnlyList<CodeLink> Mentions, int MentionCount, int RoutineCount);

/// <summary>
/// The code workshop's services for the terminal: the same patch transactions, references and exports
/// as the visual instrument, always on the original loaded right now.
/// </summary>
public sealed partial class CodeWindow
{
    private IMachine OriginalMachine() { OriginalAnnotations(); return _machine!; }

    public IReadOnlyList<SavedPatch> SavedPatches(out int damaged)
    {
        var m = OriginalMachine();
        var list = new List<SavedPatch>(); damaged = 0;
        foreach (var (key, value) in _notes.Patches.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!TryReadPatch(key, value, m.Memory.AddressBits, out uint a, out byte[] original, out byte[] replacement)) { damaged++; continue; }
            bool applied = replacement.Select((b, i) => m.Memory.Peek(a + (uint)i) == b).All(x => x);
            list.Add(new(a, original, replacement, applied, _map?.RoutineAt(a) is { } r ? Name(m, r.Start) : ""));
        }
        return list;
    }

    /// <summary>Preparation: validates bytes, range, overlap and backups, and shows the code before and after.</summary>
    public PatchPreview PreparePatch(uint address, string hex)
    {
        var m = OriginalMachine();
        if (!TryParsePatchBytes(hex, out byte[] bytes, out string? problem)) throw new ArgumentException(UiText.Message(problem!));
        if (bytes.Length == 0) throw new ArgumentException(UiText.Get("Escribí al menos un byte hexadecimal."));
        if (PlanPatch(m, address, bytes, out _, out byte[] replacement) is { } rejected) throw new InvalidOperationException(UiText.Message(rejected));
        byte[] before = Enumerable.Range(0, replacement.Length).Select(i => m.Memory.Peek(address + (uint)i)).ToArray();
        var (beforeCode, afterCode) = Compare(m, address, before, replacement);
        return new(address, before, replacement, beforeCode, afterCode, _notes.Patches.ContainsKey(CodeAnnotations.Key(address)));
    }

    /// <summary>
    /// Both listings run until they meet on the same instruction boundary, so a shorter patch also shows how
    /// the leftover bytes of the old instruction will be read.
    /// </summary>
    private (IReadOnlyList<string> Before, IReadOnlyList<string> After) Compare(IMachine m, uint start, byte[] before, byte[] after)
    {
        var old = new List<string>(); var next = new List<string>();
        uint oldEnd = Decode(m, start, before, start, start + (uint)before.Length, old);
        uint newEnd = Decode(m, start, after, start, start + (uint)after.Length, next);
        for (int round = 0; round < 8 && oldEnd != newEnd; round++)
        {
            if (oldEnd < newEnd) oldEnd = Decode(m, start, before, oldEnd, newEnd, old);
            else newEnd = Decode(m, start, after, newEnd, oldEnd, next);
        }
        return (old, next);
    }

    private uint Decode(IMachine m, uint start, byte[] bytes, uint from, uint until, List<string> lines)
    {
        byte Peek(uint a) => a >= start && a < start + (uint)bytes.Length ? bytes[a - start] : m.Memory.Peek(a);
        uint a = from;
        while (a < until && lines.Count < 64)
        {
            var ins = m.Disassembler.Disassemble(Peek, a);
            lines.Add(UiText.Format($"${a:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2"))),-12} {Pretty(m, ins.Text)}"));
            a += (uint)Math.Max(1, ins.Length);
        }
        return a;
    }

    /// <summary>Applies after the same checks as PreparePatch; the machine is restored when any byte refuses the write.</summary>
    public string ApplyPatch(uint address, string hex)
    {
        PreparePatch(address, hex);
        var m = _machine!;
        TryParsePatchBytes(hex, out byte[] bytes, out _);
        PlanPatch(m, address, bytes, out byte[] original, out byte[] replacement);
        string message = CommitPatch(m, address, original, replacement, out bool applied);
        if (!applied) throw new InvalidOperationException(UiText.Message(message));
        return UiText.Message(message);
    }

    public string UndoPatch(uint address)
    {
        var m = OriginalMachine();
        string key = CodeAnnotations.Key(address);
        if (!_notes.Patches.TryGetValue(key, out string? value)) throw new InvalidOperationException(UiText.Format($"No hay un parche guardado en ${address:X4}."));
        if (!TryReadPatch(key, value, m.Memory.AddressBits, out uint a, out byte[] original, out _))
            throw new InvalidDataException(UiText.Get("Parche guardado inválido o incompleto. Se conserva el respaldo; volvé a cargar el original antes de corregirlo."));
        if (!Write(m, a, original))
            throw new InvalidOperationException(UiText.Get("No se pudo deshacer el parche. La partida y el respaldo se conservaron; volvé a cargar el original."));
        _notes.Patches.Remove(key); _notes.Save(AnnotationId); Analyze(m);
        return UiText.Get("Parche deshecho; se restauraron los bytes originales.");
    }

    /// <summary>Writes every valid saved patch again, each one as its own transaction.</summary>
    public (int Applied, int Total) ReapplyPatches()
    {
        var m = OriginalMachine();
        int applied = 0;
        foreach (var (key, value) in _notes.Patches)
            if (TryReadPatch(key, value, m.Memory.AddressBits, out uint address, out _, out byte[] replacement) && Write(m, address, replacement)) applied++;
        if (applied > 0) Analyze(m);
        return (applied, _notes.Patches.Count);
    }


    /// <summary>The routine holding an address, its calls in both directions, and the known instructions that name it.</summary>
    public CodeReferences References(uint address)
    {
        var m = OriginalMachine();
        if (m.Memory.AddressBits < 32 && address >= 1u << m.Memory.AddressBits)
            throw new ArgumentOutOfRangeException(nameof(address), UiText.Get("La dirección está fuera de esta máquina."));
        if (_map == null) Analyze(m);
        var map = _map!;
        CodeLink Link(uint a) => new(a, Name(m, a));
        // An operand byte belongs to the routine of the instruction that covers it.
        var routine = map.RoutineAt(address) ?? map.Code.Where(pair => pair.Key < address && address < pair.Key + (uint)Math.Max(1, pair.Value.Length))
            .Select(pair => map.RoutineAt(pair.Key)).FirstOrDefault(found => found != null);
        var mentions = new List<CodeLink>(); int count = 0;
        foreach (uint a in map.Code.Keys.Order())
        {
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            bool named = AddressToken.Matches(ins.Text).Any(match => uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) == address);
            if (!named) continue;
            count++;
            if (mentions.Count < 50) mentions.Add(new(a, (map.RoutineAt(a) is { } owner ? Name(m, owner.Start) : "") + " · " + Pretty(m, ins.Text)));
        }
        return new(address, Name(m, address), routine == null ? null : Link(routine.Start), routine?.End ?? 0, routine?.Instructions.Count ?? 0,
            routine?.Executed ?? false, routine?.Calls.Order().Select(Link).ToArray() ?? [], routine?.Callers.Order().Select(Link).ToArray() ?? [],
            mentions, count, map.Routines.Count);
    }

    /// <summary>The disassembly with saved names and comments, as a new file; null path uses the export folder.</summary>
    public string ExportListing(string? path)
    {
        var m = OriginalMachine();
        if (!TrySaveListing(m, path, out string message)) throw new IOException(message);
        return message;
    }

    /// <summary>A shareable copy of this original's profile (variables, controls and boot) as a new JSON file.</summary>
    public string ExportProfile(string path)
    {
        OriginalMachine();
        if (_profile == null) throw new InvalidOperationException(UiText.Get("Este juego todavía no tiene perfil. Agregá una variable o preparalo primero."));
        var persistent = GameProfile.Parse(_profile.ToJson());
        foreach (var name in _discoveredLabels.Keys) persistent.Variables.Remove(name);
        return NewFiles.Write(path, Encoding.UTF8.GetBytes(persistent.ToJson()));
    }

    /// <summary>Adds a byte variable at an address, as the finder does: the profile is created if needed and saved.</summary>
    public string AddVariable(uint address, string name, string description)
    {
        var m = OriginalMachine();
        if (m.Memory.AddressBits > 16) throw new InvalidOperationException(UiText.Get("Las variables por órdenes están disponibles en máquinas de 8 bits."));
        if (address >= 1u << m.Memory.AddressBits) throw new ArgumentOutOfRangeException(nameof(address), UiText.Get("La dirección está fuera de esta máquina."));
        _newVarName = name; _newVarDescription = description;
        if (!AddVariable(m, address, m.Memory.Peek(address))) throw new InvalidOperationException(UiText.Message(_status));
        return UiText.Message(_status);
    }

    /// <summary>Changes a variable through the same undoable modification history as Modificar y analizar.</summary>
    public string SetVariable(string name, int value, bool? freeze)
    {
        var m = OriginalMachine();
        if (_profile == null || !_profile.Variables.TryGetValue(name, out var variable))
            throw new InvalidOperationException(UiText.Format($"No existe la variable {UiText.Raw(name)}. variables lista las conocidas."));
        if (variable.ReadOnly) throw new InvalidOperationException(UiText.Get("Esa variable es de solo lectura."));
        var preview = PreviewVariable(_profile, name, value, m.Memory);
        bool frozen = freeze ?? _modifications?.Frozen.ContainsKey(name) == true;
        if (!ApplyModifications([new(name, value, frozen)])) throw new InvalidOperationException(UiText.Message(_status));
        return string.Join(" · ", preview.Select(cell => UiText.Format($"${cell.Address:X4}: {cell.Before:X2} → {cell.After:X2}")))
            + (frozen ? UiText.Get(" · congelada") : "");
    }

    /// <summary>Undoes the last variable change, or restores the game to the moment before the first one.</summary>
    public string UndoVariables(bool all)
    {
        OriginalMachine();
        if (_modifications == null || !(all ? _modifications.CanRestore : _modifications.CanUndo))
            throw new InvalidOperationException(UiText.Get("No hay cambios de variables para deshacer en esta partida."));
        RestoreModifications(undo: !all);
        return UiText.Message(_status);
    }

}
