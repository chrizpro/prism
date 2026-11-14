using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Design history saved by an earlier build lists each command's numbers by position. Commands that gained a parameter since (Move with gizmo, Rib extends, Scale type)
/// must still read those old lists correctly, or an edited earlier step rebuilds the later ones with the wrong values.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/solid-details/README.md">Solid details.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SavedHistoryUpgradeTests
{
    static double Number(Command command, string label) => command.Params.OfType<NumberParam>().Single(p => p.Label == label).Value;

    [AvaloniaFact(DisplayName = "SOL-H112 | A primitive saved before Move with gizmo keeps its Join operation and position")]
    [Trait("TestId", "SOL-H112")]
    public void PrimitiveRecipeKeepsOperation()
    {
        var session = new CadSession();
        var box = Begin(session, () => new BoxCommand());
        // the old order: width, depth, height, position X, Y, Z, operation
        box.Restore(new FeatureRecipe(() => new BoxCommand(), "Box", [10.0, 20.0, 30.0, 1.0, 2.0, 3.0, 1], []));
        Assert.Equal(10, Number(box, "Width (X)"));
        Assert.Equal(3, Number(box, "Position Z"));
        Assert.Equal("Join", box.Params.OfType<ChoiceParam>().Single(p => p.Label == "Operation").Options[box.Params.OfType<ChoiceParam>().Single(p => p.Label == "Operation").Index]);
        Assert.False(box.Params.OfType<BoolParam>().Single(p => p.Label == "Move with gizmo").Value);
    }

    [AvaloniaFact(DisplayName = "SOL-H113 | A rib saved before Rib extends replays as a fixed-height rib with its old numbers")]
    [Trait("TestId", "SOL-H113")]
    public void RibRecipeKeepsFixedHeight()
    {
        var session = new CadSession();
        var rib = Begin(session, () => new RibCommand());
        // the old order: thickness, height, height direction, thickness placement, reverse
        rib.Restore(new FeatureRecipe(() => new RibCommand(), "Rib", [3.0, 15.0, 1, 2, true], []));
        Assert.Equal(3, Number(rib, "Thickness"));
        Assert.Equal(15, Number(rib, "Height"));
        var extent = rib.Params.OfType<ChoiceParam>().Single(p => p.Label == "Rib extends");
        Assert.Equal("Fixed height", extent.Options[extent.Index]);
        Assert.True(rib.Params.OfType<BoolParam>().Single(p => p.Label == "Reverse height direction").Value);
    }

    [AvaloniaFact(DisplayName = "SOL-H114 | A scale saved before Scale type replays as a uniform scale with its old factor and pivot")]
    [Trait("TestId", "SOL-H114")]
    public void ScaleRecipeKeepsUniformFactor()
    {
        var session = new CadSession();
        var scale = Begin(session, () => new TransformCommand(TransformCommand.Kind.Scale));
        // the old order: factor, pivot, create copy
        scale.Restore(new FeatureRecipe(() => new TransformCommand(TransformCommand.Kind.Scale), "Scale", [2.0, 1, true], []));
        var type = scale.Params.OfType<ChoiceParam>().Single(p => p.Label == "Scale type");
        Assert.Equal("Uniform", type.Options[type.Index]);
        Assert.Equal(2, Number(scale, "Factor"));
        var pivot = scale.Params.OfType<ChoiceParam>().Single(p => p.Label == "Pivot");
        Assert.Equal("World origin", pivot.Options[pivot.Index]);
        Assert.True(scale.Params.OfType<BoolParam>().Single(p => p.Label == "Create copy").Value);
    }

    [AvaloniaFact(DisplayName = "SOL-H115 | A mesh repair saved before Repair scope keeps its checkboxes in place")]
    [Trait("TestId", "SOL-H115")]
    public void MeshRepairRecipeKeepsCheckboxes()
    {
        var session = new CadSession();
        var repair = Begin(session, () => new MeshRepairCommand());
        repair.Params.OfType<BoolParam>().Single(p => p.Label == "Merge duplicate vertices").Value = false;
        var saved = repair.Capture()!;
        // a step saved before the scope choice existed: the same list without its first value
        var old = saved with { Values = saved.Values.Skip(1).ToList() };
        var again = Begin(new CadSession(), () => new MeshRepairCommand());
        again.Restore(old);
        Assert.False(again.Params.OfType<BoolParam>().Single(p => p.Label == "Merge duplicate vertices").Value);
        var scope = again.Params.OfType<ChoiceParam>().Single(p => p.Label == "Repair scope");
        Assert.Equal("Entire mesh", scope.Options[scope.Index]);
    }

    [AvaloniaFact(DisplayName = "SOL-H117 | A joint saved before Placement keeps its motion limits")]
    [Trait("TestId", "SOL-H117")]
    public void JointRecipeKeepsLimits()
    {
        var session = new CadSession();
        var joint = Begin(session, () => new JointCommand(Prism.Core.Assembly.JointType.Revolute));
        joint.Params.OfType<BoolParam>().Single(p => p.Label == "Limit the motion").Value = true;
        SetNumber(joint, "Minimum angle", -30);
        SetNumber(joint, "Maximum angle", 75);
        var saved = joint.Capture()!;
        int placement = joint.Params.IndexOf(joint.Params.OfType<ChoiceParam>().Single(p => p.Label == "Placement"));
        // a step saved before the placement choice existed: the same list without that value
        var values = saved.Values.ToList(); values.RemoveAt(placement);
        var again = Begin(new CadSession(), () => new JointCommand(Prism.Core.Assembly.JointType.Revolute));
        again.Restore(saved with { Values = values });
        Assert.True(again.Params.OfType<BoolParam>().Single(p => p.Label == "Limit the motion").Value);
        Assert.Equal(-30, Number(again, "Minimum angle"));
        Assert.Equal(75, Number(again, "Maximum angle"));
        var choice = again.Params.OfType<ChoiceParam>().Single(p => p.Label == "Placement");
        Assert.Equal("Snap to picks", choice.Options[choice.Index]);
    }
}
