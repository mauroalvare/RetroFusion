using Raylib_cs;
using Retro.App;
using StbImageSharp;

namespace App.Tests;

[NonParallelizable]
public sealed class FramebufferCaptureTests
{
    [TestCase(false)] [TestCase(true)]
    [Explicit("Requiere un contexto gráfico nativo; abre solamente una ventana oculta.")]
    public void CaptureKeepsTheFramebufferSizeAndAllFourCorners(bool highDpi)
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-framebuffer-ñ " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "captura tamaño.png");
        var flags = ConfigFlags.HiddenWindow;
        if (highDpi) flags |= ConfigFlags.HighDpiWindow;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(320, 240, "RetroEmu capture test");
        try
        {
            int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
            int pixelsWide = Raylib.GetRenderWidth(), pixelsHigh = Raylib.GetRenderHeight();
            TestContext.Out.WriteLine($"logical={width}x{height} render={pixelsWide}x{pixelsHigh} " +
                $"rlgl={Rlgl.GetFramebufferWidth()}x{Rlgl.GetFramebufferHeight()} dpi={Raylib.GetWindowScaleDPI()}");

            Raylib.BeginDrawing();
            try
            {
                Raylib.ClearBackground(Color.Black);
                Raylib.DrawRectangle(0, 0, width / 2, height / 2, Color.Red);
                Raylib.DrawRectangle(width / 2, 0, width - width / 2, height / 2, Color.Green);
                Raylib.DrawRectangle(0, height / 2, width / 2, height - height / 2, Color.Blue);
                Raylib.DrawRectangle(width / 2, height / 2, width - width / 2, height - height / 2, Color.Yellow);
                FramebufferCapture.Save(path);
            }
            finally { Raylib.EndDrawing(); }

            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            Assert.That((image.Width, image.Height), Is.EqualTo((pixelsWide, pixelsHigh)));
            AssertPixel(image, image.Width / 4, image.Height / 4, Color.Red);
            AssertPixel(image, image.Width * 3 / 4, image.Height / 4, Color.Green);
            AssertPixel(image, image.Width / 4, image.Height * 3 / 4, Color.Blue);
            AssertPixel(image, image.Width * 3 / 4, image.Height * 3 / 4, Color.Yellow);
        }
        finally
        {
            Raylib.CloseWindow();
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void AssertPixel(ImageResult image, int x, int y, Color expected)
    {
        int pixel = (y * image.Width + x) * 4;
        Assert.That(image.Data.AsSpan(pixel, 4).ToArray(),
            Is.EqualTo(new[] { expected.R, expected.G, expected.B, (byte)255 }), $"Píxel ({x}, {y})");
    }
}
