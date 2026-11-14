using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Weather;

/// <summary>The tray icon and its menu. Owns the overlay, the engine and the mouse hook.</summary>
internal sealed class TrayApp : ApplicationContext, IAppHost
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Settings _settings = Settings.Load();
    readonly Overlay _overlay;
    readonly GroundLayer _ground;
    readonly TaskbarWatcher _taskbar;
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };
    readonly System.Windows.Forms.Timer _seasonTimer = new() { Interval = 2000 };
    readonly Engine _engine;
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu;
    readonly InputHook _hook;
    Season _iconSeason = (Season)(-1);
    IntPtr _hIcon;

    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60_000 };
    bool _checkingForUpdates;

    public TrayApp(bool afterUpdate = false)
    {
        Updater.CleanUpOldVersion();
        MigrateStartup();
        _overlay = new Overlay();
        var screen = Native.PrimaryScreen();
        _ground = new GroundLayer(screen.Width, Native.WorkArea().Bottom);
        _taskbar = new TaskbarWatcher(); // hook runs on this (UI) thread
        _refresh.Tick += (_, _) => _taskbar.Refresh();
        _refresh.Start();
        _engine = new Engine(_overlay, _ground, _taskbar, _settings);
        _engine.Start();
        _hook = new InputHook(() => _engine.Sim, () => _settings.PlayWithMouse && !_settings.Paused);

        _menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Font(10f), ShowImageMargin = true, ShowCheckMargin = false };
        _menu.Opening += (_, _) => BuildMenu();
        _menu.Opened += (_, _) => Theme.DarkChrome(_menu);
        BuildMenu();

        _icon = new NotifyIcon { Visible = true, ContextMenuStrip = _menu };
        UpdateTrayIcon();
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        _seasonTimer.Tick += (_, _) => UpdateTrayIcon();
        _seasonTimer.Start();

        // Tests: WEATHER_SHOWMENU=1 pops the tray menu up by itself (for screenshots).
        if (Environment.GetEnvironmentVariable("WEATHER_SHOWMENU") == "1")
        {
            var t = new System.Windows.Forms.Timer { Interval = 2500 };
            t.Tick += (_, _) =>
            {
                t.Dispose();
                var wa = Screen.PrimaryScreen!.WorkingArea;
                _menu.Show(new Point(wa.Right - 40, wa.Bottom - 10), ToolStripDropDownDirection.AboveLeft);
            };
            t.Start();
        }

        // Pause while the PC is locked: nobody's watching.
        SystemEvents.SessionSwitch += OnSessionSwitch;

        if (afterUpdate)
            _icon.ShowBalloonTip(4000, $"{AppInfo.Name} updated", "You're on the newest version.", ToolTipIcon.Info);
        if (!_settings.WelcomeShown)
        {
            var welcome = new System.Windows.Forms.Timer { Interval = 1500 };
            welcome.Tick += (_, _) => { welcome.Dispose(); ShowWelcome(); };
            welcome.Start();
        }

        // First look a minute after starting, then every 10 minutes see whether a day has passed.
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 10 * 60_000;
            if (_settings.CheckForUpdates && DateTime.UtcNow - _settings.LastUpdateCheckUtc >= TimeSpan.FromHours(24))
                await CheckForUpdatesAsync(interactive: false);
        };
        _updateTimer.Start();
    }

    bool _lockPaused;
    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            if (!_settings.Paused) { _settings.Paused = true; _lockPaused = true; }
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            if (_lockPaused) { _settings.Paused = false; _lockPaused = false; }
        }
    }

    public Season CurrentSeason => SeasonInfo.Resolve(_settings.Season, _settings.SouthernHemisphere);

    // ───────────────────────────── tray icon and menu ─────────────────────────────

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    void UpdateTrayIcon()
    {
        var s = CurrentSeason;
        _icon.Text = _settings.Paused ? $"{AppInfo.Name} · paused" : $"{AppInfo.Name} · {SeasonInfo.Name(s)}";
        if (s == _iconSeason) return;
        _iconSeason = s;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = Sprites.SeasonGlyph(s, size);
        var h = bmp.GetHicon();
        var old = _icon.Icon;
        _icon.Icon = Icon.FromHandle(h);
        old?.Dispose();
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _hIcon = h;
    }

    Image Glyph(Season s) => Sprites.SeasonGlyph(s, (int)(18 * _menu.DeviceDpi / 96f), shadow: false);

    void BuildMenu()
    {
        foreach (ToolStripItem old in _menu.Items) old.Image?.Dispose();
        _menu.Items.Clear();
        var season = CurrentSeason;
        var sim = _engine.Sim;
        var pad = new Padding(0, 3, 0, 3);

        _menu.Items.Add(new ToolStripLabel($"{AppInfo.Name}  ·  {SeasonInfo.Name(season)}", Glyph(season))
        {
            Font = Theme.Font(10.5f, FontStyle.Bold), ForeColor = Theme.Text, Padding = new Padding(0, 6, 0, 6),
        });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()) { Font = Theme.Font(10f, FontStyle.Bold), Padding = pad });
        _menu.Items.Add(new ToolStripSeparator());

        // The season's own things to do.
        void Act(string text, SeasonAction a) => _menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => Request(a)) { Padding = pad });
        switch (season)
        {
            case Season.Winter: Act("Send the snowplough", SeasonAction.Plow); break;
            case Season.Spring:
                Act(sim.RainLevel > 0.05f ? "Stop the shower" : "Bring a spring shower", SeasonAction.Shower);
                Act("Release butterflies", SeasonAction.Critters);
                break;
            case Season.Summer: Act(sim.Night ? "Call the fireflies" : "Release butterflies", SeasonAction.Critters); break;
            default:
                Act("Send the leaf blower", SeasonAction.Plow);
                Act("Whip up a dust devil", SeasonAction.Gust);
                break;
        }

        var choose = new ToolStripMenuItem("Season") { Padding = pad };
        void Choice(string text, SeasonChoice c, Image? img)
        {
            var item = new ToolStripMenuItem(text, img, (_, _) => { _settings.Season = c; _settings.Save(); UpdateTrayIcon(); })
            { Checked = _settings.Season == c, Padding = pad };
            choose.DropDownItems.Add(item);
        }
        Choice($"Automatic ({SeasonInfo.Name(SeasonInfo.FromDate(DateTime.Now, _settings.SouthernHemisphere))} now)", SeasonChoice.Auto, null);
        choose.DropDownItems.Add(new ToolStripSeparator());
        foreach (Season s in Enum.GetValues<Season>()) Choice(SeasonInfo.Name(s), (SeasonChoice)((int)s + 1), Glyph(s));
        choose.DropDown.Renderer = _menu.Renderer;
        choose.DropDown.Font = _menu.Font;
        _menu.Items.Add(choose);
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(new ToolStripMenuItem(_settings.Paused ? "Resume the weather" : "Pause", null, (_, _) => { _settings.Paused = !_settings.Paused; _lockPaused = false; UpdateTrayIcon(); }) { Padding = pad });
        _menu.Items.Add(new ToolStripMenuItem($"Clear the {SeasonInfo.Stuff(season)}", null, (_, _) => ClearNow()) { Padding = pad });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Exit()) { Padding = pad });
    }

    public void Request(SeasonAction a) => _engine.Sim.Request(a);
    public void ClearNow() => _engine.RequestWipe();

    // ───────────────────────────── windows ─────────────────────────────

    SettingsForm? _settingsForm;

    public void ShowSettings() => ShowSettings(0);

    public void ShowSettings(int page)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings, this, page);
        _settingsForm.FormClosed += (_, _) => UpdateTrayIcon();
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    void ShowWelcome()
    {
        var card = new WelcomeCard(CurrentSeason);
        card.OpenSettings += () => ShowSettings(1 + (int)CurrentSeason);
        card.FormClosed += (_, _) => { _settings.WelcomeShown = true; _settings.Save(); };
        card.Show();
    }

    // ───────────────────────────── updates ─────────────────────────────

    /// <summary>
    /// Ask GitHub whether a newer build exists; if so, offer to install it. Automatic checks stay
    /// silent unless there is an update; a check from the menu always reports back.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            if (!Updater.CanSelfUpdate)
            {
                if (interactive)
                    Ask($"This copy of {AppInfo.Name} was built on this PC, not downloaded from GitHub, so it can't update itself.",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            var latest = await Updater.GetLatestAsync();
            if (latest == null || !Updater.IsNewer(latest))
            {
                if (interactive) Ask($"You have the latest version of {AppInfo.Name}.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string when = latest.Published == DateTimeOffset.MinValue ? "" : $" (from {latest.Published.LocalDateTime:d MMM yyyy})";
            if (Ask($"A new version of {AppInfo.Name} is available{when}.\n\nUpdate now? It will restart in a few seconds.",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            interactive = true; // they said yes: from here on, report any problem
            _icon.ShowBalloonTip(3000, AppInfo.Name, "Downloading the update…", ToolTipIcon.Info);
            await Updater.InstallAsync(latest);
            Exit(); // the new version is starting and waits for us to finish
        }
        catch (Exception ex)
        {
            if (interactive) Ask($"Couldn't check for updates:\n{ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    /// <summary>Open diagnostics.txt (what the app sees: screen, taskbar, scaling) in the default text editor.</summary>
    public void OpenDiagnostics()
    {
        string file = Path.Combine(Settings.Folder, "diagnostics.txt");
        if (!File.Exists(file))
        {
            Ask("No diagnostics yet: give it a second after the weather starts.", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) { Ask($"Couldn't open {file}:\n{ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    /// <summary>A message box that comes to the front (the tray app has no window of its own).</summary>
    static DialogResult Ask(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using var owner = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen, Size = new Size(1, 1), Opacity = 0 };
        owner.Show();
        owner.Activate();
        return MessageBox.Show(owner, text, AppInfo.Name, buttons, icon);
    }

    void Exit()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _hook.Dispose();
        _engine.Stop();
        _refresh.Stop();
        _seasonTimer.Stop();
        _updateTimer.Stop();
        _taskbar.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _ground.Dispose();
        _overlay.Dispose();
        ExitThread();
    }

    // ───────────────────────────── start with Windows ─────────────────────────────

    public bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppInfo.Name) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(AppInfo.Name, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
        }
    }

    /// <summary>An entry from before the rename starts us under the new name instead.</summary>
    void MigrateStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(AppInfo.OldName) is not string) return;
            key.DeleteValue(AppInfo.OldName, throwOnMissingValue: false);
            key.SetValue(AppInfo.Name, $"\"{Environment.ProcessPath}\"");
        }
        catch { /* not important enough to fail over */ }
    }
}

/// <summary>Dark, rounded look for the tray menu.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = true; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Theme.Hq(g);
        float k = e.ToolStrip?.DeviceDpi / 96f ?? 1f;
        var r = new RectangleF(4 * k, 1, e.Item.Width - 8 * k, e.Item.Height - 2);
        using var p = Theme.Round(r, 6 * k);
        using var b = new SolidBrush(Color.FromArgb(48, 53, 66));
        g.FillPath(b, p);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Sub;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        Theme.Hq(g);
        var r = e.ImageRectangle;
        if (e.Item.Image != null)
        {
            // The item has its own icon: ring it to show it's the chosen one.
            using var ring = new Pen(Color.FromArgb(200, 255, 255, 255), 1.4f);
            g.DrawEllipse(ring, r.X - 2, r.Y - 2, r.Width + 3, r.Height + 3);
            return;
        }
        using var pen = new Pen(Theme.Text, 1.8f * (e.ToolStrip?.DeviceDpi / 96f ?? 1f));
        float x = r.X + r.Width * 0.2f, y = r.Y + r.Height * 0.52f;
        g.DrawLines(pen, new[] { new PointF(x, y), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.74f), new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.28f) });
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Line);
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, r);
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color MenuBorder => Theme.Line;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Color.FromArgb(48, 53, 66);
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Card;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
    }
}
