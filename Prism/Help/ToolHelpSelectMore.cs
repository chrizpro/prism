using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Hover cards, with before and after pictures, for the Select group's More tools (owner request 2026-11-24).</summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindSelectMore(string? icon) => icon switch
    {
        "sel-grow" => Topic(icon, "Grow", "Add the faces, edges, corners or mesh triangles next to the selection. Ctrl++.", "Start: the top face", "Result: the faces around it added",
            "1. Select a face, edge, corner or triangles.\n2. Choose More > Grow, or press Ctrl++.\n3. Repeat to grow further.", "In Edit Freeform it grows the cage selection."),
        "sel-shrink" => Topic(icon, "Shrink", "Drop the outer ring of the selection. Ctrl+-.", "Start: the top and the sides", "Result: only the top left",
            "1. Select a region of faces, edges, corners or triangles.\n2. Choose More > Shrink, or press Ctrl+-.", "A closed run of edges has no free ends, so it does not shrink."),
        "sel-tangent" => Topic(icon, "Tangent Chain", "Add every face joined smoothly to a selected face, or an edge's tangent chain.", "Start: a fillet", "Result: the fillet and the faces it blends into",
            "1. Select a face or edge.\n2. Choose More > Tangent Chain.", "Double-clicking a face or edge does the same in one step."),
        "sel-feature" => Topic(icon, "Whole Feature", "Add every face of the recognized feature a selected face belongs to.", "Start: one wall of a counterbore", "Result: the whole counterbore",
            "1. Select one face of a hole, boss, slot, pocket, fillet, chamfer, rib or text.\n2. Choose More > Whole Feature.", "Works on solids, where features are recognized."),
        "sel-same-feature" => Topic(icon, "Same Feature", "Add every feature of the same kind and size on the body.", "Start: one small hole", "Result: every hole of that size",
            "1. Select a face of a feature, for example one 5 mm hole.\n2. Choose More > Same Feature: every 5 mm hole is selected, the bigger one is not.", "Use it before Edit Feature or Delete to change them all at once."),
        "sel-same-radius" => Topic(icon, "Same Radius", "Add the round faces, or circular edges, with the same radius.", "Start: a 3 mm hole", "Result: the hole and the 3 mm fillet",
            "1. Select a round face or a circular edge.\n2. Choose More > Same Radius.", "Holes, bosses and fillets match alike, within 0.001 mm."),
        "sel-fillets" => Topic(icon, "Fillets Up To", "Add every fillet no larger than the selected round face.", "Start: the 3 mm corner round", "Result: the 2 mm and 3 mm rounds, not the 6 mm one",
            "1. Select the largest fillet you want to take.\n2. Choose More > Fillets Up To: every smaller or equal fillet joins it.", "Handy for removing small rounds from an imported part before simulation."),
        "sel-coplanar" => Topic(icon, "Coplanar", "Add the flat faces lying in the same plane and facing the same way.", "Start: one top", "Result: every top at that height",
            "1. Select a flat face.\n2. Choose More > Coplanar.", "Looks across every visible body, so the feet of an assembly line up."),
        "sel-coaxial" => Topic(icon, "Coaxial", "Add the round faces that share the selected face's axis.", "Start: the hole", "Result: the hole and the pin on its axis",
            "1. Select a cylinder, cone or torus face.\n2. Choose More > Coaxial.", "Looks across every visible body."),
        "sel-to-faces" => Topic(icon, "To Faces", "Switch to Face and select the faces beside the selected edges or corners.", "Start: one edge", "Result: the two faces it joins",
            "1. Select edges or corners.\n2. Choose More > To Faces.", "The pick level changes to Face."),
        "sel-to-edges" => Topic(icon, "To Edges", "Switch to Edge and select the border of the selected faces.", "Start: the top face", "Result: the edges around it",
            "1. Select faces.\n2. Choose More > To Edges.", "When the faces close the whole body, every edge of them is taken."),
        "sel-to-vertices" => Topic(icon, "To Vertices", "Switch to Vertex and select the corners of the selected faces or edges.", "Start: the top face", "Result: its four corners",
            "1. Select faces or edges.\n2. Choose More > To Vertices.", "The pick level changes to Vertex."),
        "sel-reselect" => Topic(icon, "Reselect Last", "Put back the selection you had before the last change. Ctrl+Shift+L.", "Start: the selection lost to a stray click", "Result: the faces selected again",
            "1. Lose a selection, for example by clicking empty space.\n2. Choose More > Reselect Last.", "Choose it again to swap back."),
        "sel-save-set" => Topic(icon, "Save Selection Set", "Save the selection under a name, kept with the document.", "Start: faces saved as Selection 1, then cleared", "Result: its browser row clicked, the faces are back",
            "1. Select faces, edges, corners or objects.\n2. Choose More > Save Selection Set.\n3. Click its row under Selection Sets in the browser to select it again.", "F2 renames a set; Delete removes the set, never the geometry."),
        _ => null
    };

    /// <summary>The faces of a shape that pass the test (by type and radius, and centre); indices match its tessellation.</summary>
    static int[] FacesOf(Shape shape, Func<FaceInfo, Vec3, bool> test)
    {
        var found = new List<int>();
        for (int i = 0; i < shape.Count(SubKind.Face); i++) { using var f = shape.Sub(SubKind.Face, i); if (test(Occ.FaceInfoOf(f), f.Props().Center)) found.Add(i); }
        return [.. found];
    }

    static int[] EdgesOf(Shape shape, Func<EdgeInfo, bool> test)
    {
        var found = new List<int>();
        for (int i = 0; i < shape.Count(SubKind.Edge); i++) { using var e = shape.Sub(SubKind.Edge, i); if (test(Occ.EdgeInfoOf(e))) found.Add(i); }
        return [.. found];
    }

    static bool Near(double a, double b) => Math.Abs(a - b) < 1e-4;
    static bool Round(FaceInfo f, double r) => f.Type is "Cylinder" or "Cone" or "Sphere" ? Near(f.R1, r) : f.Type == "Torus" && Near(f.R2, r);

    /// <summary>The 30 x 20 x 14 block every picture starts from (x -15..15, y -10..10, z 0..14).</summary>
    static Shape Block() => Occ.Box(30, 20, 14);

    static Shape Filleted(Shape s, Func<EdgeInfo, bool> edge, double r) { using (s) return Occ.Fillet(s, EdgesOf(s, edge), r); }
    static Shape Drilled(Shape s, double x, double y, double r, double z0 = -5, double depth = 30) { using (s) { using var c = Mv(Occ.Cylinder(r, depth), x, y, z0); return Occ.Cut(s, c); } }
    static bool Vertical(EdgeInfo e, double x, double y) => e.Type == "Line" && Near(e.Start.X, x) && Near(e.End.X, x) && Near(e.Start.Y, y) && Near(e.End.Y, y);

    static ToolHelpExample? ExampleSelectMore(ToolHelpTopic topic)
    {
        string key = topic.Icon;
        StudioPreviewSource Pick(Shape shape, string label, int[]? faces, int[]? edges = null) => Src(shape.Tessellate(), label, edges, faces, Blue);
        switch (key)
        {
            case "sel-grow" or "sel-shrink":
            {
                using var body = Block();
                var top = FacesOf(body, (f, c) => f.Type == "Plane" && Near(c.Z, 14));
                var around = FacesOf(body, (f, c) => !Near(c.Z, 0));
                return key == "sel-grow" ? new(Pick(body, topic.BeforeLabel, top), Pick(body, topic.AfterLabel, around))
                                         : new(Pick(body, topic.BeforeLabel, around), Pick(body, topic.AfterLabel, top));
            }
            case "sel-tangent":
            {
                using var body = Filleted(Block(), e => e.Type == "Line" && Near(e.Start.Y, -10) && Near(e.End.Y, -10) && Near(e.Start.Z, 14) && Near(e.End.Z, 14), 4);
                var fillet = FacesOf(body, (f, _) => f.Type == "Cylinder");
                var blended = FacesOf(body, (f, c) => f.Type == "Cylinder" || f.Type == "Plane" && (Near(c.Z, 14) || Near(c.Y, -10)));
                return new(Pick(body, topic.BeforeLabel, fillet), Pick(body, topic.AfterLabel, blended));
            }
            case "sel-feature":
            {
                using var body = Drilled(Drilled(Block(), 0, 0, 3), 0, 0, 6, 10, 10);
                var wall = FacesOf(body, (f, _) => Round(f, 6));
                var counterbore = FacesOf(body, (f, c) => Round(f, 6) || Round(f, 3) || f.Type == "Plane" && Near(c.Z, 10));
                return new(Pick(body, topic.BeforeLabel, wall), Pick(body, topic.AfterLabel, counterbore));
            }
            case "sel-same-feature":
            {
                using var body = Drilled(Drilled(Drilled(Drilled(Block(), -9, -4, 2.5), 0, -4, 2.5), 9, -4, 2.5), 0, 5, 4);
                var small = FacesOf(body, (f, _) => Round(f, 2.5));
                return new(Pick(body, topic.BeforeLabel, small.Take(1).ToArray()), Pick(body, topic.AfterLabel, small));
            }
            case "sel-same-radius":
            {
                using var filleted = Filleted(Block(), e => e.Type == "Line" && Near(e.Start.Y, -10) && Near(e.End.Y, -10) && Near(e.Start.Z, 14) && Near(e.End.Z, 14), 3);
                using var body = Drilled(Drilled(filleted, 6, 3, 3), -7, 3, 5);
                var hole = FacesOf(body, (f, c) => Round(f, 3) && Near(c.X, 6));
                var same = FacesOf(body, (f, _) => Round(f, 3));
                return new(Pick(body, topic.BeforeLabel, hole), Pick(body, topic.AfterLabel, same));
            }
            case "sel-fillets":
            {
                using var body = Filleted(Filleted(Filleted(Block(), e => Vertical(e, -15, 10), 2), e => Vertical(e, 15, -10), 3), e => Vertical(e, -15, -10), 6);
                var three = FacesOf(body, (f, _) => Round(f, 3));
                var upTo = FacesOf(body, (f, _) => Round(f, 2) || Round(f, 3));
                return new(Pick(body, topic.BeforeLabel, three), Pick(body, topic.AfterLabel, upTo));
            }
            case "sel-coplanar":
            {
                using var body = Join([Mv(Occ.Box(9, 20, 14), -11, 0, 0), Occ.Box(9, 20, 20), Mv(Occ.Box(9, 20, 14), 11, 0, 0)]);
                var tops = FacesOf(body, (f, c) => f.Type == "Plane" && Near(c.Z, 14));
                return new(Pick(body, topic.BeforeLabel, tops.Take(1).ToArray()), Pick(body, topic.AfterLabel, tops));
            }
            case "sel-coaxial":
            {
                using var body = Join([Drilled(Block(), -6, 0, 4), Mv(Occ.Cylinder(4, 8), -6, 0, 20), Mv(Occ.Cylinder(4, 8), 8, 0, 20)]);
                var hole = FacesOf(body, (f, c) => f.Type == "Cylinder" && c.Z < 14);
                var axis = FacesOf(body, (f, c) => f.Type == "Cylinder" && Near(c.X, -6));
                return new(Pick(body, topic.BeforeLabel, hole), Pick(body, topic.AfterLabel, axis));
            }
            case "sel-to-faces":
            {
                using var body = Block();
                var edge = EdgesOf(body, e => Near(e.Start.Y, 10) && Near(e.End.Y, 10) && Near(e.Start.Z, 14) && Near(e.End.Z, 14));
                var faces = FacesOf(body, (_, c) => Near(c.Z, 14) || Near(c.Y, 10));
                return new(Pick(body, topic.BeforeLabel, null, edge), Pick(body, topic.AfterLabel, faces));
            }
            case "sel-to-edges":
            {
                using var body = Block();
                var top = FacesOf(body, (_, c) => Near(c.Z, 14));
                var border = EdgesOf(body, e => Near(e.Start.Z, 14) && Near(e.End.Z, 14));
                return new(Pick(body, topic.BeforeLabel, top), Pick(body, topic.AfterLabel, null, border));
            }
            case "sel-to-vertices":
            {
                using var body = Block();
                var top = FacesOf(body, (_, c) => Near(c.Z, 14));
                using var marked = Join([Block(), Mv(Occ.Sphere(1.8), 15, 10, 14), Mv(Occ.Sphere(1.8), -15, 10, 14), Mv(Occ.Sphere(1.8), 15, -10, 14), Mv(Occ.Sphere(1.8), -15, -10, 14)]);
                var corners = FacesOf(marked, (f, _) => f.Type == "Sphere");
                return new(Pick(body, topic.BeforeLabel, top), Pick(marked, topic.AfterLabel, corners));
            }
            case "sel-reselect" or "sel-save-set":
            {
                using var body = Block();
                var faces = FacesOf(body, (_, c) => Near(c.Z, 14) || Near(c.Y, -10));
                return new(Src(body.Tessellate(), topic.BeforeLabel), Pick(body, topic.AfterLabel, faces));
            }
        }
        return null;
    }
}
