using System.Collections.Concurrent;

namespace Weather;

/// <summary>Mouse input while Ctrl+Alt are held (from <see cref="InputHook"/>), in screen pixels.</summary>
internal enum PlayKind : byte { Down, Move, Up, Right }
internal readonly record struct PlayCommand(PlayKind Kind, int X, int Y, double T);

/// <summary>Things the tray menu can ask for.</summary>
internal enum SeasonAction : byte { Plow, Shower, Critters, Gust }

internal sealed class Snowman
{
    public IntPtr Surf;
    public bool OnGround, Falling, Melting, Hidden;
    public float RelX, X, Y, VX, VY, Rot, RotSpeed, Grow, Melt = 1f, Wobble;
    public int Height;
    public Sprite Img = null!;
}

internal sealed class Critter
{
    public bool Firefly;
    public int Variant, State;          // State: 0 flying, 1 resting, 2 leaving
    public float X, Y, VX, VY, TX, TY, Phase, Timer, Age, Life, Glow, GlowTimer;
    public bool Lands, OnGround;
    public IntPtr Surf;
    public float RelX;
}

internal sealed class Vehicle
{
    public bool Blower;
    public int Dir;
    public float X, Speed, Frame, Puff, W, H, Scale;
}

internal sealed class Splat
{
    public IntPtr Hwnd;                 // Zero: on the desktop (screen coordinates)
    public float RelX, RelY, R, Age;
    public int Seed;
}

internal sealed class DustDevil
{
    public float X, Age, Life;
    public float Strength => MathF.Sin(MathF.PI * Math.Clamp(Age / Life, 0f, 1f));
}

/// <summary>
/// The little extras: snowballs, snowmen, the snowplough and leaf blower, butterflies, fireflies,
/// dust devils, and playing with it all using Ctrl+Alt and the mouse.
/// </summary>
internal sealed partial class SnowSim
{
    readonly ConcurrentQueue<PlayCommand> _commands = new();
    readonly ConcurrentQueue<SeasonAction> _actions = new();

    public void Enqueue(PlayCommand c) => _commands.Enqueue(c);
    public void Request(SeasonAction a) => _actions.Enqueue(a);

    public readonly List<Snowman> Snowmen = new();
    public readonly List<Critter> Critters = new();
    public readonly List<Splat> Splats = new();
    public Vehicle? Vehicle;
    public DustDevil? Devil;

    /// <summary>Where the mouse is (set by the engine every frame).</summary>
    public float CursorX = -9999, CursorY = -9999;
    /// <summary>Display scale (taskbar DPI / 96), for sprites that should match the UI's size.</summary>
    public float UiScale = 1f;
    public bool MeadowOn { get; private set; } = true;

    // A snowball being held (Ctrl+Alt+drag in winter).
    public bool Holding;
    public float HeldVol, HeldX, HeldY;
    /// <summary>Snowballs are drawn a bit bigger on high-DPI screens.</summary>
    float BallScale => MathF.Sqrt(MathF.Max(1f, UiScale));
    public float HeldRadius => Math.Clamp(MathF.Sqrt(HeldVol / MathF.PI), 3.5f, 17f) * BallScale;

    bool _playOn = true, _autoPlowOn = true, _butterfliesOn = true, _firefliesOn = true, _devilsOn = true;
    bool _dragging;
    float _lastX, _lastY, _pathAcc;
    double _lastT;
    readonly (double T, float X, float Y)[] _trail = new (double, float, float)[12];
    int _trailN;

    float _critterTimer = 12f, _devilTimer = 150f, _plowCheck = 10f, _plowCooldown;
    float _prevCX, _prevCY;
    public float CursorStill { get; private set; }

    void ClearPlay()
    {
        Snowmen.Clear();
        Splats.Clear();
        Holding = false;
        Vehicle = null;
        Devil = null;
    }

    void UpdatePlay(float dt)
    {
        while (_commands.TryDequeue(out var c))
            if (_playOn) Handle(c);
        while (_actions.TryDequeue(out var a)) Handle(a);

        float moved = MathF.Abs(CursorX - _prevCX) + MathF.Abs(CursorY - _prevCY);
        CursorStill = moved < 3f ? CursorStill + dt : 0f;
        _prevCX = CursorX; _prevCY = CursorY;

        UpdateSnowmen(dt);
        UpdateVehicle(dt);
        UpdateCritters(dt);
        UpdateDevil(dt);
        UpdateSplats(dt);
        AutoEvents(dt);
    }

    // ───────────────────────────── input ─────────────────────────────

