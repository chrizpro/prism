using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>Fit Region (MSH-P05): exact shapes fitted to a picked mesh region, trimmed surfaces, closed solids and reference geometry.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, Fit Region.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshFitRegionTests
{
    /// <summary>A 60 x 40 x 12 mm plate with a 12 mm hole at (-15, 0), tessellated and remeshed like a scan.</summary>
    internal static TriMesh PlateScan()
    {
        using var plate = BoxAt(new Vec3(-30, -20, 0), 60, 40, 12);
        using var drill = CylinderAt(new Vec3(-15, 0, -5), 6, 30);
        using var part = Occ.Cut(plate, drill);
        var mesh = MeshAnalysis.FromMeshData(part.Tessellate(0.005));
        mesh = MeshOps.Weld(mesh, 1e-6);
        return MeshRemesh.Remesh(mesh, new MeshRemesh.Options(1.0, 6));
    }

    static (CadSession Session, SceneObject Scan) Pick(Func<Vec3, Vec3, bool> seed)
    {
        var session = new CadSession();
        var scan = AddMesh(session, PlateScan(), "Scan");
        var mesh = scan.Tri!;
        int start = -1;
        for (int t = 0; t < mesh.TriCount && start < 0; t++)
        {
            var (a, b, c) = mesh.Tri(t);
            var centre = (mesh.P(a) + mesh.P(b) + mesh.P(c)) / 3;
            var normal = Vec3.Cross(mesh.P(b) - mesh.P(a), mesh.P(c) - mesh.P(a)).Normalized();
            if (seed(centre, normal)) start = t;
        }
        Assert.True(start >= 0, "a seed triangle exists");
        var region = session.RegionFor(scan);
        region.Add(region.SmartFit(start, 0.2, 20, out _));
        session.RefreshRegion();
        return (session, scan);
    }

    static (CadSession, SceneObject) TopFace() => Pick((c, n) => n.Z > 0.99 && c.Z > 11.9 && c.X > 0 && Math.Abs(c.Y) < 5);
    static (CadSession, SceneObject) HoleWall() => Pick((c, n) => Math.Abs(new Vec3(c.X + 15, c.Y, 0).Length - 6) < 0.05 && c.Z is > 4 and < 8);

    static void Run(Command command, string action) => command.Params.OfType<ActionParam>().Single(a => a.Label == action).Run();

    static double Number(string text) => double.Parse(text.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);

    [AvaloniaFact(DisplayName = "MSH-H140 | Fit Region trims a fitted plane around a hole and adds a construction plane")]
    [Trait("TestId", "MSH-H140")]
    public void PlaneIsTrimmedToRegion()
    {
        var (session, _) = TopFace();
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetChoice(command, "Fit", "Plane");
        session.CommitCommand();
        var surface = Assert.Single(session.Objects, o => o.Kind == ObjKind.Surface);
        var area = surface.Shape!.Props().Area;
        // 60 x 40 minus the 12 mm hole is 2286.9 mm²; an untrimmed rectangle would be 2400
        AssertClose(2400 - Math.PI * 36, area, 15, "trimmed plane area");
        var plane = Assert.Single(session.Objects, o => o.Kind == ObjKind.Plane);
        AssertClose(12, plane.SketchPlane!.Value.Origin.Z, 0.01, "construction plane height");
        AssertClose(1, Math.Abs(plane.SketchPlane.Value.Normal.Z), 1e-4, "construction plane normal");
    }

    [AvaloniaFact(DisplayName = "MSH-H141 | Fit Region reports a hole's diameter and axis and builds an uncapped wall plus an axis line")]
    [Trait("TestId", "MSH-H141")]
    public void CylinderReportsAxisWithoutCaps()
    {
        var (session, _) = HoleWall();
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetChoice(command, "Fit", "Cylinder");
        Run(command, "Preview result");
        AssertClose(12, Number(Result(command, "Diameter")), 0.01, "hole diameter");
        AssertClose(-15, Number(Result(command, "Axis point (middle)").TrimStart('(').Replace(",", " ")), 0.05, "axis X in the panel");
        Assert.EndsWith(" 1)", Result(command, "Axis direction"));
        Assert.True(Number(Result(command, "Tilt").Replace("°", "")) < 0.05, string.Join(" ; ", command.Results.Select(r => r.Label + "=" + r.Value)));
        session.CommitCommand();
        var wall = Assert.Single(session.Objects, o => o.Kind == ObjKind.Surface);
        Assert.Equal(1, wall.Shape!.Tessellate(0.05).FaceCount);
        AssertClose(Math.PI * 12 * 12, wall.Shape.Props().Area, 12, "hole wall area without caps");
        var axis = Assert.Single(session.Objects, o => o.Kind == ObjKind.Curve);
        var props = axis.Shape!.Props();
        AssertClose(-15, props.Center.X, 0.05, "axis X");
        AssertClose(0, props.Center.Y, 0.05, "axis Y");
    }

    [AvaloniaTheory(DisplayName = "MSH-H142 | Fit Region can make a closed solid or only reference geometry")]
    [Trait("TestId", "MSH-H142")]
    [InlineData("Closed solid")]
    [InlineData("Reference geometry only")]
    public void CreateChoices(string create)
    {
        var (session, _) = HoleWall();
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetChoice(command, "Fit", "Cylinder");
        SetChoice(command, "Create", create);
        session.CommitCommand();
        bool solid = create == "Closed solid";
        Assert.Equal(solid ? 1 : 0, session.Objects.Count(o => o.Kind == ObjKind.Solid));
        Assert.Equal(0, session.Objects.Count(o => o.Kind == ObjKind.Surface));
        Assert.Single(session.Objects, o => o.Kind == ObjKind.Curve);
    }

    [AvaloniaTheory(DisplayName = "MSH-H143 | Fit Region measures a noisy sphere and cone by least squares on the vertices")]
    [Trait("TestId", "MSH-H143")]
    [InlineData("Sphere")]
    [InlineData("Cone")]
    public void SphereAndCone(string kind)
    {
        // sphere R10 at the origin; cone from R10 at z=0 to R4 at z=12 (half-angle atan(6/12) = 26.565 degrees)
        using var solid = kind == "Sphere" ? Occ.Sphere(10) : Occ.Cone(10, 4, 12);
        var mesh = MeshRemesh.Remesh(MeshOps.Weld(MeshAnalysis.FromMeshData(solid.Tessellate(0.005)), 1e-6), new MeshRemesh.Options(0.8, 6));
        var random = new Random(3);
        var noisy = (float[])mesh.V.Clone();
        for (int i = 0; i < noisy.Length; i++) noisy[i] += (float)((random.NextDouble() - 0.5) * 0.06);
        var session = new CadSession();
        var scan = AddMesh(session, new TriMesh(noisy, mesh.T), "Scan");
        int seed = Enumerable.Range(0, scan.Tri!.TriCount).First(t =>
        {
            var (a, b, c) = scan.Tri.Tri(t);
            var centre = (scan.Tri.P(a) + scan.Tri.P(b) + scan.Tri.P(c)) / 3;
            return kind == "Sphere" ? centre.Z > 5 : centre.Z is > 5 and < 7 && centre.X > 5;
        });
        var region = session.RegionFor(scan);
        region.Add(region.SmartFit(seed, 0.2, 20, out _));
        session.RefreshRegion();
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetChoice(command, "Fit", kind);
        Run(command, "Preview result");
        var rows = string.Join(" ; ", command.Results.Select(r => r.Label + "=" + r.Value));
        Assert.True(command.Results.Any(r => r.Label is "Diameter" or "Included angle"), rows);
        if (kind == "Sphere")
        {
            AssertClose(20, Number(Result(command, "Diameter")), 0.03, "sphere diameter " + rows);
            Assert.StartsWith("(0", Result(command, "Centre").Replace("(-0", "(0"));
        }
        else
            AssertClose(2 * Math.Atan(6.0 / 12) * 180 / Math.PI, Number(Result(command, "Included angle").Replace("°", "")), 0.2, "cone angle " + rows);
    }

    [AvaloniaFact(DisplayName = "MSH-H153 | Double-click a scan triangle in Face mode takes its whole patch, and Fit Region uses that selection with no Edit Region step")]
    [Trait("TestId", "MSH-H153")]
    public void FaceModeSelectionFeedsFitRegion()
    {
        var s = new CadSession();
        using var box = BoxAt(new Vec3(0, 0, 0), 30, 20, 12);
        var mesh = s.AddObject(null, "Scan", ObjKind.Mesh);
        var raw = MeshOps.Weld(MeshAnalysis.FromMeshData(box.Tessellate()), 1e-6);
        s.Update(mesh.Id, o => o.WithTri(MeshRemesh.Remesh(raw, new MeshRemesh.Options(2, 3))), checkpoint: false);   // scan-like: even 2 mm triangles
        s.ZoomExtents();
        s.Mode = SelMode.Face;
        var at = s.Camera.Project(new Vec3(15, 10, 12))!.Value;
        s.PointerDown(new PointerInfo(at.x, at.y, true, false, false, false, 2));
        s.PointerUp(new PointerInfo(at.x, at.y, false, false, false, false, 2));
        var picked = s.Selection.Where(i => i.Kind == SelKind.MeshFace).ToList();
        Assert.True(picked.Count > 50, $"the whole top patch, got {picked.Count} triangles");
        var tri = s.Find(mesh.Id)!.Tri!;
        Assert.All(picked, i => { for (int c = 0; c < 3; c++) Assert.Equal(12, tri.P(tri.T[3 * i.Index + c]).Z, 4); });
        s.StartCommand(() => new MeshFitSurfaceCommand());
        var command = s.ActiveCommand!;
        SetChoice(command, "Fit", "Plane");
        Assert.Equal(picked.Count, s.RegionSel!.Tris.Count);   // the Face-mode pick became the region the tool reads
        s.CommitCommand();
        Assert.True(s.Objects.Any(o => o.Kind == ObjKind.Plane), $"status: {s.Status} | active {s.ActiveCommand?.Title} | slots {string.Join(";", command.Slots.Select(x => x.Label + "=" + x.Items.Count + (x.Optional ? "(opt)" : "") + (x.IsVisible ? "" : "(hidden)")))} | objects: {string.Join(",", s.Objects.Select(o => o.Kind + " " + o.Name))}");
        var plane = Assert.Single(s.Objects, o => o.Kind == ObjKind.Plane);
        AssertClose(12, plane.SketchPlane!.Value.Origin.Z, 0.01, "plane fitted to the double-clicked patch");
    }

    [AvaloniaFact(DisplayName = "MSH-H154 | A flat face of a CAD export (two triangles) fits a plane, and with Fit Region open its triangles can be added, removed and cleared by clicking")]
    [Trait("TestId", "MSH-H154")]
    public void CoarseFaceFitsAndSelectionEditsWhileOpen()
    {
        var s = new CadSession();
        using var box = BoxAt(new Vec3(0, 0, 0), 30, 20, 12);
        var mesh = s.AddObject(null, "Export", ObjKind.Mesh);
        s.Update(mesh.Id, o => o.WithTri(MeshOps.Weld(MeshAnalysis.FromMeshData(box.Tessellate()), 1e-6)), checkpoint: false);   // two triangles per side, as a CAD export
        s.ZoomExtents(); s.Mode = SelMode.Face;
        var top = s.Camera.Project(new Vec3(15, 10, 12))!.Value;
        void Click(double x, double y, bool ctrl = false, int clicks = 1) { s.PointerDown(new PointerInfo(x, y, true, false, ctrl, false, clicks)); s.PointerUp(new PointerInfo(x, y, false, false, ctrl, false, clicks)); }
        Click(top.x, top.y, clicks: 2);
        Assert.Equal(2, s.Selection.Count(i => i.Kind == SelKind.MeshFace));
        s.StartCommand(() => new MeshFitSurfaceCommand());
        Assert.Equal(2, s.RegionSel!.Tris.Count);
        SetChoice(s.ActiveCommand!, "Fit", "Plane");
        // with the tool open, a Ctrl-click takes one triangle out and a click on empty space clears the region
        Click(top.x, top.y, ctrl: true);
        Assert.Equal(1, s.RegionSel!.Tris.Count);
        Click(5, 5);
        Assert.True(s.RegionSel == null || s.RegionSel.IsEmpty);
        Click(top.x, top.y, clicks: 2);
        Assert.Equal(2, s.RegionSel!.Tris.Count);
        s.CommitCommand();
        var plane = Assert.Single(s.Objects, o => o.Kind == ObjKind.Plane);
        AssertClose(12, plane.SketchPlane!.Value.Origin.Z, 0.01, "plane fitted to the two-triangle face");
        // the construction plane covers the 30 x 20 face with a small margin, not a square well past it
        var sides = Enumerable.Range(0, plane.Shape.Count(SubKind.Edge)).Select(e => { using var ed = plane.Shape.Sub(SubKind.Edge, e); var i = Occ.EdgeInfoOf(ed); return (i.End - i.Start).Length; }).OrderBy(l => l).ToList();
        Assert.InRange(sides[^1], 30, 34);
        Assert.InRange(sides[0], 20, 24);
        s.ClearSelection();
        Assert.True(s.RegionSel == null || s.RegionSel.IsEmpty, "clearing the selection clears the highlighted region");
    }
}
