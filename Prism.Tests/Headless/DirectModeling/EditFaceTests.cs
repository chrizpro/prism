using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>Edit Face: a flat four-sided face becomes a grid of control points with a locked outline. Owner-approved 2026-11-17.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct modeling: Edit Face.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class EditFaceTests
{
    static (CadSession session, SceneObject box, EditFaceCommand command) Start(int cells = 3)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 30, 10);
        var command = Begin(session, () => new EditFaceCommand());
        SetInteger(command, "Grid cells across", cells);
        SetInteger(command, "Grid cells up", cells);
        Pick(command, "Face to reshape", FaceFacing(box, Vec3.UnitZ));
        command.Touch();                       // the panel does this when a face is clicked
        return (session, box, command);
    }

    static Vec3[] Points(EditFaceCommand command) => (Vec3[])command.Model!.Points.Clone();

    [AvaloniaTheory(DisplayName = "DIR-H091 | A box top shows a grid with its outer ring locked")]
    [InlineData(3, 16, 12)]
    [InlineData(5, 36, 20)]
    [Trait("TestId", "DIR-H091")]
    public void GridHasLockedRing(int cells, int points, int locked)
    {
        var (_, _, command) = Start(cells);
        Assert.NotNull(command.Model);
        Assert.Equal(points, command.Model.Points.Length);
        Assert.Equal(locked, Enumerable.Range(0, points).Count(command.IsLocked));
    }

    [AvaloniaFact(DisplayName = "DIR-H092 | Moving an inner point makes a valid solid with the outline unchanged")]
    [Trait("TestId", "DIR-H092")]
    public void MovingInnerPointReshapesFace()
    {
        var (session, box, command) = Start();
        var pts = Points(command); pts[5] += new Vec3(0, 0, 2);
        command.ApplyPoints(pts);
        session.CommitCommand();
        var body = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertValidSolid(body.Shape);
        var bounds = body.Shape.Props();
        Assert.True(bounds.Volume > 12000 + 1, "The bump adds material.");
        Assert.True(bounds.Max.Z > 10.1 && bounds.Max.Z < 12, $"The new surface rises toward the moved point (max Z {bounds.Max.Z}).");
        AssertNear(0, bounds.Min.X, "Outline minimum X"); AssertNear(40, bounds.Max.X, "Outline maximum X");
        AssertNear(0, bounds.Min.Y, "Outline minimum Y"); AssertNear(30, bounds.Max.Y, "Outline maximum Y");
        AssertNear(0, bounds.Min.Z, "Bottom");
    }

    [AvaloniaFact(DisplayName = "DIR-H093 | A locked point stays put and moves follow the face normal by default")]
    [Trait("TestId", "DIR-H093")]
    public void LockedPointsAndNormalOnlyMoves()
    {
        var (_, _, command) = Start();
        var original = Points(command);
        var pts = Points(command);
        pts[0] += new Vec3(5, 5, 5);            // a corner: locked
        pts[5] += new Vec3(3, 0, 2);            // an inner point moved sideways and up
        command.ApplyPoints(pts);
        Assert.Equal(original[0], command.Model!.Points[0]);
        AssertNear(original[5].X, command.Model.Points[5].X, "Sideways part is dropped");
        AssertNear(original[5].Z + 2, command.Model.Points[5].Z, "Normal part is kept");

        SetChoice(command, "Move points", "Any direction");
        var free = Points(command); free[5] += new Vec3(3, 0, 0);
        command.ApplyPoints(free);
        AssertNear(original[5].X + 3, command.Model.Points[5].X, "Any direction keeps sideways moves");
    }

    [AvaloniaFact(DisplayName = "DIR-H094 | Reset points restores the flat face and nothing is applied")]
    [Trait("TestId", "DIR-H094")]
    public void ResetRestoresFlatFace()
    {
        var (_, _, command) = Start();
        var original = Points(command);
        var pts = Points(command); pts[5] += new Vec3(0, 0, 2);
        command.ApplyPoints(pts);
        Assert.NotNull(command.TryBuild());
        command.Params.OfType<ActionParam>().Single(a => a.Label == "Reset points").Run();
        Assert.Equal(original, command.Model!.Points);
        Assert.Null(command.TryBuild());
    }

    [AvaloniaFact(DisplayName = "DIR-H095 | A reshape that cannot close into a solid changes nothing")]
    [Trait("TestId", "DIR-H095")]
    public void InvalidReshapeChangesNothing()
    {
        var (session, box, command) = Start();
        var history = session.Current;
        var pts = Points(command); pts[5] += new Vec3(0, 0, -60);       // through the bottom of a 10 mm plate
        command.ApplyPoints(pts);
        session.CommitCommand();
        Assert.Same(history, session.Current);
        AssertNear(12000, session.Find(box.Id)!.Shape.Props().Volume, "Volume is unchanged");
    }

    [AvaloniaFact(DisplayName = "DIR-H096 | Undo, redo and Edit feature step over the whole edit")]
    [Trait("TestId", "DIR-H096")]
    public void UndoRedoAndEditFeature()
    {
        var (session, box, command) = Start();
        var pts = Points(command); pts[5] += new Vec3(0, 0, 2);
        command.ApplyPoints(pts);
        session.CommitCommand();
        double changed = session.Find(box.Id)!.Shape.Props().Volume;
        Assert.True(changed > 12001);

        session.Undo();
        AssertNear(12000, session.Find(box.Id)!.Shape.Props().Volume, "Undo restores the flat face");
        session.Redo();
        AssertNear(changed, session.Find(box.Id)!.Shape.Props().Volume, "Redo brings the bump back");

        Assert.NotNull(session.Current.Recipe);
        Assert.True(session.EditFeature(session.Current), session.Status);
        var reopened = Assert.IsType<EditFaceCommand>(session.ActiveCommand);
        Assert.NotNull(reopened.Model);
        AssertNear(2, reopened.Model.Points[5].Z - 10, "Edit feature restores the moved point");
        session.CancelCommand();
    }

    [AvaloniaTheory(DisplayName = "DIR-H097 | Faces that are not four-sided, or have holes, are refused with a reason")]
    [InlineData("hole", "holes")]
    [InlineData("disc", "sides")]
    [Trait("TestId", "DIR-H097")]
    public void UnsupportedFacesAreRefused(string kind, string reason)
    {
        var session = new CadSession();
        Shape shape;
        if (kind == "hole")
        {
            using var plate = Occ.Box(40, 30, 10);
            using var pin = Occ.Translate(Occ.Cylinder(4, 20), new Vec3(0, 0, -5));
            shape = Occ.Cut(plate, pin);
        }
        else shape = Occ.Cylinder(10, 10);
        var body = session.AddObject(shape, "Part", ObjKind.Solid);
        var command = Begin(session, () => new EditFaceCommand());
        Pick(command, "Face to reshape", FaceFacing(body, Vec3.UnitZ));
        command.Touch();
        Assert.Null(command.Model);
        Assert.Contains(reason, session.Status);
    }
}
