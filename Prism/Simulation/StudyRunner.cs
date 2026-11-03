using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Simulation;

/// <summary>Meshes the study's body, turns fixtures and loads into nodal data, runs the solver and gathers results.</summary>
public static class StudyRunner
{
    public sealed record Prepared(FemMesh Mesh, int[] FaceOfBoundary, Vec3[] BoundaryNormal, double[] BoundaryArea, SceneObject Body, double Maxh)
    {
        /// <summary>The CAD surface, used to find where bodies touch.</summary>
        public TriMesh? Surface { get; init; }
        /// <summary>The bodies of a multi-body mesh; null for one body. FaceOfBoundary then counts on through each body's faces (see FaceCode).</summary>
        public IReadOnlyList<MeshPart>? Parts { get; init; }
        public IReadOnlyList<BondedFaces> Bonds { get; init; } = [];
        /// <summary>The number FaceOfBoundary uses for a face of a body in this mesh, or -1 when the body is not in it.</summary>
        public int FaceCode(FaceRef f)
        {
            if (Parts == null) return f.Obj == Body.Id ? f.Face : -1;
            foreach (var part in Parts) if (part.Body.Id == f.Obj) return part.FaceOffset + f.Face;
            return -1;
        }
    }

    public static double AutoSize(SceneObject body) => Math.Max(body.Mesh.Max.DistanceTo(body.Mesh.Min) / 14.0, 0.05);

    /// <summary>Tetrahedral mesh of the body, plus the link from each boundary triangle back to the CAD face it lies on.</summary>
    public static Prepared Mesh(CadSession s, StudyDef st)
    {
        var body = s.Find(st.BodyId) ?? throw new InvalidOperationException("The study's body no longer exists.");
        if (st.OtherBodies.Count == 0) return Mesh(body, st.ElementSize);
        var all = new List<SceneObject> { body };
        foreach (var id in st.OtherBodies) all.Add(s.Find(id) ?? throw new InvalidOperationException("A body in this study no longer exists."));
        return CaeAssembly.Mesh(all, st.ElementSize);
    }

    public static Prepared Mesh(SceneObject body, double elementSize)
    {
        if (body.Kind is not (ObjKind.Solid or ObjKind.Form)) throw new InvalidOperationException("Studies need a solid body.");
        double diag = body.Mesh.Max.DistanceTo(body.Mesh.Min);
        double maxh = elementSize > 0 ? elementSize : AutoSize(body);
        var md = body.Shape.Tessellate(Math.Max(Math.Min(maxh * 0.15, diag * 0.002), diag * 0.0003));
        int nt = md.TriangleCount;
        var soup = new float[nt * 9];
        for (int t = 0; t < nt; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = md.Indices[3 * t + k];
                soup[9 * t + 3 * k] = md.Positions[3 * v]; soup[9 * t + 3 * k + 1] = md.Positions[3 * v + 1]; soup[9 * t + 3 * k + 2] = md.Positions[3 * v + 2];
            }
        var mesh = FemMesh.FromSurface(soup, maxh);
        var tess = new TriMesh((float[])md.Positions.Clone(), (int[])md.Indices.Clone());
        var bvh = tess.Bvh;
        var face = new int[mesh.BoundaryCount]; var nrm = new Vec3[mesh.BoundaryCount]; var area = new double[mesh.BoundaryCount];
        for (int i = 0; i < mesh.BoundaryCount; i++)
        {
            Vec3 P(int k) => new(mesh.Nodes[3 * mesh.Boundary[6 * i + k]], mesh.Nodes[3 * mesh.Boundary[6 * i + k] + 1], mesh.Nodes[3 * mesh.Boundary[6 * i + k] + 2]);
            var a = P(0); var b = P(1); var c = P(2); var cen = (a + b + c) / 3;
            area[i] = Vec3.Cross(b - a, c - a).Length * 0.5;
            var hit = bvh.ClosestPoint(cen);
            if (hit == null) { face[i] = -1; continue; }
            face[i] = md.TriFace[hit.Value.Tri];
            nrm[i] = tess.FaceNormal(hit.Value.Tri);
        }
        return new Prepared(mesh, face, nrm, area, body, maxh) { Surface = tess };
    }

