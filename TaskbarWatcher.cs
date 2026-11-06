namespace Weather;

/// <summary>
/// Knows where the top of the taskbar is, and moves the <see cref="GroundLayer"/> the instant the
/// taskbar moves: a WinEvent hook reports every taskbar location change (e.g. each step of an
/// auto-hide slide) and the snow window is moved right in that callback, with no frame in between.
/// Create on the UI thread; the hook callback runs there.
/// </summary>
internal sealed class TaskbarWatcher : IDisposable
{
    readonly GroundLayer _layer;
    readonly Native.WinEventProc _proc; // keep alive
    IntPtr _hook, _hwnd;
    uint _hookedPid;

    public IntPtr Hwnd => _hwnd;

    public TaskbarWatcher(GroundLayer layer)
    {
        _layer = layer;
        _proc = OnEvent;
        Rehook();
    }

    void Rehook()
    {
        _hwnd = Native.FindWindow("Shell_TrayWnd", null);
        if (_hwnd == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(_hwnd, out uint pid);
        if (pid == _hookedPid && _hook != IntPtr.Zero) return;
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _hook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                                       IntPtr.Zero, _proc, pid, 0, 0 /* out of context */);
        _hookedPid = pid;
    }

    void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd != _hwnd || idObject != 0) return;
        var screen = Native.PrimaryScreen();
        _layer.MoveTo(GroundY(screen, Native.WorkArea()), async: false);
    }

    /// <summary>
    /// Where snow at the bottom rests: the top of a bottom taskbar (wherever it is in its auto-hide
    /// slide), otherwise the bottom of the work area. Safe from any thread.
    /// </summary>
    public int GroundY(Native.RECT screen, Native.RECT workArea)
    {
        IntPtr h = _hwnd;
        if (h == IntPtr.Zero || !Native.IsWindow(h)) return workArea.Bottom;
        if (!Native.IsWindowVisible(h) || !Native.VisibleBounds(h, out var r)) return workArea.Bottom;
        if (r.Width < screen.Width / 2 || r.Top <= screen.Height / 2 || r.Top >= screen.Bottom) return workArea.Bottom;
        return Math.Min(workArea.Bottom, r.Top);
    }

    /// <summary>Called now and then from the UI thread: re-find the taskbar if Explorer restarted.</summary>
    public void Refresh()
    {
        if (_hwnd == IntPtr.Zero || !Native.IsWindow(_hwnd)) Rehook();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
