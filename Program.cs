namespace Weather;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\WeatherSnowOverlay", out bool firstInstance);
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WeatherSnowShowSettings");
        if (!firstInstance)
        {
            // Already running: starting it again opens the settings window there.
            showSettings.Set();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = new TrayApp();
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.RegisterWaitForSingleObject(showSettings,
            (_, _) => ui.Post(_ => app.ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        Application.Run(app);
    }
}
