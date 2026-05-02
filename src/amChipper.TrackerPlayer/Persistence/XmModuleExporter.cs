using System.Text;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the XmModuleExporter component.
/// </summary>
public static class XmModuleExporter
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int GeneratedSampleFrames = 1024;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int XmC4SampleRate = 8363;

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, string path)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        Save(song, stream);
    }

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, Stream stream) =>
        Save(song, stream, XmExportOptions.Default);

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, string path, XmExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        Save(song, stream, options);
    }

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, Stream stream, XmExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(stream);
        if (song.Format == ModuleFormat.SID)
            song = CreateSidXmApproximation(song, options);
        SongProjectSerializer.Normalize(song);

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        int channels = Math.Clamp(song.Tracks.Count, 1, 32);
        int patterns = Math.Clamp(song.Patterns.Count, 1, 256);
        int instruments = Math.Clamp(song.Instruments.Count, 1, 128);
        int songLength = Math.Clamp(song.OrderList.Count, 1, 256);

        WriteFixedAscii(writer, "Extended Module: ", 17);
        WriteFixedAscii(writer, song.Title, 20);
        writer.Write((byte)0x1A);
        WriteFixedAscii(writer, "amChipper", 20);
        writer.Write((ushort)0x0104);
        writer.Write(276u);
        writer.Write((ushort)songLength);
        writer.Write((ushort)0);
        writer.Write((ushort)channels);
        writer.Write((ushort)patterns);
        writer.Write((ushort)instruments);
        writer.Write((ushort)0);
        writer.Write((ushort)Math.Clamp(song.InitialSpeed, 1, 31));
        writer.Write((ushort)Math.Clamp(song.Bpm, 32, 255));

        for (int i = 0; i < 256; i++)
        {
            int orderPattern = i < song.OrderList.Count ? song.OrderList[i] : 0;
            writer.Write((byte)Math.Clamp(orderPattern, 0, patterns - 1));
        }

        for (int p = 0; p < patterns; p++)
            WritePattern(writer, song.Patterns[p], channels);

        for (int i = 0; i < instruments; i++)
            WriteInstrument(writer, i < song.Instruments.Count ? song.Instruments[i] : new Instrument { Name = $"Instrument {i + 1}" });
    }

    /// <summary>
    /// Executes the CreateSidXmApproximation operation.
    /// </summary>
    private static Song CreateSidXmApproximation(Song source, XmExportOptions options)
    {
        var song = source.Clone();
        song.Format = ModuleFormat.XM;
        song.Title = $"{source.Title} (SID XM)";
        song.Comment += options.SidMode switch
        {
            SidXmExportMode.RenderedMixOnly => " SID-to-XM conversion: rendered SID audio is embedded as the primary playback lane; editable trace lanes are silenced to avoid double playback.",
            SidXmExportMode.TraceOnly => " SID-to-XM conversion: editable waveform/control trace lanes are exported as XM-safe chip instruments.",
            _ => " SID-to-XM conversion: rendered SID audio is embedded for audible playback; waveform/control trace cells are kept as editable XM-safe note lanes."
        };

        var sidInstruments = new[]
        {
            CreateSidExportInstrument("SID Pulse", SynthWaveform.Square, 0.5, 0xFF39D9C8),
            CreateSidExportInstrument("SID Triangle", SynthWaveform.Triangle, 0.5, 0xFFFFB000),
            CreateSidExportInstrument("SID Saw", SynthWaveform.Saw, 0.5, 0xFFE040FB),
            CreateSidExportInstrument("SID Noise", SynthWaveform.Noise, 0.5, 0xFF8BC34A),
            CreateSidExportInstrument("SID Pulse Narrow", SynthWaveform.Square, 0.25, 0xFF61D4FF),
            CreateSidExportInstrument("SID Pulse Wide", SynthWaveform.Square, 0.75, 0xFFFF8A65)
        };

        song.Instruments.Clear();
        song.Instruments.AddRange(sidInstruments);
        for (int i = 0; i < song.Tracks.Count; i++)
        {
            song.Tracks[i].InstrumentIndex = Math.Clamp(i, 0, 2);
            if (i >= 3)
                song.Tracks[i].Name = "SID Filter Trace";
        }

        foreach (var pattern in song.Patterns)
        {
            for (int row = 0; row < pattern.RowCount; row++)
            {
                for (int channel = 0; channel < pattern.ChannelCount; channel++)
                {
                    var note = pattern.GetNote(row, channel);
                    if (channel >= 3)
                    {
                        pattern.SetNote(row, channel, new Note());
                        continue;
                    }

                    if (note.Pitch == (byte)SpecialNote.NoteOff)
                    {
                        pattern.SetNote(row, channel, new Note { Pitch = (byte)SpecialNote.NoteOff });
                        continue;
                    }

                    if (note.Pitch == 0 || note.Pitch >= (byte)SpecialNote.NoteOff)
                    {
                        pattern.SetNote(row, channel, new Note());
                        continue;
                    }

                    if (!IsSidTraceCell(note))
                    {
                        pattern.SetNote(row, channel, new Note
                        {
                            Pitch = note.Pitch,
                            InstrumentIndex = (byte)Math.Clamp((int)note.InstrumentIndex, 1, sidInstruments.Length),
                            Volume = ResolveSidXmVolume(note),
                            DurationTicks = Math.Max(1, note.DurationTicks),
                            Panning = channel switch
                            {
                                0 => 72,
                                1 => 128,
                                2 => 184,
                                _ => 128
                            }
                        });
                        continue;
                    }

                    byte control = note.EffectParam;
                    var xmNote = note.Clone();
                    xmNote.InstrumentIndex = (byte)ResolveSidXmInstrument(note);
                    xmNote.Volume = ResolveSidXmVolume(note);
                    xmNote.VolumeColumn = 0;
                    xmNote.EffectColumn = 0;
                    xmNote.Effect = (control & 0x04) != 0 ? EffectCommand.Vibrato : EffectCommand.None;
                    xmNote.EffectParam = xmNote.Effect == EffectCommand.Vibrato
                        ? (byte)(0x31 + ((note.EffectColumn ^ control) & 0x03))
                        : (byte)0;
                    xmNote.Panning = channel switch
                    {
                        0 => 72,
                        1 => 128,
                        2 => 184,
                        _ => 128
                    };
                    pattern.SetNote(row, channel, xmNote);
                }
            }
        }

        bool renderedMixAdded = false;
        if (options.SidMode is SidXmExportMode.RenderedMixOnly or SidXmExportMode.RenderedMixWithTrace &&
            source.OriginalModuleData is { Length: > 0 })
        {
            renderedMixAdded = AddRenderedSidPlaybackLane(song, source.OriginalModuleData);
        }

        if (renderedMixAdded && options.SidMode == SidXmExportMode.RenderedMixOnly)
            SilenceSidTraceLanes(song);

        return song;
    }

    /// <summary>
    /// Executes the AddRenderedSidPlaybackLane operation.
    /// </summary>
    private static bool AddRenderedSidPlaybackLane(Song song, byte[] sidData)
    {
        int renderSeconds = EstimateSongSeconds(song);
        if (renderSeconds <= 0)
            return false;

        float[] stereo = InternalChipRenderer.RenderStereoFloat(sidData, $"{song.Title}.sid", renderSeconds, XmC4SampleRate);
        byte[] monoPcm = ConvertStereoFloatToMonoPcm16(stereo);
        if (monoPcm.Length == 0)
            return false;

        var sample = new Sample
        {
            Name = "Rendered SID Mix",
            Data = monoPcm,
            SampleRate = XmC4SampleRate,
            Channels = 1,
            BitsPerSample = 16,
            BaseNote = 60,
            RelativeVolume = 255,
            RelativePanning = 128,
            Looped = false
        };

        var instrument = new Instrument
        {
            Name = "Rendered SID Mix",
            SourceType = InstrumentSourceType.Sample,
            GlobalVolume = 128,
            NoteColor = 0xFFFFFFFF,
            Samples = [sample],
            NoteMap = Enumerable.Repeat((byte)0, 128).ToArray()
        };

        song.Instruments.Add(instrument);
        int instrumentNumber = song.Instruments.Count;
        int renderChannel = song.Tracks.Count;
        song.Tracks.Add(new Track
        {
            Name = "Rendered SID Mix",
            InstrumentIndex = instrumentNumber - 1,
            Volume = 128,
            Panning = 128,
            Color = 0xFFFFFFFF,
            EffectSummary = "SID render"
        });

        foreach (var pattern in song.Patterns)
            pattern.Resize(pattern.RowCount, pattern.ChannelCount + 1);

        if (song.Patterns.Count > 0)
        {
            song.Patterns[0].SetNote(0, renderChannel, new Note
            {
                Pitch = 60,
                InstrumentIndex = (byte)Math.Clamp(instrumentNumber, 1, 255),
                Volume = 64,
                DurationTicks = Math.Max(1, song.Patterns.Sum(pattern => pattern.RowCount))
            });
        }

        double beat = 0;
        int rowsPerBeat = Math.Max(song.RowsPerBeat, 1);
        foreach (int patternIndex in song.OrderList)
        {
            if ((uint)patternIndex >= (uint)song.Patterns.Count)
                continue;

            double duration = song.Patterns[patternIndex].RowCount / (double)rowsPerBeat;
            song.Tracks[renderChannel].Blocks.Add(new PatternBlock
            {
                PatternIndex = patternIndex,
                StartBeat = beat,
                DurationBeats = duration
            });
            beat += duration;
        }

        return true;
    }

    /// <summary>
    /// Executes the SilenceSidTraceLanes operation.
    /// </summary>
    private static void SilenceSidTraceLanes(Song song)
    {
        int traceChannels = Math.Min(4, song.Tracks.Count);
        for (int i = 0; i < traceChannels; i++)
        {
            song.Tracks[i].EffectSummary = "SID trace muted in render-only XM export";
            song.Tracks[i].Muted = true;
        }

        foreach (var pattern in song.Patterns)
        {
            int channels = Math.Min(traceChannels, pattern.ChannelCount);
            for (int row = 0; row < pattern.RowCount; row++)
            {
                for (int channel = 0; channel < channels; channel++)
                    pattern.SetNote(row, channel, new Note());
            }
        }
    }

    /// <summary>
    /// Executes the EstimateSongSeconds operation.
    /// </summary>
    private static int EstimateSongSeconds(Song song)
    {
        int rows = 0;
        foreach (int patternIndex in song.OrderList)
        {
            if ((uint)patternIndex < (uint)song.Patterns.Count)
                rows += song.Patterns[patternIndex].RowCount;
        }

        double rowsPerSecond = Math.Max(1.0, (song.Bpm / 60.0) * Math.Max(song.RowsPerBeat, 1));
        return Math.Clamp((int)Math.Ceiling(rows / rowsPerSecond), 1, 3600);
    }

    /// <summary>
    /// Executes the ConvertStereoFloatToMonoPcm16 operation.
    /// </summary>
    private static byte[] ConvertStereoFloatToMonoPcm16(float[] stereo)
    {
        if (stereo.Length < 2)
            return [];

        var pcm = new byte[(stereo.Length / 2) * 2];
        for (int frame = 0; frame < pcm.Length / 2; frame++)
        {
            float mono = (stereo[frame * 2] + stereo[frame * 2 + 1]) * 0.5f;
            short value = (short)Math.Clamp(Math.Round(mono * short.MaxValue), short.MinValue, short.MaxValue);
            pcm[frame * 2] = (byte)(value & 0xFF);
            pcm[frame * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>
    /// Executes the CreateSidExportInstrument operation.
    /// </summary>
    private static Instrument CreateSidExportInstrument(string name, SynthWaveform waveform, double pulseWidth, uint color)
    {
        return new Instrument
        {
            Name = name,
            SourceType = InstrumentSourceType.Synth,
            Waveform = waveform,
            PulseWidth = pulseWidth,
            AttackMs = 0,
            ReleaseMs = 8,
            GlobalVolume = 128,
            NoteColor = color
        };
    }

    /// <summary>
    /// Executes the IsSidTraceCell operation.
    /// </summary>
    private static bool IsSidTraceCell(Note note) =>
        note.InstrumentIndex is >= 1 and <= 3 &&
        note.VolumeColumn is 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80;

    /// <summary>
    /// Executes the ResolveSidXmInstrument operation.
    /// </summary>
    private static int ResolveSidXmInstrument(Note note)
    {
        byte wave = (byte)(note.VolumeColumn & 0xF0);
        if ((wave & 0x80) != 0)
            return 4;
        if ((wave & 0x40) != 0)
        {
            double pulse = note.EffectColumn / 255.0;
            if (pulse < 0.34)
                return 5;
            if (pulse > 0.66)
                return 6;
            return 1;
        }
        if ((wave & 0x10) != 0)
            return 3;
        if ((wave & 0x20) != 0)
            return 2;

        return Math.Clamp((int)note.InstrumentIndex, 1, 3);
    }

    /// <summary>
    /// Executes the ResolveSidXmVolume operation.
    /// </summary>
    private static byte ResolveSidXmVolume(Note note)
    {
        if (note.Volume <= 64)
            return (byte)Math.Clamp((int)note.Volume, 40, 64);

        return 56;
    }

    /// <summary>
    /// Executes the WritePattern operation.
    /// </summary>
    private static void WritePattern(BinaryWriter writer, Pattern pattern, int channels)
    {
        using var data = new MemoryStream();
        int rows = Math.Clamp(pattern.RowCount, 1, 256);
        using (var patternWriter = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true))
        {
            for (int row = 0; row < rows; row++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    var note = ch < pattern.ChannelCount ? pattern.GetNote(row, ch) : new Note();
                    patternWriter.Write(ToXmNote(note));
                    patternWriter.Write(note.InstrumentIndex);
                    patternWriter.Write(ToXmVolume(note));
                    patternWriter.Write(ToXmEffect(note, out byte param));
                    patternWriter.Write(param);
                }
            }
        }

        writer.Write(9u);
        writer.Write((byte)0);
        writer.Write((ushort)rows);
        writer.Write((ushort)Math.Min(data.Length, ushort.MaxValue));
        writer.Write(data.ToArray(), 0, (int)Math.Min(data.Length, ushort.MaxValue));
    }

    /// <summary>
    /// Executes the WriteInstrument operation.
    /// </summary>
    private static void WriteInstrument(BinaryWriter writer, Instrument instrument)
    {
        var samples = instrument.SourceType == InstrumentSourceType.Sample && instrument.Samples.Count > 0
            ? instrument.Samples.Take(16).ToArray()
            : [];

        if (samples.Length == 0)
        {
            WriteGeneratedInstrument(writer, instrument);
            return;
        }

        writer.Write(263u);
        WriteFixedAscii(writer, instrument.Name, 22);
        writer.Write((byte)0);
        writer.Write((ushort)samples.Length);
        writer.Write(40u);

        for (int i = 0; i < 96; i++)
        {
            int midi = Math.Clamp(i + 12, 0, instrument.NoteMap.Length - 1);
            int mapped = instrument.NoteMap.Length > 0 ? instrument.NoteMap[midi] : 0;
            writer.Write((byte)Math.Clamp(mapped == 255 ? 0 : mapped, 0, samples.Length - 1));
        }

        WriteDefaultEnvelopeData(writer);

        foreach (var sample in samples)
            WriteSampleHeader(writer, sample);

        foreach (var sample in samples)
            writer.Write(BuildSampleData(sample));
    }

    /// <summary>
    /// Executes the WriteGeneratedInstrument operation.
    /// </summary>
    private static void WriteGeneratedInstrument(BinaryWriter writer, Instrument instrument)
    {
        writer.Write(263u);
        WriteFixedAscii(writer, instrument.Name, 22);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write(40u);

        for (int i = 0; i < 96; i++)
            writer.Write((byte)0);

        WriteDefaultEnvelopeData(writer);

        writer.Write((uint)(GeneratedSampleFrames * 2));
        writer.Write(0u);
        writer.Write((uint)((GeneratedSampleFrames - 1) * 2));
        writer.Write((byte)64);
        writer.Write((sbyte)0);
        writer.Write((byte)0x11);
        writer.Write((byte)128);
        writer.Write((sbyte)0);
        writer.Write((byte)0);
        WriteFixedAscii(writer, instrument.Name, 22);

        foreach (byte sample in BuildChipSample(instrument))
            writer.Write(sample);
    }

    /// <summary>
    /// Executes the WriteDefaultEnvelopeData operation.
    /// </summary>
    private static void WriteDefaultEnvelopeData(BinaryWriter writer)
    {
        for (int i = 0; i < 12; i++)
        {
            writer.Write((ushort)0);
            writer.Write((ushort)0);
        }
        for (int i = 0; i < 12; i++)
        {
            writer.Write((ushort)0);
            writer.Write((ushort)32);
        }

        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(new byte[22]);
    }

    /// <summary>
    /// Executes the WriteSampleHeader operation.
    /// </summary>
    private static void WriteSampleHeader(BinaryWriter writer, Sample sample)
    {
        int channels = Math.Max(sample.Channels, 1);
        int frames = Math.Max(sample.FrameCount, 0);
        int lengthBytes = frames * 2;
        int loopStartFrames = Math.Clamp(sample.LoopStart, 0, Math.Max(frames - 1, 0));
        int loopEndFrames = Math.Clamp(sample.LoopEnd, loopStartFrames, frames);
        int loopLengthFrames = Math.Max(0, loopEndFrames - loopStartFrames);
        bool looped = sample.Looped && loopLengthFrames > 2;

        writer.Write((uint)lengthBytes);
        writer.Write((uint)(looped ? loopStartFrames * 2 : 0));
        writer.Write((uint)(looped ? loopLengthFrames * 2 : 0));
        writer.Write((byte)Math.Clamp((int)Math.Round(sample.RelativeVolume / 255.0 * 64.0), 0, 64));
        writer.Write((sbyte)Math.Clamp((int)Math.Round(sample.FineTune * 128.0 / 100.0), -128, 127));
        writer.Write((byte)(0x10 | (looped ? (sample.PingPongLoop ? 0x02 : 0x01) : 0x00)));
        writer.Write(sample.RelativePanning == 255 ? (byte)128 : sample.RelativePanning);
        writer.Write((sbyte)Math.Clamp(60 - sample.BaseNote, -128, 127));
        writer.Write((byte)0);
        WriteFixedAscii(writer, sample.Name, 22);
    }

    /// <summary>
    /// Executes the BuildSampleData operation.
    /// </summary>
    private static byte[] BuildSampleData(Sample sample)
    {
        int frames = Math.Max(sample.FrameCount, 0);
        int channels = Math.Max(sample.Channels, 1);
        var data = new byte[frames * 2];
        int previous = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            int value = ReadPcm16(sample, frame, channels);
            short delta = unchecked((short)(value - previous));
            data[frame * 2] = (byte)(delta & 0xFF);
            data[frame * 2 + 1] = (byte)((delta >> 8) & 0xFF);
            previous = value;
        }

        return data;
    }

    /// <summary>
    /// Executes the ReadPcm16 operation.
    /// </summary>
    private static int ReadPcm16(Sample sample, int frame, int channels)
    {
        if (sample.Data.Length == 0)
            return 0;

        long sum = 0;
        if (sample.BitsPerSample == 16)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                int offset = (frame * channels + ch) * 2;
                if (offset + 1 >= sample.Data.Length)
                    continue;
                sum += (short)(sample.Data[offset] | (sample.Data[offset + 1] << 8));
            }
        }
        else
        {
            for (int ch = 0; ch < channels; ch++)
            {
                int offset = frame * channels + ch;
                if (offset >= sample.Data.Length)
                    continue;
                sum += ((sbyte)(sample.Data[offset] - 128)) << 8;
            }
        }

        return (int)Math.Clamp(sum / Math.Max(channels, 1), short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// Executes the ToXmNote operation.
    /// </summary>
    private static byte ToXmNote(Note note)
    {
        if (note.Pitch == (byte)SpecialNote.NoteOff)
            return 97;
        if (note.Pitch == 0 || note.Pitch >= (byte)SpecialNote.NoteOff)
            return 0;

        return (byte)Math.Clamp(note.Pitch - 23, 1, 96);
    }

    /// <summary>
    /// Executes the ToXmVolume operation.
    /// </summary>
    private static byte ToXmVolume(Note note) =>
        note.VolumeColumn != 0 ? note.VolumeColumn :
        note.Volume <= 64 ? (byte)(0x10 + note.Volume) : (byte)0;

    /// <summary>
    /// Executes the ToXmEffect operation.
    /// </summary>
    private static byte ToXmEffect(Note note, out byte param)
    {
        param = note.EffectParam;
        if (note.EffectColumn != 0 || (note.Effect == EffectCommand.None && note.EffectParam != 0))
            return note.EffectColumn;

        return note.Effect switch
        {
            EffectCommand.PortaUp => 0x01,
            EffectCommand.PortaDown => 0x02,
            EffectCommand.TonePorta => 0x03,
            EffectCommand.Vibrato => 0x04,
            EffectCommand.VolSlide => 0x05,
            EffectCommand.PortaVolSlide => 0x06,
            EffectCommand.Tremolo => 0x07,
            EffectCommand.SetPan => 0x08,
            EffectCommand.SampleOffset => 0x09,
            EffectCommand.VolumeSlide => 0x0A,
            EffectCommand.PosJump => 0x0B,
            EffectCommand.SetVolume => 0x0C,
            EffectCommand.PatternBreak => 0x0D,
            EffectCommand.FinePortaUp => Extended(0x10, note.EffectParam, out param),
            EffectCommand.FinePortaDown => Extended(0x20, note.EffectParam, out param),
            EffectCommand.NoteCut => Extended(0xC0, note.EffectParam, out param),
            EffectCommand.NoteDelay => Extended(0xD0, note.EffectParam, out param),
            EffectCommand.SetSpeed => 0x0F,
            EffectCommand.SetGlobalVol => 0x10,
            EffectCommand.RetrigNote => 0x1B,
            EffectCommand.SetBpm => 0x1D,
            _ => 0x00
        };
    }

    /// <summary>
    /// Executes the Extended operation.
    /// </summary>
    private static byte Extended(int highNibble, byte param, out byte mappedParam)
    {
        mappedParam = (byte)(highNibble | (param & 0x0F));
        return 0x0E;
    }

    /// <summary>
    /// Executes the BuildChipSample operation.
    /// </summary>
    private static byte[] BuildChipSample(Instrument instrument)
    {
        var pcm = new byte[GeneratedSampleFrames * 2];
        int previous = 0;
        double pulseWidth = Math.Clamp(instrument.PulseWidth, 0.05, 0.95);
        for (int i = 0; i < GeneratedSampleFrames; i++)
        {
            double phase = i / (double)GeneratedSampleFrames;
            int value = instrument.Waveform switch
            {
                SynthWaveform.Triangle => (int)Math.Round((4.0 * Math.Abs(phase - 0.5) - 1.0) * 28000),
                SynthWaveform.Saw => (int)Math.Round((2.0 * phase - 1.0) * 28000),
                SynthWaveform.Noise => (((i * 1103515245 + 12345) >> 16) % 56001) - 28000,
                _ => phase < pulseWidth ? 28000 : -28000
            };

            short delta = unchecked((short)(Math.Clamp(value, short.MinValue, short.MaxValue) - previous));
            pcm[i * 2] = (byte)(delta & 0xFF);
            pcm[i * 2 + 1] = (byte)((delta >> 8) & 0xFF);
            previous = value;
        }

        return pcm;
    }

    /// <summary>
    /// Executes the WriteFixedAscii operation.
    /// </summary>
    private static void WriteFixedAscii(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        int count = Math.Min(bytes.Length, length);
        writer.Write(bytes, 0, count);
        if (count < length)
            writer.Write(new byte[length - count]);
    }
}

/// <summary>
/// Lists SidXmExportMode values.
/// </summary>
public enum SidXmExportMode
{
    RenderedMixOnly,
    RenderedMixWithTrace,
    TraceOnly
}

/// <summary>
/// Carries XmExportOptions data.
/// </summary>
public sealed record XmExportOptions(SidXmExportMode SidMode)
{
    /// <summary>
    /// Stores or exposes IncludeRenderedSidMix.
    /// </summary>
    public bool IncludeRenderedSidMix => SidMode is SidXmExportMode.RenderedMixOnly or SidXmExportMode.RenderedMixWithTrace;

    public static XmExportOptions Default { get; } = new(SidXmExportMode.RenderedMixOnly);
}
