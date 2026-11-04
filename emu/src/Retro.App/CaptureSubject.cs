using System.Security.Cryptography;
using System.Text;

namespace Retro.App;

/// <summary>
/// The game a screenshot belongs to. Kind is the machine id of an original, "fusion" or "creado";
/// Identity is what stays the same for that game (content hash, fusion id or creation folder), so two
/// games with one title get separate folders and every version of one game shares its folder.
/// </summary>
public sealed record CaptureSubject(string Title, string Kind, string Identity)
{
    /// <summary>Folder for screens taken while a machine runs without a game. Physical names stay in Spanish, like juego.prg.</summary>
    public const string GeneralFolder = "Sin juego activo";

    public static CaptureSubject NoGame(string? machine) => new(string.IsNullOrWhiteSpace(machine) ? "captura" : machine, "", "");
    public bool HasGame => Kind.Length > 0;

    /// <summary>"Title [kind-123abc]": readable first, and the short id tells same-titled games apart.</summary>
    public string Folder => HasGame ? ScreenCapture.SafeName(Title) + " [" + ScreenCapture.SafeName(Kind) + "-" + ShortId + "]" : GeneralFolder;

    private string ShortId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Kind + "\n" + Identity)))[..6].ToLowerInvariant();
}
