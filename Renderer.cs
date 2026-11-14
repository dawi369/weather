namespace Weather;

/// <summary>Software rasterizer into the overlay's premultiplied BGRA buffer.</summary>
internal static unsafe partial class Renderer
{
    public static void Draw(uint* px, int w, int h, SnowSim sim)
    {
        BeginFrame(px, w, h);

        _time = sim.Time;
        _mat = sim.Material;
        _scale = sim.UiScale;
        for (int k = sim.Z.Count - 1; k >= 0; k--) DrawPile(px, w, h, sim.Z[k], 0);
        foreach (var s in sim.Z)
            if (s.LinkR != null) DrawBridge(px, w, h, s, s.LinkR);

        // Falling snow is in the background: it's only drawn where no window is, so it never covers
        // an app's content. (Snow resting on top of windows sits above their top edges, so it stays.)
        _occN = 0;
        foreach (var s in sim.Z)
        {
            if (_occN == _occ.Length) Array.Resize(ref _occ, _occN * 2);
            _occ[_occN++] = (s.Left, s.Top, s.Right, s.Bottom);
        }

        DrawSnowmen(px, w, h, sim, ground: false, 0);
        DrawDevil(px, w, h, sim);

        var ps = sim.Particles;
        for (int n = 0; n < sim.Count; n++)
        {
            ref var p = ref ps[n];
            if (p.Life > 0 && p.Surf == IntPtr.Zero) continue; // resting on the taskbar: drawn with it (DrawGround)
            DrawParticle(px, w, h, ref p, 0);
        }
        _clip = false;

        DrawCritters(px, w, h, sim);
        DrawSplats(px, w, h, sim);
        if (sim.Holding) Ball(px, w, h, sim.HeldX, sim.HeldY, sim.HeldRadius, 1f);
        EndFrame();
    }

