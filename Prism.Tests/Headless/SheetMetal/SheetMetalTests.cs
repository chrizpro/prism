using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.SheetMetal;

/// <summary>
/// Characterizes the Sheet Metal ribbon (Flange, Flat Pattern) and Flatten / Refold. The L-bracket fixture is a
/// 20 mm leg down to the origin then 30 mm along +X; with 2 mm metal on the inside and a 2 mm inside radius each straight
/// leg loses the 4 mm tangent length of the bend.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sheet-metal/README.md">Living guide.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/flatten-refold.md">Flatten and Refold.</seealso>
[Trait("Feature", "SheetMetal")]
[Trait("Layer", "Headless")]
public class SheetMetalTests
{
    const double Thickness = 2, Radius = 2, Width = 30;

    // Cross-section: legs of 16 and 26 mm at 2 mm thick, plus a quarter annulus between radii 2 and 4.
    static readonly double SectionArea = 16 * 2 + 26 * 2 + Math.PI / 4 * (4 * 4 - 2 * 2);

    static SceneObject Sketch(CadSession session, params (double u, double v)[] points)
    {
        var model = new SketchModel();
        var ids = points.Select(p => model.AddPoint(p.u, p.v)).ToArray();
        for (int i = 0; i + 1 < ids.Length; i++) model.AddLine(ids[i], ids[i + 1]);
        session.EnterSketch(Plane3.XY, model);
        session.ExitSketch(keep: true);
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    static SceneObject LProfile(CadSession session) => Sketch(session, (0, 20), (0, 0), (30, 0));

    static ContourFlangeCommand Flange(CadSession session, SceneObject profile)
    {
        var command = Begin(session, () => new ContourFlangeCommand());
        Pick(command, "Profile sketch (straight lines)", profile);
        SetNumber(command, "Thickness", Thickness);
        SetNumber(command, "Bend radius (inside)", Radius);
        SetNumber(command, "Width", Width);
        return command;
    }

    static Shape Body(Command command)
    {
        var added = Assert.Single(BuildOutput(command).Add);
        Assert.Equal(ObjKind.Solid, added.Kind);
        AssertValidSolid(added.Shape!);
        return added.Shape!;
    }

    // ------------------------------------------------------------------ Flange

    [AvaloniaFact(DisplayName = "SHM-H001 | Flange turns an L profile into a 2 mm bracket with one rounded bend")]
    [Trait("TestId", "SHM-H001")]
    public void FlangeMakesBracket()
    {
        var session = new CadSession();
        var command = Flange(session, LProfile(session));
        var body = Body(command);
        AssertClose(SectionArea * Width, body.Props().Volume, 1e-3, "Bracket volume (mm³)");
        AssertBounds(body, Vec3.Zero, new Vec3(30, 20, 30));
        Assert.Equal("1", Result(command, "Bends"));
    }

    [AvaloniaFact(DisplayName = "SHM-H002 | Flange 'Right of the profile' puts the metal outside the L")]
    [Trait("TestId", "SHM-H002")]
    public void FlangeRightSide()
    {
        var session = new CadSession();
        var command = Flange(session, LProfile(session));
        SetChoice(command, "The metal lies", "Right of the profile");
        var body = Body(command);
        // The profile is now the inner surface: legs keep 18 and 28 mm, the bend runs between radii 2 and 4.
        AssertClose((18 * 2 + 28 * 2 + Math.PI / 4 * 12) * Width, body.Props().Volume, 1e-3, "Outside bracket volume (mm³)");
        AssertBounds(body, new Vec3(-2, -2, 0), new Vec3(30, 20, 30));
    }

    [AvaloniaTheory(DisplayName = "SHM-H003 | Flange width direction: forward, reversed or centred on the sketch plane")]
    [InlineData(false, false, 0, 30)]
    [InlineData(true, false, -30, 0)]
    [InlineData(false, true, -15, 15)]
    [Trait("TestId", "SHM-H003")]
    public void FlangeWidthDirection(bool reverse, bool centred, double minZ, double maxZ)
    {
        var session = new CadSession();
        var command = Flange(session, LProfile(session));
        SetToggle(command, "Reverse width direction", reverse);
        SetToggle(command, "Centred on the sketch plane", centred);
        Assert.Equal(centred, !command.Handles.Any(h => h.Param.Label == "Width"));   // the width arrow only exists for one-sided stock
        var body = Body(command);
        AssertClose(minZ, body.Props().Min.Z, 1e-6, "Width start (mm)");
        AssertClose(maxZ, body.Props().Max.Z, 1e-6, "Width end (mm)");
    }

    [AvaloniaFact(DisplayName = "SHM-H004 | Flange hem folds the free end back over the metal")]
    [Trait("TestId", "SHM-H004")]
    public void FlangeHem()
    {
        var session = new CadSession();
        var command = Flange(session, LProfile(session));
        double plain = Body(command).Props().Volume;
        SetNumber(command, "Hem length at the end (0 = no hem)", 8);
        SetNumber(command, "Hem gap", 1);
        var hemmed = Body(command);
        Assert.True(hemmed.Props().Volume > plain + 8 * Thickness * Width - 1);
        Assert.Equal("8 long, 1 gap", Result(command, "Hem"));
    }

    [AvaloniaFact(DisplayName = "SHM-H005 | Flange makes a U channel with two bends")]
    [Trait("TestId", "SHM-H005")]
    public void FlangeChannel()
    {
        var session = new CadSession();
        var command = Flange(session, Sketch(session, (0, 20), (0, 0), (30, 0), (30, 20)));
        var body = Body(command);
        Assert.Equal("2", Result(command, "Bends"));
        AssertBounds(body, Vec3.Zero, new Vec3(30, 20, 30));
    }

    [AvaloniaFact(DisplayName = "SHM-H006 | Flange refuses closed profiles, short legs and arcs")]
    [Trait("TestId", "SHM-H006")]
    public void FlangeRefusesBadProfiles()
    {
        var session = new CadSession();
        var closed = CreateRectangleSketch(session, Plane3.XY, 20, 10);
        var command = Flange(session, closed);
        AssertBuildFails(command, "one open chain");
        session.CancelCommand();

        command = Flange(session, Sketch(session, (0, 3), (0, 0), (30, 0)));
        AssertBuildFails(command, "too short for the bends");
        session.CancelCommand();

        var model = new SketchModel();
        model.AddCircle(model.AddPoint(0, 0), 5);
        session.EnterSketch(Plane3.XY, model);
        session.ExitSketch(keep: true);
        command = Flange(session, session.Objects.Last(o => o.Kind == ObjKind.Sketch));
        AssertBuildFails(command, "straight lines only");
    }

    // ------------------------------------------------------------------ Flat Pattern

    [AvaloniaTheory(DisplayName = "SHM-H007 | Flat Pattern unrolls the bracket using the K-factor bend allowance")]
    [InlineData(0.44)]
    [InlineData(0.33)]
    [InlineData(0.5)]
    [Trait("TestId", "SHM-H007")]
    public void FlatPatternUsesKFactor(double k)
    {
        var session = new CadSession();
        var flange = Flange(session, LProfile(session));
        session.CommitCommand();
        var bracket = session.Objects.Last(o => o.Kind == ObjKind.Solid);
        var command = Begin(session, () => new FlatPatternCommand());
        Pick(command, "Sheet metal body", bracket);
        SetNumber(command, "K-factor", k);
        var output = BuildOutput(command);
        double allowance = Math.PI / 2 * (Radius + k * Thickness);
        double length = 16 + 26 + allowance;
        var flat = Assert.Single(output.Add, item => item.Kind == ObjKind.Surface).Shape!;
        AssertClose(length * Width, flat.Props().Area, 1e-2, "Flat area (mm²)");
        Assert.Equal($"{length:0.##} × {Width:0.##} mm", Result(command, "Flat size"));
        Assert.Equal($"90°, allowance {allowance:0.##} mm", Result(command, "Bend"));
        Assert.Equal("2 mm", Result(command, "Thickness"));
        Assert.Single(output.Add, item => item.Kind == ObjKind.Curve);
    }

    [AvaloniaTheory(DisplayName = "SHM-H008 | Flat Pattern lies flat beside the part or at its own position")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "SHM-H008")]
    public void FlatPatternPlacement(bool beside)
    {
        var session = new CadSession();
        Flange(session, LProfile(session));
        session.CommitCommand();
        var bracket = session.Objects.Last(o => o.Kind == ObjKind.Solid);
        var command = Begin(session, () => new FlatPatternCommand());
        Pick(command, "Sheet metal body", bracket);
        SetToggle(command, "Place it beside the part", beside);
        var flat = Assert.Single(BuildOutput(command).Add, item => item.Kind == ObjKind.Surface).Shape!.Props();
        AssertClose(0, flat.Size.Z, 1e-6, "Flat pattern thickness (mm)");
        if (beside) Assert.True(flat.Min.X >= bracket.Shape.Props().Max.X + 19.99);
    }

