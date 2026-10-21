using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

[TestFixture]
public class RecipeTests
{
    private sealed class Ram : IMemoryView
    {
        public readonly byte[] Data = new byte[0x10000];
        public int AddressBits => 16;
        public byte Peek(uint a) => Data[a & 0xFFFF];
        public void Poke(uint a, byte v) => Data[a & 0xFFFF] = v;
    }

    [Test]
    public void Variables_ReadAndWriteEachEncoding()
    {
        var ram = new Ram();
        var bcd = new GameVariable { Address = "20F8", Encoding = "bcd", Bytes = 2, Max = 9999 };
        bcd.Write(ram, 1234);
        Assert.That(ram.Data[0x20F8], Is.EqualTo(0x34));
        Assert.That(ram.Data[0x20F9], Is.EqualTo(0x12));
        Assert.That(bcd.Read(ram), Is.EqualTo(1234));

        var ascii = new GameVariable { Address = "8429", Encoding = "ascii", Bytes = 6, Max = 999999 };
        ascii.Write(ram, 4250);
        Assert.That(System.Text.Encoding.ASCII.GetString(ram.Data, 0x8429, 6), Is.EqualTo("004250"));
        Assert.That(ascii.Read(ram), Is.EqualTo(4250));

        var air = new GameVariable { Address = "80BC", Min = 36, Max = 63 };
        air.Write(ram, 99);
        Assert.That(air.Read(ram), Is.EqualTo(63), "se recorta al máximo");
        air.Write(ram, 10);
        Assert.That(air.Read(ram), Is.EqualTo(36), "se recorta al mínimo");

        var col = new GameVariable { Address = "806C", Mask = 31, ReadOnly = true };
        ram.Data[0x806C] = 0xA4;
        Assert.That(col.Read(ram), Is.EqualTo(4));
        col.Write(ram, 9);
        Assert.That(ram.Data[0x806C], Is.EqualTo(0xA4), "solo lectura");
        Assert.That(System.Text.Json.JsonSerializer.Serialize(col,GameProfile.Json), Does.Not.Contain("highBit"), "no cambia las huellas de perfiles que no usan el nuevo formato");

        var split = new GameVariable { Address="D008",Encoding="uint9",HighBitAddress="D010",HighBitMask=16,Max=511 };
        ram.Data[0xD010]=0xA5;
        split.Write(ram,300);
        Assert.That(ram.Data[0xD008], Is.EqualTo(44));
        Assert.That(ram.Data[0xD010], Is.EqualTo(0xB5), "conserva los bits de los otros sprites");
        Assert.That(split.Read(ram), Is.EqualTo(300));
        split.Write(ram,47);
        Assert.That(ram.Data[0xD010], Is.EqualTo(0xA5));
        Assert.That(split.Read(ram), Is.EqualTo(47));
        split.ReadOnly=true; split.Write(ram,300);
        Assert.That(split.Read(ram), Is.EqualTo(47));
        split.HighBitMask=3;
        Assert.Throws<InvalidDataException>(()=>split.Read(ram));
    }

}
