using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.PatternEditor;

/// <summary>
/// Custom-rendered canvas for the tracker-style pattern grid.
/// Columns: Row# | [Note Inst Vol Effect] × channels
/// Rows scroll vertically; the cursor row is highlighted.
/// </summary>
public sealed class PatternGridCanvas : FrameworkElement
{
    /// <summary>
    /// Stores or exposes ViewModel.
    /// </summary>
    public PatternEditorViewModel? ViewModel { get; set; }

    // Layout constants
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double RowH = 18.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double RowNumW = 32.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double NoteW = 36.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double InstW = 22.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double VolW = 22.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double EffW = 34.0;
    /// <summary>
    /// Stores or exposes double.
    /// </summary>
    private const double SepW = 6.0;
    /// <summary>
    /// Stores or exposes ChannelW.
    /// </summary>
    private static double ChannelW => NoteW + InstW + VolW + EffW + SepW;

    // Brushes
    /// <summary>
    /// Executes the BgEven operation.
    /// </summary>
    private static readonly Brush BgEven = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1E));
    /// <summary>
    /// Executes the BgOdd operation.
    /// </summary>
    private static readonly Brush BgOdd = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18));
    /// <summary>
    /// Executes the BgBar operation.
    /// </summary>
    private static readonly Brush BgBar = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x28));
    /// <summary>
    /// Executes the BgCursor operation.
    /// </summary>
    private static readonly Brush BgCursor = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5A));
    /// <summary>
    /// Executes the BgHeader operation.
    /// </summary>
    private static readonly Brush BgHeader = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x22));
    /// <summary>
    /// Executes the FgRowNum operation.
    /// </summary>
    private static readonly Brush FgRowNum = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x66));
    /// <summary>
    /// Executes the FgNote operation.
    /// </summary>
    private static readonly Brush FgNote = new SolidColorBrush(Color.FromRgb(0xA0, 0xD0, 0xFF));
    /// <summary>
    /// Executes the FgInst operation.
    /// </summary>
    private static readonly Brush FgInst = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
    /// <summary>
    /// Executes the FgVol operation.
    /// </summary>
    private static readonly Brush FgVol = new SolidColorBrush(Color.FromRgb(0x44, 0xFF, 0x88));
    /// <summary>
    /// Executes the FgEff operation.
    /// </summary>
    private static readonly Brush FgEff = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44));
    /// <summary>
    /// Executes the FgEmpty operation.
    /// </summary>
    private static readonly Brush FgEmpty = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
    /// <summary>
    /// Executes the FgHeader operation.
    /// </summary>
    private static readonly Brush FgHeader = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x88));
    private static readonly Pen PenSep = new(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)), 0.5);
    private static readonly Pen PenCursor = new(new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)), 1.5);

    private static readonly Typeface MonoFace = new("Consolas");

    /// <summary>
    /// Stores or exposes _scrollOffset.
    /// </summary>
    private double _scrollOffset; // vertical scroll in pixels

    static PatternGridCanvas()
    {
        BgEven.Freeze(); BgOdd.Freeze(); BgBar.Freeze(); BgCursor.Freeze();
        BgHeader.Freeze(); FgRowNum.Freeze(); FgNote.Freeze(); FgInst.Freeze();
        FgVol.Freeze(); FgEff.Freeze(); FgEmpty.Freeze(); FgHeader.Freeze();
        PenSep.Freeze(); PenCursor.Freeze();
    }

    public PatternGridCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>
    /// Executes the OnRender operation.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (ViewModel is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        dc.DrawRectangle(BgEven, null, new Rect(0, 0, w, h));

        int nCh = ViewModel.ChannelCount;
        if (nCh == 0) { DrawEmpty(dc, w, h); return; }

        double headerH = RowH;

        // ── Column headers ────────────────────────────────────────────────────
        dc.DrawRectangle(BgHeader, null, new Rect(0, 0, w, headerH));
        DrawText(dc, ViewModel["Row"].ToUpperInvariant(), RowNumW * 0.5, headerH * 0.5, FgHeader, TextAlignment.Center);
        for (int ch = 0; ch < nCh; ch++)
        {
            double cx = RowNumW + ch * ChannelW;
            DrawText(dc, $"{ViewModel["Channel"].ToUpperInvariant()}{ch + 1}", cx + ChannelW * 0.5, headerH * 0.5, FgHeader, TextAlignment.Center);
            dc.DrawLine(PenSep, new Point(cx - 1, 0), new Point(cx - 1, h));
        }
        dc.DrawLine(PenSep, new Point(0, headerH), new Point(w, headerH));

        // ── Rows ──────────────────────────────────────────────────────────────
        int firstRow = (int)(_scrollOffset / RowH);
        int visRows = (int)(h / RowH) + 2;
        var rows = ViewModel.Rows;

        for (int ri = firstRow; ri < Math.Min(firstRow + visRows, rows.Count); ri++)
        {
            double y = headerH + ri * RowH - _scrollOffset;
            var row = rows[ri];
            bool cursor = ri == ViewModel.CurrentRow;

            // Row background
            Brush bg = cursor ? BgCursor : (row.IsBarStart ? BgBar : (ri % 2 == 0 ? BgEven : BgOdd));
            dc.DrawRectangle(bg, null, new Rect(0, y, w, RowH));

            // Row number
            DrawText(dc, row.RowLabel, RowNumW * 0.5, y + RowH * 0.5, FgRowNum, TextAlignment.Center);

            // Cells per channel
            for (int ch = 0; ch < row.Cells.Count; ch++)
            {
                double cx = RowNumW + ch * ChannelW;
                bool curCh = cursor && ch == ViewModel.CurrentChannel;
                var cell = row.Cells[ch];

                DrawCell(dc, cx, y, cell, curCh, ch);
            }

            // Cursor border
            if (cursor)
                dc.DrawRectangle(null, PenCursor, new Rect(0, y, w, RowH));
        }

        // Scroll bar (simple manual draw)
        DrawScrollThumb(dc, rows.Count, firstRow, visRows, w, h, headerH);
    }

    /// <summary>
    /// Executes the DrawCell operation.
    /// </summary>
    private void DrawCell(DrawingContext dc, double cx, double y,
        PatternCell cell, bool cursorOnChannel, int channelIndex)
    {
        bool empty = cell.NoteStr == "---" && cell.InstStr == "--";

        Brush noteBrush = empty ? FgEmpty : FgNote;
        Brush instBrush = empty ? FgEmpty : FgInst;
        Brush volBrush = cell.VolStr == "--" ? FgEmpty : FgVol;
        Brush effBrush = cell.EffStr == ".00" ? FgEmpty : FgEff;

        DrawText(dc, cell.NoteStr, cx + 2, y + RowH * 0.5, noteBrush, TextAlignment.Left);
        DrawText(dc, cell.InstStr, cx + NoteW + 2, y + RowH * 0.5, instBrush, TextAlignment.Left);
        DrawText(dc, cell.VolStr, cx + NoteW + InstW + 2, y + RowH * 0.5, volBrush, TextAlignment.Left);
        DrawText(dc, cell.EffStr, cx + NoteW + InstW + VolW + 2, y + RowH * 0.5, effBrush, TextAlignment.Left);
    }

    /// <summary>
    /// Executes the DrawText operation.
    /// </summary>
    private static void DrawText(DrawingContext dc, string text, double cx, double cy,
        Brush foreground, TextAlignment align)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, MonoFace, 11, foreground, 96);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(cx, cy - ft.Height / 2));
    }

    /// <summary>
    /// Executes the DrawEmpty operation.
    /// </summary>
    private void DrawEmpty(DrawingContext dc, double w, double h)
    {
        var ft = new FormattedText(ViewModel?["NoPatternLoaded"] ?? "No pattern loaded",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, MonoFace, 14, FgEmpty, 96);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }

    /// <summary>
    /// Executes the DrawScrollThumb operation.
    /// </summary>
    private static void DrawScrollThumb(DrawingContext dc, int totalRows, int firstRow,
        int visRows, double w, double h, double headerH)
    {
        if (totalRows <= visRows) return;
        double trackH = h - headerH;
        double thumbH = Math.Max(20, trackH * visRows / totalRows);
        double thumbY = headerH + trackH * firstRow / totalRows;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x44)),
            null, new Rect(w - 6, thumbY, 5, thumbH));
    }

    // ── Mouse input ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the OnMouseWheel operation.
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _scrollOffset = Math.Max(0, _scrollOffset - e.Delta * 0.5);
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnMouseLeftButtonDown operation.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        Focus();
        var pt = e.GetPosition(this);
        int row = (int)((_scrollOffset + pt.Y - RowH) / RowH);
        int ch = (int)((pt.X - RowNumW) / ChannelW);

        if (row >= 0 && row < ViewModel.Rows.Count)
            ViewModel.CurrentRow = row;
        if (ch >= 0 && ch < ViewModel.ChannelCount)
            ViewModel.CurrentChannel = ch;

        // Scroll cursor into view
        EnsureCursorVisible();
        InvalidateVisual();
    }

    /// <summary>
    /// Executes the OnKeyDown operation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Bubble up to parent PatternEditorControl for key handling
        e.Handled = false;
    }

    /// <summary>
    /// Executes the EnsureCursorVisible operation.
    /// </summary>
    public void EnsureCursorVisible()
    {
        if (ViewModel is null) return;
        double cursorY = RowH + ViewModel.CurrentRow * RowH;
        double viewH = ActualHeight;
        if (cursorY < _scrollOffset + RowH)
            _scrollOffset = Math.Max(0, cursorY - RowH);
        else if (cursorY + RowH > _scrollOffset + viewH)
            _scrollOffset = cursorY + RowH - viewH;
        InvalidateVisual();
    }

    /// <summary>
    /// Centre the current row in the visible area (used during playback so the
    /// cursor stays in the middle of the grid rather than at the edge).
    /// </summary>
    public void CentreOnCurrentRow()
    {
        if (ViewModel is null) return;
        double cursorY = RowH + ViewModel.CurrentRow * RowH;
        double viewH = ActualHeight;
        _scrollOffset = Math.Max(0, cursorY - viewH / 2);
        InvalidateVisual();
    }
}
