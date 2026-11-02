using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

public sealed record ToolHelpTopic(string Icon, string Title, string Purpose, string BeforeLabel, string AfterLabel, string Steps, string Tip);
public sealed record ToolHelpExample(StudioPreviewSource Before, StudioPreviewSource After);

/// <summary>Small, isolated examples made with the same kernel operations as the modeling tools.</summary>
public static partial class ToolHelp
{
    static readonly Dictionary<string, ToolHelpExample> Cache = new();

    public static ToolHelpTopic? ForCommand(Commands.Command? command) => command switch
    {
        Commands.ProfileCommand => Find("cnc-profile"),
        Commands.PocketCommand => Find("cnc-pocket"),
        Commands.FixtureCommand when command.Title == "Fixed Support" => Find("sim-fixed"),
        Commands.LoadCommand when command.Title == "Force" => Find("sim-force"),
        Commands.LoadCommand when command.Title == "Pressure" => Find("sim-pressure"),
        Commands.FixtureCommand when command.Title == "Roller / Symmetry" => Find("sim-roller"),
        Commands.FixtureCommand when command.Title == "Fixed Directions" => Find("sim-directional"),
        Commands.LoadCommand when command.Title == "Torque" => Find("sim-torque"),
        Commands.LoadCommand when command.Title == "Gravity" => Find("sim-gravity"),
        Commands.GenerativeCommand => Find("generative"),
        Commands.ProbeCommand => Find("sim-probe"),
        Commands.SelectStudyCommand => Find("sim-select-study"),
        Commands.NamedFacesCommand => Find("sim-save-faces"),
        Commands.RecallFacesCommand => Find("sim-recall-faces"),
        Commands.SavedCaeCommand => Find("sim-saved"),
        Commands.ConvergenceCommand => Find("sim-refine"),
        Commands.ExportStudyCommand => Find("sim-export"),
        Commands.CaeSolveCommand solve => Find(solve.IsThermal ? "cae-thermal" : "cae-modal"),
        Commands.PipeFlowCommand => Find("cae-fluid"),
        Commands.FatigueCommand => Find("cae-fatigue"),
        Commands.MotionStudyCommand motion => Find(motion.IsRobot ? "motion-robot" : motion.IsDynamic ? "motion-dynamics" : "motion-kinematics"),
        Commands.MotionSpringCommand => Find("motion-spring"),
        Commands.MotionLoadCommand => Find("motion-force"),
        Commands.CamSetupCommand => Find("cam-setup"),
        Commands.SelectCamSetupCommand => Find("cam-select-setup"),
        Commands.CamToolLibraryCommand => Find("cam-tools"),
        Commands.CncDesignCheckCommand => Find("cam-check"),
        Commands.AdaptiveClearCommand => Find("cnc-adaptive"),
        Commands.TurnCommand => Find("cnc-turn"),
        Commands.DrillCommand => Find("cnc-drill"),
        Commands.FaceMillCommand => Find("cnc-face"),
        Commands.Rough3DCommand => Find("cnc-rough"),
        Commands.ParallelFinishCommand => Find("cnc-finish"),
        Commands.CncPostCommand => Find("cnc-post"),
        Commands.CamPlaybackCommand => Find("cam-playback"),
        Commands.CamProgramOrderCommand => Find("cam-order"),
        Commands.CncStockCommand => Find("cam-stock"),
        Commands.CamReviewCommand => Find("cam-sheet"),
        Commands.ToolpathViewCommand => Find("toolpath-view"),
        Commands.FdmCheckCommand => Find("fdm-check"),
        Commands.ResinCheckCommand => Find("resin-check"),
        Commands.ImportGcodeCommand => Find("gcode-import"),
        Commands.PrintExportCommand => Find("print-export"),
        Commands.MeshCutCommand => Find("mesh-cut"),
        Commands.MeshRepairCommand => Find("mesh-repair"),
        Commands.MeshQualityCommand => Find("mesh-quality"),
        Commands.MeshOverhangCommand => Find("mesh-overhang"),
        Commands.MeshThicknessCommand => Find("mesh-thickness"),
        Commands.MeshRemeshCommand => Find("mesh-remesh"),
        Commands.MeshBooleanCommand => Find("mesh-boolean"),
        Commands.MeasureCommand => Find("measure"),
        Commands.PropertiesCommand => Find("properties"),
        Commands.SectionCommand => Find("section"),
        Commands.InterferenceCommand => Find("interference"),
        Commands.CheckCommand => Find("check-geometry"),
        Commands.CurvatureCommand => Find("curvature"),
        Commands.DraftCommand => Find("draft"),
        Commands.DrawingCommand => Find("drawing"),
        Commands.TransformCommand when command.Title is "Move" or "Rotate" or "Scale" => Find(command.Title.ToLowerInvariant()),
        Commands.BooleanCommand => Find(command.Title.ToLowerInvariant()),
        Commands.ShellCommand => Find("shell"),
        Commands.HoleCommand => Find("hole"),
        Commands.TransformCommand when command.Title == "Mirror" => Find("mirror"),
        Commands.LinearPatternCommand => Find("pattern-linear"),
        Commands.CircularPatternCommand => Find("pattern-circular"),
        Commands.ExtrudeCommand or Commands.LoftCommand or Commands.SweepCommand or Commands.RevolveCommand or Commands.FilletCommand or Commands.ChamferCommand => Find(command.Icon),
        _ => null
    };

