namespace Weather;

internal enum PKind : byte { Flake, Clump, Powder }

internal struct Particle
{
    public float X, Y, VX, VY, R, Alpha, Phase, Freq, Sway, Vol, Depth;
    public PKind Kind;
}

/// <summary>
/// Something snow can rest on: the top edge of a window, or the "ground" (top of the taskbar).
/// Snow depth is stored as a heightmap with one column every <see cref="SnowSim.Cell"/> pixels,
/// relative to the window, so the snow rides along when the window moves.
/// </summary>
internal sealed class Surface
{
    public IntPtr Hwnd;
    public bool IsGround, Clamped, Zoomed, HasPrev, Seen;
    public int Left, Top, Right, Bottom;

    /// <summary>Y the pile sits on. For windows touching the top of the screen ("clamped") the pile hangs down from y=0 instead.</summary>
    public float AnchorY;
    public float PrevAnchorY; // AnchorY last frame

    public float VX, VY;               // smoothed window velocity, px/s
    public float AX, AY;               // smoothed window acceleration, px/s²
    public float SlideVel, SlideOffset; // snow sliding relative to the window
    public float DripL, DripR;          // timers for edge drips
    public float SpillL, SpillR;        // snow (px²) that has flowed over each edge, waiting to drop

    // A window sitting right next to this one: the two piles join into one blanket across the gap.
    public Surface? LinkL, LinkR;
    public int LinkLEdge, LinkREdge;    // our outermost visible cell on that side

    // Natural variation (see SnowSim.ComputeFields): every surface gets its own slowly drifting
    // noise pattern, so snow builds into mounds and the two ends of a window never look alike.
    public int Seed;
    public float EdgeVarL = 1f, EdgeVarR = 1f; // character of each edge
    public float FieldTimer;
    public float[] W = Array.Empty<float>();   // how much falling snow settles here (≈1 on average)
    public float[] Rp = Array.Empty<float>();  // local steepness the snow can hold (× repose)
    public float[] Cp = Array.Empty<float>();  // local max depth (× max depth)

    public int Cells;
    public float[] H = Array.Empty<float>();      // snow depth per column, px
    public byte[] Vis = Array.Empty<byte>();      // 0 = this column is covered by a higher window
    public float[] ClipY = Array.Empty<float>();  // a higher window's bottom edge cutting into the pile

    public float PileTop(int i) => Clamped ? AnchorY + H[i] : AnchorY - H[i];

    /// <summary>Resize the heightmap; new[i] = old[i + offset].</summary>
    public void Reshape(int cells, int offset)
    {
        var nh = new float[cells];
        for (int i = 0; i < cells; i++)
        {
            int o = i + offset;
            if (o >= 0 && o < Cells) nh[i] = H[o];
        }
        H = nh;
        Cells = cells;
        Vis = new byte[cells];
        Array.Fill(Vis, (byte)1);
        ClipY = new float[cells];
        Array.Fill(ClipY, float.NegativeInfinity);
        W = new float[cells]; Array.Fill(W, 1f);
        Rp = new float[cells]; Array.Fill(Rp, 1f);
        Cp = new float[cells]; Array.Fill(Cp, 1f);
        FieldTimer = 0; // recompute the noise fields on the next settle
    }
}

/// <summary>The snow physics: falling flakes, piles on windows and the taskbar, flinging, sliding and dripping.</summary>
internal sealed class SnowSim
{
    public const int Cell = 2;
    const int MaxParticles = 40000;
    const float Gravity = 1500f;          // px/s² for clumps of snow
    const float BlowSpeed = 1400f;        // window speed at which the wind starts stripping powder
    const float Settling = 2.5f;          // smoothing rate of fresh snow, 1/s (low, so mounds survive)
    const float DriftRate = 0.7f;         // how strongly wind moves settled snow along a surface
    const float MaxPile = 500f;
    const float CatchChance = 0.3f;       // share of flakes crossing a window's top edge that land on it

    // Tunables derived from the user's settings every frame (see ApplySettings).
    float FlingAccel = 11000f;     // window deceleration (px/s²) that throws snow off
    float FlingMinSpeed = 300f;    // window must have been moving up at least this fast to fling
    float SlideAccel = 5000f;      // sideways acceleration that overcomes friction
    float SlideFriction = 1800f;   // how quickly sliding snow comes to rest
    float ShakeAccel = 4000f;      // sideways acceleration that starts throwing snow off
    float Repose = 0.75f;          // max height step between neighbouring columns
    float EdgeLimit = 7f;          // snow deeper than this at an edge starts dripping off
    float MaxDepth = 40f;          // deepest a pile can get; beyond this it avalanches outward
    float DripPace = 1f;           // multiplier on time between drips
    float FlakeDeposit = 1.5f;     // how much snow one flake adds
    float SpawnPerSec = 55f, FlakeScale = 1f, FlakeAlpha = 0.7f, BaseWind = 22f, Gustiness = 0.5f;
    public bool GroundOn { get; private set; } = true;
    int WipeSeconds;

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    void ApplySettings(Settings st)
    {
        float stick = Math.Clamp(st.Stickiness / 100f, 0f, 1f);
        Repose = Lerp(0.3f, 1.6f, stick);
        EdgeLimit = Lerp(2.5f, 15f, stick);
        DripPace = Lerp(0.35f, 1.6f, stick);
        SlideAccel = Lerp(2200f, 10000f, stick);
        SlideFriction = Lerp(700f, 3800f, stick);
        ShakeAccel = Lerp(1800f, 9000f, stick);

        MaxDepth = Math.Clamp(st.MaxDepth, 4, 300);
        EdgeLimit = MathF.Min(EdgeLimit, MaxDepth);

        float fling = Math.Clamp(st.FlingSensitivity / 100f, 0f, 1f);
        FlingAccel = Lerp(22000f, 5000f, fling);
        FlingMinSpeed = Lerp(650f, 150f, fling);

        FlakeDeposit = 1.5f * Math.Clamp(st.BuildupSpeed, 0, 1000) / 100f;
        SpawnPerSec = Math.Max(0, st.SnowfallRate);
        FlakeScale = Math.Clamp(st.FlakeSize, 20, 400) / 100f;
        FlakeAlpha = Math.Clamp(st.FlakeBrightness, 5, 100) / 100f;
        BaseWind = st.Wind;
        Gustiness = Math.Clamp(st.Gusts, 0, 100) / 100f;
        WipeSeconds = st.WipeSeconds;
        if (GroundOn && !st.TaskbarSnow) Array.Clear(Ground.H);
        GroundOn = st.TaskbarSnow;
    }
    static readonly float[] Kernel = { 0.04f, 0.08f, 0.13f, 0.17f, 0.16f, 0.17f, 0.13f, 0.08f, 0.04f };

