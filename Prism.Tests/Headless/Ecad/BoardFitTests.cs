using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Ecad;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/ecad/README.md">ECAD placement, fit and exchange.</seealso>
[Trait("Feature", "ECAD")]
[Trait("Layer", "Headless")]
public sealed class BoardFitTests
{
    [AvaloniaFact(DisplayName = "ECD-H006 | Place on Board and Mounting Hole use the clicked face of a vertical board")]
    [Trait("TestId", "ECD-H006")]
    public void FacePickPlacesComponentAndCutsAcrossBoard()
    {
        var session = new CadSession();
        var board = CreateBoxBody(session, Vec3.Zero, 40, 2, 30);
        var part = CreateBoxBody(session, new Vec3(50, 10, 5), 6, 2, 3);
        var face = Enumerable.Range(0, board.Shape.Count(SubKind.Face))
            .Select(i => new SelItem(board.Id, SelKind.Face, i))
            .Single(item => session.FaceFrame(item)!.Value.normal.Y < -.99);
        var hit = new PickResult(face, new Vec3(12, 0, 14), -Vec3.UnitY, 0);
        var place = Begin(session, () => new PlaceOnBoardCommand());
        Pick(place, "Component body", part);
        place.ActivateFirstOpenSlot();
        Assert.True(place.PickPoint(hit, false));
        SetNumber(place, "Rotation on board", 90);
        session.CommitCommand();
        var placed = session.Find(part.Id)!.Shape.Props();
        AssertNear(12, placed.Center.X, "Mount X");
        AssertNear(14, placed.Center.Z, "Mount Z");
        AssertNear(0, placed.Max.Y, "Mounting plane");
        AssertNear(-3, placed.Min.Y, "Component height above board");
        AssertNear(2, placed.Size.X, "Rotated X span");
        AssertNear(6, placed.Size.Z, "Rotated Z span");

        var hole = Begin(session, () => new PcbHoleCommand());
        SetNumber(hole, "Hole diameter", 2);
        Assert.True(hole.PickPoint(hit with { Point = new Vec3(25, 0, 14) }, false));
        session.CommitCommand();
        AssertNear(2400 - 2 * Math.PI, session.Find(board.Id)!.Shape.Props().Volume, "Through hole volume");
    }

    [AvaloniaTheory(DisplayName = "ECD-H007 | Clearance distinguishes a sufficient gap, a small gap and overlap")]
    [InlineData(11.5, "1.5 mm", "Meets required clearance")]
    [InlineData(10.5, "0.5 mm", "Below required clearance")]
    [InlineData(9.5, "0 mm", "Below required clearance")]
    [Trait("TestId", "ECD-H007")]
    public void ClearanceUsesActualSolidGeometry(double x, string gap, string result)
    {
        var session = new CadSession();
        var part = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var lid = CreateBoxBody(session, new Vec3(x, 2, 2), 10, 4, 4);
        var command = Begin(session, () => new BoardClearanceCommand());
        Pick(command, "Component or board", part);
        Pick(command, "Enclosure or obstacle", lid);
        BuildOutput(command);
        Assert.Equal(gap, Assert.Single(command.Results, row => row.Label == "Minimum gap").Value);
        Assert.Equal(result, Assert.Single(command.Results, row => row.Label == "Result").Value);
    }

    [AvaloniaFact(DisplayName = "ECD-H008 | Interference never reports a clear result for unchecked pairs")]
    [Trait("TestId", "ECD-H008")]
    public void InvalidGeometryMakesInterferenceIncomplete()
    {
        var session = new CadSession();
        CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        session.AddObject(Occ.Line(new Vec3(20, 0, 0), new Vec3(30, 0, 0)), "Invalid solid", ObjKind.Solid);
        var command = Begin(session, () => new InterferenceCommand());
        BuildOutput(command);
        Assert.Contains(command.Results, row => row.Label == "Check incomplete");
        Assert.DoesNotContain(command.Results, row => row.Value.StartsWith("No interference", StringComparison.Ordinal));
    }

    [AvaloniaFact(DisplayName = "ECD-H009 | ECAD exposes the board workflow and exports just the reviewed body")]
    [Trait("TestId", "ECD-H009")]
    public void ExportUsesTheReviewedSelection()
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(tab => tab.Title == "ECAD");
        Assert.Equal(new[] { "Board", "Components", "Fit check", "Export" },
            tab.Groups.Select(group => group.Title).Where(title => title is "Board" or "Components" or "Fit check" or "Export"));
        var export = Assert.Single(tab.Groups.Single(group => group.Title == "Export").Items.OfType<RibbonButton>());
        var session = vm.Session;
        var board = CreateBoxBody(session, Vec3.Zero, 40, 30, 2);
        CreateBoxBody(session, new Vec3(0, 0, 5), 4, 4, 8);
        var path = Path.Combine(Path.GetTempPath(), $"prism-ecad-{Guid.NewGuid():N}.step");
        try
        {
            vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(path);
            session.SelectItems([SelItem.Of(board)]);
            export.Invoke();
            var command = Assert.IsType<BoardStepExportCommand>(session.ActiveCommand);
            BuildOutput(command);
            Assert.Equal(board.Name, Assert.Single(command.Results, row => row.Label == "Export body").Value);
            command.Params.OfType<ActionParam>().Single(param => param.Label == "Save STEP").Run();
            using var imported = Occ.ImportStep(path);
            Assert.Equal(1, imported.Count(SubKind.Solid));
            AssertNear(2400, imported.Props().Volume, "Exported board volume");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
