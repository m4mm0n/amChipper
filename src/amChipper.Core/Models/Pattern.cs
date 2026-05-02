namespace amChipper.Core.Models;

/// <summary>
/// A pattern holds a 2D grid of Note cells: [row, channel].
/// Rows = pattern length in ticks; channels = number of tracker channels.
/// </summary>
public sealed class Pattern
{
    /// <summary>
    /// Stores or exposes Name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Number of rows (ticks) in the pattern (typically 32-256).</summary>
    public int RowCount { get; set; } = 64;

    /// <summary>Number of tracker channels (columns) in the pattern.</summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Flat array of notes: index = row * ChannelCount + channel.
    /// A default-constructed Note represents an empty cell.
    /// </summary>
    public Note[] Notes { get; set; }

    public Pattern() : this(64, 8)
    {
    }

    public Pattern(int rowCount, int channelCount)
    {
        RowCount = rowCount;
        ChannelCount = channelCount;
        Notes = new Note[rowCount * channelCount];
        for (int i = 0; i < Notes.Length; i++)
            Notes[i] = new Note();
    }

    /// <summary>
    /// Executes the GetNote operation.
    /// </summary>
    public Note GetNote(int row, int channel) => Notes[row * ChannelCount + channel];
    /// <summary>
    /// Executes the SetNote operation.
    /// </summary>
    public void SetNote(int row, int channel, Note note) => Notes[row * ChannelCount + channel] = note;

    /// <summary>
    /// Executes the EnsureStorage operation.
    /// </summary>
    public void EnsureStorage()
    {
        RowCount = Math.Max(1, RowCount);
        ChannelCount = Math.Max(1, ChannelCount);

        int expected = RowCount * ChannelCount;
        if (Notes is null || Notes.Length != expected)
        {
            var resized = new Note[expected];
            for (int i = 0; i < resized.Length; i++)
                resized[i] = Notes is not null && i < Notes.Length ? Notes[i] ?? new Note() : new Note();
            Notes = resized;
        }

        for (int i = 0; i < Notes.Length; i++)
            Notes[i] ??= new Note();
    }

    /// <summary>Resize the pattern, preserving existing data as much as possible.</summary>
    public void Resize(int newRowCount, int newChannelCount)
    {
        if (Notes is null || Notes.Length != RowCount * ChannelCount)
            EnsureStorage();

        var newNotes = new Note[newRowCount * newChannelCount];
        for (int i = 0; i < newNotes.Length; i++) newNotes[i] = new Note();

        int copyRows = Math.Min(RowCount, newRowCount);
        int copyCh = Math.Min(ChannelCount, newChannelCount);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCh; c++)
                newNotes[r * newChannelCount + c] = Notes![r * ChannelCount + c].Clone();

        RowCount = newRowCount;
        ChannelCount = newChannelCount;
        Notes = newNotes;
    }

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Pattern Clone()
    {
        var p = new Pattern(RowCount, ChannelCount);
        p.Name = Name;
        for (int i = 0; i < Notes.Length; i++)
            p.Notes[i] = Notes[i].Clone();
        return p;
    }
}
