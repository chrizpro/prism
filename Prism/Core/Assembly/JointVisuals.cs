using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.Core.Assembly;

/// <summary>One motion value of a joint as the viewport shows it: a turn about an axis, or a slide along one.</summary>
/// <param name="Index">1 for the joint's first value, 2 for its second.</param>
/// <param name="Dir">The axis the joint turns about (angular) or slides along (linear).</param>
/// <param name="Ref">For a turn: the direction the arc starts from (the zero of the angle). Null for a slide.</param>
public readonly record struct JointChannel(int Index, Vec3 Dir, Vec3? Ref, double Min, double Max, double Value);

/// <summary>Draws where a joint is allowed to move and what its handles do, in the viewport.</summary>
public static class JointVisuals
{
    public const uint RangeColor = 0xB035B6FF, LimitColor = 0xFFFFC53D, HitColor = 0xFFE5484D, MarkerColor = 0xFFFFFFFF;

    /// <summary>A limit this large is not a limit: the joint is free to move.</summary>
    public static bool IsLimit(double v) => Math.Abs(v) < 1e5;

    /// <summary>Turn a vector about an axis by an angle in degrees.</summary>
    public static Vec3 Rotate(Vec3 v, Vec3 axis, double degrees)
    {
        var k = axis.Normalized(); double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
    }

    /// <summary>The motion values of a joint in the world, given the frame of its parent side (where the joint sits now).</summary>
    public static List<JointChannel> Channels(JointInfo j, Xform frame, double v1, double v2)
    {
        var z = frame.AxisZ; var x = frame.AxisX; var y = frame.AxisY;
        var zero = Rotate(x, z, j.ZeroAngle);
        double lo1 = j.HasLimits ? j.Min : double.MinValue, hi1 = j.HasLimits ? j.Max : double.MaxValue;
        double lo2 = j.HasLimits ? j.Min2 : double.MinValue, hi2 = j.HasLimits ? j.Max2 : double.MaxValue;
        var res = new List<JointChannel>();
        switch (j.Type)
        {
            case JointType.Revolute: res.Add(new(1, z, zero, lo1, hi1, v1)); break;
            case JointType.Slider: res.Add(new(1, z, null, lo1, hi1, v1)); break;
            case JointType.Cylindrical: res.Add(new(1, z, zero, lo1, hi1, v1)); res.Add(new(2, z, null, lo2, hi2, v2)); break;
            case JointType.Ball: res.Add(new(1, z, zero, lo1, hi1, v1)); res.Add(new(2, Rotate(x, z, j.ZeroAngle + v1), z, lo2, hi2, v2)); break;
            case JointType.Planar: res.Add(new(1, x, null, lo1, hi1, v1)); res.Add(new(2, y, null, lo2, hi2, v2)); break;
        }
        return res;
    }

    /// <summary>The allowed range as a ruler (a slide) or an arc (a turn), with a tick at each end and a mark where the value is now. Red when the value is at a limit.</summary>
    public static void Range(Overlay o, Vec3 origin, Vec3 dir, Vec3? refDir, double radius, double min, double max, double value)
    {
        bool hasMin = IsLimit(min), hasMax = IsLimit(max);
        bool atLimit = hasMin && value <= min + 1e-6 || hasMax && value >= max - 1e-6;
        var axis = dir.Normalized();
        if (refDir is { } r0)
        {
            var r = r0.Normalized();
            Vec3 At(double deg, double scale = 1) => origin + Rotate(r, axis, deg) * (radius * scale);
            if (hasMin || hasMax)
            {
                double a = hasMin ? min : Math.Min(value, 0) - 30, b = hasMax ? max : Math.Max(value, 0) + 30;
                int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(b - a) / 5));
                var pts = new List<Vec3>(); for (int i = 0; i <= n; i++) pts.Add(At(a + (b - a) * i / n));
                o.AddPolyline(pts, RangeColor, 2.2f);
                if (hasMin) o.AddLine(At(min, 0.85), At(min, 1.15), LimitColor, 3.2f);
                if (hasMax) o.AddLine(At(max, 0.85), At(max, 1.15), LimitColor, 3.2f);
            }
            o.AddLine(origin, At(value), atLimit ? HitColor : MarkerColor, 2.4f);
            o.AddDot(At(value), atLimit ? HitColor : MarkerColor, 8);
        }
        else
        {
            if (hasMin || hasMax)
            {
                double a = hasMin ? min : Math.Min(value, 0) - radius * 0.5, b = hasMax ? max : Math.Max(value, 0) + radius * 0.5;
                o.AddLine(origin + axis * a, origin + axis * b, RangeColor, 2.2f);
                var side = Math.Abs(axis.Z) < 0.9 ? Vec3.Cross(axis, Vec3.UnitZ).Normalized() : Vec3.Cross(axis, Vec3.UnitX).Normalized();
                double tick = radius * 0.12;
                if (hasMin) o.AddLine(origin + axis * min - side * tick, origin + axis * min + side * tick, LimitColor, 3.2f);
                if (hasMax) o.AddLine(origin + axis * max - side * tick, origin + axis * max + side * tick, LimitColor, 3.2f);
            }
            o.AddDot(origin + axis * value, atLimit ? HitColor : MarkerColor, 8);
        }
    }
}

/// <summary>Grab handles for driving joints in the viewport: drag an arc to turn a joint, drag an arrow to slide it, and it stops at its limits.</summary>
public static class JointHandles
{
    /// <summary>One handle per motion value. Handles follow the pose being previewed; when joints are selected only theirs show.</summary>
    public static List<HandleSpec> Build(CadSession s, IReadOnlyList<(JointInfo joint, NumberParam v1, NumberParam? v2)> rows)
    {
        var res = new List<HandleSpec>();
        if (rows.Count == 0) return res;
        var over = new Dictionary<string, JointInfo>();
        foreach (var r in rows) over[r.joint.Key] = r.joint with { Value = r.v1.Value, Value2 = r.v2?.Value ?? r.joint.Value2 };
        var placements = s.SolvePlacements(over, out _);
        var picked = new HashSet<string>(s.SelectedObjects.Where(o => o.Asm is JointInfo).Select(o => ((JointInfo)o.Asm!).Key));
        foreach (var (joint, v1, v2) in rows)
        {
            if (picked.Count > 0 && !picked.Contains(joint.Key)) continue;
            if (!placements.TryGetValue(joint.ParentKey, out var parent)) continue;
            var frame = parent * joint.A;
            double radius = Math.Max(s.Camera.PixelSizeAt(frame.T) * 70, 1);
            foreach (var ch in JointVisuals.Channels(joint, frame, v1.Value, v2?.Value ?? joint.Value2))
            {
                var param = ch.Index == 1 ? v1 : v2;
                if (param == null) continue;
                res.Add(new HandleSpec(frame.T, ch.Dir, param, 1, ch.Ref, radius, true));
            }
        }
        return res;
    }
}
