using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Weather;

/// <summary>A small pre-rendered image in premultiplied BGRA (same format as the overlay).</summary>
internal sealed class Sprite
{
    public readonly int W, H;
    public readonly uint[] Px;
    public Sprite(int w, int h, uint[] px) { W = w; H = h; Px = px; }
}

/// <summary>
/// All the little pictures the seasons need, drawn procedurally (vector paths, anti-aliased) once at
/// startup and kept as premultiplied pixel arrays, so drawing one per frame is just a small blend.
/// </summary>
internal static class Sprites
{
    public const int Rotations = 16;

    // ---------------------------------------------------------------- leaves and petals

    public const int LeafShapes = 4, LeafColors = 6;
    static readonly Color[] LeafPalette =
    {
        Color.FromArgb(178, 34, 34),    // crimson
        Color.FromArgb(214, 72, 32),    // scarlet-orange
        Color.FromArgb(232, 128, 30),   // orange
        Color.FromArgb(222, 168, 44),   // amber
        Color.FromArgb(198, 184, 64),   // gold-green
        Color.FromArgb(132, 82, 42),    // brown
    };
    public static Color LeafColor(int i) => LeafPalette[i % LeafColors];

    static Sprite[]? _leaves, _petals, _seeds;

    /// <summary>Size of the small sprites (leaves, petals, seeds, butterflies) relative to 96 DPI.</summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>Match the display: high-DPI screens get bigger, sharper sprites (rebuilt on change).</summary>
    public static void SetScale(float s)
    {
        s = MathF.Round(Math.Clamp(s, 1f, 2.5f) * 4f) / 4f;
        if (s == Scale) return;
        Scale = s;
        _leaves = _petals = _seeds = _butterflies = null;
        _leavesLying = _petalsLying = null;
    }

    /// <summary>Render a sprite designed at w×h (96 DPI) at the current <see cref="Scale"/>.</summary>
    static Sprite RenderScaled(int w, int h, Action<Graphics> draw) =>
        Render((int)MathF.Ceiling(w * Scale), (int)MathF.Ceiling(h * Scale), g => { g.ScaleTransform(Scale, Scale); draw(g); });
    public static Sprite Leaf(int shape, int color, float rot)
    {
        _leaves ??= BuildLeaves();
        return _leaves[((shape % LeafShapes) * LeafColors + color % LeafColors) * Rotations + RotIndex(rot, MathF.PI * 2)];
    }

    public const int PetalColors = 3;
    public static Sprite Petal(int color, float rot)
    {
        _petals ??= BuildPetals();
        return _petals[(color % PetalColors) * Rotations + RotIndex(rot, MathF.PI * 2)];
    }


    // Leaves and petals lying on a pile, seen side-on (squashed), pre-rendered so the many on every
    // pile's surface can be copied straight in each frame instead of resampled.
    static Sprite[]? _leavesLying, _petalsLying;

    public static Sprite LeafLying(int shape, int color, float rot, bool big)
    {
        if (_leavesLying == null)
        {
            _leaves ??= BuildLeaves();
            _leavesLying = new Sprite[_leaves.Length * 2];
            for (int i = 0; i < _leaves.Length; i++)
            {
                _leavesLying[i * 2] = Resample(_leaves[i], 0.7f, 0.7f * 0.55f);
                _leavesLying[i * 2 + 1] = Resample(_leaves[i], 0.95f, 0.95f * 0.55f);
            }
        }
        int idx = ((shape % LeafShapes) * LeafColors + color % LeafColors) * Rotations + RotIndex(rot, MathF.PI * 2);
        return _leavesLying[idx * 2 + (big ? 1 : 0)];
    }

    public static Sprite PetalLying(int color, float rot)
    {
        if (_petalsLying == null)
        {
            _petals ??= BuildPetals();
            _petalsLying = Array.ConvertAll(_petals, p => Resample(p, 0.8f, 0.8f * 0.45f));
        }
        return _petalsLying[(color % PetalColors) * Rotations + RotIndex(rot, MathF.PI * 2)];
    }

