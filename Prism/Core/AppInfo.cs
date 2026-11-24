using System;
using System.Reflection;

namespace Prism.Core;

/// <summary>Version, release stage and the public links shown in the header and Help menu.</summary>
public static class AppInfo
{
    /// <summary>Prism is not stable yet. Shown next to the wordmark; remove when the app leaves alpha.</summary>
    public const string Stage = "Alpha";

    public const string RepositoryUrl = "https://github.com/chrizpro/prism";
    public const string DocsUrl = "https://chrizpro.github.io/prism/docs/";
    public const string KnownIssuesUrl = DocsUrl + "reference/known-issues";
    public static string FeedbackUrl() => RepositoryUrl + "/issues/new?template=feedback.yml";
    public const string DownloadUrl = "https://chrizpro.github.io/prism/download";

    /// <summary>True in the WebAssembly build. Shows the web-version mark in the model view.</summary>
    /// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/browser/README.md"/>
    public static bool IsWebVersion => OperatingSystem.IsBrowser();

    /// <summary>License notices and LGPL source served next to the web version; set by Prism.Browser from the page address.</summary>
    public static string WebNoticesUrl { get; set; } = RepositoryUrl + "/blob/develop/docs/interface/browser/README.md#licensing";

    /// <summary>How the web version differs from the desktop app; listed in the web-version mark's flyout.</summary>
    public static string[] WebVersionDifferences { get; } =
    [
        "Runs slower and has less memory than the desktop app.",
        "Files open and save through your browser. Nothing is kept after you close the tab unless you save it.",
        "Simulation (meshing and solving) needs the desktop app.",
        "Scripts, the AI Assistant and the MCP bridge need the desktop app.",
        "Video export and sending to a slicer need the desktop app.",
    ];

    /// <summary>The package version the build was made with (Version in the project, or -p:Version from packaging).</summary>
    public static string Version { get; } = ReadVersion();

    public static string DisplayVersion => Version.Contains('-') ? Version : $"{Version} {Stage.ToLowerInvariant()}";

    public static string StageTip =>
        $"Prism {DisplayVersion}. Prism is still in alpha: features can change and you may run into bugs. Keep backups of your work. Click to see known issues.";

    /// <summary>The bug form on GitHub, with the version and system filled in.</summary>
    public static string BugReportUrl()
    {
        var system = CrashDiagnostic.SystemInfo;
        return $"{RepositoryUrl}/issues/new?template=bug_report.yml" +
               $"&version={Uri.EscapeDataString(DisplayVersion)}&system={Uri.EscapeDataString(system)}";
    }

    static string ReadVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Drop the "+commit" suffix the SDK appends.
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? assembly.GetName().Version?.ToString(3) ?? "0.1.0" : version;
    }
}
