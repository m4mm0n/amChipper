using System.Collections.ObjectModel;
using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the ChannelRackViewModel component.
/// </summary>
public sealed class ChannelRackViewModel : BaseViewModel
{
    /// <summary>
    /// Stores or exposes Main.
    /// </summary>
    internal MainViewModel Main { get; }

    /// <summary>
    /// Stores or exposes Tracks.
    /// </summary>
    public ObservableCollection<ChannelRackTrackViewModel> Tracks { get; } = [];

    /// <summary>
    /// Stores or exposes _currentPattern.
    /// </summary>
    private Pattern? _currentPattern;
    /// <summary>
    /// Stores or exposes _currentPatternIndex.
    /// </summary>
    private int _currentPatternIndex = -1;
    /// <summary>
    /// Stores or exposes _currentStepIndex.
    /// </summary>
    private int _currentStepIndex = -1;
    /// <summary>
    /// Stores or exposes CurrentPatternIndex.
    /// </summary>
    public int CurrentPatternIndex
    {
        get => _currentPatternIndex;
        private set => SetField(ref _currentPatternIndex, value);
    }

    /// <summary>
    /// Stores or exposes CurrentStepIndex.
    /// </summary>
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (SetField(ref _currentStepIndex, value))
                RefreshPlaybackHighlight();
        }
    }

    /// <summary>
    /// Executes the PatternLabel operation.
    /// </summary>
    public string PatternLabel => _currentPattern?.Name ?? "(no pattern)";

    public ChannelRackViewModel(MainViewModel main)
    {
        Main = main;
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh()
    {
        Tracks.Clear();
        _currentPattern = Main.PatternEditor.CurrentPattern;
        CurrentPatternIndex = Main.PatternEditor.CurrentPatternIndex;
        OnPropertyChanged(nameof(PatternLabel));

        if (Main.Song is null)
            return;

        for (int i = 0; i < Main.Song.Tracks.Count; i++)
        {
            var track = Main.Song.Tracks[i];
            var vm = new ChannelRackTrackViewModel(this, track, i);
            vm.SyncFromPattern(_currentPattern);
            track.EffectSummary = vm.EffectSummary;
            Tracks.Add(vm);
        }

        RefreshPlaybackHighlight();
    }

    /// <summary>
    /// Executes the OnStepChanged operation.
    /// </summary>
    internal void OnStepChanged(ChannelRackTrackViewModel trackVm, int stepIndex, bool isOn)
    {
        if (Main.Song is null || _currentPattern is null)
            return;

        if (trackVm.TrackIndex < 0 || trackVm.TrackIndex >= Main.Song.Tracks.Count)
            return;

        var track = Main.Song.Tracks[trackVm.TrackIndex];
        int rowsPerStep = Math.Max(1, Main.Song.RowsPerBeat);
        int row = stepIndex * rowsPerStep;
        Main.BeginHistory(row >= _currentPattern.RowCount
            ? "Resize pattern for step"
            : (isOn ? "Step on" : "Step off"));

        if (row >= _currentPattern.RowCount)
        {
            int newRows = Math.Max(_currentPattern.RowCount, (stepIndex + 1) * rowsPerStep);
            _currentPattern.Resize(newRows, Math.Max(_currentPattern.ChannelCount, Main.Song.Tracks.Count));
        }

        int channel = Math.Clamp(trackVm.TrackIndex, 0, Math.Max(_currentPattern.ChannelCount - 1, 0));

        if (isOn)
        {
            var note = new Note
            {
                Pitch = track.StepPitch,
                InstrumentIndex = (byte)Math.Clamp(track.InstrumentIndex + 1, 0, 255),
                Velocity = 110,
                Volume = 64,
                StartTick = row,
                DurationTicks = rowsPerStep
            };
            _currentPattern.SetNote(row, channel, note);
        }
        else
        {
            _currentPattern.SetNote(row, channel, new Note());
        }

        AppLogger.Info($"[ChannelRack] Step change track={trackVm.TrackIndex} row={row} on={isOn} pitch={track.StepPitch} pattern={CurrentPatternIndex}");
        Main.PatternEditor.RefreshRows();
        Main.PatternEditor.NotifyPatternDataChanged();
        Main.PianoRoll.SetCurrentPattern(CurrentPatternIndex);
        Main.CommitHistory();
    }

    /// <summary>
    /// Executes the UpdatePlaybackRow operation.
    /// </summary>
    public void UpdatePlaybackRow(int row)
    {
        int rowsPerStep = Math.Max(1, Main.Song?.RowsPerBeat ?? 4);
        CurrentStepIndex = Math.Max(0, row / rowsPerStep);
    }

    /// <summary>
    /// Executes the RefreshPlaybackHighlight operation.
    /// </summary>
    private void RefreshPlaybackHighlight()
    {
        foreach (var track in Tracks)
            track.SetCurrentStep(CurrentStepIndex);
    }
}

