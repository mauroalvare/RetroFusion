using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

/// <summary>Frozen job inputs, checked against the live library and files before any result is saved.</summary>
public sealed class FusionInputSnapshot
{
    private sealed record Stamp(long Length, long Written, long Created, FileAttributes Attributes);
    private sealed record Input(string Path, string FileHash, string MediaHash, string ProfileJson,
        string ProfilePath, string? ProfileHash, string Machine, string FirmwareHash, Stamp? FileStamp, Stamp? ProfileStamp);
    private readonly Dictionary<string, Input> _inputs = new();
    private readonly IReadOnlyDictionary<string, GameSource> _originals;
    private readonly string _firmware;
    public IReadOnlyDictionary<string, GameSource> Sources { get; }

    private FusionInputSnapshot(IReadOnlyDictionary<string, GameSource> originals,
        Dictionary<string, GameSource> frozen, string firmware)
    {
        _originals = originals; Sources = new ReadOnlyDictionary<string, GameSource>(frozen); _firmware = firmware;
    }

    public static FusionInputSnapshot Capture(IReadOnlyDictionary<string, GameSource> sources,
        IReadOnlyDictionary<string, string> paths, string firmware)
    {
        var frozen = new Dictionary<string, GameSource>();
        var snapshot = new FusionInputSnapshot(new Dictionary<string, GameSource>(sources), frozen, firmware);
        foreach (var (id, source) in sources)
        {
            if (string.IsNullOrWhiteSpace(id) || !paths.TryGetValue(id, out var path) || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("No se encontró un original de esta idea. Actualizá la biblioteca.");
            byte[] bytes = File.ReadAllBytes(path);
            string media = MediaHash(source.Files);
            if (media != MediaHash(MediaArchive.Open(bytes, Path.GetFileName(path), path)))
                throw new InvalidDataException("Un original cambió desde que se eligió. Actualizá la biblioteca antes de crear.");
            string profileJson = source.Profile.ToJson();
            string profilePath = Path.Combine(ProfileLibrary.UserFolder, id + ".json");
            if (File.Exists(profilePath) && GameProfile.Parse(File.ReadAllText(profilePath)).ToJson() != profileJson)
                throw new InvalidDataException("Un perfil cambió desde que se eligió. Actualizá la biblioteca antes de crear.");
            frozen[id] = new(GameProfile.Parse(profileJson),
                source.Files.Select(f => f with { Data = (byte[])f.Data.Clone() }).ToArray(), source.Folder);
            snapshot._inputs[id] = new(path, Hash(bytes), media, profileJson, profilePath, FileHash(profilePath),
                source.Profile.Machine, MachineFactory.FirmwareFingerprint(source.Profile.Machine, firmware),
                Metadata(path), Metadata(profilePath));
        }
        snapshot.Ensure();
        return snapshot;
    }

    public bool FilesMayHaveChanged => _inputs.Values.Any(i => Metadata(i.Path) != i.FileStamp || Metadata(i.ProfilePath) != i.ProfileStamp);

    /// <summary>Research may replace its own profile, while original bytes and firmware remain fixed.</summary>
    public void EnsureOriginals()
    {
        foreach (var input in _inputs.Values)
            if (FileHash(input.Path) != input.FileHash ||
                MachineFactory.FirmwareFingerprint(input.Machine, _firmware) != input.FirmwareHash)
                throw new InvalidDataException("Un original o su firmware cambió durante el trabajo. Se conservaron los avances anteriores.");
    }

    public void Ensure(IReadOnlyDictionary<string, GameSource>? current = null)
    {
        EnsureOriginals();
        current ??= _originals;
        foreach (var (id, input) in _inputs)
            if (!current.TryGetValue(id, out var source) || MediaHash(source.Files) != input.MediaHash ||
                source.Profile.ToJson() != input.ProfileJson || FileHash(input.ProfilePath) != input.ProfileHash)
                throw new InvalidDataException("Los originales o perfiles cambiaron durante la creación. No se guardó una fusión con datos anteriores.");
        foreach (string id in _inputs.Keys.ToArray())
            _inputs[id] = _inputs[id] with { FileStamp = Metadata(_inputs[id].Path), ProfileStamp = Metadata(_inputs[id].ProfilePath) };
    }

    private static Stamp? Metadata(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks, file.Attributes) : null;
    }
    private static string? FileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string MediaHash(IEnumerable<MediaFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var file in files)
        {
            foreach (byte[] data in new[] { Encoding.UTF8.GetBytes(file.Name), Encoding.UTF8.GetBytes(file.Source), file.Data })
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
                hash.AppendData(length); hash.AppendData(data);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
