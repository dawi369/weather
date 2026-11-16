namespace Windowsill;

/// <summary>A dandelion clock in the summer meadow: where it stands, how tall, and whether it has seeds.</summary>
internal struct Clock
{
    public float X, Height;
    public float Full;   // 0 = just blown (bare), 1 = a full white clock again
    public int Slot;
}

/// <summary>
/// Dandelion clocks stand among the summer grass. Brush past one with the mouse and it bursts into
/// a cloud of seeds; then it slowly grows a new clock. The breeze carries off the odd seed too.
/// </summary>
internal sealed partial class SnowSim
{
    public readonly List<Clock> Clocks = new();
    float[] _clockRegrow = Array.Empty<float>();
    const float ClockRegrow = 45f;
    static float H01(int seed, int i) => (Hash(seed, i) + 1f) * 0.5f;

    void UpdateClocks(float dt)
    {
        Clocks.Clear();
        if (Season != Season.Summer || !MeadowOn || !GroundOn || Ground.Cells == 0) return;
        float s = UiScale * 0.7f;
        float step = 170f * MathF.Max(1f, s);
        int slots = (int)(ScreenW / step) + 1;
        if (_clockRegrow.Length != slots) _clockRegrow = new float[slots];
        float baseY = Ground.AnchorY;
        for (int i = 0; i < slots; i++)
        {
            if (H01(4441, i) < 0.4f) continue; // not every spot has one
            float x = (i + 0.15f + 0.7f * H01(4442, i)) * step;
            if (x < 8 || x > ScreenW - 8) continue;
            float height = (17f + 9f * H01(4443, i)) * MathF.Max(1f, s);
            ref float regrow = ref _clockRegrow[i];
            if (regrow > 0) regrow -= dt;
            float full = regrow <= 0 ? 1f : 1f - regrow / ClockRegrow;
            var c = new Clock { X = x, Height = height, Full = full, Slot = i };
            Clocks.Add(c);
            if (regrow > 0) continue;

            float hx = x, hy = baseY - height;
            float dx = hx - CursorX, dy = hy - CursorY;
            if (dx * dx + dy * dy < (26f * s) * (26f * s))
            {
                // Brushed: the whole clock bursts away from the mouse.
                float away = dx >= 0 ? 1f : -1f;
                int n = 10 + _rng.Next(5);
                for (int k = 0; k < n; k++)
                    AddSeed(hx + R(-4f, 4f) * s, hy + R(-4f, 4f) * s, away * R(40f, 200f) + Wind, -R(30f, 140f), R(0.75f, 1f));
                regrow = ClockRegrow * R(0.8f, 1.2f);
            }
            else if (!Night && R() < dt * 0.03f)
                AddSeed(hx, hy, Wind + R(-20f, 20f), -R(10f, 40f), R(0.75f, 1f)); // the breeze takes one
        }
    }
}

/// <summary>A bumblebee working the summer flowers.</summary>
internal sealed class Bee
{
    public float X, Y, VX, VY, TX, TY, Timer, Age, Life;
    public int Dir = 1;
    public bool Leaving;
}

/// <summary>
/// Summer days: a few bumblebees bumble from flower to flower along the meadow and the windowsill
/// gardens, hovering at each a moment. They veer away from the mouse.
/// </summary>
internal sealed partial class SnowSim
{
    public readonly List<Bee> Bees = new();

    void UpdateBees(float dt)
    {
        float s = MathF.Max(1f, UiScale * 0.7f);
        bool season = Season == Season.Summer && IsDay && MeadowOn && GroundOn && _visitorsOn && RainLevel < 0.1f;
        if (season && Bees.Count < 4 && R() < dt * 0.25f)
        {
            bool left = R() < 0.5f;
            Bees.Add(new Bee { X = left ? -20f : ScreenW + 20f, Y = Ground.AnchorY - R(30f, 160f) * s, Life = R(40f, 90f) });
        }
        for (int i = Bees.Count - 1; i >= 0; i--)
        {
            var b = Bees[i];
            b.Age += dt;
            b.Timer -= dt;
            if (!season || b.Age > b.Life) b.Leaving = true;
            if (b.Leaving)
            {
                b.TX = b.X < ScreenW / 2 ? -60f : ScreenW + 60f;
                b.TY = b.Y - 100f;
            }
            else if (b.Timer <= 0)
            {
                b.Timer = R(1.5f, 4f);
                if (Z.Count > 0 && R() < 0.3f)
                {
                    var w = Z[_rng.Next(Z.Count)];
                    if (!w.Clamped && !w.NoSnow && w.AnchorY > 60)
                    {
                        b.TX = Math.Clamp(R(w.Left + 10f, w.Right - 10f), 0f, ScreenW);
                        b.TY = w.AnchorY - R(5f, 14f) * s; // over the windowsill garden
                        goto steer;
                    }
                }
                if (Clocks.Count > 0 && R() < 0.4f) { var c = Clocks[_rng.Next(Clocks.Count)]; b.TX = c.X; b.TY = Ground.AnchorY - c.Height - 6f * s; }
                else { b.TX = R(0f, ScreenW); b.TY = Ground.AnchorY - R(10f, 30f) * s; }
            }
        steer:
            float dx = b.TX - b.X, dy = b.TY - b.Y;
            float max = (b.Leaving ? 220f : 130f) * s;
            float wx = Math.Clamp(dx * 2f, -max, max) + MathF.Sin(b.Age * 17f) * 40f * s;
            float wy = Math.Clamp(dy * 2f, -max, max) + MathF.Cos(b.Age * 13f) * 30f * s;
            float cdx = b.X - CursorX, cdy = b.Y - CursorY;
            if (cdx * cdx + cdy * cdy < (50f * s) * (50f * s)) { wx += MathF.Sign(cdx) * 300f * s; wy -= 200f * s; }
            b.VX += (wx - b.VX) * MathF.Min(1f, dt * 4f);
            b.VY += (wy - b.VY) * MathF.Min(1f, dt * 4f);
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            if (MathF.Abs(b.VX) > 8f) b.Dir = b.VX > 0 ? 1 : -1;
            if (b.Leaving && (b.X < -50 || b.X > ScreenW + 50)) Bees.RemoveAt(i);
        }
    }
}
