using System.Security.Cryptography;
using System.Text;

namespace Retro.App;

public sealed record StateSlotInfo(int Number, DateTime? SavedAt, string? Problem = null);

/// <summary>Content-bound slot paths and metadata. Existing machine-wide states remain read-only in the UI.</summary>
public sealed class StateSlotStore(string? folder = null)
{
    private readonly string _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "states");

    public static string GameKey(string machine, IEnumerable<string> mediaHashes, string firmware) =>
        ModificationHistory.Key(machine, mediaHashes, firmware) ??
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("system-only\0" + machine + "\0" + firmware)));

    public string PathFor(string key, int slot)
    {
        ValidateSlot(slot);
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit)) throw new ArgumentException("Identidad de partida inválida.");
        return Path.Combine(_folder, key.ToUpperInvariant(), $"slot{slot}.rfstate");
    }

    public string LegacyPath(string machine, int slot)
    {
        ValidateSlot(slot);
        if (string.IsNullOrWhiteSpace(machine) || machine.Length > 40 || !machine.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Identidad de máquina inválida.");
        return Path.Combine(_folder, $"{machine}_slot{slot}.rfstate");
    }

    public IReadOnlyList<StateSlotInfo> List(string key) => Enumerable.Range(1, 5).Select(n => ReadDate(PathFor(key, n), n)).ToArray();
    public IReadOnlyList<StateSlotInfo> ListLegacy(string machine) => Enumerable.Range(1, 5).Select(n => ReadDate(LegacyPath(machine, n), n)).ToArray();

    private static StateSlotInfo ReadDate(string path, int number)
    {
        try
        {
            var file = new FileInfo(path);
            return new(number, file.Exists ? file.LastWriteTime : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(number, null, "No se pudo leer el archivo."); }
    }
    private static void ValidateSlot(int slot)
    {
        if (slot is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(slot));
    }
}