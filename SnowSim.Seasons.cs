namespace Weather;

/// <summary>
/// The four seasons: which one is showing, how it changes over, and what falls from the sky in
/// each (snow, cherry blossom and spring showers, dandelion seeds, autumn leaves).
/// </summary>
internal sealed partial class SnowSim
{
    public Season Season { get; private set; } = Season.Winter;
    public Material Material { get; private set; } = Material.Snow;

    /// <summary>The season's intensity setting (1 = normal).</summary>
    float Amount = 1f;
    bool _seasonInit, _melting;
    float _clockCheck;
    static readonly bool ForceNight = Environment.GetEnvironmentVariable("WEATHER_NIGHT") == "1";

    /// <summary>Night time (fireflies come out in summer).</summary>
    public bool Night { get; private set; }

    // Spring showers
    public float RainLevel { get; private set; }   // 0..1, eases in and out
    float _rainTarget, _showerTimer = 90f;
    bool _showersOn = true;

    float HiddenRate => Amount * Season switch { Season.Winter => 1f, Season.Autumn => 0.6f, _ => 0.35f };

    void ApplySeason(Settings st)
    {
        var want = SeasonInfo.Resolve(st.Season, st.SouthernHemisphere);
        if (!_seasonInit) { _seasonInit = true; SetSeason(want); }
        else if (want != Season) ChangeSeason(want);

        Amount = Math.Clamp(Season switch
        {
            Season.Winter => st.WinterAmount,
            Season.Spring => st.SpringAmount,
            Season.Summer => st.SummerAmount,
            _ => st.AutumnAmount,
        }, 0, 300) / 100f;

        // Each material piles differently: petals make a thin pink carpet, leaves a loose heap that
        // stands steeper than snow.
        switch (Material)
        {
            case Material.Petals:
                MaxDepth = Math.Clamp(MaxDepth * 0.35f, 5f, 14f);
                Repose *= 0.6f;
                EdgeLimit = MathF.Min(EdgeLimit, MaxDepth * 0.8f);
                break;
            case Material.Leaves:
                MaxDepth = Math.Clamp(MaxDepth * 0.75f, 6f, 28f);
                Repose = MathF.Max(Repose * 1.3f, 1.0f);
                EdgeLimit = MathF.Min(EdgeLimit * 1.2f, MaxDepth);
                break;
        }

        _showersOn = st.Showers;
        _playOn = st.PlayWithMouse;
        _autoPlowOn = st.AutoPlow;
        _butterfliesOn = st.Butterflies;
        _firefliesOn = st.Fireflies;
        _devilsOn = st.DustDevils;
        MeadowOn = st.Meadow;
    }

    void SetSeason(Season s)
    {
        Season = s;
        Material = SeasonInfo.MaterialOf(s);
        _spawnAcc = 0;
    }

    /// <summary>
    /// The season turned: what lies on the windows slides off (as the old material), the taskbar's
    /// pile melts away, snowmen melt and the critters fly off, and the new season starts falling.
    /// </summary>
    void ChangeSeason(Season next)
    {
        foreach (var s in _windows.Values)
        {
            if (s.Clamped) Array.Clear(s.H);
            else Shed(s, 1f, 0f, -30f, 40f);
        }
        _melting = true;
        foreach (var sm in Snowmen) sm.Melting = true;
        foreach (var c in Critters) c.State = 2;
        Holding = false;
        RainLevel = _rainTarget = 0;
        SetSeason(next);
    }

    /// <summary>Per-frame season housekeeping: the clock (night), a melting taskbar pile.</summary>
    void UpdateSeason(float dt)
    {
        _clockCheck -= dt;
        if (_clockCheck <= 0)
        {
            _clockCheck = 5f;
            int hour = DateTime.Now.Hour;
            Night = ForceNight || hour >= 20 || hour < 5;
        }
        if (_melting)
        {
            float k = MathF.Exp(-dt * 1.4f), max = 0;
            for (int i = 0; i < Ground.Cells; i++) { Ground.H[i] *= k; max = MathF.Max(max, Ground.H[i]); }
            if (max < 0.15f) { Array.Clear(Ground.H); _melting = false; }
        }
    }

