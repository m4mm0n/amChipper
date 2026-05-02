using System.Diagnostics;
using System.Text;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Carries ChipTuneMetadata data.
/// </summary>
public sealed record ChipTuneMetadata(
    ModuleFormat Format,
    string Type,
    string Title,
    string Artist,
    string Comment,
    int SongCount,
    int StartSong,
    int Version = 0,
    int DataOffset = 0,
    int LoadAddress = 0,
    int InitAddress = 0,
    int PlayAddress = 0,
    string Clock = "",
    string SidModel = "",
    int ProgramLength = 0,
    int DurationSeconds = 0);

/// <summary>
/// Represents the ChipTuneFile component.
/// </summary>
public static class ChipTuneFile
{
    /// <summary>
    /// Stores or exposes UseSongLengthDatabase.
    /// </summary>
    public static bool UseSongLengthDatabase { get; set; } = true;

    /// <summary>
    /// Executes the IsSupported operation.
    /// </summary>
    public static bool IsSupported(string path) =>
        ModuleFormatCatalog.TryResolve(null, Path.GetExtension(path), out var info) &&
        ModuleFormatCatalog.IsEmulatedChipFormat(info.Format);

    /// <summary>
    /// Executes the ImportAsSong operation.
    /// </summary>
    public static Song ImportAsSong(byte[] data, string path)
    {
        var metadata = ReadMetadata(data, path);
        var song = Song.CreateDefault();
        song.Title = string.IsNullOrWhiteSpace(metadata.Title)
            ? Path.GetFileNameWithoutExtension(path)
            : metadata.Title;
        song.Artist = metadata.Artist;
        song.Comment = BuildComment(metadata);
        song.Format = metadata.Format;
        song.SourceModuleType = metadata.Type;
        song.SourceModuleExtension = ModuleFormatCatalog.NormalizeExtension(Path.GetExtension(path));
        song.OriginalModuleData = (byte[])data.Clone();
        song.Bpm = 125;
        song.RowsPerBeat = 4;
        song.InitialSpeed = 6;
        song.RestartOrder = -1;

        song.Instruments.Clear();
        song.Tracks.Clear();
        song.Patterns.Clear();
        song.OrderList.Clear();

        if (metadata.Format == ModuleFormat.SID)
        {
            ImportSidStructure(song, data, metadata);
        }
        else
        {
            ImportNsfStructure(song, data, metadata);
        }

        return song;
    }

    /// <summary>
    /// Executes the ReadMetadata operation.
    /// </summary>
    public static ChipTuneMetadata ReadMetadata(byte[] data, string path)
    {
        if (data.Length >= 4)
        {
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic is "PSID" or "RSID")
                return ReadSidMetadata(data, path, magic);
            if (magic == "PP20")
                throw new InvalidDataException($"{Path.GetFileName(path)} is PowerPacker-compressed data, not a directly playable PSID/RSID file.");
            if (data.Length >= 5 && Encoding.ASCII.GetString(data, 0, 5) == "NESM\x1A")
                return ReadNsfMetadata(data, path);
            if (magic == "NSFE")
                return new ChipTuneMetadata(ModuleFormat.NSF, "NSFE", Path.GetFileNameWithoutExtension(path), string.Empty, "Nintendo Sound Format Extended", 1, 1);
        }

        var extension = ModuleFormatCatalog.NormalizeExtension(Path.GetExtension(path));
        var format = string.Equals(extension, ".nsf", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(extension, ".nsfe", StringComparison.OrdinalIgnoreCase)
            ? ModuleFormat.NSF
            : ModuleFormat.SID;
        string type = format == ModuleFormat.NSF ? "NSF" : "SID";
        return new ChipTuneMetadata(format, type, Path.GetFileNameWithoutExtension(path), string.Empty, "Unrecognised header; kept as native chip file.", 1, 1);
    }

