namespace Weather;

/// <summary>
/// Drawing for the other seasons and the extras: leaf and petal piles, sprites, rain, the taskbar
/// meadow, snowmen, snowballs, splats, the plough and blower, butterflies, fireflies, dust devils.
/// </summary>
internal static unsafe partial class Renderer
{
    // ───────────────────────────── piles of leaves and petals ─────────────────────────────

    static readonly float[] LeafR = { 178, 214, 232, 236, 240, 168 };
    static readonly float[] LeafG = { 34, 72, 128, 168, 196, 72 };
    static readonly float[] LeafB = { 34, 32, 30, 44, 60, 40 };

    /// <summary>A heap of leaves: a mottle of warm colours in leaf-sized patches, darker deeper in.</summary>
    static void LeafColor(uint* p, float depth, float shade, float cov, int xr, int seed)
    {
        // The mottle is precomputed (see LeafTexture); each pile reads it at its own offset.
        var tex = LeafTexture();
        uint c = tex[Math.Min((int)depth, LeafTexH - 1) * LeafTexW + ((xr + seed * 37) & (LeafTexW - 1))];
        float s = _scale * 0.7f;
        float occl = 1f - 0.5f * Math.Clamp(depth / (14f * s), 0f, 1f);
        float k = occl * (1f + (shade - 1f) * 0.6f);
        Blend(p, ((c >> 16) & 255) * k, ((c >> 8) & 255) * k, (c & 255) * k, 0.98f * cov);
    }

    const int LeafTexW = 1024, LeafTexH = 128;
    static uint[]? _leafTex;
    static float _leafTexScale;

    /// <summary>
    /// The colour of a heap of leaves: smooth blotches (value noise), blended between neighbouring
    /// leaf colours, with soft dark crevices. Tiles seamlessly across its width.
    /// </summary>
    static uint[] LeafTexture()
    {
        float s = _scale * 0.7f;
        if (_leafTex != null && _leafTexScale == s) return _leafTex;
        var tex = new uint[LeafTexW * LeafTexH];
        int px1 = Math.Max(4, (int)MathF.Round(LeafTexW / (6f * s))); // noise cells across (whole, so it wraps)
        for (int y = 0; y < LeafTexH; y++)
        for (int x = 0; x < LeafTexW; x++)
        {
            float u = x * px1 / (float)LeafTexW, v = y / (4f * s);
            float n = Noise2P(u, v + x * 0.013f, px1, 11) * 0.7f + Noise2P(u * 2f, v * 2.1f, px1 * 2, 18) * 0.3f;
            float fi = n * 5.2f;
            int i0 = Math.Clamp((int)fi, 0, 5), i1 = Math.Min(i0 + 1, 5);
            float t = fi - (int)fi;
            float r = LeafR[i0] + (LeafR[i1] - LeafR[i0]) * t;
            float g = LeafG[i0] + (LeafG[i1] - LeafG[i0]) * t;
            float b = LeafB[i0] + (LeafB[i1] - LeafB[i0]) * t;
            float crev = Noise2P(u * 1.7f + 3.1f, v * 1.9f, -1, 32);
            float bright = 0.72f + 0.38f * Math.Clamp((crev - 0.25f) * 2f, 0f, 1f);
            tex[y * LeafTexW + x] = ((uint)MathF.Min(255f, r * bright) << 16) | ((uint)MathF.Min(255f, g * bright) << 8) | (uint)MathF.Min(255f, b * bright);
        }
        _leafTexScale = s;
        return _leafTex = tex;
    }

