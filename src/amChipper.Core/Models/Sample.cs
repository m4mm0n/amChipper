namespace amChipper.Core.Models;

/// <summary>
/// A PCM audio sample that can be assigned to an instrument.
/// Supports 8-bit and 16-bit mono/stereo, with loop points.
/// </summary>
public sealed class Sample
{
    /// <summary>
    /// Stores or exposes Name.
    /// </summary>
    public string Name { get; set; } = "Untitled Sample";

    /// <summary>Raw PCM audio data (interleaved if stereo).</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Stores or exposes SampleRate.
    /// </summary>
    public int SampleRate { get; set; } = 44100;
    /// <summary>
    /// Stores or exposes Channels.
    /// </summary>
    public int Channels { get; set; } = 1;

    /// <summary>Bits per sample: 8 or 16.</summary>
    public int BitsPerSample { get; set; } = 16;

    // ── Loop ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Looped.
    /// </summary>
    public bool Looped { get; set; }
    /// <summary>
    /// Stores or exposes PingPongLoop.
    /// </summary>
    public bool PingPongLoop { get; set; }
    /// <summary>
    /// Stores or exposes LoopStart.
    /// </summary>
    public int LoopStart { get; set; }

    /// <summary>Exclusive end of loop (in sample frames).</summary>
    public int LoopEnd { get; set; }

    // ── Tuning ────────────────────────────────────────────────────────────────

    /// <summary>Base MIDI note for this sample (default C-4 = 60).</summary>
    public byte BaseNote { get; set; } = 60;

    /// <summary>Fine-tune in cents (-100 to +100).</summary>
    public int FineTune { get; set; }

    /// <summary>Relative volume 0-255.</summary>
    public byte RelativeVolume { get; set; } = 255;

    /// <summary>Panning 0-255 (128 = centre, 255 = not set).</summary>
    public byte RelativePanning { get; set; } = 255;

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>Total number of sample frames (not bytes).</summary>
    public int FrameCount => BitsPerSample == 16
        ? Data.Length / 2 / Channels
        : Data.Length / Channels;

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Sample Clone()
    {
        var s = (Sample)MemberwiseClone();
        s.Data = (byte[])Data.Clone();
        return s;
    }
}
