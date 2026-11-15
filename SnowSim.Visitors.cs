namespace Weather;

internal enum VisitorKind : byte { Robin, Ladybird }

/// <summary>A small creature that lands on a ledge and potters along it for a while.</summary>
internal sealed class Visitor
{
    public VisitorKind Kind;
    public int State;          // 0 flying in, 1 on a ledge, 2 flying off
    public int Action;         // on a ledge: 0 idle, 1 hop / crawl, 2 peck / wings open
    public int Dir = 1;        // facing: 1 right, -1 left
    public float X, Y, VX, VY, TX, TY, Age, Life, Timer, Phase, HopT, Lift;
    public IntPtr Surf;
    public bool OnGround;
    public Snowman? OnSnowman;
    public float RelX;
}

/// <summary>
/// Visitors: a robin that hops along the snowy (or leafy) ledges and perches on snowmen, and a
/// ladybird that wanders along window tops in spring and summer. Both are off the moment the
/// mouse comes close or their window moves.
/// </summary>
internal sealed partial class SnowSim
{
    public readonly List<Visitor> Visitors = new();
    bool _visitorsOn = true;
    float _visitorTimer = 8f;

    float VS => MathF.Max(1f, UiScale * 0.7f); // size of visitors relative to 96 DPI

    void AddVisitor(VisitorKind kind, float x = float.NaN, float y = float.NaN)
    {
        bool left = R() < 0.5f;
        var v = new Visitor
        {
            Kind = kind,
            X = float.IsNaN(x) ? (left ? -30f : ScreenW + 30f) : x,
            Y = float.IsNaN(y) ? R(0.1f, 0.5f) * ScreenH : y,
            Life = kind == VisitorKind.Robin ? R(14f, 32f) : R(20f, 45f),
            Phase = R(0f, 6.283f),
        };
        v.Dir = v.X < ScreenW / 2 ? 1 : -1;
        if (!PickPerch(v)) { v.TX = R(0.2f, 0.8f) * ScreenW; v.TY = R(0.2f, 0.5f) * ScreenH; }
        Visitors.Add(v);
    }

    /// <summary>Somewhere to land: a window's top edge, the taskbar, or (robins) a snowman's head.</summary>
    bool PickPerch(Visitor v)
    {
        v.OnSnowman = null;
        v.Surf = IntPtr.Zero;
        v.OnGround = false;
        if (v.Kind == VisitorKind.Robin && Snowmen.Count > 0 && R() < 0.35f)
        {
            var sm = Snowmen[_rng.Next(Snowmen.Count)];
            if (!sm.Falling && !sm.Melting && !sm.Hidden)
            {
                v.OnSnowman = sm;
                v.TX = sm.X;
                v.TY = sm.Y - sm.Height * 0.97f;
                return true;
            }
        }
        for (int tries = 0; tries < 6; tries++)
        {
            bool ground = v.Kind == VisitorKind.Robin && GroundOn && R() < 0.3f;
            Surface? s = ground ? Ground : Z.Count > 0 ? Z[_rng.Next(Z.Count)] : null;
            if (s == null || s.Clamped || s.Cells < 20 || s.AnchorY < 60) continue;
            int i = _rng.Next(6, s.Cells - 6);
            float x = s.Left + (i + 0.5f) * Cell;
            if (x < 20 || x > ScreenW - 20 || s.Vis[i] == 0 || s.Shield[i] != 0) continue;
            v.OnGround = s.IsGround;
            v.Surf = s.IsGround ? IntPtr.Zero : s.Hwnd;
            v.RelX = x - s.Left;
            v.TX = x;
            v.TY = s.PileTop(i);
            return true;
        }
        return false;
    }

    Surface? PerchSurface(Visitor v) =>
        v.OnGround ? (GroundOn ? Ground : null) : _windows.TryGetValue(v.Surf, out var s) ? s : null;

    void FlyOff(Visitor v)
    {
        if (v.State == 2) return;
        v.State = 2;
        v.OnSnowman = null;
        float away = CursorX < v.X ? 1f : -1f;
        v.Dir = (int)away;
        v.TX = away > 0 ? ScreenW + 60f : -60f;
        v.TY = v.Y - R(150f, 400f);
        v.VY = -120f;
    }

