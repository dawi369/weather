using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Weather;

/// <summary>
/// Self-update from the repo's "Latest build" GitHub release. Builds made by the GitHub workflow are
/// marked official and carry the commit they were built from (InformationalVersion "1.0.0+&lt;sha&gt;");
/// an update is available when the release was built from a different commit. Builds made on a PC
/// are not official and never update themselves.
/// </summary>
internal static class Updater
{
    const string Repo = "dawi369/weather";
    const string ReleaseTag = "latest";
    const string AssetName = "Windowsill.exe";

    static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(30));
    static readonly HttpClient Download = CreateClient(TimeSpan.FromMinutes(10));

    static HttpClient CreateClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Windowsill-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Full version string, e.g. "1.0.0+3f2a…".</summary>
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    /// <summary>Built by the GitHub workflow (as opposed to on someone's PC).</summary>
    public static bool IsOfficialBuild { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "OfficialBuild" && a.Value == "true") == true;

    /// <summary>The commit this official build was made from, or null for a local build.</summary>
    public static string? BuildCommit
    {
        get
        {
            if (!IsOfficialBuild) return null;
            int plus = Version.IndexOf('+');
            if (plus < 0) return null;
            string sha = Version[(plus + 1)..];
            return Regex.IsMatch(sha, "^[0-9a-f]{40}$") ? sha : null;
        }
    }

    /// <summary>Only a GitHub-built exe running as a plain .exe can replace itself.</summary>
    public static bool CanSelfUpdate =>
        BuildCommit != null &&
        Environment.ProcessPath is string p &&
        p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    public sealed record Release(string Commit, string DownloadUrl, DateTimeOffset Published);

    /// <summary>The current "Latest build" release, or null if there isn't one / it can't be read.</summary>
    public static async Task<Release?> GetLatestAsync()
    {
        string json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/tags/{ReleaseTag}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? commit = root.TryGetProperty("target_commitish", out var tc) ? tc.GetString() : null;
        if (commit == null || !Regex.IsMatch(commit, "^[0-9a-f]{40}$"))
        {
            // Fall back to the commit written in the release notes ("Built automatically from <sha>.").
            string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var m = Regex.Match(body, "[0-9a-f]{40}");
            commit = m.Success ? m.Value : null;
        }

        string? url = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if (a.GetProperty("name").GetString() == AssetName)
                    url = a.GetProperty("browser_download_url").GetString();

        DateTimeOffset published = root.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
            ? pa.GetDateTimeOffset() : DateTimeOffset.MinValue;

        return commit != null && url != null ? new Release(commit, url, published) : null;
    }

    public static bool IsNewer(Release r) =>
        BuildCommit != null && !r.Commit.Equals(BuildCommit, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Download the new exe next to the running one, swap them (a running exe can be renamed, not
    /// overwritten), and start the new one. The caller then exits; the new instance waits for it.
    /// </summary>
    public static async Task InstallAsync(Release r)
    {
        string exe = Environment.ProcessPath!;
        string fresh = exe + ".new", old = exe + ".old";

        using (var resp = await Download.GetAsync(r.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            await src.CopyToAsync(dst, 1 << 20); // big buffers: the exe is ~50 MB
        }
        if (new FileInfo(fresh).Length < 1_000_000) throw new InvalidDataException("The downloaded file is too small to be the app.");

        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try { File.Move(fresh, exe); }
        catch { File.Move(old, exe); throw; } // put the running version back if the swap failed

        Process.Start(new ProcessStartInfo(exe, "--after-update") { UseShellExecute = false });
    }

    /// <summary>Remove the previous version left behind by an update.</summary>
    public static void CleanUpOldVersion()
    {
        try
        {
            if (Environment.ProcessPath is string exe && File.Exists(exe + ".old")) File.Delete(exe + ".old");
        }
        catch { /* still locked or not ours to delete: try again next time */ }
    }
}
