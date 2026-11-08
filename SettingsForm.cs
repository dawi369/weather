namespace Weather;

/// <summary>Settings window. Every change applies to the running snow immediately and is saved.</summary>
internal sealed class SettingsForm : Form
{
    readonly Settings _s;
    readonly List<Action> _refreshers = new();
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 400 };

    public SettingsForm(Settings settings)
    {
        _s = settings;
        Text = "Weather – snow settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _s.Save(); };

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        Section(grid, "Falling snow");
        Slider(grid, "Snowfall", 0, 400, () => _s.SnowfallRate, v => _s.SnowfallRate = v, v => v == 0 ? "off" : $"{v}/s");
        Slider(grid, "Flake size", 40, 250, () => _s.FlakeSize, v => _s.FlakeSize = v, v => $"{v}%");
        Slider(grid, "Flake brightness", 10, 100, () => _s.FlakeBrightness, v => _s.FlakeBrightness = v, v => $"{v}%");
        Slider(grid, "Wind", -150, 150, () => _s.Wind, v => _s.Wind = v,
               v => v == 0 ? "calm" : v < 0 ? $"← {-v}" : $"{v} →");
        Slider(grid, "Gusts", 0, 100, () => _s.Gusts, v => _s.Gusts = v, v => $"{v}%");

        Section(grid, "Settled snow");
        Slider(grid, "Buildup speed", 25, 400, () => _s.BuildupSpeed, v => _s.BuildupSpeed = v, v => $"{v}%");
        Slider(grid, "Stickiness", 0, 100, () => _s.Stickiness, v => _s.Stickiness = v,
               v => v < 25 ? $"{v}% runny" : v < 60 ? $"{v}%" : $"{v}% sticky");
        Slider(grid, "Max depth", 6, 200, () => _s.MaxDepth, v => _s.MaxDepth = v, v => $"{v}px");
        Slider(grid, "Fling sensitivity", 0, 100, () => _s.FlingSensitivity, v => _s.FlingSensitivity = v, v => $"{v}%");
        Check(grid, "Snow builds up on the taskbar", () => _s.TaskbarSnow, v => _s.TaskbarSnow = v);
        Slider(grid, "Taskbar snow height", -120, 120, () => -_s.TaskbarSnowOffset, v => _s.TaskbarSnowOffset = -v,
               v => v == 0 ? "auto" : v > 0 ? $"{v}px higher" : $"{-v}px lower");

        Section(grid, "Clearing");
        Slider(grid, "Auto-clear every", 0, 300, () => _s.WipeSeconds, v => _s.WipeSeconds = v,
               v => v == 0 ? "never" : v < 60 ? $"{v}s" : $"{v / 60}m {v % 60:00}s");

        Section(grid, "When to hide the snow");
        Check(grid, "When windows fill the screen (maximized or split screen)", () => _s.HideWhenMaximized, v => _s.HideWhenMaximized = v);
        Check(grid, "In full-screen apps and games", () => _s.HideInFullscreen, v => _s.HideInFullscreen = v);

        Section(grid, "Updates");
        Check(grid, "Check for a new version once a day", () => _s.CheckForUpdates, v => _s.CheckForUpdates = v);
        var version = new Label
        {
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 2, 0, 0),
            Text = Updater.BuildCommit is string c ? $"Version: build {c[..7]}" : "Version: local build (doesn't update itself)",
        };
        grid.Controls.Add(version, 0, grid.RowCount);
        grid.SetColumnSpan(version, 3);
        grid.RowCount++;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        // Shown modeless, so a DialogResult button would not close the window: close it explicitly.
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        var reset = new Button { Text = "Reset to defaults", AutoSize = true };
        reset.Click += (_, _) => { _s.ResetToDefaults(); foreach (var r in _refreshers) r(); Changed(); };
        buttons.Controls.Add(close);
        buttons.Controls.Add(reset);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 3);
        grid.RowCount++;

        Controls.Add(grid);
        AcceptButton = close;
        FormClosed += (_, _) => { _saveTimer.Stop(); _s.Save(); };
    }

    void Changed() { _saveTimer.Stop(); _saveTimer.Start(); }

    static void Section(TableLayoutPanel grid, string title)
    {
        var l = new Label
        {
            Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, grid.RowCount == 0 ? 0 : 14, 0, 4),
        };
        grid.Controls.Add(l, 0, grid.RowCount);
        grid.SetColumnSpan(l, 3);
        grid.RowCount++;
    }

    void Slider(TableLayoutPanel grid, string label, int min, int max, Func<int> get, Action<int> set, Func<int, string> fmt)
    {
        var name = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 12, 0) };
        var bar = new TrackBar
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(get(), min, max),
            TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = 30,
            SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10),
        };
        var value = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 0, 0), Text = fmt(bar.Value) };
        bar.ValueChanged += (_, _) => { set(bar.Value); value.Text = fmt(bar.Value); Changed(); };
        _refreshers.Add(() => bar.Value = Math.Clamp(get(), min, max));
        int row = grid.RowCount++;
        grid.Controls.Add(name, 0, row);
        grid.Controls.Add(bar, 1, row);
        grid.Controls.Add(value, 2, row);
    }

    void Check(TableLayoutPanel grid, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Text = label, AutoSize = true, Checked = get(), Margin = new Padding(8, 4, 0, 4) };
        box.CheckedChanged += (_, _) => { set(box.Checked); Changed(); };
        _refreshers.Add(() => box.Checked = get());
        int row = grid.RowCount++;
        grid.Controls.Add(box, 0, row);
        grid.SetColumnSpan(box, 3);
    }
}
