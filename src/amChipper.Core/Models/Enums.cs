namespace amChipper.Core.Models;

/// <summary>Supported module file formats.</summary>
public enum ModuleFormat
{
    Unknown,
    MOD,   // ProTracker / compatible
    XM,    // FastTracker II Extended Module
    IT,    // Impulse Tracker
    S3M,   // ScreamTracker 3
    AmChip, // Native editable amChipper chip module
    OpenMpt, // Any additional tracker/chiptune module accepted by libopenmpt
    SID,   // Commodore 64 SID/PSID/RSID tune
    NSF    // Nintendo Sound Format tune
}

/// <summary>Player transport state.</summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Recording
}

/// <summary>Scope used when starting transport playback.</summary>
public enum PlaybackScope
{
    Song,
    Pattern,
    PianoRoll
}

/// <summary>Piano-roll note editing tool mode.</summary>
public enum PianoRollTool
{
    Draw,
    Select,
    Erase,
    Pan
}

/// <summary>Primary sound source used by an instrument.</summary>
public enum InstrumentSourceType
{
    Synth,
    Sample
}

/// <summary>Simple chiptune oscillator waveforms for native amChipper songs.</summary>
public enum SynthWaveform
{
    Square,
    Triangle,
    Saw,
    Noise
}

/// <summary>Song-editor (playlist) edit tool mode.</summary>
public enum SongEditorTool
{
    Draw,
    Select,
    Erase,
    Mute
}

/// <summary>Automation target used by song-level track lanes.</summary>
public enum AutomationTarget
{
    Volume,
    Pan
}

/// <summary>Tracker effect column commands (MOD/XM compatible subset).</summary>
public enum EffectCommand : byte
{
    None = 0x00, // also used for Arpeggio (0xy) when param != 0
    PortaUp = 0x01, // 1xx
    PortaDown = 0x02, // 2xx
    TonePorta = 0x03, // 3xx
    Vibrato = 0x04, // 4xy
    VolSlide = 0x05, // 5xy (porta+volslide)
    PortaVolSlide = 0x06, // 6xy (vibrato+volslide - reuse)
    Tremolo = 0x07, // 7xy
    SetPan = 0x08, // 8xx
    SampleOffset = 0x09, // 9xx
    VolumeSlide = 0x0A, // Axy
    PosJump = 0x0B, // Bxx
    SetVolume = 0x0C, // Cxx
    PatternBreak = 0x0D, // Dxx
    SetSpeed = 0x0F, // Fxx (<=0x20 = speed, else = BPM)
    SetGlobalVol = 0x10, // XM Gxx
    SetBpm = 0x1D, // XM Txx
    FinePortaUp = 0x12, // XM E1x
    FinePortaDown = 0x13, // XM E2x
    NoteDelay = 0x14, // XM EDx
    NoteCut = 0x15, // XM ECx
    RetrigNote = 0x19, // XM R0x
}

/// <summary>Note special values (no note, note off, note fade).</summary>
public enum SpecialNote : byte
{
    None = 0,
    NoteOff = 254,
    NoteFade = 255
}

/// <summary>Interpolation quality used by the audio engine renderer.</summary>
public enum InterpolationMode
{
    None,
    Linear,
    Sinc
}
