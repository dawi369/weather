using System.Diagnostics;

namespace Windowsill;

/// <param name="NoSnow">
/// The window hides what's behind it like any other, but nothing settles on it (this app's own
/// windows, and apps the user asked to ignore, such as pop-up voice or capture overlays).
/// </param>
internal readonly record struct WinInfo(IntPtr Hwnd, Native.RECT Rect, bool Zoomed, bool NoSnow);

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

    // Tests: WINDOWSILL_ONLY=title1|title2 tracks only those windows (so whatever else is open is ignored).
    static readonly HashSet<string>? Only = Environment.GetEnvironmentVariable("WINDOWSILL_ONLY") is { Length: > 0 } o ? new(o.Split('|')) : null;

    const byte Skip = 0, Normal = 1, NoSnow = 2;

    readonly List<WinInfo> _list = new();
    readonly Dictionary<IntPtr, byte> _kind = new();
    readonly Dictionary<uint, string> _procNames = new();
    readonly Native.EnumWindowsProc _proc;
    readonly IntPtr _self, _self2;
    readonly uint _pid = (uint)Environment.ProcessId;
    readonly Func<IReadOnlyList<string>> _ignored;
    IReadOnlyList<string>? _ignoredSeen;
    Native.RECT _screen;
    int _calls;

    public WindowTracker(IntPtr self, IntPtr self2, Func<IReadOnlyList<string>> ignoredApps)
    {
        _self = self;
        _self2 = self2;
        _ignored = ignoredApps;
        _proc = Visit; // keep the delegate alive for the lifetime of the tracker
    }

    public List<WinInfo> Enumerate(Native.RECT screen)
    {
        var ignored = _ignored();
        if (!ReferenceEquals(ignored, _ignoredSeen)) { _ignoredSeen = ignored; _kind.Clear(); } // the list changed
        if (++_calls % 1800 == 0) { _kind.Clear(); _procNames.Clear(); } // HWNDs and PIDs get recycled
        _screen = screen;
        _list.Clear();
        Native.EnumWindows(_proc, IntPtr.Zero);
        return _list;
    }

    string ProcessName(uint pid)
    {
        if (_procNames.TryGetValue(pid, out var name)) return name;
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
        catch { name = ""; }
        return _procNames[pid] = name;
    }

    byte Classify(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (IgnoredClasses.Contains(Native.ClassName(h))) return Skip;
        if (Only != null && pid != _pid && !Only.Contains(Native.Title(h))) return Skip;
        if (pid == _pid) return NoSnow; // our own settings and welcome windows
        var ignored = _ignoredSeen;
        if (ignored != null && ignored.Count > 0)
        {
            string name = ProcessName(pid);
            foreach (var app in ignored)
                if (app.Length > 0 && name.Equals(app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app, StringComparison.OrdinalIgnoreCase))
                    return NoSnow;
        }
        return Normal;
    }

    bool Visit(IntPtr h, IntPtr _)
    {
        if (h == _self || h == _self2 || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

        uint ex = Native.ExStyle(h);
        if (!_kind.TryGetValue(h, out byte kind)) _kind[h] = kind = Classify(h);
        if (kind == Skip || Native.IsCloaked(h)) return true;
        // Tool windows and untitled pop-ups are normally ignored; from ignored apps they still hide
        // what's behind them (a voice or capture overlay, say).
        if (kind == Normal)
        {
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT)) != 0) return true;
            if (Native.GetWindowTextLength(h) == 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
        }
        else if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
        if (!Native.VisibleBounds(h, out var r)) return true;
        if (r.Width < 40 || r.Height < 20 || kind == Normal && (r.Width < 100 || r.Height < 40)) return true;
        if (r.Right <= _screen.Left || r.Left >= _screen.Right || r.Bottom <= _screen.Top || r.Top >= _screen.Bottom) return true;

        _list.Add(new WinInfo(h, r, Native.IsZoomed(h), kind == NoSnow));
        return true;
    }
}
