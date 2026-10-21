namespace Retro.Core;

/// <summary>Portable libraries: package instructions alone must not hide an existing library.</summary>
public static class PlayerFolders
{
    public static string? Find(string name, string? fallback = null, string? startDirectory = null)
    {
        string? empty = null;
        for (var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            try
            {
                // Exact case keeps the source tree's firmware folder "roms" separate from games.
                var match = dir.EnumerateDirectories().FirstOrDefault(d => d.Name == name &&
                    !d.Attributes.HasFlag(FileAttributes.ReparsePoint));
                if (match == null) continue;
                empty ??= match.FullName;
                if (HasContent(match.FullName)) return match.FullName;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (fallback != null && HasContent(fallback)) return fallback;
        return empty ?? fallback;
    }

    private static bool HasContent(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint };
            return Directory.EnumerateFiles(folder, "*", options).Any(file =>
                !Path.GetRelativePath(folder, file).Replace('\\', '/').StartsWith(".retroemu/", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(file).ToLowerInvariant() is not
                    ("leeme.txt" or "readme.md" or "readme.txt" or ".gitkeep" or ".ds_store" or "desktop.ini" or "thumbs.db"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
