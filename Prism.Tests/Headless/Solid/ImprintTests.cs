using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#5-imprint-and-wrap">0.4 modeling gaps, item 5 (IMP-P1).</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class ImprintTests
{
    static (CommandOutput output, SceneObject box) Imprint(Shape curve)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var tool = session.AddObject(curve, "Curve", ObjKind.Curve);
        int top = Enumerable.Range(0, box.Shape.Count(SubKind.Face)).Single(i => Vec3.Dot(Occ.FaceInfoOf(box.Shape.Sub(SubKind.Face, i)).Normal, Vec3.UnitZ) > 0.99);
        var command = StartCommand(session, box, () => new SplitFaceCommand());
        command.ConfigureInputs(() =>
        {
            Pick(command, "Face to split", new SelItem(box.Id, SelKind.Face, top));
            Pick(command, "Splitting plane, edge or sketch", tool);
        });
        SetChoice(command, "Mode", "Imprint (mark curves on the face)");
        return (BuildOutput(command), box);
    }

    [AvaloniaFact(DisplayName = "SOL-H150 | Imprinting a circle on a flat face adds a disc face and leaves the volume unchanged")]
    [Trait("TestId", "SOL-H150")]
    public void ImprintedCircleCutsOutADisc()
    {
        using var circle = Occ.Circle(new Vec3(5, 5, 10), Vec3.UnitZ, 3);
        var (output, box) = Imprint(circle);
        var shape = Assert.Single(output.Replace).Item2;
        Assert.True(shape.IsValid);
        Assert.Equal(box.Shape.Count(SubKind.Face) + 1, shape.Count(SubKind.Face));
        AssertClose(1000, shape.Props().Volume, 1e-3, "Volume (mm³)");
        var disc = Enumerable.Range(0, shape.Count(SubKind.Face)).Select(i => shape.Sub(SubKind.Face, i).Props().Area).Single(a => Math.Abs(a - Math.PI * 9) < 1e-3);
        AssertClose(Math.PI * 9, disc, 1e-3, "Disc area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SOL-H151 | Imprinting a line that stops inside a face keeps the body valid and adds no face")]
    [Trait("TestId", "SOL-H151")]
    public void ImprintedLineThatStopsInsideAddsNoFace()
    {
        using var line = Occ.Line(new Vec3(2, 5, 10), new Vec3(6, 5, 10));
        var (output, box) = Imprint(line);
        var shape = Assert.Single(output.Replace).Item2;
        Assert.True(shape.IsValid);
        Assert.Equal(box.Shape.Count(SubKind.Face), shape.Count(SubKind.Face));
        AssertClose(1000, shape.Props().Volume, 1e-3, "Volume (mm³)");
    }
}
