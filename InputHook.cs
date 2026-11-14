using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Weather;

/// <summary>
/// Lets you play with the weather: while Ctrl+Alt are held, mouse clicks and drags go to the
/// simulation instead of the window under the mouse (a low-level mouse hook). Without Ctrl+Alt
/// nothing is touched, and plain mouse moves always pass straight through.
/// </summary>
internal sealed class InputHook : IDisposable
{
    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
    const int VK_CONTROL = 0x11, VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc;
    readonly Func<SnowSim?> _sim;
    readonly Func<bool> _enabled;
    IntPtr _hook;
    bool _leftDown, _rightDown; // a press we swallowed: swallow its release too

    public InputHook(Func<SnowSim?> sim, Func<bool> enabled)
    {
        _sim = sim;
        _enabled = enabled;
        _proc = Callback; // keep the delegate alive
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            bool chord = Down(VK_CONTROL) && Down(VK_MENU);
            var sim = _sim();
            switch (msg)
            {
                case WM_MOUSEMOVE:
                    if (_leftDown) sim?.Enqueue(new PlayCommand(PlayKind.Move, m.X, m.Y, Now));
                    break; // moves always pass through
                case WM_LBUTTONDOWN when chord && _enabled() && sim != null:
                    _leftDown = true;
                    sim.Enqueue(new PlayCommand(PlayKind.Down, m.X, m.Y, Now));
                    return 1;
                case WM_LBUTTONUP when _leftDown:
                    _leftDown = false;
                    sim?.Enqueue(new PlayCommand(PlayKind.Up, m.X, m.Y, Now));
                    return 1;
                case WM_RBUTTONDOWN when chord && _enabled() && sim != null:
                    _rightDown = true;
                    sim.Enqueue(new PlayCommand(PlayKind.Right, m.X, m.Y, Now));
                    return 1;
                case WM_RBUTTONUP when _rightDown:
                    _rightDown = false;
                    return 1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
