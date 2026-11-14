using System.Drawing.Imaging;
using System.Reflection;

namespace Weather;

/// <summary>The app's name, version and icon.</summary>
internal static class AppInfo
{
    public const string Name = "Windowsill";
    /// <summary>Names used before the rename, so old settings and startup entries carry over.</summary>
    public const string OldName = "WeatherSnow";

    /// <summary>"2.0" style version for display.</summary>
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "dev";

    static Icon? _icon;
    /// <summary>The app icon (windows, dialogs).</summary>
    public static Icon Icon => _icon ??= LoadIcon();

    static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is string p && System.Drawing.Icon.ExtractAssociatedIcon(p) is Icon i && i.Width >= 16) return i;
        }
        catch { }
        using var bmp = Sprites.AppIcon(64);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>
    /// Write a multi-size .ico of the app icon (16 to 256 px, PNG-compressed entries).
    /// Run as <c>Windowsill.exe --make-icon path.ico</c>; the result is committed as the exe's icon.
    /// </summary>
    public static void WriteIcon(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using var bmp = Sprites.AppIcon(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }
        using var f = File.Create(path);
        using var w = new BinaryWriter(f);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}
