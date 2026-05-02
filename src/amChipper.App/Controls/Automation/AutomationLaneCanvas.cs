using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.Services;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.Automation;

/// <summary>
/// Represents the AutomationLaneCanvas component.
/// </summary>
public sealed class AutomationLaneCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public IAutomationLaneViewModel? ViewModel { get; set; }

    /// <summary>
    /// Executes the BrushBg operation.
    /// </summary>
    private static readonly Brush BrushBg = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1E));
    /// <summary>
    /// Executes the BrushGrid operation.
    /// </summary>
    private static readonly Brush BrushGrid = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x36));
    /// <summary>
    /// Executes the BrushPoint operation.
    /// </summary>
    private static readonly Brush BrushPoint = new SolidColorBrush(Color.FromRgb(0x5A, 0x9B, 0xFF));
    /// <summary>
    /// Executes the BrushPointSel operation.
    /// </summary>
    private static readonly Brush BrushPointSel = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
    /// <summary>
    /// Executes the BrushPlayhead operation.
    /// </summary>
    private static readonly Brush BrushPlayhead = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0x44, 0x44));
    private static readonly Pen PenGrid = new(BrushGrid, 0.5);
    private static readonly Pen PenPoint = new(BrushPoint, 1);
    private static readonly Pen PenPlayhead = new(BrushPlayhead, 1.5);

    /// <summary>
    /// Stores or exposes _dragPoint.
    /// </summary>
    private AutomationPoint? _dragPoint;
    /// <summary>
    /// Stores or exposes _historyOpen.
    /// </summary>
    private bool _historyOpen;

    static AutomationLaneCanvas()
    {
        BrushBg.Freeze(); BrushGrid.Freeze(); BrushPoint.Freeze(); BrushPointSel.Freeze();
        BrushPlayhead.Freeze(); PenGrid.Freeze(); PenPoint.Freeze(); PenPlayhead.Freeze();
    }

    public AutomationLaneCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null)
            return;

        double w = ActualWidth;
        double h = ActualHeight;
        double beatSpan = Math.Max(w / 80.0, 16.0);
        var points = ViewModel.Points.OrderBy(p => p.Beat).ToList();

        dc.DrawRectangle(BrushBg, null, new Rect(0, 0, w, h));

        for (int i = 0; i <= 16; i++)
        {
            double x = i * w / 16.0;
            dc.DrawLine(PenGrid, new Point(x, 0), new Point(x, h));
        }

        for (int i = 0; i <= 4; i++)
        {
            double y = i * h / 4.0;
            dc.DrawLine(PenGrid, new Point(0, y), new Point(w, y));
        }

        if (points.Count > 0)
        {
            StreamGeometry geometry = new();
            using (var ctx = geometry.Open())
            {
                bool first = true;
                foreach (var point in points)
                {
                    double x = BeatToX(point.Beat, beatSpan, w);
                    double y = ValueToY(point.Value, h);
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
            dc.DrawGeometry(null, new Pen(BrushPoint, 1.5), geometry);

            foreach (var point in points)
            {
                double x = BeatToX(point.Beat, beatSpan, w);
                double y = ValueToY(point.Value, h);
                Brush fill = point == _dragPoint ? BrushPointSel : BrushPoint;
                dc.DrawEllipse(fill, PenPoint, new Point(x, y), 5, 5);
            }
        }

        double playheadX = BeatToX(ViewModel.PlaybackBeat, beatSpan, w);
        if (playheadX >= 0 && playheadX <= w)
            dc.DrawLine(PenPlayhead, new Point(playheadX, 0), new Point(playheadX, h));
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null)
            return;

        Focus();
        CaptureMouse();
        var pt = e.GetPosition(this);
        var hit = HitTestPoint(pt);

        if (hit is not null)
        {
            _dragPoint = hit;
            _historyOpen = true;
            ViewModel.BeginHistory("Move automation point");
        }
        else
        {
            var (beat, value) = ScreenToPoint(pt);
            ViewModel.AddPoint(beat, value);
            _dragPoint = HitTestPoint(pt);
            _historyOpen = false;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseMove operation.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (ViewModel is null || _dragPoint is null)
            return;

        var (beat, value) = ScreenToPoint(e.GetPosition(this));
        ViewModel.MovePoint(_dragPoint, beat, value);
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonUp operation.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (ViewModel is null)
            return;

        if (_historyOpen)
        {
            _historyOpen = false;
            ViewModel.CommitHistory();
        }

        _dragPoint = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnKeyDown operation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel is not null && _dragPoint is not null)
        {
            ViewModel.DeletePoint(_dragPoint);
            _dragPoint = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Executes the HitTestPoint operation.
    /// </summary>
    private AutomationPoint? HitTestPoint(Point pt)
    {
        if (ViewModel is null)
            return null;

        double beatSpan = Math.Max(ActualWidth / 80.0, 16.0);
        foreach (var point in ViewModel.Points)
        {
            double x = BeatToX(point.Beat, beatSpan, ActualWidth);
            double y = ValueToY(point.Value, ActualHeight);
            if (Math.Abs(pt.X - x) <= 7 && Math.Abs(pt.Y - y) <= 7)
                return point;
        }

        return null;
    }

    private (double beat, byte value) ScreenToPoint(Point pt)
    {
        double beatSpan = Math.Max(ActualWidth / 80.0, 16.0);
        double width = Math.Max(ActualWidth, 1.0);
        double beat = Math.Clamp(pt.X / width * beatSpan, 0, beatSpan);
        byte value = (byte)Math.Clamp((1.0 - pt.Y / Math.Max(ActualHeight, 1)) * 255, 0, 255);
        return (beat, value);
    }

    /// <summary>
    /// Executes the BeatToX operation.
    /// </summary>
    private static double BeatToX(double beat, double beatSpan, double width) =>
        beatSpan <= 0 ? 0 : (beat / beatSpan) * width;

    /// <summary>
    /// Executes the ValueToY operation.
    /// </summary>
    private static double ValueToY(byte value, double height) =>
        height - (value / 255.0) * height;
}
