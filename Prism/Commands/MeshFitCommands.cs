using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Turn the selected region of a mesh (Mesh > Select Region) into an exact CAD surface: an analytic shape or a smooth NURBS patch.</summary>
public sealed class MeshFitSurfaceCommand : MeshCommand
{
    readonly ChoiceParam _type;
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
        PreviewButton();
        Src.IsVisible = false;
    }

    public override string Title => "Fit Surface to Region";
    public override string Hint => "Uses the triangles picked with Select Region. Exact shapes (plane, cylinder, sphere, cone) are best for mechanical faces; the smooth patch follows organic regions and is trimmed to the outline you picked.";
    public override string Icon => "mesh-patch";
    public override string OkText => "Create surface";

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
    }

    PrimKind[] KindsFor(int type) => type switch { 1 => [PrimKind.Plane], 2 => [PrimKind.Cylinder], 3 => [PrimKind.Sphere], 4 => [PrimKind.Cone], _ => [PrimKind.Plane, PrimKind.Cylinder, PrimKind.Sphere, PrimKind.Cone] };

    protected override CommandOutput? Build()
    {
        Results.Clear(); _outlines = []; _samples = [];
        var sel = S.RegionSel;
        var obj = sel == null ? null : S.Find(sel.ObjectId);
        if (sel == null || sel.IsEmpty || obj == null) { Row("No region selected", "use Mesh > Select Region first", 2); return null; }
        var sub = sel.Extract();
        Row("Region", $"{Count(sub.TriCount)} triangles, {N(sub.Area(), "mm²")}");

        var key = Key(sel.Version, sub.TriCount, _type.Index, _tol.Value, _outline.Value, _corner.Value, _interior.Value, _grid.Value);
        var result = Cached(key, () => Fit(sel, sub));
        if (result == null) return null;
        foreach (var l in result.Rows) Results.Add(l);
        _outlines = result.Outlines; _samples = result.Samples;
        if (result.Shape == null) return null;
        var o = new CommandOutput { Label = "Fit surface", Icon = Icon };
        o.Add.Add(new(result.Shape, result.Name, result.Solid ? ObjKind.Solid : ObjKind.Surface, null, null, null, obj.Look));
        return o;
    }

    sealed record FitResult(Shape? Shape, string Name, bool Solid, List<ResultLine> Rows, List<List<Vec3>> Outlines, List<Vec3> Samples);

    FitResult Fit(MeshSelection sel, TriMesh sub)
    {
        var rows = new List<ResultLine>();
        void R(string a, string b, int lvl = 0) => rows.Add(new ResultLine(a, b, lvl));
        int type = _type.Index;

        // ---- analytic shapes
        if (type <= 4)
        {
            var opt = new PrimitiveFit.Options(_tol.Value, 15, 3, KindsFor(type).Contains(PrimKind.Plane), KindsFor(type).Contains(PrimKind.Cylinder), KindsFor(type).Contains(PrimKind.Sphere), KindsFor(type).Contains(PrimKind.Cone), 4);
            var regs = PrimitiveFit.Detect(sub, opt);
            double total = sub.Area();
            var best = regs.OrderByDescending(r => r.Area).FirstOrDefault();
            bool good = best != null && best.Area >= total * (type == 0 ? 0.85 : 0.5) && (type != 0 || best.Rms < _tol.Value * 0.6);
            if (good && best != null)
            {
                try
                {
                    var made = MeshSegmentCommand.BuildGeometry(best, sub);
                    if (made?.Shape != null)
                    {
                        R("Fitted shape", best.Describe(), 1);
                        R("Covers", $"{100 * best.Area / total:0.#}% of the region");
                        R("Fit error (RMS)", N(best.Rms, "mm"), best.Rms < _tol.Value ? 1 : 2);
                        return new FitResult(made.Shape, made.NamePrefix, made.Kind == ObjKind.Solid, rows, [], []);
                    }
                }
                catch (KernelException e) { R("Could not build", e.Message, 2); }
            }
            if (type != 0) { R("No match", best == null ? "the region does not look like that shape - try a smooth patch" : $"only {100 * best.Area / total:0.#}% of it fits a {best.Kind}", 2); return new FitResult(null, "", false, rows, [], []); }
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
