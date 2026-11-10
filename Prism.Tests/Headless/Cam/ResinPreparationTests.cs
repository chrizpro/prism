using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cam/README.md#3d-resin">3D Resin workflow.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public sealed class ResinPreparationTests
{
    [AvaloniaFact(DisplayName = "CAM-H011 | Resin ribbon groups the existing geometry tools and opens export review")]
    [Trait("TestId", "CAM-H011")]
    public void RibbonGroupsPreparationAndTroubleshooting()
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(tab => tab.Title == "3D Resin");
        Assert.Equal(new[] { "Prepare", "Troubleshoot", "Export" }, tab.Groups.Select(group => group.Title));
        var buttons = tab.Groups.SelectMany(group => group.Items).OfType<RibbonButton>().ToArray();
        Assert.Equal(new[] { "Import mold", "Tilt part", "Hollow & Drain", "Resin check", "Troubleshoot geometry", "Export for resin" },
            buttons.Select(button => button.Label));
        Assert.Equal(new[] { "Hollow body", "Drain / vent hole" }, buttons[2].Menu!.Select(button => button.Label));
        Assert.Equal(new[] { "Section", "Repair", "Mesh Quality", "Wall Thickness", "Check geometry" },
            buttons[4].Menu!.Select(button => button.Label));
        buttons[5].Invoke();
        var export = Assert.IsType<PrintExportCommand>(vm.Session.ActiveCommand);
        Assert.Equal("Export for resin", export.Title);
        Assert.Contains(export.Params, param => param is ActionParam { Label: "Review export" });
    }

    [AvaloniaFact(DisplayName = "CAM-H013 | Outward whole-body Shell leaves the original solid's space empty")]
    [Trait("TestId", "CAM-H013")]
    public void OutwardShellKeepsTheOriginalAsTheCavity()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var shell = Begin(session, () => new ShellCommand());
        Pick(shell, "Faces to remove", box);
        SetChoice(shell, "Direction", "Outside");
        using var wall = Assert.Single(BuildOutput(shell).Replace).New;
        AssertNear(24, wall.Props().Size.X, "Outside width (mm)");
        using var overlap = Occ.Common(wall, box.Shape);
        AssertNear(0, overlap.Props().Volume, "Overlap with cavity (mm cubed)");
    }

    [AvaloniaFact(DisplayName = "CAM-H012 | Hollowing and a blind drain change a sealed resin cavity into an open cavity")]
    [Trait("TestId", "CAM-H012")]
    public void DrainOpensHollowBodyWithoutPiercingOppositeWall()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var shell = Begin(session, () => new ShellCommand());
        Pick(shell, "Faces to remove", box);
        session.CommitCommand();
        var hollow = session.Find(box.Id)!;
        AssertNear(8000 - 16 * 16 * 16, hollow.Shape.Props().Volume, "Hollow body volume");
        Assert.StartsWith("1;", SealedCavities(hollow));

        var top = Enumerable.Range(0, hollow.Shape.Count(SubKind.Face))
            .Select(i => new SelItem(box.Id, SelKind.Face, i))
            .Single(item => session.FaceFrame(item) is { } f && f.point.Z > 19.99 && f.normal.Z > .99);
        var hole = Begin(session, () => new HoleCommand());
        Pick(hole, "Body or entry face", top);
        SetChoice(hole, "Extent", "Blind");
        SetNumber(hole, "Diameter", 6);
        SetNumber(hole, "Hole depth", 4);
        session.CommitCommand();
        var drained = session.Find(box.Id)!;
        AssertNear(hollow.Shape.Props().Volume - Math.PI * 9 * 2, drained.Shape.Props().Volume, "Drained body volume");
        Assert.StartsWith("0;", SealedCavities(drained));

        string SealedCavities(SceneObject body)
        {
            var check = Begin(session, () => new ResinCheckCommand());
            Pick(check, "Model", body);
            check.Params.OfType<ActionParam>().Single(param => param.Label == "Run checks").Run();
            string result = Assert.Single(check.Results, row => row.Label == "Sealed cavities").Value;
            session.CancelCommand();
            return result;
        }
    }
}
