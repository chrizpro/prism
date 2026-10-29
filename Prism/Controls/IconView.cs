using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Prism.Controls;

/// <summary>Draws a vector icon from <see cref="Icons"/> scaled into a square.</summary>
public sealed class IconView : Control
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<IconView, string?>(nameof(Icon));
    public static readonly StyledProperty<bool> DimmedProperty = AvaloniaProperty.Register<IconView, bool>(nameof(Dimmed));
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool Dimmed { get => GetValue(DimmedProperty); set => SetValue(DimmedProperty, value); }

    static IconView()
    {
        AffectsRender<IconView>(IconProperty, DimmedProperty);
        AffectsMeasure<IconView>(IconProperty);
    }

    public IconView() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = double.IsNaN(Width) ? 32 : Width;
        return new Size(s, double.IsNaN(Height) ? s : Height);
    }

    public override void Render(DrawingContext ctx)
    {
        var shapes = Icons.Get(Icon);
        if (shapes == null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height) / 32.0;
        using var _ = ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - 32 * s) / 2, (Bounds.Height - 32 * s) / 2));
        using var __ = Dimmed ? ctx.PushOpacity(0.38) : default;
        foreach (var sh in shapes)
            ctx.DrawGeometry(sh.Fill, sh.Stroke, sh.Geometry);
    }
}
