namespace Weather;

internal enum Season { Winter, Spring, Summer, Autumn }

/// <summary>What the user picked: a fixed season, or follow the calendar.</summary>
internal enum SeasonChoice { Auto, Winter, Spring, Summer, Autumn }

/// <summary>What settles on windows and the taskbar in a season.</summary>
internal enum Material : byte { Snow, Petals, Leaves, None }

internal static class SeasonInfo
{
    /// <summary>The season for today's date (meteorological seasons), flipped for the southern hemisphere.</summary>
    public static Season FromDate(DateTime date, bool southern)
    {
        int m = date.Month;
        if (southern) m = (m + 5) % 12 + 1;
        return m switch
        {
            12 or 1 or 2 => Season.Winter,
            3 or 4 or 5 => Season.Spring,
            6 or 7 or 8 => Season.Summer,
            _ => Season.Autumn,
        };
    }

    public static Season Resolve(SeasonChoice choice, bool southern) => choice switch
    {
        SeasonChoice.Winter => Season.Winter,
        SeasonChoice.Spring => Season.Spring,
        SeasonChoice.Summer => Season.Summer,
        SeasonChoice.Autumn => Season.Autumn,
        _ => FromDate(DateTime.Now, southern),
    };

    public static Material MaterialOf(Season s) => s switch
    {
        Season.Winter => Material.Snow,
        Season.Spring => Material.Petals,
        Season.Autumn => Material.Leaves,
        _ => Material.None,
    };

    public static string Name(Season s) => s.ToString();

    public static string Tagline(Season s) => s switch
    {
        Season.Winter => "Snow that settles, drifts and slides off your windows.",
        Season.Spring => "Cherry blossom, passing showers and the odd butterfly.",
        Season.Summer => "Drifting dandelion seeds by day, fireflies after dusk.",
        _ => "Tumbling leaves that pile up in the corners.",
    };

    /// <summary>Accent colour used across the UI for a season.</summary>
    public static Color Accent(Season s) => s switch
    {
        Season.Winter => Color.FromArgb(120, 182, 255),
        Season.Spring => Color.FromArgb(255, 142, 188),
        Season.Summer => Color.FromArgb(255, 196, 64),
        _ => Color.FromArgb(240, 128, 52),
    };

    public static (Color Top, Color Bottom) Sky(Season s) => s switch
    {
        Season.Winter => (Color.FromArgb(28, 44, 78), Color.FromArgb(70, 108, 160)),
        Season.Spring => (Color.FromArgb(70, 44, 92), Color.FromArgb(214, 128, 158)),
        Season.Summer => (Color.FromArgb(30, 72, 120), Color.FromArgb(244, 168, 74)),
        _ => (Color.FromArgb(52, 30, 30), Color.FromArgb(196, 98, 44)),
    };
}
