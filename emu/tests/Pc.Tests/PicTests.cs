using Retro.Systems.Pc;

namespace Pc.Tests;

public class PicTests
{
    internal static Pic8259A Ready(bool autoEoi = false, byte baseVector = 8)
    {
        var pic = new Pic8259A();
        pic.WriteCommand(0x13); pic.WriteData(baseVector); pic.WriteData(autoEoi ? (byte)3 : (byte)1);
        return pic;
    }

    [Test]
    public void InitializationAndVectorAlignmentUse8086CommandWords()
    {
        var pic = new Pic8259A();
        Assert.That(pic.Initialized, Is.False); Assert.That(pic.InterruptRequested, Is.False);
        Assert.That(pic.ReadData(), Is.EqualTo(0xFF));
        pic.WriteCommand(0x13);
        Assert.That(pic.ReadData(), Is.Zero);
        pic.WriteData(0x2B); Assert.That(pic.Initialized, Is.False);
        pic.WriteData(1); pic.SetLine(3, true);
        Assert.That(pic.Acknowledge(), Is.EqualTo(0x2B));
        Assert.That(pic.InServiceRegister, Is.EqualTo(8));
    }

    [TestCase(0x11)] [TestCase(0x1B)] [TestCase(0x12)]
    public void UnsupportedInitializationDoesNotSilentlyConfigureAnotherMachine(int word)
    {
        var pic = Ready(); pic.SetLine(2, true);
        Assert.Throws<NotSupportedException>(() => pic.WriteCommand((byte)word));
        Assert.That(pic.Initialized, Is.True);
        Assert.That(pic.Acknowledge(), Is.EqualTo(10));
    }

    [TestCase(0)] [TestCase(9)] [TestCase(0x11)]
    public void UnsupportedIcw4DoesNotCompleteInitialization(int word)
    {
        var pic = new Pic8259A(); pic.WriteCommand(0x13); pic.WriteData(8);
        Assert.Throws<NotSupportedException>(() => pic.WriteData((byte)word));
        Assert.That(pic.Initialized, Is.False);
        pic.WriteData(1); Assert.That(pic.Initialized, Is.True);
    }

    [Test]
    public void UninitializedControllerCannotAcknowledgeOrInterpretDataAsAMask()
    {
        var pic = new Pic8259A();
        Assert.Throws<InvalidOperationException>(() => pic.Acknowledge());
        Assert.Throws<InvalidOperationException>(() => pic.WriteData(0));
        Assert.Throws<InvalidOperationException>(() => pic.WriteCommand(0x20));
    }

    [Test]
    public void MaskedRequestsRemainPendingUntilUnmasked()
    {
        var pic = Ready(); pic.WriteData(0xFF); pic.SetLine(5, true);
        Assert.That((pic.RequestRegister, pic.InterruptRequested), Is.EqualTo((0x20, false)));
        pic.WriteData(0xDF); Assert.That(pic.InterruptRequested, Is.True);
        Assert.That(pic.Acknowledge(), Is.EqualTo(13));
        Assert.That((pic.RequestRegister, pic.InServiceRegister), Is.EqualTo((0, 0x20)));
    }

    [Test]
    public void EveryRequestAndMaskCombinationSelectsTheLowestUnmaskedLine()
    {
        for (int request = 0; request < 256; request++)
        for (int mask = 0; mask < 256; mask++)
        {
            var pic = Ready(autoEoi: true);
            for (int line = 0; line < 8; line++) if ((request & (1 << line)) != 0) pic.SetLine(line, true);
            pic.WriteData((byte)mask);
            int expected = -1;
            for (int line = 0; line < 8; line++)
                if ((request & ~mask & (1 << line)) != 0) { expected = line; break; }
            bool requested = pic.InterruptRequested;
            int vector = pic.Acknowledge();
            int remaining = expected < 0 ? request : request & ~(1 << expected);
            if (requested != (expected >= 0) || vector != 8 + (expected < 0 ? 7 : expected) ||
                pic.RequestRegister != remaining || pic.InServiceRegister != 0)
                Assert.Fail($"request={request:X2}, mask={mask:X2}, expected={expected}, vector={vector:X2}");
        }
    }

    [Test]
    public void HeldHighInputDoesNotRetriggerUntilItFallsAndRisesAgain()
    {
        var pic = Ready(); pic.SetLine(1, true); pic.Acknowledge(); pic.WriteCommand(0x20);
        pic.SetLine(1, true); Assert.That(pic.InterruptRequested, Is.False);
        pic.SetLine(1, false); pic.SetLine(1, true);
        Assert.That(pic.Acknowledge(), Is.EqualTo(9));
    }

    [Test]
    public void ASecondEdgeWhileInServiceWaitsForEndOfInterrupt()
    {
        var pic = Ready(); pic.SetLine(2, true); pic.Acknowledge();
        pic.SetLine(2, false); pic.SetLine(2, true);
        Assert.That(pic.InterruptRequested, Is.False);
        Assert.That(pic.RequestRegister, Is.EqualTo(4));
        pic.WriteCommand(0x20);
        Assert.That(pic.Acknowledge(), Is.EqualTo(10));
    }

