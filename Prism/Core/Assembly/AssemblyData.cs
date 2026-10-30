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
        if (ease) u = u * u * (3 - 2 * u);
        return k[i].V + (k[i + 1].V - k[i].V) * u;
    }
}

public sealed record AnimKey(double T, double V);
