using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the MidiFile component.
/// </summary>
public static class MidiFile
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    public const int PulsesPerQuarter = 480;

    /// <summary>
    /// Executes the ExportPatternChannel operation.
    /// </summary>
    public static void ExportPatternChannel(Song song, int patternIndex, int channel, string path)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SongProjectSerializer.Normalize(song);

        var pattern = song.Patterns[Math.Clamp(patternIndex, 0, Math.Max(song.Patterns.Count - 1, 0))];
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));

        using var track = BuildPatternChannelTrack(song, pattern, channel);

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var file = File.Create(path);
        WriteAscii(file, "MThd");
        WriteBE32(file, 6);
        WriteBE16(file, 0);
        WriteBE16(file, 1);
        WriteBE16(file, PulsesPerQuarter);
        WriteAscii(file, "MTrk");
        WriteBE32(file, (int)track.Length);
        track.Position = 0;
        track.CopyTo(file);
    }

    /// <summary>
    /// Executes the ExportPatternChannels operation.
    /// </summary>
    public static void ExportPatternChannels(Song song, int patternIndex, IReadOnlyCollection<int> channels, string path)
    {
        ExportPatternsChannels(song, [patternIndex], channels, path);
    }

    /// <summary>
    /// Executes the ExportPatternsChannels operation.
    /// </summary>
    public static void ExportPatternsChannels(Song song, IReadOnlyCollection<int> patternIndices, IReadOnlyCollection<int> channels, string path)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(patternIndices);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SongProjectSerializer.Normalize(song);

        var selectedPatterns = patternIndices
            .Select(patternIndex => Math.Clamp(patternIndex, 0, Math.Max(song.Patterns.Count - 1, 0)))
            .Distinct()
            .ToArray();
        if (selectedPatterns.Length == 0)
            throw new InvalidOperationException("At least one MIDI export pattern must be selected.");

        int maxChannels = selectedPatterns
            .Select(patternIndex => song.Patterns[patternIndex].ChannelCount)
            .DefaultIfEmpty(1)
            .Max();
        var selectedChannels = channels
            .Select(channel => Math.Clamp(channel, 0, Math.Max(maxChannels - 1, 0)))
            .Distinct()
            .Order()
            .ToArray();
        if (selectedChannels.Length == 0)
            throw new InvalidOperationException("At least one MIDI export channel must be selected.");

        if (selectedPatterns.Length == 1 && selectedChannels.Length == 1)
        {
            ExportPatternChannel(song, selectedPatterns[0], selectedChannels[0], path);
            return;
        }

        using var tempoTrack = new MemoryStream();
        WriteMetaTempo(tempoTrack, 0, song.Bpm);
        WriteEndOfTrack(tempoTrack);

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var file = File.Create(path);
        WriteAscii(file, "MThd");
        WriteBE32(file, 6);
        WriteBE16(file, 1);
        WriteBE16(file, selectedChannels.Length + 1);
        WriteBE16(file, PulsesPerQuarter);
        WriteTrackChunk(file, tempoTrack);

        foreach (int channel in selectedChannels)
        {
            using var track = BuildPatternChannelTrack(song, selectedPatterns, channel, includeTempo: false, midiChannel: channel % 16, trackName: $"Ch {channel + 1}");
            WriteTrackChunk(file, track);
        }
    }

    /// <summary>
    /// Executes the BuildPatternChannelTrack operation.
    /// </summary>
    private static MemoryStream BuildPatternChannelTrack(
        Song song,
        IReadOnlyCollection<int> patternIndices,
        int channel,
        bool includeTempo = true,
        int midiChannel = 0,
        string? trackName = null)
    {
        var patterns = patternIndices
            .Select(patternIndex => song.Patterns[Math.Clamp(patternIndex, 0, Math.Max(song.Patterns.Count - 1, 0))])
            .ToArray();
        return BuildPatternChannelTrack(song, patterns, channel, includeTempo, midiChannel, trackName);
    }

    /// <summary>
    /// Executes the BuildPatternChannelTrack operation.
    /// </summary>
    private static MemoryStream BuildPatternChannelTrack(
        Song song,
        Pattern pattern,
        int channel,
        bool includeTempo = true,
        int midiChannel = 0,
        string? trackName = null)
    {
        return BuildPatternChannelTrack(song, [pattern], channel, includeTempo, midiChannel, trackName);
    }

    /// <summary>
    /// Executes the BuildPatternChannelTrack operation.
    /// </summary>
    private static MemoryStream BuildPatternChannelTrack(
        Song song,
        IReadOnlyList<Pattern> patterns,
        int channel,
        bool includeTempo = true,
        int midiChannel = 0,
        string? trackName = null)
    {
        var track = new MemoryStream();
        if (includeTempo)
            WriteMetaTempo(track, 0, song.Bpm);
        if (!string.IsNullOrWhiteSpace(trackName))
            WriteTrackName(track, 0, trackName);

        var events = new List<MidiEvent>();
        int rowsPerBeat = Math.Max(song.RowsPerBeat, 1);
        int rowOffset = 0;
        for (int patternSlot = 0; patternSlot < patterns.Count; patternSlot++)
        {
            var pattern = patterns[patternSlot];
            if (channel >= pattern.ChannelCount)
            {
                rowOffset += pattern.RowCount;
                continue;
            }

            for (int row = 0; row < pattern.RowCount; row++)
            {
                var note = pattern.GetNote(row, channel);
                if (note.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                    continue;

                int start = RowToMidiTick(rowOffset + row, rowsPerBeat);
                int durationRows = ResolveExportDurationRows(patterns, patternSlot, row, channel, note);
                int endRow = rowOffset + row + durationRows;
                int end = RowToMidiTick(endRow, rowsPerBeat);
                byte velocity = note.Volume == 255
                    ? note.Velocity
                    : (byte)Math.Clamp(note.Volume * 2, 1, 127);

                events.Add(new MidiEvent(start, 0x90 | midiChannel, note.Pitch, velocity));
                events.Add(new MidiEvent(Math.Max(start + 1, end), 0x80 | midiChannel, note.Pitch, 0));
            }

            rowOffset += pattern.RowCount;
        }

        int previousTick = 0;
        foreach (var ev in events.OrderBy(e => e.Tick).ThenBy(e => e.Status))
        {
            WriteVar(track, ev.Tick - previousTick);
            previousTick = ev.Tick;
            track.WriteByte((byte)ev.Status);
            track.WriteByte(ev.Data1);
            track.WriteByte(ev.Data2);
        }

        WriteEndOfTrack(track);
        track.Position = 0;
        return track;
    }

    /// <summary>
    /// Executes the ResolveExportDurationRows operation.
    /// </summary>
    private static int ResolveExportDurationRows(IReadOnlyList<Pattern> patterns, int patternSlot, int startRow, int channel, Note note)
    {
        int explicitDuration = (int)note.DurationTicks;
        if (explicitDuration > 1)
            return explicitDuration;

        int inferred = FindNextTerminatorDistance(patterns, patternSlot, startRow, channel);
        return inferred > 0 ? inferred : Math.Max(1, explicitDuration);
    }

    /// <summary>
    /// Executes the FindNextTerminatorDistance operation.
    /// </summary>
    private static int FindNextTerminatorDistance(IReadOnlyList<Pattern> patterns, int patternSlot, int startRow, int channel)
    {
        int distance = 0;
        for (int slot = patternSlot; slot < patterns.Count; slot++)
        {
            var pattern = patterns[slot];
            if (channel >= pattern.ChannelCount)
            {
                distance += pattern.RowCount;
                continue;
            }

            int rowStart = slot == patternSlot ? startRow + 1 : 0;
            for (int row = rowStart; row < pattern.RowCount; row++)
            {
                var cell = pattern.GetNote(row, channel);
                if (cell.Pitch == (byte)SpecialNote.NoteOff || cell.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                    return distance + row - (slot == patternSlot ? startRow : 0);
            }

            distance += slot == patternSlot ? pattern.RowCount - startRow : pattern.RowCount;
        }

        return 0;
    }

    /// <summary>
    /// Executes the ImportPatternChannel operation.
    /// </summary>
    public static IReadOnlyList<Note> ImportPatternChannel(string path, int rowsPerBeat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        rowsPerBeat = Math.Clamp(rowsPerBeat, 1, 32);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (ReadAscii(reader, 4) != "MThd")
            throw new InvalidDataException("Not a MIDI file.");

        int headerLength = ReadBE32(reader);
        int format = ReadBE16(reader);
        int trackCount = ReadBE16(reader);
        int division = ReadBE16(reader);
        if ((division & 0x8000) != 0)
            throw new InvalidDataException("SMPTE MIDI timing is not supported.");

        if (headerLength > 6)
            stream.Position += headerLength - 6;

        var notes = new List<Note>();
        for (int track = 0; track < trackCount; track++)
        {
            long chunkPosition = stream.Position;
            string chunkId = ReadAscii(reader, 4);
            if (chunkId != "MTrk")
                throw new InvalidDataException($"MIDI track chunk is missing at track {track}, offset {chunkPosition}.");

            int trackLength = ReadBE32(reader);
            long trackEnd = stream.Position + trackLength;
            ParseTrack(reader, trackEnd, division, rowsPerBeat, notes);
            stream.Position = trackEnd;

            if (format == 0)
                break;
        }

        return notes.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).ToArray();
    }

    /// <summary>
    /// Executes the ParseTrack operation.
    /// </summary>
    private static void ParseTrack(BinaryReader reader, long trackEnd, int division, int rowsPerBeat, List<Note> notes)
    {
        int absoluteTick = 0;
        int runningStatus = 0;
        var active = new Dictionary<(int Channel, byte Pitch), Queue<(int Tick, byte Velocity)>>();

        while (reader.BaseStream.Position < trackEnd)
        {
            absoluteTick += ReadVar(reader);
            int status = reader.ReadByte();
            if (status < 0x80)
            {
                if (runningStatus == 0)
                    throw new InvalidDataException("MIDI running status without prior status.");
                reader.BaseStream.Position--;
                status = runningStatus;
            }
            else if (status < 0xF0)
            {
                runningStatus = status;
            }

            if (status == 0xFF)
            {
                _ = reader.ReadByte();
                int len = ReadVar(reader);
                reader.BaseStream.Position += len;
                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                int len = ReadVar(reader);
                reader.BaseStream.Position += len;
                continue;
            }

            int command = status & 0xF0;
            int channel = status & 0x0F;
            int data1 = reader.ReadByte();
            int data2 = command is 0xC0 or 0xD0 ? 0 : reader.ReadByte();

            if (command == 0x90 && data2 > 0)
            {
                var key = (channel, (byte)data1);
                if (!active.TryGetValue(key, out var queue))
                    active[key] = queue = new Queue<(int Tick, byte Velocity)>();
                queue.Enqueue((absoluteTick, (byte)Math.Clamp(data2, 1, 127)));
            }
            else if (command == 0x80 || command == 0x90)
            {
                var key = (channel, (byte)data1);
                if (!active.TryGetValue(key, out var queue) || queue.Count == 0)
                    continue;

                var start = queue.Dequeue();
                int startRow = MidiTickToRow(start.Tick, division, rowsPerBeat);
                int endRow = Math.Max(startRow + 1, MidiTickToRow(absoluteTick, division, rowsPerBeat));
                notes.Add(new Note
                {
                    Pitch = key.Item2,
                    InstrumentIndex = 1,
                    Velocity = start.Velocity,
                    Volume = (byte)Math.Clamp(start.Velocity / 2, 0, 64),
                    StartTick = startRow,
                    DurationTicks = endRow - startRow
                });
            }
        }
    }

    /// <summary>
    /// Executes the RowToMidiTick operation.
    /// </summary>
    private static int RowToMidiTick(int row, int rowsPerBeat) =>
        (int)Math.Round(row * (PulsesPerQuarter / (double)rowsPerBeat));

    /// <summary>
    /// Executes the MidiTickToRow operation.
    /// </summary>
    private static int MidiTickToRow(int tick, int division, int rowsPerBeat) =>
        (int)Math.Round(tick * (rowsPerBeat / (double)Math.Max(division, 1)));

    /// <summary>
    /// Executes the WriteMetaTempo operation.
    /// </summary>
    private static void WriteMetaTempo(Stream stream, int delta, int bpm)
    {
        int micros = 60_000_000 / Math.Clamp(bpm, 6, 999);
        WriteVar(stream, delta);
        stream.WriteByte(0xFF);
        stream.WriteByte(0x51);
        stream.WriteByte(3);
        stream.WriteByte((byte)((micros >> 16) & 0xFF));
        stream.WriteByte((byte)((micros >> 8) & 0xFF));
        stream.WriteByte((byte)(micros & 0xFF));
    }

    /// <summary>
    /// Executes the WriteTrackName operation.
    /// </summary>
    private static void WriteTrackName(Stream stream, int delta, string name)
    {
        WriteVar(stream, delta);
        stream.WriteByte(0xFF);
        stream.WriteByte(0x03);
        WriteVar(stream, name.Length);
        WriteAscii(stream, name);
    }

    /// <summary>
    /// Executes the WriteEndOfTrack operation.
    /// </summary>
    private static void WriteEndOfTrack(Stream stream)
    {
        WriteVar(stream, 0);
        stream.WriteByte(0xFF);
        stream.WriteByte(0x2F);
        stream.WriteByte(0);
    }

    /// <summary>
    /// Executes the WriteTrackChunk operation.
    /// </summary>
    private static void WriteTrackChunk(Stream file, MemoryStream track)
    {
        WriteAscii(file, "MTrk");
        WriteBE32(file, (int)track.Length);
        track.Position = 0;
        track.CopyTo(file);
    }

    /// <summary>
    /// Executes the WriteVar operation.
    /// </summary>
    private static void WriteVar(Stream stream, int value)
    {
        value = Math.Max(0, value);
        Span<byte> bytes = stackalloc byte[5];
        int count = 0;
        bytes[count++] = (byte)(value & 0x7F);
        while ((value >>= 7) > 0)
            bytes[count++] = (byte)((value & 0x7F) | 0x80);

        for (int i = count - 1; i >= 0; i--)
            stream.WriteByte(bytes[i]);
    }

    /// <summary>
    /// Executes the ReadVar operation.
    /// </summary>
    private static int ReadVar(BinaryReader reader)
    {
        int value = 0;
        byte b;
        do
        {
            b = reader.ReadByte();
            value = (value << 7) | (b & 0x7F);
        }
        while ((b & 0x80) != 0);

        return value;
    }

    /// <summary>
    /// Executes the WriteAscii operation.
    /// </summary>
    private static void WriteAscii(Stream stream, string text)
    {
        foreach (char ch in text)
            stream.WriteByte((byte)ch);
    }

    /// <summary>
    /// Executes the ReadAscii operation.
    /// </summary>
    private static string ReadAscii(BinaryReader reader, int length) =>
        System.Text.Encoding.ASCII.GetString(reader.ReadBytes(length));

    /// <summary>
    /// Executes the WriteBE16 operation.
    /// </summary>
    private static void WriteBE16(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>
    /// Executes the WriteBE32 operation.
    /// </summary>
    private static void WriteBE32(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>
    /// Executes the ReadBE16 operation.
    /// </summary>
    private static int ReadBE16(BinaryReader reader) =>
        (reader.ReadByte() << 8) | reader.ReadByte();

    /// <summary>
    /// Executes the ReadBE32 operation.
    /// </summary>
    private static int ReadBE32(BinaryReader reader) =>
        (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();

    /// <summary>
    /// Carries MidiEvent data.
    /// </summary>
    private sealed record MidiEvent(int Tick, int Status, byte Data1, byte Data2);
}
