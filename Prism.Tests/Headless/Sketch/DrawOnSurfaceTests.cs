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
}
