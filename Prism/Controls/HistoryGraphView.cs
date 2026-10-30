using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Controls;

/// <summary>Scrollable branch lanes with real focusable buttons and shared parent connectors.</summary>
public sealed class HistoryGraphView : UserControl
{
    const double Pitch = 180, Row = 64, NodeWidth = 150;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top };
    readonly Canvas _canvas = new();
    readonly Canvas _labels = new();
    readonly Border _laneGutter = new() { Width = 144, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, ClipToBounds = true, IsHitTestVisible = false };
    CadSession? _session;
    IReadOnlyList<HistoryItem> _items = [];
    Button? _current;
    public bool Compact { get; set; }
    public event Action<HistoryItem>? Navigate;
    public event Action<HistoryItem>? Edit;
    public event Action<HistoryItem?>? Hover;
    public event EventHandler<ContextRequestedEventArgs>? StepContext;

    public HistoryGraphView()
    {
        var layout = new Grid { ClipToBounds = true };
        layout.Children.Add(_scroll); layout.Children.Add(_laneGutter);
        Content = layout; _scroll.Content = _canvas; _laneGutter.Child = _labels;
        _scroll.ScrollChanged += (_, _) => AlignLaneLabels();
        _canvas.LayoutUpdated += (_, _) => AlignLaneLabels();
        _scroll.PointerExited += (_, _) => Hover?.Invoke(null);
        ActualThemeVariantChanged += (_, _) => { if (_session != null) SetHistory(_session, _items); };
        _scroll.SizeChanged += (_, _) => RevealCurrent();
        EffectiveViewportChanged += (_, _) => RevealCurrent();
        PointerWheelChanged += (_, e) =>
        {
            if (!Compact) return;
            _scroll.Offset = new Vector(Math.Max(0, _scroll.Offset.X - (e.Delta.X + e.Delta.Y) * 72), 0);
            e.Handled = true;
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppSettings.Changed += OnSettingsChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppSettings.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    void OnSettingsChanged() => Dispatcher.UIThread.Post(() => { if (_session != null) SetHistory(_session, _items); });

    IBrush Brush(string name, string fallback) => this.TryFindResource(name, ActualThemeVariant, out var resource) && resource is IBrush b ? b : new SolidColorBrush(Color.Parse(fallback));

    public void SetHistory(CadSession session, IReadOnlyList<HistoryItem> items)
    {
        Hover?.Invoke(null);
        _session = session; _items = items; _canvas.Children.Clear(); _labels.Children.Clear(); _current = null;
        var graph = HistoryGraphLayout.Build(session.Root);
        var (past, future) = session.HistoryPath();
        var entries = Compact ? past.Concat(future).Select((n, i) => new HistoryGraphLayout.Entry(n, 0, i)).ToList() : graph.Entries;
        var lookup = items.ToDictionary(i => i.Node);
        var positions = entries.ToDictionary(e => e.Node);
        IBrush ink = Brush("Ink", "#E3EAF3"), muted = Brush("InkSoft", "#A6B4C6"), panel = Brush("PanelBg", "#252E3A"), line = Brush("Line", "#455165"), accent = Brush("Accent", "#71BDF1"), gold = Brush("Gold", "#DCAD61");
        _scroll.Background = panel;
        _laneGutter.IsVisible = !Compact; _laneGutter.Background = panel;
        IBrush[] colors = [accent, gold, new SolidColorBrush(Color.Parse("#A294D9")), new SolidColorBrush(Color.Parse("#66B8A6"))];
        double gutter = Compact ? 8 : 150, pitch = Compact ? 132 : Pitch, nodeWidth = Compact ? 116 : NodeWidth;
        double width = gutter + (Compact ? entries.Count : graph.ColumnCount) * pitch + 12, height = Compact ? 46 : graph.LaneCount * Row + 20;
        _scroll.HorizontalScrollBarVisibility = Compact ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = Compact ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _canvas.Width = width; _canvas.Height = height;
        var wires = new GraphWires { Width = width, Height = height, IsHitTestVisible = false };
        _canvas.Children.Add(wires);
        for (int lane = 0; !Compact && lane < graph.LaneCount; lane++)
        {
            var color = colors[lane % colors.Length];
            var label = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            label.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = color, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var branch = graph.Entries.FirstOrDefault(e => e.Lane == lane && e.Node.Label.StartsWith("Branch: ", StringComparison.Ordinal));
            label.Children.Add(new TextBlock { Text = branch?.Node.Pin ?? (lane == 0 ? "Main design" : $"Alternative {lane}"), Foreground = color, FontSize = 12, MaxWidth = 116, TextTrimming = TextTrimming.CharacterEllipsis });
            Canvas.SetLeft(label, 16); Canvas.SetTop(label, lane * Row + 31); _labels.Children.Add(label);
        }
        foreach (var entry in entries)
        {
            if (!lookup.TryGetValue(entry.Node, out var item)) continue;
            double x = gutter + entry.Column * pitch, y = Compact ? 7 : 16 + entry.Lane * Row;
            var color = colors[entry.Lane % colors.Length];
            if (entry.Node.Parent is { } parent)
            {
                var p = positions[parent];
                wires.Edges.Add((new Point(gutter + p.Column * pitch + nodeWidth, Compact ? 23 : 38 + p.Lane * Row), new Point(x, y + (Compact ? 16 : 22)), color, p.Lane != entry.Lane));
            }
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions(Compact ? "16,*,Auto" : "22,*"), ColumnSpacing = Compact ? 5 : 8 };
            content.Children.Add(new IconView { Icon = item.Icon, Width = Compact ? 16 : 20, Height = Compact ? 16 : 20, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var text = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Label, FontSize = Compact ? 11 : 12, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.IsPinned && !Compact) text.Children.Add(new TextBlock { Text = "◆ " + item.Pin, FontSize = 10, Foreground = gold, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1); content.Children.Add(text);
            if (Compact && (item.IsPinned || entry.Node.Children.Count > 1))
            {
                var marker = new TextBlock { Text = item.IsPinned ? "◆" : "+" + entry.Node.Children.Count, FontSize = 10, Foreground = item.IsPinned ? gold : accent, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                Grid.SetColumn(marker, 2); content.Children.Add(marker);
            }
            var button = new Button
            {
                DataContext = item, Content = content, Width = nodeWidth, Height = Compact ? 32 : 44,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Padding = new Thickness(Compact ? 7 : 10, 3), CornerRadius = new CornerRadius(Compact ? 4 : 6),
                Background = item.IsCurrent ? Brush("AccentSoft", "#304E6C") : panel,
                BorderBrush = item.IsCurrent ? accent : item.IsPinned ? gold : line,
                BorderThickness = new Thickness(item.IsCurrent ? 2 : 1),
                Opacity = item.IsFuture ? 0.45 : item.OnPath || item.IsCurrent ? 1 : 0.72,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            AutomationProperties.SetName(button, item.Label + (item.IsCurrent ? ", current step" : "") + (item.IsPinned ? ", checkpoint " + item.Pin : ""));
            ToolTip.SetTip(button, item.ToolTipText + (entry.Node.Children.Count > 1 ? $"\n{entry.Node.Children.Count} continuations — expand to explore alternatives" : "") + "\nClick to revisit · Right-click for actions");
            button.Click += (_, _) => Navigate?.Invoke(item);
            button.DoubleTapped += (_, e) => { Edit?.Invoke(item); e.Handled = true; };
            button.PointerEntered += (_, _) => Hover?.Invoke(item);
            button.PointerExited += (_, _) => Hover?.Invoke(null);
            button.ContextRequested += (s, e) => StepContext?.Invoke(s, e);
            Canvas.SetLeft(button, x); Canvas.SetTop(button, y); _canvas.Children.Add(button);
            if (item.IsCurrent) _current = button;
        }
        // Extents are not valid until layout completes (especially after expand).
        Dispatcher.UIThread.Post(RevealCurrent, DispatcherPriority.Loaded);
    }

    void RevealCurrent()
    {
        if (_current == null || !IsEffectivelyVisible || _scroll.Viewport.Width <= 0) return;
        double x = Canvas.GetLeft(_current), y = Canvas.GetTop(_current);
        double left = _scroll.Offset.X, top = _scroll.Offset.Y;
        double inset = Compact ? 8 : _laneGutter.Width + 8;
        if (x < left + inset) left = Math.Max(0, x - inset);
        else if (x + _current.Width > left + _scroll.Viewport.Width - 8) left = x + _current.Width - _scroll.Viewport.Width + 8;
        if (y < top) top = y;
        else if (y + _current.Height > top + _scroll.Viewport.Height) top = y + _current.Height - _scroll.Viewport.Height;
        _scroll.Offset = new Vector(Math.Max(0, left), Math.Max(0, top));
    }

    void AlignLaneLabels()
    {
        // Account for the ScrollViewer template's content inset as well as scrolling.
        if (!Compact && _canvas.TranslatePoint(default, this) is { } origin)
            _labels.RenderTransform = new TranslateTransform(0, origin.Y);
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
