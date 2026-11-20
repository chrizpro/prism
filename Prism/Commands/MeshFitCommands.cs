using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>
/// Fit Region: turn the selected region of a mesh (Mesh > Edit Region) into exact CAD geometry: a plane, cylinder, sphere or cone
/// (as a surface trimmed to the region, a closed solid, or only its reference plane/axis) or a smooth NURBS patch.
/// See docs/modeling/mesh-and-reverse-engineer/README.md (MSH-P05).
/// </summary>
public sealed class MeshFitSurfaceCommand : MeshCommand
{
    readonly ChoiceParam _type, _create;
    readonly BoolParam _refs;
    readonly NumberParam _tol, _outline, _corner;
    readonly IntParam _interior, _grid;
    List<List<Vec3>> _outlines = []; List<Vec3> _samples = [];

    public MeshFitSurfaceCommand() : base("Mesh (the selected region is used)")
    {
        _type = Choice("Fit", 0, "Automatic: exact shape if it fits, else smooth patch", "Plane", "Cylinder", "Sphere", "Cone", "Smooth NURBS patch inside the outline", "Rectangular NURBS grid");
        _tol = Num("Fit tolerance", 0.2, 0.001, 50, 0.05);
        _outline = Num("Outline detail", 0.3, 0.01, 50, 0.1);
        _corner = Num("Corner angle", 35, 5, 120, 5, "°");
        _interior = Add(new IntParam { Label = "Points the surface must follow", Value = 150, Min = 0, Max = 1000 });
        _grid = Add(new IntParam { Label = "Grid size (points per side)", Value = 10, Min = 4, Max = 60 });
        _create = Choice("Create", 0, "Surface trimmed to the region", "Closed solid", "Reference geometry only");
        _refs = Check("Add reference geometry (plane or axis)", true);
        PreviewButton();
        Src.IsVisible = false;
    }

    public override string Title => "Fit Region";
    public override string Hint => "Uses the selected triangles: pick them in Face mode (double-click a triangle to take its whole smooth patch) or with Edit Region. Exact shapes (plane, cylinder, sphere, cone) suit mechanical faces and report their size, axis and position; the smooth patch follows organic regions and is trimmed to the outline you picked.";
    public override string Icon => "mesh-patch";
    public override string OkText => "Create";

    protected override void OnBegin()
    {
        // nothing to pick here: the region comes from the session
        if (S.RegionSel is { } r && S.Find(r.ObjectId) is { } o) { Src.Items.Clear(); Src.Items.Add(SelItem.Of(o)); }
        var diag = S.RegionSel?.Mesh.Size.Length ?? 100;
        _tol.Value = Math.Round(Math.Max(diag * 0.002, 0.01), 3);
        _outline.Value = Math.Round(Math.Max(diag * 0.004, 0.02), 3);
    }

    protected override void UpdateVisibility()
    {
        _outline.IsVisible = _corner.IsVisible = _interior.IsVisible = _type.Index == 0 || _type.Index == 5;
        _grid.IsVisible = _type.Index == 6;
        _create.IsVisible = _refs.IsVisible = _type.Index <= 4;
    }

    static int Complexity(PrimKind k) => k switch { PrimKind.Plane => 0, PrimKind.Sphere => 1, PrimKind.Cylinder => 2, _ => 3 };

    PrimKind[] KindsFor(int type) => type switch { 1 => [PrimKind.Plane], 2 => [PrimKind.Cylinder], 3 => [PrimKind.Sphere], 4 => [PrimKind.Cone], _ => [PrimKind.Plane, PrimKind.Cylinder, PrimKind.Sphere, PrimKind.Cone] };