    /// <param name="yOff">Screen y of the buffer's first row.</param>
    static void DrawParticle(uint* px, int w, int h, ref Particle p, int yOff)
    {
        float py = p.Y - yOff;
        // Landed things sit on the pile on top of a window, outside its body; snowballs, puffs and
        // exhaust are in front of everything. All else is background and goes behind windows.
        if (p.Life > 0 || yOff != 0 || p.Kind is PKind.Ball or PKind.Smoke or PKind.Splash) _clip = false;
        else
        {
            float ext = p.Kind switch
            {
                PKind.Flake => p.Shape is > 0 and < 255 ? 9f : p.R + 3f,
                PKind.Leaf => 13f * p.R,
                PKind.Petal => 8f * p.R,
                PKind.Seed => 12f * p.R,
                PKind.Rain => 30f,
                _ => p.R + 3f,
            };
            int cover = Coverage(p.X - ext, py - ext, p.X + ext, py + ext);
            if (cover == 2) return; // entirely behind a window
            _clip = cover == 1;       // straddles a window's edge: clip per pixel
        }
        bool landed = p.Life > 0;
        switch (p.Kind)
        {
            case PKind.Flake:
            {
                float a = landed ? p.Alpha * (p.Life / p.Fade) : p.Alpha; // landed flakes fade out
                // A faint cool outline on nearer flakes, so snow still reads over light windows.
                if (p.Depth > 0.45f && p.Shape == 0)
                    Disc(px, w, h, p.X, py, p.R + 1.1f, a * 0.16f, 110, 125, 155, soft: true);
                if (p.Shape == 0)
                {
                    // Near, fast flakes smear a touch along their fall (motion blur).
                    if (!landed && p.Depth > 0.6f)
                        Disc(px, w, h, p.X, py - p.VY * 0.014f, p.R * 0.85f, a * 0.32f, 255, 255, 255, soft: true);
                    Disc(px, w, h, p.X, py, p.R, a, 255, 255, 255, soft: true);
                }
                else if (p.Shape == 255) Fluffy(px, w, h, p.X, py, ref p, a);
                else if (landed)
                {
                    // A crystal that landed melts into the snow: its arms fade first, leaving a
                    // soft white speck that fades too.
                    float k = p.Life / p.Fade;
                    if (k > 0.55f) Crystal(px, w, h, p.X, py, ref p, a * (k - 0.55f) / 0.45f);
                    Disc(px, w, h, p.X, py, p.R * 0.4f, p.Alpha * MathF.Min(1f, k * 1.4f) * 0.8f, 255, 255, 255, soft: true);
                }
                else Crystal(px, w, h, p.X, py, ref p, a);
                break;
            }
            case PKind.Clump: Disc(px, w, h, p.X, py, p.R, p.Alpha, 236, 242, 252, soft: false); break;
            case PKind.Powder: Disc(px, w, h, p.X, py, p.R, p.Alpha, 250, 252, 255, soft: true); break;
            case PKind.Leaf:
            {
                float s = MathF.Sin(p.Phase);
                float rot, squash, a = p.Alpha;
                if (landed) { rot = p.Rot; squash = 0.5f; a *= MathF.Min(1f, p.Life / 3f); }
                else if (MathF.Abs(p.RotSpeed) > 2f) { rot = p.Rot; squash = 0.3f + 0.7f * MathF.Abs(MathF.Cos(p.Rot * 1.3f)); }
                else { rot = p.Rot + s * 0.7f; squash = 1f - 0.55f * MathF.Abs(s); }
                int shape = p.Shape / Sprites.LeafColors, col = p.Shape % Sprites.LeafColors;
                if (!landed && p.Depth < 0.45f) a *= 0.85f;
                if (landed) Blit(px, w, h, Sprites.LeafLying(shape, col, rot, p.R > 0.9f), p.X, py, 1f, 1f, a);
                else Blit(px, w, h, Sprites.Leaf(shape, col, rot), p.X, py, p.R, p.R * squash, a);
                break;
            }
            case PKind.Petal:
            {
                float a = p.Alpha, squash;
                if (landed) { squash = 0.55f; a *= MathF.Min(1f, p.Life / 2f); }
                else squash = 0.35f + 0.65f * MathF.Abs(MathF.Cos(p.Phase * 0.8f + p.Rot));
                if (landed) Blit(px, w, h, Sprites.PetalLying(p.Shape, p.Rot), p.X, py, 1f, 1f, a);
                else Blit(px, w, h, Sprites.Petal(p.Shape, p.Rot), p.X, py, p.R, p.R * squash, a);
                break;
            }
            case PKind.Seed:
            {
                float a = p.Alpha * MathF.Min(1f, p.Age / 2f + (landed ? 1f : 0f));
                if (p.Shape == 1)
                {
                    // A sunlit pollen mote: a tiny warm twinkle.
                    float tw = 0.5f + 0.5f * MathF.Sin(p.Age * 3f + p.Phase);
                    Disc(px, w, h, p.X, py, 3f * _scale, 0.12f * a * tw, 255, 226, 150, soft: true);
                    Disc(px, w, h, p.X, py, 0.7f * _scale, 0.8f * a * tw, 255, 240, 200, soft: true);
                    break;
                }
                Blit(px, w, h, Sprites.Seed(p.Rot / 0.35f), p.X, py - 3f * p.R, p.R, p.R, a);
                break;
            }
            case PKind.Rain:
            {
                float len = 0.02f + 0.008f * p.Depth;
                Streak(px, w, h, p.X - p.VX * len, py - p.VY * len, p.X, py, p.Alpha, 200, 215, 235);
                break;
            }
            case PKind.Splash:
            {
                float a = p.Alpha * MathF.Min(1f, p.Age * 6f);
                if (p.Shape == 1) Disc(px, w, h, p.X, py, p.R, a, 210, 225, 245, soft: true);
                else Disc(px, w, h, p.X, py, p.R, a, 250, 252, 255, soft: true);
                break;
            }
            case PKind.Ball: Ball(px, w, h, p.X, py, p.R, 1f); break;
            case PKind.Smoke:
            {
                float k = p.Age / MathF.Max(p.Fade, 0.01f);
                Disc(px, w, h, p.X, py, p.R, p.Alpha * k * k, 168, 170, 176, soft: true);
                break;
            }
        }
    }

    static float _time, _scale = 1f;
    static Material _mat;

    // Window rectangles this frame, and whether the particle being drawn needs per-pixel clipping.
    static (int L, int T, int R, int B)[] _occ = new (int, int, int, int)[64];
    static int _occN;
    static bool _clip;

