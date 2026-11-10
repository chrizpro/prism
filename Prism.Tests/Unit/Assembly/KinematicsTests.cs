using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;

namespace Prism.UnitTests.Assembly;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/assembly/README.md">Assembly kinematics.</seealso>
[Trait("Feature", "Assembly")]
[Trait("Layer", "Unit")]
public class KinematicsTests
{
    [Theory(DisplayName = "ASM-U001 | Joint types move only along their configured axes")]
    [InlineData(JointType.Rigid, 0, 0, 0, 1, 0)]
    [InlineData(JointType.Revolute, 0, 0, 0, 0, 1)]
    [InlineData(JointType.Slider, 0, 0, 90, 1, 0)]
    [InlineData(JointType.Cylindrical, 0, 0, 5, 0, 1)]
    [InlineData(JointType.Ball, 0, 0, 0, 0, 1)]
    [InlineData(JointType.Planar, 90, 5, 0, 1, 0)]
    [Trait("TestId", "ASM-U001")]
    public void JointTypesResolvePose(JointType type, int x, int y, int z, int axisX, int axisY)
    {
        var motion = Joint(type, 90, 5).Motion();
        Near(new(x, y, z), motion.T);
        Near(new(axisX, axisY, 0), motion.AxisX);
        if (type == JointType.Ball)
            Near(new(Math.Sin(Math.PI / 36), 0, Math.Cos(Math.PI / 36)), motion.AxisZ);
    }

    [Theory(DisplayName = "ASM-U002 | A slider clamps travel and can solve from either grounded end")]
    [InlineData(true, 17)]
    [InlineData(false, 3)]
    [Trait("TestId", "ASM-U002")]
    public void GroundedEndStaysFixed(bool parentGrounded, int movingZ)
    {
        var poses = new Dictionary<string, Xform>
        {
            ["parent"] = Xform.Translation(new(0, 0, 10)),
            ["child"] = Xform.Translation(new(0, 0, 10)),
            ["loose"] = Xform.Translation(new(1, 2, 3)),
        };
        var joint = Joint(JointType.Slider, 20, 0) with { HasLimits = true, Min = -4, Max = 7 };
        string fixedKey = parentGrounded ? "parent" : "child";
        string movingKey = parentGrounded ? "child" : "parent";
        var solved = AssemblyKinematics.Solve(poses, new HashSet<string> { fixedKey }, [joint], null, []);
        Near(poses[fixedKey].T, solved[fixedKey].T);
        Near(new(0, 0, movingZ), solved[movingKey].T);
        Near(poses["loose"].T, solved["loose"].T);
        Assert.All(AssemblyKinematics.ClosureError(solved, joint), residual => Assert.InRange(residual, -1e-10, 1e-10));
    }

    static JointInfo Joint(JointType type, double first, double second) =>
        new("joint", "Fixture joint", type, "parent", "child", JointInfo.Pack(Xform.Identity),
            JointInfo.Pack(Xform.Identity), false, 0, first, second, false, 0, 0, 0, 0);

    [Fact(DisplayName = "ASM-U003 | A rigid loop rejects a half-turn mismatch even when the pin origins coincide")]
    [Trait("TestId", "ASM-U003")]
    public void RigidLoopChecksOrientationWithoutMovableJoints()
    {
        var poses = new Dictionary<string, Xform> { ["parent"] = Xform.Identity, ["child"] = Xform.Identity };
        var first = Joint(JointType.Rigid, 0, 0);
        var closing = first with { Key = "closure" };
        _ = new MotionEngine(poses, new HashSet<string> { "parent" }, [first, closing], [], [], []);
        var error = Assert.Throws<InvalidOperationException>(() =>
            new MotionEngine(poses, new HashSet<string> { "parent" }, [first, closing with { ZeroAngle = 180 }], [], [], []));
        Assert.Contains("cannot close", error.Message);
    }

    static void Near(Vec3 expected, Vec3 actual) => Assert.InRange(actual.DistanceTo(expected), 0, 1e-10);

    [Theory(DisplayName = "ASM-U004 | Segment timing changes the speed between keys and preserves full turns")]
    [InlineData(AnimTiming.Linear, 450)]
    [InlineData(AnimTiming.EaseIn, 112.5)]
    [InlineData(AnimTiming.EaseOut, 787.5)]
    [InlineData(AnimTiming.EaseBoth, 281.25)]
    [InlineData(AnimTiming.Hold, 0)]
    [Trait("TestId", "ASM-U004")]
    public void SegmentTimingUsesTheOutgoingKey(AnimTiming timing, double expected)
    {
        var track = new AnimTrack("shaft", false, [new(0, 0, timing), new(4, 1800)]);
        Assert.Equal(expected, track.ValueAt(1, true), 8);
        Assert.Equal(1800, track.ValueAt(4, true));
        var legacy = new AnimTrack("shaft", false, [new(0, 0), new(4, 1800)]);
        Assert.Equal(450, legacy.ValueAt(1, false));
        Assert.Equal(281.25, legacy.ValueAt(1, true));
    }
}