    /// <summary>Value noise like <see cref="Noise2"/> that repeats every <paramref name="period"/> cells in x (no wrap if ≤ 0).</summary>
    static float Noise2P(float x, float y, int period, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = period > 0 ? ((ix % period) + period) % period : ix, x1 = period > 0 ? (x0 + 1) % period : ix + 1;
        float a = Hash01(x0 * 7919 + iy * 104729, seed), b = Hash01(x1 * 7919 + iy * 104729, seed);
        float c = Hash01(x0 * 7919 + (iy + 1) * 104729, seed), d = Hash01(x1 * 7919 + (iy + 1) * 104729, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Smooth 2-D value noise in [0, 1].</summary>
    static float Noise2(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash01(ix * 7919 + iy * 104729, seed), b = Hash01((ix + 1) * 7919 + iy * 104729, seed);
        float c = Hash01(ix * 7919 + (iy + 1) * 104729, seed), d = Hash01((ix + 1) * 7919 + (iy + 1) * 104729, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>A carpet of cherry blossom: pink and white petals, a little rosier underneath.</summary>
    static void PetalColor(uint* p, float depth, float shade, float cov, int xr, int seed)
    {
        float hsh = Hash01((xr / 3) * 7 + (int)(depth / 2f) * 13, seed);
        float f = Math.Clamp(depth / 7f, 0f, 1f);
        float r = (255f - 20f * f) * shade, g = (198f + 40f * hsh - 42f * f) * shade, b = (216f + 28f * hsh - 30f * f) * shade;
        Blend(p, MathF.Min(255f, r), MathF.Min(255f, g), MathF.Min(255f, b), 0.97f * cov);
    }

    /// <summary>Individual leaves (or petals) lying along the top of the pile, so its edge reads as leaves.</summary>
    static void DrawFringe(uint* px, int w, int h, Surface s, int yOff)
    {
        bool leaves = _mat == Material.Leaves;
        // Leaves: a layer just under the surface first, then the top layer over it.
        if (leaves) FringeLayer(px, w, h, s, yOff, true, 4.5f * Sprites.Scale, 101);
        FringeLayer(px, w, h, s, yOff, leaves, 0f, 0);
    }

    static void FringeLayer(uint* px, int w, int h, Surface s, int yOff, bool leaves, float sink, int salt)
    {
        int step = Math.Max(2, (int)((leaves ? 6 : 4) * Sprites.Scale * (sink > 0 ? 1.3f : 1f)));
        int seed = s.Seed + salt;
        int x0 = Math.Max(s.Left, 0), x1 = Math.Min(s.Right, w);
        int r0 = x0 - s.Left;
        r0 -= r0 % step;
        int width = s.Right - s.Left;
        for (int xr = r0; xr < x1 - s.Left; xr += step)
        {
            if (Hash01(xr, seed + 31) < (leaves ? 0.2f : 0.4f)) continue;
            float x = s.Left + xr + Hash01(xr, seed + 37) * step;
            if (x < x0 || x >= x1) continue;
            float cx = x - s.Left;
            if (!s.IsGround && (cx < 4f || cx > width - 4f)) continue;
            int ic = Math.Clamp((int)(cx / SnowSim.Cell), 0, s.Cells - 1);
            if (s.Vis[ic] == 0) continue;
            float hh = (HeightAt(s, cx - 3f) + 2f * HeightAt(s, cx) + HeightAt(s, cx + 3f)) / 4f;
            if (hh < (leaves ? 2f : 1f) + sink * 0.8f) continue;
            hh += (Noise1(s.Seed, cx / 9f) * 0.9f + Noise1(s.Seed + 5, cx / 4.5f) * (leaves ? 0.9f : 0.22f)) * MathF.Min(1f, hh / 6f);
            float top = s.AnchorY - hh - yOff + sink;
            if (s.ClipY[ic] - yOff > top - 2f) continue;
            float rot = Hash01(xr, seed + 47) * 6.283f;
            if (leaves)
            {
                int shape = (int)(Hash01(xr, seed + 41) * 3.999f), col = (int)(Hash01(xr, seed + 43) * 5.999f);
                Blit(px, w, h, Sprites.LeafLying(shape, col, rot, Hash01(xr, seed + 53) > 0.45f), x, top + 1.5f, 1f, 1f, 1f);
            }
            else
            {
                int col = (int)(Hash01(xr, seed + 43) * (Sprites.PetalColors - 0.001f));
                Blit(px, w, h, Sprites.PetalLying(col, rot), x, top + 0.8f, 1f, 1f, 1f);
            }
        }
    }

    // ───────────────────────────── primitives ─────────────────────────────

    static uint Px(Sprite s, int x, int y) => (uint)x < (uint)s.W && (uint)y < (uint)s.H ? s.Px[y * s.W + x] : 0u;

    /// <summary>
    /// Draw a sprite scaled (<paramref name="sx"/> negative mirrors it), rotated by
    /// <paramref name="angle"/>, with its pivot (horizontal centre, <paramref name="pivotY"/> of its
    /// height) at (<paramref name="cx"/>, <paramref name="cy"/>). Bilinear, premultiplied.
    /// </summary>
    static void Blit(uint* px, int w, int h, Sprite s, float cx, float cy, float sx, float sy, float alpha,
                     float angle = 0f, float pivotY = 0.5f)
    {
        if (alpha <= 0.01f || MathF.Abs(sx) < 0.05f || sy < 0.05f) return;
        if (angle == 0f && sx == 1f && sy == 1f)
        {
            // Unscaled: copy the pixels straight in.
            int ox = (int)MathF.Round(cx - s.W * 0.5f), oy = (int)MathF.Round(cy - s.H * pivotY);
            int fx0 = Math.Max(0, -ox), fy0 = Math.Max(0, -oy), fx1 = Math.Min(s.W, w - ox), fy1 = Math.Min(s.H, h - oy);
            if (fx0 >= fx1 || fy0 >= fy1) return;
            Mark(ox + fx0, oy + fy0, ox + fx1, oy + fy1);
            for (int yy = fy0; yy < fy1; yy++)
            {
                uint* row = px + (oy + yy) * w + ox;
                int srow = yy * s.W;
                for (int xx = fx0; xx < fx1; xx++)
                {
                    uint p = s.Px[srow + xx];
                    if (p >> 24 < 2) continue;
                    if (_clip && Covered(ox + xx, oy + yy)) continue;
                    BlendPre(row + xx, ((p >> 16) & 255) * alpha, ((p >> 8) & 255) * alpha, (p & 255) * alpha, (p >> 24) * alpha);
                }
            }
            return;
        }
        float pvx = s.W * 0.5f, pvy = s.H * pivotY;
        float c = MathF.Cos(angle), sn = MathF.Sin(angle);
        float ax = MathF.Abs(sx);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int k = 0; k < 4; k++)
        {
            float lx = ((k & 1) == 0 ? -pvx : s.W - pvx) * ax, ly = ((k & 2) == 0 ? -pvy : s.H - pvy) * sy;
            float X = lx * c - ly * sn, Y = lx * sn + ly * c;
            minX = MathF.Min(minX, X); maxX = MathF.Max(maxX, X); minY = MathF.Min(minY, Y); maxY = MathF.Max(maxY, Y);
        }
        int x0 = Math.Max((int)MathF.Floor(cx + minX), 0), x1 = Math.Min((int)MathF.Ceiling(cx + maxX) + 1, w);
        int y0 = Math.Max((int)MathF.Floor(cy + minY), 0), y1 = Math.Min((int)MathF.Ceiling(cy + maxY) + 1, h);
        if (x0 >= x1 || y0 >= y1) return;
        Mark(x0, y0, x1, y1);
        float isx = 1f / sx, isy = 1f / sy;
        if (angle == 0f)
        {
            // Axis-aligned (the usual case: sprites come pre-rotated): no per-pixel trig, and
            // unchecked reads away from the sprite's border.
            int W = s.W, H = s.H;
            fixed (uint* sp = s.Px)
            {
                for (int y = y0; y < y1; y++)
                {
                    float ly = (y + 0.5f - cy) * isy + pvy - 0.5f;
                    if (ly <= -1f || ly >= H) continue;
                    int iy = (int)MathF.Floor(ly);
                    float fy = ly - iy;
                    uint* row = px + y * w;
                    float lx = (x0 + 0.5f - cx) * isx + pvx - 0.5f;
                    for (int x = x0; x < x1; x++, lx += isx)
                    {
                        if (lx <= -1f || lx >= W) continue;
                        int ix = (int)MathF.Floor(lx);
                        float fx = lx - ix;
                        uint p00, p10, p01, p11;
                        if (ix >= 0 && iy >= 0 && ix + 1 < W && iy + 1 < H)
                        {
                            uint* q = sp + iy * W + ix;
                            p00 = q[0]; p10 = q[1]; p01 = q[W]; p11 = q[W + 1];
                        }
                        else { p00 = Px(s, ix, iy); p10 = Px(s, ix + 1, iy); p01 = Px(s, ix, iy + 1); p11 = Px(s, ix + 1, iy + 1); }
                        if ((p00 | p10 | p01 | p11) == 0) continue;
                        float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                        float a = (p00 >> 24) * w00 + (p10 >> 24) * w10 + (p01 >> 24) * w01 + (p11 >> 24) * w11;
                        if (a < 2f) continue;
                        if (_clip && Covered(x, y)) continue;
                        float r = ((p00 >> 16) & 255) * w00 + ((p10 >> 16) & 255) * w10 + ((p01 >> 16) & 255) * w01 + ((p11 >> 16) & 255) * w11;
                        float g = ((p00 >> 8) & 255) * w00 + ((p10 >> 8) & 255) * w10 + ((p01 >> 8) & 255) * w01 + ((p11 >> 8) & 255) * w11;
                        float b = (p00 & 255) * w00 + (p10 & 255) * w10 + (p01 & 255) * w01 + (p11 & 255) * w11;
                        BlendPre(row + x, r * alpha, g * alpha, b * alpha, a * alpha);
                    }
                }
            }
            return;
        }
        for (int y = y0; y < y1; y++)
        {
            float dy = y + 0.5f - cy;
            uint* row = px + y * w;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - cx;
                float lx = (dx * c + dy * sn) * isx + pvx - 0.5f;
                float ly = (-dx * sn + dy * c) * isy + pvy - 0.5f;
                if (lx <= -1f || ly <= -1f || lx >= s.W || ly >= s.H) continue;
                int ix = (int)MathF.Floor(lx), iy = (int)MathF.Floor(ly);
                float fx = lx - ix, fy = ly - iy;
                uint p00 = Px(s, ix, iy), p10 = Px(s, ix + 1, iy), p01 = Px(s, ix, iy + 1), p11 = Px(s, ix + 1, iy + 1);
                if ((p00 | p10 | p01 | p11) == 0) continue;
                float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                float a = (p00 >> 24) * w00 + (p10 >> 24) * w10 + (p01 >> 24) * w01 + (p11 >> 24) * w11;
                if (a < 2f) continue;
                if (_clip && Covered(x, y)) continue;
                float r = ((p00 >> 16) & 255) * w00 + ((p10 >> 16) & 255) * w10 + ((p01 >> 16) & 255) * w01 + ((p11 >> 16) & 255) * w11;
                float g = ((p00 >> 8) & 255) * w00 + ((p10 >> 8) & 255) * w10 + ((p01 >> 8) & 255) * w01 + ((p11 >> 8) & 255) * w11;
                float b = (p00 & 255) * w00 + (p10 & 255) * w10 + (p01 & 255) * w01 + (p11 & 255) * w11;
                BlendPre(row + x, r * alpha, g * alpha, b * alpha, a * alpha);
            }
        }
    }

    /// <summary>"Source over" with an already premultiplied colour (all channels 0..255).</summary>
    static void BlendPre(uint* p, float r, float g, float b, float a)
    {
        uint d = *p;
        if (d == 0)
        {
            *p = (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
            return;
        }
        float ia = 1f - a / 255f;
        uint nb = (uint)MathF.Min(255f, b + (d & 0xFF) * ia);
        uint ng = (uint)MathF.Min(255f, g + ((d >> 8) & 0xFF) * ia);
        uint nr = (uint)MathF.Min(255f, r + ((d >> 16) & 0xFF) * ia);
        uint na = (uint)MathF.Min(255f, a + (d >> 24) * ia);
        *p = nb | (ng << 8) | (nr << 16) | (na << 24);
    }

    /// <summary>A thin anti-aliased streak (rain), fading from tail to head.</summary>
    static void Streak(uint* px, int w, int h, float x0, float y0, float x1, float y1, float alpha, float r, float g, float b)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int n = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));
        if (n <= 0) return;
        Mark(MathF.Min(x0, x1) - 2, MathF.Min(y0, y1) - 1, MathF.Max(x0, x1) + 2, MathF.Max(y0, y1) + 2);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float x = x0 + dx * t, y = y0 + dy * t;
            int iy = (int)y;
            if ((uint)iy >= (uint)h) continue;
            int ix = (int)MathF.Floor(x - 0.5f);
            float fx = x - 0.5f - ix;
            float a = alpha * (0.25f + 0.75f * t);
            if ((uint)ix < (uint)w && !(_clip && Covered(ix, iy))) Blend(px + iy * w + ix, r, g, b, a * (1f - fx));
            if ((uint)(ix + 1) < (uint)w && !(_clip && Covered(ix + 1, iy))) Blend(px + iy * w + ix + 1, r, g, b, a * fx);
        }
    }

    /// <summary>A packed snowball, lit from the upper left.</summary>
    static void Ball(uint* px, int w, int h, float x, float y, float r, float alpha)
    {
        Disc(px, w, h, x, y, r + 1.3f, alpha * 0.2f, 90, 105, 135, soft: true);
        Disc(px, w, h, x, y, r, alpha, 238, 243, 252, soft: false);
        Disc(px, w, h, x - r * 0.3f, y - r * 0.32f, r * 0.42f, alpha * 0.55f, 255, 255, 255, soft: true);
    }

    // ───────────────────────────── winter extras ─────────────────────────────

    static void DrawSnowmen(uint* px, int w, int h, SnowSim sim, bool ground, int yOff)
    {
        _clip = false;
        foreach (var sm in sim.Snowmen)
        {
            bool inGround = sm.OnGround && !sm.Falling;
            if (inGround != ground || sm.Hidden && !sm.Falling) continue;
            float alpha = sm.Melting ? sm.Melt : 1f;
            float sy = (0.25f + 0.75f * sm.Grow) * (sm.Melting ? 0.4f + 0.6f * sm.Melt : 1f);
            float sx = 0.85f + 0.15f * sm.Grow;
            float angle = sm.Falling ? sm.Rot : MathF.Sin(_time * 14f) * sm.Wobble;
            _clip = false;
            if (sm.Falling && !ground)
            {
                // Knocked off its ledge, it falls behind the windows like everything else.
                float e = sm.Height;
                int cover = Coverage(sm.X - e, sm.Y - e * 1.5f, sm.X + e, sm.Y + e);
                if (cover == 2) continue;
                _clip = cover == 1;
            }
            Blit(px, w, h, sm.Img, sm.X, sm.Y - yOff, sx, sy, alpha, angle, pivotY: 0.98f);
        }
    }

    static void DrawSplats(uint* px, int w, int h, SnowSim sim)
    {
        _clip = true; // splats are on the desktop, behind the windows
        foreach (var sp in sim.Splats)
        {
            if (!sim.SplatPos(sp, out float x, out float y)) continue;
            float a = sp.Age < 3.5f ? 1f : MathF.Max(0f, 1f - (sp.Age - 3.5f) / 3f);
            float pop = MathF.Min(1f, sp.Age * 14f); // bursts out over the first few frames
            float r = sp.R * (0.6f + 0.4f * pop);
            // The smear it leaves as it slides down the glass.
            float slid = 15f * (1f - MathF.Exp(-0.6f * sp.Age));
            for (float t = 0; t < slid; t += 2f)
                Disc(px, w, h, x + MathF.Sin(t * 0.3f + sp.Seed) * 1.2f, y - t, r * (0.38f - 0.012f * t), a * 0.55f, 240, 245, 252, soft: true);
            Disc(px, w, h, x, y, r + 2f, a * 0.25f, 255, 255, 255, soft: true);
            Disc(px, w, h, x, y, r * 0.72f, a * 0.95f, 246, 249, 255, soft: true);
            for (int k = 0; k < 7; k++)
            {
                float ang = Hash01(k, sp.Seed) * 6.283f, dist = r * (0.35f + 0.35f * Hash01(k, sp.Seed + 1));
                Disc(px, w, h, x + MathF.Cos(ang) * dist, y + MathF.Sin(ang) * dist, r * (0.25f + 0.17f * Hash01(k, sp.Seed + 2)),
                     a * 0.92f, 244, 247, 253, soft: true);
            }
            for (int k = 0; k < 9; k++)
            {
                float ang = Hash01(k, sp.Seed + 3) * 6.283f, dist = r * (1.05f + 0.7f * Hash01(k, sp.Seed + 4)) * pop;
                Disc(px, w, h, x + MathF.Cos(ang) * dist, y + MathF.Sin(ang) * dist, r * (0.06f + 0.09f * Hash01(k, sp.Seed + 5)) + 0.6f,
                     a * 0.9f, 244, 247, 253, soft: true);
            }
        }
    }

    static void DrawVehicle(uint* px, int w, int h, SnowSim sim, Vehicle v, int yOff)
    {
        float sc = v.Scale;
        var img = Sprites.Vehicle(v.Blower, ((int)(v.Frame * 12f)) & 1, sc);
        float baseY = sim.Ground.AnchorY - yOff + MathF.Sin(v.Frame * 22f) * 0.35f;
        Blit(px, w, h, img, v.X, baseY + 1f, v.Dir > 0 ? 1f : -1f, 1f, 1f, 0f, pivotY: 1f);
        // Flashing beacon on the cab roof.
        float lx = 17.6f * sc;
        float bx = v.Dir > 0 ? v.X - v.W * 0.5f + lx : v.X + v.W * 0.5f - lx;
        float by = baseY + 1f - v.H + 2.4f * sc;
        float blink = MathF.Sin(v.Frame * 9f);
        if (blink > 0)
        {
            Disc(px, w, h, bx, by, 7f * sc, 0.3f * blink, 255, v.Blower ? 220f : 160f, 40, soft: true);
            Disc(px, w, h, bx, by, 2.2f * sc, 0.9f * blink, 255, 240, 170, soft: true);
        }
    }

    // ───────────────────────────── critters ─────────────────────────────

    static void DrawCritters(uint* px, int w, int h, SnowSim sim)
    {
        foreach (var c in sim.Critters)
        {
            if (c.Firefly)
            {
                if (c.Glow < 0.02f) continue;
                float r = 10f * _scale;
                float e = r * 3.1f; // the outer glow reaches 3r
                int cover = Coverage(c.X - e, c.Y - e, c.X + e, c.Y + e);
                if (cover == 2) continue;
                _clip = cover == 1;
                Glow(px, w, h, c.X, c.Y, r * 3f, 0.5f * c.Glow, 190, 255, 110);
                Glow(px, w, h, c.X, c.Y, r * 0.9f, 0.7f * c.Glow, 225, 255, 160);
                Disc(px, w, h, c.X, c.Y, 1.2f * _scale, 0.95f * c.Glow, 255, 255, 205, soft: true);
                continue;
            }
            if (c.State == 1 && c.OnGround) continue; // drawn with the taskbar
            DrawButterfly(px, w, h, c, 0);
        }
        _clip = false;
    }

    static void DrawButterfly(uint* px, int w, int h, Critter c, int yOff)
    {
        int frame;
        float angle = 0f;
        if (c.State == 1)
        {
            frame = (int)MathF.Round((0.5f - 0.5f * MathF.Cos(c.Phase)) * 3f);
            _clip = false;
        }
        else
        {
            frame = (int)MathF.Round(MathF.Abs(MathF.Sin(c.Phase)) * (Sprites.ButterflyFrames - 1));
            angle = Math.Clamp(c.VX / 500f, -0.35f, 0.35f);
            float r = 15f * _scale; // half the drawn sprite, with room for its tilt
            int cover = yOff != 0 ? 0 : Coverage(c.X - r, c.Y - r, c.X + r, c.Y + r);
            if (cover == 2) return;
            _clip = cover == 1;
        }
        float sc = 0.85f * _scale / Sprites.Scale;
        Blit(px, w, h, Sprites.Butterfly(c.Variant, frame), c.X, c.Y - yOff, sc, sc, 1f, angle);
        _clip = false;
    }

    static void DrawDevil(uint* px, int w, int h, SnowSim sim)
    {
        var dv = sim.Devil;
        if (dv == null) return;
        float str = dv.Strength, baseY = sim.Ground.AnchorY;
        _clip = true;
        for (int k = 0; k < 26; k++)
        {
            float t = k / 26f;
            float ang = _time * 6.5f + k * 0.9f;
            float rad = 10f + t * 70f;
            float x = dv.X + MathF.Cos(ang) * rad, y = baseY - t * 300f * str - 4f;
            float depth = 0.5f + 0.5f * MathF.Sin(ang);
            Disc(px, w, h, x, y, 1.2f + depth * 1.4f, 0.16f * str * (1f - t) * (0.5f + depth), 196, 168, 128, soft: true);
        }
        _clip = false;
    }

    // ───────────────────────────── the meadow on the taskbar ─────────────────────────────

    /// <summary>
    /// Spring and summer grow a strip of grass and wildflowers along the top of the taskbar. It sways
    /// with the wind, parts around the mouse and is flattened by anything driving over it.
    /// </summary>
    static void DrawMeadow(uint* px, int w, int h, SnowSim sim, int yOff)
    {
        bool summer = sim.Season == Season.Summer;
        _bloomMoon = sim.Night ? 1f : 0f;
        float night = sim.Night ? 0.62f : 1f; // the meadow dims after dusk, but stays visible in the moonlight
        float baseY = sim.Ground.AnchorY - yOff;
        float wind = sim.Wind, cy = sim.CursorY - yOff;
        var v = sim.Vehicle;
        float s = _scale;
        // Base and tip colours (fresh green in spring, sun-ripened in summer).
        float br = summer ? 58 : 44, bg = summer ? 100 : 112, bb = summer ? 36 : 46;
        float tr = summer ? 156 : 122, tg = summer ? 176 : 198, tb = summer ? 74 : 92;
        for (int bx = 0; bx < w; bx += 2)
        {
            float hx = Hash01(bx, 7771), hy = Hash01(bx, 9133), hz = Hash01(bx, 4211);
            float x = bx + hx * 3f;
            float H = (summer ? 7f + 11f * hy : 6f + 9f * hy) * s;
            float sway = (wind * 0.045f + MathF.Sin(_time * 1.6f + x * 0.021f) * 1.2f + MathF.Sin(_time * 2.7f + x * 0.05f + hz * 6f) * 0.5f) * (H / (12f * s));
            float cdx = x - sim.CursorX;
            if (MathF.Abs(cdx) < 55f && cy > baseY - H - 30f && cy < baseY + 12f)
                sway += MathF.Sign(cdx) * (1f - MathF.Abs(cdx) / 55f) * 9f * s;
            if (v != null && MathF.Abs(x - v.X) < v.W * 0.55f) H *= 0.25f;
            float vary = (0.82f + 0.3f * hz) * night;
            Blade(px, w, h, x, baseY, H, sway, 1.5f * s, br * vary, bg * vary, bb * vary, tr * vary, tg * vary, tb * vary);
            if (hz > (summer ? 0.982f : 0.966f))
            {
                // Most flowers stand a little above the grass; a few tall ones stand well clear of it.
                bool tall = Hash01(bx, 556) > 0.82f;
                float fh = H * (tall ? 2.3f : 1.4f) + 4f * s;
                float fs = sway * (tall ? 1.8f : 1.3f);
                Blade(px, w, h, x + 1f, baseY, fh, fs, (tall ? 1.1f : 0.9f) * s, 50, 104, 40, 80, 140, 60);
                Bloom(px, w, h, x + 1f + fs, baseY - fh, (int)(Hash01(bx, 555) * 3.999f), summer, s * (tall ? 1.5f : 1f));
            }
        }
    }

    static void Blade(uint* px, int w, int h, float x, float baseY, float H, float sway, float width,
                      float br, float bg, float bb, float tr, float tg, float tb)
    {
        int rows = (int)MathF.Ceiling(H);
        for (int k = 0; k < rows; k++)
        {
            float t = (k + 0.5f) / H;
            if (t > 1f) break;
            int y = (int)(baseY - 1 - k);
            if ((uint)y >= (uint)h) continue;
            float xc = x + sway * t * t;
            float wd = width * (1f - t) + 0.35f;
            float l = xc - wd * 0.5f, r = xc + wd * 0.5f;
            float cr = br + (tr - br) * t, cg = bg + (tg - bg) * t, cb = bb + (tb - bb) * t;
            float fade = t > 0.85f ? (1f - t) / 0.15f : 1f;
            for (int ix = (int)MathF.Floor(l); ix <= (int)MathF.Floor(r); ix++)
            {
                if ((uint)ix >= (uint)w) continue;
                float cov = MathF.Min(ix + 1, r) - MathF.Max(ix, l);
                if (cov <= 0) continue;
                Blend(px + y * w + ix, cr, cg, cb, MathF.Min(1f, cov) * 0.95f * fade);
            }
        }
    }

    static void Bloom(uint* px, int w, int h, float x, float y, int kind, bool summer, float s)
    {
        void Ring(int n, float dist, float r, float cr, float cg, float cb, float rot = 0)
        {
            for (int k = 0; k < n; k++)
            {
                float a = rot + k * 6.283f / n;
                MoonDisc(px, w, h, x + MathF.Cos(a) * dist * s, y + MathF.Sin(a) * dist * s * 0.8f, r * s, 0.97f, cr, cg, cb, soft: true);
            }
        }
        if (summer)
        {
            switch (kind)
            {
                case 0: Ring(4, 1.2f, 1.9f, 214, 40, 30); MoonDisc(px, w, h, x, y, 0.9f * s, 1f, 40, 20, 24, soft: true); break;   // poppy
                case 1: Ring(7, 1.8f, 1.0f, 76, 112, 232); MoonDisc(px, w, h, x, y, 0.8f * s, 1f, 50, 60, 140, soft: true); break; // cornflower
                case 2: Ring(6, 2.0f, 1.2f, 250, 250, 244); MoonDisc(px, w, h, x, y, 1.1f * s, 1f, 248, 196, 40, soft: true); break; // daisy
                default: Ring(5, 1.4f, 1.2f, 255, 214, 40); MoonDisc(px, w, h, x, y, 0.8f * s, 1f, 236, 156, 20, soft: true); break; // buttercup
            }
        }
        else
        {
            switch (kind)
            {
                case 0: // crocus
                    MoonDisc(px, w, h, x, y + 0.8f * s, 1.5f * s, 1f, 150, 92, 210, soft: true);
                    MoonDisc(px, w, h, x, y - 1f * s, 1.3f * s, 1f, 172, 118, 228, soft: true);
                    MoonDisc(px, w, h, x, y - 2.4f * s, 0.9f * s, 1f, 196, 150, 240, soft: true);
                    break;
                case 1: Ring(5, 1.6f, 1.3f, 252, 238, 130); MoonDisc(px, w, h, x, y, 0.9f * s, 1f, 240, 170, 40, soft: true); break; // primrose
                case 2: Ring(6, 2.0f, 1.2f, 250, 250, 244); MoonDisc(px, w, h, x, y, 1.1f * s, 1f, 248, 196, 40, soft: true); break; // daisy
                default: // tulip
                    MoonDisc(px, w, h, x, y, 2.0f * s, 1f, 232, 78, 112, soft: true);
                    MoonDisc(px, w, h, x - 1.1f * s, y - 1.4f * s, 1.1f * s, 1f, 244, 110, 140, soft: true);
                    MoonDisc(px, w, h, x + 1.1f * s, y - 1.4f * s, 1.1f * s, 1f, 244, 110, 140, soft: true);
                    break;
            }
        }
    }
}

