using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Weather;

/// <summary>Colours, fonts and window chrome for the app's dark UI.</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(22, 24, 30);
    public static readonly Color Sidebar = Color.FromArgb(16, 18, 23);
    public static readonly Color Card = Color.FromArgb(31, 34, 42);
    public static readonly Color CardHover = Color.FromArgb(37, 40, 49);
    public static readonly Color Line = Color.FromArgb(45, 49, 60);
    public static readonly Color Text = Color.FromArgb(236, 238, 243);
    public static readonly Color Sub = Color.FromArgb(146, 153, 168);
    public static readonly Color Faint = Color.FromArgb(96, 102, 118);
    public static readonly Color Track = Color.FromArgb(58, 63, 76);

    static readonly string Family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    static readonly string Display = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI Semibold";

    public static Font Font(float pt, FontStyle st = FontStyle.Regular) => new(Family, pt, st);
    public static Font Heading(float pt) => new(Display, pt, FontStyle.Bold);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string? app, string? id);

    /// <summary>Dark title bar and rounded corners (Windows 11), dark scrollbars.</summary>
    public static void DarkChrome(Control c, Color? caption = null)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(c.Handle, 20, ref on, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
            int round = 2;
            DwmSetWindowAttribute(c.Handle, 33, ref round, 4);           // DWMWA_WINDOW_CORNER_PREFERENCE: round
            if (caption is Color cc)
            {
                int bgr = cc.R | (cc.G << 8) | (cc.B << 16);
                DwmSetWindowAttribute(c.Handle, 35, ref bgr, 4);         // DWMWA_CAPTION_COLOR
            }
        }
        catch { /* older Windows: fine without */ }
    }

    public static void DarkScrollbars(Control c)
    {
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    public static GraphicsPath Round(RectangleF r, float rad) => Sprites.RoundRect(r.X, r.Y, r.Width, r.Height, MathF.Max(0.5f, MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2)));

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }
}

/// <summary>Base for the custom controls: double-buffered, transparent-looking, DPI helpers.</summary>
internal abstract class KitControl : Control
{
    protected KitControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected float K => DeviceDpi / 96f;
    protected bool Hover;
    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }

    public Color Accent { get => _accent; set { _accent = value; Invalidate(); } }
    Color _accent = Color.FromArgb(120, 182, 255);
}

/// <summary>An on/off switch with a sliding knob.</summary>
internal sealed class ToggleSwitch : KitControl
{
    bool _on;
    float _pos;
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    public event EventHandler? Changed;

    public ToggleSwitch()
    {
        _anim.Tick += (_, _) =>
        {
            float target = _on ? 1f : 0f;
            _pos += (target - _pos) * 0.35f;
            if (MathF.Abs(target - _pos) < 0.01f) { _pos = target; _anim.Stop(); }
            Invalidate();
        };
        TabStop = true;
    }

    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; _anim.Start(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void SetInitial(bool on) { _on = on; _pos = on ? 1 : 0; Invalidate(); }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) Checked = !Checked; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        var r = new RectangleF(1 * k, (Height - 22 * k) / 2, 42 * k, 22 * k);
        var off = Hover ? Color.FromArgb(74, 80, 96) : Theme.Track;
        using (var p = Theme.Round(r, r.Height / 2))
        using (var b = new SolidBrush(Theme.Mix(off, Accent, _pos)))
            g.FillPath(b, p);
        if (Focused)
        {
            using var fp = Theme.Round(RectangleF.Inflate(r, 2.5f * k, 2.5f * k), r.Height);
            using var pen = new Pen(Color.FromArgb(160, Accent), 1.2f * k);
            g.DrawPath(pen, fp);
        }
        float d = 16 * k;
        float x = r.X + 3 * k + (r.Width - d - 6 * k) * _pos;
        using var knobShadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.FillEllipse(knobShadow, x, r.Y + 3 * k + 1 * k, d, d);
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, r.Y + 3 * k, d, d);
    }

    protected override void Dispose(bool disposing) { if (disposing) _anim.Dispose(); base.Dispose(disposing); }
}

