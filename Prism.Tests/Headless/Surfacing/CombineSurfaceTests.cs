using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>
/// Subtract with a surface as the tool: two extruded rectangles (open tubes, walls only) overlap; one cuts the other. The result is the target
/// with the part inside the tool removed, and by default the cut is filled with the tool's walls so it is closed (owner request 2026-11-14).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/combine/README.md">Combine.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class CombineSurfaceTests
{
    // target B: 20 x 20 at (10, 10); tool A: 20 x 20 at the origin; both 10 tall. A covers the corner of B.
    static (SceneObject target, SceneObject tool) Tubes(CadSession s)
    {
        SceneObject Tube(string name, double x, double y)
        {
            using var wire = Occ.Polyline([new(x, y, 0), new(x + 20, y, 0), new(x + 20, y + 20, 0), new(x, y + 20, 0)], true);
            return s.AddObject(Occ.Extrude(wire, new Vec3(0, 0, 10)), name, ObjKind.Surface);
        }
        return (Tube("B", 10, 10), Tube("A", 0, 0));
    }

    static Shape Cut(MainViewModel vm, SceneObject target, SceneObject tool, bool fill)
    {
        var command = new BooleanCommand(BooleanCommand.Kind.Subtract);
        vm.Session.StartCommand(command);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Body to cut from", target);
            SetSelection(command, "Tool bodies", tool);
            command.Params.OfType<BoolParam>().Single(param => param.Label.StartsWith("Fill the cut")).Value = fill;
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        return output.Replace.Single().Item2;
    }

    [AvaloniaFact(DisplayName = "SFC-H019 | A surface tube cut by another keeps the walls outside it, with the cut left open when fill is off")]
    [Trait("TestId", "SFC-H019")]
    public void CutLeavesGap()
    {
        var vm = new MainViewModel();
        var (b, a) = Tubes(vm.Session);
        AssertNear(800, b.Shape.Props().Area, "Target wall area (mm²)");
        using var result = Cut(vm, b, a, fill: false);
        // B's perimeter is 80; 10 mm of its left wall and 10 mm of its bottom wall lie inside A: 800 - 200
        AssertNear(600, result.Props().Area, "Walls left outside the tool (mm²)");
        Assert.Equal(0, result.Count(SubKind.Solid));
    }

    [AvaloniaFact(DisplayName = "SFC-H020 | Filling the cut adds the tool's walls inside the target, closing the notch")]
    [Trait("TestId", "SFC-H020")]
    public void CutFilledByDefault()
    {
        var vm = new MainViewModel();
        var (b, a) = Tubes(vm.Session);
        using var result = Cut(vm, b, a, fill: true);
        // the L-shaped tube: A's top wall and right wall inside B add 10 + 10 mm of wall: 600 + 200 = 800 (the notched outline has the same perimeter as B)
        AssertNear(800, result.Props().Area, "Notched tube wall area (mm²)");
        Assert.True(result.IsValid);
        Assert.Empty(Occ.FreeEdges(result).Where(i => AtMidHeight(result, i)));
    }

    [AvaloniaFact(DisplayName = "SFC-H021 | The fill option is on by default and only offered when a surface is the tool")]
    [Trait("TestId", "SFC-H021")]
    public void FillOptionDefault()
    {
        var vm = new MainViewModel();
        var (b, a) = Tubes(vm.Session);
        var command = new BooleanCommand(BooleanCommand.Kind.Subtract);
        vm.Session.StartCommand(command);
        var fill = command.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Fill the cut"));
        Assert.True(fill.Value);
        Assert.False(fill.IsVisible);
        command.ConfigureInputs(() => SetSelection(command, "Tool bodies", a));
        command.OnChanged();            // picking in the viewport does this, and with it re-evaluates which options are shown
        Assert.True(fill.IsVisible);
        vm.Session.CancelCommand();
    }

    static Shape Combine(MainViewModel vm, BooleanCommand.Kind kind, SceneObject target, SceneObject tool, bool? option = null)
    {
        var command = new BooleanCommand(kind);
        vm.Session.StartCommand(command);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Base body", target);
            SetSelection(command, "Other bodies", tool);
            if (option is { } value) command.Params.OfType<BoolParam>().Single(param => param.Label.StartsWith("Keep the tools")).Value = value;
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        return output.Replace.Single().Item2;
    }

    [AvaloniaFact(DisplayName = "SFC-H022 | Union of two surface tubes keeps only the outer skin: the walls inside each other are gone")]
    [Trait("TestId", "SFC-H022")]
    public void UnionKeepsOuterSkin()
    {
        var vm = new MainViewModel();
        var (b, a) = Tubes(vm.Session);
        using var result = Combine(vm, BooleanCommand.Kind.Union, b, a);
        // each tube is 800 mm²; 200 mm² of each lies inside the other: 2 x 600
        AssertNear(1200, result.Props().Area, "Merged skin wall area (mm²)");
        Assert.Equal(0, result.Count(SubKind.Solid));
    }

    [AvaloniaFact(DisplayName = "SFC-H023 | Intersect of two surface tubes keeps the walls inside each other, or only the base's when asked")]
    [Trait("TestId", "SFC-H023")]
    public void IntersectKeepsSharedWalls()
    {
        var vm = new MainViewModel();
        var (b, a) = Tubes(vm.Session);
        using var both = Combine(vm, BooleanCommand.Kind.Intersect, b, a, option: true);
        AssertNear(400, both.Props().Area, "Walls of both tubes inside each other (mm²)");
        vm.Session.CancelCommand();
        var (b2, a2) = Tubes(vm.Session);
        using var baseOnly = Combine(vm, BooleanCommand.Kind.Intersect, b2, a2, option: false);
        AssertNear(200, baseOnly.Props().Area, "Base tube walls inside the tool (mm²)");
    }

    /// <summary>An edge that is open and lies between the top and bottom rims would mean the cut was left open.</summary>
    static bool AtMidHeight(Shape shape, int edge)
    {
        var info = Occ.EdgeInfoOf(shape.Sub(SubKind.Edge, edge));
        return Math.Abs(info.Start.Z - info.End.Z) > 1e-6;
    }
}