internal static unsafe partial class Renderer
{
    /// <summary>A soft light or puff: brightest in the middle, fading smoothly to nothing at r (no hard edge).</summary>
    static void Glow(uint* px, int w, int h, float cx, float cy, float r, float alpha, float cr, float cg, float cb)
    {
        if (r < 0.5f || alpha <= 0.005f) return;
        int x0 = Math.Max((int)MathF.Floor(cx - r), 0), x1 = Math.Min((int)MathF.Ceiling(cx + r), w);
        int y0 = Math.Max((int)MathF.Floor(cy - r), 0), y1 = Math.Min((int)MathF.Ceiling(cy + r), h);
        if (x0 >= x1 || y0 >= y1) return;
        Mark(x0, y0, x1, y1);
        float ir2 = 1f / (r * r);
        for (int y = y0; y < y1; y++)
        {
            float dy = y + 0.5f - cy;
            uint* row = px + y * w;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - cx;
                float q = 1f - (dx * dx + dy * dy) * ir2;
                if (q <= 0) continue;
                if (_clip && Covered(x, y)) continue;
                Blend(row + x, cr, cg, cb, alpha * q * q);
            }
        }
    }
}

internal static unsafe partial class Renderer
{
    /// <summary>Robins and ladybirds: perched on the taskbar they're drawn with it, otherwise on the overlay.</summary>
    static void DrawVisitors(uint* px, int w, int h, SnowSim sim, bool ground, int yOff)
    {
        foreach (var v in sim.Visitors)
        {
            bool onGround = v.State == 1 && v.OnGround;
            if (onGround != ground) continue;
            bool robin = v.Kind == VisitorKind.Robin;
            int frame;
            if (v.State == 1)
                frame = robin ? (v.Action == 2 ? 1 : v.Action == 1 && v.Lift > 1f ? 2 : 0)
                              : (v.Action == 2 ? 2 : v.Action == 1 ? ((int)(v.Phase * 10f)) & 1 : 0);
            else
                frame = robin ? (MathF.Sin(v.Age * 22f) > 0 ? 2 : 3) : 0;
            var img = robin ? Sprites.Robin(frame) : Sprites.Ladybird(frame);
            _clip = false;
            if (v.State != 1 && !ground)
            {
                int cover = Coverage(v.X - img.W * 2, v.Y - img.H * 2, v.X + img.W * 2, v.Y);
                if (cover == 2) continue;
                _clip = cover == 1;
            }
            float k = robin ? 1.6f : 2.6f; // a touch bigger than life, so they read at a glance
            Blit(px, w, h, img, v.X, v.Y - yOff, v.Dir * k, k, 1f, 0f, pivotY: robin ? 0.98f : 0.99f);
            if (!robin && v.State != 1)
            {
                // A ladybird in flight: its wing cases up and a blur of beating wings above it.
                float ws = img.H * k, flap = 0.6f + 0.4f * MathF.Abs(MathF.Sin(v.Age * 45f));
                Glow(px, w, h, v.X - v.Dir * ws * 0.25f, v.Y - yOff - ws * 0.95f, ws * 0.45f * flap, 0.55f, 225, 232, 245);
                Glow(px, w, h, v.X + v.Dir * ws * 0.1f, v.Y - yOff - ws * 0.85f, ws * 0.35f * flap, 0.5f, 225, 232, 245);
            }
        }
        _clip = false;
    }
}

