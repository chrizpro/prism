using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>
/// Characterizes the Mesh ribbon tools on small analytic meshes: Repair / Fill Holes, Print Check, Reduce, Smooth,
/// Offset, Split, Section Curves, Wall Thickness, Overhang, Deviation, Align, Boolean, Remesh, Quality and the
/// Mesh to Solid / Solid to Mesh conversions.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/mesh-and-reverse-engineer/README.md">Living guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public class MeshToolTests
{
    static readonly int[] CubeTriangles =
    [
        0, 2, 1, 0, 3, 2,   // bottom (-Z)
        4, 5, 6, 4, 6, 7,   // top (+Z)
        0, 1, 5, 0, 5, 4,   // front (-Y)
        3, 7, 6, 3, 6, 2,   // back (+Y)
        0, 4, 7, 0, 7, 3,   // left (-X)
        1, 2, 6, 1, 6, 5,   // right (+X)
    ];

    /// <summary>A closed, outward-facing 12-triangle box mesh with its minimum corner at <paramref name="min"/>.</summary>
    static TriMesh BoxMesh(Vec3 min, double sx, double sy, double sz, bool dropLastTriangle = false)
    {
        float X(double v) => (float)v;
        float[] v =
        [
            X(min.X), X(min.Y), X(min.Z), X(min.X + sx), X(min.Y), X(min.Z), X(min.X + sx), X(min.Y + sy), X(min.Z), X(min.X), X(min.Y + sy), X(min.Z),
            X(min.X), X(min.Y), X(min.Z + sz), X(min.X + sx), X(min.Y), X(min.Z + sz), X(min.X + sx), X(min.Y + sy), X(min.Z + sz), X(min.X), X(min.Y + sy), X(min.Z + sz),
        ];
        var t = dropLastTriangle ? CubeTriangles[..^3] : CubeTriangles;
        return new TriMesh(v, (int[])t.Clone());
    }

    /// <summary>A finely tessellated, welded sphere mesh of the given radius centred at the origin.</summary>
    static TriMesh SphereMesh(double radius)
    {
        using var sphere = Occ.Sphere(radius);
        return MeshAnalysis.FromMeshData(sphere.Tessellate(radius * 0.002));
    }

    static SceneObject AddMesh(CadSession session, TriMesh mesh, string name = "Mesh")
    {
        var output = new CommandOutput();
        output.Add.Add(new NewObject(null, name, ObjKind.Mesh, Tri: mesh));
        session.Apply(output);
        return session.Objects.Last();
    }

    static TriMesh ReplacedMesh(CommandOutput output, SceneObject source)
    {
        var (old, mesh) = Assert.Single(output.ReplaceMeshes);
        Assert.Equal(source.Id, old.Id);
        return mesh;
    }

    /// <summary>Slow mesh tools only compute on Preview or when applied; tests run them as an apply would.</summary>
    static CommandOutput Apply(Command command)
    {
        command.Committing = true;
        try { return BuildOutput(command); }
        finally { command.Committing = false; }
    }

    // ------------------------------------------------------------------ Repair / Fill Holes

    [AvaloniaFact(DisplayName = "MSH-H001 | Repair Mesh closes a missing triangle and reports a watertight result")]
    [Trait("TestId", "MSH-H001")]
    public void RepairFillsHole()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20, dropLastTriangle: true));
        var command = Begin(session, () => new MeshRepairCommand());
        Pick(command, "Meshes", mesh);
        var repaired = ReplacedMesh(BuildOutput(command), mesh);
        Assert.Equal("1 → 0", Result(command, "Holes"));
        Assert.Equal("11 → 12", Result(command, "Triangles"));
        Assert.Equal("Watertight and printable", Result(command, "Result"));
        AssertClose(8000, repaired.Volume(), 1e-3, "Repaired volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "MSH-H002 | Repair Mesh with 'Fill holes' off leaves the hole open")]
    [Trait("TestId", "MSH-H002")]
    public void RepairWithoutFillLeavesHole()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20, dropLastTriangle: true));
        var command = Begin(session, () => new MeshRepairCommand());
        Pick(command, "Meshes", mesh);
        SetToggle(command, "Fill holes", false);
        Assert.False(command.Params.Single(p => p.Label == "Largest hole to fill (edges)").IsVisible);
        BuildOutput(command);
        Assert.Equal("1 → 1", Result(command, "Holes"));
        Assert.Equal("Still open", Result(command, "Result"));
    }

    [AvaloniaFact(DisplayName = "MSH-H003 | Repair Mesh 'Merge duplicate vertices' welds a triangle soup into a closed cube")]
    [Trait("TestId", "MSH-H003")]
    public void RepairWeldsSoup()
    {
        var cube = BoxMesh(Vec3.Zero, 20, 20, 20);
        var soup = new float[CubeTriangles.Length * 3];
        for (int i = 0; i < CubeTriangles.Length; i++)
            for (int k = 0; k < 3; k++) soup[3 * i + k] = cube.V[3 * CubeTriangles[i] + k];
        var session = new CadSession();
        var mesh = AddMesh(session, new TriMesh(soup, Enumerable.Range(0, CubeTriangles.Length).ToArray()));
        Assert.Equal(36, mesh.Tri!.VertexCount);
        var command = Begin(session, () => new MeshRepairCommand());
        Pick(command, "Meshes", mesh);
        var repaired = ReplacedMesh(BuildOutput(command), mesh);
        Assert.Equal(8, repaired.VertexCount);
        Assert.Equal("Watertight and printable", Result(command, "Result"));
    }

    [AvaloniaFact(DisplayName = "MSH-H004 | Fill Holes starts with only hole filling ticked")]
    [Trait("TestId", "MSH-H004")]
    public void FillHolesDefaults()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20, dropLastTriangle: true));
        var command = Begin(session, () => new MeshRepairCommand(holesOnly: true));
        Assert.Equal("Fill Holes", command.Title);
        var toggles = command.Params.OfType<BoolParam>().ToDictionary(p => p.Label, p => p.Value);
        Assert.False(toggles["Merge duplicate vertices"]);
        Assert.False(toggles["Remove degenerate and duplicate triangles"]);
        Assert.False(toggles["Fix flipped and inconsistent normals"]);
        Assert.True(toggles["Fill holes"]);
        Pick(command, "Meshes", mesh);
        BuildOutput(command);
        Assert.Equal("1 → 0", Result(command, "Holes"));
    }

    // ------------------------------------------------------------------ Print Check

    [AvaloniaFact(DisplayName = "MSH-H005 | Print Check passes a closed 20 mm cube on the bed and estimates its weight")]
    [Trait("TestId", "MSH-H005")]
    public void PrintCheckCube()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshCheckCommand());
        Pick(command, "Meshes", mesh);
        SetChoice(command, "Material", "Resin");
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("Yes", Result(command, "Watertight"));
        Assert.Equal("Yes", Result(command, "Manifold"));
        Assert.Equal("Consistent, outward", Result(command, "Normals"));
        Assert.Equal("20 × 20 × 20 mm", Result(command, "Size"));
        Assert.Equal("Yes", Result(command, "Fits the printer"));
        Assert.Equal("8 cm³", Result(command, "Volume"));
        Assert.Equal("8.8 g at solid", Result(command, "Estimated weight"));
        Assert.Equal("Ready to slice", Result(command, "Verdict"));
    }

    [AvaloniaTheory(DisplayName = "MSH-H006 | Print Check compares the part with a custom build volume")]
    [InlineData(100, 100, 100, "Yes")]
    [InlineData(15, 50, 50, "Only if rotated")]
    [InlineData(10, 10, 10, "No - scale it down or split it")]
    [Trait("TestId", "MSH-H006")]
    public void PrintCheckCustomVolume(double x, double y, double z, string fits)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 40, 12, 12));
        var command = Begin(session, () => new MeshCheckCommand());
        Pick(command, "Meshes", mesh);
        SetChoice(command, "Printer", "Custom");
        Assert.True(command.Params.Single(p => p.Label == "Build width X").IsVisible);
        SetNumber(command, "Build width X", x);
        SetNumber(command, "Build depth Y", y);
        SetNumber(command, "Build height Z", z);
        BuildOutput(command);
        Assert.Equal(fits, Result(command, "Fits the printer"));
    }

    [AvaloniaFact(DisplayName = "MSH-H007 | Print Check flags an open mesh as needing repair")]
    [Trait("TestId", "MSH-H007")]
    public void PrintCheckOpenMesh()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20, dropLastTriangle: true));
        var command = Begin(session, () => new MeshCheckCommand());
        Pick(command, "Meshes", mesh);
        BuildOutput(command);
        Assert.Equal("No: 1 hole(s), 3 open edges", Result(command, "Watertight"));
        Assert.Equal("Repair the mesh first (Mesh > Repair)", Result(command, "Verdict"));
    }

    // ------------------------------------------------------------------ Reduce / Smooth / Offset

    [AvaloniaFact(DisplayName = "MSH-H008 | Reduce Mesh keeps the chosen percentage of a sphere's triangles")]
    [Trait("TestId", "MSH-H008")]
    public void ReducePercentage()
    {
        var session = new CadSession();
        var sphere = SphereMesh(10);
        var mesh = AddMesh(session, sphere);
        var command = Begin(session, () => new MeshDecimateCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "Keep", 25);
        Assert.Null(command.TryBuild());
        Assert.Equal("Press 'Preview result' to see it, or apply directly", Result(command, "Not computed yet"));
        var reduced = ReplacedMesh(Apply(command), mesh);
        Assert.InRange(reduced.TriCount, sphere.TriCount / 4 - 4, sphere.TriCount / 4 + 4);
        AssertClose(sphere.Volume(), reduced.Volume(), sphere.Volume() * 0.02, "Reduced volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "MSH-H009 | Reduce Mesh to a triangle count can keep the original as well")]
    [Trait("TestId", "MSH-H009")]
    public void ReduceToCountKeepingOriginal()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, SphereMesh(10));
        var command = Begin(session, () => new MeshDecimateCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Reduce to", "A triangle count");
        Assert.False(command.Params.Single(p => p.Label == "Keep").IsVisible);
        SetInteger(command, "Triangles", 200);
        SetToggle(command, "Keep the original too", true);
        var output = Apply(command);
        Assert.Empty(output.ReplaceMeshes);
        var copy = Assert.Single(output.Add).Tri!;
        Assert.InRange(copy.TriCount, 190, 200);
    }

    [AvaloniaFact(DisplayName = "MSH-H010 | Smooth Mesh keeps a sphere's volume and subdivision multiplies its triangles by four")]
    [Trait("TestId", "MSH-H010")]
    public void SmoothKeepsVolume()
    {
        var session = new CadSession();
        var sphere = SphereMesh(10);
        var mesh = AddMesh(session, sphere);
        var command = Begin(session, () => new MeshSmoothCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Strength", "Strong");
        SetInteger(command, "Add detail first (subdivisions)", 1);
        SetToggle(command, "Keep sharp edges and corners", false);
        Assert.False(command.Params.Single(p => p.Label == "Edges sharper than").IsVisible);
        var smooth = ReplacedMesh(Apply(command), mesh);
        Assert.Equal(sphere.TriCount * 4, smooth.TriCount);
        AssertClose(sphere.Volume(), smooth.Volume(), sphere.Volume() * 1e-3, "Smoothed volume (mm³)");
    }

    [AvaloniaTheory(DisplayName = "MSH-H011 | Offset Mesh grows or shrinks a sphere by the signed distance")]
    [InlineData(1)]
    [InlineData(-1)]
    [Trait("TestId", "MSH-H011")]
    public void OffsetSphere(double distance)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, SphereMesh(10));
        var command = Begin(session, () => new MeshOffsetCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "Offset (+ grows, - shrinks)", distance);
        var offset = ReplacedMesh(BuildOutput(command), mesh);
        AssertClose(10 + distance, offset.Max.X, 0.01, "Offset radius (mm)");
        Assert.Equal($"{distance:#,0.###} mm", Result(command, "Offset"));
    }

    // ------------------------------------------------------------------ Split / Section

    [AvaloniaTheory(DisplayName = "MSH-H012 | Split Mesh at Z = 10 keeps both capped halves or the chosen one")]
    [InlineData("Both halves", 2)]
    [InlineData("Upper half", 1)]
    [InlineData("Lower half", 1)]
    [Trait("TestId", "MSH-H012")]
    public void SplitCube(string keep, int pieces)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshCutCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "Position along the normal", 10);
        SetChoice(command, "Keep", keep);
        var output = BuildOutput(command);
        var kept = ReplacedMesh(output, mesh);
        Assert.Equal(pieces - 1, output.Add.Count);
        AssertClose(4000, kept.Volume(), 1e-3, "Kept half volume (mm³)");
        AssertClose(keep == "Lower half" ? 0 : 10, kept.Min.Z, 1e-4, "Kept half bottom (mm)");
        Assert.EndsWith("closed", Result(command, "Upper half"));
        Assert.Equal("1", Result(command, "Cut outlines"));
    }

    [AvaloniaFact(DisplayName = "MSH-H013 | Split Mesh with a custom normal, flip and open cut")]
    [Trait("TestId", "MSH-H013")]
    public void SplitCustomDirection()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshCutCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Cut plane", "Custom direction");
        SetNumber(command, "Normal X", 1);
        SetNumber(command, "Normal Y", 0);
        SetNumber(command, "Normal Z", 0);
        SetNumber(command, "Position along the normal", -5);
        SetToggle(command, "Flip side", true);
        SetToggle(command, "Close the cut faces", false);
        SetChoice(command, "Keep", "Upper half");
        var upper = ReplacedMesh(BuildOutput(command), mesh);
        // Flipping makes the normal -X, so "upper" is the X < 5 side.
        AssertClose(0, upper.Min.X, 1e-4, "Kept minimum X (mm)");
        AssertClose(5, upper.Max.X, 1e-4, "Kept maximum X (mm)");
        Assert.False(Result(command, "Upper half").EndsWith("closed"));
    }

    [AvaloniaFact(DisplayName = "MSH-H014 | Split Mesh reports a plane that misses and refuses a zero normal")]
    [Trait("TestId", "MSH-H014")]
    public void SplitMissesAndZeroNormal()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshCutCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "Position along the normal", 50);
        Assert.Null(command.TryBuild());
        Assert.Equal("move it into the part", Result(command, "The plane misses the mesh"));
        SetChoice(command, "Cut plane", "Custom direction");
        SetNumber(command, "Normal Z", 0);
        Assert.Null(command.TryBuild());
        Assert.Equal("must not be zero", Result(command, "Normal"));
    }

    [AvaloniaTheory(DisplayName = "MSH-H015 | Mesh Section Curves creates one curve per outline on each section plane")]
    [InlineData("XY (horizontal)")]
    [InlineData("XZ")]
    [InlineData("YZ")]
    [Trait("TestId", "MSH-H015")]
    public void SectionCube(string plane)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshSectionCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Section plane", plane);
        SetNumber(command, "First position", 5);
        SetInteger(command, "Number of sections", 3);
        Assert.True(command.Params.Single(p => p.Label == "Spacing").IsVisible);
        SetNumber(command, "Spacing", 5);
        SetToggle(command, "Create curves from the sections", true);
        var output = BuildOutput(command);
        Assert.Equal("3", Result(command, "Outlines"));
        Assert.Equal("1,200 mm²", Result(command, "Net section area"));
        Assert.Equal(3, output.Add.Count);
        Assert.All(output.Add, curve => AssertClose(80, Length(curve.Shape!), 1e-4, "Section perimeter (mm)"));
    }

    [AvaloniaFact(DisplayName = "MSH-H016 | Mesh Section Curves reports a plane that misses the mesh")]
    [Trait("TestId", "MSH-H016")]
    public void SectionMisses()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshSectionCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "First position", 40);
        SetToggle(command, "Create curves from the sections", true);
        Assert.Empty(BuildOutput(command).Add);
        Assert.Equal("0", Result(command, "Outlines"));
        Assert.Equal("the plane does not cut the mesh", Result(command, "Nothing here"));
    }

    // ------------------------------------------------------------------ Thickness / Overhang

    [AvaloniaTheory(DisplayName = "MSH-H017 | Wall Thickness finds a 20 mm cube's walls and compares them with the printer minimum")]
    [InlineData(0.8, true)]
    [InlineData(25, false)]
    [Trait("TestId", "MSH-H017")]
    public void ThicknessOfCube(double minimum, bool ok)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 20, 20, 20), 2));
        var command = Begin(session, () => new MeshThicknessCommand());
        Pick(command, "Mesh", mesh);
        SetNumber(command, "Minimum wall for your printer", minimum);
        Assert.True(Apply(command).NoOp);
        var thinnest = Assert.Single(command.Results, line => line.Label == "Thinnest wall");
        Assert.Equal(ok, thinnest.IsGood);
        var below = Assert.Single(command.Results, line => line.Label == "Below the minimum");
        Assert.Equal(ok, below.Value == "0% of the surface");
    }

    [AvaloniaFact(DisplayName = "MSH-H018 | Overhang finds no support for a cube on the bed and can re-orient a part")]
    [Trait("TestId", "MSH-H018")]
    public void OverhangCube()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(new Vec3(5, 5, 5), 20, 20, 20));
        var command = Begin(session, () => new MeshOverhangCommand());
        Pick(command, "Mesh", mesh);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("20 mm", Result(command, "Height"));
        SetChoice(command, "Orientation", "Best orientation");
        var oriented = ReplacedMesh(BuildOutput(command), mesh);
        AssertClose(0, oriented.Min.Z, 1e-4, "Re-oriented part sits on the bed (mm)");
        AssertClose(0, oriented.Center.X, 1e-4, "Re-oriented part is centred in X (mm)");
        AssertClose(0, oriented.Center.Y, 1e-4, "Re-oriented part is centred in Y (mm)");
    }

    // ------------------------------------------------------------------ Deviation / Align

    [AvaloniaFact(DisplayName = "MSH-H019 | Deviation Analysis reports a mesh lying on its CAD body as fully within tolerance")]
    [Trait("TestId", "MSH-H019")]
    public void DeviationAgainstMatchingBody()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 20, 20, 20), 1));
        var body = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var command = Begin(session, () => new MeshDeviationCommand());
        Pick(command, "Mesh to check", mesh);
        Pick(command, "Compare with (CAD body or mesh)", body);
        Assert.True(Apply(command).NoOp);
        Assert.Equal("100%", Result(command, "Points within tolerance"));
        Assert.Equal("0 mm", Result(command, "RMS deviation"));
    }

    [AvaloniaFact(DisplayName = "MSH-H020 | Deviation Analysis measures a 0.5 mm offset from the reference")]
    [Trait("TestId", "MSH-H020")]
    public void DeviationMeasuresOffset()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, MeshOps.Subdivide(BoxMesh(Vec3.Zero, 20, 20, 20), 1));
        var body = CreateBoxBody(session, new Vec3(0.5, 0.5, 0.5), 19, 19, 19);
        var command = Begin(session, () => new MeshDeviationCommand());
        Pick(command, "Mesh to check", mesh);
        Pick(command, "Compare with (CAD body or mesh)", body);
        SetNumber(command, "Tolerance", 0.1);
        Apply(command);
        // The nearest reference point to each mesh corner is the reference corner, √3 × 0.5 mm away.
        Assert.Equal("0.866 mm", Result(command, "Max above"));
        Assert.True(Assert.Single(command.Results, r => r.Label == "Points within tolerance").IsBad);
    }

    [AvaloniaTheory(DisplayName = "MSH-H021 | Align Mesh moves a floating box onto the bed or to the origin")]
    [InlineData("Place on the print bed (lowest point to Z = 0)", 10, 20, 0)]
    [InlineData("Center on the origin (X, Y)", -10, -10, 30)]
    [InlineData("Center of mass to the origin", -10, -10, -10)]
    [Trait("TestId", "MSH-H021")]
    public void AlignSimpleModes(string mode, double minX, double minY, double minZ)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(new Vec3(10, 20, 30), 20, 20, 20));
        var command = Begin(session, () => new MeshAlignCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Align", mode);
        Assert.False(command.Slots.Single(s => s.Label == "Reference body").IsVisible);
        var aligned = ReplacedMesh(BuildOutput(command), mesh);
        AssertPoint(new Vec3(minX, minY, minZ), aligned.Min, 1e-4, "Aligned minimum corner");
    }

    [AvaloniaFact(DisplayName = "MSH-H022 | Align Mesh 'Lay the biggest flat face on the bed' drops a slab onto its largest face")]
    [Trait("TestId", "MSH-H022")]
    public void AlignLayFlat()
    {
        var session = new CadSession();
        var slab = MeshOps.Rotate(MeshOps.Subdivide(BoxMesh(Vec3.Zero, 40, 30, 4), 2), new Vec3(20, 15, 2), Vec3.UnitX, 90);
        var mesh = AddMesh(session, slab);
        var command = Begin(session, () => new MeshAlignCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Align", "Lay the biggest flat face on the bed");
        var aligned = ReplacedMesh(BuildOutput(command), mesh);
        AssertClose(0, aligned.Min.Z, 1e-3, "On the bed (mm)");
        AssertClose(4, aligned.Size.Z, 1e-3, "Slab lies flat (mm)");
        Assert.Equal("1,200 mm²", Result(command, "Flat face"));
    }

    [AvaloniaFact(DisplayName = "MSH-H023 | Align Mesh best fit pulls a shifted scan onto its CAD body")]
    [Trait("TestId", "MSH-H023")]
    public void AlignBestFit()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 30, 20, 10);
        var mesh = AddMesh(session, MeshOps.Subdivide(BoxMesh(new Vec3(1.5, -1, 0.5), 30, 20, 10), 2));
        var command = Begin(session, () => new MeshAlignCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Align", "Best fit to a CAD body or mesh");
        Assert.True(command.Slots.Single(s => s.Label == "Reference body").IsVisible);
        Assert.Null(command.TryBuild());
        Assert.Equal("pick the CAD body or mesh to align to", Result(command, "Reference"));
        Pick(command, "Reference body", body);
        var aligned = ReplacedMesh(Apply(command), mesh);
        AssertPoint(Vec3.Zero, aligned.Min, 0.05, "Fitted minimum corner");
        Assert.True(Assert.Single(command.Results, r => r.Label == "Fit error after").IsGood);
    }

    // ------------------------------------------------------------------ Boolean / Remesh / Quality

    [AvaloniaTheory(DisplayName = "MSH-H024 | Mesh Boolean combines two overlapping cubes by each operation")]
    [InlineData("Union (join)", 15000, "Union")]
    [InlineData("Subtract (first minus second)", 7000, "Difference")]
    [InlineData("Intersect (common part)", 1000, "Intersection")]
    [Trait("TestId", "MSH-H024")]
    public void BooleanOperations(string operation, double volume, string name)
    {
        var session = new CadSession();
        var first = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20), "First");
        var second = AddMesh(session, BoxMesh(new Vec3(10, 10, 10), 20, 20, 20), "Second");
        var command = Begin(session, () => new MeshBooleanCommand());
        Pick(command, "First mesh", first);
        Pick(command, "Second mesh", second);
        SetChoice(command, "Operation", operation);
        var output = Apply(command);
        var added = Assert.Single(output.Add);
        Assert.StartsWith(name, added.NamePrefix);
        AssertClose(volume, added.Tri!.Volume(), 0.5, "Boolean volume (mm³)");
        Assert.Equal(2, output.Remove.Count);
        Assert.Equal("Closed and manifold", Result(command, "Result"));
    }

    [AvaloniaFact(DisplayName = "MSH-H025 | Mesh Boolean keeps the originals when asked and refuses the same mesh twice")]
    [Trait("TestId", "MSH-H025")]
    public void BooleanKeepAndSelf()
    {
        var session = new CadSession();
        var first = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20), "First");
        var second = AddMesh(session, BoxMesh(new Vec3(10, 10, 10), 20, 20, 20), "Second");
        var command = Begin(session, () => new MeshBooleanCommand());
        Pick(command, "First mesh", first);
        Pick(command, "Second mesh", second);
        SetToggle(command, "Keep the originals", true);
        Assert.Empty(Apply(command).Remove);
        Pick(command, "Second mesh", first);
        Assert.Null(command.TryBuild());
        Assert.Equal("pick a different mesh", Result(command, "Second mesh"));
    }

    [AvaloniaFact(DisplayName = "MSH-H026 | Remesh evens out a sphere to the target edge length while keeping its volume")]
    [Trait("TestId", "MSH-H026")]
    public void RemeshSphere()
    {
        var session = new CadSession();
        var sphere = SphereMesh(10);
        var mesh = AddMesh(session, sphere);
        var command = Begin(session, () => new MeshRemeshCommand());
        Pick(command, "Mesh", mesh);
        SetToggle(command, "Use the mesh's average edge length", false);
        Assert.True(command.Params.Single(p => p.Label == "Target edge length").IsVisible);
        SetNumber(command, "Target edge length", 2);
        SetToggle(command, "Keep the original too", true);
        var output = Apply(command);
        var remeshed = Assert.Single(output.Add).Tri!;
        AssertClose(sphere.Volume(), remeshed.Volume(), sphere.Volume() * 0.02, "Remeshed volume (mm³)");
        AssertClose(2, MeshRemesh.MeanEdge(remeshed), 0.4, "Mean edge (mm)");
        Assert.True(MeshAnalysis.Report(remeshed).Closed);
    }

    [AvaloniaFact(DisplayName = "MSH-H027 | Mesh Quality reports a closed cube's triangles and defects without changing it")]
    [Trait("TestId", "MSH-H027")]
    public void QualityOfCube()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshQualityCommand());
        Pick(command, "Mesh", mesh);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("12", Result(command, "Triangles"));
        Assert.Equal("0", Result(command, "Open edges"));
        Assert.Equal("0", Result(command, "Non-manifold edges"));
        Assert.Equal("0", Result(command, "Self-intersecting triangles"));
        Assert.Equal("max 90°", Result(command, "Largest angle"));
        Assert.Equal("min 45°, mean 45°", Result(command, "Smallest angle"));
    }

    [AvaloniaFact(DisplayName = "MSH-H028 | Mesh Quality counts the open edges of a holed mesh")]
    [Trait("TestId", "MSH-H028")]
    public void QualityOfOpenMesh()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20, dropLastTriangle: true));
        var command = Begin(session, () => new MeshQualityCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Heat map", "Off");
        SetToggle(command, "Show self-intersecting triangles (orange)", false);
        BuildOutput(command);
        Assert.Equal("3", Result(command, "Open edges"));
        Assert.DoesNotContain(command.Results, r => r.Label == "Heat map" || r.Label == "Self-intersecting triangles");
    }

    // ------------------------------------------------------------------ Mesh ↔ Solid

    [AvaloniaTheory(DisplayName = "MSH-H029 | Mesh to Solid converts a closed cube into a 8000 mm³ solid or a shell")]
    [InlineData("Solid", true)]
    [InlineData("Surface shell", false)]
    [Trait("TestId", "MSH-H029")]
    public void MeshToSolid(string create, bool solid)
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        var command = Begin(session, () => new MeshToSolidCommand());
        Pick(command, "Mesh", mesh);
        SetChoice(command, "Create", create);
        var output = Apply(command);
        var body = Assert.Single(output.Add);
        Assert.Equal(solid ? ObjKind.Solid : ObjKind.Surface, body.Kind);
        Assert.Equal(solid, body.Shape!.HasSolids);
        if (solid) AssertNear(8000, body.Shape.Props().Volume, "Converted volume (mm³)");
        Assert.Empty(output.Remove);
        Assert.Equal("12", Result(command, "Triangles used"));
    }

    [AvaloniaFact(DisplayName = "MSH-H030 | Mesh to Solid can consume the mesh and reduces large meshes first")]
    [Trait("TestId", "MSH-H030")]
    public void MeshToSolidReducesAndConsumes()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, SphereMesh(10));
        var command = Begin(session, () => new MeshToSolidCommand());
        Pick(command, "Mesh", mesh);
        SetInteger(command, "Reduce to at most (triangles)", 300);
        SetToggle(command, "Keep the mesh", false);
        var output = Apply(command);
        Assert.Equal(mesh.Id, Assert.Single(output.Remove).Id);
        Assert.InRange(int.Parse(Result(command, "Triangles used").Replace(",", "")), 200, 300);
    }

    [AvaloniaFact(DisplayName = "MSH-H031 | Solid to Mesh 'Fine' tessellates a sphere more finely than 'Coarse', both closed")]
    [Trait("TestId", "MSH-H031")]
    public void SolidToMeshDetail()
    {
        var session = new CadSession();
        var ball = session.AddObject(Occ.Sphere(10), "Ball", ObjKind.Solid);
        int Triangles(string level)
        {
            var command = Begin(session, () => new SolidToMeshCommand());
            Pick(command, "Bodies", ball);
            SetChoice(command, "Detail", level);
            Assert.False(command.Params.Single(p => p.Label == "Max chord error").IsVisible);
            var mesh = Assert.Single(BuildOutput(command).Add).Tri!;
            session.CancelCommand();
            Assert.True(MeshAnalysis.Report(mesh).Closed);
            AssertClose(4.0 / 3 * Math.PI * 1000, mesh.Volume(), 4.0 / 3 * Math.PI * 1000 * 0.02, "Mesh volume (mm³)");
            return mesh.TriCount;
        }
        // Coarse and Normal are not compared: the kernel keeps a finer triangulation already stored on the body
        // (for example the display mesh), so they can return the same mesh. See the Mesh guide's coverage gaps.
        int coarse = Triangles("Coarse (draft)");
        Assert.True(Triangles("Fine (print quality)") > coarse);
    }

    [AvaloniaFact(DisplayName = "MSH-H032 | Solid to Mesh 'Custom' uses the chord error and can drop the original")]
    [Trait("TestId", "MSH-H032")]
    public void SolidToMeshCustom()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var command = Begin(session, () => new SolidToMeshCommand());
        Pick(command, "Bodies", box);
        SetChoice(command, "Detail", "Custom");
        Assert.True(command.Params.Single(p => p.Label == "Max chord error").IsVisible);
        SetNumber(command, "Max chord error", 0.5);
        SetToggle(command, "Keep the original body", false);
        var output = BuildOutput(command);
        Assert.Equal(box.Id, Assert.Single(output.Remove).Id);
        var mesh = Assert.Single(output.Add).Tri!;
        AssertClose(8000, mesh.Volume(), 1e-3, "Mesh volume (mm³)");
        Assert.Equal(12, mesh.TriCount);
    }
}
