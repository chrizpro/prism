using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid/README.md">Solid ribbon guide.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch entry.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/construction-planes/README.md">Construction planes.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/curves/README.md">Curve creation.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/interface/inspection/README.md">Inspection.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SharedToolTests
{
    [AvaloniaFact(DisplayName = "SOL-H060 | Measure reports the 5 mm gap between two bodies")]
    [Trait("TestId", "SOL-H060")]
    public void MeasureReportsBodyGap()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(15, 0, 0), 10, 10, 10);
        var command = StartCommand(session, first, () => new MeasureCommand());
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Pick", "Whole bodies");
            command.AddPick(new PickResult(SelItem.Of(first), Vec3.Zero, Vec3.UnitX, 0), false);
            command.AddPick(new PickResult(SelItem.Of(second), new Vec3(15, 0, 0), Vec3.UnitX, 0), false);
        });
        Assert.True(command.TryBuild()?.NoOp);
        Assert.Contains(command.Results, row => row.Label == "Distance" && row.Value == "5 mm");
    }

    [AvaloniaFact(DisplayName = "SOL-H061 | Properties reports the 120 mm³ box volume")]
    [Trait("TestId", "SOL-H061")]
    public void PropertiesReportsBoxVolume()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 4, 5, 6);
        var command = StartCommand(session, source, () => new PropertiesCommand());
        Assert.True(command.TryBuild()?.NoOp);
        Assert.Contains(command.Results, row => row.Label == "Volume" && row.Value.StartsWith("120 mm³"));
    }

    [AvaloniaFact(DisplayName = "SOL-H062 | XY section through a 4 × 5 × 6 box has area 20 mm²")]
    [Trait("TestId", "SOL-H062")]
    public void SectionReportsAnalyticArea()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 4, 5, 6);
        var command = StartCommand(session, source, () => new SectionCommand());
        command.ConfigureInputs(() => SetNumber(command, "Offset", 3));
        Assert.True(command.TryBuild()?.NoOp);
        Assert.Contains(command.Results, row => row.Label == "Section area" && row.Value == "20 mm²");
        Assert.Contains(command.Results, row => row.Label == "Outline length" && row.Value == "18 mm");
    }

    [AvaloniaFact(DisplayName = "SOL-H063 | Curve from Data converts three rows into a 7 mm polyline")]
    [Trait("TestId", "SOL-H063")]
    public void CurveFromDataBuildsStraightSegments()
    {
        var session = new CadSession();
        session.StartCommand(() => new EquationCurveCommand(false));
        var command = Assert.IsType<EquationCurveCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Points from", "Table or file");
            SetChoice(command, "Curve", "Straight segments");
            command.Params.OfType<TextParam>().Single(parameter => parameter.Label == "Points").Value =
                "0,0,0; 3,0,0; 3,4,0";
        });
        using var curve = BuildSingleAddedShape(command);
        Assert.Equal(2, curve.Count(SubKind.Edge));
        AssertNear(7, Occ.CurveLength(curve), "Curve length (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H064 | Create Sketch starts sketch mode at a 7 mm XY offset")]
    [Trait("TestId", "SOL-H064")]
    public void CreateSketchCommitsOnOffsetPlane()
    {
        var session = new CadSession();
        session.StartCommand(() => new CreateSketchCommand());
        var command = Assert.IsType<CreateSketchCommand>(session.ActiveCommand);
        command.ConfigureInputs(() => SetNumber(command, "Offset", 7));
        session.CommitCommand();
        Assert.NotNull(session.Sketch);
        Assert.Equal(Plane3.XY with { Origin = new Vec3(0, 0, 7) }, session.Sketch.Plane);
    }

    [AvaloniaFact(DisplayName = "SOL-H065 | Construction Plane offsets a box top face by 4 mm")]
    [Trait("TestId", "SOL-H065")]
    public void ConstructionPlaneOffsetsPlanarFace()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new ConstructionPlaneCommand());
        int top = Enumerable.Range(0, source.Shape.Count(SubKind.Face)).Single(index =>
        {
            using var face = source.Shape.Sub(SubKind.Face, index);
            return Occ.FaceInfoOf(face).Normal.Z > 0.99;
        });
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(new SelItem(source.Id, SelKind.Face, top)));
            SetNumber(command, "Distance", 4);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var plane = Assert.Single(output.Add).Plane;
        Assert.True(plane.HasValue);
        AssertNear(14, plane.Value.Origin.Z, "Plane height (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H066 | Curve through two points creates a 5 mm straight curve")]
    [Trait("TestId", "SOL-H066")]
    public void CurveThroughTwoPointsHasAnalyticLength()
    {
        var session = new CadSession();
        session.StartCommand(() => new CurveByPointsCommand(true));
        var command = Assert.IsType<CurveByPointsCommand>(session.ActiveCommand);
        command.SetPoints([Vec3.Zero, new Vec3(3, 4, 0)]);
        using var curve = BuildSingleAddedShape(command);
        AssertNear(5, Occ.CurveLength(curve), "Curve length (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H067 | A plane through a box creates a rectangular intersection curve")]
    [Trait("TestId", "SOL-H067")]
    public void IntersectionCurveTracesBoxCrossSection()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 4, 5, 6);
        var plane = session.AddObject(null, "Cut plane", ObjKind.Plane,
            plane: Plane3.XY with { Origin = new Vec3(0, 0, 3) });
        var command = StartCommand(session, source, () => new IntersectCurvesCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "First", source);
            SetSelection(command, "Second", plane);
        });
        using var curve = BuildSingleAddedShape(command);
        Assert.Equal(4, curve.Count(SubKind.Edge));
        AssertBounds(curve, new Vec3(0, 0, 3), new Vec3(4, 5, 3));
    }
}
