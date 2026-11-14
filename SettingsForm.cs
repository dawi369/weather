namespace Weather;

/// <summary>What the settings window can ask of the running app.</summary>
internal interface IAppHost
{
    Season CurrentSeason { get; }
    void Request(SeasonAction a);
    void ClearNow();
    bool StartsWithWindows { get; set; }
    Task CheckForUpdatesAsync(bool interactive);
    void OpenDiagnostics();
}

/// <summary>
/// The settings window: a sidebar (General, the four seasons, About) and a page of cards. Every
/// change applies to the running weather at once and is saved a moment later.
/// </summary>
internal sealed class SettingsForm : Form
{
    Settings _s; // swapped for the defaults for a moment when building sliders (see Slide)
    static readonly Settings Defaults = new();
    readonly IAppHost _host;
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 400 };
    readonly Panel _side, _content;
    readonly List<NavItem> _nav = new();
    readonly float _k;
    int _page;

    static readonly string[] Pages = { "General", "Winter", "Spring", "Summer", "Autumn", "About" };
    static readonly Color AboutAccent = Color.FromArgb(160, 140, 255);

    public SettingsForm(Settings settings, IAppHost host, int page = 0)
    {
        _s = settings;
        _host = host;
        AutoScaleMode = AutoScaleMode.None;
        _k = DeviceDpi / 96f;
        Text = $"{AppInfo.Name} settings";
        Icon = AppInfo.Icon;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f);
        ClientSize = new Size(S(960), S(680));
        MinimumSize = new Size(S(760), S(480));
        KeyPreview = true;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _s.Save(); };

        _side = new Panel { Dock = DockStyle.Left, Width = S(232), BackColor = Theme.Sidebar };
        _side.Paint += PaintSidebar;
        _content = new DoubleBufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
        _content.Resize += (_, _) => StackPage(_content);
        Controls.Add(_content);
        Controls.Add(_side);

        int y = S(104);
        for (int i = 0; i < Pages.Length; i++)
        {
            Image? icon = i is >= 1 and <= 4 ? Sprites.SeasonGlyph((Season)(i - 1), S(20), shadow: false)
                        : i == 0 ? GearIcon(S(20)) : InfoIcon(S(20));
            var item = new NavItem(Pages[i], icon) { Bounds = new Rectangle(0, y, _side.Width, S(42)), Accent = PageAccent(i) };
            int idx = i;
            item.Click += (_, _) => ShowPage(idx);
            _side.Controls.Add(item);
            _nav.Add(item);
            y += S(44);
        }

        var pause = new PillButton(_s.Paused ? "Resume the weather" : "Pause the weather") { Accent = PageAccent(0) };
        pause.Bounds = new Rectangle(S(20), 0, _side.Width - S(40), S(36));
        pause.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
        pause.Click += (_, _) => { _s.Paused = !_s.Paused; pause.Text = _s.Paused ? "Resume the weather" : "Pause the weather"; };
        _side.Controls.Add(pause);
        _side.Resize += (_, _) => pause.Top = _side.Height - S(62);
        pause.Top = ClientSize.Height - S(62);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.Control && e.KeyCode == Keys.Tab) ShowPage((_page + (e.Shift ? Pages.Length - 1 : 1)) % Pages.Length);
        };
        FormClosed += (_, _) => { _saveTimer.Stop(); _s.Save(); };
        HandleCreated += (_, _) => { Theme.DarkChrome(this, Theme.Sidebar); Theme.DarkScrollbars(_content); };
        ShowPage(page);
    }

    int S(float v) => (int)MathF.Round(v * _k);
    void Changed() { _saveTimer.Stop(); _saveTimer.Start(); }

    Color PageAccent(int i) => i switch
    {
        0 => SeasonInfo.Accent(_host.CurrentSeason),
        5 => AboutAccent,
        _ => SeasonInfo.Accent((Season)(i - 1)),
    };

    void PaintSidebar(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        using var icon = Sprites.AppIcon(S(40));
        g.DrawImage(icon, S(22), S(30));
        using var nf = Theme.Heading(14f);
        using var sf = Theme.Font(8.5f);
        using var nb = new SolidBrush(Theme.Text);
        using var sb = new SolidBrush(Theme.Sub);
        g.DrawString(AppInfo.Name, nf, nb, S(70), S(28));
        g.DrawString("Seasons for your desktop", sf, sb, S(71), S(54));
    }

    /// <summary>Switch to a page: its cards are built fresh (so they show the current state).</summary>
    public void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < _nav.Count; i++) { _nav[i].Selected = i == page; _nav[i].Accent = PageAccent(i); }
        _content.SuspendLayout();
        foreach (Control c in _content.Controls.Cast<Control>().ToList()) { _content.Controls.Remove(c); c.Dispose(); }
        _content.AutoScrollPosition = Point.Empty;
        var accent = PageAccent(page);
        var items = page switch
        {
            0 => GeneralPage(accent),
            1 => SeasonPage(Season.Winter, accent),
            2 => SeasonPage(Season.Spring, accent),
            3 => SeasonPage(Season.Summer, accent),
            4 => SeasonPage(Season.Autumn, accent),
            _ => AboutPage(accent),
        };
        foreach (var c in items)
        {
            Tint(c, accent);
            _content.Controls.Add(c);
        }
        _content.ResumeLayout();
        StackPage(_content);
    }

    static void Tint(Control c, Color accent)
    {
        if (c is KitControl kc) kc.Accent = accent;
        if (c is Tip t) t.Accent = accent;
        foreach (Control child in c.Controls) Tint(child, accent);
    }

    /// <summary>Stack the page's pieces down the content area.</summary>
    void StackPage(Panel p)
    {
        if (p.Controls.Count == 0) return;
        int pad = S(30);
        // Always leave room for the scrollbar, so pages are the same width whether they scroll or not.
        int gutter = p.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
        int w = Math.Max(S(420), p.ClientSize.Width - gutter - pad * 2);
        int y = S(26) + p.AutoScrollPosition.Y;
        foreach (Control c in p.Controls)
        {
            switch (c)
            {
                case Hero: c.SetBounds(pad, y, w, S(132)); y += S(132) + S(18); break;
                case Caption: c.SetBounds(pad, y, w, S(30)); y += S(30); break;
                case Card card: card.Location = new Point(pad, y); card.Stack(w); y += card.Height + S(18); break;
                case Tip tip: tip.SetBounds(pad, y, w, tip.Measure(_k)); y += tip.Height + S(18); break;
                default: c.SetBounds(pad, y, w, c.Height); y += c.Height + S(12); break;
            }
        }
        p.AutoScrollMinSize = new Size(0, y - p.AutoScrollPosition.Y + S(10));
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    Row Toggle(string title, string detail, Func<bool> get, Action<bool> set)
    {
        var t = new ToggleSwitch();
        t.SetInitial(get());
        t.Changed += (_, _) => { set(t.Checked); Changed(); };
        return new Row(title, detail, t, 48);
    }

    Row Slide(string title, string detail, int min, int max, Func<int> get, Action<int> set, Func<int, string> fmt)
    {
        var saved = _s;
        _s = Defaults;
        int def = get();
        _s = saved;
        var s = new Slider(min, max, get()) { Format = fmt, Default = def };
        s.Changed += (_, _) => { set(s.Value); Changed(); };
        return new Row(title, detail, s, 280);
    }

    Row Button(string title, string detail, string text, Action click, bool primary = false)
    {
        var b = new PillButton(text, primary);
        b.Click += (_, _) => click();
        int w = Math.Max(110, (int)(b.PreferredWidth() / _k));
        return new Row(title, detail, b, w);
    }

    static Card Card(params Row?[] rows)
    {
        var c = new Card();
        foreach (var r in rows) if (r != null) c.Controls.Add(r);
        return c;
    }

    static string Pct(int v) => $"{v}%";

    // ───────────────────────────── pages ─────────────────────────────

    List<Control> GeneralPage(Color accent)
    {
        var now = _host.CurrentSeason;
        string nowName = SeasonInfo.Name(now).ToLowerInvariant();
        string autoLine = $"Automatic follows the calendar: it's {nowName} where you are.";
        var seasons = new Segmented(new[] { "Automatic", "Winter", "Spring", "Summer", "Autumn" }, (int)_s.Season)
        {
            AccentFor = i => i == 0 ? SeasonInfo.Accent(SeasonInfo.FromDate(DateTime.Now, _s.SouthernHemisphere)) : SeasonInfo.Accent((Season)(i - 1)),
        };
        seasons.Changed += (_, _) =>
        {
            _s.Season = (SeasonChoice)seasons.Selected;
            Changed();
            // Let the engine pick it up, then refresh the colours everywhere.
            BeginInvoke(async () => { await Task.Delay(120); ShowPage(0); });
        };
        var seasonRow = new Row("Season", autoLine, seasons, 430);

        return new List<Control>
        {
            new Hero(now, $"It's {nowName}", SeasonInfo.Tagline(now)) { Badge = _s.Season == SeasonChoice.Auto ? "By the calendar" : "Pinned" },
            new Caption("Season"),
            Card(seasonRow,
                 Toggle("Southern hemisphere", "Flip the calendar: winter in June, summer at Christmas.", () => _s.SouthernHemisphere, v => _s.SouthernHemisphere = v)),
            new Caption("Weather"),
            Card(Slide("Wind", "Which way it blows, and how hard.", -150, 150, () => _s.Wind, v => _s.Wind = v,
                       v => v == 0 ? "calm" : v < 0 ? $"{-v} left" : $"{v} right"),
                 Slide("Gusts", "How much the wind comes and goes.", 0, 100, () => _s.Gusts, v => _s.Gusts = v, Pct),
                 Slide("Size", "Flakes, petals, leaves and seeds.", 40, 250, () => _s.FlakeSize, v => _s.FlakeSize = v, Pct),
                 Slide("Brightness", "How bold the falling things look.", 10, 100, () => _s.FlakeBrightness, v => _s.FlakeBrightness = v, Pct)),
            new Caption("Play"),
            Card(Toggle("Play with Ctrl+Alt and the mouse", "Hold Ctrl+Alt to throw snowballs, build snowmen, kick leaves and more. Without them, clicks pass through as normal.",
                        () => _s.PlayWithMouse, v => _s.PlayWithMouse = v)),
            new Caption("Taskbar"),
            Card(Toggle("Things settle on the taskbar", "Snow, petals and leaves pile up along the top of the taskbar.", () => _s.TaskbarSnow, v => _s.TaskbarSnow = v),
                 Slide("Height on the taskbar", "Nudge it if it doesn't sit right on your taskbar.", -120, 120, () => -_s.TaskbarSnowOffset, v => _s.TaskbarSnowOffset = -v,
                       v => v == 0 ? "auto" : v > 0 ? $"{v}px up" : $"{-v}px down"),
                 Slide("Clear everything every", "Start fresh now and then.", 0, 3600, () => _s.WipeSeconds, v => _s.WipeSeconds = v - v % 30,
                       v => v < 30 ? "never" : v < 60 ? $"{v - v % 30}s" : $"{v / 60} min"),
                 Button($"Clear the {SeasonInfo.Stuff(now)} now", "Sweep every window and the taskbar.", "Clear", _host.ClearNow)),
            new Caption("When to step aside"),
            Card(Toggle("When windows fill the screen", "A maximized window, or apps snapped side by side.", () => _s.HideWhenMaximized, v => _s.HideWhenMaximized = v),
                 Toggle("In full-screen apps and games", "Games, videos and presentations.", () => _s.HideInFullscreen, v => _s.HideInFullscreen = v),
                 Toggle("Battery saver", "On battery, run at 30 frames a second with a little less falling.", () => _s.BatterySaver, v => _s.BatterySaver = v),
                 Toggle("Start with Windows", "Begin the weather when you sign in.", () => _host.StartsWithWindows, v => _host.StartsWithWindows = v)),
        };
    }

    List<Control> SeasonPage(Season season, Color accent)
    {
        bool showing = _host.CurrentSeason == season;
        bool pinned = _s.Season == (SeasonChoice)((int)season + 1);
        string name = SeasonInfo.Name(season);
        var hero = new Hero(season, name, SeasonInfo.Tagline(season)) { Badge = showing ? "Showing now" : "Off-season" };
        var list = new List<Control> { hero };

        Row preview = !showing
            ? Button($"Preview {name}", "Show it now. Go back to following the calendar any time.", "Preview", () => SetChoice((SeasonChoice)((int)season + 1)), primary: true)
            : pinned
                ? Button($"Pinned to {name}", "You picked this season, so it stays whatever the date.", "Use calendar", () => SetChoice(SeasonChoice.Auto))
                : Button("Following the calendar", $"Pin {name.ToLowerInvariant()} to keep it, whatever the date.", "Pin", () => SetChoice((SeasonChoice)((int)season + 1)));

        switch (season)
        {
            case Season.Winter:
                list.Add(new Caption("Snow"));
                list.Add(Card(preview,
                    Slide("Snowfall", "How much snow falls.", 0, 300, () => _s.WinterAmount, v => _s.WinterAmount = v, v => v == 0 ? "off" : Pct(v)),
                    Slide("Buildup", "How quickly it piles up.", 25, 400, () => _s.BuildupSpeed, v => _s.BuildupSpeed = v, Pct),
                    Slide("Stickiness", "Runny powder, or packing snow that stays put.", 0, 100, () => _s.Stickiness, v => _s.Stickiness = v,
                          v => v < 25 ? "powdery" : v < 60 ? Pct(v) : "sticky"),
                    Slide("Deepest pile", "How deep snow can get on a window. Autumn's leaves share this.", 6, 200, () => _s.MaxDepth, v => _s.MaxDepth = v, v => $"{v}px"),
                    Slide("Fling", "How easily moving a window throws its snow.", 0, 100, () => _s.FlingSensitivity, v => _s.FlingSensitivity = v, Pct)));
                list.Add(new Caption("The snowplough"));
                list.Add(Card(
                    Toggle("Plough the taskbar when it gets deep", "A little snowplough drives along and pushes the snow off the edge. The same switch sends the leaf blower in autumn.", () => _s.AutoPlow, v => _s.AutoPlow = v),
                    showing ? Button("Send the snowplough", "Clear the taskbar now.", "Send", () => _host.Request(SeasonAction.Plow), primary: true) : null));
                list.Add(new Caption("Play"));
                list.Add(new Tip(("Ctrl+Alt+Drag", "Scoop snow off a ledge, roll it bigger, and throw it."),
                                 ("Ctrl+Alt+Click", "Throw it straight at the screen: splat."),
                                 ("Ctrl+Alt+Right-click", "Build a snowman from the snow there (or knock one over).")));
                break;

            case Season.Spring:
                list.Add(new Caption("Spring"));
                list.Add(Card(preview,
                    Slide("Blossom", "How many petals drift down.", 0, 300, () => _s.SpringAmount, v => _s.SpringAmount = v, v => v == 0 ? "off" : Pct(v)),
                    Toggle("Spring showers", "Every so often a shower passes over, with splashes on the ledges.", () => _s.Showers, v => _s.Showers = v),
                    showing ? Button("Bring a shower", "Or stop the one that's falling.", "Shower", () => _host.Request(SeasonAction.Shower), primary: true) : null));
                list.Add(new Caption("Life"));
                list.Add(Card(
                    Toggle("Butterflies", "They wander across and rest on your windows. Get close and they're off.", () => _s.Butterflies, v => _s.Butterflies = v),
                    Toggle("Meadow on the taskbar", "Fresh grass and spring flowers that sway in the wind. Summer shares this.", () => _s.Meadow, v => _s.Meadow = v),
                    showing ? Button("Release butterflies", "A few more, right now.", "Release", () => _host.Request(SeasonAction.Critters)) : null));
                list.Add(new Caption("Play"));
                list.Add(new Tip(("Ctrl+Alt+Drag", "Sweep through the petals and scatter them."),
                                 ("Ctrl+Alt+Right-click", "Start or stop a shower.")));
                break;

            case Season.Summer:
                list.Add(new Caption("Summer"));
                list.Add(Card(preview,
                    Slide("Dandelion seeds", "How many drift by on the breeze.", 0, 300, () => _s.SummerAmount, v => _s.SummerAmount = v, v => v == 0 ? "off" : Pct(v)),
                    Toggle("Meadow on the taskbar", "Tall grass with poppies, cornflowers and daisies. Spring shares this.", () => _s.Meadow, v => _s.Meadow = v)));
                list.Add(new Caption("Life"));
                list.Add(Card(
                    Toggle("Butterflies by day", "They rest on your windows and flit off if the mouse comes near.", () => _s.Butterflies, v => _s.Butterflies = v),
                    Toggle("Fireflies after dusk", "Hold the mouse still and they gather round it.", () => _s.Fireflies, v => _s.Fireflies = v),
                    showing ? Button("Call them now", "Butterflies by day, fireflies at night.", "Call", () => _host.Request(SeasonAction.Critters), primary: true) : null));
                list.Add(new Caption("Play"));
                list.Add(new Tip(("Ctrl+Alt+Drag", "Blow a dandelion clock: seeds float off the mouse."),
                                 ("Ctrl+Alt+Right-click", "Release a butterfly (or a handful of fireflies at night).")));
                break;

            default:
                list.Add(new Caption("Leaves"));
                list.Add(Card(preview,
                    Slide("Falling leaves", "How many come down.", 0, 300, () => _s.AutumnAmount, v => _s.AutumnAmount = v, v => v == 0 ? "off" : Pct(v)),
                    Slide("Deepest pile", "How deep leaves can pile. Shared with winter's snow.", 6, 200, () => _s.MaxDepth, v => _s.MaxDepth = v, v => $"{v}px"),
                    Toggle("Dust devils", "Now and then a little whirlwind spins the leaves up off the taskbar.", () => _s.DustDevils, v => _s.DustDevils = v)));
                list.Add(new Caption("The leaf blower"));
                list.Add(Card(
                    Toggle("Clear the taskbar when it gets deep", "A leaf blower trundles along and sends them flying. The same switch sends the snowplough in winter.", () => _s.AutoPlow, v => _s.AutoPlow = v),
                    showing ? Button("Send the leaf blower", "Clear the taskbar now.", "Send", () => _host.Request(SeasonAction.Plow), primary: true) : null,
                    showing ? Button("Whip up a dust devil", "Somewhere along the taskbar.", "Whirl", () => _host.Request(SeasonAction.Gust)) : null));
                list.Add(new Caption("Play"));
                list.Add(new Tip(("Ctrl+Alt+Drag", "Kick through the leaves and send them flying."),
                                 ("Ctrl+Alt+Right-click", "Start a dust devil right there.")));
                break;
        }
        return list;
    }

    void SetChoice(SeasonChoice c)
    {
        _s.Season = c;
        Changed();
        int page = _page;
        BeginInvoke(async () => { await Task.Delay(150); ShowPage(page); });
    }

    List<Control> AboutPage(Color accent)
    {
        string version = $"Version {AppInfo.Version}";
        string build = Updater.BuildCommit is string c ? $"Build {c[..7]}. Keeps itself up to date." : "Built on this PC, so it doesn't update itself.";
        return new List<Control>
        {
            new Hero(null, AppInfo.Name, "Snow, blossom, meadows and leaves that settle on your windows.", Sprites.AppIcon(S(128))),
            new Caption("Version"),
            Card(new Row(version, build, null, 0),
                 Toggle("Check for updates once a day", "New versions install quietly in a few seconds, after asking you.", () => _s.CheckForUpdates, v => _s.CheckForUpdates = v),
                 Button("Check for updates", "See if there's a newer version now.", "Check now", () => _ = _host.CheckForUpdatesAsync(interactive: true))),
            new Caption("Help"),
            Card(Button("Diagnostics", "What the app sees: screen, taskbar and scaling. Handy if things sit in the wrong place.", "Open", _host.OpenDiagnostics),
                 Button("Reset all settings", "Back to how it was when you first installed it.", "Reset", () =>
                 {
                     _s.ResetToDefaults();
                     _s.WelcomeShown = true;
                     Changed();
                     ShowPage(_page);
                 })),
        };
    }

    // Small line icons for the General and About nav entries.
    static Bitmap GearIcon(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        Theme.Hq(g);
        float k = size / 20f;
        using var pen = new Pen(Color.FromArgb(205, 210, 222), 1.6f * k);
        g.DrawEllipse(pen, 6.5f * k, 6.5f * k, 7f * k, 7f * k);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            g.DrawLine(pen, 10 * k + (float)Math.Cos(a) * 5.4f * k, 10 * k + (float)Math.Sin(a) * 5.4f * k,
                            10 * k + (float)Math.Cos(a) * 8.2f * k, 10 * k + (float)Math.Sin(a) * 8.2f * k);
        }
        return bmp;
    }

    static Bitmap InfoIcon(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        Theme.Hq(g);
        float k = size / 20f;
        using var pen = new Pen(Color.FromArgb(205, 210, 222), 1.6f * k);
        g.DrawEllipse(pen, 2f * k, 2f * k, 16f * k, 16f * k);
        g.DrawLine(pen, 10 * k, 9 * k, 10 * k, 14.5f * k);
        using var b = new SolidBrush(Color.FromArgb(205, 210, 222));
        g.FillEllipse(b, 9 * k, 5 * k, 2 * k, 2 * k);
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _saveTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A panel that doesn't flicker while scrolling.</summary>
internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
}
