using Prism.Kernel;
using Prism.Meshing;

// Pure-C# tests (no OpenCascade needed): extended primitive fits, region merge / adjacency / boundaries / intersections, line-arc fitting.
int checks = 0, failures = 0;
void Check(bool ok, string msg) { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS " : "FAIL ") + msg); }
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

// ------------------------------------------------------------------ mesh builders
TriMesh Grid(int nu, int nv, Func<int, int, Vec3> pos, bool wrapU, bool wrapV)
{
    var v = new List<float>(); int cu = wrapU ? nu : nu + 1, cv = wrapV ? nv : nv + 1;
    for (int i = 0; i < cu; i++) for (int j = 0; j < cv; j++) { var p = pos(i, j); v.Add((float)p.X); v.Add((float)p.Y); v.Add((float)p.Z); }
    int Id(int i, int j) => (i % cu) * cv + (j % cv);
    var t = new List<int>();
    for (int i = 0; i < nu; i++)
        for (int j = 0; j < nv; j++)
        {
            int a = Id(i, j), b = Id(i + 1, j), c = Id(i + 1, j + 1), d = Id(i, j + 1);
            t.AddRange([a, b, c, a, c, d]);
        }
    return new TriMesh([.. v], [.. t]);
}

TriMesh Torus(double R, double r, int nu, int nv, double u1 = 2 * Math.PI, double v1 = 2 * Math.PI)
    => Grid(nu, nv, (i, j) => { double u = u1 * i / nu, w = v1 * j / nv; return new Vec3((R + r * Math.Cos(w)) * Math.Cos(u), (R + r * Math.Cos(w)) * Math.Sin(u), r * Math.Sin(w)); }, u1 >= 2 * Math.PI - 1e-9, v1 >= 2 * Math.PI - 1e-9);

TriMesh Extruded(int n, int nz, double h) => Grid(n, nz, (i, j) => { double a = 2 * Math.PI * i / n, rr = 10 + 2 * Math.Cos(3 * a); return new Vec3(rr * Math.Cos(a), 1.3 * rr * Math.Sin(a), h * j / nz); }, true, false);

TriMesh Vase(int n, int nh, double h) => Grid(n, nh, (i, j) => { double a = 2 * Math.PI * i / n, z = h * j / nh, rr = 8 + 3 * Math.Sin(0.35 * z); return new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), z); }, true, false);

TriMesh Bumpy(int nx, int ny) => Grid(nx, ny, (i, j) => { double x = 40.0 * i / nx, y = 30.0 * j / ny; return new Vec3(x, y, 2 * Math.Sin(x / 6) * Math.Cos(y / 5) + 0.05 * x); }, false, false);

