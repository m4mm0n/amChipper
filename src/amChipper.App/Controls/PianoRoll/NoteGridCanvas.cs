using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.Services;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.PianoRoll;

/// <summary>
/// Custom-rendered Canvas that draws the piano roll note grid.
/// Handles mouse input for drawing, selecting, and erasing notes.
/// </summary>
public sealed class NoteGridCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public PianoRollViewModel? ViewModel { get; set; }

    // Drag state
    /// <summary>
    /// Stores or exposes _dragging.
    /// </summary>
    private bool _dragging;
    /// <summary>
    /// Stores or exposes _dragNote.
    /// </summary>
    private Note? _dragNote;
    /// <summary>
    /// Stores or exposes _dragStartBeat.
    /// </summary>
    private double _dragStartBeat;
    /// <summary>
    /// Stores or exposes _dragStartPitch.
    /// </summary>
    private int _dragStartPitch;
    /// <summary>
    /// Stores or exposes _dragNoteOrigStart.
    /// </summary>
    private double _dragNoteOrigStart;
    /// <summary>
    /// Stores or exposes _dragNoteOrigDuration.
    /// </summary>
    private double _dragNoteOrigDuration;
    /// <summary>
    /// Stores or exposes _dragNoteOrigPitch.
    /// </summary>
    private byte _dragNoteOrigPitch;
    /// <summary>
    /// Stores or exposes _resizing.
    /// </summary>
    private bool _resizing;
    /// <summary>
    /// Stores or exposes _historyOpen.
    /// </summary>
    private bool _historyOpen;
    /// <summary>
    /// Stores or exposes _previewing.
    /// </summary>
    private bool _previewing;
    private readonly HashSet<Key> _heldTypingKeys = [];
    private ContextMenu? _noteMenu;

    // Brushes / pens (created once)
    /// <summary>
    /// Executes the BrushGridLine operation.
    /// </summary>
    private static readonly Brush BrushGridLine = new SolidColorBrush(Color.FromRgb(0x42, 0x27, 0x58));
    /// <summary>
    /// Executes the BrushBarLine operation.
    /// </summary>
    private static readonly Brush BrushBarLine = new SolidColorBrush(Color.FromRgb(0x6F, 0x49, 0x84));
    /// <summary>
    /// Executes the BrushBeatLine operation.
    /// </summary>
    private static readonly Brush BrushBeatLine = new SolidColorBrush(Color.FromRgb(0x35, 0x1C, 0x4A));
    /// <summary>
    /// Executes the BrushBlackKey operation.
    /// </summary>
    private static readonly Brush BrushBlackKey = new SolidColorBrush(Color.FromRgb(0x20, 0x0D, 0x30));
    /// <summary>
    /// Executes the BrushWhiteKey operation.
    /// </summary>
    private static readonly Brush BrushWhiteKey = new SolidColorBrush(Color.FromRgb(0x2B, 0x14, 0x3E));
    /// <summary>
    /// Executes the BrushPlayhead operation.
    /// </summary>
    private static readonly Brush BrushPlayhead = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
    /// <summary>
    /// Executes the BrushCKey operation.
    /// </summary>
    private static readonly Brush BrushCKey = new SolidColorBrush(Color.FromRgb(0x3A, 0x22, 0x52));
    private static readonly Pen PenGridLine = new(BrushGridLine, 0.5);
    private static readonly Pen PenBarLine = new(BrushBarLine, 1.0);
    private static readonly Pen PenPlayhead = new(BrushPlayhead, 1.5);

    /// <summary>
    /// Stores or exposes IsBlackKey.
    /// </summary>
    private static readonly bool[] IsBlackKey =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    public NoteGridCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        BrushGridLine.Freeze();
        BrushBarLine.Freeze();
        BrushBeatLine.Freeze();
        BrushBlackKey.Freeze();
        BrushWhiteKey.Freeze();
        BrushPlayhead.Freeze();
        BrushCKey.Freeze();
        PenGridLine.Freeze();
        PenBarLine.Freeze();
        PenPlayhead.Freeze();
    }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        double rh = ViewModel.RowHeight;
        double ppb = ViewModel.PixelsPerBeat;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x0F, 0x30)), null, new Rect(0, 0, w, h));

        // ── Horizontal rows (pitch stripes) ──────────────────────────────────
        for (int pitch = 127; pitch >= 0; pitch--)
        {
            double y = ViewModel.PitchToY(pitch);
            if (y + rh < 0 || y > h) continue;

            bool black = IsBlackKey[pitch % 12];
            bool isC = pitch % 12 == 0;
            Brush bg = isC ? BrushCKey : (black ? BrushBlackKey : BrushWhiteKey);
            dc.DrawRectangle(bg, null, new Rect(0, y, w, rh));

            // Row separator
            dc.DrawLine(PenGridLine, new Point(0, y), new Point(w, y));
        }

        // ── Vertical beat / bar lines ─────────────────────────────────────────
        int barsVisible = (int)(w / ppb) + 2;
        const double startBeat = 0;
        for (int b = (int)startBeat; b < startBeat + barsVisible; b++)
        {
            double x = ViewModel.BeatToX(b);
            bool isBar = b % 4 == 0;
            dc.DrawLine(isBar ? PenBarLine : PenGridLine, new Point(x, 0), new Point(x, h));

            // Beat sub-divisions (1/4)
            if (ppb >= 60)
            {
                for (int sub = 1; sub < 4; sub++)
                {
                    double sx = ViewModel.BeatToX(b + sub * 0.25);
                    dc.DrawLine(PenGridLine, new Point(sx, 0), new Point(sx, h));
                }
            }
        }

        // ── Notes ─────────────────────────────────────────────────────────────
        double ticksPerBeat = ViewModel.TicksPerBeat;
        foreach (var note in ViewModel.Notes)
        {
            double startBeatN = note.StartTick / ticksPerBeat;
            double durBeat = note.DurationTicks / ticksPerBeat;
            double x = ViewModel.BeatToX(startBeatN);
            double y = ViewModel.PitchToY(note.Pitch);
            double nw = durBeat * ppb;

            if (x + nw < 0 || x > w || y + rh < 0 || y > h) continue;

            // Note colour from instrument
            var color = Color.FromArgb(0xFF, 0x3A, 0x7B, 0xD5);
            bool selected = note == ViewModel.SelectedNote;

            Color top = selected ? Color.FromRgb(0x78, 0xC8, 0xFF) : Color.FromRgb(0x58, 0xA5, 0xFF);
            Color mid = selected ? Color.FromRgb(0x33, 0x85, 0xF4) : color;
            Color bottom = selected ? Color.FromRgb(0x14, 0x3F, 0xA8) : Color.FromRgb(0x17, 0x42, 0x9A);
            var noteBrush = new LinearGradientBrush(
                [new GradientStop(top, 0),
                 new GradientStop(mid, 0.48),
                 new GradientStop(bottom, 1)],
                90);

            var borderPen = new Pen(selected
                ? new SolidColorBrush(Color.FromRgb(0x9A, 0xEA, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0xDD, 0x16, 0x3A, 0x90)), 1.1);

            double drawW = Math.Max(nw - 2, 2);
            double drawH = Math.Max(rh - 1, 2);
            if (selected)
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x34, 0x42, 0xCE, 0xFF)), null,
                    new Rect(x - 0.5, y - 0.5, drawW + 4, drawH + 3), 4, 4);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0x5A, 0xD8)), null,
                    new Rect(x + 1, y + drawH - 2.0, drawW, 2.5), 2, 2);
            }

            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x48, 0x00, 0x00, 0x00)), null,
                new Rect(x + 2, y + 2.0, drawW, drawH), 3, 3);
            dc.DrawRoundedRectangle(noteBrush, borderPen,
                new Rect(x + 1, y + 0.5, drawW, drawH), 3, 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(selected ? (byte)0x4C : (byte)0x44, 0xFF, 0xFF, 0xFF)), null,
                new Rect(x + 3, y + 1.4, Math.Max(drawW - 4, 1), Math.Max(drawH * 0.32, 1)), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x2F, 0x00, 0x00, 0x00)), null,
                new Rect(x + 2, y + drawH * 0.62, Math.Max(drawW - 2, 1), Math.Max(drawH * 0.32, 1)), 2, 2);

            // Note name inside note
            if (nw > 20 && rh > 8)
            {
                string label = note.NoteName;
                if (nw > 56 && note.InstrumentIndex > 0)
                {
                    string inst = ViewModel.DescribeInstrument(note.InstrumentIndex);
                    label = $"{note.NoteName} {inst}";
                }

                var ft = new FormattedText(label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"), Math.Min(rh * 0.7, 10),
                    Brushes.White, 96);
                var textClip = new Rect(x + 2, y + 0.5, Math.Max(drawW - 2, 1), drawH);
                dc.PushClip(new RectangleGeometry(textClip));
                dc.DrawText(ft, new Point(x + 4, y + (drawH - ft.Height) / 2));
                dc.Pop();
            }
        }

        // ── Playhead ──────────────────────────────────────────────────────────
        double phx = ViewModel.BeatToX(ViewModel.PlayheadBeat);
        if (phx >= 0 && phx <= w)
            dc.DrawLine(PenPlayhead, new Point(phx, 0), new Point(phx, h));
    }

    // ── Mouse input ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        Focus();
        var pt = e.GetPosition(this);

        double beat = ViewModel.XToBeat(pt.X);
        int pitch = ViewModel.YToPitch(pt.Y);
        pitch = Math.Clamp(pitch, 0, 127);
        Note? hit = HitTestNote(pt);

        if (e.ClickCount >= 2)
        {
            ViewModel.SelectedNote = hit;
            if (hit is not null)
            {
                ShowNoteOptions(hit, pt);
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }

        _dragging = true;
        CaptureMouse();
        ViewModel.PreviewPitch(pitch);
        _previewing = true;

        switch (ViewModel.CurrentTool)
        {
            case PianoRollTool.Draw:
                // Check if clicking resize handle of existing note
                if (hit is not null && IsNearRightEdge(hit, pt))
                {
                    _resizing = true;
                    _dragNote = hit;
                    _dragStartBeat = beat;
                    _dragNoteOrigStart = hit.StartTick / ViewModel.TicksPerBeat;
                    _dragNoteOrigDuration = hit.DurationTicks / ViewModel.TicksPerBeat;
                    ViewModel.BeginHistory("Resize note");
                    _historyOpen = true;
                    ViewModel.SelectedNote = hit;
                    AppLogger.Debug($"[PianoRoll] Note resize start pitch={hit.Pitch} startTick={hit.StartTick} duration={hit.DurationTicks}");
                }
                else if (hit is not null)
                {
                    _dragNote = hit;
                    _dragStartBeat = beat;
                    double tb = ViewModel.TicksPerBeat;
                    _dragNoteOrigStart = hit.StartTick / tb;
                    _dragNoteOrigDuration = hit.DurationTicks / tb;
                    _dragNoteOrigPitch = hit.Pitch;
                    _dragStartPitch = pitch;
                    ViewModel.BeginHistory("Move note");
                    _historyOpen = true;
                    ViewModel.SelectedNote = hit;
                    AppLogger.Debug($"[PianoRoll] Note move start pitch={hit.Pitch} startTick={hit.StartTick} duration={hit.DurationTicks}");
                }
                else
                {
                    ViewModel.AddNote((byte)pitch, beat, ViewModel.Quantise);
                    _dragNote = ViewModel.SelectedNote;
                    _resizing = true;
                    _dragStartBeat = beat;
                    _dragStartPitch = pitch;
                    if (_dragNote is not null)
                    {
                        _dragNoteOrigStart = _dragNote.StartTick / ViewModel.TicksPerBeat;
                        _dragNoteOrigDuration = _dragNote.DurationTicks / ViewModel.TicksPerBeat;
                        _dragNoteOrigPitch = _dragNote.Pitch;
                    }
                    _historyOpen = false;
                }
                break;

            case PianoRollTool.Erase:
                if (hit is not null)
                {
                    ViewModel.DeleteNote(hit);
                }
                break;

            case PianoRollTool.Select:
                ViewModel.SelectedNote = HitTestNote(pt);
                break;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseRightButtonDown operation.
    /// </summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        Focus();
        var pt = e.GetPosition(this);
        var hit = HitTestNote(pt);
        if (hit is not null)
        {
            if (ViewModel.CurrentTool == PianoRollTool.Erase || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                ViewModel.DeleteNote(hit);
            else
            {
                ViewModel.SelectedNote = hit;
                ShowNoteOptions(hit, pt);
            }

            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Executes the OnMouseMove operation.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _dragNote is null || ViewModel is null) return;
        var pt = e.GetPosition(this);
        double beat = ViewModel.XToBeat(pt.X);
        int pitch = Math.Clamp(ViewModel.YToPitch(pt.Y), 0, 127);

        double tb = ViewModel.TicksPerBeat;
        if (_resizing)
        {
            double dur = Math.Max(ViewModel.Quantise, beat - _dragNote.StartTick / tb);
            dur = Math.Round(dur / ViewModel.Quantise) * ViewModel.Quantise;
            _dragNote.DurationTicks = (long)(dur * tb);
        }
        else
        {
            double delta = beat - _dragStartBeat;
            _dragNote.StartTick = (long)((_dragNoteOrigStart + delta) * tb);
            if (_dragNote.StartTick < 0) _dragNote.StartTick = 0;
            int pitchDelta = pitch - _dragStartPitch;
            _dragNote.Pitch = (byte)Math.Clamp(_dragNoteOrigPitch + pitchDelta, 0, 127);
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonUp operation.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        if (_dragNote is not null)
        {
            bool changed = Math.Abs(_dragNote.StartTick / ViewModel!.TicksPerBeat - _dragNoteOrigStart) > 0.0001 ||
                           Math.Abs(_dragNote.DurationTicks / ViewModel.TicksPerBeat - _dragNoteOrigDuration) > 0.0001 ||
                           _dragNote.Pitch != _dragNoteOrigPitch;
            AppLogger.Info($"[PianoRoll] Note drag end changed={changed} pitch={_dragNote.Pitch} startTick={_dragNote.StartTick} duration={_dragNote.DurationTicks} resizing={_resizing}");
            if (!_historyOpen || changed)
                ViewModel?.CommitNotesToPattern();
            if (_historyOpen)
            {
                if (changed) ViewModel?.CommitHistory();
                else ViewModel?.CancelHistory();
            }
        }
        _dragNote = null;
        _resizing = false;
        _historyOpen = false;
        if (_previewing)
        {
            ViewModel?.StopPreviewPitch();
            _previewing = false;
        }
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Executes the OnLostMouseCapture operation.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_previewing)
        {
            ViewModel?.StopPreviewPitch();
            _previewing = false;
        }
        base.OnLostMouseCapture(e);
    }

    /// <summary>
    /// Executes the OnKeyDown operation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ViewModel is null)
            return;

        if (e.Key == Key.Delete)
        {
            ViewModel.DeleteNoteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!ViewModel.TypingKeyboardEnabled || e.IsRepeat)
            return;

        if (TryMapTypingKeyboard(e.Key, ViewModel.TypingKeyboardBaseNote, out int pitch))
        {
            _heldTypingKeys.Add(e.Key);
            ViewModel.PreviewPitch(pitch, (byte)ViewModel.TypingKeyboardVelocity);
            AppLogger.Debug($"[PianoRoll] TypingKeyboard down key={e.Key} pitch={pitch} velocity={ViewModel.TypingKeyboardVelocity}");
            e.Handled = true;
        }
    }

    /// <summary>
    /// Stops typing-keyboard preview notes when their key is released.
    /// </summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (ViewModel is null)
            return;

        if (_heldTypingKeys.Remove(e.Key))
        {
            ViewModel.StopPreviewPitch();
            AppLogger.Debug($"[PianoRoll] TypingKeyboard up key={e.Key}");
            e.Handled = true;
        }
    }

    /// <summary>
    /// Stops any held typing-keyboard preview when focus leaves the piano roll.
    /// </summary>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_heldTypingKeys.Count > 0)
        {
            _heldTypingKeys.Clear();
            ViewModel?.StopPreviewPitch();
        }

        base.OnLostKeyboardFocus(e);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the HitTestNote operation.
    /// </summary>
    private Note? HitTestNote(Point pt)
    {
        if (ViewModel is null) return null;
        double tb = ViewModel.TicksPerBeat;
        double ppb = ViewModel.PixelsPerBeat;
        double rh = ViewModel.RowHeight;

        foreach (var note in ViewModel.Notes)
        {
            double x = ViewModel.BeatToX(note.StartTick / tb);
            double y = ViewModel.PitchToY(note.Pitch);
            double w = note.DurationTicks / tb * ppb;
            if (pt.X >= x && pt.X <= x + w && pt.Y >= y && pt.Y <= y + rh)
                return note;
        }
        return null;
    }

    /// <summary>
    /// Executes the IsNearRightEdge operation.
    /// </summary>
    private bool IsNearRightEdge(Note note, Point pt)
    {
        if (ViewModel is null) return false;
        double tb = ViewModel.TicksPerBeat;
        double x = ViewModel.BeatToX(note.StartTick / tb);
        double w = note.DurationTicks / tb * ViewModel.PixelsPerBeat;
        return pt.X >= x + w - 8;
    }

    /// <summary>
    /// Executes the ShowNoteOptions operation.
    /// </summary>
    private void ShowNoteOptions(Note note, Point point)
    {
        if (ViewModel is null)
            return;

        _noteMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = point.X,
            VerticalOffset = point.Y,
            StaysOpen = false
        };
        _noteMenu = menu;

        menu.Items.Add(new MenuItem { Header = $"{note.NoteName}  {ViewModel["Row"]} {note.StartTick}  {ViewModel["LengthShort"]} {note.DurationTicks}", IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = $"{ViewModel["Instrument"]} {note.InstrumentIndex}: {ViewModel.DescribeInstrument(note.InstrumentIndex)}", IsEnabled = false });
        menu.Items.Add(new MenuItem { Header = $"{ViewModel["Velocity"]} {note.Velocity}  {ViewModel["Volume"]} {(note.Volume <= 64 ? note.Volume.ToString() : "--")}", IsEnabled = false });
        menu.Items.Add(new MenuItem { Header = $"FX {note.Effect}  Cmd {note.EffectColumn:X2}  Param {note.EffectParam:X2}  VolCmd {note.VolumeColumn:X2}", IsEnabled = false });
        menu.Items.Add(new Separator());
        var audition = new MenuItem { Header = ViewModel["AuditionNote"] };
        audition.Click += (_, _) => ViewModel.PreviewPitch(note.Pitch, note.Velocity);
        menu.Items.Add(audition);

        var velocity = new MenuItem { Header = ViewModel["Velocity"] };
        foreach (byte value in new byte[] { 32, 64, 96, 110, 127 })
        {
            var item = new MenuItem { Header = value.ToString(), IsCheckable = true, IsChecked = Math.Abs(note.Velocity - value) <= 2 };
            item.Click += (_, _) =>
            {
                ViewModel.SetNoteVelocity(note, value, notify: true);
                ViewModel.RaiseNoteLayoutChanged();
            };
            velocity.Items.Add(item);
        }
        menu.Items.Add(velocity);

        var length = new MenuItem { Header = ViewModel["Length"] };
        AddMenuAction(length, ViewModel["Half"], () => ViewModel.ScaleNoteDuration(note, 0.5));
        AddMenuAction(length, ViewModel["Double"], () => ViewModel.ScaleNoteDuration(note, 2.0));
        AddMenuAction(length, ViewModel["QuantiseLength"], () => ViewModel.ScaleNoteDuration(note, 1.0));
        menu.Items.Add(length);

        var transpose = new MenuItem { Header = ViewModel["Transpose"] };
        AddMenuAction(transpose, ViewModel["UpOneSemitone"], () => ViewModel.TransposeNote(note, 1));
        AddMenuAction(transpose, ViewModel["DownOneSemitone"], () => ViewModel.TransposeNote(note, -1));
        AddMenuAction(transpose, ViewModel["UpOneOctave"], () => ViewModel.TransposeNote(note, 12));
        AddMenuAction(transpose, ViewModel["DownOneOctave"], () => ViewModel.TransposeNote(note, -12));
        menu.Items.Add(transpose);

        AddMenuAction(menu, ViewModel["DuplicateNote"], () => ViewModel.DuplicateNote(note));
        var delete = new MenuItem { Header = $"{ViewModel["DeleteNote"]}    Ctrl+Right Click / Del" };
        delete.Click += (_, _) => ViewModel.DeleteNote(note);
        menu.Items.Add(delete);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_noteMenu, menu))
                _noteMenu = null;
        };
        ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static void AddMenuAction(ItemsControl menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static bool TryMapTypingKeyboard(Key key, int baseNote, out int pitch)
    {
        int semitone = key switch
        {
            Key.Z => 0,
            Key.S => 1,
            Key.X => 2,
            Key.D => 3,
            Key.C => 4,
            Key.V => 5,
            Key.G => 6,
            Key.B => 7,
            Key.H => 8,
            Key.N => 9,
            Key.J => 10,
            Key.M => 11,
            Key.Q => 12,
            Key.D2 => 13,
            Key.W => 14,
            Key.D3 => 15,
            Key.E => 16,
            Key.R => 17,
            Key.D5 => 18,
            Key.T => 19,
            Key.D6 => 20,
            Key.Y => 21,
            Key.D7 => 22,
            Key.U => 23,
            Key.I => 24,
            _ => int.MinValue
        };

        if (semitone == int.MinValue)
        {
            pitch = 0;
            return false;
        }

        pitch = Math.Clamp(baseNote + semitone, 0, 127);
        return true;
    }

    /// <summary>
    /// Executes the MeasureOverride operation.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 2000 : Math.Max(availableSize.Width, 2000),
            double.IsInfinity(availableSize.Height) ? 128 * 14 : Math.Max(availableSize.Height, 128 * 14));
}