    readonly Random _rng = new();
    readonly Dictionary<IntPtr, Surface> _windows = new();
    readonly List<IntPtr> _gone = new();

    public readonly Particle[] Particles = new Particle[MaxParticles];
    public int Count;
    public readonly List<Surface> Z = new(); // window surfaces, topmost first
    public readonly Surface Ground = new() { IsGround = true };
    public int ScreenW, ScreenH;
    public float Wind;

    float _time, _spawnAcc, _wipeAcc;

    float R() => (float)_rng.NextDouble();
    float R(float a, float b) => a + (b - a) * R();
    static int CellsFor(int width) => Math.Max(1, (width + Cell - 1) / Cell);

    public void Update(float dt, List<WinInfo> wins, int screenW, int screenH, int groundY, Settings settings)
    {
        _time += dt;
        ScreenW = screenW;
        ScreenH = screenH;
        ApplySettings(settings);

        SyncWindows(wins, dt);
        SyncGround(groundY);
        foreach (var s in Z) HandOff(s);
        ComputeVisibility();
        foreach (var s in Z)
            if (!s.Clamped) Collide(s);
        ComputeLinks();
        UpdateWind(dt);
        Spawn(dt);
        StepParticles(dt);
        foreach (var s in Z) Settle(s, dt);
        foreach (var s in Z) FlowAcross(s);
        Settle(Ground, dt);
        WipeTimer(dt);
    }

    void WipeTimer(float dt)
    {
        if (WipeSeconds > 0)
        {
            _wipeAcc += dt;
            if (_wipeAcc >= WipeSeconds) Wipe();
        }
        else _wipeAcc = 0;
    }

    /// <summary>
    /// A maximized window is in front, so nothing is visible. Time stands still for the visible snow,
    /// but snow keeps settling unseen on top of the maximized window (above the screen). When the
    /// window is minimized or restored, that snow falls into view.
    /// </summary>
    public void UpdateHidden(float dt, List<WinInfo> wins, int screenW, int screenH, int groundY,
                             IntPtr roof, Settings settings)
    {
        _time += dt;
        ScreenW = screenW;
        ScreenH = screenH;
        ApplySettings(settings);
        SyncWindows(wins, dt);
        SyncGround(groundY);

        if (_windows.TryGetValue(roof, out var s))
        {
            _spawnAcc += SpawnRate() * dt;
            while (_spawnAcc >= 1f)
            {
                _spawnAcc -= 1f;
                float d = R();
                float r = (0.8f + d * d * 2.4f) * FlakeScale;
                float x = R(s.Left, s.Right);
                Deposit(s, x, MathF.PI * r * r * FlakeDeposit * WeightAt(s, x));
            }
            Settle(s, dt);
        }
        WipeTimer(dt);
    }

    /// <summary>Clears all settled and tumbling snow. Falling flakes keep falling.</summary>
    public void Wipe()
    {
        _wipeAcc = 0;
        foreach (var s in _windows.Values) Array.Clear(s.H);
        Array.Clear(Ground.H);
        for (int n = 0; n < Count;)
        {
            if (Particles[n].Kind != PKind.Flake) Particles[n] = Particles[--Count];
            else n++;
        }
    }

    /// <summary>Forget window velocities (after a pause) so nothing gets flung on resume.</summary>
    public void ResetTracking()
    {
        foreach (var s in _windows.Values)
        {
            s.HasPrev = false;
            s.VX = s.VY = s.AX = s.AY = s.SlideVel = 0;
        }
    }

    // ───────────────────────────── windows ─────────────────────────────

    void SyncWindows(List<WinInfo> wins, float dt)
    {
        foreach (var s in _windows.Values) s.Seen = false;
        Z.Clear();
        foreach (var w in wins)
        {
            if (!_windows.TryGetValue(w.Hwnd, out var s))
            {
                s = new Surface { Hwnd = w.Hwnd, Seed = _rng.Next(1, int.MaxValue / 2), EdgeVarL = R(0.65f, 1.35f), EdgeVarR = R(0.65f, 1.35f) };
                Place(s, w);
                s.PrevAnchorY = s.AnchorY;
                s.Reshape(CellsFor(s.Right - s.Left), 0);
                _windows[w.Hwnd] = s;
            }
            else Track(s, w, dt);
            s.Seen = true;
            Z.Add(s);
        }

        // Minimized, closed or hidden: the whole pile falls.
        _gone.Clear();
        foreach (var kv in _windows)
            if (!kv.Value.Seen) _gone.Add(kv.Key);
        foreach (var h in _gone)
        {
            Shed(_windows[h], 1f, 0, 0, 40f);
            _windows.Remove(h);
        }
    }

    static void Place(Surface s, in WinInfo w)
    {
        s.Left = w.Rect.Left;
        s.Top = w.Rect.Top;
        s.Right = w.Rect.Right;
        s.Bottom = w.Rect.Bottom;
        s.Zoomed = w.Zoomed;
        s.Clamped = s.Top <= 0;
        s.AnchorY = s.Clamped ? 0 : s.Top;
    }

