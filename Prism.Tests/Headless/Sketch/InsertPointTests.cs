using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>Insert point splits lines, arcs and circles and adds a point to a spline.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/unified-sketch.md#adding-points-to-an-existing-curve">Adding points to an existing curve.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class InsertPointTests
{
    static (CadSession s, SketchContext sk, InsertPointTool tool) Open()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY, alignView: true);
        s.Ortho = true;
        var sk = s.Sketch!;
        var tool = new InsertPointTool();
        sk.SetTool(s, tool);
        return (s, sk, tool);
    }

    static void Click(CadSession s, InsertPointTool tool, double u, double v)
    {
        var q = s.Camera.Project(new Vec3(u, v, 0))!.Value;
        tool.PointerDown(s, new(q.x, q.y, true, false, false, false));
        tool.PointerUp(s, new(q.x, q.y, false, false, false, false));
    }

    static int Count(SketchModel m, EntType t) => m.Entities.Count(e => e.Type == t);

    [AvaloniaFact(DisplayName = "SKT-H037 | Inserting a point splits a line in two and keeps them one straight line")]
    [Trait("TestId", "SKT-H037")]
    public void SplitsLine()
    {
        var (s, sk, tool) = Open();
        var m = sk.Model;
        int line = m.AddLine(m.AddPoint(0, 0), m.AddPoint(60, 0));
        sk.Solve(s);
        Click(s, tool, 20, 0);
        Assert.Equal(2, Count(m, EntType.Line));
        var pieces = m.Entities.Where(e => e.Type == EntType.Line).ToList();
        double[] lengths = pieces.Select(e => Math.Abs(m.P(e.B).X - m.P(e.A).X)).OrderBy(x => x).ToArray();
        Assert.InRange(lengths[0], 18, 22); Assert.InRange(lengths[1], 38, 42);
        Assert.Contains(m.Constraints, c => c.Type == CType.Collinear);
        Assert.True(sk.TryUndo(s));
        Assert.Equal(1, Count(m = sk.Model, EntType.Line));
    }

    [AvaloniaFact(DisplayName = "SKT-H038 | Inserting a point splits an arc in two arcs of the same circle")]
    [Trait("TestId", "SKT-H038")]
    public void SplitsArc()
    {
        var (s, sk, tool) = Open();
        var m = sk.Model;
        int c = m.AddPoint(0, 0), a = m.AddPoint(30, 0), b = m.AddPoint(0, 30);          // a quarter arc, counter-clockwise
        m.AddArc(c, a, b);
        sk.Solve(s);
        Click(s, tool, 21.2, 21.2);
        Assert.Equal(2, Count(sk.Model, EntType.Arc));
        foreach (var arc in sk.Model.Entities.Where(e => e.Type == EntType.Arc))
        {
            var centre = sk.Model.P(arc.A);
            double r1 = Math.Sqrt(Math.Pow(sk.Model.P(arc.B).X - centre.X, 2) + Math.Pow(sk.Model.P(arc.B).Y - centre.Y, 2));
            double r2 = Math.Sqrt(Math.Pow(sk.Model.P(arc.C).X - centre.X, 2) + Math.Pow(sk.Model.P(arc.C).Y - centre.Y, 2));
            Assert.InRange(r1, 29.9, 30.1); Assert.InRange(r2, 29.9, 30.1);
        }
    }

    [AvaloniaFact(DisplayName = "SKT-H039 | Inserting a point splits a circle into two arcs meeting at the point and its opposite")]
    [Trait("TestId", "SKT-H039")]
    public void SplitsCircle()
    {
        var (s, sk, tool) = Open();
        var m = sk.Model;
        m.AddCircle(m.AddPoint(0, 0), 25);
        sk.Solve(s);
        Click(s, tool, 25, 0);
        Assert.Equal(0, Count(sk.Model, EntType.Circle));
        var arcs = sk.Model.Entities.Where(e => e.Type == EntType.Arc).ToList();
        Assert.Equal(2, arcs.Count);
        var ends = arcs.SelectMany(a => new[] { a.B, a.C }).Select(id => sk.Model.P(id)).ToList();
        Assert.Contains(ends, p => Math.Abs(p.X - 25) < .5 && Math.Abs(p.Y) < .5);
        Assert.Contains(ends, p => Math.Abs(p.X + 25) < .5 && Math.Abs(p.Y) < .5);
    }

    [AvaloniaFact(DisplayName = "SKT-H040 | Inserting a point adds a point to a spline")]
    [Trait("TestId", "SKT-H040")]
    public void AddsSplinePoint()
    {
        var (s, sk, tool) = Open();
        var m = sk.Model;
        var spline = m.AddSpline([m.AddPoint(0, 0), m.AddPoint(30, 20), m.AddPoint(60, 0)], false, false);
        sk.Solve(s);
        int before = m.E(spline).Pts.Count;
        var samples = m.SplineSamples(m.E(spline));
        var on = samples[samples.Count / 4];
        Click(s, tool, on.x, on.y);
        Assert.Equal(before + 1, sk.Model.E(spline).Pts.Count);
    }

    [AvaloniaFact(DisplayName = "SKT-H041 | Clicking empty space inserts nothing")]
    [Trait("TestId", "SKT-H041")]
    public void EmptySpaceDoesNothing()
    {
        var (s, sk, tool) = Open();
        var m = sk.Model;
        m.AddLine(m.AddPoint(0, 0), m.AddPoint(60, 0));
        sk.Solve(s);
        Click(s, tool, 30, 40);
        Assert.Equal(1, Count(sk.Model, EntType.Line));
    }
}
