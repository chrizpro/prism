using Avalonia.Headless.XUnit;
using Prism.Core.Form;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/freeform/README.md#cage-edit-checks">Freeform cage edit checks.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class FormConvertTests
{
    [AvaloniaFact(DisplayName = "FRM-H120 | A fully creased cube converts to a valid solid with the cube's own volume")]
    [Trait("TestId", "FRM-H120")]
    public void CreasedCubeConvertsExactly()
    {
        var cage = FormOps.Box(30, 20, 10, 1, 1, 1);
        FormOps.SetCrease(cage, cage.Edges(), 5);
        using var solid = FormToBrep.ToShape(cage);
        Assert.True(solid.IsValid);
        Assert.True(solid.HasSolids);
        AssertClose(6000, solid.Props().Volume, 60, "Volume of the converted cube (mm³)");
        var size = solid.Props().Size;
        AssertClose(30, size.X, 0.6, "Width (mm)"); AssertClose(20, size.Y, 0.6, "Depth (mm)"); AssertClose(10, size.Z, 0.6, "Height (mm)");
    }

    [AvaloniaFact(DisplayName = "FRM-H121 | A smooth cube converts to a rounded solid that stays inside its cage")]
    [Trait("TestId", "FRM-H121")]
    public void SmoothCubeConvertsRounded()
    {
        var cage = FormOps.Box(30, 20, 10, 1, 1, 1);
        using var solid = FormToBrep.ToShape(cage);
        Assert.True(solid.IsValid && solid.HasSolids);
        var volume = solid.Props().Volume;
        Assert.InRange(volume, 6000 * .3, 6000 * .9);
        var (min, max) = cage.Bounds(); var box = solid.Props();
        Assert.True(box.Min.X >= min.X - 0.5 && box.Max.X <= max.X + 0.5, "the smoothed form does not pass outside the cage");
    }

    [AvaloniaFact(DisplayName = "FRM-H122 | A quad-ball converts to a valid solid whose volume sits just inside the cage's sphere")]
    [Trait("TestId", "FRM-H122")]
    public void SphereConverts()
    {
        var cage = FormOps.Sphere(15, 3);
        using var solid = FormToBrep.ToShape(cage);
        Assert.True(solid.IsValid && solid.HasSolids);
        double sphere = 4.0 / 3 * Math.PI * 15 * 15 * 15;
        Assert.InRange(solid.Props().Volume, sphere * .7, sphere * 1.02);
    }

    [AvaloniaFact(DisplayName = "FRM-H123 | Convert with fewer faces gives no more faces than the exact conversion, with a similar volume")]
    [Trait("TestId", "FRM-H123")]
    public void FewerFacesIsLighter()
    {
        var cage = FormOps.Sphere(15, 3);
        using var exact = FormToBrep.ToShape(cage, ConvertDetail.Exact);
        using var fewer = FormToBrep.ToShape(cage, ConvertDetail.Fewer);
        Assert.True(fewer.IsValid);
        Assert.True(fewer.Count(SubKind.Face) <= exact.Count(SubKind.Face), $"{fewer.Count(SubKind.Face)} faces against {exact.Count(SubKind.Face)}");
        AssertClose(exact.Props().Volume, fewer.Props().Volume, exact.Props().Volume * .03, "Volume against the exact conversion (mm³)");
    }
}