    /// <summary>
    /// Executes the ReadSidMetadata operation.
    /// </summary>
    private static ChipTuneMetadata ReadSidMetadata(byte[] data, string path, string magic)
    {
        int version = ReadBigEndianWord(data, 0x04);
        int dataOffset = ReadBigEndianWord(data, 0x06);
        int loadAddress = ReadBigEndianWord(data, 0x08);
        int initAddress = ReadBigEndianWord(data, 0x0A);
        int playAddress = ReadBigEndianWord(data, 0x0C);
        int songs = ReadBigEndianWord(data, 0x0E);
        int startSong = ReadBigEndianWord(data, 0x10);
        string title = ReadFixedAscii(data, 0x16, 32);
        string artist = ReadFixedAscii(data, 0x36, 32);
        string released = ReadFixedAscii(data, 0x56, 32);
        ushort flags = version >= 2 && data.Length >= 0x78 ? (ushort)ReadBigEndianWord(data, 0x76) : (ushort)0;
        string clock = ((flags >> 2) & 0x03) switch
        {
            1 => "PAL",
            2 => "NTSC",
            3 => "PAL/NTSC",
            _ => "Unknown"
        };
        string sidModel = ((flags >> 4) & 0x03) switch
        {
            1 => "MOS 6581",
            2 => "MOS 8580",
            3 => "6581/8580",
            _ => "Unknown"
        };
        int resolvedDataOffset = dataOffset > 0 ? dataOffset : 0x76;
        int resolvedLoad = loadAddress;
        int payloadOffset = resolvedDataOffset;
        if (resolvedLoad == 0 && payloadOffset + 1 < data.Length)
        {
            resolvedLoad = data[payloadOffset] | (data[payloadOffset + 1] << 8);
            payloadOffset += 2;
        }
        int resolvedInit = initAddress == 0 ? resolvedLoad : NormalizeSidEntryAddress(initAddress, resolvedLoad);
        int resolvedPlay = NormalizeSidEntryAddress(playAddress, resolvedLoad);

        return new ChipTuneMetadata(
            ModuleFormat.SID,
            magic,
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title,
            artist,
            released,
            Math.Max(songs, 1),
            Math.Max(startSong, 1),
            version,
            resolvedDataOffset,
            resolvedLoad,
            resolvedInit,
            resolvedPlay,
            clock,
            sidModel,
            Math.Max(0, data.Length - payloadOffset),
            TryFindSongLengthSeconds(path, Math.Max(startSong, 1)));
    }

    /// <summary>
    /// Executes the ReadNsfMetadata operation.
    /// </summary>
    private static ChipTuneMetadata ReadNsfMetadata(byte[] data, string path)
    {
        int songs = data.Length > 0x06 ? data[0x06] : 1;
        int startSong = data.Length > 0x07 ? data[0x07] : 1;
        string title = ReadFixedAscii(data, 0x0E, 32);
        string artist = ReadFixedAscii(data, 0x2E, 32);
        string copyright = ReadFixedAscii(data, 0x4E, 32);
        int load = ReadLittleEndianWord(data, 0x08);
        int init = ReadLittleEndianWord(data, 0x0A);
        int play = ReadLittleEndianWord(data, 0x0C);
        int ntscSpeed = ReadLittleEndianWord(data, 0x6E);
        int palSpeed = ReadLittleEndianWord(data, 0x78);
        byte expansion = data.Length > 0x7B ? data[0x7B] : (byte)0;
        string expansionText = DescribeNsfExpansion(expansion);
        string speedText = $"NTSC {ntscSpeed}us";
        if (palSpeed > 0)
            speedText += $", PAL {palSpeed}us";

        return new ChipTuneMetadata(
            ModuleFormat.NSF,
            "NSF",
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title,
            artist,
            $"{copyright} Expansion={expansionText}, {speedText}",
            Math.Max(songs, 1),
            Math.Max(startSong, 1),
            data.Length > 0x05 ? data[0x05] : 1,
            0x80,
            load,
            init,
            play,
            expansionText,
            "",
            Math.Max(0, data.Length - 0x80));
    }

    /// <summary>
    /// Executes the BuildComment operation.
    /// </summary>
    private static string BuildComment(ChipTuneMetadata metadata)
    {
        string comment = $"{metadata.Type} chip tune. Songs={metadata.SongCount}, StartSong={metadata.StartSong}.";
        if (metadata.Format == ModuleFormat.SID)
        {
            comment += $" Version={metadata.Version}, Load=${metadata.LoadAddress:X4}, Init=${metadata.InitAddress:X4}, Play=${metadata.PlayAddress:X4}, Data=${metadata.DataOffset:X4}, Bytes={metadata.ProgramLength}, Clock={metadata.Clock}, SID={metadata.SidModel}.";
            if (metadata.DurationSeconds > 0)
                comment += $" HVSC length={metadata.DurationSeconds / 60}:{metadata.DurationSeconds % 60:00}.";
        }
        else if (metadata.Format == ModuleFormat.NSF)
        {
            comment += $" Version={metadata.Version}, Load=${metadata.LoadAddress:X4}, Init=${metadata.InitAddress:X4}, Play=${metadata.PlayAddress:X4}, Data=${metadata.DataOffset:X4}, Bytes={metadata.ProgramLength}, Expansion={metadata.Clock}.";
        }
        if (!string.IsNullOrWhiteSpace(metadata.Comment))
            comment += $" {metadata.Comment}";
        return comment;
    }