/// <summary>
/// Represents the ChannelRackTrackViewModel component.
/// </summary>
public sealed class ChannelRackTrackViewModel : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _parent.
    /// </summary>
    private readonly ChannelRackViewModel _parent;
    /// <summary>
    /// Stores or exposes Track.
    /// </summary>
    public Track Track { get; }
    /// <summary>
    /// Stores or exposes TrackIndex.
    /// </summary>
    public int TrackIndex { get; }

    /// <summary>
    /// Stores or exposes Steps.
    /// </summary>
    public ObservableCollection<ChannelRackStepViewModel> Steps { get; } = [];

    /// <summary>
    /// Stores or exposes TrackName.
    /// </summary>
    public string TrackName => Track.Name;
    /// <summary>
    /// Executes the PitchLabel operation.
    /// </summary>
    public string PitchLabel => Note.PitchToName(Track.StepPitch);
    /// <summary>
    /// Stores or exposes _currentStepIndex.
    /// </summary>
    private int _currentStepIndex = -1;
    /// <summary>
    /// Stores or exposes CurrentStepIndex.
    /// </summary>
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set => SetField(ref _currentStepIndex, value);
    }
    /// <summary>
    /// Stores or exposes InstrumentLabel.
    /// </summary>
    public string InstrumentLabel
    {
        get
        {
            if (_parent.Main.Song is null)
                return "Inst: --";

            int index = Math.Clamp(Track.InstrumentIndex, 0, Math.Max(_parent.Main.Song.Instruments.Count - 1, 0));
            if (_parent.Main.Song.Instruments.Count == 0)
                return "Inst: --";

            var inst = _parent.Main.Song.Instruments[index];
            return $"Inst: {index + 1:D2} {inst.Name}";
        }
    }

    /// <summary>
    /// Stores or exposes EffectSummary.
    /// </summary>
    public string EffectSummary { get; private set; } = "FX --";

    public ChannelRackTrackViewModel(ChannelRackViewModel parent, Track track, int trackIndex)
    {
        _parent = parent;
        Track = track;
        TrackIndex = trackIndex;

        for (int i = 0; i < 16; i++)
            Steps.Add(new ChannelRackStepViewModel(this, i));
    }

    /// <summary>
    /// Executes the SyncFromPattern operation.
    /// </summary>
    public void SyncFromPattern(Pattern? pattern)
    {
        int rowsPerStep = Math.Max(1, _parent.Main.Song?.RowsPerBeat ?? 4);
        int channel = Math.Clamp(TrackIndex, 0, Math.Max(pattern?.ChannelCount - 1 ?? 0, 0));

        for (int i = 0; i < Steps.Count; i++)
        {
            bool on = false;
            if (pattern is not null)
            {
                int row = i * rowsPerStep;
                if (row < pattern.RowCount)
                {
                    var note = pattern.GetNote(row, channel);
                    on = note.Pitch == Track.StepPitch && note.InstrumentIndex == Math.Clamp(Track.InstrumentIndex + 1, 0, 255);
                }
            }

            Steps[i].SetIsOn(on);
        }

        EffectSummary = BuildEffectSummary(pattern, channel);
        Track.EffectSummary = EffectSummary;

        OnPropertyChanged(nameof(PitchLabel));
        OnPropertyChanged(nameof(TrackName));
        OnPropertyChanged(nameof(InstrumentLabel));
        OnPropertyChanged(nameof(EffectSummary));
    }

    /// <summary>
    /// Executes the BuildEffectSummary operation.
    /// </summary>
    private static string BuildEffectSummary(Pattern? pattern, int channel)
    {
        if (pattern is null || channel < 0 || channel >= pattern.ChannelCount)
            return "FX --";

        int main = 0;
        int volume = 0;
        var labels = new List<string>(4);

        for (int row = 0; row < pattern.RowCount; row++)
        {
            var note = pattern.GetNote(row, channel);
            if (note.EffectColumn != 0 || note.Effect != EffectCommand.None || note.EffectParam != 0)
            {
                main++;
                if (labels.Count < 3)
                    labels.Add(IsSidTraceCell(note) ? FormatSidTraceCell(note) : $"{FormatEffectColumn(note)}{note.EffectParam:X2}");
            }

            if (note.VolumeColumn != 0)
                volume++;
        }

        return main == 0 && volume == 0
            ? "FX none"
            : $"FX {main} main / {volume} vol  {string.Join(" ", labels)}";
    }

    /// <summary>
    /// Executes the FormatEffectColumn operation.
    /// </summary>
    private static string FormatEffectColumn(Note note)
    {
        byte command = note.EffectColumn != 0 || note.Effect == EffectCommand.None
            ? note.EffectColumn
            : (byte)note.Effect;

        return command < 0x0A
            ? command.ToString("X1")
            : ((char)('A' + command - 0x0A)).ToString();
    }

    /// <summary>
    /// Executes the IsSidTraceCell operation.
    /// </summary>
    private static bool IsSidTraceCell(Note note) =>
        note.InstrumentIndex is >= 1 and <= 3 &&
        (note.VolumeColumn is 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80 ||
         note.EffectParam != 0 && (note.EffectParam & 0xF0) != 0);

    /// <summary>
    /// Executes the FormatSidTraceCell operation.
    /// </summary>
    private static string FormatSidTraceCell(Note note)
    {
        string wave = (note.VolumeColumn & 0xF0) switch
        {
            0x10 => "SAW",
            0x20 => "TRI",
            0x40 => "PUL",
            0x80 => "NOI",
            0x30 => "S+T",
            0x50 => "S+P",
            0x60 => "T+P",
            0x70 => "MIX",
            _ => "SID"
        };
        string flags = string.Empty;
        if ((note.EffectParam & 0x02) != 0)
            flags += "S";
        if ((note.EffectParam & 0x04) != 0)
            flags += "R";
        if ((note.EffectParam & 0x08) != 0)
            flags += "T";

        return string.IsNullOrEmpty(flags) ? wave : $"{wave}/{flags}";
    }

    /// <summary>
    /// Executes the SetCurrentStep operation.
    /// </summary>
    public void SetCurrentStep(int stepIndex)
    {
        if (!SetField(ref _currentStepIndex, stepIndex))
            return;

        foreach (var step in Steps)
            step.SetIsCurrentStep(step.StepIndex == stepIndex);
    }

    /// <summary>
    /// Executes the StepToggled operation.
    /// </summary>
    internal void StepToggled(int stepIndex, bool isOn) => _parent.OnStepChanged(this, stepIndex, isOn);
}

