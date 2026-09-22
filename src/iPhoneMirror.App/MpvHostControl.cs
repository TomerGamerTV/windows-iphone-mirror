using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace iPhoneMirror.App;

public sealed class MpvHostControl : HwndHost
{
    private static readonly object InputClassLock = new();
    private static readonly Dictionary<IntPtr, WeakReference<MpvHostControl>> InputOwners = [];
    private static readonly WndProcDelegate InputWndProcDelegate = InputWndProc;
    private static ushort _inputClassAtom;

    private IntPtr _hwnd;
    private IntPtr _inputHwnd;
    private bool _viewerToolbarVisible;
    private bool _mouseCaptured;

    public IntPtr HostHandle => _hwnd;
    public event EventHandler<NativeMouseEventArgs>? NativeMouse;
    public event EventHandler<ViewerToolbarActionEventArgs>? ViewerToolbarAction;
    public event EventHandler<NativeKeyEventArgs>? NativeKeyDown;
    public event EventHandler<NativeKeyEventArgs>? NativeKeyUp;
    public bool HasNativeMouseCapture => _mouseCaptured;

    public void SetViewerToolbarVisible(bool visible)
    {
        if (_viewerToolbarVisible == visible) return;
        _viewerToolbarVisible = visible;
        if (_inputHwnd != IntPtr.Zero)
        {
            InvalidateRect(_inputHwnd, IntPtr.Zero, false);
            UpdateWindow(_inputHwnd);
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "static", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        CreateInputOverlay();
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_inputHwnd != IntPtr.Zero)
        {
            lock (InputOwners) InputOwners.Remove(_inputHwnd);
            DestroyWindow(_inputHwnd);
            _inputHwnd = IntPtr.Zero;
        }
        if (hwnd.Handle != IntPtr.Zero) DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    public void BringInputOverlayToFront()
    {
        if (_hwnd == IntPtr.Zero || _inputHwnd == IntPtr.Zero) return;
        if (!GetClientRect(_hwnd, out var rect)) return;
        SetWindowPos(_inputHwnd, HWND_TOP, 0, 0,
            Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top),
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void CreateInputOverlay()
    {
        EnsureInputClass();
        GetClientRect(_hwnd, out var rect);
        _inputHwnd = CreateWindowEx(
            0,
            InputClassName,
            string.Empty,
            WS_CHILD | WS_VISIBLE,
            0,
            0,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top),
            _hwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (_inputHwnd == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the video input overlay.");
        }
        lock (InputOwners) InputOwners[_inputHwnd] = new WeakReference<MpvHostControl>(this);
        BringInputOverlayToFront();
    }

    private static void EnsureInputClass()
    {
        if (_inputClassAtom != 0) return;
        lock (InputClassLock)
        {
            if (_inputClassAtom != 0) return;
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(InputWndProcDelegate),
                hInstance = GetModuleHandle(null),
                lpszClassName = InputClassName,
            };
            _inputClassAtom = RegisterClassEx(ref windowClass);
            if (_inputClassAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ERROR_CLASS_ALREADY_EXISTS)
                    throw new System.ComponentModel.Win32Exception(error, "Could not register the video input overlay window class.");
                _inputClassAtom = 1;
            }
        }
    }

    private static IntPtr InputWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        MpvHostControl? owner = null;
        lock (InputOwners)
        {
            if (InputOwners.TryGetValue(hwnd, out var weak)) weak.TryGetTarget(out owner);
        }
        if (owner is null) return DefWindowProc(hwnd, msg, wParam, lParam);

