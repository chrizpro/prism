using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>
/// Where on a flat face a load acts (owner request 2026-11-16: "a way to apply a vector on a certain area of a face"): the whole face, a circle, or a rectangle.
/// The centre is given as two distances from the middle of the face, measured along the same two in-face directions the tilt uses
/// (<see cref="VectorInput.InPlane"/>), so "across" and "up" mean the same everywhere. A command adds the parameters, calls <see cref="UpdateVisibility"/>,
/// <see cref="Resolve"/> to store the region on its <see cref="Load"/>, and uses <see cref="Handles"/> and <see cref="Outline"/> for the viewport.
/// </summary>
public sealed class FaceRegionInput
{
    const string WholeOption = "The whole face", CircleOption = "A circle on the face", RectangleOption = "A rectangle on the face";

    public readonly ChoiceParam Kind;
    public readonly NumberParam Across, Up, Radius, Width, Height, Turn;

    public FaceRegionInput()
    {
        Kind = new ChoiceParam { Label = "Apply to", Options = [WholeOption, CircleOption, RectangleOption], Index = 0 };
        Across = Offset("Centre across the face", "How far the centre of the area is from the middle of the face, along the face's own X reference (the part of the model's +X lying in the face; +Y for a face square to X).");
        Up = Offset("Centre up the face", "How far the centre of the area is from the middle of the face, at right angles to 'across', counter-clockwise seen from outside the face.");
        Radius = new NumberParam { Label = "Circle radius", Value = 5, Min = 0.001, Max = 1e6, Step = 1, Unit = "mm" };
        Width = new NumberParam { Label = "Rectangle width", Value = 10, Min = 0.001, Max = 1e6, Step = 1, Unit = "mm" };
        Height = new NumberParam { Label = "Rectangle height", Value = 5, Min = 0.001, Max = 1e6, Step = 1, Unit = "mm" };
        Turn = new NumberParam { Label = "Rectangle turn", Value = 0, Min = -360, Max = 360, Step = 15, Unit = "°" };
    }

    static NumberParam Offset(string label, string help) => new() { Label = label, Value = 0, Min = -1e6, Max = 1e6, Step = 1, Unit = "mm", HelpText = help };

    public IEnumerable<Param> Params => [Kind, Across, Up, Radius, Width, Height, Turn];

    public LoadRegionShape Shape => Kind.Index switch { 1 => LoadRegionShape.Circle, 2 => LoadRegionShape.Rectangle, _ => LoadRegionShape.WholeFace };
    public bool IsWholeFace => Shape == LoadRegionShape.WholeFace;

    public void UpdateVisibility()
    {
        var shape = Shape;
        Across.IsVisible = Up.IsVisible = shape != LoadRegionShape.WholeFace;
        Radius.IsVisible = shape == LoadRegionShape.Circle;
        Width.IsVisible = Height.IsVisible = Turn.IsVisible = shape == LoadRegionShape.Rectangle;
    }

    /// <summary>A starting size for the face just picked: a circle a quarter of the face's size across, a rectangle half by a quarter.</summary>
    public void SizeFor(double faceSize)
    {
        if (faceSize <= 0) return;
        Radius.Value = Math.Max(0.5, Math.Round(faceSize * 0.15, 1));
        Width.Value = Math.Max(1, Math.Round(faceSize * 0.5, 1)); Height.Value = Math.Max(0.5, Math.Round(faceSize * 0.25, 1));
    }

    /// <summary>The two unit directions the region is laid out along: the face's in-face references, the rectangle's turned by <see cref="Turn"/> about the normal.</summary>
    public (Vec3 u, Vec3 v) Frame(Vec3 faceNormal)
    {
        var (u, v) = VectorInput.InPlane(faceNormal);
        if (Shape != LoadRegionShape.Rectangle) return (u, v);
        double t = Turn.Value * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t);
        return (u * c + v * s, v * c - u * s);
    }

    /// <summary>Fill the region fields of a load. The offsets are measured on the face's own (unturned) references; the rectangle then turns about its centre.</summary>
    public void Resolve(Load load, Vec3 faceCentre, Vec3 faceNormal)
    {
        load.Region = Shape;
        if (Shape == LoadRegionShape.WholeFace) return;
        var (u0, v0) = VectorInput.InPlane(faceNormal);
        var (u, v) = Frame(faceNormal);
        load.RegionCentre = faceCentre + u0 * Across.Value + v0 * Up.Value;
        load.RegionU = u; load.RegionV = v;
        if (Shape == LoadRegionShape.Circle) { load.RegionA = Radius.Value; load.RegionB = Radius.Value; }
        else { load.RegionA = Width.Value; load.RegionB = Height.Value; }
        if (load.RegionA <= 0 || load.RegionB <= 0) throw new FormatException("The area has no size. Give it a width and height (or a radius) above zero.");
    }

    /// <summary>Grab handles: two arrows from the face centre slide the area, and size arrows from its centre set the radius, or the width and height.</summary>
    public List<HandleSpec> Handles(Vec3 faceCentre, Vec3 faceNormal)
    {
        var list = new List<HandleSpec>();
        if (IsWholeFace) return list;
        var (u0, v0) = VectorInput.InPlane(faceNormal);
        var (u, v) = Frame(faceNormal);
        var centre = faceCentre + u0 * Across.Value + v0 * Up.Value;
        list.Add(new HandleSpec(faceCentre, u0, Across));
        list.Add(new HandleSpec(faceCentre, v0, Up));
        if (Shape == LoadRegionShape.Circle) list.Add(new HandleSpec(centre, u, Radius));
        else { list.Add(new HandleSpec(centre, u, Width, 2)); list.Add(new HandleSpec(centre, v, Height, 2)); }      // arrows reach the edge, half the size away
        return list;
    }

    /// <summary>The outline of the area on the face, lifted a hair off it so it draws in front.</summary>
    public List<Vec3> Outline(Vec3 faceCentre, Vec3 faceNormal)
    {
        var probe = new Load();
        try { Resolve(probe, faceCentre, faceNormal); } catch (FormatException) { return []; }
        var lift = faceNormal.Normalized() * 0.05;
        return probe.RegionOutline().Select(p => p + lift).ToList();
    }
}
