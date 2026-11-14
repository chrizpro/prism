using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#4-solid-dress-up-depth">0.4 modeling gaps, item 4.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class DressUpDepthTests
{
    const double Side = 10;

    static (CadSession session, SceneObject box) Box()
    {
        var session = new CadSession();
        return (session, CreateBoxBody(session, Vec3.Zero, Side, Side, Side));
    }

    /// <summary>The edge of the box along X on the front-bottom (y = 0, z = 0).</summary>
    static SelItem FrontBottom(SceneObject box) =>
        Assert.Single(EdgesWhere(box, e => e.Min.Y < 1e-6 && e.Max.Y < 1e-6 && e.Min.Z < 1e-6 && e.Max.Z < 1e-6 && e.Max.X - e.Min.X > 1));

    static double RemovedVolume(string style, Action<ChamferCommand>? set = null)
    {
        var (session, box) = Box();
        var command = StartCommand(session, box, () => new ChamferCommand());
        command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
        SetChoice(command, "Style", style);
        set?.Invoke(command);
        var shape = Assert.Single(BuildOutput(command).Replace).Item2;
        Assert.True(shape.IsValid);
        return Side * Side * Side - shape.Props().Volume;
    }

    [AvaloniaTheory(DisplayName = "DRS-H001 | Chamfer can cut two distances or a distance and an angle")]
    [InlineData("Equal distance", 0.0, 0.5 * 3 * 3 * Side)]
    [InlineData("Two distances", 0.0, 0.5 * 3 * 1.5 * Side)]
    [InlineData("Distance and angle", 30.0, 0.5 * 3 * 1.7320508075688772 * Side)]
    [Trait("TestId", "DRS-H001")]
    public void ChamferStylesRemoveTheExpectedVolume(string style, double angle, double expected) =>
        AssertClose(expected, RemovedVolume(style, c =>
        {
            SetNumber(c, "Distance", 3);
            if (style == "Two distances") SetNumber(c, "Second distance", 1.5);
            if (style == "Distance and angle") SetNumber(c, "Angle", angle);
        }), 1e-3, "Removed volume (mm³)");

    [AvaloniaFact(DisplayName = "DRS-H002 | Swapping the two sides of a two-distance chamfer puts the long cut on the other face")]
    [Trait("TestId", "DRS-H002")]
    public void SwapMovesTheChamferToTheOtherFace()
    {
        // area of the box's front face (y = 0) after a 4 / 1 chamfer: 100 minus the leg that lies on it, times the 10 mm length
        double FrontArea(bool swap)
        {
            var (session, box) = Box();
            var command = StartCommand(session, box, () => new ChamferCommand());
            command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
            SetChoice(command, "Style", "Two distances");
            SetNumber(command, "Distance", 4); SetNumber(command, "Second distance", 1);
            command.Params.OfType<BoolParam>().Single(p => p.Label == "Swap the two sides").Value = swap;
            var shape = Assert.Single(BuildOutput(command).Replace).Item2;
            return Enumerable.Range(0, shape.Count(SubKind.Face)).Select(i => shape.Sub(SubKind.Face, i).Props())
                .Where(p => p.Max.Y < 1e-6).Select(p => p.Area).Single();
        }
        double plain = FrontArea(false), swapped = FrontArea(true);
        Assert.Equal(new[] { Side * (Side - 4), Side * (Side - 1) }.OrderBy(x => x), new[] { plain, swapped }.OrderBy(x => x).Select(x => Math.Round(x, 6)));
    }

    [AvaloniaFact(DisplayName = "DRS-H003 | Fillet takes a radius per edge, and an unlisted edge uses the radius")]
    [Trait("TestId", "DRS-H003")]
    public void FilletRadiusPerEdge()
    {
        double Removed(string perEdge)
        {
            var (session, box) = Box();
            var edges = EdgesWhere(box, e => e.Max.X - e.Min.X > 1 && (e.Min.Y < 1e-6 || e.Max.Y > Side - 1e-6) && (e.Min.Z < 1e-6 || e.Max.Z > Side - 1e-6)).Take(2).ToArray();
            Assert.Equal(2, edges.Length);
            var command = StartCommand(session, box, () => new FilletCommand());
            command.ConfigureInputs(() => Pick(command, "Edges", edges));
            SetNumber(command, "Radius", 1);
            if (perEdge != "") SetText(command, "Radius per edge (optional)", perEdge);
            var shape = Assert.Single(BuildOutput(command).Replace).Item2;
            Assert.True(shape.IsValid);
            return Side * Side * Side - shape.Props().Volume;
        }
        double quarter(double r) => (1 - Math.PI / 4) * r * r * Side;
        AssertClose(2 * quarter(1), Removed(""), 1e-3, "Same radius on both edges (mm³)");
        AssertClose(quarter(2) + quarter(1), Removed("2"), 1e-3, "First edge 2 mm, second falls back to Radius (mm³)");
        AssertClose(quarter(2) + quarter(3), Removed("2, 3"), 1e-3, "Each edge its own radius (mm³)");
    }

    [AvaloniaFact(DisplayName = "DRS-H004 | A radius per edge that is not a number is refused with a message")]
    [Trait("TestId", "DRS-H004")]
    public void FilletRadiusPerEdgeRejectsText()
    {
        var (session, box) = Box();
        var command = StartCommand(session, box, () => new FilletCommand());
        command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
        SetText(command, "Radius per edge (optional)", "two");
        AssertBuildFails(command, "not a radius");
    }
}
