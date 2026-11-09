using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md#offset-joins-curves-that-only-touch">Offset joins curves that only touch.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Unit")]
public class OffsetTests
{
    [Fact(DisplayName = "SKT-U010 | Offset keeps one closed ring when lines meet at a corner without sharing a point")]
    [Trait("TestId", "SKT-U010")]
    public void CornersWithSeparatePointsStayJoined()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!; var m = sk.Model;
        // a square whose four corners each have two points (one per line), as when a line is drawn to meet another
        int Pt(double x, double y) => m.AddPoint(x, y);
        var ids = new[] { m.AddLine(Pt(0, 0), Pt(10, 0)), m.AddLine(Pt(10, 0), Pt(10, 10)), m.AddLine(Pt(10, 10), Pt(0, 10)), m.AddLine(Pt(0, 10), Pt(0, 0)) };
        foreach (int id in ids) sk.SelEntities.Add(id);

        var tool = new OffsetTool();
        sk.SetTool(s, tool);
        tool.PanelSet(s, "selection", null);          // done selecting
        tool.PanelSet(s, "dist", 1.0);
        tool.PanelSet(s, "apply", null);

        var lines = m.Entities.Where(e => e.Type == EntType.Line && !ids.Contains(e.Id)).ToList();
        Assert.Equal(4, lines.Count);
        var uses = lines.SelectMany(l => new[] { l.A, l.B }).GroupBy(p => p).ToList();
        Assert.Equal(4, uses.Count);                   // four corners, each one point
        Assert.All(uses, u => Assert.Equal(2, u.Count()));   // and each shared by two lines: no gaps
    }
}