    /// <summary>
    /// Imports NSF chip driver state as tracker-visible 2A03 and expansion-chip trace lanes.
    /// </summary>
    private static void ImportNsfStructure(Song song, byte[] data, ChipTuneMetadata metadata)
    {
        string[] names =
        [
            "2A03 Pulse 1", "2A03 Pulse 2", "2A03 Triangle", "2A03 Noise",
            "2A03 DPCM", "VRC6 Pulse 1", "VRC6 Pulse 2", "VRC6 Saw",
            "VRC7 FM 1", "VRC7 FM 2", "VRC7 FM 3", "VRC7 FM 4", "VRC7 FM 5", "VRC7 FM 6",
            "MMC5 Pulse 1", "MMC5 Pulse 2",
            "N163 Wave 1", "N163 Wave 2", "N163 Wave 3", "N163 Wave 4",
            "N163 Wave 5", "N163 Wave 6", "N163 Wave 7", "N163 Wave 8",
            "S5B PSG 1", "S5B PSG 2", "S5B PSG 3", "FDS Wavetable"
        ];

        for (int channel = 0; channel < names.Length; channel++)
        {
            uint color = NsfLaneColor(channel);
            song.Instruments.Add(new Instrument
            {
                Name = names[channel],
                SourceType = InstrumentSourceType.Synth,
                Waveform = NsfLaneWaveform(channel),
                PulseWidth = channel is 0 or 1 or 5 or 6 or 14 or 15 or >= 24 and <= 26 ? 0.5 : 0.25,
                NoteColor = color,
                AttackMs = 0,
                ReleaseMs = channel == 2 ? 20 : 8
            });
            song.Tracks.Add(new Track
            {
                Name = names[channel],
                InstrumentIndex = channel,
                Volume = channel == 4 ? (byte)96 : (byte)128,
                Panning = (byte)Math.Clamp(36 + channel * 7, 24, 232),
                Color = color,
                EffectSummary = NsfLaneEffectSummary(channel)
            });
        }

        const int rowsPerPattern = 64;
        int traceRows = EstimateNsfTraceRows(song, metadata, rowsPerPattern);
        int subtunesToTrace = Math.Clamp(metadata.SongCount, 1, 3);
        var traceBudget = Stopwatch.StartNew();
        double beats = rowsPerPattern / (double)Math.Max(song.RowsPerBeat, 1);
        for (int subtune = 1; subtune <= subtunesToTrace; subtune++)
        {
            int sequenceStart = song.OrderList.Count;
            int patternCount = Math.Max(1, (int)Math.Ceiling(traceRows / (double)rowsPerPattern));
            var patterns = Enumerable.Range(0, patternCount)
                .Select(i =>
                {
                    int rows = i == patternCount - 1
                        ? Math.Max(1, traceRows - i * rowsPerPattern)
                        : rowsPerPattern;
                    return new Pattern(rows, names.Length) { Name = $"NSF S{subtune:00} Frame {i:00}" };
                })
                .ToArray();

            IReadOnlyList<NsfVoiceRow> rows;
            try
            {
                int remainingMs = Math.Max(250, 1400 - (int)traceBudget.ElapsedMilliseconds);
                rows = InternalChipRenderer.InspectNsfVoiceRows(data, traceRows, subtune, remainingMs);
                if (!rows.Any(row => row.Pitch is > 0 and < (byte)SpecialNote.NoteOff))
                    rows = BuildFallbackNsfRows(data, traceRows, subtune);
            }
            catch
            {
                rows = BuildFallbackNsfRows(data, traceRows, subtune);
            }

            foreach (var row in rows)
            {
                int patternIndex = row.Row / rowsPerPattern;
                int patternRow = row.Row % rowsPerPattern;
                if ((uint)patternIndex >= (uint)patterns.Length ||
                    (uint)patternRow >= (uint)patterns[patternIndex].RowCount ||
                    (uint)row.Voice >= (uint)names.Length)
                    continue;

                patterns[patternIndex].SetNote(patternRow, row.Voice, new Note
                {
                    Pitch = row.Pitch,
                    InstrumentIndex = (byte)(row.Voice + 1),
                    Volume = row.Volume,
                    DurationTicks = Math.Max(1, row.DurationRows),
                    Velocity = (byte)Math.Clamp(row.Volume * 2, 1, 127),
                    VolumeColumn = row.VolumeColumn,
                    EffectColumn = row.EffectColumn,
                    EffectParam = row.EffectParam,
                    Effect = DecodeNsfEffect(row),
                    Panning = song.Tracks[row.Voice].Panning
                });
            }

            int activePatternCount = Math.Clamp(CountActiveSidPatterns(patterns), 1, patterns.Length);
            for (int sequence = 0; sequence < activePatternCount; sequence++)
            {
                int patternIndex = song.Patterns.Count;
                patterns[sequence].Name = $"NSF S{subtune:00} Phrase {sequence:00}";
                song.Patterns.Add(patterns[sequence]);
                song.OrderList.Add(patternIndex);
                double startBeat = (sequenceStart + sequence) * beats;
                for (int track = 0; track < song.Tracks.Count; track++)
                {
                    song.Tracks[track].Blocks.Add(new PatternBlock
                    {
                        PatternIndex = patternIndex,
                        StartBeat = startBeat,
                        DurationBeats = beats
                    });
                }
            }

            if (traceBudget.ElapsedMilliseconds > 1500 && subtune < subtunesToTrace)
            {
                song.Comment += $" NSF trace budget reached after subtune {subtune}; remaining subtunes use source playback/render paths.";
                break;
            }
        }

        if (metadata.SongCount > subtunesToTrace)
            song.Comment += $" Traced first {subtunesToTrace} NSF subtunes; source contains {metadata.SongCount}.";
        song.Comment += $" NSF trace import: {song.OrderList.Count} order slots, {song.Patterns.Count} phrase patterns, {song.Tracks.Count} chip lanes.";
    }

