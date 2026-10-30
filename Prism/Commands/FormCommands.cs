using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

// ================================================================= create form primitives
public abstract class FormCreateCommand : Command
{
    protected NumberParam X = null!, Y = null!, Z = null!;
    protected abstract string Prefix { get; }
    protected abstract SubdMesh Make();
    public override string Hint => "Set the size and segment counts, then press OK. Edit the result with Edit Freeform.";

    protected void AddPosition() { X = Num("Position X", 0); Y = Num("Position Y", 0); Z = Num("Position Z", 0); }

    protected override CommandOutput? Build()
    {
        var m = Make();
        var off = new Vec3(X.Value, Y.Value, Z.Value);
        if (off != Vec3.Zero) for (int i = 0; i < m.V.Count; i++) m.V[i] += off;
        var o = new CommandOutput { Label = Title };
        o.Add.Add(new(null, Prefix, ObjKind.Form, null, null, m));
        return o;
    }
}

public sealed class FormPlaneCommand : FormCreateCommand
{
    readonly NumberParam _w, _h; readonly IntParam _nx, _ny; readonly ChoiceParam _plane;
    public override string Title => "Freeform Plane";
    protected override string Prefix => "Freeform";
    public FormPlaneCommand()
    {
        _plane = Choice("Plane", 0, "XY (ground)", "XZ (front)", "YZ (side)");
        _w = Num("Width", 80, 0.01); _h = Num("Length", 80, 0.01);
        _nx = Add(new IntParam { Label = "Faces across", Value = 4, Min = 1, Max = 40 });
        _ny = Add(new IntParam { Label = "Faces along", Value = 4, Min = 1, Max = 40 });
        AddPosition();
    }
    protected override SubdMesh Make()
    {
        var m = FormOps.Plane(_w.Value, _h.Value, _nx.Value, _ny.Value);
        if (_plane.Index != 0)
            for (int i = 0; i < m.V.Count; i++)
            {
                var p = m.V[i];
                m.V[i] = _plane.Index == 1 ? new(p.X, 0, p.Y) : new(0, p.X, p.Y);
            }
        if (_plane.Index == 1) FormOps.FlipNormals(m);   // keep the visible side facing the default view
        return m;
    }
}

public sealed class FormBoxCommand : FormCreateCommand
{
    readonly NumberParam _w, _d, _h; readonly IntParam _nx, _ny, _nz;
    public override string Title => "Freeform Box";
    protected override string Prefix => "Freeform";
    public FormBoxCommand()
    {
        _w = Num("Width (X)", 60, 0.01); _d = Num("Depth (Y)", 60, 0.01); _h = Num("Height (Z)", 60, 0.01);
        _nx = Add(new IntParam { Label = "Faces X", Value = 2, Min = 1, Max = 20 });
        _ny = Add(new IntParam { Label = "Faces Y", Value = 2, Min = 1, Max = 20 });
        _nz = Add(new IntParam { Label = "Faces Z", Value = 2, Min = 1, Max = 20 });
        AddPosition();
    }
    protected override SubdMesh Make() => FormOps.Box(_w.Value, _d.Value, _h.Value, _nx.Value, _ny.Value, _nz.Value);
}

public sealed class FormSphereCommand : FormCreateCommand
{
    readonly NumberParam _r; readonly IntParam _n;
    public override string Title => "Freeform Sphere";
    protected override string Prefix => "Freeform";
    public FormSphereCommand()
    {
        _r = Num("Radius", 30, 0.01);
        _n = Add(new IntParam { Label = "Faces per side", Value = 3, Min = 1, Max = 12 });
        AddPosition();
    }
    protected override SubdMesh Make() => FormOps.Sphere(_r.Value, _n.Value);
}

public sealed class FormCylinderCommand : FormCreateCommand
{
    readonly NumberParam _r, _h; readonly IntParam _seg, _rings; readonly BoolParam _caps;
    public override string Title => "Freeform Cylinder";
    protected override string Prefix => "Freeform";
    public FormCylinderCommand()
    {
        _r = Num("Radius", 25, 0.01); _h = Num("Height", 60, 0.01);
        _seg = Add(new IntParam { Label = "Sides", Value = 8, Min = 3, Max = 48 });
        _rings = Add(new IntParam { Label = "Rings", Value = 3, Min = 1, Max = 30 });
        _caps = Check("Caps", true);
        AddPosition();
    }
    protected override SubdMesh Make() => FormOps.Cylinder(_r.Value, _h.Value, _seg.Value, _rings.Value, _caps.Value);
}

public sealed class FormTorusCommand : FormCreateCommand
{
    readonly NumberParam _R, _r; readonly IntParam _a, _b;
    public override string Title => "Freeform Torus";
    protected override string Prefix => "Freeform";
    public FormTorusCommand()
    {
        _R = Num("Major radius", 30, 0.02); _r = Num("Minor radius", 10, 0.01);
        _a = Add(new IntParam { Label = "Segments around", Value = 12, Min = 3, Max = 48 });
        _b = Add(new IntParam { Label = "Segments of tube", Value = 6, Min = 3, Max = 32 });
        AddPosition();
    }
    protected override SubdMesh Make() => FormOps.Torus(_R.Value, Math.Min(_r.Value, _R.Value * 0.99), _a.Value, _b.Value);
}

// ================================================================= edit form
public enum FormMode { Vertex, Edge, Face, Object }

public sealed class EditFormCommand : Command
{
    readonly SelectionSlot _src;
    public readonly ChoiceParam Mode;
    readonly NumberParam _dist, _crease, _px, _py, _pz;
    NumberParam _angleLimit = null!, _slideAmt = null!;
    BoolParam _showPoles = null!;
    readonly BoolParam _showTriangles;
    public bool ShowTriangles => _showTriangles.Value;
    public bool ObjectSelected { get; private set; }
    public bool ShowPoles => _showPoles.Value;
    readonly IntParam _level, _subU, _subV;
    readonly ChoiceParam _loopKind;
    int _lastLevel = 3;
    readonly ChoiceParam _mirror;
    readonly BoolParam _stick;
    readonly NumberParam _scanOffset, _wrapStrength, _alignStrength, _alignRange;
    readonly SelectionSlot _curve;
    readonly IntParam _relaxPasses;
    public readonly ChoiceParam TransformMode, PivotP;
    readonly ChoiceParam _sym, _fall, _rotAxis;
    readonly IntParam _radial, _loopCount;
    readonly NumberParam _soft, _rotAngle, _scaleFactor, _bevelW, _insetAmt, _insetDepth, _loopPos;
    readonly BoolParam _insetEach;
    readonly FormEditTool _tool;
    readonly Stack<SubdMesh> _undo = new();
    bool _syncing;

