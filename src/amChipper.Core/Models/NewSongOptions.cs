namespace amChipper.Core.Models;

/// <summary>
/// Represents the NewSongOptions component.
/// </summary>
public sealed class NewSongOptions
{
    /// <summary>
    /// Stores or exposes Title.
    /// </summary>
    public string Title { get; set; } = "New Song";
    /// <summary>
    /// Stores or exposes Format.
    /// </summary>
    public ModuleFormat Format { get; set; } = ModuleFormat.IT;
    /// <summary>
    /// Stores or exposes Channels.
    /// </summary>
    public int Channels { get; set; } = 8;
    /// <summary>
    /// Stores or exposes Patterns.
    /// </summary>
    public int Patterns { get; set; } = 4;
    /// <summary>
    /// Stores or exposes RowsPerPattern.
    /// </summary>
    public int RowsPerPattern { get; set; } = 64;
    /// <summary>
    /// Stores or exposes RowsPerBeat.
    /// </summary>
    public int RowsPerBeat { get; set; } = 4;
    /// <summary>
    /// Stores or exposes Bpm.
    /// </summary>
    public int Bpm { get; set; } = 125;
    /// <summary>
    /// Stores or exposes InitialSpeed.
    /// </summary>
    public int InitialSpeed { get; set; } = 6;
    /// <summary>
    /// Stores or exposes IncludeDefaultSamples.
    /// </summary>
    public bool IncludeDefaultSamples { get; set; } = true;
    /// <summary>
    /// Stores or exposes CreatePlaylistBlocks.
    /// </summary>
    public bool CreatePlaylistBlocks { get; set; } = true;

    /// <summary>
    /// Executes the Default operation.
    /// </summary>
    public static NewSongOptions Default => new();

    /// <summary>
    /// Executes the Normalize operation.
    /// </summary>
    public NewSongOptions Normalize()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "New Song" : Title.Trim();
        Channels = Math.Clamp(Channels, 1, 64);
        Patterns = Math.Clamp(Patterns, 1, 256);
        RowsPerPattern = Math.Clamp(RowsPerPattern, 1, 512);
        RowsPerBeat = Math.Clamp(RowsPerBeat, 1, 32);
        Bpm = Math.Clamp(Bpm, 6, 999);
        InitialSpeed = Math.Clamp(InitialSpeed, 1, 31);
        return this;
    }
}
