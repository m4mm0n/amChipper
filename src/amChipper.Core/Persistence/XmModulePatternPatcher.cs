using System.Text;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the XmModulePatternPatcher component.
/// </summary>
public static class XmModulePatternPatcher
{
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    private const string Signature = "Extended Module: ";

    /// <summary>
    /// Executes the TryCreatePatchedModule operation.
    /// </summary>
    public static bool TryCreatePatchedModule(Song song, byte[] originalModule, out byte[] patchedModule)
    {
        patchedModule = [];
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(originalModule);

        if (song.Format != ModuleFormat.XM || originalModule.Length < 80)
            return false;

        if (!TryReadLayout(originalModule, out var layout))
            return false;

        if (layout.Channels <= 0 ||
            layout.Patterns is null ||
            layout.Patterns.Count == 0 ||
            song.Patterns.Count < layout.Patterns.Count ||
            song.Tracks.Count != layout.Channels)
        {
            return false;
        }

        using var stream = new MemoryStream();
        stream.Write(originalModule, 0, layout.PatternStart);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            for (int patternIndex = 0; patternIndex < layout.Patterns.Count; patternIndex++)
            {
                var originalPattern = layout.Patterns[patternIndex];
                if (PatternIsUnchanged(song.Patterns[patternIndex], originalPattern, layout.Channels))
                {
                    stream.Write(originalModule, originalPattern.StartOffset, originalPattern.EndOffset - originalPattern.StartOffset);
                }
                else
                {
                    WritePattern(writer, song.Patterns[patternIndex], originalPattern, layout.Channels);
                }
            }
        }

