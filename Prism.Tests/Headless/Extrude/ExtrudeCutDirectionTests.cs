using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Extrude;

/// <summary>Extrude Cut from a sketch on a tilted face of a body must enter the metal, not silently remove nothing.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/extrude/README.md#solid-extrude">Living Solid Extrude specification.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Extrude")]
public class ExtrudeCutDirectionTests
{
    /// <summary>The sketch plane of a face is only accurate to about a micron on bent bodies, so the tool can overlap the metal by a sliver before it points away.</summary>
    [AvaloniaTheory(DisplayName = "EXT-H009 | A cut from a sketch on a tilted face turns to point into the metal and removes the hole's volume")]
    [InlineData(20, 0)]
    [InlineData(70, 0)]
    [InlineData(70, 1e-6)]
    [InlineData(20, 1e-5)]
    [Trait("TestId", "EXT-H009")]
    public void CutFromTiltedFaceEntersTheMetal(double degrees, double planeOffsetIntoMetal)
    {
        var session = new CadSession();
        var plate = session.AddObject(Occ.Rotate(Occ.Box(60, 40, 2), Vec3.Zero, Vec3.UnitY, degrees), "Plate", ObjKind.Solid);
        // the biggest face that looks up is the top of the tilted plate; its normal points out of the metal
        int top = Enumerable.Range(0, plate.Shape.Count(SubKind.Face)).Where(i => { using var f = plate.Shape.Sub(SubKind.Face, i); return Occ.FaceInfoOf(f).Normal.Z > 0.1; })
            .MaxBy(i => { using var f = plate.Shape.Sub(SubKind.Face, i); return Occ.FaceInfoOf(f).Area; });
        using var face = plate.Shape.Sub(SubKind.Face, top);
        var normal = Occ.FaceInfoOf(face).Normal.Normalized();
        var u = Vec3.Cross(Vec3.UnitY, normal).Normalized();
        var model = new SketchModel();
        model.AddCircle(model.AddPoint(0, 0), 5);
        session.EnterSketch(new Plane3(face.Props().Center - normal * planeOffsetIntoMetal, u, Vec3.UnitY), model);
        session.ExitSketch(keep: true);
        double before = plate.Shape.Props().Volume;

        var cut = StartCommand(session, session.Objects.Last(o => o.Kind == ObjKind.Sketch), () => new ExtrudeCommand());
        SetNumber(cut, "Distance", 6);
        SetChoice(cut, "Operation", "Cut");
        var output = BuildOutput(cut);

        var (_, result) = Assert.Single(output.Replace);
        AssertValidSolid(result);
        AssertClose(before - Math.PI * 25 * 2, result.Props().Volume, 1e-3, "Volume after the cut (mm³)");
        Assert.Equal("Cut (turned to point into the body)", Result(cut, "Operation"));
    }
}