internal static unsafe partial class Renderer
{
    /// <summary>Dandelion clocks in the summer meadow: a stem and a soft white globe of seeds (or a bare head regrowing).</summary>
    static void DrawClocks(uint* px, int w, int h, SnowSim sim, int yOff)
    {
        float baseY = sim.Ground.AnchorY - yOff;
        float s = MathF.Max(1f, _scale * 0.7f);
        float night = sim.Night ? 0.5f : 1f;
        foreach (var c in sim.Clocks)
        {
            float sway = (sim.Wind * 0.045f + MathF.Sin(_time * 1.6f + c.X * 0.021f) * 1.2f) * (c.Height / (12f * s)) * 0.6f;
            float cdx = c.X - sim.CursorX, cy = sim.CursorY - yOff;
            if (MathF.Abs(cdx) < 55f && cy > baseY - c.Height - 30f && cy < baseY + 12f)
                sway += MathF.Sign(cdx) * (1f - MathF.Abs(cdx) / 55f) * 7f * s;
            Blade(px, w, h, c.X, baseY, c.Height, sway, 1.1f * s, 60 * night, 108 * night, 44 * night, 96 * night, 150 * night, 70 * night);
            float hx = c.X + sway, hy = baseY - c.Height;
            Disc(px, w, h, hx, hy, 1.3f * s, 0.95f, 150 * night, 140 * night, 90 * night, soft: true); // the seed head
            float full = c.Full < 0.6f ? 0f : (c.Full - 0.6f) / 0.4f;
            if (full <= 0.01f) continue;
            float r = 5.2f * s * (0.5f + 0.5f * full);
            Glow(px, w, h, hx, hy, r * 1.25f, 0.42f * full * night, 255, 252, 240);
            for (int k = 0; k < 16; k++)
            {
                float a = k * 6.283f / 16f + c.Slot;
                Disc(px, w, h, hx + MathF.Cos(a) * r, hy + MathF.Sin(a) * r, 0.55f * s, 0.75f * full * night, 255, 255, 250, soft: true);
            }
        }
    }
}

