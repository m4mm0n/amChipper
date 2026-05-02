using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.Services;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.SongEditor;

/// <summary>
/// Custom-rendered Canvas for the Song Editor timeline.
/// Draws a ruler, track lanes, pattern blocks, and the playhead.
/// </summary>
public sealed class TimelineCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public SongEditorViewModel? ViewModel { get; set; }

    // ── Brushes / pens ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the BrushRulerBg operation.
    /// </summary>
    private static readonly Brush BrushRulerBg = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x2E));
    /// <summary>
    /// Executes the BrushLaneBg operation.
    /// </summary>
    private static readonly Brush BrushLaneBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x11, 0x3F));
    /// <summary>
    /// Executes the BrushLaneAlt operation.
    /// </summary>
    private static readonly Brush BrushLaneAlt = new SolidColorBrush(Color.FromRgb(0x21, 0x0D, 0x33));
    /// <summary>
    /// Executes the BrushBarLine operation.
    /// </summary>
    private static readonly Brush BrushBarLine = new SolidColorBrush(Color.FromRgb(0x55, 0x37, 0x6B));
    /// <summary>
    /// Executes the BrushPlayhead operation.
    /// </summary>
    private static readonly Brush BrushPlayhead = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
    /// <summary>
    /// Executes the BrushPlayheadGlow operation.
    /// </summary>
    private static readonly Brush BrushPlayheadGlow = new SolidColorBrush(Color.FromArgb(0x42, 0xFF, 0x44, 0x44));
    /// <summary>
    /// Executes the BrushRulerTxt operation.
    /// </summary>
    private static readonly Brush BrushRulerTxt = new SolidColorBrush(Color.FromRgb(0xB8, 0x8D, 0xCA));
    /// <summary>
    /// Executes the BrushClipEnv operation.
    /// </summary>
    private static readonly Brush BrushClipEnv = new SolidColorBrush(Color.FromArgb(0xD8, 0x5A, 0x9B, 0xFF));
    /// <summary>
    /// Executes the BrushClipEnvPoint operation.
    /// </summary>
    private static readonly Brush BrushClipEnvPoint = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    private static readonly Pen PenBarLine = new(BrushBarLine, 0.5);
    private static readonly Pen PenBeat = new(new SolidColorBrush(Color.FromRgb(0x38, 0x20, 0x4E)), 0.5);
    private static readonly Pen PenPlayhead = new(BrushPlayhead, 2.4);
    private static readonly Pen PenPlayheadCore = new(new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xF4)), 0.8);
    private static readonly Pen PenBlock = new(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 1);
    private static readonly Pen PenClipEnv = new(BrushClipEnv, 1.4);
    private static readonly Pen PenClipEnvPoint = new(BrushClipEnvPoint, 1);

    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double RulerHeight = 20;

    static TimelineCanvas()
    {
        BrushRulerBg.Freeze(); BrushLaneBg.Freeze(); BrushLaneAlt.Freeze();
        BrushBarLine.Freeze(); BrushPlayhead.Freeze(); BrushRulerTxt.Freeze();
        BrushClipEnv.Freeze(); BrushClipEnvPoint.Freeze(); BrushPlayheadGlow.Freeze();
        PenBarLine.Freeze(); PenBeat.Freeze(); PenPlayhead.Freeze(); PenBlock.Freeze();
        PenClipEnv.Freeze(); PenClipEnvPoint.Freeze(); PenPlayheadCore.Freeze();
    }

    public TimelineCanvas() { ClipToBounds = true; }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        double ppb = ViewModel.PixelsPerBeat;
        double th = ViewModel.TrackHeight;

        // Background
        dc.DrawRectangle(BrushLaneBg, null, new Rect(0, 0, w, h));

        // ── Track lane stripes ────────────────────────────────────────────────
        for (int ti = 0; ti < ViewModel.Tracks.Count; ti++)
        {
            double ly = RulerHeight + ti * th;
            if (ly > h) break;
            Brush bg = ti % 2 == 0 ? BrushLaneBg : BrushLaneAlt;
            dc.DrawRectangle(bg, null, new Rect(0, ly, w, th));
        }

        // ── Ruler background ──────────────────────────────────────────────────
        dc.DrawRectangle(BrushRulerBg, null, new Rect(0, 0, w, RulerHeight));

        // ── Vertical beat/bar lines + ruler ticks ─────────────────────────────
        int startBeat = (int)ViewModel.ScrollBeat;
        int beatsVis = (int)(w / ppb) + 2;

        for (int b = startBeat; b < startBeat + beatsVis; b++)
        {
            double x = ViewModel.BeatToX(b);
            bool isBar = b % 4 == 0;
            dc.DrawLine(isBar ? PenBarLine : PenBeat, new Point(x, RulerHeight), new Point(x, h));

            if (isBar)
            {
                dc.DrawLine(PenBarLine, new Point(x, 0), new Point(x, RulerHeight));
                string label = $"{b / 4 + 1}";
                var ft = new FormattedText(label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 9,
                    BrushRulerTxt, 96);
                dc.DrawText(ft, new Point(x + 2, (RulerHeight - ft.Height) / 2));
            }
        }

        // ── Playhead guide behind clips ───────────────────────────────────────
        double phx = ViewModel.BeatToX(ViewModel.PlayheadBeat);
        if (phx >= 0 && phx <= w)
        {
            dc.DrawRectangle(BrushPlayheadGlow,
                null, new Rect(Math.Max(0, phx - 3), RulerHeight, 6, Math.Max(0, h - RulerHeight)));
        }

        // ── Tracker order underlay ────────────────────────────────────────────
        DrawOrderUnderlay(dc, w, h, ppb, th);

        // ── Pattern blocks ────────────────────────────────────────────────────
        for (int ti = 0; ti < ViewModel.Tracks.Count; ti++)
        {
            var track = ViewModel.Tracks[ti];
            double ly = RulerHeight + ti * th;

            foreach (var block in track.Blocks)
            {
                double bx = ViewModel.BeatToX(block.StartBeat);
                double bw = block.DurationBeats * ppb;
                double by = ly + 2;
                double bh = th - 4;

                if (bx + bw < 0 || bx > w) continue;

                // Block fill: track colour, dimmed if muted
                uint argb = block.Muted ? DimColor(track.Color) : track.Color;
                var fill = ArgbToBrush(argb);
                bool selected = block == ViewModel.SelectedBlock;

                var borderPen = new Pen(selected
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)), 1.0);

                dc.DrawRoundedRectangle(fill, borderPen,
                    new Rect(bx, by, Math.Max(bw - 1, 4), bh), 3, 3);

                if (selected && bw > 12)
                {
                    var gripPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1);
                    dc.DrawLine(gripPen, new Point(bx + bw - 5, by + 5), new Point(bx + bw - 5, by + bh - 5));
                    dc.DrawLine(gripPen, new Point(bx + bw - 2, by + 5), new Point(bx + bw - 2, by + bh - 5));
                }

                // Pattern name label
                if (bw > 24 && bh > 10)
                {
                    string label = block.PatternIndex < ViewModel.Patterns.Count
                        ? ViewModel.Patterns[block.PatternIndex].Name
                        : $"P{block.PatternIndex}";
                    if (block.Muted) label = "(muted) " + label;
                    label += $"  {block.DurationBeats:0.##}b";
                    if (block.Volume != 128 || block.Panning != 128)
                        label += $"  V{block.Volume} P{block.Panning}";

                    var ft = new FormattedText(label,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), Math.Min(bh * 0.45, 11),
                        Brushes.White, 96);
                    dc.PushClip(new RectangleGeometry(new Rect(bx, by, bw, bh)));
                    dc.DrawText(ft, new Point(bx + 4, by + (bh - ft.Height) / 2));
                    dc.Pop();
                }

                DrawClipEnvelope(dc, block, bx, by, bw, bh);
            }
        }

        // ── Playhead overlay above clips ──────────────────────────────────────
        DrawPlayheadOverlay(dc, phx, h);
    }

    /// <summary>
    /// Executes the DrawPlayheadOverlay operation.
    /// </summary>
    private static void DrawPlayheadOverlay(DrawingContext dc, double x, double height)
    {
        if (x < 0 || x > 100000)
            return;

        dc.DrawRectangle(BrushPlayheadGlow, null, new Rect(Math.Max(0, x - 4), 0, 8, height));
        dc.DrawLine(PenPlayhead, new Point(x, 0), new Point(x, height));
        dc.DrawLine(PenPlayheadCore, new Point(x + 1.2, 0), new Point(x + 1.2, height));

        StreamGeometry cap = new();
        using (var ctx = cap.Open())
        {
            ctx.BeginFigure(new Point(x, RulerHeight + 8), true, true);
            ctx.LineTo(new Point(x - 6, RulerHeight), true, false);
            ctx.LineTo(new Point(x + 6, RulerHeight), true, false);
        }
        cap.Freeze();
        dc.DrawGeometry(BrushPlayhead, null, cap);
    }

    /// <summary>
    /// Executes the DrawOrderUnderlay operation.
    /// </summary>
    private void DrawOrderUnderlay(DrawingContext dc, double width, double height, double pixelsPerBeat, double trackHeight)
    {
        if (ViewModel is null || ViewModel.Tracks.Count == 0)
            return;

        double visibleStart = ViewModel.ScrollBeat;
        double visibleEnd = visibleStart + width / Math.Max(pixelsPerBeat, 1);
        foreach (var order in ViewModel.EnumerateOrdersForRange(visibleStart, visibleEnd))
        {
            double bx = ViewModel.BeatToX(order.StartBeat);
            double bw = order.DurationBeats * pixelsPerBeat;
            if (bx + bw < 0 || bx > width)
                continue;

            for (int trackIndex = 0; trackIndex < ViewModel.Tracks.Count; trackIndex++)
            {
                double by = RulerHeight + trackIndex * trackHeight + 2;
                double bh = trackHeight - 4;
                if (by + bh < RulerHeight || by > height)
                    continue;

                var track = ViewModel.Tracks[trackIndex];
                var fill = new SolidColorBrush(Color.FromArgb(
                    order.LoopCopy ? (byte)0x4E : (byte)0x74,
                    (byte)(track.Color >> 16),
                    (byte)(track.Color >> 8),
                    (byte)track.Color));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xA8, 0xB7, 0xD8)), 1.0);
                dc.DrawRoundedRectangle(fill, pen, new Rect(bx, by, Math.Max(bw - 1, 4), bh), 3, 3);

                if (bw > 28 && bh > 10)
                {
                    string label = $"{(order.LoopCopy ? "LOOP " : string.Empty)}ORDER {order.Order:D2}  PAT {order.PatternIndex:D2}";
                    var ft = new FormattedText(label,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Consolas"), Math.Min(bh * 0.42, 10),
                        Brushes.White, 96);
                    dc.PushClip(new RectangleGeometry(new Rect(bx, by, bw, bh)));
                    dc.DrawText(ft, new Point(bx + 4, by + (bh - ft.Height) / 2));
                    dc.Pop();
                }
            }
        }
    }

    // ── Mouse input ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes _dragging.
    /// </summary>
    private bool _dragging;
    /// <summary>
    /// Stores or exposes _resizing.
    /// </summary>
    private bool _resizing;
    /// <summary>
    /// Stores or exposes _dragBlock.
    /// </summary>
    private PatternBlock? _dragBlock;
    /// <summary>
    /// Stores or exposes _dragTrack.
    /// </summary>
    private Track? _dragTrack;
    /// <summary>
    /// Stores or exposes _dragStartBeat.
    /// </summary>
    private double _dragStartBeat;
    /// <summary>
    /// Stores or exposes _dragBlockOrigStart.
    /// </summary>
    private double _dragBlockOrigStart;
    /// <summary>
    /// Stores or exposes _dragBlockOrigDuration.
    /// </summary>
    private double _dragBlockOrigDuration;
    /// <summary>
    /// Stores or exposes _historyOpen.
    /// </summary>
    private bool _historyOpen;
    /// <summary>
    /// Stores or exposes _clipDragPoint.
    /// </summary>
    private AutomationPoint? _clipDragPoint;
    /// <summary>
    /// Stores or exposes _clipDragBlock.
    /// </summary>
    private PatternBlock? _clipDragBlock;
    /// <summary>
    /// Stores or exposes _clipDragTrackIndex.
    /// </summary>
    private int _clipDragTrackIndex = -1;
    /// <summary>
    /// Stores or exposes _clipDragOrigBeat.
    /// </summary>
    private double _clipDragOrigBeat;
    /// <summary>
    /// Stores or exposes _clipDragOrigValue.
    /// </summary>
    private byte _clipDragOrigValue;
    /// <summary>
    /// Stores or exposes _clipDragHistoryOpen.
    /// </summary>
    private bool _clipDragHistoryOpen;
    /// <summary>
    /// Stores or exposes _clipSelectedPoint.
    /// </summary>
    private AutomationPoint? _clipSelectedPoint;

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        Focus();

        var pt = e.GetPosition(this);
        int trackIdx = (int)((pt.Y - RulerHeight) / ViewModel.TrackHeight);
        double beat = ViewModel.XToBeat(pt.X);

        if (pt.Y <= RulerHeight)
        {
            ViewModel.SeekToBeat(beat);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // ── Double-click: open pattern in editors regardless of current tool ──
        if (e.ClickCount == 2 && trackIdx >= 0 && trackIdx < ViewModel.Tracks.Count)
        {
            PatternBlock? dblHit = HitTestBlock(pt, trackIdx);
            if (dblHit is not null)
            {
                ViewModel.SelectedBlock = dblHit;
                ViewModel.RaiseBlockEditRequested(dblHit.PatternIndex);
                e.Handled = true;
                InvalidateVisual();
                return;
            }
        }

        CaptureMouse();
        _dragging = true;

        if (trackIdx < 0 || trackIdx >= ViewModel.Tracks.Count)
        {
            _dragging = false;
            ReleaseMouseCapture();
            return;
        }
        var track = ViewModel.Tracks[trackIdx];

        PatternBlock? hit = HitTestBlock(pt, trackIdx);

        if (hit is not null && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            ViewModel.SelectedBlock = hit;
            if (ViewModel.ClipEnvelope is not null)
            {
                ViewModel.ClipEnvelope.SelectedBlock = hit;
                double by = RulerHeight + trackIdx * ViewModel.TrackHeight + 2;
                double bh = ViewModel.TrackHeight - 4;
                double bx = ViewModel.BeatToX(hit.StartBeat);
                double bw = hit.DurationBeats * ViewModel.PixelsPerBeat;

                if (TryHitClipEnvelopePoint(hit, pt, bx, by, bw, bh, ViewModel.ClipEnvelope.Target, out AutomationPoint point))
                {
                    _clipDragPoint = point;
                    _clipDragBlock = hit;
                    _clipDragTrackIndex = trackIdx;
                    _clipSelectedPoint = point;
                    _clipDragOrigBeat = point.Beat;
                    _clipDragOrigValue = point.Value;
                    _clipDragHistoryOpen = true;
                    ViewModel.BeginHistory("Move clip envelope point");
                    AppLogger.Debug(
                        $"[Timeline] ClipEnvDragStart pattern={hit.PatternIndex} target={ViewModel.ClipEnvelope.Target} beat={point.Beat:0.###} value={point.Value}");
                }
                else
                {
                    double localBeat = Math.Clamp(beat - hit.StartBeat, 0, Math.Max(hit.DurationBeats, 0.25));
                    byte value = ScreenToEnvelopeValue(pt, by, bh);
                    ViewModel.ClipEnvelope.AddPoint(localBeat, value);
                    _clipSelectedPoint = FindClosestClipPoint(hit, ViewModel.ClipEnvelope.Target, localBeat, value);
                }
                e.Handled = true;
                InvalidateVisual();
                return;
            }
        }

        switch (ViewModel.CurrentTool)
        {
            case SongEditorTool.Draw:
                {
                    if (hit is not null)
                    {
                        bool resizing = IsNearRightEdge(hit, pt);
                        ViewModel.BeginHistory(resizing ? "Resize block" : "Move block");
                        _dragBlock = hit;
                        _dragTrack = track;
                        _dragStartBeat = beat;
                        _dragBlockOrigStart = hit.StartBeat;
                        _dragBlockOrigDuration = hit.DurationBeats;
                        _resizing = resizing;
                        _historyOpen = true;
                        AppLogger.Debug($"[Timeline] DragStart mode={(_resizing ? "resize" : "move")} pattern={hit.PatternIndex} start={hit.StartBeat:0.###} duration={hit.DurationBeats:0.###}");
                        ViewModel.SelectedBlock = hit;
                    }
                    else
                    {
                        // Place new block using currently selected pattern
                        int patIdx = ViewModel.Patterns.Count > 0
                            ? Math.Clamp(ViewModel.SelectedPatternIndex, 0, ViewModel.Patterns.Count - 1)
                            : -1;
                        if (patIdx >= 0)
                        {
                            ViewModel.PlaceBlock(track, patIdx, beat);
                            ViewModel.SelectedBlock = track.Blocks[^1];
                        }
                    }
                    break;
                }
            case SongEditorTool.Erase:
                {
                    if (hit is not null)
                    {
                        ViewModel.BeginHistory("Erase block");
                        track.Blocks.Remove(hit);
                        ViewModel.SelectedBlock = null;
                        ViewModel.RaiseLayoutChanged();
                        ViewModel.RaiseSongDataChanged();
                        ViewModel.CommitHistory();
                    }
                    break;
                }
            case SongEditorTool.Select:
                {
                    ViewModel.SelectedBlock = hit;
                    if (hit is not null && IsNearRightEdge(hit, pt))
                    {
                        ViewModel.BeginHistory("Resize block");
                        _dragBlock = hit;
                        _dragTrack = track;
                        _dragStartBeat = beat;
                        _dragBlockOrigStart = hit.StartBeat;
                        _dragBlockOrigDuration = hit.DurationBeats;
                        _resizing = true;
                        _historyOpen = true;
                        AppLogger.Debug($"[Timeline] DragStart mode=resize pattern={hit.PatternIndex} start={hit.StartBeat:0.###} duration={hit.DurationBeats:0.###}");
                    }
                    break;
                }
            case SongEditorTool.Mute:
                {
                    if (hit is not null)
                    {
                        ViewModel.BeginHistory("Toggle block mute");
                        hit.Muted = !hit.Muted;
                        ViewModel.RaiseLayoutChanged();
                        ViewModel.RaiseSongDataChanged();
                        ViewModel.CommitHistory();
                    }
                    break;
                }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseMove operation.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (ViewModel is null) return;

        if (_clipDragPoint is not null && _clipDragBlock is not null && ViewModel.ClipEnvelope is not null)
        {
            var mousePt = e.GetPosition(this);
            double mouseBeat = ViewModel.XToBeat(mousePt.X);
            double localBeat = Math.Clamp(mouseBeat - _clipDragBlock.StartBeat, 0, Math.Max(_clipDragBlock.DurationBeats, 0.25));
            double by = RulerHeight + Math.Max(_clipDragTrackIndex, 0) * ViewModel.TrackHeight + 2;
            double bh = ViewModel.TrackHeight - 4;
            byte value = ScreenToEnvelopeValue(mousePt, by, bh);

            if (Math.Abs(_clipDragPoint.Beat - localBeat) > 0.0001 || _clipDragPoint.Value != value)
                ViewModel.ClipEnvelope.MovePoint(_clipDragPoint, localBeat, value);

            InvalidateVisual();
            return;
        }

        if (!_dragging || _dragBlock is null || ViewModel is null) return;
        var pt = e.GetPosition(this);
        double beat = ViewModel.XToBeat(pt.X);
        double delta = beat - _dragStartBeat;

        if (_resizing)
        {
            double newDuration = Math.Max(0.25, _dragBlockOrigDuration + delta);
            _dragBlock.DurationBeats = Math.Round(newDuration * 4.0) / 4.0;
        }
        else
        {
            double newStart = Math.Max(0, _dragBlockOrigStart + delta);
            newStart = Math.Round(newStart); // 1-beat snap
            _dragBlock.StartBeat = newStart;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonUp operation.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool changed = false;

        if (_clipDragPoint is not null)
        {
            changed =
                Math.Abs(_clipDragPoint.Beat - _clipDragOrigBeat) > 0.0001 ||
                _clipDragPoint.Value != _clipDragOrigValue;

            AppLogger.Info(
                $"[Timeline] ClipEnvDragEnd changed={changed} pattern={_clipDragBlock?.PatternIndex} target={ViewModel?.ClipEnvelope.Target} " +
                $"oldBeat={_clipDragOrigBeat:0.###} newBeat={_clipDragPoint.Beat:0.###} oldValue={_clipDragOrigValue} newValue={_clipDragPoint.Value}");

            if (_clipDragHistoryOpen)
            {
                if (changed) ViewModel?.CommitHistory();
                else ViewModel?.CancelHistory();
            }

            _clipDragPoint = null;
            _clipDragBlock = null;
            _clipDragTrackIndex = -1;
            _clipDragHistoryOpen = false;
            _dragging = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }

        _dragging = false;
        if (_dragBlock is not null)
        {
            changed =
                Math.Abs(_dragBlock.StartBeat - _dragBlockOrigStart) > 0.0001 ||
                Math.Abs(_dragBlock.DurationBeats - _dragBlockOrigDuration) > 0.0001;

            AppLogger.Info(
                $"[Timeline] DragEnd changed={changed} pattern={_dragBlock.PatternIndex} oldStart={_dragBlockOrigStart:0.###} newStart={_dragBlock.StartBeat:0.###} " +
                $"oldDuration={_dragBlockOrigDuration:0.###} newDuration={_dragBlock.DurationBeats:0.###}");

            if (changed)
                ViewModel?.RaiseSongDataChanged();
        }
        if (_historyOpen)
        {
            if (changed) ViewModel?.CommitHistory();
            else
                ViewModel?.CancelHistory();
        }
        _dragBlock = null;
        _resizing = false;
        _historyOpen = false;
        _clipDragTrackIndex = -1;
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Executes the OnKeyDown operation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel?.ClipEnvelope is not null && _clipSelectedPoint is not null)
        {
            ViewModel.ClipEnvelope.DeletePoint(_clipSelectedPoint);
            _clipSelectedPoint = null;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (ViewModel?.ClipEnvelope is not null && _clipSelectedPoint is not null)
        {
            if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                DuplicateSelectedClipPoint();
                e.Handled = true;
                return;
            }

            if (TryNudgeSelectedClipPoint(e))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Delete) ViewModel?.DeleteBlockCommand.Execute(null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the HitTestBlock operation.
    /// </summary>
    private PatternBlock? HitTestBlock(Point pt, int trackIdx)
    {
        if (ViewModel is null || trackIdx >= ViewModel.Tracks.Count) return null;
        var track = ViewModel.Tracks[trackIdx];
        double ly = RulerHeight + trackIdx * ViewModel.TrackHeight;

        foreach (var block in track.Blocks)
        {
            double bx = ViewModel.BeatToX(block.StartBeat);
            double bw = block.DurationBeats * ViewModel.PixelsPerBeat;
            if (pt.X >= bx && pt.X <= bx + bw && pt.Y >= ly && pt.Y <= ly + ViewModel.TrackHeight)
                return block;
        }
        return null;
    }

    /// <summary>
    /// Executes the IsNearRightEdge operation.
    /// </summary>
    private bool IsNearRightEdge(PatternBlock block, Point pt)
    {
        if (ViewModel is null) return false;

        double bx = ViewModel.BeatToX(block.StartBeat);
        double bw = block.DurationBeats * ViewModel.PixelsPerBeat;
        return pt.X >= bx + bw - 8 && pt.X <= bx + bw + 4;
    }

    /// <summary>
    /// Executes the DrawClipEnvelope operation.
    /// </summary>
    private void DrawClipEnvelope(DrawingContext dc, PatternBlock block, double bx, double by, double bw, double bh)
    {
        if (ViewModel is null || bw < 16 || bh < 12)
            return;

        var clipVm = ViewModel.ClipEnvelope;
        if (clipVm is null)
            return;

        var points = clipVm.Target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (points.Count == 0)
            return;

        double left = bx + 3;
        double top = by + 4;
        double width = Math.Max(bw - 6, 1);
        double height = Math.Max(bh - 8, 1);
        double maxValue = clipVm.Target == AutomationTarget.Volume ? 128.0 : 255.0;

        var ordered = points.OrderBy(p => p.Beat).ToList();
        StreamGeometry geometry = new();
        using (var ctx = geometry.Open())
        {
            bool first = true;
            foreach (var point in ordered)
            {
                double x = left + (Math.Clamp(point.Beat, 0, Math.Max(block.DurationBeats, 0.25)) / Math.Max(block.DurationBeats, 0.25)) * width;
                double y = top + height - (point.Value / maxValue) * height;
                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }
        geometry.Freeze();

        dc.PushClip(new RectangleGeometry(new Rect(bx + 1, by + 1, Math.Max(bw - 2, 1), Math.Max(bh - 2, 1))));
        dc.DrawGeometry(null, PenClipEnv, geometry);
        foreach (var point in ordered)
        {
            double x = left + (Math.Clamp(point.Beat, 0, Math.Max(block.DurationBeats, 0.25)) / Math.Max(block.DurationBeats, 0.25)) * width;
            double y = top + height - (point.Value / maxValue) * height;
            bool selected = point == _clipSelectedPoint;
            Brush fill = selected ? BrushClipEnv : BrushClipEnvPoint;
            dc.DrawEllipse(fill, PenClipEnvPoint, new Point(x, y), selected ? 3.4 : 2.8, selected ? 3.4 : 2.8);
        }
        dc.Pop();
    }

    /// <summary>
    /// Executes the TryHitClipEnvelopePoint operation.
    /// </summary>
    private bool TryHitClipEnvelopePoint(PatternBlock block, Point pt, double bx, double by, double bw, double bh, AutomationTarget target, out AutomationPoint hit)
    {
        hit = default!;
        if (ViewModel is null || ViewModel.ClipEnvelope is null)
            return false;

        var points = ViewModel.ClipEnvelope.Target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (points.Count == 0)
            return false;

        double left = bx + 3;
        double top = by + 4;
        double width = Math.Max(bw - 6, 1);
        double height = Math.Max(bh - 8, 1);

        foreach (var point in points)
        {
            (double x, double y) = GetClipEnvelopePointPosition(block, point, left, top, width, height, target);
            if (Math.Abs(pt.X - x) <= 7 && Math.Abs(pt.Y - y) <= 7)
            {
                hit = point;
                return true;
            }
        }

        return false;
    }

    private static (double x, double y) GetClipEnvelopePointPosition(PatternBlock block, AutomationPoint point, double left, double top, double width, double height, AutomationTarget target)
    {
        double span = Math.Max(block.DurationBeats, 0.25);
        double x = left + (Math.Clamp(point.Beat, 0, span) / span) * width;
        double maxValue = target == AutomationTarget.Volume ? 128.0 : 255.0;
        return (x, top + height - (point.Value / maxValue) * height);
    }

    /// <summary>
    /// Executes the FindClosestClipPoint operation.
    /// </summary>
    private AutomationPoint? FindClosestClipPoint(PatternBlock block, AutomationTarget target, double beat, byte value)
    {
        var points = target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (points.Count == 0)
            return null;

        return points
            .OrderBy(p => Math.Abs(p.Beat - beat))
            .ThenBy(p => Math.Abs(p.Value - value))
            .FirstOrDefault();
    }

    /// <summary>
    /// Executes the DuplicateSelectedClipPoint operation.
    /// </summary>
    private void DuplicateSelectedClipPoint()
    {
        if (ViewModel?.ClipEnvelope is null || _clipSelectedPoint is null)
            return;

        var block = _clipDragBlock ?? ViewModel.SelectedBlock;
        if (block is null)
            return;

        var points = ViewModel.ClipEnvelope.Target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (!points.Contains(_clipSelectedPoint))
            return;

        double duplicateBeat = Math.Min(_clipSelectedPoint.Beat + 0.25, Math.Max(block.DurationBeats, 0.25));
        var clone = new AutomationPoint { Beat = duplicateBeat, Value = _clipSelectedPoint.Value };

        ViewModel.ClipEnvelope.BeginHistory("Duplicate clip envelope point");
        points.Add(clone);
        points.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        _clipSelectedPoint = clone;
        ViewModel.ClipEnvelope.CommitHistory();
        ViewModel.ClipEnvelope.Main.SongEditor.RaiseSongDataChanged();
        ViewModel.ClipEnvelope.Refresh();
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the TryNudgeSelectedClipPoint operation.
    /// </summary>
    private bool TryNudgeSelectedClipPoint(KeyEventArgs e)
    {
        if (ViewModel?.ClipEnvelope is null || _clipSelectedPoint is null)
            return false;

        var block = _clipDragBlock ?? ViewModel.SelectedBlock;
        if (block is null)
            return false;

        var points = ViewModel.ClipEnvelope.Target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (!points.Contains(_clipSelectedPoint))
            return false;

        double beatStep = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.0 : 0.25;
        int valueStep = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 8 : 1;
        double newBeat = _clipSelectedPoint.Beat;
        byte newValue = _clipSelectedPoint.Value;

        switch (e.Key)
        {
            case Key.Left:
                newBeat = Math.Max(0, newBeat - beatStep);
                break;
            case Key.Right:
                newBeat = Math.Min(Math.Max(block.DurationBeats, 0.25), newBeat + beatStep);
                break;
            case Key.Up:
                newValue = (byte)Math.Clamp(newValue + valueStep, 0, ViewModel.ClipEnvelope.Target == AutomationTarget.Volume ? 128 : 255);
                break;
            case Key.Down:
                newValue = (byte)Math.Clamp(newValue - valueStep, 0, ViewModel.ClipEnvelope.Target == AutomationTarget.Volume ? 128 : 255);
                break;
            default:
                return false;
        }

        if (Math.Abs(_clipSelectedPoint.Beat - newBeat) < 0.0001 && _clipSelectedPoint.Value == newValue)
            return true;

        ViewModel.ClipEnvelope.BeginHistory("Nudge clip envelope point");
        ViewModel.ClipEnvelope.MovePoint(_clipSelectedPoint, newBeat, newValue);
        ViewModel.ClipEnvelope.CommitHistory();
        ViewModel.ClipEnvelope.Main.SongEditor.RaiseSongDataChanged();
        ViewModel.ClipEnvelope.Refresh();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Executes the ScreenToEnvelopeValue operation.
    /// </summary>
    private byte ScreenToEnvelopeValue(Point pt, double by, double bh)
    {
        if (ViewModel is null)
            return 128;

        bool volume = ViewModel.ClipEnvelope.Target == AutomationTarget.Volume;
        double maxValue = volume ? 128.0 : 255.0;
        double normalized = 1.0 - Math.Clamp((pt.Y - by) / Math.Max(bh, 1), 0, 1);
        return (byte)Math.Clamp(Math.Round(normalized * maxValue), 0, maxValue);
    }

    /// <summary>
    /// Executes the ArgbToBrush operation.
    /// </summary>
    private static SolidColorBrush ArgbToBrush(uint argb) =>
        new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16),
                           (byte)(argb >> 8), (byte)argb));

    /// <summary>
    /// Executes the DimColor operation.
    /// </summary>
    private static uint DimColor(uint argb)
    {
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return (argb & 0xFF000000) | ((uint)(r / 2) << 16) | ((uint)(g / 2) << 8) | (uint)(b / 2);
    }

    /// <summary>
    /// Executes the MeasureOverride operation.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 3000 : Math.Max(availableSize.Width, 3000),
            double.IsInfinity(availableSize.Height) ? 400 : Math.Max(availableSize.Height, 400));
}