        stream.Write(originalModule, layout.PatternEnd, originalModule.Length - layout.PatternEnd);
        patchedModule = stream.ToArray();
        return true;
    }

    /// <summary>
    /// Executes the TrySavePatchedModule operation.
    /// </summary>
    public static bool TrySavePatchedModule(Song song, byte[] originalModule, string path)
    {
        if (!TryCreatePatchedModule(song, originalModule, out byte[] patched))
            return false;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, patched);
        return true;
    }

    /// <summary>
    /// Executes the TryGetChangeSummary operation.
    /// </summary>
    public static bool TryGetChangeSummary(Song song, byte[] originalModule, out int changedPatterns, out int changedCells)
    {
        changedPatterns = 0;
        changedCells = 0;
        if (!TryReadLayout(originalModule, out var layout) ||
            layout.Patterns is null ||
            song.Patterns.Count < layout.Patterns.Count)
        {
            return false;
        }

        for (int patternIndex = 0; patternIndex < layout.Patterns.Count; patternIndex++)
        {
            int patternChanges = CountChangedCells(song.Patterns[patternIndex], layout.Patterns[patternIndex], layout.Channels);
            if (patternChanges > 0)
            {
                changedPatterns++;
                changedCells += patternChanges;
            }
        }

        return true;
    }

    /// <summary>
    /// Executes the TryGetFirstChangeDetails operation.
    /// </summary>
    public static bool TryGetFirstChangeDetails(Song song, byte[] originalModule, int maxCount, out IReadOnlyList<string> details)
    {
        details = [];
        if (!TryReadLayout(originalModule, out var layout) ||
            layout.Patterns is null ||
            song.Patterns.Count < layout.Patterns.Count)
        {
            return false;
        }

        var result = new List<string>();
        for (int patternIndex = 0; patternIndex < layout.Patterns.Count && result.Count < maxCount; patternIndex++)
        {
            var pattern = song.Patterns[patternIndex];
            var original = layout.Patterns[patternIndex];
            int rows = Math.Min(Math.Clamp(pattern.RowCount, 1, 256), original.Rows);
            for (int row = 0; row < rows && result.Count < maxCount; row++)
            {
                for (int ch = 0; ch < layout.Channels && result.Count < maxCount; ch++)
                {
                    var note = ch < pattern.ChannelCount ? pattern.GetNote(row, ch) : new Note();
                    XmCell originalCell = row < original.Rows && ch < original.Channels
                        ? original.Cells[row * original.Channels + ch]
                        : new XmCell(0, 0, 0, 0, 0);
                    if (IsSameAsOriginal(note, originalCell))
                        continue;

                    var decoded = FromXmCell(originalCell);
                    result.Add($"p{patternIndex} r{row} c{ch}: cur n{note.Pitch}/i{note.InstrumentIndex} raw n{originalCell.Note}/i{originalCell.Instrument}->dec n{decoded.Pitch}/i{decoded.InstrumentIndex}");
                }
            }
        }

        details = result;
        return true;
    }

    /// <summary>
    /// Executes the TryReadLayout operation.
    /// </summary>
    private static bool TryReadLayout(byte[] data, out XmPatternLayout layout)
    {
        layout = default;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(17));
            if (!signature.Equals(Signature, StringComparison.Ordinal))
                return false;

            stream.Position = 60;
            uint headerSize = reader.ReadUInt32();
            long headerStart = stream.Position - sizeof(uint);
            ushort songLength = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            ushort channels = reader.ReadUInt16();
            ushort patternCount = reader.ReadUInt16();
            _ = reader.ReadUInt16();

            if (songLength == 0 || channels == 0 || patternCount == 0)
                return false;

            long patternStart = headerStart + headerSize;
            if (patternStart <= 0 || patternStart > stream.Length)
                return false;

            stream.Position = patternStart;
            var patterns = new List<XmOriginalPattern>(patternCount);
            for (int pattern = 0; pattern < patternCount; pattern++)
            {
                if (stream.Position + 9 > stream.Length)
                    return false;

                long headerOffset = stream.Position;
                uint patternHeaderLength = reader.ReadUInt32();
                if (patternHeaderLength < 9 || headerOffset + patternHeaderLength > stream.Length)
                    return false;

                _ = reader.ReadByte();
                ushort rows = reader.ReadUInt16();
                ushort packedSize = reader.ReadUInt16();
                stream.Position = headerOffset + patternHeaderLength;
                byte[] packedData = reader.ReadBytes(packedSize);
                if (packedData.Length != packedSize)
                    return false;

                patterns.Add(DecodePattern((int)headerOffset, (int)stream.Position, rows, channels, packedData));
                if (stream.Position > stream.Length)
                    return false;
            }

            layout = new XmPatternLayout((int)patternStart, (int)stream.Position, channels, patterns);
            return true;
        }
        catch
        {
            layout = default;
            return false;
        }
    }

    /// <summary>
    /// Executes the DecodePattern operation.
    /// </summary>
    private static XmOriginalPattern DecodePattern(int startOffset, int endOffset, int rows, int channels, byte[] packedData)
    {
        rows = Math.Clamp(rows, 1, 256);
        channels = Math.Clamp(channels, 1, 64);
        var cells = new XmCell[rows * channels];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new XmCell(0, 0, 0, 0, 0);

        int offset = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                if (offset >= packedData.Length)
                    return new XmOriginalPattern(startOffset, endOffset, rows, channels, cells);

                byte first = packedData[offset++];
                byte note = 0;
                byte instrument = 0;
                byte volume = 0;
                byte effect = 0;
                byte param = 0;

                if ((first & 0x80) != 0)
                {
                    if ((first & 0x01) != 0 && offset < packedData.Length) note = packedData[offset++];
                    if ((first & 0x02) != 0 && offset < packedData.Length) instrument = packedData[offset++];
                    if ((first & 0x04) != 0 && offset < packedData.Length) volume = packedData[offset++];
                    if ((first & 0x08) != 0 && offset < packedData.Length) effect = packedData[offset++];
                    if ((first & 0x10) != 0 && offset < packedData.Length) param = packedData[offset++];
                }
                else
                {
                    note = first;
                    if (offset < packedData.Length) instrument = packedData[offset++];
                    if (offset < packedData.Length) volume = packedData[offset++];
                    if (offset < packedData.Length) effect = packedData[offset++];
                    if (offset < packedData.Length) param = packedData[offset++];
                }

                cells[row * channels + ch] = new XmCell(note, instrument, volume, effect, param);
            }
        }

        return new XmOriginalPattern(startOffset, endOffset, rows, channels, cells);
    }

    /// <summary>
    /// Executes the PatternIsUnchanged operation.
    /// </summary>
    private static bool PatternIsUnchanged(Pattern pattern, XmOriginalPattern original, int channels)
        => CountChangedCells(pattern, original, channels) == 0;

    /// <summary>
    /// Executes the CountChangedCells operation.
    /// </summary>
    private static int CountChangedCells(Pattern pattern, XmOriginalPattern original, int channels)
    {
        int rows = Math.Clamp(pattern.RowCount, 1, 256);
        if (rows != original.Rows)
            return Math.Max(rows, original.Rows) * channels;

        int changes = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                var note = ch < pattern.ChannelCount ? pattern.GetNote(row, ch) : new Note();
                XmCell originalCell = row < original.Rows && ch < original.Channels
                    ? original.Cells[row * original.Channels + ch]
                    : new XmCell(0, 0, 0, 0, 0);

                if (!IsSameAsOriginal(note, originalCell))
                    changes++;
            }
        }

        return changes;
    }

    /// <summary>
    /// Executes the WritePattern operation.
    /// </summary>
    private static void WritePattern(BinaryWriter writer, Pattern pattern, XmOriginalPattern original, int channels)
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
                    XmCell originalCell = row < original.Rows && ch < original.Channels
                        ? original.Cells[row * original.Channels + ch]
                        : new XmCell(0, 0, 0, 0, 0);
                    XmCell currentCell = ToXmCell(note);
                    XmCell outputCell = IsSameAsOriginal(note, originalCell) ? originalCell : currentCell;
                    patternWriter.Write(outputCell.Note);
                    patternWriter.Write(outputCell.Instrument);
                    patternWriter.Write(outputCell.Volume);
                    patternWriter.Write(outputCell.Effect);
                    patternWriter.Write(outputCell.Param);
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
    /// Executes the IsSameAsOriginal operation.
    /// </summary>
    private static bool IsSameAsOriginal(Note current, XmCell original)
    {
        Note decoded = FromXmCell(original);
        return current.Pitch == decoded.Pitch &&
               current.InstrumentIndex == decoded.InstrumentIndex;
    }

    /// <summary>
    /// Executes the FromXmCell operation.
    /// </summary>
    private static Note FromXmCell(XmCell cell)
    {
        var note = new Note
        {
            Pitch = cell.Note switch
            {
                0 => (byte)0,
                97 => (byte)SpecialNote.NoteOff,
                _ => (byte)Math.Clamp(cell.Note + 23, 0, 127)
            },
            InstrumentIndex = cell.Instrument,
            VolumeColumn = cell.Volume,
            EffectColumn = cell.Effect,
            EffectParam = cell.Param
        };

        if (note.VolumeColumn is >= 0x10 and <= 0x50)
            note.Volume = (byte)(note.VolumeColumn - 0x10);

        return note;
    }

    /// <summary>
    /// Executes the ToXmCell operation.
    /// </summary>
    private static XmCell ToXmCell(Note note)
    {
        byte effect = ToXmEffect(note, out byte param);
        return new XmCell(ToXmNote(note), note.InstrumentIndex, ToXmVolume(note), effect, param);
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
    /// Carries struct data.
    /// </summary>
    private readonly record struct XmCell(byte Note, byte Instrument, byte Volume, byte Effect, byte Param);

    /// <summary>
    /// Carries XmOriginalPattern data.
    /// </summary>
    private sealed record XmOriginalPattern(int StartOffset, int EndOffset, int Rows, int Channels, XmCell[] Cells);

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct XmPatternLayout(int PatternStart, int PatternEnd, int Channels, IReadOnlyList<XmOriginalPattern>? Patterns);
}
