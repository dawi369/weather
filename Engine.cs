using System.Diagnostics;

namespace Weather;

/// <summary>
/// Runs simulation + rendering on its own thread, once per display refresh.
/// Frames are "late-latched": instead of starting right after a refresh (and showing window
/// positions that are a whole refresh old by the time they are on screen), each frame starts just
/// before the next refresh, so the snow is drawn from window positions only ~3 ms old.
/// </summary>
internal sealed unsafe class Engine
{
    readonly Overlay _overlay;
    readonly GroundLayer _ground;
    readonly TaskbarWatcher _taskbar;
    readonly Settings _settings;
    readonly SnowSim _sim = new();
    readonly WindowTracker _tracker;
    Thread? _thread;
    volatile bool _stop;
    int _wipeRequested;
    bool _onBattery;
    int _skip, _errors;

    public Engine(Overlay overlay, GroundLayer ground, TaskbarWatcher taskbar, Settings settings)
    {
        _overlay = overlay;
        _ground = ground;
        _taskbar = taskbar;
        _settings = settings;
        _tracker = new WindowTracker(overlay.Handle, ground.Handle);
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Snow", Priority = ThreadPriority.Highest };
        _thread.Start();
        new Thread(WatchFullscreen) { IsBackground = true, Name = "Snow fullscreen watch" }.Start();
    }

    volatile bool _fullscreen;

    /// <summary>Polls "is a full-screen app in front?" ten times a second, off the snow thread.</summary>
    void WatchFullscreen()
    {
        while (!_stop)
        {
            try { _fullscreen = Native.ForegroundIsFullscreen(Native.PrimaryScreen(), _overlay.Handle); }
            catch { /* never let this kill the app */ }
            Thread.Sleep(100);
        }
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(2000);
    }

    public void RequestWipe() => Interlocked.Exchange(ref _wipeRequested, 1);

    /// <summary>The simulation (for queuing play input and tray actions; those are thread-safe).</summary>
    public SnowSim Sim => _sim;

    // Set WEATHER_STATS=<file> to log frame timings (for tuning).
    readonly string? _stats = Environment.GetEnvironmentVariable("WEATHER_STATS");
    int _frames, _late;
    double _statStart, _sumWork, _maxWork, _sumLead;
    readonly double[] _ph = new double[4]; // time per phase: sim, draw, ground, present

    void Stat(double start, double work, double leadToVblank)
    {
        if (_frames == 0) _statStart = start;
        _frames++;
        _sumWork += work;
        _maxWork = Math.Max(_maxWork, work);
        _sumLead += leadToVblank;
        if (leadToVblank < 0) _late++;
        if (start - _statStart < 2) return;
        double n = _frames;
        File.AppendAllText(_stats!, $"fps={n / (start - _statStart):F1} work={_sumWork / n * 1000:F2}ms (max {_maxWork * 1000:F1}) " +
                                    $"done-before-refresh={_sumLead / n * 1000:F1}ms late={_late} sim={_ph[0] / n * 1000:F2} " +
                                    $"draw={_ph[1] / n * 1000:F2} ground={_ph[2] / n * 1000:F2} present={_ph[3] / n * 1000:F2} particles={_sim.Count}\n");
        Array.Clear(_ph);
        _frames = _late = 0; _sumWork = _maxWork = _sumLead = 0;
    }

    /// <summary>The maximized window that is in front (foreground or topmost in Z-order), if any.</summary>
    static IntPtr MaximizedInFront(List<WinInfo> wins)
    {
        IntPtr fg = Native.GetForegroundWindow();
        foreach (var w in wins)
            if (w.Hwnd == fg) return w.Zoomed ? w.Hwnd : IntPtr.Zero;
        return wins.Count > 0 && wins[0].Zoomed ? wins[0].Hwnd : IntPtr.Zero;
    }

