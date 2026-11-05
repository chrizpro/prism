using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Freeform;

/// <summary>
/// Characterizes the Freeform primitives (Plane, Box, Sphere, Cylinder, Torus) and the cage inspection tools (Check Cage,
/// Freeform Quality). Cage counts and extents come from the primitive options; inspection reports topology and poles.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Living guide.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class FormPrimitiveTests
{
    static SubdMesh Cage(Command command)
    {
        var added = Assert.Single(BuildOutput(command).Add);
        Assert.Equal(ObjKind.Form, added.Kind);
        return Assert.IsType<SubdMesh>(added.Form);
    }

    static (Vec3 Min, Vec3 Max) Bounds(SubdMesh cage) =>
        (new(cage.V.Min(v => v.X), cage.V.Min(v => v.Y), cage.V.Min(v => v.Z)),
         new(cage.V.Max(v => v.X), cage.V.Max(v => v.Y), cage.V.Max(v => v.Z)));

    static SceneObject AddForm(CadSession session, Command command)
    {
        session.CommitCommand();
        return session.Objects.Last(o => o.Kind == ObjKind.Form);
    }

    // ------------------------------------------------------------------ primitives

    [AvaloniaTheory(DisplayName = "FRM-H101 | Freeform Plane lies in the chosen plane with the requested faces")]
    [InlineData("XY (ground)", 2)]
    [InlineData("XZ (front)", 1)]
    [InlineData("YZ (side)", 0)]
    [Trait("TestId", "FRM-H101")]
    public void PlaneOrientation(string plane, int flatAxis)
    {
        var session = new CadSession();
        var command = Begin(session, () => new FormPlaneCommand());
        SetChoice(command, "Plane", plane);
        SetNumber(command, "Width", 40);
        SetNumber(command, "Length", 20);
        SetInteger(command, "Faces across", 4);
        SetInteger(command, "Faces along", 2);
        var cage = Cage(command);
        Assert.Equal(8, cage.F.Count);
        Assert.Equal(15, cage.V.Count);
        var (min, max) = Bounds(cage);
        var size = max - min;
        double[] extent = [size.X, size.Y, size.Z];
        AssertNear(0, extent[flatAxis], "Flat axis extent (mm)");
        Assert.Equal([20.0, 40.0], extent.Where((_, i) => i != flatAxis).Order().Select(v => Math.Round(v, 6)));
    }

    [AvaloniaFact(DisplayName = "FRM-H102 | Freeform Box has the requested size, faces per side and position")]
    [Trait("TestId", "FRM-H102")]
    public void BoxSizeAndPosition()
    {
        var session = new CadSession();
        var command = Begin(session, () => new FormBoxCommand());
        SetNumber(command, "Width (X)", 30);
        SetNumber(command, "Depth (Y)", 20);
        SetNumber(command, "Height (Z)", 10);
        SetInteger(command, "Faces X", 3);
        SetInteger(command, "Faces Y", 2);
        SetInteger(command, "Faces Z", 1);
        SetNumber(command, "Position X", 100);
        SetNumber(command, "Position Z", -5);
        var cage = Cage(command);
        Assert.Equal(2 * (3 * 2 + 3 * 1 + 2 * 1), cage.F.Count);
        Assert.All(cage.F, face => Assert.Equal(4, face.Length));
        var (min, max) = Bounds(cage);
        AssertNear(30, max.X - min.X, "Width (mm)");
        AssertNear(20, max.Y - min.Y, "Depth (mm)");
        AssertNear(10, max.Z - min.Z, "Height (mm)");
        AssertNear(100, (min.X + max.X) / 2, "Centre X (mm)");
    }

    [AvaloniaTheory(DisplayName = "FRM-H103 | Freeform Sphere is a quad-sphere with faces-per-side squared on each of six sides")]
    [InlineData(1)]
    [InlineData(3)]
    [Trait("TestId", "FRM-H103")]
    public void SphereFaces(int perSide)
    {
        var session = new CadSession();
        var command = Begin(session, () => new FormSphereCommand());
        SetNumber(command, "Radius", 15);
        SetInteger(command, "Faces per side", perSide);
        var cage = Cage(command);
        Assert.Equal(6 * perSide * perSide, cage.F.Count);
        Assert.All(cage.V, v => AssertClose(15, v.Length, 1e-6, "Cage point radius (mm)"));
    }

    [AvaloniaTheory(DisplayName = "FRM-H104 | Freeform Cylinder has sides × rings wall faces and optional caps")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "FRM-H104")]
    public void CylinderFaces(bool caps)
    {
        var session = new CadSession();
        var command = Begin(session, () => new FormCylinderCommand());
        SetNumber(command, "Radius", 10);
        SetNumber(command, "Height", 40);
        SetInteger(command, "Sides", 6);
        SetInteger(command, "Rings", 4);
        SetToggle(command, "Caps", caps);
        var cage = Cage(command);
        int wall = cage.F.Count(face => face.Select(i => cage.V[i]).Select(v => v.Z).Distinct().Count() > 1);
        Assert.Equal(6 * 4, wall);
        Assert.Equal(caps, cage.F.Count > wall);
        var (min, max) = Bounds(cage);
        AssertNear(40, max.Z - min.Z, "Height (mm)");
        Assert.Equal(caps ? 0 : 12, CageReport.Inspect(cage).BoundaryEdges);
    }

    [AvaloniaFact(DisplayName = "FRM-H105 | Freeform Torus has segments around × tube faces and caps the tube at 99% of the major radius")]
    [Trait("TestId", "FRM-H105")]
    public void TorusFaces()
    {
        var session = new CadSession();
        var command = Begin(session, () => new FormTorusCommand());
        SetNumber(command, "Major radius", 30);
        SetNumber(command, "Minor radius", 8);
        SetInteger(command, "Segments around", 10);
        SetInteger(command, "Segments of tube", 5);
        var cage = Cage(command);
        Assert.Equal(50, cage.F.Count);
        Assert.Equal(50, cage.V.Count);
        var report = CageReport.Inspect(cage);
        Assert.Equal(0, report.BoundaryEdges);
        Assert.Equal(1, report.Components);

        SetNumber(command, "Minor radius", 100);
        var fat = Cage(command);
        var (min, _) = Bounds(fat);
        AssertClose(-(30 + 30 * 0.99), min.X, 1e-6, "Outer extent with the clamped tube (mm)");
    }

    // ------------------------------------------------------------------ Check Cage / Freeform Quality

    [AvaloniaFact(DisplayName = "FRM-H106 | Check Cage passes a closed box cage and counts its eight corner poles")]
    [Trait("TestId", "FRM-H106")]
    public void CheckCageClosedBox()
    {
        var session = new CadSession();
        var box = AddForm(session, Begin(session, () => new FormBoxCommand()));
        var command = Begin(session, () => new CheckCageCommand());
        Pick(command, "Freeform bodies", box);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("Topology checks passed", Result(command, box.Name));
        Assert.Equal("0", Result(command, "Open boundary edges"));
        Assert.Equal("0", Result(command, "Winding conflicts"));
        Assert.Equal("1", Result(command, "Connected components"));
        Assert.Equal("8", Result(command, "Extraordinary interior points"));
    }

    [AvaloniaFact(DisplayName = "FRM-H107 | Check Cage reports the open border of a freeform plane")]
    [Trait("TestId", "FRM-H107")]
    public void CheckCageOpenPlane()
    {
        var session = new CadSession();
        var plane = AddForm(session, Begin(session, () => new FormPlaneCommand()));
        var command = Begin(session, () => new CheckCageCommand());
        Pick(command, "Freeform bodies", plane);
        BuildOutput(command);
        Assert.Equal("Topology checks passed", Result(command, plane.Name));
        Assert.Equal("16", Result(command, "Open boundary edges"));
        Assert.Equal("0", Result(command, "Extraordinary interior points"));
    }

    [AvaloniaFact(DisplayName = "FRM-H108 | Freeform Quality counts quads and poles of a box cage")]
    [Trait("TestId", "FRM-H108")]
    public void QualityOfBox()
    {
        var session = new CadSession();
        var box = AddForm(session, Begin(session, () => new FormBoxCommand()));
        var command = Begin(session, () => new FormQualityCommand());
        Pick(command, "Freeform to check", box);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("26 points, 24 faces (24 quads, 0 triangles, 0 n-gons)", Result(command, "Cage"));
        Assert.Equal("8 with three edges, 0 with five or more", Result(command, "Poles"));
        Assert.Equal("30 to 30 mm (mean 30)", Result(command, "Edge lengths"));
    }

    [AvaloniaFact(DisplayName = "FRM-H109 | Freeform Quality compares a freeform sphere with a CAD sphere")]
    [Trait("TestId", "FRM-H109")]
    public void QualityAgainstReference()
    {
        var session = new CadSession();
        var form = AddForm(session, Begin(session, () => new FormSphereCommand()));
        var reference = session.AddObject(Occ.Sphere(30), "Reference", ObjKind.Solid);
        var command = Begin(session, () => new FormQualityCommand());
        Pick(command, "Freeform to check", form);
        Pick(command, "Compare with (scan, CAD body or form)", reference);
        SetNumber(command, "Tolerance", 5);
        BuildOutput(command);
        Assert.Equal("100%", Result(command, "Points within tolerance"));
        SetToggle(command, "Color the surface by distance to the reference", false);
        BuildOutput(command);
        Assert.DoesNotContain(command.Results, r => r.Label == "Points within tolerance");
    }
}
