using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>Box, lasso and paint selection inside a sketch.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/interface/selection/README.md">Selection controls and region picking.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchRegionSelectTests
{
    static (CadSession session, SketchContext sketch, SelectTool tool, int near, int far) Scene(int method)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        var model = sketch.Model;
        int near = model.AddLine(model.AddPoint(20, 20), model.AddPoint(50, 60));
        int far = model.AddLine(model.AddPoint(200, 200), model.AddPoint(240, 230));
        sketch.Solve(session);
        var tool = new SelectTool();
        tool.UseMethod(method);
        sketch.SetTool(session, tool);
        return (session, sketch, tool, near, far);
    }

    static (double x, double y) Px(CadSession s, double u, double v) { var q = s.Camera.Project(new Vec3(u, v, 0))!.Value; return (q.x, q.y); }

    static void Drag(CadSession s, SketchTool tool, (double x, double y)[] path, bool shift = false)
    {
        PointerInfo At((double x, double y) p, bool left) => new(p.x, p.y, left, shift, false, false);
        tool.PointerDown(s, At(path[0], true));
        foreach (var p in path.Skip(1)) tool.PointerMove(s, At(p, true));
        tool.PointerUp(s, At(path[^1], false));
    }

    [AvaloniaFact(DisplayName = "SKT-H031 | A left-to-right box takes only the curves fully inside it; right-to-left takes any it touches")]
    [Trait("TestId", "SKT-H031")]
    public void BoxWindowAndCrossing()
    {
        var (s, sk, tool, near, far) = Scene(0);
        var a = Px(s, 10, 10); var half = Px(s, 60, 40);       // the box ends halfway along the line
        Drag(s, tool, [a, half]);
        Assert.Empty(sk.SelEntities);
        Drag(s, tool, [half, a]);
        Assert.Contains(near, sk.SelEntities);
        Assert.DoesNotContain(far, sk.SelEntities);

        sk.SelEntities.Clear();
        var whole = Px(s, 80, 80);
        Drag(s, tool, [a, whole]);
        Assert.Contains(near, sk.SelEntities);
        Assert.DoesNotContain(far, sk.SelEntities);
    }

    [AvaloniaFact(DisplayName = "SKT-H032 | Shift on a drag adds to the selection and a plain drag replaces it")]
    [Trait("TestId", "SKT-H032")]
    public void ShiftDragAdds()
    {
        var (s, sk, tool, near, far) = Scene(0);
        Drag(s, tool, [Px(s, 10, 10), Px(s, 80, 80)]);
        Assert.Equal([near], sk.SelEntities.ToArray());
        Drag(s, tool, [Px(s, 190, 190), Px(s, 260, 250)], shift: true);
        Assert.Contains(near, sk.SelEntities); Assert.Contains(far, sk.SelEntities);
        Drag(s, tool, [Px(s, 190, 190), Px(s, 260, 250)]);
        Assert.Equal([far], sk.SelEntities.ToArray());
    }

    [AvaloniaFact(DisplayName = "SKT-H033 | A lasso takes the curves it touches")]
    [Trait("TestId", "SKT-H033")]
    public void LassoTouches()
    {
        var (s, sk, tool, near, far) = Scene(1);
        Drag(s, tool, [Px(s, 5, 5), Px(s, 70, 5), Px(s, 70, 40), Px(s, 5, 40), Px(s, 5, 5)]);
        Assert.Contains(near, sk.SelEntities);
        Assert.DoesNotContain(far, sk.SelEntities);
    }

    /// <summary>A rectangle outline (four lines) plus one separate line far away.</summary>
    static (CadSession s, SketchContext sk, SelectTool tool, int[] sides, int far) Outline(int method = 0)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var sk = session.Sketch!; var m = sk.Model;
        int a = m.AddPoint(20, 20), b = m.AddPoint(60, 20), c = m.AddPoint(60, 50), d = m.AddPoint(20, 50);
        int[] sides = [m.AddLine(a, b), m.AddLine(b, c), m.AddLine(c, d), m.AddLine(d, a)];
        int far = m.AddLine(m.AddPoint(200, 200), m.AddPoint(240, 230));
        sk.Solve(session);
        var tool = new SelectTool(); tool.UseMethod(method);
        sk.SetTool(session, tool);
        return (session, sk, tool, sides, far);
    }

    static void Click(CadSession s, SketchTool tool, double u, double v, bool shift = false)
    {
        var q = Px(s, u, v);
        tool.PointerDown(s, new(q.x, q.y, true, shift, false, false));
        tool.PointerUp(s, new(q.x, q.y, false, shift, false, false));
    }

    [AvaloniaFact(DisplayName = "SKT-H034 | At Object level one click on a side takes the whole rectangle; at Edge level just that side; Shift adds sides")]
    [Trait("TestId", "SKT-H034")]
    public void ClickFollowsPickLevel()
    {
        var (s, sk, tool, sides, far) = Outline();
        Click(s, tool, 40, 20);                                   // the middle of the bottom side
        Assert.Equal(sides.OrderBy(i => i), sk.SelEntities.OrderBy(i => i));

        sk.PickLevel = SketchPick.Edge; sk.SelEntities.Clear();
        Click(s, tool, 40, 20);
        Assert.Equal([sides[0]], sk.SelEntities.ToArray());
        Click(s, tool, 60, 35, shift: true);                      // the right side
        Assert.Equal(new[] { sides[0], sides[1] }.OrderBy(i => i), sk.SelEntities.OrderBy(i => i));
        Click(s, tool, 60, 35, shift: true);                      // Shift on a selected side takes it back out
        Assert.Equal([sides[0]], sk.SelEntities.ToArray());
    }

    [AvaloniaFact(DisplayName = "SKT-H035 | A window box takes a whole rectangle only when all of it is inside; a crossing box takes it from one touched side")]
    [Trait("TestId", "SKT-H035")]
    public void BoxFollowsPickLevel()
    {
        var (s, sk, tool, sides, far) = Outline();
        Drag(s, tool, [Px(s, 10, 10), Px(s, 40, 60)]);            // covers only the left half of the rectangle
        Assert.Empty(sk.SelEntities);
        Drag(s, tool, [Px(s, 40, 60), Px(s, 10, 10)]);            // the same box dragged right to left touches it
        Assert.Equal(sides.OrderBy(i => i), sk.SelEntities.OrderBy(i => i));

        sk.SelEntities.Clear(); sk.PickLevel = SketchPick.Edge;
        Drag(s, tool, [Px(s, 40, 60), Px(s, 10, 10)]);
        Assert.True(sk.SelEntities.Count is > 0 and < 4, "at Edge level only the touched curves are taken");
        Assert.DoesNotContain(far, sk.SelEntities);
    }

    [AvaloniaFact(DisplayName = "SKT-H036 | At Point level a click takes a point and ignores curves, and a box takes only points")]
    [Trait("TestId", "SKT-H036")]
    public void PointLevelTakesPoints()
    {
        var (s, sk, tool, sides, far) = Outline();
        sk.PickLevel = SketchPick.Point;
        Click(s, tool, 40, 20);                                   // on a curve, not on a point
        Assert.Empty(sk.SelEntities); Assert.Empty(sk.SelPoints);
        Drag(s, tool, [Px(s, 70, 60), Px(s, 10, 10)]);            // around the four corners
        Assert.Empty(sk.SelEntities);
        Assert.True(sk.SelPoints.Count >= 4, "the four corners are selected");
    }
}
