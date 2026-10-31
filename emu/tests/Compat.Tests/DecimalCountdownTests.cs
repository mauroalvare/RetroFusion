using Retro.Compat;
namespace Compatibility.Tests;

public sealed class DecimalCountdownTests
{
    private static byte[] Digits(int value) => [99, (byte)(value / 100), (byte)(value / 10 % 10), (byte)(value % 10), 99];
    [Test]
    public void AWholeCountdownSurvivesBorrowAndIsPreferredOverItsSuffix()
    {
        var samples = Enumerable.Range(0, 32).Select(i => Digits(114 - i / 2)).ToArray();
        var finding = DecimalCountdowns.Find(samples, 0x6000, 5).Single();
        Assert.That((finding.Address, finding.Encoding, finding.Bytes), Is.EqualTo(("6001", "digits", 3)));
        Assert.That(finding.Status, Is.EqualTo("hypothesis"));
        Assert.That(finding.Evidence.Single(), Does.Contain("114 a 99").And.Contain("10"));
    }
    [TestCase("reset")] [TestCase("invalid-digit")] [TestCase("jump")]
    public void MixedOrUnprovenSeriesDoesNotBecomeAWholeCountdown(string corruption)
    {
        var samples = Enumerable.Range(0, 32).Select(i => Digits(114 - i / 2)).ToArray();
        if (corruption == "invalid-digit") samples[10][2] = 12;
        else samples[10] = Digits(corruption == "reset" ? 150 : 90);
        Assert.That(DecimalCountdowns.Find(samples, 0x6000, 5).Any(f => f.Address == "6001" && f.Bytes == 3), Is.False);
    }
}
