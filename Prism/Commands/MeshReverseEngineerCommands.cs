using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

// =====================================================================================================================
// Reverse-engineering flow built on PrimitiveFit / PrimitiveFit2 / RegionGraph / LineArcFit (all pure C#, unit tested).
// The CAD-building half of "Rebuild from Scan" calls OpenCascade through Occ.* and therefore needs the native kernel.
// =====================================================================================================================

/// <summary>Segment -> fit -> merge -> faces from the fitted surfaces -> boundary / intersection curves -> sew -> deviation to the scan.</summary>
[Legacy("Use Convert Mesh.")]
public sealed class MeshRebuildCommand : MeshCommand
{
    readonly NumberParam _tol, _angle, _sew, _devTol;
    readonly IntParam _minTris;
    readonly BoolParam _extended, _merge;
    readonly ChoiceParam _create;

    public MeshRebuildCommand() : base("Mesh")
    {
        _tol = Num("Surface tolerance", 0.3, 0.001, 50, 0.1);
        _angle = Num("Normal tolerance", 12, 1, 45, 1, "Â°");
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

    protected override CommandOutput? Build() => Build(Meshes().FirstOrDefault(), showRegions: true);

    internal CommandOutput? BuildConversion(SceneObject obj, bool solid, double tolerance, double sewTolerance, int minTriangles, bool extended)
    {
        _create.Index = solid ? 3 : 1;
        _tol.Value = _devTol.Value = tolerance;
        _sew.Value = sewTolerance;
        _minTris.Value = minTriangles;
        _extended.Value = extended;
        Committing = true;
        return Build(obj, showRegions: false);
    }

    CommandOutput? Build(SceneObject? obj, bool showRegions)
    {
        Results.Clear();
        Row("Output", _create.Index == 0 ? "Analysis only; no CAD geometry is created" : "Fitted CAD faces or a direct-edit shell/solid; original sketches and feature history are not recovered");
        if (obj == null) return null;
        var m = obj.Tri!;
        var an = Cached(Key(obj.Id, m.TriCount, _tol.Value, _angle.Value, _minTris.Value, _extended.Value, _merge.Value, _devTol.Value), () =>
        {
            var found = PrimitiveFit.Detect(m, new PrimitiveFit.Options(_tol.Value, _angle.Value, _minTris.Value, Extended: _extended.Value));
            if (_merge.Value) found = RegionGraph.MergeSame(m, found, _tol.Value, _angle.Value);
            return new Analysis(found, RegionGraph.Build(m, found), RegionGraph.Report(m, found, _devTol.Value));
        });
        if (an == null) return null;
        var regs = an.Regions;
        if (showRegions)
        {
            var label = new int[m.TriCount];
            for (int i = 0; i < regs.Count; i++) foreach (int t in regs[i].Tris) label[t] = i + 1;
            var (disp, sc) = m.ToLabelledMeshData(label); disp.Scalars = sc;
            S.SetField(obj, m, disp, 2, 0, regs.Count + 1, 0);
        }

        double covered = regs.Sum(r => r.Area) / Math.Max(m.Area(), 1e-9) * 100;
        Row("Regions", regs.Count.ToString(), regs.Count > 0 ? 1 : 2);
        Row("Surface explained", $"{covered:0.#}%", covered > 85 ? 1 : 2);
        Row("Region neighbours", Count(an.Graph.Edges.Count));
        for (int i = 0; i < Math.Min(regs.Count, 25); i++)
        {
            var rp = an.Report[i];
            Row($"{i + 1}.  {regs[i].Describe()}", $"{rp.Area:#,0} mmÂ²   rms {rp.Rms:0.###}   max {rp.Max:0.###}   {rp.PercentInTolerance:0}% in tol");
        }
        if (regs.Any(r => r.Kind == PrimKind.Freeform))
            Row("Free-form patches", string.Join(", ", regs.Where(r => r.Kind == PrimKind.Freeform).Select(r => $"rms {r.Patch!.Rms:0.###} / max {r.Patch.MaxError:0.###}")), 2);
        if (_create.Index == 0) return Report();

        var o = new CommandOutput { Label = "Rebuild from scan", Icon = Icon };
        var faces = new List<Shape>(); int failed = 0;
        for (int i = 0; i < regs.Count; i++)
        {
            try { faces.AddRange(BuildFaces(regs[i], m, an.Graph, i, _tol.Value)); }
            catch (KernelException e) { failed++; Row("Could not build", $"{regs[i].Describe()}: {e.Message}", 2); }
        }
        Row("Faces built", $"{faces.Count} from {regs.Count - failed} of {regs.Count} regions", failed == 0 ? 1 : 2);

        Shape? result = null;
        if (_create.Index == 3 && SolidFromRegions.Build(regs, m) is { } exact)
        {
            result = exact;
            Row("Solid", "exact solid: every edge is a true intersection, so it can be edited directly", 1);
            o.Add.Add(new(result, obj.Name + " rebuilt", ObjKind.Solid, null, null, null, obj.Look));
        }
        else if (_create.Index == 3 && faces.Count > 1)
        {
            try
            {
                // fitted faces stop about half a triangle short of each other, so the gap to close depends on the mesh, not only on the setting
                double edge = MeshRemesh.MeanEdge(m), used = _sew.Value;
                result = Occ.Sew(faces, used, true);
                foreach (double wider in new[] { edge * 0.5, edge, edge * 1.5, edge * 2.5 })
                {
                    if (result.HasSolids) break;
                    if (wider <= used) continue;
                    used = wider; result = Occ.Sew(faces, used, true);
                }
                if (used > _sew.Value) Row("Sewing tolerance", $"widened to {used:0.###} mm to match the mesh's triangle size", result.HasSolids ? 1 : 2);
                Row("Sewing", result.HasSolids ? "closed solid" : "open shell - some faces do not meet within the sewing tolerance (faces are bounded by their fitted extent, not by the true region borders)", result.HasSolids ? 1 : 2);
                o.Add.Add(new(result, obj.Name + " rebuilt", result.HasSolids ? ObjKind.Solid : ObjKind.Surface, null, null, null, obj.Look));
            }
            catch (KernelException e) { Row("Sewing failed", e.Message, 2); result = null; }
        }
        if (result == null) for (int i = 0; i < faces.Count; i++) o.Add.Add(new(faces[i], $"Scan face {i + 1}", ObjKind.Surface));

        if (showRegions)
        {
            if (_create.Index >= 2) AddCurves(o, an, m);
            ReportDeviation(m, an, faces, result);
        }
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

    internal static (double start, double span) AzimuthRange(List<Vec3> verts, FitRegion r, Vec3 u, Vec3 w)
        => CircularRange(verts.Select(v => { var d = v - r.Origin; return Math.Atan2(Vec3.Dot(d, w), Vec3.Dot(d, u)); }).ToList());
}

/// <summary>
/// Puts a plane on the scan and makes a real sketch, ready to extrude or revolve, of what it traces there: the section, the border
/// of a selected region, the silhouette, or the sharp creases (MSH-P06). Outlines are fitted with lines, arcs, circles and splines.
/// </summary>
public class MeshSectionSketchCommand : MeshCommand
{
    readonly SelectionSlot _ref;
    readonly ChoiceParam _plane, _trace;
    readonly NumberParam _crease;
    const int TraceSection = 0, TraceRegion = 1, TraceSilhouette = 2, TraceCreases = 3, FittedToRegion = 4;
    int _lastTrace;
    readonly NumberParam _offset, _spacing, _tol;
    readonly BoolParam _autoTol;
    readonly IntParam _count;
    const int Movable = 5;
    readonly bool _interactive;
    readonly NumberParam _x, _y, _z, _rx, _ry, _rz;
    readonly List<List<Vec3>> _overlay = [], _raw = [];
    Vec3[]? _planeOutline;
    int _applied;
    bool _applying;

    public MeshSectionSketchCommand() : this(false) { }

    protected MeshSectionSketchCommand(bool interactive) : base("Mesh")
    {
        _interactive = interactive;
        _plane = Choice("Plane", interactive ? Movable : 0, "XY (horizontal)", "XZ", "YZ", "A construction plane or flat face", "Fitted to the selected region", "Movable plane");
        _ref = Slot(new SelectionSlot { Label = "Plane or flat face", Filter = SelFilter.Plane | SelFilter.Face, Optional = true });
        _trace = Choice("Trace", TraceSection, "Section (slice the mesh)", "Border of the selected region", "Silhouette", "Sharp creases");
        _crease = Num("Crease angle", 40, 5, 170, unit: "°");
        _offset = Num("Offset from the plane", 0);
        _count = Add(new IntParam { Label = "Number of sketches", Value = 1, Min = 1, Max = 50 });
        _spacing = Num("Spacing", 5, 0.01);
        _autoTol = Check("Estimate the fit tolerance from the scan's noise", true);
        _tol = Num("Fit tolerance", 0.05, 0.0005, 5, 0.01);
        _x = Num("Move X", 0); _y = Num("Move Y", 0); _z = Num("Move Z", 0);
        _rx = Num("Rotate X", 0, unit: "°"); _ry = Num("Rotate Y", 0, unit: "°"); _rz = Num("Rotate Z", 0, unit: "°");
        if (interactive) RepeatApply = Add(new ActionParam { Label = "Apply section", Run = ApplySection });
        UpdateVisibility();
    }

    public override string Title => "Section Sketch";
    public override string Hint => _interactive
        ? "Drag the plane arrows or rings to move or rotate it. Apply each section, then choose Done."
        : "Fit an editable sketch of the measured outline on a world plane, construction plane, flat face, or selected mesh region.";
    public override string Icon => "mesh-section";
    public override ActionParam? RepeatApply { get; }
    public override bool SupportsPreview => !_interactive;
    public override bool RefinesAfterDrag => _interactive;
    public override string OkText => _interactive ? "Done" : "Create sketch";
    public override GizmoSpec? Gizmo => _plane.Index == Movable && Meshes().FirstOrDefault()?.Tri is { } mesh
        ? GizmoSpec.At(MovableOrigin(mesh), _x, _y, _z, _rx, _ry, _rz) with { CombinedByDefault = true }
        : null;

    protected override void UpdateVisibility()
    {
        if (_rz == null) return;
        bool movable = _plane.Index == Movable;
        _x.IsVisible = _y.IsVisible = _z.IsVisible = _rx.IsVisible = _ry.IsVisible = _rz.IsVisible = movable;
        _offset.IsVisible = !movable;
        _ref.IsVisible = _plane.Index == 3;
        // a region's border lies on the region, so start on the plane fitted to it (the face a part sits on, for example)
        if (_trace.Index != _lastTrace && _trace.Index == TraceRegion && _plane.Index != 3) _plane.Index = FittedToRegion;
        _lastTrace = _trace.Index;
        _count.IsVisible = _trace.Index == TraceSection;
        _spacing.IsVisible = _count.Value > 1 && _trace.Index == TraceSection;
        _crease.IsVisible = _trace.Index == TraceCreases;
        _tol.IsVisible = !_autoTol.Value;
    }

    Vec3 MovableOrigin(TriMesh mesh) => mesh.Center + new Vec3(_x.Value, _y.Value, _z.Value);

    void ApplySection()
    {
        if (S.ActiveCommand != this) return;
        _applying = true;
        CommandOutput? output;
        try { output = TryBuild(); } finally { _applying = false; }
        if (output == null) { S.Say(Error ?? "Position the plane where it cuts the mesh before applying.", true); return; }
        S.Apply(output, selectCreated: false);
        _applied += output.Add.Count;
        OnChanged();
        S.Say("Section applied. Move the plane to take another section, or choose Done.");
    }

    /// <summary>The plane to slice on, or null while it still needs a pick.</summary>
    Plane3? Frame(TriMesh m)
    {
        static Plane3 On(Vec3 origin, Vec3 n) { var (u, v) = SurfaceMath.Basis(n.Normalized()); return new Plane3(origin, u, v); }
        switch (_plane.Index)
        {
            case 0: return Plane3.XY;
            case 1: return Plane3.XZ;
            case 2: return Plane3.YZ;
            case Movable:
            {
                Vec3 Rotate(Vec3 axis) => DirectHelpers.RotateAbout(
                    DirectHelpers.RotateAbout(DirectHelpers.RotateAbout(axis, Vec3.UnitX, _rx.Value),
                        Vec3.UnitY, _ry.Value), Vec3.UnitZ, _rz.Value);
                return new Plane3(MovableOrigin(m), Rotate(Vec3.UnitX), Rotate(Vec3.UnitY));
            }
            case 3:
            {
                if (_ref.Items.Count == 0) return null;
                var it = _ref.Items[0];
                if (S.Find(it.Obj) is { Kind: ObjKind.Plane, SketchPlane: { } p }) return p;
                return S.FaceFrame(it) is { } f ? On(f.point, f.normal) : null;
            }
            default:
            {
                var sel = S.RegionSel;
                if (sel == null || sel.IsEmpty || !ReferenceEquals(sel.Mesh, m)) return null;
                Vec3 sum = Vec3.Zero, centre = Vec3.Zero; double area = 0;
                foreach (int t in sel.Tris)
                {
                    var (a, b, c) = m.Tri(t); var pa = m.P(a); var pb = m.P(b); var pc = m.P(c);
                    var w = Vec3.Cross(pb - pa, pc - pa); double ta = w.Length / 2;
                    sum += w / 2; centre += (pa + pb + pc) / 3 * ta; area += ta;
                }
                return sum.Length < 1e-12 ? null : On(centre / area, sum);
            }
        }
    }

    /// <summary>Fitted elements as sketch entities, sharing their end points so the outline stays closed when it is edited.</summary>
    internal static void AddToSketch(SketchModel model, List<SketchSeg> segs, bool closed)
    {
        if (segs.Count == 0) return;
        if (segs is [{ IsSpline: true, Closed: true } loop])
        {
            model.AddSpline(new[] { loop.P0 }.Concat(loop.Through!).Select(q => model.AddPoint(q.x, q.y)).ToList(), closed: true, control: false);
            return;
        }
        if (closed && segs.Count == 2 && segs.All(s => s.IsArc) && Math.Abs(segs[0].Radius - segs[1].Radius) < 0.02 * segs[0].Radius
            && Math.Abs(segs[0].Center.x - segs[1].Center.x) + Math.Abs(segs[0].Center.y - segs[1].Center.y) < 0.02 * segs[0].Radius)
        {
            model.AddCircle(model.AddPoint((segs[0].Center.x + segs[1].Center.x) / 2, (segs[0].Center.y + segs[1].Center.y) / 2), (segs[0].Radius + segs[1].Radius) / 2);
            return;
        }
        int first = model.AddPoint(segs[0].P0.x, segs[0].P0.y), last = first;
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            int end = closed && i == segs.Count - 1 ? first : model.AddPoint(s.P1.x, s.P1.y);
            if (s.IsSpline)
            {
                var ids = new List<int> { last };
                ids.AddRange(s.Through!.Select(q => model.AddPoint(q.x, q.y)));
                ids.Add(end);
                model.AddSpline(ids, closed: false, control: false);
            }
            else if (s.IsArc)
            {
                int c = model.AddPoint(s.Center.x, s.Center.y);
                if (s.SweepDeg >= 0) model.AddArc(c, last, end); else model.AddArc(c, end, last);       // arcs run counter-clockwise
            }
            else model.AddLine(last, end);
            last = end;
        }
    }

    /// <summary>
    /// Tangent constraints where fitted lines and arcs continue smoothly, each solved in turn. A tangency is kept only when
    /// solving it moves no point further than the reach: the sketch must still follow the measurement.
    /// </summary>
    internal static int AddTangency(SketchModel model, double reach)
    {
        int kept = 0;
        foreach (var e in model.Entities.Where(e => e.Type is EntType.Line or EntType.Arc).ToList())
            foreach (int point in model.EndpointsOf(e).ToList())
            {
                var before = model.Points.ToDictionary(p => p.Id, p => (p.X, p.Y));
                int count = model.Constraints.Count;
                if (FitMeshSectionTool.MakeTangent(model, e.Id, point) == 0) continue;
                var solved = SketchSolver.Solve(model);
                double moved = model.Points.Max(p => before.TryGetValue(p.Id, out var b) ? Math.Sqrt((p.X - b.X) * (p.X - b.X) + (p.Y - b.Y) * (p.Y - b.Y)) : 0);
                if (solved.Converged && moved <= reach) { kept += model.Constraints.Count - count; continue; }
                model.Constraints.RemoveRange(count, model.Constraints.Count - count);
                foreach (var p in model.Points) if (before.TryGetValue(p.Id, out var b)) { p.X = b.X; p.Y = b.Y; }
            }
        return kept;
    }

    protected override CommandOutput? Build() => _interactive && Committing ? Report() : BuildSections(create: !_interactive || _applying);

    CommandOutput? BuildSections(bool create)
    {
        Results.Clear(); _overlay.Clear(); _raw.Clear(); _planeOutline = null;
        if (_applied > 0) Row("Applied sketches", _applied.ToString(), 1);
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        if (Frame(m) is not { } frame)
        {
            Row("Plane", _plane.Index == 3 ? "pick a construction plane or a flat face" : "select an area with Edit Region first, then come back", 2);
            return null;
        }
        var n = frame.Normal;
        double offset = _plane.Index == Movable ? 0 : _offset.Value;
        if (_interactive)
        {
            double extent = Math.Max((m.Max - m.Min).Length * 0.52, 0.01);
            var p = frame with { Origin = frame.Origin + n * offset };
            _planeOutline = [p.PointAt(-extent, -extent), p.PointAt(extent, -extent), p.PointAt(extent, extent), p.PointAt(-extent, extent)];
        }
        var output = new CommandOutput { Label = Title, Icon = Icon };
        int sketches = 0, loops = 0, lines = 0, arcs = 0, splines = 0, circles = 0, rawPts = 0, grazing = 0, tangents = 0;
        double worst = 0, worstRatio = 0, sigma = 0, tolMin = double.MaxValue, tolMax = 0;
        int trace = _trace.Index;
        if (trace == TraceRegion && (S.RegionSel is not { IsEmpty: false } region || !ReferenceEquals(region.Mesh, m)))
        {
            Row("Region", "select the area to trace with Select Region or Face selection first", 2);
            return null;
        }
        int planes = trace == TraceSection ? _count.Value : 1;
        // projected creases and borders: a vertical edge lands as a speck, and parallel rims land on top of each other
        double speck = Math.Max(0.5, 0.005 * m.Size.Length);
        var kept = new List<List<(double x, double y)>>();
        for (int k = 0; k < planes; k++)
        {
            var plane = frame with { Origin = frame.Origin + n * (offset + k * _spacing.Value) };
            var model = create ? new SketchModel() : null; int found = 0;
            var fitted = new List<(MeshOutline.Trace l, List<(double x, double y)> p2, List<SketchSeg> segs, double tol)>();
            foreach (var l in Traces(m, plane, trace, ref grazing))
            {
                if (l.Points.Count < (l.Closed ? 3 : 2)) continue;
                var p2 = l.Points.Select(p => plane.Project(p)).Select(p => (x: p.u, y: p.v)).ToList();
                if (trace != TraceSection && LineArcFit.Size(p2) < speck) continue;
                double tol = _tol.Value;
                if (_autoTol.Value)
                {
                    // each outline gets a tolerance from its own measured noise, so a noisy scan is not cut into many short curves
                    double noise = LineArcFit.NoiseSigma(p2, l.Closed);
                    tol = LineArcFit.ToleranceFromNoise(noise, LineArcFit.Size(p2), _tol.Value);
                    if (!double.IsNaN(noise)) sigma = Math.Max(sigma, noise);
                }
                tol = Math.Max(tol, TolFloor(m, plane, trace, tol));
                // a region border runs along triangle edges and zig-zags by about a quarter of an edge: never fit tighter
                if (trace == TraceRegion && p2.Count > 1)
                {
                    double edges = 0; for (int i = 1; i < p2.Count; i++) edges += Math.Sqrt(Math.Pow(p2[i].x - p2[i - 1].x, 2) + Math.Pow(p2[i].y - p2[i - 1].y, 2));
                    tol = Math.Max(tol, 0.25 * edges / (p2.Count - 1));
                }
                tolMin = Math.Min(tolMin, tol); tolMax = Math.Max(tolMax, tol);
                // while the plane is dragged only lines and arcs are fitted; the splines follow when it is released
                var segs = !create && S.CommandGizmoDragging ? LineArcFit.Fit(p2, l.Closed, tol) : SectionProfileFit.Fit(p2, l.Closed, tol);
                if (segs.Count > 0) fitted.Add((l, p2, segs, tol));
            }
            // parallel rims project onto each other: keep the cleanest copy (fewest curves) of each
            if (trace == TraceCreases) fitted = fitted.OrderBy(f => f.segs.Count).ThenByDescending(f => f.p2.Count).ToList();
            foreach (var (l, p2, segs, tol) in fitted)
            {
                if (trace == TraceCreases && Covered(p2, kept, 2 * tol)) continue;
                kept.Add(p2);
                double dev = LineArcFit.RobustDeviation(p2.Select(q => LineArcFit.MaxDeviation([q], segs)).ToArray());
                loops++; found++; rawPts += l.Points.Count; worst = Math.Max(worst, dev); worstRatio = Math.Max(worstRatio, dev / tol);
                if (l.Closed && segs.Count == 2 && segs.All(s => s.IsArc)) circles++;      // two half arcs become one circle
                else { lines += segs.Count(s => !s.IsArc && !s.IsSpline); arcs += segs.Count(s => s.IsArc); splines += segs.Count(s => s.IsSpline); }
                _overlay.Add(segs.SelectMany(s => s.Polyline().Select(q => plane.PointAt(q.x, q.y))).ToList());
                if (_interactive) _raw.Add(l.Closed ? [.. l.Points, l.Points[0]] : l.Points);
                if (model != null)
                {
                    model.MeshSectionTraces.Add(new MeshSectionTrace(l.Closed, p2.Select(p => new MeshSectionSample(p.x, p.y)).ToArray()));
                    AddToSketch(model, segs, l.Closed);
                }
            }
            if (found == 0) continue;
            sketches++;
            if (model != null) tangents += AddTangency(model, 3 * tolMax);
            if (model != null)
                output.Add.Add(new NewObject(Occ.Profile(model.ToShapes(plane), faces: false), "Section sketch", ObjKind.Sketch, Plane: plane, Sketch: model));
        }
        string ignored = grazing == 0 ? "" : $"Ignored {grazing} noise outline{(grazing == 1 ? "" : "s")} where the plane slides along a face. To trace that face, select it and choose Border of the selected region.";
        if (loops == 0)
        {
            Row("Nothing here", trace switch
            {
                TraceSection => grazing > 0 ? ignored : "the plane does not cut the mesh",
                TraceCreases => "no creases sharper than the crease angle",
                _ => "nothing to trace",
            }, 2);
            return null;
        }
        Row("Sketches", sketches.ToString(), 1);
        Row("Outlines", loops.ToString());
        static string Of(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
        Row("Elements", $"{Of(lines, "line")} + {Of(arcs, "arc")} + {Of(splines, "spline")} + {Of(circles, "circle")} (from {Count(rawPts)} points)");
        if (tangents > 0) Row("Constraints", $"{tangents} tangent");
        if (grazing > 0) Row("Noise", ignored, 2);
        string range = tolMax - tolMin < 0.0005 ? N(tolMax, "mm") : $"{N(tolMin)} to {N(tolMax, "mm")}";
        Row("Fit tolerance", _autoTol.Value ? $"{range} (scan noise about {N(sigma, "mm")})" : range);
        Row("Largest deviation", N(worst, "mm"), worstRatio <= 1.5 ? 1 : 2);
        Row("Output", "Editable sketch curves. Add driving dimensions in the sketch.");
        if (_interactive) Row("Preview", "Orange: measured section. Cyan: editable curves.");
        return create ? output : Report();
    }

    /// <summary>The silhouette is traced on a grid and follows the outermost noise of the scan: never fit tighter than three cells.</summary>
    double TolFloor(TriMesh m, Plane3 plane, int trace, double tol) =>
        trace == TraceSilhouette ? 3 * MeshOutline.SilhouetteCell(m, plane, _autoTol.Value ? 0.05 : _tol.Value) : 0;

    /// <summary>True when nearly every point lies within reach of an outline already kept (a second rim projecting onto the first).</summary>
    static bool Covered(List<(double x, double y)> p2, List<List<(double x, double y)>> kept, double reach)
    {
        if (kept.Count == 0) return false;
        // points spread along the curve, ends left out: a side that only shares its corners with a kept curve is not a copy
        double length = 0; for (int i = 1; i < p2.Count; i++) length += Math.Sqrt(Math.Pow(p2[i].x - p2[i - 1].x, 2) + Math.Pow(p2[i].y - p2[i - 1].y, 2));
        if (length <= 0) return true;
        var samples = new List<(double x, double y)>();
        double walked = 0; int seg = 0;
        for (int k = 1; k < 40; k++)
        {
            double at = length * k / 40;
            while (seg < p2.Count - 2 && walked + Seg(seg) < at) { walked += Seg(seg); seg++; }
            double f = Seg(seg) > 0 ? Math.Clamp((at - walked) / Seg(seg), 0, 1) : 0;
            samples.Add((p2[seg].x + (p2[seg + 1].x - p2[seg].x) * f, p2[seg].y + (p2[seg + 1].y - p2[seg].y) * f));
        }
        double Seg(int i) => Math.Sqrt(Math.Pow(p2[i + 1].x - p2[i].x, 2) + Math.Pow(p2[i + 1].y - p2[i].y, 2));
        int near = 0, total = 0;
        foreach (var q in samples)
        {
            total++;
            if (kept.Any(k => Enumerable.Range(0, k.Count - 1).Any(j => LineArcFit.SegmentDistance(q, k[j], k[j + 1]) <= reach))) near++;
        }
        return near >= 0.9 * total;
    }

    /// <summary>The outlines the chosen source traces on this plane; section loops that only graze a face are counted and skipped.</summary>
    List<MeshOutline.Trace> Traces(TriMesh m, Plane3 plane, int trace, ref int grazing)
    {
        switch (trace)
        {
            case TraceRegion:
                return MeshOutline.RegionBorder(m, S.RegionSel!.Tris);
            case TraceSilhouette:
            {
                double cell = MeshOutline.SilhouetteCell(m, plane, _autoTol.Value ? 0.05 : _tol.Value);
                return MeshOutline.Silhouette(m, plane, cell).Select(loop => new MeshOutline.Trace(loop.Select(q => plane.PointAt(q.u, q.v)).ToList(), true)).ToList();
            }
            case TraceCreases:
                return MeshOutline.Creases(m, _crease.Value, Math.Max(1, 0.02 * m.Size.Length));
            default:
            {
                var all = MeshOutline.Section(m, plane.Origin, plane.Normal);
                grazing += all.Count(t => t.Steepness < MeshOutline.GrazingSteepness);
                return all.Where(t => t.Steepness >= MeshOutline.GrazingSteepness).ToList();
            }
        }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_planeOutline is [var a, var b, var c, var d])
        {
            o.AddTri(a, b, c, 0x1835B6FF);
            o.AddTri(a, c, d, 0x1835B6FF);
            o.AddPolyline([a, b, c, d, a], 0xC035B6FF, 1.5f, false);
        }
        foreach (var l in _raw) o.AddPolyline(l, 0xFFEAA755, 4f, false);
        foreach (var l in _overlay) o.AddPolyline(l, 0xFF35B6FF, 2.4f, false);
    }
}

public sealed class MeshSectionSketchPanelCommand() : MeshSectionSketchCommand(interactive: true);

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
                _overlay.Add(segs.SelectMany(s => s.Polyline().Select(P3)).ToList());
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
        return _create.Value ? o : Report();
    }

    public override void BuildOverlay(CadSession s, Overlay o) { foreach (var l in _overlay) o.AddPolyline(l, 0xFF35B6FF, 2.4f, false); }
}
