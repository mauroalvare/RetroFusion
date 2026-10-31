using Raylib_cs;

namespace Retro.App;

/// <summary>Captures the native framebuffer at its pixel size, including the interface.</summary>
internal static class FramebufferCapture
{
    public static unsafe void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Raylib.IsWindowReady()) throw new InvalidOperationException("La captura necesita una ventana activa.");

        // raylib 6.0 TakeScreenshot applies DPI to dimensions that already include it on
        // Windows. LoadImageFromScreen reads GetRenderWidth/Height without scaling again.
        Rlgl.DrawRenderBatchActive();
        var image = Raylib.LoadImageFromScreen();
        byte* encoded = null;
        try
        {
            if (!Raylib.IsImageValid(image)) throw new IOException("No se pudo leer el framebuffer.");
            int size = 0;
            fixed (byte* format = ".png\0"u8)
                encoded = Raylib.ExportImageToMemory(image, (sbyte*)format, &size);
            if (encoded == null || size <= 0) throw new IOException("No se pudo codificar la captura.");
            // Native fopen does not reliably handle Unicode paths on Windows.
            File.WriteAllBytes(path, new ReadOnlySpan<byte>(encoded, size));
        }
        finally
        {
            if (encoded != null) Raylib.MemFree(encoded);
            Raylib.UnloadImage(image);
        }
    }
}
