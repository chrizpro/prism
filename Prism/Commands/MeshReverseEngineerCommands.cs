using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

// =====================================================================================================================
// Reverse-engineering flow built on PrimitiveFit / PrimitiveFit2 / RegionGraph / LineArcFit (all pure C#, unit tested).
// The CAD-building half of "Rebuild from Scan" calls OpenCascade through Occ.* and therefore needs the native kernel.
// =====================================================================================================================

/// <summary>Segment -> fit -> merge -> faces from the fitted surfaces -> boundary / intersection curves -> sew -> deviation to the scan.</summary>
public sealed class MeshRebuildCommand : MeshCommand
{
    readonly NumberParam _tol, _angle, _sew, _devTol;
    readonly IntParam _minTris;
    readonly BoolParam _extended, _merge;
    readonly ChoiceParam _create;

    public MeshRebuildCommand() : base("Mesh")
    {
        _tol = Num("Surface tolerance", 0.3, 0.001, 50, 0.1);
        _angle = Num("Normal tolerance", 12, 1, 45, 1, "°");
        _minTris = Add(new IntParam { Label = "Smallest region (triangles)", Value = 40, Min = 3, Max = 100000 });
        _extended = Check("Also find tori, extrusions, revolved and free-form surfaces", true);
        _merge = Check("Merge neighbouring regions of the same shape", true);
        _create = Choice("Create", 0, "Nothing - analysis and deviation estimate", "Fitted faces", "Fitted faces + boundary curves", "Faces + curves, then sew into a shell / solid");
        _sew = Num("Sewing tolerance", 0.2, 0.0001, 10, 0.05);
        _devTol = Num("Deviation tolerance", 0.1, 0.0005, 10, 0.05);
        PreviewButton();
    }

    public override string Title => "Rebuild from Scan";
    public override string Hint => "Automatic reverse engineering: finds the planes, cylinders, cones, spheres, tori, extrusions, revolved and free-form surfaces in a scan, merges look-alike neighbours, builds CAD faces from them, adds the region borders as curves, tries to sew a solid, and reports how far the result is from the scan.";
    public override string Icon => "mesh-fit";
    public override string OkText => "Rebuild";
    protected override void UpdateVisibility() { _sew.IsVisible = _create.Index == 3; }

    sealed record Analysis(List<FitRegion> Regions, RegionGraph Graph, List<RegionGraph.RegionReport> Report);

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var an = Cached(Key(obj.Id, m.TriCount, _tol.Value, _angle.Value, _minTris.Value, _extended.Value, _merge.Value, _devTol.Value), () =>
        {
            var found = PrimitiveFit.Detect(m, new PrimitiveFit.Options(_tol.Value, _angle.Value, _minTris.Value, Extended: _extended.Value));
            if (_merge.Value) found = RegionGraph.MergeSame(m, found, _tol.Value, _angle.Value);
            return new Analysis(found, RegionGraph.Build(m, found), RegionGraph.Report(m, found, _devTol.Value));
        });
        if (an == null) return null;
        var regs = an.Regions; var label = new int[m.TriCount];
        for (int i = 0; i < regs.Count; i++) foreach (int t in regs[i].Tris) label[t] = i + 1;
        var (disp, sc) = m.ToLabelledMeshData(label); disp.Scalars = sc;
        S.SetField(obj, m, disp, 2, 0, regs.Count + 1, 0);

        double covered = regs.Sum(r => r.Area) / Math.Max(m.Area(), 1e-9) * 100;
        Row("Regions", regs.Count.ToString(), regs.Count > 0 ? 1 : 2);
        Row("Surface explained", $"{covered:0.#}%", covered > 85 ? 1 : 2);
        Row("Region neighbours", Count(an.Graph.Edges.Count));
        for (int i = 0; i < Math.Min(regs.Count, 25); i++)
        {
            var rp = an.Report[i];
            Row($"{i + 1}.  {regs[i].Describe()}", $"{rp.Area:#,0} mm²   rms {rp.Rms:0.###}   max {rp.Max:0.###}   {rp.PercentInTolerance:0}% in tol");
        }
        if (regs.Any(r => r.Kind == PrimKind.Freeform))
            Row("Free-form patches", string.Join(", ", regs.Where(r => r.Kind == PrimKind.Freeform).Select(r => $"rms {r.Patch!.Rms:0.###} / max {r.Patch.MaxError:0.###}")), 2);
        if (_create.Index == 0) return NoOp();

