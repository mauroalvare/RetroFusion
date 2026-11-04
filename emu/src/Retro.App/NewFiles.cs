namespace Retro.App;

/// <summary>
/// Exports that never replace a file: CreateNew claims the name atomically, and a failed write removes
/// the partial file so nothing half-written looks like a finished export.
/// </summary>
public static class NewFiles
{
    private const int MaxCopies = 9999;

    /// <summary>Writes a file that must not exist yet; an existing one is reported, never replaced.</summary>
    public static string Write(string path, byte[] data)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) throw new IOException(UiText.Format($"{UiText.Raw(path)} es una carpeta. Indicá un archivo nuevo."));
        if (File.Exists(path)) throw new IOException(UiText.Format($"Ya existe {UiText.Raw(path)}. Elegí otro nombre; no se reemplaza."));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!TryCreate(path, data)) throw new IOException(UiText.Format($"Ya existe {UiText.Raw(path)}. Elegí otro nombre; no se reemplaza."));
        return path;
    }

    /// <summary>folder/stem.ext, or stem-2.ext, stem-3.ext... when taken.</summary>
    public static string WriteNumbered(string folder, string stem, string extension, byte[] data)
    {
        Directory.CreateDirectory(folder);
        for (int copy = 1; copy <= MaxCopies; copy++)
        {
            string path = Path.Combine(folder, stem + (copy == 1 ? "" : "-" + copy) + extension);
            if (TryCreate(path, data)) return path;
        }
        throw new IOException(UiText.Format($"Ya hay {MaxCopies} archivos {UiText.Raw(stem + extension)} en {UiText.Raw(folder)}."));
    }

    private static bool TryCreate(string path, byte[] data)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch (IOException) when (File.Exists(path)) { return false; }
        try
        {
            using (stream) { stream.Write(data); stream.Flush(flushToDisk: true); }
        }
        catch
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return true;
    }
}
