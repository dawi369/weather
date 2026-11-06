namespace Weather;

/// <summary>
/// A full-screen, click-through, always-on-top layered window. Pixels are drawn into a
/// premultiplied BGRA buffer and pushed to the compositor with UpdateLayeredWindow.
/// </summary>
internal sealed unsafe class Overlay : NativeWindow, IDisposable
{
    readonly IntPtr _screenDC, _memDC;
    IntPtr _bmp, _defaultBmp;

    public uint* Pixels { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public Overlay()
    {
        var screen = Native.PrimaryScreen();
        var cp = new CreateParams
        {
            Caption = "WeatherSnowOverlay",
            X = 0, Y = 0, Width = screen.Width, Height = screen.Height,
            Style = unchecked((int)Native.WS_POPUP),
            ExStyle = (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                            Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE),
        };
        CreateHandle(cp);
        _screenDC = Native.GetDC(IntPtr.Zero);
        _memDC = Native.CreateCompatibleDC(_screenDC);
        Allocate(screen.Width, screen.Height);
        Present();
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
    }

    public void Allocate(int w, int h)
    {
        if (_bmp != IntPtr.Zero)
        {
            Native.SelectObject(_memDC, _defaultBmp);
            Native.DeleteObject(_bmp);
        }
        var bi = new Native.BITMAPINFOHEADER
        {
            biSize = 40, biWidth = w, biHeight = -h, // negative = top-down rows
            biPlanes = 1, biBitCount = 32,
        };
        _bmp = Native.CreateDIBSection(_screenDC, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        _defaultBmp = Native.SelectObject(_memDC, _bmp);
        Pixels = (uint*)bits;
        Width = w;
        Height = h;
        Clear();
    }

    public void Clear() => new Span<uint>(Pixels, Width * Height).Clear();

    public void Present() => Native.Present(Handle, _screenDC, _memDC, Width, Height);

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
