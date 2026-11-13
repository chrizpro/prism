using System;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Help for the Analyze tab, selection modes and the transform tools, shared by the CAE and CAM workspaces.</summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindAnalyze(string? icon) => icon switch
    {
        "measure" => Topic(icon, "Measure", "Read sizes, distances and angles between faces, edges, bodies or points.", "Start: select two faces", "Result: the distance between them",
            "1. Choose Measure.\n2. Click two faces, edges or points.\n3. Read the distance or angle.",
            "Select two parallel faces for a thickness; two edges for a distance or angle."),
        "properties" => Topic(icon, "Properties", "Get volume, mass, center of mass and moments of inertia.", "Start: a body", "Result: center of mass marked",
            "1. Choose Properties.\n2. Select the body.\n3. Read the values in the panel.",
            "Assign a material first so mass uses the real density."),
        "section" => Topic(icon, "Section Analysis", "Cut the model with a plane to see inside and read the section area.", "Start: a block with an internal channel", "Result: cut open to show the inside",
            "1. Choose Section Analysis.\n2. Pick a plane and its position.\n3. Read the section area, or clear the section.",
            "Move the plane to inspect wall thickness and hidden cavities."),
        "bbox" => Topic(icon, "Bounding Box", "Show the overall size box of the selection.", "Start: a part", "Result: the box around it",
            "1. Select a part, or leave nothing selected for all.\n2. Turn on Bounding Box.\n3. Read its size.",
            "Use it to check whether the part fits the stock or the build volume."),
        "interference" => Topic(icon, "Interference", "Find where solids overlap.", "Start: two overlapping bodies", "Result: the overlapping volume",
            "1. Choose Interference.\n2. Select the bodies to compare.\n3. Review the overlapping volumes.",
            "Small overlaps can hide in fits; check mating parts after every change."),
        "curvature" => Topic(icon, "Curvature Map", "Color surfaces by how sharply they bend.", "Start: a shape", "Result: colored by curvature",
            "1. Choose Curvature Map.\n2. Select the faces.\n3. Read the colors; clear when done.",
            "Flat areas stay blue; tight bends turn red. Smooth color changes mean a smooth surface."),
        "zebra" => Topic(icon, "Zebra Stripes", "Show reflection stripes to judge how smooth a surface is.", "Start: a smooth shape", "Result: reflection stripes",
            "1. Turn on Zebra Stripes.\n2. Rotate the model.\n3. Look for kinks or breaks in the stripes.",
            "Smooth, continuous stripes mean a smooth surface. A kink or break shows a sudden change."),
        "draft" => Topic(icon, "Draft Analysis", "Check faces for mold release in a pull direction.", "Start: a part with straight walls", "Result: walls with no draft in red",
            "1. Choose Draft Analysis.\n2. Pick the pull direction.\n3. Check the colors and fix red areas.",
            "A few degrees of draft helps the part leave the mold. Straight walls show as red."),
        "drawing" => Topic(icon, "Make Drawing", "Create an engineering drawing as PDF, SVG or DXF.", "Start: the 3D part", "Result: flat views laid out on a sheet",
            "1. Choose Make Drawing.\n2. Select bodies and views.\n3. Choose the format and export.",
            "Add dimensions, sections and a title block in the options before exporting."),
        "clear-analysis" => Topic(icon, "Clear Results", "Remove result and analysis colors and show the normal model.", "Start: a colored result", "Result: the normal model",
            "1. Choose Clear Results.\n2. The model returns to normal shading.\n3. Run the study or analysis again whenever you like.",
            "Clearing only hides the colors; it does not delete the study."),
        "sel-object" => Topic(icon, "Select Object", "Pick whole bodies, surfaces and curves.", "Start: a body", "Result: the whole body selected",
            "1. Choose Object.\n2. Click a body in the viewport.\n3. Hold Ctrl to add more.",
            "Use it to move, hide or analyze a whole part."),
        "sel-face" => Topic(icon, "Select Face", "Pick individual faces.", "Start: a body", "Result: one face selected",
            "1. Choose Face.\n2. Click a face in the viewport.\n3. Hold Ctrl to add more.",
            "Faces are what supports, loads, holes and fillets act on."),
        "sel-edge" => Topic(icon, "Select Edge", "Pick individual edges.", "Start: a body", "Result: one edge selected",
            "1. Choose Edge.\n2. Click an edge in the viewport.\n3. Hold Ctrl to add more.",
            "Edges are what Fillet and Chamfer round and bevel."),
        "sel-vertex" => Topic(icon, "Select Vertex", "Pick individual corner points.", "Start: a body", "Result: one corner selected",
            "1. Choose Vertex.\n2. Click a corner in the viewport.\n3. Hold Ctrl to add more.",
            "Use it for measuring to a corner or editing freeform cage points."),
        "sel-rect" => Topic(icon, "Rectangle Select", "Drag a rectangle to select everything inside it.", "Start: a body", "Result: what the rectangle covered is selected",
            "1. Choose Rectangle.\n2. Drag across the view. Left to right takes only what is completely inside; right to left takes whatever it touches.\n3. Hold Shift to add, Ctrl to take away.",
            "Turn on Through to also take faces and edges hidden behind others."),
        "sel-lasso" => Topic(icon, "Lasso Select", "Draw around what you want to select.", "Start: a body", "Result: what the loop touched is selected",
            "1. Choose Lasso.\n2. Draw a loop around the faces, edges or bodies you want.\n3. Hold Shift to add, Ctrl to take away.",
            "A lasso follows odd shapes better than a rectangle. Turn on Through to reach hidden faces too."),
        "sel-invert" => Topic(icon, "Invert Selection", "Select everything that is not selected.", "Start: a few faces selected", "Result: all the other faces selected",
            "1. Select the faces you want to leave out.\n2. Choose Invert.\n3. The rest of the body is selected instead.",
            "With nothing selected, Invert selects everything. Faces and edges invert within the bodies you picked from."),
        "sel-same-color" => Topic(icon, "Select Same Color", "Add every face that looks like a selected face.", "Start: one colored face selected", "Result: all faces of the body with the same color and finish selected",
            "1. Select a face.\n2. Choose Same Color.\n3. Every face of that body with the same color and finish joins the selection.",
            "Colors are those set per face in the Appearance studio; faces that follow the body color match each other."),
        "sel-same-adjacent" => Topic(icon, "Select Adjacent Faces", "Add the faces that share an edge with the selected faces.", "Start: one face selected", "Result: the faces around it selected too",
            "1. Select a face or several.\n2. Choose Adjacent.\n3. Faces touching the selection along an edge join it. Repeat to grow further.",
            "Hidden faces are skipped."),
        "sel-same-linked" => Topic(icon, "Select Linked Faces", "Add every face of the body a selected face belongs to.", "Start: one face selected", "Result: all faces of that body selected",
            "1. Select a face.\n2. Choose Linked.\n3. Every face of the same body or surface is selected.",
            "Hidden faces are skipped."),
        "move" => Topic(icon, "Move", "Slide objects to a new position.", "Start: a gap beside the post", "Result: moved to touch the post",
            "1. Select the objects.\n2. Drag an arrow, or type a distance.\n3. Check the preview, then accept.",
            "Type exact distances for precise placement. Snaps help line faces up."),
        "rotate" => Topic(icon, "Rotate", "Turn objects around an axis.", "Start: the part's orientation", "Result: the part turned",
            "1. Select the objects.\n2. Choose the axis and angle.\n3. Check the preview, then accept.",
            "Use it to lay a part flat for printing, or to turn it into the setup you need."),
        "scale" => Topic(icon, "Scale", "Resize objects uniformly.", "Start: the original size", "Result: scaled",
            "1. Select the objects.\n2. Enter the scale factor.\n3. Check the preview, then accept.",
            "Scaling changes every dimension. Use it for tabletop models, not to fix a single measurement."),
        "mesh-cut" => Topic(icon, "Split Mesh", "Cut a mesh with a plane and cap the halves.", "Start: a part too big for the bed", "Result: two pieces that fit",
            "1. Select the mesh.\n2. Position the cutting plane.\n3. Check the preview, then accept.",
            "Cut where the pieces are easy to glue, and keep each half inside your build volume."),
        "mesh-repair" => Topic(icon, "Repair Mesh", "Merge duplicate vertices, remove junk triangles, fix flipped normals and fill holes.", "Start: a scan with stray fragments", "Result: one clean, closed body",
            "1. Select the mesh.\n2. Choose what to fix.\n3. Check the before and after counts, then accept.",
            "Run Repair before Print Check or any slicing step; open or inverted meshes are the usual cause of failed prints."),
        "mesh-quality" => Topic(icon, "Mesh Quality", "Find thin or stretched triangles, open and non-manifold edges and self-intersections.", "Start: a mesh with hidden defects", "Result: problem areas marked",
            "1. Select the mesh.\n2. Choose which defects to show and an optional heat map.\n3. Fix what is marked, then check again.",
            "Highlights stay on screen after the command closes; use Clear Colors to remove them."),
        "mesh-overhang" => Topic(icon, "Overhang & Orientation", "Find what needs supports and the best way to lay the part down.", "Start: a part that needs a lot of support", "Result: laid down with fewer supports",
            "1. Select the mesh.\n2. Set the largest overhang your printer can bridge.\n3. Try the suggested orientations.",
            "Fewer supports often means a rougher bed face; weigh surface finish against support removal."),
        "mesh-thickness" => Topic(icon, "Wall Thickness", "Color the part by wall thickness to find walls that are too thin to print.", "Start: a part with thin and thick walls", "Result: thin areas show in color",
            "1. Select the mesh.\n2. Set the thinnest wall your printer can make.\n3. Thicken or redesign the colored areas.",
            "The minimum printable wall depends on nozzle size or resin; check your printer's limit."),
        "mesh-remesh" => Topic(icon, "Remesh", "Rebuild a mesh with evenly sized triangles.", "Start: uneven, stretched triangles", "Result: even triangles",
            "1. Select the mesh.\n2. Set the target edge length.\n3. Choose whether to keep sharp and open edges, then accept.",
            "Pick an edge length near the smallest detail you need to keep."),
        "mesh-boolean" => Topic(icon, "Mesh Boolean", "Combine two closed meshes: join, subtract or keep the overlap.", "Start: two overlapping meshes", "Result: one combined mesh",
            "1. Select the first and second mesh.\n2. Choose join, subtract or intersect.\n3. Check the preview, then accept.",
            "Both meshes must be closed; run Repair first if you are told one is open."),
        _ => null
    };

    static ToolHelpExample? ExampleAnalyze(ToolHelpTopic topic)
    {
        string key = topic.Icon;
        switch (key)
        {
            case "measure":
            {
                using var body = Occ.Box(30, 20, 14);
                var mesh = body.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 0, -15).Concat(FacesAt(mesh, 0, 15)).ToArray(), Blue));
            }
            case "properties":
            {
                // An L-bracket: its center of mass sits in the empty corner, so the marker is visible.
                using var foot = Occ.Box(30, 12, 8);
                using var wall = Mv(Occ.Box(8, 12, 26), -11, 0, 0);
                using var bracket = Occ.Fuse(foot, wall);
                using var center = Mv(Occ.Sphere(2.2), -2, 0, 11);
                using var both = Occ.Compound([bracket, center]);
                var plain = bracket.Tessellate(); var mesh = both.Tessellate();
                return new(Src(plain, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, Enumerable.Range(plain.FaceCount, mesh.FaceCount - plain.FaceCount).ToArray(), Red));
            }
            case "section":
            {
                using var block = Occ.Box(30, 30, 20);
                using var channel = Mv(Occ.Rotate(Occ.Cylinder(5, 40), new(0, 0, 0), new(0, 1, 0), 90), -20, 0, 10);
                using var part = Occ.Cut(block, channel);
                using var cutter = Mv(Occ.Box(40, 20, 30), 0, 10, -5);
                using var cut = Occ.Cut(part, cutter);
                return new(Src(part.Tessellate(), topic.BeforeLabel), Src(cut.Tessellate(), topic.AfterLabel));
            }
            case "bbox":
            {
                using var part = Mv(Occ.Cylinder(7, 12), 3, 2, 0);
                using var frame = Paths(Rect(-10, -9, 10, 9, 0), Rect(-10, -9, 10, 9, 12), [new(-10, -9, 0), new(-10, -9, 12)], [new(10, -9, 0), new(10, -9, 12)], [new(10, 9, 0), new(10, 9, 12)], [new(-10, 9, 0), new(-10, 9, 12)]);
                using var both = Occ.Compound([part, frame]);
                var mesh = both.Tessellate();
                return new(Src(part.Tessellate(), topic.BeforeLabel), Src(mesh, topic.AfterLabel, AllEdges(mesh).Where(e => e >= 3).ToArray(), null, Blue));
            }
            case "interference":
            {
                using var a = Occ.Box(24, 20, 14);
                using var b = Mv(Occ.Box(24, 20, 14), 14, 6, 4);
                using var both = Occ.Compound([a, b]);
                using var overlap = Occ.Common(a, b);
                var mesh = both.Tessellate(); var shared = overlap.Tessellate();
                return new(Src(mesh, topic.BeforeLabel, null, Enumerable.Range(6, 6).ToArray(), Blue), Src(shared, topic.AfterLabel, null, AllFaces(shared), null, true));
            }
            case "curvature":
            {
                using var body = Occ.Fuse(Occ.Box(24, 16, 10), Mv(Occ.Sphere(8), 0, 0, 10));
                var plain = body.Tessellate();
                var colored = Colorize(body.Tessellate(0.15), (p, n) => p.Z > 10.01 ? .1 + .7 * (p.Z - 10) / 8 : .1);
                return new(Src(plain, topic.BeforeLabel), Src(colored, topic.AfterLabel));
            }
            case "zebra":
            {
                using var ball = Occ.Sphere(12);
                var plain = ball.Tessellate();
                var stripes = Colorize(ball.Tessellate(0.03), (p, n) => Math.Sin(n.X * 9 + n.Z * 5) > 0 ? 1 : 0);
                return new(Src(plain, topic.BeforeLabel), Src(stripes, topic.AfterLabel, null, null, null, false, true));
            }
            case "draft":
            {
                var plain = GridBox(24, 16, 12, 1, 1, 1);
                var colored = Colorize(GridBox(24, 16, 12, 1, 1, 1), (p, n) => Math.Abs(n.Z) > .5 ? 0 : 1);
                return new(Src(plain, topic.BeforeLabel), Src(colored, topic.AfterLabel));
            }
            case "drawing":
            {
                using var body = Occ.Fuse(Occ.Box(20, 12, 8), Mv(Occ.Box(8, 12, 6), -6, 0, 8));
                using var views = Paths(Rect(-10, -4, 10, 4, 0), Rect(-10, 8, 10, 20, 0), Rect(14, -4, 26, 4, 0), Rect(-10, -14, -2, -10, 0));
                return FromShapes(body, views, topic, pathAfter: true);
            }
            case "clear-analysis":
            {
                var colored = Colorize(GridBox(40, 14, 10, 10, 3, 2), (p, n) => 1 - (p.X + 20) / 40);
                return new(Src(colored, topic.BeforeLabel), Src(GridBox(40, 14, 10, 10, 3, 2), topic.AfterLabel));
            }
            case "sel-rect" or "sel-lasso" or "sel-invert":
            {
                using var body = Occ.Box(30, 20, 14);
                var mesh = body.Tessellate();
                var some = FacesAt(mesh, 2, 14);
                return new(Src(mesh, topic.BeforeLabel), key switch
                {
                    "sel-invert" => Src(mesh, topic.AfterLabel, null, Enumerable.Range(0, mesh.FaceCount).Except(some).ToArray(), Blue),
                    "sel-rect" => Src(mesh, topic.AfterLabel, null, AllFaces(mesh), Blue),
                    _ => Src(mesh, topic.AfterLabel, null, some, Blue)
                });
            }
            case "sel-same-color" or "sel-same-adjacent" or "sel-same-linked":
            {
                using var body = Occ.Box(30, 20, 14);
                var mesh = body.Tessellate();
                var one = FacesAt(mesh, 2, 14);
                var after = key switch
                {
                    "sel-same-linked" => AllFaces(mesh),
                    "sel-same-adjacent" => Enumerable.Range(0, mesh.FaceCount).Where(f => !FacesAt(mesh, 2, 0).Contains(f)).ToArray(),
                    _ => one.Concat(FacesAt(mesh, 2, 0)).ToArray()
                };
                return new(Src(mesh, topic.BeforeLabel, null, one, Blue), Src(mesh, topic.AfterLabel, null, after, Blue));
            }
            case "sel-object" or "sel-face" or "sel-edge" or "sel-vertex":
            {
                using var body = Occ.Box(30, 20, 14);
                var mesh = body.Tessellate();
                if (key == "sel-vertex")
                {
                    using var corner = Mv(Occ.Sphere(2.2), 15, 10, 14);
                    using var both = Occ.Compound([body, corner]);
                    var marked = both.Tessellate();
                    return new(Src(mesh, topic.BeforeLabel), Src(marked, topic.AfterLabel, null, Enumerable.Range(6, marked.FaceCount - 6).ToArray(), Blue));
                }
                return new(Src(mesh, topic.BeforeLabel), key switch
                {
                    "sel-object" => Src(mesh, topic.AfterLabel, null, AllFaces(mesh), Blue),
                    "sel-face" => Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 2, 14), Blue),
                    _ => Src(mesh, topic.AfterLabel, [0], null, Blue)
                });
            }
            case "move":
            {
                using var post = Mv(Occ.Cylinder(4, 16), -22, 0, 0);
                using var apart = Join([Occ.Translate(post, new(0, 0, 0)), Mv(Occ.Box(14, 14, 10), 10, 0, 0)]);
                using var together = Join([Occ.Translate(post, new(0, 0, 0)), Mv(Occ.Box(14, 14, 10), -14, 0, 0)]);
                return new(Src(apart.Tessellate(), topic.BeforeLabel), Src(together.Tessellate(), topic.AfterLabel));
            }
            case "rotate":
            {
                using var bar = Occ.Box(30, 10, 8);
                using var turned = Occ.Rotate(bar, new(0, 0, 0), new(0, 0, 1), 40);
                using var mark = Mv(Occ.Box(2, 2, 2), 22, 0, 0);
                using var before = Join([Occ.Translate(bar, new(0, 0, 0)), Occ.Translate(mark, new(0, 0, 0))]);
                using var after = Join([Occ.Translate(turned, new(0, 0, 0)), Occ.Translate(mark, new(0, 0, 0))]);
                return new(Src(before.Tessellate(), topic.BeforeLabel), Src(after.Tessellate(), topic.AfterLabel));
            }
            case "scale":
            {
                using var reference = Mv(Occ.Box(4, 4, 4), -22, 0, 0);
                using var small = Join([Occ.Translate(reference, new(0, 0, 0)), Mv(Occ.Box(12, 12, 12), 4, 0, 0)]);
                using var large = Join([Occ.Translate(reference, new(0, 0, 0)), Mv(Occ.Box(20, 20, 20), 8, 0, 0)]);
                return new(Src(small.Tessellate(), topic.BeforeLabel), Src(large.Tessellate(), topic.AfterLabel));
            }
            case "mesh-cut":
            {
                using var whole = Occ.Box(40, 20, 12);
                using var halves = Join([Mv(Occ.Box(19, 20, 12), -11.5, 0, 0), Mv(Occ.Box(19, 20, 12), 11.5, 0, 0)]);
                return new(Src(whole.Tessellate(), topic.BeforeLabel), Src(halves.Tessellate(), topic.AfterLabel));
            }
            case "mesh-repair":
            {
                using var junk = Join([Occ.Box(30, 20, 12), Mv(Occ.Box(3, 3, 3), 22, 0, 0)]);
                using var clean = Occ.Box(30, 20, 12);
                return new(Src(junk.Tessellate(), topic.BeforeLabel), Src(clean.Tessellate(), topic.AfterLabel));
            }
            case "mesh-quality":
            {
                using var body = Occ.Box(30, 20, 12);
                var mesh = body.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 0, 15).ToArray(), Blue));
            }
            case "mesh-overhang":
            {
                using var flat = Join([Occ.Box(30, 10, 6), Mv(Occ.Box(6, 10, 18), 12, 0, 12)]);
                using var upright = Join([Occ.Box(10, 6, 30), Mv(Occ.Box(10, 18, 6), 0, 12, 12)]);
                return new(Src(flat.Tessellate(), topic.BeforeLabel), Src(upright.Tessellate(), topic.AfterLabel));
            }
            case "mesh-thickness":
            {
                using var body = Join([Occ.Box(30, 20, 4), Mv(Occ.Box(30, 20, 2), 0, 0, 12)]);
                var mesh = body.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 2, 13).ToArray(), Blue));
            }
            case "mesh-remesh":
            {
                using var body = Occ.Box(30, 20, 12);
                var mesh = body.Tessellate();
                return new(Src(mesh, topic.BeforeLabel, AllEdges(mesh)), Src(mesh, topic.AfterLabel));
            }
            case "mesh-boolean":
            {
                using var apart = Join([Mv(Occ.Box(20, 20, 12), -8, 0, 0), Mv(Occ.Box(20, 20, 12), 8, 8, 0)]);
                using var merged = Join([Mv(Occ.Box(20, 20, 12), -8, 0, 0), Mv(Occ.Box(20, 20, 12), 8, 8, 0), Mv(Occ.Box(16, 16, 12), 0, 4, 0)]);
                return new(Src(apart.Tessellate(), topic.BeforeLabel), Src(merged.Tessellate(), topic.AfterLabel));
            }
            default: return null;
        }
    }
}
