using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh conversion and repair guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public class MeshConsolidationTests
{
    static TriMesh FlatMesh(bool bump = false)
    {
        float[] vertices = [0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0, 5, 5, bump ? 1 : 0];
        return new TriMesh(vertices, [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4]);
    }

    [AvaloniaFact(DisplayName = "MSH-H121 | Solid conversion previews an open CAD surface and requires consent to keep it")]
    [Trait("TestId", "MSH-H121")]
    public void SolidRequiresConsentToKeepOpenSurface()
    {
        var session = new CadSession();
        AddMesh(session, MeshOps.Subdivide(FlatMesh(), 2));
        var command = Begin(session, () => new ConvertMeshCommand());
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Preview result").Run();
        Assert.Equal("Open CAD surface", Result(command, "Output"));
        Assert.NotEmpty(session.Preview);
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        Assert.NotEmpty(overlay.Lines);
        session.CommitCommand();
        Assert.Same(command, session.ActiveCommand);
        Assert.Single(session.Objects);
        command.Params.OfType<BoolParam>().Single(p => p.Label == "Keep open result as surface").Value = true;
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Single(session.Objects, obj => obj.Kind == ObjKind.Surface);
    }

    [AvaloniaTheory(DisplayName = "MSH-H122 | Selected scan region produces the chosen surface output")]
    [Trait("TestId", "MSH-H122")]
    [InlineData("Surface", "Fitted CAD surface")]
    [InlineData("NURBS", "NURBS patch")]
    public void ConvertsSelectedRegionToSurfaceOrNurbs(string type, string report)
    {
        var session = new CadSession();
        var scan = AddMesh(session, MeshOps.Subdivide(FlatMesh(), 2));
        var command = Begin(session, () => new ConvertMeshCommand());
        SetChoice(command, "Output type", type);
        SetChoice(command, "Source", "Selected region");
        Assert.Null(command.TryBuild());
        Assert.Contains(command.Results, row => row.Label == "Select a region first");
        session.RegionFor(scan).All();
        var surface = Assert.Single(Apply(command).Add);
        Assert.Equal(ObjKind.Surface, surface.Kind);
        Assert.True(surface.Shape!.IsValid);
        Assert.Equal(report, Result(command, "Output"));
        Assert.Contains("within tolerance", Result(command, "Scan to result"));
        Assert.Contains("within tolerance", Result(command, "Result to scan"));
    }

    [AvaloniaFact(DisplayName = "MSH-H123 | Selected spherical region reports the new solid extending beyond the scan")]
    [Trait("TestId", "MSH-H123")]
    public void PrimitiveReportsExtrapolation()
    {
        var session = new CadSession();
        var mesh = SphereMesh(10);
        var scan = AddMesh(session, mesh);
        session.RegionFor(scan).Add(Enumerable.Range(0, mesh.TriCount).Where(i => Enumerable.Range(0, 3).All(corner => mesh.P(mesh.T[3 * i + corner]).Z > 0.5)));
        var command = Begin(session, () => new ConvertMeshCommand());
        SetChoice(command, "Source", "Selected region");
        SetChoice(command, "Primitive", "Sphere");
        var solid = Assert.Single(Apply(command).Add);
        Assert.Equal(ObjKind.Solid, solid.Kind);
        Assert.Equal("0", Result(command, "Open edges"));
        Assert.Contains("100% within tolerance", Result(command, "Scan to result"));
        Assert.DoesNotContain("100% within tolerance", Result(command, "Result to scan"));
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        Assert.NotEmpty(overlay.Dots);
    }

    [AvaloniaFact(DisplayName = "MSH-H127 | Whole mesh conversion creates a valid solid with the fitted volume")]
    [Trait("TestId", "MSH-H127")]
    public void ConvertsDenseBoxToSolid()
    {
        var session = new CadSession();
        AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 10, 20, 30), 3));
        var command = Begin(session, () => new ConvertMeshCommand());
        var body = Assert.Single(Apply(command).Add);
        Assert.Equal(ObjKind.Solid, body.Kind);
        Assert.True(body.Shape!.IsValid);
        AssertClose(6000, body.Shape.Props().Volume, 1, "Volume (mm³)");
        Assert.Equal("0", Result(command, "Open edges"));
    }

    [AvaloniaFact(DisplayName = "MSH-H124 | Freeform conversion keeps the scan and adds an editable cage")]
    [Trait("TestId", "MSH-H124")]
    public void WrapsScanAsEditableFreeform()
    {
        var session = new CadSession();
        var scan = AddMesh(session, SphereMesh(10));
        var command = Begin(session, () => new ConvertMeshCommand());
        SetChoice(command, "Output type", "Freeform");
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.NotNull(session.Find(scan.Id));
        Assert.NotNull(Assert.Single(session.Objects, obj => obj.Kind == ObjKind.Form).Form);
    }

    [AvaloniaFact(DisplayName = "MSH-H125 | Remesh reduces a subdivided mesh and can retain the original")]
    [Trait("TestId", "MSH-H125")]
    public void RemeshCanReduceFaces()
    {
        var session = new CadSession();
        var source = AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 10, 10, 10), 2));
        int originalCount = source.Tri!.TriCount;
        var command = Begin(session, () => new MeshRemeshCommand());
        SetChoice(command, "Mode", "Reduce Faces");
        SetChoice(command, "Reduce to", "A triangle count");
        SetInteger(command, "Triangles", originalCount / 2);
        command.Params.OfType<BoolParam>().Single(p => p.Label == "Keep the original too").Value = true;
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Equal(originalCount, session.Find(source.Id)!.Tri!.TriCount);
        Assert.InRange(Assert.Single(session.Objects, obj => obj.Id != source.Id).Tri!.TriCount, 4, originalCount / 2);
    }

    [AvaloniaFact(DisplayName = "MSH-H126 | Edit Region smooths selected interior vertices and keeps the surrounding mesh fixed")]
    [Trait("TestId", "MSH-H126")]
    public void SmoothRegionAnchorsUnselectedGeometry()
    {
        var session = new CadSession();
        var flat = FlatMesh(bump: true);
        var source = new TriMesh([.. flat.V, 5, -5, 0], [.. flat.T, 1, 0, 5]);
        var scan = AddMesh(session, source);
        session.RegionFor(scan).Add([0, 1, 2, 3]);
        var command = Begin(session, () => new MeshRegionCommand());
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Smooth selected triangles").Run();
        var result = session.Find(scan.Id)!.Tri!;
        Assert.InRange(result.P(4).Z, 0, .99);
        for (int i = 0; i < 4; i++) Assert.Equal(source.P(i), result.P(i));
        Assert.Equal(source.P(5), result.P(5));
        Assert.Equal(source.T, result.T);
    }
}
