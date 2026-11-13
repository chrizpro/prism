using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Direct edits on the faces of a surface body. A surface has no volume for its neighbours to stretch through, so a moved
/// face is carried to its new place and joined back to the faces it left with walls along the step, like a raised or sunk
/// panel. Where the face lay on the open border of the surface there is no neighbour, and no wall is made.
/// </summary>
static class SurfaceFaceEdit
{
    const double Touch = 1e-4;

    /// <summary>Carry the picked faces along a vector.</summary>
    public static Shape Move(Shape sheet, IReadOnlyCollection<int> picked, Vec3 v)
    {
        var (keep, faces) = Parts(sheet, picked);
        var result = new List<Shape>(keep);
        foreach (var face in faces)
        {
            result.Add(Occ.Translate(face, v));
            foreach (var (edge, _) in StepEdges(face, faces, keep)) result.Add(Occ.Extrude(edge, v));
        }
        return Join(result);
    }

    /// <summary>Carry the picked faces along their own normals by a distance (curved faces keep their shape, offset).</summary>
    public static Shape Offset(Shape sheet, IReadOnlyCollection<int> picked, double distance)
    {
        var (keep, faces) = Parts(sheet, picked);
        var result = new List<Shape>(keep);
        foreach (var face in faces)
        {
            // the slab between the face and its offset has everything needed: the offset face and a wall on every side
            Shape slab;
            try { slab = Occ.Thicken(face, distance); }
            catch (KernelException) { throw new FormatException("That face cannot be offset by this distance. Try a smaller distance, or Move Face."); }
            var origin = ToolShapes.Centroid(face);
            var sides = Enumerable.Range(0, slab.Count(SubKind.Face)).Select(i => slab.Sub(SubKind.Face, i)).ToList();
            if (sides.Count < 3) throw new FormatException("That face cannot be offset by this distance.");
            var original = sides.MinBy(s => (ToolShapes.Centroid(s) - origin).Length)!;
            sides.Remove(original);
            var steps = StepEdges(face, faces, keep).Select(e => e.Middle).ToList();
            foreach (var side in sides)
            {
                // the offset face touches none of the old border; a wall is kept only where the face had a neighbour
                bool wall = TouchesBorder(side, face);
                if (!wall) { result.Add(side); continue; }
                foreach (var middle in steps)
                {
                    using var at = Occ.Vertex(middle);
                    if (Occ.Distance(side, at).Distance < Touch) { result.Add(side); break; }
                }
            }
        }
        return Join(result);
    }

    static (List<Shape> keep, List<Shape> faces) Parts(Shape sheet, IReadOnlyCollection<int> picked)
    {
        int n = sheet.Count(SubKind.Face);
        var keep = new List<Shape>(); var faces = new List<Shape>();
        for (int i = 0; i < n; i++) (picked.Contains(i) ? faces : keep).Add(sheet.Sub(SubKind.Face, i));
        if (faces.Count == 0) throw new FormatException("Pick a face of the surface.");
        return (keep, faces);
    }

    /// <summary>The edges of a picked face that it shares with a face that stays: the step a wall has to close.</summary>
    static List<(Shape Edge, Vec3 Middle)> StepEdges(Shape face, List<Shape> picked, List<Shape> keep)
    {
        var list = new List<(Shape, Vec3)>();
        for (int i = 0; i < face.Count(SubKind.Edge); i++)
        {
            var edge = face.Sub(SubKind.Edge, i);
            var samples = Occ.CurveSamples(edge, 9, 1);
            if (samples.Count == 0 || samples[0].Length < 2) continue;
            var middle = samples[0][samples[0].Length / 2].p;
            using var at = Occ.Vertex(middle);
            if (picked.Any(other => !ReferenceEquals(other, face) && Occ.Distance(other, at).Distance < Touch)) continue;   // inside the picked region
            if (keep.Any(other => Occ.Distance(other, at).Distance < Touch)) list.Add((edge, middle));
        }
        return list;
    }

    static bool TouchesBorder(Shape side, Shape face)
    {
        for (int i = 0; i < face.Count(SubKind.Edge); i++)
        {
            var samples = Occ.CurveSamples(face.Sub(SubKind.Edge, i), 9, 1);
            if (samples.Count == 0 || samples[0].Length < 2) continue;
            using var at = Occ.Vertex(samples[0][samples[0].Length / 2].p);
            if (Occ.Distance(side, at).Distance < Touch) return true;
        }
        return false;
    }

    static Shape Join(List<Shape> faces) => faces.Count == 1 ? faces[0] : Occ.Sew(faces, 0.01, false);
}
