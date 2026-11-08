namespace Weather;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Just updated: the previous version is still shutting down, so wait for it to let go.
        bool afterUpdate = args.Contains("--after-update");

        using var mutex = new Mutex(false, @"Local\WeatherSnowOverlay");
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WeatherSnowShowSettings");
        bool owned;
        try { owned = mutex.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; } // the old instance exited without releasing it
        if (!owned)
        {
            // Already running: starting it again opens the settings window there.
            showSettings.Set();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = new TrayApp(afterUpdate);
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.RegisterWaitForSingleObject(showSettings,
            (_, _) => ui.Post(_ => app.ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        Application.Run(app);
        mutex.ReleaseMutex();
    }
}
