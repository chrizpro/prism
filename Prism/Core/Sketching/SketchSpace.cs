using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

public enum SketchGizmoOperation { Move, Rotate, Size }

/// <summary>Work planes share a sketch document, but each retains its own planar constraint system.</summary>
public sealed partial class SketchContext
{
    List<SketchSection> _sections = [];
    int _activeSection;
    sealed record SpaceSnapshot(List<SketchSection> Sections, int Active, double Size, Plane3 Gizmo, int GizmoPlane, Guid? Datum, double TargetScale);
    Plane3 _gizmoFrame;
    public Plane3 GizmoFrame => _gizmoFrame;
    public int GizmoPlaneIndex { get; private set; }
    public bool GizmoFollowsSketch { get; set; } = true;
    public int SelectedGizmoAxis { get; set; }
    int _gizmoHover = -1, _inferredVisualAxis = -1;
    Vec3? _axisPreview;
    public string GizmoPlaneName => GizmoPlaneIndex == 0 ? "XY" : GizmoPlaneIndex == 1 ? "XZ" : "YZ";
    public Plane3 GizmoDrawingPlane
    {
        get
        {
            var axes = Basis;
            return GizmoPlaneIndex == 0 ? new(_gizmoFrame.Origin, axes[0], axes[1]) : GizmoPlaneIndex == 1 ? new(_gizmoFrame.Origin, axes[0], axes[2]) : new(_gizmoFrame.Origin, axes[1], axes[2]);
        }
    }
    public IReadOnlyList<SketchSection> DrawingPlanes => _sections;
    public int ActivePlane => _activeSection;
    public Plane3 RootPlane => _sections[0].Plane;
    public bool GizmoEditing { get; private set; }
    /// <summary>When on, transforming points is not restricted to their drawing plane: they can be lifted off it along any direction.</summary>
    public bool FreeEditing { get; set; }
    public bool Is3DEnabled { get; private set; }
    public SketchGizmoOperation GizmoOperation { get; set; }
    public double GizmoSize { get; private set; } = 1;
    public bool SnapOtherCurves { get; set; }
    public bool ProjectOtherCurves { get; set; }
    public int DrawingAxis { get; private set; }
    Vec3? _lastPlacement;
    int _gizmoDrag = -1;
    double _dragX, _dragY, _dragSize;
    Plane3 _dragPlane;
    SpaceSnapshot? _dragSnapshot;
    bool _dragChanged;
    bool _suppressFollow;

    void InitializeSpace(Plane3 plane, SketchModel? source)
    {
        var model = source?.Clone() ?? new SketchModel();
        var children = model.Sections;
        Is3DEnabled = model.SpatialEditing ?? children.Count > 0;
        model.Sections = [];
        _sections = [new() { Name = "Plane 1", Plane = plane, Model = model }];
        _sections.AddRange(children);
        _gizmoFrame = plane;
    }

    public SketchModel ExportModel()
    {
        var root = _sections[0].Model.Clone();
        root.Sections = _sections.Skip(1).Select(section => section.Clone()).ToList();
        root.SpatialEditing = Is3DEnabled;
        return root;
    }

    public void Set3DEnabled(CadSession s, bool enabled)
    {
        if (enabled == Is3DEnabled) return;
        if (_gizmoDrag >= 0) { _dragChanged = false; EndGizmoDrag(s); }
        bool wasEditing = GizmoEditing;
        Is3DEnabled = enabled; GizmoEditing = false; DrawingAxis = 0;
        if (enabled && GizmoFollowsSketch && _lastPlacement is { } last) _gizmoFrame = new(last, _gizmoFrame.U, _gizmoFrame.V);
        if (!enabled && wasEditing) SetTool(s, new SelectTool());
        if (!enabled) ProjectOtherCurves = false;
        s.NotifySketchChanged(); s.RaiseState(); s.Invalidate();
        s.Say(enabled ? "3D sketch: drawing planes and spatial controls are available." : "2D sketch: drawing on the active plane. Geometry on other planes is preserved.");
    }

    SpaceSnapshot CaptureSpace(SketchModel? before = null)
    {
        var planes = _sections.Select(section => section.Clone()).ToList();
        if (before != null) planes[_activeSection].Model = before.Clone();
        return new(planes, _activeSection, GizmoSize, _gizmoFrame, GizmoPlaneIndex, ActiveDatumId, GeometryScale);
    }

    void ClearPlaneSelection()
    {
        ClearGizmoTarget();
        SelEntities.Clear(); SelPoints.Clear(); Blamed.Clear(); SelConstraint = HoverEntity = HoverPoint = -1;
        Cursor = null; _lastPlacement = null; DrawingAxis = 0; _combKey = null;
        _inferredVisualAxis = -1; _axisPreview = null;
        _dimLabels.Clear(); _glyphs.Clear();
    }

    public void SelectPlane(CadSession s, int index)
    {
        if (index < 0 || index >= _sections.Count || index == _activeSection) return;
        Tool?.Deactivate(s);
        _activeSection = index; ClearPlaneSelection();
        _gizmoFrame = Plane; GizmoPlaneIndex = 0;
        Solve(s); s.RaiseState(); s.Invalidate();
        s.Say($"Editing {_sections[index].Name}. Constraints and dimensions apply within this plane.");
    }

    public void SetDrawingPlane(CadSession s, Plane3 plane, bool forceNew = false)
        => SetDrawingPlaneCore(s, plane, forceNew, syncGizmo: true, recordUndo: true);

