using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prism.Kernel;

namespace Prism.Simulation;

/// <summary>Plans the joint motion of an arm that carries a tool point along a path: a speed profile along the path, then inverse kinematics at every output time.</summary>
public static class RobotPath
{
    public sealed record Plan(double[] Times, double[][] JointValues, int[] Active, double MaxPositionError, double MaxOrientationError, int Unreached, double PathLength, List<string> Notes);

    /// <summary>Distance along the path (mm) at each output time for a trapezoid speed profile (accelerate, cruise, brake). Returns the end time.</summary>
    public static (double Total, Func<double, double> Distance) TimeLaw(double length, double speed, double accel)
    {
        speed = Math.Max(speed, 1e-6); accel = Math.Max(accel, 1e-6);
        double tAcc = speed / accel, dAcc = speed * speed / (2 * accel);
        if (2 * dAcc >= length)
        {
            double peak = Math.Sqrt(length * accel), tp = peak / accel;                     // never reaches full speed
            return (2 * tp, t => t <= tp ? accel * t * t / 2 : length - accel * (2 * tp - t) * (2 * tp - t) / 2);
        }
        double cruise = (length - 2 * dAcc) / speed, total = 2 * tAcc + cruise;
        return (total, t => t <= tAcc ? accel * t * t / 2 : t <= tAcc + cruise ? dAcc + speed * (t - tAcc) : length - accel * (total - t) * (total - t) / 2);
    }

    /// <summary>Round the corners of a polyline: each corner is replaced by a smooth curve that starts and ends up to <paramref name="radius"/> from it. A robot cannot turn a corner at speed without it.</summary>
    public static List<Vec3> Blend(IReadOnlyList<Vec3> pts, double radius)
    {
        if (pts.Count < 3 || radius <= 0) return [.. pts];
        var res = new List<Vec3> { pts[0] };
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var a = pts[i - 1]; var c = pts[i]; var b = pts[i + 1];
            double la = (c - a).Length, lb = (b - c).Length;
            if (la < 1e-9 || lb < 1e-9) continue;
            if (Vec3.Dot((c - a).Normalized(), (b - c).Normalized()) > 0.9999) { res.Add(c); continue; }                // already straight
            double cut = Math.Min(radius, Math.Min(la, lb) / 2);
            var p0 = c + (a - c).Normalized() * cut; var p2 = c + (b - c).Normalized() * cut;
            for (int k = 0; k <= 16; k++)
            {
                double u = k / 16.0, w0 = (1 - u) * (1 - u), w1 = 2 * u * (1 - u), w2 = u * u;
                res.Add(p0 * w0 + c * w1 + p2 * w2);
            }
        }
        res.Add(pts[^1]);
        return res;
    }

    static Vec3 PointAt(IReadOnlyList<Vec3> pts, double[] cum, double s)
    {
        if (s <= 0) return pts[0];
        if (s >= cum[^1]) return pts[^1];
        int i = Array.BinarySearch(cum, s); if (i < 0) i = ~i - 1; i = Math.Clamp(i, 0, pts.Count - 2);
        double seg = cum[i + 1] - cum[i]; return seg < 1e-12 ? pts[i] : pts[i] + (pts[i + 1] - pts[i]) * ((s - cum[i]) / seg);
    }

    /// <summary>
    /// Follow a polyline (mm) with a point of a part. If the point does not start on the path, the first move goes straight to its start.
    /// Joint values come out in degrees or mm, one row per active joint, for the output times.
    /// </summary>
    public static Plan Follow(MotionEngine engine, IReadOnlyList<int> active, string bodyKey, Vec3 local, IReadOnlyList<Vec3> path, double speed, double accel, double interval,
        bool holdOrientation, double orientationWeight = 100, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var q = engine.StartVector();
        var (here, rot) = engine.ToolPose(q, bodyKey, local);
        var pts = new List<Vec3>();
        if ((here - path[0]).Length > 1e-3) pts.Add(here);
        pts.AddRange(path);
        pts = Blend(pts, speed * speed / Math.Max(accel, 1e-6));
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length;
        double length = cum[^1];
        if (length < 1e-6) throw new InvalidOperationException("The path has no length.");
        var (total, dist) = TimeLaw(length, speed, accel);
        int steps = Math.Max(2, (int)Math.Ceiling(total / interval)); double dt = total / steps;
        var times = Enumerable.Range(0, steps + 1).Select(i => i * dt).ToArray();
        var vals = active.Select(_ => new double[steps + 1]).ToArray();
        double maxPos = 0, maxRot = 0; int unreached = 0;
        for (int i = 0; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PointAt(pts, cum, dist(times[i]));
            var r = engine.InverseKinematics(q, active, bodyKey, local, target, holdOrientation ? rot : null, orientationWeight);
            q = r.Q; maxPos = Math.Max(maxPos, r.PositionError); maxRot = Math.Max(maxRot, r.OrientationError);
            if (!r.Converged) { unreached++; if (unreached == 1) notes.Add($"The tool cannot reach the path at about {times[i]:0.###} s (it is {r.PositionError:0.##} mm short): outside the arm's reach or its joint limits."); }
            for (int a = 0; a < active.Count; a++) { var d = engine.IndependentDof(active[a]); vals[a][i] = q[active[a]] / d.Scale; }
        }
        return new Plan(times, vals, active.ToArray(), maxPos, maxRot, unreached, length, notes);
    }
}