    public SubdMesh? Work { get; private set; }
    public SceneObject? Source { get; private set; }
    public HashSet<int> Verts { get; } = [];
    public HashSet<long> Edges { get; } = [];
    public HashSet<int> Faces { get; } = [];
    public FormMode SelMode => (FormMode)Mode.Index;

    /// <summary>True while Insert edge loop is the active tool: each click on an edge adds a loop across it.</summary>
    public bool LoopArmed { get; private set; }
    public int LoopCount => _loopCount.Value;
    public double LoopPosition => _loopPos.Value;
    public void DisarmLoop() { if (!LoopArmed) return; LoopArmed = false; _tool.ResetLoopPreview(); S?.RaiseState(); }

    public EditFormCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Freeform", Filter = SelFilter.Form });
        Mode = Choice("Select", 2, "Vertices", "Edges", "Faces", "Object");
        Mode.Changed = SelectionModeChanged;
        _px = Num("Selection X", 0); _py = Num("Selection Y", 0); _pz = Num("Selection Z", 0);
        _px.IsVisible = _py.IsVisible = _pz.IsVisible = false;
        _px.Changed = _py.Changed = _pz.Changed = OnCenterField;
        // ---- Tier 1 editing tools: gizmo mode, pivot, soft selection, live symmetry
        TransformMode = Choice("Gizmo (W move, E rotate, R scale)", 0, "Move", "Rotate", "Scale");
        PivotP = Choice("Rotate / scale about", 0, "Selection center", "World origin");
        _soft = Num("Soft selection radius (0 = off)", 0, 0, 1e5, 1);
        _fall = Choice("Soft selection falloff", 0, "Smooth", "Linear", "Sharp");
        _sym = Choice("Live symmetry", 0, "Off", "Mirror across YZ (X = 0)", "Mirror across XZ (Y = 0)", "Mirror across XY (Z = 0)", "Radial around Z");
        _radial = Add(new IntParam { Label = "Radial copies", Value = 4, Min = 2, Max = 36 });
        _sym.Changed = () => { if (Work != null) Work.Sym = SymText(); OnChanged(); };
        _radial.Changed = () => { if (Work != null) Work.Sym = SymText(); OnChanged(); };
        Add(new ActionParam { Label = "Place symmetry through the selection (edge loop, face or points)", Run = () => Do(() => CenterSymmetryOnSelection(), undoable: false) });
        Add(new ActionParam { Label = "Put symmetry back on the world axes", Run = () => Do(() => ResetSymmetryPlace(), undoable: false) });
        _rotAxis = Choice("Rotate axis", 2, "X", "Y", "Z");
        _rotAngle = Num("Rotate by (degrees)", 15, -360, 360, 5, "°");
        _scaleFactor = Num("Scale by (factor)", 1.2, 0.01, 100, 0.1, "");
        _loopCount = Add(new IntParam { Label = "Loops to insert", Value = 1, Min = 1, Max = 24 });
        _loopPos = Num("Loop position along the edge (0..1)", 0.5, 0.02, 0.98, 0.05, "");
        _loopCount.Changed = _loopPos.Changed = () => _tool?.ResetLoopPreview();
        _bevelW = Num("Bevel width", 2, 0.01, 1e4, 0.5);
        _insetAmt = Num("Inset amount", 3, 0.01, 1e4, 0.5);
        _insetDepth = Num("Inset depth (push out +, in -)", 0, -1e4, 1e4, 0.5);
        _insetEach = Check("Inset each face on its own", false);
        _dist = Num("Extrude distance", 8);
        _crease = Num("Crease sharpness", 1, 0, 5, 0.1, "");
        _crease.Changed = PreviewCrease;
        _crease.HelpText = "0 removes the crease. Fractional sharpness gives a soft crease; larger values retain a sharper edge through more subdivision levels.";
        _level = Add(new IntParam { Label = "Smoothness (levels, 0 = show the cage faces)", Value = 3, Min = 0, Max = 4 });
        _loopKind = Choice("Edge selection", 0, "Loop (end to end)", "Ring (parallel across quads)");
        _loopKind.HelpText = "Loop follows the edge through each junction. Ring picks the parallel edges across a strip of quads. Used by Select Loop.";
        _subU = Add(new IntParam { Label = "Divisions across", Value = 2, Min = 1, Max = 16 });
        _subV = Add(new IntParam { Label = "Divisions down", Value = 2, Min = 1, Max = 16 });
        _subU.Changed = _subV.Changed = PreviewSubdivide;
        _subU.IsVisible = _subV.IsVisible = false;
        _mirror = Choice("Mirror plane", 1, "XY (Z = 0)", "XZ (Y = 0)", "YZ (X = 0)");
        _stick = Check("Keep moved points on the scan", false);
        _scanOffset = Num("Float above the scan", 0, -20, 20, 0.2);
        _wrapStrength = Num("Wrap strength", 1, 0, 1, 0.1, "");
        _relaxPasses = Add(new IntParam { Label = "Relax passes", Value = 5, Min = 1, Max = 100 });
        _curve = Slot(new SelectionSlot { Label = "Curve to align to (optional)", Filter = SelFilter.AnyCurve | SelFilter.Edge, Optional = true });
        _alignStrength = Num("Align strength", 100, 0, 100, 10, "%");
        _alignRange = Num("Only points within (0 = any distance)", 0, 0, 1e5, 1);
        _showPoles = Check("Mark poles, triangles and n-gons", false);
        _showTriangles = Check("Show surface triangles", false);
        _angleLimit = Num("Flood-select angle limit", 30, 0, 180, 5, "°");
        Add(new ActionParam { Label = "Grow selection", Run = () => Do(() => SelectOp("grow"), undoable: false) });
        Add(new ActionParam { Label = "Shrink selection", Run = () => Do(() => SelectOp("shrink"), undoable: false) });
        Add(new ActionParam { Label = "Invert selection", Run = () => Do(() => SelectOp("invert"), undoable: false) });
        Add(new ActionParam { Label = "Select everything", Run = () => Do(() => SelectOp("all"), undoable: false) });
        Add(new ActionParam { Label = "Flood-select faces by angle", Run = () => Do(() => SelectOp("angle"), undoable: false) });
        Add(new ActionParam { Label = "Select open (boundary) edges", Run = () => Do(() => SelectOp("boundary"), undoable: false) });
        Add(new ActionParam { Label = "Select creased edges", Run = () => Do(() => SelectOp("creased"), undoable: false) });
        Add(new ActionParam { Label = "Select edge loop or ring (kind: Edge selection)", Run = () => Do(() => SelectLoop(_loopKind.Index == 1), undoable: false) });
        Add(new ActionParam { Label = "Bridge the two selected edge groups", Run = () => Do(BridgeSel) });
        Add(new ActionParam { Label = "Freeze selection (lock it)", Run = () => Do(() => FreezeSel(true)) });
        Add(new ActionParam { Label = "Unfreeze selection", Run = () => Do(() => FreezeSel(false)) });
        Add(new ActionParam { Label = "Unfreeze everything", Run = () => Do(() => Work!.Frozen.Clear()) });
        Add(new ActionParam { Label = "Align to the curve", Run = () => Do(AlignToCurve) });
        Add(new ActionParam { Label = "Wrap selection onto the scan", Run = () => Do(() => ScanSurface.Wrap(S, Work!, MovingOrAll(), _wrapStrength.Value, _scanOffset.Value)) });
        Add(new ActionParam { Label = "Relax selection (even out the quads)", Run = () => Do(() => ScanSurface.Relax(S, Work!, MovingOrAll(), _relaxPasses.Value, _scanOffset.Value, ScanSurface.Any(S))) });
        Add(new ActionParam { Label = "Extrude selection", Run = () => Do(() => { MirrorSel(); ExtrudeSel(); }) });
        Add(new ActionParam { Label = "Inset selected faces", Run = () => Do(() => { MirrorSel(); InsetSel(); }) });
        Add(new ActionParam { Label = "Bevel selected edges (tighten them)", Run = () => Do(() => { MirrorSel(); BevelSel(); }) });
        Add(new ActionParam { Label = "Rotate selection by the angle above", Run = () => Do(() => TransformNumeric(FormTransform.RotateAbout(PivotPoint(), Axis(_rotAxis.Index), _rotAngle.Value * Math.PI / 180))) });
        Add(new ActionParam { Label = "Scale selection by the factor above", Run = () => Do(() => TransformNumeric(FormTransform.ScaleAbout(PivotPoint(), _scaleFactor.Value, _scaleFactor.Value, _scaleFactor.Value))) });
        Add(new ActionParam { Label = "Fill holes (cap open borders)", Run = () => Do(() => { int n = FormOps.FillHoles(Work!); if (n == 0) throw new InvalidOperationException("There are no open borders to fill."); ClearSelection(); S.Say($"Filled {n} hole(s)."); }) });
        Add(new ActionParam { Label = "Thicken (give the cage wall thickness: Bevel width)", Run = () => Do(() => { FormOps.Solidify(Work!, _bevelW.Value); ClearSelection(); S.Say($"Added a second skin {_bevelW.Value:0.##} mm inside the cage."); }) });
        Add(new ActionParam { Label = "Merge selected faces into one", Run = () => Do(() => { if (!FormOps.MergeFaces(Work!, Faces)) throw new InvalidOperationException("Select two or more touching faces (Select: Faces) that form one patch."); ClearSelection(); }) });
        _slideAmt = Num("Slide amount (-1..1)", 0.3, -1, 1, 0.1, "");
        Add(new ActionParam { Label = "Chamfer selected edges (width = Bevel width)", Run = () => Do(() => { MirrorSel(); var es = SelectedEdgeList(); if (es.Count == 0) throw new InvalidOperationException("Select inner edges (Select: Edges) to chamfer."); int n = FormOps.ChamferEdges(Work!, es, _bevelW.Value); if (n == 0) throw new InvalidOperationException("Chamfer needs inner edges that run through the form."); ClearSelection(); S.Say($"Chamfered: {n} new face(s)."); }) });
        Add(new ActionParam { Label = "Slide selected edge loop (Slide amount)", Run = () => Do(() => { MirrorSel(); var es = Edges.Select(SubdMesh.Unkey).ToList(); if (es.Count == 0) throw new InvalidOperationException("Select an edge loop (Select: Edges, then Select edge loop) to slide."); int n = FormOps.SlideEdges(Work!, es, _slideAmt.Value); if (n == 0) throw new InvalidOperationException("Those edges have no quad rails to slide along."); S.Say($"Slid {n} point(s)."); }) });
        Add(new ActionParam { Label = "Crease selected points (sharp corners)", Run = () => Do(() => { MirrorSel(); int n = FormOps.SetVertexCrease(Work!, MovingVertices(), _crease.Value); S.Say($"Set the corner crease on {n} point(s)."); }) });
        Add(new ActionParam { Label = "Merge selected edges (join the faces either side)", Run = () => Do(() => { var es = SelectedEdgeList(); if (es.Count == 0) throw new InvalidOperationException("Select inner edges (Select: Edges) to merge."); int n = FormOps.MergeEdges(Work!, es); if (n == 0) throw new InvalidOperationException("Those edges are not shared by two faces that can be joined."); ClearSelection(); S.Say($"Merged {n} edge(s)."); }) });
        Add(new ActionParam { Label = "Unweld selected edges (open a seam)", Run = () => Do(() => { MirrorSel(); var es = Edges.Select(SubdMesh.Unkey).ToList(); if (es.Count == 0) throw new InvalidOperationException("Select the edges to unweld (Select: Edges)."); int n = FormOps.UnweldEdges(Work!, es); if (n == 0) throw new InvalidOperationException("Nothing to split: pick edges that run through the form, not around one point."); ClearSelection(); S.Say($"Opened a seam ({n} point(s) duplicated). Move the two sides apart."); }) });
        Add(new ActionParam { Label = "Pull points onto the smooth surface", Run = () => Do(() => { MirrorSel(); int n = FormOps.PullToLimit(Work!, MovingOrAll().ToHashSet()); S.Say($"Pulled {n} point(s) onto the smooth surface."); }) });
        Add(new ActionParam { Label = "Collapse selected edges to points", Run = () => Do(() => { MirrorSel(); var es = SelectedEdgeList(); if (es.Count == 0) throw new InvalidOperationException("Select edges (or faces) to collapse."); FormOps.CollapseEdges(Work!, es); ClearSelection(); }) });
        Add(new ActionParam { Label = "Split face between two selected points", Run = () => Do(() => { var vs = MovingVertices().ToList(); if (vs.Count != 2) throw new InvalidOperationException("Select exactly two points on the same face."); var fi = Enumerable.Range(0, Work!.F.Count).FirstOrDefault(f => Work.F[f].Contains(vs[0]) && Work.F[f].Contains(vs[1]) && FormOps.SplitFace(Work, f, vs[0], vs[1]), -1); if (fi < 0) throw new InvalidOperationException("Those two points need to be non-neighbouring corners of one face."); ClearSelection(); }) });
        Add(new ActionParam { Label = "Smooth selection (relax toward neighbours)", Run = () => Do(() => { MirrorSel(); FormOps.SmoothVertices(Work!, MovingOrAll().ToHashSet(), 0.5, Math.Max(1, _relaxPasses.Value)); }) });
        Add(new ActionParam { Label = "Flatten selection onto a plane", Run = () => Do(() => { MirrorSel(); if (!FormOps.FlattenVertices(Work!, MovingVertices().ToHashSet())) throw new InvalidOperationException("Select at least three points (or faces) to flatten."); }) });
        Add(new ActionParam { Label = "Insert edge loop", Run = ArmLoop });
        Add(new ActionParam { Label = "Subdivide (choose divisions, selected faces or everything)", Run = OpenSubdivide });
        Add(new ActionParam { Label = "Smooth surface on or off", Run = () => Do(ToggleSmooth) });
        Add(new ActionParam { Label = "Delete selection", Run = () => Do(() => { MirrorSel(); DeleteSel(); }) });
        Add(new ActionParam { Label = "Weld selected vertices", Run = () => Do(() => { MirrorSel(); var v = MovingVertices().ToList(); Work!.Weld(v); ClearSelection(); }) });
        Add(new ActionParam { Label = "Apply crease to selected edges", Run = () => Do(() => { MirrorSel(); FormOps.SetCrease(Work!, SelectedEdgeList(), _crease.Value); }) });
        Add(new ActionParam { Label = "Flip normals", Run = () => Do(() => FormOps.FlipNormals(Work!)) });
        Add(new ActionParam { Label = "Mirror and weld", Run = () => Do(() => { var n = _mirror.Index == 0 ? Vec3.UnitZ : _mirror.Index == 1 ? Vec3.UnitY : Vec3.UnitX; FormOps.MirrorWeld(Work!, Vec3.Zero, n); ClearSelection(); }) });
        Add(new ActionParam { Label = "Undo last form step", Run = () => TryUndo() });
        Tool = _tool = new FormEditTool(this);
    }

    public override string Title => "Edit Freeform";
    public override string OkText => "Done";
    public override string Hint => "Pick a form, then click vertices, edges or faces. Double-click for a loop; Shift adds to the selection. Drag the gizmo to transform. Insert Edge: click edges to add loops.";
    public override string Icon => "form-edit";

    // ---- source handling
    protected override void UpdateVisibility()
    {
        var item = _src.Items.Count > 0 ? S.Find(_src.Items[0].Obj) : null;
        if (item != Source)
        {
            Source = item; Work = item?.Form?.Clone(); _tool?.Binding.Reset();_undo.Clear(); ClearSelection();
            if (Work != null) { _level.Value = Work.Level; LoadSymmetry(); }
        }
        bool scan = ScanSurface.Any(S);
        _stick.IsVisible = _scanOffset.IsVisible = _wrapStrength.IsVisible = _relaxPasses.IsVisible = scan;
        foreach (var a in Params.OfType<ActionParam>().Where(a => a.Label.Contains("scan", StringComparison.OrdinalIgnoreCase) || a.Label.StartsWith("Relax"))) a.IsVisible = scan;
        bool any = Work != null && MovingVertices().Any();
        _px.IsVisible = _py.IsVisible = _pz.IsVisible = any;
        if (any) SyncFields();
        // keep the panel short: only show the settings that belong to the current gizmo, symmetry and selection mode
        _rotAxis.IsVisible = _rotAngle.IsVisible = TransformMode.Index == 1;
        _scaleFactor.IsVisible = TransformMode.Index == 2;
        PivotP.IsVisible = TransformMode.IsVisible = false;
        _radial.IsVisible = _sym.Index == 4;
        _fall.IsVisible = _soft.Value > 0;
        _insetAmt.IsVisible = _insetDepth.IsVisible = _insetEach.IsVisible = SelMode == FormMode.Face;
        foreach (var a in Params.OfType<ActionParam>())
        {
            if (a.Label.StartsWith("Rotate selection")) a.IsVisible = TransformMode.Index == 1;
            else if (a.Label.StartsWith("Scale selection")) a.IsVisible = TransformMode.Index == 2;
            else if (a.Label.StartsWith("Inset selected")) a.IsVisible = SelMode == FormMode.Face;
            if (a.Label is "Apply crease to selected edges" or "Crease selected points (sharp corners)") a.IsVisible=false;
        }
        _crease.IsVisible=false;
        if(!HasPanelStep)_subU.IsVisible=_subV.IsVisible=false;
        if (Work != null && Work.Level != _level.Value) Work.Level = _level.Value;
    }

    protected override CommandOutput? Build()
    {
        if (Source == null || Work == null) return null;
        var o = new CommandOutput { Label = "Edit Freeform" };
        o.ReplaceForms.Add((Source, Work));
        return o;
    }

    // ---- selection state
    /// <summary>Selection tools that work on whatever kind of component the Select choice is on.</summary>
    public void SelectOp(string op)
    {
        if (Work == null) throw new InvalidOperationException("Pick a form first.");
        var m = Work;
        switch (op)
        {
            case "boundary": Mode.Index = 1; Verts.Clear(); Faces.Clear(); Edges.Clear(); foreach (var k in FormSelect.BoundaryEdges(m)) Edges.Add(k); break;
            case "creased": Mode.Index = 1; Verts.Clear(); Faces.Clear(); Edges.Clear(); foreach (var k in FormSelect.CreasedEdges(m)) Edges.Add(k); break;
            case "angle":
            {
                if (Faces.Count == 0) throw new InvalidOperationException("Select a face to start from (Select: Faces), then flood out from it.");
                var flood = FormSelect.FacesByAngle(m, Faces, _angleLimit.Value);
                Faces.Clear(); foreach (var f in flood) Faces.Add(f); break;
            }
            default:
                if (SelMode == FormMode.Face)
                {
                    var cur = Faces.ToHashSet(); var next = op switch { "grow" => FormSelect.GrowFaces(m, cur), "shrink" => FormSelect.ShrinkFaces(m, cur), "invert" => FormSelect.InvertFaces(m, cur), _ => Enumerable.Range(0, m.F.Count).ToHashSet() };
                    Faces.Clear(); foreach (var f in next) Faces.Add(f);
                }
                else if (SelMode == FormMode.Edge)
                {
                    var cur = Edges.ToHashSet(); var next = op switch { "grow" => FormSelect.GrowEdges(m, cur), "shrink" => FormSelect.ShrinkEdges(m, cur), "invert" => FormSelect.InvertEdges(m, cur), _ => m.Edges().Select(e => SubdMesh.Key(e.a, e.b)).ToHashSet() };
                    Edges.Clear(); foreach (var k in next) Edges.Add(k);
                }
                else
                {
                    var cur = Verts.ToHashSet(); var next = op switch { "grow" => FormSelect.GrowVertices(m, cur), "shrink" => FormSelect.ShrinkVertices(m, cur), "invert" => FormSelect.InvertVertices(m, cur), _ => Enumerable.Range(0, m.V.Count).ToHashSet() };
                    Verts.Clear(); foreach (var v in next) Verts.Add(v);
                }
                break;
        }
        SelectionNotify(); OnChanged();
    }

    public void ClearSelection() { ObjectSelected=false; Verts.Clear(); Edges.Clear(); Faces.Clear(); _tool?.SelectionChanged(); }
    void SelectionModeChanged() { DisarmLoop(); ClearSelection(); ObjectSelected=SelMode==FormMode.Object; OnChanged(); S?.RaiseState(); }
    public void SetSelectionMode(FormMode mode) {DisarmLoop();if(mode==FormMode.Object && SelMode==mode)SelectWholeObject();else Mode.Index=(int)mode;}
    public void SelectWholeObject() {ClearSelection();ObjectSelected=true;OnChanged();}
    public void SelectComponentLoop(long seed,bool additive,double x,double y)
    {
        if(Work==null || HasPanelStep)return;
        var m=Work;
        double Distance((int a,int b) edge)
        {
            var p=S.Camera.Project((m.V[edge.a]+m.V[edge.b])*.5);
            return p==null?double.MaxValue:Math.Pow(p.Value.x-x,2)+Math.Pow(p.Value.y-y,2);
        }
        if(SelMode==FormMode.Edge)
        {
            var(a,b)=SubdMesh.Unkey(seed);var loop=FormOps.EdgeLoop(m,a,b);
            if(!additive)ClearSelection();Edges.UnionWith(loop);
        }
        else if(SelMode==FormMode.Vertex)
        {
            int vertex=(int)seed;
            var incident=m.Edges().Where(e=>e.a==vertex || e.b==vertex).OrderBy(Distance).ToList();
            if(incident.Count==0)return;
            var edge=incident[0];var loop=FormOps.EdgeLoop(m,edge.a,edge.b);
            if(!additive)ClearSelection();
            foreach(var key in loop){var(a,b)=SubdMesh.Unkey(key);Verts.Add(a);Verts.Add(b);}
        }
        else if(SelMode==FormMode.Face)
        {
            var face=m.F[(int)seed];if(face.Length!=4){S.Say("Face loops need a strip of quads; this face is a pole or n-gon.");return;}
            var edge=Enumerable.Range(0,4).Select(i=>(a:face[i],b:face[(i+1)%4])).OrderBy(Distance).First();
            var ring=FormOps.EdgeRing(m,edge.a,edge.b);var topo=m.Topo();
            if(!additive)ClearSelection();
            foreach(var key in ring)foreach(var f in topo.EdgeFaces[key])if(m.F[f].Length==4)Faces.Add(f);
        }
        _tool.SelectionChanged();OnChanged();S.Say("Selected the component loop. Shift-double-click adds another loop.");
    }

    /// <summary>The selected points, or every point when nothing is selected.</summary>
    List<int> MovingOrAll() { var m = MovingVertices().ToList(); return m.Count > 0 ? m : Enumerable.Range(0, Work!.V.Count).Where(v => !Work.IsFrozen(v)).ToList(); }

    public bool StickToScan => _stick.Value && ScanSurface.Any(S);
    public void SnapToScan(IEnumerable<int> verts) => ScanSurface.Wrap(S, Work!, verts, 1, _scanOffset.Value);

    public IEnumerable<int> MovingVertices()
    {
        if (Work == null) return [];
        var set = new HashSet<int>(Verts);
        if(SelMode==FormMode.Object && ObjectSelected)set.UnionWith(Enumerable.Range(0,Work.V.Count));
        foreach (var k in Edges) { var (a, b) = SubdMesh.Unkey(k); set.Add(a); set.Add(b); }
        foreach (var f in Faces) if (f < Work.F.Count) foreach (var v in Work.F[f]) set.Add(v);
        return set.Where(v => v < Work.V.Count && !Work.IsFrozen(v));      // frozen points never move
    }

    List<(int a, int b)> SelectedEdgeList()
    {
        var l = Edges.Select(SubdMesh.Unkey).ToList();
        if (SelMode == FormMode.Face && Work != null)
            foreach (var f in Faces) { var fc = Work.F[f]; for (int i = 0; i < fc.Length; i++) l.Add((fc[i], fc[(i + 1) % fc.Length])); }
        return l;
    }

    public Vec3 SelectionCenter()
    {
        var vs = MovingVertices().ToList();
        return vs.Count == 0 ? Vec3.Zero : vs.Aggregate(Vec3.Zero, (s, v) => s + Work!.V[v]) / vs.Count;
    }

    // ---- transform plumbing shared by the gizmo, the numeric actions and the API
    Vec3 Axis(int i) => _tool.Binding.Axes[Math.Clamp(i,0,2)];
    string SymText() => new Symmetry(SymKind, _radial.Value, _symOrigin).Text;
    Vec3 _symOrigin;
    SymMode SymKind => _sym.Index switch { 1 => SymMode.MirrorX, 2 => SymMode.MirrorY, 3 => SymMode.MirrorZ, 4 => SymMode.Radial, _ => SymMode.Off };
    /// <summary>Show the form's own symmetry in the panel when a form is picked.</summary>
    void LoadSymmetry()
    {
        var s = Symmetry.Parse(Work?.Sym);
        _sym.Index = s.Mode switch { SymMode.MirrorX => 1, SymMode.MirrorY => 2, SymMode.MirrorZ => 3, SymMode.Radial => 4, _ => 0 };
        if (s.Mode == SymMode.Radial) _radial.Value = s.Count;
        _symOrigin = s.Origin;
    }
    /// <summary>With symmetry on, the selection grows to include its mirror images, so every edit lands on both sides.</summary>
    void MirrorSel()
    {
        if (Work == null || !Sym.Active) return;
        var edges = Edges.Select(SubdMesh.Unkey).ToList(); var verts = new HashSet<int>(Verts); var faces = new HashSet<int>(Faces);
        FormSymmetry.Expand(Work, Sym, verts, faces, edges);
        foreach (var v in verts) Verts.Add(v);
        foreach (var f in faces) Faces.Add(f);
        foreach (var (a, b) in edges) Edges.Add(SubdMesh.Key(a, b));
    }
    public Symmetry Sym => new(SymKind, _radial.Value, _symOrigin);
    /// <summary>Put the mirror plane (or radial axis) through the middle of the selected points, edge loop or faces, so the mirror follows the model wherever it sits.</summary>
    public void CenterSymmetryOnSelection()
    {
        if (Work == null) return;
        if (!Sym.Active) { S.Say("Turn on Live symmetry first, then place it."); return; }
        _symOrigin = SelectionCenter(); Work.Sym = SymText(); OnChanged();
        S.Say("Symmetry now passes through the selection.");
    }
    /// <summary>Back to the world axes.</summary>
    public void ResetSymmetryPlace() { _symOrigin = Vec3.Zero; if (Work != null) Work.Sym = SymText(); OnChanged(); }
    public double SoftRadius => _soft.Value;
    public Falloff SoftFalloff => (Falloff)_fall.Index;
    public Vec3 PivotPoint() => _tool.Binding.Center(PivotP.Index == 1 ? Vec3.Zero : SelectionCenter());
    List<(int p, int k)>[]? _partners; int _pKey;
    public List<(int p, int k)>[]? Partners()
    {
        var sym = Sym; if (!sym.Active || Work == null) return null;
        int key = HashCode.Combine(Work.V.Count, Work.F.Count, _sym.Index, _radial.Value);
        if (_partners == null || key != _pKey) { _partners = sym.Partners(Work); _pKey = key; }
        return _partners;
    }
    /// <summary>Move/rotate/scale the selection (plus its soft neighbourhood and mirror images) starting from the given positions.</summary>
    public void ApplyXf(Vec3[] baseAll, Func<Vec3, Vec3> xf)
    {
        if (Work == null) return;
        var sel = MovingVertices().ToHashSet();
        if (sel.Count == 0) throw new InvalidOperationException("Select points, edges or faces first.");
        FormTransform.Apply(Work, baseAll, sel, xf, SoftRadius, SoftFalloff, Sym, Partners());
        OnChanged();
    }
    public void ForceMode(int i) { DisarmLoop(); TransformMode.Index = i; OnChanged(); }

    void SyncFields()
    {
        _syncing = true;
        var c = SelectionCenter();
        _px.Value = Math.Round(c.X, 4); _py.Value = Math.Round(c.Y, 4); _pz.Value = Math.Round(c.Z, 4);
        _syncing = false;
    }

    void OnCenterField()
    {
        if (_syncing || Work == null) return;
        var vs = MovingVertices().ToList(); if (vs.Count == 0) return;
        var d = new Vec3(_px.Value, _py.Value, _pz.Value) - SelectionCenter();
        if (d.Length < 1e-9) return;
        PushUndo();
        TransformNumeric(FormTransform.MoveBy(d));
    }
    void TransformNumeric(Func<Vec3,Vec3> transform)
    {
        var frame=_tool.Binding.Frame;ApplyXf(Work!.V.ToArray(),transform);
        if(_tool.Binding.Bound&&frame is {} before)_tool.Binding.Transform(before,transform);
        S.Invalidate();
    }

    // ---- editing helpers (all go through Do so they can be undone)
    public void PushUndo() { if (Work != null) { _undo.Push(Work.Clone()); if (_undo.Count > 60) { var keep = _undo.Take(40).Reverse().ToArray(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); } } }

    public override bool TryUndo()
    {
        if(HasPanelStep)return FinishPanelStep(false);
        if (_undo.Count == 0 || Work == null) return false;
        var previous=_undo.Pop();
        bool sameTopology=Work.V.Count==previous.V.Count && Work.F.Count==previous.F.Count && Work.F.Zip(previous.F).All(p=>p.First.SequenceEqual(p.Second));
        Work=previous;if(!sameTopology)ClearSelection();else _tool.SelectionChanged();OnChanged();
        _tool.Binding.Reset();
        S.Say("Undid last form step");
        return true;
    }

    // Running any panel action ends Insert edge loop, unless the action is the tool's own click.
    void Do(Action act, bool undoable = true, bool keepLoop = false)
    {
        if (!keepLoop) DisarmLoop();
        if (Work == null) { S.Say("Pick a form first.", true); return; }
        if (undoable) PushUndo();
        try { act(); OnChanged(); }
        catch (Exception e) when (e is not OutOfMemoryException) { if (undoable) Work = _undo.Pop(); S.Say(e.Message, true); }
    }

    // ---- loops, bridging, freezing, aligning
    void SelectLoop(bool ring)
    {
        var m = Work!; var edges = SelectedEdgeList();
        if (edges.Count == 0) throw new InvalidOperationException("Select an edge first (Select: Edges), then choose loop or ring.");
        var set = new HashSet<long>();
        foreach (var (a, b) in edges) set.UnionWith(ring ? FormOps.EdgeRing(m, a, b) : FormOps.EdgeLoop(m, a, b));
        if (set.Count == 0) throw new InvalidOperationException("Could not find a loop from that edge.");
        SetMode(FormMode.Edge); Verts.Clear(); Faces.Clear(); Edges.Clear();
        foreach (var k in set) Edges.Add(k);
        _tool.SelectionChanged();
        S.Say($"Selected {set.Count} edges. Move them with the arrows, or use Bridge, Crease, Extrude.");
    }

    void BridgeSel()
    {
        var edges = SelectedEdgeList();
        var chains = FormOps.Chains(edges);
        if (chains.Count != 2) throw new InvalidOperationException("Select two separate groups of open edges to bridge (for example two boundary loops of the form).");
        FormOps.Bridge(Work!, chains[0].verts, chains[0].closed, chains[1].verts, chains[1].closed);
        ClearSelection();
    }

    void FreezeSel(bool freeze)
    {
        var m = Work!; var vs = new HashSet<int>(Verts);
        foreach (var k in Edges) { var (a, b) = SubdMesh.Unkey(k); vs.Add(a); vs.Add(b); }
        foreach (var f in Faces) if (f < m.F.Count) foreach (var v in m.F[f]) vs.Add(v);
        if (vs.Count == 0) throw new InvalidOperationException("Select faces, edges or points first.");
        if (freeze) m.Freeze(vs); else m.Unfreeze(vs);
        S.Say(freeze ? $"Froze {vs.Count} points: they will not move until unfrozen (shown in blue)." : $"Unfroze {vs.Count} points.");
    }

    void AlignToCurve()
    {
        var m = Work!;
        if (_curve.Items.Count == 0) throw new InvalidOperationException("Pick the curve to align to (Curve to align to), then press Align.");
        var segs = new List<(Vec3 a, Vec3 b)>();
        foreach (var it in _curve.Items)
        {
            var sh = it.Kind == SelKind.Object ? S.Find(it.Obj)?.Shape : S.ShapeOf(it);
            if (sh == null) continue;
            foreach (var (pts, closed) in Core.Cam.CamGeom.Loops(sh, 0.05))
            {
                for (int i = 0; i + 1 < pts.Count; i++) segs.Add((pts[i], pts[i + 1]));
                if (closed && pts.Count > 2) segs.Add((pts[^1], pts[0]));
            }
        }
        if (segs.Count == 0) throw new InvalidOperationException("Could not read that curve.");
        double k = _alignStrength.Value / 100, range = _alignRange.Value; int moved = 0;
        foreach (int v in MovingOrAll())
        {
            var p = m.V[v]; Vec3 best = p; double bd = double.MaxValue;
            foreach (var (a, b) in segs)
            {
                var ab = b - a; double l2 = Math.Max(Vec3.Dot(ab, ab), 1e-18); double t = Math.Clamp(Vec3.Dot(p - a, ab) / l2, 0, 1);
                var q = a + ab * t; double d = (q - p).Length; if (d < bd) { bd = d; best = q; }
            }
            if (range > 0 && bd > range) continue;
            m.V[v] = p + (best - p) * k; moved++;
        }
        S.Say($"Moved {moved} points toward the curve.");
    }

    void ExtrudeSel()
    {
        var m = Work!;
        if (SelMode == FormMode.Face && Faces.Count > 0)
        {
            if (Faces.Any(f => f < m.F.Count && m.FaceFrozen(f))) throw new InvalidOperationException("Some of those faces are frozen. Unfreeze them first.");
            FormOps.ExtrudeFaces(m, Faces, _dist.Value);
            Verts.Clear(); Edges.Clear();
        }
        else if (SelMode == FormMode.Edge && Edges.Count > 0)
        {
            var top = FormOps.ExtrudeEdges(m, Edges.Select(SubdMesh.Unkey));
            if (top.Count == 0) throw new InvalidOperationException("Only boundary (open) edges can be extruded.");
            Edges.Clear(); foreach (var (a, b) in top) { Edges.Add(SubdMesh.Key(a, b)); m.V[a] += EdgeOutward(m, a) * _dist.Value; m.V[b] += EdgeOutward(m, b) * _dist.Value; }
        }
        else throw new InvalidOperationException("Select faces or open edges to extrude.");
        _tool.SelectionChanged();
    }

    static Vec3 EdgeOutward(SubdMesh m, int v)
    {
        // direction of the last quad's outward normal at this vertex
        var topo = m.Topo(); Vec3 n = Vec3.Zero;
        foreach (var f in topo.VertFaces[v]) n += m.FaceNormal(f);
        return n.Normalized();
    }

    /// <summary>Starts the Insert edge loop tool. Anything already selected gets its loop straight away; then every click on an edge adds one.</summary>
    void ArmLoop()
    {
        if (Work == null) { S.Say("Pick a form first.", true); return; }
        bool selected = SelectedEdgeList().Count > 0;
        if (selected) Do(() => InsertLoopThrough(SelectedEdgeList(), SelMode == FormMode.Face, true));
        LoopArmed = true;
        if (!selected) S.Say("Insert edge loop: click an edge to add a loop across it. Press Esc or choose another tool to finish.");
        _tool.ResetLoopPreview(); S.RaiseState();
    }

    /// <summary>One click of the Insert edge loop tool.</summary>
    public void InsertLoopAt(long edge)
    {
        if (!LoopArmed || Work == null) return;
        var (a, b) = SubdMesh.Unkey(edge);
        Do(() => InsertLoopThrough([(a, b)], false, false), keepLoop: true);
        _tool.ResetLoopPreview();
    }

    void InsertLoopThrough(List<(int a, int b)> edges, bool everyEdge, bool selectResult)
    {
        var m = Work!;
        if (edges.Count == 0) throw new InvalidOperationException("Select an edge (or a face) to insert a loop through.");
        var newVerts = new List<int>();
        var topoCheck = m.Topo();
        foreach (var (a, b) in edges)
            foreach (var fi in FormOps.EdgeRing(m, a, b).SelectMany(k => topoCheck.EdgeFaces.TryGetValue(k, out var l) ? l : []))
                if (m.FaceFrozen(fi)) throw new InvalidOperationException("The loop would cut through frozen faces. Unfreeze them first.");
        foreach (var (a, b) in edges)
        {
            if (!m.Topo().EdgeFaces.ContainsKey(SubdMesh.Key(a, b))) continue;
            newVerts.AddRange(FormOps.InsertEdgeLoops(m, a, b, _loopCount.Value, _loopPos.Value));
            if (!everyEdge) break;     // one loop (or one set of loops) per click unless faces are selected
        }
        if (newVerts.Count == 0) throw new InvalidOperationException("A loop cannot be inserted through that edge.");
        ClearSelection();
        if (selectResult) { SetMode(FormMode.Vertex); foreach (var v in newVerts) Verts.Add(v); }
        _tool.SelectionChanged();
        if (!selectResult) S.Say(_loopCount.Value == 1 ? "Inserted a loop. Click another edge to add more; Esc to finish." : $"Inserted {_loopCount.Value} loops. Click another edge to add more; Esc to finish.");
    }

    void InsetSel()
    {
        if (SelMode != FormMode.Face || Faces.Count == 0) throw new InvalidOperationException("Select faces (Select: Faces) to inset.");
        if (Faces.Any(f => f < Work!.F.Count && Work.FaceFrozen(f))) throw new InvalidOperationException("Some of those faces are frozen. Unfreeze them first.");
        FormOps.InsetFaces(Work!, Faces, _insetAmt.Value, _insetDepth.Value, _insetEach.Value);
        Verts.Clear(); Edges.Clear();
        _tool.SelectionChanged();
        S.Say("Inset done: the selected faces are now smaller, with a ring of new faces around them.");
    }

    void BevelSel()
    {
        var edges = SelectedEdgeList();
        if (edges.Count == 0) throw new InvalidOperationException("Select edges (or faces) to bevel.");
        int n = FormOps.BevelEdges(Work!, edges, _bevelW.Value);
        if (n == 0) throw new InvalidOperationException("Could not bevel those edges (they need quad faces beside them).");
        ClearSelection();
        S.Say($"Added {n} support loops {_bevelW.Value:0.##} mm either side of the edges: the surface now turns a tighter corner there.");
    }

    void SetMode(FormMode m) { Mode.Changed = null; Mode.Index = (int)m; Mode.Changed = SelectionModeChanged; S.RaiseState(); }

    void DeleteSel()
    {
        var m = Work!; var del = new HashSet<int>();
        switch (SelMode)
        {
            case FormMode.Face: del.UnionWith(Faces); break;
            case FormMode.Edge:
                var topo = m.Topo();
                foreach (var k in Edges) if (topo.EdgeFaces.TryGetValue(k, out var l)) del.UnionWith(l);
                break;
            default:
                for (int i = 0; i < m.F.Count; i++) if (m.F[i].Any(Verts.Contains)) del.Add(i);
                break;
        }
        if (del.Count == 0) throw new InvalidOperationException("Nothing selected to delete.");
        if (del.Any(f => m.FaceFrozen(f))) throw new InvalidOperationException("Some of those faces are frozen. Unfreeze them first.");
        FormOps.DeleteFaces(m, del);
        ClearSelection();
    }

    // ---- API for the tool
    public void Moved() { OnChanged(); }
    public void SelectionNotify() { UpdateVisibility(); }

    SubdMesh? _subdivideBefore;
    public void OpenSubdivide()
    {
        DisarmLoop();
        if(DeferPanelStep("Subdivide",OpenSubdivide) || Work==null)return;
        MirrorSel();
        _subdivideBefore=Work.Clone();
        bool some=Faces.Count>0;
        OpenPanelStep("Subdivide", (some?"Splits the selected faces":"Splits every face")+" into a grid. Set the divisions with a live preview, then apply to return to Edit Freeform. Faces that are not quads get one centre split.", [_subU,_subV],
            () => {if(_subdivideBefore!=null)_undo.Push(_subdivideBefore);_subdivideBefore=null;ClearSelection();S.Say("Subdivided the cage.");},
            () => {if(_subdivideBefore!=null)Work=_subdivideBefore;_subdivideBefore=null;});
        PreviewSubdivide();
    }
    void PreviewSubdivide()
    {
        if(_subdivideBefore==null || Work==null)return;
        Work=_subdivideBefore.Clone();
        var target=Faces.Count>0?Faces:Enumerable.Range(0,Work.F.Count).ToHashSet();
        FormOps.SubdivideFaces(Work,target,_subU.Value,_subV.Value);
        OnChanged();
    }

    void ToggleSmooth()
    {
        if(Work!.Level>0){_lastLevel=Work.Level;_level.Value=0;}
        else _level.Value=_lastLevel>0?_lastLevel:3;
        Work.Level=_level.Value;
        S.Say(Work.Level==0?"Showing the cage faces (smoothing off).":$"Smooth surface on ({Work.Level} levels).");
    }
    public bool SmoothOn=>Work is{Level:>0};
    public void SelectEdgeLoopKind(bool ring){_loopKind.Index=ring?1:0;Do(()=>SelectLoop(ring),undoable:false);}

    SubdMesh? _creaseBefore;
    public void OpenCrease()
    {
        DisarmLoop();
        if(DeferPanelStep("Crease",OpenCrease) || Work==null)return;
        if(SelMode==FormMode.Object || !MovingVertices().Any()){S.Say("Select edges, vertices or faces to crease first.",true);return;}
        MirrorSel();
        _creaseBefore=Work.Clone();
        var keys=SelMode==FormMode.Vertex ? Verts.Select(v=>SubdMesh.Key(v,v)) : SelectedEdgeList().Select(e=>SubdMesh.Key(e.a,e.b));
        var amounts=keys.Select(k=>Work.Crease.GetValueOrDefault(k)).ToList();
        var changed=_crease.Changed;_crease.Changed=null;_crease.Value=amounts.Count>0?amounts.Average():1;_crease.Changed=changed;
        OpenPanelStep("Crease", "Adjust sharpness with a live preview. Zero removes the crease. Apply or cancel returns to Edit Freeform with your selection intact.", [_crease],
            () => {if(_creaseBefore!=null)_undo.Push(_creaseBefore);_creaseBefore=null;},
            () => {if(_creaseBefore!=null)Work=_creaseBefore;_creaseBefore=null;});
    }
    void PreviewCrease()
    {
        if(_creaseBefore==null || Work==null){OnChanged();return;}
        Work=_creaseBefore.Clone();
        if(SelMode==FormMode.Vertex)FormOps.SetVertexCrease(Work,Verts,_crease.Value);
        else FormOps.SetCrease(Work,SelectedEdgeList(),_crease.Value);
        OnChanged();
    }
}

