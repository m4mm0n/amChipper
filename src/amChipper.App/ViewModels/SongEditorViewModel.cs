using System.Collections.ObjectModel;
using System.Windows.Input;
using amChipper.App.Commands;
using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the SongEditorViewModel component.
/// </summary>
public sealed class SongEditorViewModel : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _main.
    /// </summary>
    private readonly MainViewModel _main;
    /// <summary>
    /// Stores or exposes _song.
    /// </summary>
    private Song? _song;

    // ── View parameters ────────────────────────────────────────────────────────

    /// <summary>Pixels per beat horizontally.</summary>
    private double _pixelsPerBeat = 48.0;
    /// <summary>
    /// Stores or exposes PixelsPerBeat.
    /// </summary>
    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set { SetField(ref _pixelsPerBeat, Math.Clamp(value, 3, 520)); LayoutChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Height of each track lane in pixels.</summary>
    private double _trackHeight = 48.0;
    /// <summary>
    /// Stores or exposes TrackHeight.
    /// </summary>
    public double TrackHeight
    {
        get => _trackHeight;
        set { SetField(ref _trackHeight, Math.Clamp(value, 20, 120)); LayoutChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>
    /// Stores or exposes _scrollBeat.
    /// </summary>
    private double _scrollBeat;
    /// <summary>
    /// Executes the ScrollBeat operation.
    /// </summary>
    public double ScrollBeat { get => _scrollBeat; set { SetField(ref _scrollBeat, Math.Max(0, value)); LayoutChanged?.Invoke(this, EventArgs.Empty); } }

    /// <summary>
    /// Stores or exposes _playheadBeat.
    /// </summary>
    private double _playheadBeat;
    /// <summary>
    /// Executes the PlayheadBeat operation.
    /// </summary>
    public double PlayheadBeat { get => _playheadBeat; set { SetField(ref _playheadBeat, value); PlayheadMoved?.Invoke(this, EventArgs.Empty); } }

    /// <summary>
    /// Stores or exposes CurrentTool.
    /// </summary>
    public SongEditorTool CurrentTool { get; set; } = SongEditorTool.Draw;

    /// <summary>
    /// Stores or exposes _selectedPatternIndex.
    /// </summary>
    private int _selectedPatternIndex;
    /// <summary>
    /// Stores or exposes SelectedPatternIndex.
    /// </summary>
    public int SelectedPatternIndex
    {
        get => _selectedPatternIndex;
        set => SetField(ref _selectedPatternIndex, Math.Max(0, value));
    }

    // ── Data binding ──────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Tracks.
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = [];
    /// <summary>
    /// Stores or exposes Patterns.
    /// </summary>
    public ObservableCollection<Pattern> Patterns { get; } = [];

    /// <summary>
    /// Stores or exposes _selectedTrack.
    /// </summary>
    private Track? _selectedTrack;
    /// <summary>
    /// Executes the SelectedTrack operation.
    /// </summary>
    public Track? SelectedTrack { get => _selectedTrack; set => SetField(ref _selectedTrack, value); }

    /// <summary>
    /// Stores or exposes _selectedBlock.
    /// </summary>
    private PatternBlock? _selectedBlock;
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
                OnPropertyChanged(nameof(HasSelectedBlock));
                OnPropertyChanged(nameof(BlockVolume));
                OnPropertyChanged(nameof(BlockPan));
            }
        }
    }

    /// <summary>
    /// Stores or exposes HasSelectedBlock.
    /// </summary>
    public bool HasSelectedBlock => SelectedBlock is not null;

    /// <summary>
    /// Stores or exposes BlockVolume.
    /// </summary>
    public byte BlockVolume
    {
        get => SelectedBlock?.Volume ?? 128;
        set
        {
            if (SelectedBlock is null || SelectedBlock.Volume == value)
                return;

            SelectedBlock.Volume = value;
            OnPropertyChanged();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            SongDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stores or exposes BlockPan.
    /// </summary>
    public byte BlockPan
    {
        get => SelectedBlock?.Panning ?? 128;
        set
        {
            if (SelectedBlock is null || SelectedBlock.Panning == value)
                return;

            SelectedBlock.Panning = value;
            OnPropertyChanged();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            SongDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? LayoutChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? PlayheadMoved;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? SongDataChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>
    /// Executes the BeginHistory operation.
    /// </summary>
    public void BeginHistory(string reason) => _main.BeginHistory(reason);
    /// <summary>
    /// Executes the CommitHistory operation.
    /// </summary>
    public void CommitHistory() => _main.CommitHistory();
    /// <summary>
    /// Executes the CancelHistory operation.
    /// </summary>
    public void CancelHistory() => _main.CancelHistory();

    /// <summary>
    /// Raised when the user double-clicks a pattern block, requesting that the
    /// pattern be opened for editing in the Pattern Editor and Piano Roll.
    /// The EventArgs.e integer payload is the pattern index.
    /// </summary>
    public event EventHandler<int>? BlockEditRequested;

    // ── Event raise helpers (needed because C# events can only fire from the declaring class) ──

    /// <summary>
    /// Executes the RaiseLayoutChanged operation.
    /// </summary>
    public void RaiseLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);
    /// <summary>
    /// Executes the RaisePlayheadMoved operation.
    /// </summary>
    public void RaisePlayheadMoved() => PlayheadMoved?.Invoke(this, EventArgs.Empty);
    /// <summary>
    /// Executes the RaiseBlockEditRequested operation.
    /// </summary>
    public void RaiseBlockEditRequested(int patternIndex) => BlockEditRequested?.Invoke(this, patternIndex);
    /// <summary>
    /// Executes the RaiseSongDataChanged operation.
    /// </summary>
    public void RaiseSongDataChanged() => SongDataChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Executes the SeekToBeat operation.
    /// </summary>
    public void SeekToBeat(double beat)
    {
        PlayheadBeat = Math.Max(0, Math.Round(beat * 4.0) / 4.0);
        AppLogger.Info($"[Timeline] SeekToBeat beat={PlayheadBeat:0.###}");
        SeekRequested?.Invoke(this, PlayheadBeat);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes ZoomInCommand.
    /// </summary>
    public ICommand ZoomInCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomOutCommand.
    /// </summary>
    public ICommand ZoomOutCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomResetCommand.
    /// </summary>
    public ICommand ZoomResetCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomFitCommand.
    /// </summary>
    public ICommand ZoomFitCommand { get; }
    /// <summary>
    /// Stores or exposes DeleteBlockCommand.
    /// </summary>
    public ICommand DeleteBlockCommand { get; }
    /// <summary>
    /// Stores or exposes DuplicateBlockCommand.
    /// </summary>
    public ICommand DuplicateBlockCommand { get; }
    /// <summary>
    /// Stores or exposes Automation.
    /// </summary>
    public AutomationViewModel Automation => _main.Automation;
    /// <summary>
    /// Stores or exposes ClipEnvelope.
    /// </summary>
    public ClipEnvelopeViewModel ClipEnvelope => _main.ClipEnvelope;

    public SongEditorViewModel(MainViewModel main)
    {
        _main = main;
        ZoomInCommand = new RelayCommand(_ => PixelsPerBeat *= 1.25);
        ZoomOutCommand = new RelayCommand(_ => PixelsPerBeat /= 1.25);
        ZoomResetCommand = new RelayCommand(_ => PixelsPerBeat = 48);
        ZoomFitCommand = new RelayCommand(width =>
        {
            double viewportWidth = width is double d && d > 0 ? d : 1200;
            PixelsPerBeat = viewportWidth / Math.Max(TotalTimelineBeats + 4, 16);
        });
        DeleteBlockCommand = new RelayCommand(_ => DeleteBlock(), _ => _selectedBlock is not null);
        DuplicateBlockCommand = new RelayCommand(_ => DuplicateBlock(), _ => _selectedBlock is not null);
    }

    /// <summary>
    /// Executes the SetSong operation.
    /// </summary>
    public void SetSong(Song song)
    {
        _song = song;
        Refresh();
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh()
    {
        Tracks.Clear();
        Patterns.Clear();
        if (_song is null) return;
        foreach (var t in _song.Tracks) Tracks.Add(t);
        foreach (var p in _song.Patterns) Patterns.Add(p);

        if (Patterns.Count == 0)
        {
            SelectedPatternIndex = 0;
        }
        else if (SelectedPatternIndex < 0 || SelectedPatternIndex >= Patterns.Count)
        {
            int initialPattern = _song.OrderList.Count > 0 && (uint)_song.OrderList[0] < (uint)Patterns.Count
                ? _song.OrderList[0]
                : Math.Clamp(SelectedPatternIndex, 0, Patterns.Count - 1);
            SelectedPatternIndex = initialPattern;
        }

        if (_selectedTrack is null || !_song.Tracks.Contains(_selectedTrack))
            SelectedTrack = _song.Tracks.FirstOrDefault();

        if (_selectedBlock is null || !_song.Tracks.Any(t => t.Blocks.Contains(_selectedBlock)))
        {
            var selectedPattern = SelectedPatternIndex;
            SelectedBlock = _song.Tracks
                .SelectMany(t => t.Blocks)
                .FirstOrDefault(b => b.PatternIndex == selectedPattern)
                ?? _song.Tracks.SelectMany(t => t.Blocks).FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasSelectedBlock));
        OnPropertyChanged(nameof(BlockVolume));
        OnPropertyChanged(nameof(BlockPan));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Block editing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the PlaceBlock operation.
    /// </summary>
    public void PlaceBlock(Track track, int patternIndex, double startBeat)
    {
        if (_song is null || patternIndex >= _song.Patterns.Count) return;
        _main.BeginHistory("Place block");
        var pat = _song.Patterns[patternIndex];
        double dur = (double)pat.RowCount / (_song.RowsPerBeat);

        // Quantise to 1-beat
        startBeat = Math.Round(startBeat);
        startBeat = Math.Max(0, startBeat);

        track.Blocks.Add(new PatternBlock
        {
            PatternIndex = patternIndex,
            StartBeat = startBeat,
            DurationBeats = dur
        });
        AppLogger.Info($"[Timeline] PlaceBlock track=\"{track.Name}\" pattern={patternIndex} startBeat={startBeat:0.###} duration={dur:0.###}");
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SongDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the DeleteBlock operation.
    /// </summary>
    public void DeleteBlock()
    {
        if (_selectedBlock is null || _song is null) return;
        _main.BeginHistory("Delete block");
        foreach (var t in _song.Tracks)
            t.Blocks.Remove(_selectedBlock);
        AppLogger.Info($"[Timeline] DeleteBlock pattern={_selectedBlock.PatternIndex} startBeat={_selectedBlock.StartBeat:0.###} duration={_selectedBlock.DurationBeats:0.###}");
        SelectedBlock = null;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SongDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the DuplicateBlock operation.
    /// </summary>
    private void DuplicateBlock()
    {
        if (_selectedBlock is null || _song is null) return;

        var sourceTrack = _song.Tracks.FirstOrDefault(t => t.Blocks.Contains(_selectedBlock));
        if (sourceTrack is null) return;

        _main.BeginHistory("Duplicate block");

        var clone = _selectedBlock.Clone();
        clone.StartBeat = Math.Round(_selectedBlock.StartBeat + _selectedBlock.DurationBeats);
        sourceTrack.Blocks.Add(clone);

        SelectedBlock = clone;
        AppLogger.Info(
            $"[Timeline] DuplicateBlock pattern={clone.PatternIndex} startBeat={clone.StartBeat:0.###} duration={clone.DurationBeats:0.###} track=\"{sourceTrack.Name}\"");
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SongDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Executes the BeatToX operation.
    /// </summary>
    public double BeatToX(double beat) => (beat - ScrollBeat) * PixelsPerBeat;
    /// <summary>
    /// Executes the XToBeat operation.
    /// </summary>
    public double XToBeat(double x) => x / PixelsPerBeat + ScrollBeat;
    /// <summary>
    /// Executes the TrackToY operation.
    /// </summary>
    public double TrackToY(int trackIndex) => trackIndex * TrackHeight;
    /// <summary>
    /// Executes the YToTrackIndex operation.
    /// </summary>
    public int YToTrackIndex(double y) => (int)(y / TrackHeight);
    /// <summary>
    /// Executes the TotalOrderBeats operation.
    /// </summary>
    public double TotalOrderBeats => EnumerateOrders().Select(o => o.StartBeat + o.DurationBeats).DefaultIfEmpty(0).Max();
    /// <summary>
    /// Executes the TotalArrangementBeats operation.
    /// </summary>
    public double TotalArrangementBeats => Tracks.SelectMany(t => t.Blocks).Select(b => b.StartBeat + b.DurationBeats).DefaultIfEmpty(0).Max();
    /// <summary>
    /// Executes the TotalTimelineBeats operation.
    /// </summary>
    public double TotalTimelineBeats => Math.Max(TotalOrderBeats, TotalArrangementBeats);

    public IEnumerable<(int Order, int PatternIndex, double StartBeat, double DurationBeats)> EnumerateOrders()
    {
        if (_song is null)
            yield break;

        double beat = 0;
        int rowsPerBeat = Math.Max(_song.RowsPerBeat, 1);
        for (int order = 0; order < _song.OrderList.Count; order++)
        {
            int patternIndex = _song.OrderList[order];
            if ((uint)patternIndex >= (uint)_song.Patterns.Count)
                continue;

            var pattern = _song.Patterns[patternIndex];
            double duration = Math.Max(pattern.RowCount / (double)rowsPerBeat, 1.0);
            yield return (order, patternIndex, beat, duration);
            beat += duration;
        }
    }

    public IEnumerable<(int Order, int PatternIndex, double StartBeat, double DurationBeats, bool LoopCopy)> EnumerateOrdersForRange(double startBeat, double endBeat)
    {
        var orders = EnumerateOrders().ToList();
        if (orders.Count == 0)
            yield break;

        double sequenceLength = orders.Max(o => o.StartBeat + o.DurationBeats);
        if (sequenceLength <= 0)
            yield break;

        int firstLoop = Math.Max(0, (int)Math.Floor(startBeat / sequenceLength) - 1);
        int lastLoop = Math.Max(firstLoop, (int)Math.Ceiling(endBeat / sequenceLength) + 1);
        for (int loop = firstLoop; loop <= lastLoop; loop++)
        {
            double offset = loop * sequenceLength;
            foreach (var order in orders)
            {
                double absoluteStart = offset + order.StartBeat;
                double absoluteEnd = absoluteStart + order.DurationBeats;
                if (absoluteEnd < startBeat || absoluteStart > endBeat)
                    continue;

                yield return (order.Order, order.PatternIndex, absoluteStart, order.DurationBeats, loop > 0);
            }
        }
    }
}
