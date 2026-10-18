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
    }

    [Test]
    public void Conditions_AreEdgeTriggered()
    {
        Assert.That(RuleCondition.Parse("a.aliens decreases").Fires(55, 54), Is.True);
        Assert.That(RuleCondition.Parse("a.aliens decreases").Fires(54, 54), Is.False);
        var eq = RuleCondition.Parse("a.ships equals 0");
        Assert.That(eq.Fires(1, 0), Is.True);
        Assert.That(eq.Fires(0, 0), Is.False, "solo al llegar, no mientras sigue igual");
        Assert.That(RuleCondition.Parse("b.air below 40").Fires(41, 39), Is.True);
        Assert.That(RuleCondition.Parse("b.air above 60").Fires(60, 61), Is.True);
        Assert.Throws<FormatException>(() => RuleCondition.Parse("a.aliens explodes"));
    }

    [Test]
    public void Actions_Parse()
    {
        Assert.That(RuleAction.Parse("b.air += 3"), Is.EqualTo(new RuleAction(ActionKind.Add, "b", "air", 3, null, null, null)));
        Assert.That(RuleAction.Parse("a.ships -= 1").Amount, Is.EqualTo(-1));
        var copy = RuleAction.Parse("a.score = b.score");
        Assert.That((copy.Kind, copy.SourceSlot, copy.SourceVariable), Is.EqualTo((ActionKind.Set, "b", "score")));
        Assert.That(RuleAction.Parse("press b.jump 10").Kind, Is.EqualTo(ActionKind.Press));
        Assert.That(RuleAction.Parse("pause a 120").Amount, Is.EqualTo(120));
        Assert.That(RuleAction.Parse("message ¡Hola!").Text, Is.EqualTo("¡Hola!"));
        Assert.Throws<FormatException>(() => RuleAction.Parse("a.ships *= 2"));
    }

    [Test]
    public void BuiltInRecipe_IsValid()
    {
        var recipe = RecipeLibrary.BuiltIn.Single(r => r.Id == "invaders-miner");
        var errors = recipe.Validate(id => ProfileLibrary.BuiltIn.FirstOrDefault(p => p.Id == id));
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(ProfileLibrary.BuiltIn.Select(p => p.Id), Is.SupersetOf(new[] { "invaders", "manicminer" }));
    }

    [Test]
    public void Validator_ExplainsMistakes()
    {
        var bad = FusionRecipe.Parse("""
            Acá va la receta:
            ```json
            { "name": "X", "games": [ {"slot":"a","profile":"invaders"}, {"slot":"b","profile":"pacman"} ],
              "layout": {"mode":"split"},
              "controls": {"left":["a.left"], "punch":["a.punch"]},
              "rules": [ {"when":"a.lives decreases","do":["a.x = 3","press a.kick 5"]} ] }
            ```
            """);
        var errors = bad.Validate(id => ProfileLibrary.BuiltIn.FirstOrDefault(p => p.Id == id));
        string all = string.Join("\n", errors);
        Assert.That(all, Does.Contain("\"pacman\" no existe"));
        Assert.That(all, Does.Contain("layout.mode"));
        Assert.That(all, Does.Contain("\"punch\" desconocido"));
        Assert.That(all, Does.Contain("Faltan los controles"));
        Assert.That(all, Does.Contain("no tiene la variable \"lives\""));
        Assert.That(all, Does.Contain("solo lectura"));
        Assert.That(all, Does.Contain("\"kick\""));
    }
}
