using System.Collections.Generic;

namespace Prism.Core;

/// <summary>One editing family for constrained geometry and spline control points.</summary>
public sealed class SketchGeometryEditTool(int variant = 0) : SketchTool
{
    SketchTool _inner = variant == 1 ? new SplineEditTool() : new SelectTool();
    public override string Name => "Edit Sketch";
    public override string Mode => variant == 1 ? "Spline points" : "Geometry";
    public override string Prompt => variant==1?_inner.Prompt:"Edit Sketch: drag corner points to reshape; box-select or double-click a shape, then drag an edge to move it. G edits only the gizmo: select targets and press Enter to bind. Outside G, its arrows, rings and scale handle transform the geometry.";
    /// <summary>The spline-point mode (add, move and remove points on a spline); otherwise this is the Edit Geometry mode that works like Select.</summary>
    public bool SplinePoints => variant == 1;
    public override string PanelTitle => variant == 1 ? "Edit spline points" : "Edit Geometry";
    // Edit Geometry offers the same Box / Lasso choice as Select. The spline mode has no method choice: it is a tool of its own, started from the sketch panel.
    public override IReadOnlyList<ToolOption> Options => variant == 1 ? [] : _inner.Options;
    public override int OptionIndex => variant == 1 ? 0 : _inner.OptionIndex;
    protected override void ApplyOption(int index) { if (variant == 0 && _inner is SelectTool select) select.UseMethod(index); }
    protected override void ResetState(CadSession s) => _inner.Activate(s);
    public override void Activate(CadSession s) => _inner.Activate(s);
    public override void Deactivate(CadSession s) => _inner.Deactivate(s);
    public override bool PointerDown(CadSession s, PointerInfo p)
    { bool used=_inner.PointerDown(s,p); s.Sketch?.CenterGizmoOnSelection(s); return used; }
    public override void PointerMove(CadSession s,PointerInfo p) => _inner.PointerMove(s,p);
    public override void PointerUp(CadSession s,PointerInfo p) { _inner.PointerUp(s,p);s.Sketch?.CenterGizmoOnSelection(s); }
    public override bool Key(CadSession s,string key)
    { bool used=_inner.Key(s,key);if(used)s.Sketch?.CenterGizmoOnSelection(s);return used; }
    public override void BuildOverlay(CadSession s,Overlay o) => _inner.BuildOverlay(s,o);
}
