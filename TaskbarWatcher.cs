namespace Weather;

/// <summary>
/// Knows where the top of the taskbar is, and reports the instant it moves: a WinEvent hook fires on
/// every taskbar location change (each step of an auto-hide slide) and signals <see cref="Moved"/>,
/// which wakes the snow thread to redraw and move the taskbar snow right away.
/// Create on the UI thread; the hook callback runs there.
/// </summary>
internal sealed class TaskbarWatcher : IDisposable
{
    readonly Native.WinEventProc _proc; // keep alive

    /// <summary>Signalled the instant the taskbar moves; the snow thread waits on it.</summary>
    public readonly AutoResetEvent Moved = new(false);
    IntPtr _hook, _hwnd;
    uint _hookedPid;

    public IntPtr Hwnd => _hwnd;

    public TaskbarWatcher()
    {
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
        Moved.Set(); // wake the snow thread: it redraws and moves the taskbar snow in one step
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
