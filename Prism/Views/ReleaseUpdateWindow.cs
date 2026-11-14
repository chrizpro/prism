using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prism.Core;

namespace Prism.Views;

public sealed class ReleaseUpdateWindow : Window
{
    public ReleaseUpdateWindow(string installer, Action<string> openDownload, ReleaseUpdates.Update? available = null)
    {
        Title = "Prism updates";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock { Text = $"Checking for updates to Prism {AppInfo.Version}…", TextWrapping = TextWrapping.Wrap };
        var close = new Button { Content = "Close", IsCancel = true };
        var download = new Button { Content = "Download update", IsVisible = false };
        var disable = new Button { Content = "Turn off startup update checks", IsVisible = available != null };
        download.Classes.Add("primary");
        close.Click += (_, _) => Close();
        disable.Click += (_, _) =>
        {
            var settings = AppSettings.Current;
            bool previous = settings.CheckForUpdatesOnStartup;
            settings.CheckForUpdatesOnStartup = false;
            if (AppSettings.TrySave(false)) Close();
            else
            {
                settings.CheckForUpdatesOnStartup = previous;
                message.Text = "Prism couldn’t save the preference. Try again or change it in Settings.";
            }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 16,
            Children =
            {
                message,
                disable,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { close, download } },
            },
        };

        void Offer(ReleaseUpdates.Update update)
        {
            message.Text = $"Prism {update.Tag.TrimStart('v')} is available. You have {AppInfo.Version}. Download the installer, save your work and close Prism before running it.";
            close.Content = "Not now";
            download.Click += (_, _) =>
            {
                Close();
                openDownload(update.DownloadUrl);
            };
            download.IsVisible = true;
        }

        if (available != null)
        {
            Offer(available);
            return;
        }

        Opened += async (_, _) =>
        {
            using var cancellation = new CancellationTokenSource();
            EventHandler cancel = (_, _) => cancellation.Cancel();
            Closed += cancel;
            try
            {
                var update = await ReleaseUpdates.Official.FindAsync(AppInfo.Version, installer, cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                if (update == null)
                {
                    message.Text = $"Prism {AppInfo.Version}. No newer compatible installer was found.";
                    return;
                }
                Offer(update);
            }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested)
                    message.Text = "Prism couldn’t check GitHub Releases. Check your connection or try again later.";
            }
            finally { Closed -= cancel; }
        };
    }
}