    /// <summary>The boundary triangles that lie on the given faces of the prepared body.</summary>
    public static int[] BoundaryTrianglesOn(Prepared p, IEnumerable<int> faceIndices)
    {
        var set = faceIndices.ToHashSet(); var tris = new List<int>();
        for (int i = 0; i < p.FaceOfBoundary.Length; i++) if (set.Contains(p.FaceOfBoundary[i])) tris.Add(i);
        return [.. tris];
    }

    static Dictionary<int, int> NodesOn(Prepared p, IEnumerable<FaceRef> faces, out List<int> tris)
    {
        var set = faces.Select(p.FaceCode).Where(f => f >= 0).ToHashSet();
        tris = [];
        var nodes = new Dictionary<int, int>();
        for (int i = 0; i < p.FaceOfBoundary.Length; i++)
            if (set.Contains(p.FaceOfBoundary[i]))
            {
                tris.Add(i);
                for (int k = 0; k < 6; k++) nodes[p.Mesh.Boundary[6 * i + k]] = 1;
            }
        return nodes;
    }

    static Vec3 NodePos(FemMesh m, int n) => new(m.Nodes[3 * n], m.Nodes[3 * n + 1], m.Nodes[3 * n + 2]);

    public sealed record Boundary(int[] FixNode, int[] FixMask, int[] TracTri, double[] TracVec, double[]? Gravity);

