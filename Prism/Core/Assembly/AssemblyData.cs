using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Prism.Kernel;

namespace Prism.Core.Assembly;

public enum JointType { Rigid, Revolute, Slider, Cylindrical, Ball, Planar }

/// <summary>Extra data an assembly object carries: what it is in the assembly, on top of the geometry it shows.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(ComponentInfo), "component")]
[JsonDerivedType(typeof(JointInfo), "joint")]
[JsonDerivedType(typeof(AnimInfo), "animation")]
[JsonDerivedType(typeof(CouplingInfo), "coupling")]
[JsonDerivedType(typeof(SpringInfo), "spring")]
[JsonDerivedType(typeof(LoadInfo), "load")]
[JsonDerivedType(typeof(Prism.Core.Cam.ToolpathInfo), "toolpath")]
[JsonDerivedType(typeof(ImageInfo), "image")]
public abstract record AssemblyData;

/// <summary>A part placed in the assembly. P is its placement (12 numbers: rotation row-major, then translation).</summary>
public sealed record ComponentInfo(string Key, string PartName, string? SourcePath, double[] P, bool Grounded, PartInfo? Info = null) : AssemblyData
{
    /// <summary>The open document this part came from, while it is open. Not saved: a saved assembly relies on SourcePath.</summary>
    [JsonIgnore] public Guid? SourceDoc { get; init; }

    [JsonIgnore] public Xform Placement => Xform.FromArray(P);
    public ComponentInfo WithPlacement(Xform x) => this with { P = x.ToArray() };
}

/// <summary>
/// A connection between two components. FrameA / FrameB are (origin, x axis, z axis) in the local coordinates of the parent and child part;
/// when the joint is solved the two frames coincide (B turned over first when Flip is set), then the motion (turn and/or slide along z) is applied.
/// </summary>
public sealed record JointInfo(string Key, string Name, JointType Type, string ParentKey, string ChildKey, double[] FrameA, double[] FrameB, bool Flip,
    double ZeroAngle, double Value, double Value2, bool HasLimits, double Min, double Max, double Min2, double Max2) : AssemblyData
{
    [JsonIgnore] public Xform A => Frame(FrameA);
    [JsonIgnore] public Xform B => Frame(FrameB);
    static Xform Frame(double[] f) => Xform.FromFrame(new Vec3(f[0], f[1], f[2]), new Vec3(f[3], f[4], f[5]), new Vec3(f[6], f[7], f[8]));
    public static double[] Pack(Xform f) => [f.T.X, f.T.Y, f.T.Z, f.AxisX.X, f.AxisX.Y, f.AxisX.Z, f.AxisZ.X, f.AxisZ.Y, f.AxisZ.Z];

    public bool UsesAngle => Type is JointType.Revolute or JointType.Cylindrical or JointType.Ball;
    public bool UsesDistance => Type is JointType.Slider or JointType.Cylindrical or JointType.Planar;
    /// <summary>Joints with two motion values: cylindrical (turn, slide), ball (spin, tilt) and planar (slide X, slide Y).</summary>
    public bool HasSecond => Type is JointType.Cylindrical or JointType.Ball or JointType.Planar;
    public bool SecondIsAngle => Type == JointType.Ball;
    public string FirstLabel => Type switch { JointType.Slider => "travel", JointType.Ball => "spin", JointType.Planar => "slide X", _ => "angle" };
    public string SecondLabel => Type switch { JointType.Ball => "tilt", JointType.Planar => "slide Y", _ => "travel" };

    /// <summary>The first (angle for turning joints, distance for sliders) and second (distance of a cylindrical joint) value after limits.</summary>
    public (double v1, double v2) Clamped()
    {
        double v1 = Value, v2 = Value2;
        if (HasLimits)
        {
            if (HasSecond) { v1 = Math.Clamp(v1, Min, Max); v2 = Math.Clamp(v2, Min2, Max2); }
            else v1 = Math.Clamp(v1, Min, Max);
        }
        return (v1, v2);
    }

    /// <summary>Where the child's frame sits relative to the parent's frame for the current values.</summary>
    public Xform Motion(double? v1 = null, double? v2 = null)
    {
        var (c1, c2) = Clamped();
        double a = v1 ?? c1, d = v2 ?? c2;
        var m = Xform.RotZ(ZeroAngle * Math.PI / 180);
        switch (Type)
        {
            case JointType.Revolute: m = Xform.RotZ((ZeroAngle + a) * Math.PI / 180); break;
            case JointType.Slider: m = m * Xform.TransZ(a); break;
            case JointType.Cylindrical: m = Xform.RotZ((ZeroAngle + a) * Math.PI / 180) * Xform.TransZ(d); break;
            case JointType.Ball: m = Xform.RotZ((ZeroAngle + a) * Math.PI / 180) * Xform.RotX(d * Math.PI / 180); break;
            case JointType.Planar: m = m * Xform.Translation(new Vec3(a, d, 0)); break;
        }
        return Flip ? m * Xform.RotX180 : m;
    }
}