// plate 60 x 40 x 10, one rounded corner (R8), a Ø12 hole
TriMesh Plate(out int holeVertsStart)
{
    var outer = new List<(double x, double y)>();
    void Line(double x0, double y0, double x1, double y1) { int k = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / 4); for (int i = 0; i < k; i++) outer.Add((x0 + (x1 - x0) * i / k, y0 + (y1 - y0) * i / k)); }
    Line(0, 0, 60, 0); Line(60, 0, 60, 32);
    for (int i = 0; i < 24; i++) { double a = Math.PI / 2 * i / 24; outer.Add((52 + 8 * Math.Cos(a), 32 + 8 * Math.Sin(a))); }
    Line(52, 40, 0, 40); Line(0, 40, 0, 0);
    var hole = Enumerable.Range(0, 64).Select(i => (x: 30 + 6 * Math.Cos(2 * Math.PI * i / 64), y: 20 + 6 * Math.Sin(2 * Math.PI * i / 64))).ToList();
    const double H = 10; var v = new List<float>(); var t = new List<int>();
    int V(double x, double y, double z) { v.Add((float)x); v.Add((float)y); v.Add((float)z); return v.Count / 3 - 1; }
    // top and bottom faces
    var all = outer.Concat(hole).ToList();
    var tris = Triangulator.Polygon(outer, [hole]);
    var top = all.Select(p => V(p.x, p.y, H)).ToArray(); var bot = all.Select(p => V(p.x, p.y, 0)).ToArray();
    foreach (var (a, b, c) in tris)
    {
        double ar = (all[b].x - all[a].x) * (all[c].y - all[a].y) - (all[b].y - all[a].y) * (all[c].x - all[a].x);
        if (ar >= 0) { t.AddRange([top[a], top[b], top[c], bot[a], bot[c], bot[b]]); } else { t.AddRange([top[a], top[c], top[b], bot[a], bot[b], bot[c]]); }
    }
    // walls: outer CCW is outward; hole wall faces the axis
    void Wall(List<(double x, double y)> ring, bool outward)
    {
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var p = ring[i]; var q = ring[(i + 1) % n];
            int a = V(p.x, p.y, 0), b = V(q.x, q.y, 0), c = V(q.x, q.y, H), d = V(p.x, p.y, H);
            if (outward) t.AddRange([a, b, c, a, c, d]); else t.AddRange([a, c, b, a, d, c]);
        }
    }
    double sa = 0; for (int i = 0; i < outer.Count; i++) { var p = outer[i]; var q = outer[(i + 1) % outer.Count]; sa += p.x * q.y - q.x * p.y; }
    Wall(sa > 0 ? outer : Enumerable.Reverse(outer).ToList(), true);
    Wall(hole, false);
    holeVertsStart = outer.Count;
    return MeshOps.Weld(new TriMesh([.. v], [.. t]), 1e-5);
}

// ------------------------------------------------------------------ torus
{
    var m = Torus(20, 5, 96, 40); var all = Enumerable.Range(0, m.TriCount).ToList();
    var r = PrimitiveFit2.FitTorus(m, all, 0.05);
    Check(r is { Kind: PrimKind.Torus }, "full torus detected");
    if (r != null)
    {
        Check(Near(r.Radius, 20, 0.05) && Near(r.MinorRadius, 5, 0.05), $"torus radii R={r.Radius:0.###} r={r.MinorRadius:0.###}");
        Check(Math.Abs(Vec3.Dot(r.Axis, Vec3.UnitZ)) > 0.9999 && r.Origin.Length < 0.05, "torus axis and centre");
        Check(r.Rms < 0.02 && r.Convex, $"torus rms {r.Rms:0.0000}");
    }
    var quarter = Torus(20, 5, 32, 24, Math.PI / 2, Math.PI / 2);        // a fillet-like piece: quarter ring, quarter tube
    var q = PrimitiveFit2.FitTorus(quarter, Enumerable.Range(0, quarter.TriCount).ToList(), 0.05);
    Check(q != null && Near(q.Radius, 20, 0.1) && Near(q.MinorRadius, 5, 0.1), $"partial torus patch fitted (R={q?.Radius:0.###}, r={q?.MinorRadius:0.###})");
    var ext = PrimitiveFit.Detect(m, new PrimitiveFit.Options(0.05, 12, 20, Extended: true));
    Check(ext.Any(x => x.Kind == PrimKind.Torus && x.Area > 0.9 * m.Area()), "Detect(Extended) turns the cylinder/sphere fragments into one torus: " + string.Join(", ", ext.Select(x => x.Kind)));
    var bumpyTorusWrong = PrimitiveFit2.FitTorus(Bumpy(40, 30), Enumerable.Range(0, Bumpy(40, 30).TriCount).ToList(), 0.05);
    Check(bumpyTorusWrong == null, "a bumpy patch is not a torus");
}

// ------------------------------------------------------------------ extrusion
{
    var m = Extruded(120, 6, 15); var all = Enumerable.Range(0, m.TriCount).ToList();
    var r = PrimitiveFit2.FitExtrusion(m, all, 0.05);
    Check(r is { Kind: PrimKind.Extrusion }, "extruded wavy profile detected");
    if (r != null)
    {
        Check(Math.Abs(Vec3.Dot(r.Axis, Vec3.UnitZ)) > 0.999, "extrusion axis is Z");
        Check(Near(r.T1 - r.T0, 15, 0.01) && r.Rms < 0.04, $"extrusion length {r.T1 - r.T0:0.###}, rms {r.Rms:0.0000}, section points {r.Profile?.Length}");
        Check(r.ProfileClosed, "closed section");
    }
    Check(PrimitiveFit2.FitExtrusion(Vase(80, 20, 20), Enumerable.Range(0, Vase(80, 20, 20).TriCount).ToList(), 0.05) == null, "a vase is not an extrusion");
}

