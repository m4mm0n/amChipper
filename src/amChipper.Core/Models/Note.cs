namespace amChipper.Core.Models;

/// <summary>
/// A single note event inside a tracker pattern row / piano-roll clip.
/// MIDI pitch 0 = C-1, 60 = C4 (Middle C).
/// </summary>
public sealed class Note
{
    // ── Tracker fields ────────────────────────────────────────────────────────

    /// <summary>MIDI note number 0-127, or SpecialNote values (NoteOff, NoteFade).</summary>
    public byte Pitch { get; set; }

    /// <summary>1-based instrument index (0 = none).</summary>
    public byte InstrumentIndex { get; set; }

    /// <summary>Volume 0-64 (0x40 = max). 255 = not set (use instrument default).</summary>
    public byte Volume { get; set; } = 255;

    /// <summary>Raw XM volume column byte. 0 = empty / derive from Volume.</summary>
    public byte VolumeColumn { get; set; }

    /// <summary>Panning 0-255 (128 = centre). 255 = not set.</summary>
    public byte Panning { get; set; } = 255;

    /// <summary>Effect command byte.</summary>
    public EffectCommand Effect { get; set; }

    /// <summary>Raw XM main effect-column command byte. 0 can still be arpeggio when EffectParam is set.</summary>
    public byte EffectColumn { get; set; }

    /// <summary>Effect parameter byte (high nibble = x, low nibble = y).</summary>
    public byte EffectParam { get; set; }

    // ── Piano-roll fields ─────────────────────────────────────────────────────

    /// <summary>Start position in ticks (piano-roll / internal sequencer).</summary>
    public long StartTick { get; set; }

    /// <summary>Duration in ticks.</summary>
    public long DurationTicks { get; set; }

    /// <summary>MIDI-style velocity 0-127 (used in piano-roll / sample playback mode).</summary>
    public byte Velocity { get; set; } = 100;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Human-readable note name like "C-4", "F#5".</summary>
    public string NoteName => PitchToName(Pitch);

    /// <summary>
    /// Executes the PitchToName operation.
    /// </summary>
    public static string PitchToName(byte pitch)
    {
        if (pitch == (byte)SpecialNote.NoteOff) return "===";
        if (pitch == (byte)SpecialNote.NoteFade) return "~~~";
        if (pitch == (byte)SpecialNote.None) return "---";
        string[] names = ["C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-"];
        int octave = pitch / 12 - 1;
        return $"{names[pitch % 12]}{octave}";
    }

    /// <summary>Parse a note name string like "C-4" or "F#5" into a MIDI pitch number.</summary>
    public static bool TryParseName(string name, out byte pitch)
    {
        pitch = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim().ToUpperInvariant();
        if (name is "===" or "OFF") { pitch = (byte)SpecialNote.NoteOff; return true; }
        if (name is "~~~" or "FAD") { pitch = (byte)SpecialNote.NoteFade; return true; }
        if (name is "---") { pitch = (byte)SpecialNote.None; return true; }

        string[] names = ["C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-"];
        if (name.Length < 3) return false;
        string noteStr = name[..2];
        if (!int.TryParse(name[2..], out int octave)) return false;
        int noteIdx = Array.IndexOf(names, noteStr);
        if (noteIdx < 0) return false;
        int p = (octave + 1) * 12 + noteIdx;
        if (p is < 0 or > 127) return false;
        pitch = (byte)p;
        return true;
    }

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Note Clone() => (Note)MemberwiseClone();
}