    /// <summary>0 = box clear of all windows, 2 = box entirely inside one window, 1 = partly covered.</summary>
    static int Coverage(float x0, float y0, float x1, float y1)
    {
        bool touched = false;
        for (int i = 0; i < _occN; i++)
        {
            var o = _occ[i];
            if (x1 <= o.L || x0 >= o.R || y1 <= o.T || y0 >= o.B) continue;
            if (x0 >= o.L && x1 <= o.R && y0 >= o.T && y1 <= o.B) return 2;
            touched = true;
        }
        return touched ? 1 : 0;
    }

    /// <summary>Is this pixel inside any window (so background snow mustn't be drawn there)?</summary>
    static bool Covered(int x, int y)
    {
        for (int i = 0; i < _occN; i++)
        {
            var o = _occ[i];
            if (x >= o.L && x < o.R && y >= o.T && y < o.B) return true;
        }
        return false;
    }

    /// <summary>
    /// A snowflake crystal: a pre-rendered six-armed dendrite at the flake's size and spin, tilting
    /// in 3D as it tumbles (squashed vertically), with a soft glow so it isn't a hard cut-out.
    /// </summary>
    static void Crystal(uint* px, int w, int h, float cx, float cy, ref Particle p, float alpha)
    {
        var mask = Crystals.Get(p.Shape - 1, p.R, p.Rot, out int size);
        float tilt = p.Life > 0 ? 0.5f : 0.5f + 0.5f * MathF.Abs(MathF.Cos(_time * p.Freq * 0.7f + p.Phase));
        float half = size / 2f;
        Disc(px, w, h, cx, cy, half * 0.95f, alpha * 0.1f, 110, 125, 155, soft: true);  // cool halo for light backgrounds
        Disc(px, w, h, cx, cy, half * 0.55f, alpha * 0.22f, 255, 255, 255, soft: true); // soft glow
        int dh = Math.Max(1, (int)MathF.Ceiling(size * tilt));
        int x0 = (int)MathF.Round(cx - half), y0 = (int)MathF.Round(cy - dh / 2f);
        if (x0 + size <= 0 || y0 + dh <= 0 || x0 >= w || y0 >= h) return;
        Mark(x0, y0, x0 + size, y0 + dh);
        for (int dy = 0; dy < dh; dy++)
        {
            int y = y0 + dy;
            if ((uint)y >= (uint)h) continue;
            // Sample the mask between two rows (bilinear in y) for the squash.
            float sy = (dy + 0.5f) / tilt - 0.5f;
            int r0 = Math.Clamp((int)MathF.Floor(sy), 0, size - 1), r1 = Math.Min(r0 + 1, size - 1);
            float t = Math.Clamp(sy - r0, 0f, 1f);
            uint* row = px + y * w;
            for (int mx = 0; mx < size; mx++)
            {
                int x = x0 + mx;
                if ((uint)x >= (uint)w) continue;
                float m = mask[r0 * size + mx] * (1f - t) + mask[r1 * size + mx] * t;
                if (m < 0.03f) continue;
                if (_clip && Covered(x, y)) continue; // behind a window
                Blend(row + x, 242f, 247f, 255f, MathF.Min(1f, alpha * m * 0.88f));
            }
        }
    }

    /// <summary>A fluffy aggregate (several crystals stuck together): a few overlapping soft blobs.</summary>
    static void Fluffy(uint* px, int w, int h, float cx, float cy, ref Particle p, float alpha)
    {
        float r = p.R;
        float ox = MathF.Cos(p.Phase) * r * 0.7f, oy = MathF.Sin(p.Phase) * r * 0.5f;
        Disc(px, w, h, cx, cy, r + 1.4f, alpha * 0.12f, 110, 125, 155, soft: true);
        Disc(px, w, h, cx, cy, r * 0.9f, alpha * 0.9f, 255, 255, 255, soft: true);
        Disc(px, w, h, cx + ox, cy + oy, r * 0.62f, alpha * 0.75f, 250, 252, 255, soft: true);
        Disc(px, w, h, cx - oy, cy + ox * 0.6f, r * 0.5f, alpha * 0.7f, 250, 252, 255, soft: true);
    }

