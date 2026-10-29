using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Controls;

/// <summary>Scrollable branch lanes with real focusable buttons and shared parent connectors.</summary>
public sealed class HistoryGraphView : UserControl
{
    const double Pitch = 180, Row = 64, NodeWidth = 150;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    readonly Canvas _canvas = new();
    CadSession? _session;
    IReadOnlyList<HistoryItem> _items = [];
    Button? _current;
    public event Action<HistoryItem>? Navigate;
    public event Action<HistoryItem>? Edit;
    public event Action<HistoryItem?>? Hover;
    public event EventHandler<ContextRequestedEventArgs>? StepContext;

    public HistoryGraphView()
    {
        Content = _scroll; _scroll.Content = _canvas;
        _scroll.PointerExited += (_, _) => Hover?.Invoke(null);
        ActualThemeVariantChanged += (_, _) => { if (_session != null) SetHistory(_session, _items); };
    }

    IBrush Brush(string name, string fallback) => this.TryFindResource(name, ActualThemeVariant, out var resource) && resource is IBrush b ? b : new SolidColorBrush(Color.Parse(fallback));

    public void SetHistory(CadSession session, IReadOnlyList<HistoryItem> items)
    {
        _session = session; _items = items; _canvas.Children.Clear(); _current = null;
        var graph = HistoryGraphLayout.Build(session.Root);
        var lookup = items.ToDictionary(i => i.Node);
        var positions = graph.Entries.ToDictionary(e => e.Node);
        IBrush ink = Brush("Ink", "#E3EAF3"), muted = Brush("InkSoft", "#A6B4C6"), panel = Brush("PanelBg", "#252E3A"), line = Brush("Line", "#455165"), accent = Brush("Accent", "#71BDF1"), gold = Brush("Gold", "#DCAD61");
        _scroll.Background = panel;
        IBrush[] colors = [accent, gold, new SolidColorBrush(Color.Parse("#A294D9")), new SolidColorBrush(Color.Parse("#66B8A6"))];
        const double gutter = 150;
        double width = gutter + graph.ColumnCount * Pitch + 30, height = graph.LaneCount * Row + 20;
        _canvas.Width = width; _canvas.Height = height;
        var wires = new GraphWires { Width = width, Height = height, IsHitTestVisible = false };
        _canvas.Children.Add(wires);
        for (int lane = 0; lane < graph.LaneCount; lane++)
        {
            var color = colors[lane % colors.Length];
            var label = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            label.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = color, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var branch = graph.Entries.FirstOrDefault(e => e.Lane == lane && e.Node.Label.StartsWith("Branch: ", StringComparison.Ordinal));
            label.Children.Add(new TextBlock { Text = branch?.Node.Pin ?? (lane == 0 ? "Main design" : $"Alternative {lane}"), Foreground = color, FontSize = 12, MaxWidth = 116, TextTrimming = TextTrimming.CharacterEllipsis });
            Canvas.SetLeft(label, 16); Canvas.SetTop(label, lane * Row + 31); _canvas.Children.Add(label);
        }
        foreach (var entry in graph.Entries)
        {
            if (!lookup.TryGetValue(entry.Node, out var item)) continue;
            double x = gutter + entry.Column * Pitch, y = 16 + entry.Lane * Row;
            var color = colors[entry.Lane % colors.Length];
            if (entry.Node.Parent is { } parent)
            {
                var p = positions[parent];
                wires.Edges.Add((new Point(gutter + p.Column * Pitch + NodeWidth, 38 + p.Lane * Row), new Point(x, y + 22), color, p.Lane != entry.Lane));
            }
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*"), ColumnSpacing = 8 };
            content.Children.Add(new IconView { Icon = item.Icon, Width = 20, Height = 20, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var text = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Label, FontSize = 12, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.IsPinned) text.Children.Add(new TextBlock { Text = "◆ " + item.Pin, FontSize = 10, Foreground = gold, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1); content.Children.Add(text);
            var button = new Button
            {
                DataContext = item, Content = content, Width = NodeWidth, Height = 44,
                Padding = new Thickness(10, 3), CornerRadius = new CornerRadius(6),
                Background = item.IsCurrent ? Brush("AccentSoft", "#304E6C") : panel,
                BorderBrush = item.IsCurrent ? accent : item.IsPinned ? gold : line,
                BorderThickness = new Thickness(item.IsCurrent ? 2 : 1),
                Opacity = item.IsFuture ? 0.45 : item.OnPath || item.IsCurrent ? 1 : 0.72,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            AutomationProperties.SetName(button, item.Label + (item.IsCurrent ? ", current step" : "") + (item.IsPinned ? ", checkpoint " + item.Pin : ""));
            ToolTip.SetTip(button, item.ToolTipText + "\nClick to revisit · Right-click for actions");
            button.Click += (_, _) => Navigate?.Invoke(item);
            button.DoubleTapped += (_, e) => { Edit?.Invoke(item); e.Handled = true; };
            button.PointerEntered += (_, _) => Hover?.Invoke(item);
            button.PointerExited += (_, _) => Hover?.Invoke(null);
            button.ContextRequested += (s, e) => StepContext?.Invoke(s, e);
            Canvas.SetLeft(button, x); Canvas.SetTop(button, y); _canvas.Children.Add(button);
            if (item.IsCurrent) _current = button;
        }
        // Keep the active branch visible after regeneration and history navigation.
        if (_current != null)
        {
            double x = Canvas.GetLeft(_current), y = Canvas.GetTop(_current);
            _scroll.Offset = new Vector(Math.Max(0, x + NodeWidth - Math.Max(500, _scroll.Viewport.Width) + 40), Math.Max(0, y - 30));
        }
    }

    sealed class GraphWires : Control
    {
        public List<(Point From, Point To, IBrush Color, bool Fork)> Edges { get; } = [];
        public override void Render(DrawingContext context)
        {
            foreach (var e in Edges)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(e.From, false);
                    if (e.Fork)
                    {
                        double bend = e.From.X + 15;
                        g.LineTo(new Point(bend, e.From.Y));
                        g.CubicBezierTo(new Point(bend + 8, e.From.Y), new Point(bend + 8, e.To.Y), new Point(bend + 18, e.To.Y));
                    }
                    g.LineTo(e.To); g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(e.Color, 1.7), geometry);
                context.DrawEllipse(e.Color, null, e.To, 3, 3);
            }
        }
    }
}