    public static ToolHelpTopic? Find(string? icon) => icon switch
    {
        "cnc-profile" => new(icon, "CNC Profile", "Generate cutter passes around a closed outline to cut a part or its boundary.", "Input: closed outline", "Generated cutting moves",
            "1. Select a closed outline.\n2. Set tool diameter, cutting depth, and side.\n3. Review the toolpath before creating it.",
            "The preview shows cutting moves generated by Profile; rapid moves are omitted for clarity."),
        "cnc-pocket" => new(icon, "CNC Pocket", "Clear material inside a closed outline using successive cutter passes.", "Input: closed outline", "Generated cutting moves",
            "1. Select the pocket outline.\n2. Set tool diameter, depth, and stepover.\n3. Review the toolpath before creating it.",
            "The preview shows Pocket's generated cutting moves; nested outlines can leave islands."),
        "sim-fixed" => new(icon, "Fixed Support", "Keep selected faces from moving during a static study.", "Study body", "Selected support face",
            "1. Start a static study.\n2. Select the faces held fixed.\n3. Save the support, then add loads and solve.",
            "The colored face illustrates the selection. A fixed support constrains all displacement directions."),
        "sim-force" => new(icon, "Force", "Apply a force to selected faces of the study body.", "Study body", "Selected load face",
            "1. Start a static study.\n2. Select faces and set the force and direction.\n3. Save the load, then mesh and solve.",
            "The colored face illustrates the selection; it is not a solved stress result."),
        "sim-pressure" => new(icon, "Pressure", "Apply pressure to selected faces of the study body.", "Study body", "Selected pressure face",
            "1. Start a static study.\n2. Select faces and enter pressure.\n3. Save the load, then mesh and solve.",
            "The colored face illustrates the selection; pressure is applied over the selected area."),
        "revolve" or "revolve-surface" => new(icon, "Revolve", "Spin an outline around an axis to create a round shape, like a knob or a bowl.", "Start: an outline", "Result: the outline spun around an axis",
            "1. Select an outline or flat face.\n2. Choose the axis to spin around.\n3. Set the angle and check the preview.",
            "A full turn is 360°. A closed outline can make a solid; an open curve makes a surface."),
        "fillet" => new(icon, "Fillet", "Round a sharp edge on a solid body.", "Start: select an edge", "Result: a rounded edge",
            "1. Select one or more edges.\n2. Set the radius.\n3. Check the preview, then accept.",
            "A larger radius makes a broader curve. If it fails, try a smaller radius."),
        "chamfer" => new(icon, "Chamfer", "Bevel a sharp edge with a flat angled face.", "Start: select an edge", "Result: a beveled edge",
            "1. Select one or more edges.\n2. Set the distance.\n3. Check the preview, then accept.",
            "Fillet rounds an edge; Chamfer cuts it back with a flat face."),
        "extrude" or "extrude-surface" => new(icon, "Extrude", "Give an outline depth by extending it in a straight line.", "Start: a flat outline", "Result: the outline extended",
            "1. Select an outline or flat face.\n2. Set the distance and direction.\n3. Check the preview, then accept.",
            icon == "extrude" ? "A closed outline makes a solid. Use Cut to remove material from an existing body." : "Surface mode makes walls without filling the ends."),
        "loft" or "loft-surface" => new(icon, "Loft", "Connect outlines to make a shape that changes size or shape along its length.", "Start: two separate outlines", "Result: a transition between them",
            "1. Prepare outlines on different planes.\n2. Select each outline in order.\n3. Check the preview, then accept.",
            "A profile is one outline. Start with two profiles; guide rails are optional curves that control the sides."),
        "sweep" or "sweep-surface" => new(icon, "Sweep", "Carry an outline along a path, like a round section following a bent tube.", "Start: an outline and a path", "Result: the outline along the path",
            "1. Select the outline (profile).\n2. Select the curve to follow (path).\n3. Check the preview, then accept.",
            "Place the outline at the start of the path, facing along it. Sweep follows a path; Loft connects separate outlines."),
        "union" => new(icon, "Union", "Merge bodies into one solid.", "Start: two overlapping bodies", "Result: one merged body",
            "1. Select the base body.\n2. Select the bodies to merge into it.\n3. Check the preview, then accept.",
            "The bodies must touch or overlap. Keep tool bodies leaves the originals in place."),
        "subtract" => new(icon, "Subtract", "Cut one body out of another.", "Start: a body and a cutter (highlighted)", "Result: the cutter's shape removed",
            "1. Select the body to cut from.\n2. Select the tool bodies that do the cutting.\n3. Check the preview, then accept.",
            "Only the first body is kept; the tool bodies are removed unless Keep tool bodies is on."),
        "intersect" => new(icon, "Intersect", "Keep only the volume that two bodies share.", "Start: two overlapping bodies", "Result: only the shared volume",
            "1. Select the base body.\n2. Select the other bodies.\n3. Check the preview, then accept.",
            "Everything outside the overlap is discarded. Use Subtract to remove the overlap instead."),
        "shell" => new(icon, "Shell", "Hollow out a solid, leaving walls of an even thickness.", "Start: select the face to open", "Result: a hollow body",
            "1. Select the faces to remove, or the whole body for a closed hollow.\n2. Set the wall thickness and direction.\n3. Check the preview, then accept.",
            "Keep the thickness smaller than the smallest feature, or the shell can fail."),
        "hole" => new(icon, "Hole", "Drill a round hole into a face of a body.", "Start: select a face", "Result: a hole in the face",
            "1. Select the face to drill into.\n2. Click to place the hole and set diameter and depth.\n3. Check the preview, then accept.",
            "Choose Through All to cut the whole body, or enter a depth for a blind hole."),
        "mirror" => new(icon, "Mirror", "Reflect objects across a plane to make a symmetric copy.", "Start: one side of a part", "Result: the part and its reflection",
            "1. Select the objects to reflect.\n2. Choose the mirror plane.\n3. Check the preview, then accept.",
            "Model half of a symmetric part, then mirror it, to keep both sides identical."),
        "pattern-linear" => new(icon, "Linear Pattern", "Repeat objects in a straight line at an even spacing.", "Start: one object", "Result: evenly spaced copies",
            "1. Select the objects to repeat.\n2. Choose a direction, then set the count and spacing.\n3. Check the preview, then accept.",
            "Add a second or third direction to make a grid or a block of copies."),
        "pattern-circular" => new(icon, "Circular Pattern", "Repeat objects around an axis at an even angle.", "Start: one object", "Result: copies around the axis",
            "1. Select the objects to repeat.\n2. Choose the axis to turn around.\n3. Set the count and angle, then accept.",
            "The copies are spread over the full 360° unless you set a smaller angle."),
        _ => FindCae(icon) ?? FindCam(icon) ?? FindAnalyze(icon) ?? FindView(icon)
    };

