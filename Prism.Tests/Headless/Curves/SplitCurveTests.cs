using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <summary>Split Curve, back in the Curves menu with three ways to split (owner request 2026-11-23). Percentages on one segment: CRV-H017, CRV-H018.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/curves/README.md">Curve workflow and compatibility.</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class SplitCurveTests
{
    static SceneObject Line(CadSession session, Vec3 start, Vec3 end) => session.AddObject(Occ.Line(start, end), "Line", ObjKind.Curve);

    static double Length(Shape curve) => Occ.CurveDivide(curve, 1).length;

    static SplitCurveCommand Start(CadSession session, SceneObject curve, string by)
    {
        var command = Begin(session, () => new SplitCurveCommand());
        Pick(command, "Curve to split", curve);
        SetChoice(command, "Split", by);
        return command;
    }

    /// <summary>An L of two 20 mm lines joined into one curve of two segments.</summary>
    static SceneObject JoinedL(CadSession session)
    {
        var join = Begin(session, () => new JoinCurvesCommand());
        Pick(join, "Curves to join", Line(session, Vec3.Zero, new(20, 0, 0)), Line(session, new(20, 0, 0), new(20, 20, 0)));
        session.CommitCommand();
        return Assert.Single(session.Objects, o => o.Kind == ObjKind.Curve);
    }

    [AvaloniaFact(DisplayName = "CRV-H055 | Split Curve cuts a 100 mm line where a surface and a crossing line meet it: 30, 40 and 30 mm, and keeps both cutters")]
    [Trait("TestId", "CRV-H055")]
    public void SplitsWhereObjectsCross()
    {
        var session = new CadSession();
        var line = Line(session, Vec3.Zero, new(100, 0, 0));
        var wall = session.AddObject(Occ.NurbsSurface([new(30, -5, -5), new(30, 5, -5), new(30, -5, 5), new(30, 5, 5)], 2, 2, 1, 1), "Wall", ObjKind.Surface);
        var across = Line(session, new(70, -5, 0), new(70, 5, 0));
        var command = Start(session, line, "Where other objects or points cross it");
        Assert.Null(command.TryBuild());                              // nothing to cut with yet
        Pick(command, "Split where these cross it", SelItem.Of(wall), SelItem.Of(across));
        var output = BuildOutput(command);
        var lengths = output.Add.Select(a => Length(a.Shape!)).OrderBy(l => l).ToArray();
        Assert.Equal(3, lengths.Length);
        AssertClose(30, lengths[0], 1e-4, "Shorter end piece (mm)");
        AssertClose(30, lengths[1], 1e-4, "Other end piece (mm)");
        AssertClose(40, lengths[2], 1e-4, "Middle piece (mm)");
        Assert.Equal(line.Id, Assert.Single(output.Remove).Id);   // only the split curve goes
        Assert.Equal("3", Result(command, "Pieces"));
    }

    [AvaloniaFact(DisplayName = "CRV-H056 | Split Curve cuts at a picked point on the curve and refuses a point off it")]
    [Trait("TestId", "CRV-H056")]
    public void SplitsAtAPickedPoint()
    {
        var session = new CadSession();
        var line = Line(session, Vec3.Zero, new(100, 0, 0));
        var post = Line(session, new(25, 0, 0), new(25, 10, 0));
        var far = Line(session, new(50, 30, 0), new(50, 40, 0));
        SelItem VertexAt(SceneObject o, Vec3 p) => Enumerable.Range(0, 2).Select(i => new SelItem(o.Id, SelKind.Vertex, i))
            .Single(item => Occ.Distance(session.ShapeOf(item)!, Occ.Line(p, p + new Vec3(0, 0, 1e-3))).Distance < 1e-6);

        var command = Start(session, line, "Where other objects or points cross it");
        Pick(command, "Split where these cross it", VertexAt(post, new(25, 0, 0)));
        var lengths = BuildOutput(command).Add.Select(a => Length(a.Shape!)).OrderBy(l => l).ToArray();
        Assert.Equal(2, lengths.Length);
        AssertClose(25, lengths[0], 0.05, "Piece up to the point (mm)");
        AssertClose(75, lengths[1], 0.05, "Piece after the point (mm)");

        Pick(command, "Split where these cross it", VertexAt(far, new(50, 30, 0)));
        AssertBuildFails(command, "not on the curve");
    }

    [AvaloniaFact(DisplayName = "CRV-H057 | Split Curve takes a joined L apart at its joint, and at 25 % of its whole length keeps the corner inside one piece")]
    [Trait("TestId", "CRV-H057")]
    public void SplitsAJoinedRun()
    {
        var session = new CadSession();
        var l = JoinedL(session);

        var joints = Start(session, l, "At every joint between segments");
        var pieces = BuildOutput(joints).Add.Select(a => a.Shape!).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, p => AssertClose(20, Length(p), 1e-6, "Segment length (mm)"));

        var percent = Start(session, l, "At percentages along it");
        SetText(percent, "Split at (% , separated)", "25");
        var parts = BuildOutput(percent).Add.Select(a => a.Shape!).OrderBy(Length).ToList();
        Assert.Equal(2, parts.Count);
        AssertClose(10, Length(parts[0]), 0.05, "First 25 % (mm)");
        AssertClose(30, Length(parts[1]), 0.05, "The rest, round the corner (mm)");
        Assert.Equal(1, parts[1].Count(SubKind.Wire));
        Assert.Equal(2, parts[1].Count(SubKind.Edge));

        var single = Start(session, Line(session, new(0, 50, 0), new(10, 50, 0)), "At every joint between segments");
        AssertBuildFails(single, "no joints");
    }
}
