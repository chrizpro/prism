using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/construction-planes/README.md">Construction planes.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class ConstructionPlanePickTests
{
    [AvaloniaFact(DisplayName = "SOL-H160 | A plane through three picked corners contains all three; empty picks fall back to the typed coordinates")]
    [Trait("TestId", "SOL-H160")]
    public void PlaneThroughPickedCorners()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(10, 20, 30), 40, 50, 60);
        var corners = session.VertexPoints(box);
        int Corner(Vec3 at) => Enumerable.Range(0, corners.Count).First(i => corners[i].DistanceTo(at) < 1e-6);
        var a = new Vec3(10, 20, 30); var b = new Vec3(50, 20, 30); var c = new Vec3(10, 70, 90);

        var command = Begin(session, () => new ConstructionPlaneCommand());
        SetChoice(command, "Method", "Through three points");
        Pick(command, "First point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(a)));
        Pick(command, "Second point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(b)));
        Pick(command, "Third point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(c)));
        var plane = Assert.Single(BuildOutput(command).Add).Plane;
        Assert.NotNull(plane);
        foreach (var point in new[] { a, b, c })
            AssertClose(0, Vec3.Dot(point - plane.Value.Origin, plane.Value.Normal), 1e-9, "Signed plane distance (mm)");

        // the third point left to the typed coordinates
        var mixed = Begin(session, () => new ConstructionPlaneCommand());
        SetChoice(mixed, "Method", "Through three points");
        Pick(mixed, "First point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(a)));
        Pick(mixed, "Second point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(b)));
        SetNumber(mixed, "Point 3 X", 10); SetNumber(mixed, "Point 3 Y", 70); SetNumber(mixed, "Point 3 Z", 90);
        var other = Assert.Single(BuildOutput(mixed).Add).Plane;
        AssertClose(0, Vec3.Dot(c - other!.Value.Origin, other.Value.Normal), 1e-9, "Typed point on the plane (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H161 | Three picked corners in a line are refused with a message")]
    [Trait("TestId", "SOL-H161")]
    public void CollinearPickedCornersRefused()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var corners = session.VertexPoints(box);
        int Corner(Vec3 at) => Enumerable.Range(0, corners.Count).First(i => corners[i].DistanceTo(at) < 1e-6);
        var command = Begin(session, () => new ConstructionPlaneCommand());
        SetChoice(command, "Method", "Through three points");
        Pick(command, "First point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(new Vec3(0, 0, 0))));
        Pick(command, "Second point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(new Vec3(10, 0, 0))));
        Pick(command, "Third point (a corner or a point)", new SelItem(box.Id, SelKind.Vertex, Corner(new Vec3(10, 0, 0))));
        AssertBuildFails(command, "in a line");
    }

    [AvaloniaTheory(DisplayName = "SOL-H162 | Changing a construction plane's method moves picking to that method's first slot")]
    [InlineData("Through three points", "First point (a corner or a point)")]
    [InlineData("Normal to a curve", "Curve or edge")]
    [InlineData("Midway between two planes", "Plane or flat face")]
    [InlineData("Tangent to a round face", "Round face (hole, boss or fillet)")]
    [Trait("TestId", "SOL-H162")]
    public void MethodMovesThePickTarget(string method, string slot)
    {
        var session = new CadSession();
        var command = Begin(session, () => new ConstructionPlaneCommand());
        Assert.Equal("Plane or flat face", command.ActiveSlot!.Label);
        command.ConfigureInputs(() => SetChoice(command, "Method", method));
        command.OnChanged();
        Assert.Equal(slot, command.ActiveSlot!.Label);
        Assert.True(command.ActiveSlot.IsVisible);
    }

    [AvaloniaFact(DisplayName = "SOL-H163 | Clicking three corners in turn fills the three point slots in order")]
    [Trait("TestId", "SOL-H163")]
    public void ClicksFillTheThreePointsInTurn()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 30, 20);
        var command = Begin(session, () => new ConstructionPlaneCommand());
        command.ConfigureInputs(() => SetChoice(command, "Method", "Through three points"));
        command.OnChanged();
        foreach (var index in new[] { 0, 1, 2 })
        {
            Assert.True(command.Pick(new SelItem(box.Id, SelKind.Vertex, index), false));
        }
        string[] labels = ["First point (a corner or a point)", "Second point (a corner or a point)", "Third point (a corner or a point)"];
        Assert.All(labels, label => Assert.Single(command.Slots.Single(s => s.Label == label).Items));
        Assert.Equal(3, labels.Select(label => command.Slots.Single(s => s.Label == label).Items[0].Index).Distinct().Count());
    }
}