    void Track(Surface s, in WinInfo w, float dt)
    {
        s.PrevAnchorY = s.AnchorY;
        var r = w.Rect;
        int newW = r.Width;

        // Maximize / restore: the top edge jumps to a new place.
        if (w.Zoomed != s.Zoomed)
        {
            ShoveOrDrop(s, r.Top);
            Place(s, w);
            s.Reshape(CellsFor(newW), 0);
            s.HasPrev = false;
            return;
        }

        // Resized: keep the snow attached to the edge that didn't move, drop what no longer fits.
        if (newW != s.Right - s.Left)
        {
            int dl = r.Left - s.Left, dr = r.Right - s.Right;
            int offset = 0;
            if (Math.Abs(dl) <= 1) offset = 0;
            else if (Math.Abs(dr) <= 1) offset = (int)MathF.Round(dl / (float)Cell);
            else ShoveOrDrop(s, r.Top); // snapped / moved and resized at once

            int newCells = CellsFor(newW);
            for (int i = 0; i < s.Cells; i++)
            {
                int ni = i - offset;
                if (ni < 0 || ni >= newCells) DropCell(s, i);
            }
            s.Reshape(newCells, offset);
            Place(s, w);
            s.HasPrev = false;
            return;
        }

        float dx = r.Left - s.Left, dy = r.Top - s.Top;
        float pvx = s.VX, pvy = s.VY;
        bool teleport = MathF.Abs(dx) > 600 || MathF.Abs(dy) > 600; // keyboard moves, snaps
        Place(s, w);
        if (!s.HasPrev || teleport)
        {
            s.VX = s.VY = s.AX = s.AY = 0;
            s.HasPrev = true;
            return;
        }

        // Smooth over time (frame-rate independent): the mouse moves windows in uneven little
        // steps, and at 100+ fps raw differences of those steps look like violent jolts.
        float kv = 1f - MathF.Exp(-dt / 0.05f);
        s.VX += (dx / dt - s.VX) * kv;
        s.VY += (dy / dt - s.VY) * kv;
        float ka = 1f - MathF.Exp(-dt / 0.03f);
        s.AX += ((s.VX - pvx) / dt - s.AX) * ka;
        s.AY += ((s.VY - pvy) / dt - s.AY) * ka;

        // The window can only push snow up. If it decelerates downward harder than the snow can
        // follow (stopping after a fast upward move, or being yanked down hard), the snow flies
        // free with the velocity it had. Slow drags never get here.
        bool fastUpStop = s.AY > FlingAccel && pvy < -FlingMinSpeed;
        bool hardYank = s.AY > FlingAccel * 2.2f;
        if (fastUpStop || hardYank)
        {
            Shed(s, Math.Clamp((s.AY - FlingAccel) / (2f * FlingAccel), 0.35f, 1f), pvx, pvy, 70f);
            s.AY = 0; // one fling per jolt
        }

        // Sideways jolts: loose snow can't change direction with the window, so it keeps the speed
        // it had and is thrown off. Shaking a window side to side sprays snow everywhere.
        if (MathF.Abs(s.AX) > ShakeAccel)
        {
            float frac = Math.Clamp((MathF.Abs(s.AX) - ShakeAccel) / (3f * ShakeAccel), 0f, 0.7f)
                         * MathF.Min(1f, dt * 40f);
            ThrowSideways(s, frac, pvx, pvy);
        }

        // Harder jolts also overcome friction and slide the rest of the pile the other way.
        if (MathF.Abs(s.AX) > SlideAccel)
            s.SlideVel -= (s.AX - MathF.Sign(s.AX) * SlideAccel) * dt;

        // Very fast motion strips loose powder off the top.
        float speed = MathF.Sqrt(s.VX * s.VX + s.VY * s.VY);
        if (speed > BlowSpeed) Erode(s, (speed - BlowSpeed) / BlowSpeed, s.VX, s.VY, dt);
    }

    void SyncGround(int groundY)
    {
        var g = Ground;
        if (g.Right != ScreenW || g.Cells == 0)
        {
            g.Reshape(CellsFor(ScreenW), 0);
            g.Left = 0;
            g.Right = ScreenW;
        }
        g.AnchorY = g.Top = groundY;
        g.Bottom = ScreenH;
    }

    /// <summary>Work out which parts of each pile are hidden behind windows higher in the Z-order.</summary>
    void ComputeVisibility()
    {
        // The ground (bottom of the screen / top of the taskbar) is the back layer: every window is in front of it.
        for (int k = 0; k <= Z.Count; k++)
        {
            var s = k == Z.Count ? Ground : Z[k];
            if (s.Cells == 0) continue;
            Array.Fill(s.Vis, (byte)1);
            Array.Fill(s.ClipY, float.NegativeInfinity);
            for (int j = 0; j < k; j++)
            {
                var o = Z[j];
                if (o.Right <= s.Left || o.Left >= s.Right) continue;
                int i0 = Math.Max(0, (int)MathF.Ceiling((o.Left - s.Left - Cell * 0.5f) / Cell));
                int i1 = Math.Min(s.Cells - 1, (int)MathF.Ceiling((o.Right - s.Left - Cell * 0.5f) / Cell) - 1);
                if (i0 > i1) continue;
                if (o.Top <= s.AnchorY && o.Bottom > s.AnchorY)
                    Array.Fill(s.Vis, (byte)0, i0, i1 - i0 + 1);
                else if (!s.Clamped && o.Bottom <= s.AnchorY)
                    for (int i = i0; i <= i1; i++)
                        if (o.Bottom > s.ClipY[i]) s.ClipY[i] = o.Bottom;
            }
        }
    }

    /// <summary>
    /// Windows behind this one are solid walls for its snow. If this window's top edge slides up under
    /// a window behind it (or that window comes down onto it), the snow in those columns is squashed to
    /// fit the gap and bulldozed sideways to just outside the other window. Snow pushed past this
    /// window's ends falls off. Windows in front pass over the snow without touching it.
    /// </summary>
    void Collide(Surface s)
    {
        var h = s.H;
        int k = Z.IndexOf(s);
        for (int j = 0; j < Z.Count; j++)
        {
            var o = Z[j];
            // Only windows *behind* us are walls for our snow; a window in front just passes over it.
            if (j <= k || o.Right <= s.Left || o.Left >= s.Right || o.Top >= s.AnchorY) continue;
            // Our top edge is already in front of its body: the gap squeezed our snow on the way in.
            if (o.Bottom >= s.AnchorY) continue;

            int i0 = Math.Max(0, (int)MathF.Ceiling((o.Left - s.Left - Cell * 0.5f) / Cell));
            int i1 = Math.Min(s.Cells - 1, (int)MathF.Ceiling((o.Right - s.Left - Cell * 0.5f) / Cell) - 1);
            if (i0 > i1) continue;

            // Room left between our top edge and the other window's bottom edge.
            float cap = s.AnchorY - o.Bottom;

            // Squashed snow doesn't stack up into a spike next to the obstacle: it breaks off and is
            // squirted out sideways past the obstacle's edge, landing wherever it lands.
            float volL = 0, volR = 0;
            for (int i = i0; i <= i1; i++)
            {
                float excess = h[i] - cap;
                if (excess <= 0) continue;
                h[i] = cap;
                if (i - i0 <= i1 - i) volL += excess * Cell; else volR += excess * Cell;
            }
            float y = s.AnchorY - MathF.Max(cap, 0f) - 2f;
            Squirt(volL, MathF.Max(s.Left - 2f, o.Left - 3f), y, -1f, s);
            Squirt(volR, MathF.Min(s.Right + 2f, o.Right + 3f), y, 1f, s);
        }
    }

