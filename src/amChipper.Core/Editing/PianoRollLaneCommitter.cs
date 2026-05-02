using amChipper.Core.Models;

namespace amChipper.Core.Editing;

/// <summary>
/// Represents the PianoRollLaneCommitter component.
/// </summary>
public static class PianoRollLaneCommitter
{
    /// <summary>
    /// Executes the LoadNotes operation.
    /// </summary>
    public static IReadOnlyList<Note> LoadNotes(
        Pattern pattern,
        int channel,
        int fallbackDuration,
        IDictionary<Note, PianoRollNoteSource>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));
        fallbackDuration = Math.Max(1, fallbackDuration);

        sources?.Clear();
        var notes = new List<Note>();
        for (int row = 0; row < pattern.RowCount; row++)
        {
            var cell = pattern.GetNote(row, channel);
            if (cell.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            int end = FindNoteEnd(pattern, row + 1, channel, fallbackDuration);
            if (end <= row)
                end = Math.Min(pattern.RowCount, row + fallbackDuration);

            var note = cell.Clone();
            note.StartTick = row;
            note.DurationTicks = Math.Max(1, end - row);
            note.Velocity = cell.Volume == 255
                ? (byte)100
                : (byte)Math.Clamp(cell.Volume * 2, 1, 127);
            notes.Add(note);
            sources?.Add(note, CaptureSource(pattern, row, end, channel));
        }

        return notes;
    }

    /// <summary>
    /// Executes the PianoRollNoteSource operation.
    /// </summary>
    public static Dictionary<Note, PianoRollNoteSource> CaptureSources(
        Pattern pattern,
        int channel,
        IEnumerable<Note> notes,
        int fallbackDuration)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));
        fallbackDuration = Math.Max(1, fallbackDuration);

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        foreach (var note in notes)
        {
            int row = (int)Math.Clamp(note.StartTick, 0, Math.Max(pattern.RowCount - 1, 0));
            int end = FindNoteEnd(pattern, row + 1, channel, fallbackDuration);
            sources[note] = CaptureSource(pattern, row, end, channel);
        }

        return sources;
    }

    /// <summary>
    /// Executes the Commit operation.
    /// </summary>
    public static PianoRollCommitResult Commit(
        Pattern pattern,
        int channel,
        IEnumerable<Note> notes,
        IReadOnlyDictionary<Note, PianoRollNoteSource> sources,
        Func<byte> resolveInstrument)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(resolveInstrument);

        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));
        int committed = 0;

        var effectSnapshot = new TrackerEffectState[pattern.RowCount];
        var sourceEffectRows = GetSourceEffectRowsToClear(notes, sources, pattern.RowCount);

        var rowCopies = new Note[pattern.RowCount];
        for (int row = 0; row < pattern.RowCount; row++)
        {
            var source = pattern.GetNote(row, channel);
            effectSnapshot[row] = TrackerEffectState.FromNote(source);
            rowCopies[row] = source.Clone();
            rowCopies[row].Pitch = 0;
            rowCopies[row].InstrumentIndex = 0;
            rowCopies[row].Volume = 255;
            rowCopies[row].Panning = 255;
            rowCopies[row].Velocity = 100;

            if (sourceEffectRows.Contains(row))
            {
                ClearEffects(rowCopies[row]);
            }
            else if (source.VolumeColumn is >= 0x10 and <= 0x50)
            {
                rowCopies[row].VolumeColumn = 0;
            }
        }

        foreach (var note in notes.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch))
        {
            int start = (int)Math.Clamp(note.StartTick, 0, pattern.RowCount - 1);
            int duration = Math.Max(1, (int)note.DurationTicks);
            int end = Math.Min(pattern.RowCount - 1, start + duration);

            var startNote = note.Clone();
            startNote.StartTick = start;
            startNote.DurationTicks = duration;
            startNote.Volume = (byte)Math.Clamp(note.Velocity / 2, 0, 64);
            if (startNote.InstrumentIndex == 0)
                startNote.InstrumentIndex = resolveInstrument();

            if (sources.TryGetValue(note, out var sourceSpan))
                ApplySourceEffects(startNote, sourceSpan, targetOffset: 0, isEnd: false);
            else
                effectSnapshot[start].ApplyTo(startNote);

            rowCopies[start] = startNote;
            committed++;

            if (end > start && end < pattern.RowCount)
            {
                var endNote = rowCopies[end];
                endNote.Pitch = (byte)SpecialNote.NoteOff;
                endNote.InstrumentIndex = startNote.InstrumentIndex;
                if (sourceSpan is not null)
                    ApplySourceEffects(endNote, sourceSpan, targetOffset: duration, isEnd: true);
                rowCopies[end] = endNote;
            }

            if (sourceSpan is not null)
                CopySpanEffects(sourceSpan, start, end, duration, rowCopies);
        }

        for (int row = 0; row < rowCopies.Length; row++)
        {
            if (rowCopies[row].Effect == EffectCommand.None && rowCopies[row].EffectParam == 0 &&
                !sourceEffectRows.Contains(row) &&
                (effectSnapshot[row].Effect != EffectCommand.None || effectSnapshot[row].EffectParam != 0))
            {
                effectSnapshot[row].ApplyMainEffectTo(rowCopies[row]);
            }

            if (rowCopies[row].VolumeColumn == 0 && !sourceEffectRows.Contains(row) && effectSnapshot[row].VolumeColumn != 0)
                rowCopies[row].VolumeColumn = effectSnapshot[row].VolumeColumn;
        }

        for (int row = 0; row < rowCopies.Length; row++)
            pattern.SetNote(row, channel, rowCopies[row]);

        return new PianoRollCommitResult(committed, sourceEffectRows.Count, CountTrackerEffects(pattern, channel));
    }

    /// <summary>
    /// Executes the FindNoteEnd operation.
    /// </summary>
    public static int FindNoteEnd(Pattern pattern, int startRow, int channel, int fallbackDuration)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));

        for (int row = Math.Max(0, startRow); row < pattern.RowCount; row++)
        {
            var cell = pattern.GetNote(row, channel);
            if (cell.Pitch == (byte)SpecialNote.NoteOff || cell.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                return row;
        }

        return Math.Min(pattern.RowCount, startRow + Math.Max(1, fallbackDuration));
    }

    /// <summary>
    /// Executes the CaptureSource operation.
    /// </summary>
    public static PianoRollNoteSource CaptureSource(Pattern pattern, int row, int end, int channel)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));

        var source = new PianoRollNoteSource(row, Math.Max(row, end));
        int lastRow = Math.Clamp(Math.Max(row, end), 0, pattern.RowCount - 1);
        bool ownsTerminalNoteOff = false;
        if (lastRow > row)
        {
            var terminal = pattern.GetNote(lastRow, channel);
            if (terminal.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
            {
                lastRow--;
            }
            else if (terminal.Pitch == (byte)SpecialNote.NoteOff)
            {
                ownsTerminalNoteOff = true;
            }
        }

        for (int effectRow = Math.Clamp(row, 0, pattern.RowCount - 1); effectRow <= lastRow; effectRow++)
        {
            var note = pattern.GetNote(effectRow, channel);
            if (!HasTrackerEffect(note))
                continue;

            source.Effects.Add(new PianoRollEffectCell(
                effectRow - row,
                ownsTerminalNoteOff && effectRow == lastRow,
                note.Effect,
                note.EffectColumn,
                note.EffectParam,
                note.VolumeColumn));
        }

        return source;
    }

    /// <summary>
    /// Executes the HasTrackerEffect operation.
    /// </summary>
    public static bool HasTrackerEffect(Note note) =>
        note.Effect != EffectCommand.None || note.EffectColumn != 0 || note.EffectParam != 0 || note.VolumeColumn != 0;

    /// <summary>
    /// Executes the CountTrackerEffects operation.
    /// </summary>
    public static int CountTrackerEffects(Pattern pattern, int channel)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));

        int count = 0;
        for (int row = 0; row < pattern.RowCount; row++)
        {
            if (HasTrackerEffect(pattern.GetNote(row, channel)))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the ApplySourceEffects operation.
    /// </summary>
    private static void ApplySourceEffects(Note note, PianoRollNoteSource source, int targetOffset, bool isEnd)
    {
        foreach (var effect in source.Effects)
        {
            bool matches = isEnd
                ? effect.IsEnd
                : effect.Offset == targetOffset && !effect.IsEnd;
            if (matches)
                effect.ApplyTo(note);
        }
    }

    /// <summary>
    /// Executes the CopySpanEffects operation.
    /// </summary>
    private static void CopySpanEffects(PianoRollNoteSource source, int start, int end, int duration, Note[] rowCopies)
    {
        foreach (var effect in source.Effects)
        {
            if (effect.Offset == 0 || effect.IsEnd)
                continue;
            if (effect.Offset >= duration)
                continue;

            int target = start + effect.Offset;
            if (target < 0 || target >= rowCopies.Length || target == start || target == end)
                continue;

            effect.ApplyTo(rowCopies[target]);
        }
    }

    /// <summary>
    /// Executes the GetSourceEffectRowsToClear operation.
    /// </summary>
    private static HashSet<int> GetSourceEffectRowsToClear(
        IEnumerable<Note> notes,
        IReadOnlyDictionary<Note, PianoRollNoteSource> sources,
        int rowCount)
    {
        var rows = new HashSet<int>();
        foreach (var note in notes)
        {
            if (!sources.TryGetValue(note, out var source))
                continue;

            int duration = Math.Max(1, (int)note.DurationTicks);
            foreach (var effect in source.Effects)
            {
                bool effectMovesWithEditedSpan = effect.Offset == 0 || effect.IsEnd || effect.Offset < duration;
                if (!effectMovesWithEditedSpan)
                    continue;

                int row = source.Row + effect.Offset;
                if (row >= 0 && row < rowCount)
                    rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>
    /// Executes the ClearEffects operation.
    /// </summary>
    private static void ClearEffects(Note note)
    {
        note.Effect = EffectCommand.None;
        note.EffectColumn = 0;
        note.EffectParam = 0;
        note.VolumeColumn = 0;
    }
}

/// <summary>
/// Represents the PianoRollNoteSource component.
/// </summary>
public sealed class PianoRollNoteSource(int row, int endRow)
{
    /// <summary>
    /// Stores or exposes Row.
    /// </summary>
    public int Row { get; } = row;
    /// <summary>
    /// Stores or exposes EndRow.
    /// </summary>
    public int EndRow { get; } = endRow;
    /// <summary>
    /// Stores or exposes Effects.
    /// </summary>
    public List<PianoRollEffectCell> Effects { get; } = [];
}

/// <summary>
/// Carries struct data.
/// </summary>
public readonly record struct PianoRollEffectCell(
    int Offset,
    bool IsEnd,
    EffectCommand Effect,
    byte EffectColumn,
    byte EffectParam,
    byte VolumeColumn)
{
    /// <summary>
    /// Executes the ApplyTo operation.
    /// </summary>
    public void ApplyTo(Note note)
    {
        note.Effect = Effect;
        note.EffectColumn = EffectColumn;
        note.EffectParam = EffectParam;
        note.VolumeColumn = VolumeColumn;
    }
}

/// <summary>
/// Carries struct data.
/// </summary>
public readonly record struct PianoRollCommitResult(
    int CommittedNotes,
    int SourceEffectRows,
    int EffectsAfter);

/// <summary>
/// Carries struct data.
/// </summary>
internal readonly record struct TrackerEffectState(
    EffectCommand Effect,
    byte EffectColumn,
    byte EffectParam,
    byte VolumeColumn)
{
    /// <summary>
    /// Executes the FromNote operation.
    /// </summary>
    public static TrackerEffectState FromNote(Note note) =>
        new(note.Effect, note.EffectColumn, note.EffectParam, note.VolumeColumn);

    /// <summary>
    /// Executes the ApplyTo operation.
    /// </summary>
    public void ApplyTo(Note note)
    {
        ApplyMainEffectTo(note);
        note.VolumeColumn = VolumeColumn;
    }

    /// <summary>
    /// Executes the ApplyMainEffectTo operation.
    /// </summary>
    public void ApplyMainEffectTo(Note note)
    {
        note.Effect = Effect;
        note.EffectColumn = EffectColumn;
        note.EffectParam = EffectParam;
    }
}
