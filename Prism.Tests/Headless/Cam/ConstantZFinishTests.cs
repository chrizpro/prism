using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cam/README.md#constant-z-finish">Constant-Z finish.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public sealed class ConstantZFinishTests
{
    [AvaloniaTheory(DisplayName = "CNC-H020 | Constant-Z offsets a fresh contour at each height and skips hidden recesses")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "CNC-H020")]
    public void TaperedCavityProducesOnlyReachablePasses(bool undercut)
    {
        var session = new CadSession();
        using var block = Occ.Cylinder(20, 12);
        using var cone = Occ.Cone(undercut ? 12 : 8, undercut ? 8 : 12, 10);
        using var cavity = Occ.Translate(cone, new(0, 0, 2));
        var body = session.AddObject(Occ.Cut(block, cavity), "Tapered recess", ObjKind.Solid);
        var setup = Begin(session, () => new CamSetupCommand());
        Pick(setup, "Model body", body);
        session.CommitCommand();
        Assert.NotNull(session.CamSetup);

        var command = Begin(session, () => new ConstantZFinishCommand());
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Finish top Z", 10);
            SetNumber(command, "Finish bottom Z", 4);
            SetNumber(command, "Depth per pass", 2);
            SetNumber(command, "Tool diameter", 4);
        });
        var output = BuildOutput(command);
        var path = Assert.IsType<ToolpathInfo>(Assert.Single(output.Add).Asm);
        Assert.Equal(new double[] { 8, 6, 4 }, Enumerable.Range(0, path.MoveCount).Select(path.Move)
            .Where(move => move.kind == MoveKind.Cut).Select(move => move.z).Distinct());
        Assert.Equal(undercut ? 3 : 6, path.LayerStarts.Length);
        Assert.Equal(undercut, command.Results.Any(row => row.Label == "Skipped contours or levels"));
        Assert.Equal(session.CamSetup!.ShapeSignature, Prism.Simulation.StudySetup.Signature(body));
        var program = Post.Cnc([path], Controller.GRBL, session.CamSetup.Origin(), "Constant-Z test");
        Assert.Contains("(Constant-Z finish)", program);
        Assert.Contains("G21 G90 G17 G94 G54", program);
        foreach (var start in path.LayerStarts)
        {
            var entry = path.Move(start + 2);
            Assert.Equal(MoveKind.Plunge, entry.kind);
            Assert.Equal(path.SafeZ, path.Move(start).z, 8);
            int end = Array.FindIndex(path.LayerStarts, index => index > start);
            int stop = end < 0 ? path.MoveCount : path.LayerStarts[end];
            Assert.Equal(path.SafeZ, path.Move(stop - 1).z, 8);
            Assert.Equal(entry.x, path.Move(stop - 2).x, 8);
            Assert.Equal(entry.y, path.Move(stop - 2).y, 8);
            double expected = Math.Sqrt(entry.x * entry.x + entry.y * entry.y) < 20
                ? 8 + .4 * (entry.z - 2) - 2.05 : 22.05;
            for (int i = start + 3; i < stop - 1; i++)
            {
                var cut = path.Move(i);
                Assert.Equal(MoveKind.Cut, cut.kind);
                Assert.Equal(entry.z, cut.z, 8);
                Assert.InRange(Math.Sqrt(cut.x * cut.x + cut.y * cut.y), expected - .01, expected + .01);
            }
        }
    }

    [AvaloniaFact(DisplayName = "CNC-H021 | Constant-Z asks for stock and rejects a clearance below stock top")]
    [Trait("TestId", "CNC-H021")]
    public void RequiresSetupAndClearanceAboveStock()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new ConstantZFinishCommand());
        Pick(command, "Model to finish", body);
        AssertBuildFails(command, "Create a CAM setup");
        session.CancelCommand();
        var setup = Begin(session, () => new CamSetupCommand());
        Pick(setup, "Model body", body);
        session.CommitCommand();
        command = Begin(session, () => new ConstantZFinishCommand());
        SetNumber(command, "Top of the material (Z)", 9);
        AssertBuildFails(command, "above the setup stock");
    }
}
