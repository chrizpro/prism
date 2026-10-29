using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Views;

public partial class PropertiesPanel : UserControl
{
    static readonly uint[] Colors =
    [
        0xFFB9C6D6, 0xFFD8C3A5, 0xFFA8CBB5, 0xFFCDB4DB, 0xFFE3B7A0, 0xFF9FC5E8,
        0xFFE5484D, 0xFFF59E0B, 0xFF1E9E68, 0xFF2F78E4, 0xFF7B8AA0, 0xFF2F3E52,
    ];

    public PropertiesPanel()
    {
        InitializeComponent();
        foreach (var c in Colors)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0), CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromUInt32(c)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), BorderThickness = new Thickness(1),
                Tag = c,
            };
            b.Click += (_, _) => (DataContext as MainViewModel)?.SetColor(c);
            Swatches.Items.Add(b);
        }
    }

    void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { (DataContext as MainViewModel)?.Rename(RenameBox.Text ?? ""); e.Handled = true; }
    }

    void OnRenameLostFocus(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.Rename(RenameBox.Text ?? "");
}
