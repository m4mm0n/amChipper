using System.IO;
using System.Text.Json;

namespace amChipper.App.Services;

/// <summary>
/// Represents the AppConfiguration component.
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>
    /// Stores or exposes SelectedTheme.
    /// </summary>
    public string SelectedTheme { get; set; } = "FL Grape";
    /// <summary>
    /// Stores or exposes ShowUiShine.
    /// </summary>
    public bool ShowUiShine { get; set; } = true;
    /// <summary>
    /// Stores or exposes ShowPanelShadows.
    /// </summary>
    public bool ShowPanelShadows { get; set; } = true;
    /// <summary>
    /// Stores or exposes WorkspaceDensity.
    /// </summary>
    public string WorkspaceDensity { get; set; } = "Balanced";
    /// <summary>
    /// Stores or exposes ToolbarButtonSize.
    /// </summary>
    public string ToolbarButtonSize { get; set; } = "Balanced";
    /// <summary>
    /// Width of the main instrument/browser pane in pixels.
    /// </summary>
    public double MainLeftPanelWidth { get; set; } = 200;
    /// <summary>
    /// User-defined order for the main editor/rack tabs.
    /// </summary>
    public string[] MainTabOrder { get; set; } = [];
    /// <summary>
    /// Stores or exposes Language.
    /// </summary>
    public string Language { get; set; } = "English";
    /// <summary>
    /// Stores or exposes ShowTipsOnStartup.
    /// </summary>
    public bool ShowTipsOnStartup { get; set; } = true;
    /// <summary>
    /// Stores or exposes LastTipIndex.
    /// </summary>
    public int LastTipIndex { get; set; }
    /// <summary>
    /// Stores or exposes ShowOldschoolAboutEffects.
    /// </summary>
    public bool ShowOldschoolAboutEffects { get; set; } = true;
    /// <summary>
    /// Stores or exposes AutoSaveConfigurationOnExit.
    /// </summary>
    public bool AutoSaveConfigurationOnExit { get; set; } = true;
    /// <summary>
    /// Stores or exposes ToolTipInitialDelayMs.
    /// </summary>
    public int ToolTipInitialDelayMs { get; set; } = 450;
    /// <summary>
    /// Stores or exposes ToolTipDurationMs.
    /// </summary>
    public int ToolTipDurationMs { get; set; } = 15000;
    /// <summary>
    /// Stores or exposes HelpTextScale.
    /// </summary>
    public double HelpTextScale { get; set; } = 1.0;
    /// <summary>
    /// Stores or exposes PreferRestartOnStop.
    /// </summary>
    public bool PreferRestartOnStop { get; set; } = true;
    /// <summary>
    /// Stores or exposes SoloSelectedPianoRollChannel.
    /// </summary>
    public bool SoloSelectedPianoRollChannel { get; set; } = true;
    /// <summary>
    /// Stores or exposes AutoOpenPianoRollOnClipSelect.
    /// </summary>
    public bool AutoOpenPianoRollOnClipSelect { get; set; } = true;
    /// <summary>
    /// Stores or exposes NotePreviewMode.
    /// </summary>
    public string NotePreviewMode { get; set; } = "Hold While Pressed";
    /// <summary>
    /// Enables FL Studio-style typing-keyboard note preview in the piano roll.
    /// </summary>
    public bool PianoRollTypingKeyboardEnabled { get; set; } = true;
    /// <summary>
    /// Base MIDI note for the lower typing-keyboard row.
    /// </summary>
    public int PianoRollTypingKeyboardBaseNote { get; set; } = 48;
    /// <summary>
    /// Velocity used by piano-roll typing-keyboard preview notes.
    /// </summary>
    public int PianoRollTypingKeyboardVelocity { get; set; } = 110;
    /// <summary>
    /// Stores or exposes MidiExportPatternDefault.
    /// </summary>
    public string MidiExportPatternDefault { get; set; } = "Current Pattern";
    /// <summary>
    /// Stores or exposes ShowAdvancedMixerReadouts.
    /// </summary>
    public bool ShowAdvancedMixerReadouts { get; set; } = true;
    /// <summary>
    /// Stores or exposes VisualizerIntensity.
    /// </summary>
    public double VisualizerIntensity { get; set; } = 1.0;
    /// <summary>
    /// Stores or exposes VisualizerPeakHold.
    /// </summary>
    public double VisualizerPeakHold { get; set; } = 0.94;
    /// <summary>
    /// Stores or exposes SpectrumAnalyzerMode.
    /// </summary>
    public string SpectrumAnalyzerMode { get; set; } = "Studio Analyzer";
    /// <summary>
    /// Stores or exposes ShowModuleDiagnostics.
    /// </summary>
    public bool ShowModuleDiagnostics { get; set; } = true;
    /// <summary>
    /// Stores or exposes ConfirmNativeExportLimitations.
    /// </summary>
    public bool ConfirmNativeExportLimitations { get; set; } = true;
    /// <summary>
    /// Stores or exposes PreferSongLengthDatabase.
    /// </summary>
    public bool PreferSongLengthDatabase { get; set; } = true;
    /// <summary>
    /// Stores or exposes SidXmExportMode.
    /// </summary>
    public string SidXmExportMode { get; set; } = "Rendered Mix Only";
    /// <summary>
    /// Stores or exposes ChipRenderTailSeconds.
    /// </summary>
    public int ChipRenderTailSeconds { get; set; }
    /// <summary>
    /// Stores or exposes AudioOutputDevice.
    /// </summary>
    public string AudioOutputDevice { get; set; } = "Default WaveOut";
    /// <summary>
    /// Stores or exposes AudioSampleRate.
    /// </summary>
    public int AudioSampleRate { get; set; } = 44100;
    /// <summary>
    /// Stores or exposes AudioLatencyMs.
    /// </summary>
    public int AudioLatencyMs { get; set; } = 200;
    /// <summary>
    /// Stores or exposes AudioBufferCount.
    /// </summary>
    public int AudioBufferCount { get; set; } = 4;
    /// <summary>
    /// Stores or exposes MidiOutputDevice.
    /// </summary>
    public string MidiOutputDevice { get; set; } = "Microsoft GS Wavetable Synth";
    /// <summary>
    /// Stores or exposes MidiInputMode.
    /// </summary>
    public string MidiInputMode { get; set; } = "Disabled";
    /// <summary>
    /// Stores or exposes MidiSynchronization.
    /// </summary>
    public string MidiSynchronization { get; set; } = "MIDI clock";
    /// <summary>
    /// Stores or exposes MidiMasterSync.
    /// </summary>
    public bool MidiMasterSync { get; set; }
    /// <summary>
    /// Stores or exposes MidiSendAllNotesOff.
    /// </summary>
    public bool MidiSendAllNotesOff { get; set; } = true;
    /// <summary>
    /// Stores or exposes MidiAutoAcceptController.
    /// </summary>
    public bool MidiAutoAcceptController { get; set; } = true;
    /// <summary>
    /// Stores or exposes MidiOmniPreviewChannel.
    /// </summary>
    public int MidiOmniPreviewChannel { get; set; } = 1;
    /// <summary>
    /// Stores or exposes MidiMasterSyncOffsetMs.
    /// </summary>
    public int MidiMasterSyncOffsetMs { get; set; }
    /// <summary>
    /// Stores or exposes AudioAutoClose.
    /// </summary>
    public bool AudioAutoClose { get; set; }
    /// <summary>
    /// Stores or exposes AudioPriority.
    /// </summary>
    public string AudioPriority { get; set; } = "Highest";
    /// <summary>
    /// Stores or exposes AudioResamplingQuality.
    /// </summary>
    public string AudioResamplingQuality { get; set; } = "24-point sinc";
    /// <summary>
    /// Stores or exposes AudioPlaybackTracking.
    /// </summary>
    public string AudioPlaybackTracking { get; set; } = "Mixer";
    /// <summary>
    /// Stores or exposes AudioSafeOverloads.
    /// </summary>
    public bool AudioSafeOverloads { get; set; } = true;
    /// <summary>
    /// Stores or exposes AudioResetPluginsOnTransport.
    /// </summary>
    public bool AudioResetPluginsOnTransport { get; set; }
    /// <summary>
    /// Stores or exposes GeneralUndoLevels.
    /// </summary>
    public int GeneralUndoLevels { get; set; } = 100;
    /// <summary>
    /// Stores or exposes GeneralUndoKnobTweaks.
    /// </summary>
    public bool GeneralUndoKnobTweaks { get; set; } = true;
    /// <summary>
    /// Stores or exposes GeneralShowRecentChanges.
    /// </summary>
    public bool GeneralShowRecentChanges { get; set; }
    /// <summary>
    /// Stores or exposes GeneralProjectWarningMb.
    /// </summary>
    public int GeneralProjectWarningMb { get; set; } = 100;
    /// <summary>
    /// Stores or exposes GeneralSilentStartup.
    /// </summary>
    public bool GeneralSilentStartup { get; set; }
    /// <summary>
    /// Stores or exposes GeneralRestorePreviousState.
    /// </summary>
    public bool GeneralRestorePreviousState { get; set; }
    /// <summary>
    /// Stores or exposes GeneralHighPerformancePowerPlan.
    /// </summary>
    public bool GeneralHighPerformancePowerPlan { get; set; } = true;
    /// <summary>
    /// Stores or exposes FileAutosaveMode.
    /// </summary>
    public string FileAutosaveMode { get; set; } = "Occasionally (every 10 minutes)";
    /// <summary>
    /// Stores or exposes FileBackupLocationMode.
    /// </summary>
    public string FileBackupLocationMode { get; set; } = "Project data folder when available";
    /// <summary>
    /// Stores or exposes UserDataFolder.
    /// </summary>
    public string UserDataFolder { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes BrowserSearchFolders.
    /// </summary>
    public string[] BrowserSearchFolders { get; set; } = [];
    /// <summary>
    /// Stores or exposes ExternalToolPath.
    /// </summary>
    public string ExternalToolPath { get; set; } = @"C:\Windows\System32\Notepad.exe";
    /// <summary>
    /// Stores or exposes ThemeGuiScaling.
    /// </summary>
    public string ThemeGuiScaling { get; set; } = "System";
    /// <summary>
    /// Stores or exposes ThemePopupScaling.
    /// </summary>
    public string ThemePopupScaling { get; set; } = "Main";
    /// <summary>
    /// Stores or exposes ThemeToolbarScaling.
    /// </summary>
    public string ThemeToolbarScaling { get; set; } = "Main";
    /// <summary>
    /// Stores or exposes ThemeAnimationMode.
    /// </summary>
    public string ThemeAnimationMode { get; set; } = "Ultrasmooth";
    /// <summary>
    /// Stores or exposes ThemeTransparentWindows.
    /// </summary>
    public bool ThemeTransparentWindows { get; set; } = true;
    /// <summary>
    /// Stores or exposes ThemeHighVisibility.
    /// </summary>
    public bool ThemeHighVisibility { get; set; }
    /// <summary>
    /// Stores or exposes ThemeSmallScrollbars.
    /// </summary>
    public bool ThemeSmallScrollbars { get; set; }
    /// <summary>
    /// Stores or exposes ThemeColorMap.
    /// </summary>
    public string ThemeColorMap { get; set; } = "Spectrum";
    /// <summary>
    /// Stores or exposes ProjectDefaultTemplate.
    /// </summary>
    public string ProjectDefaultTemplate { get; set; } = "Hardstyle_2025";
    /// <summary>
    /// Stores or exposes ProjectStartupProject.
    /// </summary>
    public string ProjectStartupProject { get; set; } = "Default template";
    /// <summary>
    /// Stores or exposes ProjectAutoNameChannels.
    /// </summary>
    public bool ProjectAutoNameChannels { get; set; } = true;
    /// <summary>
    /// Stores or exposes ProjectAutoSelectLinkedModules.
    /// </summary>
    public bool ProjectAutoSelectLinkedModules { get; set; } = true;
    /// <summary>
    /// Stores or exposes ProjectAutoZoomPianoRoll.
    /// </summary>
    public bool ProjectAutoZoomPianoRoll { get; set; }
    /// <summary>
    /// Stores or exposes ProjectCreateAutomationAtPlaybackPosition.
    /// </summary>
    public bool ProjectCreateAutomationAtPlaybackPosition { get; set; } = true;
    /// <summary>
    /// Stores or exposes ProjectSelectFirstNoteChannel.
    /// </summary>
    public bool ProjectSelectFirstNoteChannel { get; set; }
    /// <summary>
    /// Stores or exposes VerboseLogging.
    /// </summary>
    public bool VerboseLogging { get; set; }
    /// <summary>
    /// Stores or exposes LogDependencyLoadDetails.
    /// </summary>
    public bool LogDependencyLoadDetails { get; set; } = true;
    /// <summary>
    /// Stores or exposes LogDirectory.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;
}

/// <summary>
/// Represents the AppConfigurationStore component.
/// </summary>
public static class AppConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Stores or exposes ConfigurationDirectory.
    /// </summary>
    public static string ConfigurationDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "amChipper");

    /// <summary>
    /// Executes the DefaultPath operation.
    /// </summary>
    public static string DefaultPath { get; } = Path.Combine(ConfigurationDirectory, "settings.json");

    /// <summary>
    /// Executes the Load operation.
    /// </summary>
    public static AppConfiguration Load(string? path = null)
    {
        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(target))
            return new AppConfiguration();

        string json = File.ReadAllText(target);
        return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? new AppConfiguration();
    }

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(AppConfiguration configuration, string? path = null)
    {
        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        string? directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(target, json);
    }
}
