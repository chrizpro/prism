using System.Text.Json;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Reporting;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/reporting/README.md">Reporting specification.</seealso>
[Trait("Feature", "Reporting")]
[Trait("Layer", "Unit")]
public class ReportingTests
{
    [Fact(DisplayName = "RPT-U001 | Normal closures prompt at 1, 5, 15, 25 and 35 without submission tracking")]
    [Trait("TestId", "RPT-U001")]
    public void ReminderSequenceCountsCompletedSessions()
    {
        var preference = new FeedbackPreference();
        var prompts = new List<int>();
        for (int closure = 1; closure <= 40; closure++)
        {
            var session = new FeedbackSession(preference);
            if (session.BeginPrompt(false, true)) prompts.Add(closure);
            Assert.False(session.BeginPrompt(false, true));
            session.Complete(true);
            session.Complete(true); // Duplicate completed events count only once.
        }
        Assert.Equal(new[] { 1, 5, 15, 25, 35 }, prompts);
        Assert.Equal(40, preference.ClosureCount);
    }

    [Fact(DisplayName = "RPT-U002 | Cancelled and automated sessions do not consume a reminder or increment closures")]
    [Trait("TestId", "RPT-U002")]
    public void CancelledAndAutomatedRequestsDoNotCount()
    {
        var preference = new FeedbackPreference();
        var session = new FeedbackSession(preference);
        Assert.False(session.BeginPrompt(true, true));
        Assert.False(session.BeginPrompt(false, false));
        session.Complete(false);
        Assert.Equal(0, preference.ClosureCount);
        Assert.True(session.BeginPrompt(false, true));
        session.Complete(false); // A save handler can still cancel the retry.
        Assert.Equal(0, preference.ClosureCount);
        session.Complete(true);
        Assert.Equal(1, preference.ClosureCount);
    }

    [Fact(DisplayName = "RPT-U003 | Reminder count and Don't show again survive settings serialization")]
    [Trait("TestId", "RPT-U003")]
    public void PreferencesPersistWithoutIdentityOrSubmissionHistory()
    {
        var settings = new AppSettings { Feedback = new FeedbackPreference { ClosureCount = 14, Disabled = true } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(14, restored.Feedback.ClosureCount);
        Assert.False(restored.Feedback.IsDue);
        Assert.False(new FeedbackSession(restored.Feedback).BeginPrompt(false, true));
        var fields = JsonDocument.Parse(JsonSerializer.Serialize(restored.Feedback)).RootElement;
        Assert.Equal(new[] { "ClosureCount", "Disabled" }, fields.EnumerateObject().Select(x => x.Name));
        restored.Feedback.Disabled = false;
        Assert.True(restored.Feedback.IsDue);
    }

    [Theory(DisplayName = "RPT-U004 | Shared diagnostics omit private paths, messages and custom code names")]
    [Trait("TestId", "RPT-U004")]
    [InlineData(@"C:\Users\PrivatePerson\SecretProject\model.step")]
    [InlineData(@"\\private-server\customer\model.step")]
    [InlineData("/Users/private-person/customer/model.step")]
    [InlineData("/home/private-person/customer/model.step")]
    public void DiagnosticOmitsArbitraryExceptionText(string privatePath)
    {
        Exception inner;
        try { int.Parse(privatePath); throw new Exception("unreachable"); }
        catch (FormatException ex) { inner = ex; }
        var exception = new PrivateCustomerException(privatePath, inner);
        exception.Data["Document"] = privatePath;
        var diagnostic = CrashDiagnostic.Capture(exception);
        Assert.Contains("System.FormatException", diagnostic.Preview);
        Assert.Contains("[custom code omitted]", diagnostic.Preview);
        Assert.DoesNotContain(privatePath, diagnostic.Preview);
        Assert.DoesNotContain(nameof(PrivateCustomerException), diagnostic.Preview);
        Assert.DoesNotContain(nameof(DiagnosticOmitsArbitraryExceptionText), diagnostic.Preview);
        Assert.DoesNotContain(".cs:", diagnostic.Preview);
        var approved = diagnostic.Approve();
        Assert.Equal(diagnostic.Preview, approved.Text);
        Assert.Contains(Uri.EscapeDataString(diagnostic.Preview), approved.GitHubUrl()!);
    }

    [Fact(DisplayName = "RPT-U005 | Pending diagnostics are bounded, validated and consumed without reading raw logs")]
    [Trait("TestId", "RPT-U005")]
    public void PendingDiagnosticRejectsUntrustedFilesAndCanBeDismissed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prism-reporting-" + Guid.NewGuid());
        try
        {
            var store = new PendingCrashStore(directory);
            var diagnostic = CrashDiagnostic.Capture(new InvalidOperationException("private secret"));
            store.Save(diagnostic);
            Assert.Equal(diagnostic.Preview, store.Read()!.Preview);
            Assert.DoesNotContain("private secret", File.ReadAllText(Path.Combine(directory, "pending-crash.json")));
            store.Dismiss();
            Assert.Null(store.Read());
            foreach (string bad in new[] { @"C:\private\crash.log", "Prism 1.0\nWindows (X64)\n  at System.Code /home/secret/file.cs\n", new string('x', 45000) })
            {
                File.WriteAllText(Path.Combine(directory, "pending-crash.json"), JsonSerializer.Serialize(bad));
                Assert.Null(store.Read());
            }
            // A blocked directory must never replace the original exception with a reporting failure.
            new PendingCrashStore(Path.Combine(directory, "pending-crash.json")).Save(diagnostic);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact(DisplayName = "RPT-U006 | Oversized drafts require an explicit approved-copy fallback")]
    [Trait("TestId", "RPT-U006")]
    public void LargeDraftDoesNotProduceAnUnboundedBrowserUrl()
    {
        string text = "Prism 1.0\nWindows (X64)\n" + string.Concat(Enumerable.Repeat("  at System.A.B\n", 350));
        var diagnostic = CrashDiagnostic.Parse(text)!;
        Assert.NotNull(diagnostic);
        Assert.Null(diagnostic.Approve().GitHubUrl());
        Assert.Null(CrashDiagnostic.Parse(new string('a', CrashDiagnostic.MaxLength + 1)));
    }

    sealed class PrivateCustomerException(string privatePath, Exception inner) : Exception(privatePath, inner)
    {
        public override string Message => throw new Exception("Reporting must never access Message.");
        public override string ToString() => throw new Exception("Reporting must never format an exception.");
    }
}
