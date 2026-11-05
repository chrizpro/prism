using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Prism.Kernel;

namespace Prism.Core.Drawing;

public sealed record DrawingElement(Guid Id, string Kind, List<Prim> Primitives)
{
    public Rect Bounds => DrawingComposition.Bounds(Primitives);
}
public sealed record DrawingWarning(Guid Id, string Message)
{
    public override string ToString() => Message;
}
public sealed class DrawingComposition
{
    public List<DrawingElement> Elements { get; } = [];
    public List<DrawingWarning> Warnings { get; } = [];
    public List<Prim> Primitives => Elements.SelectMany(e => e.Primitives).ToList();
    public static Rect Bounds(IEnumerable<Prim> primitives)
    {
        var pts = new List<Point>();
        foreach (var p in primitives)
        {
            if (p is PolyP poly) pts.AddRange(poly.Pts.Select(x => new Point(x.x, x.y)));
            if (p is FillP fill) pts.AddRange(fill.Pts.Select(x => new Point(x.x, x.y)));
            if (p is TextP t)
            {
                double width = t.S.Length * t.Size * .56, left = t.Anchor == "middle" ? -width / 2 : t.Anchor == "end" ? -width : 0;
                double rad = -t.Rot * Math.PI / 180;
                foreach (var q in new[] { new Point(left, -t.Size), new Point(left + width, -t.Size), new Point(left + width, t.Size * .2), new Point(left, t.Size * .2) })
                    pts.Add(new Point(t.X + q.X * Math.Cos(rad) - q.Y * Math.Sin(rad), t.Y + q.X * Math.Sin(rad) + q.Y * Math.Cos(rad)));
            }
        }
        return pts.Count == 0 ? default : new Rect(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
    }
    public void Validate(DraftSheet sheet)
    {
        var inside = new Rect(10, 10, sheet.Width - 20, sheet.Height - 20);
        foreach (var e in Elements.Where(e => e.Kind != "sheet"))
        {
            if (!inside.Contains(e.Bounds)) Warnings.Add(new(e.Id, $"{e.Kind}: outside printable sheet"));
        }
        // Check individual text bounds rather than extension-line envelopes, which legitimately cross geometry.
        var texts = Elements.SelectMany(e => e.Primitives.OfType<TextP>().Select(t => (e.Id, e.Kind, Box: Bounds([t])))).ToList();
        var warned = new HashSet<Guid>();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
                if (texts[i].Id != texts[j].Id && (texts[i].Kind == "annotation" || texts[j].Kind == "annotation") && texts[i].Box.Intersects(texts[j].Box))
                {
                    if (texts[i].Kind == "annotation" && warned.Add(texts[i].Id)) Warnings.Add(new(texts[i].Id, "Annotation text overlaps another label"));
                    if (texts[j].Kind == "annotation" && warned.Add(texts[j].Id)) Warnings.Add(new(texts[j].Id, "Annotation text overlaps another label"));
                }
        foreach (var a in Elements.Where(e => e.Kind == "annotation"))
        {
            var labels = a.Primitives.OfType<TextP>().Select(t => Bounds([t])).ToList();
            bool intersects = Elements.Where(e => e.Kind == "view").SelectMany(e => e.Primitives.OfType<PolyP>()).Any(p =>
                p.Pts.Zip(p.Pts.Skip(1)).Any(s => labels.Any(b => SegmentHits(b, new(s.First.x, s.First.y), new(s.Second.x, s.Second.y)))));
            if (intersects) Warnings.Add(new(a.Id, "Annotation text intersects view geometry"));
        }
        foreach (var v in sheet.Views.Where(v => v.Unresolved)) Warnings.Add(new(v.Id, "View has an unresolved model reference"));
        foreach (var a in sheet.Annotations.Where(a => a.Unresolved)) Warnings.Add(new(a.Id, "Annotation geometry is unresolved; last value retained"));
        foreach (var a in sheet.Annotations.Where(a => a.IsDimension && a.Text.Length > 0)) Warnings.Add(new(a.Id, "Dimension has a text override"));
        foreach (var a in sheet.Annotations.Where(a => a.Type is "gdt" or "finish" && a.Text.Length == 0 && a.Tolerance.Length == 0)) Warnings.Add(new(a.Id, "Annotation needs an explicit specification"));
    }
    static bool SegmentHits(Rect b, SheetPoint a, SheetPoint c)
    {
        double t0 = 0, t1 = 1, dx = c.X - a.X, dy = c.Y - a.Y;
        bool Clip(double p, double q) { if (Math.Abs(p) < 1e-10) return q >= 0; double r = q / p; if (p < 0) t0 = Math.Max(t0, r); else t1 = Math.Min(t1, r); return t0 <= t1; }
        return Clip(-dx, a.X - b.Left) && Clip(dx, b.Right - a.X) && Clip(-dy, a.Y - b.Top) && Clip(dy, b.Bottom - a.Y);
    }
}

public sealed partial class DrawingSheet
{
    public static DraftView GenerateDraftGeometry(Shape shape, string orientation, DraftView? parent, string kind, bool vertical,
        double? cut, SheetPoint? detailCenter, double radius, double ratio)
    {
        var d = new DrawingSheet(); DrawingView v;
        if (kind == "base")
        {
            var (dir, x) = Orient(orientation.StartsWith("projected:") ? "front" : orientation);
            if (orientation.StartsWith("projected:"))
            {
                if (parent == null || parent.Orientation == "iso") throw new InvalidOperationException("Select an orthographic parent for projected views.");
                var pd = new Vec3(parent.Direction[0], parent.Direction[1], parent.Direction[2]);
                var px = new Vec3(parent.XDirection[0], parent.XDirection[1], parent.XDirection[2]);
                var up = Vec3.Cross(-pd, px).Normalized();
                (dir, x) = orientation switch { "projected:above" => (-up, px), "projected:below" => (up, px), "projected:left" => (px, -pd), _ => (-px, pd) };
            }
            v = new DrawingView { Dir = dir, XDir = x, Lines = Occ.Hlr(shape, dir, x) }; v.FitBox();
        }
        else
        {
            if (parent == null || parent.Orientation == "iso" && kind == "section") throw new InvalidOperationException("Select an orthographic parent view for a section.");
            var p = new DrawingView { Name = parent.Id.ToString(), Dir = new(parent.Direction[0], parent.Direction[1], parent.Direction[2]), XDir = new(parent.XDirection[0], parent.XDirection[1], parent.XDirection[2]),
                MinX = parent.MinX, MinY = parent.MinY, MaxX = parent.MaxX, MaxY = parent.MaxY,
                Lines = parent.Strokes.Select(l => (l.Kind, l.Points.Select(p => (p.X, p.Y)).ToArray())).ToList() };
            v = kind == "section" ? d.BuildSection(shape, p, new SectionSpec { Direction = vertical ? "vertical" : "horizontal", Offset = cut ?? (vertical ? parent.MinX + parent.MaxX : parent.MinY + parent.MaxY) / 2 }, "A")
                : d.BuildDetail(p, new DetailSpec { Cx = detailCenter?.X ?? p.Mx, Cy = detailCenter?.Y ?? p.My, Radius = radius, Ratio = ratio }, "A");
        }
        if (v.Lines.Count == 0) throw new InvalidOperationException("The requested view contains no geometry. Adjust the view definition.");
        var draft = new DraftView { Orientation = orientation, Kind = kind, Direction = [v.Dir.X, v.Dir.Y, v.Dir.Z], XDirection = [v.XDir.X, v.XDir.Y, v.XDir.Z],
            Strokes = v.Lines.Select(l => new DrawingStroke { Kind = l.kind, Points = l.pts.Select(p => new SheetPoint(p.x, p.y)).ToList() }).ToList(),
            HatchLoops = v.HatchLoops.Select(l => l.Select(p => new SheetPoint(p.x, p.y)).ToList()).ToList(),
            MinX = v.MinX, MinY = v.MinY, MaxX = v.MaxX, MaxY = v.MaxY, CutVertical = v.CutVertical, CutOffset = v.CutOffset,
            DetailCenter = new(v.DetailCx, v.DetailCy), DetailRadius = radius, DetailRatio = ratio };
        // A nearly circular spline or an ellipse must not acquire a manufactured radius from a polyline fit.
        // Match projected curves to native analytic circles and retain the kernel radius, in model mm.
        for (int i = 0; i < shape.Count(SubKind.Edge); i++)
        {
            using var edge = shape.Sub(SubKind.Edge, i); var info = Occ.EdgeInfoOf(edge);
            if (info.Type != "Circle" || info.Radius <= 0) continue;
            var samples = Occ.CurveSamples(edge, 12, 1).FirstOrDefault(); if (samples == null || samples.Length == 0) continue;
            var center3 = samples[0].p + samples[0].k * (info.Radius * info.Radius); var center = v.Proj(center3);
            var point = new SheetPoint(center.x, center.y); double r = info.Radius, tolerance = Math.Max(.00001, r * .000001);
            if (samples.Any(s => { var p = v.Proj(s.p); return Math.Abs(new SheetPoint(p.x, p.y).Distance(point) - r) > tolerance; })) continue;
            foreach (var stroke in draft.Strokes.Where(s => s.Points.Count >= 5 && s.Points.All(p => Math.Abs(p.Distance(point) - r) < tolerance)))
            { stroke.AnalyticRadius = r; stroke.AnalyticCenter = point; }
        }
        return draft;
    }
    /// <summary>One composition shared by the editor and all export formats. Editor overlays are not primitives.</summary>
    public static DrawingComposition ComposeDraft(DraftSheet sheet)
    {
        var d = new DrawingSheet { Scale = sheet.Scale, ScaleText = ScaleLabel(sheet.Scale), Opt = new DrawingOptions
        {
            Format = new SheetFormat { Name = sheet.Format, W = sheet.Width, H = sheet.Height }, Units = sheet.Units,
            Dimensions = false, HoleCallouts = false, Centerlines = false, LocatingDimensions = false, DefaultEdgeNote = false,
            ThirdAngle = sheet.ThirdAngle, Bom = sheet.ShowParts, Tolerance = sheet.GeneralTolerance,
            Notes = sheet.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Title = new TitleBlock { Title = sheet.Title, Number = sheet.Number, Material = sheet.Material, Drawn = sheet.DrawnBy, Company = sheet.Company, Revision = sheet.Revision, Date = sheet.Date },
        } };
        var result = new DrawingComposition();
        void Capture(Guid id, string kind, Action draw)
        {
            d.Prims.Clear(); draw(); result.Elements.Add(new(id, kind, [.. d.Prims]));
        }
        foreach (var v in sheet.Views)
        {
            var dir = new Vec3(v.Direction[0], v.Direction[1], v.Direction[2]);
            var x = new Vec3(v.XDirection[0], v.XDirection[1], v.XDirection[2]);
            d.Views.Add(new DrawingView { Name = v.Id.ToString(), Label = v.Name, Kind = v.Kind, Dir = dir, XDir = x,
                MinX = v.MinX, MinY = v.MinY, MaxX = v.MaxX, MaxY = v.MaxY, CX = v.X, CY = v.Y, Scale = v.Scale,
                Lines = v.Strokes.Where(l => v.HiddenLines || l.Kind is not (3 or 4)).Select(l => (l.Kind, l.Points.Select(p => (p.X, p.Y)).ToArray())).ToList(),
                HatchLoops = v.HatchLoops.Select(l => l.Select(p => (p.X, p.Y)).ToArray()).ToList(),
                CutParent = v.ParentId?.ToString() ?? "", CutVertical = v.CutVertical, CutOffset = v.CutOffset,
                Caption = v.Caption, DetailParent = v.ParentId?.ToString() ?? "", DetailCx = v.DetailCenter.X, DetailCy = v.DetailCenter.Y, DetailR = v.DetailRadius });
        }
        Capture(sheet.Id, "sheet", () =>
        {
            d.Add(new PolyP([(10, 10), (sheet.Width - 10, 10), (sheet.Width - 10, sheet.Height - 10), (10, sheet.Height - 10)], LineStyle.Visible, true));
            d.DrawTitleBlock(sheet.Parts.Select(p => new BomItem { Name = p.Name, Qty = p.Quantity, Material = p.Material }).ToList());
        });
        foreach (var v in d.Views)
            Capture(Guid.Parse(v.Name), "view", () => { d.DrawGeometry(v); d.DrawCaption(v, d.Views.Any(s => s.Kind == "section" && s.CutParent == v.Name) ? 14 : 0); if (v.Kind == "section") d.DrawCutLine(v); if (v.Kind == "detail") d.DrawDetailMarker(v); });
        foreach (var a in sheet.Annotations)
        {
            var v = sheet.Views.FirstOrDefault(v => v.Id == a.ViewId);
            if (v == null) { result.Warnings.Add(new(a.Id, "Annotation has no parent view")); continue; }
            Capture(a.Id, "annotation", () => d.DrawDraftAnnotation(a, v, sheet.Units));
        }
        result.Validate(sheet); return result;
    }

