namespace Weather;

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
            else if (R() < dt * 0.03f)
                AddSeed(hx, hy, Wind + R(-20f, 20f), -R(10f, 40f), R(0.75f, 1f)); // the breeze takes one
        }
    }
}
