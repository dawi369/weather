namespace Weather;

/// <summary>
/// A separate click-through layered window holding only the snow that rests on the taskbar.
/// It is small (screen width × <see cref="Height"/>), so the snow thread can redraw it and move it
/// in a single UpdateLayeredWindow call the instant the taskbar moves (see <see cref="TaskbarWatcher"/>):
/// new pixels and new position always arrive together, so the snow never pokes out from behind a
/// window for a frame. The pile's base sits on the bottom row of this window.
/// </summary>
internal sealed unsafe class GroundLayer : NativeWindow, IDisposable
{
    public const int Height = 240;

    readonly IntPtr _screenDC, _memDC;
    IntPtr _bmp, _defaultBmp;

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
        _screenDC = Native.GetDC(IntPtr.Zero);
        _memDC = Native.CreateCompatibleDC(_screenDC);
        Allocate(width);
        PresentAt(groundY);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE_);
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
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

    /// <summary>New pixels and new position in one atomic update: the pile's base at <paramref name="groundY"/>.</summary>
    public void PresentAt(int groundY) => Native.Present(Handle, _screenDC, _memDC, Width, Height, 0, groundY - Height);

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