    /// <summary>Fixtures and loads of a study as nodal / per-triangle data on a prepared mesh.</summary>
    public static Boundary Conditions(Prepared p, StudyDef st, List<string> notes)
    {
        var m = p.Mesh;
        // ---- fixtures
        var mask = new Dictionary<int, int>();
        foreach (var fx in st.Fixtures)
        {
            var nodes = NodesOn(p, fx.Faces, out var tris);
            if (nodes.Count == 0) { notes.Add($"{fx.Name}: no face of this body is selected."); continue; }
            int bits = fx.Kind switch { FixtureKind.Fixed => 7, FixtureKind.Directional => (fx.LockX ? 1 : 0) | (fx.LockY ? 2 : 0) | (fx.LockZ ? 4 : 0), _ => 0 };
            if (fx.Kind == FixtureKind.Slider)
            {
                var n = tris.Aggregate(Vec3.Zero, (a, t) => a + p.BoundaryNormal[t] * p.BoundaryArea[t]).Normalized();
                int axis = Math.Abs(n.X) >= Math.Abs(n.Y) && Math.Abs(n.X) >= Math.Abs(n.Z) ? 0 : Math.Abs(n.Y) >= Math.Abs(n.Z) ? 1 : 2;
                double comp = axis == 0 ? Math.Abs(n.X) : axis == 1 ? Math.Abs(n.Y) : Math.Abs(n.Z);
                bits = 1 << axis;
                if (comp < 0.98) notes.Add($"{fx.Name}: the face is not aligned with a global axis, so only the closest axis is locked.");
            }
            foreach (var nid in nodes.Keys) mask[nid] = mask.GetValueOrDefault(nid) | bits;
        }

        // ---- loads
        var traction = new Dictionary<int, Vec3>();
        Vec3? gravity = null;
        foreach (var ld in st.Loads)
        {
            if (ld.Kind == LoadKind.Gravity) { gravity = (gravity ?? Vec3.Zero) + ld.Direction.Normalized() * (9810.0 * ld.Value); continue; }
            NodesOn(p, ld.Faces, out var tris);
            if (tris.Count == 0) { notes.Add($"{ld.Name}: no face of this body is selected."); continue; }
            double areaSum = tris.Sum(t => p.BoundaryArea[t]);
            switch (ld.Kind)
            {
                case LoadKind.Pressure:
                    foreach (int t in tris) traction[t] = traction.GetValueOrDefault(t) - p.BoundaryNormal[t] * ld.Value;
                    break;
                case LoadKind.Force:
                {
                    double per = ld.Total ? ld.Value / areaSum : ld.Value / (areaSum / Math.Max(ld.Faces.Count, 1));
                    foreach (int t in tris)
                    {
                        var dir = ld.AlongNormal ? -p.BoundaryNormal[t] : ld.Direction.Normalized();
                        traction[t] = traction.GetValueOrDefault(t) + dir * per;
                    }
                    break;
                }
                case LoadKind.Torque:
                {
                    var axis = tris.Aggregate(Vec3.Zero, (a, t) => a + p.BoundaryNormal[t] * p.BoundaryArea[t]).Normalized();
                    Vec3 Cen(int t) => (NodePos(m, m.Boundary[6 * t]) + NodePos(m, m.Boundary[6 * t + 1]) + NodePos(m, m.Boundary[6 * t + 2])) / 3;
                    var center = tris.Aggregate(Vec3.Zero, (a, t) => a + Cen(t) * p.BoundaryArea[t]) / areaSum;
                    double j = tris.Sum(t => { var r = Cen(t) - center; r -= axis * Vec3.Dot(r, axis); return r.Length * r.Length * p.BoundaryArea[t]; });
                    if (j < 1e-12) { notes.Add($"{ld.Name}: the face has no extent to twist."); break; }
                    double k = ld.Value / j;
                    foreach (int t in tris) { var r = Cen(t) - center; r -= axis * Vec3.Dot(r, axis); traction[t] = traction.GetValueOrDefault(t) + Vec3.Cross(axis, r) * k; }
                    break;
                }
            }
        }

        var fixNode = mask.Keys.OrderBy(k => k).ToArray(); var fixMask = fixNode.Select(k => mask[k]).ToArray();
        var tt = traction.Keys.OrderBy(k => k).ToArray(); var tv = new double[tt.Length * 3];
        for (int i = 0; i < tt.Length; i++) { var v = traction[tt[i]]; tv[3 * i] = v.X; tv[3 * i + 1] = v.Y; tv[3 * i + 2] = v.Z; }
        double[]? g = gravity is { } gv ? [gv.X, gv.Y, gv.Z] : null;
        if (fixNode.Length == 0) throw new InvalidOperationException("The fixtures are not on any face of the body.");
        if (tt.Length == 0 && g == null) throw new InvalidOperationException("The loads are not on any face of the body.");

        return new Boundary(fixNode, fixMask, tt, tv, g);
    }