        switch (msg)
        {
            case WM_ERASEBKGND:
                return new IntPtr(1);
            case WM_PAINT:
                var paintHdc = BeginPaint(hwnd, out var paint);
                owner.PaintViewerToolbar(paintHdc);
                EndPaint(hwnd, ref paint);
                return IntPtr.Zero;
            case WM_LBUTTONDOWN:
                if (owner.TryHandleViewerToolbarClick(lParam)) return IntPtr.Zero;
                SetFocus(owner._hwnd);
                SetCapture(hwnd);
                owner._mouseCaptured = true;
                owner.RaiseMouse("down", lParam, 0);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                owner.RaiseMouse("up", lParam, 0);
                owner._mouseCaptured = false;
                ReleaseCapture();
                return IntPtr.Zero;
            case WM_CAPTURECHANGED:
            case WM_CANCELMODE:
                if (owner._mouseCaptured)
                {
                    owner._mouseCaptured = false;
                    owner.RaiseMouse("cancel", 0, 0, 0);
                }
                return IntPtr.Zero;
            case WM_MOUSEMOVE:
                owner.RaiseMouse("move", lParam, 0);
                return IntPtr.Zero;
            case WM_MOUSEWHEEL:
                var point = new POINT { X = SignedLowWord(lParam), Y = SignedHighWord(lParam) };
                ScreenToClient(hwnd, ref point);
                owner.RaiseMouse("wheel", point.X, point.Y, SignedHighWord(wParam));
                return IntPtr.Zero;
            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
                owner.RaiseKey(down: true, wParam, lParam);
                return IntPtr.Zero;
            case WM_KEYUP:
            case WM_SYSKEYUP:
                owner.RaiseKey(down: false, wParam, lParam);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_SIZE:
                BringInputOverlayToFront();
                break;
            case WM_PARENTNOTIFY:
                if ((wParam.ToInt64() & 0xFFFF) == WM_CREATE)
                    BringInputOverlayToFront();
                break;
            case WM_LBUTTONDOWN:
                SetFocus(hwnd);
                SetCapture(hwnd);
                _mouseCaptured = true;
                RaiseMouse("down", lParam, 0);
                handled = true;
                break;
            case WM_LBUTTONUP:
                RaiseMouse("up", lParam, 0);
                _mouseCaptured = false;
                ReleaseCapture();
                handled = true;
                break;
            case WM_CAPTURECHANGED:
            case WM_CANCELMODE:
                if (_mouseCaptured)
                {
                    _mouseCaptured = false;
                    RaiseMouse("cancel", 0, 0, 0);
                }
                handled = true;
                break;
            case WM_MOUSEMOVE:
                RaiseMouse("move", lParam, 0);
                break;
            case WM_MOUSEWHEEL:
                var wheelPoint = new POINT { X = SignedLowWord(lParam), Y = SignedHighWord(lParam) };
                ScreenToClient(hwnd, ref wheelPoint);
                RaiseMouse("wheel", wheelPoint.X, wheelPoint.Y, SignedHighWord(wParam));
                handled = true;
                break;
            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
                RaiseKey(down: true, wParam, lParam);
                handled = true;
                break;
            case WM_KEYUP:
            case WM_SYSKEYUP:
                RaiseKey(down: false, wParam, lParam);
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private void RaiseKey(bool down, IntPtr wParam, IntPtr lParam)
    {
        var virtualKey = wParam.ToInt32() & 0xFFFF;
        var repeat = ((long)lParam & 0x40000000) != 0;
        var args = new NativeKeyEventArgs(virtualKey, repeat);
        if (down) NativeKeyDown?.Invoke(this, args);
        else NativeKeyUp?.Invoke(this, args);
    }

    private void RaiseMouse(string kind, IntPtr lParam, int delta) =>
        RaiseMouse(kind, SignedLowWord(lParam), SignedHighWord(lParam), delta);

    private void RaiseMouse(string kind, int x, int y, int delta)
    {
        GetClientRect(_hwnd, out var rect);
        NativeMouse?.Invoke(this, new NativeMouseEventArgs(kind, x, y, rect.Right - rect.Left, rect.Bottom - rect.Top, delta));
    }

    private bool TryHandleViewerToolbarClick(IntPtr lParam)
    {
        if (!_viewerToolbarVisible) return false;
        var x = SignedLowWord(lParam);
        var y = SignedHighWord(lParam);
        GetClientRect(_hwnd, out var rect);
        var center = (rect.Right - rect.Left) / 2;
        if (y < 12 || y > 52) return false;
        if (x >= center - 38 && x < center - 4)
        {
            ViewerToolbarAction?.Invoke(this, new ViewerToolbarActionEventArgs("home"));
            return true;
        }
        if (x >= center + 4 && x < center + 38)
        {
            ViewerToolbarAction?.Invoke(this, new ViewerToolbarActionEventArgs("app_switcher"));
            return true;
        }
        return false;
    }

    private void PaintViewerToolbar(IntPtr hdc)
    {
        if (!_viewerToolbarVisible) return;
        GetClientRect(_hwnd, out var rect);
        var center = (rect.Right - rect.Left) / 2;
        var toolbar = new RECT { Left = center - 46, Top = 8, Right = center + 46, Bottom = 56 };
        var home = new RECT { Left = center - 38, Top = 12, Right = center - 4, Bottom = 52 };
        var switcher = new RECT { Left = center + 4, Top = 12, Right = center + 38, Bottom = 52 };
        using var background = new NativeBrush(0x001D1A17);
        using var button = new NativeBrush(0x00332E2A);
        FillRect(hdc, ref toolbar, background.Handle);
        FillRect(hdc, ref home, button.Handle);
        FillRect(hdc, ref switcher, button.Handle);
        using var pen = new NativePen(0x00FFFFFF, 3);
        var previous = SelectObject(hdc, pen.Handle);
        SetBkMode(hdc, 1);
        DrawHomeIcon(hdc, home);
        DrawAppGridIcon(hdc, switcher);
        SelectObject(hdc, previous);
    }

    private static void DrawHomeIcon(IntPtr hdc, RECT rect)
    {
        var centerX = (rect.Left + rect.Right) / 2;
        var roofY = rect.Top + 14;
        var wallY = rect.Top + 21;
        var bottomY = rect.Bottom - 10;
        MoveToEx(hdc, centerX - 10, roofY, IntPtr.Zero);
        LineTo(hdc, centerX, rect.Top + 4);
        LineTo(hdc, centerX + 10, roofY);
        LineTo(hdc, centerX + 9, bottomY);
        LineTo(hdc, centerX - 9, bottomY);
        LineTo(hdc, centerX - 9, roofY);
        MoveToEx(hdc, centerX - 2, bottomY, IntPtr.Zero);
        LineTo(hdc, centerX - 2, wallY);
        LineTo(hdc, centerX + 2, wallY);
        LineTo(hdc, centerX + 2, bottomY);
    }

    private static void DrawAppGridIcon(IntPtr hdc, RECT rect)
    {
        var centerX = (rect.Left + rect.Right) / 2;
        var centerY = (rect.Top + rect.Bottom) / 2;
        const int size = 8;
        const int gap = 2;
        for (var row = -1; row <= 1; row += 2)
        {
            for (var column = -1; column <= 1; column += 2)
            {
                var left = centerX + column * (size / 2 + gap) - size / 2;
                var top = centerY + row * (size / 2 + gap) - size / 2;
                Rectangle(hdc, left, top, left + size, top + size);
            }
        }
    }

    private static int SignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));
    private static int SignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WM_CREATE = 0x0001;
    private const int WM_SIZE = 0x0005;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CANCELMODE = 0x001F;
    private const int WM_CAPTURECHANGED = 0x0215;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const string InputClassName = "iPhoneMirror.InputOverlay";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr previousPoint);
    [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objectHandle);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objectHandle);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);

    private sealed class NativeBrush(uint color) : IDisposable
    {
        public IntPtr Handle { get; } = CreateSolidBrush(color);
        public void Dispose() => DeleteObject(Handle);
    }

    private sealed class NativePen(uint color, int width) : IDisposable
    {
        public IntPtr Handle { get; } = CreatePen(0, width, color);
        public void Dispose() => DeleteObject(Handle);
    }
}

public sealed record NativeMouseEventArgs(string Kind, int X, int Y, int Width, int Height, int Delta);
public sealed record ViewerToolbarActionEventArgs(string Action);
public sealed record NativeKeyEventArgs(int VirtualKey, bool IsRepeat);
