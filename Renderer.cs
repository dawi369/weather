namespace Weather;

/// <summary>Software rasterizer into the overlay's premultiplied BGRA buffer.</summary>
internal static unsafe class Renderer
{
    public static void Draw(uint* px, int w, int h, SnowSim sim)
    {
        new Span<uint>(px, w * h).Clear();

        for (int k = sim.Z.Count - 1; k >= 0; k--) DrawPile(px, w, h, sim.Z[k], 0);
        foreach (var s in sim.Z)
            if (s.LinkR != null) DrawBridge(px, w, h, s, s.LinkR);

        var ps = sim.Particles;
        for (int n = 0; n < sim.Count; n++)
        {
            ref var p = ref ps[n];
            switch (p.Kind)
            {
                case PKind.Flake: Disc(px, w, h, p.X, p.Y, p.R, p.Alpha, 255, 255, 255, soft: true); break;
                case PKind.Clump: Disc(px, w, h, p.X, p.Y, p.R, p.Alpha, 236, 242, 252, soft: false); break;
                default: Disc(px, w, h, p.X, p.Y, p.R, p.Alpha, 250, 252, 255, soft: true); break;
            }
        }
    }

    /// <summary>Draws the bottom-of-screen snow into the <see cref="GroundLayer"/> buffer, base on its last row.</summary>
    public static void DrawGround(uint* px, int w, int h, SnowSim sim)
    {
        new Span<uint>(px, w * h).Clear();
        if (sim.GroundOn) DrawPile(px, w, h, sim.Ground, (int)sim.Ground.AnchorY - h);
    }

    /// <param name="yOff">Screen y of the buffer's first row.</param>
    static void DrawPile(uint* px, int w, int h, Surface s, int yOff)
    {
        // Snow on a window touching the top of the screen sits above the screen: not visible.
        if (s.Cells == 0 || s.Clamped) return;
        const float Cell = SnowSim.Cell;
        int x0 = Math.Max(s.Left, 0), x1 = Math.Min(s.Right, w);
        for (int x = x0; x < x1; x++)
        {
            float cx = x + 0.5f - s.Left;
            int ic = Math.Clamp((int)(cx / Cell), 0, s.Cells - 1);
            if (s.Vis[ic] == 0) continue;

            // Soft outline: the profile is drawn slightly blurred so crests come out rounded.
            float hh = (HeightAt(s, cx - 6f) + 2f * HeightAt(s, cx - 3f) + 3f * HeightAt(s, cx)
                      + 2f * HeightAt(s, cx + 3f) + HeightAt(s, cx + 6f)) / 9f;

            if (!s.IsGround)
            {
                // Rounded snow edges instead of a hard vertical cut at the window corner. At an end
                // joined to a neighbouring window it only rounds down to the neighbour's snow level,
                // so the blanket carries on across the join without a step or a cliff.
                float dl = cx, dr = s.Right - s.Left - cx;
                bool leftNear = dl <= dr;
                float de = leftNear ? dl : dr;
                float floorH = 0f;
                float nTop = SnowSim.NeighbourTop(s, leftNear);
                if (!float.IsNaN(nTop)) floorH = MathF.Max(0f, s.AnchorY - nTop);
                if (hh > floorH)
                {
                    float rad = MathF.Min(hh - floorH, 10f);
                    if (de < rad) { float q = de / rad; hh = floorH + (hh - floorH) * MathF.Sqrt(q * (2f - q)); }
                }
            }
            if (hh < 0.08f) continue;

            // Light from the upper left: slopes facing left are a touch brighter. The slope is taken
            // over ~12px so small bumps between columns don't turn into vertical streaks.
            float slope = (HeightAt(s, cx + 6f) - HeightAt(s, cx - 6f)) / 12f;
            float shade = Math.Clamp(1f - slope * 0.35f, 0.9f, 1.04f);

            if (!s.Clamped)
            {
                float top = s.AnchorY - hh - yOff, bottom = s.AnchorY - yOff;
                float start = MathF.Max(top, s.ClipY[ic] - yOff);
                if (start >= bottom) continue;
                int y0 = Math.Max((int)MathF.Floor(start), 0), y1 = Math.Min((int)MathF.Ceiling(bottom), h);
                for (int y = y0; y < y1; y++)
                {
                    float cov = MathF.Min(y + 1, bottom) - MathF.Max(y, start);
                    if (cov <= 0) continue;
                    PileColor(px + y * w + x, y + 0.5f - top, shade, MathF.Min(cov, 1f));
                }
            }
            else
            {
                // Window touching the top of the screen: the pile hangs down over its title bar.
                float top = s.AnchorY, bottom = s.AnchorY + hh;
                int y0 = Math.Max((int)MathF.Floor(top), 0), y1 = Math.Min((int)MathF.Ceiling(bottom), h);
                for (int y = y0; y < y1; y++)
                {
                    float cov = MathF.Min(y + 1, bottom) - MathF.Max(y, top);
                    if (cov <= 0) continue;
                    PileColor(px + y * w + x, bottom - (y + 0.5f), shade, MathF.Min(cov, 1f));
                }
            }
        }
    }

