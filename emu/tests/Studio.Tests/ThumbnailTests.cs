using System.Buffers.Binary;
using Retro.App;
using Retro.Core;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailTests
{
    [Test]
    public void OversizedOrMalformedHeadersAreRejectedBeforeDecoding()
    {
        byte[] header = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0); "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 256); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), 192);
        Assert.That(ThumbnailFiles.Dimensions(header, out int w, out int h), Is.True);
        Assert.That((w, h), Is.EqualTo((256, 192)));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), int.MaxValue);
        Assert.That(ThumbnailFiles.Dimensions(header, out _, out _), Is.False);
        Assert.That(ThumbnailFiles.Dimensions([255, 216, 255, 192, 0, 20], out _, out _), Is.False);
        byte[] jpeg = [255, 216, 255, 192, 0, 11, 8, 0, 100, 1, 44, 1, 1, 17, 0];
        Assert.That(ThumbnailFiles.Dimensions(jpeg, out w, out h), Is.True);
        Assert.That((w, h), Is.EqualTo((300, 100)));
    }

    [Test]
    public void LocalImagesAndSavedValidationPicturesRemainPortableAndVersioned()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-thumbs-" + Guid.NewGuid())).FullName;
        string before = StudioLibrary.UserFolder;
        try
        {
            string media = Path.Combine(root, "Propio.prg"), sidecar = Path.Combine(root, "Propio.PNG");
            var picture = new VideoFrame(32, 24); Array.Fill(picture.Pixels, unchecked((int)0xFF3BA4D9));
            PngWriter.Write(sidecar, picture);
            Assert.That(ThumbnailFiles.Beside(media), Is.EqualTo(sidecar), "extensiones con mayúscula también funcionan en Linux");
            StudioLibrary.UserFolder = Path.Combine(root, "fusionados");
            var game = new StudioGame { Id = "propio", Title = "Propio", Script = "function draw() {}" };
            string meta = StudioLibrary.Save(game, picture);
            Assert.That(StudioLibrary.PicturePath(game), Is.EqualTo(Path.ChangeExtension(meta, ".png")));
            Assert.That(ThumbnailFiles.Dimensions(File.ReadAllBytes(StudioLibrary.PicturePath(game)!), out int w, out int h), Is.True);
            Assert.That((w, h), Is.EqualTo((32, 24)));
            StudioLibrary.Save(game, picture);
            Assert.That(game.Version, Is.EqualTo(2));
            Assert.That(File.Exists(Path.ChangeExtension(meta, ".png")), Is.True, "la versión anterior conserva su imagen");
            game.Id = ".."; Assert.That(StudioLibrary.PicturePath(game), Is.Null);
        }
        finally { StudioLibrary.UserFolder = before; Directory.Delete(root, true); }
    }
}