internal static unsafe partial class Renderer
{
    static readonly float[,] Spectrum =
    {
        { 255, 110, 110 }, { 255, 180, 90 }, { 255, 240, 130 }, { 140, 230, 140 }, { 120, 180, 255 }, { 190, 140, 250 },
    };

    /// <summary>
    /// After a spring shower clears, a faint rainbow arcs across the sky behind the windows, fading
    /// in over a few seconds and out again.
    /// </summary>
    static void DrawRainbow(uint* px, int w, int h, SnowSim sim)
    {
        float t = sim.Rainbow;
        if (t <= 0) return;
        float age = SnowSim.RainbowLife - t;
        float alpha = MathF.Min(1f, age / 5f) * MathF.Min(1f, t / 8f) * 0.72f;
        if (alpha <= 0.003f) return;
        float cx = w * 0.58f, cy = h * 1.32f, R = h * 1.14f, B = h * 0.085f;
        float rIn = R - B;
        int top = Math.Max(0, (int)(cy - R));
        Mark(0, top, w, h);
        _clip = true;
        for (int y = top; y < h; y++)
        {
            float dy = y + 0.5f - cy, dy2 = dy * dy;
            if (dy2 > R * R) continue;
            float xo = MathF.Sqrt(R * R - dy2), xi = dy2 < rIn * rIn ? MathF.Sqrt(rIn * rIn - dy2) : 0f;
            uint* row = px + y * w;
            for (int side = -1; side <= 1; side += 2)
            {
                int x0 = (int)MathF.Floor(side < 0 ? cx - xo : cx + xi), x1 = (int)MathF.Ceiling(side < 0 ? cx - xi : cx + xo);
                x0 = Math.Max(x0, 0); x1 = Math.Min(x1, w);
                for (int x = x0; x < x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float u = (R - MathF.Sqrt(dx * dx + dy2)) / B; // 0 outer edge (red) .. 1 inner (violet)
                    if (u <= 0f || u >= 1f) continue;
                    if (Covered(x, y)) continue;
                    float edge = MathF.Min(1f, MathF.Min(u, 1f - u) / 0.07f); // soft edges, but the red and violet bands stay strong
                    float fi = u * 5f;
                    int i0 = Math.Min((int)fi, 4);
                    float f = fi - i0;
                    float r = Spectrum[i0, 0] + (Spectrum[i0 + 1, 0] - Spectrum[i0, 0]) * f;
                    float g = Spectrum[i0, 1] + (Spectrum[i0 + 1, 1] - Spectrum[i0, 1]) * f;
                    float b = Spectrum[i0, 2] + (Spectrum[i0 + 1, 2] - Spectrum[i0, 2]) * f;
                    Blend(row + x, r, g, b, alpha * edge);
                }
            }
        }
        _clip = false;
    }

