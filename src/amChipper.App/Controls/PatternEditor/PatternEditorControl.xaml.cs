using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.PatternEditor;

/// <summary>
/// Represents the PatternEditorControl component.
/// </summary>
public partial class PatternEditorControl : UserControl
{
    /// <summary>
    /// Stores or exposes _vm.
    /// </summary>
    private PatternEditorViewModel? _vm;

    // Piano keyboard mapping (QWERTY → MIDI notes, base octave C4=60)
    private static readonly Dictionary<Key, int> KeyToSemitone = new()
    {
        { Key.Z, 0 },  { Key.S, 1 },  { Key.X, 2 },  { Key.D, 3 },
        { Key.C, 4 },  { Key.V, 5 },  { Key.G, 6 },  { Key.B, 7 },
        { Key.H, 8 },  { Key.N, 9 },  { Key.J, 10 }, { Key.M, 11 },
        { Key.Q, 12 }, { Key.D2, 13 },{ Key.W, 14 }, { Key.D3, 15 },
        { Key.E, 16 }, { Key.R, 17 }, { Key.D5, 18 },{ Key.T, 19 },
        { Key.D6, 20 },{ Key.Y, 21 }, { Key.D7, 22 },{ Key.U, 23 },
    };

    /// <summary>
    /// Stores or exposes _baseOctave.
    /// </summary>
    private int _baseOctave = 4;

    public PatternEditorControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Ensure the canvas redraws once this tab is first laid out and whenever
        // it becomes visible again (e.g. user switches back to this tab).
        Loaded += (_, _) => GridCanvas.InvalidateVisual();
        IsVisibleChanged += (_, _) => { if (IsVisible) GridCanvas.InvalidateVisual(); };
    }

    /// <summary>
    /// Executes the OnDataContextChanged operation.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.HighlightChanged -= OnHighlightChanged;

        _vm = DataContext as PatternEditorViewModel;

        if (_vm is not null)
        {
            GridCanvas.ViewModel = _vm;
            _vm.HighlightChanged += OnHighlightChanged;
            // Force an immediate draw with the already-populated Rows list.
            GridCanvas.InvalidateVisual();
        }
    }

    /// <summary>
    /// Called whenever the cursor row changes (user edit or playback tracking).
    /// Keeps the grid scrolled so the cursor stays visible.
    /// </summary>
    private void OnHighlightChanged(object? sender, EventArgs e)
    {
        GridCanvas.CentreOnCurrentRow();
    }

    /// <summary>
    /// Executes the OnKeyDown operation.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;

        // Navigation
        switch (e.Key)
        {
            case Key.Up: _vm.MoveUpCommand.Execute(null); e.Handled = true; return;
            case Key.Down: _vm.MoveDownCommand.Execute(null); e.Handled = true; return;
            case Key.Left: _vm.MoveLeftCommand.Execute(null); e.Handled = true; return;
            case Key.Right: _vm.MoveRightCommand.Execute(null); e.Handled = true; return;
            case Key.Delete: _vm.ClearCellCommand.Execute(null); e.Handled = true; return;
        }

        // Octave shift
        if (e.Key == Key.OemOpenBrackets) { _baseOctave = Math.Max(0, _baseOctave - 1); return; }
        if (e.Key == Key.OemCloseBrackets) { _baseOctave = Math.Min(8, _baseOctave + 1); return; }

        // Note entry
        if (KeyToSemitone.TryGetValue(e.Key, out int semitone))
        {
            int pitch = (_baseOctave + 1) * 12 + semitone;
            if (pitch is >= 0 and <= 127)
            {
                _vm.EnterNote((byte)pitch);
                GridCanvas.InvalidateVisual();
            }
            e.Handled = true;
        }
    }
}