    void SetDrawingPlaneCore(CadSession s, Plane3 plane, bool forceNew, bool syncGizmo, bool recordUndo)
    {
        if (!Valid(plane)) { s.Say("Choose a finite position and two perpendicular plane directions.", true); return; }
        Set3DEnabled(s, true);
        var snapshot = CaptureSpace();
        Tool?.Deactivate(s);
        if (forceNew || Model.Entities.Count > 0)
        {
            int existing = !forceNew ? _sections.FindIndex(section => SamePlane(section.Plane, plane)) : -1;
            if (existing >= 0)
            {
                var frame = _gizmoFrame; int selected = GizmoPlaneIndex;
                SelectPlane(s, existing); ClearPlaneSelection();
                _gizmoFrame = syncGizmo ? plane : frame; GizmoPlaneIndex = syncGizmo ? 0 : selected;
                s.NotifySketchChanged(); s.Invalidate(); return;
            }
            _sections.Add(new() { Name = $"Plane {_sections.Count + 1}", Plane = plane });
            _activeSection = _sections.Count - 1;
        }
        else _sections[_activeSection].Plane = plane;
        if (syncGizmo) { _gizmoFrame = plane; GizmoPlaneIndex = 0; }
        if (recordUndo) _undo.Push(snapshot);
        ClearPlaneSelection(); Solve(s); s.RaiseState(); s.Invalidate();
    }

    static bool SamePlane(Plane3 a, Plane3 b) => a.Origin.DistanceTo(b.Origin) < 1e-8 && a.U.DistanceTo(b.U) < 1e-8 && a.V.DistanceTo(b.V) < 1e-8;
    static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    static bool Valid(Plane3 p) => Finite(p.Origin) && Finite(p.U) && Finite(p.V) && Math.Abs(p.U.Length - 1) < 1e-6 && Math.Abs(p.V.Length - 1) < 1e-6 && Math.Abs(Vec3.Dot(p.U, p.V)) < 1e-6;

    public void PresetPlane(CadSession s, int index)
    {
        if (!GizmoEditing) { s.Say("Press G to edit the gizmo before choosing a plane."); return; }
        _gizmoFrame = new(_gizmoFrame.Origin, Vec3.UnitX, Vec3.UnitY);
        SelectGizmoPlane(s, index);
    }

    public void SelectGizmoPlane(CadSession s, int index)
    {
        if (!GizmoEditing) { s.Say("Press G to edit the gizmo before choosing a plane."); return; }
        SetGizmoPlane(s,index);
    }

    void SetGizmoPlane(CadSession s, int index)
    {
        if (index is < 0 or > 2) return;
        var frame = _gizmoFrame;
        GizmoPlaneIndex = index;
        if (!GeometryEditing) ApplyGizmoDrawingPlane(s);
        _gizmoFrame = frame; GizmoPlaneIndex = index;
        s.NotifySketchChanged(); s.Invalidate();
    }

    void ApplyGizmoDrawingPlane(CadSession s, bool recordUndo = true)
    {
        var previous = Plane;
        var frame = GizmoDrawingPlane;
        // Moving within the existing support plane must not rebase its local coordinates or disturb pending clicks.
        bool sameSupport = Math.Abs(Vec3.Dot(frame.Origin - previous.Origin, previous.Normal)) < 1e-7 && frame.U.DistanceTo(previous.U) < 1e-7 && frame.V.DistanceTo(previous.V) < 1e-7;
        if (sameSupport && (Model.Entities.Count > 0 || (Tool as ClickTool)?.LastPlacedWorld(s) != null || SamePlane(previous, frame))) return;
        var anchor = (Tool as ClickTool)?.LastPlacedWorld(s);
        SketchTool? continuation = Tool switch
        {
            LineTool line => new LineTool(line.OptionIndex),
            RectTool rect => new RectTool((RectMode)rect.OptionIndex),
            EllipseTool ellipse => new EllipseTool((EllipseMode)ellipse.OptionIndex,ellipse.CircleLocked),
            CircleTool circle => new CircleTool((CircleMode)circle.OptionIndex), _ => null,
        };
        bool gizmoEditing = GizmoEditing;
        SetDrawingPlaneCore(s, frame, false, syncGizmo: false, recordUndo: recordUndo);
        if (continuation != null)
        {
            SetTool(s, continuation);
            if (anchor is { } point && Math.Abs(Vec3.Dot(point - Plane.Origin, Plane.Normal)) < 1e-6)
            {
                var uv = Plane.Project(point); _suppressFollow = true;
                try { ((ClickTool)continuation).Feed(s, uv.u, uv.v); } finally { _suppressFollow = false; }
            }
            GizmoEditing = gizmoEditing;
        }
    }

    public void SetGizmoEditing(CadSession s, bool editing)
    {
        if (editing && !PlanarGizmoActive) Set3DEnabled(s, true);
        if (_gizmoDrag >= 0) EndGizmoDrag(s);
        if (editing && Tool is not ClickTool) Tool?.Deactivate(s);
        if(!editing&&GizmoEditing)LockCurrentGizmoTarget();
        if(!editing)IndependentGizmoPlacement=false;
        GizmoEditing = editing; DrawingAxis = 0;
        s.Say(editing ? GeometryEditing ? "Gizmo setup: handles move only the gizmo. Select points or curves (Shift adds); Enter locks the target at their center and returns to geometry editing." : "Gizmo: drag an axis or plane to move, a ring to rotate, or the size handle. G resumes drawing; E edits curves; N looks normal." : "Edit curves: select and drag geometry; constraints stay satisfied.");
        s.RaiseState(); s.Invalidate();
    }

    public void LookNormal(CadSession s)
    {
        s.Camera = s.Cameras[0];
        var target = Is3DEnabled ? GizmoDrawingPlane : Plane;
        var n = target.Normal;
        if (Vec3.Dot(s.Camera.EyeDirection, n) > .999) n = -n;
        s.Camera.Target = target.Origin;
        s.AnimateView(Math.Abs(n.Z) > .9999 ? s.Camera.Yaw : Math.Atan2(n.X, -n.Y), Math.Asin(Math.Clamp(n.Z, -1, 1)));
        s.Say($"Looking normal to the gizmo {GizmoPlaneName} plane. N reverses the view; drag to orbit.");
    }

