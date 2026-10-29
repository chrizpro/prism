using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Prism.Core;

namespace Prism.Controls;

/// <summary>Small procedural swatch, drawn locally without texture assets or a rendering session.</summary>
public sealed class FinishPreview : Control
{
    public static readonly StyledProperty<uint> ColorProperty = AvaloniaProperty.Register<FinishPreview, uint>(nameof(Color), 0xFFB9C6D6);
    public static readonly StyledProperty<double> GlossProperty = AvaloniaProperty.Register<FinishPreview, double>(nameof(Gloss), .35);
    public static readonly StyledProperty<SurfacePattern> PatternProperty = AvaloniaProperty.Register<FinishPreview, SurfacePattern>(nameof(Pattern));
    public uint Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Gloss { get => GetValue(GlossProperty); set => SetValue(GlossProperty, value); }
    public SurfacePattern Pattern { get => GetValue(PatternProperty); set => SetValue(PatternProperty, value); }
    static FinishPreview() => AffectsRender<FinishPreview>(ColorProperty, GlossProperty, PatternProperty);

    public override void Render(DrawingContext context)
    {
        double r = System.Math.Max(0, System.Math.Min(Bounds.Width, Bounds.Height) / 2 - 3);
        if (r == 0) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var color = Avalonia.Media.Color.FromUInt32(Color);
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(.35, .28, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.32, .22, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.85, RelativeUnit.Relative), RadiusY = new RelativeScalar(.85, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Avalonia.Media.Color.FromRgb(240, 245, 250), 0),
                new GradientStop(color, .25 + System.Math.Clamp(Gloss, 0, 1) * .15),
                new GradientStop(Avalonia.Media.Color.FromRgb((byte)(color.R * .25), (byte)(color.G * .25), (byte)(color.B * .25)), 1),
            },
        };
        var circle = new EllipseGeometry(new Rect(center.X - r, center.Y - r, r * 2, r * 2));
        context.DrawGeometry(brush, new Pen(new SolidColorBrush(0x55000000), 1), circle);
        // Studio softboxes reflected in the sphere: sharp for polished finishes, broad for satin.
        using (context.PushGeometryClip(circle))
        {
            double gloss = System.Math.Clamp(Gloss, 0, 1);
            var strip = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Avalonia.Media.Color.FromArgb((byte)(55 + gloss * 170), 255, 255, 255), .34),
                    new GradientStop(Avalonia.Media.Color.FromArgb((byte)(35 + gloss * 180), 255, 255, 255), .58),
                    new GradientStop(Colors.Transparent, 1),
                },
            };
            context.FillRectangle(strip, new Rect(center.X - r * .72, center.Y - r, r * (.85 - gloss * .48), r * 2));
            var band = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(Colors.Transparent, 0), new(Avalonia.Media.Color.FromArgb((byte)(gloss * 120), 14, 24, 40), .48), new(Avalonia.Media.Color.FromArgb((byte)(gloss * 100), 245, 250, 255), .7), new(Colors.Transparent, 1) },
            };
            context.FillRectangle(band, new Rect(center.X - r, center.Y, r * 2, r));
        }
        if (Pattern == SurfacePattern.Brushed || Pattern == SurfacePattern.Cast)
        {
            using var clip = context.PushGeometryClip(circle);
            var pen = new Pen(new SolidColorBrush(0x22FFFFFF), .6);
            double step = Pattern == SurfacePattern.Brushed ? 2 : 4;
            for (double y = center.Y - r; y < center.Y + r; y += step)
                context.DrawLine(pen, new Point(center.X - r, y), new Point(center.X + r, y + (Pattern == SurfacePattern.Cast ? 5 : 0)));
        }
    }
}
