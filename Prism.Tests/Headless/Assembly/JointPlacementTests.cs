using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Assembly;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/assembly/README.md#joint-placement">Joint placement.</seealso>
[Trait("Feature", "Assembly")]
[Trait("Layer", "Headless")]
public class JointPlacementTests
{
    const string First = "On the first part (face, circle edge or round face)", Second = "On the part that moves";

    static SelItem RoundFace(SceneObject body) =>
        new(body.Id, SelKind.Face, Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));

    [AvaloniaTheory(DisplayName = "JNT-H001 | A pin snaps level with a hole's own face, wherever the cutter that made the hole began")]
    [InlineData(0.0)]
    [InlineData(-500.0)]
    [InlineData(300.0)]
    [Trait("TestId", "JNT-H001")]
    public void PinSitsAtTheFaceNotTheCuttersBase(double cutterBase)
    {
        var session = new CadSession();
        using var plate = BoxAt(new(-30, -20, 0), 60, 40, 10);
        // a plate 10 mm thick drilled by a 1 m cutter that starts at cutterBase (a through-hole made with a long tool, or a hole from an imported part)
        using var cutter = Occ.Translate(Occ.Cylinder(5, 1000), new Vec3(0, 0, cutterBase - (cutterBase > 0 ? 995 : 0)));
        var drilled = session.AddObject(Occ.Cut(plate, cutter), "Plate", ObjKind.Solid);
        var pin = session.AddObject(Occ.Translate(Occ.Cylinder(5, 30), new Vec3(70, 0, 0)), "Pin", ObjKind.Solid);

        var joint = Begin(session, () => new JointCommand(JointType.Revolute));
        Pick(joint, First, RoundFace(drilled));
        Pick(joint, Second, RoundFace(pin));
        BuildOutput(joint); session.CommitCommand();

        var placed = session.Find(pin.Id)!.Shape.Props();
        // the middle of the pin's round face meets the middle of the hole's wall: the pin is centred on the plate's mid-height, 5 mm
        AssertClose(0, placed.Center.X, 1e-6, "Pin centre X (mm)");
        AssertClose(0, placed.Center.Y, 1e-6, "Pin centre Y (mm)");
        AssertClose(5, placed.Center.Z, 1e-6, "Pin centre Z (mm)");
    }

    [AvaloniaFact(DisplayName = "JNT-H002 | A joint warns when the second part is moved into the first, and not when it sits against it")]
    [Trait("TestId", "JNT-H002")]
    public void WarnsWhenThePartIsBuried()
    {
        string Overlap(Vec3 secondFace)
        {
            var session = new CadSession();
            var fixedBody = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
            var moving = CreateBoxBody(session, new(100, 80, 40), 10, 10, 10);
            var joint = Begin(session, () => new JointCommand(JointType.Revolute));
            Pick(joint, First, FaceFacing(fixedBody, Vec3.UnitZ));
            Pick(joint, Second, FaceFacing(moving, secondFace));
            BuildOutput(joint);
            return joint.Results.FirstOrDefault(r => r.Label == "Overlap")?.Value ?? "";
        }
        Assert.Equal("", Overlap(-Vec3.UnitZ));                       // the bottom of the second part meets the top of the first: it sits on it
        Assert.Contains("overlaps", Overlap(Vec3.UnitZ));             // the top of the second part is put on the top of the first: it goes inside
    }

    [AvaloniaFact(DisplayName = "JNT-H003 | A joint warns when the part picked to move is the grounded one, so the other part moves")]
    [Trait("TestId", "JNT-H003")]
    public void WarnsWhenTheMovingPartIsGrounded()
    {
        var session = new CadSession();
        var stays = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var grounded = CreateBoxBody(session, new(100, 0, 0), 20, 20, 10);
        session.MakePart(grounded.Id, checkpoint: true);          // made first, so it is the grounded one
        session.MakePart(stays.Id, checkpoint: true);
        var joint = Begin(session, () => new JointCommand(JointType.Revolute));
        Pick(joint, First, FaceFacing(session.Find(stays.Id)!, Vec3.UnitZ));
        Pick(joint, Second, FaceFacing(session.Find(grounded.Id)!, -Vec3.UnitZ));
        BuildOutput(joint);
        Assert.Contains(joint.Results, r => r.Label == "Grounded");
        Assert.Contains(joint.Results, r => r.Label == "First part" && r.Value.EndsWith("moves"));
    }
}