    public void MoveDrawingPlane(CadSession s, Vec3 origin) => SetGizmoTransform(s, origin, GizmoRotationDegrees);
    public void RotateDrawingPlane(CadSession s, Vec3 degrees)
    {
        if (!Finite(degrees)) return;
        Vec3 Turn(Vec3 v) => Rotate(Rotate(Rotate(v, Vec3.UnitX, degrees.X * Math.PI / 180), Vec3.UnitY, degrees.Y * Math.PI / 180), Vec3.UnitZ, degrees.Z * Math.PI / 180);
        var rotated = new Plane3(_gizmoFrame.Origin, Turn(_gizmoFrame.U).Normalized(), Turn(_gizmoFrame.V).Normalized());
        CommitGizmoFrame(s, rotated);
    }

    public Vec3 GizmoRotationDegrees
    {
        get
        {
            var f = _gizmoFrame; double pitch = Math.Asin(Math.Clamp(-f.U.Z, -1, 1));
            double roll = Math.Abs(Math.Cos(pitch)) > 1e-7 ? Math.Atan2(f.V.Z, f.Normal.Z) : 0;
            double yaw = Math.Abs(Math.Cos(pitch)) > 1e-7 ? Math.Atan2(f.U.Y, f.U.X) : Math.Atan2(-f.V.X, f.V.Y);
            return new(roll * 180 / Math.PI, pitch * 180 / Math.PI, yaw * 180 / Math.PI);
        }
    }

    public void SetGizmoTransform(CadSession s, Vec3 origin, Vec3 degrees)
    {
        if (!Finite(origin) || !Finite(degrees)) { s.Say("Enter finite gizmo coordinates.", true); return; }
        Vec3 Turn(Vec3 v) => Rotate(Rotate(Rotate(v, Vec3.UnitX, degrees.X * Math.PI / 180), Vec3.UnitY, degrees.Y * Math.PI / 180), Vec3.UnitZ, degrees.Z * Math.PI / 180);
        CommitGizmoFrame(s, new(origin, Turn(Vec3.UnitX).Normalized(), Turn(Vec3.UnitY).Normalized()));
    }

    void CommitGizmoFrame(CadSession s, Plane3 frame)
    {
        if (!Valid(frame) || SamePlane(frame, _gizmoFrame)) return;
        if (GeometryEditing && !GizmoEditing && HasGeometrySelection) { TransformGeometry(s,frame,1); return; }
        var before = CaptureSpace(); _gizmoFrame = frame;
        if (!GeometryEditing) ApplyGizmoDrawingPlane(s, recordUndo: false);
        _undo.Push(before); s.NotifySketchChanged(); s.RaiseState(); s.Invalidate();
    }

    public void ResetGizmo(CadSession s, bool location = true, bool rotation = true, bool scale = true)
    {
        if(GeometryEditing&&!GizmoEditing&&HasGeometrySelection)
        {
            var frame=new Plane3(location?Vec3.Zero:_gizmoFrame.Origin,rotation?Vec3.UnitX:_gizmoFrame.U,rotation?Vec3.UnitY:_gizmoFrame.V);
            TransformGeometry(s,frame,scale?1/GeometryScale:1);return;
        }
        var before = CaptureSpace();
        _gizmoFrame = new(location ? Vec3.Zero : _gizmoFrame.Origin, rotation ? Vec3.UnitX : _gizmoFrame.U, rotation ? Vec3.UnitY : _gizmoFrame.V);
        if (scale) GizmoSize = 1;
        if (!GeometryEditing) ApplyGizmoDrawingPlane(s, recordUndo: false);
        _undo.Push(before); s.NotifySketchChanged(); s.RaiseState(); s.Invalidate();
    }

    public void SetGizmoSize(CadSession s, double size)
    {
        if (!double.IsFinite(size) || size < .25 || size > 8) { s.Say("Plane display size must be between 0.25 and 8.", true); return; }
        if(GeometryEditing&&!GizmoEditing&&HasGeometrySelection)
        { if(Math.Abs(size-GeometryScale)>1e-8)TransformGeometry(s,_gizmoFrame,size/GeometryScale);return; }
        if (Math.Abs(size - GizmoSize) < 1e-8) return;
        Push(); GizmoSize = size; s.NotifySketchChanged(); s.Invalidate();
    }

    static Vec3 Rotate(Vec3 v, Vec3 axis, double angle) => v * Math.Cos(angle) + Vec3.Cross(axis, v) * Math.Sin(angle) + axis * (Vec3.Dot(axis, v) * (1 - Math.Cos(angle)));
    Vec3[] Basis => [_gizmoFrame.U, _gizmoFrame.V, _gizmoFrame.Normal];
    double HandleLength(CadSession s) => GizmoVisual.Length(s, _gizmoFrame.Origin);
    int ActiveGizmoDisplayOperation => GizmoEditing || GeometryEditing ? (int)GizmoOperation : 0;