/// <summary>A slim slider with a filled track and the value shown beside it.</summary>
internal sealed class Slider : KitControl
{
    int _min, _max = 100, _value;
    bool _drag;
    public Func<int, string> Format = v => v.ToString();
    public event EventHandler? Changed;

    public Slider(int min, int max, int value) { _min = min; _max = max; _value = Math.Clamp(value, min, max); TabStop = true; }

    public int Value
    {
        get => _value;
        set { value = Math.Clamp(value, _min, _max); if (value == _value) return; _value = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    float LabelW => 54 * K;
    RectangleF TrackRect => new(9 * K, Height / 2f - 2 * K, Width - LabelW - 18 * K, 4 * K);

    void SetFromX(int x)
    {
        var t = TrackRect;
        float f = Math.Clamp((x - t.X) / t.Width, 0f, 1f);
        Value = (int)MathF.Round(_min + f * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Focus(); SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value += Math.Sign(e.Delta) * Math.Max(1, (_max - _min) / 50); base.OnMouseWheel(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Math.Max(1, (_max - _min) / 50);
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= step;
        if (e.KeyCode is Keys.Right or Keys.Up) Value += step;
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        var t = TrackRect;
        float f = _max == _min ? 0 : (_value - _min) / (float)(_max - _min);
        using (var p = Theme.Round(t, t.Height / 2)) using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
        // Sliders that run both ways (wind) fill from zero.
        float zero = _min < 0 && _max > 0 ? -_min / (float)(_max - _min) : 0f;
        float a = MathF.Min(zero, f), bnd = MathF.Max(zero, f);
        var fill = new RectangleF(t.X + t.Width * a, t.Y, MathF.Max(t.Width * (bnd - a), 0.5f), t.Height);
        using (var p = Theme.Round(fill, t.Height / 2)) using (var b = new SolidBrush(Accent)) g.FillPath(b, p);
        float d = (Hover || _drag ? 18 : 16) * k;
        float cx = t.X + t.Width * f;
        if (Focused)
        {
            using var ring = new SolidBrush(Color.FromArgb(70, Accent));
            g.FillEllipse(ring, cx - d / 2 - 4 * k, Height / 2f - d / 2 - 4 * k, d + 8 * k, d + 8 * k);
        }
        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        g.FillEllipse(shadow, cx - d / 2, Height / 2f - d / 2 + 1 * k, d, d);
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, cx - d / 2, Height / 2f - d / 2, d, d);
        using var inner = new SolidBrush(Accent);
        g.FillEllipse(inner, cx - d * 0.2f, Height / 2f - d * 0.2f, d * 0.4f, d * 0.4f);
        using var font = Theme.Font(9.5f);
        using var tb = new SolidBrush(Theme.Text);
        var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        g.DrawString(Format(_value), font, tb, new RectangleF(Width - LabelW, 0, LabelW, Height), sf);
    }
}

/// <summary>A row of mutually exclusive options in one pill.</summary>
internal sealed class Segmented : KitControl
{
    readonly string[] _items;
    int _sel, _hover = -1;
    public Func<int, Color>? AccentFor;
    public event EventHandler? Changed;

    public Segmented(string[] items, int selected) { _items = items; _sel = selected; TabStop = true; }

    public int Selected { get => _sel; set { if (value == _sel) return; _sel = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); } }

    int At(int x) => Math.Clamp((int)(x / (Width / (float)_items.Length)), 0, _items.Length - 1);
    protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.X); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Selected = At(e.X); Focus(); base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Selected = Math.Max(0, _sel - 1);
        if (e.KeyCode == Keys.Right) Selected = Math.Min(_items.Length - 1, _sel + 1);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Theme.Round(r, 9 * k)) using (var b = new SolidBrush(Color.FromArgb(24, 26, 33))) g.FillPath(b, p);
        using (var p = Theme.Round(r, 9 * k)) using (var pen = new Pen(Theme.Line, 1f)) g.DrawPath(pen, p);
        float w = Width / (float)_items.Length;
        using var font = Theme.Font(9.5f, FontStyle.Regular);
        using var bold = Theme.Font(9.5f, FontStyle.Bold);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < _items.Length; i++)
        {
            var cell = new RectangleF(i * w + 3 * k, 3 * k, w - 6 * k, Height - 6 * k);
            var acc = AccentFor?.Invoke(i) ?? Accent;
            if (i == _sel)
            {
                using var p = Theme.Round(cell, 7 * k);
                using var b = new LinearGradientBrush(cell, Theme.Mix(acc, Color.White, 0.12f), Theme.Mix(acc, Color.Black, 0.12f), 90f);
                g.FillPath(b, p);
            }
            else if (i == _hover)
            {
                using var p = Theme.Round(cell, 7 * k);
                using var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                g.FillPath(b, p);
            }
            using var tb = new SolidBrush(i == _sel ? Color.FromArgb(20, 22, 28) : Theme.Text);
            g.DrawString(_items[i], i == _sel ? bold : font, tb, cell, sf);
        }
        if (Focused)
        {
            using var p = Theme.Round(RectangleF.Inflate(r, -1, -1), 9 * k);
            using var pen = new Pen(Color.FromArgb(150, Accent), 1.2f * k);
            g.DrawPath(pen, p);
        }
    }
}