    void Spawn(float dt)
    {
        UpdateSeason(dt);
        switch (Season)
        {
            case Season.Winter: SpawnSnow(dt); break;
            case Season.Spring: UpdateShowers(dt); SpawnPetals(dt); SpawnRain(dt); break;
            case Season.Summer: SpawnSeeds(dt); break;
            default: SpawnLeaves(dt); break;
        }
    }

    ref Particle NewParticle(PKind kind)
    {
        ref var p = ref Particles[Count++];
        p = default;
        p.Kind = kind;
        return ref p;
    }

    /// <summary>Start x for something spawned above the screen, upwind so the wind carries it across.</summary>
    float SpawnX(float fallSpeed) => R(-60f, ScreenW + 60f) - Wind * (ScreenH / MathF.Max(fallSpeed, 5f)) * 0.5f;

    // ───────────────────────────── spring ─────────────────────────────

    void SpawnPetals(float dt)
    {
        _spawnAcc += SpawnRate() * 0.13f * Amount * (1f - RainLevel * 0.6f) * dt;
        while (_spawnAcc >= 1f)
        {
            _spawnAcc -= 1f;
            if (Count >= MaxParticles) { _spawnAcc = 0; break; }
            ref var p = ref NewParticle(PKind.Petal);
            float d = R();
            d = 0.15f + 0.85f * d * d;
            p.Depth = d;
            p.R = (0.55f + 0.8f * d) * FlakeScale;              // sprite scale
            p.Shape = (byte)_rng.Next(Sprites.PetalColors);
            p.Vol = 9f * FlakeDeposit * p.R;
            p.Alpha = (0.75f + 0.25f * d) * MathF.Min(1f, FlakeAlpha * 1.35f);
            p.Fall = R(26f, 46f) * (0.6f + 0.5f * d);
            p.Sway = R(16f, 34f) * (0.5f + 0.6f * d);
            p.Freq = R(2.2f, 3.6f);
            p.Phase = R(0f, 6.283f);
            p.Rot = R(0f, 6.283f);
            p.RotSpeed = R(-2.6f, 2.6f);
            p.VX = Wind;
            p.VY = p.Fall;
            p.X = SpawnX(p.Fall);
            p.Y = -R(10f, 40f);
        }
    }

    void UpdateShowers(float dt)
    {
        if (!_showersOn && _rainTarget > 0 && !_manualShower) _rainTarget = 0;
        _showerTimer -= dt;
        if (_showerTimer <= 0)
        {
            if (_rainTarget > 0) { _rainTarget = 0; _manualShower = false; _showerTimer = R(240f, 720f); }
            else if (_showersOn) { _rainTarget = R(0.35f, 1f); _showerTimer = R(25f, 80f); }
            else _showerTimer = 60f;
        }
        RainLevel += (_rainTarget - RainLevel) * MathF.Min(1f, dt * 0.3f);
        if (RainLevel < 0.002f && _rainTarget == 0) RainLevel = 0;
    }

    bool _manualShower;

    /// <summary>Start a shower now, or stop the one that's falling.</summary>
    public void ToggleShower()
    {
        if (_rainTarget > 0) { _rainTarget = 0; _manualShower = false; _showerTimer = R(240f, 720f); }
        else { _rainTarget = R(0.65f, 1f); _manualShower = true; _showerTimer = R(30f, 60f); }
    }

    void SpawnRain(float dt)
    {
        if (RainLevel <= 0.01f) return;
        _rainAcc += SpawnRate() * 5f * RainLevel * MathF.Max(Amount, 0.3f) * dt;
        while (_rainAcc >= 1f)
        {
            _rainAcc -= 1f;
            if (Count >= MaxParticles) { _rainAcc = 0; break; }
            ref var p = ref NewParticle(PKind.Rain);
            float d = R();
            p.Depth = d;
            p.VY = (650f + 700f * d) * R(0.9f, 1.1f);
            p.VX = Wind * 2.2f;
            p.R = 0.5f + 0.7f * d;
            p.Alpha = (0.16f + 0.3f * d) * MathF.Min(1f, 0.5f + RainLevel);
            p.X = R(-60f, ScreenW + 60f) - p.VX * ScreenH / p.VY * 0.5f;
            p.Y = -R(10f, 120f);
        }
    }
    float _rainAcc;