    void Handle(PlayCommand c)
    {
        float x = c.X, y = c.Y;
        switch (c.Kind)
        {
            case PlayKind.Down:
                _dragging = true;
                _trailN = 0;
                Trail(c);
                _lastX = x; _lastY = y; _lastT = c.T; _pathAcc = 0;
                if (Season == Season.Winter) StartBall(x, y);
                else if (Season == Season.Summer) SeedPuff(x, y, 0, 0, 5);
                break;

            case PlayKind.Move:
            {
                if (!_dragging) break;
                Trail(c);
                float dx = x - _lastX, dy = y - _lastY;
                float ddt = (float)Math.Max(c.T - _lastT, 0.004);
                float vx = Math.Clamp(dx / ddt, -3000f, 3000f), vy = Math.Clamp(dy / ddt, -3000f, 3000f);
                _pathAcc += MathF.Sqrt(dx * dx + dy * dy);
                switch (Season)
                {
                    case Season.Winter:
                        if (Holding) { HeldX = x; HeldY = y; if (HeldVol < 900f) HeldVol += Scoop(x, y, MathF.Min(30f, 900f - HeldVol), 6); }
                        break;
                    case Season.Summer:
                        if (_pathAcc > 22f) { _pathAcc = 0; SeedPuff(x, y, vx, vy, 1); }
                        break;
                    default:
                        Brush(x, y, vx, vy, MathF.Sqrt(dx * dx + dy * dy));
                        break;
                }
                _lastX = x; _lastY = y; _lastT = c.T;
                break;
            }

            case PlayKind.Up:
                if (!_dragging) break;
                _dragging = false;
                Trail(c);
                if (Holding) ReleaseBall(x, y);
                break;

            case PlayKind.Right:
                switch (Season)
                {
                    case Season.Winter: SnowmanAt(x, y); break;
                    case Season.Spring: ToggleShower(); break;
                    case Season.Summer:
                        if (Night) for (int k = 0; k < 8; k++) AddFirefly(x + R(-20f, 20f), y + R(-20f, 20f), 60f);
                        else AddButterfly(x, y, fromEdge: false);
                        break;
                    default: StartDevil(x); break;
                }
                break;
        }
    }

    void Handle(SeasonAction a)
    {
        switch (a)
        {
            case SeasonAction.Plow:
                if (Season is Season.Winter or Season.Autumn) StartVehicle(Season == Season.Autumn);
                break;
            case SeasonAction.Shower:
                if (Season == Season.Spring) ToggleShower();
                break;
            case SeasonAction.Critters:
                if (Season == Season.Summer && Night)
                    for (int k = 0; k < 14; k++) AddFirefly(R(0.1f, 0.9f) * ScreenW, Ground.AnchorY - R(10f, 200f), 90f);
                else if (Season is Season.Spring or Season.Summer)
                    for (int k = 0; k < 3; k++) AddButterfly(0, 0, fromEdge: true);
                break;
            case SeasonAction.Gust:
                if (Season == Season.Autumn) StartDevil(R(0.2f, 0.8f) * ScreenW);
                break;
        }
    }

    void Trail(in PlayCommand c)
    {
        if (_trailN == _trail.Length) { Array.Copy(_trail, 1, _trail, 0, _trail.Length - 1); _trailN--; }
        _trail[_trailN++] = (c.T, c.X, c.Y);
    }

    /// <summary>The mouse's velocity over the last ~80ms of the drag (the throw).</summary>
    (float vx, float vy) TrailVelocity()
    {
        if (_trailN < 2) return (0, 0);
        var last = _trail[_trailN - 1];
        int i = _trailN - 2;
        while (i > 0 && last.T - _trail[i].T < 0.08) i--;
        var first = _trail[i];
        double span = last.T - first.T;
        if (span < 0.008 || last.T - _trail[_trailN - 2].T > 0.12) return (0, 0); // held still before letting go
        return ((float)((last.X - first.X) / span), (float)((last.Y - first.Y) / span));
    }

    /// <summary>The pile (window top or taskbar) the point is on or just above, and its column there.</summary>
    Surface? PileNear(float x, float y, out int col)
    {
        foreach (var s in Z)
        {
            if (x < s.Left || x >= s.Right || s.Clamped || s.Cells == 0) continue;
            int i = s.Col(x);
            if (s.Vis[i] == 0) continue;
            if (y >= s.PileTop(i) - 30f && y <= s.AnchorY + 14f) { col = i; return s; }
        }
        if (GroundOn && Ground.Cells > 0 && x >= 0 && x < ScreenW)
        {
            int i = Ground.Col(x);
            if (y >= Ground.PileTop(i) - 36f && y <= Ground.AnchorY + 40f) { col = i; return Ground; }
        }
        col = 0;
        return null;
    }

    /// <summary>Take up to <paramref name="max"/> px² from the pile near the point (more from the middle).</summary>
    float Scoop(float x, float y, float max, int halfWidth)
    {
        var s = PileNear(x, y, out int ci);
        if (s == null || max <= 0) return 0;
        float wsum = 0;
        for (int k = -halfWidth; k <= halfWidth; k++) wsum += halfWidth + 1 - Math.Abs(k);
        float got = 0;
        for (int k = -halfWidth; k <= halfWidth; k++)
        {
            int i = ci + k;
            if ((uint)i >= (uint)s.Cells || s.Vis[i] == 0) continue;
            float want = max * (halfWidth + 1 - Math.Abs(k)) / wsum / Cell;
            float a = MathF.Min(s.H[i] * 0.9f, want);
            s.H[i] -= a;
            got += a * Cell;
        }
        return got;
    }

