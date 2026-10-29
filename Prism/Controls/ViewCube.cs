using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Controls;

/// <summary>
/// Orientation cube with 26 clickable regions (6 faces, 12 edges, 8 corners), animated view changes, drag-to-orbit,
/// 90° rotate arrows, a Home button and a perspective/orthographic toggle.
/// </summary>
public sealed class ViewCube : Control
{
    public static readonly StyledProperty<CadSession?> SessionProperty = AvaloniaProperty.Register<ViewCube, CadSession?>(nameof(Session));
    public CadSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }

    public static readonly StyledProperty<Rendering.GlViewport?> ViewportProperty = AvaloniaProperty.Register<ViewCube, Rendering.GlViewport?>(nameof(Viewport));
    /// <summary>The viewport this cube belongs to (several views can be open at once).</summary>
    public Rendering.GlViewport? Viewport { get => GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    Camera Cam => Viewport?.Cam ?? Session!.Camera;

    enum Kind { Face, Edge, Corner }
    sealed record Region(Vec3 Dir, Vec3[] Pts, Kind Kind, string? Label, Vec3 U, Vec3 V);

    // ---- geometry of the bevelled cube (half-size 1, bevel B)
    const double B = 0.30;
    static readonly Region[] Regions = BuildRegions();

    static Region[] BuildRegions()
    {
        var list = new List<Region>();
        double s = 1 - B;
        string[] names = ["FRONT", "BACK", "RIGHT", "LEFT", "TOP", "BOTTOM"];
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    int n = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
                    if (n == 0) continue;
                    var dir = new Vec3(x, y, z);
                    if (n == 1)
                    {
                        // Face: the inset square on that side.
                        (Vec3 u, Vec3 v, string name) = (x, y, z) switch
                        {
                            (0, -1, 0) => (Vec3.UnitX, Vec3.UnitZ, names[0]),
                            (0, 1, 0) => (-Vec3.UnitX, Vec3.UnitZ, names[1]),
                            (1, 0, 0) => (Vec3.UnitY, Vec3.UnitZ, names[2]),
                            (-1, 0, 0) => (-Vec3.UnitY, Vec3.UnitZ, names[3]),
                            (0, 0, 1) => (Vec3.UnitX, Vec3.UnitY, names[4]),
                            _ => (Vec3.UnitX, -Vec3.UnitY, names[5]),
                        };
                        var c = dir;
                        list.Add(new(dir, [c - u * s - v * s, c + u * s - v * s, c + u * s + v * s, c - u * s + v * s], Kind.Face, name, u, v));
                    }
                    else if (n == 2)
                    {
                        // Edge: a strip joining two inset face borders.
                        int zero = x == 0 ? 0 : y == 0 ? 1 : 2;
                        Vec3 Axis(int i) => i == 0 ? Vec3.UnitX : i == 1 ? Vec3.UnitY : Vec3.UnitZ;
                        int a = zero == 0 ? 1 : 0, b = zero == 2 ? 1 : 2;
                        if (zero == 1) { a = 0; b = 2; } else if (zero == 0) { a = 1; b = 2; }
                        double da = a == 0 ? x : a == 1 ? y : z, db = b == 0 ? x : b == 1 ? y : z;
                        Vec3 P1(double t) => Axis(a) * da + Axis(b) * (db * s) + Axis(zero) * t;
                        Vec3 P2(double t) => Axis(a) * (da * s) + Axis(b) * db + Axis(zero) * t;
                        list.Add(new(dir, [P1(-s), P1(s), P2(s), P2(-s)], Kind.Edge, null, Vec3.UnitX, Vec3.UnitY));
                    }
                    else
                    {
                        list.Add(new(dir, [new(x, y * s, z * s), new(x * s, y, z * s), new(x * s, y * s, z)], Kind.Corner, null, Vec3.UnitX, Vec3.UnitY));
                    }
                }
        return [.. list];
    }

    // ---- state
    enum Hit { None, Region, Left, Right, Up, Down, Home, Projection, RollCcw, RollCw }
    Hit _hit; int _hoverRegion = -1;
    Point _down; bool _dragging, _pressed; Hit _pressHit; int _pressRegion;
    CadSession? _hooked;
    static readonly Vec3 Light = new Vec3(-0.35, -0.55, 0.75).Normalized();

    static ViewCube()
    {
        SessionProperty.Changed.AddClassHandler<ViewCube>((v, e) =>
        {
            if (v._hooked != null) v._hooked.ViewChanged -= v.InvalidateVisual;
            v._hooked = e.NewValue as CadSession;
            if (v._hooked != null) v._hooked.ViewChanged += v.InvalidateVisual;
        });
        AffectsRender<ViewCube>(SessionProperty);
    }

    // ---- projection helpers
    Point Center => new(Bounds.Width / 2, Bounds.Height / 2 + 2);
    double K => Math.Min(Bounds.Width, Bounds.Height) * 0.245;

    Point P(Vec3 w, Camera cam) => new(Center.X + Vec3.Dot(w, cam.Right) * K, Center.Y - Vec3.Dot(w, cam.Up) * K);

    sealed record Proj(int Index, Region R, Point[] Poly, double Depth);

    List<Proj> Visible(Camera cam)
    {
        var res = new List<Proj>();
        var eye = cam.EyeDirection;
        for (int i = 0; i < Regions.Length; i++)
        {
            var r = Regions[i];
            if (Vec3.Dot(r.Dir.Normalized(), eye) < 0.03) continue;
            var poly = r.Pts.Select(p => P(p, cam)).ToArray();
            var c = r.Pts.Aggregate(Vec3.Zero, (a, b) => a + b) / r.Pts.Length;
            res.Add(new(i, r, poly, Vec3.Dot(c, cam.Forward)));
        }
        return res;
    }

    // ---- buttons laid out around the cube
    Rect HomeRect => new(6, 6, 24, 24);
    Rect RollCcwRect => new(Bounds.Width - 58, 6, 24, 24);
    Rect RollCwRect => new(Bounds.Width - 30, 6, 24, 24);
    Rect ProjRect => new(Bounds.Width - 30, Bounds.Height - 30, 24, 24);
    Point[] Arrow(Hit h)
    {
        double w = Bounds.Width, hgt = Bounds.Height, m = 11, a = 7;
        return h switch
        {
            Hit.Up => [new(w / 2 - a, m + 4), new(w / 2 + a, m + 4), new(w / 2, m - 5)],
            Hit.Down => [new(w / 2 - a, hgt - m - 4), new(w / 2 + a, hgt - m - 4), new(w / 2, hgt - m + 5)],
            Hit.Left => [new(m + 4, hgt / 2 - a), new(m + 4, hgt / 2 + a), new(m - 5, hgt / 2)],
            _ => [new(w - m - 4, hgt / 2 - a), new(w - m - 4, hgt / 2 + a), new(w - m + 5, hgt / 2)],
        };
    }

    static readonly Hit[] Arrows = [Hit.Up, Hit.Down, Hit.Left, Hit.Right];
    static bool IsFaceView(Camera cam)
    {
        var d = cam.EyeDirection;
        return Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))) > 0.99999;
    }

    // ---- drawing
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2F7BF0"));
    static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#2F3E52"));
    static readonly IBrush IconInk = new SolidColorBrush(Color.Parse("#D3DDEC"));
    static readonly Pen Outline = new(new SolidColorBrush(Color.Parse("#8693A6")), 1.0, lineJoin: PenLineJoin.Round);

    static StreamGeometry Poly(Point[] pts)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(pts[0], true);
        for (int i = 1; i < pts.Length; i++) gc.LineTo(pts[i]);
        gc.EndFigure(true);
        return g;
    }

    public override void Render(DrawingContext ctx)
    {
        var s = Session; if (s == null) return;
        var cam = Cam;

        // backdrop disc
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), new Pen(new SolidColorBrush(Color.FromArgb(70, 160, 175, 200)), 1),
            Center, Bounds.Width / 2 - 4, Bounds.Width / 2 - 4);

        // regions, far to near
        foreach (var p in Visible(cam).OrderByDescending(v => v.Depth))
        {
            bool hot = _hit == Hit.Region && p.Index == _hoverRegion;
            double lit = 0.5 + 0.5 * Math.Max(0, Vec3.Dot(p.R.Dir.Normalized(), Light));
            byte baseV = p.R.Kind == Kind.Face ? (byte)Math.Clamp(206 + 44 * lit, 0, 255) : (byte)Math.Clamp(172 + 40 * lit, 0, 255);
            IBrush fill = hot ? Accent : new SolidColorBrush(Color.FromRgb((byte)(baseV - 6), (byte)(baseV - 2), baseV));
            ctx.DrawGeometry(fill, Outline, Poly(p.Poly));

            if (p.R.Kind == Kind.Face && p.R.Label != null)
            {
                var c = p.R.Dir;
                var R = cam.Right; var U = cam.Up;
                var m = new Matrix(Vec3.Dot(p.R.U, R) * K, -Vec3.Dot(p.R.U, U) * K, -Vec3.Dot(p.R.V, R) * K, Vec3.Dot(p.R.V, U) * K, P(c, cam).X, P(c, cam).Y);
                using var _ = ctx.PushTransform(m);
                var ft = new FormattedText(p.R.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 0.30, hot ? Brushes.White : Ink);
                ctx.DrawText(ft, new Point(-ft.Width / 2, -ft.Height / 2));
            }
        }

        // rotate arrows
        foreach (var a in IsFaceView(cam) ? Arrows : [])
        {
            bool hot = _hit == a;
            ctx.DrawGeometry(hot ? Accent : new SolidColorBrush(Color.Parse("#A7B6CC")), null, Poly(Arrow(a)));
        }

        // roll: turn the picture about the line of sight
        if (IsFaceView(cam))
        {
            DrawButton(ctx, RollCcwRect, _hit == Hit.RollCcw, () => RollIcon(RollCcwRect, false));
            DrawButton(ctx, RollCwRect, _hit == Hit.RollCw, () => RollIcon(RollCwRect, true));
        }

        // home + projection buttons
        DrawButton(ctx, HomeRect, _hit == Hit.Home, () =>
        {
            var r = HomeRect;
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(r.X + 12, r.Y + 5), false);
                gc.LineTo(new Point(r.X + 5, r.Y + 12)); gc.LineTo(new Point(r.X + 7.5, r.Y + 12)); gc.LineTo(new Point(r.X + 7.5, r.Y + 19));
                gc.LineTo(new Point(r.X + 16.5, r.Y + 19)); gc.LineTo(new Point(r.X + 16.5, r.Y + 12)); gc.LineTo(new Point(r.X + 19, r.Y + 12));
                gc.EndFigure(true);
            }
            return g;
        });
        DrawButton(ctx, ProjRect, _hit == Hit.Projection, () =>
        {
            var r = ProjRect;
            var g = new StreamGeometry();
            using var gc = g.Open();
            if (s.Ortho)   // square = parallel projection
            {
                gc.BeginFigure(new Point(r.X + 6, r.Y + 6), true);
                gc.LineTo(new Point(r.X + 18, r.Y + 6)); gc.LineTo(new Point(r.X + 18, r.Y + 18)); gc.LineTo(new Point(r.X + 6, r.Y + 18));
            }
            else           // trapezoid = perspective
            {
                gc.BeginFigure(new Point(r.X + 9, r.Y + 6), true);
                gc.LineTo(new Point(r.X + 15, r.Y + 6)); gc.LineTo(new Point(r.X + 20, r.Y + 18)); gc.LineTo(new Point(r.X + 4, r.Y + 18));
            }
            gc.EndFigure(true);
            return g;
        });
    }

    /// <summary>A three-quarter circle with an arrow head, running clockwise or counter-clockwise.</summary>
    static Geometry RollIcon(Rect r, bool cw)
    {
        var c = r.Center; double rad = 6.2, a0 = cw ? -200 : -340, sweep = cw ? 250 : -250;
        Point At(double deg) => new(c.X + rad * Math.Cos(deg * Math.PI / 180), c.Y + rad * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using var gc = g.Open();
        var s = At(a0); var e = At(a0 + sweep);
        gc.BeginFigure(s, false);
        gc.ArcTo(e, new Size(rad, rad), 0, Math.Abs(sweep) > 180, cw ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
        gc.EndFigure(false);
        // arrow head at the end, pointing along the direction of travel
        double dir = (a0 + sweep + (cw ? 90 : -90)) * Math.PI / 180;
        Point H(double ang, double len) => new(e.X + len * Math.Cos(dir + ang), e.Y + len * Math.Sin(dir + ang));
        gc.BeginFigure(H(Math.PI - 0.5, 4.2), false); gc.LineTo(e); gc.LineTo(H(Math.PI + 0.5, 4.2)); gc.EndFigure(false);
        return g;
    }

    void DrawButton(DrawingContext ctx, Rect r, bool hot, Func<Geometry> icon)
    {
        ctx.DrawEllipse(hot ? new SolidColorBrush(Color.Parse("#2C4B7A")) : new SolidColorBrush(Color.FromArgb(215, 38, 46, 60)),
            new Pen(new SolidColorBrush(Color.Parse(hot ? "#4C94FF" : "#56657C")), 1), r.Center, r.Width / 2, r.Height / 2);
        ctx.DrawGeometry(null, new Pen(hot ? Accent : Ink, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon());
    }

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---- hit testing
    (Hit hit, int region) HitTest(Point pt)
    {
        var s = Session; if (s == null) return (Hit.None, -1);
        if (Dist(pt, HomeRect.Center) <= 12) return (Hit.Home, -1);
        if (Dist(pt, ProjRect.Center) <= 12) return (Hit.Projection, -1);
        if (IsFaceView(Cam) && Dist(pt, RollCcwRect.Center) <= 12) return (Hit.RollCcw, -1);
        if (IsFaceView(Cam) && Dist(pt, RollCwRect.Center) <= 12) return (Hit.RollCw, -1);
        foreach (var a in IsFaceView(Cam) ? Arrows : [])
        {
            var tri = Arrow(a);
            var c = new Point(tri.Average(t => t.X), tri.Average(t => t.Y));
            if (Dist(pt, c) <= 11) return (a, -1);
        }
        var hit = Visible(Cam).Where(v => Inside(v.Poly, pt)).OrderBy(v => v.Depth).FirstOrDefault();
        return hit != null ? (Hit.Region, hit.Index) : (Hit.None, -1);
    }

    static bool Inside(Point[] poly, Point p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    static string Tip(Hit h, int region, CadSession s) => h switch
    {
        Hit.Home => "Home (isometric view)",
        Hit.Projection => s.Ortho ? "Orthographic - click for perspective" : "Perspective - click for orthographic",
        Hit.RollCcw => "Roll view counter-clockwise 90°", Hit.RollCw => "Roll view clockwise 90°",
        Hit.Left => "Rotate view left 90°", Hit.Right => "Rotate view right 90°", Hit.Up => "Rotate view up 90°", Hit.Down => "Rotate view down 90°",
        Hit.Region => Regions[region].Kind switch
        {
            Kind.Face => Regions[region].Label!.Substring(0, 1) + Regions[region].Label!.Substring(1).ToLower() + " view",
            Kind.Edge => "Edge view", _ => "Corner view",
        },
        _ => "",
    };

    // ---- input
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var s = Session; if (s == null) return;
        var pos = e.GetPosition(this);
        if (_pressed && _pressHit is Hit.Region or Hit.None)
        {
            if (!_dragging && Dist(pos, _down) > 4) _dragging = true;
            if (_dragging)
            {
                s.StopViewAnimation(); s.Camera = Cam; s.Camera.OrbitFree(pos.X - _down.X, pos.Y - _down.Y);
                _down = pos; s.Invalidate();
                return;
            }
        }
        var (h, r) = HitTest(pos);
        if (h != _hit || r != _hoverRegion) { _hit = h; _hoverRegion = r; ToolTip.SetTip(this, h == Hit.None ? null : Tip(h, r, s)); InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e) { if (!_pressed) { _hit = Hit.None; _hoverRegion = -1; InvalidateVisual(); } }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _down = e.GetPosition(this); _dragging = false; _pressed = true;
        (_pressHit, _pressRegion) = HitTest(_down);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var s = Session;
        bool wasDrag = _dragging;
        _pressed = false; _dragging = false;
        e.Pointer.Capture(null);
        if (s == null || wasDrag) return;
        var (h, r) = HitTest(e.GetPosition(this));
        if (h != _pressHit || (h == Hit.Region && r != _pressRegion)) return;
        s.Camera = Cam;
        var cam = s.Camera;
        double q = Math.PI / 2;
        double snapYaw = Math.Round(cam.Yaw / q) * q;
        switch (h)
        {
            case Hit.Home: s.AnimateView(-35 * Math.PI / 180, 28 * Math.PI / 180); break;
            case Hit.Projection: s.Ortho = !s.Ortho; break;
            case Hit.RollCcw: s.AnimateView(cam.Yaw, cam.Pitch, Math.Round(cam.Roll / q) * q + q); break;
            case Hit.RollCw: s.AnimateView(cam.Yaw, cam.Pitch, Math.Round(cam.Roll / q) * q - q); break;
            case Hit.Left: case Hit.Right: case Hit.Up: case Hit.Down:
                var eye = cam.EyeDirection; var up = cam.Up; var right = cam.Right;
                var target = h switch { Hit.Left => -right, Hit.Right => right, Hit.Up => up, _ => -up };
                var targetUp = h switch { Hit.Up => -eye, Hit.Down => eye, _ => up };
                var destination = new Camera { Yaw = cam.Yaw };
                destination.SetOrientation(target, targetUp);
                s.AnimateView(destination.Yaw, destination.Pitch, destination.Roll);
                break;
            case Hit.Region:
                var d = Regions[r].Dir.Normalized();
                double pitch = Math.Asin(Math.Clamp(d.Z, -1, 1));
                double yaw = Math.Abs(d.Z) > 0.999 ? snapYaw : Math.Atan2(d.X, -d.Y);
                s.AnimateView(yaw, pitch);
                break;
        }
    }
}
