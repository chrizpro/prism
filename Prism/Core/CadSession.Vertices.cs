using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>The Vertex pick level on solids, surfaces and curves: the corner points where their edges end.</summary>
public sealed partial class CadSession
{
    const double VertexTolerancePx = 9;
    const int MaxVertices = 4000;
    static readonly ConditionalWeakTable<MeshData, Vec3[]> VertexCache = new();

    static bool HasVertices(SceneObject o) => o.HasBrep && o.Tri == null && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Curve;

    /// <summary>The corner points of a body, in the kernel's vertex order. Empty for meshes, forms and very large bodies.</summary>
    public IReadOnlyList<Vec3> VertexPoints(SceneObject o)
    {
        if (!HasVertices(o)) return [];
        return VertexCache.GetValue(o.Mesh, _ =>
        {
            try
            {
                int n = o.Shape.Count(SubKind.Vertex);
                if (n > MaxVertices) return [];
                var points = new Vec3[n];
                for (int i = 0; i < n; i++) { using var v = o.Shape.Sub(SubKind.Vertex, i); points[i] = v.Props().Center; }
                return points;
            }
            catch (KernelException) { return []; }
        });
    }

    /// <summary>Where a selected vertex is, or null when it is gone.</summary>
    public Vec3? VertexPoint(SelItem item)
    {
        if (item.Kind != SelKind.Vertex || Find(item.Obj) is not { } o) return null;
        var points = VertexPoints(o);
        return item.Index >= 0 && item.Index < points.Count ? points[item.Index] : null;
    }

    /// <summary>Every corner point inside a drag region, leaving out the ones hidden behind a face unless Through is on.</summary>
    List<SelItem> VertexRegionItems(RegionSelect.Region region)
    {
        var found = new List<SelItem>(); var cam = Camera; var vp = cam.ViewProjection;
        var visible = Objects.Where(o => o.Visible && !HiddenIds.Contains(o.Id) && !o.IsAnnotation).ToList();
        foreach (var o in visible)
        {
            if (!HasVertices(o)) continue;
            var points = VertexPoints(o);
            for (int i = 0; i < points.Count; i++)
            {
                if (cam.Project(points[i], vp) is not { } q || !region.Contains(q.x, q.y)) continue;
                if (!XRay && Picking.RayFace(cam, visible, q.x, q.y, null, ShownFaces) is { } face && cam.Project(face.point, vp) is { } front && front.depth < q.depth - 2e-4) continue;
                found.Add(new SelItem(o.Id, SelKind.Vertex, i));
            }
        }
        return found;
    }

    /// <summary>The vertex nearest the pointer, unless a face in front hides it.</summary>
    PickResult? PickVertex(Camera cam, List<SceneObject> visible, double px, double py)
    {
        PickResult? best = null; double bestD = VertexTolerancePx;
        var vp = cam.ViewProjection;
        foreach (var o in visible)
        {
            if (!o.Visible || !HasVertices(o)) continue;
            var points = VertexPoints(o);
            for (int i = 0; i < points.Count; i++)
            {
                if (cam.Project(points[i], vp) is not { } q) continue;
                double d = Math.Sqrt((q.x - px) * (q.x - px) + (q.y - py) * (q.y - py));
                if (d < bestD) { bestD = d; best = new PickResult(new SelItem(o.Id, SelKind.Vertex, i), points[i], Vec3.UnitZ, q.depth); }
            }
        }
        if (best is not { } hit || XRay) return best;
        // hidden behind a face? a ray at the vertex itself must not hit anything clearly nearer
        if (cam.Project(hit.Point, vp) is { } at && Picking.RayFace(cam, visible, at.x, at.y, null, ShownFaces) is { } face
            && cam.Project(face.point, vp) is { } front && front.depth < hit.Depth - 2e-4) return null;
        return best;
    }

    /// <summary>Dots on the corners while the Vertex level is on, so it is clear what a click can take; selected and hovered ones stand out.</summary>
    void BuildVertexOverlay(Overlay o)
    {
        bool level = ActiveCommand == null && Sketch == null && Mode == SelMode.Vertex;
        if (level)
        {
            int shown = 0;
            foreach (var obj in Objects)
            {
                if (!obj.Visible || HiddenIds.Contains(obj.Id) || !ShownByIsolation(obj) || !HasVertices(obj)) continue;
                foreach (var p in VertexPoints(obj)) { if (++shown > MaxVertices) break; o.AddDot(p, 0xCCFFFFFF, 5); }
            }
        }
        foreach (var item in _selection) if (VertexPoint(item) is { } p) o.AddDot(p, 0xFFFF8A1F, 11);
        if (Hover is { Kind: SelKind.Vertex } hover && VertexPoint(hover) is { } hp) o.AddDot(hp, 0xFF35B6FF, 11);
    }
}
