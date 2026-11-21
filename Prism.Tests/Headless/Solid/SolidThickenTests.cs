using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#4-solid-dress-up-depth">0.4 modeling gaps, item 4 (DRS-P4).</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SolidThickenTests
{
    static Shape Thicken(double thickness, params Vec3[] faceNormals)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 10, 10, 10);
        var command = Begin(session, () => new ThickenCommand());
        Pick(command, "Surface", faceNormals.Select(n => FaceFacing(box, n)).ToArray());
        SetNumber(command, "Thickness", thickness);
        var (old, result) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(box.Id, old.Id);
        return result;
    }

    [AvaloniaFact(DisplayName = "SOL-H152 | Thicken on a solid's top face grows it outward by the thickness")]
    [Trait("TestId", "SOL-H152")]
    public void GrowsAFaceOutward()
    {
        var result = Thicken(3, new Vec3(0, 0, 1));
        AssertClose(1300, result.Props().Volume, 1e-3, "Volume (mm³)");
        AssertClose(13, result.Props().Size.Z, 1e-6, "Height (mm)");
        Assert.Equal(6, result.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "SOL-H153 | Thicken with a negative thickness cuts a face inward")]
    [Trait("TestId", "SOL-H153")]
    public void CutsAFaceInward()
    {
        var result = Thicken(-3, new Vec3(0, 0, 1));
        AssertClose(700, result.Props().Volume, 1e-3, "Volume (mm³)");
        AssertClose(7, result.Props().Size.Z, 1e-6, "Height (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H154 | Thicken grows two faces of one solid together")]
    [Trait("TestId", "SOL-H154")]
    public void GrowsTwoFaces()
    {
        var result = Thicken(2, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
        // 10x10x10 plus a 2 mm slab over the top face (10x10x2) and one over the side face (2x10x10); neither fills the corner between them
        AssertClose(1400, result.Props().Volume, 1e-3, "Volume (mm³)");
    }
}