    protected override CommandOutput? Build()
    {
        Results.Clear(); _outlines = []; _samples = [];
        var sel = S.RegionSel;
        var obj = sel == null ? null : S.Find(sel.ObjectId);
        if (sel == null || sel.IsEmpty || obj == null) { Row("No region selected", "select triangles in Face mode (double-click takes a whole patch), or use Edit Region", 2); return null; }
        var sub = sel.Extract();
        Row("Region", $"{Count(sub.TriCount)} triangles, {N(sub.Area(), "mm²")}");

        var key = Key(sel.Version, sub.TriCount, _type.Index, _tol.Value, _outline.Value, _corner.Value, _interior.Value, _grid.Value, _create.Index, _refs.Value);
        var result = Cached(key, () => Fit(sel, sub));
        if (result == null) return null;
        foreach (var l in result.Rows) Results.Add(l);
        _outlines = result.Outlines; _samples = result.Samples;
        if (result.Shape == null && result.References.Count == 0) return null;
        Row("Output", "Fitted CAD geometry; original sketches and feature history are not recovered");
        var o = new CommandOutput { Label = "Fit region", Icon = Icon };
        if (result.Shape != null) o.Add.Add(new(result.Shape, result.Name, result.Solid ? ObjKind.Solid : ObjKind.Surface, null, null, null, obj.Look));
        o.Add.AddRange(result.References);
        return o;
    }

    sealed record FitResult(Shape? Shape, string Name, bool Solid, List<ResultLine> Rows, List<List<Vec3>> Outlines, List<Vec3> Samples)
    {
        public List<NewObject> References { get; init; } = [];
    }

    static string P(Vec3 p) => $"({p.X:0.###}, {p.Y:0.###}, {p.Z:0.###})";
    static string D(Vec3 d) { if (d.X + d.Y + d.Z < 0) d = -d; return $"({d.X:0.####}, {d.Y:0.####}, {d.Z:0.####})"; }

