namespace Windowsill;

/// <summary>
/// A small dark bubble that appears next to the pointer for a few seconds (how to play), then fades.
/// Never takes focus and is click-through-free: it simply goes away.
/// </summary>
internal sealed class TipBubble : Form
{
    readonly string _title, _text;
    readonly Color _accent;
    readonly float _k;
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer _life = new() { Interval = 7000 };
    bool _closing;

    public TipBubble(string title, string text, Color accent, Point near)
    {
        _title = title; _text = text; _accent = accent;
        AutoScaleMode = AutoScaleMode.None;
        _k = DeviceDpi / 96f;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        Text = AppInfo.Name; // so the snow treats it as one of ours (it stays behind it)
        Size = new Size(S(330), S(78));
        var wa = Screen.FromPoint(near).WorkingArea;
        int x = Math.Clamp(near.X + S(18), wa.Left + 8, wa.Right - Width - 8);
        int y = near.Y - Height - S(14) < wa.Top ? near.Y + S(22) : near.Y - Height - S(14);
        Location = new Point(x, Math.Clamp(y, wa.Top + 8, wa.Bottom - Height - 8));
        Opacity = 0;
        DoubleBuffered = true;
        _fade.Tick += (_, _) =>
        {
            Opacity = _closing ? Math.Max(0, Opacity - 0.07) : Math.Min(1, Opacity + 0.1);
            if (_closing && Opacity <= 0) { _fade.Stop(); Close(); }
            else if (!_closing && Opacity >= 1) _fade.Stop();
        };
        _life.Tick += (_, _) => { _life.Stop(); _closing = true; _fade.Start(); };
        Click += (_, _) => { _closing = true; _fade.Start(); };
        HandleCreated += (_, _) => Theme.DarkChrome(this);
        Shown += (_, _) => { _fade.Start(); _life.Start(); };
    }

    protected override bool ShowWithoutActivation => true;
    int S(float v) => (int)MathF.Round(v * _k);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        using (var bar = new SolidBrush(_accent)) g.FillRectangle(bar, 0, 0, S(4), Height);
        using var hf = Theme.Font(10.5f, FontStyle.Bold);
        using var tf = Theme.Font(9.5f);
        using var white = new SolidBrush(Color.White);
        using var text = new SolidBrush(Color.FromArgb(214, 218, 228));
        g.DrawString(_title, hf, white, S(16), S(10));
        g.DrawString(_text, tf, text, new RectangleF(S(16), S(32), Width - S(28), Height - S(36)));
        using var pen = new Pen(Theme.Line);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fade.Dispose(); _life.Dispose(); }
        base.Dispose(disposing);
    }
}