        var o = new CommandOutput { Label = "Rebuild from scan", Icon = Icon };
        var faces = new List<Shape>(); int failed = 0;
        for (int i = 0; i < regs.Count; i++)
        {
            try { faces.AddRange(BuildFaces(regs[i], m, an.Graph, i, _tol.Value)); }
            catch (KernelException e) { failed++; Row("Could not build", $"{regs[i].Describe()}: {e.Message}", 2); }
        }
        Row("Faces built", $"{faces.Count} from {regs.Count - failed} of {regs.Count} regions", failed == 0 ? 1 : 2);

        Shape? result = null;
        if (_create.Index == 3 && faces.Count > 1)
        {
            try
            {
                result = Occ.Sew(faces, _sew.Value, true);
                Row("Sewing", result.HasSolids ? "closed solid" : "open shell - some faces do not meet within the sewing tolerance (faces are bounded by their fitted extent, not by the true region borders)", result.HasSolids ? 1 : 2);
                o.Add.Add(new(result, obj.Name + " rebuilt", result.HasSolids ? ObjKind.Solid : ObjKind.Surface, null, null, null, obj.Look));
            }
            catch (KernelException e) { Row("Sewing failed", e.Message, 2); result = null; }
        }
        if (result == null) for (int i = 0; i < faces.Count; i++) o.Add.Add(new(faces[i], $"Scan face {i + 1}", ObjKind.Surface));