    void DrawDraftAnnotation(DraftAnnotation a, DraftView v, string units)
    {
        var p = v.ToPaper(a.A); var q = v.ToPaper(a.B); var label = new SheetPoint(v.X + a.Label.X, v.Y + a.Label.Y);
        string value = a.ValueText(units); string txt = value + (a.Tolerance.Length > 0 ? " " + a.Tolerance : "");
        if (a.Type is "horizontal" or "vertical" or "aligned")
        {
            string kind = a.Type == "horizontal" ? "h" : a.Type == "vertical" ? "v" : "aligned";
            double side = kind == "h" ? Math.Sign(label.Y - (p.Y + q.Y) / 2) : Math.Sign(label.X - (p.X + q.X) / 2);
            if (side == 0) side = 1;
            (double, double) n = kind == "h" ? (0, side) : (side, 0);
            double off = kind == "h" ? Math.Abs(label.Y - (side > 0 ? Math.Max(p.Y, q.Y) : Math.Min(p.Y, q.Y))) : Math.Abs(label.X - (side > 0 ? Math.Max(p.X, q.X) : Math.Min(p.X, q.X)));
            if (kind == "aligned")
            {
                double dx = q.X - p.X, dy = q.Y - p.Y, length = p.Distance(q);
                if (length < 1e-8) return;
                double projection = ((label.X - p.X) * -dy + (label.Y - p.Y) * dx) / length;
                n = (-dy / length * Math.Sign(projection), dx / length * Math.Sign(projection));
                // Dim's aligned mode takes the sign of the offset to choose a side.
                Dim((p.X, p.Y), (q.X, q.Y), n, projection, value, kind, a.Tolerance); return;
            }
            Dim((p.X, p.Y), (q.X, q.Y), n, off, value, kind, a.Tolerance); return;
        }
        if (a.Type is "ordinate-x" or "ordinate-y")
        {
            bool x = a.Type == "ordinate-x";
            var elbow = x ? new SheetPoint(q.X, label.Y) : new SheetPoint(label.X, q.Y);
            Line(q.X, q.Y, elbow.X, elbow.Y, LineStyle.Dimension); Line(elbow.X, elbow.Y, label.X, label.Y, LineStyle.Dimension);
            Text(label.X, label.Y - 1, txt, 3.4); return;
        }
        if (a.Type == "angular")
        {
            var vertex = v.ToPaper(a.B); var end = v.ToPaper(a.C);
            double radius = Math.Max(6, vertex.Distance(label)), start = Math.Atan2(p.Y - vertex.Y, p.X - vertex.X), finish = Math.Atan2(end.Y - vertex.Y, end.X - vertex.X);
            double sweep = finish - start; while (sweep > Math.PI) sweep -= Math.PI * 2; while (sweep < -Math.PI) sweep += Math.PI * 2;
            var arc = Enumerable.Range(0, 49).Select(i => (vertex.X + radius * Math.Cos(start + sweep * i / 48), vertex.Y + radius * Math.Sin(start + sweep * i / 48))).ToArray();
            Add(new PolyP(arc, LineStyle.Dimension));
            foreach (double angle in new[] { start, start + sweep }) Line(vertex.X, vertex.Y, vertex.X + (radius + 2) * Math.Cos(angle), vertex.Y + (radius + 2) * Math.Sin(angle), LineStyle.Dimension);
            Arrow(arc[0].Item1, arc[0].Item2, -Math.Sin(start) * Math.Sign(sweep), Math.Cos(start) * Math.Sign(sweep));
            Arrow(arc[^1].Item1, arc[^1].Item2, Math.Sin(start + sweep) * Math.Sign(sweep), -Math.Cos(start + sweep) * Math.Sign(sweep));
            Text(vertex.X + (radius + 4) * Math.Cos(start + sweep / 2), vertex.Y + (radius + 4) * Math.Sin(start + sweep / 2), txt, 3.4, "middle"); return;
        }
        if (a.Type is "radius" or "diameter")
        {
            double len = Math.Max(.001, p.Distance(label)); var direction = new SheetPoint((label.X - p.X) / len, (label.Y - p.Y) / len);
            var edge = new SheetPoint(p.X + direction.X * a.Radius * v.Scale, p.Y + direction.Y * a.Radius * v.Scale);
            Line(a.Type == "radius" ? p.X : p.X - direction.X * a.Radius * v.Scale, a.Type == "radius" ? p.Y : p.Y - direction.Y * a.Radius * v.Scale, label.X, label.Y, LineStyle.Dimension);
            Arrow(edge.X, edge.Y, direction.X, direction.Y); Text(label.X + 2, label.Y - 1, txt, 3.4); return;
        }
        if (a.Type == "center")
        {
            double r = a.Radius * v.Scale + 3;
            Line(p.X - r, p.Y, p.X + r, p.Y, LineStyle.Center); Line(p.X, p.Y - r, p.X, p.Y + r, LineStyle.Center); return;
        }
        if (a.Type == "centerline") { Line(p.X, p.Y, q.X, q.Y, LineStyle.Center); return; }
        if (a.Type == "datum")
        {
            double dx = label.X - p.X, dy = label.Y - p.Y, length = Math.Max(.001, p.Distance(label));
            double ux = dx / length, uy = dy / length;
            Add(new FillP([(p.X, p.Y), (p.X + ux * 4 - uy * 2.3, p.Y + uy * 4 + ux * 2.3), (p.X + ux * 4 + uy * 2.3, p.Y + uy * 4 - ux * 2.3)]));
            Line(p.X, p.Y, label.X, label.Y, LineStyle.Dimension);
            Add(new PolyP([(label.X - 3.6, label.Y - 3.6), (label.X + 3.6, label.Y - 3.6), (label.X + 3.6, label.Y + 3.6), (label.X - 3.6, label.Y + 3.6)], LineStyle.Dimension, true));
            Text(label.X, label.Y + 1.4, a.Text.Length == 0 ? "A" : a.Text, 4, "middle", 0, true); return;
        }
        var at = v.ToModel(label);
        DrawAnnotation(new Annotation { Type = a.Type == "leader" || a.Type == "hole" ? "note" : a.Type, View = v.Id.ToString(),
            At = (at.X, at.Y), To = a.Type is "leader" or "hole" or "balloon" or "gdt" ? (a.A.X, a.A.Y) : null,
            Text = a.Type == "finish" && a.Text.Length == 0 ? " " : a.Text, Tolerance = a.Tolerance, Symbol = a.Symbol, Datums = a.Datums });
    }
}
