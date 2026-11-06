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
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(2000);
    }

    public void RequestWipe() => Interlocked.Exchange(ref _wipeRequested, 1);

    // Set WEATHER_STATS=<file> to log frame timings (for tuning).
    readonly string? _stats = Environment.GetEnvironmentVariable("WEATHER_STATS");
    int _frames, _late;
    double _statStart, _sumWork, _maxWork, _sumLead;

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
                                    $"done-before-refresh={_sumLead / n * 1000:F1}ms late={_late}\n");
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

    static void WaitUntil(Stopwatch clock, double t)
    {
        while (true)
        {
            double left = t - clock.Elapsed.TotalSeconds;
            if (left <= 0) return;
            if (left > 0.0015) Thread.Sleep(1);
            else Thread.SpinWait(50);
        }
    }

    void Blank()
    {
        _overlay.Clear(); _overlay.Present();
        _ground.Clear(); _ground.Present();
    }

    void Loop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            var clock = Stopwatch.StartNew();
            double last = 0, lastSlow = -10, lastFullscreenCheck = -10;
            double period = 1.0 / Native.RefreshRate();
            double workEma = 0.002;
            bool blank = false, fullscreen = false;
            var screen = Native.PrimaryScreen();
            var workArea = Native.WorkArea();

            while (!_stop)
            {
                // Wait for a refresh, then sleep until just before the next one.
                bool vsync = Native.DwmFlush() == 0;
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
                        _overlay.Allocate(screen.Width, screen.Height);
                    if (screen.Width != _ground.Width) _ground.Allocate(screen.Width);
                    // Just below the taskbar while it is always-on-top, so it and its menus stay in front.
                    // (A hidden auto-hide taskbar is not always-on-top; then we just stay where we are.)
                    if (_taskbar.Hwnd != IntPtr.Zero && Native.IsTopmost(_taskbar.Hwnd))
                    {
                        Native.PlaceBelow(_ground.Handle, _taskbar.Hwnd);
                        Native.PlaceBelow(_overlay.Handle, _ground.Handle);
                    }
                }

                // Never drop out of the always-on-top group, or activating any window would cover the snow.
                if (!Native.IsTopmost(_overlay.Handle) || !Native.IsTopmost(_ground.Handle))
                {
                    Native.KeepTopmost(_ground.Handle);
                    Native.PlaceBelow(_overlay.Handle, _ground.Handle);
                    Native.KeepTopmost(_overlay.Handle);
                }

                // Checked often so snow vanishes the moment something goes full screen.
                if (now - lastFullscreenCheck > 0.1)
                {
                    lastFullscreenCheck = now;
                    fullscreen = _settings.HideInFullscreen && Native.ForegroundIsFullscreen(screen, _overlay.Handle);
                }

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
                int groundY = _taskbar.GroundY(screen, workArea);
                _ground.MoveTo(groundY, async: true); // normally already done by the taskbar hook

                // A maximized window in front counts as full screen: show nothing, but keep
                // tracking windows so its hidden snow falls when it is minimized or restored.
                IntPtr roof = _settings.HideWhenMaximized ? MaximizedInFront(wins) : IntPtr.Zero;
                if (roof != IntPtr.Zero)
                {
                    _sim.UpdateHidden(dt, wins, screen.Width, screen.Height, groundY, roof, _settings);
                    if (!blank) { Blank(); blank = true; }
                    continue;
                }
                blank = false;

                _sim.Update(dt, wins, screen.Width, screen.Height, groundY, _settings);
                Renderer.Draw(_overlay.Pixels, _overlay.Width, _overlay.Height, _sim);
                Renderer.DrawGround(_ground.Pixels, _ground.Width, GroundLayer.Height, _sim);
                _overlay.Present();
                _ground.Present();

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