    /// <summary>
    /// Returns the display color for a tracker lane imported from an NSF chip voice.
    /// </summary>
    private static uint NsfLaneColor(int channel)
    {
        uint[] palette =
        [
            0xFFFFB000, 0xFFFF7A00, 0xFF39D9C8, 0xFFE040FB,
            0xFF8BC34A, 0xFF4DA3FF, 0xFF9C6CFF, 0xFFFF5C8A,
            0xFF89F7FE, 0xFF66A6FF, 0xFFB967FF, 0xFFFF71CE,
            0xFF01CDFE, 0xFF05FFA1, 0xFFFFFB96, 0xFFFF9F1C
        ];
        return palette[Math.Abs(channel) % palette.Length];
    }

    /// <summary>
    /// Maps an NSF trace lane to the closest built-in editable synth waveform.
    /// </summary>
    private static SynthWaveform NsfLaneWaveform(int channel)
    {
        return channel switch
        {
            0 or 1 or 5 or 6 or 14 or 15 or >= 24 and <= 26 => SynthWaveform.Square,
            2 => SynthWaveform.Triangle,
            3 or 4 => SynthWaveform.Noise,
            7 or 27 => SynthWaveform.Saw,
            >= 8 and <= 13 => SynthWaveform.Triangle,
            >= 16 and <= 23 => SynthWaveform.Saw,
            _ => SynthWaveform.Square
        };
    }

    /// <summary>
    /// Names the raw register family represented by an NSF trace lane.
    /// </summary>
    private static string NsfLaneEffectSummary(int channel)
    {
        return channel switch
        {
            < 5 => "2A03 regs",
            < 8 => "VRC6 regs",
            < 14 => "VRC7 regs",
            < 16 => "MMC5 regs",
            < 24 => "N163 regs",
            < 27 => "S5B regs",
            _ => "FDS regs"
        };
    }