    /// <summary>
    /// Snow changing hands between a moving window and the surfaces behind it (other windows and the
    /// bottom-of-screen ground, which is the back layer):
    /// moving down, wherever our top edge passes their snow surface, our snow can't follow through
    /// that solid edge and is left on top of it; moving up, wherever our top edge sweeps up past
    /// their edge, we scoop their snow up and carry it.
    /// </summary>
    void HandOff(Surface s)
    {
        if (s.Clamped) return;
        bool down = s.AnchorY > s.PrevAnchorY + 0.5f, up = s.AnchorY < s.PrevAnchorY - 0.5f;
        if (!down && !up) return;
        int k = Z.IndexOf(s);
        for (int j = -1; j < Z.Count; j++)
        {
            if (j >= 0 && j <= k) continue; // windows in front of us don't interact with our snow
            if (j < 0 && !GroundOn) continue;
            var o = j < 0 ? Ground : Z[j];
            if (o.Clamped || o.Cells == 0 || o.Right <= s.Left || o.Left >= s.Right) continue;

            // Snow changing hands is knocked about: part of it settles straight onto the new surface,
            // the rest breaks into chunks that tumble and land wherever they land.
            float looseVol = 0, looseX = 0, looseY = 0, looseVy = 0;
            int looseN = 0;
            void Loosen(float vol, float x, float y, float vy)
            {
                looseVol += vol; looseX += x; looseY += y; looseVy += vy; looseN++;
                if (looseN >= 3 || looseVol > 10f) FlushLoose();
            }
            void FlushLoose()
            {
                if (looseN > 0 && looseVol > 0.3f)
                    AddClump(looseX / looseN, looseY / looseN - 1f, looseVol, R(-45f, 45f), looseVy / looseN - R(0f, 70f),
                             looseVol < 2.5f ? PKind.Powder : PKind.Clump);
                looseVol = looseX = looseY = looseVy = 0;
                looseN = 0;
            }

            if (down)
            {
                for (int i = 0; i < s.Cells; i++)
                {
                    if (s.H[i] <= 0.02f) continue;
                    float x = s.Left + (i + 0.5f) * Cell;
                    if (x < o.Left || x >= o.Right) continue;
                    int io = Math.Min((int)((x - o.Left) / Cell), o.Cells - 1);
                    float surface = o.PileTop(io);
                    if (s.PrevAnchorY <= surface + 1f && s.AnchorY > surface)
                    {
                        float amount = s.H[i], keep = amount * R(0.5f, 0.8f);
                        o.H[io] = MathF.Min(MaxPile, o.H[io] + keep);
                        s.H[i] = 0;
                        Loosen((amount - keep) * Cell, x, surface - keep, 0f);
                    }
                }
            }
            else if (s.PrevAnchorY >= o.AnchorY - 0.5f && s.AnchorY < o.AnchorY)
            {
                for (int io = 0; io < o.Cells; io++)
                {
                    if (o.H[io] <= 0.02f) continue;
                    float x = o.Left + (io + 0.5f) * Cell;
                    if (x < s.Left || x >= s.Right) continue;
                    int i = Math.Min((int)((x - s.Left) / Cell), s.Cells - 1);
                    float amount = o.H[io], keep = amount * R(0.5f, 0.8f);
                    s.H[i] = MathF.Min(MaxPile, s.H[i] + keep);
                    o.H[io] = 0;
                    Loosen((amount - keep) * Cell, x, s.AnchorY - s.H[i], MathF.Min(s.VY, 0f) * 0.6f);
                }
            }
            FlushLoose();
        }
    }

    void Squirt(float vol, float x, float y, float dir, Surface s)
    {
        while (vol > 0.3f)
        {
            float chunk = MathF.Min(vol, R(3f, 10f));
            vol -= chunk;
            AddClump(x + dir * R(0f, 6f), y - R(0f, 4f), chunk,
                     dir * R(50f, 220f) + s.VX * 0.4f, -R(30f, 170f) + MathF.Min(s.VY, 0f) * 0.3f,
                     chunk < 2.5f ? PKind.Powder : PKind.Clump);
        }
    }

    void SpillOffEnd(Surface s, float vol, bool left)
    {
        while (vol > 0.3f)
        {
            float chunk = MathF.Min(vol, 14f);
            vol -= chunk;
            float x = left ? s.Left - 1.5f : s.Right + 1.5f;
            AddClump(x, s.AnchorY - R(1f, 6f), chunk, (left ? -1f : 1f) * R(20f, 60f) + s.VX * 0.5f,
                     R(-10f, 20f) + MathF.Min(s.VY, 0f) * 0.3f, PKind.Clump);
        }
    }

    // ───────────────────────────── particles ─────────────────────────────

    void UpdateWind(float dt)
    {
        // Slow swells, medium gusts and short puffs, all scaled by the Gusts setting; strong gusts
        // can briefly turn the wind around.
        float b = BaseWind, g = Gustiness, t = _time;
        float swell = 0.55f * MathF.Sin(t * 0.11f) + 0.35f * MathF.Sin(t * 0.31f + 1.3f);
        float puffs = Noise(9001, t * 0.9f) * 0.6f + Noise(9002, t * 2.3f) * 0.4f;
        float target = b * (1f + g * swell)
                     + g * (14f + MathF.Abs(b) * 0.6f) * MathF.Sin(t * 0.07f + 0.4f)
                     + g * (10f + MathF.Abs(b) * 0.7f) * puffs;
        Wind += (target - Wind) * MathF.Min(1f, dt * 1.5f);
    }

    float SpawnRate() => SpawnPerSec * ScreenW / 1920f;

