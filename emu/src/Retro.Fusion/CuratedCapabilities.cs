using System.Security.Cryptography;
using Retro.Core;
using Retro.Systems.Arcade;

namespace Retro.Fusion;

/// <summary>Recover curated capabilities hidden by an automatic alias, only for an exact supported edition.</summary>
public static class CuratedCapabilities
{
    // Complete 8 KiB image, h/g/f/e order. All four chip CRCs also match the documented board edition.
    private const string ArcadeEdition = "7446E0994117596DE5206519E693F8875FF3455E0BE121D5CB975C3BCC224C4E";

    public static GameProfile Resolve(GameProfile selected, IReadOnlyList<MediaFile> files)
    {
        if (selected.Machine != "invaders" || selected.Helpers.Count > 0 || SpaceInvadersRom.Probe(files) < 80) return selected;
        byte[] image;
        try { image = SpaceInvadersRom.Build(files).rom; }
        catch (InvalidDataException) { return selected; }
        if (Convert.ToHexString(SHA256.HashData(image)) != ArcadeEdition) return selected;
        var curated = GameProfile.Parse(ProfileLibrary.BuiltIn.Single(p => p.Id == "invaders").ToJson());
        var resolved = GameProfile.Parse(selected.ToJson());
        foreach (var (name, value) in curated.Variables) resolved.Variables.TryAdd(name, value);
        foreach (var (name, value) in curated.Sprites) resolved.Sprites.TryAdd(name, value);
        resolved.Helpers = curated.Helpers.ToList(); resolved.HelpersDoc = curated.HelpersDoc.ToList();
        resolved.Verified += "\nFunciones curadas recuperadas para la edición completa SHA-256 " + ArcadeEdition +
            ". Se conservaron el identificador, arranque, controles y variables del perfil seleccionado.";
        return resolved;
    }
}
