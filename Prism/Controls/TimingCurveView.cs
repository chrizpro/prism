using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prism.Commands;

namespace Prism.Controls;

public sealed class TimingCurveView : Control
{
    TimingCurveParam? _curve;
    int _handle;
    const double Pad = 16;

    public TimingCurveView()
    {
        Height = 150;
        MinWidth = 160;
        ClipToBounds = true;
        DataContextChanged += (_, _) =>
        {
            if (_curve != null) _curve.PropertyChanged -= Changed;
            _curve = DataContext as TimingCurveParam;
            if (_curve != null) _curve.PropertyChanged += Changed;
            InvalidateVisual();
        };
    }

    void Changed(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    Point At(double x, double y) => new(Pad + x * Math.Max(1, Bounds.Width - 2 * Pad), Bounds.Height - Pad - y * Math.Max(1, Bounds.Height - 2 * Pad));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_curve is not { } curve) return;
        var grid = new Pen(Brushes.Gray, .5);
        for (int i = 0; i <= 4; i++)
        {
            context.DrawLine(grid, At(i / 4.0, 0), At(i / 4.0, 1));
            context.DrawLine(grid, At(0, i / 4.0), At(1, i / 4.0));
        }
        var first = At(curve.X1, curve.Y1);
        var second = At(curve.X2, curve.Y2);
        var handlePen = new Pen(Brushes.SlateGray, 1);
        context.DrawLine(handlePen, At(0, 0), first);
        context.DrawLine(handlePen, At(1, 1), second);
        var path = new StreamGeometry();
        using (var drawing = path.Open())
        {
            drawing.BeginFigure(At(0, 0), false);
            drawing.CubicBezierTo(first, second, At(1, 1));
            drawing.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brushes.DodgerBlue, 2.5), path);
        context.DrawEllipse(Brushes.DodgerBlue, null, first, 6, 6);
        context.DrawEllipse(Brushes.MediumPurple, null, second, 6, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_curve is not { } curve || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        double DistanceSquared(Point handle) => (point.X - handle.X) * (point.X - handle.X) + (point.Y - handle.Y) * (point.Y - handle.Y);
        double first = DistanceSquared(At(curve.X1, curve.Y1));
        double second = DistanceSquared(At(curve.X2, curve.Y2));
        if (Math.Min(first, second) > 144) return;
        _handle = first <= second ? 1 : 2;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_curve is not { } curve || _handle == 0) return;
        var point = e.GetPosition(this);
        double x = Math.Clamp((point.X - Pad) / Math.Max(1, Bounds.Width - 2 * Pad), 0, 1);
        double y = Math.Clamp((Bounds.Height - Pad - point.Y) / Math.Max(1, Bounds.Height - 2 * Pad), 0, 1);
        if (_handle == 1) { curve.X1 = x; curve.Y1 = y; }
        else { curve.X2 = x; curve.Y2 = y; }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _handle = 0;
        e.Pointer.Capture(null);
    }
}
