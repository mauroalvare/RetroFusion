using Retro.Core;
using Retro.Systems.C64;

namespace Commodore.Tests;

/// <summary>The whole machine with the free Open ROMs that come with the emulator.</summary>
[TestFixture]
public class MachineTests
{
    internal static C64Firmware Firmware()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "roms", "c64")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta roms/c64 (Open ROMs).");
        return C64Firmware.Find(Path.Combine(dir!.FullName, "roms"));
    }

    private static C64 Booted()
    {
        var m = new C64(Firmware());
        C64Media.BootToReady(m);
        return m;
    }

    private static void Frames(C64 m, int n)
    {
        var sink = new short[8192];
        for (int i = 0; i < n; i++)
        {
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
    }

    /// <summary>Puts code in RAM and points the CPU at it.</summary>
    private static void Run(C64 m, ushort at, params byte[] code)
    {
        code.CopyTo(m.Ram, at);
        m.Cpu6510.PC = at;
    }

    [Test]
    public void Boot_ReachesTheBasicPrompt()
    {
        var m = Booted();
        Assert.That(C64Media.ScreenContains(m, "READY."), Is.True, m.ScreenText());
        Assert.That(m.FrameCount, Is.LessThan(250));
    }

    [Test]
    public void Frame_LastsOnePalFieldOfCycles()
    {
        var m = Booted();
        long before = m.Cpu6510.Cycles;
        // with the screen on, 25 bad lines take ~40 cycles each from the CPU
        Frames(m, 1);
        long withScreen = m.Cpu6510.Cycles - before;
        m.Vic.Write(0x11, 0x0B);   // display off: no bad lines
        Frames(m, 1);
        before = m.Cpu6510.Cycles;
        Frames(m, 1);
        long screenOff = m.Cpu6510.Cycles - before;
        Assert.That(screenOff, Is.InRange(C64.CyclesPerFrame - 10, C64.CyclesPerFrame + 10));
        Assert.That(screenOff - withScreen, Is.InRange(900, 1100));
    }

    [Test]
    public void T64_WithAWrongEndAddress_TakesTheSizeFromTheFile()
    {
        var t64 = new byte[96 + 20];
        System.Text.Encoding.ASCII.GetBytes("C64S tape image file").CopyTo(t64, 0);
        t64[34] = 1; t64[36] = 1;                     // one entry, one used
        t64[64] = 1; t64[65] = 0x82;                  // tape file, PRG
        t64[66] = 0x01; t64[67] = 0x08;               // start $0801
        t64[68] = 0xC6; t64[69] = 0xC3;               // end $C3C6: the usual bad value
        t64[72] = 96;                                 // data offset
        System.Text.Encoding.ASCII.GetBytes("GAME            ").CopyTo(t64, 80);
        for (int i = 0; i < 20; i++)
            t64[96 + i] = (byte)i;
        var files = C64Media.ReadT64(t64);
        Assert.That(files, Has.Count.EqualTo(1));
        Assert.That(files[0].Name, Is.EqualTo("GAME"));
        Assert.That(files[0].Start, Is.EqualTo(0x0801));
        Assert.That(files[0].Data, Has.Length.EqualTo(20));
        Assert.That(C64Media.Probe(new MediaFile("x.t64", t64, "x.t64")), Is.GreaterThan(90));
    }

    [TestCase("2061", 2061)]
    [TestCase(" 2064", 2064)]
    [TestCase("(2049)", 2049)]
    public void SysAddress_IsReadFromTheFirstBasicLine(string text, int expected)
    {
        var ram = new byte[0x10000];
        byte[] line = { 0x0B, 0x08, 0x0A, 0x00, 0x9E };
        line.CopyTo(ram, 0x0801);
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(ram, 0x0806);
        Assert.That(C64Media.SysAddress(ram), Is.EqualTo(expected));
    }

    /// <summary>10 SYS 2061, then TSX / STX $C000 / LDA #1 / STA $0400 / JMP *.</summary>
    private static readonly byte[] SysProgram =
    {
        0x01, 0x08,                                                   // load address
        0x0B, 0x08, 0x0A, 0x00, 0x9E, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00,
        0xBA, 0x8E, 0x00, 0xC0, 0xA9, 0x01, 0x8D, 0x00, 0x04, 0x4C, 0x16, 0x08,
    };

    [Test]
    public void Prg_StartsLikeBasicSys()
    {
        var m = new C64(Firmware());
        var result = C64Media.Load(m, new MediaFile("test.prg", SysProgram, "test.prg"));
        Frames(m, 2);
        Assert.That(result.Description, Does.Contain("SYS 2061"));
        Assert.That(m.Ram[0x0400], Is.EqualTo(1), "the program ran");
        Assert.That(m.Ram[0xC000], Is.EqualTo(0xF6), "stack pointer of a SYS call");
        Assert.That(m.Cpu6510.PC, Is.InRange(0x0816, 0x0818));
    }

    [Test]
    public void OpenRoms_InternalClearScreen_IsDoneAndReturns()
    {
        var m = Booted();
        for (int i = 0; i < 1000; i++)
            m.Ram[0x0400 + i] = 0x18;
        // JSR $E544 / LDA #$2A / STA $C100 / JMP $C008
        Run(m, 0xC000, 0x20, 0x44, 0xE5, 0xA9, 0x2A, 0x8D, 0x00, 0xC1, 0x4C, 0x08, 0xC0);
        Frames(m, 1);
        Assert.That(m.Ram[0x0400], Is.EqualTo(0x20));
        Assert.That(m.Ram[0x07E7], Is.EqualTo(0x20));
        Assert.That(m.Ram[0xC100], Is.EqualTo(0x2A), "returned to the caller");
        Assert.That(m.KernalInternalCalls, Does.Contain((ushort)0xE544));
        Assert.That(m.UnhandledKernalCalls, Is.Empty);
    }

    [Test]
    public void ResetRoutine_WithTheCbm80Signature_JumpsThroughTheCartridgeVector()
    {
        var m = Booted();
        byte[] signature = { 0x00, 0xC0, 0x00, 0xC0, 0xC3, 0xC2, 0xCD, 0x38, 0x30 };
        signature.CopyTo(m.Ram, 0x8000);
        m.Ram[0xC000] = 0xA9; m.Ram[0xC001] = 0x55;                       // LDA #$55
        m.Ram[0xC002] = 0x8D; m.Ram[0xC003] = 0x00; m.Ram[0xC004] = 0xC1; // STA $C100
        m.Ram[0xC005] = 0x4C; m.Ram[0xC006] = 0x05; m.Ram[0xC007] = 0xC0; // JMP $C005
        Run(m, 0xC200, 0x4C, 0xE2, 0xFC);                                 // JMP $FCE2
        Frames(m, 1);
        Assert.That(m.Ram[0xC100], Is.EqualTo(0x55));
        Assert.That(m.Cpu6510.S, Is.GreaterThan(0xF0), "the stack was reset");
    }

    [Test]
    public void InterruptHandlerInRam_ReturningIntoTheKernal_IsNotACall()
    {
        var m = Booted();
        // INC $C100 / LDA $DC0D / PLA / TAY / PLA / TAX / PLA / RTI
        byte[] handler = { 0xEE, 0x00, 0xC1, 0xAD, 0x0D, 0xDC, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40 };
        handler.CopyTo(m.Ram, 0xC000);
        m.KernalInternalCalls.Clear();
        m.Ram[0x0314] = 0x00; m.Ram[0x0315] = 0xC0;
        Frames(m, 10);
        Assert.That(m.Ram[0xC100], Is.GreaterThan(5), "the handler ran");
        Assert.That(m.KernalInternalCalls, Is.Empty);
    }

    [Test]
    public void Keyboard_ReachesBasicThroughTheMatrix()
    {
        var m = Booted();
        m.QueueKeys(4, "H");
        m.QueueKeys(4, "I");
        Frames(m, 20);
        Assert.That(C64Media.ScreenContains(m, "HI"), Is.True, m.ScreenText());
    }

    [Test]
    public void SaveState_RestoresExactlyTheSameRun()
    {
        var m = new C64(Firmware());
        C64Media.Load(m, new MediaFile("test.prg", SysProgram, "test.prg"));
        Frames(m, 3);
        var state = m.SaveState();
        Frames(m, 10);
        var pixels = (int[])m.Video.Pixels.Clone();
        long cycles = m.Cpu6510.Cycles;
        m.LoadState(state);
        Frames(m, 10);
        Assert.That(m.Cpu6510.Cycles, Is.EqualTo(cycles));
        Assert.That(m.Video.Pixels, Is.EqualTo(pixels));
    }

    [Test]
    public void Load_ExplainsTheFormatsOfALaterStage()
    {
        var m = new C64(Firmware());
        var ex = Assert.Throws<NotSupportedException>(() => C64Media.Load(m, new MediaFile("juego.d64", new byte[174848], "juego.d64")));
        Assert.That(ex!.Message, Does.Contain(".d64"));
    }
}
