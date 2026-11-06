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

    /// <summary>The package version the build was made with (Version in the project, or -p:Version from packaging).</summary>
    public static string Version { get; } = ReadVersion();

    public static string DisplayVersion => $"{Version} {Stage.ToLowerInvariant()}";

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
