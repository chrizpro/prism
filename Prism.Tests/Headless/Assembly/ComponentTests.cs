using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Assembly;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/assembly/README.md">Component creation and grounding.</seealso>
[Trait("Feature", "Assembly")]
[Trait("Layer", "Headless")]
public class ComponentTests
{
    [AvaloniaFact(DisplayName = "ASM-H001 | Make Part grounds the first component and roundtrips both components")]
    [Trait("TestId", "ASM-H001")]
    public void ComponentsRoundtripWithGroundingAndGeometry()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 2, 3, 4);
        var second = CreateBoxBody(session, new(10, 0, 0), 2, 3, 4);
        var fixedPart = session.MakePart(first.Id, checkpoint: true).Component!;
        var freePart = session.MakePart(second.Id, checkpoint: true).Component!;
        Assert.True(fixedPart.Grounded);
        Assert.False(freePart.Grounded);
        Assert.Equal(fixedPart.Key, session.MakePart(first.Id).Component!.Key);
        var file = Path.Combine(Path.GetTempPath(), $"prism-assembly-{Guid.NewGuid():N}.prism");
        try
        {
            session.Save(file);
            var reopened = new CadSession();
            reopened.Open(file);
            Assert.Equal(2, reopened.Components.Count());
            Assert.True(reopened.ComponentByKey(fixedPart.Key)!.Component!.Grounded);
            var restored = reopened.ComponentByKey(freePart.Key)!;
            Assert.False(restored.Component!.Grounded);
            AssertValidSolid(restored.Shape);
            AssertBounds(restored.Shape, new(10, 0, 0), new(12, 3, 4));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaTheory(DisplayName = "ASM-H002 | Joint creation aligns parts and respects the grounded parent")]
    [InlineData(JointType.Rigid, 0)]
    [InlineData(JointType.Slider, 3)]
    [Trait("TestId", "ASM-H002")]
    public void JointMovesChildAndKeepsParentFixed(JointType type, int travel)
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 2, 3, 4);
        var second = CreateBoxBody(session, new(10, 0, 0), 2, 3, 4);
        session.StartCommand(() => new JointCommand(type));
        var command = Assert.IsType<JointCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(Face(first, Vec3.UnitZ)));
            Assert.True(command.Slots[1].TryAdd(Face(second, -Vec3.UnitZ)));
        });
        session.CommitCommand();
        var joint = Assert.Single(session.JointObjects).joint;
        Assert.Equal(type, joint.Type);
        Assert.True(session.Find(first.Id)!.Component!.Grounded);
        if (travel != 0)
            session.SetJointValues(new Dictionary<string, (double v1, double v2)> { [joint.Key] = (travel, 0) });
        AssertBounds(session.Find(first.Id)!.Shape, Vec3.Zero, new(2, 3, 4));
        AssertBounds(session.Find(second.Id)!.Shape, new(0, 0, 4 + travel), new(2, 3, 8 + travel));
    }

    [AvaloniaFact(DisplayName = "ASM-H003 | Assemble exposes Joint and Rigid As Positioned preserves both component poses")]
    [Trait("TestId", "ASM-H003")]
    public void AsPositionedRigidJointPreservesPose()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var assemble = vm.Tabs.Single(t => t.Title == "Assemble");
        Assert.Equal(new[] { "Select", "Add Parts", "Assemble", "Manage Parts", "Review", "Share" }, assemble.Groups.Select(g => g.Title));
        var tools = assemble.Groups.Single(g => g.Title == "Assemble").Items.OfType<RibbonButton>().ToArray();
        Assert.Equal(new[] { "Ground", "Move\nComponent", "Mate", "Joint" }, tools.Select(b => b.Label));
        var joints = vm.Tabs.Single(t => t.Title == "Joints").Groups.Single(g => g.Title == "Joint");
        Assert.Equal(joints.Items.OfType<RibbonButton>().Select(b => b.Label), tools[3].Menu!.Select(b => b.Label));
        Assert.Equal(6, tools[3].Menu!.Count);

        var first = session.MakePart(CreateBoxBody(session, Vec3.Zero, 2, 3, 4).Id);
        var second = session.MakePart(CreateBoxBody(session, new(10, 3, 5), 2, 3, 4).Id);
        vm.RefreshRibbon();
        tools[3].Menu![0].Invoke();
        var command = Assert.IsType<JointCommand>(session.ActiveCommand);
        command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Placement").Index = 1;
        Assert.All(command.Slots, slot => Assert.Equal(SelFilter.Solid, slot.PickFilter));
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(SelItem.Of(second)));
            Assert.True(command.Slots[1].TryAdd(SelItem.Of(first)));
        });
        command.OnChanged();
        Assert.All(command.Results.Where(r => r.Label.EndsWith("part")), r => Assert.EndsWith("stays in place", r.Value));
        Assert.Equal(2, command.Results.Count(r => r.Label.EndsWith("part")));
        session.CommitCommand();
        Assert.Single(session.JointObjects);
        AssertBounds(session.Find(first.Id)!.Shape, Vec3.Zero, new(2, 3, 4));
        AssertBounds(session.Find(second.Id)!.Shape, new(10, 3, 5), new(12, 6, 9));
        var file = Path.Combine(Path.GetTempPath(), $"prism-assembly-{Guid.NewGuid():N}.prism_asm");
        try
        {
            session.Save(file);
            var reopened = new CadSession();
            reopened.Open(file);
            Assert.Equal(JointType.Rigid, Assert.Single(reopened.JointObjects).joint.Type);
            AssertBounds(reopened.Find(second.Id)!.Shape, new(10, 3, 5), new(12, 6, 9));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact(DisplayName = "ASM-H004 | Slider preview identifies the actual moving part and the solved slide axis")]
    [Trait("TestId", "ASM-H004")]
    public void SliderPreviewShowsReversePlacementAndAxis()
    {
        var session = new CadSession();
        var grounded = session.MakePart(CreateBoxBody(session, Vec3.Zero, 2, 3, 4).Id);
        var free = session.MakePart(CreateBoxBody(session, new(10, 0, 0), 2, 3, 4).Id);
        session.StartCommand(() => new JointCommand(JointType.Slider));
        var command = Assert.IsType<JointCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            command.Slots[0].TryAdd(Face(free, Vec3.UnitX));
            command.Slots[1].TryAdd(Face(grounded, -Vec3.UnitX));
        });
        command.OnChanged();
        Assert.Equal(free.Name + " · moves", command.Results.Single(r => r.Label == "First part").Value);
        Assert.Equal(grounded.Name + " · stays in place", command.Results.Single(r => r.Label == "Second part").Value);
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        var label = Assert.Single(overlay.Labels, label => label.Text == "Slide axis");
        var axis = Assert.Single(overlay.Lines, line => line.B == label.P);
        Assert.True(Math.Abs(Vec3.Dot((axis.B - axis.A).Normalized(), Vec3.UnitX)) > 0.999);
    }

    static SelItem Face(SceneObject body, Vec3 normal)
    {
        int index = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Vec3.Dot(Occ.FaceInfoOf(face).Normal, normal) > 0.99;
        });
        return new SelItem(body.Id, SelKind.Face, index);
    }
}