// ------------------------------------------------------------------ revolution
{
    var m = Vase(100, 40, 20); var all = Enumerable.Range(0, m.TriCount).ToList();
    var r = PrimitiveFit2.FitRevolution(m, all, 0.06);
    Check(r is { Kind: PrimKind.Revolution }, "revolved profile detected");
    if (r != null)
    {
        Check(Math.Abs(Vec3.Dot(r.Axis, Vec3.UnitZ)) > 0.9995, "revolution axis is Z");
        Check(r.Rms < 0.05 && r.MaxDev < 0.12, $"revolution rms {r.Rms:0.0000} max {r.MaxDev:0.0000}, profile points {r.Profile?.Length}");
        Check(r.Profile != null && r.Profile.Length >= 4, "profile polyline has structure");
    }
    var half = Grid(50, 40, (i, j) => { double a = Math.PI * i / 50, z = 20.0 * j / 40, rr = 8 + 3 * Math.Sin(0.35 * z); return new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), z); }, false, false);
    var h = PrimitiveFit2.FitRevolution(half, Enumerable.Range(0, half.TriCount).ToList(), 0.06);
    Check(h != null && Math.Abs(Vec3.Dot(h.Axis, Vec3.UnitZ)) > 0.999, "half revolution also detected");
    var vm = Vase(100, 40, 20);
    var det = PrimitiveFit.Detect(vm, new PrimitiveFit.Options(0.06, 12, 20, Extended: true));
    Check(det.Any(x => x.Kind == PrimKind.Revolution && x.Area > 0.8 * vm.Area()), "Detect(Extended) runs on a vase: " + string.Join(", ", det.Select(x => x.Describe())));
}

// ------------------------------------------------------------------ free-form
{
    var m = Bumpy(40, 30); var all = Enumerable.Range(0, m.TriCount).ToList();
    var r = PrimitiveFit2.FitFreeform(m, all, 0.05);
    Check(r is { Kind: PrimKind.Freeform, Patch: not null }, "freeform patch fitted");
    if (r?.Patch is { } p)
    {
        Check(p.Rms < 0.03 && p.MaxError < 0.1, $"patch rms {p.Rms:0.0000} max {p.MaxError:0.0000} ({p.NU}x{p.NV} poles, {p.PercentInTolerance:0.#}% in tol)");
        Check(p.Coverage > 0.95, $"coverage {p.Coverage:0.##}");
        var probe = new Vec3(20, 15, 2 * Math.Sin(20 / 6.0) * Math.Cos(15 / 5.0) + 0.05 * 20);
        Check(SurfaceMath.Distance(r, probe) < 0.03, "closest-point distance to patch");
    }
    var noisy = new Random(3); var nm = new TriMesh(m.V.Select(f => f + (float)((noisy.NextDouble() - 0.5) * 0.04)).ToArray(), m.T);
    var rn = PrimitiveFit2.FitFreeform(nm, all, 0.1);
    Check(rn?.Patch is { } pn && pn.Rms < 0.05, $"noisy patch rms {rn?.Patch?.Rms:0.0000}");
}

