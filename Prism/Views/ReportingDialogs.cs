using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prism.Core;

namespace Prism.Views;

/// <summary>Local prompts only; report composition and submission happen on GitHub.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/reporting/README.md"/>
public static class ReportingDialogs
{
    public static bool OpenBrowser(string url)
    {
        try
        {
            // A successful shell handoff to an existing browser can return no new Process.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception) { return false; } // OS errors may contain private paths; never show their text.
    }

    internal static StackPanel Layout(Window window, string title, string description)
    {
        window.Title = title;
        window.Width = 560;
        window.SizeToContent = SizeToContent.Height;
        window.CanResize = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current is { } app && app.TryFindResource("PanelBg", app.ActualThemeVariant, out var bg) && bg is IBrush brush)
            window.Background = brush;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        window.Content = panel;
        return panel;
    }
    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, MinHeight = 36, Padding = new Thickness(14, 6), HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Classes.Add(primary ? "primary" : "ghost");
        return button;
    }
    internal static StackPanel Actions(params Control[] controls)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        foreach (var control in controls) actions.Children.Add(control);
        return actions;
    }
}

public sealed class FeedbackWindow : Window
{
    public bool Disabled => disable.IsChecked == true;
    readonly CheckBox disable = new() { Content = "Don't show again" };
    public FeedbackWindow(Func<string, bool>? openBrowser = null)
    {
        var panel = ReportingDialogs.Layout(this, "How was your time with Prism?",
            "Your feedback helps shape Prism. Share what worked, what felt difficult, or what you would improve.\n\nFeedback opens on GitHub. Reports are public and require a GitHub account.");
        panel.Children.Add(disable);
        var skip = ReportingDialogs.Button("Not now");
        skip.IsCancel = true;
        skip.Click += (_, _) => Close();
        var send = ReportingDialogs.Button("Give feedback", true);
        send.Click += (_, _) =>
        {
            (openBrowser ?? ReportingDialogs.OpenBrowser)(AppInfo.FeedbackUrl());
            Close(); // Browser or persistence failure must not prevent closure.
        };
        panel.Children.Add(ReportingDialogs.Actions(skip, send));
    }
}

public sealed class CrashReviewWindow : Window
{
    public CrashReviewWindow(CrashDiagnostic diagnostic, Func<string, bool>? openBrowser = null)
    {
        var panel = ReportingDialogs.Layout(this, "Review crash report",
            "Prism closed unexpectedly. Review the prepared stack trace below. File paths, exception messages and custom code names are omitted. Nothing is sent until you choose Continue to GitHub.\n\nGitHub reports are public and require an account. You can edit the draft before submitting.");
        panel.Children.Add(new TextBox
        {
            Text = diagnostic.Preview, IsReadOnly = true, AcceptsReturn = true, Height = 240,
            FontFamily = new FontFamily("monospace"), FontSize = 12,
        });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        panel.Children.Add(status);
        ApprovedCrashDiagnostic? approved = null;
        var copy = ReportingDialogs.Button("Copy approved trace");
        copy.IsVisible = false;
        copy.Click += async (_, _) =>
        {
            if (approved == null) return;
            try
            {
                if (Clipboard == null) throw new InvalidOperationException();
                await Clipboard.SetTextAsync(approved.Text);
                status.Text = "Approved trace copied. Open GitHub from Help → Report a bug and paste it into your report.";
            }
            catch (Exception) { status.Text = "Could not copy the trace. You can dismiss this dialog and report the crash from Help."; }
        };
        var dismiss = ReportingDialogs.Button("Dismiss");
        dismiss.IsCancel = true;
        dismiss.Click += (_, _) => Close();
        var send = ReportingDialogs.Button("Continue to GitHub", true);
        send.Click += (_, _) =>
        {
            approved = diagnostic.Approve();
            if (approved.GitHubUrl() is { } url && (openBrowser ?? ReportingDialogs.OpenBrowser)(url)) { Close(); return; }
            status.Text = "Could not open the GitHub draft. You can copy the approved trace and paste it into a bug report, or dismiss this dialog.";
            status.IsVisible = copy.IsVisible = true;
        };
        panel.Children.Add(ReportingDialogs.Actions(copy, dismiss, send));
    }
}
