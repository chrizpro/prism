using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct feature and pattern actions.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class FeatureActionTests
{
    [AvaloniaTheory(DisplayName = "DIR-H004 | Feature actions change drilled volume by the expected amount")]
    [InlineData("Delete", 0)]
    [InlineData("Resize", 160)]
    [InlineData("Copy", 180)]
    [InlineData("Move", 90)]
    [Trait("TestId", "DIR-H004")]
    public void EditHoleActionChangesExpectedVolume(string action, int removedOverPi)
    {
        var (session, plate) = DrilledPlate(20);
        var command = StartCommand(session, plate, () => new FeatureEditCommand());
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(HoleFace(plate)));
            SetChoice(command, "Action", action);
            if (action == "Resize")
                SetNumber(command, "New diameter (0 = keep)", 8);
            else if (action is "Copy" or "Move")
                SetNumber(command, "Move X", 15);
        });
        using var result = Replacement(command);
        AssertNear(60000 - Math.PI * removedOverPi, result.Props().Volume, "Edited plate volume (mm³)");
        if (action is "Copy" or "Move")
        {
            var edited = session.AddObject(result, "Result", ObjKind.Solid);
            var centers = FeatureFinder.Features(edited).Where(feature => feature.Kind == FeatureKind.Hole)
                .Select(feature => Math.Round(feature.Centre.X, 3)).Order().ToArray();
            Assert.Equal(action == "Copy" ? new double[] { 20, 35 } : [35], centers);
        }
    }

    [AvaloniaTheory(DisplayName = "DIR-H005 | Edit Pattern changes a linear hole pattern")]
    [InlineData("Spacing", 30, 4)]
    [InlineData("Number of instances", 3, 3)]
    [InlineData("Remove instance (0 = none)", 3, 3)]
    [Trait("TestId", "DIR-H005")]
    public void EditPatternUpdatesHoleCountAndPositions(string option, int value, int count)
    {
        var (session, plate) = DrilledPlate(20, 40, 60, 80);
        var command = StartCommand(session, plate, () => new EditPatternCommand());
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(HoleFace(plate)));
            SetNumber(command, option, value);
        });
        using var result = Replacement(command);
        AssertNear(60000 - Math.PI * 90 * count, result.Props().Volume, "Pattern volume (mm³)");
        var body = session.AddObject(result, "Result", ObjKind.Solid);
        var centers = FeatureFinder.Features(body).Where(feature => feature.Kind == FeatureKind.Hole)
            .Select(feature => Math.Round(feature.Centre.X, 3)).Order().ToArray();
        double[] expected = option switch
        {
            "Spacing" => [20, 50, 80, 110],
            "Number of instances" => [20, 40, 60],
            _ => [20, 40, 80],
        };
        Assert.Equal(expected, centers);
    }

    static (CadSession Session, SceneObject Plate) DrilledPlate(params int[] positions)
    {
        var session = new CadSession();
        var blank = CreateBoxBody(session, Vec3.Zero, 120, 50, 10);
        Shape shape = blank.Shape;
        foreach (int x in positions)
        {
            using var cylinder = Occ.Cylinder(3, 12);
            using var tool = Occ.Translate(cylinder, new Vec3(x - 3, 22, -1) - cylinder.Props().Min);
            var cut = Occ.Cut(shape, tool);
            if (shape != blank.Shape)
                shape.Dispose();
            shape = cut;
        }
        return (session, session.AddObject(shape, "Drilled plate", ObjKind.Solid));
    }

    static SelItem HoleFace(SceneObject plate)
    {
        var hole = FeatureFinder.Features(plate).Where(feature => feature.Kind == FeatureKind.Hole)
            .OrderBy(feature => feature.Centre.X).First();
        return new SelItem(plate.Id, SelKind.Face, hole.Faces[0]);
    }

    static Shape Replacement(Command command)
    {
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        return shape;
    }
}