    [Test]
    public void HigherPriorityMayNestButEqualAndLowerRequestsWaitForEoi()
    {
        var pic = Ready(); pic.SetLine(4, true); pic.Acknowledge();
        pic.SetLine(6, true); Assert.That(pic.InterruptRequested, Is.False);
        pic.SetLine(1, true); Assert.That(pic.Acknowledge(), Is.EqualTo(9));
        Assert.That(pic.InServiceRegister, Is.EqualTo(0x12));
        pic.WriteCommand(0x20); Assert.That(pic.InServiceRegister, Is.EqualTo(0x10));
        Assert.That(pic.InterruptRequested, Is.False);
        pic.WriteCommand(0x20); Assert.That(pic.Acknowledge(), Is.EqualTo(14));
    }

    [Test]
    public void SpecificEoiClearsOnlyItsNamedInServiceBit()
    {
        var pic = Ready(); pic.SetLine(5, true); pic.Acknowledge(); pic.SetLine(2, true); pic.Acknowledge();
        pic.WriteCommand(0x65);
        Assert.That(pic.InServiceRegister, Is.EqualTo(4));
        pic.WriteCommand(0x62); Assert.That(pic.InServiceRegister, Is.Zero);
    }

    [Test]
    public void AutomaticEoiDoesNotLeaveServiceBitsBlockingTheNextRequest()
    {
        var pic = Ready(autoEoi: true); pic.SetLine(0, true); pic.SetLine(7, true);
        Assert.That(pic.Acknowledge(), Is.EqualTo(8));
        Assert.That(pic.InServiceRegister, Is.Zero);
        Assert.That(pic.Acknowledge(), Is.EqualTo(15));
    }

    [Test]
    public void StatusSelectionPersistsAndDoesNotConsumePendingRequests()
    {
        var pic = Ready(); pic.SetLine(4, true); pic.Acknowledge(); pic.SetLine(1, true);
        Assert.That(pic.ReadCommand(), Is.EqualTo(2));
        pic.WriteCommand(0x0B);
        Assert.That(pic.ReadCommand(), Is.EqualTo(16)); Assert.That(pic.ReadCommand(), Is.EqualTo(16));
        pic.WriteCommand(0x0A);
        Assert.That(pic.ReadCommand(), Is.EqualTo(2)); Assert.That(pic.RequestRegister, Is.EqualTo(2));
        pic.WriteData(0xA5); Assert.That(pic.ReadData(), Is.EqualTo(0xA5));
    }

    [Test]
    public void RotateOnNonSpecificEoiMovesTheServicedLineToLowestPriority()
    {
        var pic = Ready(); pic.SetLine(0, true); pic.Acknowledge();
        pic.WriteCommand(0xA0);
        pic.SetLine(0, false); pic.SetLine(0, true); pic.SetLine(1, true);
        Assert.That(pic.Acknowledge(), Is.EqualTo(9));
    }

    [Test]
    public void SetPriorityRotatesAllEightLevelsWithoutClearingService()
    {
        var pic = Ready(); pic.WriteCommand(0xC3);
        for(int line=0;line<8;line++) pic.SetLine(line,true);
        int[] expected = [4,5,6,7,0,1,2,3];
        foreach(int line in expected)
        {
            Assert.That(pic.Acknowledge(), Is.EqualTo(8+line));
            Assert.That(pic.InServiceRegister, Is.EqualTo(1<<line));
            pic.WriteCommand(0x20);
        }
    }

    [Test]
    public void RotateOnSpecificEoiSetsPriorityEvenForAnInactiveServiceLevel()
    {
        var pic = Ready(); pic.WriteCommand(0xE5); pic.SetLine(2,true); pic.SetLine(6,true);
        Assert.That(pic.Acknowledge(), Is.EqualTo(14));
    }

    [Test]
    public void AutomaticEoiRotationCanBeEnabledAndDisabled()
    {
        var pic = Ready(autoEoi:true); pic.WriteCommand(0x80);
        for(int line=0;line<8;line++) pic.SetLine(line,true);
        for(int expected=0;expected<8;expected++)
        {
            Assert.That(pic.Acknowledge(),Is.EqualTo(8+expected));
            pic.SetLine(expected,false);pic.SetLine(expected,true);
        }
        Assert.That(pic.Acknowledge(),Is.EqualTo(8));
        pic.SetLine(0,false);pic.SetLine(0,true);pic.WriteCommand(0);
        Assert.That(pic.Acknowledge(),Is.EqualTo(9));
        pic.SetLine(1,false);pic.SetLine(1,true);
        Assert.That(pic.Acknowledge(),Is.EqualTo(9));
    }