// ------------------------------------------------------------------ plate: detection, merge, adjacency, boundaries, intersections
{
    var m = Plate(out _);
    var regs = PrimitiveFit.Detect(m, new PrimitiveFit.Options(0.05, 10, 8));
    Console.WriteLine("  detected: " + string.Join(" | ", regs.Select(r => r.Describe())));
    var merged = RegionGraph.MergeSame(m, regs, 0.05, 10);
    int planes = merged.Count(r => r.Kind == PrimKind.Plane), cyls = merged.Count(r => r.Kind == PrimKind.Cylinder);
    Check(planes == 6 && cyls == 2, $"plate: 6 planes + 2 cylinders after merge (got {planes} + {cyls}); before merge {regs.Count} regions");
    Check(merged.Any(r => r.Kind == PrimKind.Cylinder && Near(r.Radius, 6, 0.05) && !r.Convex), "hole Ø12 found");
    Check(merged.Any(r => r.Kind == PrimKind.Cylinder && Near(r.Radius, 8, 0.5) && r.Convex), "fillet R8 found");

    // forced split of the top plane into two coplanar halves: merge must join them again
    var top = merged.First(r => r.Kind == PrimKind.Plane && r.Axis.Z > 0.9);
    var left = top.Tris.Where(t => (m.P(m.T[3 * t]).X + m.P(m.T[3 * t + 1]).X + m.P(m.T[3 * t + 2]).X) / 3 < 30).ToArray();
    var right = top.Tris.Except(left).ToArray();
    var split = merged.Where(r => r != top).Concat([PrimitiveFit2.Finish(m, top, left), PrimitiveFit2.Finish(m, top, right)]).ToList();
    var rejoined = RegionGraph.MergeSame(m, split, 0.05, 10);
    Check(split.Count == merged.Count + 1 && rejoined.Count == merged.Count, $"coplanar neighbours merge back ({split.Count} -> {rejoined.Count})");
    Check(RegionGraph.MergeSame(m, merged, 0.05, 10).Count == merged.Count, "different planes are never merged");

    var g = RegionGraph.Build(m, merged);
    int ti = merged.ToList().FindIndex(r => r.Kind == PrimKind.Plane && r.Axis.Z > 0.9);
    int nbTop = g.NeighboursOf(ti).Count();
    Check(nbTop == 6, $"top face touches 6 regions (4 sides, fillet, hole): {nbTop}");
    int bi = merged.ToList().FindIndex(r => r.Kind == PrimKind.Plane && r.Axis.Z < -0.9);
    Check(g.EdgeBetween(ti, bi) == null, "top and bottom do not touch");

    int hole = merged.ToList().FindIndex(r => r.Kind == PrimKind.Cylinder && Near(r.Radius, 6, 0.05));
    var loops = g.BoundaryLoops(hole);
    Check(loops.Count == 2 && loops.All(l => l.Closed), $"hole wall has 2 closed boundary loops ({loops.Count})");
    var topLoops = g.BoundaryLoops(ti);
    Check(topLoops.Count == 2 && topLoops.All(l => l.Closed), "top face: outer loop + hole loop");

    var eHole = g.EdgeBetween(ti, hole)!;
    var ic = g.Intersect(eHole, 0, 0.02);
    Check(ic is { Kind: CurveKind.Circle, Exact: true } && Near(ic.Radius, 6, 0.05) && Near(ic.Origin.Z, 10, 0.01) && ic.Closed, $"top/hole intersection is a circle R{ic?.Radius:0.###} at z={ic?.Origin.Z:0.###}");
    Check(ic != null && ic.MaxDev < 0.05, $"circle lies on both surfaces (dev {ic?.MaxDev:0.0000})");
    var sideIdx = Enumerable.Range(0, merged.Count).Where(i => merged[i].Kind == PrimKind.Plane && Math.Abs(merged[i].Axis.Z) < 0.1 && g.EdgeBetween(ti, i) != null).ToList();
    var eSide = g.EdgeBetween(ti, sideIdx[0])!; var il = g.Intersect(eSide);
    Check(il is { Kind: CurveKind.Line, Exact: true } && il.Points.Count == 2 && il.MaxDev < 0.01, "top/side intersection is an exact line");
    int fil = merged.ToList().FindIndex(r => r.Kind == PrimKind.Cylinder && Near(r.Radius, 8, 0.5));
    var ef = g.Intersect(g.EdgeBetween(ti, fil)!);
    Check(ef is { Kind: CurveKind.Circle } && Near(ef.Radius, 8, 0.5) && !ef.Closed, "top/fillet intersection is an R8 arc");

    var rep = RegionGraph.Report(m, merged, 0.05);
    Check(rep.All(x => x.Rms < 0.15 && x.PercentInTolerance > 90), "per-region report within tolerance: " + string.Join(" ; ", rep.Select(x => $"{x.Name} rms {x.Rms:0.000} in-tol {x.PercentInTolerance:0}%")));
}