    /// <summary>Smooth 1-D value noise in [-1, 1] from the hash (for small lumps in the snow).</summary>
    static float Noise1(int seed, float x)
    {
        int i = (int)MathF.Floor(x);
        float t = x - i;
        t = t * t * (3f - 2f * t);
        float a = Hash01(i, seed), b = Hash01(i + 1, seed);
        return (a + (b - a) * t) * 2f - 1f;
    }

    /// <summary>Cheap stable hash to [0,1).</summary>
    static float Hash01(int a, int b)
    {
        uint h = unchecked((uint)(a * 374761393 + b * 668265263));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Draws the bottom-of-screen snow into the <see cref="GroundLayer"/> buffer, base on its last row.</summary>
    public static void DrawGround(uint* px, int w, int h, SnowSim sim)
    {
        new Span<uint>(px, w * h).Clear();
        if (!sim.GroundOn) return;
        _time = sim.Time;
        _mat = sim.Material;
        _scale = sim.UiScale;
        _clip = false;
        int yOff = (int)sim.Ground.AnchorY - h;
        if (sim.MeadowOn && sim.Season is Season.Spring or Season.Summer) DrawMeadow(px, w, h, sim, yOff);
        DrawPile(px, w, h, sim.Ground, yOff);
        var ps = sim.Particles;
        for (int n = 0; n < sim.Count; n++)
        {
            ref var p = ref ps[n];
            if (p.Life > 0 && p.Surf == IntPtr.Zero) DrawParticle(px, w, h, ref p, yOff);
        }
        DrawSnowmen(px, w, h, sim, ground: true, yOff);
        foreach (var c in sim.Critters)
            if (c.State == 1 && c.OnGround) DrawButterfly(px, w, h, c, yOff);
        if (sim.Vehicle != null) DrawVehicle(px, w, h, sim, sim.Vehicle, yOff);
        _clip = false;
    }

    /// <param name="yOff">Screen y of the buffer's first row.</param>
    static void DrawPile(uint* px, int w, int h, Surface s, int yOff)
    {
        // Snow on a window touching the top of the screen sits above the screen: not visible.
        if (s.Cells == 0 || s.Clamped) return;
        float maxH = 0;
        for (int i = 0; i < s.Cells; i++) if (s.H[i] > maxH) maxH = s.H[i];
        if (maxH < 0.05f) return;
        Mark(s.Left - 12, s.AnchorY - yOff - maxH - 16 - 12 * Sprites.Scale, s.Right + 12, s.AnchorY - yOff + 8);
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

            // Small lumps and clumps on the surface (more where the snow is deeper), so it isn't a
            // smooth plastic blanket. Fixed relative to the window, so they ride along with it.
            int xr = x - s.Left;
            if (hh > 0.5f)
                hh += (Noise1(s.Seed, xr / 9f) * 0.9f + Noise1(s.Seed + 5, xr / 4.5f) * (_mat == Material.Leaves ? 0.9f : 0.22f)) * MathF.Min(1f, hh / 6f);

            if (!s.IsGround)
            {
                // Snow builds right out to the window's edge and curls over it (see DrawCornice);
                // just round the last few pixels. At an end joined to a neighbouring window it only
                // rounds down to the neighbour's snow level, so the blanket carries on across the join.
                float dl = cx, dr = s.Right - s.Left - cx;
                bool leftNear = dl <= dr;
                float de = leftNear ? dl : dr;
                float floorH = 0f;
                float nTop = SnowSim.NeighbourTop(s, leftNear);
                bool joined = !float.IsNaN(nTop);
                if (joined) floorH = MathF.Max(0f, s.AnchorY - nTop);
                if (hh > floorH)
                {
                    float rad = joined ? MathF.Min(hh - floorH, 10f) : MathF.Min(hh, 4f);
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
                float clip = s.ClipY[ic] - yOff;
                float start = MathF.Max(top, clip);
                if (start >= bottom) continue;
                // The top edge is feathered over ~1.6px rather than a hard line.
                int y0 = Math.Max((int)MathF.Floor(start - 1f), 0), y1 = Math.Min((int)MathF.Ceiling(bottom), h);
                for (int y = y0; y < y1; y++)
                {
                    float yc = y + 0.5f;
                    if (yc < clip) continue;
                    float cov = Math.Clamp((yc - (top - 0.8f)) / 1.6f, 0f, 1f) * Math.Clamp(bottom - y, 0f, 1f);
                    if (cov <= 0.01f) continue;
                    float depth = MathF.Max(0f, yc - top);
                    // Slight grain, so the snow doesn't look like flat plastic; a touch darker right at
                    // the base where it's packed against the window (ambient occlusion).
                    float grain = Grain[(xr * 7 ^ (int)depth * 13 ^ s.Seed) & 255];
                    if (bottom - yc < 2f) grain *= 0.95f;
                    PileColor(px + y * w + x, depth, shade * grain, cov, xr, s.Seed);
                }

                // Glints: the odd ice crystal on the surface catches the light and twinkles.
                if (_mat == Material.Snow && hh > 1.5f && start <= top + 0.5f && Hash01(xr, s.Seed ^ 0x5bd1) > 0.988f)
                {
                    float ph = Hash01(xr, s.Seed + 77) * 6.283f, fr = 1.2f + Hash01(xr, s.Seed + 91) * 2.5f;
                    float tw = MathF.Sin(_time * fr + ph);
                    tw = tw > 0 ? tw * tw * tw * tw : 0f;
                    int gy = (int)(top + 1.5f);
                    if (tw > 0.05f && gy >= 1 && gy < h - 1 && x > 0 && x < w - 1)
                    {
                        Blend(px + gy * w + x, 255f, 255f, 255f, tw);
                        float side = tw * 0.45f;
                        Blend(px + gy * w + x - 1, 255f, 255f, 255f, side);
                        Blend(px + gy * w + x + 1, 255f, 255f, 255f, side);
                        Blend(px + (gy - 1) * w + x, 255f, 255f, 255f, side);
                        Blend(px + (gy + 1) * w + x, 255f, 255f, 255f, side);
                    }
                }

                // Soft contact shadow on the window right under the snow, so it sits on it: a 4px
                // falloff, none where the snow is only a dusting, fading out toward the pile's ends.
                if (!s.IsGround && hh > 3f)
                {
                    float toEnd = MathF.Min(xr, s.Right - s.Left - xr);
                    float strength = Math.Clamp((hh - 3f) / 8f, 0f, 1f) * Math.Clamp(toEnd / 24f, 0f, 1f) * 0.2f;
                    int sy0 = (int)MathF.Ceiling(bottom);
                    for (int k = 0; k < 4 && strength > 0.005f; k++)
                    {
                        int y = sy0 + k;
                        if ((uint)y >= (uint)h) break;
                        float f = 1f - k / 4f;
                        Blend(px + y * w + x, 10f, 14f, 24f, strength * f * f);
                    }
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
                    PileColor(px + y * w + x, bottom - (y + 0.5f), shade, MathF.Min(cov, 1f), xr, s.Seed);
                }
            }
        }

        if (_mat is Material.Leaves or Material.Petals) DrawFringe(px, w, h, s, yOff);
        if (!s.IsGround && yOff == 0 && _mat != Material.Petals)
        {
            if (s.LinkL == null) DrawCornice(px, w, h, s, left: true);
            if (s.LinkR == null) DrawCornice(px, w, h, s, left: false);
        }
    }

    /// <summary>
    /// Real snow doesn't stop dead at an edge: it curls over it a little and droops (a small
    /// cornice). Drawn just outside the window's free ends, sized by how deep the snow is there.
    /// </summary>
    static void DrawCornice(uint* px, int w, int h, Surface s, bool left)
    {
        int e = left ? 0 : s.Cells - 1;
        if (s.Vis[e] == 0) return;
        float hEnd = HeightAt(s, left ? 3f : s.Right - s.Left - 3f);
        if (hEnd < 3f) return;
        float over = MathF.Min(7f, hEnd * 0.42f);   // how far it reaches past the edge
        float droop = MathF.Min(4f, hEnd * 0.22f);  // how far its underside sags below the edge
        float edge = left ? s.Left : s.Right;
        int xa = (int)MathF.Floor(left ? edge - over : edge), xb = (int)MathF.Ceiling(left ? edge : edge + over);
        for (int x = Math.Max(xa, 0); x < Math.Min(xb, w); x++)
        {
            float q = Math.Clamp(MathF.Abs(x + 0.5f - edge) / over, 0f, 1f);
            float round = MathF.Sqrt(1f - q * q);
            // Its top rounds down from the pile's height; its underside sags a little below the edge.
            float top = s.AnchorY - hEnd * 0.9f * round, bottom = s.AnchorY + droop * round - hEnd * 0.2f * q;
            if (bottom - top < 0.5f) continue;
            // A faint shadow just under the overhang.
            int sy = (int)MathF.Ceiling(bottom);
            if (sy >= 0 && sy + 1 < h)
            {
                Blend(px + sy * w + x, 10f, 14f, 24f, 0.14f * round);
                Blend(px + (sy + 1) * w + x, 10f, 14f, 24f, 0.06f * round);
            }
            int y0 = Math.Max((int)MathF.Floor(top - 1f), 0), y1 = Math.Min((int)MathF.Ceiling(bottom), h);
            for (int y = y0; y < y1; y++)
            {
                float yc = y + 0.5f;
                float cov = Math.Clamp((yc - (top - 0.8f)) / 1.6f, 0f, 1f) * Math.Clamp(bottom - y, 0f, 1f);
                if (cov <= 0.01f) continue;
                PileColor(px + y * w + x, MathF.Max(0f, yc - top), 1f, cov, x - s.Left, s.Seed);
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
        Mark(xa - 2, MathF.Min(topA, topB) - 2, xb + 2, MathF.Max(a.AnchorY, b.AnchorY) + 2);
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
                PileColor(px + y * w + x, y + 0.5f - top, 1f, MathF.Min(cov, 1f), x - a.Left, a.Seed);
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

    static void PileColor(uint* p, float depth, float shade, float cov, int xr, int seed)
    {
        if (_mat == Material.Leaves) { LeafColor(p, depth, shade, cov, xr, seed); return; }
        if (_mat == Material.Petals) { PetalColor(p, depth, shade, cov, xr, seed); return; }
        // Lit surface only in the top few pixels; deeper snow is a uniform shadowed body.
        // The very top is bright and lit; deeper in, snow scatters blue light (thicker = bluer shade).
        shade = 1f + (shade - 1f) * Math.Clamp(1f - depth / 5f, 0f, 1f);
        float f = Math.Clamp(depth / 22f, 0f, 1f);
        f = f * (2f - f);
        float r = MathF.Min(255f, (255f - 32f * f) * shade);
        float g = MathF.Min(255f, (255f - 22f * f) * shade);
        float b = MathF.Min(255f, (255f - 8f * f) * shade);
        Blend(p, r, g, b, 0.97f * cov);
    }

    static void Disc(uint* px, int w, int h, float cx, float cy, float r, float alpha,
                     float cr, float cg, float cb, bool soft)
    {
        int x0 = Math.Max((int)MathF.Floor(cx - r - 1), 0), x1 = Math.Min((int)MathF.Ceiling(cx + r + 1), w);
        int y0 = Math.Max((int)MathF.Floor(cy - r - 1), 0), y1 = Math.Min((int)MathF.Ceiling(cy + r + 1), h);
        if (x0 >= x1 || y0 >= y1) return;
        Mark(x0, y0, x1, y1);
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
                if (_clip && Covered(x, y)) continue; // behind a window
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
    /// <summary>Small table of grain multipliers for the settled snow (cheaper than hashing per pixel).</summary>
    static readonly float[] Grain = MakeGrain();
    static float[] MakeGrain()
    {
        var rng = new Random(12345);
        var t = new float[256];
        for (int i = 0; i < t.Length; i++) t[i] = 1f + ((float)rng.NextDouble() - 0.5f) * 0.07f;
        return t;
    }

    static void Blend(uint* p, float r, float g, float b, float a)
    {
        uint d = *p;
        if (d == 0)
        {
            // Nothing drawn here yet (most of the snow): no need to mix with what's underneath.
            *p = (uint)(b * a) | ((uint)(g * a) << 8) | ((uint)(r * a) << 16) | ((uint)(255f * a) << 24);
            return;
        }
        float ia = 1f - a;
        uint nb = (uint)(b * a + (d & 0xFF) * ia);
        uint ng = (uint)(g * a + ((d >> 8) & 0xFF) * ia);
        uint nr = (uint)(r * a + ((d >> 16) & 0xFF) * ia);
        uint na = (uint)(255f * a + (d >> 24) * ia);
        *p = nb | (ng << 8) | (nr << 16) | (na << 24);
    }
}