    /// <summary>Snow spanning the small gap between two joined windows, so the blanket looks continuous.</summary>
    static void DrawBridge(uint* px, int w, int h, Surface a, Surface b)
    {
        const float Cell = SnowSim.Cell;
        int ea = a.LinkREdge, eb = b.LinkLEdge;
        float xa = a.Left + (ea + 1) * Cell, xb = b.Left + eb * Cell;
        if (xb - xa < 0.5f) return;
        // Both piles round off to the lower of the two snow surfaces at the join; the bridge sits there too.
        float rawA = a.AnchorY - HeightAt(a, (ea + 0.5f) * Cell), rawB = b.AnchorY - HeightAt(b, (eb + 0.5f) * Cell);
        float topA = MathF.Max(rawA, rawB), topB = topA;
        if (a.AnchorY - topA < 0.5f && b.AnchorY - topB < 0.5f) return;
        int x0 = Math.Max((int)MathF.Floor(xa), 0), x1 = Math.Min((int)MathF.Ceiling(xb), w);
        for (int x = x0; x < x1; x++)
        {
            float t = Math.Clamp((x + 0.5f - xa) / (xb - xa), 0f, 1f);
            float top = topA + (topB - topA) * t;
            float bottom = a.AnchorY + (b.AnchorY - a.AnchorY) * t;
            if (bottom - top < 0.3f) continue;
            int y0 = Math.Max((int)MathF.Floor(top), 0), y1 = Math.Min((int)MathF.Ceiling(bottom), h);
            for (int y = y0; y < y1; y++)
            {
                float cov = MathF.Min(y + 1, bottom) - MathF.Max(y, top);
                if (cov <= 0) continue;
                PileColor(px + y * w + x, y + 0.5f - top, 1f, MathF.Min(cov, 1f));
            }
        }
    }

    static float HeightAt(Surface s, float cx)
    {
        float u = cx / SnowSim.Cell - 0.5f;
        int i0 = (int)MathF.Floor(u);
        float t = u - i0;
        int a = Math.Clamp(i0, 0, s.Cells - 1), b = Math.Clamp(i0 + 1, 0, s.Cells - 1);
        return s.H[a] + (s.H[b] - s.H[a]) * t;
    }

    static void PileColor(uint* p, float depth, float shade, float cov)
    {
        // Lit surface only in the top few pixels; deeper snow is a uniform shadowed body.
        shade = 1f + (shade - 1f) * Math.Clamp(1f - depth / 5f, 0f, 1f);
        float f = Math.Clamp(depth / 16f, 0f, 1f);
        float r = MathF.Min(255f, (255f - 34f * f) * shade);
        float g = MathF.Min(255f, (255f - 24f * f) * shade);
        float b = MathF.Min(255f, (255f - 9f * f) * shade);
        Blend(p, r, g, b, 0.97f * cov);
    }

    static void Disc(uint* px, int w, int h, float cx, float cy, float r, float alpha,
                     float cr, float cg, float cb, bool soft)
    {
        int x0 = Math.Max((int)MathF.Floor(cx - r - 1), 0), x1 = Math.Min((int)MathF.Ceiling(cx + r + 1), w);
        int y0 = Math.Max((int)MathF.Floor(cy - r - 1), 0), y1 = Math.Min((int)MathF.Ceiling(cy + r + 1), h);
        if (x0 >= x1 || y0 >= y1) return;
        float rr = MathF.Max(r, 0.6f);
        float scale = r < 0.6f ? r / 0.6f : 1f;
        for (int y = y0; y < y1; y++)
        {
            float dy = y + 0.5f - cy;
            uint* row = px + y * w;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - cx;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float cov = soft ? (rr + 0.6f - d) / 1.2f : rr + 0.5f - d;
                if (cov <= 0) continue;
                if (cov > 1) cov = 1;
                float a = alpha * cov * scale;
                if (!soft)
                {
                    // Slight shading on clumps so they read as lumps, not dots.
                    float lit = Math.Clamp(1.03f - (dy + dx * 0.5f) / (rr * 6f), 0.85f, 1.05f);
                    Blend(row + x, MathF.Min(255f, cr * lit), MathF.Min(255f, cg * lit), MathF.Min(255f, cb * lit), a);
                }
                else Blend(row + x, cr, cg, cb, a);
            }
        }
    }

    /// <summary>Premultiplied "source over" blend of a straight colour with alpha a.</summary>
    static void Blend(uint* p, float r, float g, float b, float a)
    {
        uint d = *p;
        float ia = 1f - a;
        uint nb = (uint)(b * a + (d & 0xFF) * ia);
        uint ng = (uint)(g * a + ((d >> 8) & 0xFF) * ia);
        uint nr = (uint)(r * a + ((d >> 16) & 0xFF) * ia);
        uint na = (uint)(255f * a + (d >> 24) * ia);
        *p = nb | (ng << 8) | (nr << 16) | (na << 24);
    }
}
