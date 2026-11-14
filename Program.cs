namespace Weather;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-icon") { AppInfo.WriteIcon(args[1]); return; }
        if (args.Length == 2 && args[0] == "--glyphs") { DumpGlyphs(args[1]); return; }

        // Just updated: the previous version is still shutting down, so wait for it to let go.
        bool afterUpdate = args.Contains("--after-update");

        using var mutex = new Mutex(false, $@"Local\{AppInfo.Name}App");
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{AppInfo.Name}ShowSettings");
        bool owned;
        try { owned = mutex.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; } // the old instance exited without releasing it
        if (!owned)
        {
            // Already running: starting it again opens the settings window there.
            showSettings.Set();
            return;
        }
        // A version from before the rename may still be closing down after updating us: wait for it.
        using (var old = new Mutex(false, $@"Local\{AppInfo.OldName}Overlay"))
        {
            try { if (old.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero)) old.ReleaseMutex(); }
            catch (AbandonedMutexException) { }
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Crash.Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash.Log(e.ExceptionObject as Exception);
        var app = new TrayApp(afterUpdate);
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.RegisterWaitForSingleObject(showSettings,
            (_, _) => ui.Post(_ => app.ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        Application.Run(app);
        mutex.ReleaseMutex();
    }

    /// <summary>Debug: the four season glyphs side by side at 128px.</summary>
    static void DumpGlyphs(string path)
    {
        using var all = new Bitmap(4 * 140, 140);
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.FromArgb(30, 32, 40));
            for (int i = 0; i < 4; i++)
            {
                using var b = Sprites.SeasonGlyph((Season)i, 128);
                g.DrawImage(b, 6 + i * 140, 6);
            }
        }
        all.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}

/// <summary>Writes unexpected errors to crash.txt in the settings folder (shown in About → Diagnostics).</summary>
internal static class Crash
{
    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "crash.txt"), $"{DateTime.Now:u} {AppInfo.Version}\n{ex}\n\n");
        }
        catch { }
    }
}
