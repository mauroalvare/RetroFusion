using Retro.Systems.C64;

namespace Commodore.Tests;

/// <summary>The CIA and VIC-II on their own, cycle by cycle.</summary>
[TestFixture]
public class ChipTests
{
    [Test]
    public void Cia_TimerA_UnderflowsAfterLatchPlusOneCycles()
    {
        var cia = new Cia();
        cia.Write(0x4, 0x05);
        cia.Write(0x5, 0x00);   // stopped: the latch is loaded into the counter
        cia.Write(0xD, 0x81);   // timer A raises the interrupt line
        cia.Write(0xE, 0x01);   // start, continuous
        for (int i = 0; i < 5; i++)
            cia.Tick();
        Assert.That(cia.Interrupt, Is.False, "the counter goes 5..0 before the underflow");
        cia.Tick();
        Assert.That(cia.Interrupt, Is.True);
        Assert.That(cia.Read(0xD), Is.EqualTo(0x81), "flag of timer A plus the IR bit");
        Assert.That(cia.Interrupt, Is.False, "reading the ICR acknowledges it");
        Assert.That(cia.Read(0x4), Is.EqualTo(0x05), "reloaded from the latch");
    }

    [Test]
    public void Cia_OneShot_StopsAfterTheUnderflow()
    {
        var cia = new Cia();
        cia.Write(0x4, 0x02);
        cia.Write(0x5, 0x00);
        cia.Write(0xE, 0x09);   // start, one shot
        for (int i = 0; i < 3; i++)
            cia.Tick();
        Assert.That(cia.Read(0xE) & 1, Is.Zero);
        Assert.That(cia.Read(0xD) & 1, Is.EqualTo(1));
    }

    [Test]
    public void Cia_TimerB_CanCountTimerAUnderflows()
    {
        var cia = new Cia();
        cia.Write(0x4, 0x01); cia.Write(0x5, 0x00);   // A underflows every 2 cycles
        cia.Write(0x6, 0x02); cia.Write(0x7, 0x00);   // B: 2, 1, 0, underflow = 3 underflows of A
        cia.Write(0xF, 0x41);                         // B counts A's underflows
        cia.Write(0xE, 0x01);
        for (int i = 0; i < 5; i++)
            cia.Tick();
        Assert.That(cia.Peek(0xD) & 2, Is.Zero);
        cia.Tick();
        Assert.That(cia.Peek(0xD) & 2, Is.EqualTo(2));
    }

    [Test]
    public void Cia_TimeOfDay_LatchesWhileTheHoursAreRead()
    {
        var cia = new Cia();
        cia.Write(0xB, 0x01);   // writing the hours stops the clock...
        cia.Write(0xA, 0x00);
        cia.Write(0x9, 0x00);
        cia.Write(0x8, 0x00);   // ...writing the tenths starts it again
        for (int i = 0; i < 10; i++)
            cia.TodTick();
        Assert.That(cia.Read(0xB), Is.EqualTo(0x01));    // latch
        for (int i = 0; i < 10; i++)
            cia.TodTick();
        Assert.That(cia.Read(0x9), Is.EqualTo(0x01), "latched seconds");
        cia.Read(0x8);                                    // reading the tenths releases the latch
        Assert.That(cia.Read(0x9), Is.EqualTo(0x02));
    }

    [Test]
    public void Vic_RasterInterrupt_FiresOnTheCompareLine()
    {
        var vic = new Vic();
        vic.Reset();
        vic.Write(0x11, 0x1B);   // compare = line 0 while on line 0: that already latches the flag, like the chip
        vic.Write(0x12, 100);
        vic.Write(0x19, 0x0F);   // acknowledge what is pending, as programs do
        vic.Write(0x1A, 0x01);
        int guard = 0;
        while (!vic.Irq && guard++ < Vic.CyclesPerLine * Vic.Lines * 2)
            vic.Tick();
        Assert.That(vic.Irq, Is.True);
        Assert.That(vic.Raster, Is.EqualTo(100));
        Assert.That(vic.Read(0x19) & 0x81, Is.EqualTo(0x81));
        vic.Write(0x19, 0x01);   // acknowledge
        Assert.That(vic.Irq, Is.False);
    }
}
