using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prism.Commands;
using Prism.Core.Assembly;

namespace Prism.Controls;

/// <summary>
/// A traditional animation timeline: a time ruler, one row per joint value (and the exploded view) with diamond keys,
/// and a draggable playhead. Click or drag the ruler to scrub, drag a diamond to retime it, double-click a row to add a key,
/// Delete removes the selected key.
/// </summary>
public sealed class AnimationTimelineView : Control
{
    const double LabelWidth = 168, RulerHeight = 26, RowHeight = 24, Pad = 14, KeyRadius = 6;

    AnimationCommand? _command;
    IReadOnlyList<AnimationCommand.TimelineRow> _rows = [];
    AnimTrack? _selTrack; AnimKey? _selKey;
    enum Drag { None, Playhead, Key }
    Drag _drag;

    public static readonly StyledProperty<AnimationCommand?> CommandProperty = AvaloniaProperty.Register<AnimationTimelineView, AnimationCommand?>(nameof(Command));
    public AnimationCommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    public AnimationTimelineView()
    {
        Focusable = true; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Arrow);
    }

    static AnimationTimelineView()
    {
        CommandProperty.Changed.AddClassHandler<AnimationTimelineView>((view, e) => view.Attach(e.NewValue as AnimationCommand));
        AffectsRender<AnimationTimelineView>(CommandProperty);
    }

    void Attach(AnimationCommand? command)
    {
        if (_command != null) _command.TimelineChanged -= OnTimelineChanged;
        _command = command; _selTrack = null; _selKey = null;
        if (_command != null) _command.TimelineChanged += OnTimelineChanged;
        OnTimelineChanged();
    }

    void OnTimelineChanged()
    {
        _rows = _command?.Rows() ?? [];
        if (_selTrack != null && (_command == null || !_rows.Any(r => ReferenceEquals(r.Track, _selTrack)) || !_selTrack.Keys.Contains(_selKey!))) { _selTrack = null; _selKey = null; }
        InvalidateMeasure(); InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, RulerHeight + Math.Max(1, _rows.Count) * RowHeight + 6);

    IBrush Res(string name, string fallback) => this.TryFindResource(name, ActualThemeVariant, out var r) && r is IBrush b ? b : new SolidColorBrush(Color.Parse(fallback));

    double Duration => Math.Max(_command?.Duration ?? 5, 0.1);
    double TrackWidth => Math.Max(Bounds.Width - LabelWidth - Pad * 2, 40);
    double X(double t) => LabelWidth + Pad + t / Duration * TrackWidth;
    double T(double x) => Math.Clamp((x - LabelWidth - Pad) / TrackWidth * Duration, 0, Duration);
    double RowTop(int i) => RulerHeight + i * RowHeight;

    FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
        => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    public override void Render(DrawingContext dc)
    {
        IBrush ink = Res("Ink", "#E3EAF3"), soft = Res("InkSoft", "#A6B4C6"), panel = Res("PanelBg", "#252E3A"), line = Res("Line", "#455165"), accent = Res("Accent", "#71BDF1"), gold = Res("Gold", "#DCAD61");
        var w = Bounds.Width; var h = Bounds.Height;
        dc.DrawRectangle(panel, null, new Rect(0, 0, w, h));
        if (_command == null) return;
        var linePen = new Pen(line, 1);
        // rows
        for (int i = 0; i < _rows.Count; i++)
        {
            double y = RowTop(i);
            if (i % 2 == 1) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), null, new Rect(0, y, w, RowHeight));
            var label = Text(_rows[i].Label, 12, _rows[i].Track == null ? soft : ink);
            dc.DrawText(label, new Point(10, y + (RowHeight - label.Height) / 2));
            dc.DrawLine(linePen, new Point(0, y + RowHeight), new Point(w, y + RowHeight));
        }
        if (_rows.Count == 0) dc.DrawText(Text("Create a joint to animate it.", 12, soft), new Point(LabelWidth + Pad, RulerHeight + 4));
        // ruler: nice tick spacing
        double[] steps = [0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60];
        double step = steps.FirstOrDefault(s => s / Duration * TrackWidth >= 58, steps[^1]);
        dc.DrawLine(linePen, new Point(0, RulerHeight), new Point(w, RulerHeight));
        for (double t = 0; t <= Duration + 1e-9; t += step)
        {
            double x = X(t);
            dc.DrawLine(linePen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            var tick = Text(t.ToString(step < 1 ? "0.0#" : "0", CultureInfo.InvariantCulture) + " s", 10.5, soft);
            dc.DrawText(tick, new Point(Math.Min(x + 3, w - tick.Width - 2), 3));
        }
        // the span of the animation
        dc.DrawLine(new Pen(line, 1, dashStyle: DashStyle.Dash), new Point(X(Duration), RulerHeight), new Point(X(Duration), h));
        // key spans (a bar between a row's first and last key) and keys
        for (int i = 0; i < _rows.Count; i++)
        {
            var track = _rows[i].Track; if (track == null || track.Keys.Count == 0) continue;
            double cy = RowTop(i) + RowHeight / 2;
            double first = track.Keys.Min(k => k.T), last = track.Keys.Max(k => k.T);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 113, 189, 241)), null, new Rect(X(first), cy - 2.5, Math.Max(X(last) - X(first), 1), 5), 2, 2);
            foreach (var k in track.Keys)
            {
                bool selected = ReferenceEquals(track, _selTrack) && ReferenceEquals(k, _selKey);
                double x = X(k.T);
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(x, cy - KeyRadius), true);
                    g.LineTo(new Point(x + KeyRadius, cy)); g.LineTo(new Point(x, cy + KeyRadius)); g.LineTo(new Point(x - KeyRadius, cy));
                    g.EndFigure(true);
                }
                dc.DrawGeometry(selected ? gold : k.T < 1e-6 ? soft : accent, new Pen(panel, 1.2), diamond);
            }
        }
        // playhead
        double px = X(_command.Time);
        var red = new SolidColorBrush(Color.Parse("#E5594F"));
        dc.DrawLine(new Pen(red, 1.5), new Point(px, 0), new Point(px, h));
        var cap = new StreamGeometry();
        using (var g = cap.Open())
        {
            g.BeginFigure(new Point(px - 6, 0), true); g.LineTo(new Point(px + 6, 0)); g.LineTo(new Point(px + 6, 10)); g.LineTo(new Point(px, 17)); g.LineTo(new Point(px - 6, 10)); g.EndFigure(true);
        }
        dc.DrawGeometry(red, null, cap);
    }

    // ------------------------------------------------------------------ interaction
    (int row, AnimTrack track, AnimKey key)? KeyAt(Point p)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var track = _rows[i].Track; if (track == null) continue;
            double cy = RowTop(i) + RowHeight / 2;
            foreach (var k in track.Keys)
                if (Math.Abs(p.X - X(k.T)) <= KeyRadius + 2 && Math.Abs(p.Y - cy) <= KeyRadius + 3) return (i, track, k);
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_command == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var p = e.GetPosition(this);
        if (KeyAt(p) is { } hit)
        {
            _selTrack = hit.track; _selKey = hit.key;
            if (e.ClickCount >= 2) { _command.Time = hit.key.T; }
            else _drag = hit.key.T < 1e-6 ? Drag.None : Drag.Key;
            if (_drag == Drag.Key) e.Pointer.Capture(this);
            InvalidateVisual(); e.Handled = true; return;
        }
        if (p.Y >= RulerHeight && e.ClickCount >= 2 && p.X > LabelWidth)
        {
            int row = (int)((p.Y - RulerHeight) / RowHeight);
            if (row >= 0 && row < _rows.Count) { _command.AddKey(_rows[row], T(p.X)); e.Handled = true; return; }
        }
        _selTrack = null; _selKey = null;
        if (p.X >= LabelWidth - 4 || p.Y < RulerHeight)
        {
            _drag = Drag.Playhead; e.Pointer.Capture(this);
            _command.Time = T(p.X);
        }
        InvalidateVisual(); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_command == null) return;
        var p = e.GetPosition(this);
        if (_drag == Drag.Playhead) _command.Time = Math.Round(T(p.X), 2);
        else if (_drag == Drag.Key && _selTrack != null && _selKey != null)
        {
            double t = T(p.X); if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)) t = Math.Round(t * 20) / 20;
            var before = _selKey;
            _command.MoveKey(_selTrack, before, t);
            _selKey = _selTrack.Keys.FirstOrDefault(k => Math.Abs(k.T - Math.Round(Math.Clamp(t, 0.01, _command.Duration), 2)) < 1e-6) ?? _selKey;
        }
        else Cursor = new Cursor(KeyAt(p) != null ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None; e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_command == null) return;
        if (e.Key is Key.Delete or Key.Back && _selTrack != null && _selKey != null) { var t = _selTrack; var k = _selKey; _selTrack = null; _selKey = null; _command.DeleteKey(t, k); e.Handled = true; }
        else if (e.Key == Key.Space) { _command.TogglePlayback(); e.Handled = true; }
        else if (e.Key == Key.Home) { _command.Time = 0; e.Handled = true; }
        else if (e.Key == Key.End) { _command.Time = _command.Duration; e.Handled = true; }
        else if (e.Key == Key.Left) { _command.Time = Math.Round(_command.Time - 0.1, 2); e.Handled = true; }
        else if (e.Key == Key.Right) { _command.Time = Math.Round(_command.Time + 0.1, 2); e.Handled = true; }
    }
}
