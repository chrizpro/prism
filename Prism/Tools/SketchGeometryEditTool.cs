using System.Collections.Generic;

namespace Prism.Core;

/// <summary>One editing family for constrained geometry and spline control points.</summary>
public sealed class SketchGeometryEditTool(int variant = 0) : SketchTool
{
    SketchTool _inner = variant == 1 ? new SplineEditTool() : new SelectTool();
    public override string Name => "Edit Sketch";
    public override string Mode => variant == 1 ? "Spline points" : "Geometry";
    public override string Prompt => variant==1?_inner.Prompt:"Edit Sketch: drag corner points to reshape; box-select or double-click a shape, then drag an edge to move it. G edits only the gizmo: select targets and press Enter to bind. Outside G, its arrows, rings and scale handle transform the geometry.";
    public override IReadOnlyList<ToolOption> Options => [new("Geometry","Select points, curves and complete shapes for constrained editing","sel-object"),new("Spline points","Add, move or remove spline control points","spline-edit")];
    public override int OptionIndex => variant;
    protected override void ApplyOption(int index) { variant=index; _inner=index==1?new SplineEditTool():new SelectTool(); }
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