/// <summary>A rounded button; Primary ones are filled with the accent colour.</summary>
internal sealed class PillButton : KitControl
{
    public bool Primary;
    bool _down;
    public PillButton(string text, bool primary = false) { Text = text; Primary = primary; TabStop = true; }

    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    public int PreferredWidth()
    {
        using var g = CreateGraphics();
        using var f = Theme.Font(9.5f, FontStyle.Bold);
        return (int)(g.MeasureString(Text, f).Width + 34 * K);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        if (_down) r.Inflate(-1, -1);
        using var p = Theme.Round(r, r.Height / 2);
        if (Primary)
        {
            using var b = new LinearGradientBrush(r, Theme.Mix(Accent, Color.White, Hover ? 0.22f : 0.12f), Theme.Mix(Accent, Color.Black, 0.1f), 90f);
            g.FillPath(b, p);
        }
        else
        {
            using var b = new SolidBrush(Hover ? Color.FromArgb(52, 57, 70) : Color.FromArgb(40, 44, 54));
            g.FillPath(b, p);
            using var pen = new Pen(Theme.Line, 1f);
            g.DrawPath(pen, p);
        }
        if (Focused)
        {
            using var fp = Theme.Round(RectangleF.Inflate(r, -2 * k, -2 * k), r.Height / 2);
            using var pen = new Pen(Color.FromArgb(180, Primary ? Color.White : Accent), 1.2f * k);
            g.DrawPath(pen, fp);
        }
        using var f = Theme.Font(9.5f, FontStyle.Bold);
        using var tb = new SolidBrush(Primary ? Color.FromArgb(18, 20, 26) : Theme.Text);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Text, f, tb, r, sf);
    }
}

/// <summary>A sidebar entry: icon and label, highlighted when selected.</summary>
internal sealed class NavItem : KitControl
{
    public Image? Icon;
    bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    public NavItem(string text, Image? icon) { Text = text; Icon = icon; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        var r = new RectangleF(8 * k, 2 * k, Width - 16 * k, Height - 4 * k);
        if (_selected || Hover)
        {
            using var p = Theme.Round(r, 8 * k);
            using var b = new SolidBrush(_selected ? Color.FromArgb(38, 42, 52) : Color.FromArgb(28, 31, 38));
            g.FillPath(b, p);
        }
        if (_selected)
        {
            using var p = Theme.Round(new RectangleF(r.X, r.Y + r.Height * 0.28f, 3 * k, r.Height * 0.44f), 1.5f * k);
            using var b = new SolidBrush(Accent);
            g.FillPath(b, p);
        }
        float icon = 20 * k;
        if (Icon != null) g.DrawImage(Icon, r.X + 12 * k, r.Y + (r.Height - icon) / 2, icon, icon);
        using var f = Theme.Font(10f, _selected ? FontStyle.Bold : FontStyle.Regular);
        using var tb = new SolidBrush(_selected ? Theme.Text : Color.FromArgb(200, 205, 216));
        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(Text, f, tb, new RectangleF(r.X + 42 * k, r.Y, r.Width - 42 * k, r.Height), sf);
    }
}

