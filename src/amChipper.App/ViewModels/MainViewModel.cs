using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using amChipper.App.Commands;
using amChipper.App.Services;
using amChipper.App.Views;
using amChipper.Audio.Engine;
using amChipper.Core.Models;
using amChipper.Core.Persistence;
using NAudio.Wave;
using PlaybackState = amChipper.Core.Models.PlaybackState;

namespace amChipper.App.ViewModels;

/// <summary>Root ViewModel wired to MainWindow.  Owns the Song and AudioEngine.</summary>
public sealed class MainViewModel : BaseViewModel
{
    // ── Services ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the Audio operation.
    /// </summary>
    public AudioEngine Audio { get; } = new AudioEngine(AppLogger.Instance);
    private int _chipPreviewRenderTicket;
    private bool _chipPreviewRenderInFlight;

    /// <summary>
    /// Stores or exposes MasterVolume.
    /// </summary>
    public float MasterVolume
    {
        get => Audio.MasterVolume;
        set
        {
            Audio.MasterVolume = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Stores or exposes _masterMeterLevel.
    /// </summary>
    private double _masterMeterLevel;
    /// <summary>
    /// Stores or exposes MasterMeterLevel.
    /// </summary>
    public double MasterMeterLevel
    {
        get => _masterMeterLevel;
        private set => SetField(ref _masterMeterLevel, Math.Clamp(value, 0d, 1d));
    }

    /// <summary>
    /// Stores or exposes SpectrumBands.
    /// </summary>
    public ObservableCollection<SpectrumBandViewModel> SpectrumBands { get; } = [];
    /// <summary>
    /// Stores or exposes ThemeOptions.
    /// </summary>
    public ObservableCollection<string> ThemeOptions { get; } =
        ["FL Grape", "Neon Studio", "Classic Tracker", "Amber CRT", "Midnight Pro", "Ice Matrix", "Magenta Circuit", "Carbon Lime", "Ruby Wave", "Ocean Lab", "Steel Mono", "Sunset Pop"];
    /// <summary>
    /// Stores or exposes MidiExportPatternDefaults.
    /// </summary>
    public ObservableCollection<string> MidiExportPatternDefaults { get; } = ["Current Pattern", "Song Order", "All Patterns"];
    /// <summary>
    /// Stores or exposes NotePreviewModes.
    /// </summary>
    public ObservableCollection<string> NotePreviewModes { get; } = ["Hold While Pressed", "Fixed 1/4 Note", "Fixed 1 Bar"];
    /// <summary>
    /// Stores or exposes WorkspaceDensityOptions.
    /// </summary>
    public ObservableCollection<string> WorkspaceDensityOptions { get; } = ["Compact", "Balanced", "Spacious"];
    /// <summary>
    /// Stores or exposes ToolbarButtonSizeOptions.
    /// </summary>
    public ObservableCollection<string> ToolbarButtonSizeOptions { get; } = ["Compact", "Balanced", "Large"];
    /// <summary>
    /// Stores or exposes SidXmExportModes.
    /// </summary>
    public ObservableCollection<string> SidXmExportModes { get; } = ["Rendered Mix Only", "Rendered Mix + Trace", "Trace Only"];
    public ObservableCollection<string> LanguageOptions { get; } = new(AppHelpContent.Languages);
    /// <summary>
    /// Stores or exposes AudioOutputDevices.
    /// </summary>
    public ObservableCollection<string> AudioOutputDevices { get; } = [];
    /// <summary>
    /// Stores or exposes AudioSampleRates.
    /// </summary>
    public ObservableCollection<int> AudioSampleRates { get; } = [44100, 48000, 88200, 96000];
    /// <summary>
    /// Stores or exposes SpectrumAnalyzerModes.
    /// </summary>
    public ObservableCollection<string> SpectrumAnalyzerModes { get; } = ["Studio Analyzer", "Compact Bars", "Peak Focus"];

    /// <summary>
    /// Executes the this operation.
    /// </summary>
    public string this[string key]
    {
        get => AppHelpContent.Translate(SelectedLanguage, key);
        set { }
    }

    /// <summary>
    /// Width of the left instrument/browser pane persisted in the user configuration.
    /// </summary>
    private double _mainLeftPanelWidth = 200;

    /// <summary>
    /// Width of the left instrument/browser pane persisted in the user configuration.
    /// </summary>
    public double MainLeftPanelWidth
    {
        get => _mainLeftPanelWidth;
        set => SetField(ref _mainLeftPanelWidth, Math.Clamp(value, 140, 320));
    }

    /// <summary>
    /// User-defined order for the main editor/rack tabs.
    /// </summary>
    public string[] MainTabOrder { get; set; } = [];

    /// <summary>
    /// Stores or exposes _selectedTheme.
    /// </summary>
    private string _selectedTheme = "FL Grape";
    /// <summary>
    /// Stores or exposes SelectedTheme.
    /// </summary>
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetField(ref _selectedTheme, value))
                ApplyTheme(value);
        }
    }

    /// <summary>
    /// Stores or exposes _showUiShine.
    /// </summary>
    private bool _showUiShine = true;
    /// <summary>
    /// Stores or exposes ShowUiShine.
    /// </summary>
    public bool ShowUiShine
    {
        get => _showUiShine;
        set
        {
            if (SetField(ref _showUiShine, value))
                ApplyUiChromeSettings();
        }
    }

    /// <summary>
    /// Stores or exposes ProjectTitleEdit.
    /// </summary>
    public string ProjectTitleEdit
    {
        get => _song.Title;
        set
        {
            string title = string.IsNullOrWhiteSpace(value) ? "Untitled" : value.Trim();
            if (_song.Title == title)
                return;
            _song.Title = title;
            MarkDirty(useNativePlayback: true);
            NotifyProjectHubChanged();
        }
    }

    /// <summary>
    /// Stores or exposes ProjectArtistEdit.
    /// </summary>
    public string ProjectArtistEdit
    {
        get => _song.Artist;
        set
        {
            string artist = value?.Trim() ?? string.Empty;
            if (_song.Artist == artist)
                return;
            _song.Artist = artist;
            MarkDirty(useNativePlayback: true);
            NotifyProjectHubChanged();
        }
    }

    /// <summary>
    /// Stores or exposes _selectedAudioOutputDevice.
    /// </summary>
    private string _selectedAudioOutputDevice = "Default WaveOut";
    /// <summary>
    /// Stores or exposes SelectedAudioOutputDevice.
    /// </summary>
    public string SelectedAudioOutputDevice
    {
        get => _selectedAudioOutputDevice;
        set => SetField(ref _selectedAudioOutputDevice, value);
    }

    /// <summary>
    /// Stores or exposes _audioSampleRate.
    /// </summary>
    private int _audioSampleRate = 44100;
    /// <summary>
    /// Stores or exposes AudioSampleRate.
    /// </summary>
    public int AudioSampleRate
    {
        get => _audioSampleRate;
        set => SetField(ref _audioSampleRate, value);
    }

    /// <summary>
    /// Stores or exposes _audioLatencyMs.
    /// </summary>
    private int _audioLatencyMs = 200;
    /// <summary>
    /// Stores or exposes AudioLatencyMs.
    /// </summary>
    public int AudioLatencyMs
    {
        get => _audioLatencyMs;
        set => SetField(ref _audioLatencyMs, Math.Clamp(value, 40, 1000));
    }

    /// <summary>
    /// Stores or exposes _audioBufferCount.
    /// </summary>
    private int _audioBufferCount = 4;
    /// <summary>
    /// Stores or exposes AudioBufferCount.
    /// </summary>
    public int AudioBufferCount
    {
        get => _audioBufferCount;
        set => SetField(ref _audioBufferCount, Math.Clamp(value, 2, 8));
    }

    /// <summary>
    /// Stores or exposes _showPanelShadows.
    /// </summary>
    private bool _showPanelShadows = true;
    /// <summary>
    /// Stores or exposes ShowPanelShadows.
    /// </summary>
    public bool ShowPanelShadows
    {
        get => _showPanelShadows;
        set
        {
            if (SetField(ref _showPanelShadows, value))
                ApplyUiChromeSettings();
        }
    }

    /// <summary>
    /// Stores or exposes _visualizerIntensity.
    /// </summary>
    private double _visualizerIntensity = 1.0;
    /// <summary>
    /// Stores or exposes VisualizerIntensity.
    /// </summary>
    public double VisualizerIntensity
    {
        get => _visualizerIntensity;
        set => SetField(ref _visualizerIntensity, Math.Clamp(value, 0.25, 2.5));
    }

    /// <summary>
    /// Stores or exposes _visualizerPeakHold.
    /// </summary>
    private double _visualizerPeakHold = 0.94;
    /// <summary>
    /// Stores or exposes VisualizerPeakHold.
    /// </summary>
    public double VisualizerPeakHold
    {
        get => _visualizerPeakHold;
        set
        {
            if (SetField(ref _visualizerPeakHold, Math.Clamp(value, 0.70, 0.995)))
                foreach (var band in SpectrumBands)
                    band.PeakHold = _visualizerPeakHold;
        }
    }

    /// <summary>
    /// Stores or exposes _spectrumAnalyzerMode.
    /// </summary>
    private string _spectrumAnalyzerMode = "Studio Analyzer";
    /// <summary>
    /// Stores or exposes SpectrumAnalyzerMode.
    /// </summary>
    public string SpectrumAnalyzerMode
    {
        get => _spectrumAnalyzerMode;
        set => SetField(ref _spectrumAnalyzerMode, NormalizeOption(value, SpectrumAnalyzerModes, "Studio Analyzer"));
    }

    /// <summary>
    /// Executes the CycleSpectrumAnalyzerMode operation.
    /// </summary>
    public void CycleSpectrumAnalyzerMode()
    {
        int index = SpectrumAnalyzerModes.IndexOf(SpectrumAnalyzerMode);
        SpectrumAnalyzerMode = SpectrumAnalyzerModes[(index + 1) % SpectrumAnalyzerModes.Count];
        AppLogger.Info($"[Analyzer] Spectrum mode changed to \"{SpectrumAnalyzerMode}\"");
    }

    /// <summary>
    /// Stores or exposes _preferRestartOnStop.
    /// </summary>
    private bool _preferRestartOnStop = true;
    /// <summary>
    /// Stores or exposes PreferRestartOnStop.
    /// </summary>
    public bool PreferRestartOnStop
    {
        get => _preferRestartOnStop;
        set => SetField(ref _preferRestartOnStop, value);
    }

    /// <summary>
    /// Stores or exposes _soloSelectedPianoRollChannel.
    /// </summary>
    private bool _soloSelectedPianoRollChannel = true;
    /// <summary>
    /// Stores or exposes SoloSelectedPianoRollChannel.
    /// </summary>
    public bool SoloSelectedPianoRollChannel
    {
        get => _soloSelectedPianoRollChannel;
        set => SetField(ref _soloSelectedPianoRollChannel, value);
    }

    /// <summary>
    /// Stores or exposes _midiExportPatternDefault.
    /// </summary>
    private string _midiExportPatternDefault = "Current Pattern";
    /// <summary>
    /// Stores or exposes MidiExportPatternDefault.
    /// </summary>
    public string MidiExportPatternDefault
    {
        get => _midiExportPatternDefault;
        set => SetField(ref _midiExportPatternDefault, value);
    }

    /// <summary>
    /// Stores or exposes _notePreviewMode.
    /// </summary>
    private string _notePreviewMode = "Hold While Pressed";
    /// <summary>
    /// Stores or exposes NotePreviewMode.
    /// </summary>
    public string NotePreviewMode
    {
        get => _notePreviewMode;
        set => SetField(ref _notePreviewMode, value);
    }

    private bool _pianoRollTypingKeyboardEnabled = true;
    /// <summary>
    /// Enables FL Studio-style typing-keyboard note preview in the piano roll.
    /// </summary>
    public bool PianoRollTypingKeyboardEnabled
    {
        get => _pianoRollTypingKeyboardEnabled;
        set => SetField(ref _pianoRollTypingKeyboardEnabled, value);
    }

    private int _pianoRollTypingKeyboardBaseNote = 48;
    /// <summary>
    /// Base MIDI note used by the lower typing-keyboard row in the piano roll.
    /// </summary>
    public int PianoRollTypingKeyboardBaseNote
    {
        get => _pianoRollTypingKeyboardBaseNote;
        set => SetField(ref _pianoRollTypingKeyboardBaseNote, Math.Clamp(value, 0, 96));
    }

    private int _pianoRollTypingKeyboardVelocity = 110;
    /// <summary>
    /// Velocity used by piano-roll typing-keyboard note preview.
    /// </summary>
    public int PianoRollTypingKeyboardVelocity
    {
        get => _pianoRollTypingKeyboardVelocity;
        set => SetField(ref _pianoRollTypingKeyboardVelocity, Math.Clamp(value, 1, 127));
    }

    /// <summary>
    /// Stores or exposes _workspaceDensity.
    /// </summary>
    private string _workspaceDensity = "Balanced";
    /// <summary>
    /// Stores or exposes WorkspaceDensity.
    /// </summary>
    public string WorkspaceDensity
    {
        get => _workspaceDensity;
        set => SetField(ref _workspaceDensity, value);
    }

    /// <summary>
    /// Stores or exposes _toolbarButtonSize.
    /// </summary>
    private string _toolbarButtonSize = "Balanced";
    /// <summary>
    /// Stores or exposes ToolbarButtonSize.
    /// </summary>
    public string ToolbarButtonSize
    {
        get => _toolbarButtonSize;
        set
        {
            if (SetField(ref _toolbarButtonSize, value))
            {
                OnPropertyChanged(nameof(EditorToolButtonWidth));
                OnPropertyChanged(nameof(EditorToolButtonHeight));
                OnPropertyChanged(nameof(EditorToolButtonFontSize));
            }
        }
    }

    /// <summary>
    /// Stores or exposes EditorToolButtonWidth.
    /// </summary>
    public double EditorToolButtonWidth => ToolbarButtonSize == "Large" ? 34 : ToolbarButtonSize == "Compact" ? 24 : 28;
    /// <summary>
    /// Stores or exposes EditorToolButtonHeight.
    /// </summary>
    public double EditorToolButtonHeight => ToolbarButtonSize == "Large" ? 28 : ToolbarButtonSize == "Compact" ? 22 : 24;
    /// <summary>
    /// Stores or exposes EditorToolButtonFontSize.
    /// </summary>
    public double EditorToolButtonFontSize => ToolbarButtonSize == "Large" ? 14 : 12;

    /// <summary>
    /// Stores or exposes _selectedLanguage.
    /// </summary>
    private string _selectedLanguage = "English";
    /// <summary>
    /// Stores or exposes SelectedLanguage.
    /// </summary>
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetField(ref _selectedLanguage, AppHelpContent.NormalizeLanguage(value)))
            {
                OnPropertyChanged("Item[]");
                NotifyProjectHubChanged();
                OnPropertyChanged(nameof(ProjectArtistLabel));
                OnPropertyChanged(nameof(ProjectRestartLabel));
                OnPropertyChanged(nameof(ProjectDirtyLabel));
                OnPropertyChanged(nameof(ProjectExportLabel));
                OnPropertyChanged(nameof(WorkflowLoadState));
                OnPropertyChanged(nameof(WorkflowEditState));
                OnPropertyChanged(nameof(WorkflowPreviewState));
                OnPropertyChanged(nameof(WorkflowExportState));
                PianoRoll.RefreshTranslations();
                PatternEditor.RefreshTranslations();
                AppLogger.Info($"[UI] Language set to {_selectedLanguage}");
            }
        }
    }

    /// <summary>
    /// Stores or exposes _showTipsOnStartup.
    /// </summary>
    private bool _showTipsOnStartup = true;
    /// <summary>
    /// Stores or exposes ShowTipsOnStartup.
    /// </summary>
    public bool ShowTipsOnStartup
    {
        get => _showTipsOnStartup;
        set => SetField(ref _showTipsOnStartup, value);
    }

    /// <summary>
    /// Stores or exposes _lastTipIndex.
    /// </summary>
    private int _lastTipIndex;

    /// <summary>
    /// Stores or exposes _showOldschoolAboutEffects.
    /// </summary>
    private bool _showOldschoolAboutEffects = true;
    /// <summary>
    /// Stores or exposes ShowOldschoolAboutEffects.
    /// </summary>
    public bool ShowOldschoolAboutEffects
    {
        get => _showOldschoolAboutEffects;
        set => SetField(ref _showOldschoolAboutEffects, value);
    }

    /// <summary>
    /// Stores or exposes _autoSaveConfigurationOnExit.
    /// </summary>
    private bool _autoSaveConfigurationOnExit = true;
    /// <summary>
    /// Stores or exposes AutoSaveConfigurationOnExit.
    /// </summary>
    public bool AutoSaveConfigurationOnExit
    {
        get => _autoSaveConfigurationOnExit;
        set => SetField(ref _autoSaveConfigurationOnExit, value);
    }

    /// <summary>
    /// Stores or exposes _toolTipInitialDelayMs.
    /// </summary>
    private int _toolTipInitialDelayMs = 450;
    /// <summary>
    /// Stores or exposes ToolTipInitialDelayMs.
    /// </summary>
    public int ToolTipInitialDelayMs
    {
        get => _toolTipInitialDelayMs;
        set => SetField(ref _toolTipInitialDelayMs, Math.Clamp(value, 0, 5000));
    }

    /// <summary>
    /// Stores or exposes _toolTipDurationMs.
    /// </summary>
    private int _toolTipDurationMs = 15000;
    /// <summary>
    /// Stores or exposes ToolTipDurationMs.
    /// </summary>
    public int ToolTipDurationMs
    {
        get => _toolTipDurationMs;
        set => SetField(ref _toolTipDurationMs, Math.Clamp(value, 1000, 60000));
    }

    /// <summary>
    /// Stores or exposes _helpTextScale.
    /// </summary>
    private double _helpTextScale = 1.0;
    /// <summary>
    /// Stores or exposes HelpTextScale.
    /// </summary>
    public double HelpTextScale
    {
        get => _helpTextScale;
        set => SetField(ref _helpTextScale, Math.Clamp(value, 0.8, 1.6));
    }

    /// <summary>
    /// Stores or exposes _showAdvancedMixerReadouts.
    /// </summary>
    private bool _showAdvancedMixerReadouts = true;
    /// <summary>
    /// Stores or exposes ShowAdvancedMixerReadouts.
    /// </summary>
    public bool ShowAdvancedMixerReadouts
    {
        get => _showAdvancedMixerReadouts;
        set => SetField(ref _showAdvancedMixerReadouts, value);
    }

    /// <summary>
    /// Stores or exposes _logDirectory.
    /// </summary>
    private string _logDirectory = AppLogger.LogDirectory;
    /// <summary>
    /// Stores or exposes LogDirectory.
    /// </summary>
    public string LogDirectory
    {
        get => string.IsNullOrWhiteSpace(_logDirectory) ? AppLogger.LogDirectory : _logDirectory;
        set => SetField(ref _logDirectory, value);
    }

    /// <summary>
    /// Stores or exposes _verboseLogging.
    /// </summary>
    private bool _verboseLogging = AppLogger.VerboseEnabled;
    /// <summary>
    /// Stores or exposes VerboseLogging.
    /// </summary>
    public bool VerboseLogging
    {
        get => _verboseLogging;
        set
        {
            if (SetField(ref _verboseLogging, value))
            {
                AppLogger.VerboseEnabled = value;
                AppLogger.Info($"[Diagnostics] Verbose logging {(value ? "enabled" : "disabled")}");
            }
        }
    }

    /// <summary>
    /// Stores or exposes _logDependencyLoadDetails.
    /// </summary>
    private bool _logDependencyLoadDetails = true;
    /// <summary>
    /// Stores or exposes LogDependencyLoadDetails.
    /// </summary>
    public bool LogDependencyLoadDetails
    {
        get => _logDependencyLoadDetails;
        set => SetField(ref _logDependencyLoadDetails, value);
    }

    /// <summary>
    /// Stores or exposes _showModuleDiagnostics.
    /// </summary>
    private bool _showModuleDiagnostics = true;
    /// <summary>
    /// Stores or exposes ShowModuleDiagnostics.
    /// </summary>
    public bool ShowModuleDiagnostics
    {
        get => _showModuleDiagnostics;
        set => SetField(ref _showModuleDiagnostics, value);
    }

    /// <summary>
    /// Stores or exposes _autoOpenPianoRollOnClipSelect.
    /// </summary>
    private bool _autoOpenPianoRollOnClipSelect = true;
    /// <summary>
    /// Stores or exposes AutoOpenPianoRollOnClipSelect.
    /// </summary>
    public bool AutoOpenPianoRollOnClipSelect
    {
        get => _autoOpenPianoRollOnClipSelect;
        set => SetField(ref _autoOpenPianoRollOnClipSelect, value);
    }

    /// <summary>
    /// Stores or exposes _confirmNativeExportLimitations.
    /// </summary>
    private bool _confirmNativeExportLimitations = true;
    /// <summary>
    /// Stores or exposes ConfirmNativeExportLimitations.
    /// </summary>
    public bool ConfirmNativeExportLimitations
    {
        get => _confirmNativeExportLimitations;
        set => SetField(ref _confirmNativeExportLimitations, value);
    }

    /// <summary>
    /// Stores or exposes _preferSongLengthDatabase.
    /// </summary>
    private bool _preferSongLengthDatabase = true;
    /// <summary>
    /// Stores or exposes PreferSongLengthDatabase.
    /// </summary>
    public bool PreferSongLengthDatabase
    {
        get => _preferSongLengthDatabase;
        set
        {
            if (SetField(ref _preferSongLengthDatabase, value))
            {
                ChipTuneFile.UseSongLengthDatabase = value;
                AppLogger.Info($"[ChipTune] Song-length database {(value ? "enabled" : "disabled")}");
            }
        }
    }

    /// <summary>
    /// Stores or exposes _embedRenderedSidMixInXm.
    /// </summary>
    private bool _embedRenderedSidMixInXm = true;
    /// <summary>
    /// Stores or exposes EmbedRenderedSidMixInXm.
    /// </summary>
    public bool EmbedRenderedSidMixInXm
    {
        get => _embedRenderedSidMixInXm;
        set => SetField(ref _embedRenderedSidMixInXm, value);
    }

    /// <summary>
    /// Stores or exposes _sidXmExportMode.
    /// </summary>
    private string _sidXmExportMode = "Rendered Mix Only";
    /// <summary>
    /// Stores or exposes SidXmExportMode.
    /// </summary>
    public string SidXmExportMode
    {
        get => _sidXmExportMode;
        set
        {
            string normalized = NormalizeSidXmExportMode(value);
            if (SetField(ref _sidXmExportMode, normalized))
            {
                EmbedRenderedSidMixInXm = normalized != "Trace Only";
                NotifyProjectHubChanged();
                AppLogger.Info($"[Export] SID XM export mode set to \"{normalized}\"");
            }
        }
    }

    /// <summary>
    /// Stores or exposes _chipRenderTailSeconds.
    /// </summary>
    private int _chipRenderTailSeconds = 0;
    /// <summary>
    /// Stores or exposes ChipRenderTailSeconds.
    /// </summary>
    public int ChipRenderTailSeconds
    {
        get => _chipRenderTailSeconds;
        set => SetField(ref _chipRenderTailSeconds, Math.Clamp(value, 0, 30));
    }

    /// <summary>
    /// Stores or exposes _transportTimeLabel.
    /// </summary>
    private string _transportTimeLabel = "00:00.0 / 00:00.0";
    /// <summary>
    /// Stores or exposes TransportTimeLabel.
    /// </summary>
    public string TransportTimeLabel
    {
        get => _transportTimeLabel;
        private set => SetField(ref _transportTimeLabel, value);
    }

    /// <summary>
    /// Stores or exposes _runtimeBpmLabel.
    /// </summary>
    private string _runtimeBpmLabel = "BPM 125 | SPD 6";
    /// <summary>
    /// Stores or exposes RuntimeBpmLabel.
    /// </summary>
    public string RuntimeBpmLabel
    {
        get => _runtimeBpmLabel;
        private set => SetField(ref _runtimeBpmLabel, value);
    }

    /// <summary>
    /// Stores or exposes _sourceFormatLabel.
    /// </summary>
    private string _sourceFormatLabel = "Internal";
    /// <summary>
    /// Stores or exposes SourceFormatLabel.
    /// </summary>
    public string SourceFormatLabel
    {
        get => _sourceFormatLabel;
        private set => SetField(ref _sourceFormatLabel, value);
    }

    /// <summary>
    /// Stores or exposes CanStartAtRestartOrder.
    /// </summary>
    public bool CanStartAtRestartOrder => _song.RestartOrder >= 0;

    /// <summary>
    /// Stores or exposes _startAtRestartOrder.
    /// </summary>
    private bool _startAtRestartOrder;
    /// <summary>
    /// Stores or exposes StartAtRestartOrder.
    /// </summary>
    public bool StartAtRestartOrder
    {
        get => _startAtRestartOrder;
        set
        {
            if (SetField(ref _startAtRestartOrder, value))
                NotifyProjectHubChanged();
        }
    }

    /// <summary>
    /// Stores or exposes RestartOrderLabel.
    /// </summary>
    public string RestartOrderLabel => _song.RestartOrder >= 0
        ? FormatL("RestartOrderAvailable", _song.RestartOrder)
        : L("NoRestartOrder");

    // ── Document ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the _song operation.
    /// </summary>
    private Song _song = Song.CreateDefault();
    /// <summary>
    /// Stores or exposes Song.
    /// </summary>
    public Song Song
    {
        get => _song;
        private set
        {
            DetachSongListeners(_song);
            SongProjectSerializer.Normalize(value);
            SetField(ref _song, value);
            AttachSongListeners(_song);
            SongEditor.SetSong(value);
            PianoRoll.SetSong(value);
            PatternEditor.SetSong(value);
            InstrumentBrowser.SetSong(value);
            ChannelRack.Refresh();
            Automation.Refresh();
            ClipEnvelope.Refresh();
            SeedEditorsFromSong();
            UpdateSourceFormatReadout();
            UpdateRuntimeTempoReadout();
            UpdateTransportReadout();
            NotifyProjectHubChanged();
            OnPropertyChanged(nameof(CanStartAtRestartOrder));
            OnPropertyChanged(nameof(RestartOrderLabel));
        }
    }

    /// <summary>
    /// Stores or exposes _filePath.
    /// </summary>
    private string _filePath = string.Empty;
    /// <summary>
    /// Stores or exposes FilePath.
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetField(ref _filePath, value))
            {
                OnPropertyChanged(nameof(Title));
                NotifyProjectHubChanged();
            }
        }
    }

    /// <summary>
    /// Executes the Title operation.
    /// </summary>
    public string Title => string.IsNullOrEmpty(_filePath)
        ? $"amChipper – Untitled{(IsDirty ? " *" : string.Empty)}"
        : $"amChipper – {Path.GetFileName(_filePath)}{(IsDirty ? " *" : string.Empty)}";

    /// <summary>
    /// Stores or exposes _isDirty.
    /// </summary>
    private bool _isDirty;
    /// <summary>
    /// Stores or exposes IsDirty.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
                NotifyProjectHubChanged();
            }
        }
    }

    /// <summary>
    /// Stores or exposes _useOriginalModulePlayback.
    /// </summary>
    private bool _useOriginalModulePlayback;
    /// <summary>
    /// Stores or exposes _originalModuleData.
    /// </summary>
    private byte[]? _originalModuleData;
    /// <summary>
    /// Stores or exposes _originalModulePath.
    /// </summary>
    private string? _originalModulePath;
    /// <summary>
    /// Stores or exposes _modulePreviewActive.
    /// </summary>
    private bool _modulePreviewActive;
    /// <summary>
    /// Stores or exposes _modulePreviewOrder.
    /// </summary>
    private int _modulePreviewOrder = -1;
    /// <summary>
    /// Stores or exposes _modulePreviewPattern.
    /// </summary>
    private int _modulePreviewPattern = -1;
    /// <summary>
    /// Stores or exposes _undoStack.
    /// </summary>
    private readonly Stack<DocumentSnapshot> _undoStack = [];
    /// <summary>
    /// Stores or exposes _redoStack.
    /// </summary>
    private readonly Stack<DocumentSnapshot> _redoStack = [];
    /// <summary>
    /// Stores or exposes _pendingHistory.
    /// </summary>
    private DocumentSnapshot? _pendingHistory;
    /// <summary>
    /// Stores or exposes _restoringHistory.
    /// </summary>
    private bool _restoringHistory;

    // ── Child ViewModels ──────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes SongEditor.
    /// </summary>
    public SongEditorViewModel SongEditor { get; }
    /// <summary>
    /// Stores or exposes PianoRoll.
    /// </summary>
    public PianoRollViewModel PianoRoll { get; }
    /// <summary>
    /// Stores or exposes PatternEditor.
    /// </summary>
    public PatternEditorViewModel PatternEditor { get; }
    /// <summary>
    /// Stores or exposes InstrumentBrowser.
    /// </summary>
    public InstrumentBrowserViewModel InstrumentBrowser { get; }
    /// <summary>
    /// Stores or exposes ChannelRack.
    /// </summary>
    public ChannelRackViewModel ChannelRack { get; }
    /// <summary>
    /// Stores or exposes Automation.
    /// </summary>
    public AutomationViewModel Automation { get; }
    /// <summary>
    /// Stores or exposes ClipEnvelope.
    /// </summary>
    public ClipEnvelopeViewModel ClipEnvelope { get; }

    // ── Transport ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _playbackState.
    /// </summary>
    private PlaybackState _playbackState = PlaybackState.Stopped;
    /// <summary>
    /// Stores or exposes PlaybackState.
    /// </summary>
    public PlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            SetField(ref _playbackState, value);
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
            NotifyProjectHubChanged();
        }
    }
    /// <summary>
    /// Stores or exposes IsPlaying.
    /// </summary>
    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    /// <summary>
    /// Stores or exposes IsStopped.
    /// </summary>
    public bool IsStopped => PlaybackState == PlaybackState.Stopped;

    /// <summary>
    /// Stores or exposes PlaybackScopes.
    /// </summary>
    public IReadOnlyList<PlaybackScope> PlaybackScopes { get; } =
    [
        PlaybackScope.Song,
        PlaybackScope.Pattern,
        PlaybackScope.PianoRoll
    ];

    /// <summary>
    /// Stores or exposes _playbackScope.
    /// </summary>
    private PlaybackScope _playbackScope = PlaybackScope.Song;
    /// <summary>
    /// Stores or exposes PlaybackScope.
    /// </summary>
    public PlaybackScope PlaybackScope
    {
        get => _playbackScope;
        set
        {
            if (SetField(ref _playbackScope, value))
                NotifyProjectHubChanged();
        }
    }

    /// <summary>
    /// Executes the ProjectTitleLabel operation.
    /// </summary>
    public string ProjectTitleLabel => string.IsNullOrWhiteSpace(_song.Title) ? "Untitled" : _song.Title;

    /// <summary>
    /// Executes the ProjectArtistLabel operation.
    /// </summary>
    public string ProjectArtistLabel => string.IsNullOrWhiteSpace(_song.Artist) ? L("NoArtistMetadata") : _song.Artist;

    /// <summary>
    /// Stores or exposes ProjectFormatLabel.
    /// </summary>
    public string ProjectFormatLabel
    {
        get
        {
            if (Path.GetExtension(FilePath).Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                string embedded = _song.OriginalModuleData is { Length: > 0 }
                    ? $"embedded {ModuleFormatCatalog.GetDisplayLabel(_song)} source ({_song.SourceModuleExtension})"
                    : "native internal song";
                return $"amChipper AMC (.amc) | {embedded}";
            }

            string label = ModuleFormatCatalog.GetDisplayLabel(_song);
            string ext = string.IsNullOrWhiteSpace(_song.SourceModuleExtension)
                ? ModuleFormatCatalog.GetPreferredExtension(_song.Format)
                : _song.SourceModuleExtension;
            return string.IsNullOrWhiteSpace(ext) ? label : $"{label} ({ext})";
        }
    }

    /// <summary>
    /// Stores or exposes ProjectStatsLabel.
    /// </summary>
    public string ProjectStatsLabel
    {
        get
        {
            int sampleCount = _song.Instruments.Sum(i => i.Samples.Count);
            return $"{_song.Tracks.Count} channels | {_song.Patterns.Count} patterns | {_song.OrderList.Count} orders | {_song.Instruments.Count} instruments | {sampleCount} samples";
        }
    }

    /// <summary>
    /// Stores or exposes ProjectTimingLabel.
    /// </summary>
    public string ProjectTimingLabel =>
        $"{Bpm} BPM | SPD {_song.InitialSpeed} | RPB {_song.RowsPerBeat} | rows/pattern {_song.DefaultRowsPerPattern}";

    /// <summary>
    /// Stores or exposes ProjectRestartLabel.
    /// </summary>
    public string ProjectRestartLabel => _song.RestartOrder >= 0
        ? StartAtRestartOrder ? FormatL("RestartOrderEnabled", _song.RestartOrder) : FormatL("RestartOrderAvailable", _song.RestartOrder)
        : L("NoRestartOrderInSource");

    /// <summary>
    /// Executes the ProjectSourceLabel operation.
    /// </summary>
    public string ProjectSourceLabel => string.IsNullOrWhiteSpace(FilePath)
        ? L("UnsavedInternalProject")
        : Path.GetExtension(FilePath).Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase) && _song.OriginalModuleData is { Length: > 0 }
        ? $"{FilePath} | AMC container with {_song.OriginalModuleData.Length:N0} embedded source bytes"
        : FilePath;

    /// <summary>
    /// Stores or exposes ProjectDirtyLabel.
    /// </summary>
    public string ProjectDirtyLabel => IsDirty
        ? L("UnsavedInternalExport")
        : L("SavedNativeEligible");

    /// <summary>
    /// Stores or exposes ProjectPlaybackLabel.
    /// </summary>
    public string ProjectPlaybackLabel
    {
        get
        {
            string engine = Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded
                ? "libopenmpt module player"
                : Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded
                ? "rendered chip audio"
                : _useOriginalModulePlayback
                ? "native source bytes"
                : "internal sequencer";
            return $"{PlaybackScope} scope | {PlaybackState} | {engine}";
        }
    }

    /// <summary>
    /// Stores or exposes ProjectExportLabel.
    /// </summary>
    public string ProjectExportLabel
    {
        get
        {
            if (ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format))
                return $"Chip source: native copy, WAV/MP3 render, XM {SidXmExportMode}";

            if (_song.Format is ModuleFormat.XM or ModuleFormat.MOD)
                return IsDirty ? "Native patch export available for edited XM/MOD" : "Native copy/export available";

            return L("ExactNativeExportLimited");
        }
    }

    /// <summary>
    /// Executes the WorkflowLoadState operation.
    /// </summary>
    public string WorkflowLoadState => _originalModuleData is not null || !string.IsNullOrWhiteSpace(FilePath)
        ? L("WorkflowReady")
        : L("WorkflowNewProject");

    /// <summary>
    /// Stores or exposes WorkflowEditState.
    /// </summary>
    public string WorkflowEditState => _song.Patterns.Count > 0 && _song.Tracks.Count > 0
        ? L("WorkflowPatternsAvailable")
        : L("WorkflowNeedsPatterns");

    /// <summary>
    /// Stores or exposes WorkflowPreviewState.
    /// </summary>
    public string WorkflowPreviewState => IsPlaying
        ? FormatL("WorkflowPlaying", PlaybackScope)
        : L("WorkflowStopped");

    /// <summary>
    /// Stores or exposes WorkflowExportState.
    /// </summary>
    public string WorkflowExportState => IsDirty
        ? L("WorkflowSavePending")
        : L("WorkflowClean");

    /// <summary>
    /// Stores or exposes Bpm.
    /// </summary>
    public int Bpm
    {
        get => Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded && Audio.ModulePlayer.CurrentTempo > 0
            ? Audio.ModulePlayer.CurrentTempo
            : _song.Bpm;
        set
        {
            if (_song.Bpm != value)
            {
                _song.Bpm = value;
                OnPropertyChanged();
                UpdateRuntimeTempoReadout();
                NotifyProjectHubChanged();
            }
        }
    }

    /// <summary>
    /// Stores or exposes RowsPerBeat.
    /// </summary>
    public int RowsPerBeat
    {
        get => _song.RowsPerBeat;
        set
        {
            if (_song.RowsPerBeat != value)
            {
                _song.RowsPerBeat = value;
                OnPropertyChanged();
                NotifyProjectHubChanged();
            }
        }
    }

    /// <summary>
    /// Stores or exposes SelectedInstrumentNumber.
    /// </summary>
    public byte SelectedInstrumentNumber
    {
        get
        {
            var selected = InstrumentBrowser.SelectedInstrument;
            if (selected is null)
                return 1;

            int index = _song.Instruments.IndexOf(selected);
            return (byte)(index >= 0 ? index + 1 : 1);
        }
    }

    // ── Status bar ────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _statusText.
    /// </summary>
    private string _statusText = "Ready";
    /// <summary>
    /// Executes the StatusText operation.
    /// </summary>
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    /// <summary>
    /// Stores or exposes CanUndo.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0 || _pendingHistory is not null;
    /// <summary>
    /// Stores or exposes CanRedo.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes NewSongCommand.
    /// </summary>
    public ICommand NewSongCommand { get; }
    /// <summary>
    /// Stores or exposes OpenFileCommand.
    /// </summary>
    public ICommand OpenFileCommand { get; }
    /// <summary>
    /// Stores or exposes SaveFileCommand.
    /// </summary>
    public ICommand SaveFileCommand { get; }
    /// <summary>
    /// Stores or exposes SaveAsCommand.
    /// </summary>
    public ICommand SaveAsCommand { get; }
    /// <summary>
    /// Stores or exposes ExportToCommand.
    /// </summary>
    public ICommand ExportToCommand { get; }
    /// <summary>
    /// Stores or exposes ExportXmCommand.
    /// </summary>
    public ICommand ExportXmCommand { get; }
    /// <summary>
    /// Stores or exposes ExportNativeModuleCommand.
    /// </summary>
    public ICommand ExportNativeModuleCommand { get; }
    /// <summary>
    /// Stores or exposes ExportRenderedWavCommand.
    /// </summary>
    public ICommand ExportRenderedWavCommand { get; }
    /// <summary>
    /// Stores or exposes ExportPianoRollMidiCommand.
    /// </summary>
    public ICommand ExportPianoRollMidiCommand { get; }
    /// <summary>
    /// Stores or exposes ImportPianoRollMidiCommand.
    /// </summary>
    public ICommand ImportPianoRollMidiCommand { get; }
    /// <summary>
    /// Stores or exposes ExportPianoRollFscCommand.
    /// </summary>
    public ICommand ExportPianoRollFscCommand { get; }
    /// <summary>
    /// Stores or exposes ImportPianoRollFscCommand.
    /// </summary>
    public ICommand ImportPianoRollFscCommand { get; }
    /// <summary>
    /// Stores or exposes ApplyThemeCommand.
    /// </summary>
    public ICommand ApplyThemeCommand { get; }
    /// <summary>
    /// Stores or exposes ApplyAudioSettingsCommand.
    /// </summary>
    public ICommand ApplyAudioSettingsCommand { get; }
    /// <summary>
    /// Stores or exposes RefreshAudioDevicesCommand.
    /// </summary>
    public ICommand RefreshAudioDevicesCommand { get; }

    /// <summary>
    /// Stores or exposes PlayCommand.
    /// </summary>
    public ICommand PlayCommand { get; }
    /// <summary>
    /// Stores or exposes PauseCommand.
    /// </summary>
    public ICommand PauseCommand { get; }
    /// <summary>
    /// Stores or exposes StopCommand.
    /// </summary>
    public ICommand StopCommand { get; }
    /// <summary>
    /// Stores or exposes TogglePlaybackCommand.
    /// </summary>
    public ICommand TogglePlaybackCommand { get; }
    /// <summary>
    /// Stores or exposes SetPlaybackScopeCommand.
    /// </summary>
    public ICommand SetPlaybackScopeCommand { get; }
    /// <summary>
    /// Stores or exposes ApplyLogSettingsCommand.
    /// </summary>
    public ICommand ApplyLogSettingsCommand { get; }
    /// <summary>
    /// Stores or exposes OpenLogFolderCommand.
    /// </summary>
    public ICommand OpenLogFolderCommand { get; }
    /// <summary>
    /// Stores or exposes SaveConfigurationCommand.
    /// </summary>
    public ICommand SaveConfigurationCommand { get; }
    /// <summary>
    /// Stores or exposes LoadConfigurationCommand.
    /// </summary>
    public ICommand LoadConfigurationCommand { get; }
    /// <summary>
    /// Stores or exposes ExportConfigurationCommand.
    /// </summary>
    public ICommand ExportConfigurationCommand { get; }
    /// <summary>
    /// Stores or exposes ImportConfigurationCommand.
    /// </summary>
    public ICommand ImportConfigurationCommand { get; }
    /// <summary>
    /// Stores or exposes ResetConfigurationCommand.
    /// </summary>
    public ICommand ResetConfigurationCommand { get; }
    /// <summary>
    /// Stores or exposes RegisterOpenWithCommand.
    /// </summary>
    public ICommand RegisterOpenWithCommand { get; }
    /// <summary>
    /// Stores or exposes ShowTipCommand.
    /// </summary>
    public ICommand ShowTipCommand { get; }
    /// <summary>
    /// Stores or exposes ShowHelpCommand.
    /// </summary>
    public ICommand ShowHelpCommand { get; }
    /// <summary>
    /// Stores or exposes ShowAboutCommand.
    /// </summary>
    public ICommand ShowAboutCommand { get; }

    /// <summary>
    /// Stores or exposes UndoCommand.
    /// </summary>
    public ICommand UndoCommand { get; }
    /// <summary>
    /// Stores or exposes RedoCommand.
    /// </summary>
    public ICommand RedoCommand { get; }

    /// <summary>
    /// Stores or exposes AddTrackCommand.
    /// </summary>
    public ICommand AddTrackCommand { get; }
    /// <summary>
    /// Stores or exposes AddPatternCommand.
    /// </summary>
    public ICommand AddPatternCommand { get; }
    /// <summary>
    /// Stores or exposes AddInstrumentCommand.
    /// </summary>
    public ICommand AddInstrumentCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        SongEditor = new SongEditorViewModel(this);
        PianoRoll = new PianoRollViewModel(this);
        PatternEditor = new PatternEditorViewModel(this);
        InstrumentBrowser = new InstrumentBrowserViewModel(this);
        ChannelRack = new ChannelRackViewModel(this);
        Automation = new AutomationViewModel(this);
        ClipEnvelope = new ClipEnvelopeViewModel(this);

        NewSongCommand = new RelayCommand(_ => NewSong());
        OpenFileCommand = new RelayCommand(_ => OpenFile());
        SaveFileCommand = new RelayCommand(_ => SaveFile());
        SaveAsCommand = new RelayCommand(_ => SaveAs());
        ExportToCommand = new RelayCommand(_ => ExportTo());
        ExportXmCommand = new RelayCommand(_ => ExportXm());
        ExportNativeModuleCommand = new RelayCommand(_ => ExportNativeModule());
        ExportRenderedWavCommand = new RelayCommand(_ => ExportRenderedWav());
        ExportPianoRollMidiCommand = new RelayCommand(_ => ExportPianoRollMidi());
        ImportPianoRollMidiCommand = new RelayCommand(_ => ImportPianoRollMidi());
        ExportPianoRollFscCommand = new RelayCommand(_ => ExportPianoRollFsc());
        ImportPianoRollFscCommand = new RelayCommand(_ => ImportPianoRollFsc());
        ApplyThemeCommand = new RelayCommand(theme => SelectedTheme = NormalizeThemeName(theme?.ToString() ?? "FL Grape"));
        ApplyAudioSettingsCommand = new RelayCommand(_ => ApplyAudioSettings());
        RefreshAudioDevicesCommand = new RelayCommand(_ => RefreshAudioOutputDevices());

        PlayCommand = new RelayCommand(_ => Play(), _ => !IsPlaying);
        PauseCommand = new RelayCommand(_ => Pause(), _ => IsPlaying);
        StopCommand = new RelayCommand(_ => Stop(), _ => PlaybackState != PlaybackState.Stopped);
        TogglePlaybackCommand = new RelayCommand(_ => TogglePlayback());
        SetPlaybackScopeCommand = new RelayCommand(scope => SetPlaybackScope(scope?.ToString()));
        ApplyLogSettingsCommand = new RelayCommand(_ => ApplyLogSettings());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        SaveConfigurationCommand = new RelayCommand(_ => SaveConfiguration());
        LoadConfigurationCommand = new RelayCommand(_ => LoadConfiguration());
        ExportConfigurationCommand = new RelayCommand(_ => ExportConfiguration());
        ImportConfigurationCommand = new RelayCommand(_ => ImportConfiguration());
        ResetConfigurationCommand = new RelayCommand(_ => ResetConfiguration());
        RegisterOpenWithCommand = new RelayCommand(_ => RegisterOpenWith());
        ShowTipCommand = new RelayCommand(_ => ShowTipWindow(startup: false));
        ShowHelpCommand = new RelayCommand(_ => ShowHelpWindow());
        ShowAboutCommand = new RelayCommand(_ => ShowAboutWindow());
        UndoCommand = new RelayCommand(_ => Undo(), _ => CanUndo);
        RedoCommand = new RelayCommand(_ => Redo(), _ => CanRedo);

        AddTrackCommand = new RelayCommand(_ => AddTrack());
        AddPatternCommand = new RelayCommand(_ => AddPattern());
        AddInstrumentCommand = new RelayCommand(_ => AddInstrument());

        for (int i = 0; i < 40; i++)
            SpectrumBands.Add(new SpectrumBandViewModel(i));

        RefreshAudioOutputDevices();
        LoadConfiguration(silent: true);
        ApplyUiChromeSettings();
        RuntimeDependencyResolver.DependencyLoaded += (_, info) =>
        {
            if (LogDependencyLoadDetails)
                AppLogger.Info($"[DependencyLoad] name=\"{info.Name}\" state={info.State} path=\"{info.Path}\"");
        };

        ApplyAudioSettings(silent: true);
        Audio.Sequencer.MeterLevelsChanged += (_, e) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ApplyMeterLevels(e.TrackLevels, e.MasterLevel);
            });
        };
        Audio.BufferRendered += (_, e) =>
        {
            float peak = 0f;
            int limit = Math.Min(e.Buffer.Length, e.FrameCount * 2);
            for (int i = 0; i < limit; i++)
            {
                float abs = Math.Abs(e.Buffer[i]);
                if (abs > peak)
                    peak = abs;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                MasterMeterLevel = peak;
                UpdateSpectrum(e.Buffer, limit);
                if (Audio.UseModulePlayer)
                    ApplyModuleVuMeters();
                UpdateRuntimeTempoReadout();
                SyncPlaybackVisualsFromTransport();
                UpdateTransportReadout();
            });
        };

        // ── Wire ModulePlayer playback position → UI ──────────────────────────
        // RowChanged fires on the NAudio buffer thread up to ~10× per second.
        // We dispatch to the UI thread but NEVER call RefreshRows here — that
        // rebuilds the entire row collection and is far too expensive per tick.
        // Instead we use TrackPlayback() which only updates the cursor row.
        Audio.ModulePlayer.RowChanged += (_, e) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                AppLogger.Debug($"[ModulePlayback] order={e.Order} pattern={e.Pattern} row={e.Row}");
                UpdateRuntimeTempoReadout();
                PulseModuleTrackMeters(e.Pattern, e.Row);

                if (_modulePreviewActive && (e.Order != _modulePreviewOrder || (e.Pattern >= 0 && e.Pattern != _modulePreviewPattern)))
                {
                    AppLogger.Debug($"[ModulePlayback] Preview scope completed order={_modulePreviewOrder} pattern={_modulePreviewPattern} currentOrder={e.Order} currentPattern={e.Pattern}");
                    Stop();
                    return;
                }

                // If the playing pattern changed, switch the editor view — but use
                // TrackPlayback so it skips the full RefreshRows rebuild.
                if (PatternEditor.CurrentPatternIndex != e.Pattern)
                    PatternEditor.TrackPlayback(e.Pattern, e.Row);
                else
                    PatternEditor.CurrentRow = e.Row;

                // Update Song Editor playhead.
                int rowsPerBeat = _song.RowsPerBeat > 0 ? _song.RowsPerBeat : 4;
                double localBeat = (double)e.Row / rowsPerBeat;
                if (TryGetOrderStartBeat(e.Order, out double orderStartBeat))
                {
                    double beat = orderStartBeat + localBeat;
                    SongEditor.PlayheadBeat = beat;
                    PianoRoll.PlayheadBeat = _modulePreviewActive && PlaybackScope == PlaybackScope.PianoRoll
                        ? localBeat
                        : beat;
                }
                else if (_modulePreviewActive && PlaybackScope == PlaybackScope.PianoRoll)
                {
                    PianoRoll.PlayheadBeat = localBeat;
                }

                ChannelRack.UpdatePlaybackRow(e.Row);
                Automation.NotifyPlaybackMoved();
                ClipEnvelope.NotifyPlaybackMoved();
                UpdateTransportReadout();
            });
        };

        Audio.Sequencer.RowAdvanced += (_, e) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                AppLogger.Debug($"[SequencerUI] pattern={e.pattern} row={e.row} beat={e.beat:0.###} scope={PlaybackScope}");
                PatternEditor.TrackPlayback(e.pattern, e.row);
                SongEditor.PlayheadBeat = e.beat;
                PianoRoll.PlayheadBeat = e.beat;
                ChannelRack.UpdatePlaybackRow(e.row);
                Automation.NotifyPlaybackMoved();
                ClipEnvelope.NotifyPlaybackMoved();
                UpdateRuntimeTempoReadout();
                UpdateTransportReadout();
            });
        };

        SongEditor.SetSong(_song);
        PianoRoll.SetSong(_song);
        PatternEditor.SetSong(_song);
        InstrumentBrowser.SetSong(_song);
        ChannelRack.Refresh();
        Automation.Refresh();
        ClipEnvelope.Refresh();

        SongEditor.PropertyChanged += (_, e) =>
        {
            if (_restoringHistory)
                return;

            if (e.PropertyName == nameof(SongEditorViewModel.SelectedPatternIndex))
            {
                int selectedPattern = Math.Clamp(SongEditor.SelectedPatternIndex, 0, Math.Max(_song.Patterns.Count - 1, 0));
                if (selectedPattern < 0 || selectedPattern >= _song.Patterns.Count)
                    return;

                AppLogger.Debug($"[SongEditor] selected pattern changed selectedPattern={selectedPattern} syncing editors");
                if (PatternEditor.CurrentPatternIndex != selectedPattern)
                    PatternEditor.CurrentPatternIndex = selectedPattern;
                if (PianoRoll.CurrentPatternIndex != selectedPattern)
                    PianoRoll.SetCurrentPattern(selectedPattern);
                return;
            }

            if (e.PropertyName == nameof(SongEditorViewModel.SelectedBlock))
            {
                var block = SongEditor.SelectedBlock;
                if (block is null)
                    return;

                int patternIndex = Math.Clamp(block.PatternIndex, 0, Math.Max(_song.Patterns.Count - 1, 0));
                int trackIndex = _song.Tracks.FindIndex(t => t.Blocks.Contains(block));
                if (trackIndex < 0)
                    trackIndex = 0;

                AppLogger.Debug(
                    $"[SongEditor] selected block changed pattern={patternIndex} track={trackIndex} startBeat={block.StartBeat:0.###} duration={block.DurationBeats:0.###}");

                if (SongEditor.SelectedPatternIndex != patternIndex)
                    SongEditor.SelectedPatternIndex = patternIndex;
                if (PatternEditor.CurrentPatternIndex != patternIndex)
                    PatternEditor.CurrentPatternIndex = patternIndex;
                if (PianoRoll.CurrentPatternIndex != patternIndex)
                    PianoRoll.SetCurrentPattern(patternIndex);

                SongEditor.SelectedTrack = _song.Tracks[Math.Clamp(trackIndex, 0, Math.Max(_song.Tracks.Count - 1, 0))];
                Automation.SelectedTrack = SongEditor.SelectedTrack;
                PatternEditor.CurrentChannel = Math.Clamp(trackIndex, 0, Math.Max(PatternEditor.ChannelCount - 1, 0));
                PianoRoll.CurrentChannel = Math.Clamp(trackIndex, 0, Math.Max(PianoRoll.ChannelOptions.Count - 1, 0));
                ChannelRack.Refresh();
                Automation.Refresh();
                ClipEnvelope.Refresh();
            }
        };

        // Double-clicking a block in the Song Editor navigates the Pattern Editor
        // and Piano Roll to that pattern.
        SongEditor.BlockEditRequested += (_, patIdx) =>
        {
            AppLogger.Info($"[Navigation] block edit requested pattern={patIdx}");
            if (patIdx >= 0 && patIdx < _song.Patterns.Count)
            {
                PatternEditor.CurrentPatternIndex = patIdx;
                PianoRoll.SetCurrentPattern(patIdx);
            }
        };

        PatternEditor.PatternDataChanged += (_, _) =>
        {
            AppLogger.Debug($"[PatternEditor] data changed pattern={PatternEditor.CurrentPatternIndex} row={PatternEditor.CurrentRow} channel={PatternEditor.CurrentChannel}");
            MarkDirty(useNativePlayback: true);
            if (SongEditor.SelectedPatternIndex != PatternEditor.CurrentPatternIndex)
                SongEditor.SelectedPatternIndex = PatternEditor.CurrentPatternIndex;
            PianoRoll.SetCurrentPattern(PatternEditor.CurrentPatternIndex);
            ChannelRack.Refresh();
            Automation.Refresh();
            ClipEnvelope.Refresh();
        };

        PatternEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PatternEditorViewModel.CurrentPatternIndex))
            {
                ChannelRack.Refresh();
                Automation.Refresh();
                ClipEnvelope.Refresh();
            }
        };

        PianoRoll.PatternDataChanged += (_, _) =>
        {
            int editedPattern = PianoRoll.CurrentPatternIndex;
            int editedChannel = PianoRoll.CurrentChannel;
            AppLogger.Debug(
                $"[PianoRoll] pattern data changed pattern={editedPattern} channel={editedChannel} " +
                $"songEditorPattern={PatternEditor.CurrentPatternIndex} songEditorRow={PatternEditor.CurrentRow}");
            MarkDirty(useNativePlayback: true);
            if (TryResolveArrangementBlockForPattern(PianoRoll.CurrentPatternIndex, editedChannel, out var block, out int trackIndex))
            {
                SongEditor.SelectedBlock = block;
                if (trackIndex >= 0 && trackIndex < _song.Tracks.Count)
                    SongEditor.SelectedTrack = _song.Tracks[trackIndex];
            }
            if (SongEditor.SelectedPatternIndex != editedPattern)
                SongEditor.SelectedPatternIndex = editedPattern;
            if (PatternEditor.CurrentPatternIndex != editedPattern)
                PatternEditor.CurrentPatternIndex = editedPattern;
            if (PianoRoll.CurrentChannel != editedChannel)
                PianoRoll.CurrentChannel = editedChannel;
            if (PatternEditor.CurrentChannel != editedChannel)
                PatternEditor.CurrentChannel = editedChannel;
            AppLogger.Debug(
                $"[PianoRoll] pattern sync complete pattern={PatternEditor.CurrentPatternIndex} channel={PatternEditor.CurrentChannel} " +
                $"dirty={IsDirty} nativePlayback={_useOriginalModulePlayback}");
            SongEditor.RaiseSongDataChanged();
            SongEditor.RaiseLayoutChanged();
            PatternEditor.RefreshRows();
            ChannelRack.Refresh();
            Automation.Refresh();
            ClipEnvelope.Refresh();
        };

        PianoRoll.PropertyChanged += (_, e) =>
        {
            if (_restoringHistory)
                return;

            if (e.PropertyName is nameof(PianoRollViewModel.CurrentPatternIndex))
            {
                int patternIndex = PianoRoll.CurrentPatternIndex;
                AppLogger.Debug($"[PianoRoll] current pattern changed pattern={patternIndex} syncing document");
                if (SongEditor.SelectedPatternIndex != patternIndex)
                    SongEditor.SelectedPatternIndex = patternIndex;
                if (PatternEditor.CurrentPatternIndex != patternIndex)
                    PatternEditor.CurrentPatternIndex = patternIndex;
                return;
            }

            if (e.PropertyName is nameof(PianoRollViewModel.CurrentChannel))
            {
                AppLogger.Debug($"[PianoRoll] current channel changed channel={PianoRoll.CurrentChannel} pattern={PianoRoll.CurrentPatternIndex}");
                if (PatternEditor.CurrentChannel != PianoRoll.CurrentChannel)
                    PatternEditor.CurrentChannel = PianoRoll.CurrentChannel;
            }
        };

        SongEditor.SongDataChanged += (_, _) =>
        {
            AppLogger.Debug($"[SongEditor] song data changed tracks={_song.Tracks.Count} patterns={_song.Patterns.Count} blocks={_song.Tracks.Sum(t => t.Blocks.Count)}");
            MarkDirty(useNativePlayback: true);
            ClipEnvelope.Refresh();
        };

        SongEditor.SeekRequested += (_, beat) =>
            SeekToBeat(beat);

        AttachSongListeners(_song);
    }

    // ── History ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the BeginHistory operation.
    /// </summary>
    public void BeginHistory(string reason)
    {
        if (_restoringHistory || _pendingHistory is not null)
            return;

        _pendingHistory = CaptureSnapshot(reason);
        AppLogger.Debug($"[History] Begin reason=\"{reason}\" undo={_undoStack.Count} redo={_redoStack.Count}");
    }

    /// <summary>
    /// Executes the CommitHistory operation.
    /// </summary>
    public void CommitHistory()
    {
        if (_restoringHistory || _pendingHistory is null)
            return;

        _undoStack.Push(_pendingHistory);
        _pendingHistory = null;
        _redoStack.Clear();
        AppLogger.Debug($"[History] Commit undo={_undoStack.Count} redo={_redoStack.Count}");
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Executes the CancelHistory operation.
    /// </summary>
    public void CancelHistory()
    {
        if (_pendingHistory is null)
            return;

        _pendingHistory = null;
        AppLogger.Debug($"[History] Cancel undo={_undoStack.Count} redo={_redoStack.Count}");
    }

    /// <summary>
    /// Executes the Undo operation.
    /// </summary>
    public void Undo()
    {
        CancelHistory();
        if (_undoStack.Count == 0)
            return;

        var current = CaptureSnapshot("undo-current");
        var snapshot = _undoStack.Pop();
        _redoStack.Push(current);
        RestoreSnapshot(snapshot);
        AppLogger.Info($"[History] Undo undo={_undoStack.Count} redo={_redoStack.Count}");
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Executes the Redo operation.
    /// </summary>
    public void Redo()
    {
        CancelHistory();
        if (_redoStack.Count == 0)
            return;

        var current = CaptureSnapshot("redo-current");
        var snapshot = _redoStack.Pop();
        _undoStack.Push(current);
        RestoreSnapshot(snapshot);
        AppLogger.Info($"[History] Redo undo={_undoStack.Count} redo={_redoStack.Count}");
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Executes the CaptureSnapshot operation.
    /// </summary>
    private DocumentSnapshot CaptureSnapshot(string reason)
    {
        return new DocumentSnapshot(
            Song.Clone(),
            FilePath,
            _useOriginalModulePlayback,
            PlaybackScope,
            SelectedInstrumentNumber,
            SongEditor.SelectedPatternIndex,
            PianoRoll.CurrentPatternIndex,
            PianoRoll.CurrentChannel,
            PatternEditor.CurrentPatternIndex,
            PatternEditor.CurrentRow,
            PatternEditor.CurrentChannel,
            SongEditor.PlayheadBeat,
            PianoRoll.PlayheadBeat,
            PlaybackState,
            StatusText,
            reason);
    }

    /// <summary>
    /// Executes the RestoreSnapshot operation.
    /// </summary>
    private void RestoreSnapshot(DocumentSnapshot snapshot)
    {
        _restoringHistory = true;
        try
        {
            Stop();
            Song = snapshot.Song.Clone();
            FilePath = snapshot.FilePath;
            _useOriginalModulePlayback = snapshot.UseOriginalModulePlayback;
            PlaybackScope = snapshot.PlaybackScope;
            StatusText = snapshot.StatusText;
            IsDirty = true;

            int patternIndex = Math.Clamp(snapshot.SongEditorPatternIndex, 0, Math.Max(_song.Patterns.Count - 1, 0));
            SongEditor.SelectedPatternIndex = patternIndex;

            if (_song.Patterns.Count > 0)
            {
                int pianoPattern = Math.Clamp(snapshot.PianoPatternIndex, 0, _song.Patterns.Count - 1);
                PianoRoll.SetCurrentPattern(pianoPattern);
                PatternEditor.CurrentPatternIndex = pianoPattern;
            }

            PatternEditor.CurrentRow = Math.Clamp(snapshot.PatternRow, 0, Math.Max(PatternEditor.CurrentPattern?.RowCount - 1 ?? 0, 0));
            PatternEditor.CurrentChannel = Math.Clamp(snapshot.PatternChannel, 0, Math.Max(PatternEditor.ChannelCount - 1, 0));
            PianoRoll.CurrentChannel = Math.Clamp(snapshot.PianoChannel, 0, Math.Max(PianoRoll.ChannelOptions.Count - 1, 0));

            if (snapshot.SelectedInstrumentNumber > 0 && snapshot.SelectedInstrumentNumber <= _song.Instruments.Count)
            {
                InstrumentBrowser.SelectedInstrument = _song.Instruments[snapshot.SelectedInstrumentNumber - 1];
            }

            SongEditor.PlayheadBeat = snapshot.SongPlayheadBeat;
            PianoRoll.PlayheadBeat = snapshot.PianoPlayheadBeat;
            CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            _restoringHistory = false;
        }
    }

    /// <summary>
    /// Executes the ClearHistory operation.
    /// </summary>
    public void ClearHistory()
    {
        _pendingHistory = null;
        _undoStack.Clear();
        _redoStack.Clear();
        CommandManager.InvalidateRequerySuggested();
        AppLogger.Debug("[History] Cleared");
    }

    // ── Transport actions ─────────────────────────────────────────────────────

    /// <summary>
    /// Executes the Play operation.
    /// </summary>
    private async void Play()
    {
        if (PlaybackScope == PlaybackScope.Song)
        {
            if (SongEditor.SelectedBlock is null &&
                TryResolveArrangementBlockAtBeat(SongEditor.PlayheadBeat, out var activeBlock, out int activeTrackIndex))
            {
                SongEditor.SelectedBlock = activeBlock;
                if (activeTrackIndex >= 0 && activeTrackIndex < _song.Tracks.Count)
                    SongEditor.SelectedTrack = _song.Tracks[activeTrackIndex];
            }
        }

        double songStartBeat = ResolveSongPlaybackStartBeat();
        int activePatternIndex = PlaybackScope == PlaybackScope.PianoRoll
            ? PianoRoll.CurrentPatternIndex
            : PatternEditor.CurrentPatternIndex;
        int activeChannel = PlaybackScope == PlaybackScope.PianoRoll
            ? PianoRoll.CurrentChannel
            : PatternEditor.CurrentChannel;

        AppLogger.Info(
            $"[Transport] Play requested scope={PlaybackScope} pattern={PatternEditor.CurrentPatternIndex} " +
            $"patternRow={PatternEditor.CurrentRow} pianoPattern={PianoRoll.CurrentPatternIndex} " +
            $"pianoChannel={PianoRoll.CurrentChannel} " +
            $"playheadBeat={SongEditor.PlayheadBeat:0.###} bpm={_song.Bpm} rpb={_song.RowsPerBeat} " +
            $"songStartBeat={songStartBeat:0.###} selectedBlock={(SongEditor.SelectedBlock is null ? "none" : $"{SongEditor.SelectedBlock.PatternIndex}@{SongEditor.SelectedBlock.StartBeat:0.###}")} " +
            $"moduleLoaded={Audio.ModulePlayer.IsLoaded} useOriginalModule={_useOriginalModulePlayback} " +
            $"selectedInstrument={SelectedInstrumentNumber}:{InstrumentBrowser.SelectedInstrument?.Name ?? "(none)"}");

        bool canUseModulePreview = Audio.ModulePlayer.IsLoaded
            && PlaybackScope is PlaybackScope.Pattern or PlaybackScope.PianoRoll
            && _useOriginalModulePlayback
            && !IsDirty;

        if (ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format) && PlaybackScope == PlaybackScope.Song && !IsDirty)
        {
            _modulePreviewActive = false;
            Audio.UseModulePlayer = false;
            if (!Audio.AudioFilePlayer.IsLoaded)
            {
                if (await TryRenderChipPreviewForPlaybackAsync().ConfigureAwait(true))
                {
                    if (PlaybackScope != PlaybackScope.Song || IsDirty)
                        return;
                }
                else
                {
                    return;
                }
            }

            if (Audio.AudioFilePlayer.IsLoaded)
            {
                Audio.UseAudioFilePlayer = true;
                Audio.AudioFilePlayer.PositionSecs = 0;
                AppLogger.Info($"[Transport] Using rendered chip playback format={_song.Format} path=\"{Audio.AudioFilePlayer.FilePath}\"");
            }
            else
            {
                StatusText = $"{_song.Format} internal render failed.";
                AppLogger.Warning($"[Transport] Cannot play {_song.Format}: no rendered WAV is loaded.");
                return;
            }
        }
        else if (canUseModulePreview)
        {
            Audio.UseAudioFilePlayer = false;
            _modulePreviewPattern = activePatternIndex;
            _modulePreviewOrder = ResolveModuleOrderForPattern(_modulePreviewPattern);
            _modulePreviewActive = _modulePreviewOrder >= 0;

            if (_modulePreviewActive)
            {
                Audio.UseModulePlayer = true;
                ApplyModuleMuteProfile();
                int row = ResolvePlaybackStartRow();
                AppLogger.Info($"[Transport] Using libopenmpt preview scope={PlaybackScope} pattern={_modulePreviewPattern} order={_modulePreviewOrder} row={row} pianoChannel={PianoRoll.CurrentChannel}");
                Audio.ModulePlayer.SeekToOrder(_modulePreviewOrder, row);
            }
            else
            {
                AppLogger.Warning($"[Transport] Could not resolve preview pattern {_modulePreviewPattern} to module order; falling back to sequencer.");
                Audio.Sequencer.SetSong(_song);
                Audio.Sequencer.Play(
                    PlaybackScope,
                    activePatternIndex,
                    ResolvePlaybackStartRow(),
                    PlaybackScope == PlaybackScope.PianoRoll ? activeChannel : null,
                    SongEditor.PlayheadBeat);
                Audio.UseModulePlayer = false;
            }
        }
        else if (_useOriginalModulePlayback && Audio.ModulePlayer.IsLoaded && PlaybackScope == PlaybackScope.Song)
        {
            // Module file is open — play via libopenmpt.
            Audio.UseAudioFilePlayer = false;
            _modulePreviewActive = false;
            _modulePreviewPattern = -1;
            _modulePreviewOrder = -1;
            ApplyModuleMuteProfile();
            Audio.UseModulePlayer = true;
            SeekModuleToBeat(SongEditor.PlayheadBeat);
        }
        else
        {
            // Edited tracker playback is rendered to an in-memory module and
            // played by libopenmpt so effects and imported samples stay faithful.
            if (TryLoadLivePreviewModule())
            {
                Audio.UseAudioFilePlayer = false;
                _modulePreviewPattern = activePatternIndex;
                _modulePreviewOrder = ResolveModuleOrderForPattern(_modulePreviewPattern);
                _modulePreviewActive = PlaybackScope is PlaybackScope.Pattern or PlaybackScope.PianoRoll
                    && _modulePreviewOrder >= 0;

                Audio.UseModulePlayer = true;
                ApplyModuleMuteProfile();

                if (PlaybackScope == PlaybackScope.Song)
                {
                    SeekModuleToBeat(songStartBeat);
                }
                else if (_modulePreviewOrder >= 0)
                {
                    int row = ResolvePlaybackStartRow();
                    AppLogger.Info($"[Transport] Using live XM render scope={PlaybackScope} pattern={_modulePreviewPattern} order={_modulePreviewOrder} row={row} pianoChannel={PianoRoll.CurrentChannel}");
                    Audio.ModulePlayer.SeekToOrder(_modulePreviewOrder, row);
                }
                else
                {
                    AppLogger.Warning($"[Transport] Live XM render could not resolve pattern {_modulePreviewPattern}; seeking to module start.");
                    Audio.ModulePlayer.SeekToOrder(0, 0);
                }
            }
            else
            {
                if (Audio.ModulePlayer.IsLoaded && _useOriginalModulePlayback)
                {
                    AppLogger.Debug($"[Transport] Using sequencer for editable module scope={PlaybackScope} pattern={activePatternIndex} channel={activeChannel} dirty={IsDirty}");
                }

                Audio.Sequencer.SetSong(_song);
                Audio.UseAudioFilePlayer = false;
                Audio.Sequencer.Play(
                    PlaybackScope,
                    activePatternIndex,
                    ResolvePlaybackStartRow(),
                    PlaybackScope == PlaybackScope.PianoRoll ? activeChannel : null,
                    songStartBeat);
                Audio.UseModulePlayer = false;
            }
        }

        Audio.ModulePlayer.LoopEnabled = Audio.UseModulePlayer && PlaybackScope == PlaybackScope.Song;
        Audio.ModulePlayer.LoopFromRestartOrder = StartAtRestartOrder;
        Audio.Play();
        PlaybackState = PlaybackState.Playing;
        StatusText = $"Playing {PlaybackScope}…";
    }

    /// <summary>
    /// Executes the PlayPianoRoll operation.
    /// </summary>
    public void PlayPianoRoll()
    {
        if (IsPlaying)
            Stop();

        PlaybackScope = PlaybackScope.PianoRoll;
        AppLogger.Info($"[PianoRoll] Play lane requested pattern={PianoRoll.CurrentPatternIndex} channel={PianoRoll.CurrentChannel} beat={PianoRoll.PlayheadBeat:0.###}");
        Play();
    }

    /// <summary>
    /// Executes the PlayPattern operation.
    /// </summary>
    public void PlayPattern()
    {
        if (IsPlaying)
            Stop();

        PlaybackScope = PlaybackScope.Pattern;
        AppLogger.Info($"[PatternEditor] Play pattern requested pattern={PatternEditor.CurrentPatternIndex} row={PatternEditor.CurrentRow}");
        Play();
    }

    /// <summary>
    /// Executes the TryLoadLivePreviewModule operation.
    /// </summary>
    private bool TryLoadLivePreviewModule()
    {
        if (_song.Format is not (ModuleFormat.XM or ModuleFormat.MOD))
            return false;

        try
        {
            byte[] moduleBytes = [];
            bool patchedOriginal = _originalModuleData is not null && _song.Format switch
            {
                ModuleFormat.XM => XmModulePatternPatcher.TryCreatePatchedModule(_song, _originalModuleData, out moduleBytes),
                ModuleFormat.MOD => ModModulePatternPatcher.TryCreatePatchedModule(_song, _originalModuleData, out moduleBytes),
                _ => false
            };
            if (!patchedOriginal)
            {
                if (_song.Format != ModuleFormat.XM)
                    return false;

                using var stream = new MemoryStream();
                XmModuleExporter.Save(_song, stream, CreateXmExportOptions());
                moduleBytes = stream.ToArray();
            }

            string extension = GetNativeModuleExtension(_song);
            bool loaded = Audio.ModulePlayer.Load(moduleBytes, $"{_song.Title} live-preview{extension}");
            AppLogger.Info($"[Transport] Live {_song.Format} render {(loaded ? "loaded" : "failed")} bytes={moduleBytes.Length} dirty={IsDirty} patchedOriginal={patchedOriginal}");
            return loaded;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Transport] Live XM render failed.");
            return false;
        }
    }

    /// <summary>
    /// Executes the Pause operation.
    /// </summary>
    private void Pause()
    {
        AppLogger.Info($"[Transport] Pause requested scope={PlaybackScope} playheadBeat={SongEditor.PlayheadBeat:0.###}");
        Audio.Pause();
        PlaybackState = PlaybackState.Paused;
        StatusText = "Paused";
    }

    /// <summary>
    /// Executes the Stop operation.
    /// </summary>
    private void Stop()
    {
        AppLogger.Info($"[Transport] Stop requested scope={PlaybackScope} playheadBeat={SongEditor.PlayheadBeat:0.###}");
        Audio.Stop();
        Audio.UseAudioFilePlayer = false;
        ClearTrackMeters();
        _modulePreviewActive = false;
        _modulePreviewPattern = -1;
        _modulePreviewOrder = -1;
        Audio.ModulePlayer.LoopEnabled = false;
        if (Audio.ModulePlayer.IsLoaded)
            Audio.ModulePlayer.UnmuteAllChannels();

        int resetOrder = PreferRestartOnStop && StartAtRestartOrder && _song.RestartOrder >= 0
            ? _song.RestartOrder
            : 0;
        double resetBeat = 0;
        if (resetOrder > 0 && !TryGetOrderStartBeat(resetOrder, out resetBeat))
            resetBeat = 0;

        AppLogger.Info($"[Transport] Reset position order={resetOrder} beat={resetBeat:0.###} restartEnabled={StartAtRestartOrder} preferRestartOnStop={PreferRestartOnStop} restartOrder={_song.RestartOrder}");
        if (Audio.ModulePlayer.IsLoaded)
            Audio.ModulePlayer.SeekToOrder(resetOrder, 0);

        PatternEditor.CurrentRow = 0;
        SongEditor.PlayheadBeat = resetBeat;
        PianoRoll.PlayheadBeat = 0;
        ChannelRack.UpdatePlaybackRow(0);

        PlaybackState = PlaybackState.Stopped;
        StatusText = "Stopped";
    }

    /// <summary>
    /// Executes the TogglePlayback operation.
    /// </summary>
    private void TogglePlayback()
    {
        AppLogger.Info($"[Shortcut] TogglePlayback state={PlaybackState} scope={PlaybackScope}");
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    /// <summary>
    /// Executes the SetPlaybackScope operation.
    /// </summary>
    private void SetPlaybackScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return;

        if (!Enum.TryParse(scope, ignoreCase: true, out PlaybackScope parsed))
            return;

        PlaybackScope = parsed;
        StatusText = $"Play scope: {PlaybackScope}";
        AppLogger.Info($"[Shortcut] PlaybackScope set to {PlaybackScope}");
    }

    /// <summary>
    /// Executes the ApplyLogSettings operation.
    /// </summary>
    private void ApplyLogSettings()
    {
        string target = string.IsNullOrWhiteSpace(LogDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "amChipper", "Logs")
            : Environment.ExpandEnvironmentVariables(LogDirectory.Trim());

        try
        {
            AppLogger.VerboseEnabled = VerboseLogging;
            AppLogger.Initialise(target);
            LogDirectory = AppLogger.LogDirectory;
            AppLogger.Info($"[Diagnostics] Log settings applied directory=\"{AppLogger.LogDirectory}\" verbose={VerboseLogging} dependencyDetails={LogDependencyLoadDetails}");
            StatusText = $"Logging to {AppLogger.LogFilePath}";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Diagnostics] Failed to apply log settings.");
            StatusText = $"Could not change log directory: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the RefreshAudioOutputDevices operation.
    /// </summary>
    private void RefreshAudioOutputDevices()
    {
        string previous = SelectedAudioOutputDevice;
        AudioOutputDevices.Clear();
        AudioOutputDevices.Add("Default WaveOut");
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            try
            {
                var caps = WaveOut.GetCapabilities(i);
                AudioOutputDevices.Add($"{i}: {caps.ProductName}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[AudioSettings] Could not read WaveOut device {i}: {ex.Message}");
            }
        }

        SelectedAudioOutputDevice = AudioOutputDevices.Contains(previous) ? previous : "Default WaveOut";
        AppLogger.Info($"[AudioSettings] Refreshed output devices count={AudioOutputDevices.Count}");
    }

    /// <summary>
    /// Executes the ApplyAudioSettings operation.
    /// </summary>
    private void ApplyAudioSettings(bool silent = false)
    {
        int deviceNumber = -1;
        if (!string.IsNullOrWhiteSpace(SelectedAudioOutputDevice))
        {
            int colon = SelectedAudioOutputDevice.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && int.TryParse(SelectedAudioOutputDevice[..colon], out int parsed))
                deviceNumber = parsed;
        }

        try
        {
            Audio.Reconfigure(AudioSampleRate, deviceNumber, AudioLatencyMs, AudioBufferCount);
            AppLogger.Info($"[AudioSettings] Applied output=\"{SelectedAudioOutputDevice}\" sampleRate={AudioSampleRate} latency={AudioLatencyMs} buffers={AudioBufferCount}");
            if (!silent)
                StatusText = $"Audio output applied: {SelectedAudioOutputDevice}";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[AudioSettings] Failed to apply audio settings.");
            if (!silent)
                StatusText = $"Could not apply audio output: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the OpenLogFolder operation.
    /// </summary>
    private void OpenLogFolder()
    {
        string folder = string.IsNullOrWhiteSpace(LogDirectory) ? AppLogger.LogDirectory : LogDirectory;
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Diagnostics] Failed to open log folder.");
            StatusText = $"Could not open log folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the SaveConfiguration operation.
    /// </summary>
    private void SaveConfiguration()
    {
        try
        {
            AppConfigurationStore.Save(CaptureConfiguration());
            StatusText = $"Saved configuration: {AppConfigurationStore.DefaultPath}";
            AppLogger.Info($"[Settings] Configuration saved path=\"{AppConfigurationStore.DefaultPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to save configuration.");
            StatusText = $"Could not save configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the LoadConfiguration operation.
    /// </summary>
    private void LoadConfiguration(bool silent = false)
    {
        try
        {
            ApplyConfiguration(AppConfigurationStore.Load());
            if (!silent)
                StatusText = $"Loaded configuration: {AppConfigurationStore.DefaultPath}";
            AppLogger.Info($"[Settings] Configuration loaded path=\"{AppConfigurationStore.DefaultPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to load configuration.");
            if (!silent)
                StatusText = $"Could not load configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the ExportConfiguration operation.
    /// </summary>
    private void ExportConfiguration()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export amChipper Configuration",
            Filter = "amChipper configuration (*.amchipsettings)|*.amchipsettings|JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = "amChipper-settings.amchipsettings"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            AppConfigurationStore.Save(CaptureConfiguration(), dlg.FileName);
            StatusText = $"Exported configuration: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Settings] Configuration exported path=\"{dlg.FileName}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to export configuration.");
            StatusText = $"Could not export configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the ImportConfiguration operation.
    /// </summary>
    private void ImportConfiguration()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import amChipper Configuration",
            Filter = "amChipper configuration (*.amchipsettings;*.json)|*.amchipsettings;*.json|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var configuration = AppConfigurationStore.Load(dlg.FileName);
            ApplyConfiguration(configuration);
            AppConfigurationStore.Save(configuration);
            StatusText = $"Imported configuration: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Settings] Configuration imported path=\"{dlg.FileName}\" savedTo=\"{AppConfigurationStore.DefaultPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to import configuration.");
            StatusText = $"Could not import configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Registers amChipper as a per-user Open With target for supported project/module files.
    /// </summary>
    private void RegisterOpenWith()
    {
        try
        {
            int count = FileAssociationService.RegisterCurrentExecutable();
            StatusText = $"Registered amChipper in Open With for {count} file types.";
            AppLogger.Info($"[Settings] Open With registration completed extensions={count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Open With registration failed.");
            StatusText = $"Could not register Open With: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the ResetConfiguration operation.
    /// </summary>
    private void ResetConfiguration()
    {
        try
        {
            ApplyConfiguration(new AppConfiguration());
            AppConfigurationStore.Save(CaptureConfiguration());
            StatusText = "Reset configuration to defaults.";
            AppLogger.Info($"[Settings] Configuration reset path=\"{AppConfigurationStore.DefaultPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to reset configuration.");
            StatusText = $"Could not reset configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes the CaptureConfiguration operation.
    /// </summary>
    private AppConfiguration CaptureConfiguration() => new()
    {
        SelectedTheme = SelectedTheme,
        ShowUiShine = ShowUiShine,
        ShowPanelShadows = ShowPanelShadows,
        WorkspaceDensity = WorkspaceDensity,
        ToolbarButtonSize = ToolbarButtonSize,
        MainLeftPanelWidth = MainLeftPanelWidth,
        MainTabOrder = MainTabOrder,
        Language = SelectedLanguage,
        ShowTipsOnStartup = ShowTipsOnStartup,
        LastTipIndex = _lastTipIndex,
        ShowOldschoolAboutEffects = ShowOldschoolAboutEffects,
        AutoSaveConfigurationOnExit = AutoSaveConfigurationOnExit,
        ToolTipInitialDelayMs = ToolTipInitialDelayMs,
        ToolTipDurationMs = ToolTipDurationMs,
        HelpTextScale = HelpTextScale,
        PreferRestartOnStop = PreferRestartOnStop,
        SoloSelectedPianoRollChannel = SoloSelectedPianoRollChannel,
        AutoOpenPianoRollOnClipSelect = AutoOpenPianoRollOnClipSelect,
        NotePreviewMode = NotePreviewMode,
        PianoRollTypingKeyboardEnabled = PianoRollTypingKeyboardEnabled,
        PianoRollTypingKeyboardBaseNote = PianoRollTypingKeyboardBaseNote,
        PianoRollTypingKeyboardVelocity = PianoRollTypingKeyboardVelocity,
        MidiExportPatternDefault = MidiExportPatternDefault,
        ShowAdvancedMixerReadouts = ShowAdvancedMixerReadouts,
        VisualizerIntensity = VisualizerIntensity,
        VisualizerPeakHold = VisualizerPeakHold,
        SpectrumAnalyzerMode = SpectrumAnalyzerMode,
        ShowModuleDiagnostics = ShowModuleDiagnostics,
        ConfirmNativeExportLimitations = ConfirmNativeExportLimitations,
        PreferSongLengthDatabase = PreferSongLengthDatabase,
        SidXmExportMode = SidXmExportMode,
        ChipRenderTailSeconds = ChipRenderTailSeconds,
        AudioOutputDevice = SelectedAudioOutputDevice,
        AudioSampleRate = AudioSampleRate,
        AudioLatencyMs = AudioLatencyMs,
        AudioBufferCount = AudioBufferCount,
        VerboseLogging = VerboseLogging,
        LogDependencyLoadDetails = LogDependencyLoadDetails,
        LogDirectory = LogDirectory
    };

    /// <summary>
    /// Executes the ApplyConfiguration operation.
    /// </summary>
    private void ApplyConfiguration(AppConfiguration configuration)
    {
        SelectedTheme = NormalizeThemeName(configuration.SelectedTheme);
        ShowUiShine = configuration.ShowUiShine;
        ShowPanelShadows = configuration.ShowPanelShadows;
        WorkspaceDensity = NormalizeOption(configuration.WorkspaceDensity, WorkspaceDensityOptions, "Balanced");
        ToolbarButtonSize = NormalizeOption(configuration.ToolbarButtonSize, ToolbarButtonSizeOptions, "Balanced");
        MainLeftPanelWidth = Math.Clamp(configuration.MainLeftPanelWidth <= 0 ? 200 : configuration.MainLeftPanelWidth, 140, 320);
        MainTabOrder = configuration.MainTabOrder ?? [];
        SelectedLanguage = AppHelpContent.NormalizeLanguage(configuration.Language);
        ShowTipsOnStartup = configuration.ShowTipsOnStartup;
        _lastTipIndex = Math.Max(0, configuration.LastTipIndex);
        ShowOldschoolAboutEffects = configuration.ShowOldschoolAboutEffects;
        AutoSaveConfigurationOnExit = configuration.AutoSaveConfigurationOnExit;
        ToolTipInitialDelayMs = configuration.ToolTipInitialDelayMs;
        ToolTipDurationMs = configuration.ToolTipDurationMs;
        HelpTextScale = configuration.HelpTextScale;
        PreferRestartOnStop = configuration.PreferRestartOnStop;
        SoloSelectedPianoRollChannel = configuration.SoloSelectedPianoRollChannel;
        AutoOpenPianoRollOnClipSelect = configuration.AutoOpenPianoRollOnClipSelect;
        NotePreviewMode = NormalizeOption(configuration.NotePreviewMode, NotePreviewModes, "Hold While Pressed");
        PianoRollTypingKeyboardEnabled = configuration.PianoRollTypingKeyboardEnabled;
        PianoRollTypingKeyboardBaseNote = configuration.PianoRollTypingKeyboardBaseNote;
        PianoRollTypingKeyboardVelocity = configuration.PianoRollTypingKeyboardVelocity;
        MidiExportPatternDefault = NormalizeOption(configuration.MidiExportPatternDefault, MidiExportPatternDefaults, "Current Pattern");
        ShowAdvancedMixerReadouts = configuration.ShowAdvancedMixerReadouts;
        VisualizerIntensity = configuration.VisualizerIntensity;
        VisualizerPeakHold = configuration.VisualizerPeakHold;
        SpectrumAnalyzerMode = NormalizeOption(configuration.SpectrumAnalyzerMode, SpectrumAnalyzerModes, "Studio Analyzer");
        ShowModuleDiagnostics = configuration.ShowModuleDiagnostics;
        ConfirmNativeExportLimitations = configuration.ConfirmNativeExportLimitations;
        PreferSongLengthDatabase = configuration.PreferSongLengthDatabase;
        SidXmExportMode = NormalizeSidXmExportMode(configuration.SidXmExportMode);
        ChipRenderTailSeconds = configuration.ChipRenderTailSeconds;
        SelectedAudioOutputDevice = AudioOutputDevices.Contains(configuration.AudioOutputDevice)
            ? configuration.AudioOutputDevice
            : "Default WaveOut";
        AudioSampleRate = configuration.AudioSampleRate <= 0 ? 44100 : configuration.AudioSampleRate;
        AudioLatencyMs = configuration.AudioLatencyMs <= 0 ? 200 : configuration.AudioLatencyMs;
        AudioBufferCount = configuration.AudioBufferCount <= 0 ? 4 : configuration.AudioBufferCount;
        VerboseLogging = configuration.VerboseLogging;
        LogDependencyLoadDetails = configuration.LogDependencyLoadDetails;
        LogDirectory = string.IsNullOrWhiteSpace(configuration.LogDirectory) ? AppLogger.LogDirectory : configuration.LogDirectory;
    }

    /// <summary>
    /// Executes the ShowStartupTipIfEnabled operation.
    /// </summary>
    public void ShowStartupTipIfEnabled()
    {
        if (ShowTipsOnStartup)
            ShowTipWindow(startup: true);
    }

    /// <summary>
    /// Executes the SaveConfigurationOnExit operation.
    /// </summary>
    public void SaveConfigurationOnExit()
    {
        if (!AutoSaveConfigurationOnExit)
            return;

        try
        {
            AppConfigurationStore.Save(CaptureConfiguration());
            AppLogger.Info($"[Settings] Configuration auto-saved path=\"{AppConfigurationStore.DefaultPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[Settings] Failed to auto-save configuration.");
        }
    }

    /// <summary>
    /// Executes the L operation.
    /// </summary>
    private string L(string key) => AppHelpContent.Translate(SelectedLanguage, key);

    /// <summary>
    /// Formats a localized string with invariant arguments.
    /// </summary>
    private string FormatL(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, L(key), args);

    /// <summary>
    /// Executes the ShowTipWindow operation.
    /// </summary>
    private void ShowTipWindow(bool startup)
    {
        var tips = AppHelpContent.GetTips(SelectedLanguage);
        if (tips.Count == 0)
            return;

        int index = Math.Clamp(_lastTipIndex, 0, tips.Count - 1);
        string tip = tips[index];
        _lastTipIndex = (index + 1) % tips.Count;

        var neverAgain = new CheckBox
        {
            Content = L("ShowTipsStartup"),
            IsChecked = ShowTipsOnStartup,
            Margin = new Thickness(0, 14, 0, 0),
            ToolTip = L("TipShowStartup")
        };

        var text = new TextBlock
        {
            Text = tip,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            FontSize = 15,
            LineHeight = 23,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var nextButton = new Button { Content = L("NextTip"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), ToolTip = L("TipNext") };
        var helpButton = new Button { Content = L("OpenHelp"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), ToolTip = L("TipOpenHelp") };
        var closeButton = new Button { Content = L("Close"), IsDefault = true, MinWidth = 80, ToolTip = L("TipClose") };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(nextButton);
        buttons.Children.Add(helpButton);
        buttons.Children.Add(closeButton);

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = L("TipHeader"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Application.Current.TryFindResource("AccentLight") as Brush ?? Brushes.DeepSkyBlue
        });
        panel.Children.Add(text);
        panel.Children.Add(neverAgain);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = L("TipWindowTitle"),
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MinHeight = 240,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Application.Current.TryFindResource("BgPanel") as Brush ?? Brushes.Black,
            Content = panel
        };

        nextButton.Click += (_, _) =>
        {
            int next = Math.Clamp(_lastTipIndex, 0, tips.Count - 1);
            text.Text = tips[next];
            _lastTipIndex = (next + 1) % tips.Count;
        };
        helpButton.Click += (_, _) => ShowHelpWindow();
        closeButton.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            ShowTipsOnStartup = neverAgain.IsChecked == true;
            SaveConfiguration();
        };

        window.ShowDialog();
    }

    /// <summary>
    /// Executes the ShowHelpWindow operation.
    /// </summary>
    private void ShowHelpWindow()
    {
        Brush bgPanel = Application.Current.TryFindResource("BgPanel") as Brush ?? Brushes.Black;
        Brush bgControl = Application.Current.TryFindResource("BgControl") as Brush ?? Brushes.Black;
        Brush border = Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray;
        Brush textPrimary = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        Brush textSecondary = Application.Current.TryFindResource("TextSecondary") as Brush ?? Brushes.LightGray;
        Brush accentLight = Application.Current.TryFindResource("AccentLight") as Brush ?? Brushes.DeepSkyBlue;

        TextBlock Title(string value) => new()
        {
            Text = value,
            Foreground = accentLight,
            FontSize = 18 * HelpTextScale,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Border Card(string icon, string title, string body) => new()
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 12, 12),
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = bgControl,
            Child = new StackPanel
            {
                Children =
                {
                    new Border
                    {
                        Width = 34,
                        Height = 34,
                        CornerRadius = new CornerRadius(8),
                        Background = Application.Current.TryFindResource("BgSelect") as Brush ?? Brushes.DarkSlateBlue,
                        BorderBrush = accentLight,
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 8),
                        Child = new TextBlock
                        {
                            Text = icon,
                            Foreground = textPrimary,
                            FontSize = 18 * HelpTextScale,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    Title(title),
                    new TextBlock
                    {
                        Text = body,
                        Foreground = textSecondary,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 20 * HelpTextScale,
                        FontSize = 13 * HelpTextScale
                    }
                }
            }
        };

        Border ShortcutCard() => new()
        {
            Padding = new Thickness(16),
            Margin = new Thickness(18),
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = bgControl,
            Child = new TextBlock
            {
                Text =
@"Keyboard / Workflow

Space          Play / pause current scope
Shift+Space    Stop
Ctrl+1         Song playback scope
Ctrl+2         Pattern playback scope
Ctrl+3         Piano-roll playback scope
Ctrl+N         New song
Ctrl+O         Open supported module/audio/project
Ctrl+S         Save
Ctrl+Shift+S   Save as
Ctrl+D         Duplicate selected block or pattern
F1             Feature reference

Analyzer

Click the compact playlist spectrum to cycle analyzer mode and open the Analyzer rack.
Use Settings -> Mixer Visualizer to tune intensity, peak hold and analyzer mode.",
                Foreground = textSecondary,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14 * HelpTextScale,
                LineHeight = 22 * HelpTextScale
            }
        };

        var quickGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(18) };
        quickGrid.Children.Add(Card("+", "Load / Create", "Open tracker modules, SID/NSF chip sources, audio renders, MIDI/FSC scores, or create a new native amChipper project."));
        quickGrid.Children.Add(Card("▦", "Playlist", "Draw arrangement blocks, duplicate patterns, zoom the timeline, follow restart order and jump playback from arbitrary positions."));
        quickGrid.Children.Add(Card("▤", "Piano Roll", "Edit pattern/channel notes, preview the real instrument, preserve tracker FX rows, export MIDI/FSC and edit velocity lanes."));
        quickGrid.Children.Add(Card("#", "Tracker Editor", "Work directly with tracker rows, channels, instruments, volume columns, raw effects and effect parameters."));
        quickGrid.Children.Add(Card("▥", "Analyzer", "Use logarithmic spectrum bands, peak hold, master output diagnostics, timing readout and compact/mixer/pro analyzer modes."));
        quickGrid.Children.Add(Card("☰", "Channel Rack", "Inspect channel instruments, live state, mute/solo, effect summaries and per-channel workflow routing."));
        quickGrid.Children.Add(Card("◧", "Mixer", "Control master/channel volume, pan, meters, visualizer intensity, peak hold and audio output behaviour."));
        quickGrid.Children.Add(Card("↯", "Formats", "Route XM/MOD/S3M/IT through module playback, SID/NSF through chip rendering, and convert to XM/WAV/MP3/MIDI/FSC."));
        quickGrid.Children.Add(Card("⚙", "Settings", "Configure language, theme, density, tooltips, audio device, sample rate, latency, module conversion, logging and startup tips."));

        var reference = new TextBox
        {
            Text = AppHelpContent.GetHelpText(SelectedLanguage),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14 * HelpTextScale,
            Foreground = textPrimary,
            Background = bgPanel
        };

        var fullReferenceText = reference.Text;
        var searchBox = new TextBox
        {
            Margin = new Thickness(18, 14, 18, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Text = string.Empty,
            ToolTip = "Type to filter the full feature reference.",
            Foreground = textPrimary,
            Background = bgControl,
            BorderBrush = border,
            BorderThickness = new Thickness(1)
        };
        searchBox.TextChanged += (_, _) =>
        {
            string query = searchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                reference.Text = fullReferenceText;
                return;
            }

            var lines = fullReferenceText
                .Split(Environment.NewLine)
                .Where(line => line.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || string.IsNullOrWhiteSpace(line)
                    || !line.StartsWith("-", StringComparison.Ordinal))
                .ToArray();
            reference.Text = lines.Length == 0
                ? string.Format(CultureInfo.CurrentCulture, L("NoHelpEntriesMatched"), query)
                : string.Join(Environment.NewLine, lines);
        };

        var referencePanel = new DockPanel();
        DockPanel.SetDock(searchBox, Dock.Top);
        referencePanel.Children.Add(searchBox);
        referencePanel.Children.Add(reference);

        var tabs = new TabControl
        {
            Background = bgPanel,
            BorderThickness = new Thickness(0)
        };
        tabs.Items.Add(new TabItem { Header = L("HelpFeatureGuide"), Content = new ScrollViewer { Content = quickGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = L("HelpShortcuts"), Content = new ScrollViewer { Content = ShortcutCard(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = L("HelpSearchableReference"), Content = referencePanel });

        var window = new Window
        {
            Title = L("HelpWindowTitle"),
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 780,
            Height = 640,
            MinWidth = 520,
            MinHeight = 420,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = bgPanel,
            Content = tabs
        };

        WindowChromeTheme.Attach(window);
        window.Show();
        AppLogger.Info($"[Help] Feature reference opened language={SelectedLanguage}");
    }

    /// <summary>
    /// Executes the ShowAboutWindow operation.
    /// </summary>
    private void ShowAboutWindow()
    {
        string version = NormalizeDisplayVersion(typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "v0.1.0.0");

        var logo = new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo_splash.png")),
            Width = 260,
            Height = 118,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect
            {
                BlurRadius = ShowOldschoolAboutEffects ? 22 : 8,
                ShadowDepth = 0,
                Opacity = ShowOldschoolAboutEffects ? 0.9 : 0.35,
                Color = (Application.Current.TryFindResource("AccentLight") as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue
            },
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(1, 1),
                    new SkewTransform(0, 0),
                    new TranslateTransform(0, 0)
                }
            }
        };

        if (ShowOldschoolAboutEffects && logo.RenderTransform is TransformGroup group)
        {
            if (group.Children[0] is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1.025, TimeSpan.FromMilliseconds(680))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }

            if (group.Children[1] is SkewTransform skew)
            {
                skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(-1.3, 1.3, TimeSpan.FromMilliseconds(920))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
        }

        Brush bgDeep = Application.Current.TryFindResource("BgDeep") as Brush ?? Brushes.Black;
        Brush bgPanel = Application.Current.TryFindResource("BgPanel") as Brush ?? Brushes.Black;
        Brush bgControl = Application.Current.TryFindResource("BgControl") as Brush ?? Brushes.Black;
        Brush borderBrush = Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray;
        Brush textPrimary = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        Brush textSecondary = Application.Current.TryFindResource("TextSecondary") as Brush ?? Brushes.LightGray;
        Brush accent = Application.Current.TryFindResource("Accent") as Brush ?? Brushes.DeepSkyBlue;
        Brush accentLight = Application.Current.TryFindResource("AccentLight") as Brush ?? Brushes.DeepSkyBlue;

        TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = accentLight,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Border Card(UIElement content) => new()
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = bgControl,
            Child = content
        };

        var overview = new Grid { Margin = new Thickness(16) };
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        overview.ColumnDefinitions.Add(new ColumnDefinition());

        var logoPanel = new StackPanel();
        logoPanel.Children.Add(logo);
        logoPanel.Children.Add(new TextBlock
        {
            Text = version,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = accentLight,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        });
        logoPanel.Children.Add(new TextBlock
        {
            Text = ".NET 10 / WPF",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = textSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var logoCard = Card(logoPanel);
        Grid.SetColumn(logoCard, 0);
        overview.Children.Add(logoCard);

        var summaryPanel = new StackPanel();
        summaryPanel.Children.Add(SectionTitle("amChipper"));
        summaryPanel.Children.Add(new TextBlock
        {
            Text = L("AboutSummary"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = textPrimary,
            FontSize = 14,
            LineHeight = 21
        });
        summaryPanel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        summaryPanel.Children.Add(SectionTitle(L("CreditsScroller")));
        summaryPanel.Children.Add(BuildCreditsScroller(bgDeep, borderBrush, textPrimary, textSecondary, accentLight, L));
        var summaryCard = Card(summaryPanel);
        Grid.SetColumn(summaryCard, 2);
        overview.Children.Add(summaryCard);

        var formatList = new ListView
        {
            MinHeight = 420,
            ItemsSource = BuildFormatSupportRows(),
            Background = Application.Current.TryFindResource("BgDeep") as Brush ?? Brushes.Black,
            Foreground = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            BorderBrush = Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray,
            BorderThickness = new Thickness(1)
        };
        ApplyThemedListStyles(formatList);
        var formatGrid = new GridView();
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnFormat"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Type)), Width = 78 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnExtension"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Extension)), Width = 82 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnName"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.DisplayName)), Width = 230 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnPlayback"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Playback)), Width = 150 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnEngine"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Engine)), Width = 150 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnExportEdit"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Export)), Width = 220 });
        formatGrid.Columns.Add(new GridViewColumn { Header = L("ColumnNotes"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormatSupportRow.Notes)), Width = 330 });
        formatList.View = formatGrid;
        ApplyThemedGridHeader(formatGrid);

        var pluginList = new ListView
        {
            MinHeight = 420,
            ItemsSource = BuildRuntimePluginRows(),
            Background = Application.Current.TryFindResource("BgDeep") as Brush ?? Brushes.Black,
            Foreground = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            BorderBrush = Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray,
            BorderThickness = new Thickness(1)
        };
        ApplyThemedListStyles(pluginList);
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = L("ColumnPlugin"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RuntimePluginRow.Name)), Width = 220 });
        gridView.Columns.Add(new GridViewColumn { Header = L("ColumnState"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RuntimePluginRow.State)), Width = 80 });
        gridView.Columns.Add(new GridViewColumn { Header = L("ColumnVersion"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RuntimePluginRow.Version)), Width = 160 });
        gridView.Columns.Add(new GridViewColumn { Header = L("ColumnPath"), DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RuntimePluginRow.Path)), Width = 520 });
        pluginList.View = gridView;
        ApplyThemedGridHeader(gridView);

        var closeButton = new Button
        {
            Content = L("Close"),
            IsDefault = true,
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 18, 0)
        };

        var formatPanel = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var formatTitle = new TextBlock
        {
            Text = L("FormatFeatureSupport"),
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = accentLight,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var formatIntro = new TextBlock
        {
            Text = L("FormatFeatureSupportIntro"),
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var formatLegend = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        formatLegend.Children.Add(Card(new TextBlock { Text = "AMC: compressed native project + embedded exact source", Foreground = textPrimary, TextWrapping = TextWrapping.Wrap }));
        formatLegend.Children.Add(Card(new TextBlock { Text = "XM/MOD: editable native patch path with row/effect retention", Foreground = textPrimary, TextWrapping = TextWrapping.Wrap }));
        formatLegend.Children.Add(Card(new TextBlock { Text = "IT/S3M/OpenMPT: broad playback and render/convert support", Foreground = textPrimary, TextWrapping = TextWrapping.Wrap }));
        formatLegend.Children.Add(Card(new TextBlock { Text = "SID/NSF: internal chip trace, audio render, and reconstructed editable rows", Foreground = textPrimary, TextWrapping = TextWrapping.Wrap }));
        DockPanel.SetDock(formatTitle, Dock.Top);
        DockPanel.SetDock(formatIntro, Dock.Top);
        DockPanel.SetDock(formatLegend, Dock.Top);
        formatPanel.Children.Add(formatTitle);
        formatPanel.Children.Add(formatIntro);
        formatPanel.Children.Add(formatLegend);
        formatPanel.Children.Add(formatList);

        var runtimePanel = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var runtimeTitle = new TextBlock
        {
            Text = L("InstalledPlugins"),
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = accentLight,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(runtimeTitle, Dock.Top);
        runtimePanel.Children.Add(runtimeTitle);
        runtimePanel.Children.Add(pluginList);

        var logPanel = BuildAboutLogViewer(bgDeep, bgControl, borderBrush, textPrimary, textSecondary, accentLight);

        string changelogMarkdown = LoadBundledText("CHANGELOG.md", "# amChipper Changelog\r\n\r\nNo bundled changelog was found.");
        var changelogView = BuildExpandableChangelog(changelogMarkdown, bgDeep, borderBrush, textPrimary, textSecondary, accentLight);
        var changelogPanel = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var changelogTitle = new TextBlock
        {
            Text = L("DetailedChangelog"),
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = accentLight,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var changelogIntro = new TextBlock
        {
            Text = L("ChangelogIntro"),
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(changelogTitle, Dock.Top);
        DockPanel.SetDock(changelogIntro, Dock.Top);
        changelogPanel.Children.Add(changelogTitle);
        changelogPanel.Children.Add(changelogIntro);
        changelogPanel.Children.Add(changelogView);

        Border AmcFeature(string icon, string title, string body) => Card(new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new Border
                {
                    Width = 42,
                    Height = 42,
                    CornerRadius = new CornerRadius(8),
                    Background = Application.Current.TryFindResource("BgSelect") as Brush ?? Brushes.DarkSlateBlue,
                    BorderBrush = accentLight,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    Child = new TextBlock
                    {
                        Text = icon,
                        Foreground = textPrimary,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            Foreground = accentLight,
                            FontWeight = FontWeights.Bold,
                            FontSize = 14,
                            Margin = new Thickness(0, 0, 0, 4)
                        },
                        new TextBlock
                        {
                            Text = body,
                            Foreground = textSecondary,
                            TextWrapping = TextWrapping.Wrap,
                            LineHeight = 18
                        }
                    }
                }
            }
        });

        var amcGrid = new Grid { Margin = new Thickness(22, 18, 22, 16) };
        amcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        amcGrid.RowDefinitions.Add(new RowDefinition());
        var amcHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        amcHeader.Children.Add(new TextBlock
        {
            Text = "amChipper AMC Native Chip Module",
            Foreground = accentLight,
            FontSize = 18,
            FontWeight = FontWeights.Bold
        });
        amcHeader.Children.Add(new TextBlock
        {
            Text = ".amc is the amChipper-native container: smaller than the imported source when compression wins, source-preserving for exact playback, and richer than legacy tracker limits.",
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        amcGrid.Children.Add(amcHeader);
        var amcFeatures = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        amcFeatures.Children.Add(AmcFeature("▣", "Source-preserving exact playback", "Imported XM/MOD/etc bytes are stored as a compressed embedded source section, so the original module player path remains available after reopening the .amc."));
        amcFeatures.Children.Add(AmcFeature("⇣", "Smaller compressed container", "Metadata and embedded source are Brotli-compressed separately. The included Outlive no2 example is smaller as .amc than the original XM."));
        amcFeatures.Children.Add(AmcFeature("64", "Modern channel headroom", "The amChipper song model supports up to 64 channels, beyond classic MOD and FastTracker XM practical limits."));
        amcFeatures.Children.Add(AmcFeature("FX", "Tracker effects retained", "Rows preserve note, instrument, volume column, raw effect command and effect parameter data for tracker-faithful editing."));
        amcFeatures.Children.Add(AmcFeature("▤", "DAW editing model", "The container also carries normalized patterns, order list, tracks, instruments, samples, playlist blocks, channel state and automation-ready data."));
        amcFeatures.Children.Add(AmcFeature("↯", "Hybrid future path", "AMC can act as both an exact source wrapper and the foundation for amChipper-only features that old tracker formats cannot represent."));
        Grid.SetRow(amcFeatures, 1);
        amcGrid.Children.Add(amcFeatures);

        var tabs = new TabControl
        {
            Background = bgPanel,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(16, 0, 16, 12)
        };
        tabs.Items.Add(new TabItem { Header = L("AboutOverview"), Content = overview });
        tabs.Items.Add(new TabItem { Header = L("AboutAmcFormat"), Content = new ScrollViewer { Content = amcGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = L("AboutChangelog"), Content = changelogPanel });
        tabs.Items.Add(new TabItem { Header = L("AboutFormats"), Content = formatPanel });
        tabs.Items.Add(new TabItem { Header = L("AboutRuntime"), Content = runtimePanel });
        tabs.Items.Add(new TabItem { Header = L("AboutLogs"), Content = logPanel });

        var footer = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(0, 0, 0, 16)
        };
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);

        var root = new DockPanel { Background = bgPanel };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(tabs);

        var window = new Window
        {
            Title = L("AboutWindowTitle"),
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1040,
            Height = 700,
            MinWidth = 760,
            MinHeight = 520,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = bgPanel,
            Content = root
        };

        WindowChromeTheme.Attach(window);
        closeButton.Click += (_, _) => window.Close();
        window.ShowDialog();
        AppLogger.Info("[Help] About window opened.");
    }

    /// <summary>
    /// Keeps About-window version text readable by hiding build metadata hashes appended by the SDK.
    /// </summary>
    private static string NormalizeDisplayVersion(string version)
    {
        string clean = string.IsNullOrWhiteSpace(version) ? "v0.1.0.0" : version.Trim();
        int metadataIndex = clean.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex > 0)
            clean = clean[..metadataIndex];

        return clean.StartsWith('v') ? clean : $"v{clean}";
    }

    /// <summary>
    /// Builds the About window log-viewer tab with refresh, copy, and log-folder actions.
    /// </summary>
    private DockPanel BuildAboutLogViewer(
        Brush bgDeep,
        Brush bgControl,
        Brush borderBrush,
        Brush textPrimary,
        Brush textSecondary,
        Brush accentLight)
    {
        var panel = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = L("AboutLogViewer"),
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = accentLight
        });

        var pathText = new TextBlock
        {
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        header.Children.Add(pathText);

        var hintText = new TextBlock
        {
            Text = L("LogTailNote"),
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        header.Children.Add(hintText);

        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var refreshButton = new Button { Content = $"↻ {L("RefreshLog")}", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        var copyButton = new Button { Content = $"⧉ {L("CopyLog")}", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        var openButton = new Button { Content = $"📁 {L("OpenFolder")}", MinWidth = 120 };
        buttons.Children.Add(refreshButton);
        buttons.Children.Add(copyButton);
        buttons.Children.Add(openButton);
        DockPanel.SetDock(buttons, Dock.Top);
        panel.Children.Add(buttons);

        var filterPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        filterPanel.Children.Add(new TextBlock
        {
            Text = L("LogFilter"),
            Foreground = textSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var filterBox = new ComboBox
        {
            Width = 190,
            ItemsSource = new[] { L("LogFilterAll"), L("LogFilterCritical"), L("LogFilterErrors"), L("LogFilterWarnings"), L("LogFilterInfo"), L("LogFilterDebug") },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        filterPanel.Children.Add(filterBox);
        DockPanel.SetDock(filterPanel, Dock.Top);
        panel.Children.Add(filterPanel);

        Border MetricCard(string caption, Brush brush, out TextBlock valueBlock)
        {
            valueBlock = new TextBlock
            {
                Text = "0",
                Foreground = brush,
                FontWeight = FontWeights.Bold,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 8, 10),
                MinWidth = 116,
                Background = bgControl,
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Effect = Application.Current.TryFindResource("PanelShadow") as Effect,
                Child = new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = caption,
                            Foreground = textSecondary,
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 0, 10, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        valueBlock
                    }
                }
            };
        }

        var warningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57));
        var errorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70));
        var debugBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xB8, 0xFF));
        var infoBrush = accentLight;
        var metrics = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        TextBlock totalCount;
        TextBlock criticalCount;
        TextBlock errorCount;
        TextBlock warningCount;
        TextBlock infoCount;
        TextBlock debugCount;
        metrics.Children.Add(MetricCard("LINES", textPrimary, out totalCount));
        metrics.Children.Add(MetricCard("CRITICAL", errorBrush, out criticalCount));
        metrics.Children.Add(MetricCard("ERRORS", errorBrush, out errorCount));
        metrics.Children.Add(MetricCard("WARN", warningBrush, out warningCount));
        metrics.Children.Add(MetricCard("INFO", infoBrush, out infoCount));
        metrics.Children.Add(MetricCard("DEBUG", debugBrush, out debugCount));
        DockPanel.SetDock(metrics, Dock.Top);
        panel.Children.Add(metrics);

        var logBox = new RichTextBox
        {
            IsReadOnly = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = bgDeep,
            Foreground = textPrimary,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Document = new FlowDocument { PagePadding = new Thickness(0), Background = bgDeep }
        };
        panel.Children.Add(logBox);
        string rawLogText = string.Empty;
        string visibleLogText = string.Empty;

        void RenderLog()
        {
            string[] rawLines = rawLogText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);
            totalCount.Text = rawLines.Length.ToString(CultureInfo.InvariantCulture);
            criticalCount.Text = rawLines.Count(IsCriticalLogLine).ToString(CultureInfo.InvariantCulture);
            errorCount.Text = rawLines.Count(IsErrorLogLine).ToString(CultureInfo.InvariantCulture);
            warningCount.Text = rawLines.Count(IsWarningLogLine).ToString(CultureInfo.InvariantCulture);
            infoCount.Text = rawLines.Count(IsInfoLogLine).ToString(CultureInfo.InvariantCulture);
            debugCount.Text = rawLines.Count(IsDebugLogLine).ToString(CultureInfo.InvariantCulture);

            string filter = filterBox.SelectedIndex switch
            {
                1 => "critical",
                2 => "error",
                3 => "warning",
                4 => "info",
                5 => "debug",
                _ => "all"
            };

            var filteredLines = rawLines.Where(line => LogLineMatchesFilter(line, filter)).ToArray();
            visibleLogText = filteredLines.Length == 0
                ? L("LogViewerEmpty")
                : string.Join(Environment.NewLine, filteredLines);

            logBox.Document = BuildLogViewerDocument(visibleLogText, bgDeep, textPrimary, textSecondary, accentLight);
            logBox.ScrollToEnd();
        }

        void RefreshLog()
        {
            string logPath = AppLogger.LogFilePath;
            pathText.Text = string.IsNullOrWhiteSpace(logPath)
                ? L("NoLogFile")
                : $"{L("LogFile")}: {logPath}";
            rawLogText = ReadLogTail(logPath, 2000);
            if (string.IsNullOrWhiteSpace(rawLogText))
                rawLogText = L("LogViewerEmpty");

            RenderLog();
            AppLogger.Info("[Help] About log viewer refreshed.");
        }

        filterBox.SelectionChanged += (_, _) => RenderLog();
        refreshButton.Click += (_, _) => RefreshLog();
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(visibleLogText))
                Clipboard.SetText(visibleLogText);
        };
        openButton.Click += (_, _) => OpenLogFolder();
        RefreshLog();
        return panel;
    }

    /// <summary>
    /// Builds a syntax-coloured log document for the About log viewer.
    /// </summary>
    private static FlowDocument BuildLogViewerDocument(
        string text,
        Brush background,
        Brush textPrimary,
        Brush textSecondary,
        Brush accentLight)
    {
        var warningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57));
        var errorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70));
        var debugBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xB8, 0xFF));
        var pathBrush = new SolidColorBrush(Color.FromRgb(0x72, 0xE0, 0xC8));
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = background,
            Foreground = textPrimary
        };

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            Brush foreground = ResolveLogLineBrush(line, textPrimary, textSecondary, accentLight, warningBrush, errorBrush, debugBrush);
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 1),
                LineHeight = 16
            };
            if (ReferenceEquals(foreground, errorBrush))
                paragraph.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0x30, 0x50));
            else if (ReferenceEquals(foreground, warningBrush))
                paragraph.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xC8, 0x57));

            paragraph.Inlines.Add(new Run((lineIndex + 1).ToString("0000", CultureInfo.InvariantCulture) + "  ")
            {
                Foreground = textSecondary,
                FontWeight = FontWeights.Light
            });

            if (TrySplitLogPrefix(line, out string prefix, out string rest))
            {
                paragraph.Inlines.Add(new Run(prefix)
                {
                    Foreground = textSecondary,
                    FontWeight = FontWeights.SemiBold
                });
                AddHighlightedLogRuns(paragraph, rest, foreground, pathBrush);
            }
            else
            {
                AddHighlightedLogRuns(paragraph, line, foreground, pathBrush);
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static Brush ResolveLogLineBrush(
        string line,
        Brush textPrimary,
        Brush textSecondary,
        Brush accentLight,
        Brush warningBrush,
        Brush errorBrush,
        Brush debugBrush)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[ERR", StringComparison.OrdinalIgnoreCase))
            return errorBrush;
        if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return warningBrush;
        if (line.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DBG", StringComparison.OrdinalIgnoreCase))
            return debugBrush;
        if (line.Contains("info", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[INF", StringComparison.OrdinalIgnoreCase))
            return accentLight;
        return string.IsNullOrWhiteSpace(line) ? textSecondary : textPrimary;
    }

    /// <summary>
    /// Returns true when a log line is a fatal/critical event.
    /// </summary>
    private static bool IsCriticalLogLine(string line) =>
        line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[FTL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a log line is an error or worse.
    /// </summary>
    private static bool IsErrorLogLine(string line) =>
        IsCriticalLogLine(line) ||
        line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[ERR", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a log line is a warning.
    /// </summary>
    private static bool IsWarningLogLine(string line) =>
        line.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[WRN", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a log line is informational.
    /// </summary>
    private static bool IsInfoLogLine(string line) =>
        line.Contains("info", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[INF", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a log line is debug/trace output.
    /// </summary>
    private static bool IsDebugLogLine(string line) =>
        line.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("trace", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[DBG", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[TRC", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies the selected About-log severity filter.
    /// </summary>
    private static bool LogLineMatchesFilter(string line, string filter) =>
        filter switch
        {
            "critical" => IsCriticalLogLine(line),
            "error" => IsErrorLogLine(line),
            "warning" => IsWarningLogLine(line),
            "info" => IsInfoLogLine(line),
            "debug" => IsDebugLogLine(line),
            _ => true
        };

    private static bool TrySplitLogPrefix(string line, out string prefix, out string rest)
    {
        prefix = string.Empty;
        rest = line;
        int bracket = line.IndexOf(']', StringComparison.Ordinal);
        if (bracket is > 0 and < 80)
        {
            prefix = line[..(bracket + 1)] + " ";
            rest = line[(bracket + 1)..].TrimStart();
            return true;
        }

        int separator = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separator is > 0 and < 80)
        {
            prefix = line[..(separator + 3)];
            rest = line[(separator + 3)..];
            return true;
        }

        return false;
    }

    private static void AddHighlightedLogRuns(Paragraph paragraph, string line, Brush foreground, Brush pathBrush)
    {
        string[] tokens = line.Split(' ');
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            bool looksLikePath = token.Contains(@":\", StringComparison.Ordinal) ||
                token.Contains(".dll", StringComparison.OrdinalIgnoreCase) ||
                token.Contains(".xm", StringComparison.OrdinalIgnoreCase) ||
                token.Contains(".sid", StringComparison.OrdinalIgnoreCase) ||
                token.Contains(".nsf", StringComparison.OrdinalIgnoreCase) ||
                token.Contains(".amc", StringComparison.OrdinalIgnoreCase);

            paragraph.Inlines.Add(new Run(token + (i == tokens.Length - 1 ? string.Empty : " "))
            {
                Foreground = looksLikePath ? pathBrush : foreground,
                FontWeight = looksLikePath ? FontWeights.SemiBold : FontWeights.Normal
            });
        }
    }

    /// <summary>
    /// Reads the end of the current log file without locking the logger.
    /// </summary>
    private static string ReadLogTail(string path, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || maxLines <= 0)
            return string.Empty;

        try
        {
            var lines = new Queue<string>(maxLines + 1);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > maxLines)
                    lines.Dequeue();
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Unable to read log: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads text bundled next to the executable, with a repository-root fallback for development runs.
    /// </summary>
    private static string LoadBundledText(string fileName, string fallback)
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", fileName),
            Path.Combine(Environment.CurrentDirectory, fileName)
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                    return File.ReadAllText(fullPath);
            }
            catch
            {
                // Keep the About window usable even if the optional bundled file is inaccessible.
            }
        }

        return fallback;
    }

    /// <summary>
    /// Builds a lightweight GitHub-style Markdown document for bundled release notes.
    /// </summary>
    private static FlowDocument BuildMarkdownDocument(string markdown, Brush textPrimary, Brush textSecondary, Brush accentLight)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = textPrimary,
            PagePadding = new Thickness(14)
        };

        foreach (string rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                document.Blocks.Add(new Paragraph(new Run(line[2..]))
                {
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentLight,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                document.Blocks.Add(new Paragraph(new Run(line[3..]))
                {
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentLight,
                    Margin = new Thickness(0, 14, 0, 7)
                });
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                document.Blocks.Add(new Paragraph(new Run(line[4..]))
                {
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textPrimary,
                    Margin = new Thickness(0, 10, 0, 4)
                });
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(12, 1, 0, 1),
                    Foreground = textSecondary
                };
                paragraph.Inlines.Add(new Run("• ") { Foreground = accentLight, FontWeight = FontWeights.Bold });
                paragraph.Inlines.Add(new Run(line[2..]));
                document.Blocks.Add(paragraph);
                continue;
            }

            document.Blocks.Add(new Paragraph(new Run(line))
            {
                Foreground = textSecondary,
                Margin = new Thickness(0, 2, 0, 4)
            });
        }

        return document;
    }

    /// <summary>
    /// Builds an FL Studio-style vertical credits scroller for the About overview.
    /// </summary>
    private static Border BuildCreditsScroller(Brush background, Brush borderBrush, Brush textPrimary, Brush textSecondary, Brush accentLight, Func<string, string> l)
    {
        var credits = new StackPanel
        {
            Margin = new Thickness(14, 0, 14, 0),
            RenderTransform = new TranslateTransform()
        };

        void AddCredit(string line, Brush brush, double size = 14, FontWeight? weight = null)
        {
            credits.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = brush,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 7)
            });
        }

        AddCredit("amChipper", accentLight, 20, FontWeights.Bold);
        AddCredit(l("CreditsTagline"), textPrimary, 15, FontWeights.SemiBold);
        AddCredit(l("CreditsCreatedBy"), accentLight, 13, FontWeights.Bold);
        AddCredit("Geir Gustavsen", textPrimary, 17, FontWeights.Bold);
        AddCredit("ZeroLinez Softworx", accentLight, 15, FontWeights.Bold);
        AddCredit(string.Empty, textSecondary);
        AddCredit(l("CreditsMainCoder"), accentLight, 13, FontWeights.Bold);
        AddCredit("Geir Gustavsen", textPrimary);
        AddCredit(l("CreditsCoreWork"), textSecondary, 12);
        AddCredit(string.Empty, textSecondary);
        AddCredit(l("CreditsRuntime"), accentLight, 13, FontWeights.Bold);
        AddCredit("amChipper.Core", textPrimary);
        AddCredit("amChipper.Audio", textPrimary);
        AddCredit("amChipper.AmcPlayer", textPrimary);
        AddCredit("NAudio playback integration", textSecondary, 12);
        AddCredit("libopenmpt tracker playback", textSecondary, 12);
        AddCredit(string.Empty, textSecondary);
        AddCredit(l("CreditsFormats"), accentLight, 13, FontWeights.Bold);
        AddCredit("AMC / AMCHIP / XM / MOD / S3M / IT / SID / NSF / MIDI / FSC / WAV / MP3", textSecondary, 12);
        AddCredit(string.Empty, textSecondary);
        AddCredit(l("CreditsGraphics"), accentLight, 13, FontWeights.Bold);
        AddCredit(l("CreditsGraphicsBody"), textSecondary, 12);
        AddCredit(string.Empty, textSecondary);
        AddCredit("ZeroLinez Softworx", accentLight, 18, FontWeights.Bold);

        Color fadeColor = background is SolidColorBrush solid ? solid.Color : Color.FromRgb(6, 12, 20);
        var scroller = new Grid { ClipToBounds = true };
        void StartScroller()
        {
            if (credits.RenderTransform is not TranslateTransform translate)
                return;

            double from = Math.Max(260, scroller.ActualHeight) + 24;
            double to = -Math.Max(credits.ActualHeight, 520) - 24;
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(38))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = null,
                FillBehavior = FillBehavior.Stop
            });
        }

        scroller.Children.Add(credits);
        scroller.Loaded += (_, _) => StartScroller();
        scroller.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 42,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(
                Color.FromArgb(245, fadeColor.R, fadeColor.G, fadeColor.B),
                Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B),
                90)
        });
        scroller.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 54,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B),
                Color.FromArgb(245, fadeColor.R, fadeColor.G, fadeColor.B),
                90)
        });

        return new Border
        {
            Height = 260,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = background,
            ClipToBounds = true,
            Child = scroller
        };
    }

    /// <summary>
    /// Builds expandable changelog groups from the bundled Markdown changelog.
    /// </summary>
    private static ScrollViewer BuildExpandableChangelog(string markdown, Brush background, Brush borderBrush, Brush textPrimary, Brush textSecondary, Brush accentLight)
    {
        var panel = new StackPanel();
        var versions = new List<ChangelogGroup>();
        ChangelogGroup? currentVersion = null;
        ChangelogGroup? currentSection = null;

        foreach (string rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# ", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentVersion = new ChangelogGroup(line[3..]);
                versions.Add(currentVersion);
                currentSection = null;
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSection = new ChangelogGroup(line[4..]);
                if (currentVersion is not null)
                    currentVersion.Children.Add(currentSection);
                else
                    versions.Add(currentSection);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var target = currentSection ?? currentVersion;
                target?.Items.Add(line[2..]);
            }
            else
            {
                (currentSection ?? currentVersion)?.Items.Add(line);
            }
        }

        foreach (var version in versions)
            panel.Children.Add(CreateChangelogExpander(version, 0, background, borderBrush, textPrimary, textSecondary, accentLight));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };
    }

    /// <summary>
    /// Creates one expandable changelog group.
    /// </summary>
    private static Expander CreateChangelogExpander(ChangelogGroup group, int depth, Brush background, Brush borderBrush, Brush textPrimary, Brush textSecondary, Brush accentLight)
    {
        var body = new StackPanel { Margin = new Thickness(12, 6, 8, 8) };
        foreach (var child in group.Children)
            body.Children.Add(CreateChangelogExpander(child, depth + 1, background, borderBrush, textPrimary, textSecondary, accentLight));

        foreach (string item in group.Items)
        {
            body.Children.Add(new TextBlock
            {
                Text = "• " + item,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 2, 4, 2)
            });
        }

        return new Expander
        {
            Header = $"{group.Title}  ({CountChangelogItems(group)} changes)",
            IsExpanded = depth == 0,
            Foreground = textPrimary,
            Background = depth == 0 ? background : Brushes.Transparent,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(depth * 12, 0, 0, 8),
            Padding = new Thickness(8, 5, 8, 5),
            Content = body
        };
    }

    /// <summary>
    /// Counts every bullet/detail row in an expandable changelog group.
    /// </summary>
    private static int CountChangelogItems(ChangelogGroup group)
    {
        return group.Items.Count + group.Children.Sum(CountChangelogItems);
    }

    /// <summary>
    /// Executes the BuildRuntimePluginRows operation.
    /// </summary>
    private static IReadOnlyList<RuntimePluginRow> BuildRuntimePluginRows()
    {
        var rows = new List<RuntimePluginRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string path, string state)
        {
            string key = string.IsNullOrWhiteSpace(path) ? $"{name}|{state}" : path;
            if (!seen.Add(key))
                return;

            rows.Add(new RuntimePluginRow(name, state, ResolveFileVersion(path), path));
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .OrderBy(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = assembly.GetName().Name ?? assembly.FullName ?? "Assembly";
            string path = string.Empty;
            try { path = assembly.Location; } catch { }
            if (!ShouldShowRuntimePlugin(name, path))
                continue;

            Add($"{name}.dll", path, "loaded");
        }

        foreach (var info in RuntimeDependencyResolver.GetLoadEventsSnapshot())
        {
            if (!ShouldShowRuntimePlugin(info.Name, info.Path))
                continue;
            Add(info.Name, info.Path, info.State);
        }

        foreach (var info in RuntimeDependencyResolver.GetKnownDependencyFiles())
        {
            if (!ShouldShowRuntimePlugin(info.Name, info.Path))
                continue;
            Add(info.Name, info.Path, info.State);
        }

        return rows
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.State, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Executes the BuildFormatSupportRows operation.
    /// </summary>
    private static IReadOnlyList<FormatSupportRow> BuildFormatSupportRows() =>
        ModuleFormatCatalog.Formats
            .OrderBy(format => format.Format)
            .ThenBy(format => format.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(format => format.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(format => new FormatSupportRow(
                format.Type,
                format.Extension,
                format.DisplayName,
                ResolveFormatPlayback(format),
                ResolveFormatEngine(format),
                ResolveFormatExport(format),
                ResolveFormatNotes(format)))
            .ToArray();

    /// <summary>
    /// Describes the player used for an About-window format row.
    /// </summary>
    private static string ResolveFormatPlayback(ModuleFormatInfo format) =>
        format.Format switch
        {
            ModuleFormat.AmChip => "Native AMC",
            ModuleFormat.SID => "Internal SID",
            ModuleFormat.NSF => "Internal NSF",
            ModuleFormat.OpenMpt => "libopenmpt",
            _ => "Native / libopenmpt"
        };

    /// <summary>
    /// Describes the import/export engine used for an About-window format row.
    /// </summary>
    private static string ResolveFormatEngine(ModuleFormatInfo format) =>
        format.Format switch
        {
            ModuleFormat.AmChip => "amChipper.AmcPlayer",
            ModuleFormat.SID => "amChipper.SidPlayer",
            ModuleFormat.NSF => "amChipper.NsfPlayer",
            ModuleFormat.OpenMpt => "OpenMPT bridge",
            ModuleFormat.XM => "XM patcher",
            ModuleFormat.MOD => "MOD patcher",
            ModuleFormat.IT => "IT reader / renderer",
            ModuleFormat.S3M => "S3M reader / renderer",
            _ => "Format catalog"
        };

    /// <summary>
    /// Describes export/edit behavior for an About-window format row.
    /// </summary>
    private static string ResolveFormatExport(ModuleFormatInfo format)
    {
        if (format.DirtyNativePatchSupported)
            return "native patch + conversion";

        return format.Format switch
        {
            ModuleFormat.SID or ModuleFormat.NSF => "keep source + render + trace",
            ModuleFormat.OpenMpt => "render + convert",
            _ => "native keep + render/convert"
        };
    }

    /// <summary>
    /// Adds concise user-facing support notes for a format row.
    /// </summary>
    private static string ResolveFormatNotes(ModuleFormatInfo format) =>
        format.Format switch
        {
            ModuleFormat.AmChip => "Compressed native container with embedded source, normalized rows, 64-channel headroom, and amChipper-only metadata.",
            ModuleFormat.XM => "Strongest editable round-trip path; preserves tracker rows, instruments, volume column, and effect bytes.",
            ModuleFormat.MOD => "Editable native patch path for classic ProTracker-style rows and effects.",
            ModuleFormat.IT => "Playback/import supported; exact native write-back is renderer/conversion focused.",
            ModuleFormat.S3M => "Playback/import supported; exact native write-back is renderer/conversion focused.",
            ModuleFormat.SID => "Chip state is reconstructed from SID register analysis; timing and pattern rows remain active-development territory.",
            ModuleFormat.NSF => "NES APU state is traced into editable material where possible; some driver-specific behavior is reconstructed.",
            ModuleFormat.OpenMpt => "Handled through libopenmpt for broad tracker-family playback and rendering.",
            _ => "Supported through the catalog and routed to the best available native, libopenmpt, or conversion path."
        };

    /// <summary>
    /// Executes the ShouldShowRuntimePlugin operation.
    /// </summary>
    private static bool ShouldShowRuntimePlugin(string name, string path)
    {
        string fileName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(path))
            fileName = Path.GetFileName(path);

        if (fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("DirectWrite", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Accessibility.dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.StartsWith("amChipper", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("QuickLog", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("NAudio", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("openmpt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("mpg123", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("vorbis", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("ogg", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("zlib", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("libs directory", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("native search path", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes the ApplyThemedListStyles operation.
    /// </summary>
    private static void ApplyThemedListStyles(ListView list)
    {
        list.Resources[SystemColors.HighlightBrushKey] = Application.Current.TryFindResource("BgSelect") as Brush ?? Brushes.DarkSlateBlue;
        list.Resources[SystemColors.HighlightTextBrushKey] = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Application.Current.TryFindResource("BgHover") as Brush ?? Brushes.DarkSlateGray;
        list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        var tooltipStyle = new Style(typeof(ToolTip));
        tooltipStyle.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.TryFindResource("BgControl") as Brush ?? Brushes.Black));
        tooltipStyle.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White));
        tooltipStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Application.Current.TryFindResource("Accent") as Brush ?? Brushes.DeepSkyBlue));
        tooltipStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        tooltipStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        list.Resources[typeof(ToolTip)] = tooltipStyle;

        var rowStyle = new Style(typeof(ListViewItem));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.TryFindResource("BgPanel") as Brush ?? Brushes.Black));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White));
        rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
        rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.TryFindResource("BgSelect") as Brush ?? Brushes.DarkSlateBlue));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Application.Current.TryFindResource("Accent") as Brush ?? Brushes.DeepSkyBlue));
        var hoverTrigger = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.TryFindResource("BgHover") as Brush ?? Brushes.DarkSlateGray));
        rowStyle.Triggers.Add(hoverTrigger);
        rowStyle.Triggers.Add(selectedTrigger);
        list.ItemContainerStyle = rowStyle;
    }

    /// <summary>
    /// Executes the ApplyThemedGridHeader operation.
    /// </summary>
    private static void ApplyThemedGridHeader(GridView grid)
    {
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.TryFindResource("BgControl") as Brush ?? Brushes.Black));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Application.Current.TryFindResource("Border") as Brush ?? Brushes.DimGray));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        grid.ColumnHeaderContainerStyle = headerStyle;
    }

    /// <summary>
    /// Executes the ResolveFileVersion operation.
    /// </summary>
    private static string ResolveFileVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return string.Empty;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : info.FileVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── Song actions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the NewSong operation.
    /// </summary>
    private void NewSong()
    {
        var dialog = new NewSongDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = this
        };
        if (dialog.ShowDialog() != true)
            return;

        Stop();
        Song = Song.CreateDefault(dialog.Options);
        FilePath = string.Empty;
        _useOriginalModulePlayback = false;
        _originalModuleData = null;
        _originalModulePath = null;
        Audio.UseModulePlayer = false;
        Audio.UseAudioFilePlayer = false;
        Audio.Sequencer.SetSong(_song);
        UpdateSourceFormatReadout();
        IsDirty = false;
        ClearHistory();
        StatusText = $"New {_song.Format} track created: {_song.Tracks.Count} channels, {_song.Patterns.Count} patterns.";
        AppLogger.Info(
            $"[Document] New song created title=\"{_song.Title}\" format={_song.Format} channels={_song.Tracks.Count} " +
            $"patterns={_song.Patterns.Count} rows={_song.DefaultRowsPerPattern} samples={_song.Instruments.Sum(i => i.Samples.Count)}");
    }

    /// <summary>
    /// Executes the OpenFile operation.
    /// </summary>
    private void OpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("OpenSongOrModule"),
            Filter = BuildOpenDialogFilter()
        };
        if (dlg.ShowDialog() != true) return;

        Stop();
        try
        {
            if (Path.GetExtension(dlg.FileName).Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                Song = NativeChipModuleFile.Load(dlg.FileName);
                FilePath = dlg.FileName;
                _originalModulePath = dlg.FileName;
                _originalModuleData = _song.OriginalModuleData is null ? null : (byte[])_song.OriginalModuleData.Clone();
                _useOriginalModulePlayback = false;
                Audio.UseModulePlayer = false;
                if (_originalModuleData is { Length: > 0 })
                {
                    string sourceName = $"{Path.GetFileNameWithoutExtension(dlg.FileName)}{_song.SourceModuleExtension}";
                    _useOriginalModulePlayback = Audio.ModulePlayer.Load(_originalModuleData, sourceName);
                    Audio.UseModulePlayer = _useOriginalModulePlayback;
                    AppLogger.Info($"[Document] AMC embedded source playback {(_useOriginalModulePlayback ? "loaded" : "failed")} ext={_song.SourceModuleExtension} bytes={_originalModuleData.Length}");
                }

                Audio.UseAudioFilePlayer = false;
                Audio.Sequencer.SetSong(_song);
                UpdateSourceFormatReadout();
                UpdateRuntimeTempoReadout();
                IsDirty = false;
                ClearHistory();
                StatusText = _useOriginalModulePlayback
                    ? $"Loaded native chip module with embedded {_song.Format} source: {Path.GetFileName(dlg.FileName)}"
                    : $"Loaded native chip module: {Path.GetFileName(dlg.FileName)}";
                AppLogger.Info(
                    $"[Document] Loaded AMC path=\"{dlg.FileName}\" title=\"{_song.Title}\" " +
                    $"format={_song.Format} sourceBytes={_originalModuleData?.Length ?? 0} " +
                    $"instruments={_song.Instruments.Count} tracks={_song.Tracks.Count} patterns={_song.Patterns.Count} blocks={_song.Tracks.Sum(t => t.Blocks.Count)}");
                return;
            }

            if (Path.GetExtension(dlg.FileName).Equals(SongProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase))
            {
                Song = SongProjectSerializer.Load(dlg.FileName);
                FilePath = dlg.FileName;
                _originalModulePath = dlg.FileName;
                _originalModuleData = _song.OriginalModuleData is null ? null : (byte[])_song.OriginalModuleData.Clone();
                _useOriginalModulePlayback = false;
                Audio.UseModulePlayer = false;
                Audio.UseAudioFilePlayer = false;
                Audio.Sequencer.SetSong(_song);
                UpdateSourceFormatReadout();
                UpdateRuntimeTempoReadout();
                IsDirty = false;
                ClearHistory();
                StatusText = $"Loaded project: {Path.GetFileName(dlg.FileName)}";
                AppLogger.Info(
                    $"[Document] Loaded amchip path=\"{dlg.FileName}\" title=\"{_song.Title}\" " +
                    $"instruments={_song.Instruments.Count} tracks={_song.Tracks.Count} patterns={_song.Patterns.Count} blocks={_song.Tracks.Sum(t => t.Blocks.Count)}");
                return;
            }

            string extension = Path.GetExtension(dlg.FileName);
            if (extension.Equals(".fsc", StringComparison.OrdinalIgnoreCase))
            {
                ImportNotesFromOpenDialog(dlg.FileName, FLScoreFile.ImportPatternChannel(dlg.FileName, _song.RowsPerBeat), "FSC");
                return;
            }

            if (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
            {
                ImportNotesFromOpenDialog(dlg.FileName, MidiFile.ImportPatternChannel(dlg.FileName, _song.RowsPerBeat), "MIDI");
                return;
            }

            byte[] data = File.ReadAllBytes(dlg.FileName);
            if (ChipTuneFile.IsSupported(dlg.FileName))
            {
                Song = ChipTuneFile.ImportAsSong(data, dlg.FileName);
                FilePath = dlg.FileName;
                _originalModulePath = dlg.FileName;
                _originalModuleData = (byte[])data.Clone();
                _useOriginalModulePlayback = false;
                Audio.UseModulePlayer = false;
                Audio.UseAudioFilePlayer = false;
                UpdateSourceFormatReadout();
                UpdateRuntimeTempoReadout();
                PlaybackState = PlaybackState.Stopped;
                IsDirty = false;
                ClearHistory();
                AppLogger.Info(
                    $"[Document] Imported chip tune path=\"{dlg.FileName}\" format={_song.Format} type={_song.SourceModuleType} ext={_song.SourceModuleExtension} bytes={data.Length}");
                StatusText = $"Loaded chip tune: {Path.GetFileName(dlg.FileName)} - internal renderer ready";
                _ = TryRenderChipPreviewForPlaybackAsync();
                return;
            }

            if (Audio.ModulePlayer.Load(data, dlg.FileName))
            {
                var imported = Audio.ModulePlayer.ImportAsSong();
                if (imported is not null)
                {
                    Song = imported;
                    _song.OriginalModuleData = (byte[])data.Clone();
                    FilePath = dlg.FileName;
                    _originalModulePath = dlg.FileName;
                    _useOriginalModulePlayback = true;
                    _originalModuleData = (byte[])data.Clone();
                    Audio.UseModulePlayer = true;
                    UpdateSourceFormatReadout();
                    UpdateRuntimeTempoReadout();
                    // Don't auto-play — user presses Play when ready.
                    PlaybackState = PlaybackState.Stopped;
                    IsDirty = false;
                    ClearHistory();
                    StatusText = $"Loaded: {Path.GetFileName(dlg.FileName)} — press Play";
                    AppLogger.Info(
                        $"[Document] Imported module path=\"{dlg.FileName}\" format={_song.Format} type={_song.SourceModuleType} ext={_song.SourceModuleExtension} " +
                        $"instruments={_song.Instruments.Count} tracks={_song.Tracks.Count} patterns={_song.Patterns.Count} orders={_song.OrderList.Count}");
                }
            }
            else
            {
                MessageBox.Show("Could not load module file.\n" +
                    "Ensure libopenmpt.dll is present in the application folder.",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to open module file");
            MessageBox.Show($"Error opening file:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the BuildOpenDialogFilter operation.
    /// </summary>
    private static string BuildOpenDialogFilter()
    {
        string modulePattern = ModuleFormatCatalog.SupportedExtensionsPattern;
        string allSupported = $"*.amc;*.amchip;{modulePattern};*.fsc;*.mid;*.midi";
        return $"All Supported amChipper Files|{allSupported}|" +
               "amChipper Native Chip Module (*.amc)|*.amc|" +
               "Compressed amChipper Project (*.amchip)|*.amchip|" +
               $"Tracker / Chiptune / Console Music ({modulePattern})|{modulePattern}|" +
               "FL Studio Score (*.fsc)|*.fsc|" +
               "MIDI File (*.mid;*.midi)|*.mid;*.midi|" +
               "All Files (*.*)|*.*";
    }

    /// <summary>
    /// Executes the ImportNotesFromOpenDialog operation.
    /// </summary>
    private void ImportNotesFromOpenDialog(string path, IReadOnlyList<Note> notes, string kind)
    {
        if (notes.Count == 0)
            throw new InvalidDataException($"{kind} file did not contain any importable notes.");

        PianoRoll.ReplaceCurrentLaneNotes(notes);
        PatternEditor.RefreshRows();
        ChannelRack.Refresh();
        MarkDirty(useNativePlayback: true);
        StatusText = $"Imported {kind}: {Path.GetFileName(path)}";
        AppLogger.Info(
            $"[Document] Open imported {kind} path=\"{path}\" notes={notes.Count} " +
            $"pattern={PianoRoll.CurrentPatternIndex} channel={PianoRoll.CurrentChannel}");
    }

    /// <summary>
    /// Executes the SaveFile operation.
    /// </summary>
    private void SaveFile()
    {
        if (string.IsNullOrWhiteSpace(FilePath) ||
            !Path.GetExtension(FilePath).Equals(SongProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase))
        {
            SaveAs();
            return;
        }

        try
        {
            SongProjectSerializer.Save(_song, FilePath);
            IsDirty = false;
            StatusText = $"Saved: {Path.GetFileName(FilePath)}";
            AppLogger.Info($"[Document] Saved path=\"{FilePath}\" title=\"{_song.Title}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to save project");
            MessageBox.Show($"Error saving project:\n{ex.Message}",
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the SaveAs operation.
    /// </summary>
    private void SaveAs()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("SaveSong"),
            Filter = "Compressed amChipper Project (*.amchip)|*.amchip|amChipper Native Chip Module (*.amc)|*.amc|FastTracker XM (*.xm)|*.xm",
            DefaultExt = SongProjectSerializer.Extension,
            AddExtension = true
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                string ext = Path.GetExtension(dlg.FileName);
                string path = string.Equals(ext, ".xm", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(dlg.FileName, ".xm")
                    : string.Equals(ext, NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(dlg.FileName, NativeChipModuleFile.Extension)
                    : Path.ChangeExtension(dlg.FileName, SongProjectSerializer.Extension);

                if (string.Equals(Path.GetExtension(path), ".xm", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsDirty && _originalModuleData is not null && _song.Format == ModuleFormat.XM)
                    {
                        File.WriteAllBytes(path, _originalModuleData);
                        AppLogger.Info($"[Document] Exported original XM bytes path=\"{path}\" bytes={_originalModuleData.Length}");
                    }
                    else if (_originalModuleData is not null &&
                             XmModulePatternPatcher.TrySavePatchedModule(_song, _originalModuleData, path))
                    {
                        AppLogger.Info($"[Document] Exported patched original XM path=\"{path}\" bytes={new FileInfo(path).Length}");
                    }
                    else
                    {
                        XmModuleExporter.Save(_song, path, CreateXmExportOptions());
                        AppLogger.Info($"[Document] Exported XM path=\"{path}\" title=\"{_song.Title}\"");
                    }

                    ValidateExportedXm(path);
                    StatusText = $"Exported XM: {Path.GetFileName(path)}";
                }
                else if (string.Equals(Path.GetExtension(path), NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    NativeChipModuleFile.Save(_song, path);
                    FilePath = path;
                    IsDirty = false;
                    StatusText = $"Saved native chip module: {Path.GetFileName(path)}";
                    AppLogger.Info($"[Document] SaveAs AMC path=\"{path}\" title=\"{_song.Title}\"");
                }
                else
                {
                    SongProjectSerializer.Save(_song, path);
                    FilePath = path;
                    IsDirty = false;
                    StatusText = $"Saved: {Path.GetFileName(path)}";
                    AppLogger.Info($"[Document] SaveAs path=\"{path}\" title=\"{_song.Title}\"");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to save project");
                MessageBox.Show($"Error saving project:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Executes the ExportTo operation.
    /// </summary>
    private void ExportTo()
    {
        var options = new[]
        {
            new ExportOption(L("ExportOptionAmcTitle"), L("ExportOptionAmcDescription"), ExportNativeChipModule),
            new ExportOption(L("ExportOptionProjectTitle"), L("ExportOptionProjectDescription"), ExportProject),
            new ExportOption(L("ExportOptionNativeTitle"), L("ExportOptionNativeDescription"), ExportNativeModule),
            new ExportOption(L("ExportOptionXmTitle"), L("ExportOptionXmDescription"), ExportXm),
            new ExportOption(L("ExportOptionAudioTitle"), L("ExportOptionAudioDescription"), ExportAudioConversion),
            new ExportOption(L("ExportOptionWavTitle"), L("ExportOptionWavDescription"), ExportRenderedWav),
            new ExportOption(L("ExportOptionMidiTitle"), L("ExportOptionMidiDescription"), ExportPianoRollMidi),
            new ExportOption(L("ExportOptionFscTitle"), L("ExportOptionFscDescription"), ExportPianoRollFsc)
        };

        var list = new ListBox
        {
            Margin = new Thickness(12),
            ItemsSource = options,
            DisplayMemberPath = nameof(ExportOption.Title),
            SelectedIndex = 0
        };

        var description = new TextBlock
        {
            Margin = new Thickness(12, 0, 12, 8),
            Foreground = Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap
        };
        list.SelectionChanged += (_, _) => description.Text = (list.SelectedItem as ExportOption)?.Description ?? string.Empty;
        description.Text = options[0].Description;

        var window = new Window
        {
            Title = L("ExportTo"),
            Owner = Application.Current?.MainWindow,
            Width = 520,
            Height = 430,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Application.Current?.TryFindResource("BgPanel") as Brush ?? Brushes.Black,
            Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            Content = new DockPanel()
        };

        var root = (DockPanel)window.Content;
        var title = new TextBlock
        {
            Text = L("ChooseExportTarget"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        DockPanel.SetDock(description, Dock.Bottom);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12)
        };
        var exportButton = new Button { Content = L("Export"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = L("Cancel"), IsCancel = true, MinWidth = 90 };
        exportButton.Click += (_, _) => window.DialogResult = list.SelectedItem is ExportOption;
        buttons.Children.Add(exportButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(description);
        root.Children.Add(list);

        if (window.ShowDialog() == true && list.SelectedItem is ExportOption option)
        {
            AppLogger.Info($"[Document] Export chooser selected \"{option.Title}\"");
            option.Action();
        }
    }

    /// <summary>
    /// Executes the ExportProject operation.
    /// </summary>
    private void ExportProject()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("ExportProjectTitle"),
            Filter = "Compressed amChipper Project (*.amchip)|*.amchip",
            DefaultExt = SongProjectSerializer.Extension,
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}{SongProjectSerializer.Extension}"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            SongProjectSerializer.Save(_song, dlg.FileName);
            StatusText = $"Exported project: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Document] Exported amchip project path=\"{dlg.FileName}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export amChipper project");
            MessageBox.Show($"Error exporting project:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ExportNativeChipModule operation.
    /// </summary>
    private void ExportNativeChipModule()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("ExportNativeChipTitle"),
            Filter = "amChipper Native Chip Module (*.amc)|*.amc",
            DefaultExt = NativeChipModuleFile.Extension,
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}{NativeChipModuleFile.Extension}"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            NativeChipModuleFile.Save(_song, dlg.FileName);
            StatusText = $"Exported native chip module: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Document] Exported AMC path=\"{dlg.FileName}\" patterns={_song.Patterns.Count} tracks={_song.Tracks.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export native chip module");
            MessageBox.Show($"Error exporting native chip module:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ExportXm operation.
    /// </summary>
    private void ExportXm()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("ExportXmTitle"),
            Filter = "FastTracker XM (*.xm)|*.xm",
            DefaultExt = ".xm",
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}.xm"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            if (!IsDirty && _originalModuleData is not null && _song.Format == ModuleFormat.XM)
            {
                File.WriteAllBytes(dlg.FileName, _originalModuleData);
                AppLogger.Info($"[Document] Exported original XM bytes path=\"{dlg.FileName}\" bytes={_originalModuleData.Length}");
            }
            else if (_originalModuleData is not null &&
                     XmModulePatternPatcher.TrySavePatchedModule(_song, _originalModuleData, dlg.FileName))
            {
                AppLogger.Info($"[Document] Exported patched original XM path=\"{dlg.FileName}\" bytes={new FileInfo(dlg.FileName).Length}");
            }
            else
            {
                XmModuleExporter.Save(_song, dlg.FileName, CreateXmExportOptions());
                AppLogger.Info($"[Document] Exported XM path=\"{dlg.FileName}\" patterns={_song.Patterns.Count} tracks={_song.Tracks.Count}");
            }

            ValidateExportedXm(dlg.FileName);
            StatusText = $"Exported XM: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export XM");
            MessageBox.Show($"Error exporting XM:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ValidateExportedXm operation.
    /// </summary>
    private void ValidateExportedXm(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var player = new ModulePlayer(Audio.SampleRate, AppLogger.Instance);
            if (!player.Load(bytes, Path.GetFileName(path)))
            {
                AppLogger.Warning($"[Document] Exported XM validation failed load path=\"{path}\" bytes={bytes.Length}");
                return;
            }

            float[] buffer = new float[1024 * 2];
            float peak = 0f;
            double sumSquares = 0;
            long samples = 0;
            int framesLeft = Math.Max(1, Audio.SampleRate * 6);
            while (framesLeft > 0)
            {
                int frames = Math.Min(1024, framesLeft);
                int rendered = player.Render(buffer, frames);
                if (rendered <= 0)
                    break;

                int limit = Math.Min(buffer.Length, rendered * 2);
                for (int i = 0; i < limit; i++)
                {
                    float value = Math.Abs(buffer[i]);
                    peak = Math.Max(peak, value);
                    sumSquares += value * value;
                }

                samples += limit;
                framesLeft -= rendered;
            }

            double rms = samples > 0 ? Math.Sqrt(sumSquares / samples) : 0;
            AppLogger.Info(
                $"[Document] Exported XM validation path=\"{path}\" bytes={bytes.Length} loaded=true " +
                $"duration={player.DurationSecs:0.###}s orders={player.OrderCount} patterns={player.PatternCount} channels={player.ChannelCount} " +
                $"peak={peak:0.000000} rms={rms:0.000000}");

            if (peak <= 0.0001f)
            {
                StatusText = $"Exported XM appears silent: {Path.GetFileName(path)}";
                AppLogger.Warning($"[Document] Exported XM appears silent path=\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[Document] Exported XM validation crashed path=\"{path}\"");
        }
    }

    /// <summary>
    /// Executes the CreateXmExportOptions operation.
    /// </summary>
    private XmExportOptions CreateXmExportOptions() =>
        new(SidXmExportMode switch
        {
            "Rendered Mix + Trace" => amChipper.Core.Persistence.SidXmExportMode.RenderedMixWithTrace,
            "Trace Only" => amChipper.Core.Persistence.SidXmExportMode.TraceOnly,
            _ => amChipper.Core.Persistence.SidXmExportMode.RenderedMixOnly
        });

    /// <summary>
    /// Executes the ExportNativeModule operation.
    /// </summary>
    private void ExportNativeModule()
    {
        if (_originalModuleData is null)
        {
            MessageBox.Show("No original module bytes are available for native export.",
                "Native Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string extension = GetNativeModuleExtension(_song);
        if (IsDirty && _song.Format is not (ModuleFormat.XM or ModuleFormat.MOD))
        {
            string format = ModuleFormatCatalog.GetDisplayLabel(_song);
            AppLogger.Warning($"[Document] Native export blocked for dirty {format}; exact patcher is currently available for XM and MOD only.");
            if (ConfirmNativeExportLimitations)
            {
                MessageBox.Show(
                    $"{format} native export is only exact while the module is unchanged.\n\n" +
                    "Playback/import is handled by libopenmpt, but dirty native patch export currently needs a format-specific pattern writer. " +
                    "Save the project as .amchip to preserve your edits, or export XM as an explicit conversion.",
                    "Native Export Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusText = $"{format} dirty native export needs .amchip or XM conversion.";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, L("ExportNativeModuleTitle"), ModuleFormatCatalog.GetDisplayLabel(_song)),
            Filter = $"{ModuleFormatCatalog.GetDisplayLabel(_song)} Module (*{extension})|*{extension}|All Files (*.*)|*.*",
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}{extension}"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            if (_song.Format == ModuleFormat.XM && IsDirty)
            {
                if (!XmModulePatternPatcher.TrySavePatchedModule(_song, _originalModuleData, dlg.FileName))
                    XmModuleExporter.Save(_song, dlg.FileName, CreateXmExportOptions());
                AppLogger.Info($"[Document] Exported native patched XM path=\"{dlg.FileName}\"");
            }
            else if (_song.Format == ModuleFormat.MOD && IsDirty)
            {
                if (!ModModulePatternPatcher.TrySavePatchedModule(_song, _originalModuleData, dlg.FileName))
                    throw new InvalidOperationException("Could not patch MOD pattern data.");
                AppLogger.Info($"[Document] Exported native patched MOD path=\"{dlg.FileName}\"");
            }
            else
            {
                File.WriteAllBytes(dlg.FileName, _originalModuleData);
                AppLogger.Info($"[Document] Exported native original module path=\"{dlg.FileName}\" format={_song.Format} bytes={_originalModuleData.Length}");
            }

            StatusText = $"Exported native {ModuleFormatCatalog.GetDisplayLabel(_song)}: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export native module");
            MessageBox.Show($"Error exporting native module:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ExportAudioConversion operation.
    /// </summary>
    private void ExportAudioConversion()
    {
        var formatBox = new ComboBox
        {
            Width = 160,
            ItemsSource = new[] { "WAV", "MP3" },
            SelectedIndex = 0,
            Margin = new Thickness(0, 4, 0, 10)
        };
        var sampleRateBox = new ComboBox
        {
            Width = 160,
            ItemsSource = new[] { 22050, 32000, 44100, 48000, 96000 },
            SelectedItem = Audio.SampleRate,
            Margin = new Thickness(0, 4, 0, 10)
        };
        var durationBox = new TextBox
        {
            Width = 160,
            Text = Math.Ceiling(Math.Clamp(EstimateSongDurationSeconds() + ChipRenderTailSeconds, 1, 3600)).ToString("0"),
            Margin = new Thickness(0, 4, 0, 10)
        };
        var bitrateBox = new ComboBox
        {
            Width = 160,
            ItemsSource = new[] { 128, 160, 192, 256, 320 },
            SelectedItem = 192,
            Margin = new Thickness(0, 4, 0, 10)
        };
        var normalizeBox = new CheckBox
        {
            Content = L("NormalizePeak"),
            IsChecked = true,
            Margin = new Thickness(0, 4, 0, 10)
        };

        formatBox.SelectionChanged += (_, _) => bitrateBox.IsEnabled = string.Equals(formatBox.SelectedItem?.ToString(), "MP3", StringComparison.OrdinalIgnoreCase);
        bitrateBox.IsEnabled = false;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddConversionRow(grid, 0, L("TargetFormat"), formatBox);
        AddConversionRow(grid, 1, L("SampleRate"), sampleRateBox);
        AddConversionRow(grid, 2, L("RenderSeconds"), durationBox);
        AddConversionRow(grid, 3, L("Mp3Bitrate"), bitrateBox);
        AddConversionRow(grid, 4, L("Level"), normalizeBox);

        var description = new TextBlock
        {
            Margin = new Thickness(16, 0, 16, 8),
            Foreground = Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            Text = L("AudioConversionDescription")
        };

        var window = new Window
        {
            Title = L("ConvertRenderAudio"),
            Owner = Application.Current?.MainWindow,
            Width = 480,
            Height = 360,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Application.Current?.TryFindResource("BgPanel") as Brush ?? Brushes.Black,
            Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            Content = new DockPanel()
        };

        var root = (DockPanel)window.Content;
        var title = new TextBlock
        {
            Text = L("AudioConversion"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 14, 16, 4)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        DockPanel.SetDock(description, Dock.Bottom);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16)
        };
        var exportButton = new Button { Content = L("Convert"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = L("Cancel"), IsCancel = true, MinWidth = 90 };
        exportButton.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(exportButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(description);
        root.Children.Add(grid);

        if (window.ShowDialog() != true)
            return;

        string target = formatBox.SelectedItem?.ToString() ?? "WAV";
        int sampleRate = sampleRateBox.SelectedItem is int sr ? sr : Audio.SampleRate;
        int seconds = int.TryParse(durationBox.Text, out int parsedSeconds)
            ? Math.Clamp(parsedSeconds, 1, 3600)
            : (int)Math.Ceiling(Math.Clamp(EstimateSongDurationSeconds() + ChipRenderTailSeconds, 1, 3600));
        int bitrate = bitrateBox.SelectedItem is int br ? br : 192;
        bool normalize = normalizeBox.IsChecked == true;
        string ext = string.Equals(target, "MP3", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".wav";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, L("ConvertToTarget"), target),
            Filter = string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase)
                ? "MP3 Audio (*.mp3)|*.mp3|Wave Audio (*.wav)|*.wav"
                : "Wave Audio (*.wav)|*.wav|MP3 Audio (*.mp3)|*.mp3",
            DefaultExt = ext,
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}{ext}"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var options = new AudioConversionOptions(sampleRate, seconds, bitrate * 1000, normalize);
            ConvertCurrentSongToAudio(dlg.FileName, options);
            StatusText = $"Converted audio: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Document] Converted audio path=\"{dlg.FileName}\" sampleRate={sampleRate} seconds={seconds} bitrate={bitrate}kbps normalize={normalize}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Audio conversion failed.");
            MessageBox.Show($"Audio conversion failed:\n{ex.Message}", "Convert / Render Audio", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Audio conversion failed.";
        }
    }

    /// <summary>
    /// Executes the AddConversionRow operation.
    /// </summary>
    private static void AddConversionRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.LightGray,
            Margin = new Thickness(0, 4, 12, 10)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
    }

    /// <summary>
    /// Executes the ConvertCurrentSongToAudio operation.
    /// </summary>
    private void ConvertCurrentSongToAudio(string outputPath, AudioConversionOptions options)
    {
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        float[] samples = RenderCurrentSongToStereoFloat(options.SampleRate, options.Seconds);
        if (options.NormalizePeak)
            NormalizeStereo(samples);

        if (extension == ".mp3")
        {
            string tempWav = Path.Combine(Path.GetTempPath(), "amChipper-convert", $"{Guid.NewGuid():N}.wav");
            WriteStereoFloatAsPcm16Wav(tempWav, samples, options.SampleRate);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            using var reader = new AudioFileReader(tempWav);
            MediaFoundationEncoder.EncodeToMp3(reader, outputPath, options.Mp3Bitrate);
            TryDeleteFile(tempWav);
            return;
        }

        WriteStereoFloatAsPcm16Wav(outputPath, samples, options.SampleRate);
    }

    /// <summary>
    /// Executes the RenderCurrentSongToStereoFloat operation.
    /// </summary>
    private float[] RenderCurrentSongToStereoFloat(int sampleRate, int seconds)
    {
        if (ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format))
        {
            string? chipSourcePath = ResolveChipSourcePath();
            if (string.IsNullOrWhiteSpace(chipSourcePath))
                throw new InvalidOperationException("No original SID/NSF source is available for audio conversion.");

            byte[] bytes = _originalModuleData ?? _song.OriginalModuleData ?? File.ReadAllBytes(chipSourcePath);
            return InternalChipRenderer.RenderStereoFloat(bytes, chipSourcePath, seconds, sampleRate);
        }

        if (_song.Format is ModuleFormat.AmChip or ModuleFormat.Unknown || _originalModuleData is null || IsDirty)
            return RenderSongWithSequencer(sampleRate, seconds);

        return RenderModuleBytesToStereoFloat(_originalModuleData, _originalModulePath ?? _song.Title, sampleRate, seconds);
    }

    /// <summary>
    /// Executes the RenderModuleBytesToStereoFloat operation.
    /// </summary>
    private float[] RenderModuleBytesToStereoFloat(byte[] bytes, string sourceName, int sampleRate, int seconds)
    {
        using var player = new ModulePlayer(sampleRate, AppLogger.Instance);
        if (!player.Load(bytes, sourceName))
            throw new InvalidOperationException("The module backend could not load the current source for audio conversion.");

        double duration = player.DurationSecs > 0 ? Math.Min(player.DurationSecs + 2.0, seconds) : seconds;
        int frames = Math.Max(1, (int)Math.Ceiling(duration * sampleRate));
        float[] result = new float[frames * 2];
        float[] buffer = new float[4096 * 2];
        int written = 0;
        while (written < frames)
        {
            int request = Math.Min(4096, frames - written);
            Array.Clear(buffer, 0, request * 2);
            int rendered = player.Render(buffer, request);
            if (rendered <= 0)
                break;
            Array.Copy(buffer, 0, result, written * 2, rendered * 2);
            written += rendered;
        }

        if (written < frames)
            Array.Resize(ref result, Math.Max(written * 2, 2));
        return result;
    }

    /// <summary>
    /// Executes the RenderSongWithSequencer operation.
    /// </summary>
    private float[] RenderSongWithSequencer(int sampleRate, int seconds)
    {
        var sequencer = new InternalSequencer(sampleRate, AppLogger.Instance);
        sequencer.SetSong(_song);
        sequencer.Play(PlaybackScope.Song, 0, 0, null, 0);

        int frames = Math.Max(1, seconds * sampleRate);
        float[] result = new float[frames * 2];
        float[] buffer = new float[4096 * 2];
        int written = 0;
        while (written < frames)
        {
            int request = Math.Min(4096, frames - written);
            Array.Clear(buffer, 0, request * 2);
            sequencer.Render(buffer, request);
            Array.Copy(buffer, 0, result, written * 2, request * 2);
            written += request;
        }

        return result;
    }

    /// <summary>
    /// Executes the NormalizeStereo operation.
    /// </summary>
    private static void NormalizeStereo(float[] samples)
    {
        float peak = 0;
        foreach (float sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));

        if (peak < 0.0001f || peak >= 0.98f)
            return;

        float gain = 0.98f / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
    }

    /// <summary>
    /// Executes the WriteStereoFloatAsPcm16Wav operation.
    /// </summary>
    private static void WriteStereoFloatAsPcm16Wav(string path, float[] samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 2));
        byte[] buffer = new byte[samples.Length * sizeof(short)];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm = (short)Math.Clamp(Math.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            buffer[i * 2] = (byte)(pcm & 0xFF);
            buffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    /// <summary>
    /// Executes the TryDeleteFile operation.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    /// <summary>
    /// Executes the ExportRenderedWav operation.
    /// </summary>
    private void ExportRenderedWav()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, L("RenderFormatToWav"), ModuleFormatCatalog.GetDisplayLabel(_song)),
            Filter = "Wave Audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}.wav"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            int seconds = (int)Math.Ceiling(Math.Clamp(EstimateSongDurationSeconds() + ChipRenderTailSeconds, 1, 3600));
            ConvertCurrentSongToAudio(dlg.FileName, new AudioConversionOptions(Audio.SampleRate, seconds, 192000, true));
            StatusText = $"Rendered WAV: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[Document] Rendered WAV path=\"{dlg.FileName}\" format={_song.Format}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[Document] WAV render failed format={_song.Format}");
            MessageBox.Show(ex.Message, "Rendered WAV Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText = "WAV render failed.";
        }
    }

    /// <summary>
    /// Executes the TryRenderChipPreviewForPlayback operation.
    /// </summary>
    private bool TryRenderChipPreviewForPlayback()
    {
        string? chipSourcePath = ResolveChipSourcePath();
        if (!ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format) ||
            string.IsNullOrWhiteSpace(chipSourcePath))
        {
            return false;
        }

        string outputPath = Path.Combine(Path.GetTempPath(), "amChipper-chip-render", $"{SanitizeFileName(_song.Title)}-{Guid.NewGuid():N}.wav");
        try
        {
            byte[] bytes = _originalModuleData ?? _song.OriginalModuleData ?? File.ReadAllBytes(chipSourcePath);
            int requestedSeconds = (int)Math.Ceiling(Math.Clamp(EstimateSongDurationSeconds() + ChipRenderTailSeconds, 1, 3600));
            int seconds = Math.Min(requestedSeconds, _song.Format == ModuleFormat.NSF ? 20 : 45);
            int previewSampleRate = _song.Format == ModuleFormat.NSF ? Math.Min(Audio.SampleRate, 32000) : Audio.SampleRate;
            StatusText = seconds < requestedSeconds
                ? $"Rendering {_song.Format} preview ({seconds}s quick cache)..."
                : $"Rendering {_song.Format} preview...";
            InternalChipRenderer.RenderToWav(bytes, chipSourcePath, outputPath, seconds, previewSampleRate);
            AppLogger.Info($"[ChipRender] Preview rendered format={_song.Format} seconds={seconds} requested={requestedSeconds} estimated={EstimateSongDurationSeconds():0.###} tail={ChipRenderTailSeconds} path=\"{outputPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[ChipRender] Internal preview render failed format={_song.Format}");
            return false;
        }

        bool loaded = Audio.AudioFilePlayer.Load(outputPath);
        Audio.UseAudioFilePlayer = loaded;
        Audio.UseModulePlayer = false;
        if (loaded)
            UpdateTransportReadout();
        return loaded;
    }

    /// <summary>
    /// Renders SID/NSF preview audio without blocking the WPF UI thread.
    /// </summary>
    private async Task<bool> TryRenderChipPreviewForPlaybackAsync()
    {
        string? chipSourcePath = ResolveChipSourcePath();
        if (!ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format) ||
            string.IsNullOrWhiteSpace(chipSourcePath))
        {
            return false;
        }

        if (_chipPreviewRenderInFlight)
        {
            StatusText = $"Rendering {_song.Format} preview...";
            return false;
        }

        int ticket = Interlocked.Increment(ref _chipPreviewRenderTicket);
        _chipPreviewRenderInFlight = true;
        StatusText = $"Rendering {_song.Format} preview...";
        string outputPath = Path.Combine(Path.GetTempPath(), "amChipper-chip-render", $"{SanitizeFileName(_song.Title)}-{Guid.NewGuid():N}.wav");
        byte[] bytes = _originalModuleData ?? _song.OriginalModuleData ?? await File.ReadAllBytesAsync(chipSourcePath).ConfigureAwait(true);
        int requestedSeconds = (int)Math.Ceiling(Math.Clamp(EstimateSongDurationSeconds() + ChipRenderTailSeconds, 1, 3600));
        var format = _song.Format;
        int seconds = Math.Min(requestedSeconds, format == ModuleFormat.NSF ? 20 : 45);
        int sampleRate = format == ModuleFormat.NSF ? Math.Min(Audio.SampleRate, 32000) : Audio.SampleRate;

        try
        {
            await Task.Run(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                InternalChipRenderer.RenderToWav(bytes, chipSourcePath, outputPath, seconds, sampleRate);
            }).ConfigureAwait(true);
            if (ticket != _chipPreviewRenderTicket)
                return false;

            bool loaded = Audio.AudioFilePlayer.Load(outputPath);
            Audio.UseAudioFilePlayer = loaded;
            Audio.UseModulePlayer = false;
            if (loaded)
            {
                UpdateTransportReadout();
                AppLogger.Info($"[ChipRender] Async preview rendered format={format} seconds={seconds} requested={requestedSeconds} path=\"{outputPath}\"");
            }

            return loaded;
        }
        catch (Exception ex)
        {
            StatusText = $"{format} internal render failed.";
            AppLogger.Error(ex, $"[ChipRender] Async internal preview render failed format={format}");
            return false;
        }
        finally
        {
            _chipPreviewRenderInFlight = false;
        }
    }

    /// <summary>
    /// Executes the ResolveChipSourcePath operation.
    /// </summary>
    private string? ResolveChipSourcePath()
    {
        if (!ModuleFormatCatalog.IsEmulatedChipFormat(_song.Format))
            return null;

        if (!string.IsNullOrWhiteSpace(_originalModulePath) &&
            File.Exists(_originalModulePath) &&
            ChipTuneFile.IsSupported(_originalModulePath))
        {
            return _originalModulePath;
        }

        byte[]? bytes = _originalModuleData ?? _song.OriginalModuleData;
        if (bytes is null || bytes.Length == 0)
            return null;

        string extension = GetNativeModuleExtension(_song);
        string tempPath = Path.Combine(Path.GetTempPath(), "amChipper-chip-source", $"{SanitizeFileName(_song.Title)}-{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        File.WriteAllBytes(tempPath, bytes);
        return tempPath;
    }

    /// <summary>
    /// Executes the ExportPianoRollMidi operation.
    /// </summary>
    private void ExportPianoRollMidi()
    {
        var selection = SelectMidiExportScope();
        if (selection is null || selection.Patterns.Count == 0 || selection.Channels.Count == 0)
            return;

        int maxChannelCount = selection.Patterns
            .Select(patternIndex => _song.Patterns[patternIndex].ChannelCount)
            .DefaultIfEmpty(1)
            .Max();
        string patternSuffix = selection.Patterns.Count == 1
            ? $"pat{selection.Patterns[0]:D2}"
            : $"patterns-{selection.Patterns.Count}";
        string channelSuffix = selection.Channels.Count == maxChannelCount
            ? "all-channels"
            : string.Join("+", selection.Channels.Select(channel => $"ch{channel + 1}"));

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("ExportPianoRollMidi"),
            Filter = "MIDI File (*.mid)|*.mid",
            DefaultExt = ".mid",
            AddExtension = true,
            FileName = $"{SanitizeFileName(_song.Title)}-{patternSuffix}-{channelSuffix}.mid"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            MidiFile.ExportPatternsChannels(_song, selection.Patterns, selection.Channels, dlg.FileName);
            StatusText = $"Exported MIDI: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[PianoRoll] Exported MIDI path=\"{dlg.FileName}\" patterns={string.Join(",", selection.Patterns)} channels={string.Join(",", selection.Channels)}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export piano-roll MIDI");
            MessageBox.Show($"Error exporting MIDI:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the SelectMidiExportScope operation.
    /// </summary>
    private MidiExportSelection? SelectMidiExportScope()
    {
        if (_song.Patterns.Count == 0)
            return null;

        int currentPattern = Math.Clamp(PianoRoll.CurrentPatternIndex, 0, _song.Patterns.Count - 1);
        int channelCount = Math.Max(_song.Patterns.Max(pattern => pattern.ChannelCount), 1);
        var patternChecks = new List<CheckBox>(_song.Patterns.Count);
        var checks = new List<CheckBox>(channelCount);

        var patternList = new StackPanel { Margin = new Thickness(12, 8, 8, 8) };
        for (int patternIndex = 0; patternIndex < _song.Patterns.Count; patternIndex++)
        {
            var pattern = _song.Patterns[patternIndex];
            bool selectedByDefault = MidiExportPatternDefault switch
            {
                "Song Order" => _song.OrderList.Contains(patternIndex),
                "All Patterns" => true,
                _ => patternIndex == currentPattern
            };
            var check = new CheckBox
            {
                Content = $"Pattern {patternIndex:D2}: {pattern.Name} ({pattern.RowCount} rows)",
                Tag = patternIndex,
                IsChecked = selectedByDefault,
                Margin = new Thickness(0, 2, 0, 2)
            };
            patternChecks.Add(check);
            patternList.Children.Add(check);
        }

        var channelList = new StackPanel { Margin = new Thickness(8, 8, 12, 8) };
        for (int channel = 0; channel < channelCount; channel++)
        {
            var check = new CheckBox
            {
                Content = $"Channel {channel + 1}",
                Tag = channel,
                IsChecked = channel == Math.Clamp(PianoRoll.CurrentChannel, 0, channelCount - 1),
                Margin = new Thickness(0, 2, 0, 2)
            };
            checks.Add(check);
            channelList.Children.Add(check);
        }

        var window = new Window
        {
            Title = L("ExportMidiScope"),
            Owner = Application.Current?.MainWindow,
            Width = 620,
            Height = Math.Min(620, 190 + Math.Max(_song.Patterns.Count, channelCount) * 28),
            MinWidth = 520,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Application.Current?.TryFindResource("BgPanel") as Brush ?? Brushes.Black,
            Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            Content = new DockPanel()
        };

        var root = (DockPanel)window.Content;
        var prompt = new TextBlock
        {
            Text = "Choose the pattern(s) and channel(s) to write into the MIDI file. Multiple patterns are written in sequence.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 12, 12, 2)
        };
        DockPanel.SetDock(prompt, Dock.Top);
        root.Children.Add(prompt);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12)
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        void SetAllChannels(bool selected)
        {
            foreach (var check in checks)
                check.IsChecked = selected;
        }

        void SetAllPatterns(bool selected)
        {
            foreach (var check in patternChecks)
                check.IsChecked = selected;
        }

        var currentPatternButton = new Button { Content = L("CurrentPattern"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 0) };
        currentPatternButton.Click += (_, _) =>
        {
            SetAllPatterns(false);
            patternChecks[currentPattern].IsChecked = true;
        };
        buttonRow.Children.Add(currentPatternButton);

        var orderButton = new Button { Content = L("SongOrder"), MinWidth = 88, Margin = new Thickness(0, 0, 6, 0) };
        orderButton.Click += (_, _) =>
        {
            SetAllPatterns(false);
            foreach (int patternIndex in _song.OrderList.Distinct())
                if ((uint)patternIndex < (uint)patternChecks.Count)
                    patternChecks[patternIndex].IsChecked = true;
        };
        buttonRow.Children.Add(orderButton);

        var allPatternsButton = new Button { Content = L("AllPatterns"), MinWidth = 88, Margin = new Thickness(0, 0, 12, 0) };
        allPatternsButton.Click += (_, _) => SetAllPatterns(true);
        buttonRow.Children.Add(allPatternsButton);

        var currentButton = new Button { Content = L("CurrentChannelShort"), MinWidth = 84, Margin = new Thickness(0, 0, 6, 0) };
        currentButton.Click += (_, _) =>
        {
            SetAllChannels(false);
            checks[Math.Clamp(PianoRoll.CurrentChannel, 0, checks.Count - 1)].IsChecked = true;
        };
        buttonRow.Children.Add(currentButton);

        var allButton = new Button { Content = L("All"), MinWidth = 56, Margin = new Thickness(0, 0, 12, 0) };
        allButton.Click += (_, _) => SetAllChannels(true);
        buttonRow.Children.Add(allButton);

        var okButton = new Button { Content = L("Export"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        okButton.Click += (_, _) =>
        {
            if (patternChecks.Any(check => check.IsChecked == true) && checks.Any(check => check.IsChecked == true))
                window.DialogResult = true;
        };
        buttonRow.Children.Add(okButton);

        var cancelButton = new Button { Content = L("Cancel"), IsCancel = true, MinWidth = 72 };
        buttonRow.Children.Add(cancelButton);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        var patternScroller = new ScrollViewer
        {
            Content = patternList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetColumn(patternScroller, 0);
        columns.Children.Add(patternScroller);

        var channelScroller = new ScrollViewer
        {
            Content = channelList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetColumn(channelScroller, 1);
        columns.Children.Add(channelScroller);
        root.Children.Add(columns);

        if (window.ShowDialog() != true)
            return null;

        var patterns = patternChecks
            .Where(check => check.IsChecked == true)
            .Select(check => (int)check.Tag)
            .ToArray();
        var channels = checks
            .Where(check => check.IsChecked == true)
            .Select(check => (int)check.Tag)
            .ToArray();

        return new MidiExportSelection(patterns, channels);
    }

    /// <summary>
    /// Executes the ImportPianoRollMidi operation.
    /// </summary>
    private void ImportPianoRollMidi()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("ImportMidiIntoPianoRoll"),
            Filter = "MIDI File (*.mid;*.midi)|*.mid;*.midi|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var notes = MidiFile.ImportPatternChannel(dlg.FileName, _song.RowsPerBeat);
            PianoRoll.ReplaceCurrentLaneNotes(notes);
            PatternEditor.RefreshRows();
            ChannelRack.Refresh();
            MarkDirty(useNativePlayback: true);
            StatusText = $"Imported MIDI: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[PianoRoll] Imported MIDI path=\"{dlg.FileName}\" notes={notes.Count} pattern={PianoRoll.CurrentPatternIndex} channel={PianoRoll.CurrentChannel}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to import piano-roll MIDI");
            MessageBox.Show($"Error importing MIDI:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ExportPianoRollFsc operation.
    /// </summary>
    private void ExportPianoRollFsc()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("ExportFlStudioScore"),
            Filter = "FL Studio Score (*.fsc)|*.fsc",
            DefaultExt = ".fsc",
            AddExtension = true,
            FileName = $"{SanitizeFileName(PianoRoll.CurrentPatternName)}-ch{PianoRoll.CurrentChannel + 1}.fsc"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            FLScoreFile.ExportPatternChannel(_song, PianoRoll.CurrentPatternIndex, PianoRoll.CurrentChannel, dlg.FileName);
            StatusText = $"Exported FSC: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[PianoRoll] Exported FSC path=\"{dlg.FileName}\" pattern={PianoRoll.CurrentPatternIndex} channel={PianoRoll.CurrentChannel}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to export piano-roll FSC");
            MessageBox.Show($"Error exporting FSC:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the ImportPianoRollFsc operation.
    /// </summary>
    private void ImportPianoRollFsc()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("ImportFlStudioScoreIntoPianoRoll"),
            Filter = "FL Studio Score (*.fsc)|*.fsc|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var notes = FLScoreFile.ImportPatternChannel(dlg.FileName, _song.RowsPerBeat);
            PianoRoll.ReplaceCurrentLaneNotes(notes);
            PatternEditor.RefreshRows();
            ChannelRack.Refresh();
            MarkDirty(useNativePlayback: true);
            StatusText = $"Imported FSC: {Path.GetFileName(dlg.FileName)}";
            AppLogger.Info($"[PianoRoll] Imported FSC path=\"{dlg.FileName}\" notes={notes.Count} pattern={PianoRoll.CurrentPatternIndex} channel={PianoRoll.CurrentChannel}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to import piano-roll FSC");
            MessageBox.Show($"Error importing FSC:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Executes the AddTrack operation.
    /// </summary>
    private void AddTrack()
    {
        BeginHistory("Add track");
        _song.Tracks.Add(new Track
        {
            Name = $"Track {_song.Tracks.Count + 1}",
            InstrumentIndex = Math.Min(_song.Tracks.Count, Math.Max(_song.Instruments.Count - 1, 0))
        });

        foreach (var pattern in _song.Patterns)
        {
            if (pattern.ChannelCount < _song.Tracks.Count)
                pattern.Resize(pattern.RowCount, _song.Tracks.Count);
        }

        SongEditor.Refresh();
        PatternEditor.RefreshRows();
        ChannelRack.Refresh();
        Automation.Refresh();
        ClipEnvelope.Refresh();
        MarkDirty(useNativePlayback: true);
        CommitHistory();
        StatusText = "Track added.";
        AppLogger.Info($"[SongEdit] Track added count={_song.Tracks.Count} patternChannels={string.Join(",", _song.Patterns.Select(p => p.ChannelCount))}");
    }

    /// <summary>
    /// Executes the AddPattern operation.
    /// </summary>
    private void AddPattern()
    {
        BeginHistory("Add pattern");
        var pat = new Pattern(_song.DefaultRowsPerPattern, Math.Max(_song.Tracks.Count, 1))
        {
            Name = $"Pattern {_song.Patterns.Count:D2}"
        };
        _song.Patterns.Add(pat);
        _song.OrderList.Add(_song.Patterns.Count - 1);
        SongEditor.Refresh();
        PatternEditor.RefreshPatterns();
        ChannelRack.Refresh();
        Automation.Refresh();
        ClipEnvelope.Refresh();
        MarkDirty(useNativePlayback: true);
        CommitHistory();
        StatusText = "Pattern added.";
        AppLogger.Info($"[SongEdit] Pattern added index={_song.Patterns.Count - 1} rows={pat.RowCount} channels={pat.ChannelCount}");
    }

    /// <summary>
    /// Executes the AddInstrument operation.
    /// </summary>
    private void AddInstrument()
    {
        BeginHistory("Add instrument");
        _song.Instruments.Add(new Instrument
        {
            Name = $"Instrument {_song.Instruments.Count + 1}",
            SourceType = InstrumentSourceType.Synth
        });
        InstrumentBrowser.Refresh();
        ClipEnvelope.Refresh();
        MarkDirty(useNativePlayback: true);
        CommitHistory();
        StatusText = "Instrument added.";
        AppLogger.Info($"[SongEdit] Instrument added index={_song.Instruments.Count - 1} name=\"{_song.Instruments[^1].Name}\" source={_song.Instruments[^1].SourceType}");
    }

    /// <summary>
    /// Executes the SeedEditorsFromSong operation.
    /// </summary>
    private void SeedEditorsFromSong()
    {
        if (_song.Patterns.Count == 0)
            return;

        int initialPattern = ResolveInitialPatternIndex(_song);
        SongEditor.SelectedPatternIndex = initialPattern;
        if (PatternEditor.CurrentPatternIndex != initialPattern)
            PatternEditor.CurrentPatternIndex = initialPattern;
        if (PianoRoll.CurrentPatternIndex != initialPattern)
            PianoRoll.SetCurrentPattern(initialPattern);

        SongEditor.SelectedTrack = _song.Tracks.FirstOrDefault();
        Automation.SelectedTrack = SongEditor.SelectedTrack;

        var selectedBlock = ResolveInitialBlock(_song, initialPattern);
        SongEditor.SelectedBlock = selectedBlock;
        ClipEnvelope.SelectedBlock = selectedBlock;

        if (PatternEditor.CurrentChannel < 0 && PatternEditor.ChannelCount > 0)
            PatternEditor.CurrentChannel = 0;
        if (PianoRoll.CurrentChannel < 0 && PianoRoll.ChannelOptions.Count > 0)
            PianoRoll.CurrentChannel = 0;

        ChannelRack.Refresh();
        Automation.Refresh();
        ClipEnvelope.Refresh();

        AppLogger.Debug(
            $"[Document] Seeded editors pattern={initialPattern} track=\"{SongEditor.SelectedTrack?.Name ?? "(none)"}\" " +
            $"block={(selectedBlock is null ? "none" : $"{selectedBlock.PatternIndex}@{selectedBlock.StartBeat:0.###}")} " +
            $"tracks={_song.Tracks.Count} patterns={_song.Patterns.Count} orders={_song.OrderList.Count}");
    }

    /// <summary>
    /// Executes the ResolveInitialPatternIndex operation.
    /// </summary>
    private int ResolveInitialPatternIndex(Song song)
    {
        if (song.Patterns.Count == 0)
            return 0;

        if (song.OrderList.Count > 0)
        {
            int firstOrderPattern = song.OrderList[0];
            if ((uint)firstOrderPattern < (uint)song.Patterns.Count)
                return firstOrderPattern;
        }

        return 0;
    }

    /// <summary>
    /// Executes the ResolveInitialBlock operation.
    /// </summary>
    private static PatternBlock? ResolveInitialBlock(Song song, int patternIndex)
    {
        var byPattern = song.Tracks
            .SelectMany(track => track.Blocks)
            .FirstOrDefault(block => block.PatternIndex == patternIndex);
        if (byPattern is not null)
            return byPattern;

        return song.Tracks.SelectMany(track => track.Blocks).FirstOrDefault();
    }

    /// <summary>
    /// Executes the TryResolveArrangementBlockForPattern operation.
    /// </summary>
    private bool TryResolveArrangementBlockForPattern(int patternIndex, int preferredTrackIndex, out PatternBlock? block, out int trackIndex)
    {
        block = null;
        trackIndex = -1;

        if (_song.Tracks.Count == 0)
            return false;

        if (preferredTrackIndex >= 0 && preferredTrackIndex < _song.Tracks.Count)
        {
            block = _song.Tracks[preferredTrackIndex].Blocks.FirstOrDefault(b => b.PatternIndex == patternIndex);
            if (block is not null)
            {
                trackIndex = preferredTrackIndex;
                return true;
            }
        }

        for (int ti = 0; ti < _song.Tracks.Count; ti++)
        {
            var track = _song.Tracks[ti];
            block = track.Blocks.FirstOrDefault(b => b.PatternIndex == patternIndex);
            if (block is not null)
            {
                trackIndex = ti;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes the ResolveSongPlaybackStartBeat operation.
    /// </summary>
    private double ResolveSongPlaybackStartBeat()
    {
        if (PlaybackScope != PlaybackScope.Song)
            return SongEditor.PlayheadBeat;

        return SongEditor.PlayheadBeat;
    }

    /// <summary>
    /// Executes the MarkDirty operation.
    /// </summary>
    private void MarkDirty(bool useNativePlayback)
    {
        IsDirty = true;
        if (useNativePlayback)
        {
            _useOriginalModulePlayback = false;
        }
        NotifyProjectHubChanged();
        AppLogger.Debug($"[Document] Dirty useNativePlayback={useNativePlayback} useOriginalModule={_useOriginalModulePlayback} originalBytes={_originalModuleData?.Length ?? 0}");
    }

    /// <summary>
    /// Marks the current document dirty after an instrument-level edit.
    /// </summary>
    public void MarkInstrumentEdited(string reason)
    {
        MarkDirty(useNativePlayback: true);
        PianoRoll.Refresh();
        PatternEditor.RefreshRows();
        StatusText = reason;
        AppLogger.Info($"[InstrumentEdit] {reason}");
    }

    /// <summary>
    /// Executes the NotifyProjectHubChanged operation.
    /// </summary>
    private void NotifyProjectHubChanged()
    {
        OnPropertyChanged(nameof(ProjectTitleLabel));
        OnPropertyChanged(nameof(ProjectTitleEdit));
        OnPropertyChanged(nameof(ProjectArtistLabel));
        OnPropertyChanged(nameof(ProjectArtistEdit));
        OnPropertyChanged(nameof(ProjectFormatLabel));
        OnPropertyChanged(nameof(ProjectStatsLabel));
        OnPropertyChanged(nameof(ProjectTimingLabel));
        OnPropertyChanged(nameof(ProjectRestartLabel));
        OnPropertyChanged(nameof(ProjectSourceLabel));
        OnPropertyChanged(nameof(ProjectDirtyLabel));
        OnPropertyChanged(nameof(ProjectPlaybackLabel));
        OnPropertyChanged(nameof(ProjectExportLabel));
        OnPropertyChanged(nameof(WorkflowLoadState));
        OnPropertyChanged(nameof(WorkflowEditState));
        OnPropertyChanged(nameof(WorkflowPreviewState));
        OnPropertyChanged(nameof(WorkflowExportState));
    }

    /// <summary>
    /// Executes the UpdateSourceFormatReadout operation.
    /// </summary>
    private void UpdateSourceFormatReadout()
    {
        string extension = Path.GetExtension(FilePath);
        string source = extension.Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase)
            ? _song.OriginalModuleData is { Length: > 0 }
                ? $"amChipper AMC container | embedded {ModuleFormatCatalog.GetDisplayLabel(_song)} source | exact module path"
                : "amChipper AMC container | internal sequencer"
            : extension.Equals(SongProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase)
            ? "amChipper compressed project | internal sequencer"
            : _originalModuleData is null
            ? "Internal"
            : $"{ModuleFormatCatalog.GetDisplayLabel(_song)} | native source";
        if (!string.IsNullOrWhiteSpace(_originalModulePath))
            source += $" | {Path.GetFileName(_originalModulePath)}";
        SourceFormatLabel = source;
        NotifyProjectHubChanged();
    }

    /// <summary>
    /// Executes the UpdateRuntimeTempoReadout operation.
    /// </summary>
    private void UpdateRuntimeTempoReadout()
    {
        if (Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded)
        {
            RuntimeBpmLabel = $"{ModuleFormatCatalog.GetDisplayLabel(_song)} rendered audio";
            OnPropertyChanged(nameof(Bpm));
            return;
        }

        int bpm = Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded && Audio.ModulePlayer.CurrentTempo > 0
            ? Audio.ModulePlayer.CurrentTempo
            : _song.Bpm;
        int speed = Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded && Audio.ModulePlayer.CurrentSpeed > 0
            ? Audio.ModulePlayer.CurrentSpeed
            : _song.InitialSpeed;
        RuntimeBpmLabel = Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded
            ? $"Live BPM {bpm} | Initial {_song.Bpm} | SPD {speed}"
            : $"BPM {_song.Bpm} | SPD {speed}";
        OnPropertyChanged(nameof(Bpm));
        NotifyProjectHubChanged();
    }

    /// <summary>
    /// Executes the UpdateTransportReadout operation.
    /// </summary>
    private void UpdateTransportReadout()
    {
        double position = Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded
            ? Audio.AudioFilePlayer.PositionSecs
            : Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded
            ? Audio.ModulePlayer.PositionSecs
            : SongEditor.PlayheadBeat * 60.0 / Math.Max(_song.Bpm, 1);
        double duration = Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded
            ? Audio.AudioFilePlayer.DurationSecs
            : Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded
            ? Audio.ModulePlayer.DurationSecs
            : EstimateSongDurationSeconds();

        TransportTimeLabel = $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    /// <summary>
    /// Executes the SyncPlaybackVisualsFromTransport operation.
    /// </summary>
    private void SyncPlaybackVisualsFromTransport()
    {
        if (!IsPlaying)
            return;

        if (Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded)
        {
            double beat = Audio.AudioFilePlayer.PositionSecs * Math.Max(_song.Bpm, 1) / 60.0;
            SyncEditorsToBeat(beat, "[AudioFilePlayback]");
            return;
        }

        if (Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded)
        {
            int order = Audio.ModulePlayer.CurrentOrder;
            int row = Math.Max(0, Audio.ModulePlayer.CurrentRow);
            int patternIndex = ResolvePatternForOrder(order);
            if (patternIndex < 0)
                patternIndex = PatternEditor.CurrentPatternIndex;

            double beat = TryGetOrderStartBeat(order, out double orderStartBeat)
                ? orderStartBeat + row / (double)Math.Max(_song.RowsPerBeat, 1)
                : Audio.ModulePlayer.PositionSecs * Math.Max(Bpm, 1) / 60.0;

            SyncEditorsToPatternRow(patternIndex, row, beat, _modulePreviewActive && PlaybackScope == PlaybackScope.PianoRoll, "[ModulePlayback]");
            return;
        }

        if (Audio.Sequencer.IsPlaying)
        {
            SyncEditorsToPatternRow(
                Audio.Sequencer.CurrentPatternIndex,
                Audio.Sequencer.CurrentRow,
                Audio.Sequencer.CurrentBeat,
                PlaybackScope == PlaybackScope.PianoRoll,
                "[SequencerPlayback]");
        }
    }

    /// <summary>
    /// Executes the SyncEditorsToBeat operation.
    /// </summary>
    private void SyncEditorsToBeat(double beat, string source)
    {
        beat = Math.Max(0, beat);
        int rowsPerBeat = Math.Max(_song.RowsPerBeat, 1);
        int globalRow = Math.Max(0, (int)Math.Floor(beat * rowsPerBeat));
        int patternIndex = PatternEditor.CurrentPatternIndex;
        int localRow = globalRow;

        if (TryResolveOrderAtBeat(beat, out int order, out int row))
        {
            localRow = row;
            int orderPattern = ResolvePatternForOrder(order);
            if (orderPattern >= 0)
                patternIndex = orderPattern;
        }
        else if (_song.Patterns.Count > 0)
        {
            var pattern = _song.Patterns[Math.Clamp(patternIndex, 0, _song.Patterns.Count - 1)];
            localRow = Math.Clamp(globalRow, 0, Math.Max(pattern.RowCount - 1, 0));
        }

        SyncEditorsToPatternRow(patternIndex, localRow, beat, PlaybackScope == PlaybackScope.PianoRoll, source);
    }

    /// <summary>
    /// Executes the SyncEditorsToPatternRow operation.
    /// </summary>
    private void SyncEditorsToPatternRow(int patternIndex, int row, double songBeat, bool pianoRollUsesLocalBeat, string source)
    {
        if (_song.Patterns.Count == 0)
            return;

        patternIndex = Math.Clamp(patternIndex, 0, _song.Patterns.Count - 1);
        var pattern = _song.Patterns[patternIndex];
        row = Math.Clamp(row, 0, Math.Max(pattern.RowCount - 1, 0));

        if (_modulePreviewActive && (Audio.ModulePlayer.CurrentOrder != _modulePreviewOrder || patternIndex != _modulePreviewPattern))
        {
            AppLogger.Debug($"{source} Preview scope completed order={_modulePreviewOrder} pattern={_modulePreviewPattern} currentOrder={Audio.ModulePlayer.CurrentOrder} currentPattern={patternIndex}");
            Stop();
            return;
        }

        PatternEditor.TrackPlayback(patternIndex, row);
        if (PianoRoll.CurrentPatternIndex != patternIndex && (PlaybackScope is PlaybackScope.PianoRoll or PlaybackScope.Pattern))
            PianoRoll.SetCurrentPattern(patternIndex);

        int rowsPerBeat = Math.Max(_song.RowsPerBeat, 1);
        SongEditor.PlayheadBeat = Math.Max(0, songBeat);
        PianoRoll.PlayheadBeat = row / (double)rowsPerBeat;

        PulseModuleTrackMeters(patternIndex, row);
        ChannelRack.UpdatePlaybackRow(row);
        Automation.NotifyPlaybackMoved();
        ClipEnvelope.NotifyPlaybackMoved();
    }

    /// <summary>
    /// Executes the ResolvePatternForOrder operation.
    /// </summary>
    private int ResolvePatternForOrder(int order)
    {
        if (order >= 0 && order < _song.OrderList.Count)
        {
            int patternIndex = _song.OrderList[order];
            if ((uint)patternIndex < (uint)_song.Patterns.Count)
                return patternIndex;
        }

        return -1;
    }

    /// <summary>
    /// Executes the EstimateSongDurationSeconds operation.
    /// </summary>
    private double EstimateSongDurationSeconds()
    {
        double endBeat = 0;
        foreach (var track in _song.Tracks)
            foreach (var block in track.Blocks)
                endBeat = Math.Max(endBeat, block.StartBeat + block.DurationBeats);

        if (endBeat <= 0 && _song.Patterns.Count > 0)
            endBeat = _song.Patterns[0].RowCount / (double)Math.Max(_song.RowsPerBeat, 1);

        return endBeat * 60.0 / Math.Max(_song.Bpm, 1);
    }

    /// <summary>
    /// Executes the FormatTime operation.
    /// </summary>
    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;
        int minutes = (int)(seconds / 60);
        double rem = seconds - minutes * 60;
        return $"{minutes:00}:{rem:00.0}";
    }

    /// <summary>
    /// Executes the UpdateSpectrum operation.
    /// </summary>
    private void UpdateSpectrum(float[] buffer, int sampleCount)
    {
        if (sampleCount <= 0 || SpectrumBands.Count == 0)
            return;

        int bandCount = SpectrumBands.Count;
        int channels = 2;
        int availableFrames = Math.Min(sampleCount / channels, buffer.Length / channels);
        int frames = Math.Min(availableFrames, SpectrumAnalyzerMode == "Compact Bars" ? 512 : 1024);
        if (frames <= 8)
            return;

        int sampleRate = Math.Max(8000, Audio.SampleRate);
        double minHz = 31.0;
        double maxHz = Math.Min(18000.0, sampleRate * 0.48);
        int startFrame = Math.Max(0, availableFrames - frames);
        double analyzerLift = SpectrumAnalyzerMode switch
        {
            "Peak Focus" => 1.28,
            "Compact Bars" => 1.08,
            _ => 1.18
        };

        for (int band = 0; band < bandCount; band++)
        {
            double normalized = (band + 0.5) / bandCount;
            double hz = minHz * Math.Pow(maxHz / minHz, normalized);
            double omega = 2.0 * Math.PI * hz / sampleRate;
            double re = 0;
            double im = 0;

            for (int frame = 0; frame < frames; frame += 2)
            {
                int sampleIndex = (startFrame + frame) * 2;
                if (sampleIndex + 1 >= buffer.Length)
                    break;

                double sample = (buffer[sampleIndex] + buffer[sampleIndex + 1]) * 0.5;
                double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * frame / Math.Max(1, frames - 1));
                double phase = omega * frame;
                re += sample * window * Math.Cos(phase);
                im -= sample * window * Math.Sin(phase);
            }

            double magnitude = Math.Sqrt(re * re + im * im) / (frames * 0.25);
            double db = 20.0 * Math.Log10(Math.Max(magnitude * analyzerLift * VisualizerIntensity, 0.000001));
            double shaped = Math.Clamp((db + 72.0) / 72.0, 0, 1);
            shaped = Math.Pow(shaped, 0.72);

            double previous = SpectrumBands[band].Level;
            double release = SpectrumAnalyzerMode == "Peak Focus" ? 0.86 : 0.80;
            SpectrumBands[band].Level = shaped >= previous
                ? previous + (shaped - previous) * 0.62
                : Math.Max(shaped, previous * release);
        }
    }

    /// <summary>
    /// Executes the PulseModuleTrackMeters operation.
    /// </summary>
    private void PulseModuleTrackMeters(int patternIndex, int row)
    {
        if (patternIndex < 0 || patternIndex >= _song.Patterns.Count)
            return;

        var pattern = _song.Patterns[patternIndex];
        if (row < 0 || row >= pattern.RowCount)
            return;

        int channels = Math.Min(pattern.ChannelCount, _song.Tracks.Count);
        for (int channel = 0; channel < channels; channel++)
        {
            var note = pattern.GetNote(row, channel);
            bool active = note.Pitch is > 0 and < (byte)SpecialNote.NoteOff
                || note.InstrumentIndex > 0
                || note.Volume != 255
                || note.VolumeColumn != 0
                || note.Effect != EffectCommand.None
                || note.EffectColumn != 0
                || note.EffectParam != 0;
            if (!active)
                continue;

            double noteVolume = note.Volume <= 64 ? note.Volume / 64.0 : 0.86;
            double trackVolume = _song.Tracks[channel].Volume / 128.0;
            double pulse = note.Pitch is > 0 and < (byte)SpecialNote.NoteOff
                ? Math.Clamp(noteVolume * trackVolume, 0.55, 1.0)
                : Math.Clamp(noteVolume * trackVolume, 0.25, 0.8);
            _song.Tracks[channel].MeterLevel = Math.Max(_song.Tracks[channel].MeterLevel, pulse);
            _song.Tracks[channel].EffectSummary = BuildLiveCellSummary(note);
        }
    }

    /// <summary>
    /// Executes the BuildLiveCellSummary operation.
    /// </summary>
    private static string BuildLiveCellSummary(Note note)
    {
        if (IsSidTraceCell(note))
            return $"LIVE {FormatSidTraceCell(note)} {note.NoteName} PW{note.EffectColumn:X2} CTRL{note.EffectParam:X2}";

        string pitch = note.Pitch is > 0 and < (byte)SpecialNote.NoteOff
            ? note.NoteName
            : "---";
        string inst = note.InstrumentIndex > 0 ? $"I{note.InstrumentIndex:D2}" : "I--";
        string vol = note.VolumeColumn != 0
            ? $"V{note.VolumeColumn:X2}"
            : note.Volume <= 64 ? $"V{note.Volume:X2}" : "V--";
        string fx = note.EffectColumn != 0 || note.Effect != EffectCommand.None || note.EffectParam != 0
            ? $"FX{FormatLiveEffect(note)}{note.EffectParam:X2}"
            : "FX--";
        return $"LIVE {pitch} {inst} {vol} {fx}";
    }

    /// <summary>
    /// Executes the FormatLiveEffect operation.
    /// </summary>
    private static string FormatLiveEffect(Note note)
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
        note.VolumeColumn is 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80;

    /// <summary>
    /// Executes the FormatSidTraceCell operation.
    /// </summary>
    private static string FormatSidTraceCell(Note note)
    {
        string wave = (note.VolumeColumn & 0xF0) switch
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
        string flags = string.Empty;
        if ((note.EffectParam & 0x02) != 0)
            flags += " sync";
        if ((note.EffectParam & 0x04) != 0)
            flags += " ring";
        if ((note.EffectParam & 0x08) != 0)
            flags += " test";
        return wave + flags;
    }

    /// <summary>
    /// Executes the DecayModuleTrackMeters operation.
    /// </summary>
    private void DecayModuleTrackMeters()
    {
        foreach (var track in _song.Tracks)
            track.MeterLevel *= 0.94;
    }

    /// <summary>
    /// Executes the ApplyModuleVuMeters operation.
    /// </summary>
    private void ApplyModuleVuMeters()
    {
        if (!Audio.ModulePlayer.IsLoaded)
        {
            DecayModuleTrackMeters();
            return;
        }

        int count = Math.Min(_song.Tracks.Count, Audio.ModulePlayer.ChannelCount);
        bool usedNativeVu = false;
        for (int i = 0; i < count; i++)
        {
            double vu = Audio.ModulePlayer.GetCurrentChannelVuMono(i);
            if (vu < 0)
                continue;
            if (vu <= 0.0001)
                continue;

            usedNativeVu = true;
            double boosted = Math.Clamp(vu * 2.4, 0, 1);
            _song.Tracks[i].MeterLevel = Math.Max(_song.Tracks[i].MeterLevel * 0.72, boosted);
        }

        for (int i = count; i < _song.Tracks.Count; i++)
            _song.Tracks[i].MeterLevel *= 0.94;

        if (!usedNativeVu)
            DecayModuleTrackMeters();
    }

    /// <summary>
    /// Executes the SanitizeFileName operation.
    /// </summary>
    private static string SanitizeFileName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "amChipper" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    /// <summary>
    /// Executes the ApplyTheme operation.
    /// </summary>
    private void ApplyTheme(string theme)
    {
        theme = NormalizeThemeName(theme);
        var palette = theme.ToUpperInvariant() switch
        {
            "CLASSIC TRACKER" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF02110A",
                ["BgPanel"] = "#FF062016",
                ["BgControl"] = "#FF0B2B1D",
                ["BgHover"] = "#FF124A30",
                ["BgSelect"] = "#FF17663F",
                ["Accent"] = "#FF00E676",
                ["AccentLight"] = "#FF80FFB6",
                ["Border"] = "#FF1E7048",
                ["TextPrimary"] = "#FFF2FFF7",
                ["TextSecondary"] = "#FFC8FAD8",
                ["TextDisabled"] = "#FF5C8B70"
            },
            "AMBER CRT" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF130800",
                ["BgPanel"] = "#FF241107",
                ["BgControl"] = "#FF351B0A",
                ["BgHover"] = "#FF532C11",
                ["BgSelect"] = "#FF724015",
                ["Accent"] = "#FFFFB000",
                ["AccentLight"] = "#FFFFE082",
                ["Border"] = "#FF8A551C",
                ["TextPrimary"] = "#FFFFF5DD",
                ["TextSecondary"] = "#FFFFD599",
                ["TextDisabled"] = "#FF8E7051"
            },
            "MIDNIGHT PRO" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF05070C",
                ["BgPanel"] = "#FF0D111C",
                ["BgControl"] = "#FF151B2B",
                ["BgHover"] = "#FF202A44",
                ["BgSelect"] = "#FF263864",
                ["Accent"] = "#FF4F8BFF",
                ["AccentLight"] = "#FFA7C4FF",
                ["Border"] = "#FF34425F",
                ["TextPrimary"] = "#FFF4F7FF",
                ["TextSecondary"] = "#FFB8C3DC",
                ["TextDisabled"] = "#FF586174"
            },
            "ICE MATRIX" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF061111",
                ["BgPanel"] = "#FF0D1F21",
                ["BgControl"] = "#FF123034",
                ["BgHover"] = "#FF1B454B",
                ["BgSelect"] = "#FF23606A",
                ["Accent"] = "#FF39D9C8",
                ["AccentLight"] = "#FF9BFFF4",
                ["Border"] = "#FF2D6A73",
                ["TextPrimary"] = "#FFEFFFFD",
                ["TextSecondary"] = "#FFB5E7E1",
                ["TextDisabled"] = "#FF537978"
            },
            "MAGENTA CIRCUIT" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF100713",
                ["BgPanel"] = "#FF1B0D20",
                ["BgControl"] = "#FF2A1430",
                ["BgHover"] = "#FF421F4B",
                ["BgSelect"] = "#FF5B2A6A",
                ["Accent"] = "#FFFF4FD8",
                ["AccentLight"] = "#FFFFA3ED",
                ["Border"] = "#FF6C3B7A",
                ["TextPrimary"] = "#FFFFF2FD",
                ["TextSecondary"] = "#FFEEC5E8",
                ["TextDisabled"] = "#FF84617D"
            },
            "NEON STUDIO" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF080A18",
                ["BgPanel"] = "#FF11142A",
                ["BgControl"] = "#FF1A1F3B",
                ["BgHover"] = "#FF252E58",
                ["BgSelect"] = "#FF2B3A74",
                ["Accent"] = "#FF2F8CFF",
                ["AccentLight"] = "#FF66E6FF",
                ["Border"] = "#FF35406B",
                ["TextPrimary"] = "#FFF1F6FF",
                ["TextSecondary"] = "#FFC8D8FF",
                ["TextDisabled"] = "#FF5F688E"
            },
            "CARBON LIME" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF070907",
                ["BgPanel"] = "#FF101610",
                ["BgControl"] = "#FF1A241A",
                ["BgHover"] = "#FF263827",
                ["BgSelect"] = "#FF335135",
                ["Accent"] = "#FF9BE436",
                ["AccentLight"] = "#FFD8FF7A",
                ["Border"] = "#FF4B6F3F",
                ["TextPrimary"] = "#FFF6FFF0",
                ["TextSecondary"] = "#FFD1E9C6",
                ["TextDisabled"] = "#FF6F8668"
            },
            "RUBY WAVE" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF12070B",
                ["BgPanel"] = "#FF220D16",
                ["BgControl"] = "#FF351522",
                ["BgHover"] = "#FF512238",
                ["BgSelect"] = "#FF6B2D4E",
                ["Accent"] = "#FFFF3D7F",
                ["AccentLight"] = "#FFFF9DBF",
                ["Border"] = "#FF7A3756",
                ["TextPrimary"] = "#FFFFF3F7",
                ["TextSecondary"] = "#FFFFC5D6",
                ["TextDisabled"] = "#FF8A6270"
            },
            "OCEAN LAB" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF041218",
                ["BgPanel"] = "#FF09222C",
                ["BgControl"] = "#FF103545",
                ["BgHover"] = "#FF19516A",
                ["BgSelect"] = "#FF216B8B",
                ["Accent"] = "#FF23B7E5",
                ["AccentLight"] = "#FF8BE9FF",
                ["Border"] = "#FF2B6D84",
                ["TextPrimary"] = "#FFF1FCFF",
                ["TextSecondary"] = "#FFBFEAF5",
                ["TextDisabled"] = "#FF5D7C86"
            },
            "STEEL MONO" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF0D0F12",
                ["BgPanel"] = "#FF181B20",
                ["BgControl"] = "#FF242932",
                ["BgHover"] = "#FF343B47",
                ["BgSelect"] = "#FF454F60",
                ["Accent"] = "#FF9DAFC7",
                ["AccentLight"] = "#FFE2ECF8",
                ["Border"] = "#FF596371",
                ["TextPrimary"] = "#FFF7FAFF",
                ["TextSecondary"] = "#FFC7D0DD",
                ["TextDisabled"] = "#FF737B86"
            },
            "SUNSET POP" => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF180B16",
                ["BgPanel"] = "#FF2A1425",
                ["BgControl"] = "#FF3D2037",
                ["BgHover"] = "#FF5A2E51",
                ["BgSelect"] = "#FF794069",
                ["Accent"] = "#FFFF8A3D",
                ["AccentLight"] = "#FFFFD56F",
                ["Border"] = "#FF8A516D",
                ["TextPrimary"] = "#FFFFF7EF",
                ["TextSecondary"] = "#FFFFD9C0",
                ["TextDisabled"] = "#FF9A756D"
            },
            _ => new Dictionary<string, string>
            {
                ["BgDeep"] = "#FF140A1D",
                ["BgPanel"] = "#FF231333",
                ["BgControl"] = "#FF322045",
                ["BgHover"] = "#FF4A2D66",
                ["BgSelect"] = "#FF5D367C",
                ["Accent"] = "#FFB64DFF",
                ["AccentLight"] = "#FFFF8CFF",
                ["Border"] = "#FF6D4D86",
                ["TextPrimary"] = "#FFF9F2FF",
                ["TextSecondary"] = "#FFD5BEDF",
                ["TextDisabled"] = "#FF806B8F"
            }
        };

        foreach (var (key, hex) in palette)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            SetThemeBrushResource(key, color);
        }

        SetThemeGradientResource("FlStudioChrome", palette["BgSelect"], palette["BgPanel"], palette["BgDeep"], vertical: true);
        SetThemeGradientResource("FlStudioPanel", palette["BgControl"], palette["BgPanel"], palette["BgDeep"], vertical: true);
        SetThemeGradientResource("FlStudioStrip", palette["BgHover"], palette["BgPanel"], palette["BgDeep"], vertical: false);
        SetThemeGradientResource("FlStudioActive", palette["AccentLight"], palette["Accent"], palette["BgSelect"], vertical: true);

        var accentLight = (Color)ColorConverter.ConvertFromString(palette["AccentLight"]);
        if (Application.Current.TryFindResource("SoftGlow") is DropShadowEffect { IsFrozen: false } glow)
            glow.Color = accentLight;
        else
            Application.Current.Resources["SoftGlow"] = new DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.42,
                Color = accentLight
            };

        if (Application.Current.MainWindow is not null &&
            Application.Current.TryFindResource("BgDeep") is Brush windowBrush)
        {
            Application.Current.MainWindow.Background = windowBrush;
            WindowChromeTheme.Apply(Application.Current.MainWindow);
        }

        ApplyUiChromeSettings();
        StatusText = $"Theme: {theme}";
        AppLogger.Info($"[UI] Theme applied name={theme}");
    }

    /// <summary>
    /// Executes the ApplyUiChromeSettings operation.
    /// </summary>
    private void ApplyUiChromeSettings()
    {
        if (Application.Current is null)
            return;

        double shineAlpha = ShowUiShine ? 0x58 : 0x00;
        double panelAlpha = ShowUiShine ? 0x24 : 0x00;
        Application.Current.Resources["ButtonShine"] = new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb((byte)shineAlpha, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.42),
                new GradientStop(Color.FromArgb(0x28, 0, 0, 0), 1)
            ],
            new Point(0, 0),
            new Point(0, 1));
        Application.Current.Resources["PanelSheen"] = new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb((byte)panelAlpha, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.36),
                new GradientStop(Color.FromArgb(0x24, 0, 0, 0), 1)
            ],
            new Point(0, 0),
            new Point(1, 1));
        Application.Current.Resources["PanelShadow"] = new DropShadowEffect
        {
            BlurRadius = ShowPanelShadows ? 18 : 0,
            ShadowDepth = ShowPanelShadows ? 1 : 0,
            Opacity = ShowPanelShadows ? 0.34 : 0,
            Color = Colors.Black
        };
    }

    /// <summary>
    /// Executes the NormalizeThemeName operation.
    /// </summary>
    private static string NormalizeThemeName(string theme)
    {
        return theme.Trim().ToUpperInvariant() switch
        {
            "CLASSIC" or "CLASSIC TRACKER" => "Classic Tracker",
            "AMBER" or "AMBER CRT" => "Amber CRT",
            "MIDNIGHT" or "MIDNIGHT PRO" => "Midnight Pro",
            "ICE" or "ICE MATRIX" => "Ice Matrix",
            "MAGENTA" or "MAGENTA CIRCUIT" => "Magenta Circuit",
            "NEON" or "NEON STUDIO" => "Neon Studio",
            "CARBON" or "CARBON LIME" => "Carbon Lime",
            "RUBY" or "RUBY WAVE" => "Ruby Wave",
            "OCEAN" or "OCEAN LAB" => "Ocean Lab",
            "STEEL" or "STEEL MONO" => "Steel Mono",
            "SUNSET" or "SUNSET POP" => "Sunset Pop",
            _ => "FL Grape"
        };
    }

    /// <summary>
    /// Executes the NormalizeOption operation.
    /// </summary>
    private static string NormalizeOption(string? value, IEnumerable<string> allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim();
        return allowed.FirstOrDefault(option => string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    /// <summary>
    /// Executes the NormalizeSidXmExportMode operation.
    /// </summary>
    private static string NormalizeSidXmExportMode(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "TRACE" or "TRACE ONLY" => "Trace Only",
            "RENDERED MIX + TRACE" or "RENDERED MIX WITH TRACE" or "MIX + TRACE" => "Rendered Mix + Trace",
            _ => "Rendered Mix Only"
        };
    }

    /// <summary>
    /// Executes the SetThemeBrushResource operation.
    /// </summary>
    private static void SetThemeBrushResource(string key, Color color)
    {
        var updated = false;
        if (Application.Current is not null)
            updated = TrySetThemeBrushResource(Application.Current.Resources, key, color);

        if (!updated && Application.Current is not null)
            Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    /// <summary>
    /// Executes the SetThemeGradientResource operation.
    /// </summary>
    private static void SetThemeGradientResource(string key, string first, string second, string third, bool vertical)
    {
        if (Application.Current is null)
            return;

        Application.Current.Resources[key] = new LinearGradientBrush(
            [
                new GradientStop((Color)ColorConverter.ConvertFromString(first), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(second), 0.54),
                new GradientStop((Color)ColorConverter.ConvertFromString(third), 1)
            ],
            new Point(0, 0),
            vertical ? new Point(0, 1) : new Point(1, 1));
    }

    /// <summary>
    /// Executes the TrySetThemeBrushResource operation.
    /// </summary>
    private static bool TrySetThemeBrushResource(ResourceDictionary dictionary, string key, Color color)
    {
        var updated = false;
        if (dictionary.Contains(key))
        {
            if (dictionary[key] is SolidColorBrush { IsFrozen: false } brush)
            {
                brush.Color = color;
            }
            else
            {
                dictionary[key] = new SolidColorBrush(color);
            }

            updated = true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
            updated |= TrySetThemeBrushResource(merged, key, color);

        return updated;
    }

    /// <summary>
    /// Executes the GetNativeModuleExtension operation.
    /// </summary>
    private static string GetNativeModuleExtension(Song song) =>
        ModuleFormatCatalog.GetPreferredExtension(song.Format, song.SourceModuleExtension);

    /// <summary>
    /// Executes the ApplyMeterLevels operation.
    /// </summary>
    private void ApplyMeterLevels(float[] trackLevels, float masterLevel)
    {
        for (int i = 0; i < _song.Tracks.Count; i++)
        {
            double level = i < trackLevels.Length ? trackLevels[i] : 0d;
            _song.Tracks[i].MeterLevel = level;
        }

        MasterMeterLevel = masterLevel;
    }

    /// <summary>
    /// Executes the ClearTrackMeters operation.
    /// </summary>
    private void ClearTrackMeters()
    {
        foreach (var track in _song.Tracks)
            track.MeterLevel = 0;
        MasterMeterLevel = 0;
        ClearSpectrum();
    }

    /// <summary>
    /// Executes the ClearSpectrum operation.
    /// </summary>
    private void ClearSpectrum()
    {
        foreach (var band in SpectrumBands)
            band.Reset();
    }

    /// <summary>
    /// Executes the AttachSongListeners operation.
    /// </summary>
    private void AttachSongListeners(Song song)
    {
        foreach (var track in song.Tracks)
            track.PropertyChanged += Track_PropertyChanged;
    }

    /// <summary>
    /// Executes the DetachSongListeners operation.
    /// </summary>
    private void DetachSongListeners(Song song)
    {
        foreach (var track in song.Tracks)
            track.PropertyChanged -= Track_PropertyChanged;
    }

    /// <summary>
    /// Executes the Track_PropertyChanged operation.
    /// </summary>
    private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoringHistory)
            return;

        if (string.Equals(e.PropertyName, nameof(Track.MeterLevel), StringComparison.Ordinal))
            return;
        if (string.Equals(e.PropertyName, nameof(Track.EffectSummary), StringComparison.Ordinal))
            return;

        if (sender is Track track)
        {
            int trackIndex = _song.Tracks.IndexOf(track);
            if (trackIndex >= 0)
                ApplyTrackStateToModule(trackIndex, track);
        }

        AppLogger.Debug($"[SongModel] Track property changed property={e.PropertyName ?? "(unknown)"}");
        MarkDirty(useNativePlayback: true);
        SongEditor.RaiseLayoutChanged();
        SongEditor.RaiseSongDataChanged();
    }

    /// <summary>
    /// Executes the ResolvePlaybackStartRow operation.
    /// </summary>
    private int ResolvePlaybackStartRow()
    {
        if (PlaybackScope == PlaybackScope.Song)
            return Math.Max(0, (int)Math.Round(SongEditor.PlayheadBeat * Math.Max(_song.RowsPerBeat, 1)));

        if (PlaybackScope == PlaybackScope.PianoRoll)
            return Math.Max(0, (int)Math.Round(PianoRoll.PlayheadBeat * Math.Max(_song.RowsPerBeat, 1)));

        return Math.Clamp(PatternEditor.CurrentRow, 0,
            Math.Max(PatternEditor.CurrentPattern?.RowCount - 1 ?? 0, 0));
    }

    /// <summary>
    /// Executes the SeekToBeat operation.
    /// </summary>
    private void SeekToBeat(double beat)
    {
        beat = Math.Max(0, beat);
        AppLogger.Info($"[Transport] Seek requested beat={beat:0.###} audioMode={(Audio.UseModulePlayer ? "Module" : "Sequencer")} isPlaying={IsPlaying}");
        SongEditor.PlayheadBeat = beat;
        if (PlaybackScope == PlaybackScope.PianoRoll)
            PianoRoll.PlayheadBeat = beat;

        if (Audio.UseAudioFilePlayer && Audio.AudioFilePlayer.IsLoaded)
        {
            Audio.AudioFilePlayer.PositionSecs = beat * 60.0 / Math.Max(_song.Bpm, 1);
            SyncEditorsToBeat(beat, "[AudioFileSeek]");
            StatusText = $"Position: beat {beat:0.##}";
            return;
        }

        if (Audio.UseModulePlayer && Audio.ModulePlayer.IsLoaded)
        {
            SeekModuleToBeat(beat);
            return;
        }

        Audio.Sequencer.SeekToBeat(beat);
        StatusText = $"Position: beat {beat:0.##}";
    }

    /// <summary>
    /// Executes the TryResolveArrangementBlockAtBeat operation.
    /// </summary>
    private bool TryResolveArrangementBlockAtBeat(double beat, out PatternBlock? block, out int trackIndex)
    {
        block = null;
        trackIndex = -1;

        if (_song.Tracks.Count == 0)
            return false;

        for (int ti = 0; ti < _song.Tracks.Count; ti++)
        {
            var track = _song.Tracks[ti];
            foreach (var candidate in track.Blocks)
            {
                double start = candidate.StartBeat;
                double end = candidate.StartBeat + candidate.DurationBeats;
                if (beat < start || beat >= end)
                    continue;

                block = candidate;
                trackIndex = ti;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes the TryGetOrderStartBeat operation.
    /// </summary>
    private bool TryGetOrderStartBeat(int order, out double startBeat)
    {
        startBeat = 0;
        if (order < 0 || order >= _song.OrderList.Count)
            return false;

        for (int i = 0; i < order; i++)
        {
            int patternIndex = _song.OrderList[i];
            if ((uint)patternIndex >= (uint)_song.Patterns.Count)
                continue;

            startBeat += Math.Max(_song.Patterns[patternIndex].RowCount / (double)Math.Max(_song.RowsPerBeat, 1), 1.0);
        }

        return true;
    }

    /// <summary>
    /// Executes the TryResolveOrderAtBeat operation.
    /// </summary>
    private bool TryResolveOrderAtBeat(double beat, out int order, out int row)
    {
        order = 0;
        row = 0;
        beat = Math.Max(0, beat);

        double cursor = 0;
        int rowsPerBeat = Math.Max(_song.RowsPerBeat, 1);
        for (int i = 0; i < _song.OrderList.Count; i++)
        {
            int patternIndex = _song.OrderList[i];
            if ((uint)patternIndex >= (uint)_song.Patterns.Count)
                continue;

            var pattern = _song.Patterns[patternIndex];
            double duration = Math.Max(pattern.RowCount / (double)rowsPerBeat, 1.0);
            if (beat >= cursor && beat < cursor + duration)
            {
                order = i;
                row = Math.Clamp((int)Math.Round((beat - cursor) * rowsPerBeat), 0, Math.Max(pattern.RowCount - 1, 0));
                return true;
            }

            cursor += duration;
        }

        if (_song.OrderList.Count > 0)
        {
            order = Math.Max(0, _song.OrderList.Count - 1);
            int patternIndex = _song.OrderList[order];
            row = (uint)patternIndex < (uint)_song.Patterns.Count
                ? Math.Max(_song.Patterns[patternIndex].RowCount - 1, 0)
                : 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes the SeekModuleToBeat operation.
    /// </summary>
    private void SeekModuleToBeat(double beat)
    {
        if (!Audio.ModulePlayer.IsLoaded)
            return;

        if (StartAtRestartOrder && beat <= 0.001 && _song.RestartOrder >= 0)
        {
            AppLogger.Info($"[Transport] Seeking to tracker restart order {_song.RestartOrder}");
            Audio.ModulePlayer.SeekToOrder(_song.RestartOrder, 0);
            return;
        }

        if (TryResolveOrderAtBeat(beat, out int order, out int row))
        {
            Audio.ModulePlayer.SeekToOrder(order, row);
            return;
        }

        Audio.ModulePlayer.SeekToOrder(0, 0);
    }

    /// <summary>
    /// Executes the ResolveModuleOrderForPattern operation.
    /// </summary>
    private int ResolveModuleOrderForPattern(int patternIndex)
    {
        if (patternIndex < 0 || patternIndex >= _song.Patterns.Count)
            return -1;

        if (_song.OrderList.Count > 0)
        {
            int order = _song.OrderList.IndexOf(patternIndex);
            if (order >= 0)
                return order;
        }

        foreach (var track in _song.Tracks)
        {
            for (int i = 0; i < track.Blocks.Count; i++)
            {
                if (track.Blocks[i].PatternIndex == patternIndex)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Executes the ApplyModuleMuteProfile operation.
    /// </summary>
    private void ApplyModuleMuteProfile()
    {
        if (!Audio.ModulePlayer.IsLoaded)
            return;

        int channelCount = Audio.ModulePlayer.ChannelCount;
        if (channelCount <= 0)
            return;

        bool hasSolo = _song.Tracks.Any(t => t.Solo);
        for (int i = 0; i < channelCount; i++)
        {
            var track = i < _song.Tracks.Count ? _song.Tracks[i] : null;
            bool mute = track is null
                || track.Muted
                || (hasSolo && !track.Solo)
                || (SoloSelectedPianoRollChannel && _modulePreviewActive && PlaybackScope == PlaybackScope.PianoRoll && i != Math.Clamp(PianoRoll.CurrentChannel, 0, channelCount - 1));

            if (track is not null)
                ApplyTrackStateToModule(i, track, mute);
            else
                Audio.ModulePlayer.SetChannelMuteStatus(i, mute);
        }
    }

    /// <summary>
    /// Executes the ApplyTrackStateToModule operation.
    /// </summary>
    private void ApplyTrackStateToModule(int channelIndex, Track track, bool? muteOverride = null)
    {
        if (!Audio.ModulePlayer.IsLoaded)
            return;

        Audio.ModulePlayer.SetChannelVolume(channelIndex, track.Volume / 128.0);
        Audio.ModulePlayer.SetChannelPanning(channelIndex, (track.Panning / 255.0) * 2.0 - 1.0);

        bool hasSolo = _song.Tracks.Any(t => t.Solo);
        bool mute = muteOverride ?? (track.Muted || (hasSolo && !track.Solo));
        Audio.ModulePlayer.SetChannelMuteStatus(channelIndex, mute);
    }

    /// <summary>
    /// Carries DocumentSnapshot data.
    /// </summary>
    private sealed record DocumentSnapshot(
        Song Song,
        string FilePath,
        bool UseOriginalModulePlayback,
        PlaybackScope PlaybackScope,
        byte SelectedInstrumentNumber,
        int SongEditorPatternIndex,
        int PianoPatternIndex,
        int PianoChannel,
        int PatternPatternIndex,
        int PatternRow,
        int PatternChannel,
        double SongPlayheadBeat,
        double PianoPlayheadBeat,
        PlaybackState PlaybackState,
        string StatusText,
        string Reason);

    /// <summary>
    /// Carries MidiExportSelection data.
    /// </summary>
    private sealed record MidiExportSelection(IReadOnlyList<int> Patterns, IReadOnlyList<int> Channels);

    /// <summary>
    /// Carries AudioConversionOptions data.
    /// </summary>
    private sealed record AudioConversionOptions(int SampleRate, int Seconds, int Mp3Bitrate, bool NormalizePeak);

    /// <summary>
    /// Carries RuntimePluginRow data.
    /// </summary>
    private sealed record RuntimePluginRow(string Name, string State, string Version, string Path);

    /// <summary>
    /// Carries FormatSupportRow data.
    /// </summary>
    private sealed record FormatSupportRow(string Type, string Extension, string DisplayName, string Playback, string Engine, string Export, string Notes);

    /// <summary>
    /// Carries one expandable changelog group parsed from CHANGELOG.md.
    /// </summary>
    private sealed class ChangelogGroup(string title)
    {
        /// <summary>Group title shown in the expander header.</summary>
        public string Title { get; } = title;

        /// <summary>Direct bullet changes in this group.</summary>
        public List<string> Items { get; } = [];

        /// <summary>Nested subsection groups below this group.</summary>
        public List<ChangelogGroup> Children { get; } = [];
    }

    /// <summary>
    /// Represents the ExportOption component.
    /// </summary>
    private sealed class ExportOption(string title, string description, Action action)
    {
        /// <summary>
        /// Stores or exposes Title.
        /// </summary>
        public string Title { get; } = title;
        /// <summary>
        /// Stores or exposes Description.
        /// </summary>
        public string Description { get; } = description;
        /// <summary>
        /// Stores or exposes Action.
        /// </summary>
        public Action Action { get; } = action;
    }
}

/// <summary>
/// Represents the SpectrumBandViewModel component.
/// </summary>
public sealed class SpectrumBandViewModel(int index) : BaseViewModel
{
    /// <summary>
    /// Stores or exposes _level.
    /// </summary>
    private double _level;
    /// <summary>
    /// Stores or exposes _peak.
    /// </summary>
    private double _peak;
    /// <summary>
    /// Stores or exposes _peakHold.
    /// </summary>
    private double _peakHold = 0.94;

    /// <summary>
    /// Stores or exposes Index.
    /// </summary>
    public int Index { get; } = index;
    /// <summary>
    /// Stores or exposes FrequencyLabel.
    /// </summary>
    public string FrequencyLabel => Index switch
    {
        0 => "31",
        5 => "80",
        10 => "200",
        15 => "500",
        20 => "1k",
        25 => "2.5k",
        30 => "6k",
        35 => "12k",
        39 => "18k",
        _ => string.Empty
    };

    /// <summary>
    /// Executes the ShowFrequencyLabel operation.
    /// </summary>
    public bool ShowFrequencyLabel => !string.IsNullOrEmpty(FrequencyLabel);

    /// <summary>
    /// Stores or exposes PeakHold.
    /// </summary>
    public double PeakHold
    {
        get => _peakHold;
        set => _peakHold = Math.Clamp(value, 0.70, 0.995);
    }

    /// <summary>
    /// Stores or exposes Level.
    /// </summary>
    public double Level
    {
        get => _level;
        set
        {
            double clamped = Math.Clamp(value, 0d, 1d);
            if (SetField(ref _level, clamped))
            {
                _peak = Math.Max(clamped, _peak * _peakHold);
                OnPropertyChanged(nameof(BarHeight));
                OnPropertyChanged(nameof(CompactBarHeight));
                OnPropertyChanged(nameof(AnalyzerBarHeight));
                OnPropertyChanged(nameof(GlowHeight));
                OnPropertyChanged(nameof(AnalyzerGlowHeight));
                OnPropertyChanged(nameof(PeakHeight));
                OnPropertyChanged(nameof(CompactPeakHeight));
                OnPropertyChanged(nameof(AnalyzerPeakHeight));
                OnPropertyChanged(nameof(GlowOpacity));
                OnPropertyChanged(nameof(BarBrush));
            }
        }
    }

    /// <summary>
    /// Stores or exposes BarHeight.
    /// </summary>
    public double BarHeight => 4 + _level * 91;

    /// <summary>
    /// Stores or exposes CompactBarHeight.
    /// </summary>
    public double CompactBarHeight => 2 + _level * 20;

    /// <summary>
    /// Stores or exposes AnalyzerBarHeight.
    /// </summary>
    public double AnalyzerBarHeight => 5 + _level * 250;

    /// <summary>
    /// Stores or exposes GlowHeight.
    /// </summary>
    public double GlowHeight => 10 + _level * 91;

    /// <summary>
    /// Stores or exposes AnalyzerGlowHeight.
    /// </summary>
    public double AnalyzerGlowHeight => 12 + _level * 248;

    /// <summary>
    /// Executes the PeakHeight operation.
    /// </summary>
    public double PeakHeight => Math.Clamp(3 + _peak * 91, 0, 96);

    /// <summary>
    /// Executes the CompactPeakHeight operation.
    /// </summary>
    public double CompactPeakHeight => Math.Clamp(2 + _peak * 20, 0, 22);

    /// <summary>
    /// Executes the AnalyzerPeakHeight operation.
    /// </summary>
    public double AnalyzerPeakHeight => Math.Clamp(3 + _peak * 250, 0, 260);

    /// <summary>
    /// Stores or exposes GlowOpacity.
    /// </summary>
    public double GlowOpacity => 0.08 + _level * 0.46;

    /// <summary>
    /// Stores or exposes BarBrush.
    /// </summary>
    public Brush BarBrush
    {
        get
        {
            Color top = Index switch
            {
                < 8 => Color.FromRgb(0xB6, 0xFF, 0xC8),
                < 18 => Color.FromRgb(0x7F, 0xE8, 0xFF),
                < 30 => Color.FromRgb(0xBE, 0xA8, 0xFF),
                _ => Color.FromRgb(0xFF, 0xAA, 0xE1)
            };
            Color bottom = Index switch
            {
                < 8 => Color.FromRgb(0x1B, 0x8D, 0x53),
                < 18 => Color.FromRgb(0x12, 0x69, 0xC8),
                < 30 => Color.FromRgb(0x5D, 0x36, 0xC7),
                _ => Color.FromRgb(0xB7, 0x27, 0x8C)
            };
            return new LinearGradientBrush(top, bottom, 90);
        }
    }

    /// <summary>
    /// Executes the Reset operation.
    /// </summary>
    public void Reset()
    {
        _level = 0;
        _peak = 0;
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(BarHeight));
        OnPropertyChanged(nameof(CompactBarHeight));
        OnPropertyChanged(nameof(AnalyzerBarHeight));
        OnPropertyChanged(nameof(GlowHeight));
        OnPropertyChanged(nameof(AnalyzerGlowHeight));
        OnPropertyChanged(nameof(PeakHeight));
        OnPropertyChanged(nameof(CompactPeakHeight));
        OnPropertyChanged(nameof(AnalyzerPeakHeight));
        OnPropertyChanged(nameof(GlowOpacity));
        OnPropertyChanged(nameof(BarBrush));
    }
}