    // Handles: 0 center; 1..3 axes; 4..6 plane squares; 7..9 rotation rings; 10 display size.
    public int GizmoHit(CadSession s, double x, double y, bool editing)
    {
        if (!Is3DEnabled && !PlanarGizmoActive) return -1;
        var basis = Basis; var origin = _gizmoFrame.Origin; double length = HandleLength(s);
        var display = GizmoSettings.Current; int active = ActiveGizmoDisplayOperation;
        double best = display.Tolerance; int handle = -1;
        void Test(Vec3 p, int id, double tolerance = 0)
        {
            var q = s.Camera.Project(p); if (q == null) return;
            double dist = Math.Sqrt(Math.Pow(q.Value.x - x, 2) + Math.Pow(q.Value.y - y, 2));
            if (dist < (tolerance > 0 ? tolerance : display.Tolerance) && dist < best) { best = dist; handle = id; }
        }
        bool transforming = GeometryEditing && !GizmoEditing && HasGeometrySelection;
        if ((editing || transforming) && display.Allows((int)GizmoOperation, active)) Test(origin, 0);
        var projectedOrigin = s.Camera.Project(origin);
        for (int i = 0; i < 3; i++)
        {
            if (!display.AxisVisible(i)||!display.Allows(0, active) || PlanarGizmoActive && i == 2) continue;
            var tip = origin + basis[i] * length; var q = s.Camera.Project(tip);
            if (q != null && projectedOrigin != null && Math.Abs(q.Value.x - projectedOrigin.Value.x) + Math.Abs(q.Value.y - projectedOrigin.Value.y) > 20) Test(tip, i + 1);
        }
        // Translation arrows are draggable along the visible shaft, not just at their tips.
        if(display.Allows(0, active) && projectedOrigin is { } po && handle!=0)
        {
            double nearest=display.Tolerance*.8;int axisHit=-1;
            for(int i=0;i<3;i++)
            {
                if (!display.AxisVisible(i)||PlanarGizmoActive && i == 2) continue;
                var tip=s.Camera.Project(origin+basis[i]*length);if(tip==null)continue;
                double dx=tip.Value.x-po.x,dy=tip.Value.y-po.y;
                if(dx*dx+dy*dy<400)continue;
                var (t,distance)=Picking.SegDist(x,y,po.x+dx*.22,po.y+dy*.22,tip.Value.x,tip.Value.y);
                if(distance<nearest){nearest=distance;axisHit=i+1;}
            }
            if(axisHit>=0)return axisHit;
        }
        int[][] pairs = [[0, 1], [0, 2], [1, 2]];
        if ((GizmoEditing || GeometryEditing) && (editing || transforming) && display.PlaneHandles && display.Allows(0,active))
            for (int i = 0; i < 3; i++)
                if (display.AxisVisible(pairs[i][0])&&display.AxisVisible(pairs[i][1])&&(!PlanarGizmoActive || i == GizmoPlaneIndex)) Test(origin + (basis[pairs[i][0]] + basis[pairs[i][1]]) * (length * .28), i + 4);
        // Position the gizmo in G mode; outside G, Edit Sketch handles transform its selected target.
        if (editing || transforming)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if(!display.AxisVisible(axis)||!display.Allows(1,active) || PlanarGizmoActive&&axis!=2)continue;
                var a = basis[(axis + 1) % 3]; var b = basis[(axis + 2) % 3];
                for (int i = 0; i < 72; i++)
                {
                    double angle = i * Math.PI / 36;
                    var point = origin + (a * Math.Cos(angle) + b * Math.Sin(angle)) * length * .8;
                    var q = s.Camera.Project(point);
                    if (q != null && projectedOrigin != null && Math.Abs(q.Value.x - projectedOrigin.Value.x) + Math.Abs(q.Value.y - projectedOrigin.Value.y) > 20)
                        Test(point, axis + 7, display.Tolerance*.8);
                }
            }
            if (display.Allows(2,active)) Test(GizmoVisual.UniformPoint(s, origin, length), 10);
        }
        return handle;
    }

    bool LockedTargetClick(CadSession s)
    {
        s.Say("The gizmo is locked to its target. Drag its handles to transform it, or press G to choose a different target.");
        s.Invalidate(); return true;
    }

    public bool SpacePointerDown(CadSession s, PointerInfo p)
    {
        if (!Is3DEnabled && !PlanarGizmoActive) return p.Left && !p.Alt && Tool is SelectTool && PickDatum(s,p.X,p.Y);
        if (!p.Left || p.Alt) return false;
        bool editing = GizmoEditing || p.Ctrl;
        bool transforming = GeometryEditing && !GizmoEditing && !p.Ctrl && HasGeometrySelection;
        int hit = GizmoHit(s, p.X, p.Y, editing);
        if (GeometryEditing && !p.Ctrl && !(transforming && hit>=0) && PickPoint(s,p.X,p.Y)>=0)
        {
            if (GizmoEditing) { _gizmoTargetPicker.PointerDown(s,p);PreviewGizmoTarget(s);return true; }
            // Outside Gizmo Handles the bound target stays locked: clicking another point must not retarget the gizmo.
            return LockedTargetClick(s);
        }
        // Points and curves on another drawing plane (Gizmo Handles only): switch to that plane and pick the target there in the same click.
        if (GeometryEditing && GizmoEditing && !p.Ctrl && hit < 0 && PickPoint(s,p.X,p.Y) < 0 && PickEntity(s,p.X,p.Y) < 0 && PickOtherPlane(s,p.X,p.Y) is >= 0 and int otherPlane)
        {
            if (p.Shift) { s.Say("Shift-click adds targets on the active drawing plane only. Click a point on another plane (without Shift) to switch to it.",true); return true; }
            SelectPlane(s,otherPlane);
            _gizmoTargetPicker.PointerDown(s,p); PreviewGizmoTarget(s);
            return true;
        }
        if (GeometryEditing && !GizmoEditing && !p.Ctrl && hit < 0) return LockedTargetClick(s);
        if (hit < 0 && !editing && Tool is ClickTool && _axisPreview is { } candidate && _inferredVisualAxis >= 0 && Math.Abs(Vec3.Dot(candidate - Plane.Origin,Plane.Normal)) > 1e-6)
        {
            int axis = _inferredVisualAxis;
            SetGizmoPlane(s,axis == 2 ? 1 : axis == 1 ? 0 : 1);
            _axisPreview = candidate; _inferredVisualAxis = axis;
        }
        if (hit < 0 && GizmoEditing && Tool is not ClickTool && PickDatum(s,p.X,p.Y)) return true;
        if (!editing && !transforming)
        {
            if (Tool is ClickTool && hit is >= 1 and <= 3)
            {
                int axis = hit - 1; int[][] axesByPlane = [[0, 1], [0, 2], [1, 2]];
                SelectedGizmoAxis = axis;
                if (!axesByPlane[GizmoPlaneIndex].Contains(axis)) SetGizmoPlane(s, axis == 2 ? 1 : 0);
                int localAxis = Array.IndexOf(axesByPlane[GizmoPlaneIndex], axis) + 1;
                DrawingAxis = DrawingAxis == localAxis ? 0 : localAxis;
                s.Say(DrawingAxis == 0 ? "Axis lock released." : $"Drawing along the plane's {(DrawingAxis == 1 ? "U" : "V")} axis. Click the arrow again to release."); s.Invalidate(); return true;
            }
            return false;
        }
        if (hit < 0 && GizmoEditing && Tool is SelectTool or SketchGeometryEditTool && PickOtherPlane(s,p.X,p.Y) is int other && other>=0)
        { SelectPlane(s,other); return true; }
        if (hit < 0 && GizmoEditing && GeometryEditing) { _gizmoTargetPicker.PointerDown(s,p);PreviewGizmoTarget(s);return true; }
        if (hit < 0) return GizmoEditing;
        if (hit is >= 1 and <= 6) GizmoOperation = SketchGizmoOperation.Move;
        else if (hit is >= 7 and <= 9) GizmoOperation = SketchGizmoOperation.Rotate;
        else if (hit == 10) GizmoOperation = SketchGizmoOperation.Size;
        if (hit is >= 1 and <= 3) SelectedGizmoAxis = hit - 1;
        _dragSnapshot = CaptureSpace();
        _geometryDrag=transforming; _geometryValid=true; _geometryError="";
        _gizmoDrag = hit; _dragX = p.X; _dragY = p.Y; _dragPlane = _gizmoFrame; _dragSize = GizmoSize; _dragChanged = false;
        s.Invalidate(); return true;
    }

    public bool SpacePointerMove(CadSession s, PointerInfo p)
    {
        if (!Is3DEnabled && !PlanarGizmoActive) return false;
        if (_gizmoDrag < 0)
        {
            int hover = GizmoHit(s,p.X,p.Y,GizmoEditing || p.Ctrl);
            if (hover != _gizmoHover) { _gizmoHover = hover; s.Invalidate(); }
            _inferredVisualAxis = -1; _axisPreview = null;
            if (!GizmoEditing && !p.Ctrl && !p.Alt && Tool is ClickTool)
            {
                var origin = s.Camera.Project(GizmoFrame.Origin);
                if (origin is { } q)
                {
                    double best = 7, length = HandleLength(s);
                    var (rayOrigin,rayDirection) = s.Camera.Ray(p.X,p.Y);
                    for (int i = 0; i < 3; i++)
                    {
                        var axis = Basis[i]; var end = s.Camera.Project(GizmoFrame.Origin + axis * length); if (end == null) continue;
                        double axisDx = end.Value.x-q.x, axisDy=end.Value.y-q.y, l2=axisDx*axisDx+axisDy*axisDy; if(l2<400)continue;
                        double t=((p.X-q.x)*axisDx+(p.Y-q.y)*axisDy)/l2;
                        double distance=Math.Sqrt(Math.Pow(q.x+t*axisDx-p.X,2)+Math.Pow(q.y+t*axisDy-p.Y,2));
                        if(Math.Abs(t)*Math.Sqrt(l2)<22 || distance>=best)continue;
                        var w=GizmoFrame.Origin-rayOrigin; double b=Vec3.Dot(axis,rayDirection),den=1-b*b;
                        if(den<1e-4)continue;
                        double along=(b*Vec3.Dot(rayDirection,w)-Vec3.Dot(axis,w))/den;
                        _axisPreview=GizmoFrame.Origin+axis*along; _inferredVisualAxis=i; best=distance;
                    }
                    if (_axisPreview is { } point) s.Cursor3=point;
                }
            }
            if (GizmoEditing && GeometryEditing) _gizmoTargetPicker.PointerMove(s,p);
            return GizmoEditing;
        }
        double dx = p.X - _dragX, dy = p.Y - _dragY;
        if (Math.Abs(dx) + Math.Abs(dy) < 2 && !_dragChanged) return true;
        _dragChanged = true;
        var axes = new[] { _dragPlane.U, _dragPlane.V, _dragPlane.Normal };
        if (_gizmoDrag == 10 || GizmoOperation == SketchGizmoOperation.Size && _gizmoDrag == 0)
            GizmoSize = Math.Clamp(_dragSize * GizmoVisual.SnapScale(Math.Exp((dx - dy) / 180), p.Alt), .25, 8);
        else if (_gizmoDrag == 0 && GizmoOperation == SketchGizmoOperation.Rotate)
        {
            Vec3 Turn(Vec3 v) => PlanarGizmoActive?Rotate(v,Plane.Normal,GizmoVisual.SnapAngle((dx-dy)*.01,p.Alt)):Rotate(Rotate(v, s.Camera.Up, GizmoVisual.SnapAngle(dx * .01,p.Alt)), s.Camera.Right, GizmoVisual.SnapAngle(dy * .01,p.Alt));
            _gizmoFrame = new(_dragPlane.Origin, Turn(_dragPlane.U).Normalized(), Turn(_dragPlane.V).Normalized());
        }
        else if (_gizmoDrag >= 7 || GizmoOperation == SketchGizmoOperation.Rotate && _gizmoDrag == 0)
        {
            int axis = _gizmoDrag >= 7 ? _gizmoDrag - 7 : Math.Clamp(_gizmoDrag - 1, 0, 2);
            var a = axes[axis]; double angle = GizmoVisual.SnapAngle((dx - dy) * .01,p.Alt);
            _gizmoFrame = new(_dragPlane.Origin, Rotate(_dragPlane.U, a, angle).Normalized(), Rotate(_dragPlane.V, a, angle).Normalized());
        }
        else
        {
            var delta = (s.Camera.Right * dx - s.Camera.Up * dy) * s.Camera.PixelSizeAt(_dragPlane.Origin);
            if (_gizmoDrag is >= 1 and <= 3)
            {
                var a = axes[_gizmoDrag - 1];
                var q0 = s.Camera.Project(_dragPlane.Origin); var q1 = s.Camera.Project(_dragPlane.Origin + a);
                if (q0 != null && q1 != null)
                {
                    double ax = q1.Value.x - q0.Value.x, ay = q1.Value.y - q0.Value.y, den = ax * ax + ay * ay;
                    delta = den > 1e-6 ? a * ((dx * ax + dy * ay) / den) : Vec3.Zero;
                }
            }
            else if (_gizmoDrag is >= 4 and <= 6)
            {
                int[][] pairs = [[0, 1], [0, 2], [1, 2]]; var pair = pairs[_gizmoDrag - 4];
                var dragPlane = new Plane3(_dragPlane.Origin, axes[pair[0]], axes[pair[1]]);
                var (o0, d0) = s.Camera.Ray(_dragX, _dragY); var (o1, d1) = s.Camera.Ray(p.X, p.Y);
                if (dragPlane.Intersect(o0, d0) is { } a && dragPlane.Intersect(o1, d1) is { } b) delta = b - a;
                else delta = dragPlane.U * Vec3.Dot(delta, dragPlane.U) + dragPlane.V * Vec3.Dot(delta, dragPlane.V);
            }
            if((_geometryDrag && SelEntities.Count==0 && !FreeEditing || PlanarGizmoActive) && _gizmoDrag==0 && _dragSnapshot is { } pointSnapshot)
            {
                var normal=pointSnapshot.Sections[pointSnapshot.Active].Plane.Normal;
                delta-=normal*Vec3.Dot(delta,normal);
            }
                        // Quantize in the gizmo frame so an axis drag remains on its local axis.
            if (GizmoSettings.Current.Snap && !p.Alt)
            {
                double step = GizmoSettings.Current.MoveStep;
                if (double.IsFinite(step) && step > 1e-9)
                {
                    double Q(double value) => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
                    delta = axes[0]*Q(Vec3.Dot(delta,axes[0])) + axes[1]*Q(Vec3.Dot(delta,axes[1])) + axes[2]*Q(Vec3.Dot(delta,axes[2]));
                }
            }
            _gizmoFrame = new(_dragPlane.Origin + delta, _dragPlane.U, _dragPlane.V);
        }
        if(_geometryDrag&&_dragSnapshot is { } snapshot)
            _geometryValid=PreviewGeometryTransform(s,snapshot,_gizmoFrame,GizmoSize/_dragSize);
        s.NotifySketchChanged();
        s.Invalidate(); return true;
    }

    public bool EndGizmoDrag(CadSession s, PointerInfo? pointer = null)
    {
        if (_gizmoDrag < 0)
        {
            if (GizmoEditing && GeometryEditing && pointer is { } p) { _gizmoTargetPicker.PointerUp(s,p);PreviewGizmoTarget(s); return true; }
            return false;
        }
        int handle = _gizmoDrag; bool changed = _dragChanged;
        if (_dragSnapshot is { } snapshot)
        {
            if (_dragChanged)
            {
                if(_geometryDrag)
                {
                    if(_geometryValid)_undo.Push(snapshot);
                    else{RestoreSpace(snapshot);s.Say(_geometryError.Length>0?_geometryError:"That transform conflicts with the constraints.",true);}
                }
                else
                { if (!GeometryEditing && !SamePlane(snapshot.Gizmo, _gizmoFrame)) ApplyGizmoDrawingPlane(s, recordUndo: false); _undo.Push(snapshot); }
            }
            else { _sections = snapshot.Sections; _activeSection = snapshot.Active; GizmoSize = snapshot.Size; _gizmoFrame = snapshot.Gizmo; GizmoPlaneIndex = snapshot.GizmoPlane; GeometryScale=snapshot.TargetScale; }
        }
        _dragSnapshot = null; _gizmoDrag = -1;
        _geometryDrag=false;
        if (!changed && handle is >= 4 and <= 6) SelectGizmoPlane(s,handle-4);
        Solve(s); RememberGizmoTargetCenter();s.RaiseState(); s.Invalidate(); return true;
    }

    public bool SpaceKey(CadSession s, string key)
    {
        if(IndependentGizmoPlacement&&key=="Escape"){CancelPlacement(s,_independentGizmoBefore);return true;}
        if (!Is3DEnabled && !PlanarGizmoActive && key is "SketchGizmo" or "PlaneXY" or "PlaneXZ" or "PlaneYZ" or "GizmoMove" or "GizmoRotate" or "GizmoSize")
        { s.Say("Enable 3D sketch to use spatial drawing controls."); return true; }
        switch (key)
        {
            case "SketchGizmo":
                if (GizmoEditing)
                {
                    if (_gizmoDrag>=0) EndGizmoDrag(s);
                    LockCurrentGizmoTarget();GizmoEditing=false;IndependentGizmoPlacement=false; _gizmoTargetPicker.Deactivate(s);s.Say(Tool?.Prompt??"Choose a sketch tool.");s.RaiseState();s.Invalidate();
                }
                else SetGizmoEditing(s,true);
                return true;
            case "SketchEdit": ToggleGeometryEditing(s); return true;
            case "Enter" when GizmoEditing && GeometryEditing: ConfirmGizmoTarget(s); return true;
            case "LookAt": LookNormal(s); return true;
            case "PlaneXY": PresetPlane(s, 0); return true;
            case "PlaneXZ": PresetPlane(s, 1); return true;
            case "PlaneYZ": PresetPlane(s, 2); return true;
            case "GizmoMove": GizmoOperation = SketchGizmoOperation.Move; break;
            case "GizmoRotate": GizmoOperation = SketchGizmoOperation.Rotate; break;
            case "GizmoSize": GizmoOperation = SketchGizmoOperation.Size; break;
            case "Escape" when _gizmoDrag >= 0:
                _dragChanged = false; EndGizmoDrag(s); return true;
            case "Escape" when GizmoEditing: SetGizmoEditing(s, false); return true;
            default: return false;
        }
        s.RaiseState(); s.Invalidate(); return true;
    }

    public bool DrawingKey(CadSession s, string key)
    {
        if (GizmoEditing) return key.StartsWith("Char:");
        SketchTool? tool = key switch
        {
            "Char:L" => new LineTool(), "Char:R" => new RectTool(RectMode.TwoCorner),
            "Char:C" => new EllipseTool(EllipseMode.Center, circleLock:true), "Char:S" => new SplineTool(false),
            "Char:A" => new ArcTool(ArcMode.ThreePoint), "Char:D" => new DimensionTool(DimMode.Smart), _ => null,
        };
        if (tool == null) return false;
        SetTool(s, tool); return true;
    }

    public void RecordPlacement(CadSession s, Vec3 point)
    {
        _lastPlacement = point;
        if (!Is3DEnabled || !GizmoFollowsSketch || _suppressFollow) return;
        _gizmoFrame = new(point, _gizmoFrame.U, _gizmoFrame.V);
        s.NotifySketchChanged(); s.Invalidate();
    }
    public void ReleaseDrawingAxis(CadSession s) { DrawingAxis = 0; s.Invalidate(); }
    public void LockSelectedGizmoAxis(CadSession s)
    {
        int[][] pairs = [[0,1],[0,2],[1,2]];
        if(!pairs[GizmoPlaneIndex].Contains(SelectedGizmoAxis))SetGizmoPlane(s,SelectedGizmoAxis==2?1:0);
        DrawingAxis=Array.IndexOf(pairs[GizmoPlaneIndex],SelectedGizmoAxis)+1; s.Invalidate();
    }

    int PickOtherPlane(CadSession s, double x, double y)
    {
        int result = -1; double best = 8;
        for (int i = 0; i < _sections.Count; i++)
        {
            if (i == _activeSection) continue;
            foreach (var (entity, points) in _sections[i].Model.Polylines(_sections[i].Plane))
            {
                if (entity.Construction && !_sections[i].Model.ShowConstruction) continue;
                for (int k = 1; k < points.Count; k++)
                {
                    var a = s.Camera.Project(points[k - 1]); var b = s.Camera.Project(points[k]); if (a == null || b == null) continue;
                    var (_, distance) = Picking.SegDist(x, y, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
                    if (distance < best) { best = distance; result = i; }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Sketches that were already turned into features are hidden; one the user shows again is there to be used. Its geometry behaves as if it
    /// were in this sketch: endpoints, centers and midpoints snap first (<paramref name="pointsOnly"/>), then anywhere along its curves.
    /// </summary>
    SnapInfo? SnapReferenceSketches(CadSession s, double px, double py, bool pointsOnly)
    {
        SnapInfo? best = null; double distance = pointsOnly ? 10 : 8;
        void Candidate(Vec3 world, double screenDistance, string note)
        {
            if (screenDistance >= distance) return;
            bool projected = Math.Abs(Vec3.Dot(world - Plane.Origin, Plane.Normal)) > 1e-6;
            if (projected && !ProjectOtherCurves) return;
            var (u, v) = Plane.Project(world);
            best = new(u, v, projected ? SnapKind.Projected : SnapKind.External, -1, note); distance = screenDistance;
        }
        void Point(Vec3 world, string note)
        {
            var p = s.Camera.Project(world); if (p == null) return;
            Candidate(world, Math.Sqrt(Math.Pow(p.Value.x - px, 2) + Math.Pow(p.Value.y - py, 2)), note);
        }
        foreach (var obj in s.Objects.Where(o => o.Kind == ObjKind.Sketch && o.Visible && o.Id != EditingId && !s.HiddenIds.Contains(o.Id) && o.SketchData != null && o.SketchPlane != null))
            foreach (var (model, plane) in obj.SketchData!.DrawingModels(obj.SketchPlane!.Value))
            {
                if (pointsOnly)
                {
                    foreach (var e in model.Entities.Where(e => !e.Construction || model.ShowConstruction))
                    {
                        Vec3 At(int id) { var q = model.P(id); return plane.PointAt(q.X, q.Y); }
                        switch (e.Type)
                        {
                            case EntType.Line:
                                Point(At(e.A), obj.Name + " · endpoint"); Point(At(e.B), obj.Name + " · endpoint");
                                Point((At(e.A) + At(e.B)) * 0.5, obj.Name + " · midpoint");
                                break;
                            case EntType.Circle or EntType.Ellipse: Point(At(e.A), obj.Name + " · center"); break;
                            case EntType.Arc: Point(At(e.A), obj.Name + " · center"); Point(At(e.B), obj.Name + " · endpoint"); Point(At(e.C), obj.Name + " · endpoint"); break;
                            case EntType.Point: Point(At(e.A), obj.Name + " · point"); break;
                        }
                    }
                    continue;
                }
                foreach (var (entity, points) in model.Polylines(plane))
                {
                    if (entity.Construction && !model.ShowConstruction) continue;
                    for (int k = 1; k < points.Count; k++)
                    {
                        var a = s.Camera.Project(points[k - 1]); var b = s.Camera.Project(points[k]); if (a == null || b == null) continue;
                        var (t, dist) = Picking.SegDist(px, py, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
                        Candidate(Vec3.Lerp(points[k - 1], points[k], t), dist, obj.Name + " · curve");
                    }
                }
            }
        return best;
    }

    SnapInfo? SnapExternal(CadSession s, double px, double py)
    {
        SnapInfo? best = null; double distance = 10;
        void Candidate(Vec3 world, double screenDistance)
        {
            bool projected = Math.Abs(Vec3.Dot(world - Plane.Origin, Plane.Normal)) > 1e-6;
            if (projected && !ProjectOtherCurves || screenDistance >= distance) return;
            var (u, v) = Plane.Project(world);
            best = new(u, v, projected ? SnapKind.Projected : SnapKind.External, -1); distance = screenDistance;
        }
        void Point(Vec3 world)
        {
            var p = s.Camera.Project(world); if (p == null) return;
            Candidate(world, Math.Sqrt(Math.Pow(p.Value.x - px, 2) + Math.Pow(p.Value.y - py, 2)));
        }
        foreach(var datum in Datums)Point(datum.Frame.Origin);
        foreach(var obj in s.Objects.Where(obj=>obj.Visible&&obj.Id!=EditingId&&obj.SketchData!=null))
            foreach(var datum in obj.SketchData!.Datums)Point(datum.Frame.Origin);
        foreach (var section in _sections.Where((_, i) => i != _activeSection))
        {
            foreach (var point in section.Model.Points.Where(point => point.Id != 0 && (section.Model.ShowConstruction || section.Model.Entities.Any(entity => !entity.Construction && section.Model.PointsOf(entity).Contains(point.Id)))))
                Point(section.Plane.PointAt(point.X, point.Y));
            foreach (var (entity, points) in section.Model.Polylines(section.Plane))
            {
                if (entity.Construction && !section.Model.ShowConstruction) continue;
                for (int k = 1; k < points.Count; k++)
                {
                    var a = s.Camera.Project(points[k - 1]); var b = s.Camera.Project(points[k]); if (a == null || b == null) continue;
                    var (t, dist) = Picking.SegDist(px, py, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
                    Candidate(Vec3.Lerp(points[k - 1], points[k], t), dist);
                }
            }
        }
        var objects = s.Objects.Where(obj => obj.Id != EditingId && obj.Visible && !s.HiddenIds.Contains(obj.Id) && obj.HasBrep && !obj.IsAnnotation);
        // Evaluate each segment so an off-plane edge cannot hide a valid coplanar edge in the same object.
        foreach (var obj in objects)
        {
            var mesh = obj.Mesh;
            for (int i = 0; i < mesh.SegEdge.Length; i++)
            {
                var a = new Vec3(mesh.EdgePoints[i * 6], mesh.EdgePoints[i * 6 + 1], mesh.EdgePoints[i * 6 + 2]);
                var b = new Vec3(mesh.EdgePoints[i * 6 + 3], mesh.EdgePoints[i * 6 + 4], mesh.EdgePoints[i * 6 + 5]);
                var pa = s.Camera.Project(a); var pb = s.Camera.Project(b); if (pa == null || pb == null) continue;
                var (t, dist) = Picking.SegDist(px, py, pa.Value.x, pa.Value.y, pb.Value.x, pb.Value.y);
                // Correct screen interpolation for perspective before interpolating in world space.
                if (!s.Camera.Orthographic)
                {
                    double da = Vec3.Dot(a - s.Camera.Eye, s.Camera.Forward), db = Vec3.Dot(b - s.Camera.Eye, s.Camera.Forward);
                    if (da > 0 && db > 0) t = (t / db) / ((1 - t) / da + t / db);
                }
                Candidate(Vec3.Lerp(a, b, t), dist);
            }
        }
        return best;
    }

    void BuildSpaceOverlay(CadSession s, Overlay o)
    {
        DrawDatums(s,o);
        for (int i = 0; i < _sections.Count; i++)
        {
            if (i == _activeSection) continue;
            foreach (var (entity, points) in _sections[i].Model.Polylines(_sections[i].Plane))
                if (!entity.Construction || _sections[i].Model.ShowConstruction) o.AddPolyline(points, 0xA080A9CD, 1.8f);
        }
        if (!Is3DEnabled && !PlanarGizmoActive) return;
        var axes = Basis; var origin = _gizmoFrame.Origin; double length = HandleLength(s);
        var display = GizmoSettings.Current; int active = ActiveGizmoDisplayOperation;
        var selectedPlane = GizmoDrawingPlane;
        double half = length * 1.25 * GizmoSize;
        Vec3 P(double u, double v) => origin + selectedPlane.U * u + selectedPlane.V * v;
        if (display.WorkPlane)
        {
            uint accent = GizmoVisual.Accent;
            o.AddTri(P(-half, -half), P(half, -half), P(half, half), (accent & 0xFFFFFF) | 0x08000000);
            o.AddTri(P(-half, -half), P(half, half), P(-half, half), (accent & 0xFFFFFF) | 0x08000000);
            o.AddPolyline([P(-half, -half), P(half, -half), P(half, half), P(-half, half)], (accent & 0xFFFFFF) | 0x60000000, 1, true);
        }
        int[][] pairs = [[0, 1], [0, 2], [1, 2]];
        if (display.PlaneHandles && display.Allows(0,active) && (GizmoEditing || GeometryEditing))
            for (int i = 0; i < 3; i++)
            {
                if (!display.AxisVisible(pairs[i][0])||!display.AxisVisible(pairs[i][1])||PlanarGizmoActive && i != GizmoPlaneIndex) continue;
                var pair = pairs[i]; var a = axes[pair[0]]; var b = axes[pair[1]];
                var p0 = origin + (a + b) * length * .13; var p1 = p0 + a * length * .3;
                var p2 = p1 + b * length * .3; var p3 = p0 + b * length * .3;
                uint accent = GizmoVisual.Accent;
                uint color = (accent & 0xFFFFFF) | (_gizmoHover == i + 4 ? 0xA0000000u : 0x30000000u);
                o.AddTri(p0, p1, p2, color); o.AddTri(p0, p2, p3, color);
                o.AddPolyline([p0, p1, p2, p3], (accent & 0xFFFFFF) | 0xA0000000, display.Stroke*.6f, true);
            }
        for (int operation = 0; operation < 3; operation++)
        {
            int hotAxis = operation == 0 && _gizmoHover is >= 1 and <= 3 ? _gizmoHover - 1 : operation == 1 && _gizmoHover is >= 7 and <= 9 ? _gizmoHover - 7 : -1;
            GizmoVisual.DrawOperation(o, s, origin, axes, operation, active, hotAxis,
                _gizmoHover == 0 && operation == (int)GizmoOperation || _gizmoHover == 10 && operation == 2, axisScale:false, planar:PlanarGizmoActive);
        }
        var colors = GizmoVisual.AxisColors;
        if (_axisPreview is { } point && _inferredVisualAxis >= 0)
        {
            var axis=axes[_inferredVisualAxis];
            o.AddLine(origin-axis*length*4,origin+axis*length*4,colors[_inferredVisualAxis],1.4f);
            o.AddDot(point,0xFFFFC53D,10);
        }
    }
}
