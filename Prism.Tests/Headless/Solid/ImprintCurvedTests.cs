using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/solid-details/README.md#emboss-on-a-curved-face">Imprint on a curved face.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class ImprintCurvedTests
{
    [AvaloniaFact(DisplayName = "SOL-H176 | Imprint of a sketch on a cylinder marks the front only: one new face of the exact patch area, body unchanged")]
    [Trait("TestId", "SOL-H176")]
    public void ImprintOnACylinder()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), 20, 40), "Drum", ObjKind.Solid);
        var sketch = CreateRectangleSketch(session, Plane3.XZ with { Origin = new Vec3(0, -30, 0) }, 12, 10, startU: -6, startV: 15);
        int side = Enumerable.Range(0, drum.Shape.Count(SubKind.Face)).First(i => { using var f = drum.Shape.Sub(SubKind.Face, i); return Occ.FaceInfoOf(f).Type == "Cylinder"; });
        var command = Begin(session, () => new SplitFaceCommand());
        Pick(command, "Face to split", new SelItem(drum.Id, SelKind.Face, side));
        Pick(command, "Splitting plane, edge or sketch", sketch);
        SetChoice(command, "Mode", "Imprint (mark curves on the face)");
        var (_, result) = Assert.Single(BuildOutput(command).Replace);
        Assert.True(result.IsValid);
        Assert.Equal(drum.Shape.Count(SubKind.Face) + 1, result.Count(SubKind.Face));
        AssertClose(drum.Shape.Props().Volume, result.Props().Volume, 1e-6, "Volume (mm³)");
        // the new face is the patch of the drum 12 mm wide as seen: 10 mm long and 2 asin(6/20) of the radius round
        var areas = Enumerable.Range(0, result.Count(SubKind.Face)).Select(i => { using var f = result.Sub(SubKind.Face, i); return f.Props().Area; }).OrderBy(a => a).ToArray();
        AssertClose(10 * 20 * 2 * Math.Asin(6.0 / 20), areas[0], areas[0] * .01, "Marked patch area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SOL-H177 | Imprint says so when curves cannot be marked on the face")]
    [Trait("TestId", "SOL-H177")]
    public void NothingToMark()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), 20, 40), "Drum", ObjKind.Solid);
        var far = session.AddObject(Occ.Polyline([new(200, 200, 200), new(210, 200, 200)], false), "Far", ObjKind.Curve);
        int side = Enumerable.Range(0, drum.Shape.Count(SubKind.Face)).First(i => { using var f = drum.Shape.Sub(SubKind.Face, i); return Occ.FaceInfoOf(f).Type == "Cylinder"; });
        var command = Begin(session, () => new SplitFaceCommand());
        Pick(command, "Face to split", new SelItem(drum.Id, SelKind.Face, side));
        Pick(command, "Splitting plane, edge or sketch", far);
        SetChoice(command, "Mode", "Imprint (mark curves on the face)");
        Assert.Null(command.TryBuild());
        Assert.False(string.IsNullOrEmpty(command.Error));
    }
}
