using System.ComponentModel;

namespace amChipper.Core.Models;

/// <summary>
/// A track in the Song Editor / playlist.
/// A Track is a horizontal lane that references pattern blocks placed on a timeline.
/// In tracker mode each track also maps to one or more pattern channels.
/// </summary>
public sealed class Track : INotifyPropertyChanged
{
    /// <summary>
    /// Raised when PropertyChangedEventHandler changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Executes the SetField operation.
    /// </summary>
    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Stores or exposes _name.
    /// </summary>
    private string _name = "Track";
    /// <summary>
    /// Executes the Name operation.
    /// </summary>
    public string Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>
    /// Stores or exposes _muted.
    /// </summary>
    private bool _muted;
    /// <summary>
    /// Executes the Muted operation.
    /// </summary>
    public bool Muted { get => _muted; set => SetField(ref _muted, value); }

    /// <summary>
    /// Stores or exposes _solo.
    /// </summary>
    private bool _solo;
    /// <summary>
    /// Executes the Solo operation.
    /// </summary>
    public bool Solo { get => _solo; set => SetField(ref _solo, value); }

    /// <summary>Track volume 0-128 (128 = 100%).</summary>
    private byte _volume = 128;
    /// <summary>
    /// Executes the Volume operation.
    /// </summary>
    public byte Volume { get => _volume; set => SetField(ref _volume, value); }

    /// <summary>Track panning 0-255 (128 = centre).</summary>
    private byte _panning = 128;
    /// <summary>
    /// Executes the Panning operation.
    /// </summary>
    public byte Panning { get => _panning; set => SetField(ref _panning, value); }

    /// <summary>Index of the instrument assigned to this track (0-based).</summary>
    private int _instrumentIndex;
    /// <summary>
    /// Executes the InstrumentIndex operation.
    /// </summary>
    public int InstrumentIndex { get => _instrumentIndex; set => SetField(ref _instrumentIndex, value); }

    /// <summary>Default pitch used by the step sequencer / channel rack.</summary>
    private byte _stepPitch = 60;
    /// <summary>
    /// Executes the StepPitch operation.
    /// </summary>
    public byte StepPitch { get => _stepPitch; set => SetField(ref _stepPitch, value); }

    /// <summary>ARGB colour of this track's lane in the song editor.</summary>
    private uint _color = 0xFF2979FF;
    /// <summary>
    /// Executes the Color operation.
    /// </summary>
    public uint Color { get => _color; set => SetField(ref _color, value); }

    /// <summary>Recent output level in the mixer, 0-1.</summary>
    private double _meterLevel;
    /// <summary>
    /// Executes the MeterLevel operation.
    /// </summary>
    public double MeterLevel { get => _meterLevel; set => SetField(ref _meterLevel, Math.Clamp(value, 0d, 1d)); }

    /// <summary>
    /// Stores or exposes _effectSummary.
    /// </summary>
    private string _effectSummary = "FX --";
    /// <summary>
    /// Executes the EffectSummary operation.
    /// </summary>
    public string EffectSummary { get => _effectSummary; set => SetField(ref _effectSummary, value); }

    /// <summary>Volume automation points in beats.</summary>
    public List<AutomationPoint> VolumeAutomation { get; set; } = [];

    /// <summary>Pan automation points in beats.</summary>
    public List<AutomationPoint> PanAutomation { get; set; } = [];

    /// <summary>Pattern blocks placed on this track's timeline lane.</summary>
    public List<PatternBlock> Blocks { get; set; } = [];

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Track Clone()
    {
        return new Track
        {
            Name = Name,
            Muted = Muted,
            Solo = Solo,
            Volume = Volume,
            Panning = Panning,
            InstrumentIndex = InstrumentIndex,
            StepPitch = StepPitch,
            Color = Color,
            EffectSummary = EffectSummary,
            VolumeAutomation = VolumeAutomation.Select(p => p.Clone()).ToList(),
            PanAutomation = PanAutomation.Select(p => p.Clone()).ToList(),
            Blocks = Blocks.Select(b => b.Clone()).ToList()
        };
    }
}

/// <summary>
/// A reference to a Pattern placed at a specific beat position on a Track lane.
/// </summary>
public sealed class PatternBlock
{
    /// <summary>0-based index into Song.Patterns.</summary>
    public int PatternIndex { get; set; }

    /// <summary>Start position in beats (quarter notes from song start).</summary>
    public double StartBeat { get; set; }

    /// <summary>Duration in beats (typically the pattern's row count / rows-per-beat).</summary>
    public double DurationBeats { get; set; }

    /// <summary>Per-clip volume in the range 0-128.</summary>
    public byte Volume { get; set; } = 128;

    /// <summary>Per-clip pan in the range 0-255.</summary>
    public byte Panning { get; set; } = 128;

    /// <summary>Whether this specific block instance is muted.</summary>
    public bool Muted { get; set; }

    /// <summary>Per-clip volume automation points in beats relative to the block start.</summary>
    public List<AutomationPoint> VolumeAutomation { get; set; } = [];

    /// <summary>Per-clip pan automation points in beats relative to the block start.</summary>
    public List<AutomationPoint> PanAutomation { get; set; } = [];

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public PatternBlock Clone()
    {
        return new PatternBlock
        {
            PatternIndex = PatternIndex,
            StartBeat = StartBeat,
            DurationBeats = DurationBeats,
            Volume = Volume,
            Panning = Panning,
            Muted = Muted,
            VolumeAutomation = VolumeAutomation.Select(p => p.Clone()).ToList(),
            PanAutomation = PanAutomation.Select(p => p.Clone()).ToList()
        };
    }
}

/// <summary>Beat-based automation point used by the song editor.</summary>
public sealed class AutomationPoint
{
    /// <summary>
    /// Stores or exposes Beat.
    /// </summary>
    public double Beat { get; set; }
    /// <summary>
    /// Stores or exposes Value.
    /// </summary>
    public byte Value { get; set; } = 128;

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public AutomationPoint Clone() => (AutomationPoint)MemberwiseClone();
}
