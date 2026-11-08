using System.Runtime.InteropServices;
using System.Text;

namespace Weather;

/// <summary>Win32 interop used by the overlay, window tracker and engine.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const uint WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                      WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
    const uint SPI_GETWORKAREA = 0x30;
    const uint ULW_ALPHA = 2;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint SWP_NOSIZE_ = 0x1, SWP_NOACTIVATE_ = 0x10, SWP_NOOWNERZORDER_ = 0x200, SWP_ASYNC_ = 0x4000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr ptDst, ref SIZE size, IntPtr src,
                                           ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);

    /// <summary>Push new pixels to a layered window without touching its position.</summary>
    public static void PresentInPlace(IntPtr hwnd, IntPtr screenDC, IntPtr memDC, int w, int h)
    {
        var src = new POINT();
        var size = new SIZE { cx = w, cy = h };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(hwnd, screenDC, IntPtr.Zero, ref size, memDC, ref src, 0, ref blend, ULW_ALPHA);
    }

    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);

    /// <summary>Display refresh rate in Hz (60 if unknown).</summary>
    public static int RefreshRate()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        int hz = GetDeviceCaps(dc, 116); // VREFRESH
        ReleaseDC(IntPtr.Zero, dc);
        return hz > 1 ? hz : 60;
    }
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref RECT r, uint winIni);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT ptDst, ref SIZE size, IntPtr src,
                                           ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);

    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    public static uint Style(IntPtr h) => (uint)GetWindowLongPtr(h, GWL_STYLE).ToInt64();
    public static uint ExStyle(IntPtr h) => (uint)GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Visible frame of a window (excludes the invisible resize borders Windows 10/11 adds).</summary>
    public static bool VisibleBounds(IntPtr h, out RECT r)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) == 0) return true;
        return GetWindowRect(h, out r);
    }

    public static bool IsCloaked(IntPtr h) =>
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int v, sizeof(int)) == 0 && v != 0;

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

    /// <summary>
    /// Primary monitor bounds and work area in real (physical) pixels. GetMonitorInfo is per-monitor
    /// DPI aware; the older GetSystemMetrics/SystemParametersInfo can return values scaled for the
    /// DPI Windows started with, which is wrong when display scaling has changed since sign-in
    /// (common on laptops) and put the taskbar snow too high.
    /// </summary>
    static bool PrimaryMonitor(out RECT monitor, out RECT work)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr mon = MonitorFromPoint(new POINT(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi)) { monitor = mi.rcMonitor; work = mi.rcWork; return true; }
        monitor = new RECT { Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        work = monitor;
        SystemParametersInfo(SPI_GETWORKAREA, 0, ref work, 0);
        return false;
    }

    public static RECT PrimaryScreen() { PrimaryMonitor(out var m, out _); return m; }

    public static RECT WorkArea() { PrimaryMonitor(out _, out var w); return w; }

    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);

    /// <summary>One-line summary of what the app sees, for the diagnostics file.</summary>
    public static string Describe(IntPtr taskbar)
    {
        PrimaryMonitor(out var m, out var w);
        var spi = new RECT();
        SystemParametersInfo(SPI_GETWORKAREA, 0, ref spi, 0);
        string tb = "none";
        if (taskbar != IntPtr.Zero && VisibleBounds(taskbar, out var d) && GetWindowRect(taskbar, out var g))
            tb = $"dwm=({d.Left},{d.Top},{d.Right},{d.Bottom}) rect=({g.Left},{g.Top},{g.Right},{g.Bottom}) dpi={GetDpiForWindow(taskbar)} topmost={IsTopmost(taskbar)}";
        int awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        return $"monitor=({m.Left},{m.Top},{m.Right},{m.Bottom}) work=({w.Left},{w.Top},{w.Right},{w.Bottom}) " +
               $"spiWork=({spi.Left},{spi.Top},{spi.Right},{spi.Bottom}) metrics={GetSystemMetrics(0)}x{GetSystemMetrics(1)} " +
               $"taskbar[{tb}] dpiAwareness={awareness}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);

    /// <summary>
    /// Keep a snow window always-on-top but directly *below* the taskbar, so the taskbar, its menus
    /// and flyouts, and any menu opened later stay in front of the snow.
    /// </summary>
    public static void PlaceBelow(IntPtr h, IntPtr above)
    {
        if (above == IntPtr.Zero) { KeepTopmost(h); return; }
        SetWindowPos(h, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    public static bool IsTopmost(IntPtr h) => (ExStyle(h) & WS_EX_TOPMOST) != 0;

    public static void KeepTopmost(IntPtr h) =>
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);

    public static void Present(IntPtr hwnd, IntPtr screenDC, IntPtr memDC, int w, int h, int x = 0, int y = 0)
    {
        var dst = new POINT { X = x, Y = y };
        var src = new POINT();
        var size = new SIZE { cx = w, cy = h };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(hwnd, screenDC, ref dst, ref size, memDC, ref src, 0, ref blend, ULW_ALPHA);
    }

    static readonly HashSet<string> ShellClasses = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    /// <summary>
    /// True when a full-screen app (game, F11 browser, video player, presentation) is in front.
    /// A normal maximized window with a title bar does not count; it still gets snow.
    /// </summary>
    public static bool ForegroundIsFullscreen(RECT screen, IntPtr self)
    {
        // Exclusive-mode games and presentation mode, as Windows itself reports them.
        if (SHQueryUserNotificationState(out int quns) == 0 &&
            (quns == QUNS_RUNNING_D3D_FULL_SCREEN || quns == QUNS_PRESENTATION_MODE))
            return true;

        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self) return false;
        if (ShellClasses.Contains(ClassName(fg))) return false;
        if (!GetWindowRect(fg, out var r)) return false;
        bool covers = r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        if (!covers) return false;

        // Covering the whole screen without being maximized means the app went full screen itself
        // (F11 in browsers, video players, borderless games), whatever window styles it kept.
        // A maximized window only counts if it has also dropped its title bar.
        return !IsZoomed(fg) || (Style(fg) & WS_CAPTION) != WS_CAPTION;
    }
}
