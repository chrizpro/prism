using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Prism.Core;

/// <summary>Finds newer official releases that include an installer for this desktop.</summary>
public sealed class ReleaseUpdates(HttpClient http)
{
    const string Api = "https://api.github.com/repos/chrizpro/prism/releases?per_page=100";
    static readonly Regex ReleaseTag = new(@"\Av?(\d+\.\d+\.\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z");
    public static ReleaseUpdates Official { get; } = new(new HttpClient { Timeout = TimeSpan.FromSeconds(12) });

    public sealed record Update(string Tag, string Asset)
    {
        public string DownloadUrl => AppInfo.RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(Tag) + "/" + Uri.EscapeDataString(Asset);
    }

    public static string? DesktopInstaller => (RuntimeInformation.ProcessArchitecture, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux()) switch
    {
        (Architecture.X64, true, _, _) => "Prism-windows-x64-Setup.exe",
        (Architecture.Arm64, _, true, _) => "Prism-macos-arm64.dmg",
        (Architecture.X64, _, true, _) => "Prism-macos-x64.dmg",
        (Architecture.X64, _, _, true) => "Prism-linux-x64.deb",
        _ => null,
    };

    public async Task<Update?> FindAsync(string installedVersion, string asset, CancellationToken cancellationToken = default)
    {
        var best = ParseVersion(installedVersion) ?? throw new FormatException("The installed Prism version cannot be compared.");
        Update? update = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        for (int page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + "&page=" + page);
            request.Headers.UserAgent.ParseAdd("Prism-UpdateCheck");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, deadline.Token);
            response.EnsureSuccessStatusCode();
            using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString();
                if (release.GetProperty("draft").GetBoolean() || tag == null || ParseVersion(tag) is not { } candidate || Compare(candidate, best) <= 0)
                    continue;
                foreach (var file in release.GetProperty("assets").EnumerateArray())
                {
                    if (file.GetProperty("name").GetString() != asset || file.GetProperty("state").GetString() != "uploaded")
                        continue;
                    best = candidate;
                    update = new Update(tag, asset);
                    break;
                }
            }
            if (releases.RootElement.GetArrayLength() < 100) return update;
        }
    }

    static (Version Number, string[] Label)? ParseVersion(string value)
    {
        var match = ReleaseTag.Match(value.Split('+')[0]);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var number)) return null;
        return (number, match.Groups[2].Success ? match.Groups[2].Value.Split('.') : []);
    }

    static int Compare((Version Number, string[] Label) a, (Version Number, string[] Label) b)
    {
        int order = a.Number.CompareTo(b.Number);
        if (order != 0) return order;
        if (a.Label.Length == 0 || b.Label.Length == 0) return b.Label.Length.CompareTo(a.Label.Length);
        for (int i = 0; i < Math.Min(a.Label.Length, b.Label.Length); i++)
        {
            var x = a.Label[i];
            var y = b.Label[i];
            bool xNumeric = x.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
            bool yNumeric = y.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
            if (xNumeric != yNumeric) return xNumeric ? -1 : 1;
            if (xNumeric)
            {
                x = x.TrimStart('0');
                y = y.TrimStart('0');
                order = x.Length.CompareTo(y.Length);
                if (order != 0) return order;
            }
            order = string.CompareOrdinal(x, y);
            if (order != 0) return order;
        }
        return a.Label.Length.CompareTo(b.Label.Length);
    }
}
