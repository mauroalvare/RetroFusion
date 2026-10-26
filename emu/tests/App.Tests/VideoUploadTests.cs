using Retro.App;
using Retro.Core;

namespace App.Tests;

public sealed class VideoUploadTests
{
    [TestCase(1)] [TestCase(7)] [TestCase(16)] [TestCase(17)] [TestCase(1001)]
    public void ConversionMatchesByteColorLayoutIncludingVectorTail(int width)
    {
        var frame = new VideoFrame(width, 1); var random = new Random(32);
        for (int i = 0; i < width; i++) frame.Pixels[i] = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
        var output = new int[width]; var cache = new VideoUploadCache();
        Assert.That(cache.Convert(frame, output), Is.True);
        for (int i = 0; i < width; i++)
        {
            uint color = (uint)frame.Pixels[i];
            uint expected = 0xFF000000u | (color & 0xFF00u) | ((color & 255) << 16) | ((color >> 16) & 255);
            Assert.That((uint)output[i], Is.EqualTo(expected));
        }
        Assert.That(cache.Convert(frame, output), Is.False);
        frame.Pixels[^1] ^= 0x100; Assert.That(cache.Convert(frame, output), Is.True);
    }
    [Test]
    public void ResizeRestoreAndTextureInvalidationCannotLeaveAStaleImage()
    {
        var cache = new VideoUploadCache(); var first = new VideoFrame(2, 3); var output = new int[6];
        cache.Convert(first, output); Assert.That(cache.Convert(first, output), Is.False);
        var reshaped = new VideoFrame(3, 2);
        Assert.That(cache.Convert(reshaped, output), Is.True);
        cache.Invalidate(); Assert.That(cache.Convert(reshaped, output), Is.True);
        reshaped.Pixels[0] = 0x123456; cache.Convert(reshaped, output);
        Assert.That(cache.Convert(first, output), Is.True);
        Assert.Throws<ArgumentException>(() => cache.Convert(first, new int[5]));
    }
}