    /// <summary>Several bodies with their own materials, bonded where they touch.</summary>
    static StudyResults RunBodies(CadSession s, StudyDef st, SceneObject source, Prepared? prepared, Action<string>? progress)
    {
        var sw = Stopwatch.StartNew();
        var others = st.OtherBodies.Select(id => s.Find(id) ?? throw new InvalidOperationException("A body in this study no longer exists.")).ToArray();
        var all = new[] { source }.Concat(others).ToArray();
        foreach (var body in all)
        {
            var material = body.Material ?? throw new InvalidOperationException($"Assign a material to {body.Name} before running this study.");
            MaterialRules.Structural(material);
        }
        if (st.Fixtures.Count == 0) throw new InvalidOperationException("Add at least one fixture before solving.");
        if (st.Loads.Count == 0) throw new InvalidOperationException("Add at least one load before solving.");
        var refs = st.Fixtures.SelectMany(f => f.Faces).Concat(st.Loads.SelectMany(l => l.Faces)).ToList();
        StudySetup.Validate(source, st.ShapeSignature, refs.Where(f => f.Obj == source.Id).Select(f => f.Face).ToArray());
        foreach (var body in others) StudySetup.Validate(body, null, refs.Where(f => f.Obj == body.Id).Select(f => f.Face).ToArray());
        if (!System.IO.File.Exists(CaeSolver.Executable)) throw new System.IO.FileNotFoundException("Studies of several bodies use CalculiX, which is not installed. Run tools/Install-CaeSolver.ps1 or set PRISM_CCX to ccx.exe.");
        progress?.Invoke("Meshing…");
        var p = prepared ?? Mesh(s, st);
        try
        {
            var notes = new List<string>(); var m = p.Mesh;
            var cond = Conditions(p, st, notes);
            progress?.Invoke($"Solving {m.TetCount:#,0} elements with CalculiX…");
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "CAE", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
            var sol = CaeAssembly.SolveStatic(p, cond, folder);
            var yieldOf = new double[m.NodeCount]; var sf = new double[m.NodeCount];
            foreach (var part in p.Parts!)
                for (int n = part.NodeStart; n < part.NodeStart + part.NodeCount; n++)
                {
                    double y = part.Body.Material!.Yield; yieldOf[n] = y; double v = sol.VonMises[n];
                    sf[n] = !double.IsFinite(y) ? double.NaN : v < 1e-9 ? 1000.0 : Math.Min(y / v, 1000.0);
                }
            int iv = 0, id = 0; double mv = 0, md = 0;
            for (int i = 0; i < m.NodeCount; i++)
            {
                double d = Math.Sqrt(sol.Displacement[3 * i] * sol.Displacement[3 * i] + sol.Displacement[3 * i + 1] * sol.Displacement[3 * i + 1] + sol.Displacement[3 * i + 2] * sol.Displacement[3 * i + 2]);
                if (sol.VonMises[i] > mv) { mv = sol.VonMises[i]; iv = i; }
                if (d > md) { md = d; id = i; }
            }
            var finite = yieldOf.Where(double.IsFinite).ToArray(); var safe = sf.Where(double.IsFinite).ToArray();
            double eq = sol.Applied > 1e-9 ? Math.Abs(sol.Reaction.Length - sol.Applied) / sol.Applied : 0;
            if (eq > 0.02) notes.Add($"The supports react with {sol.Reaction.Length:0.##} N but the loads add up to {sol.Applied:0.##} N ({eq * 100:0.#}% apart): check the setup.");
            notes.Add($"{all.Length} bodies bonded at {p.Bonds.Count} joint{(p.Bonds.Count == 1 ? "" : "s")}: touching faces act as one, with no sliding, gap or separation. Each body's stress is read against its own material.");
            notes.Add($"{m.TetCount:#,0} elements, {m.NodeCount:#,0} nodes (10-node tetrahedra), element size {p.Maxh:0.##} mm. Solved with CalculiX in {sol.SolveSeconds:0.#} s. Files: {folder}");
            notes.Add("Stress peaks right at supports, point-like loads and the edges of a joint can grow with finer meshes (singularities); judge the stress away from them.");
            return new StudyResults
            {
                SourceBody = source, OtherBodies = others, Mesh = m, Solution = sol, BoundaryNormal = p.BoundaryNormal, SafetyFactor = sf, Yield = finite.Length > 0 ? finite.Min() : double.NaN,
                MaxVonMises = mv, MaxDisplacement = md, MaxVonMisesNode = iv, MaxDisplacementNode = id, MinSafety = safe.Length > 0 ? safe.Min() : double.NaN, SolveSeconds = sw.Elapsed.TotalSeconds,
                MaterialName = string.Join(" + ", all.Select(b => b.Material!.Name).Distinct()), Notes = notes,
            };
        }
        catch { if (prepared == null) p.Mesh.Dispose(); throw; }
    }

