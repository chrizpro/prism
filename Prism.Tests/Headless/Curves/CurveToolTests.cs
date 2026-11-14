using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <summary>
/// Characterizes the NURBS/Surface curve-editing tools: fillet/chamfer, offset, blend, join, explode, trim, split,
/// extend, divide, rebuild, project, intersect surfaces, duplicate edge and the curvature graph.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/curves/README.md">Living guide.</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class CurveToolTests
{
    // An open L: 20 mm along X, then 20 mm along Y, in the XY plane.
    static SceneObject LCurve(CadSession session) =>
        CreateCurve(session, [new Vec3(0, 0, 0), new Vec3(20, 0, 0), new Vec3(20, 20, 0)]);

    static SceneObject StraightLine(CadSession session, Vec3 start, Vec3 end) =>
        session.AddObject(Occ.Line(start, end), "Line", ObjKind.Curve);

    static Shape SingleCurve(CommandOutput output)
    {
        var added = Assert.Single(output.Add);
        Assert.Equal(ObjKind.Curve, added.Kind);
        return added.Shape!;
    }

    // ------------------------------------------------------------------ Fillet / Chamfer Curve

    [AvaloniaFact(DisplayName = "CRV-H001 | Fillet Curve rounds the corner of an open L with the set radius")]
    [Trait("TestId", "CRV-H001")]
    public void FilletCurveRoundsCorner()
    {
        var session = new CadSession();
        var source = LCurve(session);
        var command = Begin(session, () => new CurveFilletCommand(chamfer: false));
        Pick(command, "Flat curve or sketch, or edges of a surface", source);
        SetNumber(command, "Fillet radius", 5);
        var curve = SingleCurve(BuildOutput(command));
        AssertClose(40 - 10 + Math.PI / 2 * 5, Length(curve), 1e-3, "Filleted length (mm)");
        var (start, end) = Ends(curve);
        AssertPoint(Vec3.Zero, start, 1e-6, "Open start is kept");
        AssertPoint(new Vec3(20, 20, 0), end, 1e-6, "Open end is kept");
    }

    [AvaloniaFact(DisplayName = "CRV-H002 | Chamfer Curve cuts the corner of an open L by the set distance")]
    [Trait("TestId", "CRV-H002")]
    public void ChamferCurveCutsCorner()
    {
        var session = new CadSession();
        var source = LCurve(session);
        var command = Begin(session, () => new CurveFilletCommand(chamfer: true));
        Assert.Equal("Chamfer (cut)", command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Corners").Selected);
        Pick(command, "Flat curve or sketch, or edges of a surface", source);
        SetNumber(command, "Chamfer distance", 3);
        var curve = SingleCurve(BuildOutput(command));
        AssertClose(40 - 6 + 3 * Math.Sqrt(2), Length(curve), 1e-6, "Chamfered length (mm)");
    }

    [AvaloniaFact(DisplayName = "CRV-H003 | Fillet Curve shows only the size that matches the chosen corner type")]
    [Trait("TestId", "CRV-H003")]
    public void FilletCurveShowsMatchingSize()
    {
        var session = new CadSession();
        var command = Begin(session, () => new CurveFilletCommand(chamfer: false));
        var radius = command.Params.Single(p => p.Label == "Fillet radius");
        var distance = command.Params.Single(p => p.Label == "Chamfer distance");
        Assert.True(radius.IsVisible);
        Assert.False(distance.IsVisible);
        SetChoice(command, "Corners", "Chamfer (cut)");
        Assert.False(radius.IsVisible);
        Assert.True(distance.IsVisible);
    }

    [AvaloniaTheory(DisplayName = "CRV-H004 | Fillet Curve keeps or consumes the original curve")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "CRV-H004")]
    public void FilletCurveKeepOriginal(bool keep)
    {
        var session = new CadSession();
        var source = LCurve(session);
        var command = Begin(session, () => new CurveFilletCommand(chamfer: false));
        Pick(command, "Flat curve or sketch, or edges of a surface", source);
        SetToggle(command, "Keep the original curve", keep);
        var output = BuildOutput(command);
        if (keep) Assert.Empty(output.Remove);
        else Assert.Equal(source.Id, Assert.Single(output.Remove).Id);
    }

    // ------------------------------------------------------------------ Offset Curve

    [AvaloniaTheory(DisplayName = "CRV-H005 | Offset Curve grows a closed 20 mm square with rounded or sharp corners")]
    [InlineData("Rounded", 80 + 2 * Math.PI * 5)]
    [InlineData("Sharp", 120)]
    [Trait("TestId", "CRV-H005")]
    public void OffsetCurveGrowsSquare(string corners, double expectedLength)
    {
        var session = new CadSession();
        var source = CreateCurve(session, [new Vec3(0, 0, 0), new Vec3(20, 0, 0), new Vec3(20, 20, 0), new Vec3(0, 20, 0)], closed: true);
        var command = Begin(session, () => new OffsetCurveCommand());
        Pick(command, "Flat curve or sketch", source);
        SetNumber(command, "Offset distance", 5);
        SetChoice(command, "Corners", corners);
        var curve = SingleCurve(BuildOutput(command));
        AssertClose(expectedLength, Length(curve), 1e-3, "Offset perimeter (mm)");
        AssertBounds(curve, new Vec3(-5, -5, 0), new Vec3(25, 25, 0));
    }

    [AvaloniaFact(DisplayName = "CRV-H006 | A negative Offset Curve distance shrinks a closed square")]
    [Trait("TestId", "CRV-H006")]
    public void NegativeOffsetShrinksSquare()
    {
        var session = new CadSession();
        var source = CreateCurve(session, [new Vec3(0, 0, 0), new Vec3(20, 0, 0), new Vec3(20, 20, 0), new Vec3(0, 20, 0)], closed: true);
        var command = Begin(session, () => new OffsetCurveCommand());
        Pick(command, "Flat curve or sketch", source);
        SetNumber(command, "Offset distance", -5);
        var curve = SingleCurve(BuildOutput(command));
        AssertClose(40, Length(curve), 1e-3, "Shrunk perimeter (mm)");
        AssertBounds(curve, new Vec3(5, 5, 0), new Vec3(15, 15, 0));
        Assert.Empty(BuildOutput(command).Remove);
    }

    // ------------------------------------------------------------------ Blend Curve

    [AvaloniaTheory(DisplayName = "CRV-H007 | Blend Curve joins the nearest free ends at every continuity")]
    [InlineData("Position (G0)")]
    [InlineData("Tangent (G1)")]
    [InlineData("Curvature (G2)")]
    [Trait("TestId", "CRV-H007")]
    public void BlendCurveJoinsNearestEnds(string continuity)
    {
        var session = new CadSession();
        var first = StraightLine(session, new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        var second = StraightLine(session, new Vec3(20, 10, 0), new Vec3(30, 10, 0));
        var command = Begin(session, () => new BlendCurveCommand());
        Pick(command, "First curve", first);
        Pick(command, "Second curve", second);
        SetChoice(command, "Continuity", continuity);
        var blend = SingleCurve(BuildOutput(command));
        var (start, end) = Ends(blend);
        AssertPoint(new Vec3(10, 0, 0), start, 1e-6, "Blend start");
        AssertPoint(new Vec3(20, 10, 0), end, 1e-6, "Blend end");
        double chord = Math.Sqrt(200);
        if (continuity == "Position (G0)") AssertClose(chord, Length(blend), 1e-6, "Straight blend length (mm)");
        else Assert.True(Length(blend) > chord + 0.1, "A tangent blend leaves each line along its direction, so it is longer than the chord.");
    }

    [AvaloniaFact(DisplayName = "CRV-H008 | A larger Blend Curve bulge makes a fuller, longer tangent blend")]
    [Trait("TestId", "CRV-H008")]
    public void BlendCurveBulgeLengthens()
    {
        var session = new CadSession();
        var first = StraightLine(session, new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        var second = StraightLine(session, new Vec3(20, 10, 0), new Vec3(30, 10, 0));
        double LengthWithBulge(double bulge)
        {
            var command = Begin(session, () => new BlendCurveCommand());
            Pick(command, "First curve", first);
            Pick(command, "Second curve", second);
            SetNumber(command, "Bulge at first curve", bulge);
            SetNumber(command, "Bulge at second curve", bulge);
            var length = Length(SingleCurve(BuildOutput(command)));
            session.CancelCommand();
            return length;
        }
        Assert.True(LengthWithBulge(2.5) > LengthWithBulge(0.5) + 0.5);
    }

    [AvaloniaFact(DisplayName = "CRV-H009 | Blend Curve refuses curves whose ends already touch")]
    [Trait("TestId", "CRV-H009")]
    public void BlendCurveRefusesTouchingEnds()
    {
        var session = new CadSession();
        var first = StraightLine(session, new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        var second = StraightLine(session, new Vec3(10, 0, 0), new Vec3(10, 10, 0));
        var command = Begin(session, () => new BlendCurveCommand());
        Pick(command, "First curve", first);
        Pick(command, "Second curve", second);
        AssertBuildFails(command, "already touch");
    }

    // ------------------------------------------------------------------ Join / Explode

    [AvaloniaFact(DisplayName = "CRV-H010 | Join Curves makes one wire from touching lines and consumes them")]
    [Trait("TestId", "CRV-H010")]
    public void JoinCurvesMakesOneWire()
    {
        var session = new CadSession();
        var first = StraightLine(session, new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        var second = StraightLine(session, new Vec3(10, 0, 0), new Vec3(10, 10, 0));
        var command = Begin(session, () => new JoinCurvesCommand());
        Pick(command, "Curves to join", first, second);
        var output = BuildOutput(command);
        var joined = SingleCurve(output);
        Assert.Equal(1, joined.Count(SubKind.Wire));
        Assert.Equal(2, joined.Count(SubKind.Edge));
        AssertClose(20, Length(joined), 1e-6, "Joined length (mm)");
        Assert.Equal(2, output.Remove.Count);
        Assert.Equal("1 curve(s) from 2 segments", Result(command, "Pieces"));
    }

    [AvaloniaFact(DisplayName = "CRV-H011 | Join Curves leaves separated lines as separate pieces and can keep originals")]
    [Trait("TestId", "CRV-H011")]
    public void JoinCurvesKeepsGapsAndOriginals()
    {
        var session = new CadSession();
        var first = StraightLine(session, new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        var second = StraightLine(session, new Vec3(20, 0, 0), new Vec3(30, 0, 0));
        var command = Begin(session, () => new JoinCurvesCommand());
        Pick(command, "Curves to join", first, second);
        SetToggle(command, "Keep the originals", true);
        var output = BuildOutput(command);
        Assert.Equal(2, SingleCurve(output).Count(SubKind.Wire));
        Assert.Empty(output.Remove);
    }

    [AvaloniaTheory(DisplayName = "CRV-H012 | Explode Curve splits a three-segment polyline into three curves")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "CRV-H012")]
    public void ExplodeCurveMakesSegments(bool keep)
    {
        var session = new CadSession();
        var source = CreateCurve(session, [new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(10, 10, 0), new Vec3(0, 10, 0)]);
        var command = Begin(session, () => new ExplodeCurveCommand());
        Pick(command, "Curve or sketch", source);
        SetToggle(command, "Keep the original", keep);
        var output = BuildOutput(command);
        Assert.Equal(3, output.Add.Count);
        Assert.All(output.Add, segment => AssertClose(10, Length(segment.Shape!), 1e-6, "Segment length (mm)"));
        Assert.Equal(keep ? 0 : 1, output.Remove.Count);
        Assert.Equal("3", Result(command, "Segments"));
    }

    [AvaloniaFact(DisplayName = "CRV-H013 | Explode Curve refuses a curve that is already a single segment")]
    [Trait("TestId", "CRV-H013")]
    public void ExplodeCurveRefusesSingleSegment()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(10, 0, 0));
        var command = Begin(session, () => new ExplodeCurveCommand());
        Pick(command, "Curve or sketch", source);
        AssertBuildFails(command, "single segment");
    }

    // ------------------------------------------------------------------ Trim / Split / Extend

    [AvaloniaFact(DisplayName = "CRV-H014 | Trim Curve keeps the 20 % to 70 % portion of a 100 mm line")]
    [Trait("TestId", "CRV-H014")]
    public void TrimCurveKeepsRange()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new TrimCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetNumber(command, "Keep from (%)", 20);
        SetNumber(command, "Keep to (%)", 70);
        var output = BuildOutput(command);
        var (start, end) = Ends(SingleCurve(output));
        AssertPoint(new Vec3(20, 0, 0), start, 1e-6, "Kept start");
        AssertPoint(new Vec3(70, 0, 0), end, 1e-6, "Kept end");
        Assert.Equal(source.Id, Assert.Single(output.Remove).Id);
    }

    [AvaloniaFact(DisplayName = "CRV-H015 | Trim Curve 'Cut into pieces' splits at every listed percentage")]
    [Trait("TestId", "CRV-H015")]
    public void TrimCurveCutsIntoPieces()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new TrimCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetChoice(command, "Action", "Cut into pieces");
        Assert.False(command.Params.Single(p => p.Label == "Keep from (%)").IsVisible);
        Assert.True(command.Params.Single(p => p.Label == "Split at (% , separated)").IsVisible);
        SetText(command, "Split at (% , separated)", "25, 50, 75");
        SetToggle(command, "Keep the original", true);
        var output = BuildOutput(command);
        Assert.Equal(4, output.Add.Count);
        Assert.All(output.Add, piece => AssertClose(25, Length(piece.Shape!), 1e-6, "Piece length (mm)"));
        Assert.Empty(output.Remove);
    }

    [AvaloniaFact(DisplayName = "CRV-H016 | Trim Curve refuses a multi-segment curve")]
    [Trait("TestId", "CRV-H016")]
    public void TrimCurveRefusesPolyline()
    {
        var session = new CadSession();
        var command = Begin(session, () => new TrimCurveCommand());
        Pick(command, "Curve (a single segment)", LCurve(session));
        AssertBuildFails(command, "one segment");
    }

    [AvaloniaTheory(DisplayName = "CRV-H017 | Legacy Split adapter cuts a 100 mm line at the listed percentages")]
    [InlineData("50", new[] { 50.0, 50.0 })]
    [InlineData("10; 40", new[] { 10.0, 30.0, 60.0 })]
    [InlineData("0, 30, 100, 30", new[] { 30.0, 70.0 })]
    [Trait("TestId", "CRV-H017")]
    public void SplitCurveCutsAtPercentages(string positions, double[] lengths)
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new SplitCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetText(command, "Split at (% , separated)", positions);
        var output = BuildOutput(command);
        Assert.Equal(lengths.Length, output.Add.Count);
        for (int i = 0; i < lengths.Length; i++)
            AssertClose(lengths[i], Length(output.Add[i].Shape!), 1e-6, $"Piece {i + 1} length (mm)");
        Assert.Equal(source.Id, Assert.Single(output.Remove).Id);
    }

    [AvaloniaFact(DisplayName = "CRV-H018 | Legacy Split adapter refuses positions outside 0–100 % and can keep the original")]
    [Trait("TestId", "CRV-H018")]
    public void SplitCurveValidatesPositions()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new SplitCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetText(command, "Split at (% , separated)", "abc, 150");
        AssertBuildFails(command, "between 0 and 100");
        SetText(command, "Split at (% , separated)", "50");
        SetToggle(command, "Keep the original", true);
        Assert.Empty(BuildOutput(command).Remove);
    }

    [AvaloniaTheory(DisplayName = "CRV-H019 | Extend Curve lengthens a 100 mm line at the chosen end")]
    [InlineData("Start", -10, 100, 110)]
    [InlineData("End", 0, 110, 110)]
    [InlineData("Both ends", -10, 110, 120)]
    [Trait("TestId", "CRV-H019")]
    public void ExtendCurveLengthensChosenEnd(string end, double startX, double endX, double length)
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new ExtendCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetChoice(command, "Extend", end);
        SetNumber(command, "Length", 10);
        var output = BuildOutput(command);
        var curve = SingleCurve(output);
        AssertClose(length, Length(curve), 1e-6, "Extended length (mm)");
        var bounds = curve.Props();
        AssertClose(startX, bounds.Min.X, 1e-6, "Extended minimum X (mm)");
        AssertClose(endX, bounds.Max.X, 1e-6, "Extended maximum X (mm)");
        Assert.Equal($"{length:0.###} mm", Result(command, "New length"));
        Assert.Single(output.Remove);
    }

    [AvaloniaFact(DisplayName = "CRV-H020 | Extend Curve can keep the original")]
    [Trait("TestId", "CRV-H020")]
    public void ExtendCurveKeepsOriginal()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new ExtendCurveCommand());
        Pick(command, "Curve (a single segment)", source);
        SetToggle(command, "Keep the original", true);
        Assert.Empty(BuildOutput(command).Remove);
    }

    // ------------------------------------------------------------------ Divide / Rebuild

    [AvaloniaFact(DisplayName = "CRV-H021 | Points Along Curve places Segments + 1 points and reports length and spacing")]
    [Trait("TestId", "CRV-H021")]
    public void DivideCurvePlacesPoints()
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new DivideCurveCommand());
        Pick(command, "Curve", source);
        SetInteger(command, "Segments", 4);
        var points = SingleCurve(BuildOutput(command));
        Assert.Equal(5, points.Count(SubKind.Vertex));
        AssertBounds(points, Vec3.Zero, new Vec3(100, 0, 0));
        Assert.Equal("100 mm", Result(command, "Curve length"));
        Assert.Equal("25 mm", Result(command, "Spacing"));
    }

    [AvaloniaTheory(DisplayName = "CRV-H022 | Rebuild Curve follows a straight line exactly and honours Keep the original")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "CRV-H022")]
    public void RebuildCurveFollowsLine(bool keep)
    {
        var session = new CadSession();
        var source = StraightLine(session, Vec3.Zero, new Vec3(100, 0, 0));
        var command = Begin(session, () => new RebuildCurveCommand());
        Pick(command, "Curve", source);
        SetInteger(command, "Points", 5);
        SetToggle(command, "Keep the original", keep);
        var output = BuildOutput(command);
        AssertClose(100, Length(SingleCurve(output)), 1e-4, "Rebuilt length (mm)");
        Assert.True(Assert.Single(command.Results, r => r.Label == "Largest deviation").IsGood);
        Assert.Equal(keep ? 0 : 1, output.Remove.Count);
    }

    [AvaloniaFact(DisplayName = "CRV-H023 | Rebuild Curve keeps a closed circle closed and on its radius")]
    [Trait("TestId", "CRV-H023")]
    public void RebuildCurveKeepsCircleClosed()
    {
        var session = new CadSession();
        var source = session.AddObject(Occ.Circle(Vec3.Zero, Vec3.UnitZ, 10), "Circle", ObjKind.Curve);
        var command = Begin(session, () => new RebuildCurveCommand());
        Pick(command, "Curve", source);
        SetInteger(command, "Points", 24);
        var curve = SingleCurve(BuildOutput(command));
        var (start, end) = Ends(curve);
        AssertPoint(start, end, 1e-6, "Closed rebuilt curve");
        AssertClose(2 * Math.PI * 10, Length(curve), 0.05, "Rebuilt circumference (mm)");
    }

    // ------------------------------------------------------------------ Project / Intersect / Duplicate edge

    [AvaloniaTheory(DisplayName = "CRV-H024 | Project Curve drops a square onto the top of a box by either method")]
    [InlineData("Along a direction")]
    [InlineData("Pull normal to the surface")]
    [Trait("TestId", "CRV-H024")]
    public void ProjectCurveOntoTopFace(string method)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 40, 10);
        var square = CreateCurve(session, [new Vec3(10, 10, 30), new Vec3(30, 10, 30), new Vec3(30, 30, 30), new Vec3(10, 30, 30)], closed: true);
        var command = Begin(session, () => new ProjectCurveCommand());
        Pick(command, "Curves to project", square);
        Pick(command, "Surface to project onto", FaceFacing(box, Vec3.UnitZ));
        SetChoice(command, "Method", method);
        Assert.Equal(method == "Along a direction", command.Params.Single(p => p.Label == "Direction").IsVisible);
        var projected = SingleCurve(BuildOutput(command));
        AssertBounds(projected, new Vec3(10, 10, 10), new Vec3(30, 30, 10));
        Assert.Equal("4", Result(command, "Projected segments"));
    }

    [AvaloniaFact(DisplayName = "CRV-H025 | Project Curve 'Right (+X)' lands a curve on the box side facing it")]
    [Trait("TestId", "CRV-H025")]
    public void ProjectCurveAlongPositiveX()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 40, 40);
        var line = StraightLine(session, new Vec3(-20, 10, 20), new Vec3(-20, 30, 20));
        var command = Begin(session, () => new ProjectCurveCommand());
        Pick(command, "Curves to project", line);
        Pick(command, "Surface to project onto", FaceFacing(box, -Vec3.UnitX));
        SetChoice(command, "Direction", "Right (+X)");
        var projected = SingleCurve(BuildOutput(command));
        AssertBounds(projected, new Vec3(0, 10, 20), new Vec3(0, 30, 20));
    }

    [AvaloniaFact(DisplayName = "CRV-H026 | Intersect Surfaces outlines where two overlapping boxes cross")]
    [Trait("TestId", "CRV-H026")]
    public void IntersectSurfacesOfOverlappingBoxes()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var second = CreateBoxBody(session, new Vec3(10, 10, 10), 20, 20, 20);
        var command = Begin(session, () => new IntersectSurfacesCommand());
        Pick(command, "First surface or body", first);
        Pick(command, "Second surface or body", second);
        var curve = SingleCurve(BuildOutput(command));
        Assert.True(curve.Count(SubKind.Edge) >= 6);
        AssertBounds(curve, new Vec3(10, 10, 10), new Vec3(20, 20, 20));
    }

    [AvaloniaFact(DisplayName = "CRV-H027 | Intersect Surfaces reports when the shapes do not cross")]
    [Trait("TestId", "CRV-H027")]
    public void IntersectSurfacesRefusesDisjointShapes()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(50, 0, 0), 10, 10, 10);
        var command = Begin(session, () => new IntersectSurfacesCommand());
        Pick(command, "First surface or body", first);
        Pick(command, "Second surface or body", second);
        AssertBuildFails(command, "do not cross");
    }

    [AvaloniaFact(DisplayName = "CRV-H028 | Extract Edges copies the four top edges of a box as one closed 40 mm curve")]
    [Trait("TestId", "CRV-H028")]
    public void DuplicateEdgeJoinsTopLoop()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var top = EdgesWhere(box, edge => edge.Min.Z > 9.99 && edge.Max.Z > 9.99);
        Assert.Equal(4, top.Length);
        var command = Begin(session, () => new DuplicateEdgeCommand());
        Pick(command, "Edges", top);
        var output = BuildOutput(command);
        var curve = SingleCurve(output);
        Assert.Equal(1, curve.Count(SubKind.Wire));
        AssertClose(40, Length(curve), 1e-6, "Loop length (mm)");
        Assert.Empty(output.Remove);
    }

    // ------------------------------------------------------------------ Curvature graph

    [AvaloniaFact(DisplayName = "CRV-H029 | Curvature Graph reports a 10 mm circle's bend radius without changing the document")]
    [Trait("TestId", "CRV-H029")]
    public void CurvatureGraphReportsCircleRadius()
    {
        var session = new CadSession();
        var circle = session.AddObject(Occ.Circle(Vec3.Zero, Vec3.UnitZ, 10), "Circle", ObjKind.Curve);
        var command = Begin(session, () => new CurveCurvatureCommand());
        Pick(command, "Curves", circle);
        SetInteger(command, "Sample density", 60);
        var output = BuildOutput(command);
        Assert.True(output.NoOp);
        Assert.Empty(output.Add);
        Assert.Equal("radius 10 mm", Result(command, "Tightest bend"));
        Assert.Equal("radius 10 mm", Result(command, "Typical bend"));
    }

    [AvaloniaFact(DisplayName = "CRV-H030 | Curvature Graph reports a straight line as having no bend")]
    [Trait("TestId", "CRV-H030")]
    public void CurvatureGraphReportsStraightLine()
    {
        var session = new CadSession();
        var line = StraightLine(session, Vec3.Zero, new Vec3(50, 0, 0));
        var command = Begin(session, () => new CurveCurvatureCommand());
        Pick(command, "Curves", line);
        BuildOutput(command);
        Assert.Equal("none: the curve is straight", Result(command, "Tightest bend"));
        Assert.Equal("1", Result(command, "Segments"));
    }
}
