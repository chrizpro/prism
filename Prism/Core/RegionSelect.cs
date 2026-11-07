using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>How a click or drag in the viewport picks things.</summary>
public enum SelectMethod { Click, Rectangle, Lasso, Paint }

/// <summary>
/// Picks everything inside a rectangle or a lasso drawn on screen. Only what you can see is picked, unless "through" is on (X-ray): then faces,
/// edges and bodies behind others are picked too, on both sides of the part. A rectangle dragged left to right takes only what is completely
/// inside it; dragged right to left (or a lasso) it takes whatever it touches. Paint takes whatever the stroke itself crosses.
/// </summary>
public static class RegionSelect
{
    /// <summary>The screen shape that was drawn: a list of points, closed for a lasso, or two corners for a rectangle.</summary>
    public sealed class Region
    {
        public readonly List<(double x, double y)> Points = [];
        public bool IsBox;
        public bool Crossing;
        /// <summary>Paint select: the trail itself is the selector — whatever it crosses counts.</summary>
        public bool Stroke;
        public double X0 => Points.Min(p => p.x); public double X1 => Points.Max(p => p.x);
        public double Y0 => Points.Min(p => p.y); public double Y1 => Points.Max(p => p.y);

        public static Region Box(double x0, double y0, double x1, double y1) =>
            new() { IsBox = true, Crossing = x1 < x0, Points = { (Math.Min(x0, x1), Math.Min(y0, y1)), (Math.Max(x0, x1), Math.Max(y0, y1)) } };