// ================================================================= create face
public sealed class CreateFaceCommand : Command
{
    readonly SelectionSlot _target;
    readonly ChoiceParam _plane;
    readonly NumberParam _offset, _scanOff;
    readonly BoolParam _snapScan;
    readonly CreateFaceTool _tool;
    public SubdMesh Work { get; private set; } = new();
    public SceneObject? Source { get; private set; }
    int _faces;

    public CreateFaceCommand()
    {
        _target = Slot(new SelectionSlot { Label = "Add to form (optional)", Filter = SelFilter.Form, Optional = true });
        _plane = Choice("Draw on plane", 0, "XY (ground)", "XZ (front)", "YZ (side)");
        _offset = Num("Plane offset", 0);
        _snapScan = Check("Place corners on the scan", true);
        _scanOff = Num("Float above the scan", 0, -20, 20, 0.2);
        Add(new ActionParam { Label = "Undo last face", Run = () => TryUndo() });
        Tool = _tool = new CreateFaceTool(this);
    }
    public override string Title => "Create Face";
    public override string Hint => "Click to place the corners of a face (click a corner of the form to connect). Enter or click the first point to finish a face.";
    public override string Icon => "form-face";
    public override string OkText => "Done";

    public override Plane3? WorkPlane
    {
        get
        {
            var p = _plane.Index == 0 ? Plane3.XY : _plane.Index == 1 ? Plane3.XZ : Plane3.YZ;
            return new Plane3(p.Origin + p.Normal * _offset.Value, p.U, p.V);
        }
    }

