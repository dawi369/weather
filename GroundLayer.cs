namespace Weather;

/// <summary>
/// A separate click-through layered window holding only the snow that rests on the taskbar.
/// Its pixels are redrawn with the rest of the snow, but its *position* is moved directly whenever
/// the taskbar moves (see <see cref="TaskbarWatcher"/>). Moving a window costs nothing and lands in
/// the same compositor frame as the taskbar's own move, so the snow rides the taskbar without lag.
/// The pile's base sits on the bottom row of this window.
/// </summary>
internal sealed unsafe class GroundLayer : NativeWindow, IDisposable
{
    public const int Height = 240;

    readonly IntPtr _screenDC, _memDC;
    IntPtr _bmp, _defaultBmp;
    volatile int _top = int.MinValue;

    public uint* Pixels { get; private set; }
    public int Width { get; private set; }

    public GroundLayer(int width, int groundY)
    {
        var cp = new CreateParams
        {
            Caption = "WeatherSnowGround",
            X = 0, Y = groundY - Height, Width = width, Height = Height,
            Style = unchecked((int)Native.WS_POPUP),
            ExStyle = (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                            Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE),
        };
        CreateHandle(cp);
        _top = groundY - Height;
        _screenDC = Native.GetDC(IntPtr.Zero);
        _memDC = Native.CreateCompatibleDC(_screenDC);
        Allocate(width);
        Native.Present(Handle, _screenDC, _memDC, width, Height); // first present sets size
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, _top, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE_);
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
    }

    /// <summary>Put the pile's base at <paramref name="groundY"/>. Safe from any thread.</summary>
    public void MoveTo(int groundY, bool async)
    {
        int top = groundY - Height;
        if (top == _top) return;
        _top = top;
        const uint SWP_NOZORDER = 0x4; // keep its place just below the taskbar
        uint flags = Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE_ | Native.SWP_NOOWNERZORDER_ | SWP_NOZORDER |
                     (async ? Native.SWP_ASYNC_ : 0);
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, top, 0, 0, flags);
    }

    public void Allocate(int w)
    {
        if (_bmp != IntPtr.Zero)
        {
            Native.SelectObject(_memDC, _defaultBmp);
            Native.DeleteObject(_bmp);
        }
        var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -Height, biPlanes = 1, biBitCount = 32 };
        _bmp = Native.CreateDIBSection(_screenDC, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        _defaultBmp = Native.SelectObject(_memDC, _bmp);
        Pixels = (uint*)bits;
        Width = w;
        Clear();
    }

    public void Clear() => new Span<uint>(Pixels, Width * Height).Clear();

    public void Present() => Native.PresentInPlace(Handle, _screenDC, _memDC, Width, Height);

    public void Dispose()
    {
        if (_bmp != IntPtr.Zero)
        {
            Native.SelectObject(_memDC, _defaultBmp);
            Native.DeleteObject(_bmp);
            _bmp = IntPtr.Zero;
        }
        Native.DeleteDC(_memDC);
        Native.ReleaseDC(IntPtr.Zero, _screenDC);
        DestroyHandle();
    }
}
