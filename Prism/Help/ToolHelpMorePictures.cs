using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Help;

/// <summary>
/// Hover help for the buttons that had a text-only card or none at all before v0.4: the Mesh region and boundary tools, and the modeling
/// tools whose cards were written without a picture (Thread, Draft, Edit Distance, Edit Face, Duplicate, Knife, Draw on Surface, Insert
/// Point, Transform). Like the other pictures these are small illustrations built from kernel shapes and hand-made meshes.
/// </summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindMeshButton(string? key) => key switch
    {
        "mesh-patch|Fill Hole" => T(key, "Fill Hole", "Close one or more openings in a mesh with new triangles that follow the surface around them.",
            "Start: a scan with an opening", "Result: the opening filled",
            "1. Choose Fill Hole.\n2. Click the edge of each opening to fill (they turn orange).\n3. Check the preview, then press Fill.",
            "Use Repair to fill every small hole at once; Fill Hole is for the ones you pick.", "pic-mesh-fill"),
        "mesh-repair|Stitch Seam" => T(key, "Stitch Seam", "Join two open edges of a mesh across a small gap, so two pieces become one closed mesh.",
            "Start: two pieces with a gap", "Result: one mesh, the seam closed",
            "1. Choose Stitch Seam.\n2. Click the two boundary edges to join.\n3. Set the largest gap to bridge, check the preview, then press Stitch.",
            "Edges further apart than the gap are left open.", "pic-mesh-stitch"),
        "mesh-patch|Delete and Patch" => T(key, "Delete and Patch", "Remove a bad area of a mesh, such as a spike or a fused fragment, and fill the gap smoothly.",
            "Start: a scan with a bump", "Result: the area rebuilt smoothly",
            "1. Choose Delete and Patch.\n2. Select the triangles to remove (in Face mode, double-click takes a whole patch).\n3. Accept: the hole is filled to follow the surface around it.",
            "Select a little more than the defect so the patch blends in.", "pic-mesh-delete-patch"),
        "mesh-repair|Cleanup" => T(key, "Cleanup", "Tidy a mesh without filling its openings: merge duplicate vertices, drop stray fragments and broken triangles, and make the triangles face the same way.",
            "Start: a scan with stray fragments", "Result: one clean mesh, openings left as they were",
            "1. Choose Cleanup.\n2. Pick the mesh.\n3. Check the counts in the panel, then accept.",
            "Use Repair to fill the openings as well.", "pic-mesh-cleanup"),
        "mesh-select|Extract" => T(key, "Extract", "Copy the selected triangles of a mesh into a separate mesh, leaving the original as it was.",
            "Start: a region selected", "Result: the region as its own mesh",
            "1. Choose Extract.\n2. Select the triangles (in Face mode, double-click takes a whole patch).\n3. Accept: the copy appears as a new mesh.",
            "Fit a surface to the copy, or export it on its own.", "pic-mesh-extract"),
        "mesh-select|Edit Region" => T(key, "Edit Region", "Pick triangles on a mesh with smart patch, brush, rectangle or lasso, then smooth, delete and patch, or extract them.",
            "A mesh", "A region selected",
            "1. Choose Edit Region and pick a method in the panel.\n2. Click or drag on the mesh.\n3. Smooth, delete and patch, or extract the region.",
            "Face mode selection does the same for most tools: click a triangle, or double-click for its patch.", "mesh-select"),
        "form-face|Draw Faces" => T(key, "Draw Faces", "Draw new triangles and quads point by point and stitch them into a mesh, for gaps the automatic fill cannot close.",
            "Start: a sheet with a gap", "Result: the gap covered by drawn faces",
            "1. Choose Draw Faces.\n2. Click three or four points (they snap to the mesh's vertices) to make each face.\n3. Accept: the faces join the mesh.",
            "Faces drawn against an open edge are stitched to it.", "pic-mesh-draw"),
        "mesh-reconstruct|Reconstruct Mesh" => T(key, "Reconstruct Mesh", "Build a mesh from the imported point cloud: clean up, estimate normals, set the detail and trim.",
            "A point cloud", "A mesh",
            "1. Import a point cloud.\n2. Choose Reconstruct Mesh.\n3. Set the cleanup, detail and trim, then accept.",
            "", "mesh-reconstruct"),
        _ => null
    };

    static ToolHelpExample? MorePictures(ToolHelpTopic topic, string id)
    {
        string b = topic.BeforeLabel, a = topic.AfterLabel;
        var own = new List<Shape>();
        Shape K(Shape s) { own.Add(s); return s; }
        try
        {
            switch (id)
            {
                // ---- mesh ----
                case "pic-mesh-fill":
                {
                    var after = Ball(12, 24, 16);
                    return new(Src(DropWhere(Ball(12, 24, 16), c => c.Z > 8), b), Src(after, a, null, [Patch(after, c => c.Z > 8)], Blue));
                }
                case "pic-mesh-stitch":
                {
                    var lower = DropWhere(Ball(12, 24, 16), c => c.Z > 0);
                    var upper = Shift(DropWhere(Ball(12, 24, 16), c => c.Z < 0), new(0, 0, 3));
                    var after = Ball(12, 24, 16);
                    return new(Src(Merge(lower, upper), b), Src(after, a, null, [Patch(after, c => Math.Abs(c.Z) < 1.7)], Blue));
                }
                case "pic-mesh-cleanup":
                {
                    var bits = Merge(Shift(Ball(2, 6, 4), new(17, 4, 6)), Shift(Ball(1.5, 6, 4), new(-15, -9, -4)), Shift(Ball(1.2, 6, 4), new(4, 16, 9)));
                    var before = Merge(DropWhere(Ball(12, 24, 16), c => c.Z > 10), bits);
                    return new(Src(before, b, null, Enumerable.Range(1, 3).ToArray(), Red), Src(DropWhere(Ball(12, 24, 16), c => c.Z > 10), a));
                }
                case "pic-mesh-delete-patch":
                {
                    var before = Ball(12, 24, 16, d => d.Z > 0.8 ? 3.5 * Math.Sin(d.X * 9) * Math.Cos(d.Y * 9) : 0);
                    var after = Ball(12, 24, 16);
                    return new(Src(before, b, null, [Patch(before, c => c.Z > 9)], Blue), Src(after, a, null, [Patch(after, c => c.Z > 9)], Blue));
                }
                case "pic-mesh-extract":
                {
                    var before = Ball(12, 24, 16);
                    var after = Merge(Ball(12, 24, 16), Shift(DropWhere(Ball(12, 24, 16), c => c.Z <= 6), new(0, 0, 8)));
                    return new(Src(before, b, null, [Patch(before, c => c.Z > 6)], Blue), Src(after, a, null, [Patch(after, c => c.Z > 13)], Blue));
                }
                case "pic-mesh-draw":
                {
                    static double Wave(double x, double y) => 2 * Math.Sin(x / 7) * Math.Cos(y / 8);
                    static bool Gap(Vec3 c) => Math.Abs(c.X - 3) < 6 && Math.Abs(c.Y) < 5;
                    var after = SheetMesh(Wave, 0);
                    return new(Src(DropWhere(SheetMesh(Wave, 0), Gap), b), Src(after, a, null, [Patch(after, Gap)], Blue));
                }

                // ---- solid and direct modeling ----
                case "pic-thread":
                {
                    var pin = K(Occ.Cylinder(6, 24));
                    var groove = K(Mv(Occ.ThreadGrooveExternal(12, 1.75, 16), 0, 0, 8));
                    return new(Plain(pin, b), Plain(K(Occ.Cut(pin, groove)), a));
                }
                case "pic-draft-face":
                {
                    var box = K(Occ.Box(30, 22, 18));
                    var sides = Enumerable.Range(0, box.Count(SubKind.Face)).Where(i => { using var f = box.Sub(SubKind.Face, i); var p = f.Props(); return p.Size.Z > 17.9; }).ToArray();
                    var drafted = K(Occ.Draft(box, sides, Vec3.UnitZ, 8, Vec3.Zero, Vec3.UnitZ));
                    return new(Plain(box, b), Plain(drafted, a));
                }
                case "pic-edit-distance":
                {
                    var plate = K(Occ.Box(44, 22, 10));
                    var before = K(Occ.Cut(plate, K(Mv(Occ.Cylinder(4, 12), -12, 0, -1))));
                    var after = K(Occ.Cut(plate, K(Mv(Occ.Cylinder(4, 12), 8, 0, -1))));
                    return new(Plain(before, b), Plain(after, a));
                }
                case "pic-edit-face":
                {
                    static double Bump(Vec3 p) => 5 * Math.Cos(p.X / 30 * Math.PI) * Math.Cos(p.Y / 22 * Math.PI);
                    var flat = GridBox(30, 22, 12, 6, 4, 2, true);
                    var shaped = Renormal(Deform(GridBox(30, 22, 12, 6, 4, 2, true), p => p.Z > 11.99 ? p with { Z = p.Z + Bump(p) } : p));
                    return new(Src(flat, b, null, [Patch(flat, c => c.Z > 11.9)], Blue), Src(shaped, a, null, [Patch(shaped, c => c.Z > 11.9)], Blue));
                }
                case "pic-duplicate":
                {
                    var part = K(Occ.Fuse(K(Occ.Box(16, 16, 8)), K(Mv(Occ.Cylinder(4, 14), 0, 0, 8))));
                    return new(Plain(part, b), Plain(K(Occ.Compound([part, K(Mv(Occ.Translate(part, Vec3.Zero), 24, 0, 0))])), a));
                }
                case "pic-knife":
                {
                    var form = K(Occ.Box(24, 24, 24));
                    var plane = K(Occ.Rotate(K(Mv(Occ.Box(40, 40, 0.3), 0, 0, 12)), new(0, 0, 12), Vec3.UnitX, 25));
                    var halves = K(Occ.Split(form, plane));
                    return new(Plain(form, b), Marked(halves, plane, a));
                }
                case "pic-draw-on-surface":
                {
                    var ball = K(Occ.Sphere(12));
                    var path = Enumerable.Range(0, 9).Select(i => { double t = -1.1 + i * 0.275; return new Vec3(Math.Sin(t), -0.55, Math.Cos(t) * 0.9 + 0.2).Normalized() * 12.05; }).ToList();
                    return new(Plain(ball, b), Marked(ball, K(Occ.Spline(path)), a));
                }
                case "pic-insert-point":
                {
                    var line = K(Occ.Polyline([new(-16, 0, 0), new(16, 0, 0), new(16, 14, 0)], false));
                    var split = K(Occ.Compound([K(Occ.Line(new(-16, 0, 0), new(0, 0, 0))), K(Occ.Polyline([new(0, 0, 0), new(16, 0, 0), new(16, 14, 0)], false)), K(Dots(new Vec3(0, 0, 0)))]));
                    return new(Lines(line, b), Lines(split, a));
                }
                case "pic-transform":
                {
                    var square = K(Occ.Polyline([new(-8, -8, 0), new(8, -8, 0), new(8, 8, 0), new(-8, 8, 0)], true));
                    var turned = K(Mv(Occ.Rotate(square, Vec3.Zero, Vec3.UnitZ, 30), 14, 6, 0));
                    return new(Lines(square, b), Lines(K(Occ.Compound([square, turned])), a));
                }
                default:
                    return null;
            }
        }
        finally { foreach (var shape in own) shape.Dispose(); }
    }
}
