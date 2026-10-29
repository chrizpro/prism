using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Drawing;

public sealed class SheetFormat
{
    public string Name = "A3"; public double W = 420, H = 297;
    public static readonly Dictionary<string, SheetFormat> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A4"] = new() { Name = "A4", W = 297, H = 210 }, ["A3"] = new() { Name = "A3", W = 420, H = 297 },
        ["A2"] = new() { Name = "A2", W = 594, H = 420 }, ["A1"] = new() { Name = "A1", W = 841, H = 594 }, ["A0"] = new() { Name = "A0", W = 1189, H = 841 },
        ["Letter"] = new() { Name = "Letter", W = 279.4, H = 215.9 }, ["Tabloid"] = new() { Name = "Tabloid", W = 431.8, H = 279.4 },
        ["ANSI-C"] = new() { Name = "ANSI-C", W = 558.8, H = 431.8 }, ["ANSI-D"] = new() { Name = "ANSI-D", W = 863.6, H = 558.8 },
    };
}

public sealed class TitleBlock
{
    public string Title = "Part", Drawn = "", Material = "", Number = "", Revision = "A", Company = "";
    public string Date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>A section view: a cut through the parent view, looking at the half that remains.</summary>
public sealed class SectionSpec
{
    public string Parent = "front";
    /// <summary>vertical: the cutting line is vertical in the parent view (the section looks sideways); horizontal: the line is horizontal (looks down).</summary>
    public string Direction = "vertical";
    /// <summary>Where the cut is, in the parent view's drawing coordinates (x for vertical, y for horizontal). NaN = the middle of the part.</summary>
    public double Offset = double.NaN;
}

/// <summary>A magnified circle of a parent view.</summary>
public sealed class DetailSpec
{
    public string Parent = "front";
    public double Cx = double.NaN, Cy = double.NaN, Radius = 10, Ratio = 3;
}

/// <summary>A hand-placed annotation. Coordinates are in the named view's drawing coordinates (mm, model scale; origin = model origin projected).</summary>
public sealed class Annotation
{
    public string Type = "note", View = "front", Text = "", Tolerance = "", Orientation = "aligned", Symbol = "", Datums = "";
    public (double x, double y)? From, To, At, Center;
    public double Offset = 8, Radius, Angle = 45;
    public int Number;
}

public sealed class DrawingOptions
{
    public SheetFormat Format = SheetFormat.All["A3"];
    public TitleBlock Title = new();
    public List<string> Views = ["front", "top", "right", "iso"];
    public double? Scale;
    public bool Dimensions = true, HoleCallouts = true, Centerlines = true, LocatingDimensions = true, ThirdAngle = true;
    public bool? Bom;
    public string Units = "mm";
    public string Tolerance = "LINEAR ±0.1   ANGULAR ±0.5°";
    public List<string> Notes = [];
    public List<SectionSpec> Sections = [];
    public List<DetailSpec> Details = [];
    public List<Annotation> Annotations = [];
}

public sealed class BomItem { public string Name = "", Material = ""; public int Qty = 1; }

public sealed class DrawingView
{
    public string Name = "", Label = "", Kind = "base";
    public Vec3 Dir, XDir;
    public List<(int kind, (double x, double y)[] pts)> Lines = [];
    public List<(double x, double y)[]> HatchLoops = [];
    public double MinX, MinY, MaxX, MaxY, Scale = 1, CX, CY;
    public string Caption = "", CutParent = "", DetailParent = "";
    public bool CutVertical;
    public double CutOffset, DetailCx, DetailCy, DetailR = 1, DetailRatio = 3;
    public Vec3 Up => Vec3.Cross(-Dir, XDir).Normalized();
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double Mx => (MinX + MaxX) / 2;
    public double My => (MinY + MaxY) / 2;
    public (double x, double y) P(double x, double y) => (CX + (x - Mx) * Scale, CY - (y - My) * Scale);
    public (double x, double y) Proj(Vec3 p) => (Vec3.Dot(p, XDir), Vec3.Dot(p, Up));
    public void FitBox()
    {
        var pts = Lines.Where(l => l.kind is 0 or 1 or 2).SelectMany(l => l.pts).ToList();
        if (pts.Count < 2) pts = Lines.SelectMany(l => l.pts).ToList();
        if (pts.Count == 0) pts = [(0, 0)];
        MinX = pts.Min(p => p.x); MaxX = pts.Max(p => p.x); MinY = pts.Min(p => p.y); MaxY = pts.Max(p => p.y);
    }
}

/// <summary>An engineering drawing: projected views with hidden lines, section and detail views, dimensions, centerlines, hole callouts, annotations, BOM and a title block.</summary>
public sealed class DrawingSheet
{
    public DrawingOptions Opt = new();
    public List<DrawingView> Views = [];
    public List<Prim> Prims = [];
    public double Scale = 1;
    public string ScaleText = "1:1";
    public int DimensionCount, HoleCount, SectionCount, DetailCount, AnnotationCount;
    public List<string> Warnings = [];

    const double Margin = 10, Gap = 34, TitleW = 180, TitleH = 42, BomRow = 6, ArrowL = 2.6;
    static readonly (double num, double den)[] Standard = [(10, 1), (5, 1), (2, 1), (1, 1), (1, 2), (1, 5), (1, 10), (1, 20), (1, 50), (1, 100), (1, 200), (1, 500), (1, 1000)];

    static (Vec3 dir, Vec3 x) Orient(string v) => v.ToLowerInvariant() switch
    {
        "front" => (new Vec3(0, 1, 0), new Vec3(1, 0, 0)),
        "back" => (new Vec3(0, -1, 0), new Vec3(-1, 0, 0)),
        "top" => (new Vec3(0, 0, -1), new Vec3(1, 0, 0)),
        "bottom" => (new Vec3(0, 0, 1), new Vec3(1, 0, 0)),
        "right" => (new Vec3(-1, 0, 0), new Vec3(0, 1, 0)),
        "left" => (new Vec3(1, 0, 0), new Vec3(0, -1, 0)),
        "iso" or "isometric" => IsoAxes(),
        _ => throw new ArgumentException($"Unknown view '{v}'. Use front, back, top, bottom, left, right or iso."),
    };

