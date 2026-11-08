namespace Weather;

/// <summary>
/// Pre-rendered snowflake crystals: six-armed dendrites (the classic "stellar dendrite" shape),
/// a few designs, each at a few sizes and rotations, as anti-aliased alpha masks. Drawing one is a
/// small alpha blit, about as cheap as a soft dot. Built once at startup (~tens of ms).
/// </summary>
internal static class Crystals
{
    public const int Designs = 6;
    public const int Rotations = 12;                     // over 60° (six-fold symmetric)
    public static readonly int[] Sizes = { 9, 13, 17 };  // px across

    static readonly float[][] Masks = Build();

    /// <summary>Alpha mask (Size×Size) for a design at a given radius and rotation (radians).</summary>
    public static float[] Get(int design, float radius, float rotation, out int size)
    {
        int si = radius < 5.5f ? 0 : radius < 7.5f ? 1 : 2;
        size = Sizes[si];
        float sector = MathF.PI / 3f;
        float r = rotation % sector;
        if (r < 0) r += sector;
        int ri = (int)(r / sector * Rotations) % Rotations;
        return Masks[(design % Designs * Sizes.Length + si) * Rotations + ri];
    }

    readonly record struct Seg(float X0, float Y0, float X1, float Y1, float Width);

    static float[][] Build()
    {
        var all = new float[Designs * Sizes.Length * Rotations][];
        for (int d = 0; d < Designs; d++)
        {
            var rng = new Random(7919 * (d + 1));
            // Arm design (in units of the crystal's radius): side branches at a few points along the arm,
            // longer near the middle, swept toward the tip at 60°, like real dendrites.
            int branches = 2 + rng.Next(2);
            var bt = new float[branches];
            var bl = new float[branches];
            for (int b = 0; b < branches; b++)
            {
                bt[b] = 0.3f + 0.55f * (b + 0.5f + (float)rng.NextDouble() * 0.3f) / branches;
                bl[b] = (0.18f + 0.22f * (float)rng.NextDouble()) * (1.1f - bt[b] * 0.6f);
            }
            bool plate = rng.NextDouble() < 0.5; // some have a small hexagonal plate at the centre
            float thick = 0.85f + (float)rng.NextDouble() * 0.6f; // some crystals are delicate, some chunky

            for (int si = 0; si < Sizes.Length; si++)
            for (int ri = 0; ri < Rotations; ri++)
            {
                int size = Sizes[si];
                float radiusPx = size / 2f - 0.6f;
                float rot = ri * (MathF.PI / 3f) / Rotations;
                // Line widths in pixels don't shrink with the crystal, so small ones read as stars.
                float mainW = 1.1f * thick / radiusPx, branchW = 0.8f * thick / radiusPx;
                var segs = new List<Seg>();
                for (int arm = 0; arm < 6; arm++)
                {
                    float a = rot + arm * MathF.PI / 3f;
                    float ax = MathF.Cos(a), ay = MathF.Sin(a);
                    segs.Add(new Seg(0, 0, ax, ay, mainW));
                    for (int b = 0; b < branches; b++)
                    {
                        float bx = ax * bt[b], by = ay * bt[b];
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float ba = a + side * MathF.PI / 3f;
                            segs.Add(new Seg(bx, by, bx + MathF.Cos(ba) * bl[b], by + MathF.Sin(ba) * bl[b], branchW));
                        }
                    }
                }
                all[(d * Sizes.Length + si) * Rotations + ri] = Rasterize(size, radiusPx, segs, plate ? 0.2f : 0f, rot);
            }
        }
        return all;
    }

    static float[] Rasterize(int size, float radiusPx, List<Seg> segs, float plate, float rot)
    {
        const int SS = 4; // supersampling
        var mask = new float[size * size];
        float c = size / 2f;
        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            int hits = 0;
            for (int sy = 0; sy < SS; sy++)
            for (int sx = 0; sx < SS; sx++)
            {
                float x = (px + (sx + 0.5f) / SS - c) / radiusPx, y = (py + (sy + 0.5f) / SS - c) / radiusPx;
                bool on = false;
                if (plate > 0 && InHexagon(x, y, plate, rot)) on = true;
                for (int i = 0; i < segs.Count && !on; i++)
                    if (DistToSeg(x, y, segs[i]) <= segs[i].Width * 0.5f) on = true;
                if (on) hits++;
            }
            mask[py * size + px] = hits / (float)(SS * SS);
        }
        return mask;
    }

    static float DistToSeg(float x, float y, Seg s)
    {
        float dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
        float t = Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / (dx * dx + dy * dy), 0f, 1f);
        float ex = s.X0 + dx * t - x, ey = s.Y0 + dy * t - y;
        return MathF.Sqrt(ex * ex + ey * ey);
    }

    static bool InHexagon(float x, float y, float r, float rot)
    {
        for (int k = 0; k < 3; k++)
        {
            float a = rot + k * MathF.PI / 3f + MathF.PI / 6f;
            if (MathF.Abs(x * MathF.Cos(a) + y * MathF.Sin(a)) > r * 0.866f) return false;
        }
        return true;
    }
}
