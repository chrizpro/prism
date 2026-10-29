using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prism.Controls;

/// <summary>One entry of a marking menu.</summary>
public sealed record MarkItem(string Label, string Icon, Action Run, bool Enabled = true);

/// <summary>
/// A radial "marking" menu: eight entries around the pointer. Hold the right button still, drag toward an entry and let go (the gesture becomes muscle memory),
/// or tap the right button and click an entry. Entries come from the context (what is selected, whether a sketch or command is open).
/// Radial menus keep contextual commands close to the pointer.
/// </summary>
public sealed class MarkingMenu : Canvas
{
    const double Radius = 134, DeadZone = 34, PillW = 128, PillH = 36;
    readonly List<(Border pill, MarkItem? item)> _slots = [];
    Point _center;
    bool _open, _hold;
    int _hot = -1;

    /// <summary>Raised when the menu closes (so the viewport can give the pointer back).</summary>
    public event Action? Closed;

    public MarkingMenu()
    {
        IsHitTestVisible = false; IsVisible = false; Background = Brushes.Transparent; ZIndex = 50;
        PointerMoved += (_, e) => { if (_open && !_hold) MoveTo(e.GetPosition(this)); };
        PointerPressed += (_, e) =>
        {
            if (!_open || _hold) return;
            var p = e.GetPosition(this); MoveTo(p);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _hot >= 0) Execute(); else Close();
            e.Handled = true;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    public bool IsOpen => _open;

    static IBrush Res(string key, IBrush fallback) => Application.Current is { } a && a.TryFindResource(key, a.ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    // slot k sits k * 45 degrees clockwise from straight up
    static Point SlotCentre(Point c, int k) { double a = k * Math.PI / 4; return new Point(c.X + Radius * Math.Sin(a), c.Y - Radius * Math.Cos(a)); }

    public void Open(Point where, IReadOnlyList<MarkItem?> items, bool hold)
    {
        Close(silent: true);
        var host = (Parent as Control)?.Bounds ?? Bounds;          // this control is hidden (and so not laid out) until it opens: use the space it will fill
        double w = host.Width, h = host.Height, m = Radius + PillW / 2 + 8, mv = Radius + PillH / 2 + 8;
        _center = new Point(Math.Clamp(where.X, Math.Min(m, w / 2), Math.Max(w - m, w / 2)), Math.Clamp(where.Y, Math.Min(mv, h / 2), Math.Max(h - mv, h / 2)));
        _hold = hold; _hot = -1; _open = true;
        Children.Clear(); _slots.Clear();
        var ink = Res("Ink", Brushes.White); var line = Res("Line", Brushes.Gray);
        var ring = new Ellipse { Width = 2 * Radius, Height = 2 * Radius, Stroke = Res("InkFaint", Brushes.Gray), StrokeThickness = 1.2, IsHitTestVisible = false };
        SetLeft(ring, _center.X - Radius); SetTop(ring, _center.Y - Radius); Children.Add(ring);
        var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromArgb(200, 255, 197, 61)), IsHitTestVisible = false };
        SetLeft(dot, _center.X - 5); SetTop(dot, _center.Y - 5); Children.Add(dot);
        for (int k = 0; k < 8; k++)
        {
            var item = k < items.Count ? items[k] : null;
            var sp = SlotCentre(_center, k);
            var pill = new Border
            {
                Width = PillW, Height = PillH, CornerRadius = new CornerRadius(PillH / 2), BorderThickness = new Thickness(1), IsHitTestVisible = false,
                Background = Res("PopupBg", Brushes.DimGray), BorderBrush = line,
                Opacity = item == null ? 0.35 : item.Enabled ? 1 : 0.5,
            };
            if (item != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (item.Icon.Length == 0) row.Children.Add(new TextBlock { Text = "☰", FontSize = 15, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                else row.Children.Add(new IconView { Icon = item.Icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = item.Label.Replace("\n", " "), FontSize = 12.5, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                pill.Child = row;
            }
            SetLeft(pill, sp.X - PillW / 2); SetTop(pill, sp.Y - PillH / 2); Children.Add(pill);
            _slots.Add((pill, item));
        }
        IsVisible = true; IsHitTestVisible = !hold; Focusable = true;
        if (!hold) Focus();
    }

    void Highlight()
    {
        for (int k = 0; k < _slots.Count; k++)
        {
            var (pill, item) = _slots[k]; bool on = k == _hot && item is { Enabled: true };
            pill.Background = on ? Res("Accent", Brushes.DodgerBlue) : Res("PopupBg", Brushes.DimGray);
            pill.BorderBrush = on ? Res("Accent", Brushes.DodgerBlue) : Res("Line", Brushes.Gray);
        }
    }

    public void MoveTo(Point p)
    {
        if (!_open) return;
        double dx = p.X - _center.X, dy = p.Y - _center.Y, d = Math.Sqrt(dx * dx + dy * dy);
        int hot = -1;
        if (d > DeadZone)
        {
            double ang = Math.Atan2(dx, -dy); if (ang < 0) ang += 2 * Math.PI;               // 0 = up, clockwise
            hot = (int)Math.Round(ang / (Math.PI / 4)) % 8;
        }
        if (hot != _hot) { _hot = hot; Highlight(); }
    }

    /// <summary>Let go of the right button: run what is highlighted.</summary>
    public void Release(Point p)
    {
        if (!_open || !_hold) return;
        MoveTo(p);
        if (_hot >= 0 && _slots[_hot].item is { Enabled: true }) Execute(); else Close();
    }

    void Execute()
    {
        var item = _hot >= 0 ? _slots[_hot].item : null;
        Close();
        if (item is { Enabled: true }) { try { item.Run(); } catch (Exception) { } }
    }

    public void Close(bool silent = false)
    {
        bool was = _open;
        _open = false; _hold = false; _hot = -1; IsVisible = false; IsHitTestVisible = false; Children.Clear(); _slots.Clear();
        if (was && !silent) Closed?.Invoke();
    }
}
