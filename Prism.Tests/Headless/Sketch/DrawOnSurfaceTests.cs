using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md#drawing-directly-on-a-surface">Drawing directly on a surface.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class DrawOnSurfaceTests
{
    [AvaloniaFact(DisplayName = "SKT-H022 | Esc stops adding points but keeps the curve to adjust; a second Esc cancels the tool")]
    [Trait("TestId", "SKT-H022")]
    public void EscLeavesPointPlacingBeforeCancelling()
    {
        var s = new CadSession();
        var box = s.AddObject(Occ.Box(10, 20, 30), "Block", ObjKind.Solid);
        var m = box.Mesh;

        // with nothing placed there is nothing to keep, so Esc cancels at once
        s.StartCommand(() => new DrawOnSurfaceCommand());
        s.Key("Escape");
        Assert.Null(s.ActiveCommand);

        s.StartCommand(() => new DrawOnSurfaceCommand());
        var cmd = Assert.IsType<DrawOnSurfaceCommand>(s.ActiveCommand);
        cmd.AddPoint(s, box, new Vec3(m.Min.X + 2, m.Min.Y + 3, m.Max.Z));
        cmd.AddPoint(s, box, new Vec3(m.Max.X - 2, m.Max.Y - 3, m.Max.Z));
        Assert.Equal(2, cmd.PickCount);
        Assert.True(cmd.Placing);

        Assert.True(s.Key("Escape"));
        Assert.Same(cmd, s.ActiveCommand);          // still open: the curve can be adjusted and finished
        Assert.False(cmd.Placing);
        Assert.Equal(2, cmd.PickCount);

        s.Key("Escape");
        Assert.Null(s.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "SKT-H057 | Draw on Surface can split a surface along the curve; ends short of an edge run on to it")]
    [Trait("TestId", "SKT-H057")]
    public void SplitsSurfaceAlongDrawnCurve()
    {
        var s = new CadSession();
        var sheet = Sheet(s);
        var cmd = Start(s, split: true);
        cmd.AddPoint(s, sheet, new Vec3(20, 3, 0));          // 3 mm short of each edge
        cmd.AddPoint(s, sheet, new Vec3(20, 27, 0));
        s.CommitCommand();
        Assert.False(s.StatusIsError, s.Status);
        var result = s.Find(sheet.Id)!;
        Assert.Equal(ObjKind.Surface, result.Kind);
        Assert.Equal(2, result.Shape.Count(SubKind.Face));
        Assert.Equal(1200, result.Shape.Props().Area, 3);
        using var half = result.Shape.Sub(SubKind.Face, 0);
        Assert.Equal(600, half.Props().Area, 1);
        Assert.DoesNotContain(s.Objects, o => o.Kind == ObjKind.Curve);
    }

    [AvaloniaFact(DisplayName = "SKT-H058 | A closed loop drawn on a surface cuts out a region as its own face")]
    [Trait("TestId", "SKT-H058")]
    public void ClosedLoopSplitsOutRegion()
    {
        var s = new CadSession();
        var sheet = Sheet(s);
        var cmd = Start(s, split: true);
        cmd.Params.OfType<BoolParam>().Single(p => p.Label == "Closed loop").Value = true;
        cmd.AddPoint(s, sheet, new Vec3(10, 8, 0));
        cmd.AddPoint(s, sheet, new Vec3(30, 8, 0));
        cmd.AddPoint(s, sheet, new Vec3(20, 22, 0));
        s.CommitCommand();
        Assert.False(s.StatusIsError, s.Status);
        var result = s.Find(sheet.Id)!;
        Assert.Equal(2, result.Shape.Count(SubKind.Face));
        Assert.Equal(1200, result.Shape.Props().Area, 3);
    }

    [AvaloniaFact(DisplayName = "SKT-H059 | Draw on Surface splits a face of a solid and keeps the solid")]
    [Trait("TestId", "SKT-H059")]
    public void SplitsSolidFace()
    {
        var s = new CadSession();
        var box = s.AddObject(Occ.Box(10, 20, 30), "Block", ObjKind.Solid);
        var m = box.Mesh;
        var cmd = Start(s, split: true);
        cmd.AddPoint(s, box, new Vec3((m.Min.X + m.Max.X) / 2, m.Min.Y + 2, m.Max.Z));
        cmd.AddPoint(s, box, new Vec3((m.Min.X + m.Max.X) / 2, m.Max.Y - 2, m.Max.Z));
        s.CommitCommand();
        Assert.False(s.StatusIsError, s.Status);
        var result = s.Find(box.Id)!;
        Assert.Equal(ObjKind.Solid, result.Kind);
        Assert.Equal(7, result.Shape.Count(SubKind.Face));
        Assert.Equal(6000, result.Shape.Props().Volume, 3);
    }

    static SceneObject Sheet(CadSession s)
    {
        using var boundary = Occ.Polyline([Vec3.Zero, new(40, 0, 0), new(40, 30, 0), new(0, 30, 0)], true);
        return s.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    static DrawOnSurfaceCommand Start(CadSession s, bool split)
    {
        s.StartCommand(() => new DrawOnSurfaceCommand());
        var cmd = Assert.IsType<DrawOnSurfaceCommand>(s.ActiveCommand);
        if (split) cmd.Params.OfType<ChoiceParam>().Single(p => p.Label == "Result").Index = 1;
        return cmd;
    }
}
