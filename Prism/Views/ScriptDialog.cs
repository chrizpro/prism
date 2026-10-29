using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prism.Views;

/// <summary>
/// A dialog a script can ask for: a list of fields (number, text, checkbox, choice) and OK / Cancel.
/// Fields are JSON: {key, label, type: number|text|check|choice, value, min, max, step, options, unit}.
/// </summary>
public static class ScriptDialog
{
    static IBrush Res(string key, IBrush fallback) => Application.Current is { } a && a.TryFindResource(key, a.ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    public static async Task<JsonObject?> ShowAsync(string title, JsonArray fields)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var win = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Res("PanelBg", Brushes.DimGray),
        };
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
        var getters = new List<(string key, Func<JsonNode?> get)>();
        foreach (var f in fields.OfType<JsonObject>())
        {
            string key = f["key"]?.ToString() ?? "value", label = f["label"]?.ToString() ?? key, type = (f["type"]?.ToString() ?? "text").ToLowerInvariant();
            var val = f["value"];
            Control editor;
            switch (type)
            {
                case "number":
                {
                    var nud = new NumericUpDown
                    {
                        Value = val != null ? (decimal?)val.GetValue<double>() : 0, FormatString = "0.######", ShowButtonSpinner = false, ParsingNumberStyle = NumberStyles.Float,
                        Minimum = f["min"] != null ? (decimal)f["min"]!.GetValue<double>() : decimal.MinValue / 2, Maximum = f["max"] != null ? (decimal)f["max"]!.GetValue<double>() : decimal.MaxValue / 2,
                        Increment = f["step"] != null ? (decimal)f["step"]!.GetValue<double>() : 1, MinHeight = 30, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
                    };
                    nud.Classes.Add("field");
                    getters.Add((key, () => (double)(nud.Value ?? 0)));
                    var unit = f["unit"]?.ToString();
                    editor = unit is { Length: > 0 } ? new DockPanel { Children = { new TextBlock { Text = unit, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right }, nud } } : nud;
                    break;
                }
                case "check":
                {
                    var cb = new CheckBox { Content = label, IsChecked = val != null && val.GetValue<bool>() };
                    getters.Add((key, () => cb.IsChecked == true));
                    panel.Children.Add(cb); continue;
                }
                case "choice":
                {
                    var opts = (f["options"] as JsonArray)?.Select(o => o!.ToString()).ToList() ?? [];
                    var cb = new ComboBox { ItemsSource = opts, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
                    cb.Classes.Add("field");
                    int idx = val != null ? opts.IndexOf(val.ToString()) : 0; cb.SelectedIndex = Math.Max(idx, 0);
                    getters.Add((key, () => cb.SelectedItem?.ToString()));
                    editor = cb; break;
                }
                default:
                {
                    var tb = new TextBox { Text = val?.ToString() ?? "", MinHeight = 30 };
                    tb.Classes.Add("field");
                    getters.Add((key, () => tb.Text ?? ""));
                    editor = tb; break;
                }
            }
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 });
            row.Children.Add(editor);
            panel.Children.Add(row);
        }
        JsonObject? result = null;
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("primary");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Classes.Add("ghost");
        ok.Click += (_, _) => { result = new JsonObject(); foreach (var (k, g) in getters) result[k] = g(); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { cancel, ok } });
        win.Content = panel;
        if (owner != null) await win.ShowDialog(owner); else { var tcs = new TaskCompletionSource(); win.Closed += (_, _) => tcs.TrySetResult(); win.Show(); await tcs.Task; }
        return result;
    }
}
