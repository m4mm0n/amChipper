namespace amChipper.AmcPlayer;

/// <summary>
/// Configures the standalone AMC playback renderer.
/// </summary>
public sealed class AmcPlaybackOptions
{
    /// <summary>Output sample rate for rendered stereo float PCM.</summary>
    public int SampleRate { get; init; } = 44100;

    /// <summary>Master gain applied after voice mixing.</summary>
    public float MasterGain { get; init; } = 0.55f;

    /// <summary>Maximum number of simultaneous voices retained by the lightweight renderer.</summary>
    public int MaxVoices { get; init; } = 128;

    /// <summary>Default note length, in rows, when the note does not carry piano-roll duration data.</summary>
    public int DefaultNoteRows { get; init; } = 1;

    /// <summary>Normalizes settings to values the renderer can safely use.</summary>
    public AmcPlaybackOptions Normalize() => new()
    {
        SampleRate = Math.Clamp(SampleRate, 8000, 192000),
        MasterGain = Math.Clamp(MasterGain, 0f, 4f),
        MaxVoices = Math.Clamp(MaxVoices, 1, 512),
        DefaultNoteRows = Math.Clamp(DefaultNoteRows, 1, 64)
    };
}
