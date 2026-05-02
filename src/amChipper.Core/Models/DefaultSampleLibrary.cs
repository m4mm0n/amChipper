namespace amChipper.Core.Models;

/// <summary>
/// Represents the DefaultSampleLibrary component.
/// </summary>
public static class DefaultSampleLibrary
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int SampleRate = 22050;

    /// <summary>
    /// Executes the CreateStarterInstruments operation.
    /// </summary>
    public static IReadOnlyList<Instrument> CreateStarterInstruments()
    {
        var instruments = new List<Instrument>
        {
            CreateSampleInstrument("Chip Kick", CreateKick(), 36, 0xFFFF6D00),
            CreateSampleInstrument("Chip Snare", CreateSnare(), 38, 0xFFE74C3C),
            CreateSampleInstrument("Tight Hat", CreateHat(), 42, 0xFFFFD54F),
            CreateSampleInstrument("Noise Zap", CreateZap(), 60, 0xFF40C4FF),
            CreateSampleInstrument("Click Perc", CreateClick(), 39, 0xFFB388FF)
        };

        return instruments;
    }

    /// <summary>
    /// Executes the CreateSampleInstrument operation.
    /// </summary>
    private static Instrument CreateSampleInstrument(string name, Sample sample, byte rootNote, uint color)
    {
        var instrument = new Instrument
        {
            Name = name,
            SourceType = InstrumentSourceType.Sample,
            NoteColor = color,
            Samples = [sample]
        };

        sample.BaseNote = rootNote;
        for (int i = 0; i < instrument.NoteMap.Length; i++)
            instrument.NoteMap[i] = 0;

        return instrument;
    }

    /// <summary>
    /// Executes the CreateKick operation.
    /// </summary>
    private static Sample CreateKick()
    {
        int frames = (int)(SampleRate * 0.26);
        short[] pcm = new short[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 15.0);
            double freq = 118.0 * Math.Exp(-t * 18.0) + 38.0;
            phase += freq / SampleRate;
            double body = Math.Sin(phase * Math.Tau) * env;
            double click = i < 90 ? (1.0 - i / 90.0) * 0.45 : 0;
            pcm[i] = ToPcm16(body * 0.85 + click);
        }

        return CreateSample("Chip Kick", pcm, looped: false);
    }

    /// <summary>
    /// Executes the CreateSnare operation.
    /// </summary>
    private static Sample CreateSnare()
    {
        int frames = (int)(SampleRate * 0.24);
        short[] pcm = new short[frames];
        uint noise = 0xC0FFEEu;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 12.0);
            double tone = Math.Sin(t * 190.0 * Math.Tau) * Math.Exp(-t * 22.0) * 0.35;
            double n = (NextNoise(ref noise) * 2.0 - 1.0) * env * 0.68;
            pcm[i] = ToPcm16(tone + n);
        }

        return CreateSample("Chip Snare", pcm, looped: false);
    }

    /// <summary>
    /// Executes the CreateHat operation.
    /// </summary>
    private static Sample CreateHat()
    {
        int frames = (int)(SampleRate * 0.085);
        short[] pcm = new short[frames];
        uint noise = 0xA11CEu;
        double last = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 45.0);
            double n = NextNoise(ref noise) * 2.0 - 1.0;
            double high = n - last;
            last = n;
            pcm[i] = ToPcm16(high * env * 0.55);
        }

        return CreateSample("Tight Hat", pcm, looped: false);
    }

    /// <summary>
    /// Executes the CreateZap operation.
    /// </summary>
    private static Sample CreateZap()
    {
        int frames = (int)(SampleRate * 0.18);
        short[] pcm = new short[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 16.0);
            double freq = 1000.0 * Math.Exp(-t * 13.0) + 120.0;
            phase += freq / SampleRate;
            double saw = (phase - Math.Floor(phase)) * 2.0 - 1.0;
            pcm[i] = ToPcm16(saw * env * 0.65);
        }

        return CreateSample("Noise Zap", pcm, looped: false);
    }

    /// <summary>
    /// Executes the CreateClick operation.
    /// </summary>
    private static Sample CreateClick()
    {
        int frames = (int)(SampleRate * 0.045);
        short[] pcm = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            double env = 1.0 - i / (double)Math.Max(frames - 1, 1);
            double wave = i % 8 < 4 ? 1.0 : -1.0;
            pcm[i] = ToPcm16(wave * env * 0.45);
        }

        return CreateSample("Click Perc", pcm, looped: false);
    }

    /// <summary>
    /// Executes the CreateSample operation.
    /// </summary>
    private static Sample CreateSample(string name, short[] pcm, bool looped)
    {
        byte[] data = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, data, 0, data.Length);
        return new Sample
        {
            Name = name,
            Data = data,
            SampleRate = SampleRate,
            Channels = 1,
            BitsPerSample = 16,
            Looped = looped,
            LoopStart = 0,
            LoopEnd = looped ? pcm.Length : 0,
            RelativeVolume = 230,
            RelativePanning = 128
        };
    }

    /// <summary>
    /// Executes the NextNoise operation.
    /// </summary>
    private static double NextNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (double)0xFFFFFF;
    }

    /// <summary>
    /// Executes the ToPcm16 operation.
    /// </summary>
    private static short ToPcm16(double value) =>
        (short)Math.Clamp(Math.Round(Math.Clamp(value, -1.0, 1.0) * short.MaxValue), short.MinValue, short.MaxValue);
}
