using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Ribbon;

/// <summary>The shipped ribbon follows one workflow order. User layouts remain independent.</summary>
public static class RibbonLayoutPolicy
{
    public static readonly IReadOnlyList<string> SectionOrder = Array.AsReadOnly(new[]
    {
        "Document", "Select", "References", "Setup", "Study", "Board", "Get a mesh", "Write",
        "Draw", "Create", "Primitives", "Features", "Sheet metal", "Components", "Joint",
        "Animation", "Render", "Lighting", "Conduction", "Vibration", "Pipe flow", "CNC", "Preparation",
        "Modify", "Direct", "Faces", "Edit", "Repair", "Cut-outs", "Edit curves",
        "Curves from surfaces", "Surfaces", "Fill", "Shape", "Detail", "Loops", "Lock and align",
        "Topology", "Symmetry", "Continuity", "Finish", "Convert", "Combine", "Transform", "Pattern",
        "Constrain", "Dimension", "Supports", "Loads", "Solve", "Generative", "Study workflow",
        "Saved workflow", "Results", "Review", "Motion", "Nozzle review", "Mechanical checks",
        "Mold inspection", "Print analysis", "Select & trace", "Reverse engineering", "Inspect",
        "Bounds", "Section", "Check", "Surface quality", "Quality", "Validate", "Analyze",
        "Appearance", "Image", "Apply", "Library", "Orientation", "Navigate", "Layout", "Display",
        "Show", "Drawing", "Exchange", "Output", "Share", "Your own editor", "Close", "Exit",
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
