using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>Trim: which curves cut, and the owner's rectangle-and-circle case (2026-11-19).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/rectangle/README.md">Rectangle modes and controls.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public sealed class SketchTrimTests
{
    /// <summary>
    /// A tall 20 × 60 rectangle centred on the origin with both construction diagonals, and a circle through its two top corners and the point
    /// (0, 45) above them: the circle's lower part lies inside the rectangle, and one diagonal crosses it there.
    /// </summary>
    static (CadSession s, SketchModel m, int circle, int top, int[] corners) Setup(double nudge = 0)
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY, alignView: true);
        var m = s.Sketch!.Model;
        int a = m.AddPoint(-10, -30), b = m.AddPoint(10, -30), c = m.AddPoint(10, 30), d = m.AddPoint(-10, 30);
        m.AddLine(a, b); m.AddLine(b, c); int top = m.AddLine(c, d); m.AddLine(d, a);
        m.AddLine(a, c, true); m.AddLine(b, d, true);
        double cy = 1025.0 / 30, r = 45 - cy;   // the circle through (±10, 30) and (0, 45)
        int circle = m.AddCircle(m.AddPoint(0, cy), r + nudge);   // a solved sketch leaves the circle a hair off the corners
        return (s, m, circle, top, [c, d]);
    }

    [AvaloniaTheory(DisplayName = "SKT-H067 | Trim removes a circle's lower part inside a rectangle back to the corners it passes through, also when solving left it a hair off them")]
    [InlineData(0.0)]
    [InlineData(1e-6)]
    [InlineData(-1e-6)]
    [Trait("TestId", "SKT-H067")]
    public void CircleThroughCornersTrimsToTheCorners(double nudge)
    {
        var (s, m, circle, _, _) = Setup(nudge);
        Assert.True(TrimTool.Run(s, circle, 0, 24));
        Assert.DoesNotContain(m.Entities, e => e.Type == EntType.Circle);
        var arc = Assert.Single(m.Entities, e => e.Type == EntType.Arc);
        var (cx, cy, r, a0, sweep) = m.ArcGeometry(arc);
        var ends = new[] { (cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), (cx + r * Math.Cos(a0 + sweep), cy + r * Math.Sin(a0 + sweep)) }.Select(p => (Math.Round(p.Item1, 4), Math.Round(p.Item2, 4))).Order().ToArray();
        Assert.Equal(new[] { (-10.0, 30.0), (10.0, 30.0) }, ends);
        double mid = a0 + sweep / 2;
        Assert.True(cy + r * Math.Sin(mid) > 44.99, "the arc that stays is the one above the rectangle");
    }

    [AvaloniaFact(DisplayName = "SKT-H069 | Insert Point snaps to a line's midpoint or a crossing, so one piece can be deleted without Trim")]
    [Trait("TestId", "SKT-H069")]
    public void InsertPointSnapsAndSplits()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY, alignView: true);
        var sk = s.Sketch!; var m = sk.Model;
        int line = m.AddLine(m.AddPoint(0, 0), m.AddPoint(40, 0));
        m.AddLine(m.AddPoint(30, -10), m.AddPoint(30, 10));                // crosses it at x = 30
        foreach (var (target, expect) in new[] { (20.0, 20.0), (30.0, 30.0) })
        {
            var at = s.Camera.Project(sk.Plane.PointAt(target, 0))!.Value;
            var snapped = InsertPointTool.Snap(s, sk, m.E(line), at.x + 3, at.y + 2);
            Assert.NotNull(snapped);
            Assert.Equal(expect, snapped.Value.u, 6); Assert.Equal(0, snapped.Value.v, 6);
        }
        int mid = InsertOps.Insert(m, line, 20, 0);
        Assert.Equal(20, m.P(mid).X, 9);
        var halves = m.Entities.Where(e => e.Type == EntType.Line && m.PointsOf(e).Contains(mid)).ToList();
        Assert.Equal(2, halves.Count);
        m.RemoveEntity(halves.Single(e => m.PointsOf(e).Any(p => m.P(p).X > 39)).Id);
        var kept = Assert.Single(m.Entities, e => e.Type == EntType.Line && m.PointsOf(e).Contains(mid));
        Assert.Equal(new[] { 0.0, 20.0 }, m.PointsOf(kept).Select(p => m.P(p).X).Order());
    }

    [AvaloniaFact(DisplayName = "SKT-H068 | Construction lines never cut normal geometry, but a construction line is still trimmed where normal curves cross it")]
    [Trait("TestId", "SKT-H068")]
    public void ConstructionDoesNotCut()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY, alignView: true);
        var m = s.Sketch!.Model;
        int line = m.AddLine(m.AddPoint(-20, 0), m.AddPoint(20, 0));
        int guide = m.AddLine(m.AddPoint(0, -10), m.AddPoint(0, 10), true);
        var plan = TrimTool.Plan(s.Sketch, line, 5, 0)!;
        Assert.True(plan.DeleteAll, "a crossing construction line does not split the line");
        var cut = TrimTool.Plan(s.Sketch, guide, 0, 5)!;
        Assert.False(cut.DeleteAll);
        Assert.Equal(line, cut.Lo!.Value.Cutter);
    }
}
