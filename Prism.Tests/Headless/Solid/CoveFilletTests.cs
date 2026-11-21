using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>Fillet with Shape: Cove, a concave quarter-round scoop centred on the edge. Owner decision 2026-11-16 ("yes, concave").</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid-details/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class CoveFilletTests
{
    static List<int> TopEdges(Shape box, double z) =>
        Enumerable.Range(0, box.Count(SubKind.Edge)).Where(i =>
        {
            var info = Occ.EdgeInfoOf(box.Sub(SubKind.Edge, i));
            return Math.Abs(info.Start.Z - z) < 1e-6 && Math.Abs(info.End.Z - z) < 1e-6;
        }).ToList();

    static FilletCommand Fillet(CadSession session, SceneObject body, IEnumerable<int> edges, double radius, bool cove)
    {
        var command = Begin(session, () => new FilletCommand());
        Pick(command, "Edges", edges.Select(i => new SelItem(body.Id, SelKind.Edge, i)).ToArray());
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Radius", radius);
            if (cove) SetChoice(command, "Shape", "Cove (concave scoop)");
        });
        return command;
    }

    [AvaloniaFact(DisplayName = "SOL-H090 | A cove of radius 2 along one 10 mm edge of a 10 mm cube removes a quarter disc: 10 pi mm3")]
    [Trait("TestId", "SOL-H090")]
    public void CoveRemovesQuarterDisc()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var edge = TopEdges(body.Shape, 10).First();
        var output = Fillet(session, body, [edge], 2, cove: true).TryBuild();
        Assert.NotNull(output);
        using var result = Assert.Single(output.Replace).New;
        AssertValidSolid(result);
        AssertNear(1000 - 10 * Math.PI, result.Props().Volume, "Volume after the cove (mm³)");
        AssertNear(10, result.Props().Size.X, "Width unchanged (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H091 | A round fillet of the same radius removes less than a cove, and still works as before")]
    [Trait("TestId", "SOL-H091")]
    public void RoundIsUnchangedAndRemovesLess()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var edge = TopEdges(body.Shape, 10).First();
        var round = Fillet(session, body, [edge], 2, cove: false).TryBuild();
        Assert.NotNull(round);
        using var rounded = Assert.Single(round.Replace).New;
        AssertNear(1000 - 10 * (4 - Math.PI), rounded.Props().Volume, "Round fillet volume (mm³): (r² - pi r²/4) x length removed");
        Assert.True(rounded.Props().Volume > 1000 - 10 * Math.PI, "a round fillet takes less material than a cove");
    }

    [AvaloniaFact(DisplayName = "SOL-H092 | Two opposite edges coved remove twice as much")]
    [Trait("TestId", "SOL-H092")]
    public void TwoOppositeEdges()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var edges = TopEdges(body.Shape, 10);
        var first = edges[0]; var firstInfo = Occ.EdgeInfoOf(body.Shape.Sub(SubKind.Edge, first));
        var opposite = edges.First(i =>
        {
            var info = Occ.EdgeInfoOf(body.Shape.Sub(SubKind.Edge, i));
            return i != first && (info.End - info.Start).Normalized().DistanceTo((firstInfo.End - firstInfo.Start).Normalized()) < 1e-6 || i != first && (info.End - info.Start).Normalized().DistanceTo(-(firstInfo.End - firstInfo.Start).Normalized()) < 1e-6;
        });
        var output = Fillet(session, body, [first, opposite], 2, cove: true).TryBuild();
        Assert.NotNull(output);
        using var result = Assert.Single(output.Replace).New;
        AssertValidSolid(result);
        AssertNear(1000 - 20 * Math.PI, result.Props().Volume, "Volume after two coves (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H093 | A cove on a curved edge is refused and the body is unchanged")]
    [Trait("TestId", "SOL-H093")]
    public void CurvedEdgeIsRefused()
    {
        var session = new CadSession();
        using var cylinder = Occ.Cylinder(10, 20);
        var round = session.AddObject(Occ.Translate(cylinder, Vec3.Zero), "Round", ObjKind.Solid);
        int circle = Enumerable.Range(0, round.Shape.Count(SubKind.Edge)).First(i => Occ.EdgeInfoOf(round.Shape.Sub(SubKind.Edge, i)).Type != "Line");
        var command = Fillet(session, round, [circle], 1, cove: true);
        Assert.Null(command.TryBuild());
        Assert.Contains("straight edges", command.Error);
    }

    [AvaloniaFact(DisplayName = "SOL-H094 | Shape is the last Fillet parameter and defaults to Round, so older saved steps line up (order check only, no old file opened)")]
    [Trait("TestId", "SOL-H094")]
    public void ShapeIsLastAndDefaultsToRound()
    {
        var command = new FilletCommand();
        var shapeIndex = command.Params.Count - 1;
        Assert.Equal("Shape", command.Params[shapeIndex].Label);                  // last, so old recipes (one value shorter) line up
        Assert.Equal(0, ((ChoiceParam)command.Params[shapeIndex]).Index);          // Round by default
    }
}
