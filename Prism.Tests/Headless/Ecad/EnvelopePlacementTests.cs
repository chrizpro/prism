using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Ecad;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/ecad/README.md">ECAD mechanical modeling.</seealso>
[Trait("Feature", "ECAD")]
[Trait("Layer", "Headless")]
public class EnvelopePlacementTests
{
    static SceneObject MakeBoard(CadSession session, double elevation)
    {
        var command = Begin(session, () => new PcbBoardCommand());
        SetToggle(command, "Four mounting holes", false);
        SetNumber(command, "Board thickness", 1.6);
        SetNumber(command, "Bottom elevation (Z)", elevation);
        session.CommitCommand();
        return session.Objects.Last(o => o.Kind == ObjKind.Solid);
    }

    [AvaloniaFact(DisplayName = "ECD-H011 | A picked board face puts the envelope on that board with no elevation to type")]
    [Trait("TestId", "ECD-H011")]
    public void EnvelopeSitsOnEachPickedBoard()
    {
        var session = new CadSession();
        var low = MakeBoard(session, 0);
        var high = MakeBoard(session, 20);
        foreach (var (board, top) in new[] { (low, 1.6), (high, 21.6) })
        {
            var command = Begin(session, () => new ComponentEnvelopeCommand());
            Pick(command, "Board face", FaceFacing(board, Vec3.UnitZ));
            SetNumber(command, "Center X", 5);
            SetNumber(command, "Center Y", 3);
            SetNumber(command, "Body width (X)", 10);
            SetNumber(command, "Body length (Y)", 6);
            SetNumber(command, "Body height", 4);
            session.CommitCommand();
            var envelope = session.Objects.Last();
            AssertBounds(envelope.Shape, new(0, 0, top), new(10, 6, top + 4));
        }
    }

    [AvaloniaFact(DisplayName = "ECD-H012 | Clicking a vertical board face stands the envelope on that face")]
    [Trait("TestId", "ECD-H012")]
    public void EnvelopeStandsOnClickedVerticalFace()
    {
        var session = new CadSession();
        var board = CreateBoxBody(session, Vec3.Zero, 40, 2, 30);
        var face = Enumerable.Range(0, board.Shape.Count(SubKind.Face))
            .Select(i => new SelItem(board.Id, SelKind.Face, i))
            .Single(item => session.FaceFrame(item)!.Value.normal.Y < -.99);
        var command = Begin(session, () => new ComponentEnvelopeCommand());
        command.ActivateFirstOpenSlot();
        Assert.True(command.PickPoint(new PickResult(face, new Vec3(12, 0, 14), -Vec3.UnitY, 0), false));
        SetNumber(command, "Body width (X)", 6);
        SetNumber(command, "Body length (Y)", 4);
        SetNumber(command, "Body height", 5);
        session.CommitCommand();
        var envelope = session.Objects.Last().Shape.Props();
        AssertNear(12, envelope.Center.X, "Clicked X");
        AssertNear(14, envelope.Center.Z, "Clicked Z");
        AssertNear(0, envelope.Max.Y, "Mounting plane");
        AssertNear(-5, envelope.Min.Y, "Height away from the board");
    }

    [AvaloniaFact(DisplayName = "ECD-H013 | With no board face the envelope still uses world coordinates")]
    [Trait("TestId", "ECD-H013")]
    public void EnvelopeWithoutBoardUsesWorldCoordinates()
    {
        var session = new CadSession();
        var command = Begin(session, () => new ComponentEnvelopeCommand());
        SetNumber(command, "Center X", 7);
        SetNumber(command, "Board face elevation (Z)", 3);
        SetNumber(command, "Body height", 2);
        session.CommitCommand();
        var envelope = session.Objects.Last().Shape.Props();
        AssertNear(7, envelope.Center.X, "Center X");
        AssertNear(3, envelope.Min.Z, "Elevation");
    }
}