    /// <summary>A puff of loose snow (just for show).</summary>
    void Puff(float x, float y, int n, float speed)
    {
        for (int k = 0; k < n && Count < MaxParticles; k++)
        {
            ref var p = ref NewParticle(PKind.Splash);
            float a = R(0f, 6.283f), v = R(0.3f, 1f) * speed;
            p.X = x; p.Y = y;
            p.VX = MathF.Cos(a) * v;
            p.VY = MathF.Sin(a) * v - speed * 0.5f;
            p.Age = R(0.3f, 0.7f);
            p.R = R(0.8f, 1.8f);
            p.Alpha = 0.85f;
        }
    }

    // ───────────────────────────── winter: snowballs ─────────────────────────────

    void StartBall(float x, float y)
    {
        float got = Scoop(x, y, 160f, 10);
        HeldVol = MathF.Max(got, 90f); // fresh flakes pack into a small one even off the pile
        Holding = true;
        HeldX = x; HeldY = y;
        Puff(x, y, 5, 60f);
    }

    void ReleaseBall(float x, float y)
    {
        Holding = false;
        var (vx, vy) = TrailVelocity();
        float sp = MathF.Sqrt(vx * vx + vy * vy);
        if (sp < 160f)
        {
            // Thrown straight at the screen: it splats on the glass of whatever window is there.
            SplatAt(x, y, HeldVol);
            return;
        }
        if (sp > 2600f) { vx *= 2600f / sp; vy *= 2600f / sp; }
        if (Count >= MaxParticles) return;
        ref var p = ref NewParticle(PKind.Ball);
        p.X = x; p.Y = y; p.VX = vx; p.VY = vy;
        p.Vol = HeldVol;
        p.R = HeldRadius;
        p.Alpha = 1f;
        p.NoLand = 0.1f;
    }

    void SplatAt(float x, float y, float vol)
    {
        var sp = new Splat { R = Math.Clamp(MathF.Sqrt(vol / MathF.PI) * 1.8f, 10f, 34f) * BallScale, Seed = _rng.Next() };
        Surface? on = null;
        foreach (var s in Z)
            if (x >= s.Left && x < s.Right && y >= s.Top && y < s.Bottom) { on = s; break; }
        if (on != null) { sp.Hwnd = on.Hwnd; sp.RelX = x - on.Left; sp.RelY = y - on.Top; }
        else { sp.RelX = x; sp.RelY = y; }
        Splats.Add(sp);
        if (Splats.Count > 12) Splats.RemoveAt(0);
        Puff(x, y, 14, 220f);
    }

    void UpdateSplats(float dt)
    {
        for (int i = Splats.Count - 1; i >= 0; i--)
        {
            var sp = Splats[i];
            sp.Age += dt;
            sp.RelY += 9f * MathF.Exp(-sp.Age * 0.6f) * dt; // slides down the glass a little, slowing
            if (sp.Age > 6.5f || sp.Hwnd != IntPtr.Zero && !_windows.ContainsKey(sp.Hwnd)) Splats.RemoveAt(i);
        }
    }

    /// <summary>Where a splat is on screen right now (it rides along with its window).</summary>
    public bool SplatPos(Splat sp, out float x, out float y)
    {
        x = sp.RelX; y = sp.RelY;
        if (sp.Hwnd == IntPtr.Zero) return true;
        if (!_windows.TryGetValue(sp.Hwnd, out var s)) return false;
        x += s.Left; y += s.Top;
        return true;
    }

    /// <summary>A thrown snowball lands on a pile: most of it stays there, the rest bursts into powder.</summary>
    void BallSplat(Surface s, ref Particle p)
    {
        Deposit(s, p.X, p.Vol * 0.8f);
        float y = s.PileTopAt(p.X);
        int n = Math.Clamp((int)(p.Vol / 25f), 6, 20);
        for (int k = 0; k < n && Count < MaxParticles; k++)
            AddClump(p.X, y - 2f, k < n / 3 ? p.Vol * 0.2f / (n / 3) : 0f,
                     p.VX * R(0.05f, 0.25f) + R(-160f, 160f), -R(80f, 320f), k % 2 == 0 ? PKind.Powder : PKind.Clump);
    }

    bool BallHitsSnowman(ref Particle p)
    {
        foreach (var sm in Snowmen)
        {
            if (sm.Falling || sm.Melting || sm.Hidden) continue;
            float half = sm.Height * 0.36f;
            if (p.X > sm.X - half - p.R && p.X < sm.X + half + p.R && p.Y > sm.Y - sm.Height - p.R && p.Y < sm.Y + p.R)
            {
                Topple(sm, p.VX * 0.35f, MathF.Min(p.VY * 0.2f, 0f) - 120f);
                Puff(p.X, p.Y, 16, 240f);
                return true;
            }
        }
        return false;
    }

    // ───────────────────────────── winter: snowmen ─────────────────────────────

