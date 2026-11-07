using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/rectangle/README.md">Rectangle modes and controls.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class RectangleTests
{
    [AvaloniaFact(DisplayName = "REC-H001 | Two-point creates the rectangle between opposing corners")]
    [Trait("TestId", "REC-H001")]
    public void TwoPointUsesOppositeCorners()
    {
        var (session, tool) = StartRectangle(RectMode.TwoCorner);
        tool.Feed(session, 20, 30);
        tool.Feed(session, 60, 50);
        AssertRectangle(session, new(20, 30, 0), new(60, 30, 0), new(60, 50, 0), new(20, 50, 0));
    }

    [AvaloniaFact(DisplayName = "REC-H002 | Center preserves the clicked midpoint and doubles the half-extents")]
    [Trait("TestId", "REC-H002")]
    public void CenterUsesSymmetricExtents()
    {
        var (session, tool) = StartRectangle(RectMode.Center);
        tool.Feed(session, 40, 40);
        tool.Feed(session, 60, 50);
        AssertRectangle(session, new(20, 30, 0), new(60, 30, 0), new(60, 50, 0), new(20, 50, 0));
        Assert.Contains(session.Sketch!.Model.Constraints, constraint => constraint.Type == CType.Midpoint);
        Assert.Single(session.Sketch.Model.Entities, entity => entity.Construction && entity.Type == EntType.Line);
    }

    [AvaloniaFact(DisplayName = "REC-H003 | Three-point uses the chosen base and signed perpendicular width")]
    [Trait("TestId", "REC-H003")]
    public void ThreePointKeepsObliqueBase()
    {
        var (session, tool) = StartRectangle(RectMode.ThreePoint);
        tool.Feed(session, 20, 30);
        tool.Feed(session, 50, 70);
        tool.Feed(session, 4, 42);
        AssertRectangle(session, new(20, 30, 0), new(50, 70, 0), new(34, 82, 0), new(4, 42, 0));
    }

    [AvaloniaTheory(DisplayName = "REC-H004 | Typed width and height honor the 90° orientation")]
    [InlineData(RectMode.TwoCorner)]
    [InlineData(RectMode.Center)]
    [Trait("TestId", "REC-H004")]
    public void TypedDimensionsRespectOrientation(RectMode mode)
    {
        var (session, tool) = StartRectangle(mode);
        tool.PanelSet(session, "orientation", 90d);
        tool.Feed(session, 40, 40);
        var pixel = session.Camera.Project(new Vec3(20, 70, 0))!.Value;
        tool.PointerMove(session, new(pixel.x, pixel.y, false, false, false, true));
        tool.PanelSet(session, "draw-0", 30d);
        tool.PanelSet(session, "draw-1", 10d);
        tool.PanelSet(session, "place", null);

        if (mode == RectMode.TwoCorner)
            AssertRectangle(session, new(40, 40, 0), new(40, 70, 0), new(30, 70, 0), new(30, 40, 0));
        else
            AssertRectangle(session, new(45, 25, 0), new(45, 55, 0), new(35, 55, 0), new(35, 25, 0));
    }

    [AvaloniaTheory(DisplayName = "REC-H005 | Three-point axis lock constrains the first side")]
    [InlineData("Char:H", CType.Horizontal, 50, 30)]
    [InlineData("Char:V", CType.Vertical, 20, 60)]
    [Trait("TestId", "REC-H005")]
    public void ThreePointAxisLockConstrainsBase(string key, CType constraint, int endU, int endV)
    {
        var (session, tool) = StartRectangle(RectMode.ThreePoint);
        tool.Feed(session, 20, 30);
        Assert.True(tool.Key(session, key));
        Click(session, tool, 50, 60);
        tool.Feed(session, 35, 45);
        var model = session.Sketch!.Model;
        var side = model.Entities.First(entity => entity.Type == EntType.Line);
        AssertNear(endU, model.P(side.B).X, "Locked side end U (mm)");
        AssertNear(endV, model.P(side.B).Y, "Locked side end V (mm)");
        Assert.Contains(model.Constraints, item => item.Type == constraint);
    }

    [AvaloniaTheory(DisplayName = "REC-H006 | Shift places a square and adds an Equal constraint")]
    [InlineData(RectMode.TwoCorner, 30)]
    [InlineData(RectMode.Center, 60)]
    [Trait("TestId", "REC-H006")]
    public void ShiftMakesSquare(RectMode mode, int expectedSide)
    {
        var (session, tool) = StartRectangle(mode);
        tool.Feed(session, 20, 30);
        Click(session, tool, 50, 45, shift: true);
        var model = session.Sketch!.Model;
        var sides = model.Entities.Where(entity => entity.Type == EntType.Line && !entity.Construction).ToList();
        Assert.Equal(4, sides.Count);
        Assert.All(sides, side =>
        {
            var start = model.P(side.A);
            var end = model.P(side.B);
            AssertNear(expectedSide, new Vec3(start.X - end.X, start.Y - end.Y, 0).Length, "Square side (mm)");
        });
        Assert.Contains(model.Constraints, item => item.Type == CType.Equal);
    }

    [AvaloniaFact(DisplayName = "REC-H007 | Zero-width placement preserves reference points without creating edges")]
    [Trait("TestId", "REC-H007")]
    public void DegenerateRectanglePreservesPoints()
    {
        var (session, tool) = StartRectangle(RectMode.TwoCorner);
        tool.Feed(session, 20, 30);
        tool.Feed(session, 20, 50);
        var sketch = session.Sketch!;
        Assert.Equal(2, sketch.Model.Entities.Count);
        Assert.All(sketch.Model.Entities, entity => Assert.Equal(EntType.Point, entity.Type));
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaFact(DisplayName = "REC-H008 | Three-point accepts a typed base length, angle and width")]
    [Trait("TestId", "REC-H008")]
    public void ThreePointUsesTypedBaseAndWidth()
    {
        var (session, tool) = StartRectangle(RectMode.ThreePoint);
        tool.Feed(session, 20, 30);
        Move(session, tool, 30, 50);
        tool.PanelSet(session, "draw-0", 30d);
        tool.PanelSet(session, "draw-1", 90d);
        tool.PanelSet(session, "place", null);
        Move(session, tool, 10, 50);
        tool.PanelSet(session, "draw-0", 12d);
        tool.PanelSet(session, "place", null);
        AssertRectangle(session, new(20, 30, 0), new(20, 60, 0), new(8, 60, 0), new(8, 30, 0));
    }

    [AvaloniaTheory(DisplayName = "REC-H009 | Rotate preview turns the rectangle while retaining its extents")]
    [InlineData(RectMode.TwoCorner)]
    [InlineData(RectMode.Center)]
    [Trait("TestId", "REC-H009")]
    public void RotatePreviewKeepsExtents(RectMode mode)
    {
        var (session, tool) = StartRectangle(mode);
        tool.Feed(session, 40, 40);
        Move(session, tool, 60, 50);
        tool.PanelSet(session, "rotate", null);
        Move(session, tool, 40, 70);
        Click(session, tool, 40, 70);
        if (mode == RectMode.TwoCorner)
            AssertRectangle(session, new(40, 40, 0), new(40, 60, 0), new(30, 60, 0), new(30, 40, 0));
        else
            AssertRectangle(session, new(50, 20, 0), new(50, 60, 0), new(30, 60, 0), new(30, 20, 0));
    }

    static void Move(CadSession session, RectTool tool, double u, double v)
    {
        var pixel = session.Camera.Project(new Vec3(u, v, 0))!.Value;
        tool.PointerMove(session, new(pixel.x, pixel.y, false, false, false, true));
    }

    static (CadSession Session, RectTool Tool) StartRectangle(RectMode mode)
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var button = vm.Tabs.Single(tab => tab.Title == "Sketch").Buttons.Single(item => item.Label == "Rectangle" && item.Icon == "rect");
        button.Refresh();
        button.Invoke();
        var tool = Assert.IsType<RectTool>(session.Sketch!.Tool);
        Assert.Equal(new[] { "2-Point", "Center", "3-Point" }, tool.Options.Select(option => option.Label));
        bool hadOption = SketchTool.Remembered.TryGetValue("Rectangle", out int previous);
        try { tool.SetOption(session, (int)mode); }
        finally
        {
            if (hadOption) SketchTool.Remembered["Rectangle"] = previous;
            else SketchTool.Remembered.Remove("Rectangle");
        }
        return (session, tool);
    }

    static void Click(CadSession session, RectTool tool, double u, double v, bool shift = false)
    {
        var pixel = session.Camera.Project(new Vec3(u, v, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, shift, false, true));
    }

    static void AssertRectangle(CadSession session, params Vec3[] corners)
    {
        var sketch = session.Sketch!;
        Assert.True(sketch.Result.Converged);
        var model = sketch.Model;
        var sides = model.Entities.Where(entity => entity.Type == EntType.Line && !entity.Construction).ToList();
        Assert.Equal(4, sides.Count);
        for (int index = 0; index < sides.Count; index++)
        {
            var start = model.P(sides[index].A);
            AssertNear(corners[index].X, start.X, $"Corner {index + 1} U (mm)");
            AssertNear(corners[index].Y, start.Y, $"Corner {index + 1} V (mm)");
            Assert.Equal(sides[(index + 1) % sides.Count].A, sides[index].B);
        }
    }
}
