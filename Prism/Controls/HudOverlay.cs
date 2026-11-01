using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Prism.Core;
using Prism.Rendering;

namespace Prism.Controls;

/// <summary>Draws text labels (dimensions while sketching) over the viewport at projected 3D positions.</summary>
public sealed class HudOverlay : Control
{
    public static readonly StyledProperty<GlViewport?> ViewportProperty = AvaloniaProperty.Register<HudOverlay, GlViewport?>(nameof(Viewport));
    public GlViewport? Viewport { get => GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    GlViewport? _hooked;

    static HudOverlay()
    {
        ViewportProperty.Changed.AddClassHandler<HudOverlay>((h, e) =>
        {
            if (h._hooked != null) h._hooked.FrameRendered -= h.InvalidateVisual;
            h._hooked = e.NewValue as GlViewport;
            if (h._hooked != null) h._hooked.FrameRendered += h.InvalidateVisual;
        });
        IsHitTestVisibleProperty.OverrideDefaultValue<HudOverlay>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var vp = Viewport; var s = vp?.Session;
        if (vp == null || s == null) return;
        foreach (var r in vp.LastOverlay.Rects)
        {
            var rect = new Rect(Math.Min(r.X0, r.X1), Math.Min(r.Y0, r.Y1), Math.Abs(r.X1 - r.X0), Math.Abs(r.Y1 - r.Y0));
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 31, 111, 235)), new Pen(new SolidColorBrush(Color.FromRgb(31, 111, 235)), 1, DashStyle.Dash), rect);
        }
        foreach (var path in vp.LastOverlay.ScreenPaths)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(path[0].x, path[0].y), true);
                for (int i = 1; i < path.Count; i++) g.LineTo(new Point(path[i].x, path[i].y));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 31, 111, 235)), new Pen(new SolidColorBrush(Color.FromRgb(31, 111, 235)), 1.5, DashStyle.Dash), geometry);
        }
        foreach (var l in vp.LastOverlay.Labels)
        {
            var p = (vp.Cam ?? s.Camera).Project(l.P);
            if (p == null) continue;
            var ft = new FormattedText(l.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Medium), 12, Brushes.White);
            if (l.Style is 4 or 5)
            {
                var mt = new FormattedText(l.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 12.5, Brushes.White);
                var mr = new Rect(p.Value.x - mt.Width / 2 - 8, p.Value.y - mt.Height / 2 - 4, mt.Width + 16, mt.Height + 8);
                bool typing = l.Style == 5;
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(240, 20, 28, 44)), new Pen(new SolidColorBrush(Color.Parse(typing ? "#FFC53D" : "#5C7AA6")), typing ? 1.8 : 1), mr, 6, 6);
                ctx.DrawText(mt, new Point(mr.X + 8, mr.Y + 4));
                continue;
            }
            if (l.Style is 2 or 3 or 6)
            {
                bool bad = l.Style == 3, picked = l.Style == 6;
                var gt = new FormattedText(l.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol, Inter", FontStyle.Normal, FontWeight.Normal), 11, new SolidColorBrush(Color.Parse("#CFE0FF")));
                var gr = new Rect(p.Value.x - 10, p.Value.y - 10, 20, 20);
                ctx.DrawRectangle(new SolidColorBrush(bad ? Color.FromArgb(240, 92, 28, 34) : picked ? Color.FromArgb(245, 88, 64, 12) : Color.FromArgb(235, 30, 44, 70)), new Pen(new SolidColorBrush(Color.Parse(bad ? "#FF6B6F" : picked ? "#FFC53D" : "#4C94FF")), bad || picked ? 2 : 1), gr, 5, 5);
                ctx.DrawText(gt, new Point(gr.X + (20 - gt.Width) / 2, gr.Y + (20 - gt.Height) / 2));
                continue;
            }
            if (l.Style == 1)
            {
                var dt = new FormattedText(l.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 12.5, new SolidColorBrush(Color.Parse("#E9F1FF")));
                var dr = new Rect(p.Value.x - dt.Width / 2 - 8, p.Value.y - dt.Height / 2 - 4, dt.Width + 16, dt.Height + 8);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(240, 22, 36, 62)), new Pen(new SolidColorBrush(Color.Parse("#4C94FF")), 1.2), dr, 6, 6);
                ctx.DrawText(dt, new Point(dr.X + 8, dr.Y + 4));
                continue;
            }
            var rect = new Rect(p.Value.x + 12, p.Value.y - 28, ft.Width + 16, ft.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(225, 30, 42, 59)), null, rect, 6, 6);
            ctx.DrawText(ft, new Point(rect.X + 8, rect.Y + 4));
        }
    }
}
