using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Controls;

/// <summary>A live, geometry-faithful preview using the same overlay builder as viewport manipulators.</summary>
public sealed class GizmoPreview : Control
{
    public static readonly StyledProperty<CadSession?> SessionProperty = AvaloniaProperty.Register<GizmoPreview, CadSession?>(nameof(Session));
    public static readonly StyledProperty<int> OperationProperty = AvaloniaProperty.Register<GizmoPreview, int>(nameof(Operation), -1);
    public CadSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public int Operation { get => GetValue(OperationProperty); set => SetValue(OperationProperty, value); }
    readonly CadSession _preview = new();
    static GizmoPreview() => AffectsRender<GizmoPreview>(SessionProperty, OperationProperty);
    public GizmoPreview()
    {
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { if (Session != null) Session.ViewChanged += Refresh; };
        DetachedFromVisualTree += (_, _) => { if (Session != null) Session.ViewChanged -= Refresh; };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SessionProperty || !this.IsAttachedToVisualTree()) return;
        if (change.OldValue is CadSession old) old.ViewChanged -= Refresh;
        if (change.NewValue is CadSession next) next.ViewChanged += Refresh;
    }
    void Refresh() => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double scale = 1;
        var camera = _preview.Camera; camera.Width = Bounds.Width; camera.Height = Bounds.Height; camera.Orthographic = true;
        int active = Operation >= 0 ? Operation : Session?.Sketch is { } sk ? (int)sk.GizmoOperation
            : Session?.ActiveCommand?.Params.OfType<ChoiceParam>().FirstOrDefault(p => p.Label.StartsWith("Gizmo"))?.Index ?? 0;
        var overlay = new Overlay();
        for (int operation = 0; operation < 3; operation++) GizmoVisual.DrawOperation(overlay, _preview, Vec3.Zero, GizmoVisual.WorldAxes, operation, active);
        using var transform = context.PushTransform(Matrix.Identity);
        Point? Project(Vec3 p) => camera.Project(p) is { } q ? new Point(q.x, q.y) : null;
        foreach (var triangle in overlay.Tris)
        {
            if (Project(triangle.A) is not { } a || Project(triangle.B) is not { } b || Project(triangle.C) is not { } c) continue;
            var shape = new StreamGeometry(); using (var geometry = shape.Open()) { geometry.BeginFigure(a, true); geometry.LineTo(b); geometry.LineTo(c); geometry.EndFigure(true); }
            context.DrawGeometry(new SolidColorBrush(Color.FromUInt32(triangle.Color)), null, shape);
        }
        foreach (var line in overlay.Lines)
            if (Project(line.A) is { } a && Project(line.B) is { } b) context.DrawLine(new Pen(new SolidColorBrush(Color.FromUInt32(line.Color)), line.Width), a, b);
        foreach (var dot in overlay.Dots)
            if (Project(dot.P) is { } p) context.DrawEllipse(new SolidColorBrush(Color.FromUInt32(dot.Color)), null, p, dot.Size / 2, dot.Size / 2);
        foreach (var label in overlay.Labels)
            if (Project(label.P) is { } p) context.DrawText(new FormattedText(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12 / scale, new SolidColorBrush(Color.FromUInt32(GizmoVisual.Ink))), p);
    }
}
