namespace Weather;

/// <summary>
/// First run: a small card above the tray saying hello, where the app lives and how to play.
/// Fades in, and away again after a while if left alone.
/// </summary>
internal sealed class WelcomeCard : Form
{
    readonly Season _season;
    readonly float _k;
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer _life = new() { Interval = 30_000 };
    bool _closing;
    public event Action? OpenSettings;

    public WelcomeCard(Season season)
    {
        _season = season;
        AutoScaleMode = AutoScaleMode.None;
        _k = DeviceDpi / 96f;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        Size = new Size(S(400), S(214));
        var work = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(work.Right - Width - S(16), work.Bottom - Height - S(16));
        Opacity = 0;
        DoubleBuffered = true;

        var open = new PillButton("Show me around", primary: true) { Accent = SeasonInfo.Accent(season) };
        open.SetBounds(Width - S(170) - S(20), Height - S(56), S(170), S(36));
        open.Click += (_, _) => { OpenSettings?.Invoke(); FadeOut(); };
        var ok = new PillButton("Got it") { Accent = SeasonInfo.Accent(season) };
        ok.SetBounds(open.Left - S(100) - S(10), Height - S(56), S(100), S(36));
        ok.Click += (_, _) => FadeOut();
        Controls.Add(open);
        Controls.Add(ok);

        _fade.Tick += (_, _) =>
        {
            if (_closing)
            {
                Opacity = Math.Max(0, Opacity - 0.08);
                if (Opacity <= 0) { _fade.Stop(); Close(); }
            }
            else
            {
                Opacity = Math.Min(1, Opacity + 0.08);
                if (Opacity >= 1) _fade.Stop();
            }
        };
        _life.Tick += (_, _) => FadeOut();
        HandleCreated += (_, _) => Theme.DarkChrome(this);
        Shown += (_, _) => { _fade.Start(); _life.Start(); };
    }

    protected override bool ShowWithoutActivation => true;

    int S(float v) => (int)MathF.Round(v * _k);

    void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _life.Stop();
        _fade.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = _k;
        // A strip of the season's sky along the top.
        var (top, bottom) = SeasonInfo.Sky(_season);
        var band = new RectangleF(0, 0, Width, 74 * k);
        using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(band, top, bottom, 10f)) g.FillRectangle(b, band);
        using (var glyph = Sprites.SeasonGlyph(_season, S(44)))
            g.DrawImage(glyph, Width - S(66), S(15));
        using (var icon = Sprites.AppIcon(S(44)))
            g.DrawImage(icon, S(20), S(15));
        using var hf = Theme.Heading(14f);
        using var sf = Theme.Font(9f);
        using var tf = Theme.Font(9.5f);
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        using var text = new SolidBrush(Color.FromArgb(214, 218, 228));
        g.DrawString($"{AppInfo.Name} is on", hf, white, S(74), S(14));
        g.DrawString($"It's {SeasonInfo.Name(_season).ToLowerInvariant()}. Settings live in the tray, by the clock.", sf, soft,
                     new RectangleF(S(75), S(40), Width - S(150), S(32)));

        float y = 90 * k;
        void Line(string keys, string what)
        {
            float x = 20 * k;
            using var kf = Theme.Font(8.5f, FontStyle.Bold);
            foreach (var key in keys.Split('+'))
            {
                var sz = g.MeasureString(key, kf);
                var kr = new RectangleF(x, y, sz.Width + 10 * k, 20 * k);
                using var kp = Theme.Round(kr, 5 * k);
                using var kb = new SolidBrush(Color.FromArgb(52, 57, 70));
                g.FillPath(kb, kp);
                g.DrawString(key, kf, white, kr.X + 5 * k, kr.Y + (kr.Height - sz.Height) / 2);
                x = kr.Right + 4 * k;
            }
            g.DrawString(what, tf, text, new RectangleF(x + 8 * k, y, Width - x - 28 * k, 22 * k),
                         new StringFormat { LineAlignment = StringAlignment.Center });
            y += 28 * k;
        }
        Line("Ctrl+Alt+Drag", _season switch
        {
            Season.Winter => "scoop and throw a snowball",
            Season.Spring => "sweep through the petals",
            Season.Summer => "blow a dandelion clock",
            _ => "kick up the leaves",
        });
        Line("Ctrl+Alt+Right-click", _season switch
        {
            Season.Winter => "build a snowman",
            Season.Spring => "call a spring shower",
            Season.Summer => "release a butterfly",
            _ => "whip up a dust devil",
        });
        using var pen = new Pen(Theme.Line);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fade.Dispose(); _life.Dispose(); }
        base.Dispose(disposing);
    }
}