// ------------------------------------------------------------------ line / arc fitting
{
    List<(double x, double y)> Rounded(double w, double h, double r, double step, double noise, int seed)
    {
        var rnd = new Random(seed); var pts = new List<(double, double)>();
        void L(double x0, double y0, double x1, double y1) { int k = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / step); for (int i = 0; i < k; i++) pts.Add((x0 + (x1 - x0) * i / k, y0 + (y1 - y0) * i / k)); }
        void A(double cx, double cy, double a0) { int k = (int)Math.Ceiling(r * Math.PI / 2 / step); for (int i = 0; i < k; i++) { double a = a0 + Math.PI / 2 * i / k; pts.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a))); } }
        L(r, 0, w - r, 0); A(w - r, r, -Math.PI / 2); L(w, r, w, h - r); A(w - r, h - r, 0); L(w - r, h, r, h); A(r, h - r, Math.PI / 2); L(0, h - r, 0, r); A(r, r, Math.PI);
        return pts.Select(p => (p.Item1 + (rnd.NextDouble() - 0.5) * noise, p.Item2 + (rnd.NextDouble() - 0.5) * noise)).ToList();
    }
    var pts = Rounded(100, 60, 10, 0.5, 0, 1);
    var segs = LineArcFit.Fit(pts, true, 0.05);
    int arcs = segs.Count(s => s.IsArc), lines = segs.Count - arcs;
    Check(segs.Count == 8 && arcs == 4 && lines == 4, $"rounded rectangle -> 4 lines + 4 arcs (got {lines} + {arcs})");
    Check(segs.Where(s => s.IsArc).All(s => Near(s.Radius, 10, 0.1) && Near(Math.Abs(s.SweepDeg), 90, 1.5)), "arc radius 10 and sweep 90 deg: " + string.Join(", ", segs.Where(s => s.IsArc).Select(s => $"R{s.Radius:0.###}/{s.SweepDeg:0.#}")));
    Check(LineArcFit.MaxDeviation(pts, segs) < 0.06, $"max deviation {LineArcFit.MaxDeviation(pts, segs):0.0000} within tolerance");
    double gap = 0; for (int i = 0; i < segs.Count; i++) { var a = segs[i].P1; var b = segs[(i + 1) % segs.Count].P0; gap = Math.Max(gap, Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y))); }
    Check(gap < 1e-9, "elements join end to end (closed loop)");

    var noisy = Rounded(100, 60, 10, 0.5, 0.02, 5);
    var ns = LineArcFit.Fit(noisy, true, 0.05);
    Check(ns.Count is >= 8 and <= 12 && LineArcFit.MaxDeviation(noisy, ns) < 0.08, $"noisy section: {ns.Count} elements, dev {LineArcFit.MaxDeviation(noisy, ns):0.0000}");
    Check(ns.Where(s => s.IsArc).Count(s => Near(s.Radius, 10, 0.3)) >= 4, "noisy arcs keep radius 10 +-0.3");

    var circle = Enumerable.Range(0, 120).Select(i => (x: 5 + 7 * Math.Cos(2 * Math.PI * i / 120), y: -3 + 7 * Math.Sin(2 * Math.PI * i / 120))).ToList();
    var cs = LineArcFit.Fit(circle, true, 0.02);
    Check(cs.Count == 2 && cs.All(s => s.IsArc && Near(s.Radius, 7, 0.01) && Near(s.Center.x, 5, 0.01)), "full circle -> two semicircle arcs");

    var open = pts.Take(185).ToList();   // first side + corner arc, open polyline
    var os = LineArcFit.Fit(open, false, 0.05);
    Check(os.Count >= 2 && !os[0].IsArc, $"open polyline -> {os.Count} elements starting with a line");
}

Console.WriteLine($"\n{checks - failures}/{checks} checks passed");
return failures == 0 ? 0 : 1;
