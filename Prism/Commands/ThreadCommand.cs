using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Cut a modeled 60° thread into an existing round face: the outside of a pin, boss or bolt (external thread) or the wall of a hole that
/// is already drilled (internal thread). Which one is read from the face, so the same tool does both. The Hole tool makes new threaded holes.
/// </summary>
public sealed class ThreadCommand : Command
{
    readonly SelectionSlot _faces;
    readonly ChoiceParam _pitchKind;
    readonly NumberParam _pitch, _length, _offset;
    readonly BoolParam _otherEnd, _leftHand;

    /// <summary>ISO 261 coarse pitches by nominal diameter (mm).</summary>
    internal static readonly (double diameter, double pitch)[] Coarse =
        [(1.6, .35), (2, .4), (2.5, .45), (3, .5), (4, .7), (5, .8), (6, 1), (8, 1.25), (10, 1.5), (12, 1.75), (14, 2), (16, 2), (18, 2.5), (20, 2.5), (24, 3), (30, 3.5), (36, 4), (42, 4.5), (48, 5), (56, 5.5), (64, 6)];
    const double HoleFactor = 1.082532;           // tap-drill diameter = nominal - HoleFactor * pitch

    public ThreadCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Round faces to thread", Filter = SelFilter.Face, Multi = true });
        _pitchKind = Choice("Pitch", 0, "Standard coarse metric", "Custom");
        _pitch = Num("Custom pitch", 1.5, .05, 100, .05);
        _length = Num("Thread length (0 = whole face)", 0, 0, 100000, .5);
        _offset = Num("Start this far in from the end", 0, 0, 100000, .5);
        _otherEnd = Check("Start at the other end", false);
        _leftHand = Check("Left-hand thread", false);
    }

    public override string Title => "Thread";
    public override string Icon => "hole";
    public override string Hint => "Pick the round face of a bolt, pin or boss for an external thread, or the wall of a hole you already drilled for an internal one. The thread takes its size from the face: a pin's diameter is the thread's major diameter, and a hole's diameter is the tap drill. The pitch is the standard coarse one for that size, or type your own. The thread starts at the higher end of the face (tick Start at the other end to swap) and runs the length you set, the whole face by default.";

    protected override void UpdateVisibility() { _pitch.IsVisible = _pitchKind.Index == 1; }

    /// <summary>The standard coarse size nearest to a pin of diameter d, or a hole whose bore is d, if there is one close enough to be it.</summary>
    internal static (double diameter, double pitch)? Standard(double d, bool external)
    {
        var best = Coarse.Select(c => (c, off: Math.Abs(d - (external ? c.diameter : c.diameter - HoleFactor * c.pitch)))).OrderBy(x => x.off).First();
        return best.off <= Math.Max(0.15, 0.02 * d) ? best.c : null;
    }

    sealed record Plan(SceneObject Body, bool External, double Diameter, double Pitch, double Length, Vec3 Origin, Vec3 Into, bool ToTheEnd);

    Plan Resolve(SelItem item)
    {
        var body = S.Find(item.Obj) ?? throw new FormatException("That body no longer exists.");
        if (body.Kind != ObjKind.Solid || !body.HasBrep) throw new FormatException("Threads go on exact solid bodies. Convert a mesh to a solid first.");
        var face = S.ShapeOf(item) ?? throw new FormatException("That face no longer exists.");
        var info = Occ.FaceInfoOf(face);
        if (info.Type != "Cylinder" || Occ.AxisOf(face) is not { } axis) throw new FormatException("Pick a round (cylindrical) face: the outside of a pin or the wall of a hole. A thread cannot go on a " + info.Type.ToLowerInvariant() + " face.");
        var frame = S.FaceFrame(item) ?? throw new FormatException("Could not read that face.");
        var d = axis.Dir.Normalized();
        var radial = frame.point - axis.Origin; radial -= d * Vec3.Dot(radial, d);
        bool external = Vec3.Dot(frame.normal, radial) > 0;
        double radius = info.R1;

        double pitch, diameter;
        if (_pitchKind.Index == 1)
        {
            pitch = _pitch.Value;
            diameter = external ? 2 * radius : 2 * radius + HoleFactor * pitch;
        }
        else
        {
            var std = Standard(2 * radius, external) ?? throw new FormatException($"There is no standard coarse metric thread for a {(external ? "pin" : "hole")} of Ø{2 * radius:0.###} mm. Choose Custom and type the pitch.");
            (diameter, pitch) = std;
        }
        if (external ? diameter <= 1.2269 * pitch : diameter <= HoleFactor * pitch) throw new FormatException("The pitch is too coarse for that diameter. Use a smaller pitch.");

        var (t0, t1) = DirectHelpers.ExtentAlong(body, [item.Index], axis.Origin, d);
        double available = t1 - t0;
        if (available < 1e-6) throw new FormatException("That face has no length to thread.");
        // the start end: the one higher in the world (up if the axis is mostly vertical, else toward +X or +Y), or the other when asked
        double sign = Math.Abs(d.Z) > .5 ? Math.Sign(d.Z) : Math.Abs(d.X) >= Math.Abs(d.Y) ? Math.Sign(d.X) : Math.Sign(d.Y);
        if (sign == 0) sign = 1;
        bool fromHigh = !_otherEnd.Value;
        var into = fromHigh ? d * -sign : d * sign;
        double startT = fromHigh ? (sign > 0 ? t1 : t0) : (sign > 0 ? t0 : t1);
        double length = _length.Value == 0 ? available - _offset.Value : _length.Value;
        if (length <= 1e-6 || _offset.Value + length > available + 1e-6) throw new FormatException($"The thread does not fit: the face is {available:0.###} mm long, and the start offset plus the length is {_offset.Value + length:0.###} mm.");
        if (length / pitch > 200) throw new FormatException("A thread of more than 200 turns is not supported. Shorten it or use a coarser pitch.");
        return new Plan(body, external, diameter, pitch, length, axis.Origin + d * startT + into * _offset.Value, into, _offset.Value + length >= available - 1e-6);
    }

    /// <summary>The groove starts 0.01 mm outside the start end, and runs 0.01 mm past the far end when the thread reaches it, so no boolean meets a coincident face.</summary>
    static readonly double[] Phases = [0, 37, 83, 131, 211, 289];

    static double Overshoot(Plan plan) => plan.ToTheEnd ? .02 : .01;

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (!Complete) return null;
        var plans = _faces.Items.Where(i => i.Kind == SelKind.Face).Select(Resolve).ToList();
        if (plans.Count == 0) throw new FormatException("Pick a round face.");
        var o = new CommandOutput { Label = "Thread", Icon = Icon };
        foreach (var group in plans.GroupBy(p => p.Body))
        {
            var result = group.Key.Shape; bool owned = false;
            try
            {
                foreach (var plan in group)
                {
                    using var groove = plan.External ? Occ.ThreadGrooveExternal(plan.Diameter, plan.Pitch, plan.Length + Overshoot(plan), _leftHand.Value)
                                                     : Occ.ThreadGroove(plan.Diameter, plan.Pitch, plan.Length + Overshoot(plan), _leftHand.Value);
                    double grooveVolume = groove.Props().Volume, volumeBefore = result.Props().Volume;
                    // A right-handed frame like the Hole tool's, the groove starting 0.01 mm outside the end. The kernel's boolean is fragile where a
                    // helical groove meets a round face's seam, and sometimes returns the body untouched or cut wrongly, so the cut is checked against the
                    // groove's own volume and tried again with the groove turned to another phase if it is off.
                    var reference = Math.Abs(plan.Into.X) < .9 ? Vec3.UnitX : Vec3.UnitY;
                    var x0 = (reference - plan.Into * Vec3.Dot(reference, plan.Into)).Normalized(); var y0 = Vec3.Cross(plan.Into, x0).Normalized();
                    Shape? cut = null;
                    foreach (double degrees in Phases)
                    {
                        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
                        var gx = x0 * c + y0 * s; var gy = y0 * c - x0 * s;
                        double[] frame = [gx.X, gy.X, plan.Into.X, gx.Y, gy.Y, plan.Into.Y, gx.Z, gy.Z, plan.Into.Z];
                        using var placed = Occ.Transform(groove, frame, plan.Origin - plan.Into * .01);
                        try
                        {
                            var candidate = Occ.Cut(result, placed);
                            double removed = volumeBefore - candidate.Props().Volume;
                            if (candidate.IsValid && candidate.HasSolids && removed >= .85 * grooveVolume && removed <= 1.02 * grooveVolume) { cut = candidate; break; }
                            candidate.Dispose();
                        }
                        catch (KernelException) { }
                    }
                    if (cut == null) throw new FormatException("The kernel could not cut that thread cleanly. Try a slightly different length or start offset, or a custom pitch.");
                    if (owned) result.Dispose();
                    result = cut; owned = true;
                }
                var before = group.Key.Shape.Props();
                if (!result.IsValid || !result.HasSolids || before.Volume - result.Props().Volume < 1e-7)
                    throw new FormatException("The thread did not cut anything valid. Check the face, the start end and the length.");
                o.Replace.Add((group.Key, result)); owned = false;
            }
            finally { if (owned) result.Dispose(); }
        }
        var first = plans[0];
        Results.Add(new("Thread", $"M{first.Diameter:0.###} × {first.Pitch:0.###}, {(first.External ? "external" : "internal")}, {(_leftHand.Value ? "left" : "right")}-hand"));
        Results.Add(new("Length", $"{first.Length:0.###} mm ({first.Length / first.Pitch:0.#} turns)"));
        if (plans.Count > 1) Results.Add(new("Threads", plans.Count.ToString()));
        return o;
    }
}
