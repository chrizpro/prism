using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Ribbon;

/// <summary>The shipped ribbon follows one workflow order. User layouts remain independent.</summary>
public static class RibbonLayoutPolicy
{
    public static readonly IReadOnlyList<string> SectionOrder = Array.AsReadOnly(new[]
    {
        // what you pick and draw with
        "Document", "Select", "Datum", "References", "Curves", "Edit curves", "Setup", "Study", "Board", "Get a mesh", "Region", "Set up", "Sketch from scan", "Fit", "Write",
        // making things
        "Draw", "Create", "Primitives", "Features", "Sheet metal", "Components", "Mechanism", "Joint", "Couple",
        "Animation", "Render", "Lighting", "Conduction", "Vibration", "Pipe flow", "3D flow", "Toolpaths", "Prepare", "Troubleshoot", "Preparation", "Resin preparation", "Mesh preparation",
        // changing things
        "Modify", "Direct Edit", "Add", "Edit faces", "Edit features", "Direct", "Faces", "Edit", "Repair", "Reshape", "Cut-outs",
        "Curves from surfaces", "Surfaces", "Fill", "Shape", "Detail", "Loops", "Lock and align",
        "Topology", "Symmetry", "Quality", "Validate", "Continuity", "Finish", "Convert", "Combine", "Pattern",
        // setting up and running analysis
        "Constrain", "Dimension", "Supports", "Loads", "Springs and loads", "Solve", "Simulate", "Generative", "Study workflow",
        // reading the results
        "Saved workflow", "Results", "Review", "Motion", "Nozzle review", "Mechanical checks",
        "Mold inspection", "Print analysis", "Orientation & inspection", "Select & trace", "Reverse engineering",
        "Bounds", "Check", "Surface quality", "Analyze",
        "Image", "Apply", "Library", "Orientation", "Navigate", "Layout", "Display",
        "Show", "Section", "Drawing", "Exchange", "Export", "Output", "Share", "Your own editor",
        // always the last three, then the way out
        "Transform", "Inspect", "Appearance", "Close", "Brainstorm", "Exit",
    });

    public static int Rank(string section)
    {
        for (int i = 0; i < SectionOrder.Count; i++) if (SectionOrder[i] == section) return i;
        return SectionOrder.Count;
    }

    public static void Order(RibbonTab tab)
    {
        var groups = tab.Groups.OrderBy(g => Rank(g.Title)).ToArray();
        tab.Groups.Clear();
        tab.Groups.AddRange(groups);
    }
}