    public static StudyResults Run(CadSession s, StudyDef st, Prepared? prepared = null, Action<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var source = prepared?.Body ?? s.Find(st.BodyId) ?? throw new InvalidOperationException("The study's body no longer exists.");
        if (st.OtherBodies.Count > 0) return RunBodies(s, st, source, prepared, progress);
        var mat = source.Material ?? throw new InvalidOperationException("Assign a material before running a structural study.");
        MaterialRules.Structural(mat);
        if (st.Fixtures.Count == 0) throw new InvalidOperationException("Add at least one fixture before solving.");
        if (st.Loads.Count == 0) throw new InvalidOperationException("Add at least one load before solving.");
        StudySetup.Validate(source,st.ShapeSignature,st.Fixtures.SelectMany(f=>f.Faces).Concat(st.Loads.SelectMany(l=>l.Faces)).Select(f=>f.Face).ToArray());
        progress?.Invoke("Meshing…");
        var p = prepared ?? Mesh(s, st);
        try
        {
        var m = p.Mesh; var notes = new List<string>();
        if (st.Fixtures.Count == 0) throw new InvalidOperationException("Add at least one fixture (for example Fixed support on a face), or the part is free to float away.");
        if (st.Loads.Count == 0) throw new InvalidOperationException("Add at least one load (force, pressure, torque or gravity).");

        notes.Add($"Material data: {mat.Evidence.Condition} Source: {mat.Evidence.Source}");
        double yield = mat.Yield;
        if (!double.IsFinite(yield)) notes.Add("No yield strength is defined: a yield-based factor of safety is unavailable. Ultimate or bending strength is not substituted.");

        var cond = Conditions(p, st, notes);
        var fixNode = cond.FixNode; var fixMask = cond.FixMask; var tt = cond.TracTri; var tv = cond.TracVec; var g = cond.Gravity;

        progress?.Invoke($"Solving {m.NodeCount * 3:#,0} unknowns…");
        double rho = mat.Density * 1e-9;
        var sol = m.SolveStatic(mat.Youngs * 1000.0, mat.Poisson, rho, fixNode, fixMask, null, [], [], tt, tv, g);

        int iv = 0, id = 0; double mv = 0, md = 0;
        var disp = new double[m.NodeCount];
        for (int i = 0; i < m.NodeCount; i++)
        {
            disp[i] = Math.Sqrt(sol.Displacement[3 * i] * sol.Displacement[3 * i] + sol.Displacement[3 * i + 1] * sol.Displacement[3 * i + 1] + sol.Displacement[3 * i + 2] * sol.Displacement[3 * i + 2]);
            if (sol.VonMises[i] > mv) { mv = sol.VonMises[i]; iv = i; }
            if (disp[i] > md) { md = disp[i]; id = i; }
        }
        var sf = sol.VonMises.Select(v => !double.IsFinite(yield) ? double.NaN : v < 1e-9 ? 1000.0 : Math.Min(yield / v, 1000.0)).ToArray();
        // equilibrium check: applied load vs reaction
        // equilibrium check: the net reaction must balance the net applied force (Applied is the net force the loads add up to)
        double rNet = sol.Reaction.Length;
        double eq = sol.Applied > 1e-9 ? Math.Abs(rNet - sol.Applied) / sol.Applied : 0;
        if (eq > 0.02) notes.Add($"The supports react with {rNet:0.##} N but the loads add up to {sol.Applied:0.##} N ({eq * 100:0.#}% apart): check the setup.");
        notes.Add($"{m.TetCount:#,0} elements, {m.NodeCount:#,0} nodes (10-node tetrahedra), element size {p.Maxh:0.##} mm. Assembly {sol.AssembleSeconds:0.#} s, solve {sol.SolveSeconds:0.#} s" + (sol.Iterations > 0 ? $" ({sol.Iterations:0} iterations)." : "."));
        notes.Add("Stress peaks right at supports and point-like loads can grow with finer meshes (singularities); judge the stress away from them.");
        return new StudyResults
        {
            SourceBody=source, Mesh = m, Solution = sol, BoundaryNormal = p.BoundaryNormal, SafetyFactor = sf, Yield = yield, MaxVonMises = mv, MaxDisplacement = md, MaxVonMisesNode = iv, MaxDisplacementNode = id,
            MinSafety = sf.Min(), SolveSeconds = sw.Elapsed.TotalSeconds, MaterialName = mat.Name, Notes = notes,
        };
        }
        catch { if(prepared==null)p.Mesh.Dispose(); throw; }
    }
}
