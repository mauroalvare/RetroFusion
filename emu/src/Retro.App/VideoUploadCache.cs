using System.Numerics;
using Retro.Core;

namespace Retro.App;

/// <summary>Converts changed ARGB frames into the persistent RGBA upload buffer.</summary>
public sealed class VideoUploadCache
{
    private int[] _previous = [];
    private int _width, _height;
    private bool _valid;
    public void Invalidate() => _valid = false;
    public bool Convert(VideoFrame frame, Span<int> rgba)
    {
        ReadOnlySpan<int> source = frame.Pixels;
        if (rgba.Length != source.Length) throw new ArgumentException("El búfer no coincide con la pantalla.", nameof(rgba));
        if (_valid && frame.Width == _width && frame.Height == _height && source.SequenceEqual(_previous)) return false;
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var alpha = new Vector<int>(unchecked((int)0xFF000000));
            var bytes = new Vector<int>(255); var green = new Vector<int>(0xFF00);
            for (; i <= source.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                var value = new Vector<int>(source.Slice(i, Vector<int>.Count));
                (alpha | ((value & bytes) << 16) | (value & green) | ((value >> 16) & bytes)).CopyTo(rgba[i..]);
            }
        }
        for (; i < source.Length; i++)
        {
            int value = source[i];
            rgba[i] = unchecked((int)0xFF000000) | ((value & 255) << 16) | (value & 0xFF00) | ((value >> 16) & 255);
        }
        if (_previous.Length != source.Length) _previous = new int[source.Length];
        source.CopyTo(_previous); _width = frame.Width; _height = frame.Height; _valid = true;
        return true;
    }
}