        public bool Contains(double x, double y)
        {
            if (IsBox) return x >= Points[0].x && x <= Points[1].x && y >= Points[0].y && y <= Points[1].y;
            bool inside = false;
            for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
            {
                var (xi, yi) = Points[i]; var (xj, yj) = Points[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        List<((double x, double y) a, (double x, double y) b)>? _segs;
        /// <summary>The stroke as a list of segments, thinned so a long drag stays quick to test.</summary>
        public IReadOnlyList<((double x, double y) a, (double x, double y) b)> Segments
        {
            get
            {
                if (_segs != null) return _segs;
                _segs = [];
                int step = Math.Max(1, Points.Count / 128);
                (double x, double y) last = Points[0];
                for (int i = step; i < Points.Count; i += step) { _segs.Add((last, Points[i])); last = Points[i]; }
                if (last != Points[^1]) _segs.Add((last, Points[^1]));
                return _segs;
            }
        }

        /// <summary>Paint: does the stroke cross this screen segment? A lone dot counts at its point.</summary>
        public bool StrokeSeg((double x, double y) p, (double x, double y) q)
        {
            if (Segments.Count == 0) return Near(Points[0], p) || Near(Points[0], q);
            foreach (var (a, b) in Segments) if (SegSeg(a, b, p, q) || Near(a, p) || Near(a, q)) return true;
            return false;
        }

        /// <summary>Paint: does the stroke cross this screen triangle?</summary>
        public bool StrokeTri((double x, double y) a, (double x, double y) b, (double x, double y) c)
        {
            if (Segments.Count == 0) return InsideTri(Points[0], a, b, c);
            foreach (var (p, q) in Segments)
                if (InsideTri(p, a, b, c) || SegSeg(p, q, a, b) || SegSeg(p, q, b, c) || SegSeg(p, q, c, a)) return true;
            return false;
        }

        /// <summary>Paint: is this screen point within <paramref name="tol"/> pixels of the stroke? A datum point is a dot, so a stroke passing over it counts.</summary>
        public bool StrokeNear(double x, double y, double tol = 6)
        {
            if (Segments.Count == 0) return Math.Abs(Points[0].x - x) + Math.Abs(Points[0].y - y) <= tol;
            foreach (var (a, b) in Segments)
            {
                double dx = b.x - a.x, dy = b.y - a.y, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Clamp(((x - a.x) * dx + (y - a.y) * dy) / len2, 0, 1);
                double ex = a.x + t * dx - x, ey = a.y + t * dy - y;
                if (ex * ex + ey * ey <= tol * tol) return true;
            }
            return false;
        }

        static bool Near((double x, double y) p, (double x, double y) q, double tol = 2) =>
            Math.Abs(p.x - q.x) + Math.Abs(p.y - q.y) <= tol;

        static bool InsideTri((double x, double y) q, (double x, double y) a, (double x, double y) b, (double x, double y) c)
        {
            double d1 = (q.x - b.x) * (a.y - b.y) - (a.x - b.x) * (q.y - b.y);
            double d2 = (q.x - c.x) * (b.y - c.y) - (b.x - c.x) * (q.y - c.y);
            double d3 = (q.x - a.x) * (c.y - a.y) - (c.x - a.x) * (q.y - a.y);
            bool neg = d1 < -1e-9 || d2 < -1e-9 || d3 < -1e-9, pos = d1 > 1e-9 || d2 > 1e-9 || d3 > 1e-9;
            return !(neg && pos);
        }

        static bool SegSeg((double x, double y) p0, (double x, double y) p1, (double x, double y) q0, (double x, double y) q1)
        {
            double d = (q1.y - q0.y) * (p1.x - p0.x) - (q1.x - q0.x) * (p1.y - p0.y);
            if (Math.Abs(d) < 1e-12) return false;      // parallel strokes are covered by the point tests
            double t = ((q1.x - q0.x) * (p0.y - q0.y) - (q1.y - q0.y) * (p0.x - q0.x)) / d;
            double u = ((p1.x - p0.x) * (p0.y - q0.y) - (p1.y - p0.y) * (p0.x - q0.x)) / d;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }
    }

    /// <summary>A coarse depth image of the region, so "is this point in front?" is a lookup instead of a ray cast.</summary>
    sealed class DepthMap
    {
        readonly double _x0, _y0, _step; readonly int _w, _h; readonly float[] _z;

        public DepthMap(Camera cam, IEnumerable<SceneObject> objects, Region r)
        {
            double x0 = Math.Max(0, Math.Floor(r.X0)), y0 = Math.Max(0, Math.Floor(r.Y0)), x1 = Math.Min(cam.Width, Math.Ceiling(r.X1)), y1 = Math.Min(cam.Height, Math.Ceiling(r.Y1));
            _x0 = x0; _y0 = y0;
            _step = Math.Max(1, Math.Ceiling(Math.Max(x1 - x0, y1 - y0) / 700));
            _w = Math.Max(1, (int)Math.Ceiling((x1 - x0) / _step)); _h = Math.Max(1, (int)Math.Ceiling((y1 - y0) / _step));
            _z = new float[_w * _h]; Array.Fill(_z, float.MinValue);
            var vp = cam.ViewProjection;
            foreach (var obj in objects)
            {
                var m = obj.Mesh; var P = m.Positions; var I = m.Indices;
                var proj = new (double x, double y, double depth)?[m.VertexCount];
                for (int v = 0; v < proj.Length; v++) proj[v] = ProjectKey(cam, new Vec3(P[3 * v], P[3 * v + 1], P[3 * v + 2]), vp);
                for (int t = 0; t + 2 < I.Length; t += 3)
                {
                    if (proj[I[t]] is not { } a || proj[I[t + 1]] is not { } b || proj[I[t + 2]] is not { } c) continue;
                    double bx0 = Math.Min(a.x, Math.Min(b.x, c.x)), bx1 = Math.Max(a.x, Math.Max(b.x, c.x)), by0 = Math.Min(a.y, Math.Min(b.y, c.y)), by1 = Math.Max(a.y, Math.Max(b.y, c.y));
                    if (bx1 < x0 || bx0 > x1 || by1 < y0 || by0 > y1) continue;
                    Fill(a, b, c);
                }
            }
        }

        void Fill((double x, double y, double depth) a, (double x, double y, double depth) b, (double x, double y, double depth) c)
        {
            double den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Math.Abs(den) < 1e-12) return;
            int ix0 = Math.Max(0, (int)Math.Floor((Math.Min(a.x, Math.Min(b.x, c.x)) - _x0) / _step)), ix1 = Math.Min(_w - 1, (int)Math.Ceiling((Math.Max(a.x, Math.Max(b.x, c.x)) - _x0) / _step));
            int iy0 = Math.Max(0, (int)Math.Floor((Math.Min(a.y, Math.Min(b.y, c.y)) - _y0) / _step)), iy1 = Math.Min(_h - 1, (int)Math.Ceiling((Math.Max(a.y, Math.Max(b.y, c.y)) - _y0) / _step));
            for (int iy = iy0; iy <= iy1; iy++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    double px = _x0 + (ix + 0.5) * _step, py = _y0 + (iy + 0.5) * _step;
                    double w1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / den, w2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / den, w3 = 1 - w1 - w2;
                    if (w1 < -1e-6 || w2 < -1e-6 || w3 < -1e-6) continue;
                    float z = (float)(w1 * a.depth + w2 * b.depth + w3 * c.depth);
                    int k = iy * _w + ix; if (z > _z[k]) _z[k] = z;
                }
        }

        /// <summary>True when nothing covers this point in front of depth <paramref name="z"/>.</summary>
        public bool Visible(double x, double y, double z, double eps)
        {
            int ix = (int)((x - _x0) / _step), iy = (int)((y - _y0) / _step);
            if (ix < 0 || iy < 0 || ix >= _w || iy >= _h) return false;
            // a bigger key is nearer. Look at the neighbouring cells too, and take the farthest of them, so a face edge-on to the view or right at an outline is not lost
            float here = _z[iy * _w + ix];
            if (here == float.MinValue) return true;
            float far = here;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int jx = ix + dx, jy = iy + dy; if (jx < 0 || jy < 0 || jx >= _w || jy >= _h) continue;
                float v = _z[jy * _w + jx]; if (v != float.MinValue && v < far) far = v;
            }
            return z >= far - eps * Math.Abs(far);
        }
    }

