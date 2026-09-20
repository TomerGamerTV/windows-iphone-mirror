using System.Windows.Input;

namespace iPhoneMirror.App;

internal static class KeyboardMapper
{
    public static int? ToHidUsage(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return 4 + (key - Key.A);
        if (key is >= Key.D1 and <= Key.D9) return 30 + (key - Key.D1);
        if (key == Key.D0) return 39;
        return key switch
        {
            Key.Enter => 40,
            Key.Escape => 41,
            Key.Back => 42,
            Key.Tab => 43,
            Key.Space => 44,
            Key.OemMinus => 45,
            Key.OemPlus => 46,
            Key.OemOpenBrackets => 47,
            Key.Oem6 => 48,
            Key.Oem5 => 49,
            Key.Oem1 => 51,
            Key.Oem7 => 52,
            Key.Oem3 => 53,
            Key.OemComma => 54,
            Key.OemPeriod => 55,
            Key.Oem2 => 56,
            Key.CapsLock => 57,
            Key.Insert => 73,
            Key.Home => 74,
            Key.PageUp => 75,
            Key.Delete => 76,
            Key.End => 77,
            Key.PageDown => 78,
            Key.Right => 79,
            Key.Left => 80,
            Key.Down => 81,
            Key.Up => 82,
            Key.LeftCtrl => 224,
            Key.LeftShift => 225,
            Key.LeftAlt => 226,
            Key.LWin => 227,
            Key.RightCtrl => 228,
            Key.RightShift => 229,
            Key.RightAlt => 230,
            Key.RWin => 231,
            _ => null,
        };
    }
}
