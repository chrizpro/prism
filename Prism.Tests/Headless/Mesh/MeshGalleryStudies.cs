using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>
/// End-to-end scan workflows behind the Mesh gallery studies: a synthetic scan is imported, faces are picked with Smart patch,
/// measured with Fit Region and rebuilt from Section Sketch. Each study asserts what it recovers. Set PRISM_GALLERY_OUT to a folder
/// to also save the finished documents (gallery/README.md, "Mesh studies").
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshGalleryStudies(Xunit.Abstractions.ITestOutputHelper log)
{
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    void Step(string what) => log.WriteLine($"{_clock.Elapsed.TotalSeconds,7:0.0} s  {what}");
    /// <summary>Turns a CAD body into a scan: even 0.8 mm triangles, every vertex moved along its normal by noise of the given deviation.</summary>
    internal static TriMesh FakeScan(Shape part, double edge, double noise, int seed)
    {
        var raw = MeshOps.Weld(MeshAnalysis.FromMeshData(part.Tessellate(0.005)), 1e-6);
        var mesh = MeshRemesh.Remesh(raw, new MeshRemesh.Options(edge, 6));
        var normals = new Vec3[mesh.VertexCount];
        for (int t = 0; t < mesh.TriCount; t++)
        {
            var (a, b, c) = mesh.Tri(t);
            var n = Vec3.Cross(mesh.P(b) - mesh.P(a), mesh.P(c) - mesh.P(a));
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        var random = new Random(seed);
        var v = (float[])mesh.V.Clone();
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            // Box-Muller normal noise along the vertex normal
            double g = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()) * noise;
            var d = normals[i].Normalized() * g;
            v[3 * i] += (float)d.X; v[3 * i + 1] += (float)d.Y; v[3 * i + 2] += (float)d.Z;
        }
        return new TriMesh(v, mesh.T);
    }

    internal static string WriteObj(TriMesh mesh, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.obj");
        var sb = new StringBuilder();
        for (int i = 0; i < mesh.VertexCount; i++) sb.Append(CultureInfo.InvariantCulture, $"v {mesh.V[3 * i]:0.#####} {mesh.V[3 * i + 1]:0.#####} {mesh.V[3 * i + 2]:0.#####}\n");
        for (int t = 0; t < mesh.TriCount; t++) sb.Append(CultureInfo.InvariantCulture, $"f {mesh.T[3 * t] + 1} {mesh.T[3 * t + 1] + 1} {mesh.T[3 * t + 2] + 1}\n");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>Import Mesh with the given reduction, exactly as the options panel does it.</summary>
    internal static SceneObject Import(CadSession session, string path, double keepPercent)
    {
        var prefs = MeshImportPrefs.File;
        MeshImportPrefs.File = Path.Combine(Path.GetTempPath(), $"prism-prefs-{Guid.NewGuid():N}.json");
        try
        {
            new MeshImportPrefs(Reduce: keepPercent < 100, ReducePercent: keepPercent).Save();
            var command = Begin(session, () => new ImportMeshCommand(path));
            for (int i = 0; i < 800 && command.Error == "Still reading the file."; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
            session.CommitCommand();
            return session.Objects.Last(o => o.Kind == ObjKind.Mesh);
        }
        finally { File.Delete(MeshImportPrefs.File); MeshImportPrefs.File = prefs; }
    }

    /// <summary>Smart patch from the first triangle the filter accepts (a click on that face).</summary>
    static string SmartPatch(CadSession session, SceneObject scan, Func<Vec3, Vec3, bool> at)
    {
        var mesh = scan.Tri!;
        int seed = Enumerable.Range(0, mesh.TriCount).First(t =>
        {
            var (a, b, c) = mesh.Tri(t);
            var n = Vec3.Cross(mesh.P(b) - mesh.P(a), mesh.P(c) - mesh.P(a)).Normalized();
            return at((mesh.P(a) + mesh.P(b) + mesh.P(c)) / 3, n);
        });
        var region = session.RegionFor(scan);
        region.Clear();
        region.Add(region.SmartFit(seed, 0.2, 20, out var what));
        session.RefreshRegion();
        return what;
    }

    static Command Fit(CadSession session, string kind, string create, bool refs = true)
    {
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetChoice(command, "Fit", kind);
        SetChoice(command, "Create", create);
        command.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Add reference")).Value = refs;
        command.Params.OfType<ActionParam>().Single(a => a.Label == "Preview result").Run();
        return command;
    }

    static double Number(string text) => double.Parse(text.Split(' ')[0], CultureInfo.InvariantCulture);

    static SceneObject Section(CadSession session, SceneObject scan, string plane, double offset)
    {
        int before = session.Objects.Count(o => o.Kind == ObjKind.Sketch);
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", scan);
        SetChoice(command, "Plane", plane); SetNumber(command, "Offset from the plane", offset);
        session.CommitCommand();
        Assert.Equal(before + 1, session.Objects.Count(o => o.Kind == ObjKind.Sketch));
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    static void Recolor(CadSession session, SceneObject o, uint color, string? name = null, bool visible = true) =>
        session.Update(o.Id, x => { x = x.WithColor(color).WithVisible(visible); return name == null ? x : x.WithName(name); });

    static void SaveForGallery(CadSession session, string id)
    {
        if (Environment.GetEnvironmentVariable("PRISM_GALLERY_OUT") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        session.Save(Path.Combine(folder, id + ".prism"));
    }

    [AvaloniaFact(DisplayName = "GAL-H201 | Pillow block scan: import with reduction, Smart patch and Fit Region measure it, Section Sketch rebuilds a solid within 0.15 mm")]
    [Trait("TestId", "GAL-H201")]
    public void PillowBlockFromScan()
    {
        // the part the scanner saw: an 80 x 50 x 10 mm base with two 8 mm bolt holes and a 36 mm boss with a 20 mm bore, 40 mm tall
        using var plate = BoxAt(new Vec3(-40, -25, 0), 80, 50, 10);
        using var boss = CylinderAt(new Vec3(0, 0, 0), 18, 40);
        using var body = Occ.Fuse(plate, boss);
        using var bore = CylinderAt(new Vec3(0, 0, -5), 10, 60);
        using var hole1 = CylinderAt(new Vec3(-30, 0, -5), 4, 30);
        using var hole2 = CylinderAt(new Vec3(30, 0, -5), 4, 30);
        using var cut1 = Occ.Cut(body, bore); using var cut2 = Occ.Cut(cut1, hole1); using var part = Occ.Cut(cut2, hole2);
        var fake = FakeScan(part, 0.8, 0.03, 4);
        Step($"fake scan {fake.TriCount} triangles");
        var path = WriteObj(fake, "pillow-block-scan");
        Step("written");
        var session = new CadSession();
        try
        {
            var scan = Import(session, path, 60);
            Recolor(session, scan, 0xFFB8C0CC, "Pillow block scan");
            Step("import");

            // the base's top face: a plane at 10 mm, and a construction plane on it
            Assert.Equal("Plane", SmartPatch(session, scan, (c, n) => n.Z > 0.98 && c.Z > 9.5 && c.X > 22 && c.X < 26));
            var plane = Fit(session, "Plane", "Reference geometry only");
            AssertClose(10, Number(Result(plane, "Point on plane").Split(", ")[2].TrimEnd(')')), 0.02, "top face height");
            Assert.True(Number(Result(plane, "Tilt").Replace("°", "")) < 0.1);
            session.CommitCommand();

            Step("plane fit");
            // the boss and the bore: their diameters, axes and the axis lines
            Assert.StartsWith("Cylinder", SmartPatch(session, scan, (c, n) => Math.Abs(new Vec3(c.X, c.Y, 0).Length - 18) < 0.3 && c.Z is > 20 and < 30));
            var outside = Fit(session, "Cylinder", "Reference geometry only");
            AssertClose(36, Number(Result(outside, "Diameter")), 0.05, "boss diameter");
            session.CommitCommand();
            Assert.StartsWith("Hole", SmartPatch(session, scan, (c, n) => Math.Abs(new Vec3(c.X, c.Y, 0).Length - 10) < 0.3 && c.Z is > 20 and < 30));
            var inside = Fit(session, "Cylinder", "Surface trimmed to the region");
            AssertClose(20, Number(Result(inside, "Diameter")), 0.05, "bore diameter");
            session.CommitCommand();
            var boreWall = session.Objects.Last(o => o.Kind == ObjKind.Surface);
            Step("cylinder fits");
            Recolor(session, boreWall, 0xFFE8A23A, "Fitted bore");

            // rebuild: the base outline from a section at 5 mm, the boss ring from a section at 25 mm
            var baseSketch = Section(session, scan, "XY (horizontal)", 5);
            var extrudeBase = StartCommand(session, baseSketch, () => new ExtrudeCommand());
            SetChoice(extrudeBase, "Extent", "Symmetric"); SetNumber(extrudeBase, "Distance", 10);
            session.CommitCommand();
            var bossSketch = Section(session, scan, "XY (horizontal)", 25);
            var extrudeBoss = StartCommand(session, bossSketch, () => new ExtrudeCommand());
            SetChoice(extrudeBoss, "Extent", "Symmetric"); SetNumber(extrudeBoss, "Distance", 30);
            SetChoice(extrudeBoss, "Operation", "Join");
            session.CommitCommand();
            var solid = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
            AssertValidSolid(solid.Shape!);
            Step("sections+extrudes");
            AssertClose(part.Props().Volume, solid.Shape!.Props().Volume, part.Props().Volume * 0.01, "rebuilt volume");
            Recolor(session, solid, 0xFF2F8F9D, "Rebuilt pillow block");

            var deviation = Begin(session, () => new MeshDeviationCommand());
            Pick(deviation, "Mesh to check", session.Find(scan.Id)!);
            Pick(deviation, "Compare with (CAD body or mesh)", session.Find(solid.Id)!);
            SetNumber(deviation, "Tolerance", 0.15);
            Apply(deviation);
            Assert.True(Number(Result(deviation, "Points within tolerance").TrimEnd('%')) >= 99, Result(deviation, "Points within tolerance"));
            session.CancelCommand();

            Step("deviation");
            // keep the sketches for editing, but show the part beside the scan
            foreach (var sketch in session.Objects.Where(o => o.Kind == ObjKind.Sketch).ToList()) session.Update(sketch.Id, x => x.WithVisible(false));
            session.Update(solid.Id, x => x.WithShape(Occ.Translate(x.Shape!, new Vec3(0, 70, 0))));
            session.Update(boreWall.Id, x => x.WithVisible(false));
            foreach (var reference in session.Objects.Where(o => o.Kind is ObjKind.Plane or ObjKind.Curve && o.Name.StartsWith("Fitted")).ToList())
                session.Update(reference.Id, x => x.WithVisible(false));
            SaveForGallery(session, "pillow-block-scan");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "GAL-H202 | Turned knob scan: fitted cylinder, cone and dome rebuild one closed solid")]
    [Trait("TestId", "GAL-H202")]
    public void TurnedKnobPrimitives()
    {
        // a Ø30 x 10 mm grip, a cone narrowing to Ø16 over 14 mm, and an R8 dome on top
        using var grip = CylinderAt(new Vec3(0, 0, 0), 15, 10);
        using var cone0 = Occ.Cone(15, 8, 14);
        using var cone = Occ.Translate(cone0, new Vec3(0, 0, 10));
        using var dome0 = Occ.Sphere(8);
        using var dome = Occ.Translate(dome0, new Vec3(0, 0, 24));
        using var lower = Occ.Fuse(grip, cone);
        using var part = Occ.Fuse(lower, dome);
        var path = WriteObj(FakeScan(part, 0.6, 0.025, 8), "turned-knob-scan");
        var session = new CadSession();
        try
        {
            var scan = Import(session, path, 100);
            Recolor(session, scan, 0xFFB8C0CC, "Turned knob scan");

            Assert.StartsWith("Cylinder", SmartPatch(session, scan, (c, n) => Math.Abs(new Vec3(c.X, c.Y, 0).Length - 15) < 0.3 && c.Z is > 3 and < 7));
            var grip1 = Fit(session, "Cylinder", "Surface trimmed to the region");
            AssertClose(30, Number(Result(grip1, "Diameter")), 0.05, "grip diameter");
            session.CommitCommand();
            Recolor(session, session.Objects.Last(o => o.Kind == ObjKind.Surface), 0xFF2F8F9D, "Fitted grip");

            SmartPatch(session, scan, (c, n) => c.Z is > 15 and < 19 && c.X > 8);
            var taper = Fit(session, "Cone", "Surface trimmed to the region", refs: false);
            // the cone narrows by 7 mm in radius over 14 mm: a 53.13 degree included angle
            AssertClose(2 * Math.Atan(7.0 / 14) * 180 / Math.PI, Number(Result(taper, "Included angle").Replace("°", "")), 0.3, "cone angle");
            session.CommitCommand();
            Recolor(session, session.Objects.Last(o => o.Kind == ObjKind.Surface), 0xFFE8A23A, "Fitted cone");

            Assert.StartsWith("Sphere", SmartPatch(session, scan, (c, n) => c.Z > 29));
            var cap = Fit(session, "Sphere", "Surface trimmed to the region");
            AssertClose(16, Number(Result(cap, "Diameter")), 0.05, "dome diameter");
            Assert.StartsWith("(0", Result(cap, "Centre").Replace("(-0", "(0"));
            session.CommitCommand();
            Recolor(session, session.Objects.Last(o => o.Kind == ObjKind.Surface), 0xFFC2477A, "Fitted dome");

            session.DropRegion();
            session.Update(scan.Id, x => x.WithTri(x.Tri!.Transformed(p => p + new Vec3(-40, 0, 0))));
            // The fitted patches stop short of the transitions. Extend the measured primitives to
            // their intersections and close the bottom to recover one machinable solid.
            double gripRadius = Number(Result(grip1, "Diameter")) / 2;
            double domeRadius = Number(Result(cap, "Diameter")) / 2;
            double slope = Math.Tan(Number(Result(taper, "Included angle").Replace("°", "")) * Math.PI / 360);
            static double Z(string point) => Number(point.Split(", ")[2].TrimEnd(')'));
            double equator = Z(Result(cap, "Centre"));
            double shoulder = Z(Result(taper, "Apex")) - gripRadius / slope;
            using var gripSolid = CylinderAt(Vec3.Zero, gripRadius, shoulder);
            using var taperSolid = Occ.Translate(Occ.Cone(gripRadius, domeRadius, equator - shoulder), Vec3.UnitZ * shoulder);
            using var domeSolid = Occ.Translate(Occ.Sphere(domeRadius), Vec3.UnitZ * equator);
            using var shank = Occ.Fuse(gripSolid, taperSolid);
            var rebuilt = Occ.Fuse(shank, domeSolid);
            AssertValidSolid(rebuilt);
            AssertClose(part.Props().Volume, rebuilt.Props().Volume, part.Props().Volume * 0.01, "knob volume");
            foreach (var item in session.Objects.Where(o => o.Id != scan.Id).ToArray())
                session.Update(item.Id, o => o.WithVisible(false));
            session.AddObject(rebuilt, "Rebuilt turned knob", ObjKind.Solid, 0xFFA6ABB2);
            SaveForGallery(session, "turned-knob-scan");
            Gallery.IndustrialGalleryStudies.Publish(session, "turned-knob-scan", -12, 22);
        }
        finally { File.Delete(path); }
    }
}
