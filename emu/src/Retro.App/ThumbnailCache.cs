using Raylib_cs;
using StbImageSharp;

namespace Retro.App;

/// <summary>Visible items request images; one CPU decoder feeds a bounded LRU of main-thread GPU textures.</summary>
internal sealed unsafe class ThumbnailCache : IDisposable
{
    private sealed class Entry(string key, string? path)
    {
        public readonly string Key = key;
        public readonly string? Path = path;
        public Texture2D Texture;
        public bool Queued, Tried;
        public LinkedListNode<string>? Recent;
    }
    public const int Capacity = 32;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();
    private readonly Queue<Entry> _waiting = new();
    private Task<Image?>? _decoding;
    private Entry? _active;
    private bool _disposed;

    public bool TryGet(string key, Func<string?> find, out Texture2D texture)
    {
        texture = default;
        if (_disposed) return false;
        if (!_entries.TryGetValue(key, out var entry))
        {
            if (_entries.Count == Capacity)
            {
                string oldest = _recent.Last!.Value;
                Release(_entries[oldest]); _entries.Remove(oldest); _recent.RemoveLast();
            }
            entry = new Entry(key, find()); _entries.Add(key, entry);
            entry.Recent = _recent.AddFirst(key);
        }
        else { _recent.Remove(entry.Recent!); _recent.AddFirst(entry.Recent!); }
        if (!entry.Tried && !entry.Queued && entry.Path != null && _waiting.Count < 8)
        { entry.Queued = true; _waiting.Enqueue(entry); }
        texture = entry.Texture;
        return texture.Id != 0;
    }

    public void Poll()
    {
        if (_disposed) return;
        if (_decoding is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully && done.Result is { } picture)
            {
                try
                {
                    if (_active != null && _entries.TryGetValue(_active.Key, out var entry) && ReferenceEquals(entry, _active))
                    {
                        entry.Texture = Raylib.LoadTextureFromImage(picture);
                        Raylib.SetTextureFilter(entry.Texture, TextureFilter.Point);
                    }
                }
                finally { Raylib.UnloadImage(picture); }
            }
            if (_active != null) { _active.Tried = true; _active.Queued = false; }
            _decoding = null; _active = null;
        }
        while (_decoding == null && _waiting.TryDequeue(out var queued))
        {
            if (!_entries.TryGetValue(queued.Key, out var entry) || !ReferenceEquals(entry, queued)) continue;
            _active = queued;
            _decoding = Task.Run(() => Decode(queued.Path!));
        }
    }

    private static Image? Decode(string path)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length <= 0 || source.Length > ThumbnailFiles.MaxBytes) return null;
            byte[] bytes = new byte[(int)source.Length]; source.ReadExactly(bytes);
            if (!ThumbnailFiles.Dimensions(bytes, out int width, out int height)) return null;
            var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            if (decoded.Width != width || decoded.Height != height || decoded.Data.Length != checked(width * height * 4)) return null;
            double scale = Math.Min(1, Math.Min(256.0 / width, 192.0 / height));
            int outputWidth = Math.Max(1, (int)(width * scale)), outputHeight = Math.Max(1, (int)(height * scale));
            var image = Raylib.GenImageColor(outputWidth, outputHeight, Color.Blank);
            if (image.Data == null) return null;
            bool keep = false;
            try
            {
                Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
                if (image.Data == null) return null;
                fixed (byte* pixels = decoded.Data)
                    for (int y = 0; y < outputHeight; y++)
                        for (int x = 0; x < outputWidth; x++)
                            ((uint*)image.Data)[y * outputWidth + x] = ((uint*)pixels)[(y * height / outputHeight) * width + x * width / outputWidth];
                keep = true; return image;
            }
            finally { if (!keep) Raylib.UnloadImage(image); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException) { return null; }
    }

    private static void Release(Entry entry) { if (entry.Texture.Id != 0) Raylib.UnloadTexture(entry.Texture); }
    public void Clear()
    {
        foreach (var entry in _entries.Values) Release(entry);
        _entries.Clear(); _recent.Clear(); _waiting.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Clear();
        if (_decoding is { } pending)
            _ = pending.ContinueWith(t => { if (t.IsCompletedSuccessfully && t.Result is { } picture) Raylib.UnloadImage(picture); }, TaskScheduler.Default);
        _decoding = null; _active = null;
    }
}
