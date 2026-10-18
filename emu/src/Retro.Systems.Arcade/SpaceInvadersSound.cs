using Retro.Core;

namespace Retro.Systems.Arcade;

/// <summary>
/// Sound board of Space Invaders. The original is a set of analog circuits, each one
/// triggered by a bit of port 3 or port 5:
///   port 3: bit 0 UFO (repeats while set), 1 shot, 2 base hit, 3 invader hit,
///           4 extended play, 5 amplifier enable
///   port 5: bits 0-3 fleet movement notes 1-4, bit 4 UFO hit
/// When to sound comes exactly from the game code; the sounds themselves are
/// synthesized approximations of the circuits, or the recordings of a real board
/// (MAME sample set 0.wav..9.wav) when they are available.
/// </summary>
public sealed class SpaceInvadersSound
{
    private readonly int _rate;
    private readonly List<Voice> _voices = new();
    private readonly List<(double pos, int sound)> _triggers = new();
    private readonly Random _rng = new(1978);
    private short[]?[] _samples = new short[]?[10];
    private double _sampleCarry;
    private double _ufoPhase, _ufoLfo;
    private int _ufoSamplePos;
    private double _noiseLp;

    public byte Port3 { get; private set; }
    public byte Port5 { get; private set; }
    public bool UsingSamples => _samples[1] != null;
    public double Volume { get; set; } = 0.6;

    public SpaceInvadersSound(int sampleRate) => _rate = sampleRate;

    public void Reset()
    {
        Port3 = Port5 = 0;
        _voices.Clear();
        _triggers.Clear();
    }

    public void WritePort3(byte value, double posInFrame)
    {
        int rising = value & ~Port3;
        Port3 = value;
        if ((rising & 0x02) != 0) _triggers.Add((posInFrame, 1));
        if ((rising & 0x04) != 0) _triggers.Add((posInFrame, 2));
        if ((rising & 0x08) != 0) _triggers.Add((posInFrame, 3));
        if ((rising & 0x10) != 0) _triggers.Add((posInFrame, 9));
    }

    public void WritePort5(byte value, double posInFrame)
    {
        int rising = value & ~Port5;
        Port5 = value;
        for (int i = 0; i < 4; i++)
            if ((rising & (1 << i)) != 0)
                _triggers.Add((posInFrame, 4 + i));
        if ((rising & 0x10) != 0) _triggers.Add((posInFrame, 8));
    }

    /// <summary>Loads MAME's invaders sample set (0.wav..9.wav) from a folder or a zip.</summary>
    public int LoadSamples(string folderOrZip)
    {
        if (File.Exists(folderOrZip))
            return LoadSamples(MediaArchive.Open(folderOrZip));
        var files = new List<MediaFile>();
        for (int i = 0; i < 10; i++)
        {
            string path = Path.Combine(folderOrZip, $"{i}.wav");
            if (File.Exists(path))
                files.Add(new MediaFile($"{i}.wav", File.ReadAllBytes(path), path));
        }
        return LoadSamples(files);
    }

    /// <summary>Loads the recordings named 0.wav .. 9.wav (MAME's numbering for this game).</summary>
    public int LoadSamples(IEnumerable<MediaFile> files)
    {
        int loaded = 0;
        foreach (var f in files)
        {
            if (f.Extension == ".wav" && int.TryParse(Path.GetFileNameWithoutExtension(f.Name), out int i) && i is >= 0 and < 10)
            {
                _samples[i] = WavReader.ReadMono(f.Data, _rate);
                loaded++;
            }
        }
        return loaded;
    }