    /// <summary>A thin ring spreading on the wet taskbar.</summary>
    static void Ripple(uint* px, int w, int h, float cx, float cy, float r, float alpha)
    {
        float rx = r, ry = r * 0.3f;
        int x0 = Math.Max((int)(cx - rx - 2), 0), x1 = Math.Min((int)(cx + rx + 2), w);
        int y0 = Math.Max((int)(cy - ry - 2), 0), y1 = Math.Min((int)(cy + ry + 2), h);
        for (int y = y0; y < y1; y++)
        {
            float dy = (y + 0.5f - cy) / ry;
            for (int x = x0; x < x1; x++)
            {
                float dx = (x + 0.5f - cx) / rx;
                float e = MathF.Sqrt(dx * dx + dy * dy);
                float cov = 1f - MathF.Abs(e - 1f) * rx / 1.1f;
                if (cov <= 0) continue;
                Blend(px + y * w + x, 235, 242, 255, alpha * MathF.Min(cov, 1f));
            }
        }
    }
}

internal static unsafe partial class Renderer
{
    /// <summary>
    /// Summer: a windowsill garden. Little tufts of grass, the odd clover or daisy, grow along each
    /// window's top edge and sway in the breeze (fixed to the window, so they ride along with it).
    /// </summary>
    static void DrawSill(uint* px, int w, int h, SnowSim sim, Surface s)
    {
        if (s.Cells == 0 || s.Clamped || s.IsGround) return;
        float sc = MathF.Max(1f, _scale * 0.7f);
        // At night the grass catches a little moonlight: dimmer and bluer, not black.
        float night = sim.Night ? 0.7f : 1f, moon = sim.Night ? 70f : 0f;
        _bloomMoon = sim.Night ? 1f : 0f;
        int step = Math.Max(4, (int)(10f * sc));
        int width = s.Right - s.Left;
        float baseY = s.AnchorY;
        Mark(s.Left - 12, baseY - 46f * sc, s.Right + 12, baseY + 1);
        for (int xr = step / 2; xr < width - step / 2; xr += step)
        {
            float hA = Hash01(xr, s.Seed + 501);
            // Tufts grow in clumps with bare stretches between them, taller where they are thickest.
            float clump = 0.5f + 0.5f * Noise1(s.Seed + 509, xr / (70f * sc));
            if (hA > 0.15f + clump) continue;
            float x = s.Left + xr + (Hash01(xr, s.Seed + 502) - 0.5f) * step * 0.6f;
            if (x < 0 || x >= w) continue;
            int col = Math.Clamp((int)((x - s.Left) / SnowSim.Cell), 0, s.Cells - 1);
            if (s.Vis[col] == 0) continue;
            int blades = 3 + (int)(Hash01(xr, s.Seed + 503) * 4f);
            float sway = (sim.Wind * 0.03f + MathF.Sin(_time * 1.8f + x * 0.03f) * 0.8f) * sc;
            for (int b = 0; b < blades; b++)
            {
                float bx = x + (b - blades / 2f) * 1.6f * sc;
                float bh = (5.5f + 8f * Hash01(xr * 7 + b, s.Seed + 504)) * sc * (0.7f + 0.6f * clump);
                float lean = sway + (b - blades / 2f) * 0.9f * sc;
                float v = 0.85f + 0.3f * Hash01(xr + b, s.Seed + 505);
                Blade(px, w, h, bx, baseY, bh, lean, 1.4f * sc, 40 * v * night, 86 * v * night, 30 * v * night + moon, 168 * v * night, 210 * v * night, 92 * v * night + moon);
            }
            float fl = Hash01(xr, s.Seed + 506);
            if (fl > 0.72f)
            {
                bool hero = fl > 0.95f; // now and then a tall one stands well above the rest
                float fh = (hero ? 26f + 10f * Hash01(xr, s.Seed + 507) : 12f + 8f * Hash01(xr, s.Seed + 507)) * sc;
                Blade(px, w, h, x, baseY, fh, sway * 1.4f, 0.8f * sc, 50 * night, 100 * night, 40 * night, 80 * night, 140 * night, 60 * night);
                Bloom(px, w, h, x + sway * 1.4f, baseY - fh, (int)(Hash01(xr, s.Seed + 508) * 3.999f), true, sc * (hero ? 2.4f : 1.8f));
                if (sim.Night) Glow(px, w, h, x + sway * 1.4f, baseY - fh, 4f * sc, 0.25f, 210, 225, 255); // pale in the moonlight
            }
        }
    }
}

