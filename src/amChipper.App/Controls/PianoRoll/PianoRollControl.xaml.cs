using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.PianoRoll;

/// <summary>
/// Represents the PianoRollControl component.
/// </summary>
public partial class PianoRollControl : UserControl
{
    /// <summary>
    /// Stores or exposes _vm.
    /// </summary>
    private PianoRollViewModel? _vm;

    public PianoRollControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Executes the OnLoaded operation.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateQuantise();
    }

    /// <summary>
    /// Executes the OnDataContextChanged operation.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as PianoRollViewModel;
        if (_vm is null) return;

        NoteGrid.ViewModel = _vm;
        PianoKeys.ViewModel = _vm;
        VelocityPanel.ViewModel = _vm;

        _vm.NoteLayoutChanged += (_, _) =>
        {
            NoteGrid.InvalidateVisual();
            PianoKeys.InvalidateVisual();
            VelocityPanel.InvalidateVisual();
        };
        _vm.PlayheadMoved += (_, _) =>
        {
            EnsurePlayheadVisible();
            NoteGrid.InvalidateVisual();
            VelocityPanel.InvalidateVisual();
        };

        // Sync scroll: piano keys mirrors the vertical scroll of the note grid
        NoteScroll.ScrollToVerticalOffset(_vm.ScrollPitch * _vm.RowHeight);
    }

    // ── Toolbar event handlers ────────────────────────────────────────────────

    /// <summary>
    /// Executes the Tool_Draw operation.
    /// </summary>
    private void Tool_Draw(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = PianoRollTool.Draw;
        BtnSelect.IsChecked = false;
        BtnErase.IsChecked = false;
    }

    /// <summary>
    /// Executes the Tool_Select operation.
    /// </summary>
    private void Tool_Select(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = PianoRollTool.Select;
        BtnDraw.IsChecked = false;
        BtnErase.IsChecked = false;
    }

    /// <summary>
    /// Executes the Tool_Erase operation.
    /// </summary>
    private void Tool_Erase(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.CurrentTool = PianoRollTool.Erase;
        BtnDraw.IsChecked = false;
        BtnSelect.IsChecked = false;
    }

    /// <summary>
    /// Executes the PopulateQuantise operation.
    /// </summary>
    private void PopulateQuantise()
    {
        if (_vm is null) return;
        foreach (var (label, _) in _vm.QuantiseOptions)
            CmbQuantise.Items.Add(label);
        CmbQuantise.SelectedIndex = 2; // 1/4 default
    }

    /// <summary>
    /// Executes the CmbQuantise_SelectionChanged operation.
    /// </summary>
    private void CmbQuantise_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        int idx = CmbQuantise.SelectedIndex;
        if (idx >= 0 && idx < _vm.QuantiseOptions.Count)
            _vm.Quantise = _vm.QuantiseOptions[idx].Value;
    }

    /// <summary>
    /// Executes the ZoomInH operation.
    /// </summary>
    private void ZoomInH(object sender, RoutedEventArgs e) => _vm?.ZoomInHCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomOutH operation.
    /// </summary>
    private void ZoomOutH(object sender, RoutedEventArgs e) => _vm?.ZoomOutHCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomInV operation.
    /// </summary>
    private void ZoomInV(object sender, RoutedEventArgs e) => _vm?.ZoomInVCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomOutV operation.
    /// </summary>
    private void ZoomOutV(object sender, RoutedEventArgs e) => _vm?.ZoomOutVCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomReset operation.
    /// </summary>
    private void ZoomReset(object sender, RoutedEventArgs e) => _vm?.ZoomResetCommand.Execute(null);
    /// <summary>
    /// Executes the ZoomFit operation.
    /// </summary>
    private void ZoomFit(object sender, RoutedEventArgs e) => _vm?.ZoomFitCommand.Execute(NoteScroll.ViewportWidth);

    /// <summary>
    /// Executes the NoteScroll_ScrollChanged operation.
    /// </summary>
    private void NoteScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ScrollBeat = e.HorizontalOffset / _vm.PixelsPerBeat;
        _vm.ScrollPitch = e.VerticalOffset / _vm.RowHeight;
        // Sync piano key strip
        PianoKeys.ScrollOffset = e.VerticalOffset;
        PianoKeys.InvalidateVisual();
    }

    /// <summary>
    /// Executes the NoteScroll_PreviewMouseWheel operation.
    /// </summary>
    private void NoteScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Delta > 0)
                _vm.ZoomInVCommand.Execute(null);
            else
                _vm.ZoomOutVCommand.Execute(null);
        }
        else
        {
            if (e.Delta > 0)
                _vm.ZoomInHCommand.Execute(null);
            else
                _vm.ZoomOutHCommand.Execute(null);
        }

        NoteGrid.InvalidateVisual();
        PianoKeys.InvalidateVisual();
        VelocityPanel.InvalidateVisual();
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
        double left = NoteScroll.HorizontalOffset;
        double right = left + NoteScroll.ViewportWidth;
        double margin = Math.Max(_vm.PixelsPerBeat * 4, 120);

        if (x < left + margin)
            NoteScroll.ScrollToHorizontalOffset(Math.Max(0, x - margin));
        else if (x > right - margin)
            NoteScroll.ScrollToHorizontalOffset(Math.Max(0, x - NoteScroll.ViewportWidth + margin));
    }
}