    /// <summary>Scale a sprite (bilinear, premultiplied).</summary>
    static Sprite Resample(Sprite s, float sx, float sy)
    {
        int w = Math.Max(1, (int)MathF.Ceiling(s.W * sx)), h = Math.Max(1, (int)MathF.Ceiling(s.H * sy));
        var px = new uint[w * h];
        uint At(int x, int y) => (uint)x < (uint)s.W && (uint)y < (uint)s.H ? s.Px[y * s.W + x] : 0u;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Average the source area under this pixel (box filter: it's a downscale).
            float u0 = x / sx, u1 = (x + 1) / sx, v0 = y / sy, v1 = (y + 1) / sy;
            float a = 0, r = 0, g = 0, b = 0, n = 0;
            for (float v = v0 + 0.25f / sy; v < v1; v += 0.5f / sy)
            for (float u = u0 + 0.25f / sx; u < u1; u += 0.5f / sx)
            {
                uint p = At((int)u, (int)v);
                a += p >> 24; r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255; n++;
            }
            if (n == 0) continue;
            px[y * w + x] = (uint)(b / n) | ((uint)(g / n) << 8) | ((uint)(r / n) << 16) | ((uint)(a / n) << 24);
        }
        return new Sprite(w, h, px);
    }
    /// <summary>A dandelion seed swaying by <paramref name="sway"/> (-1 = fully left, 1 = fully right).</summary>
    public static Sprite Seed(float sway)
    {
        _seeds ??= BuildSeeds();
        int i = (int)((Math.Clamp(sway, -1f, 1f) + 1f) * 0.5f * (_seeds.Length - 0.01f));
        return _seeds[i];
    }

    static int RotIndex(float rot, float period)
    {
        float r = rot % period;
        if (r < 0) r += period;
        return (int)(r / period * Rotations) % Rotations;
    }

    static Sprite[] BuildLeaves()
    {
        var all = new Sprite[LeafShapes * LeafColors * Rotations];
        for (int s = 0; s < LeafShapes; s++)
        for (int c = 0; c < LeafColors; c++)
        for (int r = 0; r < Rotations; r++)
        {
            float ang = r * 360f / Rotations;
            all[(s * LeafColors + c) * Rotations + r] = RenderScaled(17, 17, g =>
            {
                g.TranslateTransform(8.5f, 8.5f);
                g.RotateTransform(ang);
                using var path = LeafPath(s, 7.4f);
                var baseCol = LeafPalette[c];
                using var fill = new LinearGradientBrush(new PointF(-7, -7), new PointF(7, 7),
                    Lighten(baseCol, 0.18f), Darken(baseCol, 0.18f));
                g.FillPath(fill, path);
                using var vein = new Pen(Color.FromArgb(150, Darken(baseCol, 0.45f)), 0.7f);
                g.DrawLine(vein, 0, 6.5f, 0, -5.2f);           // main vein
                if (s == 0)                                      // maple: side veins to the lobes
                {
                    g.DrawLine(vein, 0, 1.5f, -4.2f, -2.5f);
                    g.DrawLine(vein, 0, 1.5f, 4.2f, -2.5f);
                }
                using var stem = new Pen(Darken(baseCol, 0.35f), 0.9f);
                g.DrawLine(stem, 0, 6.2f, 0.4f, 8.2f);
            });
        }
        return all;
    }