internal static unsafe partial class Renderer
{
    static void DrawBees(uint* px, int w, int h, SnowSim sim)
    {
        foreach (var b in sim.Bees)
        {
            var img = Sprites.Bee(((int)(b.Age * 40f)) & 1);
            float k = 2.1f, e = img.W * k;
            int cover = Coverage(b.X - e, b.Y - e, b.X + e, b.Y + e);
            if (cover == 2) continue;
            _clip = cover == 1;
            Blit(px, w, h, img, b.X, b.Y, b.Dir * k, k, 1f);
        }
        _clip = false;
    }
}

internal static unsafe partial class Renderer
{
    /// <summary>1 at night: flowers drawn now are seen by moonlight (paler, bluer, less saturated).</summary>
    static float _bloomMoon;

    static void MoonDisc(uint* px, int w, int h, float cx, float cy, float r, float alpha, float cr, float cg, float cb, bool soft)
    {
        if (_bloomMoon > 0)
        {
            const float mr = 150, mg = 165, mb = 205;
            cr = (cr + (mr - cr) * 0.55f) * 0.82f;
            cg = (cg + (mg - cg) * 0.55f) * 0.82f;
            cb = (cb + (mb - cb) * 0.55f) * 0.9f;
        }
        Disc(px, w, h, cx, cy, r, alpha, cr, cg, cb, soft);
    }
}