    [AvaloniaFact(DisplayName = "SHM-H009 | Flat Pattern refuses a body with no bends")]
    [Trait("TestId", "SHM-H009")]
    public void FlatPatternRefusesFlatBody()
    {
        var session = new CadSession();
        var plate = CreateBoxBody(session, Vec3.Zero, 30, 20, 2);
        var command = Begin(session, () => new FlatPatternCommand());
        Pick(command, "Sheet metal body", plate);
        AssertBuildFails(command, "no bends");
    }

    [AvaloniaFact(DisplayName = "SHM-H010 | Flat Pattern keeps a round-ended slot as a hole instead of mistaking its ends for bends")]
    [Trait("TestId", "SHM-H010")]
    public void FlatPatternIgnoresSlotEnds()
    {
        var session = new CadSession();
        var flange = Flange(session, LProfile(session));
        var bracket = Body(flange);
        // a 6 mm wide, 16 mm long round-ended slot through the 2 mm leg that runs along +X (metal y in [0, 2], width along Z); its ends are partial cylinders with axes through the metal
        var outline = Occ.Profile([
            Occ.Line(new(15, -1, 10), new(15, -1, 20)), Occ.Arc3(new(15, -1, 20), new(18, -1, 23), new(21, -1, 20)),
            Occ.Line(new(21, -1, 20), new(21, -1, 10)), Occ.Arc3(new(21, -1, 10), new(18, -1, 7), new(15, -1, 10)),
        ], true);
        var slot = Occ.Extrude(outline, new Vec3(0, 4, 0));
        var slotted = session.AddObject(Occ.Cut(bracket, slot), "Slotted bracket", ObjKind.Solid);
        double slotArea = Math.PI * 9 + 6 * 10;
        AssertClose(bracket.Props().Volume - slotArea * Thickness, slotted.Shape.Props().Volume, 1e-3, "Volume after the slot (mm³)");

        var command = Begin(session, () => new FlatPatternCommand());
        Pick(command, "Sheet metal body", slotted);
        var output = BuildOutput(command);
        double length = 16 + 26 + Math.PI / 2 * (Radius + 0.44 * Thickness);
        var flat = Assert.Single(output.Add, item => item.Kind == ObjKind.Surface).Shape!;
        AssertClose(length * Width - slotArea, flat.Props().Area, 0.05, "Flat area with the slot as a hole (mm², arcs drawn as 0.05 mm chords)");
        Assert.Single(command.Results, line => line.Label == "Bend");
    }