    /// <summary>A raindrop hits a ledge or the taskbar: a couple of tiny droplets bounce up.</summary>
    void Splash(float x, float y, float depth)
    {
        int n = 2 + _rng.Next(2);
        for (int k = 0; k < n && Count < MaxParticles; k++)
        {
            ref var p = ref NewParticle(PKind.Splash);
            p.X = x + R(-2f, 2f);
            p.Y = y - 1f;
            p.VX = R(-70f, 70f);
            p.VY = -R(90f, 200f) * (0.5f + 0.5f * depth);
            p.Age = R(0.16f, 0.32f);
            p.R = 0.6f + 0.5f * depth;
            p.Alpha = 0.4f + 0.3f * depth;
            p.Shape = 1; // water (vs. 0 = snow spray)
        }
    }

    // ───────────────────────────── summer ─────────────────────────────

    void SpawnSeeds(float dt)
    {
        _spawnAcc += SpawnRate() * 0.02f * Amount * (Night ? 0.25f : 1f) * dt; // few seeds about at night
        while (_spawnAcc >= 1f)
        {
            _spawnAcc -= 1f;
            if (Count >= MaxParticles) { _spawnAcc = 0; break; }
            float d = R();
            float roll = R();
            float x, y, vy;
            if (roll < 0.5f) { x = SpawnX(12f); y = -R(10f, 40f); vy = 10f; }
            else if (roll < 0.8f) { x = Wind >= 0 ? -20f : ScreenW + 20f; y = R(0.05f, 0.6f) * ScreenH; vy = 0f; }
            else { x = R(0f, ScreenW); y = Ground.AnchorY - R(2f, 14f); vy = -R(20f, 45f); } // lifted off the meadow
            AddSeed(x, y, Wind, vy, d);
        }
        // Sunlit pollen motes drift and twinkle in the warm air by day.
        if (!Night)
        {
            _moteAcc += SpawnRate() * 0.03f * Amount * dt;
            while (_moteAcc >= 1f && Count < MaxParticles)
            {
                _moteAcc -= 1f;
                AddSeed(R(0f, ScreenW), R(0.08f, 0.92f) * ScreenH, Wind, R(-6f, 6f), R());
                ref var m = ref Particles[Count - 1];
                m.Shape = 1;
                m.Age = R(8f, 18f);
                m.Fall = R(-3f, 4f);
            }
        }
    }
    float _moteAcc;

    void AddSeed(float x, float y, float vx, float vy, float depth)
    {
        if (Count >= MaxParticles) return;
        ref var p = ref NewParticle(PKind.Seed);
        p.Depth = depth;
        p.R = (0.7f + 0.5f * depth) * FlakeScale;
        p.Alpha = (0.7f + 0.3f * depth) * MathF.Min(1f, FlakeAlpha * 1.4f);
        p.Fall = R(6f, 14f);
        p.Sway = R(6f, 14f);
        p.Freq = R(0.6f, 1.3f);
        p.Phase = R(0f, 6.283f);
        p.Age = R(30f, 60f);
        p.X = x; p.Y = y; p.VX = vx; p.VY = vy;
        p.NoLand = 0.5f;
    }

    // ───────────────────────────── autumn ─────────────────────────────

    void SpawnLeaves(float dt)
    {
        _spawnAcc += SpawnRate() * 0.085f * Amount * dt;
        while (_spawnAcc >= 1f)
        {
            _spawnAcc -= 1f;
            if (Count >= MaxParticles) { _spawnAcc = 0; break; }
            ref var p = ref NewParticle(PKind.Leaf);
            float d = R();
            d = 0.12f + 0.88f * d * d;
            p.Depth = d;
            p.R = (0.6f + 0.7f * d) * FlakeScale;
            p.Shape = RandomLeaf();
            p.Vol = 22f * FlakeDeposit * p.R;
            p.Alpha = (0.72f + 0.28f * d) * MathF.Min(1f, FlakeAlpha * 1.4f);
            InitLeafMotion(ref p, d);
            p.VX = Wind;
            p.VY = p.Fall;
            p.X = SpawnX(p.Fall);
            p.Y = -R(15f, 50f);
        }
    }

