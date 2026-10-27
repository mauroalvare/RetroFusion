using Raylib_cs;
using Retro.App;
using Retro.Core;
using StbImageSharp;

namespace App.Tests;

public sealed class ScreenCaptureTests
{
    private string _folder = "";

    [SetUp]
    public void CreateFolder() { _folder = Path.Combine(Path.GetTempPath(), "retro-capture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    private static VideoFrame Frame(int width, int height, int seed)
    {
        var frame = new VideoFrame(width, height);
        var random = new Random(seed);
        for (int i = 0; i < frame.Pixels.Length; i++) frame.Pixels[i] = unchecked((int)0xFF000000) | random.Next(0x1000000);
        return frame;
    }

    [TestCase(1, 1)] [TestCase(256, 192)] [TestCase(320, 200)] [TestCase(7, 3)]
    public void CapturesDecodeToTheSameColours(int width, int height)
    {
        var frame = Frame(width, height, width * 31 + height);
        using var stream = new MemoryStream();
        ScreenCapture.WritePng(stream, width, height, frame.Pixels);
        var image = ImageResult.FromMemory(stream.ToArray(), ColorComponents.RedGreenBlue);
        Assert.That((image.Width, image.Height), Is.EqualTo((width, height)));
        for (int i = 0; i < frame.Pixels.Length; i++)
        {
            int pixel = frame.Pixels[i];
            Assert.That((image.Data[i * 3], image.Data[i * 3 + 1], image.Data[i * 3 + 2]),
                Is.EqualTo(((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel)), $"Píxel {i}");
        }
    }

    [Test]
    public void CapturesGetUniqueSafeNamesAndNeverReplaceFiles()
    {
        var frame = Frame(4, 2, 1);
        var now = new DateTime(2026, 10, 27, 21, 5, 9);
        string first = ScreenCapture.Save(frame, _folder, "Juego: prueba/1?", now);
        string second = ScreenCapture.Save(frame, _folder, "Juego: prueba/1?", now);
        Assert.That(Path.GetFileName(first), Is.EqualTo("Juego_ prueba_1_-20261027-210509.png"));
        Assert.That(Path.GetFileName(second), Is.EqualTo("Juego_ prueba_1_-20261027-210509-2.png"));
        Assert.That(ScreenCapture.SafeName("  ...  "), Is.EqualTo("captura"));
        Assert.That(ScreenCapture.SafeName(new string('a', 90)), Has.Length.EqualTo(60));
        Assert.That(Directory.GetFiles(_folder), Has.Length.EqualTo(2));
    }

    [Test]
    public void ThumbnailsGoBesideTheGameOnlyWhenItHasNoImage()
    {
        string game = Path.Combine(_folder, "juego.tzx");
        File.WriteAllBytes(game, [1, 2, 3]);
        string? saved = ScreenCapture.SaveThumbnail(Frame(8, 8, 2), game);
        Assert.That(saved, Is.EqualTo(Path.Combine(_folder, "juego.png")));
        byte[] written = File.ReadAllBytes(saved!);
        Assert.That(ScreenCapture.SaveThumbnail(Frame(8, 8, 3), game), Is.Null);
        Assert.That(File.ReadAllBytes(saved!), Is.EqualTo(written), "No se reemplaza la miniatura existente.");

        string other = Path.Combine(_folder, "otro.zip");
        File.WriteAllBytes(other, [4]);
        File.WriteAllBytes(Path.Combine(_folder, "OTRO.jpg"), [255, 216, 255]);
        Assert.That(ScreenCapture.SaveThumbnail(Frame(8, 8, 4), other), Is.Null, "Una carátula JPG ya cuenta como imagen.");
        Assert.That(File.Exists(Path.Combine(_folder, "otro.png")), Is.False);
    }

    [Test]
    public void TheGamepadStaysOutOfTheGameUntilEveryButtonIsReleased()
    {
        var input = new InputMapper();
        var down = new HashSet<GamepadButton>();
        Assert.That(input.GamepadReady(true, down.Contains), Is.True);
        down.Add(GamepadButton.MiddleLeft); down.Add(GamepadButton.MiddleRight);
        input.HoldGamepadUntilReleased();
        Assert.That(input.GamepadReady(true, down.Contains), Is.False);
        down.Remove(GamepadButton.MiddleRight);
        Assert.That(input.GamepadReady(true, down.Contains), Is.False, "Select sigue apretado.");
        down.Clear();
        Assert.That(input.GamepadReady(true, down.Contains), Is.True);
        down.Add(GamepadButton.MiddleRight);
        Assert.That(input.GamepadReady(true, down.Contains), Is.True, "Una pulsación nueva llega al juego.");
        Assert.That(input.GamepadReady(false, down.Contains), Is.False);
    }

    [Test]
    public void TheCaptureShortcutDoesNotReachTheMachine() =>
        Assert.That(InputMapper.ToolChordHeld(new[] { KeyboardKey.LeftControl, KeyboardKey.LeftShift, KeyboardKey.S }.Contains), Is.True);

    [Test]
    public void TheLastPickerFolderIsRememberedAndOversizedPathsAreIgnored()
    {
        string path = Path.Combine(_folder, "preferencias.json");
        var prefs = new AppPreferences(path) { LastFolder = _folder };
        Assert.That(prefs.Save(), Is.True);
        Assert.That(new AppPreferences(path).LastFolder, Is.EqualTo(_folder));
        prefs.LastFolder = new string('x', 2000);
        Assert.That(prefs.Save(), Is.True);
        var reloaded = new AppPreferences(path);
        Assert.That((reloaded.Problem, reloaded.LastFolder), Is.EqualTo(((string?)null, (string?)null)));
    }
}
