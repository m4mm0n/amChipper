using System.Collections.ObjectModel;
using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the AutomationViewModel component.
/// </summary>
public sealed class AutomationViewModel : BaseViewModel, IAutomationLaneViewModel
{
    /// <summary>
    /// Stores or exposes Main.
    /// </summary>
    internal MainViewModel Main { get; }
    /// <summary>
    /// Stores or exposes _selectedTrack.
    /// </summary>
    private Track? _selectedTrack;
    /// <summary>
    /// Stores or exposes _target.
    /// </summary>
    private AutomationTarget _target = AutomationTarget.Volume;

    /// <summary>
    /// Stores or exposes Tracks.
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>
    /// Stores or exposes SelectedTrack.
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetField(ref _selectedTrack, value))
            {
                OnPropertyChanged(nameof(Points));
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
                OnPropertyChanged(nameof(Points));
        }
    }

    /// <summary>
    /// Stores or exposes TargetLabel.
    /// </summary>
    public string TargetLabel => Target == AutomationTarget.Volume ? "Volume" : "Pan";

    /// <summary>
    /// Executes the Points operation.
    /// </summary>
    public IReadOnlyList<AutomationPoint> Points => GetPoints();
    /// <summary>
    /// Stores or exposes PlaybackBeat.
    /// </summary>
    public double PlaybackBeat => Main.SongEditor.PlayheadBeat;

    public AutomationViewModel(MainViewModel main)
    {
        Main = main;
        Main.SongEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SongEditorViewModel.SelectedTrack))
                SelectedTrack = Main.SongEditor.SelectedTrack;
        };
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh()
    {
        Tracks.Clear();
        if (Main.Song is not null)
        {
            foreach (var track in Main.Song.Tracks)
                Tracks.Add(track);
        }

        if (SelectedTrack is null || !Tracks.Contains(SelectedTrack))
            SelectedTrack = Main.SongEditor.SelectedTrack ?? Tracks.FirstOrDefault();

        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(TargetLabel));
    }

    /// <summary>
    /// Executes the NotifyPlaybackMoved operation.
    /// </summary>
    public void NotifyPlaybackMoved() => OnPropertyChanged(nameof(PlaybackBeat));

    /// <summary>
    /// Executes the EnsureSelection operation.
    /// </summary>
    public void EnsureSelection()
    {
        if (SelectedTrack is null)
            SelectedTrack = Main.SongEditor.SelectedTrack ?? Tracks.FirstOrDefault();
    }

    /// <summary>
    /// Executes the AddPoint operation.
    /// </summary>
    public void AddPoint(double beat, byte value)
    {
        if (SelectedTrack is null) return;
        Main.BeginHistory("Add automation point");
        var list = GetPointsMutable();
        list.Add(new AutomationPoint { Beat = Math.Max(0, beat), Value = value });
        SortPoints(list);
        OnPropertyChanged(nameof(Points));
        Main.CommitHistory();
        Main.SongEditor.RaiseSongDataChanged();
        AppLogger.Info($"[Automation] AddPoint track=\"{SelectedTrack.Name}\" target={Target} beat={beat:0.###} value={value}");
    }

    /// <summary>
    /// Executes the DeletePoint operation.
    /// </summary>
    public void DeletePoint(AutomationPoint point)
    {
        if (SelectedTrack is null) return;
        Main.BeginHistory("Delete automation point");
        var list = GetPointsMutable();
        list.Remove(point);
        OnPropertyChanged(nameof(Points));
        Main.CommitHistory();
        Main.SongEditor.RaiseSongDataChanged();
        AppLogger.Info($"[Automation] DeletePoint track=\"{SelectedTrack.Name}\" target={Target} beat={point.Beat:0.###} value={point.Value}");
    }

    /// <summary>
    /// Executes the MovePoint operation.
    /// </summary>
    public void MovePoint(AutomationPoint point, double beat, byte value)
    {
        if (SelectedTrack is null) return;
        point.Beat = Math.Max(0, beat);
        point.Value = value;
        SortPoints(GetPointsMutable());
        OnPropertyChanged(nameof(Points));
        Main.SongEditor.RaiseSongDataChanged();
    }

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
    /// Executes the GetPointsMutable operation.
    /// </summary>
    private List<AutomationPoint> GetPointsMutable() =>
        SelectedTrack is null ? [] : (Target == AutomationTarget.Volume ? SelectedTrack.VolumeAutomation : SelectedTrack.PanAutomation);

    /// <summary>
    /// Executes the GetPoints operation.
    /// </summary>
    private IReadOnlyList<AutomationPoint> GetPoints() =>
        SelectedTrack is null ? [] : (Target == AutomationTarget.Volume ? SelectedTrack.VolumeAutomation : SelectedTrack.PanAutomation);

    /// <summary>
    /// Executes the SortPoints operation.
    /// </summary>
    private static void SortPoints(List<AutomationPoint> points) =>
        points.Sort((a, b) => a.Beat.CompareTo(b.Beat));
}
