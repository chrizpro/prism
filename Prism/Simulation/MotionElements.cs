using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Simulation;

/// <summary>How a load changes in time: always on, on for a stretch, or a sine about zero.</summary>
public enum LoadShape { Constant, Pulse, Sine }

public sealed record LoadProfile(LoadShape Shape, double Start = 0, double Length = 1, double Frequency = 1)
{
    public static readonly LoadProfile Always = new(LoadShape.Constant);
    public double Factor(double t) => Shape switch
    {
        LoadShape.Pulse => t >= Start && t < Start + Length ? 1 : 0,
        LoadShape.Sine => Math.Sin(2 * Math.PI * Frequency * t),
        _ => 1,
    };
}

/// <summary>A force or torque element acting on the mechanism. Points are in the local coordinates of their part (mm).</summary>
public abstract record MotionForce(string Name);

/// <summary>
/// A spring, damper and push in one: it acts along the line between two points (the second may be a fixed point in the world).
/// Force pulling the points together = Stiffness·(length − Rest) + Damping·(length rate) − Push. A hydraulic or electric cylinder is a Push (its pressure times its area).
/// </summary>
public sealed record SpringForce(string Name, string BodyA, Vec3 PointA, string? BodyB, Vec3 PointB, double Stiffness, double Damping, double RestLength, double Push) : MotionForce(Name);

/// <summary>A force of a given size and direction at a point of a part. The direction is fixed in the world, or turns with the part.</summary>
public sealed record PointForce(string Name, string Body, Vec3 Point, Vec3 Direction, bool BodyFixed, double Magnitude, LoadProfile Profile) : MotionForce(Name);

/// <summary>A pure torque (N·m) about an axis on a part.</summary>
public sealed record BodyTorque(string Name, string Body, Vec3 Axis, bool BodyFixed, double Magnitude, LoadProfile Profile) : MotionForce(Name);

/// <summary>A marked point on a part: a payload when it has mass (kg), or a tool point to follow when it has none.</summary>
public sealed record MotionPoint(string Name, string BodyKey, Vec3 Local, double Mass);

/// <summary>Reads a joint's motion as rows of time and position, typed or from a file: a cam's lift, a recorded road, any motion no other drive describes.</summary>
public static class MotionTable
{
    public static (double[] Times, double[] Values) Read(string text, bool repeat, double duration)
    {
        string source = text.Trim();
        if (source.IndexOfAny(['\n', ';']) < 0 && System.IO.File.Exists(source)) source = System.IO.File.ReadAllText(source);
        var t = new List<double>(); var v = new List<double>();
        foreach (var row in source.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = row.Split([',', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length >= 2 && double.TryParse(cols[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
                && double.TryParse(cols[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)) { t.Add(a); v.Add(b); }   // anything else is a header
        }
        if (t.Count < 2) throw new InvalidOperationException("A motion table needs at least two rows of time and position.");
        for (int i = 1; i < t.Count; i++) if (t[i] <= t[i - 1]) throw new InvalidOperationException($"The times in a motion table must increase (see the row at {t[i]:0.###} s).");
        if (repeat)
        {
            if (Math.Abs(v[^1] - v[0]) > 1e-9) throw new InvalidOperationException("To repeat, a motion table must end at the position it starts from.");
            double t0 = t[0], period = t[^1] - t0; int n = t.Count;
            for (int cycle = 1; t0 + cycle * period < duration; cycle++)
                for (int i = 1; i < n; i++) { t.Add(t[i] + cycle * period); v.Add(v[i]); }
        }
        return (t.ToArray(), v.ToArray());
    }
}

/// <summary>A natural cubic spline through (time, value) samples: smooth position, speed and acceleration for a joint that follows a planned path.</summary>
public sealed class CubicSpline
{
    readonly double[] _t, _y, _m;
    public CubicSpline(double[] t, double[] y)
    {
        if (t.Length != y.Length || t.Length < 2) throw new ArgumentException("A path needs at least two samples.");
        _t = t; _y = y; int n = t.Length; _m = new double[n];
        if (n < 3) return;
        // second derivatives from the tridiagonal system, natural ends
        var a = new double[n]; var b = new double[n]; var c = new double[n]; var d = new double[n];
        b[0] = 1; b[n - 1] = 1;
        for (int i = 1; i < n - 1; i++)
        {
            double h0 = t[i] - t[i - 1], h1 = t[i + 1] - t[i];
            a[i] = h0; b[i] = 2 * (h0 + h1); c[i] = h1;
            d[i] = 6 * ((y[i + 1] - y[i]) / h1 - (y[i] - y[i - 1]) / h0);
        }
        for (int i = 1; i < n; i++) { double w = a[i] / b[i - 1]; b[i] -= w * c[i - 1]; d[i] -= w * d[i - 1]; }
        _m[n - 1] = d[n - 1] / b[n - 1];
        for (int i = n - 2; i >= 0; i--) _m[i] = (d[i] - c[i] * _m[i + 1]) / b[i];
    }

    public double Start => _t[0];
    public double End => _t[^1];

    /// <summary>Value, first and second derivative at a time; outside the samples the ends are held still.</summary>
    public (double q, double v, double a) At(double time)
    {
        if (time <= _t[0]) return (_y[0], 0, 0);
        if (time >= _t[^1]) return (_y[^1], 0, 0);
        int i = Array.BinarySearch(_t, time); if (i < 0) i = ~i - 1; i = Math.Clamp(i, 0, _t.Length - 2);
        double h = _t[i + 1] - _t[i], l = _t[i + 1] - time, r = time - _t[i];
        double p = _y[i] / h - _m[i] * h / 6, q = _y[i + 1] / h - _m[i + 1] * h / 6;
        double val = _m[i] * l * l * l / (6 * h) + _m[i + 1] * r * r * r / (6 * h) + p * l + q * r;
        double vel = -_m[i] * l * l / (2 * h) + _m[i + 1] * r * r / (2 * h) - p + q;
        double acc = _m[i] * l / h + _m[i + 1] * r / h;
        return (val, vel, acc);
    }
}