    void SnowmanAt(float x, float y)
    {
        // Right-clicking a snowman knocks it over.
        foreach (var sm in Snowmen)
        {
            if (sm.Falling || sm.Melting) continue;
            float half = sm.Height * 0.4f;
            if (x > sm.X - half && x < sm.X + half && y > sm.Y - sm.Height - 6 && y < sm.Y + 6)
            {
                Topple(sm, x < sm.X ? 220f : -220f, -160f);
                return;
            }
        }
        var s = PileNear(x, y, out int ci);
        if (s == null) { Puff(x, y, 6, 80f); return; }
        float px = s.Left + (ci + 0.5f) * Cell;
        float vol = Scoop(px, s.PileTop(ci), 1500f, 24);
        if (vol < 90f)
        {
            // Not enough snow here yet: put it back.
            if (vol > 0) Deposit(s, px, vol);
            Puff(px, s.PileTop(ci) - 2f, 8, 90f);
            return;
        }
        int height = (int)(Math.Clamp(16f + MathF.Sqrt(vol) * 1.25f, 22f, 62f) * UiScale);
        var man = new Snowman
        {
            Surf = s.IsGround ? IntPtr.Zero : s.Hwnd,
            OnGround = s.IsGround,
            RelX = px - s.Left,
            X = px,
            Y = s.PileTop(ci),
            Height = height,
            Img = Sprites.Snowman(height, _rng.Next(15)),
        };
        Snowmen.Add(man);
        if (Snowmen.Count > 8) Topple(Snowmen[0], R(-100f, 100f), -100f);
        Puff(px, man.Y - 4f, 12, 120f);
    }

    void Topple(Snowman sm, float vx, float vy)
    {
        if (sm.Falling) return;
        sm.Falling = true;
        sm.VX = vx;
        sm.VY = vy;
        sm.RotSpeed = (vx >= 0 ? 1f : -1f) * R(2.5f, 5f);
    }

    /// <summary>A snowman hits something: it bursts back into clumps of snow.</summary>
    void BreakSnowman(Snowman sm)
    {
        float vol = sm.Height * sm.Height * 0.45f;
        int n = Math.Clamp(sm.Height / 3, 8, 22);
        float cy = sm.Y - sm.Height * 0.4f;
        for (int k = 0; k < n && Count < MaxParticles; k++)
            AddClump(sm.X + R(-sm.Height * 0.3f, sm.Height * 0.3f), cy + R(-sm.Height * 0.3f, sm.Height * 0.3f),
                     vol / n, sm.VX * 0.3f + R(-150f, 150f), -R(60f, 260f), PKind.Clump);
        Puff(sm.X, cy, 14, 200f);
    }

    void UpdateSnowmen(float dt)
    {
        for (int i = Snowmen.Count - 1; i >= 0; i--)
        {
            var sm = Snowmen[i];
            sm.Grow = MathF.Min(1f, sm.Grow + dt * 2.2f);
            sm.Wobble *= MathF.Exp(-dt * 4f);
            if (sm.Melting)
            {
                sm.Melt -= dt * 0.45f;
                if (sm.Melt <= 0) { Snowmen.RemoveAt(i); continue; }
            }
            if (!sm.Falling)
            {
                Surface? s = sm.OnGround ? (GroundOn ? Ground : null) : _windows.TryGetValue(sm.Surf, out var ws) ? ws : null;
                if (s == null || s.Cells == 0 || s.Clamped) { Topple(sm, 0, 0); continue; }
                // A hard jerk of its window throws it off.
                if (MathF.Abs(s.AY) > FlingAccel * 0.7f && s.VY < -FlingMinSpeed * 0.5f || MathF.Abs(s.AX) > ShakeAccel * 1.2f)
                {
                    Topple(sm, s.VX * 0.8f, MathF.Min(s.VY, 0f) * 0.8f - 60f);
                    continue;
                }
                if (MathF.Abs(s.AX) > ShakeAccel * 0.3f) sm.Wobble = MathF.Min(0.35f, sm.Wobble + MathF.Abs(s.AX) / ShakeAccel * 0.05f);
                sm.X = s.Left + sm.RelX;
                if (sm.RelX < 0 || sm.RelX >= s.Right - s.Left) { Topple(sm, s.VX, 0); continue; }
                int ci = s.Col(sm.X);
                sm.Hidden = s.Vis[ci] == 0;
                sm.Y = s.PileTop(ci) + 2f; // its base sits a little into the snow
            }
            else
            {
                float y0 = sm.Y;
                sm.VY += Gravity * dt;
                sm.X += sm.VX * dt;
                sm.Y += sm.VY * dt;
                sm.Rot += sm.RotSpeed * dt;
                if (sm.VY > 0 && TryLand(sm.X, y0, sm.Y, false, out _) || sm.Y > ScreenH + 60)
                {
                    if (!sm.Melting) BreakSnowman(sm);
                    Snowmen.RemoveAt(i);
                }
            }
        }
    }

    // ───────────────────────────── snowplough and leaf blower ─────────────────────────────

    public void StartVehicle(bool blower)
    {
        if (Vehicle != null || !GroundOn || Ground.Cells == 0) return;
        var img = Sprites.Vehicle(blower, 0, UiScale);
        int dir = R() < 0.5f ? 1 : -1;
        Vehicle = new Vehicle
        {
            Blower = blower,
            Dir = dir,
            W = img.W, H = img.H, Scale = UiScale,
            X = dir > 0 ? -img.W : ScreenW + img.W,
            Speed = 150f * UiScale,
        };
    }

