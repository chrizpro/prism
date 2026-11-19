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
}