    // ------------------------------------------------------------------ Flatten / Refold

    static (CadSession Session, SceneObject Cylinder, SelItem Wall) Cylinder()
    {
        var session = new CadSession();
        var cylinder = session.AddObject(CylinderAt(Vec3.Zero, 25, 70), "Cylinder", ObjKind.Solid);
        var wall = new SelItem(cylinder.Id, SelKind.Face, Enumerable.Range(0, cylinder.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = cylinder.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));
        return (session, cylinder, wall);
    }

    [AvaloniaTheory(DisplayName = "SHM-H010 | Flatten unrolls a radius 25 × 70 mm cylinder wall to 157.08 × 70 mm")]
    [InlineData("Flat surface and outline", 1, 1)]
    [InlineData("Flat surface only", 1, 0)]
    [InlineData("Outline curve only", 0, 1)]
    [Trait("TestId", "SHM-H010")]
    public void FlattenCylinder(string create, int surfaces, int curves)
    {
        var (session, cylinder, wall) = Cylinder();
        var command = Begin(session, () => new FlattenCommand());
        Assert.Equal("Flatten", command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Operation").Selected);
        Pick(command, "Faces or a surface to flatten", wall);
        SetChoice(command, "Create", create);
        SetChoice(command, "Place the flat pattern", "At the origin (on the ground plane)");
        var output = BuildOutput(command);
        Assert.Equal(surfaces, output.Add.Count(item => item.Kind == ObjKind.Surface));
        Assert.Equal(curves, output.Add.Count(item => item.Kind == ObjKind.Curve));
        var piece = output.Add.First().Shape!.Props();
        AssertClose(2 * Math.PI * 25, piece.Size.X, 1e-4, "Unrolled length (mm)");
        AssertClose(70, piece.Size.Y, 1e-4, "Unrolled height (mm)");
        AssertClose(0, piece.Size.Z, 1e-6, "Flat (mm)");
        Assert.Empty(output.Remove);
        Assert.Equal(cylinder.Shape.Props().Volume, session.Find(cylinder.Id)!.Shape.Props().Volume);
    }

    [AvaloniaFact(DisplayName = "SHM-H011 | Refold returns a flattened cylinder wall to its 25 mm radius with the same area")]
    [Trait("TestId", "SHM-H011")]
    public void RefoldReturnsFace()
    {
        var (session, _, wall) = Cylinder();
        var flatten = Begin(session, () => new FlattenCommand());
        Pick(flatten, "Faces or a surface to flatten", wall);
        SetChoice(flatten, "Create", "Flat surface only");
        session.CommitCommand();
        var pattern = session.Objects.Last(o => o.Kind == ObjKind.Surface);
        Assert.NotNull(pattern.FlatMap);

        var refold = Begin(session, () => new RefoldCommand());
        Pick(refold, "Original flat pattern", pattern);
        var face = Assert.Single(BuildOutput(refold).Add).Shape!;
        AssertClose(2 * Math.PI * 25 * 70, face.Props().Area, 1e-2, "Refolded area (mm²)");
        AssertClose(50, face.Props().Size.X, 1e-3, "Refolded diameter (mm)");
        Assert.Equal("0", Result(refold, "Preserved cutouts"));
    }

    [AvaloniaFact(DisplayName = "SHM-H012 | Refold needs the face or a drawing to return")]
    [Trait("TestId", "SHM-H012")]
    public void RefoldNeedsSomething()
    {
        var (session, _, wall) = Cylinder();
        var flatten = Begin(session, () => new FlattenCommand());
        Pick(flatten, "Faces or a surface to flatten", wall);
        SetChoice(flatten, "Create", "Flat surface only");
        session.CommitCommand();
        var pattern = session.Objects.Last(o => o.Kind == ObjKind.Surface);
        var refold = Begin(session, () => new RefoldCommand());
        Pick(refold, "Original flat pattern", pattern);
        SetToggle(refold, "Create a copy of the curved face", false);
        AssertBuildFails(refold, "Enable face refolding or select a drawing");
    }
}