    /// <summary>
    /// Windows that together fill the screen count as full screen too: two apps snapped side by
    /// side, four quarters, or any mix that leaves (almost) no desktop showing. Checked on a coarse
    /// grid over the work area. Returns the window the hidden snow should settle on.
    /// </summary>
    static IntPtr FilledByWindows(List<WinInfo> wins, Native.RECT work)
    {
        if (wins.Count == 0) return IntPtr.Zero;
        const int Cols = 48, Rows = 27;
        int covered = 0;
        float cw = work.Width / (float)Cols, ch = work.Height / (float)Rows;
        for (int r = 0; r < Rows; r++)
        for (int c = 0; c < Cols; c++)
        {
            float x = work.Left + (c + 0.5f) * cw, y = work.Top + (r + 0.5f) * ch;
            foreach (var w in wins)
                if (x >= w.Rect.Left && x < w.Rect.Right && y >= w.Rect.Top && y < w.Rect.Bottom) { covered++; break; }
        }
        if (covered < Cols * Rows * 0.97f) return IntPtr.Zero;
        IntPtr fg = Native.GetForegroundWindow();
        foreach (var w in wins) if (w.Hwnd == fg) return fg;
        return wins[0].Hwnd;
    }

    // Kept between frames so a taskbar move can be handled while we wait for the next frame.
    Native.RECT _screen, _workArea;
    int _groundY;
    bool _groundLive; // the last frame drew the taskbar snow (not paused or hidden)

    /// <summary>
    /// Wait until <paramref name="t"/>. If the taskbar moves meanwhile, redraw and move the taskbar
    /// snow right away (pixels and position together), instead of a frame later.
    /// </summary>
    void WaitUntil(Stopwatch clock, double t)
    {
        while (true)
        {
            double left = t - clock.Elapsed.TotalSeconds;
            if (left <= 0) return;
            if (left > 0.0015)
            {
                if (_taskbar.Moved.WaitOne(1)) RefreshGround();
            }
            else Thread.SpinWait(50);
        }
    }

    void RefreshGround()
    {
        if (!_groundLive) return;
        int g = _taskbar.GroundY(_screen, _workArea);
        if (g == _groundY) return;
        _groundY = g;
        _sim.RefreshGround(g);
        Renderer.DrawGround(_ground.Pixels, _ground.Width, GroundLayer.Height, _sim);
        _ground.PresentAt(g);
        Trace("hook", g);
    }

    // Set WEATHER_TRACE=<file> to log every taskbar-snow position change with a QPC timestamp.
    readonly string? _trace = Environment.GetEnvironmentVariable("WEATHER_TRACE");
    int _lastTraced = int.MinValue;
    void Trace(string src, int g)
    {
        if (_trace == null || g == _lastTraced) return;
        _lastTraced = g;
        File.AppendAllText(_trace, $"{Stopwatch.GetTimestamp()} {src} {g}\n");
    }

    string? _lastDiag;

    // Set WEATHER_DEMO=1 to have the season's extras happen on a fixed schedule (for screenshots and
    // tests): critters, the season's tray action, snowmen and a thrown snowball on the window at
    // (560..1320, top 110), then the action again.
    readonly string? _demo = Environment.GetEnvironmentVariable("WEATHER_DEMO");
    int _demoStep;
    double _demoStart = -1;

    void Demo(double now)
    {
        if (_demoStart < 0) _demoStart = now;
        double t = now - _demoStart;
        var s = _sim;
        SeasonAction act = s.Season switch { Season.Spring => SeasonAction.Shower, Season.Autumn => SeasonAction.Gust, _ => SeasonAction.Plow };
        switch (_demoStep)
        {
            case 0 when t > 4: s.Request(SeasonAction.Critters); _demoStep++; break;
            case 1 when t > 9: s.Request(act); _demoStep++; break;
            case 2 when t > 15:
                s.Enqueue(new PlayCommand(PlayKind.Right, 700, 104, now));
                s.Enqueue(new PlayCommand(PlayKind.Right, 1180, 104, now));
                _demoStep++;
                break;
            case 3 when t > 17:
                s.Enqueue(new PlayCommand(PlayKind.Down, 900, 104, now));
                s.Enqueue(new PlayCommand(PlayKind.Move, 920, 100, now + 0.02));
                s.Enqueue(new PlayCommand(PlayKind.Move, 980, 80, now + 0.04));
                s.Enqueue(new PlayCommand(PlayKind.Move, 1060, 50, now + 0.06));
                s.Enqueue(new PlayCommand(PlayKind.Up, 1100, 40, now + 0.07));
                _demoStep++;
                break;
            case 4 when t > 18:
                s.Enqueue(new PlayCommand(PlayKind.Down, 1000, 300, now));
                s.Enqueue(new PlayCommand(PlayKind.Up, 1000, 300, now + 0.2));
                _demoStep++;
                break;
            case 5 when t > 27: s.Request(act); _demoStep++; break;
        }
    }