    /// <summary>Angle in degrees between a direction and the closest world axis, ignoring sign.</summary>
    internal static double TiltFromWorld(Vec3 d)
    {
        d = d.Normalized();
        double best = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z)));
        return Math.Acos(Math.Clamp(best, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>The fitted shape's numbers: what a reverse engineer types into a sketch or feature.</summary>
    static void Describe(FitRegion r, TriMesh sub, Action<string, string, int> row)
    {
        string tilt = $"{TiltFromWorld(r.Axis):0.###}° from the nearest world axis";
        switch (r.Kind)
        {
            case PrimKind.Plane:
            {
                var c = sub.Centroid();
                row("Point on plane", P(c - r.Axis * Vec3.Dot(c - r.Origin, r.Axis)), 0);
                row("Normal", D(r.Axis), 0);
                row("Tilt", tilt, 0);
                break;
            }
            case PrimKind.Cylinder:
            case PrimKind.Cone:
            {
                if (r.Kind == PrimKind.Cylinder) row("Diameter", N(2 * r.Radius, "mm"), 1);
                else { row("Included angle", $"{2 * r.HalfAngle:0.###}°", 1); row("Apex", P(r.Origin), 0); }
                row("Axis point (middle)", P(r.Origin + r.Axis * ((r.T0 + r.T1) / 2)), 0);
                row("Axis direction", D(r.Axis), 0);
                row("Length along the axis", N(Math.Abs(r.T1 - r.T0), "mm"), 0);
                row("Tilt", tilt, 0);
                break;
            }
            case PrimKind.Sphere:
                row("Diameter", N(2 * r.Radius, "mm"), 1);
                row("Centre", P(r.Origin), 0);
                break;
        }
    }

    /// <summary>A construction plane for a fitted plane, or an axis line for a cylinder or cone.</summary>
    static List<NewObject> ReferencesFor(FitRegion r, TriMesh sub)
    {
        var refs = new List<NewObject>();
        switch (r.Kind)
        {
            case PrimKind.Plane:
            {
                var c = sub.Centroid();
                var on = c - r.Axis * Vec3.Dot(c - r.Origin, r.Axis);
                var (u, v) = SurfaceMath.Basis(r.Axis);
                double half = Math.Max(sub.Size.Length * 0.6, 5);
                var pl = new Plane3(on, u, v);
                var rect = Occ.Polyline([pl.PointAt(-half, -half), pl.PointAt(half, -half), pl.PointAt(half, half), pl.PointAt(-half, half)], true);
                refs.Add(new NewObject(rect, "Fitted plane", ObjKind.Plane, Plane: pl));
                break;
            }
            case PrimKind.Cylinder:
            case PrimKind.Cone:
            {
                double t0 = r.Kind == PrimKind.Cone ? Math.Min(0, r.T0) : r.T0, t1 = r.T1, extra = Math.Max((t1 - t0) * 0.15, 1);
                refs.Add(new NewObject(Occ.Line(r.Origin + r.Axis * (t0 - extra), r.Origin + r.Axis * (t1 + extra)), "Fitted axis", ObjKind.Curve, 0xFFE8A23A));
                break;
            }
        }
        return refs;
    }

    internal CommandOutput? BuildConversion(SceneObject obj, MeshSelection selection, int type, double tolerance, bool surfaceOnly, bool primitiveOnly)
    {
        _type.Index = type;
        _tol.Value = tolerance;
        // Convert Mesh keeps its own outputs: a closed solid (or its surface) and no reference geometry
        _create.Index = surfaceOnly ? 0 : 1;
        _refs.Value = false;
        _outline.Value = Math.Round(Math.Max(selection.Mesh.Size.Length * 0.004, 0.02), 3);
        var fit = Fit(selection, selection.Extract(), surfaceOnly, primitiveOnly);
        foreach (var row in fit.Rows) Results.Add(row);
        if (fit.Shape == null) return null;
        var output = new CommandOutput();
        output.Add.Add(new(fit.Shape, fit.Name, fit.Solid ? ObjKind.Solid : ObjKind.Surface, Look: obj.Look));
        return output;
    }

    FitResult Fit(MeshSelection sel, TriMesh sub, bool surfaceOnly = false, bool primitiveOnly = false)
    {
        var rows = new List<ResultLine>();
        void R(string a, string b, int lvl = 0) => rows.Add(new ResultLine(a, b, lvl));
        int type = _type.Index;

        // ---- analytic shapes
        if (type <= 4)
        {
            var opt = new PrimitiveFit.Options(_tol.Value, 15, 3, !primitiveOnly && KindsFor(type).Contains(PrimKind.Plane), KindsFor(type).Contains(PrimKind.Cylinder), KindsFor(type).Contains(PrimKind.Sphere), KindsFor(type).Contains(PrimKind.Cone), 4);
            var regs = PrimitiveFit.Detect(sub, opt);
            double total = sub.Area();
            var best = regs.OrderByDescending(r => r.Area).FirstOrDefault();
            bool good = best != null && best.Area >= total * (type == 0 ? 0.85 : 0.5) && (type != 0 || best.Rms < _tol.Value * 0.6);
            if (!surfaceOnly)
            {
                // The user picked the region, so fit the whole of it by least squares on its vertices. An explicit choice always gets that
                // shape; Automatic takes the simplest shape whose error is close to the best one and within the tolerance.
                var whole = KindsFor(type).Select(k => PrimitivePolish.FitWhole(sub, k)).Where(f => f != null).Select(f => f!).ToList();
                FitRegion? pick = null;
                if (type != 0) pick = whole.FirstOrDefault(f => f.Rms <= Math.Max(_tol.Value, 1e-6) * 3);
                else if (whole.Count > 0)
                {
                    double least = whole.Min(f => f.Rms);
                    pick = whole.Where(f => f.Rms <= least * 1.25 + 1e-6 && f.Rms <= _tol.Value * 0.6).OrderBy(f => Complexity(f.Kind)).FirstOrDefault();
                }
                if (pick != null) { best = pick; good = true; }
                else if (best != null) best = PrimitivePolish.Refine(best, sub);
            }
            if (good && best != null)
            {
                try
                {
                    NewObject? made = null;
                    int create = surfaceOnly ? 0 : _create.Index;
                    if (create == 2) { }
                    else if (create == 0 || best.Kind == PrimKind.Plane)
                    {
                        var graph = RegionGraph.Build(sub, [best]);
                        var faces = MeshRebuildCommand.BuildFaces(best, sub, graph, 0, _tol.Value);
                        var face = faces.Count == 1 ? faces[0] : Occ.Compound(faces);
                        made = new NewObject(face, best.Kind + " fit", ObjKind.Surface);
                    }
                    else
                        made = MeshSegmentCommand.BuildGeometry(best, sub);
                    var refs = !surfaceOnly && (_refs.Value || create == 2) ? ReferencesFor(best, sub) : [];
                    if (made?.Shape != null || refs.Count > 0)
                    {
                        R("Fitted shape", best.Describe(), 1);
                        Describe(best, sub, R);
                        R("Covers", $"{100 * best.Area / total:0.#}% of the region");
                        R("Fit error (RMS)", N(best.Rms, "mm"), best.Rms < _tol.Value ? 1 : 2);
                        if (best.MaxDev > 0) R("Largest error", N(best.MaxDev, "mm"), best.MaxDev <= _tol.Value * 3 ? 0 : 2);
                        if (create == 1 && best.Kind == PrimKind.Plane) R("Closed solid", "a plane has no closed solid, so the trimmed surface is created", 2);
                        if (create == 2 && refs.Count == 0) R("Reference geometry", "a sphere has no plane or axis; its centre is listed above", 2);
                        return new FitResult(made?.Shape, made?.NamePrefix ?? "", made?.Kind == ObjKind.Solid, rows, [], []) { References = refs };
                    }
                }
                catch (KernelException e) { R("Could not build", e.Message, 2); }
            }
            if (type != 0 || primitiveOnly) { R("No match", best == null ? "the selected region does not match the requested shape" : $"only {100 * best.Area / total:0.#}% of it fits a {best.Kind}", 2); return new FitResult(null, "", false, rows, [], []); }
            R("Not a single exact shape", "using a smooth patch instead");
        }

        // ---- smooth patch bounded by the region outline
        if (type is 0 or 5)
        {
            var outlines = sel.Outlines();
            if (outlines.Count == 0) { R("The region has no outline", "it covers a closed shell or touches nothing", 2); return new FitResult(null, "", false, rows, [], []); }
            var outer = outlines[0];
            var closed = new List<Vec3>(outer) { outer[0] };
            var simp = MeshSectionCommand.Simplify(closed, _outline.Value, true);
            var edges = BuildEdges(simp, _corner.Value);
            var boundary = new HashSet<Vec3>(outer);
            int stride = Math.Max(1, sub.VertexCount / Math.Max(_interior.Value, 1));
            var pts = new List<Vec3>();
            if (_interior.Value > 0) for (int i = 0; i < sub.VertexCount; i += stride) { var p = sub.P(i); if (!boundary.Contains(p)) pts.Add(p); }
            Shape? face = null; string note = "";
            foreach (var count in new[] { pts.Count, Math.Min(pts.Count, 40), 0 })
            {
                try { face = Occ.PatchThrough(edges, pts.Take(count).ToList(), 3, 15, 3, Math.Max(_tol.Value * 0.5, 1e-3)); note = count == pts.Count ? "" : $" (reduced to {count} guide points)"; break; }
                catch (KernelException) { }
            }
            if (face == null) { R("The patch could not be built", "try a larger outline detail or a simpler region", 3); return new FitResult(null, "", false, rows, [outer], pts); }
            var dev = Deviation(face, sub);
            R("Smooth patch", $"{edges.Count} outline edge(s){note}", 1);
            if (dev != null)
            {
                R("Fit error (RMS)", N(dev.Rms, "mm"), dev.Rms <= _tol.Value ? 1 : 2);
                R("Largest error", N(dev.AbsMax, "mm"), dev.AbsMax <= _tol.Value * 3 ? 1 : 2);
                R("Within tolerance", $"{dev.WithinTolerance:0.#}% of the region");
            }
            return new FitResult(face, "Patch fit", false, rows, [outer], pts);
        }

        // ---- rectangular grid
        {
            var (_, ax) = MeshAnalysis.Principal(sub);
            var n = ax[2]; var u = ax[0]; var v = ax[1]; var c = sub.Centroid();
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            for (int i = 0; i < sub.VertexCount; i++) { var d = sub.P(i) - c; double a = Vec3.Dot(d, u), b = Vec3.Dot(d, v); u0 = Math.Min(u0, a); u1 = Math.Max(u1, a); v0 = Math.Min(v0, b); v1 = Math.Max(v1, b); }
            int gn = _grid.Value; var grid = new Vec3[gn * gn]; int missed = 0;
            double reach = sub.Size.Length;
            for (int j = 0; j < gn; j++)
                for (int i = 0; i < gn; i++)
                {
                    var p = c + u * (u0 + (u1 - u0) * i / (gn - 1)) + v * (v0 + (v1 - v0) * j / (gn - 1));
                    Vec3? hit = null; double bt = double.MaxValue;
                    foreach (var dir in new[] { n, -n })
                        if (sub.Bvh.Raycast(p - dir * reach, dir, reach * 2) is { } h) { var q = p - dir * reach + dir * h.t; double dd = q.DistanceTo(p); if (dd < bt) { bt = dd; hit = q; } }
                    if (hit == null) { missed++; hit = sub.Bvh.ClosestPoint(p)?.Point ?? p; }
                    grid[j * gn + i] = hit.Value;
                }
            var shape = Occ.FitSurface(grid, gn, gn);
            var dev = Deviation(shape, sub);
            R("Grid patch", $"{gn} × {gn} points" + (missed > 0 ? $", {missed} snapped to the nearest triangle" : ""), 1);
            if (dev != null) R("Fit error (RMS)", N(dev.Rms, "mm"), dev.Rms <= _tol.Value ? 1 : 2);
            R("Note", "the grid covers the region's bounding box; trim it with a boundary patch if needed");
            return new FitResult(shape, "Grid fit", false, rows, [], grid.ToList());
        }
    }

    /// <summary>Outline edges: splines between corner points (so sharp corners stay sharp), or four arcs for a smooth loop.</summary>
    static List<Shape> BuildEdges(List<Vec3> loop, double cornerDeg)
    {
        var pts = loop.Take(loop.Count - 1).ToList();      // drop the repeated closing point
        int n = pts.Count;
        var corner = new bool[n]; double cosC = Math.Cos(cornerDeg * Math.PI / 180);
        for (int i = 0; i < n; i++)
        {
            var a = (pts[i] - pts[(i + n - 1) % n]).Normalized(); var b = (pts[(i + 1) % n] - pts[i]).Normalized();
            corner[i] = Vec3.Dot(a, b) < cosC;
        }
        var idx = Enumerable.Range(0, n).Where(i => corner[i]).ToList();
        // a smooth loop (or a single corner) is split into a few spans so the surface fill has several boundary edges to work with
        if (idx.Count == 0) idx = [0, n / 4, n / 2, 3 * n / 4];
        else if (idx.Count == 1) idx = [idx[0], (idx[0] + n / 3) % n, (idx[0] + 2 * n / 3) % n];
        idx = idx.Distinct().OrderBy(i => i).ToList();
        var edges = new List<Shape>();
        for (int k = 0; k < idx.Count; k++)
        {
            int a = idx[k], b = idx[(k + 1) % idx.Count];
            var run = new List<Vec3> { pts[a] };
            for (int i = (a + 1) % n; i != b; i = (i + 1) % n) run.Add(pts[i]);
            run.Add(pts[b]);
            if (run.Count < 2 || run[0].DistanceTo(run[^1]) < 1e-9 && run.Count < 3) continue;
            // light smoothing of the free points so scanner noise does not wiggle the edge
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < run.Count - 1; i++) run[i] = run[i] * 0.5 + (run[i - 1] + run[i + 1]) * 0.25;
            edges.Add(run.Count == 2 ? Occ.Line(run[0], run[1]) : Occ.Spline(run, false));
        }
        return edges;
    }

    static MeshAnalysis.DeviationResult? Deviation(Shape face, TriMesh sub)
    {
        try
        {
            var surf = MeshAnalysis.FromMeshData(face.Tessellate(Math.Max(sub.Size.Length * 0.0005, 1e-3)));
            var s = new List<float>(); int step = Math.Max(1, sub.VertexCount / 4000);
            for (int i = 0; i < sub.VertexCount; i += step) { s.Add(sub.V[3 * i]); s.Add(sub.V[3 * i + 1]); s.Add(sub.V[3 * i + 2]); }
            return MeshAnalysis.Deviation(new TriMesh([.. s], []), surf, Math.Max(sub.Size.Length * 0.002, 0.01));
        }
        catch (KernelException) { return null; }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var l in _outlines) o.AddPolyline(l, 0xFF35B6FF, 2.6f, true);
        foreach (var p in _samples.Take(300)) o.AddDot(p, 0xFFFFC53D, 5);
    }
}