    void Spawn(float dt)
    {
        _spawnAcc += SpawnRate() * dt;
        while (_spawnAcc >= 1f)
        {
            _spawnAcc -= 1f;
            if (Count >= MaxParticles) { _spawnAcc = 0; break; }
            ref var p = ref Particles[Count++];
            p = default;
            p.Kind = PKind.Flake;
            float d = R();
            d *= d; // most flakes small and far away
            p.Depth = d;
            p.R = (0.8f + d * 2.4f) * FlakeScale;
            float depth = d;
            p.VY = 38f + 70f * depth + R(0f, 14f);
            p.Alpha = FlakeAlpha * (0.43f + 0.57f * depth); // mostly faint, only near flakes are bright
            p.Sway = R(6f, 26f);
            p.Freq = R(0.5f, 1.6f);
            p.Phase = R(0f, 6.283f);
            float drift = Wind * (ScreenH / p.VY) * 0.5f;
            p.X = R(-60f, ScreenW + 60f) - drift;
            p.Y = -R(4f, 30f);
            p.Vol = MathF.PI * p.R * p.R * FlakeDeposit;
        }
    }

    void AddClump(float x, float y, float vol, float vx, float vy, PKind kind)
    {
        if (Count >= MaxParticles) return;
        ref var p = ref Particles[Count++];
        p = default;
        p.Kind = kind;
        p.X = x; p.Y = y; p.VX = vx; p.VY = vy; p.Vol = vol;
        float r = MathF.Sqrt(vol / MathF.PI);
        p.R = kind == PKind.Powder ? Math.Clamp(r * 1.2f, 0.7f, 1.6f) : Math.Clamp(r * 1.15f, 0.9f, 5f);
        p.Alpha = kind == PKind.Powder ? 0.75f : 0.97f;
        p.Phase = R(0f, 6.283f);
        p.Freq = R(2f, 5f);
    }

    void StepParticles(float dt)
    {
        float wind = Wind;
        for (int n = 0; n < Count;)
        {
            ref var p = ref Particles[n];
            float y0 = p.Y;
            switch (p.Kind)
            {
                case PKind.Flake:
                {
                    float depth = p.Depth;
                    float fw = wind * (0.55f + 0.45f * depth);
                    p.X += (fw + MathF.Sin(_time * p.Freq + p.Phase) * p.Sway) * dt;
                    p.Y += p.VY * dt;
                    break;
                }
                case PKind.Clump:
                {
                    float drag = 1.6f / (0.5f + p.R * 0.35f);
                    p.VY += Gravity * dt;
                    p.VX += (wind - p.VX) * drag * dt;
                    p.VY -= p.VY * drag * dt;
                    p.X += p.VX * dt;
                    p.Y += p.VY * dt;
                    break;
                }
                default: // powder
                    p.VY += 220f * dt;
                    p.VX += (wind - p.VX) * 2.8f * dt;
                    p.VY -= p.VY * 2.2f * dt;
                    p.X += (p.VX + MathF.Sin(_time * p.Freq + p.Phase) * 12f) * dt;
                    p.Y += p.VY * dt;
                    break;
            }

            bool remove = false;
            // Blown spray (Vol 0) is only for show and never settles.
            if (p.Y > y0 && (p.Vol > 0 || p.Kind == PKind.Flake) && TryLand(p.X, y0, p.Y, p.Kind == PKind.Flake, out var hit))
            {
                // Only a share of flakes hit a window edge, so scale them up to keep the buildup rate.
                float vol = p.Kind == PKind.Flake && !hit!.IsGround ? p.Vol / CatchChance : p.Vol;
                if (p.Kind == PKind.Flake) vol *= WeightAt(hit!, p.X); // uneven buildup: mounds and hollows
                Deposit(hit!, p.X, vol);
                remove = true;
            }
            else if (p.Y > ScreenH + 40 || p.Y < -4000 || p.X < -2500 || p.X > ScreenW + 2500)
                remove = true;

            if (remove) { Particles[n] = Particles[--Count]; continue; }
            n++;
        }
    }

    /// <summary>Find the first visible snow surface a particle falling from y0 to y1 at x hits.</summary>
    /// <remarks>
    /// Windows are flat panes: most flakes fall in front of them, and only the ones that happen to be
    /// at the window's depth (<see cref="CatchChance"/>) land on its top edge. Clumps that came off a
    /// pile are already at window depth, so they always land. The taskbar is the floor and catches everything.
    /// </remarks>
    bool TryLand(float x, float y0, float y1, bool isFlake, out Surface? hit)
    {
        foreach (var s in Z)
        {
            if (x < s.Left || x >= s.Right) continue;
            int i = Math.Min((int)((x - s.Left) / Cell), s.Cells - 1);
            if (s.Vis[i] == 0) continue;
            float top = s.PileTop(i);
            if (y1 < top) continue;
            bool crosses = s.Clamped
                ? y0 <= top
                : y0 <= s.AnchorY + 1f && y1 >= s.ClipY[i];
            if (!crosses) continue;
            if (isFlake && (y0 > top || R() >= CatchChance)) continue; // passes in front
            hit = s;
            return true;
        }
        if (GroundOn && x >= 0 && x < ScreenW && Ground.Cells > 0)
        {
            // Behind a window? The flake fell in front of that window and just leaves the screen.
            int i = Math.Min((int)(x / Cell), Ground.Cells - 1);
            if (Ground.Vis[i] != 0 && y1 >= Ground.PileTop(i) && y1 >= Ground.ClipY[i]) { hit = Ground; return true; }
        }
        hit = null;
        return false;
    }

    /// <summary>Spread landed snow in a soft tent; bigger chunks splat wider, so nothing lands as a spike.</summary>
    static void Deposit(Surface s, float x, float vol)
    {
        int c = (int)((x - s.Left) / Cell);
        int half = Math.Clamp(4 + (int)MathF.Sqrt(vol), 4, 16);
        float total = 0;
        for (int k = -half; k <= half; k++) total += half + 1 - Math.Abs(k);
        for (int k = -half; k <= half; k++)
        {
            int i = c + k;
            if ((uint)i >= (uint)s.Cells) continue;
            float h = s.H[i] + vol * (half + 1 - Math.Abs(k)) / total / Cell;
            s.H[i] = h > MaxPile ? MaxPile : h;
        }
    }

    // ───────────────────────────── pile behaviour ─────────────────────────────

