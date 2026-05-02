using System.Collections.ObjectModel;
using System.Windows.Input;
using amChipper.App.Commands;
using amChipper.App.Services;
using amChipper.Core.Editing;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the PianoRollViewModel component.
/// </summary>
public sealed class PianoRollViewModel : BaseViewModel
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

    // ── Active instrument / pattern ────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _manualInstrument.
    /// </summary>
    private Instrument? _manualInstrument;
    /// <summary>
    /// Stores or exposes _channelInstrument.
    /// </summary>
    private Instrument? _channelInstrument;
    /// <summary>
    /// Stores or exposes _pattern.
    /// </summary>
    private Pattern? _pattern;
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
            if (_song is null || _song.Patterns.Count == 0)
                return;

            int clamped = Math.Clamp(value, 0, _song.Patterns.Count - 1);
            if (!SetField(ref _currentPatternIndex, clamped))
                return;

            _pattern = _song.Patterns[clamped];
            OnPropertyChanged(nameof(CurrentPatternName));
            OnPropertyChanged(nameof(CurrentPatternLabel));
            RefreshChannelOptions();
            RefreshActiveInstrument();
            RefreshFromPattern();
            AppLogger.Info($"[PianoRoll] SetCurrentPattern pattern={clamped} name=\"{CurrentPatternName}\" rows={_pattern.RowCount} channels={_pattern.ChannelCount}");
        }
    }

    /// <summary>
    /// Executes the SetSong operation.
    /// </summary>
    public void SetSong(Song song)
    {
        _song = song;
        RefreshPatternOptions();
        CurrentPatternIndex = Math.Clamp(CurrentPatternIndex, 0, Math.Max(song.Patterns.Count - 1, 0));
    }

    /// <summary>
    /// Executes the SetInstrument operation.
    /// </summary>
    public void SetInstrument(Instrument? inst)
    {
        _manualInstrument = inst;
        RefreshActiveInstrument();
        AppLogger.Info($"[PianoRoll] SetInstrument name=\"{inst?.Name ?? "(none)"}\" source={inst?.SourceType.ToString() ?? "none"} waveform={inst?.Waveform.ToString() ?? "none"}");
    }

    /// <summary>
    /// Switch the piano roll to display / edit the pattern at <paramref name="patternIndex"/>.
    /// Fires NoteLayoutChanged so the canvas redraws.
    /// </summary>
    public void SetCurrentPattern(int patternIndex)
    {
        CurrentPatternIndex = patternIndex;
    }

    /// <summary>
    /// Executes the InstrumentName operation.
    /// </summary>
    public string InstrumentName => ActiveInstrument?.Name ?? "(none)";

    /// <summary>
    /// Stores or exposes CurrentPatternName.
    /// </summary>
    public string CurrentPatternName =>
        _pattern is null
            ? "(none)"
            : string.IsNullOrWhiteSpace(_pattern.Name)
                ? $"Pattern {_currentPatternIndex:D2}"
                : _pattern.Name;

    /// <summary>
    /// Stores or exposes CurrentPatternLabel.
    /// </summary>
    public string CurrentPatternLabel => _pattern is null
        ? "Pattern --"
        : $"Pattern {Math.Min(_currentPatternIndex + 1, int.MaxValue):D2}: {CurrentPatternName}";

    /// <summary>
    /// Executes the DescribeInstrument operation.
    /// </summary>
    public string DescribeInstrument(byte instrumentIndex)
    {
        if (_song is null || instrumentIndex == 0 || instrumentIndex > _song.Instruments.Count)
            return "Inst --";

        var inst = _song.Instruments[instrumentIndex - 1];
        string sampleLabel = inst.Samples.Count > 0 ? inst.Samples[0].Name : inst.SourceType.ToString();
        return $"Inst {instrumentIndex:D2} {inst.Name} [{sampleLabel}]";
    }

    /// <summary>
    /// Stores or exposes ActiveInstrument.
    /// </summary>
    private Instrument? ActiveInstrument => _main.InstrumentBrowser.HasExplicitSelection
        ? _manualInstrument
        : _channelInstrument ?? _manualInstrument;

    // ── Notes (piano-roll clip) ───────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Notes.
    /// </summary>
    public ObservableCollection<Note> Notes { get; } = [];
    /// <summary>
    /// Stores or exposes PatternOptions.
    /// </summary>
    public ObservableCollection<Pattern> PatternOptions { get; } = [];
    /// <summary>
    /// Stores or exposes EffectRows.
    /// </summary>
    public ObservableCollection<PianoRollEffectRowViewModel> EffectRows { get; } = [];
    /// <summary>
    /// Stores or exposes EffectCommandOptions.
    /// </summary>
    public IReadOnlyList<EffectCommand> EffectCommandOptions { get; } =
        Enum.GetValues<EffectCommand>();
    private readonly Dictionary<Note, PianoRollNoteSource> _noteSources = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Stores or exposes _selectedNote.
    /// </summary>
    private Note? _selectedNote;
    /// <summary>
    /// Stores or exposes SelectedNote.
    /// </summary>
    public Note? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (!SetField(ref _selectedNote, value))
                return;

            SyncSelectedEffectRowToNote();
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    /// <summary>
    /// Stores or exposes _selectedEffectRow.
    /// </summary>
    private PianoRollEffectRowViewModel? _selectedEffectRow;
    /// <summary>
    /// Stores or exposes SelectedEffectRow.
    /// </summary>
    public PianoRollEffectRowViewModel? SelectedEffectRow
    {
        get => _selectedEffectRow;
        set
        {
            if (!SetField(ref _selectedEffectRow, value))
                return;

            OnPropertyChanged(nameof(SelectedEffectCommand));
            OnPropertyChanged(nameof(SelectedEffectColumnHex));
            OnPropertyChanged(nameof(SelectedEffectParamHex));
            OnPropertyChanged(nameof(SelectedVolumeColumnHex));
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    /// <summary>
    /// Stores or exposes SelectedNoteEffectLabel.
    /// </summary>
    public string SelectedNoteEffectLabel => SelectedEffectRow?.Summary ?? "No effect row selected";

    /// <summary>
    /// Stores or exposes SelectedEffectCommand.
    /// </summary>
    public EffectCommand SelectedEffectCommand
    {
        get => SelectedEffectRow?.Effect ?? EffectCommand.None;
        set
        {
            if (SelectedEffectRow is null || SelectedEffectRow.Effect == value)
                return;

            SelectedEffectRow.Effect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    /// <summary>
    /// Stores or exposes SelectedEffectColumnHex.
    /// </summary>
    public string SelectedEffectColumnHex
    {
        get => SelectedEffectRow?.EffectColumn.ToString("X2") ?? "00";
        set
        {
            if (SelectedEffectRow is null)
                return;
            SelectedEffectRow.EffectColumn = ParseHexByte(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    /// <summary>
    /// Stores or exposes SelectedEffectParamHex.
    /// </summary>
    public string SelectedEffectParamHex
    {
        get => SelectedEffectRow?.EffectParam.ToString("X2") ?? "00";
        set
        {
            if (SelectedEffectRow is null)
                return;
            SelectedEffectRow.EffectParam = ParseHexByte(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    /// <summary>
    /// Stores or exposes SelectedVolumeColumnHex.
    /// </summary>
    public string SelectedVolumeColumnHex
    {
        get => SelectedEffectRow?.VolumeColumn.ToString("X2") ?? "00";
        set
        {
            if (SelectedEffectRow is null)
                return;
            SelectedEffectRow.VolumeColumn = ParseHexByte(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNoteEffectLabel));
        }
    }

    // ── View parameters ────────────────────────────────────────────────────────

    /// <summary>Pixels per beat (horizontal zoom).</summary>
    private double _pixelsPerBeat = 80.0;
    /// <summary>
    /// Stores or exposes PixelsPerBeat.
    /// </summary>
    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set { SetField(ref _pixelsPerBeat, Math.Clamp(value, 4, 640)); NoteLayoutChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Pixels per semitone row (vertical zoom).</summary>
    private double _rowHeight = 14.0;
    /// <summary>
    /// Stores or exposes RowHeight.
    /// </summary>
    public double RowHeight
    {
        get => _rowHeight;
        set { SetField(ref _rowHeight, Math.Clamp(value, 2, 72)); NoteLayoutChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Horizontal scroll offset in beats.</summary>
    private double _scrollBeat;
    /// <summary>
    /// Executes the ScrollBeat operation.
    /// </summary>
    public double ScrollBeat { get => _scrollBeat; set { SetField(ref _scrollBeat, Math.Max(0, value)); NoteLayoutChanged?.Invoke(this, EventArgs.Empty); } }

    /// <summary>Vertical scroll offset in semitones from top (C8).</summary>
    private double _scrollPitch;
    /// <summary>
    /// Executes the ScrollPitch operation.
    /// </summary>
    public double ScrollPitch { get => _scrollPitch; set { SetField(ref _scrollPitch, Math.Max(0, value)); NoteLayoutChanged?.Invoke(this, EventArgs.Empty); } }

    /// <summary>
    /// Executes the PlayheadBeat operation.
    /// </summary>
    public double PlayheadBeat { get => _playheadBeat; set { SetField(ref _playheadBeat, value); PlayheadMoved?.Invoke(this, EventArgs.Empty); } }
    /// <summary>
    /// Stores or exposes _playheadBeat.
    /// </summary>
    private double _playheadBeat;

    /// <summary>
    /// Stores or exposes CurrentTool.
    /// </summary>
    public PianoRollTool CurrentTool { get; set; } = PianoRollTool.Draw;

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
            int clamped = Math.Clamp(value, 0, Math.Max(ChannelOptions.Count - 1, 0));
            if (SetField(ref _currentChannel, clamped))
            {
                AppLogger.Info($"[PianoRoll] SetChannel channel={_currentChannel} pattern={_currentPatternIndex}");
                RefreshActiveInstrument();
                RefreshFromPattern();
            }
        }
    }

    /// <summary>
    /// Stores or exposes ChannelOptions.
    /// </summary>
    public ObservableCollection<int> ChannelOptions { get; } = [];

    /// <summary>
    /// Stores or exposes TicksPerBeat.
    /// </summary>
    public double TicksPerBeat => _song?.RowsPerBeat ?? 4;

    // ── Quantise ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _quantise.
    /// </summary>
    private double _quantise = 0.25; // 1/4 beat
    /// <summary>
    /// Executes the Quantise operation.
    /// </summary>
    public double Quantise { get => _quantise; set => SetField(ref _quantise, value); }

    /// <summary>True when the piano roll should accept FL Studio-style typing-keyboard note preview.</summary>
    public bool TypingKeyboardEnabled
    {
        get => _main.PianoRollTypingKeyboardEnabled;
        set => _main.PianoRollTypingKeyboardEnabled = value;
    }

    /// <summary>Base MIDI note for the lower typing-keyboard row.</summary>
    public int TypingKeyboardBaseNote
    {
        get => _main.PianoRollTypingKeyboardBaseNote;
        set => _main.PianoRollTypingKeyboardBaseNote = value;
    }

    /// <summary>Velocity used by typing-keyboard preview.</summary>
    public int TypingKeyboardVelocity
    {
        get => _main.PianoRollTypingKeyboardVelocity;
        set => _main.PianoRollTypingKeyboardVelocity = value;
    }

    public IReadOnlyList<(string Label, double Value)> QuantiseOptions { get; } =
    [
        ("1/1",   1.0),   ("1/2",  0.5),  ("1/4",  0.25),
        ("1/8",  0.125), ("1/16", 0.0625), ("1/32", 0.03125)
    ];

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? NoteLayoutChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? PlayheadMoved;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? PatternDataChanged;

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

    // ── Event raise helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Executes the RaiseNoteLayoutChanged operation.
    /// </summary>
    public void RaiseNoteLayoutChanged() => NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
    /// <summary>
    /// Executes the RaisePlayheadMoved operation.
    /// </summary>
    public void RaisePlayheadMoved() => PlayheadMoved?.Invoke(this, EventArgs.Empty);

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes ZoomInHCommand.
    /// </summary>
    public ICommand ZoomInHCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomOutHCommand.
    /// </summary>
    public ICommand ZoomOutHCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomInVCommand.
    /// </summary>
    public ICommand ZoomInVCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomOutVCommand.
    /// </summary>
    public ICommand ZoomOutVCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomResetCommand.
    /// </summary>
    public ICommand ZoomResetCommand { get; }
    /// <summary>
    /// Stores or exposes ZoomFitCommand.
    /// </summary>
    public ICommand ZoomFitCommand { get; }
    /// <summary>
    /// Stores or exposes PlayLaneCommand.
    /// </summary>
    public ICommand PlayLaneCommand { get; }
    /// <summary>
    /// Stores or exposes StopCommand.
    /// </summary>
    public ICommand StopCommand { get; }
    /// <summary>
    /// Stores or exposes DeleteNoteCommand.
    /// </summary>
    public ICommand DeleteNoteCommand { get; }
    /// <summary>
    /// Stores or exposes ApplyEffectCommand.
    /// </summary>
    public ICommand ApplyEffectCommand { get; }
    /// <summary>
    /// Stores or exposes ClearEffectCommand.
    /// </summary>
    public ICommand ClearEffectCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public PianoRollViewModel(MainViewModel main)
    {
        _main = main;
        ZoomInHCommand = new RelayCommand(_ => PixelsPerBeat *= 1.25);
        ZoomOutHCommand = new RelayCommand(_ => PixelsPerBeat /= 1.25);
        ZoomInVCommand = new RelayCommand(_ => RowHeight *= 1.25);
        ZoomOutVCommand = new RelayCommand(_ => RowHeight /= 1.25);
        ZoomResetCommand = new RelayCommand(_ =>
        {
            PixelsPerBeat = 80;
            RowHeight = 14;
        });
        ZoomFitCommand = new RelayCommand(width =>
        {
            double viewportWidth = width is double d && d > 0 ? d : 1200;
            double lastBeat = Notes.Select(n => (n.StartTick + n.DurationTicks) / Math.Max(TicksPerBeat, 1)).DefaultIfEmpty(16).Max();
            PixelsPerBeat = viewportWidth / Math.Max(lastBeat + 4, 16);
        });
        DeleteNoteCommand = new RelayCommand(_ => DeleteSelected(), _ => _selectedNote is not null);
        ApplyEffectCommand = new RelayCommand(_ => ApplySelectedEffect(), _ => SelectedEffectRow is not null);
        ClearEffectCommand = new RelayCommand(_ => ClearSelectedEffect(), _ => SelectedEffectRow is not null);
        PlayLaneCommand = new RelayCommand(_ => _main.PlayPianoRoll());
        StopCommand = new RelayCommand(_ => _main.StopCommand.Execute(null));
    }

    // ── Note editing ──────────────────────────────────────────────────────────

    /// <summary>Add a note via click on the canvas. Start/duration are in beats.</summary>
    public void AddNote(byte pitch, double startBeat, double durationBeats, byte velocity = 100)
    {
        _main.BeginHistory("Add note");
        // Quantise
        startBeat = Math.Round(startBeat / Quantise) * Quantise;
        durationBeats = Math.Round(durationBeats / Quantise) * Quantise;
        if (durationBeats < Quantise) durationBeats = Quantise;

        long ticksPerBeat = _song?.RowsPerBeat ?? 4;
        var note = new Note
        {
            Pitch = pitch,
            StartTick = (long)(startBeat * ticksPerBeat),
            DurationTicks = (long)(durationBeats * ticksPerBeat),
            Velocity = velocity,
            Volume = (byte)Math.Clamp(velocity / 2, 0, 64),
            InstrumentIndex = ResolveInstrumentIndex()
        };
        Notes.Add(note);
        SelectedNote = note;
        CommitNotesToPattern();
        AppLogger.Info(
            $"[PianoRoll] AddNote pattern={_currentPatternIndex} channel={CurrentChannel} pitch={pitch} " +
            $"startBeat={startBeat:0.###} durationBeat={durationBeats:0.###} velocity={velocity} instrument={note.InstrumentIndex}:{DescribeInstrument(note.InstrumentIndex)}");
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the DeleteSelected operation.
    /// </summary>
    public void DeleteSelected()
    {
        if (_selectedNote is null) return;
        _main.BeginHistory("Delete note");
        Notes.Remove(_selectedNote);
        AppLogger.Info($"[PianoRoll] DeleteNote pattern={_currentPatternIndex} channel={CurrentChannel} pitch={_selectedNote.Pitch} startTick={_selectedNote.StartTick}");
        _selectedNote = null;
        CommitNotesToPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    public void Refresh()
    {
        RefreshPatternOptions();
        RefreshChannelOptions();
        RefreshFromPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the RefreshPatternOptions operation.
    /// </summary>
    private void RefreshPatternOptions()
    {
        PatternOptions.Clear();
        if (_song is null)
            return;

        foreach (var pattern in _song.Patterns)
            PatternOptions.Add(pattern);

        if (_song.Patterns.Count == 0)
            return;

        int clamped = Math.Clamp(_currentPatternIndex, 0, _song.Patterns.Count - 1);
        if (_currentPatternIndex != clamped || _pattern is null)
        {
            _currentPatternIndex = clamped;
            _pattern = _song.Patterns[clamped];
            OnPropertyChanged(nameof(CurrentPatternIndex));
            OnPropertyChanged(nameof(CurrentPatternName));
            OnPropertyChanged(nameof(CurrentPatternLabel));
        }
    }

    /// <summary>
    /// Executes the RefreshChannelOptions operation.
    /// </summary>
    private void RefreshChannelOptions()
    {
        ChannelOptions.Clear();
        int count = Math.Max(_pattern?.ChannelCount ?? 1, 1);
        for (int i = 0; i < count; i++)
            ChannelOptions.Add(i);

        if (_currentChannel >= count)
            _currentChannel = count - 1;

        OnPropertyChanged(nameof(CurrentChannel));
    }

    /// <summary>
    /// Executes the CommitNotesToPattern operation.
    /// </summary>
    public void CommitNotesToPattern(bool notify = true)
    {
        if (_pattern is null) return;

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        var result = PianoRollLaneCommitter.Commit(_pattern, channel, Notes, _noteSources, ResolveInstrumentIndex);
        ReindexNoteSources();
        AppLogger.Debug($"[PianoRoll] CommitNotes pattern={_currentPatternIndex} channel={channel} noteCount={Notes.Count} committed={result.CommittedNotes} sourceEffectRows={result.SourceEffectRows} effectsAfter={result.EffectsAfter} preserveEffects=CoreSpan notify={notify}");
        RefreshEffectRows();
        if (notify)
            PatternDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the SetNoteVelocity operation.
    /// </summary>
    public void SetNoteVelocity(Note note, byte velocity, bool notify = false)
    {
        note.Velocity = velocity;
        note.Volume = (byte)Math.Clamp(velocity / 2, 0, 64);

        if (_pattern is not null)
        {
            int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
            int row = (int)Math.Clamp(note.StartTick, 0, _pattern.RowCount - 1);
            var patternNote = _pattern.GetNote(row, channel);
            if (patternNote.Pitch == note.Pitch)
            {
                patternNote.Velocity = velocity;
                patternNote.Volume = note.Volume;
                if (patternNote.VolumeColumn is >= 0x10 and <= 0x50)
                    patternNote.VolumeColumn = 0;
            }
        }

        if (notify)
            PatternDataChanged?.Invoke(this, EventArgs.Empty);

        AppLogger.Debug($"[PianoRoll] SetVelocity pattern={_currentPatternIndex} channel={CurrentChannel} pitch={note.Pitch} startTick={note.StartTick} velocity={velocity} volume={note.Volume} notify={notify}");
    }

    /// <summary>Moves one note by semitones and commits the lane back to the pattern.</summary>
    public void TransposeNote(Note note, int semitones)
    {
        if (!Notes.Contains(note))
            return;

        _main.BeginHistory("Transpose note");
        note.Pitch = (byte)Math.Clamp(note.Pitch + semitones, 0, 127);
        SelectedNote = note;
        CommitNotesToPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>Scales one note duration and commits the lane back to the pattern.</summary>
    public void ScaleNoteDuration(Note note, double multiplier)
    {
        if (!Notes.Contains(note))
            return;

        _main.BeginHistory("Resize note");
        note.DurationTicks = Math.Max(1, (long)Math.Round(note.DurationTicks * Math.Max(0.0625, multiplier)));
        SelectedNote = note;
        CommitNotesToPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>Duplicates a note one quantise step later.</summary>
    public void DuplicateNote(Note note)
    {
        if (!Notes.Contains(note))
            return;

        _main.BeginHistory("Duplicate note");
        var clone = note.Clone();
        clone.StartTick = Math.Max(0, clone.StartTick + Math.Max(1, (long)Math.Round(Quantise * TicksPerBeat)));
        Notes.Add(clone);
        SelectedNote = clone;
        CommitNotesToPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the ReplaceCurrentLaneNotes operation.
    /// </summary>
    public void ReplaceCurrentLaneNotes(IReadOnlyList<Note> importedNotes)
    {
        if (_pattern is null)
            return;

        _main.BeginHistory("Import MIDI");
        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        long endTick = importedNotes.Count == 0 ? _pattern.RowCount : importedNotes.Max(n => n.StartTick + Math.Max(1, n.DurationTicks));
        if (endTick > _pattern.RowCount)
            _pattern.Resize((int)Math.Min(Math.Max(endTick, _pattern.RowCount), 512), _pattern.ChannelCount);

        Notes.Clear();
        byte fallbackInstrument = ResolveInstrumentIndex();
        foreach (var imported in importedNotes.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch))
        {
            var note = imported.Clone();
            note.StartTick = Math.Clamp(note.StartTick, 0, _pattern.RowCount - 1);
            note.DurationTicks = Math.Max(1, Math.Min(note.DurationTicks, _pattern.RowCount - note.StartTick));
            if (note.InstrumentIndex == 0)
                note.InstrumentIndex = fallbackInstrument;
            if (note.Volume == 255)
                note.Volume = (byte)Math.Clamp(note.Velocity / 2, 0, 64);
            Notes.Add(note);
        }

        CommitNotesToPattern();
        AppLogger.Info($"[PianoRoll] Imported lane notes pattern={_currentPatternIndex} channel={channel} notes={Notes.Count}");
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the PreviewPitch operation.
    /// </summary>
    public void PreviewPitch(int pitch, byte? velocity = null)
    {
        int instrumentIndex = Math.Max(ResolveInstrumentIndex() - 1, 0);
        byte previewVelocity = velocity ?? SelectedNote?.Velocity ?? 110;
        int milliseconds = _main.NotePreviewMode switch
        {
            "Fixed 1/4 Note" => Math.Max(40, (int)Math.Round(60000.0 / Math.Max(_song?.Bpm ?? 125, 1) / 4.0)),
            "Fixed 1 Bar" => Math.Max(40, (int)Math.Round(60000.0 / Math.Max(_song?.Bpm ?? 125, 1) * 4.0)),
            _ => 60000
        };
        AppLogger.Info($"[PianoRoll] PreviewPitch pitch={pitch} instrumentIndex={instrumentIndex}:{DescribeInstrument((byte)(instrumentIndex + 1))} channel={CurrentChannel} velocity={previewVelocity} ms={milliseconds}");
        _main.Audio.PreviewNote(
            _main.Song,
            pitch,
            instrumentIndex,
            CurrentChannel,
            previewVelocity,
            milliseconds);
    }

    /// <summary>
    /// Executes the StopPreviewPitch operation.
    /// </summary>
    public void StopPreviewPitch()
    {
        _main.Audio.StopPreviewNote(CurrentChannel);
    }

    /// <summary>
    /// Executes the DeleteNote operation.
    /// </summary>
    public void DeleteNote(Note note)
    {
        if (!Notes.Contains(note))
            return;

        _main.BeginHistory("Delete note");
        Notes.Remove(note);
        if (SelectedNote == note)
            SelectedNote = null;
        AppLogger.Info($"[PianoRoll] DeleteNote pattern={_currentPatternIndex} channel={CurrentChannel} pitch={note.Pitch} startTick={note.StartTick}");
        CommitNotesToPattern();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the RefreshFromPattern operation.
    /// </summary>
    private void RefreshFromPattern()
    {
        Notes.Clear();
        _noteSources.Clear();

        if (_pattern is null)
        {
            RefreshActiveInstrument();
            NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        RefreshActiveInstrument();
        int fallbackDuration = Math.Max(1, _song?.RowsPerBeat ?? 4);

        for (int row = 0; row < _pattern.RowCount; row++)
        {
            var cell = _pattern.GetNote(row, channel);
            if (cell.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            int end = FindNoteEnd(row + 1, channel);
            if (end <= row)
                end = Math.Min(_pattern.RowCount, row + fallbackDuration);

            var note = cell.Clone();
            note.StartTick = row;
            note.DurationTicks = Math.Max(1, end - row);
            note.Velocity = cell.Volume == 255
                ? (byte)100
                : (byte)Math.Clamp(cell.Volume * 2, 1, 127);
            Notes.Add(note);
            _noteSources[note] = PianoRollLaneCommitter.CaptureSource(_pattern, row, end, channel);
        }

        AppLogger.Debug($"[PianoRoll] RefreshFromPattern pattern={_currentPatternIndex} channel={channel} loadedNotes={Notes.Count}");
        RefreshEffectRows();
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the RefreshEffectRows operation.
    /// </summary>
    private void RefreshEffectRows()
    {
        EffectRows.Clear();
        if (_pattern is null)
        {
            SelectedEffectRow = null;
            return;
        }

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        for (int row = 0; row < _pattern.RowCount; row++)
        {
            var note = _pattern.GetNote(row, channel);
            if (!HasTrackerEffect(note))
                continue;

            EffectRows.Add(new PianoRollEffectRowViewModel(row, note));
        }

        SyncSelectedEffectRowToNote();
    }

    /// <summary>
    /// Executes the SyncSelectedEffectRowToNote operation.
    /// </summary>
    private void SyncSelectedEffectRowToNote()
    {
        if (_selectedNote is null)
        {
            SelectedEffectRow ??= EffectRows.FirstOrDefault();
            return;
        }

        int row = (int)Math.Clamp(_selectedNote.StartTick, 0, Math.Max((_pattern?.RowCount ?? 1) - 1, 0));
        SelectedEffectRow = EffectRows.FirstOrDefault(r => r.Row == row) ?? SelectedEffectRow;
    }

    /// <summary>
    /// Executes the ApplySelectedEffect operation.
    /// </summary>
    private void ApplySelectedEffect()
    {
        if (_pattern is null || SelectedEffectRow is null)
            return;

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        int row = Math.Clamp(SelectedEffectRow.Row, 0, Math.Max(_pattern.RowCount - 1, 0));
        var note = _pattern.GetNote(row, channel);

        _main.BeginHistory("Edit tracker effect");
        note.Effect = SelectedEffectRow.Effect;
        note.EffectColumn = SelectedEffectRow.EffectColumn;
        note.EffectParam = SelectedEffectRow.EffectParam;
        note.VolumeColumn = SelectedEffectRow.VolumeColumn;
        _pattern.SetNote(row, channel, note);

        var pianoNote = Notes.FirstOrDefault(n => n.StartTick == row);
        if (pianoNote is not null)
        {
            pianoNote.Effect = note.Effect;
            pianoNote.EffectColumn = note.EffectColumn;
            pianoNote.EffectParam = note.EffectParam;
            pianoNote.VolumeColumn = note.VolumeColumn;
        }

        AppLogger.Info($"[PianoRollFX] Apply pattern={_currentPatternIndex} channel={channel} row={row} effect={note.Effect} raw={note.EffectColumn:X2} param={note.EffectParam:X2} volcol={note.VolumeColumn:X2}");
        RefreshEffectRows();
        PatternDataChanged?.Invoke(this, EventArgs.Empty);
        NoteLayoutChanged?.Invoke(this, EventArgs.Empty);
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the ClearSelectedEffect operation.
    /// </summary>
    private void ClearSelectedEffect()
    {
        if (SelectedEffectRow is null)
            return;

        SelectedEffectRow.Effect = EffectCommand.None;
        SelectedEffectRow.EffectColumn = 0;
        SelectedEffectRow.EffectParam = 0;
        SelectedEffectRow.VolumeColumn = 0;
        ApplySelectedEffect();
    }

    /// <summary>
    /// Executes the HasTrackerEffect operation.
    /// </summary>
    private static bool HasTrackerEffect(Note note) => PianoRollLaneCommitter.HasTrackerEffect(note);

    /// <summary>
    /// Executes the ParseHexByte operation.
    /// </summary>
    private static byte ParseHexByte(string value)
    {
        string clean = new((value ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
        if (clean.Length == 0)
            return 0;
        if (clean.Length > 2)
            clean = clean[^2..];
        return byte.TryParse(clean, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte parsed)
            ? parsed
            : (byte)0;
    }

    /// <summary>
    /// Executes the FindNoteEnd operation.
    /// </summary>
    private int FindNoteEnd(int startRow, int channel)
    {
        if (_pattern is null) return startRow;

        for (int row = startRow; row < _pattern.RowCount; row++)
        {
            var cell = _pattern.GetNote(row, channel);
            if (cell.Pitch == (byte)SpecialNote.NoteOff || cell.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                return row;
        }

        return Math.Min(_pattern.RowCount, startRow + Math.Max(1, _song?.RowsPerBeat ?? 4));
    }

    /// <summary>
    /// Executes the ReindexNoteSources operation.
    /// </summary>
    private void ReindexNoteSources()
    {
        _noteSources.Clear();
        if (_pattern is null)
            return;

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        foreach (var note in Notes)
        {
            int row = (int)Math.Clamp(note.StartTick, 0, Math.Max(_pattern.RowCount - 1, 0));
            int end = FindNoteEnd(row + 1, channel);
            _noteSources[note] = PianoRollLaneCommitter.CaptureSource(_pattern, row, end, channel);
        }
    }

    /// <summary>
    /// Executes the ResolveInstrumentIndex operation.
    /// </summary>
    private byte ResolveInstrumentIndex()
    {
        if (_song is null)
            return 1;

        var activeInstrument = ActiveInstrument;
        if (activeInstrument is not null)
        {
            int index = _song.Instruments.IndexOf(activeInstrument);
            if (index >= 0)
                return (byte)(index + 1);
        }

        var channelInstrument = ResolveChannelInstrumentIndex();
        if (channelInstrument > 0)
            return (byte)channelInstrument;

        return 1;
    }

    /// <summary>
    /// Executes the ResolveChannelInstrumentIndex operation.
    /// </summary>
    private int ResolveChannelInstrumentIndex()
    {
        if (_pattern is null)
            return 0;

        int channel = Math.Clamp(CurrentChannel, 0, Math.Max(_pattern.ChannelCount - 1, 0));
        for (int row = 0; row < _pattern.RowCount; row++)
        {
            var cell = _pattern.GetNote(row, channel);
            if (cell.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            if (cell.InstrumentIndex > 0)
                return cell.InstrumentIndex;
        }

        return 0;
    }

    /// <summary>
    /// Executes the RefreshActiveInstrument operation.
    /// </summary>
    private void RefreshActiveInstrument()
    {
        if (_song is null)
        {
            _channelInstrument = null;
            OnPropertyChanged(nameof(InstrumentName));
            return;
        }

        int channelInstrumentIndex = ResolveChannelInstrumentIndex();
        _channelInstrument = channelInstrumentIndex > 0 && channelInstrumentIndex <= _song.Instruments.Count
            ? _song.Instruments[channelInstrumentIndex - 1]
            : null;

        OnPropertyChanged(nameof(InstrumentName));
    }

    // ── Helpers used by the Canvas ────────────────────────────────────────────

    /// <summary>Convert a MIDI pitch to canvas Y coordinate.</summary>
    public double PitchToY(int pitch) =>
        (127 - pitch) * RowHeight;

    /// <summary>Convert a beat position to canvas X coordinate.</summary>
    public double BeatToX(double beat) =>
        beat * PixelsPerBeat;

    /// <summary>Reverse: canvas X → beat.</summary>
    public double XToBeat(double x) =>
        x / PixelsPerBeat;

    /// <summary>Reverse: canvas Y → MIDI pitch.</summary>
    public int YToPitch(double y) =>
        127 - (int)Math.Floor(y / RowHeight);

}

/// <summary>
/// Represents the PianoRollEffectRowViewModel component.
/// </summary>
public sealed class PianoRollEffectRowViewModel(int row, Note source) : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _effect.
    /// </summary>
    private EffectCommand _effect = source.Effect;
    /// <summary>
    /// Stores or exposes _effectColumn.
    /// </summary>
    private byte _effectColumn = source.EffectColumn;
    /// <summary>
    /// Stores or exposes _effectParam.
    /// </summary>
    private byte _effectParam = source.EffectParam;
    /// <summary>
    /// Stores or exposes _volumeColumn.
    /// </summary>
    private byte _volumeColumn = source.VolumeColumn;

    /// <summary>
    /// Stores or exposes Row.
    /// </summary>
    public int Row { get; } = row;
    /// <summary>
    /// Stores or exposes RowLabel.
    /// </summary>
    public string RowLabel => $"{Row:D3}";
    /// <summary>
    /// Executes the NoteLabel operation.
    /// </summary>
    public string NoteLabel => source.Pitch is > 0 and < (byte)SpecialNote.NoteOff ? source.NoteName : "---";

    /// <summary>
    /// Stores or exposes Effect.
    /// </summary>
    public EffectCommand Effect
    {
        get => _effect;
        set
        {
            if (SetField(ref _effect, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// Stores or exposes EffectColumn.
    /// </summary>
    public byte EffectColumn
    {
        get => _effectColumn;
        set
        {
            if (SetField(ref _effectColumn, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// Stores or exposes EffectParam.
    /// </summary>
    public byte EffectParam
    {
        get => _effectParam;
        set
        {
            if (SetField(ref _effectParam, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// Stores or exposes VolumeColumn.
    /// </summary>
    public byte VolumeColumn
    {
        get => _volumeColumn;
        set
        {
            if (SetField(ref _volumeColumn, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// Executes the Summary operation.
    /// </summary>
    public string Summary => IsSidTraceCell()
        ? $"Row {Row:D3} {NoteLabel}  {FormatSidTraceCell()}  PW {EffectColumn:X2}  CTRL {EffectParam:X2}"
        : $"Row {Row:D3} {NoteLabel}  FX {FormatEffectColumn(EffectColumn, Effect)}{EffectParam:X2}  VOL {VolumeColumn:X2}";

    /// <summary>
    /// Executes the IsSidTraceCell operation.
    /// </summary>
    private bool IsSidTraceCell() =>
        source.InstrumentIndex is >= 1 and <= 3 &&
        VolumeColumn is 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80;

    /// <summary>
    /// Executes the FormatSidTraceCell operation.
    /// </summary>
    private string FormatSidTraceCell()
    {
        string wave = (VolumeColumn & 0xF0) switch
        {
            0x10 => "SID SAW",
            0x20 => "SID TRI",
            0x40 => "SID PULSE",
            0x80 => "SID NOISE",
            0x30 => "SID SAW+TRI",
            0x50 => "SID SAW+PULSE",
            0x60 => "SID TRI+PULSE",
            0x70 => "SID MIX",
            _ => "SID"
        };

        var flags = new List<string>();
        if ((EffectParam & 0x01) != 0) flags.Add("GATE");
        if ((EffectParam & 0x02) != 0) flags.Add("SYNC");
        if ((EffectParam & 0x04) != 0) flags.Add("RING");
        if ((EffectParam & 0x08) != 0) flags.Add("TEST");
        return flags.Count == 0 ? wave : $"{wave} {string.Join('+', flags)}";
    }

    /// <summary>
    /// Executes the FormatEffectColumn operation.
    /// </summary>
    private static string FormatEffectColumn(byte effectColumn, EffectCommand effect)
    {
        byte command = effectColumn != 0 || effect == EffectCommand.None
            ? effectColumn
            : (byte)effect;

        return command < 0x0A
            ? command.ToString("X1")
            : ((char)('A' + command - 0x0A)).ToString();
    }
}