/// <summary>
/// Represents the ChannelRackStepViewModel component.
/// </summary>
public sealed class ChannelRackStepViewModel : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _parent.
    /// </summary>
    private readonly ChannelRackTrackViewModel _parent;
    /// <summary>
    /// Stores or exposes StepIndex.
    /// </summary>
    public int StepIndex { get; }

    /// <summary>
    /// Stores or exposes _isOn.
    /// </summary>
    private bool _isOn;
    /// <summary>
    /// Stores or exposes IsOn.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetField(ref _isOn, value))
                _parent.StepToggled(StepIndex, value);
        }
    }

    /// <summary>
    /// Stores or exposes _isCurrentStep.
    /// </summary>
    private bool _isCurrentStep;
    /// <summary>
    /// Stores or exposes IsCurrentStep.
    /// </summary>
    public bool IsCurrentStep
    {
        get => _isCurrentStep;
        private set => SetField(ref _isCurrentStep, value);
    }

    public ChannelRackStepViewModel(ChannelRackTrackViewModel parent, int stepIndex)
    {
        _parent = parent;
        StepIndex = stepIndex;
    }

    /// <summary>
    /// Executes the SetIsOn operation.
    /// </summary>
    public void SetIsOn(bool isOn)
    {
        _isOn = isOn;
        OnPropertyChanged(nameof(IsOn));
    }

    /// <summary>
    /// Executes the SetIsCurrentStep operation.
    /// </summary>
    public void SetIsCurrentStep(bool isCurrentStep)
    {
        IsCurrentStep = isCurrentStep;
    }
}
