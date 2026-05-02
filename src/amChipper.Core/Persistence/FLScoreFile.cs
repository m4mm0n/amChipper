using System.Buffers.Binary;
using System.Text;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the FLScoreFile component.
/// </summary>
public static class FLScoreFile
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int PulsesPerBeat = 96;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int LegacyRecordLength = 20;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int ModernRecordLength = 24;
    /// <summary>
    /// Stores or exposes HeaderPayload.
    /// </summary>
    private static readonly byte[] HeaderPayload = [0x10, 0x00, 0x05, 0x00, 0x60, 0x00];
    /// <summary>
    /// Executes the DataPrefix operation.
    /// </summary>
    private static readonly byte[] DataPrefix = [0xC7, 0x06, (byte)'3', (byte)'.', (byte)'0', (byte)'.', (byte)'0', 0x00, 0x41, 0x00, 0x00, 0xE0, 0x50];

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
        int rowsPerBeat = Math.Max(song.RowsPerBeat, 1);

        using var data = new MemoryStream();
        data.Write(DataPrefix);
        byte[] record = new byte[LegacyRecordLength];

        for (int row = 0; row < pattern.RowCount; row++)
        {
            var note = pattern.GetNote(row, channel);
            if (note.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            Array.Clear(record);
            int startRow = note.StartTick > 0 ? (int)note.StartTick : row;
            int durationRows = note.DurationTicks > 0
                ? (int)note.DurationTicks
                : FindNoteDuration(pattern, row, channel, rowsPerBeat);
            int start = RowToPulse(startRow, rowsPerBeat);
            int duration = RowToPulse(Math.Max(1, durationRows), rowsPerBeat);
            byte velocity = note.Volume <= 64
                ? (byte)Math.Clamp(note.Volume * 2, 1, 127)
                : note.Velocity;

            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), start);
            record[5] = 0x40;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8, 4), duration);
            record[12] = note.Pitch;
            record[16] = 0x40;
            record[17] = velocity;
            record[18] = note.Panning < 255 ? note.Panning : (byte)0x80;
            record[19] = 0x50;
            data.Write(record);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var file = File.Create(path);
        WriteAscii(file, "FLhd");
        WriteLE32(file, HeaderPayload.Length);
        file.Write(HeaderPayload);
        WriteAscii(file, "FLdt");
        WriteLE32(file, (int)data.Length);
        data.Position = 0;
        data.CopyTo(file);
    }

    /// <summary>
    /// Executes the ImportPatternChannel operation.
    /// </summary>
    public static IReadOnlyList<Note> ImportPatternChannel(string path, int rowsPerBeat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        rowsPerBeat = Math.Clamp(rowsPerBeat, 1, 32);

        byte[] bytes = File.ReadAllBytes(path);
        int dataOffset = FindChunk(bytes, "FLdt", out int dataLength);
        if (dataOffset < 0)
            throw new InvalidDataException("Not an FL Studio score file.");

        int dataEnd = Math.Min(bytes.Length, dataOffset + dataLength);
        var layout = FindRecordLayout(bytes, dataOffset, dataEnd);
        var notes = new List<Note>();

        for (int offset = layout.StartOffset; offset + layout.RecordLength <= dataEnd; offset += layout.RecordLength)
        {
            int startPulse = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            int durationPulse = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
            byte pitch = bytes[offset + 12];
            if (pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            byte velocity = bytes[offset + layout.VelocityOffset];
            notes.Add(new Note
            {
                Pitch = pitch,
                InstrumentIndex = 1,
                Velocity = velocity == 0 ? (byte)100 : velocity,
                Volume = (byte)Math.Clamp((velocity == 0 ? 100 : velocity) / 2, 0, 64),
                Panning = bytes[offset + layout.PanningOffset],
                StartTick = PulseToRow(startPulse, rowsPerBeat),
                DurationTicks = Math.Max(1, PulseToRow(Math.Max(durationPulse, 1), rowsPerBeat))
            });
        }

        return notes.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).ToArray();
    }

    /// <summary>
    /// Executes the FindRecordLayout operation.
    /// </summary>
    private static FscRecordLayout FindRecordLayout(byte[] bytes, int dataOffset, int dataEnd)
    {
        ReadOnlySpan<byte> version = "3.0.0\0"u8;
        int prefixSearchEnd = Math.Min(dataEnd, dataOffset + 64);
        for (int i = dataOffset; i + version.Length <= prefixSearchEnd; i++)
        {
            if (!bytes.AsSpan(i, version.Length).SequenceEqual(version))
                continue;

            int afterPrefix = i + version.Length + 5;
            if (TryCreateLayout(bytes, afterPrefix, dataEnd, LegacyRecordLength, out var legacyLayout))
                return legacyLayout;
        }

        FscRecordLayout best = default;
        int bestScore = -1;
        for (int i = dataOffset; i < Math.Min(dataEnd, dataOffset + 96); i++)
        {
            foreach (int recordLength in new[] { ModernRecordLength, LegacyRecordLength })
            {
                if (!TryCreateLayout(bytes, i, dataEnd, recordLength, out var layout))
                    continue;

                int score = ScoreLayout(bytes, layout, dataEnd);
                if (score > bestScore)
                {
                    best = layout;
                    bestScore = score;
                }
            }
        }

        if (bestScore >= 0)
            return best;

        return new FscRecordLayout(dataOffset, LegacyRecordLength);
    }

    /// <summary>
    /// Executes the TryCreateLayout operation.
    /// </summary>
    private static bool TryCreateLayout(byte[] bytes, int startOffset, int dataEnd, int recordLength, out FscRecordLayout layout)
    {
        layout = default;
        if (startOffset < 0 || startOffset + recordLength > dataEnd || (dataEnd - startOffset) % recordLength != 0)
            return false;

        layout = new FscRecordLayout(startOffset, recordLength);
        return LooksLikeRecord(bytes, layout, startOffset, dataEnd);
    }

    /// <summary>
    /// Executes the LooksLikeRecord operation.
    /// </summary>
    private static bool LooksLikeRecord(byte[] bytes, FscRecordLayout layout, int offset, int dataEnd)
    {
        if (offset + layout.RecordLength > dataEnd)
            return false;

        int startPulse = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
        int durationPulse = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
        byte velocity = bytes[offset + layout.VelocityOffset];
        byte realPitch = bytes[offset + 12];
        return startPulse >= 0 &&
               startPulse <= PulsesPerBeat * 1024 &&
               durationPulse is > 0 and <= PulsesPerBeat * 1024 &&
               bytes[offset + 5] == 0x40 &&
               realPitch is > 0 and < (byte)SpecialNote.NoteOff &&
               velocity <= 127;
    }

    /// <summary>
    /// Executes the ScoreLayout operation.
    /// </summary>
    private static int ScoreLayout(byte[] bytes, FscRecordLayout layout, int dataEnd)
    {
        int score = 0;
        int records = 0;
        for (int offset = layout.StartOffset; offset + layout.RecordLength <= dataEnd; offset += layout.RecordLength)
        {
            records++;
            if (LooksLikeRecord(bytes, layout, offset, dataEnd))
                score += 8;
            if (bytes[offset + 5] == 0x40)
                score += 2;
            if (bytes[offset + layout.VelocityOffset] <= 127)
                score += 2;
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 8, 4)) > 0)
                score++;
        }

        return records > 0 ? score : -1;
    }

    /// <summary>
    /// Executes the FindChunk operation.
    /// </summary>
    private static int FindChunk(byte[] bytes, string id, out int length)
    {
        length = 0;
        ReadOnlySpan<byte> chunkId = Encoding.ASCII.GetBytes(id);
        for (int i = 0; i + 8 <= bytes.Length; i++)
        {
            if (!bytes.AsSpan(i, 4).SequenceEqual(chunkId))
                continue;

            length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i + 4, 4));
            return i + 8;
        }

        return -1;
    }

    /// <summary>
    /// Executes the RowToPulse operation.
    /// </summary>
    private static int RowToPulse(int row, int rowsPerBeat) =>
        (int)Math.Round(row * (PulsesPerBeat / (double)Math.Max(rowsPerBeat, 1)));

    /// <summary>
    /// Executes the PulseToRow operation.
    /// </summary>
    private static int PulseToRow(int pulse, int rowsPerBeat) =>
        (int)Math.Round(pulse * (Math.Max(rowsPerBeat, 1) / (double)PulsesPerBeat));

    /// <summary>
    /// Executes the FindNoteDuration operation.
    /// </summary>
    private static int FindNoteDuration(Pattern pattern, int startRow, int channel, int fallbackRows)
    {
        for (int row = startRow + 1; row < pattern.RowCount; row++)
        {
            var cell = pattern.GetNote(row, channel);
            if (cell.Pitch == (byte)SpecialNote.NoteOff || cell.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                return Math.Max(1, row - startRow);
        }

        return Math.Max(1, Math.Min(fallbackRows, pattern.RowCount - startRow));
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
    /// Executes the WriteLE32 operation.
    /// </summary>
    private static void WriteLE32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct FscRecordLayout(int StartOffset, int RecordLength)
    {
        /// <summary>
        /// Stores or exposes VelocityOffset.
        /// </summary>
        public int VelocityOffset => RecordLength == ModernRecordLength ? 21 : 17;
        /// <summary>
        /// Stores or exposes PanningOffset.
        /// </summary>
        public int PanningOffset => RecordLength == ModernRecordLength ? 22 : 18;
    }
}
