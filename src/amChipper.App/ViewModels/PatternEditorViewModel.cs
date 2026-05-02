using System.Collections.ObjectModel;
using System.Windows.Input;
using amChipper.App.Commands;
using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Drives the MilkyTracker-style pattern editor grid.
/// </summary>
public sealed class PatternEditorViewModel : BaseViewModel
{
    /// <summary>
    /// Returns translated UI text from the owning main view model.
    /// </summary>
    public string this[string key] => _main[key];

    /// <summary>
    /// Refreshes translated indexer bindings after the application language changes.
    /// </summary>
    public void RefreshTranslations() => OnPropertyChanged("Item[]");

    /// <summary>
    /// Stores or exposes _main.
    /// </summary>
    private readonly MainViewModel _main;
    /// <summary>
    /// Stores or exposes _song.
    /// </summary>
    private Song? _song;
    /// <summary>
    /// Stores or exposes _currentPattern.
    /// </summary>
    private Pattern? _currentPattern;

    /// <summary>
    /// Executes the SetSong operation.
    /// </summary>
    public void SetSong(Song song)
    {
        _song = song;
        RefreshPatterns();
    }

    // ── Pattern selection ─────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Patterns.
    /// </summary>
    public ObservableCollection<Pattern> Patterns { get; } = [];

    /// <summary>
    /// Stores or exposes _currentPatternIndex.
    /// </summary>
    private int _currentPatternIndex;
    /// <summary>
    /// Stores or exposes CurrentPatternIndex.
    /// </summary>
    public int CurrentPatternIndex
    {
        get => _currentPatternIndex;
        set
        {
            if (!SetField(ref _currentPatternIndex, value))
                return;

            _currentPattern = (_song is not null && value >= 0 && value < _song.Patterns.Count)
                ? _song.Patterns[value] : null;
            OnPropertyChanged(nameof(CurrentPattern));
            OnPropertyChanged(nameof(ChannelCount));
            OnPropertyChanged(nameof(CurrentChannelName));
            OnPropertyChanged(nameof(CurrentChannelInstrumentLabel));
            RefreshRows();
        }
    }

    /// <summary>
    /// Stores or exposes CurrentPattern.
    /// </summary>
    public Pattern? CurrentPattern => _currentPattern;

    /// <summary>
    /// Stores or exposes DefaultInstrumentIndex.
    /// </summary>
    public byte DefaultInstrumentIndex { get; set; } = 1;

    // ── Grid data ─────────────────────────────────────────────────────────────

    /// <summary>Flattened list of PatternRow view-models, one per row.</summary>
    public ObservableCollection<PatternRow> Rows { get; } = [];