    /// <summary>True for MAME's samples/invaders.zip (recordings, not the game program).</summary>
    public static bool IsSampleSet(IReadOnlyList<MediaFile> files) =>
        Enumerable.Range(1, 9).All(i => files.Any(f => f.Name.Equals($"{i}.wav", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Looks for the sample set next to the game: samples/invaders(.zip) beside the ROM,
    /// MAME's layout (roms/ and samples/ side by side), or the emulator's roms folder.
    /// </summary>
    public static string? FindSamples(string? mediaFolder, string? romFolder)
    {
        var places = new List<string>();
        foreach (var baseDir in new[] { mediaFolder, romFolder })
        {
            if (baseDir == null)
                continue;
            places.Add(Path.Combine(baseDir, "samples", "invaders"));
            places.Add(Path.Combine(baseDir, "samples", "invaders.zip"));
            places.Add(Path.Combine(baseDir, "..", "samples", "invaders"));
            places.Add(Path.Combine(baseDir, "..", "samples", "invaders.zip"));
        }
        foreach (var p in places)
        {
            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "1.wav")))
                return p;
            if (File.Exists(p))
            {
                try
                {
                    if (IsSampleSet(MediaArchive.Open(p)))
                        return p;
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        return null;
    }

    /// <summary>Produces the samples for one video frame into <paramref name="output"/>.</summary>
    public void EndFrame(AudioBuffer output, double fps)
    {
        _sampleCarry += _rate / fps;
        int count = (int)_sampleCarry;
        _sampleCarry -= count;
        _triggers.Sort((a, b) => a.pos.CompareTo(b.pos));
        int t = 0;
        bool amp = (Port3 & 0x20) != 0;
        for (int i = 0; i < count; i++)
        {
            while (t < _triggers.Count && _triggers[t].pos * count <= i)
                Start(_triggers[t++].sound);
            double mix = 0;
            if ((Port3 & 0x01) != 0)
                mix += UfoSample();
            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                mix += voice.Next(this);
                if (voice.Done)
                    _voices.RemoveAt(v);
            }
            if (!amp)
                mix = 0;
            if (output.Count < output.Samples.Length)
                output.Samples[output.Count++] = (short)Math.Clamp(mix * Volume * 32767, -32767, 32767);
        }
        while (t < _triggers.Count)
            Start(_triggers[t++].sound);
        _triggers.Clear();
    }

    private void Start(int sound)
    {
        if (_samples[sound] is { } pcm)
        {
            _voices.Add(new SampleVoice(pcm));
            return;
        }
        double sr = _rate;
        switch (sound)
        {
            case 1: _voices.Add(new SynthVoice(0.30, sr, (s, t) => (s.Noise(0.5) * 0.6 + Square(t * Lerp(1200, 300, t / 0.30)) * 0.4) * Math.Exp(-t * 9))); break;
            case 2: _voices.Add(new SynthVoice(1.20, sr, (s, t) => s.Noise(0.08) * 1.4 * Math.Exp(-t * 2.6))); break;
            case 3: _voices.Add(new SynthVoice(0.25, sr, (s, t) => s.Noise(0.6) * Math.Exp(-t * 14))); break;
            case 4: case 5: case 6: case 7:
            {
                double f = new[] { 92.5, 82.4, 73.4, 65.4 }[sound - 4];
                _voices.Add(new SynthVoice(0.11, sr, (s, t) => Triangle(t * f) * 0.9 * Math.Exp(-t * 18)));
                break;
            }
            case 8: _voices.Add(new SynthVoice(1.00, sr, (s, t) => Square(t * (900 - 500 * t + 120 * Math.Sin(t * 2 * Math.PI * 12))) * 0.35 * (1 - t))); break;
            case 9: _voices.Add(new SynthVoice(1.00, sr, (s, t) => ((int)(t * 16) % 2 == 0 ? Square(t * 1600) * 0.25 : 0))); break;
        }
    }

    private double UfoSample()
    {
        if (_samples[0] is { } pcm)
        {
            double v = pcm[_ufoSamplePos] / 32768.0;
            _ufoSamplePos = (_ufoSamplePos + 1) % pcm.Length;
            return v;
        }
        // warbling tone: carrier swept by a slow triangle
        _ufoLfo += 6.5 / _rate;
        double f = 600 + 220 * Triangle(_ufoLfo);
        _ufoPhase += f / _rate;
        return Square(_ufoPhase) * 0.22;
    }

    private double Noise(double brightness)
    {
        double white = _rng.NextDouble() * 2 - 1;
        _noiseLp += (white - _noiseLp) * brightness;
        return _noiseLp;
    }

    private static double Square(double phase) => phase - Math.Floor(phase) < 0.5 ? 1 : -1;
    private static double Triangle(double phase) { double p = phase - Math.Floor(phase); return p < 0.5 ? 4 * p - 1 : 3 - 4 * p; }
    private static double Lerp(double a, double b, double k) => a + (b - a) * Math.Clamp(k, 0, 1);

    private abstract class Voice
    {
        public bool Done { get; protected set; }
        public abstract double Next(SpaceInvadersSound owner);
    }

    private sealed class SynthVoice : Voice
    {
        private readonly int _length;
        private readonly double _rate;
        private readonly Func<SpaceInvadersSound, double, double> _wave;
        private int _pos;

        public SynthVoice(double seconds, double rate, Func<SpaceInvadersSound, double, double> wave)
        {
            _length = (int)(seconds * rate);
            _rate = rate;
            _wave = wave;
        }

        public override double Next(SpaceInvadersSound owner)
        {
            if (_pos >= _length) { Done = true; return 0; }
            return _wave(owner, _pos++ / _rate);
        }
    }

    private sealed class SampleVoice : Voice
    {
        private readonly short[] _pcm;
        private int _pos;

        public SampleVoice(short[] pcm) => _pcm = pcm;

        public override double Next(SpaceInvadersSound owner)
        {
            if (_pos >= _pcm.Length) { Done = true; return 0; }
            return _pcm[_pos++] / 32768.0;
        }
    }
}

/// <summary>Minimal RIFF/WAVE PCM reader (8/16 bit, mono/stereo, any rate) with resampling.</summary>
public static class WavReader
{
    public static short[] ReadMono(byte[] data, int targetRate)
    {
        if (data.Length < 44 || data[0] != 'R' || data[8] != 'W')
            throw new InvalidDataException("No es un archivo WAV.");
        int pos = 12, channels = 1, rate = 44100, bits = 16;
        byte[]? pcm = null;
        while (pos + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            int size = BitConverter.ToInt32(data, pos + 4);
            int body = pos + 8;
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(data, body + 2);
                rate = BitConverter.ToInt32(data, body + 4);
                bits = BitConverter.ToInt16(data, body + 14);
            }
            else if (id == "data")
            {
                pcm = data.AsSpan(body, Math.Min(size, data.Length - body)).ToArray();
            }
            pos = body + size + (size & 1);
        }
        if (pcm == null)
            throw new InvalidDataException("WAV sin datos.");
        int frameBytes = channels * bits / 8;
        int frames = pcm.Length / frameBytes;
        var src = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int o = i * frameBytes + c * bits / 8;
                sum += bits == 8 ? (pcm[o] - 128) / 128.0 : BitConverter.ToInt16(pcm, o) / 32768.0;
            }
            src[i] = sum / channels;
        }
        int outFrames = (int)((long)frames * targetRate / rate);
        var output = new short[outFrames];
        for (int i = 0; i < outFrames; i++)
        {
            double sp = (double)i * rate / targetRate;
            int i0 = (int)sp;
            double frac = sp - i0;
            double v = i0 + 1 < frames ? src[i0] * (1 - frac) + src[i0 + 1] * frac : src[Math.Min(i0, frames - 1)];
            output[i] = (short)Math.Clamp(v * 32767, -32767, 32767);
        }
        return output;
    }
}