    static (Vec3, Vec3) IsoAxes() { var d = new Vec3(-1, 1, -1).Normalized(); return (d, Vec3.Cross(d, Vec3.UnitZ).Normalized()); }
    static (int c, int r) Cell(string v) => v switch { "left" => (0, 1), "front" => (1, 1), "right" => (2, 1), "back" => (3, 1), "top" => (1, 0), "bottom" => (1, 2), _ => (2, 0) };

    // ================================================================= build
    public static DrawingSheet Create(Shape shape, IReadOnlyList<BomItem> bom, DrawingOptions opt)
    {
        var d = new DrawingSheet { Opt = opt };
        var props = shape.Props();
        foreach (var n0 in opt.Views.Select(v => v.ToLowerInvariant() == "isometric" ? "iso" : v.ToLowerInvariant()).Distinct())
        {
            var (dir, x) = Orient(n0);
            var lines = Occ.Hlr(shape, dir, x);
            if (lines.Count == 0) throw new InvalidOperationException($"The {n0} view has no visible geometry.");
            var v = new DrawingView { Name = n0, Label = n0.ToUpperInvariant(), Dir = dir.Normalized(), XDir = x.Normalized(), Lines = lines };
            v.FitBox(); d.Views.Add(v);
        }
        if (d.Views.Count == 0) throw new ArgumentException("No views were requested.");
        var extras = new List<DrawingView>();
        int letter = 0;
        foreach (var sp in opt.Sections)
        {
            var parent = d.Views.FirstOrDefault(v => v.Name == sp.Parent.ToLowerInvariant()) ?? throw new ArgumentException($"Section parent view '{sp.Parent}' is not on the drawing. Add it to the views list.");
            if (parent.Name == "iso") throw new ArgumentException("Sections need an orthographic parent view, not iso.");
            extras.Add(d.BuildSection(shape, parent, sp, ((char)('A' + letter)).ToString())); letter++;
        }
        d.Letters = letter;
        foreach (var dt in opt.Details)
        {
            var parent = d.Views.Concat(extras).FirstOrDefault(v => v.Name == dt.Parent.ToLowerInvariant()) ?? throw new ArgumentException($"Detail parent view '{dt.Parent}' is not on the drawing.");
            extras.Add(d.BuildDetail(parent, dt, ((char)('A' + d.Letters)).ToString())); d.Letters++;
        }
        d.Layout(extras, (bom.Count > 1 || opt.Bom == true) && opt.Bom != false ? bom.Count + 1 : 0);
        d.Compose(shape, props, extras, bom);
        return d;
    }

    int Letters;

    DrawingView BuildSection(Shape shape, DrawingView parent, SectionSpec sp, string letter)
    {
        bool vert = !sp.Direction.StartsWith("h", StringComparison.OrdinalIgnoreCase);
        var nrm = vert ? parent.XDir : parent.Up;
        double off = !double.IsNaN(sp.Offset) ? sp.Offset : vert ? parent.Mx : parent.My;
        var origin = nrm * off;
        var half = Occ.Halve(shape, origin, nrm);
        var look = -nrm;
        var xdir = vert ? Vec3.Cross(look, parent.Up).Normalized() : parent.XDir;
        var v = new DrawingView { Name = "section-" + letter, Label = $"SECTION {letter}-{letter}", Kind = "section", Dir = look, XDir = xdir, Lines = Occ.Hlr(half, look, xdir), Caption = letter };
        v.FitBox();
        try
        {
            var cut = Occ.Section(shape, origin, nrm);
            var edges = Occ.CurveSamples(cut, 48, 1024).Select(e => e.Select(s => v.Proj(s.p)).ToList()).ToList();
            v.HatchLoops = ChainLoops(edges, 1e-3);
            if (v.HatchLoops.Count == 0) Warnings.Add($"Section {letter}-{letter}: the cut did not produce closed outlines, so it is not hatched.");
        }
        catch (Exception e) { Warnings.Add($"Section {letter}-{letter}: no hatching ({e.Message})."); }
        // remember the cutting line for the parent view
        v.CutParent = parent.Name; v.CutVertical = vert; v.CutOffset = off;
        SectionCount++;
        return v;
    }

    DrawingView BuildDetail(DrawingView parent, DetailSpec dt, string letter)
    {
        double cx = double.IsNaN(dt.Cx) ? parent.Mx : dt.Cx, cy = double.IsNaN(dt.Cy) ? parent.My : dt.Cy, r = Math.Max(dt.Radius, 0.1);
        var v = new DrawingView { Name = "detail-" + letter, Label = $"DETAIL {letter}", Kind = "detail", Dir = parent.Dir, XDir = parent.XDir, Caption = letter, DetailParent = parent.Name, DetailRatio = dt.Ratio };
        foreach (var (kind, pts) in parent.Lines)
            foreach (var seg in ClipToCircle(pts, cx, cy, r)) v.Lines.Add((kind, seg));
        v.MinX = cx - r; v.MaxX = cx + r; v.MinY = cy - r; v.MaxY = cy + r;
        v.DetailCx = cx; v.DetailCy = cy; v.DetailR = r;
        DetailCount++;
        return v;
    }