    /// <summary>Help for ribbon buttons that are not commands (toggles and actions), keyed by the tab they sit on and their icon.</summary>
    public static string? ForButton(string tab, string icon) => icon switch
    {
        // Shared buttons: the same instance appears on several tabs, so these cannot depend on the tab.
        "materials" or "sel-object" or "sel-face" or "sel-edge" or "sel-vertex" => Find(icon)?.Icon,
        _ => ForTabButton(tab, icon)
    };

    static string? ForTabButton(string tab, string icon) => (tab, icon) switch
    {
        ("View", "ortho" or "perspective" or "xray" or "isolate") => Find(icon)?.Icon,
        ("View", "zebra" or "clear-analysis") => Find(icon)?.Icon,
        ("Static", "sim-study") => Find("sim-study")?.Icon,
        ("Static", "sim-mesh") => Find("sim-mesh")?.Icon,
        ("Static", "sim-run") => Find("sim-run")?.Icon,
        ("Static", "sim-probe") => Find("sim-probe")?.Icon,
        ("Static", "sim-stress") => Find("sim-stress")?.Icon,
        ("Static", "sim-displacement") => Find("sim-displacement")?.Icon,
        ("Static", "sim-safety") => Find("sim-safety")?.Icon,
        ("Static", "cae-fatigue") => Find("cae-fatigue")?.Icon,
        ("Static" or "Thermal" or "Dynamics" or "Motion" or "Analyze", "clear-analysis") => Find("clear-analysis")?.Icon,
        ("Analyze", "bbox" or "zebra") => Find(icon)?.Icon,
        _ => null
    };

