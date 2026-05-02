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
