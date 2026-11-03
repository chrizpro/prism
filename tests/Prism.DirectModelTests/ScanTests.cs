using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Prism.ViewModels;

/// <summary>From a mesh to something you can work with: delete and patch, a sketch from any plane, and a noisy scan rebuilt and direct-edited.</summary>
static class ScanTests
{
    static TriMesh Globe(double r, int rings, int segments)
    {
        var v = new List<float> { 0, 0, (float)r };
        for (int i = 1; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                double a = Math.PI * i / rings, b = 2 * Math.PI * j / segments;
                v.AddRange([(float)(r * Math.Sin(a) * Math.Cos(b)), (float)(r * Math.Sin(a) * Math.Sin(b)), (float)(r * Math.Cos(a))]);
            }
        int south = v.Count / 3; v.AddRange([0, 0, (float)-r]);
        int At(int i, int j) => 1 + (i - 1) * segments + (j + segments) % segments;
        var t = new List<int>();
        for (int j = 0; j < segments; j++)
        {
            t.AddRange([0, At(1, j), At(1, j + 1)]);
            for (int i = 1; i < rings - 1; i++) t.AddRange([At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j), At(i + 1, j + 1), At(i, j + 1)]);
            t.AddRange([south, At(rings - 1, j + 1), At(rings - 1, j)]);
        }
        return new TriMesh([.. v], [.. t]);
    }

    static HashSet<int> Near(TriMesh m, Vec3 at, double radius)
    {
        var res = new HashSet<int>();
        for (int t = 0; t < m.TriCount; t++)
        {
            var (a, b, c) = m.Tri(t);
            if (((m.P(a) + m.P(b) + m.P(c)) / 3 - at).Length < radius) res.Add(t);
        }
        return res;
    }

    static double WorstOffSphere(TriMesh m, double r)
    {
        double worst = 0;
        for (int t = 0; t < m.TriCount; t++) { var (a, b, c) = m.Tri(t); worst = Math.Max(worst, Math.Abs(((m.P(a) + m.P(b) + m.P(c)) / 3).Length - r)); }
        return worst;
    }

    static SceneObject AddMesh(CadSession s, TriMesh m, string name)
    {
        var o = new CommandOutput { Label = name, Icon = "mesh" };
        o.Add.Add(new(null, name, ObjKind.Mesh, null, null, null, null, m));
        s.Apply(o);
        return s.Objects.Last(x => x.Kind == ObjKind.Mesh);
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- Delete and Patch
        var globe = Globe(50, 40, 60);
        check(globe.Topology().HoleLoops.Count == 0, "the test globe starts closed");
        var spot = Near(globe, new Vec3(50, 0, 0), 22);
        var smooth = MeshPatch.DeleteAndPatch(globe, spot, true);
        var flat = MeshPatch.DeleteAndPatch(globe, spot, false);
        double smoothMiss = WorstOffSphere(smooth.Mesh, 50), flatMiss = WorstOffSphere(flat.Mesh, 50);
        check(smooth.Openings == 1 && smooth.Mesh.Topology().HoleLoops.Count == 0, "Delete and Patch leaves the mesh closed");
        check(flatMiss > 3, $"a flat patch cuts straight across the opening ({flatMiss:0.##} mm off the sphere)");
        check(smoothMiss < flatMiss * 0.25, $"a smooth patch follows the sphere ({smoothMiss:0.##} mm off, against {flatMiss:0.##} mm flat)");
        check(smooth.NewTriangles > flat.NewTriangles * 2, $"and gets new points inside at the mesh's own size ({smooth.NewTriangles} triangles, {flat.NewTriangles} flat)");
        check(Enumerable.Range(0, smooth.Mesh.TriCount).All(t => { var (a, b, c) = smooth.Mesh.Tri(t); return Vec3.Dot(Vec3.Cross(smooth.Mesh.P(b) - smooth.Mesh.P(a), smooth.Mesh.P(c) - smooth.Mesh.P(a)), smooth.Mesh.P(a)) > 0; }), "with every triangle still facing outward");

        var withHole = MeshPatch.DeleteAndPatch(globe, Near(globe, new Vec3(-50, 0, 0), 12), true).Mesh;
        var kept = new HashSet<int>(); kept.UnionWith(Near(withHole, new Vec3(0, 50, 0), 8));
        var open = new TriMesh(withHole.V, Enumerable.Range(0, withHole.TriCount).Where(t => !kept.Contains(t)).SelectMany(t => new[] { withHole.T[3 * t], withHole.T[3 * t + 1], withHole.T[3 * t + 2] }).ToArray());
        var one = MeshPatch.DeleteAndPatch(open, Near(open, new Vec3(50, 0, 0), 15), true);
        check(one.Openings == 1 && one.Mesh.Topology().HoleLoops.Count == 1, "a hole somewhere else in the mesh stays open");
        var two = MeshPatch.DeleteAndPatch(globe, new HashSet<int>(Near(globe, new Vec3(50, 0, 0), 10).Concat(Near(globe, new Vec3(-50, 0, 0), 10))), true);
        check(two.Openings == 2 && two.Mesh.Topology().HoleLoops.Count == 0, "two selected areas are two patches");

        var vm = new MainViewModel(); var s = vm.Session;
        var meshObj = AddMesh(s, globe, "Scan");
        s.StartCommand(() => new MeshRegionCommand());
        var region = (MeshRegionCommand)s.ActiveCommand!;
        region.Sel()!.Add(Near(globe, new Vec3(50, 0, 0), 14));
        region.Params.OfType<ActionParam>().First(a => a.Label == "Delete and patch").Run!();
        var after = s.Find(meshObj.Id)!.Tri!;
        check(after.TriCount != globe.TriCount && after.Topology().HoleLoops.Count == 0 && s.Status.Contains("patched 1 opening"), "Delete and patch works from Select Region: " + s.Status);
        s.CancelCommand();
        foreach (var name in new[] { "Mesh", "Reverse Engineer" })
            check(vm.Tabs.Single(t => t.Title == name).Buttons.Any(b => b.Label.Replace("\n", " ") == "Select Region"), $"{name} has Select Region");

        // ---- Section Sketch
        var plate = Occ.Cut(Occ.Box(60, 40, 10), Occ.Translate(Occ.Cylinder(6, 30), new Vec3(0, 0, -10)));
        var plateMesh = MeshOps.Weld(MeshAnalysis.FromMeshData(plate.Tessellate(0.02)), 1e-4);
        var p2 = new MainViewModel().Session;
        var scan = AddMesh(p2, plateMesh, "Plate scan");
        p2.StartCommand(() => new MeshSectionSketchCommand());
        var sk = (MeshSectionSketchCommand)p2.ActiveCommand!;
        sk.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Offset")).Value = 5;
        sk.OnChanged();
        p2.CommitCommand();
        var sketch = p2.Objects.Single(o => o.Kind == ObjKind.Sketch);
        var ents = sketch.SketchData!.Entities;
        check(ents.Count(e => e.Type == Prism.Core.Sketching.EntType.Circle) == 1 && ents.Count(e => e.Type == Prism.Core.Sketching.EntType.Line) == 4, $"Section Sketch makes a real sketch: 4 lines and a circle ({string.Join(", ", ents.GroupBy(e => e.Type).Select(g => g.Count() + " " + g.Key))})");
        check(Math.Abs(ents.Single(e => e.Type == Prism.Core.Sketching.EntType.Circle).R - 6) < 0.05, "the hole's radius comes through");
        p2.StartCommand(() => new ExtrudeCommand());
        var ex = p2.ActiveCommand!;
        ex.Slots[0].Items.Clear(); ex.Slots[0].TryAdd(SelItem.Of(p2.Find(sketch.Id)!));
        ex.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Distance") || p.Label.StartsWith("Depth")).Value = 5;
        ex.OnChanged(); p2.CommitCommand();
        var built = p2.Objects.LastOrDefault(o => o.Kind == ObjKind.Solid);
        double want = (60 * 40 - Math.PI * 36) * 5;
        check(built != null && Math.Abs(built.Shape.Props().Volume - want) < want * 0.01, $"and extrudes straight into a solid with the hole ({built?.Shape.Props().Volume:0} mm³, {want:0} mm³ expected)");

        // the plane fitted to a selected area, on a tilted part
        var tilted = MeshOps.Weld(MeshAnalysis.FromMeshData(Occ.Rotate(Occ.Box(60, 40, 10), Vec3.Zero, Vec3.UnitY, 30).Tessellate(0.02)), 1e-4);
        var dense = MeshRemesh.Remesh(tilted, new MeshRemesh.Options(2));
        var p3 = new MainViewModel().Session;
        AddMesh(p3, dense, "Tilted");
        p3.StartCommand(() => new MeshRegionCommand());
        var pick = (MeshRegionCommand)p3.ActiveCommand!;
        var up = new Vec3(Math.Sin(30 * Math.PI / 180), 0, Math.Cos(30 * Math.PI / 180));
        pick.Sel()!.Add(Enumerable.Range(0, dense.TriCount).Where(t => { var (a, b, c) = dense.Tri(t); return Vec3.Dot(Vec3.Cross(dense.P(b) - dense.P(a), dense.P(c) - dense.P(a)).Normalized(), up) > 0.99; }));
        p3.CommitCommand();
        check(p3.RegionSel is { IsEmpty: false }, "the selected region stays after Select Region closes");
        p3.StartCommand(() => new MeshSectionSketchCommand());
        var fit = (MeshSectionSketchCommand)p3.ActiveCommand!;
        fit.Params.OfType<ChoiceParam>().First(p => p.Label == "Plane").Index = 4;
        fit.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Offset")).Value = -5;
        fit.OnChanged();
        p3.CommitCommand();
        var slanted = p3.Objects.Single(o => o.Kind == ObjKind.Sketch);
        check(slanted.SketchData!.Entities.Count(e => e.Type == Prism.Core.Sketching.EntType.Line) == 4 && Vec3.Dot(slanted.SketchPlane!.Value.Normal, up) is > 0.99 or < -0.99, "a plane fitted to the selected area slices a tilted part square");

        // ---- a noisy scan, rebuilt, then direct edited
        var drilled = Occ.Cut(Occ.Box(60, 40, 10), Occ.Translate(Occ.Cylinder(6, 30), new Vec3(10, 5, -10)));
        var bracket = Occ.Cut(Occ.Fuse(Occ.Box(60, 40, 8), Occ.Translate(Occ.Box(8, 40, 30), new Vec3(-26, 0, 0))), Occ.Translate(Occ.Cylinder(5, 30), new Vec3(10, 0, -10)));
        var bossed = Occ.Fuse(Occ.Box(60, 40, 8), Occ.Translate(Occ.Cylinder(10, 15), new Vec3(0, 0, 4)));
        // the largest upward face, pulled 2 mm with the Direct Edit gizmo: the volume it adds, or null when the pull fails
        static double? PullTop(MainViewModel view, SceneObject body)
        {
            var s = view.Session;
            view.EnterDirectEdit();
            var gizmo = (DirectEditCommand)s.ActiveCommand!;
            int count = body.Shape.Count(SubKind.Face), best = -1; double area = 0;
            for (int i = 0; i < count; i++)
            {
                var info = Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i));
                if (info.Type == "Plane" && info.Area > area && s.FaceFrame(new SelItem(body.Id, SelKind.Face, i)) is { } fr && fr.normal.Z > 0.99) { best = i; area = info.Area; }
            }
            gizmo.Pick(new SelItem(body.Id, SelKind.Face, best), false);
            gizmo.Params.OfType<NumberParam>().Single(p => p.Label.StartsWith("Pull")).Value = 2;
            double? added = gizmo.TryBuild() is { } done ? done.Replace.Single().New.Props().Volume - body.Shape.Props().Volume : null;
            view.ExitDirectEdit();
            return added;
        }

        foreach (var (name, cad, faces) in new[] { ("plate", drilled, 7), ("bracket", bracket, 9), ("boss", bossed, 8) })
        {
            var baseVm = new MainViewModel();
            double baseline = PullTop(baseVm, baseVm.Session.AddObject(cad, "Original", ObjKind.Solid)) ?? double.NaN;
            var clean = MeshRemesh.Remesh(MeshOps.Weld(MeshAnalysis.FromMeshData(cad.Tessellate(0.02)), 1e-4), new MeshRemesh.Options(1.5));
            foreach (double noise in new[] { 0.0, 0.05, 0.1, 0.2 })
            {
                var rnd = new Random(7);
                var noisy = new TriMesh(clean.V.Select(f => f + (float)((rnd.NextDouble() - 0.5) * 2 * noise)).ToArray(), clean.T);
                var p4vm = new MainViewModel(); var p4 = p4vm.Session;
                AddMesh(p4, noisy, "Scan");
                p4.StartCommand(() => new MeshRebuildCommand());
                var re = p4.ActiveCommand!;
                re.Params.OfType<ChoiceParam>().First(p => p.Label == "Create").Index = 3;
                re.OnChanged();
                p4.CommitCommand();
                var solid = p4.Objects.FirstOrDefault(o => o.Kind == ObjKind.Solid);
                check(solid != null && Occ.Check(solid.Shape).Valid && solid.Shape.Count(SubKind.Face) == faces, $"{name} scan with ±{noise} mm noise rebuilds to a valid solid with its {faces} faces");
                double part = cad.Props().Volume, off = Math.Abs(solid!.Shape.Props().Volume - part) / part * 100;
                check(off < 0.3, $"and its volume is {off:0.00}% off the original");
                double pulled = PullTop(p4vm, solid) ?? double.NaN;
                check(Math.Abs(pulled - baseline) < Math.Abs(baseline) * 0.02, $"Direct Edit pulls its top face as on the CAD original ({pulled:0} mm³ against {baseline:0} mm³)");
            }
        }
    }
}