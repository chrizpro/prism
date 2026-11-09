using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>Esc leaves the drawing tool; Enter finishes a curve in progress and otherwise leaves the sketch.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/unified-sketch.md#leaving-tools-and-the-sketch">Leaving tools and the sketch.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchKeysTests
{
    static (CadSession session, SketchContext sketch) Sketching()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        return (session, session.Sketch!);
    }

    [AvaloniaFact(DisplayName = "SKT-H029 | Esc leaves the drawing tool and never the sketch")]
    [Trait("TestId", "SKT-H029")]
    public void EscLeavesToolNotSketch()
    {
        var (session, sketch) = Sketching();
        sketch.SetTool(session, new LineTool(0));
        session.Key("Escape");
        Assert.IsType<SelectTool>(sketch.Tool);
        Assert.NotNull(session.Sketch);
        session.Key("Escape");
        Assert.NotNull(session.Sketch);
    }

    [AvaloniaFact(DisplayName = "SKT-H030 | Enter finishes a curve in progress, and with none in progress leaves the sketch")]
    [Trait("TestId", "SKT-H030")]
    public void EnterFinishesCurveThenSketch()
    {
        var (session, sketch) = Sketching();
        sketch.SetTool(session, new SplineTool(false));
        var tool = (SplineTool)sketch.Tool!;
        tool.Feed(session, 0, 0); tool.Feed(session, 10, 5); tool.Feed(session, 20, 0);
        Assert.NotNull(tool.LastPlacedWorld(session));
        session.Key("Enter");
        Assert.NotNull(session.Sketch);                                  // the spline is done, the sketch is still open
        Assert.Contains(sketch.Model.Entities, e => e.Type == EntType.Spline);
        session.Key("Enter");
        Assert.Null(session.Sketch);                                     // nothing in progress: Enter leaves the sketch
    }
}
