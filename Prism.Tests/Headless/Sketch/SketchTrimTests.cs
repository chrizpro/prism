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

    /// <summary>The owner's sketch of 2026-11-20: an ellipse whose centre lies on the top edge of a rectangle exactly as wide as the ellipse, with both
    /// construction diagonals. The top edge ends on the ellipse's two side vertices and the rectangle's sides touch it there.</summary>
    static (CadSession s, SketchModel m, int ellipse, int diagonal) EllipseSetup()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY, alignView: true);
        var m = s.Sketch!.Model;
        int a = m.AddPoint(-50, -60), b = m.AddPoint(50, -60), c = m.AddPoint(50, 40), d = m.AddPoint(-50, 40);
        m.AddLine(a, b); m.AddLine(b, c); m.AddLine(c, d); m.AddLine(d, a);
        int diagonal = m.AddLine(a, c, true); m.AddLine(b, d, true);
        int ellipse = m.AddEllipse(m.AddPoint(0, 40), m.AddPoint(50, 40), 38);
        return (s, m, ellipse, diagonal);
    }

    [AvaloniaFact(DisplayName = "SKT-H070 | Trim removes the lower half of an ellipse inside a rectangle back to where the top edge meets it; the upper half stays as a curve")]
    [Trait("TestId", "SKT-H070")]
    public void EllipseLowerHalfTrims()
    {
        var (s, m, ellipse, _) = EllipseSetup();
        Assert.True(TrimTool.Run(s, ellipse, 0, 2));
        Assert.DoesNotContain(m.Entities, e => e.Type == EntType.Ellipse);
        var kept = Assert.Single(m.Entities, e => e.Type == EntType.Spline);
        var pts = kept.Pts.Select(m.P).ToList();
        var ends = new[] { pts[0], pts[^1] }.Select(p => (Math.Round(p.X, 3), Math.Round(p.Y, 3))).OrderBy(p => p.Item1).ToArray();
        Assert.Equal(new[] { (-50.0, 40.0), (50.0, 40.0) }, ends);
        Assert.All(pts, p => Assert.True(p.Y >= 40 - 1e-6, "only the upper half stays"));
        Assert.Contains(pts, p => Math.Abs(p.X) < 4 && Math.Abs(p.Y - 78) < 0.5);
    }

    [AvaloniaFact(DisplayName = "SKT-H071 | Trim leaves construction lines alone: a click on a diagonal changes nothing")]
    [Trait("TestId", "SKT-H071")]
    public void ConstructionLinesAreNotTrimmed()
    {
        var (s, m, _, diagonal) = EllipseSetup();
        var sk = s.Sketch!;
        sk.SetTool(s, new TrimTool());
        int before = m.Entities.Count;
        var at = s.Camera.Project(sk.Plane.PointAt(-25, -35))!.Value;   // on the diagonal from (-50, -60) to (50, 40), away from other curves
        s.PointerDown(new PointerInfo(at.x, at.y, true, false, false, false));
        s.PointerUp(new PointerInfo(at.x, at.y, false, false, false, false));
        Assert.Equal(before, m.Entities.Count);
        Assert.NotNull(m.EntityOrNull(diagonal));
    }
}