    void UpdateVehicle(float dt)
    {
        var v = Vehicle;
        if (v == null) return;
        if (!GroundOn || Ground.Cells == 0) { Vehicle = null; return; }
        v.X += v.Dir * v.Speed * dt;
        v.Frame += dt;
        var g = Ground;
        float sc = v.Scale;
        float front = v.X + v.Dir * v.W * 0.5f, back = v.X - v.Dir * v.W * 0.4f;

        if (!v.Blower)
        {
            // Everything under the plough is scraped up and carried along in front of the blade.
            int a = g.Col(MathF.Min(front, back)), b = g.Col(MathF.Max(front, back));
            float load = 0;
            if (MathF.Max(front, back) > 0 && MathF.Min(front, back) < ScreenW)
                for (int i = a; i <= b; i++) { load += g.H[i]; g.H[i] = 0; }
            int start = g.Col(front) + v.Dir;
            float frontEdge = front + v.Dir * Cell;
            if (frontEdge >= 0 && frontEdge < ScreenW)
            {
                // Heap it into a berm just ahead of the blade (pushed off the screen at the end).
                for (int k = 0; k < 6; k++)
                {
                    int i = start + v.Dir * k;
                    if ((uint)i >= (uint)g.Cells) break;
                    g.H[i] = MathF.Min(MaxPile, g.H[i] + load * (6 - k) / 21f);
                }
                // A bit of spray off the top of the berm.
                if (load > 1f && R() < 0.6f)
                    AddClump(front + v.Dir * 6f, g.AnchorY - MathF.Min(g.H[Math.Clamp(start, 0, g.Cells - 1)], 30f) - 2f, 0f,
                             v.Dir * R(60f, 200f), -R(60f, 200f), PKind.Powder);
            }
        }
        else
        {
            // The blower lifts the leaves ahead of its nozzle and sends them flying.
            float n0 = front, n1 = front + v.Dir * 80f * sc;
            int a = g.Col(MathF.Min(n0, n1)), b = g.Col(MathF.Max(n0, n1));
            if (MathF.Max(n0, n1) > 0 && MathF.Min(n0, n1) < ScreenW)
            {
                float k = MathF.Min(1f, dt * 7f);
                float acc = 0;
                for (int i = a; i <= b; i++)
                {
                    float take = g.H[i] * k;
                    g.H[i] -= take;
                    acc += take * Cell;
                    if (acc > 14f && Count < MaxParticles)
                    {
                        AddClump(g.Left + (i + 0.5f) * Cell, g.AnchorY - 3f, acc, v.Dir * R(250f, 600f), -R(80f, 340f), PKind.Clump);
                        acc = 0;
                    }
                }
            }
            for (int i = 0; i < Count; i++)
            {
                ref var p = ref Particles[i];
                if (p.Kind != PKind.Leaf) continue;
                float dx = (p.X - front) * v.Dir;
                if (dx < -10f || dx > 140f * sc || p.Y < g.AnchorY - 90f * sc) continue;
                if (p.Life > 0) { if (p.Surf != IntPtr.Zero) continue; p.Life = 0; p.FromPile = true; p.NoLand = 0.3f; }
                p.VX += v.Dir * 1800f * dt;
                p.VY -= 500f * dt;
            }
        }

        // Anything in the way gets bowled over.
        foreach (var sm in Snowmen)
            if (sm.OnGround && !sm.Falling && MathF.Abs(sm.X - front) < 10f * sc) Topple(sm, v.Dir * R(250f, 400f), -R(200f, 320f));
        foreach (var c in Critters)
            if (c.State == 1 && c.OnGround && MathF.Abs(c.X - v.X) < v.W) TakeOff(c);

        // Exhaust puffs from the pipe.
        v.Puff -= dt;
        if (v.Puff <= 0 && Count < MaxParticles)
        {
            v.Puff = 0.13f;
            float pipeX = v.X + v.Dir * (34f * sc - v.W * 0.5f);
            ref var p = ref NewParticle(PKind.Smoke);
            p.X = pipeX; p.Y = g.AnchorY - v.H + 4f * sc;
            p.VX = -v.Dir * R(10f, 30f); p.VY = -R(40f, 70f);
            p.R = 2.5f * sc;
            p.Age = p.Fade = R(1.1f, 1.6f);
            p.Alpha = 0.32f;
        }

        if (v.Dir > 0 ? v.X - v.W > ScreenW : v.X + v.W < 0) Vehicle = null;
    }

    // ───────────────────────────── butterflies and fireflies ─────────────────────────────

    void AddButterfly(float x, float y, bool fromEdge)
    {
        if (Critters.Count(c => !c.Firefly) >= 6) return;
        var b = new Critter { Variant = _rng.Next(Sprites.ButterflyKinds), Phase = R(0f, 6.283f), Life = R(70f, 140f) };
        if (fromEdge)
        {
            bool left = R() < 0.5f;
            b.X = left ? -20f : ScreenW + 20f;
            b.Y = R(0.15f, 0.65f) * ScreenH;
            b.VX = left ? 60f : -60f;
        }
        else { b.X = x; b.Y = y; b.VY = -120f; }
        PickTarget(b);
        Critters.Add(b);
    }

