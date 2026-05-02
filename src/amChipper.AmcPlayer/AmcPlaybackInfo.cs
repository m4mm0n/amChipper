namespace amChipper.AmcPlayer;

/// <summary>
/// Describes a loaded AMC module without exposing the mutable song object.
/// </summary>
public sealed record AmcPlaybackInfo(
    string Title,
    string Artist,
    string ContainerFormat,
    string EmbeddedSourceFormat,
    string EmbeddedSourceExtension,
    int Channels,
    int Patterns,
    int Orders,
    int Instruments,
    int Bpm,
    int RowsPerBeat,
    double DurationSeconds);
