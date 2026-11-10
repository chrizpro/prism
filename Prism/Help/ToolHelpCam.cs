using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Help for the CAM tabs (Subtractive, 3D FDM, 3D Resin). Toolpaths here are illustrations of each operation.</summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindCam(string? icon) => icon switch
    {
        "cam-setup" => Topic(icon, "CAM Setup", "Define the stock and the work zero that every operation uses.", "Start: the finished part", "Result: stock around the part",
            "1. Choose CAM setup.\n2. Set the stock size or let it fit the part.\n3. Set the G54 work zero.",
            "Leave a little extra stock on every side. Work zero should match how you will touch off on the machine."),
        "cam-select-setup" => Topic(icon, "Select Setup", "Switch between named stock and work-zero setups.", "Start: several setups", "Result: one setup active",
            "1. Choose Select setup.\n2. Pick a setup from the list.\n3. New operations use that setup.",
            "Use one setup per way the part is held, such as top side and flipped."),
        "cam-tools" => Topic(icon, "Tool Library", "Save named cutters with feeds and speeds for each material.", "Start: one tool", "Result: a library of saved tools",
            "1. Open Tool library.\n2. Add a tool: type, diameter and length.\n3. Enter feeds and speeds for your material.",
            "Operations pick tools from this list, so spend the time here once."),
        "cam-check" => Topic(icon, "Design Check", "Find features a cutter cannot reach before you make toolpaths.", "Start: a part with a thin wall", "Result: the problem area highlighted",
            "1. Choose Design check.\n2. Select the bodies or meshes.\n3. Review the highlighted problems.",
            "Checks top-down access, tool reach and thin walls. Fix them in the model, or plan another setup."),
        "cnc-adaptive" => Topic(icon, "Adaptive Clearing", "Clear a pocket with an even cutter load, so you can cut deeper and faster.", "Input: closed outline", "Generated cutting moves",
            "1. Select the pocket outline.\n2. Set tool diameter, depth and stepover.\n3. Review the toolpath before creating it.",
            "Curved, constant-engagement moves avoid slotting. Use a smaller stepover and deeper cut than for Pocket."),
        "cnc-turn" => Topic(icon, "Turning", "Face, rough and finish a round part on a lathe.", "Input: half-section outline", "Result: the turned part",
            "1. Sketch half of the part's section.\n2. Select it and choose the stock.\n3. Set the passes, then review.",
            "The outline is turned about the part's axis, like Revolve. Put the axis on the sketch's edge."),
        "cnc-drill" => Topic(icon, "Drill", "Drill holes, with peck cycles for deep holes.", "Input: hole positions", "Result: holes drilled",
            "1. Select circles or points for the holes.\n2. Set depth and peck.\n3. Review the cycle before creating it.",
            "Peck drilling retracts to clear chips; use it when depth is more than a few diameters."),
        "cnc-image" => Topic(icon, "Image Carve", "Carve a picture into the top of the stock: darker or lighter areas are cut deeper.", "Input: a picture file and the stock", "Generated cutting moves",
            "1. Type the picture's path and the width of the carving.\n2. Choose whether dark or light areas are deepest, the bit and the depth.\n3. Review the toolpath, which shows the pattern, then create it.",
            "Use a ball nose bit for smooth relief and a small stepover for fine detail."),
        "cnc-face" => Topic(icon, "Face", "Skim the top of the stock flat with back-and-forth passes.", "Input: raw stock", "Generated cutting moves",
            "1. Select the stock or the area to face.\n2. Set tool diameter and depth.\n3. Review the toolpath before creating it.",
            "Face first so later operations start from a clean, flat top."),
        "cnc-rough" => Topic(icon, "3D Rough", "Remove most of the material in layers, leaving an allowance around the model.", "Start: the 3D model", "Result: stepped layers left for finishing",
            "1. Select the model and setup stock.\n2. Set the layer depth and allowance.\n3. Review the toolpath before creating it.",
            "Roughing leaves steps on purpose; 3D Finish removes them."),
        "cnc-finish" => Topic(icon, "3D Finish", "Follow the surface with parallel passes for a smooth finish.", "Start: the roughed shape", "Generated cutting moves",
            "1. Select the model.\n2. Choose ball nose or flat end mill, and the stepover.\n3. Review the toolpath before creating it.",
            "A smaller stepover leaves a smoother surface but takes longer."),
        "cnc-post" => Topic(icon, "Post G-code", "Turn finished toolpaths into a G-code file for your machine.", "Start: toolpaths", "Result: a G-code file",
            "1. Create your toolpaths.\n2. Choose Post G-code and your controller.\n3. Choose a file name.",
            "Pick the post for GRBL, Mach3 or LinuxCNC to match your controller. Always dry-run before cutting."),
        "cam-playback" => Topic(icon, "Playback", "Animate a cutter or nozzle along its path.", "Start: a finished path", "Result: the tool moving along the path",
            "1. Select a toolpath.\n2. Choose Playback.\n3. Play, pause or drag the slider.",
            "Watch for rapid moves through the part and unexpected plunges before you send the file to the machine."),
        "cam-order" => Topic(icon, "Operation Order", "Set the sequence the operations run in.", "Start: operations in creation order", "Result: operations in the chosen order",
            "1. Choose Operation order.\n2. Move operations up or down.\n3. Confirm.",
            "Rough before finish, and use the same tool for operations next to each other to save tool changes."),
        "cam-stock" => Topic(icon, "Stock Review", "Simulate material removal and see what is left.", "Start: untouched stock", "Result: remaining stock colored",
            "1. Create your operations.\n2. Choose Stock review.\n3. Look for leftovers, gouges and holder contacts.",
            "Red is the most material left. Check for gouges before posting."),
        "cam-sheet" => Topic(icon, "Setup Sheet", "Review each operation and export a sheet for the machine operator.", "Start: operations", "Result: a setup sheet listing them",
            "1. Choose Setup sheet.\n2. Check each operation's source and tool.\n3. Export the sheet.",
            "Include it with the G-code so the operator loads the right tools in the right order."),
        "toolpath-view" => Topic(icon, "Pass / Layer View", "Step through the depth passes or print layers of a path.", "Start: one finished pass", "Result: all passes stacked by depth",
            "1. Select the toolpath.\n2. Choose the view.\n3. Drag the slider to step through passes or layers.",
            "Use it to check depth of cut and where each layer begins."),
        "fdm-check" => Topic(icon, "FDM Check", "Find overhangs, thin walls and bed-contact problems before printing.", "Start: a part with an overhang", "Result: the overhang highlighted",
            "1. Choose FDM check.\n2. Select the body and enter the build dimensions.\n3. Run checks and review the findings.",
            "Overhangs steeper than about 45° usually need supports. Try rotating the part first."),
        "print-export" => Topic(icon, "Export for Printing", "Write a print file for your slicer.", "Start: the part", "Result: the part ready for the slicer",
            "1. Choose Export for printing.\n2. Choose a format and folder. Review the listed bodies.\n3. Export, then open it in your slicer.",
            "3MF keeps units and multiple bodies; STL is the widest supported."),
        "check-geometry" => Topic(icon, "Check Geometry", "Test the model for invalid geometry and open edges.", "Start: the model", "Result: problem edges highlighted",
            "1. Choose Check Geometry.\n2. Select the bodies.\n3. Review the highlighted problems.",
            "Fix problems before you export; open edges can make slicers fail."),
        "gcode-import" => Topic(icon, "Import Nozzle Path", "Read linear G0/G1 moves from your slicer's G-code to review them.", "Start: the sliced part", "Result: nozzle paths shown layer by layer",
            "1. Choose Import nozzle path.\n2. Choose the G-code file.\n3. Review the paths, or use Playback.",
            "Only linear G0/G1 moves are read. Arcs and other commands are skipped."),
        "resin-check" => Topic(icon, "Resin Check", "Find cups, trapped resin and thin features before printing.", "Start: an open cup", "Result: the suction cup highlighted",
            "1. Choose Resin check.\n2. Select the bodies.\n3. Review cavities, drainage, islands and thin features.",
            "A cup that faces the build plate can trap resin. Tilt the part or add a drain hole, then recheck."),
        _ => null
    };

    static ToolHelpExample? ExampleCam(ToolHelpTopic topic)
    {
        string key = topic.Icon;
        switch (key)
        {
            case "cam-setup" or "cam-sheet":
            {
                using var part = Occ.Cylinder(8, 16);
                using var stock = Occ.Box(40, 30, 10);
                using var both = Occ.Compound([stock, part]);
                var mesh = both.Tessellate();
                return new(Src(part.Tessellate(), topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, Enumerable.Range(0, 6).ToArray(), Copper));
            }
            case "cam-select-setup":
            {
                using var pair = Join([Occ.Box(30, 22, 10), Mv(Occ.Box(30, 22, 10), 40, 0, 0)]);
                var mesh = pair.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, Enumerable.Range(0, 6).ToArray(), Copper));
            }
            case "cam-tools":
            {
                using var one = Occ.Cylinder(3.5, 22);
                using var set = Join([Mv(Occ.Cylinder(2, 16), -21, 0, 0), Mv(Occ.Cylinder(3.5, 22), -7, 0, 0), Mv(Occ.Cylinder(5, 18), 7, 0, 0), Mv(Occ.Cylinder(6.5, 24), 22, 0, 0)]);
                return new(Src(one.Tessellate(), topic.BeforeLabel), Src(set.Tessellate(), topic.AfterLabel));
            }
            case "cam-check":
            {
                using var blank = Occ.Box(30, 20, 12);
                using var pocket = Mv(Occ.Box(27, 16, 12), -0.5, 0, 2);
                using var part = Occ.Cut(blank, pocket);
                var mesh = part.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 0, -15), null, true));
            }
            case "cnc-adaptive":
            {
                using var path = AdaptivePath();
                var outline = Occ.Polyline([new(0, 0, 0), new(40, 0, 0), new(40, 30, 0), new(0, 30, 0)], true);
                using (outline) return FromShapes(outline, path, topic, pathBefore: true, pathAfter: true);
            }
            case "cnc-turn":
            {
                using var outline = Occ.Polyline([new(0, 0, 0), new(8, 0, 0), new(8, 0, 10), new(5, 0, 14), new(5, 0, 22), new(0, 0, 22)], true);
                using var profile = Occ.Profile([outline], true);
                using var result = Occ.Revolve(profile, new(0, 0, 0), new(0, 0, 1), 360);
                return FromShapes(outline, result, topic, pathBefore: true);
            }
            case "cnc-drill":
            {
                using var block = Occ.Box(30, 20, 10);
                var centers = new[] { new Vec3(-9, -5, 10), new Vec3(9, -5, 10), new Vec3(-9, 5, 10), new Vec3(9, 5, 10) };
                using var circles = Join(centers.Select(c => Occ.Circle(c, new(0, 0, 1), 2.5)));
                using var marked = Occ.Compound([block, circles]);
                var result = block;
                foreach (var c in centers) { var next = Occ.Cut(result, Mv(Occ.Cylinder(2.5, 14), c.X, c.Y, -2)); if (!ReferenceEquals(result, block)) result.Dispose(); result = next; }
                using var drilled = result;
                var b = marked.Tessellate();
                return new(Src(b, topic.BeforeLabel, b.SegEdge.Distinct().Where(e => e >= 12).ToArray(), null, Blue), Src(drilled.Tessellate(), topic.AfterLabel));
            }
            case "cnc-image":
            {
                using var stock = Occ.Box(40, 30, 10);
                using var path = Paths(Zigzag(-15, 15, -10, 10, 2.5, 10, (x, y) => 10 - 1.5 * (0.5 + 0.5 * Math.Sin(x * 0.5) * Math.Cos(y * 0.5))));
                return FromShapes(stock, path, topic, pathAfter: true);
            }
            case "cnc-face":
            {
                using var stock = Occ.Box(40, 30, 10);
                using var path = Paths(Zigzag(-17, 17, -12, 12, 6, 10));
                return FromShapes(stock, path, topic, pathAfter: true);
            }
            case "cnc-rough" or "cnc-finish":
            {
                using var dome = Occ.Cut(Occ.Sphere(14), Mv(Occ.Box(40, 40, 20), 0, 0, -20));
                var steps = new List<Shape>();
                try
                {
                    double z = 0;
                    foreach (double height in new[] { 4.0, 4.0, 3.0, 3.0 })
                    { steps.Add(Mv(Occ.Cylinder(Math.Sqrt(196 - z * z), height), 0, 0, z)); z += height; }
                    using var stack = steps.Skip(1).Aggregate(steps[0], (a, b) => ReferenceEquals(a, steps[0]) ? Occ.Fuse(a, b) : FuseAndFree(a, b));
                    if (key == "cnc-rough") return FromShapes(dome, stack, topic);
                    using var finish = Paths(Zigzag(-12, 12, -12, 12, 3, 0, (x, y) => Math.Sqrt(Math.Max(0, 196 - x * x - y * y))));
                    return FromShapes(stack, finish, topic, pathAfter: true);
                }
                finally { foreach (var step in steps) step.Dispose(); }
            }
            case "cnc-post":
            {
                using var before = Paths(Rect(-16, -11, 16, 11, 0), Rect(-16, -11, 16, 11, -3), Rect(-16, -11, 16, 11, -6));
                var b = before.Tessellate();
                return new(Src(b, topic.BeforeLabel, AllEdges(b), null, Blue), Src(b, topic.AfterLabel, AllEdges(b), null, Green));
            }
            case "cam-playback":
            {
                using var path = Paths(Rect(-16, -11, 16, 11, 0), Rect(-12, -7, 12, 7, 0));
                using var withTool = Join([Occ.Polyline(Rect(-16, -11, 16, 11, 0).ToList(), false), Occ.Polyline(Rect(-12, -7, 12, 7, 0).ToList(), false), Mv(Occ.Cylinder(3, 14), 16, -3, 0)]);
                var b = path.Tessellate(); var a = withTool.Tessellate();
                return new(Src(b, topic.BeforeLabel, AllEdges(b), null, Blue), Src(a, topic.AfterLabel, AllEdges(a), null, Blue));
            }
            case "cam-order":
            {
                var rects = new[] { Rect(-24, -8, -12, 8, 0), Rect(-22.5, -6.5, -13.5, 6.5, 0), Rect(-6, -8, 6, 8, 0), Rect(-4.5, -6.5, 4.5, 6.5, 0), Rect(12, -8, 24, 8, 0), Rect(13.5, -6.5, 22.5, 6.5, 0) };
                using var before = Paths(rects);
                using var after = Paths(rects.Append([new Vec3(-18, 0, 0), new(0, 0, 0), new(18, 0, 0)]).ToArray());
                var b = before.Tessellate(); var a = after.Tessellate();
                return new(Src(b, topic.BeforeLabel, AllEdges(b), null, Blue), Src(a, topic.AfterLabel, AllEdges(a), null, Green));
            }
            case "cam-stock":
            {
                var plain = GridBox(40, 30, 10, 8, 6, 2);
                var left = Colorize(GridBox(40, 30, 10, 8, 6, 2), (p, n) => Math.Min(1, Math.Sqrt(p.X * p.X / 400 + p.Y * p.Y / 225)));
                return new(Src(plain, topic.BeforeLabel), Src(left, topic.AfterLabel));
            }
            case "toolpath-view":
            {
                using var one = Paths(Rect(-16, -11, 16, 11, 0));
                using var stack = Paths(Rect(-16, -11, 16, 11, 0), Rect(-16, -11, 16, 11, -3), Rect(-16, -11, 16, 11, -6), Rect(-16, -11, 16, 11, -9));
                return FromShapes(one, stack, topic, pathBefore: true, pathAfter: true);
            }
            case "fdm-check":
            {
                using var part = Occ.Cone(6, 14, 12);
                var mesh = part.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesWhere(mesh, n => n.Z < -.1), null, true));
            }
            case "print-export":
            {
                using var part = Occ.Cylinder(8, 12);
                using var bed = Mv(Occ.Box(50, 40, 1), 0, 0, -1);
                using var both = Occ.Compound([bed, part]);
                var mesh = both.Tessellate();
                return new(Src(part.Tessellate(), topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, Enumerable.Range(0, 6).ToArray(), Blue));
            }
            case "check-geometry":
            {
                using var body = Occ.Box(30, 20, 14);
                var mesh = body.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, [0, 1, 2], null, null, true));
            }
            case "gcode-import":
            {
                using var part = Occ.Cylinder(10, 8);
                using var layers = Paths(Enumerable.Range(1, 8).Select(i => Ring(10, i)).ToArray());
                return FromShapes(part, layers, topic, pathAfter: true);
            }
            case "resin-check":
            {
                using var box = Occ.Box(30, 30, 9);
                var solid = box.Tessellate();
                using var cup = Occ.Shell(box, [FaceAtHeight(solid, 9)], -2);
                var mesh = cup.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, FacesAt(mesh, 2, 2), null, true));
            }
            default: return null;
        }
    }

    static Shape FuseAndFree(Shape a, Shape b) { var result = Occ.Fuse(a, b); a.Dispose(); return result; }

    static IEnumerable<Vec3> Ring(double radius, double z)
        => Enumerable.Range(0, 37).Select(i => new Vec3(radius * Math.Cos(i * Math.PI / 18), radius * Math.Sin(i * Math.PI / 18), z));

    /// <summary>Rectangles stepping inward across a 40 × 30 outline, like the clearing passes of an adaptive pocket.</summary>
    static Shape AdaptivePath()
    {
        var spiral = new List<Vec3>();
        for (double inset = 4; inset < 14; inset += 3)
        { spiral.Add(new(inset, inset, 0)); spiral.Add(new(40 - inset, inset, 0)); spiral.Add(new(40 - inset, 30 - inset, 0)); spiral.Add(new(inset, 30 - inset, 0)); }
        return Paths(spiral);
    }
}
