using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prism.Views;

/// <summary>
/// The desktop start-up card (v0.5, owner request 2026-11-23): a borderless 560 × 320 card centred on screen with the Prism mark, the name, the version,
/// the stage being loaded and a progress bar. App shows it before building the main window and moves the bar between stages; see docs/interface/startup.md.
/// The look matches the web version's loading page (Prism.Browser/wwwroot/index.html).
/// </summary>
public sealed class SplashWindow : Window
{
    static readonly IBrush Card = Brush.Parse("#142338"), Track = Brush.Parse("#24344D"), Bar = Brush.Parse("#39C8ED"),
        Ink = Brush.Parse("#EEF4FB"), Soft = Brush.Parse("#8FA3BF"), Faint = Brush.Parse("#5D7393");
    const double BarWidth = 488;
    readonly TextBlock _status;
    readonly Border _bar;

    public SplashWindow()
    {
        Title = "Prism";
        Width = 560; Height = 320;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowInTaskbar = true;

        var big = Mark(300); big.Opacity = 0.08;
        Canvas.SetLeft(big, 330); Canvas.SetTop(big, -50);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        heading.Children.Add(Mark(64));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "Prism", FontSize = 34, FontWeight = FontWeight.Medium, Foreground = Ink });
        words.Children.Add(new TextBlock { Text = "CAD for parts, surfaces and freeform", FontSize = 13, Foreground = Soft, Margin = new Thickness(0, 2, 0, 0) });
        heading.Children.Add(words);
        Canvas.SetLeft(heading, 36); Canvas.SetTop(heading, 40);

        _status = new TextBlock { Text = "Starting…", FontSize = 12, Foreground = Soft };
        var version = new TextBlock { Text = "v" + Core.AppInfo.DisplayVersion, FontSize = 12, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Width = BarWidth, Margin = new Thickness(0, 0, 0, 8) };
        line.Children.Add(_status);
        Grid.SetColumn(version, 1); line.Children.Add(version);
        _bar = new Border { Background = Bar, CornerRadius = new CornerRadius(2), Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Background = Track, CornerRadius = new CornerRadius(2), Height = 3, Width = BarWidth, Child = _bar };
        var footer = new TextBlock { Text = "Open source · OCCT geometry kernel", FontSize = 11, Foreground = Faint, Margin = new Thickness(0, 10, 0, 0) };
        var bottom = new StackPanel();
        bottom.Children.Add(line); bottom.Children.Add(track); bottom.Children.Add(footer);
        Canvas.SetLeft(bottom, 36); Canvas.SetTop(bottom, 232);

        var canvas = new Canvas { Width = 560, Height = 320, ClipToBounds = true };
        canvas.Children.Add(big); canvas.Children.Add(heading); canvas.Children.Add(bottom);
        Content = new Border { Background = Card, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = canvas };
    }

    /// <summary>The stage now loading and how far start-up is (0 to 1).</summary>
    public void SetStage(string stage, double fraction)
    {
        _status.Text = stage;
        _bar.Width = BarWidth * Math.Clamp(fraction, 0, 1);
    }

    public string Stage => _status.Text ?? "";
    public double Fraction => _bar.Width / BarWidth;

    /// <summary>The Prism mark (Assets/prism-mark.svg, viewBox 80 80 360 320 around its five facets) at the given width.</summary>
    static Control Mark(double width)
    {
        (string fill, string points)[] facets =
        [
            ("#7762ED", "232,94 294,148 418,380 356,326"),
            ("#354FA8", "108,326 356,326 418,380 170,380"),
            ("#B5F2FF", "232,94 108,326 232,250"),
            ("#39C8ED", "232,94 232,250 356,326"),
            ("#4088ED", "108,326 356,326 232,250"),
        ];
        var art = new Canvas { Width = 360, Height = 320 };
        foreach (var (fill, points) in facets)
        {
            var poly = new Polygon { Fill = Brush.Parse(fill) };
            foreach (var p in points.Split(' '))
            {
                var xy = p.Split(',');
                poly.Points.Add(new Point(double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture) - 80, double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture) - 80));
            }
            art.Children.Add(poly);
        }
        return new Viewbox { Width = width, Height = width * 320 / 360, Child = art };
    }
}
