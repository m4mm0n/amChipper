using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.Services;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.SongEditor;

/// <summary>
/// Renders the track header strip on the left of the song editor.
/// Shows track name, colour chip, mute/solo buttons.
/// </summary>
public sealed class TrackHeaderPanel : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public SongEditorViewModel? ViewModel { get; set; }
    /// <summary>
    /// Stores or exposes ScrollOffset.
    /// </summary>
    public double ScrollOffset { get; set; }

    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double RulerHeight = 20;

    /// <summary>
    /// Executes the BrushBg operation.
    /// </summary>
    private static readonly Brush BrushBg = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1E));
    /// <summary>
    /// Executes the BrushMutedBg operation.
    /// </summary>
    private static readonly Brush BrushMutedBg = new SolidColorBrush(Color.FromRgb(0x30, 0x20, 0x20));
    /// <summary>
    /// Executes the BrushSoloBg operation.
    /// </summary>
    private static readonly Brush BrushSoloBg = new SolidColorBrush(Color.FromRgb(0x20, 0x30, 0x20));
    private static readonly Pen PenSep = new(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x32)), 0.5);
    /// <summary>
    /// Executes the BrushText operation.
    /// </summary>
    private static readonly Brush BrushText = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xF0));

    static TrackHeaderPanel()
    {
        BrushBg.Freeze(); BrushMutedBg.Freeze(); BrushSoloBg.Freeze();
        PenSep.Freeze(); BrushText.Freeze();
    }

    public TrackHeaderPanel() { ClipToBounds = true; }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        double th = ViewModel.TrackHeight;

        dc.DrawRectangle(BrushBg, null, new Rect(0, 0, w, h));

        // Ruler spacer
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18)),
            null, new Rect(0, 0, w, RulerHeight));

        for (int ti = 0; ti < ViewModel.Tracks.Count; ti++)
        {
            double y = RulerHeight + ti * th - ScrollOffset;
            if (y + th < 0 || y > h) continue;

            var track = ViewModel.Tracks[ti];
            bool selected = track == ViewModel.SelectedTrack;

            // Lane background
            Brush bg = track.Muted ? BrushMutedBg : (selected
                ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32))
                : BrushBg);
            dc.DrawRectangle(bg, null, new Rect(0, y, w, th));

            // Colour chip
            var chipBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(track.Color >> 24), (byte)(track.Color >> 16),
                (byte)(track.Color >> 8), (byte)track.Color));
            dc.DrawRectangle(chipBrush, null, new Rect(2, y + (th - 4) / 2, 4, 4));

            // Track name
            double fontSize = Math.Min(th * 0.38, 13);
            var ft = new FormattedText(track.Name,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize,
                BrushText, 96);
            dc.PushClip(new RectangleGeometry(new Rect(10, y, w - 60, th)));
            dc.DrawText(ft, new Point(10, y + (th - ft.Height) / 2));
            dc.Pop();

            // Mute button ("M")
            DrawMiniButton(dc, w - 44, y + (th - 16) / 2, 16, "M",
                track.Muted ? Brushes.OrangeRed : null);

            // Solo button ("S")
            DrawMiniButton(dc, w - 24, y + (th - 16) / 2, 16, "S",
                track.Solo ? Brushes.LimeGreen : null);

            // Separator
            dc.DrawLine(PenSep, new Point(0, y + th), new Point(w, y + th));
        }
    }

    /// <summary>
    /// Executes the DrawMiniButton operation.
    /// </summary>
    private static void DrawMiniButton(DrawingContext dc, double x, double y,
        double size, string label, Brush? activeBg)
    {
        Brush bg = activeBg ?? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x32));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x50)), 0.5);
        dc.DrawRoundedRectangle(bg, pen, new Rect(x, y, size, size), 2, 2);
        var ft = new FormattedText(label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Bold"), 8,
            Brushes.White, 96);
        dc.DrawText(ft, new Point(x + (size - ft.Width) / 2, y + (size - ft.Height) / 2));
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        var pt = e.GetPosition(this);
        int ti = (int)((pt.Y + ScrollOffset - RulerHeight) / ViewModel.TrackHeight);
        if (ti < 0 || ti >= ViewModel.Tracks.Count) return;

        var track = ViewModel.Tracks[ti];
        double th = ViewModel.TrackHeight;
        double y = RulerHeight + ti * th - ScrollOffset;
        bool dataChanged = false;

        // Check Mute button hit
        if (pt.X >= ActualWidth - 44 && pt.X <= ActualWidth - 28)
        {
            ViewModel.BeginHistory("Toggle mute");
            track.Muted = !track.Muted;
            dataChanged = true;
            AppLogger.Info($"[TrackHeader] ToggleMute track={ti} name=\"{track.Name}\" muted={track.Muted}");
        }
        // Check Solo button hit
        else if (pt.X >= ActualWidth - 24 && pt.X <= ActualWidth - 8)
        {
            ViewModel.BeginHistory("Toggle solo");
            track.Solo = !track.Solo;
            dataChanged = true;
            AppLogger.Info($"[TrackHeader] ToggleSolo track={ti} name=\"{track.Name}\" solo={track.Solo}");
        }
        else
        {
            ViewModel.SelectedTrack = track;
            AppLogger.Debug($"[TrackHeader] SelectTrack track={ti} name=\"{track.Name}\"");
        }

        ViewModel.RaiseLayoutChanged();
        if (dataChanged)
        {
            ViewModel.RaiseSongDataChanged();
            ViewModel.CommitHistory();
        }
        InvalidateVisual();
    }
}