/// <summary>
/// A setting: title and description on the left, its control on the right. Rows are stacked
/// inside a <see cref="Card"/>.
/// </summary>
internal sealed class Row : Control
{
    public readonly string Title, Detail;
    public readonly Control? Ctrl;
    public int CtrlWidth;

    public Row(string title, string detail, Control? ctrl, int ctrlWidth)
    {
        Title = title; Detail = detail; Ctrl = ctrl; CtrlWidth = ctrlWidth;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        if (ctrl != null) Controls.Add(ctrl);
    }

    float K => DeviceDpi / 96f;

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Ctrl == null) return;
        float k = K;
        int w = (int)(CtrlWidth * k), h = Ctrl is ToggleSwitch ? (int)(26 * k) : Ctrl is Slider ? (int)(30 * k) : (int)(32 * k);
        Ctrl.SetBounds(Width - w - (int)(18 * k), (Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        float textW = Width - (Ctrl == null ? 36 : CtrlWidth + 48) * k;
        using var tf = Theme.Font(10.5f);
        using var df = Theme.Font(9f);
        using var tb = new SolidBrush(Theme.Text);
        using var db = new SolidBrush(Theme.Sub);
        float th = g.MeasureString(Title, tf).Height;
        float dh = string.IsNullOrEmpty(Detail) ? 0 : g.MeasureString(Detail, df, (int)textW).Height;
        float y = (Height - th - dh) / 2;
        g.DrawString(Title, tf, tb, 18 * k, y);
        if (dh > 0) g.DrawString(Detail, df, db, new RectangleF(18 * k, y + th, textW, dh + 2));
    }
}

/// <summary>A rounded panel stacking <see cref="Row"/>s with hairlines between them.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Padding = Padding.Empty;
    }

    float K => DeviceDpi / 96f;

    public void Stack(int width)
    {
        float k = K;
        int y = (int)(4 * k); // rows inset so their square corners stay inside the rounded card
        foreach (Control c in Controls)
        {
            int h = c is Row row ? (int)((row.Detail.Length > 60 ? 74 : string.IsNullOrEmpty(row.Detail) ? 52 : 64) * k) : c.Height;
            c.SetBounds((int)(4 * k), y, width - (int)(8 * k), h);
            y += h + 1;
        }
        Size = new Size(width, y + (int)(3 * k));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = K;
        using var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10 * k);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, p);
        using var pen = new Pen(Theme.Line, 1f);
        g.DrawPath(pen, p);
        // Separators between rows
        using var sep = new Pen(Theme.Line, 1f);
        for (int i = 1; i < Controls.Count; i++)
        {
            int y = Controls[i].Top - 1;
            g.DrawLine(sep, 18 * k, y, Width - 18 * k, y);
        }
    }

}

/// <summary>A small caption above a card.</summary>
internal sealed class Caption : Control
{
    public Caption(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = DeviceDpi / 96f;
        using var f = Theme.Font(9f, FontStyle.Bold);
        using var b = new SolidBrush(Theme.Sub);
        var sf = new StringFormat { LineAlignment = StringAlignment.Far };
        g.DrawString(Text.ToUpperInvariant(), f, b, new RectangleF(4 * k, 0, Width, Height - 6 * k), sf);
    }
}

/// <summary>The banner at the top of a page: the season's sky, its glyph, a title and a line of text.</summary>
internal sealed class Hero : Control
{
    readonly Season? _season;
    readonly Image? _art;
    readonly string _title, _line;
    public string Badge = "";

