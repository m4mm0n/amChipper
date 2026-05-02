using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.SongEditor;

/// <summary>
/// Represents the SongEditorControl component.
/// </summary>
public partial class SongEditorControl : UserControl
{
    /// <summary>
    /// Stores or exposes _vm.
    /// </summary>
    private SongEditorViewModel? _vm;
    /// <summary>
    /// Stores or exposes _clipHistoryOpen.
    /// </summary>
    private bool _clipHistoryOpen;

    public SongEditorControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Make sure canvases redraw once the control is actually in the visual tree.
        // This handles the case where SetSong / LayoutChanged fires before WPF has
        // finished the first layout pass (which happens when this tab is selected first).
        Loaded += (_, _) => RefreshCanvases();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshCanvases(); };
    }

    /// <summary>
    /// Executes the RefreshCanvases operation.
    /// </summary>
    private void RefreshCanvases()
    {
        if (_vm is not null)
        {
            double contentHeight = 20 + Math.Max(1, _vm.Tracks.Count) * _vm.TrackHeight;
            Timeline.MinHeight = contentHeight;
            TrackHeaders.MinHeight = contentHeight;
            double visibleBeats = Math.Max(_vm.TotalTimelineBeats, _vm.PlayheadBeat + 8);
            Timeline.MinWidth = Math.Max(3000, visibleBeats * _vm.PixelsPerBeat);
        }

        Timeline.InvalidateVisual();
        TrackHeaders.InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnDataContextChanged operation.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Detach old vm handlers
        if (_vm is not null)
        {
            _vm.LayoutChanged -= OnLayoutChanged;
            _vm.PlayheadMoved -= OnPlayheadMoved;
        }

        _vm = DataContext as SongEditorViewModel;
        if (_vm is null) return;

        Timeline.ViewModel = _vm;
        TrackHeaders.ViewModel = _vm;

        _vm.LayoutChanged += OnLayoutChanged;
        _vm.PlayheadMoved += OnPlayheadMoved;

        // Force an immediate redraw with the new ViewModel data.
        RefreshCanvases();
    }

    /// <summary>
    /// Executes the OnLayoutChanged operation.
    /// </summary>
    private void OnLayoutChanged(object? s, EventArgs e) => RefreshCanvases();
    /// <summary>
    /// Executes the OnPlayheadMoved operation.
    /// </summary>
    private void OnPlayheadMoved(object? s, EventArgs e)
    {
        EnsurePlayheadVisible();
        Timeline.InvalidateVisual();
    }

    /// <summary>
    /// Executes the Tool_Draw operation.
    /// </summary>
    private void Tool_Draw(object s, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = SongEditorTool.Draw;
        BtnSelect.IsChecked = false; BtnErase.IsChecked = false;
    }
    /// <summary>
    /// Executes the Tool_Select operation.
    /// </summary>
    private void Tool_Select(object s, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = SongEditorTool.Select;
        BtnDraw.IsChecked = false; BtnErase.IsChecked = false;
    }
    /// <summary>
    /// Executes the Tool_Erase operation.
    /// </summary>
    private void Tool_Erase(object s, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = SongEditorTool.Erase;
        BtnDraw.IsChecked = false; BtnSelect.IsChecked = false;
    }

    /// <summary>
    /// Executes the ZoomIn operation.
    /// </summary>
    private void ZoomIn(object s, RoutedEventArgs e) => _vm?.ZoomInCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomOut operation.
    /// </summary>
    private void ZoomOut(object s, RoutedEventArgs e) => _vm?.ZoomOutCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomReset operation.
    /// </summary>
    private void ZoomReset(object s, RoutedEventArgs e) => _vm?.ZoomResetCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomFit operation.
    /// </summary>
    private void ZoomFit(object s, RoutedEventArgs e) => _vm?.ZoomFitCommand.Execute(TimelineScroll.ViewportWidth);

    /// <summary>
    /// Executes the SpectrumPreview_MouseLeftButtonUp operation.
    /// </summary>
    private void SpectrumPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window && window.DataContext is MainViewModel main)
        {
            main.CycleSpectrumAnalyzerMode();
            window.SelectAnalyzerTab();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Executes the ClipSlider_PreviewMouseLeftButtonDown operation.
    /// </summary>
    private void ClipSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || !_vm.HasSelectedBlock || _clipHistoryOpen)
            return;

        _clipHistoryOpen = true;
        _vm.BeginHistory("Edit clip properties");
    }

    /// <summary>
    /// Executes the ClipSlider_PreviewMouseLeftButtonUp operation.
    /// </summary>
    private void ClipSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || !_clipHistoryOpen)
            return;

        _clipHistoryOpen = false;
        _vm.CommitHistory();
    }

    /// <summary>
    /// Executes the TimelineScroll_Changed operation.
    /// </summary>
    private void TimelineScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ScrollBeat = e.HorizontalOffset / _vm.PixelsPerBeat;
        TrackHeaders.ScrollOffset = e.VerticalOffset;
        TrackHeaders.InvalidateVisual();
    }

    /// <summary>
    /// Executes the TimelineScroll_PreviewMouseWheel operation.
    /// </summary>
    private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Delta > 0)
            _vm.ZoomInCommand.Execute(null);
        else
            _vm.ZoomOutCommand.Execute(null);

        RefreshCanvases();
        e.Handled = true;
    }

    /// <summary>
    /// Executes the EnsurePlayheadVisible operation.
    /// </summary>
    private void EnsurePlayheadVisible()
    {
        if (_vm is null)
            return;

        double x = _vm.PlayheadBeat * _vm.PixelsPerBeat;
        Timeline.MinWidth = Math.Max(Timeline.MinWidth, (_vm.PlayheadBeat + 8) * _vm.PixelsPerBeat);
        double left = TimelineScroll.HorizontalOffset;
        double right = left + TimelineScroll.ViewportWidth;
        double margin = Math.Max(_vm.PixelsPerBeat * 4, 120);

        if (x < left + margin)
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - margin));
        else if (x > right - margin)
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - TimelineScroll.ViewportWidth + margin));
    }
}