    /// <summary>A screen point plus a depth key that grows toward the viewer and is linear in screen space (1 / distance, or minus the distance when orthographic).</summary>
    static (double x, double y, double depth)? ProjectKey(Camera cam, Vec3 p, Matrix4x4 vp)
    {
        if (cam.Project(p, vp) is not { } q) return null;
        double d = Vec3.Dot(p - cam.Eye, cam.Forward);
        if (d <= 1e-9 && !cam.Orthographic) return null;
        return (q.x, q.y, cam.Orthographic ? -d : 1 / d);
    }

    static bool Eligible(CadSession s, SceneObject o) =>
        o.Visible && !s.HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form or ObjKind.Curve or ObjKind.Sketch or ObjKind.Mesh && (o.Kind != ObjKind.Mesh || o.Tri != null);

    /// <summary>Everything of the given kind inside the region. <paramref name="mode"/> decides faces, edges or whole bodies.</summary>
    public static List<SelItem> Collect(CadSession s, Region region, SelMode mode, bool through)
    {
        var cam = s.Camera; var vp = cam.ViewProjection;
        var objects = s.Objects.Where(o => Eligible(s, o)).ToList();
        var result = new List<SelItem>();
        if (region.Points.Count < 2) return result;
        DepthMap? depth = through ? null : new DepthMap(cam, objects, region);

        foreach (var obj in objects)
        {
            var m = obj.Mesh; var P = m.Positions; var I = m.Indices;
            var proj = new (double x, double y, double depth)?[m.VertexCount];
            for (int v = 0; v < proj.Length; v++) proj[v] = ProjectKey(cam, new Vec3(P[3 * v], P[3 * v + 1], P[3 * v + 2]), vp);

            bool whole = obj.Kind is ObjKind.Mesh, wire = obj.Kind is ObjKind.Curve or ObjKind.Sketch;
            if (wire && mode == SelMode.Face) continue;
            if (wire && mode == SelMode.Object)
            {
                // a curve has no faces: it is picked by its segments
                var W = m.EdgePoints; bool any = false, all = true, seen = false;
                for (int sg = 0; sg < m.SegEdge.Length; sg++)
                {
                    var a = ProjectKey(cam, new Vec3(W[6 * sg], W[6 * sg + 1], W[6 * sg + 2]), vp); var b = ProjectKey(cam, new Vec3(W[6 * sg + 3], W[6 * sg + 4], W[6 * sg + 5]), vp);
                    if (a is not { } pa || b is not { } pb) { all = false; continue; }
                    double mx = (pa.x + pb.x) / 2, my = (pa.y + pb.y) / 2, mz = (pa.depth + pb.depth) / 2;
                    bool ina, inb, inm;
                    if (region.Stroke) { inm = region.StrokeSeg((pa.x, pa.y), (pb.x, pb.y)); ina = inb = inm; }
                    else { ina = region.Contains(pa.x, pa.y); inb = region.Contains(pb.x, pb.y); inm = region.Contains(mx, my); }
                    if (ina || inb || inm) { any = true; if (depth == null || SegmentVisible(depth, region, pa, pb, 2e-3)) seen = true; }
                    if (!ina || !inb) all = false;
                }
                if (region.Crossing || !region.IsBox ? any && seen : all && any) result.Add(SelItem.Of(obj));
                continue;
            }
            if (mode == SelMode.Edge && !whole)
            {
                var E = m.EdgePoints; var hit = new HashSet<int>(); var rejected = new HashSet<int>();
                for (int sg = 0; sg < m.SegEdge.Length; sg++)
                {
                    int edge = m.SegEdge[sg];
                    var a = ProjectKey(cam, new Vec3(E[6 * sg], E[6 * sg + 1], E[6 * sg + 2]), vp); var b = ProjectKey(cam, new Vec3(E[6 * sg + 3], E[6 * sg + 4], E[6 * sg + 5]), vp);
                    if (a is not { } pa || b is not { } pb) { rejected.Add(edge); continue; }
                    double mx = (pa.x + pb.x) / 2, my = (pa.y + pb.y) / 2, mz = (pa.depth + pb.depth) / 2;
                    if (region.Stroke) { if (!region.StrokeSeg((pa.x, pa.y), (pb.x, pb.y))) continue; }
                    else
                    {
                        bool ina = region.Contains(pa.x, pa.y), inb = region.Contains(pb.x, pb.y);
                        if (region.Crossing || !region.IsBox) { if (!(ina || inb || region.Contains(mx, my))) continue; }
                        else if (!(ina && inb)) { rejected.Add(edge); continue; }
                    }
                    if (depth != null && !SegmentVisible(depth, region, pa, pb, 2e-3)) continue;
                    hit.Add(edge);
                }
                // a complete-inside rectangle takes an edge only when every one of its segments is inside
                foreach (int e in hit) if (!(region.IsBox && !region.Crossing) || !rejected.Contains(e)) result.Add(new SelItem(obj.Id, SelKind.Edge, e));
                continue;
            }

            var faces = new List<int>();
            int faceCount = whole ? 1 : m.FaceCount;
            for (int f = 0; f < faceCount; f++)
            {
                int t0 = whole ? 0 : m.FaceTriStart[f], t1 = whole ? m.TriangleCount : m.FaceTriStart[f + 1];
                if (t1 <= t0) continue;
                bool allIn = true, anyIn = false, anyVisible = false; int samples = 0, stride = Math.Max(1, (t1 - t0) / 48);
                for (int t = t0; t < t1; t++)
                {
                    var a = proj[I[3 * t]]; var b = proj[I[3 * t + 1]]; var c = proj[I[3 * t + 2]];
                    if (a == null || b == null || c == null) { allIn = false; continue; }
                    double cx = (a.Value.x + b.Value.x + c.Value.x) / 3, cy = (a.Value.y + b.Value.y + c.Value.y) / 3, cz = (a.Value.depth + b.Value.depth + c.Value.depth) / 3;
                    if (region.Stroke)
                    {
                        if (!anyIn && region.StrokeTri((a.Value.x, a.Value.y), (b.Value.x, b.Value.y), (c.Value.x, c.Value.y)))
                        { anyIn = true; if (depth == null || depth.Visible(cx, cy, cz, 1e-3)) anyVisible = true; }
                        continue;
                    }
                    foreach (var q in new[] { a.Value, b.Value, c.Value }) { if (region.Contains(q.x, q.y)) anyIn = true; else allIn = false; }
                    if ((t - t0) % stride != 0) continue;
                    samples++;
                    if (region.Contains(cx, cy)) { anyIn = true; if (depth == null || depth.Visible(cx, cy, cz, 1e-3)) anyVisible = true; }
                }
                if (!anyIn && region.IsBox && region.Crossing) anyIn = BoxInsideFace(region, proj, I, t0, t1);
                if (depth == null) anyVisible = anyIn;
                bool take = region.Crossing || !region.IsBox ? anyIn && anyVisible : allIn && (depth == null || anyVisible || FaceVisible(proj, I, t0, t1, depth));
                if (take) faces.Add(f);
            }
            // a scanned mesh is one face (index 0), exactly what a click on it in Face mode gives; a cage has its own faces
            if (mode == SelMode.Face) foreach (int f in faces) result.Add(new SelItem(obj.Id, SelKind.Face, f));
            else if (mode != SelMode.Face && faces.Count > 0)
            {
                // whole bodies: a drag that crosses the body, or a rectangle that encloses all of it
                bool enclosed = true;
                if (!region.Crossing && region.IsBox) for (int v = 0; v < proj.Length && enclosed; v++) if (proj[v] is { } pv && !region.Contains(pv.x, pv.y)) enclosed = false;
                if (region.Crossing || !region.IsBox || enclosed) result.Add(SelItem.Of(obj));
            }
        }
        if (mode == SelMode.Object) CollectReferences(s, region, depth, result);
        return result;
    }