    /// <summary>
    /// Stores or exposes _currentRow.
    /// </summary>
    private int _currentRow;
    /// <summary>
    /// Stores or exposes CurrentRow.
    /// </summary>
    public int CurrentRow
    {
        get => _currentRow;
        set { SetField(ref _currentRow, value); HighlightChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>
    /// Stores or exposes _currentChannel.
    /// </summary>
    private int _currentChannel;
    /// <summary>
    /// Stores or exposes CurrentChannel.
    /// </summary>
    public int CurrentChannel
    {
        get => _currentChannel;
        set
        {
            if (SetField(ref _currentChannel, value))
            {
                OnPropertyChanged(nameof(CurrentChannelName));
                OnPropertyChanged(nameof(CurrentChannelInstrumentLabel));
            }
        }
    }

    /// <summary>Number of channels (columns) in the current pattern.</summary>
    public int ChannelCount => _currentPattern?.ChannelCount ?? 0;
    /// <summary>
    /// Stores or exposes CurrentChannelName.
    /// </summary>
    public string CurrentChannelName
    {
        get
        {
            if (_song is null || _currentPattern is null || _currentChannel < 0 || _currentChannel >= _currentPattern.ChannelCount)
                return "Ch --";

            int trackIndex = Math.Clamp(_currentChannel, 0, Math.Max(_song.Tracks.Count - 1, 0));
            if (trackIndex < 0 || trackIndex >= _song.Tracks.Count)
                return $"Ch {_currentChannel + 1:D2}";

            return string.IsNullOrWhiteSpace(_song.Tracks[trackIndex].Name)
                ? $"Ch {_currentChannel + 1:D2}"
                : _song.Tracks[trackIndex].Name;
        }
    }

    /// <summary>
    /// Stores or exposes CurrentChannelInstrumentLabel.
    /// </summary>
    public string CurrentChannelInstrumentLabel
    {
        get
        {
            if (_song is null || _song.Instruments.Count == 0 || _currentChannel < 0)
                return "Inst --";

            int trackIndex = Math.Clamp(_currentChannel, 0, Math.Max(_song.Tracks.Count - 1, 0));
            if (trackIndex < 0 || trackIndex >= _song.Tracks.Count)
                return "Inst --";

            int instIndex = Math.Clamp(_song.Tracks[trackIndex].InstrumentIndex, 0, _song.Instruments.Count - 1);
            var inst = _song.Instruments[instIndex];
            return $"Inst {instIndex + 1:D2} {inst.Name}";
        }
    }

    /// <summary>
    /// Stores or exposes CurrentCellNoteLabel.
    /// </summary>
    public string CurrentCellNoteLabel
    {
        get
        {
            var note = CurrentCell;
            return note is null ? "Note --" : $"Note {note.NoteName}";
        }
    }

    /// <summary>
    /// Stores or exposes CurrentCellInstrumentLabel.
    /// </summary>
    public string CurrentCellInstrumentLabel
    {
        get
        {
            var note = CurrentCell;
            if (note is null || note.InstrumentIndex == 0 || _song is null || _song.Instruments.Count == 0)
                return "Cell Inst --";

            int instIndex = Math.Clamp(note.InstrumentIndex - 1, 0, _song.Instruments.Count - 1);
            var inst = _song.Instruments[instIndex];
            return $"Cell Inst {instIndex + 1:D2} {inst.Name}";
        }
    }

    /// <summary>
    /// Stores or exposes CurrentCellEffectLabel.
    /// </summary>
    public string CurrentCellEffectLabel
    {
        get
        {
            var note = CurrentCell;
            if (note is null || (byte)note.Effect == 0 && note.EffectParam == 0)
                return "Eff .00";
            return $"Eff {(byte)note.Effect:X1}{note.EffectParam:X2}";
        }
    }

    /// <summary>
    /// Stores or exposes CurrentCell.
    /// </summary>
    private Note? CurrentCell =>
        _currentPattern is not null && _currentRow >= 0 && _currentRow < _currentPattern.RowCount &&
        _currentChannel >= 0 && _currentChannel < _currentPattern.ChannelCount
            ? _currentPattern.GetNote(_currentRow, _currentChannel)
            : null;

    // ── Display options ───────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _rowsPerBeat.
    /// </summary>
    private int _rowsPerBeat = 4;
    /// <summary>
    /// Stores or exposes RowsPerBeat.
    /// </summary>
    public int RowsPerBeat
    {
        get => _rowsPerBeat;
        set { SetField(ref _rowsPerBeat, value); RefreshRows(); }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? HighlightChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? PatternDataChanged;

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes MoveUpCommand.
    /// </summary>
    public ICommand MoveUpCommand { get; }
    /// <summary>
    /// Stores or exposes MoveDownCommand.
    /// </summary>
    public ICommand MoveDownCommand { get; }
    /// <summary>
    /// Stores or exposes MoveLeftCommand.
    /// </summary>
    public ICommand MoveLeftCommand { get; }
    /// <summary>
    /// Stores or exposes MoveRightCommand.
    /// </summary>
    public ICommand MoveRightCommand { get; }
    /// <summary>
    /// Stores or exposes ClearCellCommand.
    /// </summary>
    public ICommand ClearCellCommand { get; }
    /// <summary>
    /// Stores or exposes PrevPatternCommand.
    /// </summary>
    public ICommand PrevPatternCommand { get; }
    /// <summary>
    /// Stores or exposes NextPatternCommand.
    /// </summary>
    public ICommand NextPatternCommand { get; }
    /// <summary>
    /// Stores or exposes DuplicatePatternCommand.
    /// </summary>
    public ICommand DuplicatePatternCommand { get; }

    public PatternEditorViewModel(MainViewModel main)
    {
        _main = main;
        MoveUpCommand = new RelayCommand(_ => MoveUp());
        MoveDownCommand = new RelayCommand(_ => MoveDown());
        MoveLeftCommand = new RelayCommand(_ => { if (_currentChannel > 0) CurrentChannel--; });
        MoveRightCommand = new RelayCommand(_ => { if (_currentPattern is not null && _currentChannel < _currentPattern.ChannelCount - 1) CurrentChannel++; });
        ClearCellCommand = new RelayCommand(_ => ClearCell());
        PrevPatternCommand = new RelayCommand(_ => { if (_currentPatternIndex > 0) CurrentPatternIndex--; });
        NextPatternCommand = new RelayCommand(_ => { if (_song is not null && _currentPatternIndex < _song.Patterns.Count - 1) CurrentPatternIndex++; });
        DuplicatePatternCommand = new RelayCommand(_ => DuplicatePattern(), _ => _currentPattern is not null);
    }

    /// <summary>
    /// Executes the MoveUp operation.
    /// </summary>
    private void MoveUp() { if (_currentRow > 0) CurrentRow--; }
    /// <summary>
    /// Executes the MoveDown operation.
    /// </summary>
    private void MoveDown() { if (_currentPattern is not null && _currentRow < _currentPattern.RowCount - 1) CurrentRow++; }

    /// <summary>
    /// Executes the ClearCell operation.
    /// </summary>
    private void ClearCell()
    {
        if (_currentPattern is null) return;
        _main.BeginHistory("Clear cell");
        var note = _currentPattern.GetNote(_currentRow, _currentChannel);
        note.Pitch = 0;
        note.InstrumentIndex = 0;
        note.Volume = 255;
        note.VolumeColumn = 0;
        note.Effect = EffectCommand.None;
        note.EffectColumn = 0;
        note.EffectParam = 0;
        RefreshRows();
        PatternDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>Enter a note by MIDI pitch (from keyboard shortcut).</summary>
    public void EnterNote(byte pitch, byte instrumentIndex = 0, byte velocity = 100)
    {
        if (_currentPattern is null) return;
        _main.BeginHistory("Enter note");
        var note = _currentPattern.GetNote(_currentRow, _currentChannel);
        note.Pitch = pitch;
        note.InstrumentIndex = instrumentIndex == 0 ? DefaultInstrumentIndex : instrumentIndex;
        note.Volume = (byte)(velocity / 2);  // 0-127 → 0-64 approx
        note.VolumeColumn = (byte)(0x10 + note.Volume);
        Rows[_currentRow].Refresh(_currentChannel, note);
        MoveDown();
        PatternDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Called during playback tracking to switch to a new pattern+row without
    /// triggering the expensive RefreshRows rebuild.  The row list is rebuilt
    /// lazily only if the pattern actually changed.
    /// </summary>
    public void TrackPlayback(int patternIndex, int row)
    {
        if (_song is null) return;

        bool patternChanged = patternIndex != _currentPatternIndex
                           && patternIndex >= 0
                           && patternIndex < _song.Patterns.Count;

        if (patternChanged)
        {
            // Update backing fields directly — skip the CurrentPatternIndex setter
            // so we don't fire RefreshRows twice (we do it once below).
            _currentPatternIndex = patternIndex;
            _currentPattern = _song.Patterns[patternIndex];
            OnPropertyChanged(nameof(CurrentPatternIndex));
            OnPropertyChanged(nameof(CurrentPattern));
            OnPropertyChanged(nameof(ChannelCount));
            RefreshRows(); // one rebuild per pattern change, not per row
        }

        // Update cursor row cheaply.
        CurrentRow = Math.Clamp(row, 0, (_currentPattern?.RowCount - 1) ?? 0);
    }

    /// <summary>
    /// Executes the RefreshPatterns operation.
    /// </summary>
    public void RefreshPatterns()
    {
        Patterns.Clear();
        if (_song is null) return;
        foreach (var p in _song.Patterns) Patterns.Add(p);
        if (Patterns.Count == 0)
        {
            _currentPattern = null;
            _currentPatternIndex = 0;
            OnPropertyChanged(nameof(CurrentPatternIndex));
            OnPropertyChanged(nameof(CurrentPattern));
        }
        else
        {
            int target = _currentPatternIndex;
            if (target < 0 || target >= Patterns.Count)
            {
                target = _song.OrderList.Count > 0 && (uint)_song.OrderList[0] < (uint)Patterns.Count
                    ? _song.OrderList[0]
                    : 0;
            }

            CurrentPatternIndex = target;
        }
        OnPropertyChanged(nameof(CurrentChannelName));
        OnPropertyChanged(nameof(CurrentChannelInstrumentLabel));
    }

    /// <summary>
    /// Executes the RefreshRows operation.
    /// </summary>
    public void RefreshRows()
    {
        Rows.Clear();
        if (_currentPattern is null) return;
        _rowsPerBeat = _song?.RowsPerBeat ?? 4;

        for (int r = 0; r < _currentPattern.RowCount; r++)
        {
            var row = new PatternRow(r, _currentPattern.ChannelCount, _rowsPerBeat);
            for (int ch = 0; ch < _currentPattern.ChannelCount; ch++)
                row.Refresh(ch, _currentPattern.GetNote(r, ch));
            Rows.Add(row);
        }
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the DuplicatePattern operation.
    /// </summary>
    private void DuplicatePattern()
    {
        if (_song is null || _currentPattern is null) return;

        int sourceIndex = _currentPatternIndex;
        _main.BeginHistory("Duplicate pattern");

        var clone = _currentPattern.Clone();
        clone.Name = string.IsNullOrWhiteSpace(clone.Name)
            ? $"Pattern {_song.Patterns.Count:D2}"
            : $"{clone.Name} Copy";

        _song.Patterns.Add(clone);
        _song.OrderList.Add(_song.Patterns.Count - 1);
        RefreshPatterns();
        CurrentPatternIndex = _song.Patterns.Count - 1;
        AppLogger.Info($"[PatternEditor] DuplicatePattern source={sourceIndex} newIndex={_song.Patterns.Count - 1} rows={clone.RowCount} channels={clone.ChannelCount}");
        PatternDataChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the NotifyPatternDataChanged operation.
    /// </summary>
    public void NotifyPatternDataChanged()
    {
        PatternDataChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>View-model for a single pattern row, exposed for ItemsControl binding.</summary>
public sealed class PatternRow : BaseViewModel
{
    /// <summary>
    /// Stores or exposes RowIndex.
    /// </summary>
    public int RowIndex { get; }
    /// <summary>
    /// Stores or exposes ChannelCount.
    /// </summary>
    public int ChannelCount { get; }
    /// <summary>
    /// Stores or exposes IsBarStart.
    /// </summary>
    public bool IsBarStart { get; }

    /// <summary>
    /// Stores or exposes Cells.
    /// </summary>
    public List<PatternCell> Cells { get; } = [];

    public PatternRow(int rowIndex, int channelCount, int rowsPerBeat)
    {
        RowIndex = rowIndex;
        ChannelCount = channelCount;
        IsBarStart = rowIndex % rowsPerBeat == 0;
        for (int i = 0; i < channelCount; i++)
            Cells.Add(new PatternCell());
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh(int channel, Note note)
    {
        if (channel >= Cells.Count) return;
        Cells[channel].Update(note);
    }

    /// <summary>
    /// Stores or exposes RowLabel.
    /// </summary>
    public string RowLabel => $"{RowIndex:D3}";
}

/// <summary>View-model for one note cell (one channel column in a row).</summary>
public sealed class PatternCell : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _noteStr.
    /// </summary>
    private string _noteStr = "---";
    /// <summary>
    /// Stores or exposes _instStr.
    /// </summary>
    private string _instStr = "--";
    /// <summary>
    /// Stores or exposes _volStr.
    /// </summary>
    private string _volStr = "--";
    /// <summary>
    /// Stores or exposes _effStr.
    /// </summary>
    private string _effStr = ".00";

    /// <summary>
    /// Executes the NoteStr operation.
    /// </summary>
    public string NoteStr { get => _noteStr; private set => SetField(ref _noteStr, value); }
    /// <summary>
    /// Executes the InstStr operation.
    /// </summary>
    public string InstStr { get => _instStr; private set => SetField(ref _instStr, value); }
    /// <summary>
    /// Executes the VolStr operation.
    /// </summary>
    public string VolStr { get => _volStr; private set => SetField(ref _volStr, value); }
    /// <summary>
    /// Executes the EffStr operation.
    /// </summary>
    public string EffStr { get => _effStr; private set => SetField(ref _effStr, value); }

    /// <summary>
    /// Executes the Update operation.
    /// </summary>
    public void Update(Note note)
    {
        NoteStr = note.Pitch == 0 && note.InstrumentIndex == 0 ? "---" : note.NoteName;
        InstStr = note.InstrumentIndex == 0 ? "--" : $"{note.InstrumentIndex:D2}";
        VolStr = note.Volume == 255 ? "--" : $"{note.Volume:D2}";

        // Show effect as one hex digit + two hex param digits (e.g. "A50", "100").
        // Using straight hex avoids letter-offset bugs; the format mirrors OpenMPT.
        int eff = (int)(byte)note.Effect;
        EffStr = eff == 0 && note.EffectParam == 0
            ? ".00"
            : $"{eff:X1}{note.EffectParam:X2}";
    }
}