    [Test]
    public void NormalMaskDoesNotRemoveAnInServicePriorityBarrierButSpecialMaskDoes()
    {
        var pic = Ready(); pic.SetLine(1,true);pic.Acknowledge();pic.WriteData(2);pic.SetLine(5,true);
        Assert.That(pic.InterruptRequested,Is.False);
        pic.WriteCommand(0x68);
        Assert.That(pic.Acknowledge(),Is.EqualTo(13));
        pic.WriteCommand(0x20);
        Assert.That(pic.InServiceRegister,Is.EqualTo(2),"Non-specific EOI skips masked service bits in special mask mode.");
        pic.WriteCommand(0x48);pic.SetLine(5,false);pic.SetLine(5,true);
        Assert.That(pic.InterruptRequested,Is.False);
        pic.WriteCommand(0x61);Assert.That(pic.Acknowledge(),Is.EqualTo(13));
    }

    [TestCase(false)] [TestCase(true)]
    public void PollConsumesOneReadAndUpdatesServiceLikeAnAcknowledge(bool dataPort)
    {
        var pic = Ready();pic.SetLine(3,true);pic.WriteCommand(0x0C);
        byte answer=dataPort?pic.ReadData():pic.ReadCommand();
        Assert.That(answer,Is.EqualTo(0x83));Assert.That(pic.InServiceRegister,Is.EqualTo(8));
        Assert.That(pic.ReadData(),Is.Zero);
        Assert.That(pic.ReadCommand(),Is.Zero);
    }

    [Test]
    public void PollPriorityIsFrozenUntilItsReadAndLaterRequestsRemainPending()
    {
        var pic=Ready();pic.SetLine(5,true);pic.WriteCommand(0x0C);pic.SetLine(0,true);
        Assert.That(pic.ReadCommand(),Is.EqualTo(0x85));
        Assert.That(pic.Acknowledge(),Is.EqualTo(8));
        Assert.That(pic.InServiceRegister,Is.EqualTo(0x21));
    }

    [Test]
    public void EmptyPollDoesNotSetIrq7ButNormalIrq7AndSpuriousIrq7RemainDistinct()
    {
        var pic=Ready();pic.WriteCommand(0x0C);
        Assert.That(pic.ReadCommand(),Is.Zero);
        Assert.That(pic.Acknowledge(),Is.EqualTo(15));Assert.That(pic.InServiceRegister,Is.Zero);
        pic.SetLine(7,true);Assert.That(pic.Acknowledge(),Is.EqualTo(15));
        Assert.That(pic.InServiceRegister,Is.EqualTo(0x80));
        Assert.That(pic.Acknowledge(),Is.EqualTo(15));
        Assert.That(pic.InServiceRegister,Is.EqualTo(0x80),"A spurious acknowledge must not clear an existing ISR7.");
    }

    [Test]
    public void ReinitializationClearsModesAndPendingStateWithoutInventingAnotherEdge()
    {
        var pic=Ready(autoEoi:true);pic.SetLine(2,true);pic.WriteData(0xA5);pic.WriteCommand(0x68);pic.WriteCommand(0x0B);
        pic.WriteCommand(0x13);pic.WriteData(0x20);pic.WriteData(1);
        Assert.That((pic.Mask,pic.RequestRegister,pic.InServiceRegister),Is.EqualTo((0,0,0)));
        Assert.That(pic.ReadCommand(),Is.Zero);
        pic.SetLine(2,true);Assert.That(pic.InterruptRequested,Is.False);
        pic.SetLine(2,false);pic.SetLine(2,true);
        Assert.That(pic.Acknowledge(),Is.EqualTo(0x22));Assert.That(pic.InServiceRegister,Is.EqualTo(4));
    }

    [Test]
    public void BoardResetRequiresInitializationAgain()
    {
        var pic=Ready();pic.SetLine(0,true);pic.Acknowledge();pic.Reset();
        Assert.That((pic.Initialized,pic.InterruptRequested,pic.RequestRegister,pic.InServiceRegister,pic.Mask),
            Is.EqualTo((false,false,0,0,0xFF)));
    }

    [TestCase(-1)] [TestCase(8)]
    public void InvalidIrqNumberDoesNotChangeTheController(int line)
    {
        var pic=Ready();
        Assert.Throws<ArgumentOutOfRangeException>(()=>pic.SetLine(line,true));
        Assert.That(pic.RequestRegister,Is.Zero);
    }
    [Test]
    public void UnverifiedPollAndAutomaticEoiCombinationIsRejectedWithoutConsumingTheRequest()
    {
        var pic = Ready(autoEoi: true);
        pic.SetLine(0, true);
        Assert.Throws<NotSupportedException>(() => pic.WriteCommand(0x0C));
        Assert.That((pic.RequestRegister, pic.InServiceRegister), Is.EqualTo((1, 0)));
        Assert.That(pic.Acknowledge(), Is.EqualTo(8));
        Assert.That(pic.InServiceRegister, Is.Zero);
    }

}
