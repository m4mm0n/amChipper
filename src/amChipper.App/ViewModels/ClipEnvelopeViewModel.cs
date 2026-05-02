using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the ClipEnvelopeViewModel component.
/// </summary>
public sealed class ClipEnvelopeViewModel : BaseViewModel, IAutomationLaneViewModel
{
    /// <summary>
    /// Stores or exposes Main.
    /// </summary>
    internal MainViewModel Main { get; }
    /// <summary>
    /// Stores or exposes _selectedBlock.
    /// </summary>
    private PatternBlock? _selectedBlock;
    /// <summary>
    /// Stores or exposes _target.
    /// </summary>
    private AutomationTarget _target = AutomationTarget.Volume;

    /// <summary>
    /// Stores or exposes SelectedBlock.
    /// </summary>
    public PatternBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (SetField(ref _selectedBlock, value))
            {
                OnPropertyChanged(nameof(Points));
                OnPropertyChanged(nameof(BlockLabel));
                OnPropertyChanged(nameof(TargetLabel));
            }
        }
    }

    /// <summary>
    /// Stores or exposes Target.
    /// </summary>
    public AutomationTarget Target
    {
        get => _target;
        set
        {
            if (SetField(ref _target, value))
            {
                OnPropertyChanged(nameof(Points));
                OnPropertyChanged(nameof(TargetLabel));
            }
        }
    }

    /// <summary>
    /// Stores or exposes TargetLabel.
    /// </summary>
    public string TargetLabel => Target == AutomationTarget.Volume ? "Volume" : "Pan";
    /// <summary>
    /// Stores or exposes BlockLabel.
    /// </summary>
    public string BlockLabel => SelectedBlock is null ? "No clip selected" : $"Clip {SelectedBlock.PatternIndex}";
    /// <summary>
    /// Executes the Points operation.
    /// </summary>
    public IReadOnlyList<AutomationPoint> Points => GetPoints();
    /// <summary>
    /// Stores or exposes PlaybackBeat.
    /// </summary>
    public double PlaybackBeat => SelectedBlock is null
        ? 0
        : Math.Max(0, Main.SongEditor.PlayheadBeat - SelectedBlock.StartBeat);

    public ClipEnvelopeViewModel(MainViewModel main)
    {
        Main = main;
        Main.SongEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SongEditorViewModel.SelectedBlock))
                SelectedBlock = Main.SongEditor.SelectedBlock;
        };
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh()
    {
        if (SelectedBlock is null || !Main.SongEditor.Tracks.Any(t => t.Blocks.Contains(SelectedBlock)))
            SelectedBlock = Main.SongEditor.SelectedBlock;

        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(BlockLabel));
        OnPropertyChanged(nameof(TargetLabel));
    }

    /// <summary>
    /// Executes the NotifyPlaybackMoved operation.
    /// </summary>
    public void NotifyPlaybackMoved() => OnPropertyChanged(nameof(PlaybackBeat));

    /// <summary>
    /// Executes the BeginHistory operation.
    /// </summary>
    public void BeginHistory(string reason) => Main.BeginHistory(reason);
    /// <summary>
    /// Executes the CommitHistory operation.
    /// </summary>
    public void CommitHistory() => Main.CommitHistory();
    /// <summary>
    /// Executes the CancelHistory operation.
    /// </summary>
    public void CancelHistory() => Main.CancelHistory();

    /// <summary>
    /// Executes the AddPoint operation.
    /// </summary>
    public void AddPoint(double beat, byte value)
    {
        if (SelectedBlock is null) return;
        Main.BeginHistory("Add clip envelope point");
        var list = GetPointsMutable();
        list.Add(new AutomationPoint { Beat = Math.Max(0, beat), Value = value });
        SortPoints(list);
        OnPropertyChanged(nameof(Points));
        Main.CommitHistory();
        Main.SongEditor.RaiseSongDataChanged();
        AppLogger.Info($"[ClipEnv] AddPoint clip={SelectedBlock.PatternIndex} target={Target} beat={beat:0.###} value={value}");
    }

    /// <summary>
    /// Executes the DeletePoint operation.
    /// </summary>
    public void DeletePoint(AutomationPoint point)
    {
        if (SelectedBlock is null) return;
        Main.BeginHistory("Delete clip envelope point");
        var list = GetPointsMutable();
        list.Remove(point);
        OnPropertyChanged(nameof(Points));
        Main.CommitHistory();
        Main.SongEditor.RaiseSongDataChanged();
        AppLogger.Info($"[ClipEnv] DeletePoint clip={SelectedBlock.PatternIndex} target={Target} beat={point.Beat:0.###} value={point.Value}");
    }

    /// <summary>
    /// Executes the MovePoint operation.
    /// </summary>
    public void MovePoint(AutomationPoint point, double beat, byte value)
    {
        if (SelectedBlock is null) return;
        point.Beat = Math.Max(0, beat);
        point.Value = value;
        SortPoints(GetPointsMutable());
        OnPropertyChanged(nameof(Points));
        Main.SongEditor.RaiseSongDataChanged();
    }

    /// <summary>
    /// Executes the GetPointsMutable operation.
    /// </summary>
    private List<AutomationPoint> GetPointsMutable() =>
        SelectedBlock is null ? [] : (Target == AutomationTarget.Volume ? SelectedBlock.VolumeAutomation : SelectedBlock.PanAutomation);

    /// <summary>
    /// Executes the GetPoints operation.
    /// </summary>
    private IReadOnlyList<AutomationPoint> GetPoints() =>
        SelectedBlock is null ? [] : (Target == AutomationTarget.Volume ? SelectedBlock.VolumeAutomation : SelectedBlock.PanAutomation);

    /// <summary>
    /// Executes the SortPoints operation.
    /// </summary>
    private static void SortPoints(List<AutomationPoint> points) =>
        points.Sort((a, b) => a.Beat.CompareTo(b.Beat));
}
