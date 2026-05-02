using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace amChipper.App.Services;

/// <summary>
/// Applies theme colors to native Windows title bars when the host OS exposes DWM caption coloring.
/// </summary>
public static class WindowChromeTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>
    /// Hooks a WPF window so its native title bar follows the active amChipper theme.
    /// </summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
    }

    /// <summary>
    /// Applies the current resource colors to the specified window title bar.
    /// </summary>
    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;

        int darkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        int caption = ToColorRef(FindColor("BgDeep", Color.FromRgb(8, 10, 18)));
        int border = ToColorRef(FindColor("Accent", Color.FromRgb(50, 130, 255)));
        int text = ToColorRef(FindColor("TextPrimary", Colors.White));

        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
    }

    /// <summary>
    /// Reads a color from the active WPF resources.
    /// </summary>
    private static Color FindColor(string key, Color fallback)
    {
        return Application.Current.TryFindResource(key) switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            _ => fallback
        };
    }

    /// <summary>
    /// Converts a WPF RGB color to the COLORREF integer expected by DWM.
    /// </summary>
    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);
}
