using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch editing, constraints and dimensions.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchEditTests
{
    [AvaloniaTheory(DisplayName = "SKT-H010 | Dimension mode determines the measured component")]
    [InlineData(DimMode.Smart, CType.LineLength, 50)]
    [InlineData(DimMode.Horizontal, CType.HDistance, 30)]
    [InlineData(DimMode.Vertical, CType.VDistance, 40)]
    [Trait("TestId", "SKT-H010")]
    public void DimensionModeMeasuresSelectedLine(DimMode mode, CType type, int length)
    {
        var (session, sketch, _) = LineSketch();
        var tool = new DimensionTool(mode);
        sketch.SetTool(session, tool);
        Click(session, tool, 35, 40);
        tool.PanelSet(session, "apply", null);
        var dimension = Assert.Single(sketch.Model.Constraints, item => !item.Hidden);
        Assert.Equal(type, dimension.Type);
        AssertNear(length, Math.Abs(dimension.Value), "Dimension value (mm)");
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaTheory(DisplayName = "SKT-H011 | Axis constraints solve an oblique line")]
    [InlineData(CType.Horizontal)]
    [InlineData(CType.Vertical)]
    [Trait("TestId", "SKT-H011")]
    public void AxisConstraintAlignsLine(CType type)
    {
        var (session, sketch, id) = LineSketch();
        var tool = new ConstraintTool(type);
        sketch.SetTool(session, tool);
        Click(session, tool, 35, 40);
        Assert.Equal(type, Assert.Single(sketch.Model.Constraints, item => !item.Hidden).Type);
        var line = sketch.Model.E(id);
        var a = sketch.Model.P(line.A);
        var b = sketch.Model.P(line.B);
        AssertNear(0, type == CType.Horizontal ? a.Y - b.Y : a.X - b.X, "Perpendicular displacement (mm)");
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaFact(DisplayName = "SKT-H012 | Offset creates a parallel copy 3 mm from the source")]
    [Trait("TestId", "SKT-H012")]
    public void OffsetCopiesWithoutMovingSource()
    {
        var (session, sketch, id) = LineSketch();
        sketch.SelEntities.Add(id);
        var tool = new OffsetTool();
        sketch.SetTool(session, tool);
        tool.Key(session, "Enter");
        tool.PanelSet(session, "dist", 3d);
        tool.PanelSet(session, "apply", null);
        var model = sketch.Model;
        var original = model.E(id);
        var copy = Assert.Single(model.Entities, entity => entity.Id != id && entity.Type == EntType.Line);
        var a = model.P(original.A);
        var b = model.P(original.B);
        var c = model.P(copy.A);
        var d = model.P(copy.B);
        AssertNear(20, a.X, "Original start U (mm)");
        AssertNear(20, a.Y, "Original start V (mm)");
        var direction = new Vec3(b.X - a.X, b.Y - a.Y, 0).Normalized();
        AssertNear(3, Vec3.Cross(direction, new Vec3(c.X - a.X, c.Y - a.Y, 0)).Length, "Offset (mm)");
        AssertNear(0, Vec3.Cross(direction, new Vec3(d.X - c.X, d.Y - c.Y, 0)).Length, "Parallel deviation");
    }

    static (CadSession Session, SketchContext Sketch, int Line) LineSketch()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        var model = sketch.Model;
        int line = model.AddLine(model.AddPoint(20, 20), model.AddPoint(50, 60));
        sketch.Solve(session);
        return (session, sketch, line);
    }

    static void Click(CadSession session, SketchTool tool, double u, double v)
    {
        var pixel = session.Camera.Project(new Vec3(u, v, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
    }
}