/// <summary>A keyed animation: for each joint a list of (time, value) keys, plus the exploded-view factor over time.</summary>
public sealed record AnimInfo(double Duration, List<AnimTrack> Tracks, bool Loop, bool Ease = true) : AssemblyData
{
    /// <summary>Joint values at time t for every joint that has keys.</summary>
    public Dictionary<string, (double v1, double v2)> Sample(double t, IReadOnlyDictionary<string, (double v1, double v2)> rest)
    {
        var res = new Dictionary<string, (double v1, double v2)>();
        foreach (var tr in Tracks)
        {
            var cur = res.TryGetValue(tr.JointKey, out var c) ? c : rest.GetValueOrDefault(tr.JointKey);
            double v = tr.ValueAt(t, Ease);
            res[tr.JointKey] = tr.SecondValue ? (cur.v1, v) : (v, cur.v2);
        }
        return res;
    }
}

public sealed record AnimTrack(string JointKey, bool SecondValue, List<AnimKey> Keys)
{
    public double ValueAt(double t, bool ease)
    {
        if (Keys.Count == 0) return 0;
        var k = Keys.OrderBy(x => x.T).ToList();
        if (t <= k[0].T) return k[0].V;
        if (t >= k[^1].T) return k[^1].V;
        int i = k.FindLastIndex(x => x.T <= t);
        double u = (t - k[i].T) / Math.Max(k[i + 1].T - k[i].T, 1e-9);
        u = k[i].Timing switch
        {
            AnimTiming.Linear => u,
            AnimTiming.EaseIn => u * u,
            AnimTiming.EaseOut => 1 - (1 - u) * (1 - u),
            AnimTiming.EaseBoth => u * u * (3 - 2 * u),
            AnimTiming.Hold => 0,
            AnimTiming.Custom => (k[i].Curve ?? new AnimCurve()).ValueAt(u),
            _ => ease ? u * u * (3 - 2 * u) : u,
        };
        return k[i].V + (k[i + 1].V - k[i].V) * u;
    }
}

public enum AnimTiming { ClipDefault, Linear, EaseIn, EaseOut, EaseBoth, Hold, Custom }

public sealed record AnimKey(double T, double V, AnimTiming Timing = AnimTiming.ClipDefault, AnimCurve? Curve = null);

/// <summary>A cubic timing curve from (0,0) to (1,1). X is elapsed time and Y is progress.</summary>
public sealed record AnimCurve(double X1 = .25, double Y1 = .1, double X2 = .25, double Y2 = 1)
{
    public double ValueAt(double time)
    {
        if (!double.IsFinite(X1) || !double.IsFinite(Y1) || !double.IsFinite(X2) || !double.IsFinite(Y2)) return time;
        return Bezier(ParameterAt(time), Y1, Y2);
    }

    static double Bezier(double t, double a, double b)
        => 3 * (1 - t) * (1 - t) * t * Math.Clamp(a, 0, 1) + 3 * (1 - t) * t * t * Math.Clamp(b, 0, 1) + t * t * t;

    double ParameterAt(double time)
    {
        double low = 0, high = 1;
        for (int i = 0; i < 40; i++)
        {
            double mid = (low + high) * .5;
            if (Bezier(mid, X1, X2) < time) low = mid;
            else high = mid;
        }
        return (low + high) * .5;
    }

    public (double Value, double Speed, double Acceleration) KinematicsAt(double time)
    {
        double t = ParameterAt(time), remaining = 1 - t;
        double First(double a, double b) => 3 * (remaining * remaining * a + 2 * remaining * t * (b - a) + t * t * (1 - b));
        double Second(double a, double b) => 6 * (remaining * (b - 2 * a) + t * (1 - 2 * b + a));
        double dx = First(X1, X2), dy = First(Y1, Y2);
        return (Bezier(t, Y1, Y2), dy / dx, (Second(Y1, Y2) * dx - dy * Second(X1, X2)) / (dx * dx * dx));
    }
}
