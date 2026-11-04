using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
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