    /// <summary>Datums and construction planes: no mesh, so they are tested by the key points and lines they are drawn with.</summary>
    static void CollectReferences(CadSession s, Region r, DepthMap? depth, List<SelItem> result)
    {
        var cam = s.Camera; var vp = cam.ViewProjection;
        bool window = r.IsBox && !r.Crossing;
        double cx = (r.X0 + r.X1) / 2, cy = (r.Y0 + r.Y1) / 2;
        foreach (var sh in s.ReferenceShapes())
        {
            var pts = sh.Points.Select(p => ProjectKey(cam, p, vp)).ToArray();
            bool allIn = true, any = false; (double x, double y, double depth)? probe = null;
            foreach (var p in pts)
            {
                if (p is not { } q) { allIn = false; continue; }
                if (r.Contains(q.x, q.y)) { any = true; probe ??= q; } else allIn = false;
            }
            if (window) { if (!allIn || !any) continue; }
            else if (r.Stroke)
            {
                bool hit = false;
                foreach (var p in pts) if (p is { } q && r.StrokeNear(q.x, q.y)) { hit = true; probe ??= q; }
                foreach (var (a, b) in sh.Lines)
                    if (pts[a] is { } qa && pts[b] is { } qb && r.StrokeSeg((qa.x, qa.y), (qb.x, qb.y))) { hit = true; probe ??= qa; }
                if (sh.Filled && !hit && pts[0] is { } p0 && pts[1] is { } p1 && pts[2] is { } p2 && pts[3] is { } p3
                    && (r.StrokeTri((p0.x, p0.y), (p1.x, p1.y), (p2.x, p2.y)) || r.StrokeTri((p0.x, p0.y), (p2.x, p2.y), (p3.x, p3.y)))) { hit = true; probe ??= p0; }
                if (!hit) continue;
            }
            else
            {
                // a line passing through the region without an end or key point in it
                foreach (var (a, b) in sh.Lines)
                {
                    if (pts[a] is not { } qa || pts[b] is not { } qb) continue;
                    for (int k = 1; k < 32; k++)
                    {
                        double t = k / 32.0, x = qa.x + (qb.x - qa.x) * t, y = qa.y + (qb.y - qa.y) * t;
                        if (r.Contains(x, y)) { any = true; probe ??= (x, y, qa.depth + (qb.depth - qa.depth) * t); }
                    }
                }
                // a region lying wholly inside a plane's outline touches it though none of its lines do
                if (!any && sh.Filled && pts.Take(4).All(p => p != null))
                {
                    var poly = pts.Take(4).Select(p => (p!.Value.x, p.Value.y)).ToArray();
                    foreach (var c in r.Points.Append((cx, cy)))
                        if (InPoly(poly, c.Item1, c.Item2)) { any = true; probe ??= (c.Item1, c.Item2, pts[0]!.Value.depth); break; }
                }
                if (!any) continue;
            }
            if (depth != null && sh.CheckDepth && probe is { } pb && !depth.Visible(pb.x, pb.y, pb.depth, 5e-3)) continue;
            result.Add(sh.Item);
        }
    }

