using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Reflection;
using System.Runtime.InteropServices;
using amChipper.App.Services;
using amChipper.App.ViewModels;

namespace amChipper.App;

/// <summary>
/// Represents the MainWindow component.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int SystemMenuAboutId = 0x1F10;
    /// <summary>
    /// Stores or exposes uint.
    /// </summary>
    private const uint MfSeparator = 0x00000800;
    /// <summary>
    /// Stores or exposes uint.
    /// </summary>
    private const uint MfString = 0x00000000;

    /// <summary>
    /// Stores or exposes _vm.
    /// </summary>
    private readonly MainViewModel _vm;
    /// <summary>
    /// Stores or exposes _mixerEditSlider.
    /// </summary>
    private Slider? _mixerEditSlider;
    /// <summary>
    /// Stores or exposes _mixerEditStartValue.
    /// </summary>
    private double _mixerEditStartValue;
    /// <summary>
    /// Stores or exposes _mixerHistoryOpen.
    /// </summary>
    private bool _mixerHistoryOpen;

    public MainWindow()
    {
        InitializeComponent();
        WindowChromeTheme.Attach(this);
        _vm = new MainViewModel();
        DataContext = _vm;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplyPersistedLayout();
            Dispatcher.BeginInvoke(new Action(() => _vm.ShowStartupTipIfEnabled()));
        };
    }

    /// <summary>
    /// Executes the OnClosed operation.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        CapturePersistedLayout();
        _vm?.SaveConfigurationOnExit();
        _vm?.Audio?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Applies persisted workspace pane sizes after XAML has loaded.
    /// </summary>
    private void ApplyPersistedLayout()
    {
        LeftPanelColumn.Width = new GridLength(_vm.MainLeftPanelWidth, GridUnitType.Pixel);
    }

    /// <summary>
    /// Captures current workspace pane sizes into the ViewModel configuration state.
    /// </summary>
    private void CapturePersistedLayout()
    {
        _vm.MainLeftPanelWidth = LeftPanelColumn.ActualWidth;
    }

    /// <summary>
    /// Saves the left browser pane width when the user finishes dragging the main splitter.
    /// </summary>
    private void MainSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        CapturePersistedLayout();
        _vm.SaveConfigurationOnExit();
    }

    /// <summary>
    /// Executes the OnSourceInitialized operation.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WindowProc);

        nint menu = GetSystemMenu(helper.Handle, false);
        if (menu != 0)
        {
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            string aboutText = (_vm["About"] ?? "About amChipper").Replace("_", string.Empty, StringComparison.Ordinal);
            AppendMenu(menu, MfString, SystemMenuAboutId, $"{aboutText}...");
        }
    }

    /// <summary>
    /// Executes the WindowProc operation.
    /// </summary>
    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int wmSysCommand = 0x0112;
        if (msg == wmSysCommand && (wParam.ToInt32() & 0xFFF0) == SystemMenuAboutId)
        {
            _vm.ShowAboutCommand.Execute(null);
            handled = true;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    /// <summary>
    /// Executes the GetSystemMenu operation.
    /// </summary>
    private static extern nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>
    /// Executes the AppendMenu operation.
    /// </summary>
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);

    // ── Menu handlers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the MenuItem_Exit operation.
    /// </summary>
    private void MenuItem_Exit(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Executes the View_ProjectHub operation.
    /// </summary>
    private void View_ProjectHub(object sender, RoutedEventArgs e) =>
        MainTabControl.SelectedItem = ProjectHubTab;

    /// <summary>
    /// Executes the View_SongEditor operation.
    /// </summary>
    private void View_SongEditor(object sender, RoutedEventArgs e) =>
        MainTabControl.SelectedItem = SongEditorTab;

    /// <summary>
    /// Executes the View_PianoRoll operation.
    /// </summary>
    private void View_PianoRoll(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = PianoRollTab;
        int patternIndex = Math.Clamp(_vm.SongEditor.SelectedPatternIndex, 0, Math.Max(_vm.Song.Patterns.Count - 1, 0));
        if (_vm.PianoRoll.CurrentPatternIndex != patternIndex)
            _vm.PianoRoll.SetCurrentPattern(patternIndex);
    }

    /// <summary>
    /// Executes the View_PatternEditor operation.
    /// </summary>
    private void View_PatternEditor(object sender, RoutedEventArgs e) =>
        MainTabControl.SelectedItem = PatternEditorTab;

    /// <summary>
    /// Executes the View_Analyzer operation.
    /// </summary>
    private void View_Analyzer(object sender, RoutedEventArgs e) =>
        MainTabControl.SelectedItem = AnalyzerTab;

    /// <summary>
    /// Executes the SelectAnalyzerTab operation.
    /// </summary>
    public void SelectAnalyzerTab() =>
        MainTabControl.SelectedItem = AnalyzerTab;

    /// <summary>
    /// Executes the SpectrumPreview_MouseLeftButtonUp operation.
    /// </summary>
    private void SpectrumPreview_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm.CycleSpectrumAnalyzerMode();
        MainTabControl.SelectedItem = AnalyzerTab;
    }

    /// <summary>
    /// Executes the SpectrumPreview_MouseLeftButtonUp operation.
    /// </summary>
    private void SpectrumPreview_MouseLeftButtonUp(object sender, RoutedEventArgs e)
    {
        _vm.CycleSpectrumAnalyzerMode();
        MainTabControl.SelectedItem = AnalyzerTab;
    }

    /// <summary>
    /// Executes the MixerSlider_PreviewMouseLeftButtonDown operation.
    /// </summary>
    private void MixerSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_mixerHistoryOpen || sender is not Slider slider)
            return;

        _mixerEditSlider = slider;
        _mixerEditStartValue = slider.Value;
        _mixerHistoryOpen = true;
        _vm.BeginHistory("Adjust mixer");
    }

    /// <summary>
    /// Executes the MixerSlider_PreviewMouseLeftButtonUp operation.
    /// </summary>
    private void MixerSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_mixerHistoryOpen || sender is not Slider slider || !ReferenceEquals(slider, _mixerEditSlider))
            return;

        bool changed = Math.Abs(slider.Value - _mixerEditStartValue) > 0.0001;
        if (changed)
            _vm.CommitHistory();
        else
            _vm.CancelHistory();

        _mixerEditSlider = null;
        _mixerHistoryOpen = false;
    }
}