    // Set WEATHER_DUMP=<file> to write the pile state every 2s (debugging the physics).
    readonly string? _dump = Environment.GetEnvironmentVariable("WEATHER_DUMP");
    double _lastDump;

    /// <summary>
    /// Keep %APPDATA%\WeatherSnow\diagnostics.txt describing what the app sees (screen, work area,
    /// taskbar, DPI, where the taskbar snow sits), rewritten whenever that changes. Handy when the snow
    /// sits in the wrong place on someone else's PC.
    /// </summary>
    void WriteDiagnostics()
    {
        try
        {
            string d = $"{Native.Describe(_taskbar.Hwnd)} groundY={_groundY} heightNudge={_settings.TaskbarSnowOffset} version={Updater.Version}";
            if (d == _lastDiag) return;
            _lastDiag = d;
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(Path.Combine(Settings.Folder, "diagnostics.txt"), $"{DateTime.Now:u}\n{d.Replace(" ", "\n")}\n");
        }
        catch { /* diagnostics are best effort */ }
    }

    void Blank()
    {
        _groundLive = false;
        _overlay.Clear(); _overlay.Present();
        Renderer.Invalidate();
        _ground.Clear(); _ground.PresentAt(_groundY);
    }

    void Loop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            var clock = Stopwatch.StartNew();
            double last = 0, lastSlow = -10;
            double period = 1.0 / Native.RefreshRate();
            double workEma = 0.002;
            bool blank = false, fullscreen = false;
            var screen = Native.PrimaryScreen();
            var workArea = Native.WorkArea();
            _screen = screen; _workArea = workArea; _groundY = workArea.Bottom;