    /// <summary>Turn a fraction of a pile into flying clumps.</summary>
    void Shed(Surface s, float frac, float vx, float vy, float spread)
    {
        for (int i = 0; i < s.Cells; i += 2)
        {
            int end = Math.Min(i + 2, s.Cells);
            float vol = 0;
            for (int j = i; j < end; j++)
            {
                float a = s.H[j] * frac;
                s.H[j] -= a;
                vol += a * Cell;
            }
            if (vol < 0.4f) continue;
            float x = s.Left + i * Cell + Cell;
            float y = s.Clamped ? s.AnchorY + s.H[i] + 2f : s.AnchorY - s.H[i] - 2f;
            int n = vol > 30 ? 3 : vol > 10 ? 2 : 1;
            for (int k = 0; k < n; k++)
                AddClump(x + R(-1.5f, 1.5f), y - k * 2.5f, vol / n,
                         vx * R(0.8f, 1.1f) + R(-spread, spread),
                         vy * R(0.8f, 1.1f) + R(-spread, spread * 0.5f), PKind.Clump);
        }
    }

    /// <summary>
    /// The window's top edge jumped to <paramref name="newTop"/>. If it jumped up (maximize, snap to top)
    /// the edge sweeps through the snow and throws it upward from the new edge; it then falls back onto
    /// the window. If it jumped down (restore) the snow is left in the air and falls.
    /// </summary>
    void ShoveOrDrop(Surface s, int newTop)
    {
        float oldTop = s.AnchorY;
        float edge = Math.Max(newTop, 0);
        if (edge < oldTop - 4f)
        {
            float jump = oldTop - edge;
            s.AnchorY = edge;
            s.Clamped = false;
            Shed(s, 1f, 0f, -MathF.Min(1700f, 450f + jump * 2.5f), 90f);
        }
        else Shed(s, 1f, 0f, 0f, 30f);
    }

    /// <summary>Loose top snow keeps its old velocity while the window lurches sideways underneath it.</summary>
    void ThrowSideways(Surface s, float frac, float vx, float vy)
    {
        if (frac <= 0.001f) return;
        for (int i = 0; i < s.Cells; i += 2)
        {
            int end = Math.Min(i + 2, s.Cells);
            float vol = 0;
            for (int j = i; j < end; j++)
            {
                if (s.H[j] < 0.3f) continue;
                float a = s.H[j] * frac;
                s.H[j] -= a;
                vol += a * Cell;
            }
            if (vol < 0.25f) continue;
            float x = s.Left + i * Cell + Cell;
            float y = s.Clamped ? s.AnchorY + s.H[i] + 1f : s.AnchorY - s.H[i] - 1f;
            AddClump(x, y, vol, vx * R(0.75f, 1.15f) + R(-140f, 140f),
                     -R(40f, 280f) + MathF.Min(vy, 0f) * 0.5f, vol < 3f ? PKind.Powder : PKind.Clump);
        }
    }

    void DropCell(Surface s, int i)
    {
        float vol = s.H[i] * Cell;
        s.H[i] = 0;
        if (vol < 0.3f) return;
        float x = s.Left + i * Cell + Cell * 0.5f;
        AddClump(x, s.Clamped ? s.AnchorY + 2f : s.AnchorY - 2f, vol, R(-20f, 20f), R(0f, 30f), PKind.Clump);
    }

    void Erode(Surface s, float strength, float vx, float vy, float dt)
    {
        float chance = MathF.Min(1f, strength * 6f * dt);
        for (int i = 0; i < s.Cells; i++)
        {
            if (s.H[i] < 0.3f || R() > chance) continue;
            float a = MathF.Min(s.H[i], 0.4f + strength * 0.6f);
            s.H[i] -= a;
            if (s.Vis[i] == 0) continue;
            AddClump(s.Left + i * Cell + Cell * 0.5f, s.PileTop(i) - 1f, a * Cell,
                     vx * R(0.4f, 0.8f), vy * R(0.4f, 0.8f) - R(20f, 80f), PKind.Powder);
        }
    }

    // ───────────────────────────── neighbouring windows ─────────────────────────────

    const float LinkMaxGap = 14f, LinkMaxStep = 40f;

    /// <summary>
    /// Find windows sitting side by side (snapped, or nearly touching, tops at similar heights).
    /// Their touching ends are joined: no rounded-off edges, no snow spilling into the gap, and
    /// snow flows from one to the other according to the actual surface heights.
    /// </summary>
    void ComputeLinks()
    {
        foreach (var s in Z) { s.LinkL = s.LinkR = null; }
        foreach (var a in Z)
        {
            if (a.Clamped || a.Cells < 4) continue;
            float bestGap = float.MaxValue;
            foreach (var b in Z)
            {
                if (b == a || b.Clamped || b.Cells < 4 || b.LinkL != null) continue;
                if (MathF.Abs(a.AnchorY - b.AnchorY) > LinkMaxStep) continue;
                // a's right end against b's left end; allow a little overlap.
                float gap = b.Left - a.Right;
                if (gap < -LinkMaxGap || gap > LinkMaxGap) continue;
                int ea = LastVisible(a), eb = FirstVisible(b);
                if (ea < 0 || eb < 0) continue;
                float xa = a.Left + (ea + 1) * Cell, xb = b.Left + eb * Cell;
                float realGap = xb - xa;
                if (realGap < -Cell * 2 || realGap > LinkMaxGap || MathF.Abs(realGap) >= bestGap) continue;
                bestGap = MathF.Abs(realGap);
                if (a.LinkR != null) a.LinkR.LinkL = null;
                a.LinkR = b; a.LinkREdge = ea;
                b.LinkL = a; b.LinkLEdge = eb;
            }
        }
    }

    // Outermost visible cells near each end (an end tucked under a window in front doesn't count).
    static int LastVisible(Surface s)
    {
        for (int i = s.Cells - 1; i >= Math.Max(0, s.Cells - 1 - (int)(LinkMaxGap / Cell)); i--) if (s.Vis[i] != 0) return i;
        return -1;
    }
    static int FirstVisible(Surface s)
    {
        for (int i = 0; i < Math.Min(s.Cells, 1 + (int)(LinkMaxGap / Cell)); i++) if (s.Vis[i] != 0) return i;
        return -1;
    }