    void AddFirefly(float x, float y, float life)
    {
        if (Critters.Count(c => c.Firefly) >= 40) return;
        Critters.Add(new Critter
        {
            Firefly = true, X = x, Y = y, Phase = R(0f, 100f), Life = life, GlowTimer = R(0f, 2f),
            TX = x, TY = y, VX = R(-10f, 10f), VY = -R(5f, 20f),
        });
    }

    /// <summary>Somewhere to fly to: often a window's top edge or a flower to rest on, otherwise just a point.</summary>
    void PickTarget(Critter b)
    {
        b.Lands = false;
        if (R() < 0.65f)
        {
            bool meadow = Season is Season.Spring or Season.Summer && MeadowOn && GroundOn;
            int choices = Z.Count + (meadow ? 2 : 0);
            if (choices > 0)
            {
                int pick = _rng.Next(choices);
                Surface? s = pick < Z.Count ? Z[pick] : Ground;
                if (!s.Clamped && s.Cells > 4 && s.Right > 0 && s.Left < ScreenW && s.AnchorY > 40)
                {
                    int i = _rng.Next(2, s.Cells - 2);
                    float x = s.Left + (i + 0.5f) * Cell;
                    if (x > 10 && x < ScreenW - 10 && s.Vis[i] != 0 && s.Shield[i] == 0)
                    {
                        b.Lands = true;
                        b.OnGround = s.IsGround;
                        b.Surf = s.IsGround ? IntPtr.Zero : s.Hwnd;
                        b.RelX = x - s.Left;
                        b.TX = x;
                        b.TY = s.PileTop(i) - (s.IsGround ? 14f * UiScale : 5f);
                        return;
                    }
                }
            }
        }
        b.TX = R(0.05f, 0.95f) * ScreenW;
        b.TY = R(0.1f, 0.75f) * ScreenH;
    }

    void TakeOff(Critter b)
    {
        b.State = 0;
        b.VY = -140f;
        b.VX = R(-60f, 60f);
        b.Timer = 0;
        PickTarget(b);
        b.Lands = false; // wander a while before settling again
    }

