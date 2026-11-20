using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prism.Views;

/// <summary>A plain yes/no question for actions that cannot be undone. Closes with true for the action button, false for Cancel.</summary>
public sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string action)
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height;
        CanResize = false; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 8), IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var go = new Button { Content = action, Padding = new Thickness(16, 8), IsDefault = true };
        go.Classes.Add("primary");
        go.Click += (_, _) => Close(true);
        actions.Children.Add(cancel); actions.Children.Add(go);
        panel.Children.Add(actions); Content = panel;
    }
}
