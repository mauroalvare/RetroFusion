using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>Small original vector illustrations; portable and independent of image files or textures.</summary>
internal static class HardwareArt
{
    public static void Draw(ImDrawListPtr draw, string machine, Vector2 position, Vector2 size)
    {
        float scale = Math.Min(size.X / 160, size.Y / 100);
        Vector2 origin = position + (size - new Vector2(160, 100) * scale) / 2;
        Vector2 At(float x, float y) => origin + new Vector2(x, y) * scale;
        void Box(float x, float y, float w, float h, uint color, float round = 1) =>
            draw.AddRectFilled(At(x, y), At(x + w, y + h), color, round * scale);
        void Line(float x, float y, float xx, float yy, uint color, float thick = 1) =>
            draw.AddLine(At(x, y), At(xx, yy), color, Math.Max(1, thick * scale));
        const uint dark = 0xFF25262A, light = 0xFFBFC4CC, red = 0xFF4D4DDD;
        Box(12, 83, 136, 8, 0x35000000, 6);
        switch (machine)
        {
            case "zx48": case "zx128":
                Box(8, 25, 144, 61, dark, 6); Box(14, 30, 132, 8, 0xFF17181B, 2);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 10; col++)
                        Box(16 + col * 13, 45 + row * 9, 10, 6, 0xFF0C0E12, 2);
                Box(17, 32, 27, 2, light);
                uint[] rainbow = [red, 0xFF43DCEB, 0xFF73CF5B, 0xFFEBA942];
                for (int i = 0; i < rainbow.Length; i++) Line(119 + i * 6, 30, 133 + i * 6, 42, rainbow[i], 4);
                break;
            case "c64":
                Box(8, 29, 144, 57, 0xFF8DA4B6, 9); Box(13, 35, 134, 6, 0xFF45505B, 2);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 10; col++)
                        Box(17 + col * 12, 45 + row * 8, 9, 6, 0xFF353E49, 1);
                Box(41, 77, 67, 5, 0xFF353E49); Box(140, 46, 4, 4, red);
                Box(19, 37, 6, 2, 0xFFE6BA5B); Box(28, 37, 7, 2, light);
                break;
            case "nes":
                Box(13, 28, 133, 49, light, 3); Box(13, 61, 133, 16, 0xFF7B828B, 2);
                Box(26, 36, 106, 20, 0xFF5A6067, 2); Box(31, 39, 96, 12, dark);
                Box(24, 67, 19, 4, red); Box(47, 67, 8, 4, dark);
                Line(40, 77, 51, 90, dark, 2);
                Box(48, 80, 82, 18, light, 3); Box(52, 83, 74, 12, dark);
                Box(59, 83, 4, 12, 0xFF121317); Box(55, 87, 12, 4, 0xFF121317);
                Box(106, 87, 5, 5, red, 3); Box(116, 87, 5, 5, red, 3);
                Box(79, 90, 7, 2, light); Box(90, 90, 7, 2, light);
                break;
            default:
                Box(8, 24, 144, 62, 0xFF425C2B, 4);
                for (int i = 0; i < 6; i++)
                {
                    Line(13, 31 + i * 9, 39, 31 + i * 9, 0xFF638B44);
                    Line(118, 32 + i * 8, 146, 32 + i * 8, 0xFF638B44);
                    Box(49 + i * 11, 34, 5, 7, light); Box(49 + i * 11, 66, 5, 7, light);
                    Box(147, 35 + i * 7, 7, 4, 0xFF6DC3D8);
                }
                Box(40, 40, 78, 26, dark, 2); Box(17, 40, 13, 29, dark, 1);
                Box(129, 42, 10, 15, dark, 1); Box(125, 69, 12, 5, 0xFF6DC3D8);
                string label = machine == "invaders" ? "8080" : "CPU";
                string[] glyphs = label == "8080" ? ["111101111101111", "111101101101111", "111101111101111", "111101101101111"]
                    : ["111100100100111", "110101110100100", "101101101101111"];
                for (int c = 0; c < glyphs.Length; c++)
                    for (int pixel = 0; pixel < 15; pixel++)
                        if (glyphs[c][pixel] == '1') Box(55 + c * 12 + pixel % 3 * 2, 48 + pixel / 3 * 2, 1.5f, 1.5f, light, 0);
                break;
        }
    }
}
