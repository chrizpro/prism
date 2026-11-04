using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class FaceEditTests
{
    [AvaloniaTheory(DisplayName = "DIR-H001 | Moving a top face to a target plane preserves one solid at the target height")]
    [InlineData("Replace Face", 14)]
    [InlineData("Match Face", 14)]
    [InlineData("Extend / Trim Face", 14)]
    [InlineData("Extend / Trim Face", 6)]
    [Trait("TestId", "DIR-H001")]
    public void FaceEditReachesTargetPlane(string tool, int height)
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var body = CreateBoxBody(session, Vec3.Zero, 10, 8, 10);
        var reference = CreateBoxBody(session, new Vec3(30, 0, height), 4, 4, 1);
        var command = Start(vm, tool);
        command.ConfigureInputs(() =>
        {
            var normal = tool == "Extend / Trim Face" ? Vec3.UnitX : Vec3.UnitZ;
            Assert.True(command.Slots[0].TryAdd(Face(body, normal)));
            Assert.True(command.Slots[1].TryAdd(Face(reference, -Vec3.UnitZ)));
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var replacement = Assert.Single(output.Replace);
        Assert.Equal(body.Id, replacement.Old.Id);
        using var shape = replacement.New;
        AssertValidSolid(shape);
        AssertBounds(shape, Vec3.Zero, new Vec3(10, 8, height));
        AssertNear(80 * height, shape.Props().Volume, "Edited volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "DIR-H002 | Split Face preserves volume and Merge Faces removes the split")]
    [Trait("TestId", "DIR-H002")]
    public void SplitAndMergePreserveSolid()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var body = CreateBoxBody(session, new Vec3(-5, -4, 0), 10, 8, 10);
        var splitter = session.AddObject(null, "Split plane", ObjKind.Plane, plane: Plane3.YZ);
        var split = Start(vm, "Split Face");
        split.ConfigureInputs(() =>
        {
            Assert.True(split.Slots[0].TryAdd(Face(body, Vec3.UnitZ)));
            Assert.True(split.Slots[1].TryAdd(SelItem.Of(splitter)));
        });
        var output = split.TryBuild();
        Assert.True(output != null, split.Error);
        using var divided = Assert.Single(output.Replace).New;
        AssertValidSolid(divided);
        Assert.Equal(7, divided.Count(SubKind.Face));
        AssertNear(800, divided.Props().Volume, "Split volume (mm³)");

        session.CancelCommand();
        var splitBody = session.AddObject(divided, "Split", ObjKind.Solid);
        var merge = Start(vm, "Merge Faces");
        var topFaces = Faces(splitBody, Vec3.UnitZ).ToList();
        Assert.Equal(2, topFaces.Count);
        merge.ConfigureInputs(() =>
        {
            foreach (var top in topFaces)
                Assert.True(merge.Slots[0].TryAdd(top));
        });
        var merged = merge.TryBuild();
        Assert.True(merged != null, merge.Error);
        using var shape = Assert.Single(merged.Replace).New;
        AssertValidSolid(shape);
        Assert.Equal(6, shape.Count(SubKind.Face));
        AssertNear(800, shape.Props().Volume, "Merged volume (mm³)");
    }

    [AvaloniaTheory(DisplayName = "DIR-H003 | The cap mode closes a planar missing face into a solid")]
    [InlineData("Automatic (flat when the loop is flat)")]
    [InlineData("Flat")]
    [InlineData("Smooth patch")]
    [Trait("TestId", "DIR-H003")]
    public void CapModesClosePlanarOpening(string mode)
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        using var box = Occ.Box(10, 8, 10);
        var faces = new List<Shape>();
        try
        {
            for (int index = 0; index < box.Count(SubKind.Face); index++)
            {
                var face = box.Sub(SubKind.Face, index);
                if (Occ.FaceInfoOf(face).Normal.Z > 0.99)
                    face.Dispose();
                else
                    faces.Add(face);
            }
            var open = session.AddObject(Occ.Sew(faces, 0.01, false), "Open box", ObjKind.Surface);
            int[] edges = Occ.FreeEdges(open.Shape);
            Assert.Equal(4, edges.Length);
            var command = Start(vm, "Fill / Cap");
            command.ConfigureInputs(() =>
            {
                Assert.True(command.Slots[0].TryAdd(new SelItem(open.Id, SelKind.Edge, edges[0])));
                SetChoice(command, "Cap", mode);
            });
            var output = command.TryBuild();
            Assert.True(output != null, command.Error);
            using var shape = Assert.Single(output.Replace).New;
            AssertValidSolid(shape);
            AssertNear(800, shape.Props().Volume, "Capped solid volume (mm³)");
        }
        finally
        {
            foreach (var face in faces)
                face.Dispose();
        }
    }

    static Command Start(MainViewModel vm, string label)
    {
        vm.EnterDirectEdit();
        var tab = vm.Tabs.Single(item => item.Title == "Direct Modeling");
        var button = tab.Buttons.SelectMany(item => (IEnumerable<RibbonButton>?)item.Menu ?? [item])
            .Distinct().Single(item => item.Label.Replace('\n', ' ') == label);
        button.Invoke();
        Assert.NotNull(vm.Session.ActiveCommand);
        Assert.Equal(label, vm.Session.ActiveCommand.Title);
        return vm.Session.ActiveCommand;
    }

    static SelItem Face(SceneObject body, Vec3 normal) => Faces(body, normal).Single();

    static IEnumerable<SelItem> Faces(SceneObject body, Vec3 normal)
    {
        for (int index = 0; index < body.Shape.Count(SubKind.Face); index++)
        {
            using var face = body.Shape.Sub(SubKind.Face, index);
            if (Vec3.Dot(Occ.FaceInfoOf(face).Normal, normal) > 0.99)
                yield return new SelItem(body.Id, SelKind.Face, index);
        }
    }
}
