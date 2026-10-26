using Retro.App;

namespace App.Tests;

public sealed class AudioTests
{
    [TestCase(1)] [TestCase(7)] [TestCase(128)]
    public void BlockQueueMatchesFifoAndUnderrunFadeAcrossWrapAndOverflow(int capacity)
    {
        var random = new Random(108); var queue = new AudioQueue(capacity); var reference = new Queue<short>(); short last = 0;
        for (int n = 0; n < 1500; n++)
        {
            int operation = random.Next(10);
            if (operation < 5)
            {
                var values = Enumerable.Range(0, random.Next(capacity * 2 + 1)).Select(_ => (short)random.Next(short.MinValue, short.MaxValue + 1)).ToArray();
                queue.Push(values);
                foreach (short value in values) { if (reference.Count == capacity) reference.Dequeue(); reference.Enqueue(value); }
            }
            else if (operation < 8)
            {
                var actual = new short[random.Next(capacity + 3)]; queue.Pop(actual);
                foreach (short value in actual) { last = reference.Count > 0 ? reference.Dequeue() : (short)(last * 0.98); Assert.That(value, Is.EqualTo(last)); }
            }
            else if (operation == 8)
            {
                int keep = random.Next(capacity + 1); queue.Discard(keep);
                while (reference.Count > keep) reference.Dequeue();
            }
            else { queue.Clear(); reference.Clear(); last = 0; }
            Assert.That(queue.Count, Is.EqualTo(reference.Count));
        }
    }
    [Test]
    public async Task ProducerAndConsumerKeepOrderWithoutLosingSamples()
    {
        var queue = new AudioQueue(20000);
        var producer = Task.Run(() => { for (int i = 0; i < 20000; i += 100) queue.Push(Enumerable.Range(i, 100).Select(v => (short)v).ToArray()); });
        var samples = new short[64]; int received = 0; var end = DateTime.UtcNow.AddSeconds(5);
        while (received < 20000 && DateTime.UtcNow < end)
        {
            int take = Math.Min(queue.Count, samples.Length);
            if (take == 0) { await Task.Yield(); continue; }
            queue.Pop(samples.AsSpan(0, take));
            for (int i = 0; i < take; i++) Assert.That(samples[i], Is.EqualTo(received++));
        }
        await producer; Assert.That(received, Is.EqualTo(20000));
    }
    [Test]
    public void InvalidQueueArgumentsDoNotCorruptExistingData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioQueue(0));
        var queue = new AudioQueue(4); queue.Push(new short[] { 1, 2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Discard(-1));
        var result = new short[2]; queue.Pop(result); Assert.That(result, Is.EqualTo(new short[] { 1, 2 }));
    }
    [Test]
    public void GainAndMuteRampSmoothlyAndSettleAtTheRequestedLevel()
    {
        var output = new AudioOutput(); var samples = Enumerable.Repeat((short)10000, 512).ToArray();
        output.Process(samples, 1, false);
        Assert.That(samples[0], Is.InRange((short)1, (short)99)); Assert.That(samples[^1], Is.EqualTo(10000));
        Assert.That(samples.Zip(samples.Skip(1)).All(pair => pair.First <= pair.Second), Is.True);
        Array.Fill(samples, (short)10000); output.Process(samples, 0.25f, false);
        Assert.That(samples[^1], Is.EqualTo(2500));
        Assert.That(samples.Zip(samples.Skip(1)).All(pair => pair.First >= pair.Second), Is.True);
        Array.Fill(samples, (short)10000); output.Process(samples, 1, true);
        Assert.That(samples[^1], Is.Zero); Assert.That(samples.All(v => v >= 0 && v <= 2500), Is.True);
        output.Reset(); Array.Fill(samples, short.MinValue); output.Process(samples, float.NaN, false);
        Assert.That(samples.All(v => v == 0), Is.True);
    }
    [Test]
    public void SteadyGainPreservesSignedPcmWithoutAllocatingPerCallback()
    {
        var output = new AudioOutput(); output.Process(new short[256], 0.5f, false);
        short[] source = [short.MinValue, -101, 0, 101, short.MaxValue]; var actual = source.ToArray();
        output.Process(actual, 0.5f, false);
        Assert.That(actual, Is.EqualTo(source.Select(v => (short)(v * 0.5f)).ToArray()));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) output.Process(actual, 0.5f, false);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    [Test]
    public void PreferencesPersistOnlyInTheirOwnFileAndUnreadableFilesArePreserved()
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-sound-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "sound.json");
        try
        {
            var settings = new SoundSettings(path) { VolumePercent = 37, Muted = true, MuteInBackground = true, BufferFrames = 4 };
            Assert.That(settings.Save(), Is.True);
            var loaded = new SoundSettings(path);
            Assert.That((loaded.VolumePercent, loaded.Muted, loaded.MuteInBackground, loaded.BufferFrames), Is.EqualTo((37, true, true, 4)));
            loaded.VolumePercent = 101; Assert.That(loaded.Save(), Is.False);
            Assert.That(new SoundSettings(path).VolumePercent, Is.EqualTo(37));
            File.WriteAllText(path, "broken");
            var unreadable = new SoundSettings(path); Assert.That(unreadable.Problem, Is.Not.Null);
            Assert.That(unreadable.Save(), Is.False); Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
            var temporary = new SoundSettings(path, persist: false); temporary.Defaults(); Assert.That(temporary.Save(), Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1));
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
}
