using System.Drawing.Drawing2D;

namespace Weather;

internal static partial class Sprites
{
    // ---------------------------------------------------------------- robin and ladybird

    public const int RobinFrames = 4; // 0 standing, 1 pecking, 2 wings up, 3 wings down
    public const int LadybirdFrames = 3; // 0 and 1 walking, 2 wing cases open
    static Sprite[]? _robin, _ladybird;

    /// <summary>A robin facing right.</summary>
    public static Sprite Robin(int frame)
    {
        _robin ??= BuildRobin();
        return _robin[Math.Clamp(frame, 0, RobinFrames - 1)];
    }

    /// <summary>A ladybird facing right, seen from the side.</summary>
    public static Sprite Ladybird(int frame)
    {
        _ladybird ??= BuildLadybird();
        return _ladybird[Math.Clamp(frame, 0, LadybirdFrames - 1)];
    }

    static Sprite[] BuildRobin()
    {
        var brown = Color.FromArgb(124, 94, 72);
        var dark = Color.FromArgb(92, 68, 52);
        var breast = Color.FromArgb(236, 104, 54);
        var all = new Sprite[RobinFrames];
        for (int f = 0; f < RobinFrames; f++)
        {
            int frame = f;
            all[f] = RenderScaled(26, 22, g =>
            {
                g.TranslateTransform(13f, 12f);
                if (frame == 1) g.RotateTransform(24f, MatrixOrder.Prepend); // tipped forward to peck
                using var b = new SolidBrush(brown);
                using var d = new SolidBrush(dark);
                using var o = new SolidBrush(breast);
                using var cream = new SolidBrush(Color.FromArgb(238, 232, 220));
                // Tail, cocked up a little
                g.FillPolygon(d, new[] { new PointF(-11f, -3.5f), new PointF(-4f, 0.5f), new PointF(-4f, 3f), new PointF(-11.5f, -1.2f) });
                // Body, belly, breast
                g.FillEllipse(b, -7f, -4.2f, 13f, 10.5f);
                g.FillEllipse(cream, -2.5f, 1.8f, 7.5f, 4.4f);
                g.FillEllipse(o, 0.2f, -3.2f, 6.6f, 6.8f);
                // Head with the orange face
                g.FillEllipse(b, 1.5f, -9f, 7.6f, 7.4f);
                g.FillEllipse(o, 4.2f, -6.6f, 4.6f, 5.4f);
                // Eye with a glint, and beak
                using (var eye = new SolidBrush(Color.FromArgb(24, 18, 16))) g.FillEllipse(eye, 5.6f, -6.6f, 1.7f, 1.7f);
                using (var glint = new SolidBrush(Color.White)) g.FillEllipse(glint, 6.3f, -6.4f, 0.6f, 0.6f);
                using (var beak = new SolidBrush(Color.FromArgb(58, 46, 38)))
                    g.FillPolygon(beak, new[] { new PointF(8.6f, -5.3f), new PointF(11.4f, -4.6f), new PointF(8.6f, -3.8f) });
                // Wing
                switch (frame)
                {
                    case 2: g.FillPolygon(d, new[] { new PointF(-4f, -2f), new PointF(3f, -2f), new PointF(-3f, -10f), new PointF(-7f, -9f) }); break;
                    case 3: g.FillPolygon(d, new[] { new PointF(-4f, 0f), new PointF(3f, 0f), new PointF(-2f, 7f), new PointF(-6.5f, 6f) }); break;
                    default: g.FillEllipse(d, -5.5f, -2.5f, 8.5f, 5.2f); break;
                }
                // Legs (tucked in flight)
                if (frame < 2)
                {
                    using var leg = new Pen(Color.FromArgb(150, 108, 92), 0.8f);
                    g.DrawLine(leg, -0.5f, 5.6f, -1f, 9.5f);
                    g.DrawLine(leg, 1.6f, 5.6f, 2f, 9.5f);
                }
            });
        }
        return all;
    }

    static Sprite[] BuildLadybird()
    {
        var all = new Sprite[LadybirdFrames];
        for (int f = 0; f < LadybirdFrames; f++)
        {
            int frame = f;
            all[f] = RenderScaled(16, 12, g =>
            {
                g.TranslateTransform(8f, 7.5f);
                using var black = new SolidBrush(Color.FromArgb(28, 24, 26));
                // Legs
                using (var leg = new Pen(Color.FromArgb(28, 24, 26), 0.6f))
                {
                    float a = frame == 1 ? 0.8f : -0.8f;
                    g.DrawLine(leg, -2.5f, 2f, -3.5f + a, 4.4f);
                    g.DrawLine(leg, 0f, 2f, -0.6f - a, 4.4f);
                    g.DrawLine(leg, 2.4f, 2f, 2.6f + a, 4.4f);
                }
                if (frame == 2)
                {
                    // Wing cases lifted, the thin flying wings showing beneath.
                    using var wing = new SolidBrush(Color.FromArgb(120, 220, 224, 232));
                    g.FillEllipse(wing, -7.5f, -5.5f, 8f, 3.4f);
                    var st = g.Save();
                    g.RotateTransform(-22f);
                    using var red = new SolidBrush(Color.FromArgb(222, 44, 36));
                    g.FillPie(red, -4.6f, -3.2f, 9f, 7.6f, 180, 180);
                    g.Restore(st);
                }
                else
                {
                    using var red = new LinearGradientBrush(new PointF(0, -4), new PointF(0, 2), Color.FromArgb(240, 70, 52), Color.FromArgb(190, 30, 26));
                    g.FillPie(red, -4.6f, -3.6f, 9.2f, 8f, 180, 180);
                    g.FillEllipse(black, -2.6f, -2.4f, 1.8f, 1.8f);
                    g.FillEllipse(black, 0.4f, -3.2f, 1.6f, 1.6f);
                    g.FillEllipse(black, 1.8f, -1.2f, 1.5f, 1.5f);
                    using var shine = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
                    g.FillEllipse(shine, -1.6f, -3.2f, 2.2f, 1f);
                }
                g.FillEllipse(black, 3.4f, -2f, 3.2f, 3f); // head
                using var eye = new SolidBrush(Color.FromArgb(230, 230, 230));
                g.FillEllipse(eye, 5.4f, -1.4f, 0.8f, 0.8f);
            });
        }
        return all;
    }
}
