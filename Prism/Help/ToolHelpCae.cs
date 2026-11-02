using System;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Help for the CAE tabs (Static, Thermal, Dynamics, Motion, Fluids). Result colors are illustrations, not solved studies.</summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindCae(string? icon) => icon switch
    {
        "sim-study" => Topic(icon, "New Study", "Start a static stress study on a solid body.", "Start: a solid body", "Result: the body is the study part",
            "1. Select the solid body to analyze.\n2. Choose New Study.\n3. Add supports and loads, then run the study.",
            "A study needs at least one support and one load. Each study remembers its own supports, loads and material."),
        "materials" => Topic(icon, "Materials", "Choose the material so studies and renders use real values.", "Start: a body with no material", "Result: material applied",
            "1. Open Materials.\n2. Pick a material from the library.\n3. Assign it to the body.",
            "Static studies use stiffness and strength, thermal studies use conductivity and heat capacity, frequencies use stiffness and density."),
        "sim-roller" => Topic(icon, "Roller / Symmetry", "Let selected faces slide in their plane but not move through it.", "Study body", "Selected roller face",
            "1. Start a static study.\n2. Select the faces that may slide.\n3. Save the support, then add loads and solve.",
            "Use it where a part rests on a smooth surface, or to model half of a symmetric part on its cut face."),
        "sim-directional" => Topic(icon, "Fixed Directions", "Lock only the global directions you choose on selected faces.", "Study body", "Selected face, chosen directions locked",
            "1. Start a static study.\n2. Select the faces.\n3. Tick the X, Y or Z directions to lock, then save.",
            "Unlocked directions stay free to move. Lock all three to get a Fixed Support."),
        "sim-torque" => Topic(icon, "Torque", "Apply a twisting moment to a flat face.", "Study body", "Selected torque face",
            "1. Start a static study.\n2. Select a flat face.\n3. Set the torque and axis, then save and solve.",
            "The torque turns about the face's axis. Keep a support on the opposite end so the part cannot spin freely."),
        "sim-gravity" => Topic(icon, "Gravity", "Include the part's own weight in the study.", "Study body", "Whole body loaded by its weight",
            "1. Start a static study.\n2. Choose Gravity and its direction.\n3. Save, then solve.",
            "The weight comes from the material density, so assign a material first. It acts on the whole body."),
        "sim-mesh" => Topic(icon, "Create Mesh", "Divide the body into small elements and look at the mesh before solving.", "Start: the smooth body", "Result: the body divided into elements",
            "1. Start a study and set the supports and loads.\n2. Choose Create Mesh.\n3. Check the mesh, refine it if needed, then run.",
            "Smaller elements give more accurate stress but take longer. The illustration shows a surface mesh only."),
        "sim-run" => Topic(icon, "Run Study", "Mesh the body if needed and solve the study.", "Start: supports and loads set", "Result: a solved stress field",
            "1. Set the material, supports and loads.\n2. Choose Run Study.\n3. Pick a result to color the model.",
            "Colors illustrate a result; they are not a solved study. Check the mesh with Mesh refinement before trusting a number."),
        "generative" => Topic(icon, "Generative Design", "Let the software remove the material that carries the least load.", "Start: a solid block", "Result: material removed where it is not needed",
            "1. Set the supports and loads.\n2. Choose a material reduction goal.\n3. Run, then review the lighter shape.",
            "Keep out faces you need, such as bolt holes. The result is a suggestion to remodel, not a finished part."),
        "sim-stress" => Topic(icon, "von Mises Stress", "Color the model by equivalent stress.", "Start: the solved model", "Result: colored by stress",
            "1. Run the study.\n2. Choose von Mises Stress.\n3. Use Probe to read values.",
            "Red is high stress. Compare it with the yield strength, or use Factor of Safety."),
        "sim-displacement" => Topic(icon, "Displacement", "Color the model by how far each point moves.", "Start: the undeformed model", "Result: the deformed shape, colored by movement",
            "1. Run the study.\n2. Choose Displacement.\n3. Use Probe to read values.",
            "The shape is exaggerated so small movements are visible. Check the legend for the real value."),
        "sim-safety" => Topic(icon, "Factor of Safety", "Show how far each point is from yielding.", "Start: the solved model", "Result: colored by safety margin",
            "1. Run the study.\n2. Choose Factor of Safety.\n3. Look for regions below 1.",
            "Yield strength divided by stress. Below 1 means the material yields; many designs aim for 1.5 or more."),
        "sim-probe" => Topic(icon, "Probe", "Click the result to read the value at a point.", "Start: a colored result", "Result: a value read at the clicked point",
            "1. Show a result.\n2. Choose Probe.\n3. Click the model to read the value there.",
            "Click near the highlighted areas first: peaks are usually at supports, holes and corners."),
        "sim-select-study" => Topic(icon, "Select Static Study", "Switch to another saved static study.", "Start: several studies", "Result: one study active",
            "1. Choose Select static study.\n2. Pick a study from the list.\n3. Its supports, loads and results appear.",
            "Each study keeps its own setup, so you can compare designs side by side."),
        "sim-save-faces" => Topic(icon, "Save Face Selection", "Give the selected faces a name so you can reuse them.", "Start: faces selected", "Result: saved under a name",
            "1. Select the boundary faces.\n2. Choose Save face selection.\n3. Type a name.",
            "Use names such as bolt holes or contact face, then recall them in other studies."),
        "sim-recall-faces" => Topic(icon, "Recall Face Selection", "Select a named group of faces again.", "Start: nothing selected", "Result: the saved faces selected",
            "1. Choose Recall face selection.\n2. Pick a saved name.\n3. Use the faces for a support or load.",
            "Recalled faces are checked against the current body; faces that no longer exist are skipped."),
        "sim-saved" => Topic(icon, "Saved CAE Setups", "Run a saved case, with its supports and loads, again.", "Start: a bare body", "Result: supports and loads restored",
            "1. Choose Saved setups.\n2. Pick a saved case.\n3. Review it, then run.",
            "Save a setup before you change the design so you can rerun it on the new shape."),
        "sim-refine" => Topic(icon, "Mesh Refinement", "Compare results on three meshes to see whether the mesh is fine enough.", "Start: a coarse mesh", "Result: a finer mesh",
            "1. Set up and run a study.\n2. Choose Mesh refinement.\n3. Compare displacement on the three meshes.",
            "If the answer barely changes as the mesh gets finer, the result has converged."),
        "sim-export" => Topic(icon, "Export Results", "Save nodal results and study details to a file.", "Start: a solved study", "Result: values written to a file",
            "1. Run the study.\n2. Choose Export results.\n3. Choose a file name.",
            "The file holds the values at each mesh node plus the study settings, for reports or other tools."),
        "cae-thermal" => Topic(icon, "Thermal Analysis", "Find how heat flows through a solid between hot and cold faces.", "Start: a hot face selected", "Result: temperature spread through the body",
            "1. Select the solid body.\n2. Select the faces held at each temperature.\n3. Choose steady or transient, then run.",
            "Steady shows the final state; transient shows how it changes over time. One uniform solid only."),
        "cae-modal" => Topic(icon, "Natural Frequencies", "Find the frequencies a part vibrates at, and the shape of each.", "Start: a fixed support", "Result: a vibration mode shape",
            "1. Select the solid body.\n2. Select the fixed support faces.\n3. Set the number of modes, then run.",
            "The deformation amplitude is arbitrary. The shape matters, the size does not."),
        "motion-kinematics" => Topic(icon, "Kinematics", "Move a mechanism through a motion you choose and see how every joint and part moves.", "Start: the mechanism at rest", "Result: parts swung through their motion, colored by speed",
            "1. Build the mechanism in Assemble: insert parts, ground one, add joints (and gears).\n2. Open Kinematics and choose for each joint: hold, constant speed or oscillate.\n3. Run, then scrub or play the motion and read the speeds, accelerations and paths.",
            "No masses or forces are needed. Turn on the collision check to find where moving parts hit each other; it is slower. Gears and racks carry the motion to the joints they drive."),
        "motion-dynamics" => Topic(icon, "Motion Dynamics", "Find the torque a motor needs, or let a joint swing freely under gravity and loads.", "Start: a link held out sideways", "Result: the link swung down under its own weight",
            "1. Build the mechanism in Assemble and give every part a material.\n2. Open Motion Dynamics, set gravity, and choose for each joint: driven by a motor or free.\n3. Add loads and friction, run, and read the drive torque, power, energy and part accelerations.",
            "Parts are rigid and joints and gears are ideal: no flexing, backlash, tooth contact or collisions between moving parts. The result is only as good as the masses and loads you enter."),
        "motion-robot" => Topic(icon, "Robot Path", "Move a robot arm's tool along a curve and see the joint motion and torque it takes.", "Start: the arm at rest, a curve to follow", "Result: the arm reaching along the curve",
            "1. Build the arm in Assemble: parts, joints, a grounded base.\n2. Pick a point on the last link as the tool, then the curve to follow.\n3. Set the speed and payload, run, then scrub or play the motion.",
            "The arm must be able to reach the curve; points it cannot reach are reported. Corners are rounded so the arm does not have to stop. With six joints the tool can also keep its direction."),
        "motion-spring" => Topic(icon, "Spring / Actuator", "Add a spring, damper or cylinder between two parts.", "Start: two parts", "Result: a spring joining them",
            "1. Pick a point on the first part.\n2. Pick a point on the second part, or on a fixed body.\n3. Set stiffness, damping or an actuator push.",
            "Used by Motion Dynamics. A push of zero stiffness acts like a hydraulic or electric cylinder: force equals pressure times area."),
        "motion-force" => Topic(icon, "Force and Torque Loads", "Push or twist a part with a load that can change in time.", "Start: a part", "Result: a load acting on it",
            "1. Pick where it acts.\n2. Choose the direction or axis and the size.\n3. Make it constant, a pulse or alternating.",
            "Used by Motion Dynamics. The direction stays fixed in space unless you choose that it turns with the part."),
        "cae-fluid" => Topic(icon, "Pipe Flow", "Estimate pressure drop and Reynolds number in a round pipe.", "Start: a pipe", "Result: pressure falling along the pipe",
            "1. Enter the pipe size, length and fluid.\n2. Set the flow rate.\n3. Read the pressure drop and Reynolds number.",
            "A quick hand calculation, not 3D CFD. It does not model bends, valves or changes in size."),
        _ => null
    };

    static MeshData Beam(int nx = 10) => GridBox(40, 14, 10, nx, 3, 2);

    static ToolHelpExample? ExampleCae(ToolHelpTopic topic)
    {
        string key = topic.Icon;
        switch (key)
        {
            case "sim-study" or "materials":
            {
                using var body = Occ.Box(32, 18, 12);
                var mesh = body.Tessellate();
                bool material = key == "materials";
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, AllFaces(mesh), material ? Copper : Blue));
            }
            case "sim-roller" or "sim-directional" or "sim-torque" or "sim-gravity":
            {
                using var body = Occ.Box(32, 18, 12);
                var mesh = body.Tessellate();
                uint color = key switch { "sim-roller" => Blue, "sim-directional" => Purple, "sim-torque" => Orange, _ => Amber };
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, key == "sim-gravity" ? AllFaces(mesh) : [0], color));
            }
            case "sim-mesh" or "sim-refine":
            {
                bool refine = key == "sim-refine";
                var before = refine ? GridBox(40, 14, 10, 4, 2, 1, true) : GridBox(40, 14, 10, 1, 1, 1);
                var after = refine ? GridBox(40, 14, 10, 12, 4, 3, true) : GridBox(40, 14, 10, 6, 3, 2, true);
                return new(Src(before, topic.BeforeLabel, refine ? [0] : null, null, Blue), Src(after, topic.AfterLabel, [0], null, Blue));
            }
            case "sim-run" or "sim-stress" or "sim-export" or "sim-probe" or "sim-select-study" or "sim-saved" or "sim-save-faces" or "sim-recall-faces":
                return ExampleStudy(topic);
            case "sim-displacement":
            {
                var after = Colorize(Deform(Beam(12), p => p + new Vec3(0, 0, -6 * Math.Pow((p.X + 20) / 40, 2))), (p, n) => p.X);
                return new(Src(Beam(12), topic.BeforeLabel, null, [3], Green), Src(after, topic.AfterLabel));
            }
            case "sim-safety":
            {
                var before = Colorize(Beam(), (p, n) => 1 - (p.X + 20) / 40);
                var after = Colorize(Beam(), (p, n) => Math.Pow(1 - (p.X + 20) / 40, 4));
                return new(Src(before, topic.BeforeLabel), Src(after, topic.AfterLabel));
            }
            case "generative":
            {
                using var block = Occ.Box(40, 24, 14);
                var result = Occ.Cut(block, Mv(Occ.Rotate(Occ.Cylinder(4.5, 40), new(0, 0, 0), new(1, 0, 0), 90), -11, 20, 7));
                var more = Occ.Cut(result, Mv(Occ.Rotate(Occ.Cylinder(4.5, 40), new(0, 0, 0), new(1, 0, 0), 90), 0, 20, 7));
                var last = Occ.Cut(more, Mv(Occ.Rotate(Occ.Cylinder(4.5, 40), new(0, 0, 0), new(1, 0, 0), 90), 11, 20, 7));
                result.Dispose(); more.Dispose();
                using var final = last;
                var mesh = block.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(final.Tessellate(), topic.AfterLabel));
            }
            case "cae-thermal":
            {
                var hot = GridBox(40, 14, 10, 8, 3, 2);
                var spread = Colorize(GridBox(40, 14, 10, 8, 3, 2), (p, n) => 1 - (p.X + 20) / 40);
                return new(Src(hot, topic.BeforeLabel, null, [3], Red), Src(spread, topic.AfterLabel));
            }
            case "cae-modal":
            {
                static double Bend(double x) { double s = (x + 22) / 44; return (3 * s * s - s * s * s) / 2; }
                var plate = GridBox(44, 10, 3, 16, 2, 1);
                var bent = Colorize(Deform(GridBox(44, 10, 3, 16, 2, 1), p => p + new Vec3(0, 0, 9 * Bend(p.X))), (p, n) => p.Z);
                return new(Src(plate, topic.BeforeLabel, null, [3], Green), Src(bent, topic.AfterLabel));
            }
            case "motion-robot" or "motion-spring" or "motion-force" or "motion-kinematics" or "motion-dynamics":
            {
                bool dyn = key == "motion-dynamics";
                Vec3 Swing(Vec3 p, double deg) { double a = deg * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a); double x = p.X + 20; return new Vec3(-20 + x * c, p.Y, x * sn + p.Z * c); }
                var rest = GridBox(40, 8, 6, 10, 2, 2);
                var moved = Colorize(Deform(GridBox(40, 8, 6, 10, 2, 2), p => Swing(p, dyn ? -55 : 50)), (p, n) => (p.X + 20) / 40);
                return new(Src(rest, topic.BeforeLabel, null, [3], Green), Src(moved, topic.AfterLabel));
            }
            case "cae-fluid":
            {
                using var outer = Occ.Cylinder(6, 40);
                using var bore = Mv(Occ.Cylinder(4.2, 42), 0, 0, -1);
                using var tube = Occ.Cut(outer, bore);
                using var lying = Occ.Rotate(tube, new(0, 0, 0), new(0, 1, 0), 90);
                var plain = lying.Tessellate();
                var flow = Colorize(lying.Tessellate(), (p, n) => 1 - (p.X - plain.Min.X) / (plain.Max.X - plain.Min.X));
                return new(Src(plain, topic.BeforeLabel), Src(flow, topic.AfterLabel));
            }
            default: return null;
        }
    }

    /// <summary>A cantilever beam with a fixed end, shown before and after a study result.</summary>
    static ToolHelpExample ExampleStudy(ToolHelpTopic topic)
    {
        var plain = Beam();
        var stress = Colorize(Beam(), (p, n) => 1 - (p.X + 20) / 40);
        switch (topic.Icon)
        {
            case "sim-probe":
            {
                int marker = Patch(stress, p => Math.Abs(p.X + 8) < 6 && p.Z > 9);
                return new(Src(stress, topic.BeforeLabel), Src(stress, topic.AfterLabel, null, [marker], 0xFFFFFFFF));
            }
            case "sim-select-study":
            {
                using var pair = Join([Occ.Box(24, 16, 10), Mv(Occ.Box(24, 16, 10), 34, 0, 0)]);
                var mesh = pair.Tessellate();
                return new(Src(mesh, topic.BeforeLabel), Src(mesh, topic.AfterLabel, null, Enumerable.Range(0, 6).ToArray(), Blue));
            }
            case "sim-saved":
                return new(Src(plain, topic.BeforeLabel), Src(plain, topic.AfterLabel, null, [3, 2], Green));
            case "sim-save-faces":
                return new(Src(plain, topic.BeforeLabel, null, [3], Blue), Src(plain, topic.AfterLabel, null, [3], Blue));
            case "sim-recall-faces":
                return new(Src(plain, topic.BeforeLabel), Src(plain, topic.AfterLabel, null, [3], Blue));
            case "sim-export":
                return new(Src(stress, topic.BeforeLabel), Src(Colorize(Beam(), (p, n) => 1 - (p.X + 20) / 40), topic.AfterLabel));
            case "sim-stress":
                return new(Src(plain, topic.BeforeLabel, null, [3], Green), Src(stress, topic.AfterLabel));
            default:
                return new(Src(plain, topic.BeforeLabel, null, [3], Green), Src(stress, topic.AfterLabel));
        }
    }
}