    static bool InPoly((double x, double y)[] poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var (xi, yi) = poly[i]; var (xj, yj) = poly[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Is a segment in front, judged where the depth image actually exists? The depth image only covers the region, so the midpoint of
    /// an edge whose end alone is inside a small rectangle would be "off the map" and the edge wrongly treated as hidden.
    /// </summary>
    static bool SegmentVisible(DepthMap depth, Region r, (double x, double y, double depth) a, (double x, double y, double depth) b, double eps)
    {
        var mid = ((a.x + b.x) / 2, (a.y + b.y) / 2, (a.depth + b.depth) / 2);
        foreach (var q in new[] { mid, a, b })
            if (q.Item1 >= r.X0 && q.Item1 <= r.X1 && q.Item2 >= r.Y0 && q.Item2 <= r.Y1) return depth.Visible(q.Item1, q.Item2, q.Item3, eps);
        return depth.Visible(mid.Item1, mid.Item2, mid.Item3, eps);
    }

    static bool VertexVisible(Region r, (double x, double y, double depth)?[] proj, int[] I, int t0, int t1, DepthMap depth)
    {
        for (int t = t0; t < t1; t++)
            for (int k = 0; k < 3; k++)
                if (proj[I[3 * t + k]] is { } q && r.Contains(q.x, q.y) && depth.Visible(q.x, q.y, q.depth, 1e-3)) return true;
        return false;
    }

    static bool FaceVisible((double x, double y, double depth)?[] proj, int[] I, int t0, int t1, DepthMap depth)
    {
        for (int t = t0; t < t1; t++)
            if (proj[I[3 * t]] is { } a && proj[I[3 * t + 1]] is { } b && proj[I[3 * t + 2]] is { } c && depth.Visible((a.x + b.x + c.x) / 3, (a.y + b.y + c.y) / 3, (a.depth + b.depth + c.depth) / 3, 1e-3)) return true;
        return false;
    }

    /// <summary>A small rectangle inside one big face touches it even though none of the face's corners are in the rectangle.</summary>
    static bool BoxInsideFace(Region r, (double x, double y, double depth)?[] proj, int[] I, int t0, int t1)
    {
        double cx = (r.X0 + r.X1) / 2, cy = (r.Y0 + r.Y1) / 2;
        for (int t = t0; t < t1; t++)
        {
            if (proj[I[3 * t]] is not { } a || proj[I[3 * t + 1]] is not { } b || proj[I[3 * t + 2]] is not { } c) continue;
            double d1 = (cx - b.x) * (a.y - b.y) - (a.x - b.x) * (cy - b.y), d2 = (cx - c.x) * (b.y - c.y) - (b.x - c.x) * (cy - c.y), d3 = (cx - a.x) * (c.y - a.y) - (c.x - a.x) * (cy - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos)) return true;
        }
        return false;
    }
}