    void UpdateVisitors(float dt)
    {
        float sc = VS;
        for (int i = Visitors.Count - 1; i >= 0; i--)
        {
            var v = Visitors[i];
            v.Age += dt;
            v.Phase += dt;
            float cdx = v.X - CursorX, cdy = v.Y - CursorY;
            bool cursorNear = cdx * cdx + cdy * cdy < (70f * sc) * (70f * sc);

            if (v.State == 1)
            {
                if (cursorNear || v.Age > v.Life) { FlyOff(v); continue; }
                float baseY;
                if (v.OnSnowman is Snowman sm)
                {
                    if (sm.Falling || sm.Melting || sm.Hidden || !Snowmen.Contains(sm)) { FlyOff(v); continue; }
                    v.X = sm.X + v.RelX;
                    baseY = sm.Y - sm.Height * (0.25f + 0.75f * sm.Grow) * 0.97f;
                    if (MathF.Abs(sm.Wobble) > 0.1f) { FlyOff(v); continue; }
                }
                else
                {
                    var s = PerchSurface(v);
                    if (s == null || s.Cells == 0 || s.Clamped || MathF.Abs(s.VX) + MathF.Abs(s.VY) > 40f) { FlyOff(v); continue; }
                    float width = s.Right - s.Left;
                    // Turn round before the end of the ledge.
                    if (v.RelX < 8f * sc && v.Dir < 0 || v.RelX > width - 8f * sc && v.Dir > 0) v.Dir = -v.Dir;
                    v.X = s.Left + v.RelX;
                    int col = s.Col(v.X);
                    if (s.Vis[col] == 0) { FlyOff(v); continue; }
                    baseY = s.PileTop(col) + 1f;
                }
                StepPerched(v, dt, sc);
                v.Y = baseY - v.Lift;
                continue;
            }

            // Flying: robins bound along in swoops, ladybirds buzz.
            if (v.State == 0 && cursorNear) { FlyOff(v); continue; }
            if (v.State == 0)
            {
                if (v.OnSnowman is Snowman sm)
                {
                    if (sm.Falling || sm.Melting || !Snowmen.Contains(sm)) { if (!PickPerch(v)) { FlyOff(v); continue; } }
                    else { v.TX = sm.X; v.TY = sm.Y - sm.Height * 0.97f; }
                }
                else if (PerchSurface(v) is Surface s && s.Cells > 0)
                {
                    v.TX = s.Left + v.RelX;
                    int col = s.Col(v.TX);
                    if (s.Vis[col] == 0 || s.Shield[col] != 0) { if (!PickPerch(v)) { FlyOff(v); continue; } }
                    else v.TY = s.PileTop(col);
                }
                else if (!PickPerch(v)) { FlyOff(v); continue; }
            }
            float dx = v.TX - v.X, dy = v.TY - v.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy) + 0.01f;
            bool robin = v.Kind == VisitorKind.Robin;
            float speed = (robin ? 260f : 110f) * sc * (v.State == 2 ? 1.3f : 1f);
            float wantX = dx / d * speed, wantY = dy / d * speed;
            if (robin) wantY += MathF.Sin(v.Age * 8f) * 90f * sc;           // bounding flight
            else { wantX += MathF.Sin(v.Age * 13f) * 25f; wantY += MathF.Cos(v.Age * 11f) * 25f; }
            if (d < 50f * sc) { wantX = dx * 4f; wantY = dy * 4f; }           // settle gently onto the perch
            v.VX += (wantX - v.VX) * MathF.Min(1f, dt * 5f);
            v.VY += (wantY - v.VY) * MathF.Min(1f, dt * 5f);
            v.X += v.VX * dt;
            v.Y += v.VY * dt;
            if (MathF.Abs(v.VX) > 5f) v.Dir = v.VX > 0 ? 1 : -1;

            if (v.State == 2)
            {
                if (v.X < -80 || v.X > ScreenW + 80 || v.Y < -80) Visitors.RemoveAt(i);
                continue;
            }
            if (d < 3f * sc)
            {
                v.State = 1;
                v.VX = v.VY = 0;
                v.Action = 0;
                v.Timer = R(0.4f, 1.2f);
                if (v.OnSnowman != null) v.RelX = 0;
            }
        }
    }

    /// <summary>Little routines on the ledge: robins hop, look about and peck; ladybirds crawl and pause.</summary>
    void StepPerched(Visitor v, float dt, float sc)
    {
        bool robin = v.Kind == VisitorKind.Robin;
        bool onSnowman = v.OnSnowman != null;
        v.Timer -= dt;
        switch (v.Action)
        {
            case 1 when robin:
                v.HopT += dt / 0.26f;
                if (!onSnowman) v.RelX += v.Dir * R(30f, 60f) * sc * dt;
                v.Lift = MathF.Sin(MathF.PI * MathF.Min(v.HopT, 1f)) * 5f * sc;
                if (v.HopT >= 1f) { v.Action = 0; v.Lift = 0; v.Timer = R(0.2f, 0.9f); }
                return;
            case 1:
                if (!onSnowman) v.RelX += v.Dir * 11f * sc * dt;
                break;
        }
        if (v.Timer > 0) return;
        float roll = R();
        if (robin)
        {
            if (roll < 0.2f) v.Dir = -v.Dir;
            if (roll < 0.55f && !onSnowman) { v.Action = 1; v.HopT = 0; }
            else if (roll < 0.8f) { v.Action = 2; v.Timer = R(0.25f, 0.6f); }  // peck
            else { v.Action = 0; v.Timer = R(0.6f, 2f); }
        }
        else
        {
            if (roll < 0.15f) v.Dir = -v.Dir;
            if (roll < 0.7f) { v.Action = 1; v.Timer = R(1.5f, 5f); }        // crawl
            else if (roll < 0.85f) { v.Action = 2; v.Timer = R(0.4f, 0.9f); } // flutter its wing cases
            else { v.Action = 0; v.Timer = R(0.8f, 3f); }
        }
    }

    void AutoVisitors(float dt)
    {
        _visitorTimer -= dt;
        if (_visitorTimer > 0) return;
        _visitorTimer = R(20f, 60f);
        if (!_visitorsOn) return;
        bool daytime = IsDay;
        int have = Visitors.Count(v => v.State != 2);
        if (have >= 2) return;
        if (Season is Season.Winter or Season.Autumn) AddVisitor(VisitorKind.Robin);
        else if (daytime && RainLevel < 0.1f) AddVisitor(VisitorKind.Ladybird);
    }
}
