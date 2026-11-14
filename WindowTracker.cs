namespace Weather;

internal readonly record struct WinInfo(IntPtr Hwnd, Native.RECT Rect, bool Zoomed);

/// <summary>Lists the real, visible top-level windows in Z-order (topmost first).</summary>
internal sealed class WindowTracker
{
    static readonly HashSet<string> IgnoredClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "Xaml_WindowedPopupClass", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
        "#32768", "tooltips_class32", "ForegroundStaging", "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
    };

    // Tests: WEATHER_ONLY=title1|title2 tracks only those windows (so whatever else is open is ignored).
    static readonly HashSet<string>? Only = Environment.GetEnvironmentVariable("WEATHER_ONLY") is { Length: > 0 } o ? new(o.Split('|')) : null;

    readonly List<WinInfo> _list = new();
    readonly Dictionary<IntPtr, bool> _classOk = new();
    readonly Native.EnumWindowsProc _proc;
    readonly IntPtr _self, _self2;
    readonly uint _pid = (uint)Environment.ProcessId;
    Native.RECT _screen;
    int _calls;

    public WindowTracker(IntPtr self, IntPtr self2)
    {
        _self = self;
        _self2 = self2;
        _proc = Visit; // keep the delegate alive for the lifetime of the tracker
    }

    public List<WinInfo> Enumerate(Native.RECT screen)
    {
        if (++_calls % 1800 == 0) _classOk.Clear(); // HWNDs get recycled; refresh now and then
        _screen = screen;
        _list.Clear();
        Native.EnumWindows(_proc, IntPtr.Zero);
        return _list;
    }

    bool Visit(IntPtr h, IntPtr _)
    {
        if (h == _self || h == _self2 || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

        uint ex = Native.ExStyle(h);
        if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT)) != 0) return true;

        if (!_classOk.TryGetValue(h, out bool ok))
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            ok = pid != _pid && !IgnoredClasses.Contains(Native.ClassName(h)) && (Only == null || Only.Contains(Native.Title(h)));
            _classOk[h] = ok;
        }
        if (!ok || Native.IsCloaked(h)) return true;
        if (Native.GetWindowTextLength(h) == 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
        if (!Native.VisibleBounds(h, out var r)) return true;
        if (r.Width < 100 || r.Height < 40) return true;
        if (r.Right <= _screen.Left || r.Left >= _screen.Right || r.Bottom <= _screen.Top || r.Top >= _screen.Bottom) return true;

        _list.Add(new WinInfo(h, r, Native.IsZoomed(h)));
        return true;
    }
}
