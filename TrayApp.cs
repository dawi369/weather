using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Weather;

/// <summary>The tray icon and its menu. Owns the overlay and the engine.</summary>
internal sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "WeatherSnow";

    readonly Settings _settings = Settings.Load();
    readonly Overlay _overlay;
    readonly GroundLayer _ground;
    readonly TaskbarWatcher _taskbar;
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };
    readonly Engine _engine;
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _pauseItem;

    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60_000 };
    bool _checkingForUpdates;

    public TrayApp(bool afterUpdate = false)
    {
        Updater.CleanUpOldVersion();
        _overlay = new Overlay();
        var screen = Native.PrimaryScreen();
        _ground = new GroundLayer(screen.Width, Native.WorkArea().Bottom);
        _taskbar = new TaskbarWatcher(); // hook runs on this (UI) thread
        _refresh.Tick += (_, _) => _taskbar.Refresh();
        _refresh.Start();
        _engine = new Engine(_overlay, _ground, _taskbar, _settings);
        _engine.Start();

        _pauseItem = new ToolStripMenuItem("Pause snow", null, (_, _) => TogglePause());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Clear snow now", null, (_, _) => _engine.RequestWipe()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows() };
        startup.Click += (_, _) => { SetStartWithWindows(!startup.Checked); startup.Checked = StartsWithWindows(); };
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripMenuItem("Check for updates…", null, async (_, _) => await CheckForUpdatesAsync(interactive: true)));
        menu.Items.Add(new ToolStripMenuItem("Open diagnostics", null, (_, _) => OpenDiagnostics()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        _icon = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Weather – snow",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        if (afterUpdate)
            _icon.ShowBalloonTip(4000, "Weather updated", "You're on the newest version.", ToolTipIcon.Info);

        // First look a minute after starting, then every 10 minutes see whether a day has passed.
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 10 * 60_000;
            if (_settings.CheckForUpdates && DateTime.UtcNow - _settings.LastUpdateCheckUtc >= TimeSpan.FromHours(24))
                await CheckForUpdatesAsync(interactive: false);
        };
        _updateTimer.Start();
    }

    /// <summary>
    /// Ask GitHub whether a newer build exists; if so, offer to install it. Automatic checks stay
    /// silent unless there is an update; a check from the menu always reports back.
    /// </summary>
    async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            if (!Updater.CanSelfUpdate)
            {
                if (interactive)
                    Ask("This copy of Weather was built on this PC, not downloaded from GitHub, so it can't update itself.",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            var latest = await Updater.GetLatestAsync();
            if (latest == null || !Updater.IsNewer(latest))
            {
                if (interactive) Ask("You have the latest version of Weather.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string when = latest.Published == DateTimeOffset.MinValue ? "" : $" (from {latest.Published.LocalDateTime:d MMM yyyy})";
            if (Ask($"A new version of Weather is available{when}.\n\nUpdate now? The snow will restart in a few seconds.",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            interactive = true; // they said yes: from here on, report any problem
            _icon.ShowBalloonTip(3000, "Weather", "Downloading the update…", ToolTipIcon.Info);
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
    static void OpenDiagnostics()
    {
        string file = Path.Combine(Settings.Folder, "diagnostics.txt");
        if (!File.Exists(file))
        {
            Ask("No diagnostics yet: give it a second after the snow starts.", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        return MessageBox.Show(owner, text, "Weather", buttons, icon);
    }

    SettingsForm? _settingsForm;

    public void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false }) { _settingsForm.Activate(); return; }
        _settingsForm = new SettingsForm(_settings);
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _pauseItem.Text = _settings.Paused ? "Resume snow" : "Pause snow";
    }

    void Exit()
    {
        _engine.Stop();
        _refresh.Stop();
        _updateTimer.Stop();
        _taskbar.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _ground.Dispose();
        _overlay.Dispose();
        ExitThread();
    }

    static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    /// <summary>Draws a little snowflake for the tray.</summary>
    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var outline = new Pen(Color.FromArgb(220, 25, 55, 100), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fill = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var pen in new[] { outline, fill })
            {
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3;
                    float ex = 16 + (float)Math.Cos(a) * 13, ey = 16 + (float)Math.Sin(a) * 13;
                    g.DrawLine(pen, 16, 16, ex, ey);
                    float bx = 16 + (float)Math.Cos(a) * 8, by = 16 + (float)Math.Sin(a) * 8;
                    foreach (double side in new[] { -0.7, 0.7 })
                        g.DrawLine(pen, bx, by, bx + (float)Math.Cos(a + side) * 5, by + (float)Math.Sin(a + side) * 5);
                }
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
