using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using amChipper.App.Commands;
using amChipper.App.Services;
using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents the InstrumentBrowserViewModel component.
/// </summary>
public sealed class InstrumentBrowserViewModel : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _main.
    /// </summary>
    private readonly MainViewModel _main;
    /// <summary>
    /// Stores or exposes _song.
    /// </summary>
    private Song? _song;
    /// <summary>
    /// Stores or exposes _hasExplicitSelection.
    /// </summary>
    private bool _hasExplicitSelection;

    /// <summary>
    /// Stores or exposes Instruments.
    /// </summary>
    public ObservableCollection<Instrument> Instruments { get; } = [];

    /// <summary>
    /// Stores or exposes _selectedInstrument.
    /// </summary>
    private Instrument? _selectedInstrument;
    /// <summary>
    /// Stores or exposes SelectedInstrument.
    /// </summary>
    public Instrument? SelectedInstrument
    {
        get => _selectedInstrument;
        set
        {
            SelectInstrument(value, explicitSelection: true);
        }
    }

    /// <summary>
    /// Stores or exposes HasExplicitSelection.
    /// </summary>
    public bool HasExplicitSelection => _hasExplicitSelection && _selectedInstrument is not null;

    /// <summary>
    /// Stores or exposes _selectedSample.
    /// </summary>
    private Sample? _selectedSample;
    /// <summary>
    /// Executes the SelectedSample operation.
    /// </summary>
    public Sample? SelectedSample { get => _selectedSample; set => SetField(ref _selectedSample, value); }

    /// <summary>
    /// Stores or exposes Samples.
    /// </summary>
    public ObservableCollection<Sample> Samples { get; } = [];

    /// <summary>Source-type choices for the instrument editor.</summary>
    public IReadOnlyList<InstrumentSourceType> SourceTypeOptions { get; } = Enum.GetValues<InstrumentSourceType>();

    /// <summary>Waveform choices for native synth instruments.</summary>
    public IReadOnlyList<SynthWaveform> WaveformOptions { get; } = Enum.GetValues<SynthWaveform>();

    /// <summary>
    /// Stores or exposes ImportSampleCommand.
    /// </summary>
    public ICommand ImportSampleCommand { get; }
    /// <summary>
    /// Stores or exposes DeleteInstrumentCommand.
    /// </summary>
    public ICommand DeleteInstrumentCommand { get; }
    /// <summary>
    /// Stores or exposes ApplyInstrumentSettingsCommand.
    /// </summary>
    public ICommand ApplyInstrumentSettingsCommand { get; }

    public InstrumentBrowserViewModel(MainViewModel main)
    {
        _main = main;
        ImportSampleCommand = new RelayCommand(_ => ImportSample(), _ => _selectedInstrument is not null);
        DeleteInstrumentCommand = new RelayCommand(_ => DeleteInstrument(), _ => _selectedInstrument is not null);
        ApplyInstrumentSettingsCommand = new RelayCommand(_ => ApplyInstrumentSettings(), _ => _selectedInstrument is not null);
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
        Instruments.Clear();
        if (_song is null) return;
        foreach (var inst in _song.Instruments)
            Instruments.Add(inst);
        if (_selectedInstrument is not null && Instruments.Contains(_selectedInstrument))
        {
            SelectInstrument(_selectedInstrument, explicitSelection: _hasExplicitSelection);
        }
        else
        {
            SelectInstrument(Instruments.Count > 0 ? Instruments[0] : null, explicitSelection: false);
        }
    }

    /// <summary>
    /// Executes the SelectInstrument operation.
    /// </summary>
    private void SelectInstrument(Instrument? instrument, bool explicitSelection)
    {
        SetField(ref _selectedInstrument, instrument);
        _hasExplicitSelection = explicitSelection && instrument is not null;
        RefreshSamples();
        OnPropertyChanged(nameof(SelectedInstrument));
        _main.PianoRoll.SetInstrument(instrument);
        _main.PatternEditor.DefaultInstrumentIndex = _main.SelectedInstrumentNumber;
        AppLogger.Info($"[InstrumentBrowser] SelectedInstrument number={_main.SelectedInstrumentNumber} name=\"{instrument?.Name ?? "(none)"}\" sampleCount={instrument?.Samples.Count ?? 0} source={instrument?.SourceType.ToString() ?? "none"} explicit={_hasExplicitSelection}");
    }

    /// <summary>
    /// Executes the ImportSample operation.
    /// </summary>
    private void ImportSample()
    {
        if (_selectedInstrument is null || _song is null) return;
        _main.BeginHistory("Import sample");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Sample",
            Filter = "Wave Audio (*.wav)|*.wav|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true)
        {
            _main.CancelHistory();
            return;
        }

        // Minimal WAV loader (PCM only)
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(dlg.FileName);
            var fmt = reader.WaveFormat;
            var data = new byte[reader.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = reader.Read(data, offset, data.Length - offset);
                if (read == 0) break;
                offset += read;
            }

            if (offset != data.Length)
                Array.Resize(ref data, offset);

            var sample = new Sample
            {
                Name = Path.GetFileNameWithoutExtension(dlg.FileName),
                Data = data,
                SampleRate = fmt.SampleRate,
                Channels = fmt.Channels,
                BitsPerSample = fmt.BitsPerSample
            };
            _selectedInstrument.Samples.Add(sample);
            _selectedInstrument.SourceType = InstrumentSourceType.Sample;
            for (int i = 0; i < _selectedInstrument.NoteMap.Length; i++)
                _selectedInstrument.NoteMap[i] = 0;
            RefreshSamples();
            _main.PianoRoll.Refresh();
            _main.StatusText = $"Imported sample: {sample.Name}";
            AppLogger.Info(
                $"[InstrumentBrowser] ImportedSample file=\"{dlg.FileName}\" instrument=\"{_selectedInstrument.Name}\" " +
                $"sample=\"{sample.Name}\" bytes={sample.Data.Length} rate={sample.SampleRate} channels={sample.Channels} bits={sample.BitsPerSample}");
            _main.CommitHistory();
        }
        catch (Exception ex)
        {
            _main.StatusText = $"Sample import failed: {ex.Message}";
            AppLogger.Error(ex, $"[InstrumentBrowser] Sample import failed file=\"{dlg.FileName}\"");
            _main.CancelHistory();
        }
    }

    /// <summary>
    /// Executes the DeleteInstrument operation.
    /// </summary>
    private void DeleteInstrument()
    {
        if (_selectedInstrument is null || _song is null) return;
        _main.BeginHistory("Delete instrument");
        AppLogger.Info($"[InstrumentBrowser] DeleteInstrument name=\"{_selectedInstrument.Name}\"");
        _song.Instruments.Remove(_selectedInstrument);
        Refresh();
        _main.CommitHistory();
    }

    /// <summary>
    /// Executes the RefreshSamples operation.
    /// </summary>
    private void RefreshSamples()
    {
        Samples.Clear();
        if (_selectedInstrument is null) return;
        foreach (var s in _selectedInstrument.Samples) Samples.Add(s);
        SelectedSample = Samples.Count > 0 ? Samples[0] : null;
    }

    /// <summary>
    /// Applies edited advanced instrument settings to playback and document state.
    /// </summary>
    private void ApplyInstrumentSettings()
    {
        if (_selectedInstrument is null)
            return;

        _main.BeginHistory("Instrument settings");
        _selectedInstrument.RootNote = (byte)Math.Clamp((int)_selectedInstrument.RootNote, 0, 127);
        _selectedInstrument.FineTuneCents = Math.Clamp(_selectedInstrument.FineTuneCents, -1200, 1200);
        _selectedInstrument.PulseWidth = Math.Clamp(_selectedInstrument.PulseWidth, 0.05, 0.95);
        _selectedInstrument.AttackMs = Math.Clamp(_selectedInstrument.AttackMs, 0, 10000);
        _selectedInstrument.HoldMs = Math.Clamp(_selectedInstrument.HoldMs, 0, 10000);
        _selectedInstrument.DecayMs = Math.Clamp(_selectedInstrument.DecayMs, 0, 10000);
        _selectedInstrument.ReleaseMs = Math.Clamp(_selectedInstrument.ReleaseMs, 0, 10000);
        _selectedInstrument.SustainLevel = (byte)Math.Clamp((int)_selectedInstrument.SustainLevel, 0, 128);
        _selectedInstrument.MaxPolyphony = Math.Clamp(_selectedInstrument.MaxPolyphony, 0, 256);
        _selectedInstrument.PortaTimeMs = Math.Clamp(_selectedInstrument.PortaTimeMs, 0, 5000);
        _selectedInstrument.LfoAmount = Math.Clamp(_selectedInstrument.LfoAmount, 0, 24);
        _selectedInstrument.LfoSpeedHz = Math.Clamp(_selectedInstrument.LfoSpeedHz, 0, 64);
        _selectedInstrument.ArpRange = Math.Clamp(_selectedInstrument.ArpRange, 0, 48);
        _selectedInstrument.ArpRepeat = Math.Clamp(_selectedInstrument.ArpRepeat, 1, 32);
        _selectedInstrument.EchoFeedback = Math.Clamp(_selectedInstrument.EchoFeedback, 0, 0.98);
        _selectedInstrument.EchoTimeMs = Math.Clamp(_selectedInstrument.EchoTimeMs, 0, 4000);
        _selectedInstrument.FilterCutoff = Math.Clamp(_selectedInstrument.FilterCutoff, 0, 1);
        _selectedInstrument.FilterResonance = Math.Clamp(_selectedInstrument.FilterResonance, 0, 1);
        _main.MarkInstrumentEdited($"Applied instrument settings: {_selectedInstrument.Name}");
        _main.CommitHistory();
    }
}
