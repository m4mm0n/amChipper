using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.Services;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.PianoRoll;

/// <summary>
/// Velocity editor strip shown below the note grid.
/// Each note's velocity is represented as a vertical bar whose height = velocity/127.
/// Click / drag a bar to change the velocity.
/// </summary>
public sealed class VelocityCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public PianoRollViewModel? ViewModel { get; set; }

    /// <summary>
    /// Stores or exposes _draggingNote.
    /// </summary>
    private Note? _draggingNote;
    /// <summary>
    /// Stores or exposes _draggingOrigVelocity.
    /// </summary>
    private byte _draggingOrigVelocity;
    /// <summary>
    /// Stores or exposes _historyOpen.
    /// </summary>
    private bool _historyOpen;
    /// <summary>
    /// Executes the BrushBar operation.
    /// </summary>
    private static readonly Brush BrushBar = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5));
    /// <summary>
    /// Executes the BrushBarSel operation.
    /// </summary>
    private static readonly Brush BrushBarSel = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
    /// <summary>
    /// Executes the BrushBg operation.
    /// </summary>
    private static readonly Brush BrushBg = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18));
    private static readonly Pen PenBar = new(new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xA0)), 0.5);

    static VelocityCanvas()
    {
        BrushBar.Freeze(); BrushBarSel.Freeze(); BrushBg.Freeze(); PenBar.Freeze();
    }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        double ppb = ViewModel.PixelsPerBeat;
        double tb = ViewModel.TicksPerBeat;

        dc.DrawRectangle(BrushBg, null, new Rect(0, 0, w, h));

        foreach (var note in ViewModel.Notes)
        {
            double x = ViewModel.BeatToX(note.StartTick / tb);
            double bw = Math.Max(note.DurationTicks / tb * ppb - 2, 3);
            if (x + bw < 0 || x > w) continue;

            double barH = note.Velocity / 127.0 * h;
            bool sel = note == ViewModel.SelectedNote;
            Brush fg = sel ? BrushBarSel : BrushBar;
            dc.DrawRectangle(fg, PenBar, new Rect(x + 1, h - barH, Math.Max(bw, 3), barH));
        }
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        CaptureMouse();
        AppLogger.Debug("[PianoRoll] Velocity drag start");
        _historyOpen = false;
        DragVelocity(e.GetPosition(this));
    }

    /// <summary>
    /// Executes the OnMouseMove operation.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingNote is null || ViewModel is null) return;
        DragVelocity(e.GetPosition(this));
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonUp operation.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool changed = _draggingNote is not null && _draggingNote.Velocity != _draggingOrigVelocity;
        if (changed)
            ViewModel?.CommitNotesToPattern();
        if (_historyOpen)
        {
            if (changed)
                ViewModel?.CommitHistory();
            else
                ViewModel?.CancelHistory();
        }
        AppLogger.Debug("[PianoRoll] Velocity drag end");
        _draggingNote = null;
        _historyOpen = false;
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Executes the DragVelocity operation.
    /// </summary>
    private void DragVelocity(Point pt)
    {
        if (ViewModel is null) return;
        double tb = ViewModel.TicksPerBeat;
        double ppb = ViewModel.PixelsPerBeat;
        double h = ActualHeight;

        // Find note closest to clicked X
        Note? closest = null;
        double minDist = double.MaxValue;
        foreach (var note in ViewModel.Notes)
        {
            double nx = ViewModel.BeatToX(note.StartTick / tb);
            double d = Math.Abs(pt.X - nx);
            if (d < minDist) { minDist = d; closest = note; }
        }

        if (closest is null) return;
        if (_draggingNote is null)
        {
            _draggingNote = closest;
            _draggingOrigVelocity = closest.Velocity;
            ViewModel.BeginHistory("Edit velocity");
            _historyOpen = true;
        }
        byte vel = (byte)Math.Clamp((1.0 - pt.Y / h) * 127, 1, 127);
        ViewModel.SetNoteVelocity(closest, vel);
        ViewModel.SelectedNote = closest;
        InvalidateVisual();
        ViewModel.RaiseNoteLayoutChanged();
    }
}
