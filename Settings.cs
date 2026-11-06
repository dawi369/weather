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
    public int Gusts { get; set; } = 65;               // % how much the wind varies

    // Settled snow
    public int BuildupSpeed { get; set; } = 100;       // %
    public int Stickiness { get; set; } = 30;          // % 0 = flows and shakes off easily, 100 = stays put
    public int MaxDepth { get; set; } = 40;            // px: deepest snow can pile on a window
    public int FlingSensitivity { get; set; } = 50;    // % how easily a quick move throws snow
    public bool TaskbarSnow { get; set; } = true;

    // Clearing
    public int WipeSeconds { get; set; } = 0;          // 0 = never

    // When to hide
    public bool HideWhenMaximized { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;

    [JsonIgnore] public volatile bool Paused;

    public void ResetToDefaults()
    {
        var d = new Settings();
        foreach (var p in typeof(Settings).GetProperties())
            if (p.CanWrite) p.SetValue(this, p.GetValue(d));
    }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeatherSnow", "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
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