    public static ToolHelpExample Example(ToolHelpTopic topic)
    {
        if (Cache.TryGetValue(topic.Icon, out var cached)) return cached;
        bool solid = !topic.Icon.EndsWith("-surface", StringComparison.Ordinal);
        ToolHelpExample example;
        if (topic.Icon is "sim-fixed" or "sim-force" or "sim-pressure")
        {
            using var body = Occ.Box(32, 18, 12);
            var mesh = body.Tessellate();
            example = new(new(mesh, [], [], false, topic.BeforeLabel),
                new(mesh, [], [0], false, topic.AfterLabel, topic.Icon == "sim-fixed" ? 0xFF2EA043u : 0xFFE5484Du));
        }
        else if (topic.Icon is "cnc-profile" or "cnc-pocket")
        {
            var session = new Core.CadSession();
            var outline = session.AddObject(Occ.Polyline([new(0, 0, 0), new(40, 0, 0), new(40, 30, 0), new(0, 30, 0)], true), "Outline", Core.ObjKind.Curve);
            Commands.Command command = topic.Icon == "cnc-profile" ? new Commands.ProfileCommand() : new Commands.PocketCommand();
            session.StartCommand(command);
            command.Pick(Core.SelItem.Of(outline), false);
            var output = command.TryBuild() ?? throw new InvalidOperationException(command.Error ?? "Toolpath could not be generated.");
            var path = (Core.Cam.ToolpathInfo)output.Add[0].Asm!;
            var segments = new List<Shape>();
            try
            {
                for (int i = 1; i < path.MoveCount; i++)
                {
                    var a = path.Move(i - 1); var b = path.Move(i);
                    if (b.kind != Core.Cam.MoveKind.Cut) continue;
                    var from = new Vec3(a.x, a.y, a.z); var to = new Vec3(b.x, b.y, b.z);
                    if ((to - from).Length > 1e-6) segments.Add(Occ.Line(from, to));
                }
                if (segments.Count == 0) throw new InvalidOperationException("The toolpath has no cutting moves.");
                using var cuts = Occ.Compound(segments);
                var pathMesh = cuts.Tessellate();
                var inputMesh = outline.Shape.Tessellate();
                example = new(new(inputMesh, inputMesh.SegEdge.Distinct().ToArray(), [], false, topic.BeforeLabel, 0xFF3988E8),
                    new(pathMesh, pathMesh.SegEdge.Distinct().ToArray(), [], false, topic.AfterLabel, 0xFF3988E8));
            }
            finally { foreach (var segment in segments) segment.Dispose(); foreach (var item in output.Add) item.Shape?.Dispose(); }
        }
        else if (topic.Icon is "fillet" or "chamfer")
        {
            using var input = Occ.Box(30, 22, 18);
            using var result = topic.Icon == "fillet" ? Occ.Fillet(input, [0], 4) : Occ.Chamfer(input, [0], 4);
            var mesh = input.Tessellate();
            example = new(new(mesh, [0], [], false, topic.BeforeLabel, 0xFF3988E8),
                new(result.Tessellate(), [], [], false, topic.AfterLabel));
        }
        else if (topic.Icon is "union" or "subtract" or "intersect")
        {
            using var body = Occ.Box(24, 24, 14);
            using var cutter = Occ.Cylinder(8, 24);
            using var tool = Occ.Translate(cutter, new(0, 0, -5));
            using var input = Occ.Compound([body, tool]);
            using var result = topic.Icon switch { "union" => Occ.Fuse(body, tool), "subtract" => Occ.Cut(body, tool), _ => Occ.Common(body, tool) };
            var mesh = input.Tessellate();
            // The cutter's faces follow the box's six, so they are highlighted to tell the two bodies apart.
            example = new(new(mesh, [], Enumerable.Range(6, Math.Max(0, mesh.FaceCount - 6)).ToArray(), false, topic.BeforeLabel, 0xFF3988E8),
                new(result.Tessellate(), [], [], false, topic.AfterLabel));
        }
        else if (topic.Icon is "shell" or "hole")
        {
            double height = topic.Icon == "shell" ? 18 : 14;
            using var body = Occ.Box(30, 22, height);
            var mesh = body.Tessellate();
            int top = FaceAtHeight(mesh, height);
            using var result = topic.Icon == "shell" ? Occ.Shell(body, [top], -3) : HoleResult(body);
            example = new(new(mesh, [], [top], false, topic.BeforeLabel, 0xFF3988E8),
                new(result.Tessellate(), [], [], false, topic.AfterLabel));
        }
        else if (topic.Icon == "mirror")
        {
            using var baseBlock = Occ.Translate(Occ.Box(10, 10, 14), new(12, 0, 0));
            using var step = Occ.Translate(Occ.Box(5, 10, 10), new(14.5, 0, 14));
            using var part = Occ.Fuse(baseBlock, step);
            using var reflected = Occ.Mirror(part, new(0, 0, 0), new(1, 0, 0));
            using var both = Occ.Compound([part, reflected]);
            example = new(new(part.Tessellate(), [], [], false, topic.BeforeLabel), new(both.Tessellate(), [], [], false, topic.AfterLabel));
        }
        else if (topic.Icon is "pattern-linear" or "pattern-circular")
        {
            bool linear = topic.Icon == "pattern-linear";
            using var seed = linear ? Occ.Translate(Occ.Box(8, 8, 8), new(-21, 0, 0)) : Occ.Translate(Occ.Cylinder(3.5, 8), new(14, 0, 0));
            var copies = new List<Shape>();
            try
            {
                for (int i = 0; i < 4; i++)
                    copies.Add(linear ? Occ.Translate(seed, new(i * 14, 0, 0)) : Occ.Rotate(seed, new(0, 0, 0), new(0, 0, 1), i * 90));
                using var all = Occ.Compound(copies);
                example = new(new(seed.Tessellate(), [], [], false, topic.BeforeLabel), new(all.Tessellate(), [], [], false, topic.AfterLabel));
            }
            finally { foreach (var copy in copies) copy.Dispose(); }
        }
        else if ((ExampleCae(topic) ?? ExampleCam(topic) ?? ExampleAnalyze(topic) ?? ExampleView(topic)) is { } extra) example = extra;
        else if (topic.Icon.StartsWith("revolve", StringComparison.Ordinal))
        {
            using var outline = Occ.Polyline([new(4, 0, 0), new(12, 0, 0), new(10, 0, 20), new(4, 0, 20)], true);
            using var profile = Occ.Profile([outline], solid);
            using var result = Occ.Revolve(profile, new(0, 0, 0), new(0, 0, 1), 360);
            example = Pair(outline, result, topic);
        }
        else if (topic.Icon.StartsWith("loft", StringComparison.Ordinal))
        {
            using var a = Occ.Circle(new(0, 0, 0), new(0, 0, 1), 14);
            using var b = Occ.Circle(new(5, 0, 32), new(0, 0, 1), 7);
            using var input = Occ.Compound([a, b]);
            using var result = Occ.Loft([a, b], solid, false);
            example = Pair(input, result, topic);
        }
        else if (topic.Icon.StartsWith("sweep", StringComparison.Ordinal))
        {
            using var outline = Occ.Circle(new(0, 0, 0), new(0, 0, 1), 4);
            using var path = Occ.Arc3(new(0, 0, 0), new(5.858, 0, 14.142), new(20, 0, 20));
            using var input = Occ.Compound([outline, path]);
            using var result = Occ.Sweep(outline, path, solid);
            example = Pair(input, result, topic);
        }
        else
        {
            using var outline = Occ.Polyline([new(-12, -9, 0), new(12, -9, 0), new(12, 9, 0), new(-12, 9, 0)], true);
            using var profile = Occ.Profile([outline], solid);
            using var result = Occ.Extrude(profile, new(0, 0, 26));
            example = Pair(outline, result, topic);
        }
        Cache[topic.Icon] = example;
        return example;
    }

