using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Update;

public sealed record ReleaseAsset(Version Version, string Runtime, string FileName, Uri Url, long Size, string Sha256);
public sealed record UpdateProgress(string Message, double? Fraction = null);

/// <summary>Only stable, platform-matched assets from the application's public repository.</summary>
public sealed class ReleaseClient(HttpClient http)
{
    public const string Repository = "mauroalvare/RetroFusion";
    public static readonly Uri Latest = new($"https://api.github.com/repos/{Repository}/releases/latest");
    public const long MaxDownload = 128L * 1024 * 1024;
    public static string? Runtime => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64
        ? null : OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : null;

    public static Version ParseVersion(string text)
    {
        if (!Regex.IsMatch(text, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$") || !Version.TryParse(text, out var version))
            throw new InvalidDataException("La versión de la actualización no es válida.");
        return version;
    }

    public async Task<ReleaseAsset?> CheckAsync(Version current, string runtime, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RetroEmu", current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        await CopyBounded(stream, body, 4 * 1024 * 1024, ct);
        using var doc = JsonDocument.Parse(body.ToArray());
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v')) return null;
        Version version;
        try { version = ParseVersion(tag[1..]); } catch (InvalidDataException) { return null; }
        if (version <= current) return null;
        string extension = runtime switch { "win-x64" => ".zip", "linux-x64" => ".tar.gz", _ => throw new InvalidDataException("Sistema sin paquete automático.") };
        string name = $"RetroEmu-{version.ToString(3)}-{runtime}{extension}";
        var matches = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("No hay un paquete único para este sistema.");
        var asset = matches[0];
        long size = asset.GetProperty("size").GetInt64();
        string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        string url = asset.GetProperty("browser_download_url").GetString() ?? "";
        string expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        if (url != expected || size is < 1 or > MaxDownload || asset.GetProperty("state").GetString() != "uploaded"
            || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("El paquete no tiene una descarga verificable del repositorio oficial.");
        return new(version, runtime, name, new Uri(url), size, digest[7..]);
    }

    public async Task DownloadAsync(ReleaseAsset asset, string path, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { Scheme: not "https" }) throw new InvalidDataException("La descarga no usa HTTPS.");
        if (response.Content.Headers.ContentLength is long length && length != asset.Size) throw new InvalidDataException("Tamaño de descarga incorrecto.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        byte[] buffer = new byte[65536]; long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += count;
            if (total > asset.Size || total > MaxDownload) throw new InvalidDataException("La descarga supera el tamaño publicado.");
            hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
            progress?.Report(new("Descargando actualización...", (double)total / asset.Size));
        }
        if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La descarga está incompleta o no coincide con su verificación.");
        output.Flush(true);
    }

    internal static async Task CopyBounded(Stream input, Stream output, long limit, CancellationToken ct)
    {
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("El archivo supera el tamaño permitido.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }
}
