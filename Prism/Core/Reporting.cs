using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Prism.Core;

/// <summary>Only local reminder state; no identity or submission history.</summary>
public sealed class FeedbackPreference
{
    public long ClosureCount { get; set; }
    public bool Disabled { get; set; }
    [JsonIgnore]
    public bool IsDue => !Disabled && (ClosureCount == 0 || ClosureCount == 4 || ClosureCount >= 14 && (ClosureCount - 14) % 10 == 0);
    public void RecordClosure() => ClosureCount = Math.Max(0, ClosureCount) == long.MaxValue ? long.MaxValue : Math.Max(0, ClosureCount) + 1;
}

/// <summary>One desktop session: cancelled and repeated requests never count as completed closures.</summary>
public sealed class FeedbackSession(FeedbackPreference preference)
{
    bool prompted, completed;
    public bool BeginPrompt(bool cancelled, bool interactive)
    {
        if (cancelled || !interactive || prompted || completed || !preference.IsDue) return false;
        prompted = true;
        return true;
    }
    public void Complete(bool normalInteractiveClosure)
    {
        if (completed || !normalInteractiveClosure) return;
        completed = true;
        preference.RecordClosure();
    }
}

/// <summary>A bounded diagnostic containing code identifiers, never exception text or file locations.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/reporting/README.md"/>
public sealed class CrashDiagnostic
{
    public const int MaxLength = 6000;
    static readonly Regex Identifier = new(@"^(System|Prism|Avalonia)(\.[A-Za-z0-9_+`]+)+$", RegexOptions.CultureInvariant);
    static readonly Regex VersionPattern = new(@"^[0-9][A-Za-z0-9.\-]{0,79}$", RegexOptions.CultureInvariant);
    static readonly System.Reflection.Assembly[] KnownAssemblies = [typeof(object).Assembly, typeof(Enumerable).Assembly,
        typeof(CrashDiagnostic).Assembly, typeof(Kernel.Occ).Assembly, typeof(Avalonia.Application).Assembly];
    public string Preview { get; }
    CrashDiagnostic(string preview) => Preview = preview;

    public static string SystemInfo => $"{(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Other")} ({RuntimeInformation.OSArchitecture})";

    static string SafeType(Type? type) => type != null && KnownAssemblies.Contains(type.Assembly) && Identifier.IsMatch(type.FullName ?? "")
        ? type.FullName! : "[custom code omitted]";

    public static CrashDiagnostic Capture(Exception? exception)
    {
        var text = new StringBuilder();
        text.AppendLine("Prism " + (VersionPattern.IsMatch(AppInfo.Version) ? AppInfo.Version : "unknown"));
        text.AppendLine(SystemInfo);
        // Do not call Exception.ToString/Message/Data or StackFrame.GetFileName, even for local persistence.
        for (int depth = 0; exception != null && depth < 4; depth++, exception = exception.InnerException)
        {
            text.AppendLine("Exception: " + SafeType(exception.GetType()));
            foreach (var frame in (new StackTrace(exception, false).GetFrames() ?? []).Take(24))
            {
                var method = frame.GetMethod();
                var type = SafeType(method?.DeclaringType);
                var name = method?.Name ?? "";
                string line = type != "[custom code omitted]" && Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,119}$")
                    ? type + "." + name : "[custom code omitted]";
                if (text.Length + line.Length + 8 > MaxLength) break;
                text.AppendLine("  at " + line);
            }
        }
        return Parse(text.ToString()) ?? new CrashDiagnostic("Prism unknown\nOther (Unknown)\nException: [custom code omitted]\n");
    }

    // Validate the persisted file too: never display arbitrary log contents as an approved diagnostic.
    public static CrashDiagnostic? Parse(string text)
    {
        if (text.Length > MaxLength) return null;
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length < 2 || !lines[0].StartsWith("Prism ", StringComparison.Ordinal)) return null;
        var version = lines[0][6..];
        if (version != "unknown" && !VersionPattern.IsMatch(version)) return null;
        if (!Regex.IsMatch(lines[1], @"^(Windows|macOS|Linux|Other) \((X64|X86|Arm64|Arm|Wasm|S390x|LoongArch64|Armv6|Ppc64le|RiscV64|Unknown)\)$")) return null;
        foreach (var line in lines.Skip(2))
        {
            string? token = line.StartsWith("Exception: ", StringComparison.Ordinal) ? line[11..] : line.StartsWith("  at ", StringComparison.Ordinal) ? line[5..] : null;
            if (token == null || token.Length > 500 || token != "[custom code omitted]" && !Identifier.IsMatch(token)) return null;
        }
        return new CrashDiagnostic(string.Join('\n', lines) + "\n");
    }

    /// <summary>Call only in response to the user approving this exact preview.</summary>
    public ApprovedCrashDiagnostic Approve() => new(this);
}

public sealed class ApprovedCrashDiagnostic
{
    internal ApprovedCrashDiagnostic(CrashDiagnostic diagnostic) => Text = diagnostic.Preview;
    public string Text { get; }
    public string? GitHubUrl()
    {
        var url = AppInfo.RepositoryUrl + "/issues/new?template=crash_report.yml&trace=" + Uri.EscapeDataString(Text);
        return url.Length <= 7800 ? url : null;
    }
}

/// <summary>Stores only the latest prepared diagnostic. All filesystem failures are non-fatal.</summary>
public sealed class PendingCrashStore(string directory)
{
    string FilePath => Path.Combine(directory, "pending-crash.json");
    public void Save(CrashDiagnostic diagnostic)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(diagnostic.Preview));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception) { }
    }
    public CrashDiagnostic? Read()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 40000) return null;
            var text = JsonSerializer.Deserialize<string>(File.ReadAllText(FilePath));
            return text == null ? null : CrashDiagnostic.Parse(text);
        }
        catch (Exception) { return null; }
    }
    public void Dismiss()
    {
        try { File.Delete(FilePath); } catch (Exception) { }
    }
    public static PendingCrashStore Local => new(Environment.GetEnvironmentVariable("PRISM_REPORTING_DIRECTORY")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism"));
}