    /// <summary>Leaf outlines in a box of the given half-size, tip pointing up (-y), stem down.</summary>
    static GraphicsPath LeafPath(int shape, float s)
    {
        var p = new GraphicsPath();
        var pts = new List<PointF>();
        switch (shape)
        {
            case 0: // maple: five pointed lobes (right half, mirrored)
            {
                (float x, float y)[] half =
                {
                    (0f, -1f), (0.1f, -0.66f), (0.2f, -0.72f), (0.3f, -0.86f), (0.34f, -0.6f), (0.5f, -0.64f),
                    (0.86f, -0.6f), (0.72f, -0.36f), (0.8f, -0.24f), (0.62f, -0.12f), (0.7f, 0.02f), (0.46f, 0.06f),
                    (0.6f, 0.36f), (0.3f, 0.26f), (0.2f, 0.34f), (0.07f, 0.24f), (0.03f, 0.46f),
                };
                foreach (var (x, y) in half) pts.Add(new PointF(x * s, y * s));
                for (int i = half.Length - 1; i >= 1; i--) pts.Add(new PointF(-half[i].x * s, half[i].y * s));
                break;
            }
            case 1: // oak: wavy rounded lobes
                for (int i = 0; i <= 120; i++)
                {
                    float t = i / 120f * MathF.PI * 2;
                    float r = 1f + 0.16f * MathF.Sin(t * 7f);
                    pts.Add(new PointF(MathF.Cos(t) * s * 0.55f * r, MathF.Sin(t) * s * 0.95f * r));
                }
                break;
            case 2: // elm/birch: pointed oval, finely toothed
                for (int i = 0; i <= 120; i++)
                {
                    float t = i / 120f * MathF.PI * 2;
                    float point = 1f - 0.25f * MathF.Max(0f, -MathF.Sin(t)); // narrower toward the tip
                    float tooth = 1f + 0.05f * MathF.Sin(t * 26f);
                    pts.Add(new PointF(MathF.Cos(t) * s * 0.6f * point * tooth, MathF.Sin(t) * s * 0.98f * tooth));
                }
                break;
            default: // ginkgo: a fan with a notch
                pts.Add(new PointF(0, s * 0.75f));
                for (int i = 0; i <= 60; i++)
                {
                    float t = -MathF.PI / 2 - 1.1f + i / 60f * 2.2f;
                    float r = s * (0.95f - (MathF.Abs(i - 30) < 3 ? 0.25f : 0f));
                    pts.Add(new PointF(MathF.Cos(t) * r, MathF.Sin(t) * r * 0.85f + s * 0.1f));
                }
                break;
        }
        p.AddPolygon(pts.ToArray());
        return p;
    }

    static readonly Color[] PetalPalette =
    {
        Color.FromArgb(255, 214, 228),
        Color.FromArgb(255, 186, 210),
        Color.FromArgb(255, 238, 244),
    };
    public static Color PetalColor(int i) => PetalPalette[i % PetalColors];

