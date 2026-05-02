using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.ViewModels;

namespace amChipper.App.Controls.PianoRoll;

/// <summary>
/// Renders the 128-note piano keyboard in the left gutter of the piano roll.
/// Clicking a key plays a preview note via the audio engine.
/// </summary>
public sealed class PianoKeysCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public PianoRollViewModel? ViewModel { get; set; }
    /// <summary>
    /// Stores or exposes ScrollOffset.
    /// </summary>
    public double ScrollOffset { get; set; }

    /// <summary>
    /// Stores or exposes IsBlackKey.
    /// </summary>
    private static readonly bool[] IsBlackKey =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    /// <summary>
    /// Executes the BrushWhite operation.
    /// </summary>
    private static readonly Brush BrushWhite = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF5, 0xFF), Color.FromRgb(0xB8, 0xBC, 0xD8), 90);
    /// <summary>
    /// Executes the BrushBlack operation.
    /// </summary>
    private static readonly Brush BrushBlack = new LinearGradientBrush(Color.FromRgb(0x36, 0x36, 0x48), Color.FromRgb(0x05, 0x05, 0x0B), 90);
    /// <summary>
    /// Executes the BrushWhiteH operation.
    /// </summary>
    private static readonly Brush BrushWhiteH = new LinearGradientBrush(Color.FromRgb(0x7E, 0xB7, 0xFF), Color.FromRgb(0x2F, 0x7E, 0xE8), 90);
    /// <summary>
    /// Executes the BrushBlackFade operation.
    /// </summary>
    private static readonly Brush BrushBlackFade = new LinearGradientBrush(
        [new GradientStop(Color.FromArgb(0xC8, 0x10, 0x10, 0x1A), 0),
         new GradientStop(Color.FromArgb(0x72, 0x18, 0x12, 0x25), 0.58),
         new GradientStop(Color.FromArgb(0x18, 0x5A, 0x9B, 0xFF), 1)],
        0);
    /// <summary>
    /// Executes the BrushCLabel operation.
    /// </summary>
    private static readonly Brush BrushCLabel = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x80));
    private static readonly Pen PenKey = new(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x30)), 0.5);

    /// <summary>
    /// Stores or exposes _hoveredPitch.
    /// </summary>
    private int _hoveredPitch = -1;
    private bool _mousePreviewing;

    static PianoKeysCanvas()
    {
        BrushWhite.Freeze(); BrushBlack.Freeze(); BrushBlackFade.Freeze();
        BrushWhiteH.Freeze(); BrushCLabel.Freeze();
        PenKey.Freeze();
    }

    public PianoKeysCanvas()
    {
        ClipToBounds = true;
    }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double rh = ViewModel.RowHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1E)), null,
            new Rect(0, 0, w, ActualHeight));

        double blackW = w * 0.55;

        for (int pitch = 127; pitch >= 0; pitch--)
        {
            double y = (127 - pitch) * rh - ScrollOffset;
            if (y + rh < 0 || y > ActualHeight) continue;

            bool black = IsBlackKey[pitch % 12];
            bool hovered = pitch == _hoveredPitch;
            bool isC = pitch % 12 == 0;

            if (!black)
            {
                Brush bg = hovered ? BrushWhiteH : BrushWhite;
                dc.DrawRectangle(bg, PenKey, new Rect(0, y, w, rh));

                if (isC && rh >= 8)
                {
                    string label = $"C{pitch / 12 - 1}";
                    var ft = new FormattedText(label,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Consolas"), Math.Min(rh * 0.6, 9),
                        BrushCLabel, 96);
                    dc.DrawText(ft, new Point(w - ft.Width - 2, y + (rh - ft.Height) / 2));
                }
            }
            else
            {
                Brush bg = hovered ? BrushWhiteH : BrushBlack;
                dc.DrawRectangle(BrushBlackFade, null, new Rect(0, y, w, rh));
                dc.DrawRoundedRectangle(bg, PenKey, new Rect(0, y + 0.5, blackW, Math.Max(1, rh - 1)), 0, 0);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)), null, new Rect(2, y + 1, Math.Max(1, blackW - 6), Math.Max(1, rh * 0.22)));
            }
        }
    }

    /// <summary>
    /// Executes the OnMouseMove operation.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (ViewModel is null) return;
        var pt = e.GetPosition(this);
        _hoveredPitch = 127 - (int)Math.Floor((pt.Y + ScrollOffset) / ViewModel.RowHeight);
        _hoveredPitch = Math.Clamp(_hoveredPitch, 0, 127);
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;

        Focus();
        CaptureMouse();
        var pt = e.GetPosition(this);
        int pitch = 127 - (int)Math.Floor((pt.Y + ScrollOffset) / ViewModel.RowHeight);
        pitch = Math.Clamp(pitch, 0, 127);
        _hoveredPitch = pitch;
        ViewModel.PreviewPitch(pitch);
        _mousePreviewing = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonUp operation.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        StopMousePreview();
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Executes the OnMouseLeave operation.
    /// </summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
            StopMousePreview();
        _hoveredPitch = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// Stops the held piano-key preview voice.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        StopMousePreview();
        base.OnLostMouseCapture(e);
    }

    private void StopMousePreview()
    {
        if (!_mousePreviewing)
            return;

        ViewModel?.StopPreviewPitch();
        _mousePreviewing = false;
    }
}