    /// <summary>
    /// Snow going over one end of a window: onto the neighbouring window if one is joined there,
    /// otherwise into that edge's spill, to drop off in chunks.
    /// </summary>
    void OverEdge(Surface s, bool left, float vol)
    {
        var n = left ? s.LinkL : s.LinkR;
        if (n != null)
        {
            int e = left ? n.LinkREdge : n.LinkLEdge;
            if (e >= 0 && e < n.Cells) { n.H[e] = MathF.Min(MaxPile, n.H[e] + vol / Cell); return; }
        }
        if (left) s.SpillL += vol; else s.SpillR += vol;
    }

    /// <summary>Let snow flow across a join, levelling by real surface height (a higher pile slides onto a lower one).</summary>
    void FlowAcross(Surface a)
    {
        var b = a.LinkR;
        if (b == null) return;
        int ea = a.LinkREdge, eb = b.LinkLEdge;
        for (int k = 0; k < 3; k++) // a few cells deep on each side, so the join looks continuous
        {
            int ia = ea - k, ib = eb + k;
            if (ia < 0 || ib >= b.Cells) break;
            float topA = a.AnchorY - a.H[ia], topB = b.AnchorY - b.H[ib];
            float rep = Repose * (k * 2 + 1);
            float d = topB - topA; // > 0: b's surface is lower, snow slides from a to b
            if (d > rep) { float t = MathF.Min(a.H[ia], (d - rep) * 0.25f); a.H[ia] -= t; b.H[ib] += t; }
            else if (d < -rep) { float t = MathF.Min(b.H[ib], (-d - rep) * 0.25f); b.H[ib] -= t; a.H[ia] += t; }
        }
    }

    // ───────────────────────────── natural variation ─────────────────────────────

    static float Hash(int seed, int i)
    {
        uint h = unchecked((uint)(i * 374761393 + seed * 668265263));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777215f * 2f - 1f;
    }

    /// <summary>Smooth 1-D value noise in [-1, 1].</summary>
    static float Noise(int seed, float x)
    {
        int i = (int)MathF.Floor(x);
        float t = x - i;
        t = t * t * (3f - 2f * t);
        float a = Hash(seed, i), b = Hash(seed, i + 1);
        return a + (b - a) * t;
    }

    /// <summary>
    /// Recompute a surface's variation fields: big soft mounds (~170px), medium bumps (~65px) and
    /// small ripples (~24px). They drift slowly over time so the landscape keeps changing.
    /// </summary>
    void ComputeFields(Surface s)
    {
        if (s.Seed == 0) s.Seed = _rng.Next(1, int.MaxValue / 2);
        float t = _time;
        for (int i = 0; i < s.Cells; i++)
        {
            float u = i * Cell;
            float n1 = Noise(s.Seed, u / 170f + t * 0.004f);
            float n2 = Noise(s.Seed + 17, u / 65f + t * 0.011f);
            float n3 = Noise(s.Seed + 31, u / 24f + t * 0.02f);
            s.W[i] = Math.Clamp(1f + 0.6f * n1 + 0.12f * n2 + 0.04f * n3, 0.25f, 2f);
            s.Rp[i] = 0.7f + 0.3f * (n2 * 0.5f + 0.5f) + 0.35f * (n3 * 0.5f + 0.5f);
            s.Cp[i] = Math.Clamp(0.62f + 0.38f * (n1 * 0.5f + 0.5f) + 0.12f * n2, 0.45f, 1.1f);
        }
    }

    float WeightAt(Surface s, float x)
    {
        if (s.Cells == 0) return 1f;
        int i = Math.Clamp((int)((x - s.Left) / Cell), 0, s.Cells - 1);
        return s.W[i];
    }

    /// <summary>
    /// Wind drift: the wind skims the loose top layer off exposed snow and carries it downwind.
    /// The windward end gets scoured thin, the leeward end builds a heavier drift, and snow blown
    /// past the leeward edge falls off that side.
    /// </summary>
    void Drift(Surface s, float dt)
    {
        float w = Wind;
        float aw = MathF.Abs(w);
        if (aw < 3f) return;
        var h = s.H;
        int n = s.Cells, dir = w > 0 ? 1 : -1;
        float rate = MathF.Min(0.9f, DriftRate * (aw / 60f) * (aw / 60f + 0.35f)) * dt;
        int start = dir > 0 ? n - 1 : 0, end = dir > 0 ? -1 : n;
        for (int i = start; i != end; i -= dir) // downwind end first, so snow moves one cell per frame
        {
            if (h[i] < 0.05f) continue;
            int up = i - dir;
            // Exposed snow loses more than sheltered snow, but gradually, so it doesn't sort itself
            // into spikes.
            float exposure = up < 0 || up >= n ? 1.3f : Math.Clamp(1f + (h[i] - h[up]) * 0.15f, 0.45f, 1.3f);
            float m = MathF.Min(h[i], 3f) * rate * exposure * (0.6f + 0.4f * s.W[i]);
            h[i] -= m;
            // Blown snow hops: it comes down spread over the next few cells downwind.
            float lost = 0;
            for (int k = 1; k <= 3; k++)
            {
                float part = m * (k == 1 ? 0.5f : k == 2 ? 0.3f : 0.2f);
                int to = i + dir * k;
                if (to >= 0 && to < n) h[to] += part; else lost += part;
            }
            if (lost > 0 && !s.IsGround) OverEdge(s, left: dir < 0, lost * Cell * 0.6f);
        }
    }

    void Settle(Surface s, float dt)
    {
        var h = s.H;
        int n = s.Cells;
        if (n == 0) return;

        s.FieldTimer -= dt;
        if (s.FieldTimer <= 0) { ComputeFields(s); s.FieldTimer = 0.5f; }

        // Too deep: the excess avalanches outward toward the nearer edge (and eventually off it).
        // How deep it can get varies along the window, so a full window is rolling mounds, not a slab.
        if (s.IsGround)
        {
            for (int i = 0; i < n; i++)
            {
                float cap = MaxDepth * s.Cp[i];
                if (h[i] > cap) h[i] = cap;
            }
        }
        else
        {
            int mid = n / 2;
            for (int i = mid; i >= 0; i--)
            {
                float cap = MaxDepth * s.Cp[i];
                if (h[i] > cap)
                {
                    float ex = h[i] - cap; h[i] = cap;
                    if (i > 0) h[i - 1] += ex; else OverEdge(s, left: true, ex * Cell);
                }
            }
            for (int i = mid + 1; i < n; i++)
            {
                float cap = MaxDepth * s.Cp[i];
                if (h[i] > cap)
                {
                    float ex = h[i] - cap; h[i] = cap;
                    if (i < n - 1) h[i + 1] += ex; else OverEdge(s, left: false, ex * Cell);
                }
            }
        }

        Drift(s, dt);

        // Angle of repose (varying along the window): steep steps avalanche into their neighbours.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n - 1; i++) Relax(s, i);
            for (int i = n - 2; i >= 0; i--) Relax(s, i);
        }