    byte RandomLeaf()
    {
        float r = R();
        int shape = r < 0.38f ? 0 : r < 0.62f ? 1 : r < 0.86f ? 2 : 3;
        return (byte)(shape * Sprites.LeafColors + _rng.Next(Sprites.LeafColors));
    }

    void InitLeafMotion(ref Particle p, float d)
    {
        p.Fall = R(55f, 95f) * (0.6f + 0.5f * d);
        p.Sway = R(35f, 70f) * (0.5f + 0.6f * d);
        p.Freq = R(1.8f, 3.0f);
        p.Phase = R(0f, 6.283f);
        p.Rot = R(0f, 6.283f);
        // Most leaves rock like a pendulum; a few tumble end over end.
        p.RotSpeed = R() < 0.15f ? R(3f, 6f) * (R() < 0.5f ? -1 : 1) : R(-0.6f, 0.6f);
    }

    /// <summary>
    /// Snow physics asked for a chunk of the pile (flung, shaken, slid off): in spring and autumn that
    /// is a petal or a leaf, carrying the same amount, that tumbles down and lands again.
    /// </summary>
    void AddLitter(float x, float y, float vol, float vx, float vy, bool small)
    {
        if (small && R() > 0.4f) return; // fine spray: only some of it is worth a whole leaf
        bool leaf = Material == Material.Leaves;
        ref var p = ref NewParticle(leaf ? PKind.Leaf : PKind.Petal);
        p.FromPile = true;
        p.X = x; p.Y = y; p.VX = vx; p.VY = vy;
        p.Vol = vol;
        p.Depth = 1f;
        p.Alpha = 1f;
        p.NoLand = 0.25f;
        if (leaf)
        {
            p.R = R(0.75f, 1.05f) * FlakeScale;
            p.Shape = RandomLeaf();
            InitLeafMotion(ref p, 1f);
        }
        else
        {
            p.R = R(0.8f, 1.2f) * FlakeScale;
            p.Shape = (byte)_rng.Next(Sprites.PetalColors);
            p.Fall = R(30f, 46f);
            p.Sway = R(18f, 32f);
            p.Freq = R(2.2f, 3.6f);
            p.Phase = R(0f, 6.283f);
            p.RotSpeed = R(-2.6f, 2.6f);
        }
    }

    /// <summary>
    /// Petals, leaves and seeds don't fall, they flutter: each swings like a little pendulum, falling
    /// fastest through the middle of a swing and almost stalling at its ends, carried by the wind and
    /// the shared air currents. Their velocity eases toward that, so a kick or a gust carries them a while.
    /// </summary>
    void StepFlutter(ref Particle p, float dt, float wind)
    {
        float d = p.Depth, par = 0.45f + 0.55f * d;
        p.Phase += p.Freq * dt;
        p.NoLand -= dt;
        float s = MathF.Sin(p.Phase), c = MathF.Cos(p.Phase);
        Turbulence(p.X, p.Y + d * 400f, out float tx, out float ty);
        float tvx, tvy;
        if (p.Kind == PKind.Seed)
        {
            // Dandelion seeds ride the air: currents carry them up as often as down.
            tvx = wind * (0.8f + 0.4f * d) + tx * TurbAmp * 1.8f * par + c * p.Sway;
            tvy = p.Fall + ty * TurbAmp * 1.5f * par + s * p.Sway * 0.4f;
            p.Rot = s * 0.35f;
        }
        else
        {
            tvx = wind * (0.6f + 0.5f * d) + c * p.Sway + tx * TurbAmp * par;
            tvy = p.Fall * (0.35f + 1.1f * c * c) + ty * TurbAmp * 0.4f * par;
            p.Rot += p.RotSpeed * dt;
        }
        float k = MathF.Min(1f, (p.FromPile ? 2.2f : 4f) * dt);
        p.VX += (tvx - p.VX) * k;
        p.VY += (tvy - p.VY) * k;
        p.X += p.VX * dt;
        p.Y += p.VY * dt;
    }
}