    static IEnumerable<(double x, double y)[]> ClipToCircle((double x, double y)[] pts, double cx, double cy, double r)
    {
        var results = new List<(double x, double y)[]>();
        var cur = new List<(double x, double y)>();
        void Flush() { if (cur.Count >= 2) results.Add(cur.ToArray()); cur = []; }
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var (ax, ay) = pts[i]; var (bx, by) = pts[i + 1];
            double dx = bx - ax, dy = by - ay, fx = ax - cx, fy = ay - cy;
            double a = dx * dx + dy * dy, b = 2 * (fx * dx + fy * dy), c = fx * fx + fy * fy - r * r;
            double t0 = 0, t1 = 1;
            if (a < 1e-18) { if (c > 0) { Flush(); continue; } }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc < 0) { Flush(); continue; }
                double s = Math.Sqrt(disc); double u0 = (-b - s) / (2 * a), u1 = (-b + s) / (2 * a);
                t0 = Math.Max(0, u0); t1 = Math.Min(1, u1);
                if (t0 >= t1) { Flush(); continue; }
            }
            var p0 = (ax + dx * t0, ay + dy * t0); var p1 = (ax + dx * t1, ay + dy * t1);
            if (cur.Count == 0 || t0 > 0) { Flush(); cur.Add(p0); }
            cur.Add(p1);
            if (t1 < 1) Flush();
        }
        Flush();
        return results;
    }

    /// <summary>Joins edge polylines end to end into closed outlines (anything that does not close is dropped).</summary>
    static List<(double x, double y)[]> ChainLoops(List<List<(double x, double y)>> edges, double tol)
    {
        var pool = edges.Where(e => e.Count >= 2).Select(e => e.ToList()).ToList();
        var loops = new List<(double, double)[]>();
        bool Near((double x, double y) a, (double x, double y) b) => Math.Abs(a.x - b.x) < tol * 20 && Math.Abs(a.y - b.y) < tol * 20;
        while (pool.Count > 0)
        {
            var cur = pool[0]; pool.RemoveAt(0);
            bool grew = true;
            while (grew && !Near(cur[0], cur[^1]))
            {
                grew = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var e = pool[i];
                    if (Near(cur[^1], e[0])) cur.AddRange(e.Skip(1));
                    else if (Near(cur[^1], e[^1])) { e.Reverse(); cur.AddRange(e.Skip(1)); }
                    else if (Near(cur[0], e[^1])) { e.AddRange(cur.Skip(1)); cur = e; }
                    else if (Near(cur[0], e[0])) { e.Reverse(); e.AddRange(cur.Skip(1)); cur = e; }
                    else continue;
                    pool.RemoveAt(i); grew = true; break;
                }
            }
            if (cur.Count >= 3 && Near(cur[0], cur[^1])) loops.Add(cur.ToArray());
        }
        return loops;
    }

    // ================================================================= layout
    void Layout(List<DrawingView> extras, int bomRows)
    {
        var f = Opt.Format; double ux = f.W - 2 * Margin, uy = f.H - 2 * Margin - TitleH - (bomRows > 0 ? bomRows * BomRow : 0) - 6;
        var cells = Views.Select(v => (v, cell: Cell(v.Name))).ToList();
        if (!Opt.ThirdAngle) cells = cells.Select(t => (t.v, cell: t.v.Name switch { "top" => (1, 2), "bottom" => (1, 0), "right" => (0, 1), "left" => (2, 1), _ => t.cell })).ToList();
        var used = cells.Where(t => t.v.Name != "iso").Select(t => t.cell).ToHashSet();
        cells = cells.Select(t => t.v.Name == "iso" ? (t.v, (used.Contains((2, 0)) ? (3, 0) : (2, 0))) : t).ToList();
        int cols = cells.Max(t => t.cell.Item1) + 1, rows = cells.Max(t => t.cell.Item2) + 1;
        double DetailDiam(DrawingView v, double s) => Math.Min(2 * v.DetailR * v.DetailRatio * s, 75);
        double ExtraW(double s) => extras.Count == 0 ? 0 : extras.Max(e => e.Kind == "detail" ? DetailDiam(e, s) : e.Width * s);
        double ExtraH(double s) => extras.Count == 0 ? 0 : extras.Sum(e => e.Kind == "detail" ? DetailDiam(e, s) : e.Height * s) + (extras.Count - 1) * Gap;
        double Over(double s)
        {
            double w = Enumerable.Range(0, cols).Sum(c => cells.Where(t => t.cell.Item1 == c).Select(t => t.v.Width * s).DefaultIfEmpty(0).Max());
            double h = Enumerable.Range(0, rows).Sum(r => cells.Where(t => t.cell.Item2 == r).Select(t => t.v.Height * s).DefaultIfEmpty(0).Max());
            double totalW = w + (cols - 1) * Gap + (extras.Count > 0 ? Gap + ExtraW(s) : 0) + 2 * Gap, totalH = Math.Max(h + (rows - 1) * Gap, ExtraH(s)) + 2 * Gap;
            return Math.Max(totalW - ux, totalH - uy);
        }
        double chosen; string text;
        if (Opt.Scale is { } rq && rq > 0) { chosen = rq; text = rq >= 1 ? $"{rq:0.##}:1" : $"1:{1 / rq:0.##}"; }
        else
        {
            (chosen, text) = (Standard[^1].num / Standard[^1].den, "1:1000");
            foreach (var (n, dn) in Standard) if (Over(n / dn) <= 0) { chosen = n / dn; text = n >= dn ? $"{n / dn:0.##}:1" : $"1:{dn / n:0.##}"; break; }
        }
        Scale = chosen; ScaleText = text;
        var colW = Enumerable.Range(0, cols).Select(c => cells.Where(t => t.cell.Item1 == c).Select(t => t.v.Width * chosen).DefaultIfEmpty(0).Max()).ToArray();
        var rowH = Enumerable.Range(0, rows).Select(r => cells.Where(t => t.cell.Item2 == r).Select(t => t.v.Height * chosen).DefaultIfEmpty(0).Max()).ToArray();
        double gridW = colW.Sum() + (cols - 1) * Gap, gridH = rowH.Sum() + (rows - 1) * Gap, exW = ExtraW(chosen), exH = ExtraH(chosen);
        double totW = gridW + (extras.Count > 0 ? Gap + exW : 0), totH = Math.Max(gridH, exH);
        double x0 = Margin + (ux - totW) / 2, y0 = Margin + 3 + (uy - totH) / 2;
        foreach (var (v, (c, r)) in cells)
        {
            v.Scale = chosen;
            v.CX = x0 + colW.Take(c).Sum() + c * Gap + colW[c] / 2; v.CY = y0 + (totH - gridH) / 2 + rowH.Take(r).Sum() + r * Gap + rowH[r] / 2;
        }
        double ey = y0 + (totH - exH) / 2, ex = x0 + gridW + Gap + exW / 2;
        foreach (var e in extras)
        {
            double h = e.Kind == "detail" ? DetailDiam(e, chosen) : e.Height * chosen;
            e.Scale = e.Kind == "detail" ? h / (2 * e.DetailR) : chosen;
            e.CX = ex; e.CY = ey + h / 2; ey += h + Gap;
        }
        Views.AddRange(extras);
    }

    // ================================================================= composition
    string Num(double mm)
    {
        bool inch = Opt.Units.StartsWith("in", StringComparison.OrdinalIgnoreCase);
        double v = inch ? mm / 25.4 : mm;
        return Math.Round(v, inch ? 3 : 2).ToString(inch ? "0.###" : "0.##", CultureInfo.InvariantCulture);
    }

    void Add(Prim p) => Prims.Add(p);
    void Line(double x1, double y1, double x2, double y2, LineStyle s) => Add(new PolyP([(x1, y1), (x2, y2)], s));
    void Text(double x, double y, string s, double size = 3.2, string anchor = "start", double rot = 0, bool bold = false) => Add(new TextP(x, y, s, size, anchor, rot, bold));
    void Arrow(double x, double y, double vx, double vy)         // tip at (x, y); (vx, vy) points from the tip back along the line
    {
        double l = Math.Sqrt(vx * vx + vy * vy); if (l < 1e-9) return; vx /= l; vy /= l;
        double bx = x + vx * ArrowL, by = y + vy * ArrowL, px = -vy * ArrowL * 0.17, py = vx * ArrowL * 0.17;
        Add(new FillP([(x, y), (bx + px, by + py), (bx - px, by - py)]));
    }

    void Compose(Shape shape, ShapeProps props, List<DrawingView> extras, IReadOnlyList<BomItem> bom)
    {
        var f = Opt.Format;
        Add(new PolyP([(Margin, Margin), (f.W - Margin, Margin), (f.W - Margin, f.H - Margin), (Margin, f.H - Margin)], LineStyle.Visible, true));
        foreach (var v in Views) DrawGeometry(v);
        var cyls = Opt.HoleCallouts || Opt.Centerlines || Opt.LocatingDimensions ? Occ.Cylinders(shape) : [];
        foreach (var v in Views.Where(v => v.Kind == "base" && v.Name != "iso"))
        {
            if (Opt.Centerlines) DrawCenterlines(v, cyls);
            if (Opt.Dimensions) DrawOverall(v);
            if (Opt.HoleCallouts) DrawHoles(v, cyls, props);
        }
        foreach (var v in Views.Where(v => v.Kind == "section")) DrawCutLine(v);
        foreach (var v in Views.Where(v => v.Kind == "detail")) DrawDetailMarker(v);
        foreach (var v in Views) DrawCaption(v);
        foreach (var a in Opt.Annotations) { try { DrawAnnotation(a); AnnotationCount++; } catch (Exception e) { Warnings.Add($"Annotation '{a.Type}': {e.Message}"); } }
        DrawTitleBlock(bom);
    }

    void DrawGeometry(DrawingView v)
    {
        foreach (var (kind, pts) in v.Lines)
        {
            if (pts.Length < 2) continue;
            var sp = pts.Select(p => v.P(p.x, p.y)).ToArray();
            Add(new PolyP(sp, kind switch { 3 or 4 => LineStyle.Hidden, 1 => LineStyle.Thin, _ => LineStyle.Visible }));
        }
        if (v.Kind == "section") Hatch(v);
        if (v.Kind == "detail")
        {
            var c = v.P(v.DetailCx, v.DetailCy); double r = v.DetailR * v.Scale;
            Add(new PolyP(Circle(c.x, c.y, r), LineStyle.Thin, true));
        }
    }

    static (double x, double y)[] Circle(double cx, double cy, double r, int n = 72) =>
        Enumerable.Range(0, n).Select(i => (cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n))).ToArray();

    void Hatch(DrawingView v)
    {
        if (v.HatchLoops.Count == 0) return;
        var loops = v.HatchLoops.Select(l => l.Select(p => v.P(p.x, p.y)).ToArray()).ToList();
        double minx = loops.Min(l => l.Min(p => p.x)), maxx = loops.Max(l => l.Max(p => p.x)), miny = loops.Min(l => l.Min(p => p.y)), maxy = loops.Max(l => l.Max(p => p.y));
        const double spacing = 2.5;
        double c = (maxx - minx) + (maxy - miny);
        for (double k = -(maxy - miny); k < maxx - minx; k += spacing)     // 45° lines: y = x - (minx + k) + miny ... scan by intercept
        {
            // line through (minx + k, miny) rising to the upper right (sheet y points down, so use slope -1)
            double ox = minx + k, oy = maxy;
            var xs = new List<double>();
            foreach (var l in loops)
                for (int i = 0; i < l.Length; i++)
                {
                    var (ax, ay) = l[i]; var (bx, by) = l[(i + 1) % l.Length];
                    // parameter along the line: point = (ox + t, oy - t); solve intersection with segment
                    double dx = bx - ax, dy = by - ay, den = dx * (-1) - dy * 1;   // line direction (1,-1)
                    double det = -dx - dy;
                    if (Math.Abs(det) < 1e-12) continue;
                    double u = ((ox - ax) * (-1) - (oy - ay) * 1) / det;           // position on the segment
                    if (u < 0 || u >= 1) continue;
                    double px = ax + dx * u, py = ay + dy * u;
                    xs.Add(px - ox);
                }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                Add(new PolyP([(ox + xs[i], oy - xs[i]), (ox + xs[i + 1], oy - xs[i + 1])], LineStyle.Hatch));
        }
    }

    void DrawCaption(DrawingView v)
    {
        var (left, top) = v.P(v.MinX, v.MaxY); var (right, bottom) = v.P(v.MaxX, v.MinY);
        string scale = Math.Abs(v.Scale - Scale) > 1e-9 || v.Kind != "base" ? " (" + ScaleLabel(v.Kind == "detail" ? v.Scale : v.Scale) + ")" : "";
        double dimBelow = v.Kind == "base" && v.Name != "iso" && Opt.Dimensions ? 18 : 9;
        Text((left + right) / 2, bottom + dimBelow, v.Label + (Math.Abs(v.Scale - Scale) > 1e-9 ? scale : ""), 4, "middle", 0, true);
    }

    static string ScaleLabel(double s) => s >= 1 ? $"{s:0.##}:1" : $"1:{1 / s:0.##}";

    void DrawCutLine(DrawingView sv)
    {
        var p = Views.FirstOrDefault(v => v.Name == sv.CutParent); if (p == null) return;
        double ext = 8;
        string L = sv.Caption;
        if (sv.CutVertical)
        {
            var (x, yTop) = p.P(sv.CutOffset, p.MaxY); var (_, yBot) = p.P(sv.CutOffset, p.MinY);
            Line(x, yTop - ext, x, yBot + ext, LineStyle.Cut);
            Line(x, yTop - ext, x, yTop - ext + 6, LineStyle.Visible); Line(x, yBot + ext, x, yBot + ext - 6, LineStyle.Visible);
            Line(x, yTop - ext, x - 8, yTop - ext, LineStyle.Thin); Arrow(x - 8, yTop - ext, 1, 0);
            Line(x, yBot + ext, x - 8, yBot + ext, LineStyle.Thin); Arrow(x - 8, yBot + ext, 1, 0);
            Text(x - 11, yTop - ext + 1.4, L, 4, "end", 0, true); Text(x - 11, yBot + ext + 1.4, L, 4, "end", 0, true);
        }
        else
        {
            var (xL, y) = p.P(p.MinX, sv.CutOffset); var (xR, _) = p.P(p.MaxX, sv.CutOffset);
            Line(xL - ext, y, xR + ext, y, LineStyle.Cut);
            Line(xL - ext, y, xL - ext + 6, y, LineStyle.Visible); Line(xR + ext, y, xR + ext - 6, y, LineStyle.Visible);
            Line(xL - ext, y, xL - ext, y + 8, LineStyle.Thin); Arrow(xL - ext, y + 8, 0, -1);
            Line(xR + ext, y, xR + ext, y + 8, LineStyle.Thin); Arrow(xR + ext, y + 8, 0, -1);
            Text(xL - ext, y + 14, L, 4, "middle", 0, true); Text(xR + ext, y + 14, L, 4, "middle", 0, true);
        }
    }

    void DrawDetailMarker(DrawingView dv)
    {
        var p = Views.FirstOrDefault(v => v.Name == dv.DetailParent); if (p == null) return;
        var c = p.P(dv.DetailCx, dv.DetailCy); double r = dv.DetailR * p.Scale;
        Add(new PolyP(Circle(c.x, c.y, r), LineStyle.Thin, true));
        double a = -Math.PI / 4;
        Line(c.x + r * Math.Cos(a), c.y + r * Math.Sin(a), c.x + (r + 6) * Math.Cos(a), c.y + (r + 6) * Math.Sin(a), LineStyle.Thin);
        Text(c.x + (r + 7) * Math.Cos(a), c.y + (r + 7) * Math.Sin(a) - 1, dv.Caption, 4, "start", 0, true);
    }

    // ---- centerlines and hole marks
    void DrawCenterlines(DrawingView v, List<Occ.CylFace> cyls)
    {
        foreach (var c in cyls)
        {
            double along = Math.Abs(Vec3.Dot(c.Axis, v.Dir));
            if (!c.Full && c.Radius > 0 && along < 0.999) continue;
            if (along > 0.999)
            {
                var (x, y) = v.Proj(c.Origin); var s = v.P(x, y); double r = c.Radius * v.Scale, e = r + 3;
                Line(s.x - e, s.y, s.x + e, s.y, LineStyle.Center); Line(s.x, s.y - e, s.x, s.y + e, LineStyle.Center);
            }
            else if (c.Full && along < 0.05)
            {
                var a = v.Proj(c.Start); var b = v.Proj(c.End); var sa = v.P(a.x, a.y); var sb = v.P(b.x, b.y);
                double dx = sb.x - sa.x, dy = sb.y - sa.y, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-6) continue;
                double ux = dx / l, uy = dy / l;
                Line(sa.x - ux * 3, sa.y - uy * 3, sb.x + ux * 3, sb.y + uy * 3, LineStyle.Center);
            }
        }
    }

    // ---- overall dimensions
    void DrawOverall(DrawingView v)
    {
        var (l, t) = v.P(v.MinX, v.MaxY); var (r, b) = v.P(v.MaxX, v.MinY);
        if (v.Name is not ("right" or "left")) { Dim((l, b), (r, b), (0, 1), 9, Num(v.Width), "h"); }
        Dim((r, t), (r, b), (1, 0), 9, Num(v.Height), "v");
        if (v.Name is "right" or "left") Dim((l, b), (r, b), (0, 1), 9, Num(v.Width), "h");
    }

    /// <summary>A linear dimension between two sheet points measured along u (h, v or aligned); n is the side the dimension line sits on.</summary>
    void Dim((double x, double y) a, (double x, double y) b, (double x, double y) n, double off, string text, string kind, string tol = "")
    {
        (double x, double y) u = kind == "h" ? (1, 0) : kind == "v" ? (0, 1) : (b.x - a.x, b.y - a.y);
        double ul = Math.Sqrt(u.x * u.x + u.y * u.y); if (ul < 1e-9) return; u = (u.x / ul, u.y / ul);
        if (kind == "aligned") n = (-u.y * Math.Sign(off == 0 ? 1 : off), u.x * Math.Sign(off == 0 ? 1 : off));
        double na = a.x * n.x + a.y * n.y, nb = b.x * n.x + b.y * n.y, t = Math.Max(na, nb) + Math.Abs(off);
        var A = (x: a.x + n.x * (t - na), y: a.y + n.y * (t - na)); var B = (x: b.x + n.x * (t - nb), y: b.y + n.y * (t - nb));
        Line(a.x + n.x * 1.5, a.y + n.y * 1.5, A.x + n.x * 2, A.y + n.y * 2, LineStyle.Dimension);
        Line(b.x + n.x * 1.5, b.y + n.y * 1.5, B.x + n.x * 2, B.y + n.y * 2, LineStyle.Dimension);
        double len = Math.Sqrt((B.x - A.x) * (B.x - A.x) + (B.y - A.y) * (B.y - A.y));
        bool tight = len < ArrowL * 2.6;
        if (tight) { Line(A.x - u.x * 5, A.y - u.y * 5, B.x + u.x * 5, B.y + u.y * 5, LineStyle.Dimension); Arrow(A.x, A.y, -u.x, -u.y); Arrow(B.x, B.y, u.x, u.y); }
        else { Line(A.x, A.y, B.x, B.y, LineStyle.Dimension); Arrow(A.x, A.y, u.x, u.y); Arrow(B.x, B.y, -u.x, -u.y); }
        double ang = Math.Atan2(-u.y, u.x) * 180 / Math.PI; if (ang > 90) ang -= 180; if (ang <= -90) ang += 180;
        double ar = ang * Math.PI / 180; var g = (x: Math.Sin(ar), y: -Math.Cos(ar));           // direction glyphs grow in, on the sheet
        var mid = ((A.x + B.x) / 2, (A.y + B.y) / 2);
        string s = tol.Length > 0 ? text + " " + tol : text;
        Text(mid.Item1 + g.x * 0.9, mid.Item2 + g.y * 0.9, s, 3.4, "middle", ang);
        DimensionCount++;
    }

    // ---- hole callouts and locating dimensions
    void DrawHoles(DrawingView v, List<Occ.CylFace> cyls, ShapeProps props)
    {
        var axial = cyls.Where(c => c.Full && Math.Abs(Vec3.Dot(c.Axis, v.Dir)) > 0.999).ToList();
        if (axial.Count == 0) return;
        // each distinct hole/boss is dimensioned once, in the first view that looks down its axis
        var key = (Occ.CylFace c) => $"{Math.Round(c.Radius, 3)}|{Math.Round(Math.Abs(c.Axis.X), 2)},{Math.Round(Math.Abs(c.Axis.Y), 2)},{Math.Round(Math.Abs(c.Axis.Z), 2)}|{Math.Round(c.Length, 2)}|{c.Hole}";
        var claimed = ClaimedHoles;
        var groups = axial.GroupBy(key).Where(g => !claimed.Contains(g.Key)).ToList();
        int idx = 0;
        var centers = new List<(double x, double y)>();
        foreach (var g in groups)
        {
            claimed.Add(g.Key);
            var first = g.First(); int count = g.Count();
            var c0 = v.Proj(first.Origin); var s0 = v.P(c0.x, c0.y); double r = first.Radius * v.Scale;
            double ext = Math.Abs(first.Axis.X) * (props.Max.X - props.Min.X) + Math.Abs(first.Axis.Y) * (props.Max.Y - props.Min.Y) + Math.Abs(first.Axis.Z) * (props.Max.Z - props.Min.Z);
            string text = (count > 1 ? $"{count}X " : "") + "Ø" + Num(first.Radius * 2);
            if (first.Hole) text += first.Length >= ext - 0.01 ? " THRU" : "  ↧ " + Num(first.Length);
            double ang = (45 + 90 * (idx % 4)) * Math.PI / 180 * (idx % 2 == 0 ? 1 : -1);
            double ex = s0.x + (r + 1) * Math.Cos(ang), ey = s0.y - (r + 1) * Math.Sin(ang), lx = s0.x + (r + 9) * Math.Cos(ang), ly = s0.y - (r + 9) * Math.Sin(ang);
            bool right = Math.Cos(ang) >= 0;
            Line(ex, ey, lx, ly, LineStyle.Dimension); Arrow(ex, ey, lx - ex, ly - ey);
            Line(lx, ly, lx + (right ? 4 : -4), ly, LineStyle.Dimension);
            Text(lx + (right ? 4.6 : -4.6), ly + 1.2, text, 3.4, right ? "start" : "end");
            HoleCount += count; idx++; DimensionCount++;
            foreach (var h in g) { var cp = v.Proj(h.Origin); centers.Add(cp); }
        }
        if (!Opt.LocatingDimensions || !Opt.Dimensions) return;
        var xs = centers.Select(c => Math.Round(c.x, 3)).Distinct().OrderBy(x => x).Where(x => Math.Abs(x - v.MinX) > 0.01).Take(5).ToList();
        var ys = centers.Select(c => Math.Round(c.y, 3)).Distinct().OrderBy(y => y).Where(y => Math.Abs(y - v.MinY) > 0.01).Take(5).ToList();
        var (l, t) = v.P(v.MinX, v.MaxY); var (_, bt) = v.P(v.MinX, v.MinY);
        int k = 0;
        foreach (var x in xs)
        {
            var cy = centers.First(c => Math.Abs(c.x - x) < 0.01).y; var sc = v.P(x, cy);
            Dim((l, t), (sc.x, sc.y), (0, -1), 8 + 7 * k++, Num(x - v.MinX), "h");
        }
        k = 0;
        foreach (var y in ys)
        {
            var cx = centers.First(c => Math.Abs(c.y - y) < 0.01).x; var sc = v.P(cx, y);
            Dim((l, bt), (sc.x, sc.y), (-1, 0), 8 + 7 * k++, Num(y - v.MinY), "v");
        }
    }

    HashSet<string> ClaimedHoles { get; } = [];

    // ---- manual annotations
    (double x, double y) V(string view, (double x, double y) p)
    {
        var v = Views.FirstOrDefault(w => w.Name == view.ToLowerInvariant()) ?? throw new ArgumentException($"View '{view}' is not on the drawing (views: {string.Join(", ", Views.Select(w => w.Name))}).");
        return v.P(p.x, p.y);
    }

    void DrawAnnotation(Annotation a)
    {
        double sc = Views.FirstOrDefault(w => w.Name == a.View.ToLowerInvariant())?.Scale ?? Scale;
        switch (a.Type.ToLowerInvariant())
        {
            case "dimension" or "linear":
            {
                var p = V(a.View, a.From ?? throw new ArgumentException("from required")); var q = V(a.View, a.To ?? throw new ArgumentException("to required"));
                string o = a.Orientation.ToLowerInvariant();
                double val = Math.Sqrt(Math.Pow(q.x - p.x, 2) + Math.Pow(q.y - p.y, 2)) / sc;
                if (o.StartsWith('h')) val = Math.Abs(q.x - p.x) / sc; else if (o.StartsWith('v')) val = Math.Abs(q.y - p.y) / sc;
                var n = o.StartsWith('h') ? (0.0, a.Offset >= 0 ? 1.0 : -1.0) : o.StartsWith('v') ? (a.Offset >= 0 ? 1.0 : -1.0, 0.0) : (0.0, 0.0);
                Dim(p, q, n, Math.Abs(a.Offset), a.Text.Length > 0 ? a.Text : Num(val), o.StartsWith('h') ? "h" : o.StartsWith('v') ? "v" : "aligned", a.Tolerance);
                break;
            }
            case "diameter" or "radius":
            {
                var c = V(a.View, a.Center ?? throw new ArgumentException("center required")); double r = a.Radius * sc;
                double ang = a.Angle * Math.PI / 180; var dir = (x: Math.Cos(ang), y: -Math.Sin(ang));
                bool dia = a.Type.StartsWith("d", StringComparison.OrdinalIgnoreCase);
                var e = (x: c.x + dir.x * r, y: c.y + dir.y * r);
                var far = (x: c.x + dir.x * (r + 10), y: c.y + dir.y * (r + 10));
                if (dia) { var o = (x: c.x - dir.x * r, y: c.y - dir.y * r); Line(o.x, o.y, far.x, far.y, LineStyle.Dimension); Arrow(o.x, o.y, dir.x, dir.y); }
                else Line(c.x, c.y, far.x, far.y, LineStyle.Dimension);
                Arrow(e.x, e.y, -dir.x, -dir.y);
                bool right = dir.x >= 0;
                Line(far.x, far.y, far.x + (right ? 4 : -4), far.y, LineStyle.Dimension);
                string txt = a.Text.Length > 0 ? a.Text : (dia ? "Ø" : "R") + Num(a.Radius * (dia ? 2 : 1));
                Text(far.x + (right ? 4.6 : -4.6), far.y + 1.2, txt + (a.Tolerance.Length > 0 ? " " + a.Tolerance : ""), 3.4, right ? "start" : "end");
                DimensionCount++;
                break;
            }
            case "note":
            {
                var p = V(a.View, a.At ?? throw new ArgumentException("at required"));
                if (a.To is { } to) { var t = V(a.View, to); Line(p.x, p.y, t.x, t.y, LineStyle.Dimension); Arrow(t.x, t.y, p.x - t.x, p.y - t.y); }
                double y = p.y;
                foreach (var line in a.Text.Split('\n')) { Text(p.x, y, line, 3.4); y += 4.6; }
                break;
            }
            case "balloon":
            {
                var p = V(a.View, a.At ?? throw new ArgumentException("at required"));
                if (a.To is { } to) { var t = V(a.View, to); double dx = t.x - p.x, dy = t.y - p.y, l = Math.Sqrt(dx * dx + dy * dy); if (l > 4) { Line(p.x + dx / l * 4, p.y + dy / l * 4, t.x, t.y, LineStyle.Dimension); Arrow(t.x, t.y, -dx, -dy); } }
                Add(new PolyP(Circle(p.x, p.y, 4), LineStyle.Dimension, true));
                Text(p.x, p.y + 1.3, a.Number > 0 ? a.Number.ToString() : a.Text, 3.6, "middle", 0, true);
                break;
            }
            case "datum":
            {
                var p = V(a.View, a.At ?? throw new ArgumentException("at required"));
                Add(new FillP([(p.x, p.y), (p.x - 2.6, p.y + 4.5), (p.x + 2.6, p.y + 4.5)]));
                Line(p.x, p.y + 4.5, p.x, p.y + 8, LineStyle.Dimension);
                Add(new PolyP([(p.x - 3.6, p.y + 8), (p.x + 3.6, p.y + 8), (p.x + 3.6, p.y + 15.2), (p.x - 3.6, p.y + 15.2)], LineStyle.Dimension, true));
                Text(p.x, p.y + 13.4, a.Text.Length > 0 ? a.Text : "A", 4, "middle", 0, true);
                break;
            }
            case "gdt":
                DrawFcf(a);
                break;
            case "finish":
            {
                var p = V(a.View, a.At ?? throw new ArgumentException("at required"));
                Add(new PolyP([(p.x - 3, p.y - 4), (p.x, p.y), (p.x + 3, p.y - 8), (p.x + 9, p.y - 8)], LineStyle.Dimension));
                Text(p.x + 3.5, p.y - 9, a.Text.Length > 0 ? a.Text : "Ra 1.6", 3, "start");
                break;
            }
            default: throw new ArgumentException($"Unknown annotation type '{a.Type}'. Use dimension, diameter, radius, note, balloon, datum, gdt or finish.");
        }
    }

    /// <summary>A geometric tolerance frame: symbol | tolerance | datums, with an optional leader to the feature.</summary>
    void DrawFcf(Annotation a)
    {
        var p = V(a.View, a.At ?? throw new ArgumentException("at required"));
        const double h = 7, symW = 7;
        var datums = a.Datums.Split(['|', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
        string tol = a.Text.Length > 0 ? a.Text : a.Tolerance;
        double tolW = Math.Max(11, tol.Length * 2.2 + 5), x = p.x, y = p.y;
        double total = symW + tolW + datums.Length * 7;
        Add(new PolyP([(x, y), (x + total, y), (x + total, y + h), (x, y + h)], LineStyle.Dimension, true));
        Line(x + symW, y, x + symW, y + h, LineStyle.Dimension); Line(x + symW + tolW, y, x + symW + tolW, y + h, LineStyle.Dimension);
        for (int i = 1; i < datums.Length; i++) Line(x + symW + tolW + i * 7, y, x + symW + tolW + i * 7, y + h, LineStyle.Dimension);
        Text(x + symW + tolW / 2, y + 4.9, tol, 3.4, "middle");
        for (int i = 0; i < datums.Length; i++) Text(x + symW + tolW + i * 7 + 3.5, y + 4.9, datums[i], 3.4, "middle");
        GdtSymbol(a.Symbol.ToLowerInvariant(), x + symW / 2, y + h / 2);
        if (a.To is { } to) { var t = V(a.View, to); Line(x, y + h / 2, t.x, t.y, LineStyle.Dimension); Arrow(t.x, t.y, x - t.x, y + h / 2 - t.y); }
        DimensionCount++;
    }

    void GdtSymbol(string s, double cx, double cy)
    {
        void L(double x1, double y1, double x2, double y2) => Line(cx + x1, cy + y1, cx + x2, cy + y2, LineStyle.Dimension);
        void C(double r, double ox = 0) => Add(new PolyP(Circle(cx + ox, cy, r, 24), LineStyle.Dimension, true));
        switch (s)
        {
            case "flatness": Add(new PolyP([(cx - 2.8, cy + 1.4), (cx - 1.2, cy - 1.4), (cx + 2.8, cy - 1.4), (cx + 1.2, cy + 1.4)], LineStyle.Dimension, true)); break;
            case "straightness": L(-2.8, 0, 2.8, 0); break;
            case "circularity" or "roundness": C(2.2); break;
            case "cylindricity": C(1.8); L(-3.4, 2.2, -1.4, -2.2); L(3.4, 2.2, 1.4, -2.2); break;
            case "perpendicularity": L(-2.8, 2.4, 2.8, 2.4); L(0, 2.4, 0, -2.6); break;
            case "parallelism": L(-1, 2.4, 1.2, -2.4); L(1.2, 2.4, 3.4, -2.4); L(-3.2, 2.4, -1, -2.4); break;
            case "angularity": L(-2.8, 2.4, 2.8, -1.6); L(-2.8, 2.4, 2.8, 2.4); break;
            case "position": C(2.6); L(-3.6, 0, 3.6, 0); L(0, -3.6, 0, 3.6); break;
            case "concentricity" or "coaxiality": C(2.6); C(1.2); break;
            case "symmetry": L(-2.8, -2, 2.8, -2); L(-2.8, 0, 2.8, 0); L(-2.8, 2, 2.8, 2); break;
            case "runout" or "circular_runout": L(-2.6, 2.6, 2.4, -2.4); Add(new FillP([(cx + 2.8, cy - 2.8), (cx + 0.2, cy - 2.2), (cx + 2.2, cy - 0.2)])); break;
            case "total_runout": L(-3, 2.6, 1.4, -1.8); Add(new FillP([(cx + 1.8, cy - 2.2), (cx - 0.8, cy - 1.8), (cx + 1.4, cy + 0.2)])); L(-0.4, 2.6, 4, -1.8); Add(new FillP([(cx + 4.4, cy - 2.2), (cx + 1.8, cy - 1.8), (cx + 4, cy + 0.2)])); break;
            case "profile_line": Add(new PolyP(Enumerable.Range(0, 17).Select(i => (cx - 3 + 6.0 * i / 16, cy + 2.4 - 4.8 * Math.Sin(Math.PI * i / 16))).ToArray(), LineStyle.Dimension)); break;
            case "profile_surface": Add(new PolyP(Enumerable.Range(0, 17).Select(i => (cx - 3 + 6.0 * i / 16, cy + 2.4 - 4.8 * Math.Sin(Math.PI * i / 16))).ToArray(), LineStyle.Dimension)); L(-3, 2.4, 3, 2.4); break;
            default: Text(cx, cy + 1.2, s.Length > 0 ? s[..Math.Min(3, s.Length)].ToUpperInvariant() : "?", 2.6, "middle"); break;
        }
    }

    // ---- title block, BOM, notes
    void DrawTitleBlock(IReadOnlyList<BomItem> bom)
    {
        var f = Opt.Format; var t = Opt.Title;
        double x = f.W - Margin - TitleW, y = f.H - Margin - TitleH;
        Add(new PolyP([(x, y), (x + TitleW, y), (x + TitleW, y + TitleH), (x, y + TitleH)], LineStyle.Visible, true));
        Line(x, y + 16, x + TitleW, y + 16, LineStyle.Visible); Line(x + 110, y + 16, x + 110, y + TitleH, LineStyle.Visible);
        Line(x, y + 29, x + TitleW, y + 29, LineStyle.Dimension); Line(x + 145, y + 16, x + 145, y + TitleH, LineStyle.Dimension);
        Text(x + 3, y + 11, t.Title, 7, "start", 0, true);
        if (t.Company.Length > 0) Text(x + 113, y + 6, t.Company, 3.2);
        void Cell(double dx, double dy, string head, string val, bool bold = false) { Text(x + dx, y + dy, head, 2); Text(x + dx, y + dy + 5.5, val, 3.2, "start", 0, bold); }
        Cell(3, 21, "PART NO.", t.Number); Cell(113, 21, "MATERIAL", t.Material); Cell(148, 21, "SCALE", ScaleText, true);
        Cell(3, 34, "DRAWN", t.Drawn); Cell(113, 34, "DATE", t.Date); Cell(148, 34, "REV / SHEET", $"{t.Revision} / {f.Name}");
        // projection symbol: truncated cone seen from the side and the end
        double px = x - 28, py = y + TitleH - 8;
        if (Opt.ThirdAngle) { Add(new PolyP([(px, py - 5), (px + 14, py - 2.5), (px + 14, py + 2.5), (px, py + 5)], LineStyle.Dimension, true)); Add(new PolyP(Circle(px + 24, py, 5, 24), LineStyle.Dimension, true)); Add(new PolyP(Circle(px + 24, py, 2.5, 24), LineStyle.Dimension, true)); }
        else { Add(new PolyP(Circle(px + 6, py, 5, 24), LineStyle.Dimension, true)); Add(new PolyP(Circle(px + 6, py, 2.5, 24), LineStyle.Dimension, true)); Add(new PolyP([(px + 16, py - 5), (px + 30, py - 2.5), (px + 30, py + 2.5), (px + 16, py + 5)], LineStyle.Dimension, true)); }
        // BOM above the title block
        if ((bom.Count > 1 || Opt.Bom == true) && Opt.Bom != false)
        {
            double rows = bom.Count + 1, by = y - rows * BomRow;
            Add(new PolyP([(x, by), (x + TitleW, by), (x + TitleW, y), (x, y)], LineStyle.Visible, true));
            double[] cx = [x, x + 14, x + 28, x + 135, x + TitleW];
            for (int i = 1; i < cx.Length - 1; i++) Line(cx[i], by, cx[i], y, LineStyle.Dimension);
            for (int r = 1; r < rows; r++) Line(x, by + r * BomRow, x + TitleW, by + r * BomRow, LineStyle.Dimension);
            string[] heads = ["ITEM", "QTY", "PART", "MATERIAL"];
            for (int i = 0; i < 4; i++) Text(cx[i] + 2, by + 4.2, heads[i], 3, "start", 0, true);
            for (int r = 0; r < bom.Count; r++)
            {
                double ry = by + (r + 1) * BomRow + 4.2;
                Text(cx[0] + 2, ry, (r + 1).ToString(), 3.2); Text(cx[1] + 2, ry, bom[r].Qty.ToString(), 3.2); Text(cx[2] + 2, ry, bom[r].Name, 3.2); Text(cx[3] + 2, ry, bom[r].Material, 3.2);
            }
        }
        // general notes, bottom left
        var notes = new List<string>(Opt.Notes);
        notes.Insert(0, $"UNLESS OTHERWISE SPECIFIED: DIMENSIONS IN {(Opt.Units.StartsWith("in", StringComparison.OrdinalIgnoreCase) ? "INCHES" : "MILLIMETRES")}.  {Opt.Tolerance}");
        notes.Add("BREAK ALL SHARP EDGES.  DO NOT SCALE DRAWING.");
        double ny = f.H - Margin - 3 - (notes.Count - 1) * 4.2;
        foreach (var n in notes) { Text(Margin + 3, ny, n, 2.8); ny += 4.2; }
    }
}