    /// <summary>
    /// Executes the ImportSidStructure operation.
    /// </summary>
    private static void ImportSidStructure(Song song, byte[] data, ChipTuneMetadata metadata)
    {
        uint[] colors = [0xFF39D9C8, 0xFFFFB000, 0xFFE040FB, 0xFF8BC34A];
        SynthWaveform[] waves = [SynthWaveform.Square, SynthWaveform.Triangle, SynthWaveform.Saw];
        for (int voice = 0; voice < 3; voice++)
        {
            song.Instruments.Add(new Instrument
            {
                Name = $"SID Voice {voice + 1}",
                SourceType = InstrumentSourceType.Synth,
                Waveform = waves[voice],
                PulseWidth = 0.5,
                NoteColor = colors[voice]
            });
            song.Tracks.Add(new Track
            {
                Name = $"SID Voice {voice + 1}",
                InstrumentIndex = voice,
                Volume = 128,
                Panning = (byte)(voice == 0 ? 72 : voice == 1 ? 128 : 184),
                Color = colors[voice],
                EffectSummary = "SID regs"
            });
        }
        song.Instruments.Add(new Instrument
        {
            Name = "SID Filter / D418",
            SourceType = InstrumentSourceType.Synth,
            Waveform = SynthWaveform.Noise,
            NoteColor = colors[3]
        });
        song.Tracks.Add(new Track
        {
            Name = "SID Filter / D418",
            InstrumentIndex = 3,
            Volume = 96,
            Panning = 128,
            Color = colors[3],
            EffectSummary = "SID ctl"
        });

        const int rowsPerPattern = 64;
        int traceRows = EstimateSidTraceRows(song, metadata, rowsPerPattern);
        int subtunesToTrace = Math.Clamp(metadata.SongCount, 1, 8);
        double beats = rowsPerPattern / (double)Math.Max(song.RowsPerBeat, 1);
        for (int subtune = 1; subtune <= subtunesToTrace; subtune++)
        {
            int sequenceStart = song.OrderList.Count;
            int patternCount = Math.Max(1, (int)Math.Ceiling(traceRows / (double)rowsPerPattern));
            var patterns = Enumerable.Range(0, patternCount)
                .Select(i =>
                {
                    int rows = i == patternCount - 1
                        ? Math.Max(1, traceRows - i * rowsPerPattern)
                        : rowsPerPattern;
                    return new Pattern(rows, 4) { Name = $"SID S{subtune:00} Phrase {i:00}" };
                })
                .ToArray();
            try
            {
                var rows = InternalChipRenderer.InspectSidVoiceRows(data, traceRows, subtune);
                if (CountPlayableSidRows(rows) == 0)
                    rows = BuildFallbackSidRows(data, traceRows, subtune);
                var previousControl = new SidControlState();
                foreach (var row in rows)
                {
                    int patternIndex = row.Row / rowsPerPattern;
                    int patternRow = row.Row % rowsPerPattern;
                    if ((uint)patternIndex >= (uint)patterns.Length ||
                        (uint)patternRow >= (uint)patterns[patternIndex].RowCount ||
                        (uint)row.Voice >= 3)
                        continue;

                    var note = new Note
                    {
                        Pitch = row.Pitch,
                        InstrumentIndex = (byte)(row.Voice + 1),
                        Volume = row.Volume,
                        DurationTicks = row.DurationRows,
                        Effect = DecodeSidEffect(row),
                        EffectParam = row.Control,
                        VolumeColumn = row.Waveform,
                        EffectColumn = (byte)(row.PulseWidth >> 4),
                        Panning = row.FilterModeVolume
                    };
                    patterns[patternIndex].SetNote(patternRow, row.Voice, note);
                    var control = new SidControlState(row.FilterRouting, row.FilterModeVolume, row.FilterCutoff);
                    if (!control.Equals(previousControl))
                    {
                        patterns[patternIndex].SetNote(patternRow, 3, new Note
                        {
                            Pitch = 36,
                            InstrumentIndex = 4,
                            Volume = (byte)Math.Clamp((row.FilterModeVolume & 0x0F) * 4, 0, 64),
                            DurationTicks = 1,
                            Effect = EffectCommand.SetGlobalVol,
                            EffectParam = row.FilterModeVolume,
                            EffectColumn = (byte)(row.FilterCutoff >> 3),
                            VolumeColumn = row.FilterRouting,
                            Panning = 128
                        });
                        previousControl = control;
                    }

                    int endRow = Math.Min(row.Row + Math.Max(1, row.DurationRows), traceRows - 1);
                    if (row.Pitch is > 0 and < (byte)SpecialNote.NoteOff && endRow > row.Row)
                    {
                        int offPattern = endRow / rowsPerPattern;
                        int offRow = endRow % rowsPerPattern;
                        if ((uint)offPattern < (uint)patterns.Length && (uint)offRow < (uint)patterns[offPattern].RowCount)
                            patterns[offPattern].SetNote(offRow, row.Voice, new Note
                            {
                                Pitch = (byte)SpecialNote.NoteOff,
                                InstrumentIndex = (byte)(row.Voice + 1),
                                Effect = EffectCommand.None,
                                EffectParam = row.FilterModeVolume,
                                VolumeColumn = row.SustainRelease
                            });
                    }
                }
            }
            catch
            {
                // Header import must still work for difficult RSID tunes. Playback/render can report the detailed failure.
                foreach (var row in BuildFallbackSidRows(data, traceRows, subtune))
                {
                    int patternIndex = row.Row / rowsPerPattern;
                    int patternRow = row.Row % rowsPerPattern;
                    if ((uint)patternIndex >= (uint)patterns.Length ||
                        (uint)patternRow >= (uint)patterns[patternIndex].RowCount ||
                        (uint)row.Voice >= 3)
                        continue;

                    patterns[patternIndex].SetNote(patternRow, row.Voice, new Note
                    {
                        Pitch = row.Pitch,
                        InstrumentIndex = (byte)(row.Voice + 1),
                        Volume = row.Volume,
                        DurationTicks = row.DurationRows,
                        Effect = DecodeSidEffect(row),
                        EffectParam = row.Control,
                        VolumeColumn = row.Waveform,
                        EffectColumn = (byte)(row.PulseWidth >> 4),
                        Panning = row.FilterModeVolume
                    });
                }
            }

            var patternMap = new Dictionary<string, int>(StringComparer.Ordinal);
            int activePatternCount = metadata.DurationSeconds > 0
                ? patterns.Length
                : Math.Clamp(DetectSidSequenceLength(patterns), 1, patterns.Length);
            for (int sequence = 0; sequence < activePatternCount; sequence++)
            {
                var pattern = patterns[sequence];
                string signature = BuildPatternSignature(pattern);
                if (!patternMap.TryGetValue(signature, out int patternIndex))
                {
                    patternIndex = song.Patterns.Count;
                    pattern.Name = $"SID S{subtune:00} Phrase {patternMap.Count:00}";
                    song.Patterns.Add(pattern);
                    patternMap.Add(signature, patternIndex);
                }

                song.OrderList.Add(patternIndex);
                double startBeat = (sequenceStart + sequence) * beats;
                for (int track = 0; track < song.Tracks.Count; track++)
                {
                    song.Tracks[track].Blocks.Add(new PatternBlock
                    {
                        PatternIndex = patternIndex,
                        StartBeat = startBeat,
                        DurationBeats = beats
                    });
                }
            }
        }

        if (metadata.SongCount > subtunesToTrace)
        {
            song.Comment += $" Traced first {subtunesToTrace} subtunes; source contains {metadata.SongCount}.";
        }
        song.Comment += $" SID trace import: {song.OrderList.Count} order slots, {song.Patterns.Count} unique phrase patterns, {song.Tracks.Count} lanes, {traceRows} traced rows.";
    }