    public bool SnapToScan => _snapScan.Value && ScanSurface.Any(S);
    public double ScanOffset => _scanOff.Value;

    protected override void UpdateVisibility()
    {
        _snapScan.IsVisible = _scanOff.IsVisible = ScanSurface.Any(S);
        var item = _target.Items.Count > 0 ? S.Find(_target.Items[0].Obj) : null;
        if (item != Source)
        {
            Source = item; Work = item?.Form?.Clone() ?? new SubdMesh(); _snap.Clear(); _faces = 0;
        }
    }

    readonly Stack<SubdMesh> _snap = new();
    public override bool TryUndo()
    {
        if (_snap.Count == 0) return false;
        Work = _snap.Pop(); _faces--; OnChanged(); S.Say("Removed last face"); return true;
    }

    public void AddPolygon(List<(int existing, Vec3 pos)> pts, Vec3 facing)
    {
        _snap.Push(Work.Clone());
        var ids = new List<int>();
        foreach (var (existing, pos) in pts)
        {
            if (existing >= 0) ids.Add(existing);
            else { ids.Add(Work.V.Count); Work.V.Add(pos); }
        }
        FormOps.AddFace(Work, ids, facing);
        _faces++;
        OnChanged();
    }

    protected override CommandOutput? Build()
    {
        if (Work.IsEmpty) return null;
        var o = new CommandOutput { Label = "Create Face" };
        if (Source != null) o.ReplaceForms.Add((Source, Work));
        else o.Add.Add(new(null, "Form", ObjKind.Form, null, null, Work));
        return o;
    }
}
