using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace iPhoneMirror.App;

internal static class NativeTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static void Apply(Window window)
    {
        var dark = IsDarkTheme();
        var handle = new WindowInteropHelper(window).Handle;
        var darkValue = dark ? 1 : 0;
        var micaValue = 2;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref micaValue, sizeof(int));

        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            resources["WindowBrush"] = SystemColors.WindowBrush;
            resources["SurfaceBrush"] = SystemColors.ControlBrush;
            resources["SurfaceAltBrush"] = SystemColors.ControlLightBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["MutedTextBrush"] = SystemColors.GrayTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["DangerBrush"] = SystemColors.HotTrackBrush;
            return;
        }
        resources["WindowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(17, 19, 21) : Color.FromRgb(243, 246, 249));
        resources["SurfaceBrush"] = new SolidColorBrush(dark ? Color.FromRgb(23, 26, 29) : Colors.White);
        resources["SurfaceAltBrush"] = new SolidColorBrush(dark ? Color.FromRgb(32, 36, 40) : Color.FromRgb(232, 237, 242));
        resources["TextBrush"] = new SolidColorBrush(dark ? Colors.WhiteSmoke : Color.FromRgb(24, 28, 32));
        resources["MutedTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(170, 177, 184) : Color.FromRgb(88, 96, 104));
        resources["AccentBrush"] = new SolidColorBrush(SystemParameters.WindowGlassColor);
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
