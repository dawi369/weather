using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weather;

/// <summary>
/// Everything the user can tune. Edited live from the settings window (UI thread) and read every
/// frame by the snow thread; plain fields of primitive types, so that is safe enough.
/// </summary>
internal sealed class Settings
{
    // Falling snow
    public int SnowfallRate { get; set; } = 55;        // flakes per second across a 1920px-wide screen
    public int FlakeSize { get; set; } = 100;          // %
    public int FlakeBrightness { get; set; } = 70;     // % opacity of the nearest flakes
    public int Wind { get; set; } = 22;                // px/s, negative blows left
    public int Gusts { get; set; } = 50;               // % how much the wind varies

    // Settled snow
    public int BuildupSpeed { get; set; } = 60;        // %
    public int Stickiness { get; set; } = 30;          // % 0 = flows and shakes off easily, 100 = stays put
    public int MaxDepth { get; set; } = 30;            // px: deepest snow can pile on a window
    public int FlingSensitivity { get; set; } = 50;    // % how easily a quick move throws snow
    public bool TaskbarSnow { get; set; } = true;
    public int TaskbarSnowOffset { get; set; } = 0;    // px: nudge the taskbar snow down (+) or up (-) on PCs where it sits off

    // Clearing
    public int WipeSeconds { get; set; } = 0;          // 0 = never

    // When to hide
    public bool HideWhenMaximized { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;

    // Seasons
    public SeasonChoice Season { get; set; } = SeasonChoice.Auto;
    public bool SouthernHemisphere { get; set; }
    public int WinterAmount { get; set; } = 100;       // % how much falls in each season
    public int SpringAmount { get; set; } = 100;
    public int SummerAmount { get; set; } = 100;
    public int AutumnAmount { get; set; } = 100;
    public bool PlayWithMouse { get; set; } = true;    // Ctrl+Alt + mouse: snowballs, snowmen, kicking leaves…
    public bool AutoPlow { get; set; } = true;         // the plough / leaf blower clears a deep taskbar by itself
    public bool Showers { get; set; } = true;          // spring
    public bool Butterflies { get; set; } = true;      // spring and summer
    public bool Meadow { get; set; } = true;           // grass and flowers on the taskbar, spring and summer
    public bool Fireflies { get; set; } = true;        // summer nights
    public bool DustDevils { get; set; } = true;       // autumn

    // Performance
    public bool BatterySaver { get; set; } = true;     // on battery: 30 fps, less falling

    // Updates
    public bool CheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; }

    public bool WelcomeShown { get; set; }

    [JsonIgnore] public volatile bool Paused;

    public void ResetToDefaults()
    {
        var d = new Settings();
        foreach (var p in typeof(Settings).GetProperties())
            if (p.CanWrite) p.SetValue(this, p.GetValue(d));
    }

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    static readonly string OldFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.OldName);

    static readonly string FilePath = Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            // Carry settings over from before the app was renamed (WeatherSnow). They still get the
            // welcome card: the other three seasons are new to them.
            string old = Path.Combine(OldFolder, "settings.json");
            if (!File.Exists(FilePath) && File.Exists(old))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(old), Json) ?? new Settings();
                s.WelcomeShown = false;
                s.Save();
                return s;
            }
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch { /* fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
