using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Retro.Core;

namespace Retro.Fusion;

/// <summary>A variable a game keeps in RAM (lives, score, position...).</summary>
public sealed class GameVariable
{
    public string Address { get; set; } = "0000";
    /// <summary>uint8, uint16 (little endian), bcd (n bytes, little endian), ascii (n digits), digits (n bytes of 0-9, most significant first).</summary>
    public string Encoding { get; set; } = "uint8";
    public int Bytes { get; set; } = 1;
    public int Mask { get; set; }
    public int Min { get; set; }
    public int Max { get; set; } = 255;
    public bool ReadOnly { get; set; }
    public string Description { get; set; } = "";

    [JsonIgnore]
    public uint AddressValue => Convert.ToUInt32(Address, 16);

    public int Read(IMemoryView mem)
    {
        uint a = AddressValue;
        int v;
        switch (Encoding)
        {
            case "uint16":
                v = mem.Peek(a) | (mem.Peek(a + 1) << 8);
                break;
            case "bcd":
                v = 0;
                for (int i = Bytes - 1; i >= 0; i--)
                {
                    byte b = mem.Peek((uint)(a + i));
                    v = v * 100 + (b >> 4) * 10 + (b & 0x0F);
                }
                break;
            case "ascii":
            case "digits":
                v = 0;
                for (int i = 0; i < Bytes; i++)
                {
                    int d = mem.Peek((uint)(a + i)) - (Encoding == "ascii" ? '0' : 0);
                    v = v * 10 + Math.Clamp(d, 0, 9);
                }
                break;
            default:
                v = mem.Peek(a);
                break;
        }
        return Mask != 0 ? v & Mask : v;
    }

    public void Write(IMemoryView mem, int value)
    {
        if (ReadOnly)
            return;
        value = Math.Clamp(value, Min, Max);
        uint a = AddressValue;
        switch (Encoding)
        {
            case "uint16":
                mem.Poke(a, (byte)value);
                mem.Poke(a + 1, (byte)(value >> 8));
                break;
            case "bcd":
                for (int i = 0; i < Bytes; i++)
                {
                    int two = value % 100;
                    value /= 100;
                    mem.Poke((uint)(a + i), (byte)(((two / 10) << 4) | (two % 10)));
                }
                break;
            case "ascii":
            case "digits":
                for (int i = Bytes - 1; i >= 0; i--)
                {
                    mem.Poke((uint)(a + i), (byte)((Encoding == "ascii" ? '0' : 0) + value % 10));
                    value /= 10;
                }
                break;
            default:
                if (Mask != 0)
                    value = (mem.Peek(a) & ~Mask) | (value & Mask);
                mem.Poke(a, (byte)value);
                break;
        }
    }
}

/// <summary>A 1-bit graphic in the game's memory (ROM or RAM) that fusion scripts can draw.</summary>
public sealed class SpriteDef
{
    public string Address { get; set; } = "0000";
    public int Width { get; set; } = 8;
    public int Height { get; set; } = 8;
    /// <summary>"rows": each row is Width/8 bytes, left pixel = bit 7 (ZX Spectrum).
    /// "columns": each column is Height/8 bytes, bottom pixel = bit 0 of the first byte (Space Invaders).</summary>
    public string Layout { get; set; } = "rows";
    public string Description { get; set; } = "";

    [JsonIgnore]
    public uint AddressValue => Convert.ToUInt32(Address, 16);

    /// <summary>Reads the bitmap: true = pixel set, indexed [y * Width + x] with y down.</summary>
    public bool[] Decode(IMemoryView mem)
    {
        var px = new bool[Width * Height];
        uint a = AddressValue;
        if (Layout == "columns")
        {
            int bytesPerColumn = (Height + 7) / 8;
            for (int x = 0; x < Width; x++)
                for (int k = 0; k < bytesPerColumn; k++)
                {
                    byte b = mem.Peek((uint)(a + x * bytesPerColumn + k));
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int fromBottom = k * 8 + bit;
                        if (fromBottom < Height && (b & (1 << bit)) != 0)
                            px[(Height - 1 - fromBottom) * Width + x] = true;
                    }
                }
        }
        else
        {
            int bytesPerRow = (Width + 7) / 8;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    byte b = mem.Peek((uint)(a + y * bytesPerRow + x / 8));
                    if ((b & (0x80 >> (x % 8))) != 0)
                        px[y * Width + x] = true;
                }
        }
        return px;
    }
}