    /// <summary>The face whose vertices all lie at the given height (the top of a box standing on the XY plane).</summary>
    static int FaceAtHeight(MeshData mesh, double z)
    {
        for (int face = 0; face < mesh.FaceCount; face++)
        {
            bool flat = mesh.FaceTriStart[face + 1] > mesh.FaceTriStart[face];
            for (int tri = mesh.FaceTriStart[face]; tri < mesh.FaceTriStart[face + 1] && flat; tri++)
                for (int k = 0; k < 3; k++)
                    flat &= Math.Abs(mesh.Positions[mesh.Indices[tri * 3 + k] * 3 + 2] - z) < 1e-4;
            if (flat) return face;
        }
        return 0;
    }

    static Shape HoleResult(Shape body)
    {
        using var drill = Occ.Cylinder(5, 10);
        using var tool = Occ.Translate(drill, new(0, 0, 6));
        return Occ.Cut(body, tool);
    }

    static ToolHelpExample Pair(Shape input, Shape result, ToolHelpTopic topic)
    {
        var mesh = input.Tessellate();
        return new(new(mesh, mesh.SegEdge.Distinct().ToArray(), [], false, topic.BeforeLabel, 0xFF3988E8),
            new(result.Tessellate(), [], [], false, topic.AfterLabel));
    }
}
