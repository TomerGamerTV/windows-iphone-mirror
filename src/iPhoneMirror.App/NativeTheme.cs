using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace iPhoneMirror.App;

public enum BackdropType
{
    Auto = 0,
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4
}

internal static class NativeTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static void Apply(Window window, BackdropType backdrop = BackdropType.Mica)
    {
        var dark = IsDarkTheme();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var darkValue = dark ? 1 : 0;
            var backdropValue = (int)backdrop;
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropValue, sizeof(int));
        }

        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            resources["WindowBrush"] = SystemColors.WindowBrush;
            resources["SurfaceBrush"] = SystemColors.ControlBrush;
            resources["SurfaceAltBrush"] = SystemColors.ControlLightBrush;
            resources["ToolbarGlassBrush"] = SystemColors.ControlBrush;
            resources["GlassBorderBrush"] = SystemColors.ControlDarkBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["MutedTextBrush"] = SystemColors.GrayTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["DangerBrush"] = SystemColors.HotTrackBrush;
            return;
        }

        // When using a system backdrop (Mica / Acrylic), translucent backgrounds allow the DWM effect to shine through
        var useBackdrop = backdrop is BackdropType.Mica or BackdropType.Acrylic or BackdropType.MicaAlt;
        if (useBackdrop)
        {
            resources["WindowBrush"] = new SolidColorBrush(dark ? Color.FromArgb(175, 17, 19, 23) : Color.FromArgb(185, 243, 246, 249));
            resources["SurfaceBrush"] = new SolidColorBrush(dark ? Color.FromArgb(150, 25, 28, 33) : Color.FromArgb(185, 255, 255, 255));
            resources["SurfaceAltBrush"] = new SolidColorBrush(dark ? Color.FromArgb(170, 36, 40, 48) : Color.FromArgb(195, 232, 237, 242));
        }
        else
        {
            resources["WindowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(17, 19, 21) : Color.FromRgb(243, 246, 249));
            resources["SurfaceBrush"] = new SolidColorBrush(dark ? Color.FromRgb(23, 26, 29) : Colors.White);
            resources["SurfaceAltBrush"] = new SolidColorBrush(dark ? Color.FromRgb(32, 36, 40) : Color.FromRgb(232, 237, 242));
        }

        // Floating Liquid Glass brushes (translucent with specular rim lighting)
        resources["ToolbarGlassBrush"] = new SolidColorBrush(dark ? Color.FromArgb(195, 28, 32, 38) : Color.FromArgb(220, 248, 250, 253));
        resources["GlassBorderBrush"] = new SolidColorBrush(dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
        resources["TextBrush"] = new SolidColorBrush(dark ? Colors.WhiteSmoke : Color.FromRgb(24, 28, 32));
        resources["MutedTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(170, 177, 184) : Color.FromRgb(88, 96, 104));
        resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(10, 132, 255));
        resources["DangerBrush"] = new SolidColorBrush(Color.FromRgb(255, 69, 58));
    }

    public static bool IsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return !Equals(key?.GetValue("AppsUseLightTheme"), 1);
        }
        catch
        {
            return true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