            while (!_stop)
            {
                // Wait for a refresh, then sleep until just before the next one.
                bool vsync = Native.DwmFlush() == 0;
                // Battery saver: every other refresh only (30 fps on a 60 Hz screen).
                if (vsync && _onBattery && (++_skip & 1) == 1) continue;
                double vblank = clock.Elapsed.TotalSeconds;
                double budget = Math.Clamp(workEma * 1.5 + 0.0015, 0.0025, period * 0.8);
                if (vsync) WaitUntil(clock, vblank + period - budget);
                else Thread.Sleep(8);

                double now = clock.Elapsed.TotalSeconds;

                if (now - lastSlow > 0.5)
                {
                    lastSlow = now;
                    screen = Native.PrimaryScreen();
                    workArea = Native.WorkArea();
                    period = 1.0 / Native.RefreshRate();
                    if (screen.Width != _overlay.Width || screen.Height != _overlay.Height)
                    { _overlay.Allocate(screen.Width, screen.Height); Renderer.Invalidate(); }
                    if (screen.Width != _ground.Width) _ground.Allocate(screen.Width);
                    // Just below the taskbar while it is always-on-top, so it and its menus stay in front.
                    // (A hidden auto-hide taskbar is not always-on-top; then we just stay where we are.)
                    if (_taskbar.Hwnd != IntPtr.Zero && Native.IsTopmost(_taskbar.Hwnd))
                    {
                        Native.PlaceBelow(_ground.Handle, _taskbar.Hwnd);
                        Native.PlaceBelow(_overlay.Handle, _ground.Handle);
                    }
                    if (_taskbar.Hwnd != IntPtr.Zero) { uint dpi = Native.GetDpiForWindow(_taskbar.Hwnd); if (dpi > 0) _sim.UiScale = dpi / 96f; }
                    Sprites.SetScale(_sim.UiScale * 0.7f);
                    _onBattery = _settings.BatterySaver && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
                    _sim.Economy = _onBattery ? 0.6f : 1f;
                    WriteDiagnostics();
                }

                // Never drop out of the always-on-top group, or activating any window would cover the snow.
                if (!Native.IsTopmost(_overlay.Handle) || !Native.IsTopmost(_ground.Handle))
                {
                    Native.KeepTopmost(_ground.Handle);
                    Native.PlaceBelow(_overlay.Handle, _ground.Handle);
                    Native.KeepTopmost(_overlay.Handle);
                }
                // Checked on its own thread (see WatchFullscreen): asking the shell can block for a
                // long time while Explorer is busy, e.g. animating the taskbar.
                fullscreen = _settings.HideInFullscreen && _fullscreen;

                if (_settings.Paused || fullscreen)
                {
                    if (!blank) { Blank(); blank = true; }
                    _sim.ResetTracking();
                    Thread.Sleep(100);
                    last = clock.Elapsed.TotalSeconds;
                    continue;
                }
                float dt = (float)Math.Clamp(now - last, 0.001, 0.05);
                last = now;

                if (Interlocked.Exchange(ref _wipeRequested, 0) == 1) _sim.Wipe();

                var wins = _tracker.Enumerate(screen);
                _taskbar.Offset = _settings.TaskbarSnowOffset;
                int groundY = _taskbar.GroundY(screen, workArea);
                _screen = screen; _workArea = workArea; _groundY = groundY;

                // A maximized window in front counts as full screen: show nothing, but keep
                // tracking windows so its hidden snow falls when it is minimized or restored.
                IntPtr roof = IntPtr.Zero;
                if (_settings.HideWhenMaximized)
                {
                    roof = MaximizedInFront(wins);
                    if (roof == IntPtr.Zero) roof = FilledByWindows(wins, workArea); // split screen etc.
                }
                if (roof != IntPtr.Zero)
                {
                    _sim.UpdateHidden(dt, wins, screen.Width, screen.Height, groundY, roof, _settings);
                    if (!blank) { Blank(); blank = true; }
                    continue;
                }
                blank = false;

                if (Native.GetCursorPos(out var cur)) { _sim.CursorX = cur.X; _sim.CursorY = cur.Y; }
                if (_demo != null) Demo(now);
                double p0 = clock.Elapsed.TotalSeconds, p1, p2, p3;
                try
                {
                    _sim.Update(dt, wins, screen.Width, screen.Height, groundY, _settings);
                    p1 = clock.Elapsed.TotalSeconds;
                    Renderer.Draw(_overlay.Pixels, _overlay.Width, _overlay.Height, _sim);
                    p2 = clock.Elapsed.TotalSeconds;
                    Renderer.DrawGround(_ground.Pixels, _ground.Width, GroundLayer.Height, _sim);
                    p3 = clock.Elapsed.TotalSeconds;
                }
                catch (Exception ex)
                {
                    // A bug in one frame must not take the whole app down: note it and carry on.
                    if (_errors++ < 5) Crash.Log(ex);
                    Renderer.Invalidate();
                    continue;
                }
                if (Renderer.HasDirty) _overlay.PresentDirty(Renderer.Dirty);
                _ground.PresentAt(groundY);
                if (_stats != null)
                {
                    double p4 = clock.Elapsed.TotalSeconds;
                    _ph[0] += p1 - p0; _ph[1] += p2 - p1; _ph[2] += p3 - p2; _ph[3] += p4 - p3;
                }
                _groundLive = true;
                if (_dump != null && now - _lastDump > 2) { _lastDump = now; try { File.WriteAllText(_dump, _sim.DebugDump()); } catch { } }
                Trace("frame", groundY);

                double done = clock.Elapsed.TotalSeconds;
                double work = done - now;
                workEma += (work - workEma) * 0.05;
                if (work > workEma) workEma += (work - workEma) * 0.3; // react quickly to slow frames
                if (_stats != null) Stat(now, work, vblank + period - done);
            }

            Blank();
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }
}
