using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Help for the View tab tools whose effect is not obvious from the name. Standard views, zoom and grid get a plain tooltip.</summary>
public static partial class ToolHelp
{
    static ToolHelpTopic? FindView(string? icon) => icon switch
    {
        "ortho" => Topic(icon, "Orthographic", "Show the model with parallel projection, so equal sizes look equal at any distance.", "Start: perspective", "Result: parallel edges stay parallel",
            "1. Choose Orthographic.\n2. Orbit or choose a standard view.\n3. Measure sizes straight off the screen.",
            "Best for checking sizes and alignment. Standard views switch to orthographic automatically."),
        "perspective" => Topic(icon, "Perspective", "Show the model as the eye sees it: far parts look smaller.", "Start: orthographic", "Result: edges converge with distance",
            "1. Choose Perspective.\n2. Orbit and zoom.\n3. Choose Orthographic to go back.",
            "Best for judging shape and presenting a part. Sizes on screen are no longer true."),
        "xray" => Topic(icon, "X-ray", "See through faces to the geometry behind them.", "Start: solid faces hide the inside", "Result: hidden edges show through",
            "1. Turn on X-ray.\n2. Orbit to look through the model.\n3. Turn it off again.",
            "Use it to find internal holes and channels, or to pick geometry that is hidden behind a face."),
        "isolate" => Topic(icon, "Isolate", "Hide everything except the selected objects, or show only the faces you selected.", "Start: several bodies", "Result: only the selected body",
            "1. Select the objects or faces to keep.\n2. Choose Isolate.\n3. Choose Show All to bring the rest back.",
            "Hidden objects are only hidden, not deleted. With faces selected, the other faces of those bodies are hidden too; nothing in the document changes."),
        _ => null
    };

    static MeshData EdgesOnly(MeshData mesh)
    {
        var lines = new MeshData(0, 0, mesh.SegmentCount) { Min = mesh.Min, Max = mesh.Max, FaceCount = 0, EdgeCount = mesh.EdgeCount };
        mesh.EdgePoints.CopyTo(lines.EdgePoints, 0); mesh.SegEdge.CopyTo(lines.SegEdge, 0);
        return lines;
    }

    static ToolHelpExample? ExampleView(ToolHelpTopic topic)
    {
        switch (topic.Icon)
        {
            case "ortho" or "perspective":
            {
                var bar = GridBox(70, 18, 18, 1, 1, 1);
                bool flat = topic.Icon == "ortho";
                return new(new(bar, [], [], false, topic.BeforeLabel, null, false, flat), new(bar, [], [], false, topic.AfterLabel, null, false, !flat));
            }
            case "xray":
            {
                using var block = Occ.Box(30, 20, 16);
                using var channel = Mv(Occ.Rotate(Occ.Cylinder(4, 40), new(0, 0, 0), new(0, 1, 0), 90), -20, 0, 8);
                using var part = Occ.Cut(block, channel);
                var solid = part.Tessellate();
                return new(Src(solid, topic.BeforeLabel), Src(EdgesOnly(solid), topic.AfterLabel));
            }
            case "isolate":
            {
                using var all = Join([Occ.Box(16, 16, 12), Mv(Occ.Cylinder(8, 18), 26, 0, 0), Mv(Occ.Sphere(9), -26, 0, 9)]);
                using var kept = Occ.Cylinder(8, 18);
                var many = all.Tessellate();
                return new(Src(many, topic.BeforeLabel), Src(kept.Tessellate(), topic.AfterLabel));
            }
            default: return null;
        }
    }
}
