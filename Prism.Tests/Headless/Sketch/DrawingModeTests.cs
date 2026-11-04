using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch drawing modes.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class DrawingModeTests
{
    [AvaloniaTheory(DisplayName = "SKT-H001 | All three ellipse modes produce a 40 × 20 mm ellipse")]
    [InlineData(EllipseMode.Center)]
    [InlineData(EllipseMode.TwoPoint)]
    [InlineData(EllipseMode.ThreePoint)]
    [Trait("TestId", "SKT-H001")]
    public void EllipseModeProducesAnalyticBounds(EllipseMode mode)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var sketch = session.Sketch!;
        var tool = new EllipseTool(mode);
        sketch.SetTool(session, tool);
        switch (mode)
        {
            case EllipseMode.Center:
                tool.Feed(session, 40, 40);
                tool.Feed(session, 60, 40);
                tool.Feed(session, 40, 50);
                break;
            case EllipseMode.TwoPoint:
                tool.Feed(session, 20, 30);
                tool.Feed(session, 60, 50);
                break;
            case EllipseMode.ThreePoint:
                tool.Feed(session, 20, 40);
                tool.Feed(session, 60, 40);
                tool.Feed(session, 40, 50);
                break;
        }
        Assert.True(sketch.Result.Converged);
        using var curve = Assert.Single(sketch.Model.ToShapes(sketch.Plane));
        Assert.True(curve.IsValid);
        AssertBounds(curve, new Vec3(20, 30, 0), new Vec3(60, 50, 0));
    }

    [AvaloniaTheory(DisplayName = "SKT-H002 | Chain and Midpoint lines use different first-click semantics")]
    [InlineData(0, 20)]
    [InlineData(1, -10)]
    [Trait("TestId", "SKT-H002")]
    public void LineModeInterpretsFirstPoint(int mode, int minimumX)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var sketch = session.Sketch!;
        var tool = new LineTool(mode);
        sketch.SetTool(session, tool);
        tool.Feed(session, 20, 40);
        tool.Feed(session, 50, 40);
        using var edge = Assert.Single(sketch.Model.ToShapes(sketch.Plane));
        AssertBounds(edge, new Vec3(minimumX, 40, 0), new Vec3(50, 40, 0));
    }

    [AvaloniaTheory(DisplayName = "SKT-H003 | Polygon guide radius applies to vertices or sides according to mode")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "SKT-H003")]
    public void PolygonDistinguishesCircumradiusAndApothem(bool circumscribed)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var sketch = session.Sketch!;
        var tool = new PolygonTool(circumscribed);
        sketch.SetTool(session, tool);
        int sides = tool.StepperValue;
        tool.Feed(session, 30, 40);
        tool.Feed(session, 50, 40);

        var lines = sketch.Model.Entities.Where(entity => entity.Type == EntType.Line && !entity.Construction).ToList();
        Assert.Equal(sides, lines.Count);
        double expectedSide = 40 * (circumscribed ? Math.Tan(Math.PI / sides) : Math.Sin(Math.PI / sides));
        foreach (var line in lines)
        {
            var start = sketch.Model.P(line.A);
            var end = sketch.Model.P(line.B);
            AssertNear(expectedSide, new Vec3(start.X - end.X, start.Y - end.Y, 0).Length, "Polygon side (mm)");
        }
    }

    [AvaloniaTheory(DisplayName = "SKT-H004 | Three-point and center placement construct a radius-10 quarter arc")]
    [InlineData(ArcMode.ThreePoint)]
    [InlineData(ArcMode.CenterStartEnd)]
    [Trait("TestId", "SKT-H004")]
    public void ArcPlacementHasAnalyticLength(ArcMode mode)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var sketch = session.Sketch!;
        var tool = new ArcTool(mode);
        sketch.SetTool(session, tool);
        if (mode == ArcMode.CenterStartEnd)
            tool.Feed(session, 30, 40);
        tool.Feed(session, 40, 40);
        tool.Feed(session, 30, 50);
        if (mode == ArcMode.ThreePoint)
            tool.Feed(session, 30 + Math.Sqrt(50), 40 + Math.Sqrt(50));
        using var curve = Assert.Single(sketch.Model.ToShapes(sketch.Plane));
        AssertNear(5 * Math.PI, Occ.CurveLength(curve), "Arc length (mm)");
    }

    [AvaloniaFact(DisplayName = "SKT-H005 | Tangent Arc shares the source endpoint and adds tangency")]
    [Trait("TestId", "SKT-H005")]
    public void TangentArcContinuesFromLineEndpoint()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        var model = sketch.Model;
        int start = model.AddPoint(10, 20);
        int end = model.AddPoint(30, 20);
        model.AddLine(start, end);
        sketch.Solve(session);
        var tool = new ArcTool(ArcMode.Tangent);
        sketch.SetTool(session, tool);
        var pixel = session.Camera.Project(new Vec3(30, 20, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
        tool.Feed(session, 40, 30);
        var arc = Assert.Single(model.Entities, entity => entity.Type == EntType.Arc);
        Assert.Contains(end, new[] { arc.B, arc.C });
        Assert.Contains(model.Constraints, item => item.Type == CType.Tangent);
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaTheory(DisplayName = "SKT-H006 | Fit and control spline modes give the same picks different geometry")]
    [InlineData(false, 30)]
    [InlineData(true, 15)]
    [Trait("TestId", "SKT-H006")]
    public void SplineModeChangesCurvePeak(bool controlPoints, int expectedPeak)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var sketch = session.Sketch!;
        var tool = new SplineTool(controlPoints);
        sketch.SetTool(session, tool);
        tool.Feed(session, 10, 0);
        tool.Feed(session, 30, 30);
        tool.Feed(session, 50, 0);
        tool.FinishFeed(session);
        using var curve = Assert.Single(sketch.Model.ToShapes(sketch.Plane));
        AssertNear(expectedPeak, curve.Props().Max.Y, "Spline peak Y (mm)");
    }
}