    void UpdateCritters(float dt)
    {
        for (int i = Critters.Count - 1; i >= 0; i--)
        {
            var c = Critters[i];
            c.Age += dt;
            if (c.Firefly) { if (!UpdateFirefly(c, dt)) Critters.RemoveAt(i); continue; }

            float cdx = c.X - CursorX, cdy = c.Y - CursorY;
            float cd2 = cdx * cdx + cdy * cdy;
            if (c.Age > c.Life && c.State != 2) c.State = 2;
            if (c.State == 2 && c.TX > -59f && c.TX < ScreenW + 59f) { c.TX = c.X < ScreenW / 2 ? -80f : ScreenW + 80f; c.TY = c.Y - 200f; }

            if (c.State == 1)
            {
                // Resting: wings slowly open and close; off it goes if anything disturbs it.
                Surface? s = c.OnGround ? Ground : _windows.TryGetValue(c.Surf, out var ws) ? ws : null;
                c.Phase += dt * 1.6f;
                c.Timer -= dt;
                if (s == null || s.Cells == 0 || cd2 < 90f * 90f || MathF.Abs(s.VX) + MathF.Abs(s.VY) > 40f || c.Timer <= 0)
                { TakeOff(c); continue; }
                c.X = s.Left + c.RelX;
                int col = s.Col(c.X);
                if (s.Vis[col] == 0) { TakeOff(c); continue; }
                c.Y = s.PileTop(col) - (c.OnGround ? 14f * UiScale : 5f);
                continue;
            }

            // Flying: a fluttery, bobbing path toward the target, veering away from the cursor.
            c.Phase += dt * 26f;
            float dx = c.TX - c.X, dy = c.TY - c.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy) + 0.01f;
            float speed = c.State == 2 ? 140f : 85f;
            float wantX = dx / d * speed + MathF.Sin(c.Age * 1.7f + c.Variant) * 45f;
            float wantY = dy / d * speed + MathF.Sin(c.Age * 6.5f) * 70f;
            if (d < 40f && c.Lands) { wantX = dx * 2.5f; wantY = dy * 2.5f; }
            if (cd2 < 120f * 120f && c.State != 2)
            {
                float cd = MathF.Sqrt(cd2) + 0.01f;
                wantX += cdx / cd * 320f;
                wantY += cdy / cd * 320f - 80f;
            }
            c.VX += (wantX - c.VX) * MathF.Min(1f, dt * 3f);
            c.VY += (wantY - c.VY) * MathF.Min(1f, dt * 3f);
            c.X += c.VX * dt;
            c.Y += c.VY * dt;

            if (c.State == 2)
            {
                if (c.X < -80 || c.X > ScreenW + 80 || c.Y < -80) Critters.RemoveAt(i);
                continue;
            }
            if (d < 5f)
            {
                if (c.Lands && cd2 > 120f * 120f) { c.State = 1; c.Timer = R(5f, 16f); c.VX = c.VY = 0; }
                else PickTarget(c);
            }
            else if (c.Lands)
            {
                // Keep aiming at the landing spot as its window moves.
                Surface? s = c.OnGround ? Ground : _windows.TryGetValue(c.Surf, out var ws) ? ws : null;
                if (s == null || s.Cells == 0) PickTarget(c);
                else
                {
                    c.TX = s.Left + c.RelX;
                    int col = s.Col(c.TX);
                    if (s.Vis[col] == 0 || s.Shield[col] != 0) PickTarget(c);
                    else c.TY = s.PileTop(col) - (c.OnGround ? 14f * UiScale : 5f);
                }
            }
        }
    }

    /// <summary>A firefly drifts low and slowly, blinking; they gather around a mouse that's held still.</summary>
    bool UpdateFirefly(Critter f, float dt)
    {
        // Short-lived ones (released by hand) go when their time is up; the evening's own fireflies at dawn.
        bool night = Season == Season.Summer && Night;
        if (f.State != 2 && (f.Age > f.Life || !night && f.Life >= 200f)) f.State = 2;
        if (f.State == 2 && f.Age < f.Life) f.Life = f.Age; // fade out from now

        // Blink: a slow swell of light, then dark for a few seconds.
        f.GlowTimer -= dt;
        if (f.GlowTimer <= -1.3f) f.GlowTimer = R(0.6f, 2.6f);
        f.Glow = f.GlowTimer < 0 ? MathF.Sin(-f.GlowTimer / 1.3f * MathF.PI) : 0f;
        if (f.State == 2)
        {
            f.Glow *= MathF.Max(0f, 1f - (f.Age - f.Life) / 4f);
            if (f.Age - f.Life > 4f) return false;
        }

        f.Timer -= dt;
        bool gather = CursorStill > 1.2f && CursorX > -1000;
        if (gather)
        {
            float a = f.Phase + f.Age * 0.4f;
            float r = 40f + (f.Phase % 7f) * 14f;
            f.TX = CursorX + MathF.Cos(a) * r;
            f.TY = CursorY + MathF.Sin(a) * r * 0.6f;
        }
        else if (f.Timer <= 0)
        {
            f.Timer = R(2f, 6f);
            f.TX = Math.Clamp(f.X + R(-160f, 160f), 0f, ScreenW);
            f.TY = Math.Clamp(f.Y + R(-90f, 90f), ScreenH * 0.35f, Ground.AnchorY - 6f);
        }
        float dx = f.TX - f.X, dy = f.TY - f.Y;
        float k = gather ? 0.9f : 0.35f;
        f.VX += (dx * k - f.VX) * MathF.Min(1f, dt * 1.2f) + MathF.Sin(f.Age * 2.1f + f.Phase) * 6f * dt;
        f.VY += (dy * k - f.VY) * MathF.Min(1f, dt * 1.2f) + MathF.Cos(f.Age * 1.7f + f.Phase) * 6f * dt;
        float sp = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY);
        float max = gather ? 60f : 25f;
        if (sp > max) { f.VX *= max / sp; f.VY *= max / sp; }
        // Scatter when the mouse dashes through them.
        float cdx = f.X - CursorX, cdy = f.Y - CursorY;
        if (!gather && cdx * cdx + cdy * cdy < 70f * 70f) { f.VX += cdx * 3f * dt; f.VY += cdy * 3f * dt; }
        f.X += f.VX * dt;
        f.Y += f.VY * dt;
        return true;
    }

    // ───────────────────────────── spring, summer and autumn play ─────────────────────────────

    /// <summary>Dragging through petals or leaves: airborne ones are swept along, resting ones kicked up.</summary>
    void Brush(float x, float y, float vx, float vy, float dist)
    {
        if (dist < 0.5f) return;
        const float Reach = 60f;
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref Particles[i];
            if (p.Kind is not (PKind.Leaf or PKind.Petal or PKind.Seed)) continue;
            float dx = p.X - x, dy = p.Y - y;
            float d2 = dx * dx + dy * dy;
            if (d2 > Reach * Reach) continue;
            float f = 1f - MathF.Sqrt(d2) / Reach;
            if (p.Life > 0) { p.Life = 0; p.FromPile = true; p.NoLand = 0.35f; p.VX = 0; p.VY = 0; }
            p.VX += vx * 0.45f * f;
            p.VY += vy * 0.45f * f - 140f * f;
        }
        // Kick up the pile right under the mouse.
        var s = PileNear(x, y, out int ci);
        if (s == null || y < s.PileTop(ci) - 14f) return;
        float vol = Scoop(x, y, MathF.Min(70f, dist * 3f), 5);
        int n = Math.Clamp((int)(vol / 18f), vol > 2 ? 1 : 0, 6);
        for (int k = 0; k < n && Count < MaxParticles; k++)
            AddLitter(x + R(-6f, 6f), s.PileTop(ci) - 2f, vol / n,
                      vx * R(0.25f, 0.6f) + R(-60f, 60f), MathF.Min(vy * 0.4f, 0f) - R(120f, 300f), false);
    }

    /// <summary>Blowing a dandelion clock: seeds float off from the mouse.</summary>
    void SeedPuff(float x, float y, float vx, float vy, int n)
    {
        for (int k = 0; k < n; k++)
            AddSeed(x + R(-4f, 4f), y + R(-4f, 4f), vx * 0.25f + R(-50f, 50f), vy * 0.25f - R(15f, 60f), R(0.7f, 1f));
    }

    public void StartDevil(float x)
    {
        if (Devil != null) return;
        Devil = new DustDevil { X = x, Life = R(5.5f, 8.5f) };
    }

    /// <summary>
    /// A small whirlwind wanders along the taskbar, lifting leaves off it and spinning them up in a
    /// column before letting them drift down again.
    /// </summary>
    void UpdateDevil(float dt)
    {
        var dv = Devil;
        if (dv == null) return;
        dv.Age += dt;
        if (dv.Age > dv.Life) { Devil = null; return; }
        dv.X += (Wind * 0.6f + MathF.Sin(dv.Age * 0.8f) * 25f) * dt;
        float str = dv.Strength;
        var g = Ground;
        float baseY = g.AnchorY;

        if (Material == Material.Leaves && GroundOn && g.Cells > 0)
        {
            int a = g.Col(dv.X - 16f), b = g.Col(dv.X + 16f);
            float k = MathF.Min(1f, dt * 2.5f * str);
            float acc = 0;
            for (int i = a; i <= b; i++)
            {
                float take = g.H[i] * k;
                g.H[i] -= take;
                acc += take * Cell;
                if (acc > 16f && Count < MaxParticles)
                {
                    AddClump(g.Left + (i + 0.5f) * Cell, baseY - 3f, acc, R(-60f, 60f), -R(200f, 420f) * str, PKind.Clump);
                    acc = 0;
                }
            }
        }
        const float Radius = 110f;
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref Particles[i];
            if (p.Kind is not (PKind.Leaf or PKind.Petal or PKind.Seed)) continue;
            float dx = p.X - dv.X;
            if (MathF.Abs(dx) > Radius || p.Y < baseY - 480f || p.Y > baseY + 4f) continue;
            if (p.Life > 0)
            {
                if (p.Surf != IntPtr.Zero || MathF.Abs(dx) > 40f) continue;
                p.Life = 0; p.FromPile = true; p.NoLand = 0.5f; p.VY = -R(150f, 300f) * str;
            }
            float fall = 1f - MathF.Abs(dx) / Radius;
            float swirl = MathF.Sin(_time * 3.2f + p.Phase) * 280f * str;
            p.VX += (swirl - dx * 1.6f - p.VX) * dt * 4f * fall;
            p.VY += (-200f * str * (1f - (baseY - p.Y) / 520f) - p.VY) * dt * 3f * fall;
        }
    }

    // ───────────────────────────── things that happen by themselves ─────────────────────────────

    void AutoEvents(float dt)
    {
        _plowCooldown -= dt;
        _plowCheck -= dt;
        if (_plowCheck <= 0)
        {
            _plowCheck = 15f;
            if (_autoPlowOn && Vehicle == null && _plowCooldown <= 0 && GroundOn && Ground.Cells > 0
                && Season is Season.Winter or Season.Autumn)
            {
                float sum = 0;
                for (int i = 0; i < Ground.Cells; i++) sum += Ground.H[i];
                if (sum / Ground.Cells > MaxDepth * 0.6f) { StartVehicle(Season == Season.Autumn); _plowCooldown = 240f; }
            }
        }

        bool daytime = DateTime.Now.Hour is >= 7 and < 20;
        _critterTimer -= dt;
        if (_critterTimer <= 0)
        {
            _critterTimer = R(18f, 50f);
            if (_butterfliesOn && daytime && (Season == Season.Summer || Season == Season.Spring && RainLevel < 0.1f))
            {
                int have = Critters.Count(c => !c.Firefly && c.State != 2);
                if (have < (Season == Season.Summer ? 3 : 2)) AddButterfly(0, 0, fromEdge: true);
            }
        }
        // Butterflies shelter from the rain.
        if (RainLevel > 0.3f)
            foreach (var c in Critters) if (!c.Firefly && c.State == 0) { c.State = 2; c.TX = c.X < ScreenW / 2 ? -60f : ScreenW + 60f; c.TY = c.Y; }

        if (_firefliesOn && Season == Season.Summer && Night)
        {
            int have = 0;
            foreach (var c in Critters) if (c.Firefly && c.State != 2) have++;
            int want = (int)(34 * MathF.Min(1.5f, Amount));
            if (have < want && R() < dt * 2f)
                AddFirefly(R(0f, ScreenW), Ground.AnchorY - R(4f, 40f), R(240f, 600f));
        }

        _devilTimer -= dt;
        if (_devilTimer <= 0)
        {
            _devilTimer = R(150f, 420f);
            if (_devilsOn && Season == Season.Autumn && GroundOn) StartDevil(R(0.1f, 0.9f) * ScreenW);
        }
    }
}