    static Sprite[] BuildPetals()
    {
        var all = new Sprite[PetalColors * Rotations];
        for (int c = 0; c < PetalColors; c++)
        for (int r = 0; r < Rotations; r++)
        {
            float ang = r * 360f / Rotations;
            all[c * Rotations + r] = RenderScaled(11, 11, g =>
            {
                g.TranslateTransform(5.5f, 5.5f);
                g.RotateTransform(ang);
                using var path = new GraphicsPath();
                // Cherry blossom petal: narrow at the base, broad top with a small notch.
                path.AddBezier(0, 4.4f, -3.6f, 2.0f, -4.2f, -3.2f, -1.2f, -4.2f);
                path.AddLine(-1.2f, -4.2f, 0, -3.0f);
                path.AddLine(0, -3.0f, 1.2f, -4.2f);
                path.AddBezier(1.2f, -4.2f, 4.2f, -3.2f, 3.6f, 2.0f, 0, 4.4f);
                var col = PetalPalette[c];
                using var fill = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(255, 255, 250, 252),
                    SurroundColors = new[] { col },
                    CenterPoint = new PointF(0, 1.5f),
                };
                g.FillPath(fill, path);
                using var tint = new SolidBrush(Color.FromArgb(70, 230, 120, 160));
                g.FillEllipse(tint, -0.8f, 2.6f, 1.6f, 1.8f); // a blush of colour at the base
            });
        }
        return all;
    }

    static Sprite[] BuildSeeds()
    {
        var all = new Sprite[8];
        for (int r = 0; r < all.Length; r++)
        {
            float ang = (r - 4) * 8f; // the seed sways a little either way
            all[r] = RenderScaled(17, 17, g =>
            {
                g.TranslateTransform(8.5f, 8.5f);
                g.RotateTransform(ang);
                // A soft, silvery clock of fine hairs on a thin stalk, with the seed hanging below.
                using (var haze = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    haze.AddEllipse(-6f, -5.5f, 12f, 8f);
                    using var hb = new PathGradientBrush(haze) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } };
                    g.FillPath(hb, haze);
                }
                using var stem = new Pen(Color.FromArgb(120, 230, 226, 214), 0.45f);
                g.DrawLine(stem, 0, -1f, 0, 5f);
                using var seed = new SolidBrush(Color.FromArgb(170, 156, 126, 86));
                g.FillEllipse(seed, -0.6f, 4.6f, 1.2f, 2.2f);
                using var fluff = new Pen(Color.FromArgb(105, 255, 255, 255), 0.35f);
                using var tip = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
                for (int i = 0; i < 17; i++)
                {
                    float a = MathF.PI * 0.92f + i / 16f * MathF.PI * 1.16f;
                    float len = 5.6f + 0.6f * MathF.Sin(i * 2.3f);
                    float x = MathF.Cos(a) * len, y = MathF.Sin(a) * len * 0.8f - 1f;
                    g.DrawLine(fluff, 0, -1f, x, y);
                    g.FillEllipse(tip, x - 0.45f, y - 0.45f, 0.9f, 0.9f);
                }
            });
        }
        return all;
    }

    // ---------------------------------------------------------------- butterflies

    public const int ButterflyKinds = 4, ButterflyFrames = 6;
    static Sprite[]? _butterflies;
    /// <summary>Wing beat frame 0 = open flat, up to Frames-1 = wings almost closed (seen edge-on).</summary>
    public static Sprite Butterfly(int kind, int frame)
    {
        _butterflies ??= BuildButterflies();
        return _butterflies[(kind % ButterflyKinds) * ButterflyFrames + Math.Clamp(frame, 0, ButterflyFrames - 1)];
    }

    static Sprite[] BuildButterflies()
    {
        (Color wing, Color edge, Color spot)[] kinds =
        {
            (Color.FromArgb(242, 132, 24), Color.FromArgb(40, 24, 16), Color.White),           // monarch
            (Color.FromArgb(78, 150, 255), Color.FromArgb(24, 36, 72), Color.FromArgb(200, 230, 255)), // blue
            (Color.FromArgb(250, 224, 80), Color.FromArgb(150, 120, 30), Color.FromArgb(255, 245, 190)), // brimstone
            (Color.FromArgb(246, 244, 236), Color.FromArgb(90, 90, 90), Color.FromArgb(60, 60, 60)),     // cabbage white
        };
        var all = new Sprite[ButterflyKinds * ButterflyFrames];
        for (int k = 0; k < ButterflyKinds; k++)
        for (int f = 0; f < ButterflyFrames; f++)
        {
            float open = MathF.Cos(f / (float)(ButterflyFrames - 1) * MathF.PI / 2 * 0.92f);
            var (wing, edge, spot) = kinds[k];
            all[k * ButterflyFrames + f] = RenderScaled(23, 19, g =>
            {
                g.TranslateTransform(11.5f, 9.5f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var st = g.Save();
                    g.ScaleTransform(side * MathF.Max(open, 0.12f), 1f);
                    using var wingBrush = new SolidBrush(wing);
                    using var edgePen = new Pen(edge, 1.1f);
                    g.FillEllipse(wingBrush, 0.4f, -8.2f, 9.6f, 9.0f);   // forewing
                    g.FillEllipse(wingBrush, 0.6f, -0.6f, 7.2f, 7.4f);   // hindwing
                    g.DrawEllipse(edgePen, 0.4f, -8.2f, 9.6f, 9.0f);
                    g.DrawEllipse(edgePen, 0.6f, -0.6f, 7.2f, 7.4f);
                    using var spotBrush = new SolidBrush(spot);
                    g.FillEllipse(spotBrush, 6.4f, -6.2f, 1.6f, 1.6f);
                    g.FillEllipse(spotBrush, 4.6f, 3.0f, 1.3f, 1.3f);
                    g.Restore(st);
                }
                using var body = new SolidBrush(Color.FromArgb(40, 30, 26));
                g.FillEllipse(body, -1.0f, -6.0f, 2.0f, 12.0f);
                using var ant = new Pen(Color.FromArgb(40, 30, 26), 0.6f);
                g.DrawLine(ant, 0, -5.5f, -2.4f, -8.8f);
                g.DrawLine(ant, 0, -5.5f, 2.4f, -8.8f);
            });
        }
        return all;
    }

    // ---------------------------------------------------------------- snowmen

    static readonly Color[] ScarfColors =
    {
        Color.FromArgb(214, 48, 49), Color.FromArgb(46, 134, 222), Color.FromArgb(39, 174, 96),
        Color.FromArgb(243, 156, 18), Color.FromArgb(155, 89, 182),
    };

    /// <summary>A snowman of the given height (px), with a random scarf and maybe a hat.</summary>
    public static Sprite Snowman(int height, int variant)
    {
        float s = height / 46f;
        int w = (int)MathF.Ceiling(34 * s), h = (int)MathF.Ceiling(48 * s);
        var scarf = ScarfColors[variant % ScarfColors.Length];
        bool hat = variant % 3 != 1, beanie = variant % 3 == 1;
        return Render(w, h, g =>
        {
            g.ScaleTransform(s, s);
            void Ball(float cx, float cy, float r)
            {
                using var path = new GraphicsPath();
                path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using var b = new PathGradientBrush(path)
                {
                    CenterColor = Color.White,
                    SurroundColors = new[] { Color.FromArgb(255, 196, 212, 236) },
                    CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.4f),
                };
                g.FillPath(b, path);
            }
            Ball(17, 37, 10);  // base
            Ball(17, 22.5f, 7.5f);
            Ball(17, 11.5f, 5.8f);
            using var stick = new Pen(Color.FromArgb(112, 76, 48), 1.2f);
            g.DrawLine(stick, 10.5f, 21, 3, 15.5f);
            g.DrawLine(stick, 5.2f, 17.2f, 3.6f, 14.2f);
            g.DrawLine(stick, 23.5f, 21, 31, 16.5f);
            g.DrawLine(stick, 28.6f, 18.2f, 31.5f, 19.6f);
            using var coal = new SolidBrush(Color.FromArgb(34, 34, 40));
            g.FillEllipse(coal, 14.6f, 10.0f, 1.6f, 1.6f);
            g.FillEllipse(coal, 18.2f, 10.0f, 1.6f, 1.6f);
            for (int i = 0; i < 3; i++) g.FillEllipse(coal, 16.3f, 19.6f + i * 3.1f, 1.5f, 1.5f);
            g.FillEllipse(coal, 15.0f, 14.6f, 1.0f, 1.0f);
            g.FillEllipse(coal, 16.5f, 15.2f, 1.0f, 1.0f);
            g.FillEllipse(coal, 18.0f, 14.6f, 1.0f, 1.0f);
            using var carrot = new SolidBrush(Color.FromArgb(240, 126, 36));
            g.FillPolygon(carrot, new[] { new PointF(17, 12.3f), new PointF(17, 13.9f), new PointF(22.5f, 13.4f) });
            using var scarfB = new SolidBrush(scarf);
            g.FillRectangle(scarfB, 11.6f, 16.0f, 10.8f, 2.4f);
            g.FillPolygon(scarfB, new[] { new PointF(19.4f, 17.6f), new PointF(21.8f, 17.6f), new PointF(22.4f, 23.6f), new PointF(20.2f, 23.4f) });
            if (hat)
            {
                using var hatB = new SolidBrush(Color.FromArgb(30, 30, 34));
                g.FillRectangle(hatB, 12.0f, 5.2f, 10.0f, 1.4f);
                g.FillRectangle(hatB, 13.6f, -0.2f, 6.8f, 5.6f);
                using var band = new SolidBrush(scarf);
                g.FillRectangle(band, 13.6f, 3.6f, 6.8f, 1.2f);
            }
            else if (beanie)
            {
                using var bean = new SolidBrush(scarf);
                g.FillPie(bean, 11.6f, 3.4f, 10.8f, 7.2f, 180, 180);
                using var bob = new SolidBrush(Color.White);
                g.FillEllipse(bob, 15.6f, 1.6f, 2.8f, 2.8f);
            }
        });
    }

    // ---------------------------------------------------------------- vehicles

    static Sprite[]? _plow, _blower;
    /// <summary>The snowplough (winter) or leaf blower cart (autumn), facing right; frame animates the tracks.</summary>
    public static Sprite Vehicle(bool blower, int frame, float scale)
    {
        ref Sprite[]? cache = ref blower ? ref _blower : ref _plow;
        if (cache == null || cache[0].H != (int)MathF.Ceiling(34 * scale))
            cache = new[] { BuildVehicle(blower, 0, scale), BuildVehicle(blower, 1, scale) };
        return cache[frame & 1];
    }

    static Sprite BuildVehicle(bool blower, int frame, float scale)
    {
        int w = (int)MathF.Ceiling(70 * scale), h = (int)MathF.Ceiling(34 * scale);
        Color body = blower ? Color.FromArgb(54, 150, 84) : Color.FromArgb(246, 184, 18);
        return Render(w, h, g =>
        {
            g.ScaleTransform(scale, scale);
            // Tracks / wheels
            using var track = new SolidBrush(Color.FromArgb(44, 44, 50));
            FillRound(g, track, 6, 24, 40, 9.5f, 4.7f);
            using var hub = new SolidBrush(Color.FromArgb(120, 124, 132));
            for (int i = 0; i < 4; i++)
            {
                float cx = 11 + i * 10, cy = 28.7f;
                g.FillEllipse(hub, cx - 3, cy - 3, 6, 6);
                using var spoke = new Pen(Color.FromArgb(60, 60, 66), 0.9f);
                float a = frame * 0.8f + i;
                g.DrawLine(spoke, cx - MathF.Cos(a) * 2.6f, cy - MathF.Sin(a) * 2.6f, cx + MathF.Cos(a) * 2.6f, cy + MathF.Sin(a) * 2.6f);
            }
            // Body and cab
            using var bodyB = new LinearGradientBrush(new PointF(0, 10), new PointF(0, 26), Lighten(body, 0.15f), Darken(body, 0.12f));
            FillRound(g, bodyB, 4, 14, 40, 11, 2.5f);
            FillRound(g, bodyB, 8, 3.5f, 20, 12, 2.2f);
            using var glass = new SolidBrush(Color.FromArgb(170, 214, 238));
            FillRound(g, glass, 11, 5.8f, 6.5f, 6.5f, 1.2f);
            FillRound(g, glass, 19.2f, 5.8f, 6.5f, 6.5f, 1.2f);
            using var trim = new SolidBrush(Darken(body, 0.35f));
            g.FillRectangle(trim, 4, 19.2f, 40, 1.2f);
            // Exhaust and beacon
            using var pipe = new SolidBrush(Color.FromArgb(70, 70, 76));
            g.FillRectangle(pipe, 33, 4.5f, 2.2f, 10);
            using var beacon = new SolidBrush(blower ? Color.FromArgb(255, 220, 90) : Color.FromArgb(255, 150, 30));
            g.FillEllipse(beacon, 15.5f, 0.6f, 4.2f, 3.6f);
            if (!blower)
            {
                // Plough blade, angled, at the front (right)
                using var blade = new LinearGradientBrush(new PointF(46, 0), new PointF(66, 0),
                    Color.FromArgb(200, 206, 214), Color.FromArgb(120, 128, 140));
                g.FillPolygon(blade, new[] { new PointF(48, 11), new PointF(64, 9), new PointF(68, 33), new PointF(50, 33) });
                using var edgeP = new Pen(Color.FromArgb(80, 86, 96), 1f);
                g.DrawLine(edgeP, 44, 21, 50, 21);
                g.DrawLine(edgeP, 44, 25, 50, 27);
            }
            else
            {
                // Blower nozzle pointing ahead and down
                using var noz = new SolidBrush(Color.FromArgb(230, 120, 40));
                g.FillPolygon(noz, new[] { new PointF(44, 15), new PointF(60, 22), new PointF(64, 30), new PointF(56, 31), new PointF(44, 22) });
                using var fan = new SolidBrush(Color.FromArgb(60, 60, 66));
                g.FillEllipse(fan, 38, 9, 10, 10);
            }
        });
    }

    // ---------------------------------------------------------------- icons

    /// <summary>A season glyph (tray icon and UI): snowflake, blossom, sun or maple leaf.</summary>
    public static Bitmap SeasonGlyph(Season s, int size, bool shadow = true)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = size / 32f;
        g.ScaleTransform(k, k);
        g.TranslateTransform(16, 16);
        switch (s)
        {
            case Season.Winter:
            {
                using var outline = new Pen(Color.FromArgb(shadow ? 210 : 0, 20, 44, 84), 4.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var fill = new Pen(Color.FromArgb(235, 246, 255), 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                foreach (var pen in new[] { outline, fill })
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3 - Math.PI / 2;
                        float ex = (float)Math.Cos(a) * 12.5f, ey = (float)Math.Sin(a) * 12.5f;
                        g.DrawLine(pen, 0, 0, ex, ey);
                        float bx = (float)Math.Cos(a) * 7.5f, by = (float)Math.Sin(a) * 7.5f;
                        foreach (double side in new[] { -0.75, 0.75 })
                            g.DrawLine(pen, bx, by, bx + (float)Math.Cos(a + side) * 4.6f, by + (float)Math.Sin(a + side) * 4.6f);
                    }
                break;
            }
            case Season.Spring:
            {
                for (int i = 0; i < 5; i++)
                {
                    var st = g.Save();
                    g.RotateTransform(i * 72);
                    using var path = new GraphicsPath();
                    path.AddBezier(0, -1.5f, -7.5f, -5.5f, -6.5f, -14.5f, -1.8f, -14.2f);
                    path.AddLine(-1.8f, -14.2f, 0, -12.2f);
                    path.AddLine(0, -12.2f, 1.8f, -14.2f);
                    path.AddBezier(1.8f, -14.2f, 6.5f, -14.5f, 7.5f, -5.5f, 0, -1.5f);
                    if (shadow) { using var sh = new Pen(Color.FromArgb(150, 120, 30, 70), 1.6f); g.DrawPath(sh, path); }
                    using var fillB = new LinearGradientBrush(new PointF(0, -14), new PointF(0, 0), Color.FromArgb(255, 255, 214, 230), Color.FromArgb(255, 255, 128, 176));
                    g.FillPath(fillB, path);
                    g.Restore(st);
                }
                using var heart = new SolidBrush(Color.FromArgb(255, 255, 214, 92));
                g.FillEllipse(heart, -3, -3, 6, 6);
                break;
            }
            case Season.Summer:
            {
                using var ray = new Pen(Color.FromArgb(255, 255, 196, 40), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var rayOut = new Pen(Color.FromArgb(shadow ? 170 : 0, 120, 70, 0), 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                foreach (var pen in new[] { rayOut, ray })
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(pen, (float)Math.Cos(a) * 10.5f, (float)Math.Sin(a) * 10.5f, (float)Math.Cos(a) * 14.2f, (float)Math.Sin(a) * 14.2f);
                    }
                if (shadow) { using var sh = new SolidBrush(Color.FromArgb(170, 120, 70, 0)); g.FillEllipse(sh, -8.6f, -8.6f, 17.2f, 17.2f); }
                using var disc = new LinearGradientBrush(new PointF(-8, -8), new PointF(8, 8), Color.FromArgb(255, 255, 236, 120), Color.FromArgb(255, 255, 170, 30));
                g.FillEllipse(disc, -7.6f, -7.6f, 15.2f, 15.2f);
                break;
            }
            default:
            {
                using var path = LeafPath(0, 14.5f);
                if (shadow) { using var sh = new Pen(Color.FromArgb(170, 90, 30, 10), 2.2f); g.DrawPath(sh, path); }
                using var fillB = new LinearGradientBrush(new PointF(-12, -12), new PointF(12, 12), Color.FromArgb(255, 255, 170, 60), Color.FromArgb(255, 214, 66, 30));
                g.FillPath(fillB, path);
                using var vein = new Pen(Color.FromArgb(160, 140, 40, 16), 1.1f);
                g.DrawLine(vein, 0, 13, 0, -10);
                g.DrawLine(vein, 0, 3, -8, -5);
                g.DrawLine(vein, 0, 3, 8, -5);
                using var stem = new Pen(Color.FromArgb(255, 140, 70, 30), 1.6f);
                g.DrawLine(stem, 0, 12.5f, 1, 16);
                break;
            }
        }
        return bmp;
    }

    /// <summary>
    /// The app icon: a little four-pane window, each pane a season, with a cap of snow on its sill.
    /// </summary>
    public static Bitmap AppIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = size / 256f;
        g.ScaleTransform(k, k);

        // Rounded-square tile with a night-to-dusk gradient
        using (var tile = RoundRect(8, 8, 240, 240, 54))
        using (var bg = new LinearGradientBrush(new PointF(0, 8), new PointF(0, 248), Color.FromArgb(34, 46, 86), Color.FromArgb(18, 24, 44)))
            g.FillPath(bg, tile);
        using (var rim = new Pen(Color.FromArgb(60, 255, 255, 255), 3)) using (var tile = RoundRect(9.5f, 9.5f, 237, 237, 53)) g.DrawPath(rim, tile);

        // Small sizes: a bigger window with plain colour panes (glyphs would just be mush).
        bool small = size < 40;
        if (small) { g.TranslateTransform(128, 150); g.ScaleTransform(1.24f, 1.24f); g.TranslateTransform(-128, -150); }

        // Window panes: winter, spring / summer, autumn
        var panes = new (RectangleF r, Season s)[]
        {
            (new RectangleF(58, 74, 66, 66), Season.Winter), (new RectangleF(132, 74, 66, 66), Season.Spring),
            (new RectangleF(58, 148, 66, 66), Season.Summer), (new RectangleF(132, 148, 66, 66), Season.Autumn),
        };
        foreach (var (r, s) in panes)
        {
            var (top, bottom) = SeasonInfo.Sky(s);
            if (small) { top = Lighten(SeasonInfo.Accent(s), 0.15f); bottom = Darken(SeasonInfo.Accent(s), 0.1f); }
            using var pane = RoundRect(r.X, r.Y, r.Width, r.Height, 10);
            using var sky = new LinearGradientBrush(new PointF(0, r.Y), new PointF(0, r.Bottom), Lighten(top, 0.1f), Lighten(bottom, 0.05f));
            g.FillPath(sky, pane);
            if (small) continue;
            using var glyph = SeasonGlyph(s, 64, shadow: false);
            g.DrawImage(glyph, r.X + 9, r.Y + 9, 48, 48);
        }
        // Frame
        using var frame = new Pen(Color.FromArgb(236, 226, 214), 9) { LineJoin = LineJoin.Round };
        using (var outer = RoundRect(53, 69, 150, 150, 14)) g.DrawPath(frame, outer);
        g.DrawLine(frame, 128, 70, 128, 218);
        g.DrawLine(frame, 54, 144, 202, 144);
        // Sill
        using var sill = new LinearGradientBrush(new PointF(0, 218), new PointF(0, 236), Color.FromArgb(246, 238, 228), Color.FromArgb(196, 182, 168));
        if (!small) using (var sp = RoundRect(40, 218, 176, 16, 6)) g.FillPath(sill, sp);
        // Snow cap on top of the frame, softly lumpy, drooping over the ends
        using var snow = new GraphicsPath();
        snow.AddBezier(44, 72, 46, 50, 80, 54, 96, 56);
        snow.AddBezier(96, 56, 112, 44, 140, 46, 156, 54);
        snow.AddBezier(156, 54, 178, 48, 212, 52, 212, 72);
        snow.AddBezier(212, 72, 214, 82, 204, 84, 200, 76);
        snow.AddLine(200, 76, 56, 76);
        snow.AddBezier(56, 76, 50, 86, 42, 82, 44, 72);
        using var snowB = new LinearGradientBrush(new PointF(0, 44), new PointF(0, 84), Color.White, Color.FromArgb(206, 222, 244));
        g.FillPath(snowB, snow);
        return bmp;
    }

    // ---------------------------------------------------------------- helpers

    public static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void FillRound(Graphics g, Brush b, float x, float y, float w, float h, float r)
    {
        using var p = RoundRect(x, y, w, h, MathF.Min(r, MathF.Min(w, h) / 2));
        g.FillPath(b, p);
    }

    public static Color Lighten(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));

    public static Color Darken(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));

    /// <summary>Draw with GDI+ (anti-aliased) into a premultiplied sprite.</summary>
    public static Sprite Render(int w, int h, Action<Graphics> draw)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            draw(g);
        }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var px = new uint[w * h];
        unsafe
        {
            for (int y = 0; y < h; y++)
            {
                uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < w; x++) px[y * w + x] = row[x];
            }
        }
        bmp.UnlockBits(data);
        return new Sprite(w, h, px);
    }
}
