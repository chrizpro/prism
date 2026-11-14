using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#4-solid-dress-up-depth">0.4 modeling gaps, item 4 (DRS-P2).</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class ShellPerFaceTests
{
    static Shape Shell(Vec3[] ownFaces, double ownThickness, string direction = "Inside")
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 20, 20, 20);
        var command = Begin(session, () => new ShellCommand());
        Pick(command, "Faces to remove", FaceFacing(box, new(0, 0, 1)));
        SetNumber(command, "Wall thickness", 2);
        SetChoice(command, "Direction", direction);
        if (ownFaces.Length > 0)
        {
            Pick(command, "Faces with a different wall (optional)", ownFaces.Select(n => FaceFacing(box, n)).ToArray());
            SetNumber(command, "Wall thickness on those faces", ownThickness);
        }
        return Assert.Single(BuildOutput(command).Replace).Item2;
    }

    [AvaloniaFact(DisplayName = "SOL-H155 | Shell with no per-face walls is unchanged: an open box with 2 mm walls")]
    [Trait("TestId", "SOL-H155")]
    public void PlainShellIsUnchanged() =>
        AssertClose(8000 - 16 * 16 * 18, Shell([], 0).Props().Volume, 1e-3, "Volume (mm³)");

    [AvaloniaFact(DisplayName = "SOL-H156 | Shell gives one wall its own thickness")]
    [Trait("TestId", "SOL-H156")]
    public void OneWallIsThicker() =>
        // the side wall is 4 mm, so the cavity is 14 wide (not 16): 8000 - 14 * 16 * 18
        AssertClose(8000 - 14 * 16 * 18, Shell([new Vec3(1, 0, 0)], 4).Props().Volume, 1e-3, "Volume (mm³)");

    [AvaloniaFact(DisplayName = "SOL-H157 | Shell gives two walls their own thickness, one thinner")]
    [Trait("TestId", "SOL-H157")]
    public void TwoWallsDiffer() =>
        // both X walls are 1 mm, the Y walls stay 2 mm: the cavity is 18 (X) by 16 (Y) by 18 (Z)
        AssertClose(8000 - 18 * 16 * 18, Shell([new Vec3(1, 0, 0), new Vec3(-1, 0, 0)], 1).Props().Volume, 1e-3, "Volume (mm³)");
}
