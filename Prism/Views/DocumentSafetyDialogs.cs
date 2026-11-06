using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prism.Core;

namespace Prism.Views;

public sealed class SaveChangesWindow : Window
{
    public SaveChangesWindow(string name, string? path = null, bool pendingEdit = false)
    {
        Title = "Save changes?"; Width = 510; SizeToContent = SizeToContent.Height;
        CanResize = false; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Save changes to “" + name + "”?", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = path ?? "This document has not been saved yet.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = pendingEdit ? "Finish or cancel the current edit before saving. Don't save discards this document's changes when Prism closes." : "Your changes will be lost if you don't save them.", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var item in new[] { ("Don't save", DocumentCloseDecision.Discard), ("Cancel", DocumentCloseDecision.Cancel), ("Save", DocumentCloseDecision.Save) })
        {
            var button = new Button { Content = item.Item1, Padding = new Thickness(16, 8), IsCancel = item.Item2 == DocumentCloseDecision.Cancel, IsDefault = item.Item2 == DocumentCloseDecision.Save };
            if (item.Item2 == DocumentCloseDecision.Save) button.Classes.Add("primary");
            var choice = item.Item2;
            button.Click += (_, _) => Close(choice);
            actions.Children.Add(button);
        }
        panel.Children.Add(actions); Content = panel;
    }
}

public sealed class RecoveryFilesWindow : Window
{
    public RecoveryFilesWindow(RecoveryStore store)
    {
        Title = "Recover autosaved work"; Width = 660; Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var heading = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "Recover autosaved work", FontSize = 20, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Open a recovery copy as a new unsaved document. Your original file is kept.", TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var list = new ListBox();
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var open = new Button { Content = "Recover selected", IsEnabled = false, Padding = new Thickness(14, 8) };
        open.Classes.Add("primary");
        try
        {
            var entries = store.List(); list.ItemsSource = entries;
            if (entries.Count > 0) list.SelectedIndex = 0;
            else note.Text = "No recovery copies yet. Enable autosave in Settings → Workspace to create them.";
        }
        catch (Exception e) { note.Text = "Could not read recovery copies: " + e.Message; }
        open.IsEnabled = list.SelectedItem is RecoveryEntry;
        list.SelectionChanged += (_, _) => open.IsEnabled = list.SelectedItem is RecoveryEntry;
        open.Click += (_, _) => { if (list.SelectedItem is RecoveryEntry entry) Close(entry.Path); };
        var close = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 8) };
        close.Click += (_, _) => Close(null);
        var folder = new Button { Content = "Open recovery folder", Padding = new Thickness(14, 8) };
        folder.Click += (_, _) =>
        {
            try { System.IO.Directory.CreateDirectory(store.Folder); Process.Start(new ProcessStartInfo(store.Folder) { UseShellExecute = true }); }
            catch (Exception e) { note.Text = "Could not open the folder: " + e.Message; }
        };
        var bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(note);
        bottom.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { folder, close, open } });
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        panel.Children.Add(list); Content = panel;
    }
}