public sealed class ProfileMatch
{
    public List<string> Files { get; set; } = new();
    public string? NamePattern { get; set; }
}

/// <summary>
/// What the fusion engine (and an AI) needs to know about one game: its machine, its
/// RAM variables, its controls and how to get from power-on to playing.
/// </summary>
public sealed class GameProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Machine id: zx48, invaders...</summary>
    public string Machine { get; set; } = "";
    public ProfileMatch Match { get; set; } = new();
    public string Description { get; set; } = "";
    public Dictionary<string, GameVariable> Variables { get; set; } = new();
    /// <summary>Abstract control name (left, right, fire, jump, start, coin) -> machine key.</summary>
    public Dictionary<string, string> Controls { get; set; } = new();
    /// <summary>Steps from power-on to playing: "wait N", "press KEY N", "load-tape".</summary>
    public List<string> Boot { get; set; } = new();
    public string Verified { get; set; } = "";
    /// <summary>Graphics the fusion scripts can draw, by name.</summary>
    public Dictionary<string, SpriteDef> Sprites { get; set; } = new();
    /// <summary>
    /// JavaScript (one line per entry) that adds helper functions to the game handle
    /// <c>g</c> of the fusion scripts: where the hero and the enemies are, how to kill them...
    /// </summary>
    public List<string> Helpers { get; set; } = new();
    /// <summary>What each helper does, for the AI that writes fusion scripts.</summary>
    public List<string> HelpersDoc { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static GameProfile Parse(string json) =>
        JsonSerializer.Deserialize<GameProfile>(json, Json) ?? throw new InvalidDataException("Perfil vacío.");

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>True when one of the files belongs to this game.</summary>
    public bool Matches(IReadOnlyList<MediaFile> files)
    {
        if (Match.Files.Count > 0 && Match.Files.All(n => files.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
            return true;
        if (Match.NamePattern != null)
            return files.Any(f => Regex.IsMatch(f.Name, Match.NamePattern, RegexOptions.IgnoreCase))
                   || files.Any(f => Regex.IsMatch(f.Source, Match.NamePattern, RegexOptions.IgnoreCase));
        return false;
    }
}

/// <summary>Built-in profiles plus the ones the user (or the AI) saved.</summary>
public static class ProfileLibrary
{
    private static List<GameProfile>? _builtIn;

    public static IReadOnlyList<GameProfile> BuiltIn
    {
        get
        {
            if (_builtIn != null)
                return _builtIn;
            var asm = Assembly.GetExecutingAssembly();
            _builtIn = asm.GetManifestResourceNames()
                .Where(n => n.Contains(".Profiles.") && n.EndsWith(".json"))
                .Select(n =>
                {
                    using var s = asm.GetManifestResourceStream(n)!;
                    using var r = new StreamReader(s);
                    return GameProfile.Parse(r.ReadToEnd());
                })
                .ToList();
            return _builtIn;
        }
    }

    public static string UserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "profiles");

    public static IReadOnlyList<GameProfile> All()
    {
        var list = new List<GameProfile>(BuiltIn);
        if (Directory.Exists(UserFolder))
            foreach (var f in Directory.GetFiles(UserFolder, "*.json"))
            {
                try
                {
                    var p = GameProfile.Parse(File.ReadAllText(f));
                    list.RemoveAll(x => x.Id == p.Id);
                    list.Add(p);
                }
                catch (JsonException)
                {
                }
            }
        return list;
    }

    public static GameProfile? Find(string id) => All().FirstOrDefault(p => p.Id == id);

    public static GameProfile? ForFiles(IReadOnlyList<MediaFile> files) => All().FirstOrDefault(p => p.Matches(files));

    public static void Save(GameProfile profile)
    {
        Directory.CreateDirectory(UserFolder);
        File.WriteAllText(Path.Combine(UserFolder, profile.Id + ".json"), profile.ToJson());
    }
}