        // Gentle settling so individual flakes blend in, without flattening the mounds.
        float diff = MathF.Min(0.45f, Settling * dt);
        for (int i = 0; i < n - 1; i++)
        {
            float t = (h[i] - h[i + 1]) * diff * 0.5f;
            h[i] -= t;
            h[i + 1] += t;
        }

        if (s.IsGround) return;

        // Shoulders: near an edge the pile can only be so tall; whatever is above that flows over
        // the edge (collected here, and dropped off in chunks by Drip). Each edge has its own
        // character, and the wind makes the leeward edge build a heavier drift and the windward
        // edge stay thin.
        float wf = Math.Clamp(Wind / 70f, -1f, 1f);
        float edgeL = EdgeLimit * s.EdgeVarL * (wf < 0 ? 1f - 0.7f * wf : 1f - 0.45f * wf);
        float edgeR = EdgeLimit * s.EdgeVarR * (wf > 0 ? 1f + 0.7f * wf : 1f + 0.45f * wf);
        float slopeL = Repose * 1.4f * (1.3f - 0.3f * s.EdgeVarR);
        float slopeR = Repose * 1.4f * (1.3f - 0.3f * s.EdgeVarL);
        // A joined end (window right next to us) has no edge to fall off: no shoulder there.
        if (s.LinkL != null) edgeL = float.PositiveInfinity;
        if (s.LinkR != null) edgeR = float.PositiveInfinity;
        for (int d = 0; d < n / 2; d++)
        {
            int l = d, r = n - 1 - d;
            float capL = (edgeL + d * slopeL * s.Rp[l]);
            float capR = (edgeR + d * slopeR * s.Rp[r]);
            if (capL >= MaxDepth && capR >= MaxDepth) break;
            if (h[l] > capL) { s.SpillL += (h[l] - capL) * Cell; h[l] = capL; }
            if (r != l && h[r] > capR) { s.SpillR += (h[r] - capR) * Cell; h[r] = capR; }
        }

        if (s.SlideVel != 0)
        {
            s.SlideOffset += s.SlideVel * dt;
            float f = SlideFriction * dt;
            s.SlideVel = MathF.Abs(s.SlideVel) <= f ? 0 : s.SlideVel - MathF.Sign(s.SlideVel) * f;
            int guard = 0;
            while (s.SlideOffset >= Cell && guard++ < n) { s.SlideOffset -= Cell; ShiftPile(s, +1); }
            while (s.SlideOffset <= -Cell && guard++ < n) { s.SlideOffset += Cell; ShiftPile(s, -1); }
            if (s.SlideVel == 0) s.SlideOffset = 0;
        }

        Drip(s, dt, left: true);
        Drip(s, dt, left: false);
    }

    void Relax(Surface s, int i)
    {
        var h = s.H;
        float rep = Repose * 0.5f * (s.Rp[i] + s.Rp[i + 1]);
        float d = h[i] - h[i + 1];
        if (d > rep) { float t = (d - rep) * 0.3f; h[i] -= t; h[i + 1] += t; }
        else if (d < -rep) { float t = (-d - rep) * 0.3f; h[i] += t; h[i + 1] -= t; }
    }

    /// <summary>Slide the whole pile one column; the column pushed past the edge falls off.</summary>
    void ShiftPile(Surface s, int dir)
    {
        var h = s.H;
        int n = s.Cells;
        int edge = dir > 0 ? n - 1 : 0;
        float vol = h[edge] * Cell;
        if (vol > 0.3f && s.Vis[edge] == 1)
        {
            float x = dir > 0 ? s.Right + 1f : s.Left - 1f;
            AddClump(x, s.PileTop(edge), vol, s.VX + s.SlideVel, MathF.Min(s.VY, 0f) * 0.5f + R(-20f, 10f), PKind.Clump);
        }
        if (dir > 0) { Array.Copy(h, 0, h, 1, n - 1); h[0] = 0; }
        else { Array.Copy(h, 1, h, 0, n - 1); h[n - 1] = 0; }
    }

    /// <summary>Snow that flowed over an edge drops off it in chunks of varying size.</summary>
    void Drip(Surface s, float dt, bool left)
    {
        int e = left ? 0 : s.Cells - 1;
        ref float timer = ref (left ? ref s.DripL : ref s.DripR);
        ref float spill = ref (left ? ref s.SpillL : ref s.SpillR);
        if ((left ? s.LinkL : s.LinkR) != null && spill > 0) { float v = spill; spill = 0; OverEdge(s, left, v); return; }
        if (s.Vis[e] == 0) { spill = 0; return; } // edge hidden behind a window: nothing to show
        if (spill < 0.6f) return;
        timer -= dt;
        if (timer > 0) return;
        timer = R(0.04f, 0.22f) * DripPace;

        // Drop it as fast as it arrives (bigger and more chunks when a lot is coming over), so all
        // of it ends up on whatever is below.
        float y = s.Clamped ? s.AnchorY + 2f : s.AnchorY - MathF.Min(s.H[e], EdgeLimit) * 0.6f;
        int chunks = 0;
        while (spill >= 0.6f && chunks++ < 6)
        {
            float vol = MathF.Min(spill, MathF.Max(R(2f, 12f), spill * R(0.15f, 0.35f)));
            spill -= vol;
            float x = left ? s.Left - R(1f, 3f) : s.Right + R(1f, 3f);
            AddClump(x, y - R(0f, 3f), vol, (left ? -1f : 1f) * R(8f, 45f) + s.VX * 0.3f, R(0f, 30f),
                     vol < 2.5f ? PKind.Powder : PKind.Clump);
        }
    }
}