        if (_create.Index >= 2) AddCurves(o, an, m);
        ReportDeviation(m, an, faces, result);
        return o;
    }

    void AddCurves(CommandOutput o, Analysis an, TriMesh m)
    {
        int made = 0, exact = 0;
        foreach (var e in an.Graph.Edges)
        {
            if (e.Borders.Count == 0) continue;
            try
            {
                var ic = an.Graph.Intersect(e, 0, _tol.Value * 0.2);
                if (ic == null || ic.Points.Count < 2) continue;
                o.Add.Add(new(CurveShape(ic), $"Edge {e.A + 1}-{e.B + 1}", ObjKind.Curve));
                made++; if (ic.Exact) exact++;
            }
            catch (KernelException) { }
            if (made >= 150) break;
        }
        Row("Edge curves", $"{made} ({exact} exact line / circle, rest snapped to both surfaces)", made > 0 ? 1 : 2);
    }

    static Shape CurveShape(IntersectionCurve ic)
    {
        var p = ic.Points;
        if (ic.Kind == CurveKind.Line) return Occ.Line(p[0], p[^1]);
        if (ic.Kind == CurveKind.Circle && p.Count >= 3 && !ic.Closed) return Occ.Arc3(p[0], p[p.Count / 2], p[^1]);
        return Occ.Polyline(p, ic.Closed);
    }

    /// <summary>Compares the scan with the rebuilt faces (tessellated) and with the analytic fit of each region.</summary>
    void ReportDeviation(TriMesh m, Analysis an, List<Shape> faces, Shape? sewn)
    {
        // exact-surface statistics need no kernel
        var tol = _devTol.Value; double se = 0, mx = 0; long n = 0, within = 0;
        foreach (var r in an.Report) { se += r.Rms * r.Rms * r.Triangles; n += r.Triangles; mx = Math.Max(mx, r.Max); within += (long)(r.PercentInTolerance / 100 * r.Triangles); }
        if (n > 0) Row("Fit error of the regions", $"rms {Math.Sqrt(se / n):0.###}, max {mx:0.###}, {100.0 * within / n:0.#}% within {tol:0.###}");
        if (faces.Count == 0) return;
        try
        {
            var parts = new List<TriMesh>();
            double diag = m.Size.Length;
            foreach (var f in (sewn != null ? new[] { sewn } : faces.ToArray())) parts.Add(MeshAnalysis.FromMeshData(f.Tessellate(Math.Max(diag * 0.0005, 1e-3))));
            var rebuilt = TriMesh.Merge(parts);
            if (rebuilt.TriCount == 0) return;
            var dev = MeshAnalysis.Deviation(m, rebuilt, tol);
            Row("Scan vs rebuilt: max", $"{dev.Max:+0.###;-0.###} / {dev.Min:+0.###;-0.###} mm");
            Row("Scan vs rebuilt: mean", $"{dev.Mean:+0.###;-0.###} mm   rms {dev.Rms:0.###}");
            Row("Scan vs rebuilt: in tolerance", $"{dev.WithinTolerance:0.#}% within {tol:0.###} mm", dev.WithinTolerance > 90 ? 1 : dev.WithinTolerance > 60 ? 2 : 3);
            Row("Note", "uncovered scan areas (free-form leftovers, unbuilt regions) count as deviation");
        }
        catch (KernelException e) { Row("Deviation", "could not tessellate the result: " + e.Message, 2); }
    }

    // ------------------------------------------------------------------------------------------ face building
    /// <summary>
    /// A CAD face (or two) for a fitted region. Planes are trimmed to the region's real border loop; every other kind is the full fitted
    /// surface limited to the region's axial / angular extent (NOT trimmed to its exact border - that needs face splitting by projected curves).
    /// </summary>
    internal static List<Shape> BuildFaces(FitRegion r, TriMesh m, RegionGraph g, int index, double tol)
    {
        var verts = r.Tris.SelectMany(t => new[] { m.P(m.T[3 * t]), m.P(m.T[3 * t + 1]), m.P(m.T[3 * t + 2]) }).Distinct().ToList();
        var res = new List<Shape>();
        switch (r.Kind)
        {
            case PrimKind.Plane:
            {
                var loops = g.BoundaryLoops(index).Where(l => l.Points.Count >= 3).ToList();
                if (loops.Count == 0) throw new KernelException("the plane has no border loop");
                IEnumerable<Shape> Wires(IEnumerable<BoundaryLoop> ls) => ls.Select(l =>
                {
                    var pts = l.Points.Select(p => p - r.Axis * Vec3.Dot(p - r.Origin, r.Axis)).ToList();
                    return Occ.Polyline(Simplify(pts, tol * 0.25), true);
                });
                var ordered = loops.OrderByDescending(l => Extent(l.Points)).ToList();
                try { res.Add(Occ.Profile(Wires(ordered).ToList(), true)); }                       // outer loop + holes
                catch (KernelException) { res.Add(Occ.Profile(Wires(ordered.Take(1)).ToList(), true)); }    // outer loop only
                return res;
            }
            case PrimKind.Cylinder:
            case PrimKind.Cone:
            {
                var (u, w) = SurfaceMath.Basis(r.Axis);
                var (a0, span) = AzimuthRange(verts, r, u, w);
                double t0 = r.Kind == PrimKind.Cone ? Math.Max(r.T0, 0) : r.T0, t1 = Math.Max(r.T1, t0 + 1e-3), tan = Math.Tan(r.HalfAngle * Math.PI / 180);
                var dir = u * Math.Cos(a0) + w * Math.Sin(a0);
                Vec3 At(double h) => r.Origin + r.Axis * h + dir * (r.Kind == PrimKind.Cone ? h * tan : r.Radius);
                res.Add(Occ.Revolve(Occ.Line(At(t0), At(t1)), r.Origin, r.Axis, span * 180 / Math.PI));
                return res;
            }
            case PrimKind.Sphere:
            {
                var (u, w) = SurfaceMath.Basis(Vec3.UnitZ);
                var (a0, span) = AzimuthRange(verts, new FitRegion { Origin = r.Origin, Axis = Vec3.UnitZ }, u, w);
                double p0 = verts.Min(v => Math.Acos(Math.Clamp(Vec3.Dot((v - r.Origin).Normalized(), Vec3.UnitZ), -1, 1))), p1 = verts.Max(v => Math.Acos(Math.Clamp(Vec3.Dot((v - r.Origin).Normalized(), Vec3.UnitZ), -1, 1)));
                if (p1 - p0 < 1e-3) throw new KernelException("degenerate sphere patch");
                var dir = u * Math.Cos(a0) + w * Math.Sin(a0);
                Vec3 At(double phi) => r.Origin + Vec3.UnitZ * (r.Radius * Math.Cos(phi)) + dir * (r.Radius * Math.Sin(phi));
                res.Add(Occ.Revolve(Occ.Arc3(At(p0), At((p0 + p1) / 2), At(p1)), r.Origin, Vec3.UnitZ, span * 180 / Math.PI));
                return res;
            }
            case PrimKind.Torus:
            {
                var (u, w) = SurfaceMath.Basis(r.Axis);
                var (a0, span) = AzimuthRange(verts, r, u, w);
                var dir = u * Math.Cos(a0) + w * Math.Sin(a0);
                // tube angle psi measured from the outward radial direction
                var psi = verts.Select(v => { var d = v - r.Origin; double h = Vec3.Dot(d, r.Axis); double rho = (d - r.Axis * h).Length; return Math.Atan2(h, rho - r.Radius); }).ToList();
                var (s0, sspan) = CircularRange(psi);
                Vec3 At(double a) => r.Origin + r.Axis * (r.MinorRadius * Math.Sin(a)) + dir * (r.Radius + r.MinorRadius * Math.Cos(a));
                if (sspan > 2 * Math.PI - 0.05) { res.Add(Occ.Revolve(Occ.Arc3(At(0), At(Math.PI / 2), At(Math.PI)), r.Origin, r.Axis, span * 180 / Math.PI)); res.Add(Occ.Revolve(Occ.Arc3(At(Math.PI), At(1.5 * Math.PI), At(2 * Math.PI)), r.Origin, r.Axis, span * 180 / Math.PI)); }
                else res.Add(Occ.Revolve(Occ.Arc3(At(s0), At(s0 + sspan / 2), At(s0 + sspan)), r.Origin, r.Axis, span * 180 / Math.PI));
                return res;
            }
            case PrimKind.Revolution:
            {
                var (u, w) = SurfaceMath.Basis(r.Axis);
                var (a0, span) = AzimuthRange(verts, r, u, w);
                var dir = u * Math.Cos(a0) + w * Math.Sin(a0);
                var prof = SurfaceMath.ProfileHR(r).Select(q => r.Origin + r.Axis * q.x + dir * q.y).ToList();
                res.Add(Occ.Revolve(Occ.Polyline(prof, r.ProfileClosed), r.Origin, r.Axis, span * 180 / Math.PI));
                return res;
            }
            case PrimKind.Extrusion:
            {
                var wire = Occ.Polyline(r.Profile!.Select(p => p + r.Axis * r.T0).ToList(), r.ProfileClosed);
                res.Add(Occ.Extrude(wire, r.Axis * (r.T1 - r.T0)));
                return res;
            }
            default:
            {
                var p = r.Patch ?? throw new KernelException("no patch");
                res.Add(Occ.NurbsSurface(p.Poles, p.NU, p.NV, p.DegU, p.DegV));
                return res;
            }
        }
    }

    static double Extent(List<Vec3> p) { var mn = new Vec3(p.Min(a => a.X), p.Min(a => a.Y), p.Min(a => a.Z)); var mx = new Vec3(p.Max(a => a.X), p.Max(a => a.Y), p.Max(a => a.Z)); return (mx - mn).Length; }

    static List<Vec3> Simplify(List<Vec3> p, double tol)
    {
        if (p.Count < 4) return p;
        var keep = new bool[p.Count]; keep[0] = true; keep[p.Count - 1] = true;
        var st = new Stack<(int, int)>(); st.Push((0, p.Count - 1));
        while (st.Count > 0)
        {
            var (a, b) = st.Pop(); var ab = p[b] - p[a]; double l2 = Vec3.Dot(ab, ab), best = 0; int idx = -1;
            for (int i = a + 1; i < b; i++) { double t = l2 < 1e-18 ? 0 : Math.Clamp(Vec3.Dot(p[i] - p[a], ab) / l2, 0, 1); double d = (p[i] - (p[a] + ab * t)).Length; if (d > best) { best = d; idx = i; } }
            if (idx >= 0 && best > tol) { keep[idx] = true; st.Push((a, idx)); st.Push((idx, b)); }
        }
        var res = p.Where((_, i) => keep[i]).ToList();
        if (res.Count > 1 && res[0].DistanceTo(res[^1]) < 1e-9) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>Start angle and span (radians) of the arc of angles occupied by the values: the complement of the largest gap.</summary>
    static (double start, double span) CircularRange(List<double> angles)
    {
        var a = angles.Select(x => { x %= 2 * Math.PI; return x < 0 ? x + 2 * Math.PI : x; }).OrderBy(x => x).ToList();
        if (a.Count < 2) return (a.FirstOrDefault(), 0);
        double gap = a[0] + 2 * Math.PI - a[^1], start = a[0];
        for (int i = 0; i + 1 < a.Count; i++) if (a[i + 1] - a[i] > gap) { gap = a[i + 1] - a[i]; start = a[i + 1]; }
        double span = 2 * Math.PI - gap;
        return span > 2 * Math.PI - 0.06 ? (0, 2 * Math.PI) : (start, span);
    }

    static (double start, double span) AzimuthRange(List<Vec3> verts, FitRegion r, Vec3 u, Vec3 w)
        => CircularRange(verts.Select(v => { var d = v - r.Origin; return Math.Atan2(Vec3.Dot(d, w), Vec3.Dot(d, u)); }).ToList());
}

