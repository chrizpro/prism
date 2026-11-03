using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// An exact solid from the planes, cylinders, cones and spheres found in a scan. Each fitted surface slices a block around the scan; the pieces that lie inside the scan are joined.
/// Sewing fitted faces needs a tolerance as wide as the gaps between them, which leaves edges that later booleans cannot use. Here every edge is an exact intersection.
/// </summary>
public static class SolidFromRegions
{
    const int MaxRegions = 40, MaxCells = 400;

    /// <summary>The solid, or null when a region is free-form or the pieces do not add up to the scan: the caller then sews instead.</summary>
    public static Shape? Build(IReadOnlyList<FitRegion> regions, TriMesh scan)
    {
        if (regions.Count == 0 || regions.Count > MaxRegions) return null;
        if (regions.Any(r => r.Kind is not (PrimKind.Plane or PrimKind.Cylinder or PrimKind.Cone or PrimKind.Sphere))) return null;
        var min = Vec3.Zero + new Vec3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
        for (int i = 0; i < scan.VertexCount; i++) { var p = scan.P(i); min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z)); max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z)); }
        double diag = (max - min).Length, pad = diag * 0.1 + 1;
        var size = max - min + new Vec3(2 * pad, 2 * pad, 2 * pad);
        var cells = new List<Shape> { Occ.Translate(Occ.Translate(Occ.Box(size.X, size.Y, size.Z), new Vec3(size.X / 2, size.Y / 2, 0)), min - new Vec3(pad, pad, pad)) };
        double smallest = diag * diag * diag * 1e-9;

        try
        {
            foreach (var r in regions.OrderBy(r => r.Kind == PrimKind.Plane ? 0 : 1))
            {
                var tool = Tool(r, diag * 4);
                var next = new List<Shape>();
                foreach (var cell in cells)
                    foreach (var piece in new[] { Occ.Common(cell, tool), Occ.Cut(cell, tool) })
                        if (piece.HasSolids && piece.Props().Volume > smallest) next.Add(piece);
                cells = next;
                if (cells.Count == 0 || cells.Count > MaxCells) return null;
            }
            var inside = cells.Where(c => IsInside(c, scan, diag)).ToList();
            if (inside.Count == 0) return null;
            var solid = inside.Aggregate(Occ.Fuse);
            try { solid = Occ.UnifyFaces(solid, Enumerable.Range(0, solid.Count(SubKind.Face)).ToArray()); } catch (KernelException) { }
            double want = Math.Abs(scan.Volume());
            return solid.HasSolids && Math.Abs(solid.Props().Volume - want) < want * 0.03 ? solid : null;
        }
        catch (KernelException) { return null; }
    }

    /// <summary>The solid side of a fitted surface: a half-space behind a plane, or the filled shape of a cylinder, cone or sphere.</summary>
    static Shape Tool(FitRegion r, double reach)
    {
        Shape Along(Shape s)
        {
            var f = Xform.FromFrame(r.Origin, Xform.AnyPerpendicular(r.Axis), r.Axis);
            return Occ.Transform(s, f.R, f.T);
        }
        switch (r.Kind)
        {
            case PrimKind.Plane: return Along(Occ.Translate(Occ.Box(2 * reach, 2 * reach, reach), new Vec3(0, 0, -reach)));
            case PrimKind.Cylinder: return Along(Occ.Translate(Occ.Cylinder(r.Radius, 2 * reach), new Vec3(0, 0, -reach)));
            case PrimKind.Sphere: return Occ.Translate(Occ.Sphere(r.Radius), r.Origin);
            default:
            {
                double tan = Math.Tan(r.HalfAngle * Math.PI / 180);
                return Along(Occ.Cone(1e-6, reach * tan, reach));
            }
        }
    }

    /// <summary>Steps just inside the piece from points spread over its skin and lets those points vote, because the middle of a ring-shaped piece is not in the piece.</summary>
    static bool IsInside(Shape cell, TriMesh scan, double diag)
    {
        var m = cell.Tessellate(); int tris = m.Indices.Length / 3;
        var size = (m.Max - m.Min); double depth = Math.Min(diag * 0.01, Math.Min(size.X, Math.Min(size.Y, size.Z)) * 0.25);
        int stride = Math.Max(1, tris / 15), yes = 0, votes = 0;
        for (int t = 0; t < tris; t += stride)
        {
            int a = m.Indices[3 * t] * 3, b = m.Indices[3 * t + 1] * 3, c = m.Indices[3 * t + 2] * 3;
            Vec3 P(int i) => new(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
            var cross = Vec3.Cross(P(b) - P(a), P(c) - P(a));
            if (cross.Length < 1e-12) continue;
            votes++;
            if (MeshBoolean.Inside((P(a) + P(b) + P(c)) / 3 - cross.Normalized() * depth, scan)) yes++;
        }
        return votes > 0 && yes * 2 > votes;
    }
}
