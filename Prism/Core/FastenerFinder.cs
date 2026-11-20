using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>A bolt, screw or pin found in holes of other bodies, with any nut threaded on it.</summary>
/// <param name="Kind">"hex bolt", "socket screw", "round-head screw", or "pin" (no head).</param>
/// <param name="Size">The metric size its shank matches ("M6"), or null.</param>
public sealed record Fastener(SceneObject Body, string Kind, double Diameter, double Length, string? Size, Vec3 Origin, Vec3 Axis,
    IReadOnlyList<(SceneObject Body, Feat Hole)> Holes, IReadOnlyList<SceneObject> Nuts)
{
    public string Describe => $"{(Size ?? $"Ø{Diameter:0.##}")} × {Length:0.#} {Kind}" + (Nuts.Count > 0 ? $" with {(Nuts.Count == 1 ? "a nut" : $"{Nuts.Count} nuts")}" : "");
}

/// <summary>
/// Finds fasteners among the solids of a document: a separate body whose longest outward round face (its shank) runs along the axis of a hole in
/// another body, that hole being a clearance or tapping size for it. A head wider than the shank makes it a bolt or screw (six flats around the
/// axis: hex head; a recess in the head: socket screw); a nut is a body with six flats whose hole fits round the shank. Recognition only reads the
/// geometry, so it works on assemblies imported from STEP.
/// </summary>
public static class FastenerFinder
{
    public static List<Fastener> Find(IEnumerable<SceneObject> objects)
    {
        var solids = objects.Where(o => o.Kind == ObjKind.Solid && o.HasBrep && o.Visible).ToList();
        var found = new List<Fastener>(); var nutsTaken = new HashSet<Guid>();
        foreach (var b in solids)
        {
            if (Shank(b) is not { } sh) continue;
            var (origin, axis, r, t0, t1) = sh;
            var holes = new List<(SceneObject, Feat)>();
            foreach (var other in solids)
            {
                if (other.Id == b.Id) continue;
                foreach (var h in FeatureFinder.Features(other))
                {
                    if (Bore(other, h) is not { } hr) continue;
                    if (!Coaxial(origin, axis, hr.Origin, h.Dir)) continue;
                    if (hr.Radius < r * 0.75 || hr.Radius > r * 1.6) continue;    // a tapping hole is about 0.83 of the shank, a loose clearance about 1.2
                    var (h0, h1) = Along(hr.Origin, h.Dir, hr.T0, hr.T1, origin, axis);
                    if (Math.Min(h1, t1) - Math.Max(h0, t0) <= 1e-6) continue;   // the shank must reach into the hole
                    holes.Add((other, h));
                }
            }
            if (holes.Count == 0) continue;
            var (outer, flats, recess) = Head(b, origin, axis, r);
            string kind = outer < r * 1.25 ? "pin" : flats >= 6 ? "hex bolt" : recess ? "socket screw" : "round-head screw";
            var nuts = solids.Where(n => n.Id != b.Id && !nutsTaken.Contains(n.Id) && IsNutOn(n, origin, axis, r)).ToList();
            foreach (var n in nuts) nutsTaken.Add(n.Id);
            found.Add(new Fastener(b, kind, 2 * r, t1 - t0, StandardHoles.Match(2 * r) is { } m && m.EndsWith("nominal") ? m.Split(' ')[0] : null, origin, axis, holes, nuts));
        }
        // a nut is not a bolt of its own
        return found.Where(f => !nutsTaken.Contains(f.Body.Id)).ToList();
    }

    /// <summary>The fasteners in this hole of this body.</summary>
    public static List<Fastener> In(IEnumerable<SceneObject> objects, SceneObject body, Feat hole) =>
        Find(objects).Where(f => f.Holes.Any(h => h.Body.Id == body.Id && h.Hole.Faces.SequenceEqual(hole.Faces))).ToList();

    /// <summary>
    /// The narrowest bore of a hole-like feature: a plain hole's own round, or for a counterbore or countersink the smallest coaxial round wall
    /// (the part the shank passes through). Null for anything else.
    /// </summary>
    public static Round? Bore(SceneObject body, Feat f)
    {
        if (f.Round is { Hole: true } r) return r;
        if (f.Kind is not (FeatureKind.Counterbore or FeatureKind.Countersink)) return null;
        Round? best = null; var m = body.Mesh;
        foreach (int i in f.Faces)
        {
            using var face = body.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            if (info.Type != "Cylinder" || Occ.AxisOf(face) is not { } ax || best != null && info.R1 >= best.Radius) continue;
            var d = ax.Dir.Normalized(); double t0 = double.MaxValue, t1 = double.MinValue;
            for (int t = m.FaceTriStart[i]; t < m.FaceTriStart[i + 1]; t++)
                for (int c = 0; c < 3; c++)
                {
                    int vi = m.Indices[3 * t + c] * 3;
                    double v = Vec3.Dot(new Vec3(m.Positions[vi], m.Positions[vi + 1], m.Positions[vi + 2]) - ax.Origin, d);
                    t0 = Math.Min(t0, v); t1 = Math.Max(t1, v);
                }
            best = new Round(ax.Origin, info.R1, true, t0, t1);
        }
        return best;
    }

    static bool Coaxial(Vec3 o1, Vec3 d1, Vec3 o2, Vec3 d2)
    {
        if (Vec3.Cross(d1.Normalized(), d2.Normalized()).Length > 1e-3) return false;
        var off = o2 - o1; var d = d1.Normalized();
        return (off - d * Vec3.Dot(off, d)).Length < 0.05;
    }

    static (double, double) Along(Vec3 o, Vec3 d, double t0, double t1, Vec3 origin, Vec3 axis)
    {
        var a = axis.Normalized(); double s = Vec3.Dot(d.Normalized(), a) < 0 ? -1 : 1;
        double b0 = Vec3.Dot(o + d.Normalized() * t0 - origin, a), b1 = Vec3.Dot(o + d.Normalized() * t1 - origin, a);
        return s > 0 ? (b0, b1) : (b1, b0);
    }

    /// <summary>The body's longest round outer face: its axis, radius and extent along the axis (measured from the origin).</summary>
    static (Vec3 origin, Vec3 axis, double r, double t0, double t1)? Shank(SceneObject b)
    {
        Feat? best = null;
        foreach (var f in FeatureFinder.Features(b))
            if (f.Round is { Hole: false } rd && (best == null || rd.T1 - rd.T0 > best.Round!.T1 - best.Round.T0)) best = f;
        if (best?.Round is not { } r) return null;
        var size = b.Shape.Props(); if (r.T1 - r.T0 < 0.5 * r.Radius) return null;   // a thin disc is not a shank
        return (r.Origin, best.Dir.Normalized(), r.Radius, r.T0, r.T1);
    }

    /// <summary>How far the body reaches from the axis (the head), how many flat faces run along the axis on the outside, and whether a pocket sits on the axis.</summary>
    static (double outer, int flats, bool recess) Head(SceneObject b, Vec3 origin, Vec3 axis, double r)
    {
        var m = b.Mesh; double outer = 0;
        for (int i = 0; i < m.Positions.Length; i += 3)
        {
            var p = new Vec3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]) - origin;
            outer = Math.Max(outer, (p - axis * Vec3.Dot(p, axis)).Length);
        }
        int flats = 0; bool recess = false;
        for (int i = 0; i < b.Shape.Count(SubKind.Face); i++)
        {
            using var face = b.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            if (info.Type != "Plane" || Math.Abs(Vec3.Dot(info.Normal.Normalized(), axis)) > 1e-3) continue;
            var c = face.Props().Center - origin; var radial = c - axis * Vec3.Dot(c, axis);
            if (radial.Length > r * 1.05) flats++;
            else if (radial.Length < r * 0.9) recess = true;
        }
        foreach (var f in FeatureFinder.Features(b))
            if (f.Kind is FeatureKind.Pocket or FeatureKind.Hole && Coaxial(origin, axis, f.Centre, f.Dir)) recess = true;
        return (outer, flats, recess);
    }

    static bool IsNutOn(SceneObject n, Vec3 origin, Vec3 axis, double r)
    {
        bool hole = FeatureFinder.Features(n).Any(f => f.Round is { Hole: true } hr && Coaxial(origin, axis, hr.Origin, f.Dir) && hr.Radius > r * 0.75 && hr.Radius < r * 1.3);
        if (!hole) return false;
        int flats = 0;
        for (int i = 0; i < n.Shape.Count(SubKind.Face); i++)
        {
            using var face = n.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            if (info.Type == "Plane" && Math.Abs(Vec3.Dot(info.Normal.Normalized(), axis)) < 1e-3) flats++;
        }
        return flats >= 6;
    }
}
