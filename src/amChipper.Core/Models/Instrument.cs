namespace amChipper.Core.Models;

/// <summary>
/// An instrument definition.  In tracker mode this maps notes to samples
/// via a KeyMap and may carry envelope data.  In piano-roll mode it is the
/// primary sound source for a Track.
/// </summary>
public sealed class Instrument
{
    /// <summary>
    /// Stores or exposes Name.
    /// </summary>
    public string Name { get; set; } = "Untitled";

    // ── Source ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes SourceType.
    /// </summary>
    public InstrumentSourceType SourceType { get; set; } = InstrumentSourceType.Synth;
    /// <summary>
    /// Stores or exposes Waveform.
    /// </summary>
    public SynthWaveform Waveform { get; set; } = SynthWaveform.Square;

    /// <summary>Duty cycle for square/pulse instruments, 0.05-0.95.</summary>
    public double PulseWidth { get; set; } = 0.5;

    /// <summary>Simple synth attack time in milliseconds.</summary>
    public int AttackMs { get; set; } = 2;

    /// <summary>Simple synth release time in milliseconds.</summary>
    public int ReleaseMs { get; set; } = 25;

    /// <summary>Root MIDI note used for sample tuning and keyboard display.</summary>
    public byte RootNote { get; set; } = 60;

    /// <summary>Fine tune in cents for native amChipper playback.</summary>
    public int FineTuneCents { get; set; }

    /// <summary>Maximum simultaneous voices for this instrument. 0 means unlimited by instrument.</summary>
    public int MaxPolyphony { get; set; }

    /// <summary>Forces monophonic playback for this instrument in the native engine.</summary>
    public bool Mono { get; set; }

    /// <summary>Enables glide toward the next note for this instrument.</summary>
    public bool Porta { get; set; }

    /// <summary>Portamento time in milliseconds for native playback.</summary>
    public int PortaTimeMs { get; set; } = 80;

    /// <summary>Delay before the synth envelope reaches attack, in milliseconds.</summary>
    public int DelayMs { get; set; }

    /// <summary>Hold time after attack before release behavior starts, in milliseconds.</summary>
    public int HoldMs { get; set; }

    /// <summary>Decay time after hold, in milliseconds.</summary>
    public int DecayMs { get; set; } = 80;

    /// <summary>Sustain level 0-128 for native synth playback.</summary>
    public byte SustainLevel { get; set; } = 96;

    /// <summary>LFO amount in semitones for native synth pitch modulation.</summary>
    public double LfoAmount { get; set; }

    /// <summary>LFO speed in Hz for native synth pitch modulation.</summary>
    public double LfoSpeedHz { get; set; } = 5.0;

    /// <summary>Arpeggiator range in semitones for native amChipper instruments.</summary>
    public int ArpRange { get; set; }

    /// <summary>Arpeggiator repeat step count for native amChipper instruments.</summary>
    public int ArpRepeat { get; set; } = 1;

    /// <summary>Delay/echo feedback amount 0-1 for future native AMC rendering.</summary>
    public double EchoFeedback { get; set; }

    /// <summary>Delay/echo time in milliseconds for future native AMC rendering.</summary>
    public int EchoTimeMs { get; set; } = 180;

    /// <summary>Low-pass filter cutoff 0-1 for future native AMC rendering.</summary>
    public double FilterCutoff { get; set; } = 1.0;

    /// <summary>Low-pass filter resonance 0-1 for future native AMC rendering.</summary>
    public double FilterResonance { get; set; }

    // ── Samples ───────────────────────────────────────────────────────────────

    /// <summary>The sample pool for this instrument (max 99 per IT spec).</summary>
    public List<Sample> Samples { get; set; } = [];

    /// <summary>
    /// Note-to-sample mapping: index = MIDI pitch (0-127),
    /// value = 0-based index into Samples list (255 = none).
    /// </summary>
    public byte[] NoteMap { get; set; } = Enumerable.Repeat((byte)255, 128).ToArray();

    // ── Volume Envelope ───────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes VolumeEnvelopeEnabled.
    /// </summary>
    public bool VolumeEnvelopeEnabled { get; set; }
    /// <summary>
    /// Stores or exposes VolumeEnvelope.
    /// </summary>
    public List<EnvelopePoint> VolumeEnvelope { get; set; } = [];
    /// <summary>
    /// Stores or exposes VolEnvSustainPoint.
    /// </summary>
    public int VolEnvSustainPoint { get; set; } = -1;
    /// <summary>
    /// Stores or exposes VolEnvLoopStart.
    /// </summary>
    public int VolEnvLoopStart { get; set; }
    /// <summary>
    /// Stores or exposes VolEnvLoopEnd.
    /// </summary>
    public int VolEnvLoopEnd { get; set; }

    // ── Panning Envelope ──────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes PanEnvelopeEnabled.
    /// </summary>
    public bool PanEnvelopeEnabled { get; set; }
    /// <summary>
    /// Stores or exposes PanEnvelope.
    /// </summary>
    public List<EnvelopePoint> PanEnvelope { get; set; } = [];

    // ── Global Settings ───────────────────────────────────────────────────────

    /// <summary>Global volume 0-128.</summary>
    public byte GlobalVolume { get; set; } = 128;

    /// <summary>Fade-out speed 0-255 (IT-style).</summary>
    public int FadeOut { get; set; }

    /// <summary>New Note Action: 0=cut, 1=continue, 2=note-off, 3=fade.</summary>
    public byte NewNoteAction { get; set; }

    // ── Piano-roll colour ─────────────────────────────────────────────────────

    /// <summary>ARGB colour used to paint notes in the piano roll and playlist.</summary>
    public uint NoteColor { get; set; } = 0xFF3A7BD5;

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Instrument Clone()
    {
        var inst = (Instrument)MemberwiseClone();
        inst.Samples = Samples.Select(s => s.Clone()).ToList();
        inst.NoteMap = (byte[])NoteMap.Clone();
        inst.VolumeEnvelope = VolumeEnvelope.Select(p => p.Clone()).ToList();
        inst.PanEnvelope = PanEnvelope.Select(p => p.Clone()).ToList();
        return inst;
    }
}

/// <summary>
/// Represents the EnvelopePoint component.
/// </summary>
public sealed class EnvelopePoint
{
    /// <summary>Tick position.</summary>
    public int Tick { get; set; }

    /// <summary>Value 0-64 for volume, -32..+32 for panning.</summary>
    public int Value { get; set; }

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public EnvelopePoint Clone() => (EnvelopePoint)MemberwiseClone();
}
