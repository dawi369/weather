namespace Weather;

/// <summary>
/// Dirty tracking for the full-screen overlay. Everything drawn marks the 64×64 tiles it touches;
/// next frame only those tiles are cleared (not the whole screen, which at 4K is 33 MB), and only
/// the rectangle around this frame's and last frame's tiles is sent to the compositor.
/// </summary>
internal static unsafe partial class Renderer
{
    const int TileShift = 6, TileSize = 1 << TileShift;
    static byte[] _tiles = Array.Empty<byte>(), _prevTiles = Array.Empty<byte>();
    static int _tw, _th, _bw, _bh;
    static bool _track;
    static int _mx0, _my0, _mx1, _my1;          // this frame's marked bounds (pixels)
    static int _px0, _py0, _px1, _py1;          // last frame's
    static bool _forceFull = true;

    /// <summary>The part of the overlay that changed this frame (empty when nothing did).</summary>
    public static Native.RECT Dirty;
    public static bool HasDirty => Dirty.Right > Dirty.Left && Dirty.Bottom > Dirty.Top;

    /// <summary>The buffer was cleared or reallocated behind our back: start over.</summary>
    public static void Invalidate() => _forceFull = true;

    static void BeginFrame(uint* px, int w, int h)
    {
        int tw = (w + TileSize - 1) >> TileShift, th = (h + TileSize - 1) >> TileShift;
        if (_forceFull || tw != _tw || th != _th || w != _bw || h != _bh)
        {
            _tw = tw; _th = th; _bw = w; _bh = h;
            _tiles = new byte[tw * th];
            _prevTiles = new byte[tw * th];
            new Span<uint>(px, w * h).Clear();
            _px0 = 0; _py0 = 0; _px1 = w; _py1 = h; // present everything once
            _forceFull = false;
        }
        else
        {
            // Clear what was drawn last frame.
            for (int ty = 0; ty < th; ty++)
            {
                int y0 = ty << TileShift, y1 = Math.Min(y0 + TileSize, h);
                int row = ty * tw;
                for (int tx = 0; tx < tw; tx++)
                {
                    if (_prevTiles[row + tx] == 0) continue;
                    int run = tx;
                    while (run + 1 < tw && _prevTiles[row + run + 1] != 0) run++; // clear runs of tiles at once
                    int x0 = tx << TileShift, x1 = Math.Min((run + 1) << TileShift, w);
                    for (int y = y0; y < y1; y++) new Span<uint>(px + y * w + x0, x1 - x0).Clear();
                    tx = run;
                }
            }
        }
        Array.Clear(_tiles);
        _mx0 = _my0 = int.MaxValue; _mx1 = _my1 = int.MinValue;
        _track = true;
    }

    static void EndFrame()
    {
        _track = false;
        int x0 = Math.Min(_mx0, _px0), y0 = Math.Min(_my0, _py0), x1 = Math.Max(_mx1, _px1), y1 = Math.Max(_my1, _py1);
        Dirty = x1 > x0 && y1 > y0
            ? new Native.RECT { Left = Math.Max(x0, 0), Top = Math.Max(y0, 0), Right = Math.Min(x1, _bw), Bottom = Math.Min(y1, _bh) }
            : default;
        (_prevTiles, _tiles) = (_tiles, _prevTiles);
        if (_mx1 > _mx0) { _px0 = _mx0; _py0 = _my0; _px1 = _mx1; _py1 = _my1; }
        else { _px0 = _py0 = 0; _px1 = _py1 = 0; }
    }

    /// <summary>Note that pixels in [x0,x1)×[y0,y1) may be drawn this frame.</summary>
    static void Mark(float x0, float y0, float x1, float y1)
    {
        if (!_track) return;
        int a = Math.Max(0, (int)MathF.Floor(x0)), b = Math.Min(_bw, (int)MathF.Ceiling(x1));
        int c = Math.Max(0, (int)MathF.Floor(y0)), d = Math.Min(_bh, (int)MathF.Ceiling(y1));
        if (a >= b || c >= d) return;
        if (a < _mx0) _mx0 = a;
        if (c < _my0) _my0 = c;
        if (b > _mx1) _mx1 = b;
        if (d > _my1) _my1 = d;
        int tx0 = a >> TileShift, tx1 = (b - 1) >> TileShift, ty0 = c >> TileShift, ty1 = (d - 1) >> TileShift;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            int row = ty * _tw;
            for (int tx = tx0; tx <= tx1; tx++) _tiles[row + tx] = 1;
        }
    }
}
