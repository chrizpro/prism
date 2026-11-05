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
/// Characterizes the scan-to-CAD tools: Segment &amp; Fit, Rebuild from Scan, Section Lines &amp; Arcs, Section Sketch and
/// Select Region, on subdivided box and tessellated cylinder meshes whose exact shapes are known.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/mesh-and-reverse-engineer/README.md">Living guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public class ReverseEngineerTests
{
    /// <summary>A 20 mm cube subdivided so each face has enough triangles to count as a region.</summary>
    static TriMesh DenseCube() => MeshOps.Subdivide(BoxMesh(Vec3.Zero, 20, 20, 20), 3);

    /// <summary>
    /// A closed Ø10 × 20 mm cylinder mesh standing on the XY plane at the origin, remeshed to even 1 mm triangles like a
    /// scan (the kernel's display tessellation uses long strips that do not resemble scanner output).
    /// </summary>
    static TriMesh CylinderMesh()
    {
        using var cylinder = Occ.Cylinder(5, 20);
        var tessellated = MeshAnalysis.FromMeshData(cylinder.Tessellate(0.005));
        return MeshRemesh.Remesh(tessellated, new MeshRemesh.Options(1, 5, true, 35, true));
    }

    static void Run(Command command, string action) =>
        command.Params.OfType<ActionParam>().Single(a => a.Label == action).Run();

    // ------------------------------------------------------------------ Segment & Fit

    [AvaloniaFact(DisplayName = "REV-H101 | Segment & Fit 'All regions' finds the six planes of a cube and builds a surface for each")]
    [Trait("TestId", "REV-H101")]
    public void SegmentCubeAllRegions()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSegmentCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create CAD geometry", "All regions");
        Assert.False(command.Params.Single(p => p.Label == "Region number").IsVisible);
        var output = Apply(command);
        Assert.Equal("6", Result(command, "Regions found"));
        Assert.Equal("100%", Result(command, "Surface explained"));
        Assert.Equal(6, output.Add.Count);
        Assert.All(output.Add, face => AssertClose(400, face.Shape!.Props().Area, 1, "Fitted face area (mm²)"));
    }

    [AvaloniaFact(DisplayName = "REV-H102 | Segment & Fit measures a cylinder's diameter and can build only the chosen region")]
    [Trait("TestId", "REV-H102")]
    public void SegmentCylinderOneRegion()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, CylinderMesh());
        var command = Begin(session, () => new MeshSegmentCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create CAD geometry", "Only the region below");
        Assert.True(command.Params.Single(p => p.Label == "Region number").IsVisible);
        Apply(command);
        var cylinderRow = Assert.Single(command.Results, r => r.Label.Contains("Cylinder"));
        int region = int.Parse(cylinderRow.Label.Split('.')[0]);
        double diameter = double.Parse(cylinderRow.Label[(cylinderRow.Label.IndexOf('Ø') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        AssertClose(10, diameter, 0.1, "Fitted diameter (mm)");
        SetInteger(command, "Region number", region);
        var output = Apply(command);
        var fit = Assert.Single(output.Add);
        AssertClose(Math.PI * 10 * 20, fit.Shape!.Props().Area, Math.PI * 10 * 20 * 0.01, "Fitted cylinder wall area within 1% (mm²)");
    }

    [AvaloniaFact(DisplayName = "REV-H103 | Segment & Fit honours the shape-type switches")]
    [Trait("TestId", "REV-H103")]
    public void SegmentShapeSwitches()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, CylinderMesh());
        var command = Begin(session, () => new MeshSegmentCommand());
        Pick(command, "Mesh", mesh);
        SetToggle(command, "Cylinders", false);
        SetChoice(command, "Create CAD geometry", "All regions");
        Apply(command);
        Assert.DoesNotContain(command.Results, r => r.Label.Contains("Cylinder") || r.Label.Contains("Hole"));
    }

    // ------------------------------------------------------------------ Rebuild from Scan

    [AvaloniaFact(DisplayName = "REV-H104 | Rebuild from Scan sews a scanned cube back into an exact 8000 mm³ solid")]
    [Trait("TestId", "REV-H104")]
    public void RebuildCubeIntoSolid()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshRebuildCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create", "Faces + curves, then sew into a shell / solid");
        Assert.True(command.Params.Single(p => p.Label == "Sewing tolerance").IsVisible);
        var output = Apply(command);
        var solid = Assert.Single(output.Add, item => item.Kind == ObjKind.Solid);
        AssertValidSolid(solid.Shape!);
        AssertClose(8000, solid.Shape!.Props().Volume, 1, "Rebuilt volume (mm³)");
        Assert.Equal("6", Result(command, "Regions"));
        Assert.Equal(12, output.Add.Count(item => item.Kind == ObjKind.Curve));
    }

    [AvaloniaTheory(DisplayName = "REV-H105 | Rebuild from Scan creates fitted faces, with boundary curves when asked")]
    [InlineData("Fitted faces", 0)]
    [InlineData("Fitted faces + boundary curves", 12)]
    [Trait("TestId", "REV-H105")]
    public void RebuildCubeFaces(string create, int curves)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshRebuildCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create", create);
        Assert.False(command.Params.Single(p => p.Label == "Sewing tolerance").IsVisible);
        var output = Apply(command);
        Assert.Equal(6, output.Add.Count(item => item.Kind == ObjKind.Surface));
        Assert.Equal(curves, output.Add.Count(item => item.Kind == ObjKind.Curve));
        Assert.Equal("6 from 6 of 6 regions", Result(command, "Faces built"));
    }

    // ------------------------------------------------------------------ Section Lines & Arcs / Section Sketch

    [AvaloniaFact(DisplayName = "REV-H106 | Section Lines & Arcs fits four lines to a cube section")]
    [Trait("TestId", "REV-H106")]
    public void SectionFitCube()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSectionFitCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "First position", 10);
        var output = BuildOutput(command);
        var outline = Assert.Single(output.Add);
        AssertClose(80, Length(outline.Shape!), 1e-3, "Outline length (mm)");
        Assert.Equal("4 lines + 0 arcs (from 64 points)", Result(command, "Elements"));
    }

    [AvaloniaTheory(DisplayName = "REV-H107 | Section Lines & Arcs fits arcs across a cylinder and lines along it")]
    [InlineData("XY (horizontal)", 10)]
    [InlineData("XZ", 0)]
    [Trait("TestId", "REV-H107")]
    public void SectionFitCylinder(string plane, double position)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, CylinderMesh());
        var command = Begin(session, () => new MeshSectionFitCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Section plane", plane);
        SetNumber(command, "First position", position);
        SetNumber(command, "Fit tolerance", 0.05);
        BuildOutput(command);
        var counts = Result(command, "Elements").Split(' ');
        int lines = int.Parse(counts[0]), arcs = int.Parse(counts[3]);
        // Across the axis the outline is a circle (arcs only); along it, a rectangle (lines only).
        if (plane == "XY (horizontal)") { Assert.Equal(0, lines); Assert.True(arcs >= 2); }
        else { Assert.Equal(0, arcs); Assert.True(lines >= 4); }
        Assert.True(Assert.Single(command.Results, r => r.Label == "Largest deviation").IsGood);
    }

    [AvaloniaFact(DisplayName = "REV-H108 | Section Lines & Arcs spaces several sections and reports a miss")]
    [Trait("TestId", "REV-H108")]
    public void SectionFitSeveralAndMiss()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSectionFitCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "First position", 4);
        SetInteger(command, "Number of sections", 3);
        SetNumber(command, "Spacing", 6);
        Assert.Equal(3, BuildOutput(command).Add.Count);
        Assert.Equal("3", Result(command, "Outlines"));
        SetNumber(command, "First position", 40);
        Assert.Null(command.TryBuild());
        Assert.Equal("the plane does not cut the mesh", Result(command, "Nothing here"));
    }

    [AvaloniaTheory(DisplayName = "REV-H109 | Section Sketch makes an editable sketch on a world plane")]
    [InlineData("XY (horizontal)", 10)]
    [InlineData("XZ", -10)]
    [InlineData("YZ", 10)]
    [Trait("TestId", "REV-H109")]
    public void SectionSketchWorldPlanes(string plane, double offset)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Plane", plane);
        // The XZ plane's normal is -Y, so a positive offset moves away from the cube.
        SetNumber(command, "Offset from the plane", offset);
        var output = BuildOutput(command);
        Assert.Equal("1", Result(command, "Sketches"));
        Assert.StartsWith("4 lines + 0 arcs", Result(command, "Elements"));
        session.Apply(output);
        output.After!(session);
        var sketch = Assert.Single(session.Objects, o => o.Kind == ObjKind.Sketch);
        AssertClose(80, Length(sketch.Shape), 1e-3, "Sketch outline length (mm)");
    }

    [AvaloniaFact(DisplayName = "REV-H110 | Section Sketch on a picked flat face, and asking for a pick or region first")]
    [Trait("TestId", "REV-H110")]
    public void SectionSketchNeedsReference()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var cutter = CreateBoxBody(session, new Vec3(40, 0, 0), 10, 10, 8);
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Plane", "A construction plane or flat face");
        Assert.True(command.Slots.Single(s => s.Label == "Plane or flat face").IsVisible);
        Assert.Null(command.TryBuild());
        Assert.Equal("pick a construction plane or a flat face", Result(command, "Plane"));
        Pick(command, "Plane or flat face", FaceFacing(cutter, Vec3.UnitZ));
        BuildOutput(command);
        Assert.Equal("1", Result(command, "Outlines"));
        SetChoice(command, "Plane", "Fitted to the selected region");
        Assert.Null(command.TryBuild());
        Assert.Equal("select an area with Select Region first, then come back", Result(command, "Plane"));
    }

    // ------------------------------------------------------------------ report-only modes

    [AvaloniaFact(DisplayName = "REV-H114 | Segment & Fit 'Nothing - just analyze' keeps its region report and creates nothing")]
    [Trait("TestId", "REV-H114")]
    public void SegmentAnalyzeOnly()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSegmentCommand());
        Pick(command, "Mesh", mesh);
        var output = Apply(command);
        Assert.True(output.NoOp);
        Assert.Empty(output.Add);
        Assert.Equal("6", Result(command, "Regions found"));
        Assert.Equal(6, command.Results.Count(r => r.Label.EndsWith("Plane")));
    }

    [AvaloniaFact(DisplayName = "REV-H115 | Segment & Fit reports a region number that does not exist")]
    [Trait("TestId", "REV-H115")]
    public void SegmentMissingRegion()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSegmentCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create CAD geometry", "Only the region below");
        SetInteger(command, "Region number", 9);
        Assert.Empty(Apply(command).Add);
        Assert.Equal("9 does not exist", Result(command, "Region"));
    }

    [AvaloniaFact(DisplayName = "REV-H116 | Rebuild from Scan analysis mode keeps its region and deviation report")]
    [Trait("TestId", "REV-H116")]
    public void RebuildAnalysisOnly()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshRebuildCommand());
        Pick(command, "Mesh", mesh);
        var output = Apply(command);
        Assert.True(output.NoOp);
        Assert.Equal("6", Result(command, "Regions"));
        Assert.Equal("100%", Result(command, "Surface explained"));
    }

    [AvaloniaFact(DisplayName = "REV-H117 | Section Lines & Arcs reports its fit without creating curves")]
    [Trait("TestId", "REV-H117")]
    public void SectionFitReportOnly()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, DenseCube());
        var command = Begin(session, () => new MeshSectionFitCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "First position", 10);
        SetToggle(command, "Create lines and arcs", false);
        var output = BuildOutput(command);
        Assert.True(output.NoOp);
        Assert.Equal("1", Result(command, "Outlines"));
        Assert.Equal("4 lines + 0 arcs (from 64 points)", Result(command, "Elements"));
    }

    // ------------------------------------------------------------------ Select Region

    [AvaloniaFact(DisplayName = "REV-H111 | Select Region 'Select everything', invert and clear update the reported selection")]
    [Trait("TestId", "REV-H111")]
    public void SelectRegionActions()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshRegionCommand());
        Pick(command, "Mesh (click it)", mesh);
        BuildOutput(command);
        Assert.Equal("click, paint or drag on the mesh", Result(command, "Nothing selected"));
        Run(command, "Select everything");
        BuildOutput(command);
        Assert.Equal("12 of 12 triangles", Result(command, "Selected"));
        Assert.Equal("2,400 mm²", Result(command, "Area"));
        Assert.Equal("20 × 20 × 20 mm", Result(command, "Size"));
        Run(command, "Invert selection");
        BuildOutput(command);
        Assert.Contains(command.Results, r => r.Label == "Nothing selected");
    }

    [AvaloniaFact(DisplayName = "REV-H112 | Select Region extracts or deletes the selected triangles")]
    [Trait("TestId", "REV-H112")]
    public void SelectRegionExtractAndDelete()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshRegionCommand());
        Pick(command, "Mesh (click it)", mesh);
        session.RegionFor(mesh).Add([0, 1]);
        Run(command, "Extract selection as a new mesh");
        var part = Assert.Single(session.Objects, o => o.Kind == ObjKind.Mesh && o.Id != mesh.Id);
        Assert.Equal(2, part.Tri!.TriCount);

        session.RegionFor(session.Find(mesh.Id)!).Add([0, 1]);
        Run(command, "Delete selected triangles");
        Assert.Equal(10, session.Find(mesh.Id)!.Tri!.TriCount);
    }

    [AvaloniaTheory(DisplayName = "REV-H113 | Select Region 'Delete and patch' replaces the selected face with a patch")]
    [InlineData("Smooth (follows the surface around it)")]
    [InlineData("Flat")]
    [Trait("TestId", "REV-H113")]
    public void SelectRegionDeleteAndPatch(string patch)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 20, 20, 20), 2));
        var command = Begin(session, () => new MeshRegionCommand());
        Pick(command, "Mesh (click it)", mesh);
        SetChoice(command, "Patch", patch);
        var top = Enumerable.Range(0, mesh.Tri!.TriCount).Where(t => mesh.Tri.FaceNormal(t).Z > 0.99).ToList();
        session.RegionFor(mesh).Add(top);
        Run(command, "Delete and patch");
        var patched = session.Find(mesh.Id)!.Tri!;
        Assert.True(MeshAnalysis.Report(patched).Closed);
        AssertClose(8000, patched.Volume(), 1, "Patched volume (mm³)");
    }
}
