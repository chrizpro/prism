using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.SheetMetal;

/// <summary>
/// Characterizes Fold: a flat sketch (outline as normal lines, bends as construction lines) folded into sheet metal. Fixtures use 2 mm metal, a 3 mm inside
/// radius and K = 0.44, so a 90° bend allows π/2 · (3 + 0.44 · 2) = 6.0947 mm of flat. Expected volumes and sizes come from straight legs plus bend sectors,
/// and from the Flange tool building the same part, not from reading Fold's own output.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sheet-metal/README.md">Living guide.</seealso>
[Trait("Feature", "SheetMetal")]
[Trait("Layer", "Headless")]
public class FoldTests
{
    const double T = 2, R = 3, K = 0.44;
    static readonly double Allowance = Math.PI / 2 * (R + K * T);

    /// <summary>Flat sketch on XY: the outline loop, optional round holes and construction bend lines.</summary>
    static SceneObject FlatSketch(CadSession session, (double u, double v)[] outline, ((double u, double v) a, (double u, double v) b)[] bends,
        ((double u, double v) centre, double radius)[]? holes = null)
    {
        var model = new SketchModel();
        var ids = outline.Select(p => model.AddPoint(p.u, p.v)).ToArray();
        for (int i = 0; i < ids.Length; i++) model.AddLine(ids[i], ids[(i + 1) % ids.Length]);
        foreach (var (centre, radius) in holes ?? []) model.AddCircle(model.AddPoint(centre.u, centre.v), radius);
        foreach (var (a, b) in bends) model.AddLine(model.AddPoint(a.u, a.v), model.AddPoint(b.u, b.v), construction: true);
        session.EnterSketch(Plane3.XY, model);
        session.ExitSketch(keep: true);
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    static (double u, double v)[] Rectangle(double width, double height) => [(0, 0), (width, 0), (width, height), (0, height)];

    static FoldCommand Fold(CadSession session, SceneObject sketch, string? angles = null, double angle = 90)
    {
        var command = Begin(session, () => new FoldCommand());
        Pick(command, "Flat sketch (outline and bend lines)", sketch);
        SetNumber(command, "Thickness", T);
        SetNumber(command, "Bend radius (inside)", R);
        SetNumber(command, "Bend angle", angle);
        if (angles != null) SetText(command, "Angle of each bend (optional)", angles);
        return command;
    }

    static Shape Body(Command command)
    {
        var added = Assert.Single(BuildOutput(command).Add, item => item.Kind == ObjKind.Solid);
        AssertValidSolid(added.Shape!);
        return added.Shape!;
    }

    /// <summary>L bracket blank: 17 mm and 27 mm of straight leg either side of a 90° bend, 40 mm wide.</summary>
    static (CadSession session, SceneObject sketch, double length) LBlank()
    {
        var session = new CadSession();
        double length = 17 + 27 + Allowance, line = 17 + Allowance / 2;
        return (session, FlatSketch(session, Rectangle(length, 40), [((line, -5), (line, 45))]), length);
    }

    [AvaloniaFact(DisplayName = "FLD-H001 | Fold turns an L blank into the same bracket Flange draws from its profile")]
    [Trait("TestId", "FLD-H001")]
    public void FoldMatchesFlange()
    {
        var (session, sketch, _) = LBlank();
        var foldCommand = Fold(session, sketch);
        var folded = Body(foldCommand);
        // Flange: the profile is the inside line (metal on the outside of the turn); legs of 20 and 30 mm measured to the sharp corner.
        var flangeSession = new CadSession();
        var profileModel = new SketchModel();
        var p = new[] { profileModel.AddPoint(0, 20), profileModel.AddPoint(0, 0), profileModel.AddPoint(30, 0) };
        profileModel.AddLine(p[0], p[1]); profileModel.AddLine(p[1], p[2]);
        flangeSession.EnterSketch(Plane3.XY, profileModel); flangeSession.ExitSketch(keep: true);
        var flange = Begin(flangeSession, () => new ContourFlangeCommand());
        Pick(flange, "Profile sketch (straight lines)", flangeSession.Objects.Last(o => o.Kind == ObjKind.Sketch));
        SetNumber(flange, "Thickness", T); SetNumber(flange, "Bend radius (inside)", R); SetNumber(flange, "Width", 40);
        SetChoice(flange, "The metal lies", "Right of the profile");
        var reference = Body(flange);
        double section = 17 * T + 27 * T + Math.PI / 4 * ((R + T) * (R + T) - R * R);
        AssertClose(section * 40, folded.Props().Volume, 1e-3, "Bracket volume from legs and bend sector (mm³)");
        AssertClose(reference.Props().Volume, folded.Props().Volume, 1e-3, "Volume against Flange (mm³)");
        var size = folded.Props().Size; var expected = reference.Props().Size;
        var got = new[] { size.X, size.Y, size.Z }.Order().ToArray(); var want = new[] { expected.X, expected.Y, expected.Z }.Order().ToArray();
        for (int i = 0; i < 3; i++) AssertClose(want[i], got[i], 1e-3, "Sorted bounding box size (mm)");
        Assert.Equal("1", Result(foldCommand, "Bends"));
    }

    [AvaloniaFact(DisplayName = "FLD-H002 | A folded L bracket unfolds with Flat Pattern to the blank it was folded from")]
    [Trait("TestId", "FLD-H002")]
    public void FoldThenFlatPatternReturnsBlank()
    {
        var (session, sketch, length) = LBlank();
        Fold(session, sketch); session.CommitCommand();
        var bracket = session.Objects.Last(o => o.Kind == ObjKind.Solid);
        var command = Begin(session, () => new FlatPatternCommand());
        Pick(command, "Sheet metal body", bracket);
        SetNumber(command, "K-factor", K);
        var flat = Assert.Single(BuildOutput(command).Add, item => item.Kind == ObjKind.Surface).Shape!.Props();
        AssertClose(length, flat.Size.X, 1e-3, "Unfolded length (mm)");
        AssertClose(40, flat.Size.Y, 1e-3, "Unfolded width (mm)");
    }

    /// <summary>A plus-shaped blank: a 100 × 60 base with a 30 mm wall past a bend zone on each side.</summary>
    static (CadSession session, SceneObject sketch) TrayBlank()
    {
        var session = new CadSession();
        double e = Allowance + 30, x = 50 + Allowance / 2, y = 30 + Allowance / 2;
        var outline = new (double, double)[]
        {
            (50, -30), (50 + e, -30), (50 + e, 30), (50, 30), (50, 30 + e), (-50, 30 + e), (-50, 30),
            (-50 - e, 30), (-50 - e, -30), (-50, -30), (-50, -30 - e), (50, -30 - e),
        };
        return (session, FlatSketch(session, outline, [((-x, -100), (-x, 100)), ((-150, -y), (150, -y)), ((-150, y), (150, y)), ((x, -100), (x, 100))]));
    }

    [AvaloniaFact(DisplayName = "FLD-H003 | Four 90° bends fold a plus-shaped blank into a tray with the closed-form size and volume")]
    [Trait("TestId", "FLD-H003")]
    public void FoldMakesTray()
    {
        var (session, sketch) = TrayBlank();
        var command = Fold(session, sketch);
        var tray = Body(command);
        // outside half-length 50 + r + t = 55, half-width 30 + r + t = 35; walls stand 30 + r above the 2 mm base inside face
        AssertBounds(tray, new Vec3(-55, -35, 0), new Vec3(55, 35, T + R + 30));
        double sector = Math.PI / 2 * T * (R + T / 2);                       // per mm of bend length: θ · t · (r + t / 2)
        double volume = T * (100 * 60 + 2 * 30 * 60 + 2 * 30 * 100) + sector * (2 * 60 + 2 * 100);
        AssertClose(volume, tray.Props().Volume, 1e-3, "Tray volume (mm³)");
        Assert.Equal("4", Result(command, "Bends"));
    }

    [AvaloniaFact(DisplayName = "FLD-H004 | A hole drawn on the flat sheet is a hole in the folded part")]
    [Trait("TestId", "FLD-H004")]
    public void HolesSurviveFolding()
    {
        var session = new CadSession();
        double length = 17 + 27 + Allowance, line = 17 + Allowance / 2;
        var sketch = FlatSketch(session, Rectangle(length, 40), [((line, -5), (line, 45))], [((37, 20), 5)]);
        var body = Body(Fold(session, sketch));
        double section = 17 * T + 27 * T + Math.PI / 4 * ((R + T) * (R + T) - R * R);
        AssertClose(section * 40 - Math.PI * 25 * T, body.Props().Volume, 1e-3, "Volume with the hole (mm³)");
    }

    [AvaloniaFact(DisplayName = "FLD-H005 | Signed angles fold bends opposite ways into a Z channel")]
    [Trait("TestId", "FLD-H005")]
    public void SignedAnglesFoldOppositeWays()
    {
        var session = new CadSession();
        double length = 20 + Allowance + 40 + Allowance + 20, a = 20 + Allowance / 2, b = 20 + Allowance + 40 + Allowance / 2;
        var sketch = FlatSketch(session, Rectangle(length, 30), [((a, -5), (a, 35)), ((b, -5), (b, 35))]);
        var z = Body(Fold(session, sketch, angles: "90, -90"));
        // the 40 mm middle stays; one leg rises 20 + r above its 2 mm top, the other drops 20 + r below its underside
        double baseStart = 20 + Allowance;                                        // the middle region begins where the first bend zone ends
        AssertBounds(z, new Vec3(baseStart - R - T, 0, -(20 + R)), new Vec3(baseStart + 40 + R + T, 30, T + 20 + R));
        var same = Body(Fold(session, sketch, angles: "90, 90"));
        Assert.True(same.Props().Size.Z < z.Props().Size.Z - 10, "Both legs folding the same way must be shorter overall than the Z channel");
    }

    [AvaloniaFact(DisplayName = "FLD-H006 | Fold rejects sketches it cannot fold and says why")]
    [Trait("TestId", "FLD-H006")]
    public void FoldRejectsBadSketches()
    {
        var session = new CadSession();
        var plain = FlatSketch(session, Rectangle(60, 40), []);
        AssertBuildFails(Fold(session, plain), "construction line");

        var outside = FlatSketch(session, Rectangle(60, 40), [((200, -5), (200, 45))]);
        AssertBuildFails(Fold(session, outside), "does not cross the sheet");

        var cross = FlatSketch(session, Rectangle(80, 80), [((10, 10), (70, 70)), ((10, 70), (70, 10))]);
        AssertBuildFails(Fold(session, cross), "cross inside the sheet");

        var ok = FlatSketch(session, Rectangle(60, 40), [((30, -5), (30, 45))]);
        AssertBuildFails(Fold(session, ok, angles: "ninety"), "not an angle");
        AssertBuildFails(Fold(session, ok, angles: "90, 90"), "2 angles");
        AssertBuildFails(Fold(session, ok, angle: 90, angles: "181"), "181");
    }

    [AvaloniaFact(DisplayName = "FLD-H007 | Fold refuses a bend that runs through a hole")]
    [Trait("TestId", "FLD-H007")]
    public void FoldRefusesHoleInBendZone()
    {
        var session = new CadSession();
        var sketch = FlatSketch(session, Rectangle(60, 40), [((30, -5), (30, 45))], [((30, 20), 4)]);
        AssertBuildFails(Fold(session, sketch), "hole, slot or notch");
    }

    [AvaloniaFact(DisplayName = "FLD-H008 | Fold lays out the flat pattern beside the part with every hole and a line for each bend zone")]
    [Trait("TestId", "FLD-H008")]
    public void FoldMakesFlatPattern()
    {
        var (session, sketch) = TrayBlank();
        var command = Fold(session, sketch);
        var output = BuildOutput(command);
        double e = Allowance + 30;
        var flat = Assert.Single(output.Add, item => item.Kind == ObjKind.Surface).Shape!.Props();
        // the plus-shaped blank: 100 x 60 base, 30 + allowance of flap on each side
        AssertClose(100 * 60 + 2 * e * 60 + 2 * e * 100, flat.Area, 1e-3, "Flat pattern area (mm²)");
        AssertClose(100 + 2 * e, flat.Size.X, 1e-3, "Flat pattern length (mm)");
        AssertClose(0, flat.Size.Z, 1e-6, "Flat pattern thickness (mm)");
        var tray = Assert.Single(output.Add, item => item.Kind == ObjKind.Solid).Shape!.Props();
        Assert.True(flat.Min.X >= tray.Max.X + 19.99, "The flat pattern lies beside the folded part");
        var bendLines = Assert.Single(output.Add, item => item.Kind == ObjKind.Curve).Shape!;
        Assert.Equal(4, bendLines.Count(SubKind.Edge));
    }

    [AvaloniaFact(DisplayName = "FLD-H009 | The flat pattern can be left out, and holes stay holes in it")]
    [Trait("TestId", "FLD-H009")]
    public void FlatPatternOptionalWithHoles()
    {
        var session = new CadSession();
        double length = 17 + 27 + Allowance, line = 17 + Allowance / 2;
        var sketch = FlatSketch(session, Rectangle(length, 40), [((line, -5), (line, 45))], [((37, 20), 5)]);
        var withFlat = BuildOutput(Fold(session, sketch));
        var flat = Assert.Single(withFlat.Add, item => item.Kind == ObjKind.Surface).Shape!;
        AssertClose(length * 40 - Math.PI * 25, flat.Props().Area, 1e-3, "Flat area with the hole (mm²)");
        Assert.Equal(2, flat.Sub(SubKind.Face, 0).Count(SubKind.Wire));
        var command = Fold(session, sketch);
        SetToggle(command, "Also make the flat pattern beside the part", false);
        Assert.Single(BuildOutput(command).Add);
    }

    [AvaloniaFact(DisplayName = "FLD-H010 | Each bend can have its own inside radius, and bad radii are named")]
    [Trait("TestId", "FLD-H010")]
    public void PerBendRadii()
    {
        var session = new CadSession();
        double a1 = Math.PI / 2 * (3 + K * T), a2 = Math.PI / 2 * (8 + K * T);
        double x1 = 20 + a1 / 2, x2 = 20 + a1 + 40 + a2 / 2, length = 20 + a1 + 40 + a2 + 20;
        var sketch = FlatSketch(session, Rectangle(length, 30), [((x1, -5), (x1, 35)), ((x2, -5), (x2, 35))]);
        var command = Fold(session, sketch);
        SetText(command, "Radius of each bend (optional)", "3, 8");
        var body = Body(command);
        double sector(double r) => Math.PI / 2 * T * (r + T / 2);                   // per mm of bend length
        AssertClose(T * 30 * 80 + 30 * (sector(3) + sector(8)), body.Props().Volume, 1e-3, "Volume with radii 3 and 8 (mm³)");
        Assert.Contains("inside radius 8 mm", Result(command, "Bend 2"));
        var bad = Fold(session, sketch);
        SetText(bad, "Radius of each bend (optional)", "3, wide");
        AssertBuildFails(bad, "not a bend radius");
        var many = Fold(session, sketch);
        SetText(many, "Radius of each bend (optional)", "3, 3, 3");
        AssertBuildFails(many, "3 radii");
    }

    [Fact(DisplayName = "FLD-H011 | The flat pattern DXF is written with outlines and bend lines")]
    [Trait("TestId", "FLD-H011")]
    public void FlatPatternDxfIsWritten()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prism-fold-dxf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = FlatDxf.Save(directory, "My tray", [[(0, 0), (60, 0), (60, 40), (0, 40)]], [((30, 0), (30, 40))]);
            Assert.Equal("My_tray_flat.dxf", Path.GetFileName(path));
            var text = File.ReadAllText(path);
            Assert.Contains("ENTITIES", text);
            Assert.True(text.Length > 200, "The DXF has content");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>A 40 mm base with a 180° hem-style fold at each end, the legs of the given length lying back over the base.</summary>
    static (CadSession session, SceneObject sketch) DoubleBackBlank(double leg)
    {
        var session = new CadSession();
        double a = Math.PI * (R + K * T), x1 = leg + a / 2, x2 = leg + a + 40 + a / 2, length = leg + a + 40 + a + leg;
        return (session, FlatSketch(session, Rectangle(length, 30), [((x1, -5), (x1, 35)), ((x2, -5), (x2, 35))]));
    }

    [AvaloniaFact(DisplayName = "FLD-H012 | Fold refuses flanges that overlap each other instead of fusing them silently")]
    [Trait("TestId", "FLD-H012")]
    public void FoldRefusesOverlappingFlanges()
    {
        // two 30 mm legs folded back over a 40 mm base meet and cross in the middle
        var (session, sketch) = DoubleBackBlank(30);
        AssertBuildFails(Fold(session, sketch, angles: "180, 180"), "overlap");
        // 15 mm legs leave a gap between them: the same part is fine and keeps its full volume
        var (okSession, okSketch) = DoubleBackBlank(15);
        var body = Body(Fold(okSession, okSketch, angles: "180, 180"));
        double sector = Math.PI * T * (R + T / 2);                                 // a 180° bend, per mm of its length
        AssertClose(T * 30 * (15 + 40 + 15) + 30 * 2 * sector, body.Props().Volume, 1e-3, "Volume of the double-back part (mm³)");
    }

    [AvaloniaFact(DisplayName = "FLD-H013 | The fixed region can be the one over the sketch origin instead of the largest")]
    [Trait("TestId", "FLD-H013")]
    public void FixedRegionOverTheOrigin()
    {
        // the 17 mm leg is under the sketch origin (x from 0); the 27 mm leg is the larger region
        var (session, sketch, _) = LBlank();
        var largest = Body(Fold(session, sketch));
        AssertClose(27 + R + T, largest.Props().Size.X, 1e-3, "Width with the long leg fixed (mm)");
        var command = Fold(session, sketch);
        SetChoice(command, "Fixed region", "The region over the sketch origin");
        var held = Body(command);
        // the 17 mm leg stays flat; the 27 mm leg rises: outside face at 17 + r + t, top at the 2 mm inside face plus r plus 27
        var size = held.Props().Size;
        AssertClose(17 + R + T, size.X, 1e-3, "Width with the short leg fixed (mm)");
        AssertClose(T + R + 27, size.Z, 1e-3, "Height with the short leg fixed (mm)");
        AssertClose(largest.Props().Volume, held.Props().Volume, 1e-3, "Same part, same volume (mm³)");

        var off = FlatSketch(session, Rectangle(60, 40), [((30, -5), (30, 45))]);
        var moved = Fold(session, off);
        SetChoice(moved, "Fixed region", "The region over the sketch origin");
        Body(moved);
        var away = new Prism.Core.Sketching.SketchModel();
        foreach (var (a, b) in new[] { ((100.0, 100.0), (160.0, 100.0)), ((160.0, 100.0), (160.0, 140.0)), ((160.0, 140.0), (100.0, 140.0)), ((100.0, 140.0), (100.0, 100.0)) })
            away.AddLine(away.AddPoint(a.Item1, a.Item2), away.AddPoint(b.Item1, b.Item2));
        away.AddLine(away.AddPoint(130, 95), away.AddPoint(130, 145), construction: true);
        session.EnterSketch(Plane3.XY, away); session.ExitSketch(keep: true);
        var elsewhere = Fold(session, session.Objects.Last(o => o.Kind == ObjKind.Sketch));
        SetChoice(elsewhere, "Fixed region", "The region over the sketch origin");
        AssertBuildFails(elsewhere, "origin is not on the sheet");
    }

    [AvaloniaFact(DisplayName = "FLD-H014 | A bend line is infinite: drawing it far from the sheet folds the same part as drawing it across the sheet")]
    [Trait("TestId", "FLD-H014")]
    public void BendLineMayBeDrawnFarAway()
    {
        double length = 17 + 27 + Allowance, line = 17 + Allowance / 2;
        var near = new CadSession();
        var nearBody = Body(Fold(near, FlatSketch(near, Rectangle(length, 40), [((line, -5), (line, 45))])));
        // the same line on x = 17 + allowance / 2, but with both of its points hundreds of millimetres from the sheet
        var far = new CadSession();
        var farBody = Body(Fold(far, FlatSketch(far, Rectangle(length, 40), [((line, -900), (line, -800))])));
        AssertClose(nearBody.Props().Volume, farBody.Props().Volume, 1e-4, "Volume with a distant bend line (mm³)");
        AssertClose(nearBody.Props().Size.X, farBody.Props().Size.X, 1e-4, "Width with a distant bend line (mm)");
    }
}