    /// <summary>
    /// Executes the EstimateSidTraceRows operation.
    /// </summary>
    private static int EstimateSidTraceRows(Song song, ChipTuneMetadata metadata, int rowsPerPattern)
    {
        double rowsPerSecond = Math.Max(1.0, (song.Bpm / 60.0) * Math.Max(song.RowsPerBeat, 1));
        int seconds = metadata.DurationSeconds > 0 ? metadata.DurationSeconds : metadata.ProgramLength switch
        {
            <= 2048 => 72,
            <= 4096 => 96,
            <= 8192 => 128,
            _ => InternalChipRenderer.DefaultRenderSeconds
        };

        if (metadata.DurationSeconds <= 0 && metadata.SongCount > 1)
            seconds = Math.Min(seconds, 96);

        int rows = (int)Math.Ceiling(seconds * rowsPerSecond);
        return Math.Clamp(rows, rowsPerPattern * 1, rowsPerPattern * 128);
    }

    /// <summary>
    /// Executes the CountPlayableSidRows operation.
    /// </summary>
    private static int CountPlayableSidRows(IEnumerable<SidVoiceRow> rows)
    {
        int count = 0;
        foreach (var row in rows)
        {
            if (row.Pitch is > 0 and < (byte)SpecialNote.NoteOff && (row.Control & 0x01) != 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the CountActiveSidPatterns operation.
    /// </summary>
    private static int CountActiveSidPatterns(IReadOnlyList<Pattern> patterns)
    {
        int active = 0;
        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            bool hasContent = pattern.Notes.Any(note =>
                note.Pitch != 0 ||
                note.InstrumentIndex != 0 ||
                note.Volume != 255 ||
                note.VolumeColumn != 0 ||
                note.Effect != EffectCommand.None ||
                note.EffectColumn != 0 ||
                note.EffectParam != 0);
            if (hasContent)
                active = i + 1;
        }

        return active;
    }

    /// <summary>
    /// Executes the DetectSidSequenceLength operation.
    /// </summary>
    private static int DetectSidSequenceLength(IReadOnlyList<Pattern> patterns)
    {
        int active = Math.Clamp(CountActiveSidPatterns(patterns), 1, patterns.Count);
        var signatures = new string[active];
        for (int i = 0; i < active; i++)
            signatures[i] = BuildMusicalPatternSignature(patterns[i]);

        const int minimumPatterns = 4;
        for (int current = minimumPatterns; current < active; current++)
        {
            for (int previous = 0; previous <= current - minimumPatterns; previous++)
            {
                if (!string.Equals(signatures[current], signatures[previous], StringComparison.Ordinal))
                    continue;

                int matched = 1;
                while (current + matched < active &&
                       previous + matched < current &&
                       string.Equals(signatures[current + matched], signatures[previous + matched], StringComparison.Ordinal))
                {
                    matched++;
                }

                if (matched >= 2 || current + 1 >= active)
                    return current;
            }
        }

        return active;
    }

    /// <summary>
    /// Executes the BuildFallbackSidRows operation.
    /// </summary>
    private static IReadOnlyList<SidVoiceRow> BuildFallbackSidRows(byte[] data, int traceRows, int subtune)
    {
        var rows = new List<SidVoiceRow>();
        int payloadStart = data.Length > 0x7 ? Math.Clamp(ReadBigEndianWord(data, 6), 0, data.Length) : 0;
        if (payloadStart <= 0 || payloadStart >= data.Length)
            payloadStart = Math.Min(0x7E, data.Length);

        int payloadLength = Math.Max(1, data.Length - payloadStart);
        for (int row = 0; row < traceRows; row += 8)
        {
            for (int voice = 0; voice < 3; voice++)
            {
                int idx = payloadStart + ((row * 5 + voice * 37 + subtune * 53) % payloadLength);
                byte a = data[idx];
                byte b = data[payloadStart + ((idx - payloadStart + 17) % payloadLength)];
                byte waveform = voice switch
                {
                    0 => (byte)0x40,
                    1 => (byte)0x10,
                    _ => (byte)0x20
                };
                byte pitch = (byte)Math.Clamp(36 + ((a + b + voice * 7) % 48), 24, 96);
                byte control = (byte)(waveform | 0x01);
                rows.Add(new SidVoiceRow(
                    row + voice * 2,
                    voice,
                    pitch,
                    (byte)(36 + (a & 0x0F)),
                    control,
                    waveform,
                    6,
                    (ushort)((a << 8) | b),
                    (ushort)(0x0800 + ((b & 0x3F) << 4)),
                    0,
                    0,
                    0,
                    (byte)(0x0F | ((b & 0x07) << 4)),
                    (ushort)((a & 0x7F) << 4)));
            }
        }

        return rows;
    }

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct SidControlState(byte FilterRouting, byte FilterModeVolume, ushort FilterCutoff);

    /// <summary>
    /// Executes the BuildPatternSignature operation.
    /// </summary>
    private static string BuildPatternSignature(Pattern pattern)
    {
        var builder = new StringBuilder(pattern.RowCount * pattern.ChannelCount * 12);
        for (int row = 0; row < pattern.RowCount; row++)
        {
            for (int channel = 0; channel < pattern.ChannelCount; channel++)
            {
                var note = pattern.GetNote(row, channel);
                builder
                    .Append(note.Pitch).Append(',')
                    .Append(note.InstrumentIndex).Append(',')
                    .Append(note.Volume).Append(',')
                    .Append(note.VolumeColumn).Append(',')
                    .Append((byte)note.Effect).Append(',')
                    .Append(note.EffectColumn).Append(',')
                    .Append(note.EffectParam).Append(',')
                    .Append(note.Panning).Append(';');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Executes the BuildMusicalPatternSignature operation.
    /// </summary>
    private static string BuildMusicalPatternSignature(Pattern pattern)
    {
        var builder = new StringBuilder(pattern.RowCount * 24);
        for (int row = 0; row < pattern.RowCount; row++)
        {
            for (int channel = 0; channel < Math.Min(pattern.ChannelCount, 3); channel++)
            {
                var note = pattern.GetNote(row, channel);
                if (note.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                {
                    builder
                        .Append(note.Pitch).Append(':')
                        .Append(note.InstrumentIndex).Append(':')
                        .Append(note.VolumeColumn & 0xF0).Append('|');
                }
                else if (note.Pitch == (byte)SpecialNote.NoteOff)
                {
                    builder.Append("off|");
                }
                else
                {
                    builder.Append(".|");
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Executes the DecodeSidEffect operation.
    /// </summary>
    private static EffectCommand DecodeSidEffect(SidVoiceRow row)
    {
        if ((row.Control & 0x04) != 0)
            return EffectCommand.Vibrato; // ring-mod flag

        if ((row.Control & 0x02) != 0)
            return EffectCommand.TonePorta; // sync flag

        if (row.FilterRouting != 0 || (row.FilterModeVolume & 0x70) != 0)
            return EffectCommand.SetGlobalVol; // carries raw SID filter/volume context in param columns

        return EffectCommand.None;
    }

    /// <summary>
    /// Estimates how many play-call rows should be traced from an NSF source.
    /// </summary>
    private static int EstimateNsfTraceRows(Song song, ChipTuneMetadata metadata, int rowsPerPattern)
    {
        int baseRows = metadata.ProgramLength switch
        {
            <= 4096 => rowsPerPattern * 4,
            <= 16384 => rowsPerPattern * 8,
            <= 32768 => rowsPerPattern * 12,
            _ => rowsPerPattern * 16
        };

        if (metadata.SongCount > 1)
            baseRows = Math.Min(baseRows, rowsPerPattern * 8);

        return Math.Clamp(baseRows, rowsPerPattern, rowsPerPattern * 64);
    }

    /// <summary>
    /// Builds a deterministic NSF trace fallback when the NSF driver cannot expose usable APU states yet.
    /// </summary>
    private static IReadOnlyList<NsfVoiceRow> BuildFallbackNsfRows(byte[] data, int traceRows, int subtune)
    {
        var rows = new List<NsfVoiceRow>();
        int payloadStart = Math.Min(0x80, data.Length);
        int payloadLength = Math.Max(1, data.Length - payloadStart);
        for (int row = 0; row < traceRows; row += 8)
        {
            for (int voice = 0; voice < 4; voice++)
            {
                int idx = payloadStart + ((row * 11 + voice * 41 + subtune * 59) % payloadLength);
                byte a = data.Length == 0 ? (byte)0 : data[idx];
                byte b = data.Length == 0 ? (byte)0 : data[payloadStart + ((idx - payloadStart + 19) % payloadLength)];
                byte pitch = voice == 3
                    ? (byte)Math.Clamp(36 + (a & 0x0F), 24, 72)
                    : (byte)Math.Clamp(36 + ((a + b + voice * 5) % 48), 24, 96);
                rows.Add(new NsfVoiceRow(
                    row + voice,
                    voice,
                    voice switch
                    {
                        0 => "2A03 Pulse 1",
                        1 => "2A03 Pulse 2",
                        2 => "2A03 Triangle",
                        _ => "2A03 Noise"
                    },
                    pitch,
                    (byte)(32 + (a & 0x1F)),
                    4,
                    voice == 2 ? (byte)0x20 : voice == 3 ? (byte)0x40 : (byte)((a & 0x03) << 4),
                    a,
                    b,
                    "fallback"));
            }
        }

        return rows;
    }

    /// <summary>
    /// Decodes trace metadata into tracker effect labels for NSF chip rows.
    /// </summary>
    private static EffectCommand DecodeNsfEffect(NsfVoiceRow row)
    {
        if (row.Source.Contains("noise", StringComparison.OrdinalIgnoreCase))
            return EffectCommand.RetrigNote;
        if (row.Source.Contains("dpcm", StringComparison.OrdinalIgnoreCase))
            return EffectCommand.SampleOffset;
        if (row.Source.Contains("saw", StringComparison.OrdinalIgnoreCase))
            return EffectCommand.Tremolo;
        if (row.EffectColumn != 0 || row.EffectParam != 0)
            return EffectCommand.SetSpeed;
        return EffectCommand.None;
    }

    /// <summary>
    /// Describes NSF expansion flags from the NSF header.
    /// </summary>
    private static string DescribeNsfExpansion(byte flags)
    {
        if (flags == 0)
            return "2A03";

        var parts = new List<string>(6);
        if ((flags & 0x01) != 0) parts.Add("VRC6");
        if ((flags & 0x02) != 0) parts.Add("VRC7");
        if ((flags & 0x04) != 0) parts.Add("FDS");
        if ((flags & 0x08) != 0) parts.Add("MMC5");
        if ((flags & 0x10) != 0) parts.Add("N163");
        if ((flags & 0x20) != 0) parts.Add("S5B");
        return parts.Count == 0 ? $"0x{flags:X2}" : string.Join("+", parts);
    }

    /// <summary>
    /// Executes the ReadBigEndianWord operation.
    /// </summary>
    private static int ReadBigEndianWord(byte[] data, int offset)
    {
        if (offset + 1 >= data.Length)
            return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    /// <summary>
    /// Executes the ReadLittleEndianWord operation.
    /// </summary>
    private static int ReadLittleEndianWord(byte[] data, int offset)
    {
        if (offset + 1 >= data.Length)
            return 0;
        return data[offset] | (data[offset + 1] << 8);
    }

    /// <summary>
    /// Executes the NormalizeSidEntryAddress operation.
    /// </summary>
    private static int NormalizeSidEntryAddress(int address, int loadAddress)
    {
        if (address == 0)
            return 0;

        return address < 0x0400 && loadAddress >= 0x0400 ? loadAddress + address : address;
    }

    /// <summary>
    /// Executes the TryFindSongLengthSeconds operation.
    /// </summary>
    private static int TryFindSongLengthSeconds(string path, int songNumber)
    {
        if (!UseSongLengthDatabase)
            return 0;

        string? database = FindSongLengthDatabase(path);
        if (database is null)
            return 0;

        string fileName = Path.GetFileName(path);
        string? parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        string? pendingPath = null;
        int fileNameFallbackSeconds = 0;
        foreach (string rawLine in File.ReadLines(database))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(';'))
            {
                pendingPath = line[1..].Trim();
                continue;
            }

            if (pendingPath is null || !PathLooksLikeSid(pendingPath, parent, fileName))
                continue;

            int equals = line.IndexOf('=');
            if (equals < 0)
                continue;

            string[] stamps = line[(equals + 1)..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            int index = Math.Clamp(songNumber, 1, Math.Max(stamps.Length, 1)) - 1;
            if (index >= stamps.Length || !TryParseSongLengthStamp(stamps[index], out int seconds))
                continue;

            if (PathHasParent(pendingPath, parent))
                return seconds;

            fileNameFallbackSeconds = seconds;
        }

        return fileNameFallbackSeconds;
    }

    /// <summary>
    /// Executes the PathLooksLikeSid operation.
    /// </summary>
    private static bool PathLooksLikeSid(string databasePath, string? parent, string fileName)
    {
        string normalized = databasePath.Replace('\\', '/');
        return normalized.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes the PathHasParent operation.
    /// </summary>
    private static bool PathHasParent(string databasePath, string? parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
            return true;

        string normalized = databasePath.Replace('\\', '/');
        return normalized.Contains("/" + parent + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes the TryParseSongLengthStamp operation.
    /// </summary>
    private static bool TryParseSongLengthStamp(string stamp, out int seconds)
    {
        seconds = 0;
        int flags = stamp.IndexOf('(');
        if (flags >= 0)
            stamp = stamp[..flags];
        if (stamp == "-:--")
            return false;

        string[] parts = stamp.Split(':', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int minutes) ||
            !int.TryParse(parts[1], out int secs) ||
            secs is < 0 or > 59)
        {
            return false;
        }

        seconds = Math.Max(1, minutes * 60 + secs);
        return true;
    }

    /// <summary>
    /// Executes the FindSongLengthDatabase operation.
    /// </summary>
    private static string? FindSongLengthDatabase(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        for (int i = 0; i < 8 && directory is not null; i++)
        {
            string candidate = Path.Combine(directory, "c64", "DOCUMENTS", "Songlengths.txt");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(directory, "DOCUMENTS", "Songlengths.txt");
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        string appRoot = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(appRoot); i++)
        {
            string candidate = Path.Combine(appRoot, "Songlengths.txt");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(appRoot, "DOCUMENTS", "Songlengths.txt");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(appRoot, "c64", "DOCUMENTS", "Songlengths.txt");
            if (File.Exists(candidate))
                return candidate;

            appRoot = Directory.GetParent(appRoot)?.FullName ?? string.Empty;
        }

        return null;
    }

    /// <summary>
    /// Executes the ReadFixedAscii operation.
    /// </summary>
    private static string ReadFixedAscii(byte[] data, int offset, int length)
    {
        if (offset >= data.Length)
            return string.Empty;
        int count = Math.Min(length, data.Length - offset);
        int end = offset;
        while (end < offset + count && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
    }
}
