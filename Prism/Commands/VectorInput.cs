using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// One reusable way for a command to ask for a direction in space (owner request 2026-11-16: finer control over how a force acts on a face, and a
/// vector that other CAE tools can share). The panel shows a single <c>Direction</c> choice, whose first entries are the ones loads always had, plus:
/// <list type="bullet">
/// <item><b>Custom</b>: the three components X, Y, Z of any vector in global axes (only the direction matters; the size is the load's own value).</item>
/// <item><b>Tilted from the face normal</b> (faces only): an angle from "straight into the face" (0°) to "along the face" (90°), and a turn around the normal.</item>
/// <item><b>Along an edge, or a plane's normal</b>: taken from geometry you pick, with Reverse to flip it.</item>
/// </list>
/// A command adds <see cref="Kind"/>, the numbers and <see cref="From"/> to its own parameter list, calls <see cref="UpdateVisibility"/> from its own,
/// and asks <see cref="Resolve"/> for the unit vector. Everything is a unit vector in global axes (mm, N, Z up); a direction with no length is refused.
/// </summary>
public sealed class VectorInput
{
    public enum Source { Normal, Axis, Custom, Tilted, Geometry }

    public const string DirectionLabel = "Direction";
    static readonly string[] AxisNames = ["+X", "+Y", "+Z", "-X", "-Y", "-Z"];
    static readonly Vec3[] AxisVectors = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitX, -Vec3.UnitY, -Vec3.UnitZ];
    const string NormalOption = "Into the face (along its normal)", CustomOption = "Custom: type X, Y, Z", TiltedOption = "Tilted from the face normal", GeometryOption = "Along an edge, or a plane's normal";

    public readonly ChoiceParam Kind;
    public readonly NumberParam X, Y, Z, Tilt, Around;
    public readonly BoolParam Reverse;
    public readonly SelectionSlot From;
    readonly bool _faces;

    /// <param name="faceModes">True for loads on faces: adds "into the face" and "tilted from the face normal".</param>
    /// <param name="defaultAxis">Which of ±X, ±Y, ±Z is chosen to begin with when <paramref name="faceModes"/> is false (index into +X, +Y, +Z, -X, -Y, -Z).</param>
    public VectorInput(bool faceModes, int defaultAxis = 5)
    {
        _faces = faceModes;
        var options = new List<string>();
        if (faceModes) options.Add(NormalOption);
        options.AddRange(AxisNames);
        options.Add(CustomOption);
        if (faceModes) options.Add(TiltedOption);
        options.Add(GeometryOption);
        Kind = new ChoiceParam { Label = DirectionLabel, Options = options, Index = faceModes ? 0 : defaultAxis };
        X = new NumberParam { Label = "Direction X", Value = 0, Min = -1e9, Max = 1e9, Step = 0.1, Unit = "" };
        Y = new NumberParam { Label = "Direction Y", Value = 0, Min = -1e9, Max = 1e9, Step = 0.1, Unit = "" };
        Z = new NumberParam { Label = "Direction Z", Value = -1, Min = -1e9, Max = 1e9, Step = 0.1, Unit = "" };
        Tilt = new NumberParam { Label = "Tilt from the face normal (0° straight in, 90° along the face)", Value = 30, Min = 0, Max = 90, Step = 5, Unit = "°" };
        Around = new NumberParam { Label = "Turn around the face normal", Value = 0, Min = -360, Max = 360, Step = 15, Unit = "°" };
        Reverse = new BoolParam { Label = "Reverse the direction", Value = false };
        From = new SelectionSlot { Label = "Direction taken from (edge, plane or face)", Filter = SelFilter.Edge | SelFilter.Plane | SelFilter.Face, Optional = true };
        X.HelpText = Y.HelpText = Z.HelpText = "The components of the direction in the model's X, Y and Z axes. Only their ratio matters: 1, 1, 0 and 5, 5, 0 point the same way. The size of the load is set separately.";
        Tilt.HelpText = "Straight into a flat face is 0°. Raising it leans the load toward sliding across the face (90° is pure shear). Turn around the normal chooses which way it leans.";
    }

    /// <summary>The parameters in the order they should be added to a command.</summary>
    public IEnumerable<Param> Params => [Kind, X, Y, Z, Tilt, Around, Reverse];

    public Source Mode => Kind.Selected switch
    {
        NormalOption => Source.Normal,
        CustomOption => Source.Custom,
        TiltedOption => Source.Tilted,
        GeometryOption => Source.Geometry,
        _ => Source.Axis,
    };

    /// <summary>Show only the numbers the chosen direction needs.</summary>
    public void UpdateVisibility()
    {
        var mode = Mode;
        X.IsVisible = Y.IsVisible = Z.IsVisible = mode == Source.Custom;
        Tilt.IsVisible = Around.IsVisible = mode == Source.Tilted;
        Reverse.IsVisible = mode == Source.Geometry;
        From.IsVisible = mode == Source.Geometry;
    }

    // ------------------------------------------------------------------ the maths (no document needed, so it can be checked against hand calculations)

    /// <summary>The same direction with length 1. A zero, tiny or non-finite vector has no direction and is refused.</summary>
    public static Vec3 Unit(Vec3 v)
    {
        if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)) throw new FormatException("The direction is not a number. Check X, Y and Z.");
        double length = v.Length;
        if (length < 1e-9) throw new FormatException("The direction has no length. Give at least one of X, Y and Z a value other than zero.");
        return v / length;
    }

    /// <summary>
    /// The direction of a load tilted away from "straight into a face". 0° is -normal (pushing in); 90° lies in the face. <paramref name="aroundDeg"/> turns the lean about
    /// the normal, counter-clockwise seen from outside the face, starting from the face's own X reference (the part of global +X lying in the face; +Y when the face is square to X).
    /// </summary>
    public static Vec3 Tilted(Vec3 faceNormal, double tiltDeg, double aroundDeg)
    {
        var n = Unit(faceNormal);
        var (u, v) = InPlane(n);
        double a = aroundDeg * Math.PI / 180, t = tiltDeg * Math.PI / 180;
        var lean = u * Math.Cos(a) + v * Math.Sin(a);
        return Unit(-n * Math.Cos(t) + lean * Math.Sin(t));
    }

    /// <summary>The two unit directions in a face that the tilt's turn angle is measured from (u) and toward (v = normal x u).</summary>
    public static (Vec3 u, Vec3 v) InPlane(Vec3 normal)
    {
        var n = normal.Normalized();
        var reference = Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
        var u = (reference - n * Vec3.Dot(reference, n)).Normalized();
        return (u, Vec3.Cross(n, u));
    }

    /// <summary>Split a force direction into the part that pushes into a face (positive) or pulls out of it (negative), and the part that slides across it.</summary>
    public static (double normal, double shear) Split(Vec3 unitDirection, Vec3 faceNormal, double magnitude)
    {
        var n = faceNormal.Normalized();
        double into = -Vec3.Dot(unitDirection, n);
        double along = Math.Sqrt(Math.Max(0, 1 - into * into));
        return (into * magnitude, along * magnitude);
    }

    // ------------------------------------------------------------------ the choice resolved against the document

    /// <summary>What the choice means: along each face's own normal, or one fixed unit vector in global axes.</summary>
    public sealed record Resolved(bool AlongNormal, Vec3 Direction);

    /// <param name="s">The document, to read a picked edge or plane.</param>
    /// <param name="faceNormals">Outward normals of the picked faces; needed for Tilted, which needs them to be flat and parallel.</param>
    public Resolved Resolve(CadSession s, IReadOnlyList<Vec3> faceNormals)
    {
        switch (Mode)
        {
            case Source.Normal: return new(true, -Vec3.UnitZ);
            case Source.Axis: return new(false, AxisVectors[Array.IndexOf(AxisNames, Kind.Selected)]);
            case Source.Custom: return new(false, Unit(new Vec3(X.Value, Y.Value, Z.Value)));
            case Source.Tilted:
            {
                if (!_faces || faceNormals.Count == 0) throw new FormatException("Pick the faces first: a tilt is measured from their normal.");
                var n = faceNormals[0].Normalized();
                if (faceNormals.Any(f => Vec3.Dot(f.Normalized(), n) < 0.9999))
                    throw new FormatException("A tilt needs flat faces that all point the same way. Pick one flat face, or use Custom to type the direction.");
                return new(false, Tilted(n, Tilt.Value, Around.Value));
            }
            default:
            {
                if (From.Items.Count == 0) throw new FormatException("Pick an edge, a plane or a face to take the direction from.");
                var v = FromGeometry(s, From.Items[0]);
                return new(false, Reverse.Value ? -v : v);
            }
        }
    }

    /// <summary>A straight edge gives its direction from start to end; a plane or flat face gives its normal.</summary>
    static Vec3 FromGeometry(CadSession s, SelItem item)
    {
        if (item.Kind == SelKind.Edge)
        {
            var edge = s.ShapeOf(item) ?? throw new FormatException("That edge is gone.");
            var info = Occ.EdgeInfoOf(edge);
            var along = info.End - info.Start;
            if (along.Length < 1e-9 || Math.Abs(info.Length - along.Length) > 1e-6 * Math.Max(1, info.Length))
                throw new FormatException("A direction can be taken from a straight edge. Curved edges have no single direction.");
            return Unit(along);
        }
        if (s.PlaneOf(item) is { } plane) return Unit(plane.Normal);
        throw new FormatException("Pick a straight edge, a plane or a flat face.");
    }

    /// <summary>A short readout of the direction for the panel: its unit vector.</summary>
    public static string Describe(Vec3 unit) => $"({unit.X:0.####}, {unit.Y:0.####}, {unit.Z:0.####})";
}