/// <summary>Fits lines and arcs to mesh section outlines and creates them as curves.</summary>
public sealed class MeshSectionFitCommand : MeshCommand
{
    readonly ChoiceParam _plane;
    readonly NumberParam _offset, _spacing, _tol;
    readonly IntParam _count;
    readonly BoolParam _create;
    readonly List<List<Vec3>> _overlay = [];

    public MeshSectionFitCommand() : base("Mesh")
    {
        _plane = Choice("Section plane", 0, "XY (horizontal)", "XZ", "YZ");
        _offset = Num("First position", 0);
        _count = Add(new IntParam { Label = "Number of sections", Value = 1, Min = 1, Max = 50 });
        _spacing = Num("Spacing", 5, 0.01);
        _tol = Num("Fit tolerance", 0.05, 0.0005, 5, 0.01);
        _create = Check("Create lines and arcs", true);
    }

    public override string Title => "Section Lines & Arcs";
    public override string Hint => "Slices the scan and fits straight lines and circular arcs to each outline within a tolerance - clean sketch geometry you can extrude, instead of thousands of tiny segments.";
    public override string Icon => "mesh-section";
    public override string OkText => "Create sketch curves";
    protected override void UpdateVisibility() { _spacing.IsVisible = _count.Value > 1; }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _overlay.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var (n, u, v) = _plane.Index switch { 0 => (Vec3.UnitZ, Vec3.UnitX, Vec3.UnitY), 1 => (Vec3.UnitY, Vec3.UnitX, Vec3.UnitZ), _ => (Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ) };
        var o = new CommandOutput { Label = "Section lines and arcs", Icon = Icon };
        int loops = 0, lines = 0, arcs = 0, rawPts = 0; double worst = 0;
        for (int k = 0; k < _count.Value; k++)
        {
            var origin = n * (_offset.Value + k * _spacing.Value);
            double h = Vec3.Dot(origin, n);
            foreach (var l in MeshOps.Section(m, origin, n))
            {
                if (l.Points.Count < 3) continue;
                var p2 = l.Points.Select(p => (x: Vec3.Dot(p, u), y: Vec3.Dot(p, v))).ToList();
                var segs = LineArcFit.Fit(p2, l.Closed, _tol.Value);
                if (segs.Count == 0) continue;
                loops++; rawPts += l.Points.Count; worst = Math.Max(worst, LineArcFit.MaxDeviation(p2, segs));
                Vec3 P3((double x, double y) q) => u * q.x + v * q.y + n * h;
                _overlay.Add(segs.SelectMany(s => s.IsArc ? new[] { P3(s.P0), P3(s.Mid), P3(s.P1) } : new[] { P3(s.P0), P3(s.P1) }).ToList());
                lines += segs.Count(s => !s.IsArc); arcs += segs.Count(s => s.IsArc);
                if (!_create.Value) continue;
                var edges = new List<Shape>();
                try
                {
                    foreach (var s in segs) edges.Add(s.IsArc ? Occ.Arc3(P3(s.P0), P3(s.Mid), P3(s.P1)) : Occ.Line(P3(s.P0), P3(s.P1)));
                    try { o.Add.Add(new(Occ.Profile(edges, false), "Section outline", ObjKind.Curve)); }
                    catch (KernelException) { foreach (var e in edges) o.Add.Add(new(e, "Section element", ObjKind.Curve)); }
                }
                catch (KernelException) { }
            }
        }
        Row("Outlines", loops.ToString(), loops > 0 ? 1 : 2);
        if (loops == 0) { Row("Nothing here", "the plane does not cut the mesh", 2); return null; }
        Row("Elements", $"{lines} lines + {arcs} arcs (from {Count(rawPts)} points)");
        Row("Largest deviation", N(worst, "mm"), worst <= _tol.Value * 1.5 ? 1 : 2);
        return _create.Value ? o : NoOp();
    }

    public override void BuildOverlay(CadSession s, Overlay o) { foreach (var l in _overlay) o.AddPolyline(l, 0xFF35B6FF, 2.4f, false); }
}
