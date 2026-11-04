using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Ecad;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/ecad/README.md">ECAD mechanical modeling.</seealso>
[Trait("Feature", "ECAD")]
[Trait("Layer", "Headless")]
public class BoardTests
{
    [AvaloniaTheory(DisplayName = "ECD-H001 | Board options determine volume, elevation and material")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "ECD-H001")]
    public void BoardHolesAndElevation(bool holes)
    {
        var session = new CadSession();
        session.StartCommand(() => new PcbBoardCommand());
        var command = Assert.IsType<PcbBoardCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetToggle(command, "Four mounting holes", holes);
            SetNumber(command, "Board width (X)", 40);
            SetNumber(command, "Board length (Y)", 30);
            SetNumber(command, "Board thickness", 2);
            SetNumber(command, "Bottom elevation (Z)", 7);
            SetNumber(command, "Mounting hole diameter", 4);
            SetNumber(command, "Hole center inset from edges", 6);
        });
        session.CommitCommand();
        var board = Assert.Single(session.Objects);
        AssertValidSolid(board.Shape);
        AssertBounds(board.Shape, new(-20, -15, 7), new(20, 15, 9));
        AssertNear(2400 - (holes ? 32 * Math.PI : 0), board.Shape.Props().Volume, "Board volume (mm³)");
        Assert.Equal("gfrp", board.Material?.Id);
        session.Undo();
        Assert.Empty(session.Objects);
        session.Redo();
        Assert.Equal(board.Id, Assert.Single(session.Objects).Id);
    }

    [AvaloniaTheory(DisplayName = "ECD-H002 | Invalid mounting-hole spacing leaves the document unchanged")]
    [InlineData(1)]
    [InlineData(14)]
    [Trait("TestId", "ECD-H002")]
    public void InvalidHoleInsetDoesNotCreateBoard(int inset)
    {
        var session = new CadSession();
        var history = session.Current;
        session.StartCommand(() => new PcbBoardCommand());
        var command = Assert.IsType<PcbBoardCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Board width (X)", 40);
            SetNumber(command, "Board length (Y)", 30);
            SetNumber(command, "Mounting hole diameter", 4);
            SetNumber(command, "Hole center inset from edges", inset);
        });
        Assert.Null(command.TryBuild());
        Assert.Contains("Hole centers must leave material", command.Error);
        Assert.Empty(session.Objects);
        Assert.Same(history, session.Current);
    }

    [AvaloniaTheory(DisplayName = "ECD-H003 | Envelope side, rotation and clearance determine bounds")]
    [InlineData("Top (+Z)", 0, 8, 16, 5, 8)]
    [InlineData("Bottom (−Z)", 1, 7, 17, 1, 5)]
    [Trait("TestId", "ECD-H003")]
    public void EnvelopeFitsSpecifiedSide(string side, int clearance, int minX, int maxX, int minZ, int maxZ)
    {
        var session = new CadSession();
        session.StartCommand(() => new ComponentEnvelopeCommand());
        var command = Assert.IsType<ComponentEnvelopeCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Board side", side);
            SetNumber(command, "Center X", 12);
            SetNumber(command, "Center Y", 20);
            SetNumber(command, "Board face elevation (Z)", 5);
            SetNumber(command, "Body width (X)", 12);
            SetNumber(command, "Body length (Y)", 8);
            SetNumber(command, "Rotation about Z", 90);
            SetNumber(command, "Clearance allowance", clearance);
        });
        using var envelope = BuildSingleAddedShape(command);
        AssertValidSolid(envelope);
        AssertBounds(envelope, new(minX, 14 - clearance, minZ), new(maxX, 26 + clearance, maxZ));
    }

    [AvaloniaTheory(DisplayName = "ECD-H004 | Outline board requires a closed planar region and retains its source")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "ECD-H004")]
    public void OutlineRequiresClosedRegion(bool closed)
    {
        var session = new CadSession();
        var outline = CreateCurve(session, [Vec3.Zero, new(20, 0, 0), new(20, 10, 0), new(0, 10, 0)], closed);
        var history = session.Current;
        var command = StartCommand(session, outline, () => new PcbOutlineCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Closed planar outline", outline);
            SetNumber(command, "Board thickness", 2.5);
        });
        session.CommitCommand();
        Assert.NotNull(session.Find(outline.Id));
        if (closed)
        {
            var board = Assert.Single(session.Objects, obj => obj.Kind == ObjKind.Solid);
            AssertValidSolid(board.Shape);
            AssertNear(500, board.Shape.Props().Volume, "Outline board volume (mm³)");
            Assert.Equal("gfrp", board.Material?.Id);
        }
        else
        {
            Assert.Single(session.Objects);
            Assert.Same(history, session.Current);
            Assert.False(string.IsNullOrWhiteSpace(command.Error));
        }
    }

    [AvaloniaTheory(DisplayName = "ECD-H005 | Mounting hole cuts at the requested position or rejects a miss")]
    [InlineData(4, true)]
    [InlineData(100, false)]
    [Trait("TestId", "ECD-H005")]
    public void MountingHolePreservesTargetAndHistoryOnMiss(int x, bool intersects)
    {
        var session = new CadSession();
        session.StartCommand(() => new PcbBoardCommand());
        var create = Assert.IsType<PcbBoardCommand>(session.ActiveCommand);
        create.ConfigureInputs(() => SetToggle(create, "Four mounting holes", false));
        session.CommitCommand();
        var board = Assert.Single(session.Objects);
        var originalVolume = board.Shape.Props().Volume;
        var history = session.Current;
        var command = StartCommand(session, board, () => new PcbHoleCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Board body", board);
            SetNumber(command, "Hole center X", x);
            SetNumber(command, "Hole center Y", 3);
            SetNumber(command, "Hole diameter", 4);
        });
        session.CommitCommand();
        var result = Assert.Single(session.Objects);
        Assert.Equal(board.Id, result.Id);
        Assert.Equal("gfrp", result.Material?.Id);
        AssertNear(originalVolume - (intersects ? Math.PI * 4 * 1.6 : 0), result.Shape.Props().Volume,
            "Drilled board volume (mm³)");
        if (!intersects)
        {
            Assert.Same(history, session.Current);
            Assert.Contains("does not remove material", command.Error);
        }
    }
}