    public Hero(Season? season, string title, string line, Image? art = null)
    {
        _season = season; _title = title; _line = line; _art = art;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var (top, bottom) = _season is Season s ? SeasonInfo.Sky(s) : (Color.FromArgb(40, 46, 74), Color.FromArgb(92, 70, 120));
        using var path = Theme.Round(r, 14 * k);
        using (var b = new LinearGradientBrush(r, top, bottom, 20f)) g.FillPath(b, path);
        g.SetClip(path);
        // A few soft circles of light for depth.
        using (var glow = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
        {
            g.FillEllipse(glow, Width * 0.62f, -Height * 0.6f, Height * 1.6f, Height * 1.6f);
            g.FillEllipse(glow, Width * 0.78f, Height * 0.3f, Height * 1.0f, Height * 1.0f);
        }
        float art = Height * 0.62f;
        var img = _art ?? (_season is Season ss ? Sprites.SeasonGlyph(ss, (int)art, shadow: true) : null);
        if (img != null) g.DrawImage(img, Width - art - 30 * k, (Height - art) / 2, art, art);
        if (_art == null) img?.Dispose();
        g.ResetClip();
        using var tf = Theme.Heading(20f);
        using var lf = Theme.Font(10f);
        using var tb = new SolidBrush(Color.White);
        using var lb = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        float y = Height / 2f - 26 * k;
        g.DrawString(_title, tf, tb, 26 * k, y);
        g.DrawString(_line, lf, lb, new RectangleF(28 * k, y + 38 * k, Width - art - 90 * k, 40 * k));
        if (Badge.Length > 0)
        {
            using var bf = Theme.Font(8.5f, FontStyle.Bold);
            var sz = g.MeasureString(Badge, bf);
            float tw = g.MeasureString(_title, tf).Width;
            var br = new RectangleF(26 * k + tw + 10 * k, y + 10 * k, sz.Width + 14 * k, sz.Height + 4 * k);
            using var bp = Theme.Round(br, br.Height / 2);
            using var bb = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            g.FillPath(bb, bp);
            g.DrawString(Badge, bf, tb, br.X + 7 * k, br.Y + 2 * k);
        }
    }
}

/// <summary>A card with a hint about mouse play (keyboard-key chips and text).</summary>
internal sealed class Tip : Control
{
    readonly (string keys, string text)[] _lines;
    public Color Accent = Color.White;

    public Tip(params (string keys, string text)[] lines)
    {
        _lines = lines;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    public int Measure(float k) => (int)((22 + _lines.Length * 30) * k);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        float k = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Theme.Round(r, 10 * k))
        {
            using var b = new SolidBrush(Theme.Mix(Theme.Card, Accent, 0.08f));
            g.FillPath(b, p);
            using var pen = new Pen(Theme.Mix(Theme.Line, Accent, 0.35f), 1f);
            g.DrawPath(pen, p);
        }
        using var kf = Theme.Font(8.5f, FontStyle.Bold);
        using var tf = Theme.Font(9.5f);
        using var kb = new SolidBrush(Theme.Text);
        using var tb = new SolidBrush(Color.FromArgb(214, 218, 228));
        float y = 11 * k;
        foreach (var (keys, text) in _lines)
        {
            float x = 16 * k;
            foreach (var key in keys.Split('+'))
            {
                var sz = g.MeasureString(key, kf);
                var kr = new RectangleF(x, y + 3 * k, sz.Width + 10 * k, 20 * k);
                using var kp = Theme.Round(kr, 5 * k);
                using var kbg = new SolidBrush(Color.FromArgb(52, 57, 70));
                g.FillPath(kbg, kp);
                using var kbd = new Pen(Color.FromArgb(78, 84, 100), 1f);
                g.DrawPath(kbd, kp);
                g.DrawString(key, kf, kb, kr.X + 5 * k, kr.Y + (kr.Height - sz.Height) / 2);
                x = kr.Right + 4 * k;
            }
            g.DrawString(text, tf, tb, new RectangleF(MathF.Max(x + 8 * k, 190 * k), y + 3 * k, Width - 200 * k, 24 * k),
                         new StringFormat { LineAlignment = StringAlignment.Center });
            y += 30 * k;
        }
    }
}
