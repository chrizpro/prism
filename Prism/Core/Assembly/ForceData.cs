using System;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Core.Assembly;

/// <summary>
/// A spring, damper and actuator between two points. PointA is in the local coordinates of component A. The other end is either on component B
/// (PointB in its local coordinates) or fixed in space (CompB null, PointB in world coordinates). Lengths are mm, stiffness N/mm, damping N·s/mm, push N.
/// </summary>
public sealed record SpringInfo(string Key, string Name, string CompA, double[] PointA, string? CompB, double[] PointB, double Stiffness, double Damping, double RestLength, double Push) : AssemblyData
{
    public MotionForce ToForce() => new SpringForce(Name, CompA, V(PointA), CompB, V(PointB), Stiffness, Damping, RestLength, Push);
    internal static Vec3 V(double[] a) => new(a[0], a[1], a[2]);
}

/// <summary>A force (N) or a torque (N·m) on a component. Direction is fixed in the world, or turns with the part when BodyFixed. Shape 0 = constant, 1 = pulse, 2 = sine.</summary>
public sealed record LoadInfo(string Key, string Name, bool IsTorque, string Comp, double[] Point, double[] Direction, bool BodyFixed, double Magnitude, int Shape, double Start, double Length, double Frequency) : AssemblyData
{
    public LoadProfile Profile => new((LoadShape)Math.Clamp(Shape, 0, 2), Start, Length, Frequency);
    public MotionForce ToForce() => IsTorque
        ? new BodyTorque(Name, Comp, SpringInfo.V(Direction), BodyFixed, Magnitude, Profile)
        : new PointForce(Name, Comp, SpringInfo.V(Point), SpringInfo.V(Direction), BodyFixed, Magnitude, Profile);
}
