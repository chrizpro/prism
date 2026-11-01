using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

public sealed partial class SketchContext
{
    public IReadOnlyList<SketchDatum> Datums => _sections[0].Model.Datums;
    public Guid? ActiveDatumId { get; private set; }
    public SketchDatum SaveDatum(CadSession s, SketchDatumKind kind, string? name = null, int axis = 2,double normalOffset=0)
    {
        Push();
        var frame = kind == SketchDatumKind.Plane ? GizmoDrawingPlane : GizmoFrame;
        if(!Is3DEnabled)frame=new(_lastPlacement??Plane.Origin,Plane.U,Plane.V);
        if(kind==SketchDatumKind.Plane)frame=new(frame.Origin+frame.Normal*normalOffset,frame.U,frame.V);
        if (kind == SketchDatumKind.Axis)
        {
            var direction = Basis[Math.Clamp(axis,0,2)];
            var side = Math.Abs(direction.Z) < .9 ? Vec3.Cross(Vec3.UnitZ,direction).Normalized() : Vec3.UnitX;
            frame = new(frame.Origin,direction,side);
        }
        var datum = new SketchDatum { Name = string.IsNullOrWhiteSpace(name) ? $"{kind} {Datums.Count(d => d.Kind == kind) + 1}" : name.Trim(), Kind = kind, Frame = frame, Scale = GizmoSize };
        _sections[0].Model.Datums.Add(datum);
        if (kind is SketchDatumKind.Gizmo or SketchDatumKind.Axis or SketchDatumKind.Plane) ActiveDatumId = datum.Id;
        s.NotifySketchChanged(); s.Invalidate(); s.Say($"Saved {datum.Name}. Select it in Datum to reuse this reference."); return datum;
    }

    public void UseDatum(CadSession s, Guid? id)
    {
        if (id == null)
        {
            // Live gizmo: back to normal. After working from a saved plane the gizmo returns to the global axes (XY) where it stands.
            bool wasReference = ActiveDatumId != null;
            ActiveDatumId = null;
            if (wasReference && Is3DEnabled && !HasGeometrySelection)
            {
                _gizmoFrame = new(_gizmoFrame.Origin, Vec3.UnitX, Vec3.UnitY); GizmoSize = 1; GizmoStretch = 1;
                SetGizmoPlane(s, 0);
                s.Say("Live gizmo: back to the global axes. The saved plane stays in the object tree.");
            }
            s.NotifySketchChanged(); s.RaiseState(); s.Invalidate(); return;
        }
        var datum = Datums.FirstOrDefault(d => d.Id == id); if (datum == null) return;
        var frame = datum.Frame; var before = CaptureSpace();
        if(!Is3DEnabled)
        {
            ActiveDatumId=datum.Id;_gizmoFrame=frame;GizmoSize=datum.Scale;GizmoStretch=datum.Stretch;
            if(datum.Kind==SketchDatumKind.Gizmo){_planarGizmoEnabled=true;SetTool(s,new SketchGeometryEditTool());GizmoEditing=true;}
            s.NotifySketchChanged();s.Invalidate();s.Say($"Selected {datum.Name}.");return;
        }
        if(GeometryEditing&&HasGeometrySelection)
        {
            _gizmoFrame=frame;GizmoPlaneIndex=0;GizmoSize=datum.Scale;GizmoStretch=datum.Stretch;ActiveDatumId=datum.Id;_undo.Push(before);
            s.NotifySketchChanged();s.Invalidate();s.Say($"Using {datum.Name} as the selection transform pivot.");return;
        }
        _gizmoFrame=frame; GizmoPlaneIndex = 0; ApplyGizmoDrawingPlane(s,recordUndo:false); _undo.Push(before);
        GizmoSize = datum.Scale; GizmoStretch = datum.Stretch; ActiveDatumId = datum.Id;
        if (datum.Kind == SketchDatumKind.Plane)
        {
            int existing = _sections.FindIndex(section => Math.Abs(Vec3.Dot(section.Plane.Origin - frame.Origin,frame.Normal)) < 1e-6 && section.Plane.U.DistanceTo(frame.U) < 1e-6 && section.Plane.V.DistanceTo(frame.V) < 1e-6);
            if (existing >= 0) { SelectPlane(s,existing); _gizmoFrame = frame; }
        }
        s.NotifySketchChanged(); s.Invalidate(); s.Say($"Using {datum.Name}. Only this gizmo is active.");
    }

    public void UpdateDatum(CadSession s)
    {
        var datum = Datums.FirstOrDefault(d => d.Id == ActiveDatumId);
        if (datum == null) { s.Say("Select a saved datum first.",true); return; }
        Push(); datum.Frame = datum.Kind == SketchDatumKind.Plane ? GizmoDrawingPlane : GizmoFrame; datum.Scale = GizmoSize; datum.Stretch = GizmoStretch;
        s.NotifySketchChanged(); s.Invalidate();
    }

    bool PickDatum(CadSession s, double x, double y)
    {
        var candidates = Datums.Where(d => d.Id != ActiveDatumId).ToList();
        int hit = Picking.NearestPoint(s.Camera,candidates.Select(d => d.Frame.Origin).ToList(),x,y,10);
        if(hit<0)
        {
            double best=9;
            for(int i=0;i<candidates.Count;i++)
            {
                var datum=candidates[i];if(datum.Kind!=SketchDatumKind.Axis)continue;
                double size=s.Camera.PixelSizeAt(datum.Frame.Origin)*38*datum.Scale;
                var a=s.Camera.Project(datum.Frame.Origin-datum.Frame.U*size);var b=s.Camera.Project(datum.Frame.Origin+datum.Frame.U*size);
                if(a==null||b==null)continue;
                var (_,distance)=Picking.SegDist(x,y,a.Value.x,a.Value.y,b.Value.x,b.Value.y);
                if(distance<best){hit=i;best=distance;}
            }
        }
        if (hit < 0) return false;
        UseDatum(s,candidates[hit].Id); return true;
    }

    void DrawDatums(CadSession s, Overlay o)
        => DrawDatumReferences(s,o,Datums,Is3DEnabled?ActiveDatumId:null);

    public static void DrawDatumReferences(CadSession s,Overlay o,IEnumerable<SketchDatum> datums,Guid? active=null)
    {
        foreach (var datum in datums)
        {
            if (datum.Id == active) continue;
            var f = datum.Frame; double size = s.Camera.PixelSizeAt(f.Origin) * 38 * datum.Scale;
            uint color = datum.Id==active || datum.Id==s.Sketch?.ActiveDatumId ? 0xFFFFC53D : 0x909DC6F0;
            o.AddDot(f.Origin,color,7);
            if (datum.Kind == SketchDatumKind.Point) continue;
            if (datum.Kind == SketchDatumKind.Axis) { o.AddLine(f.Origin - f.U * size,f.Origin + f.U * size,color,1.8f); continue; }
            if (datum.Kind == SketchDatumKind.Plane)
                o.AddPolyline([f.PointAt(-size,-size),f.PointAt(size,-size),f.PointAt(size,size),f.PointAt(-size,size)],color,1.2f,true);
            else
            { o.AddLine(f.Origin,f.Origin+f.U*size,color,1.5f); o.AddLine(f.Origin,f.Origin+f.V*size,color,1.5f); o.AddLine(f.Origin,f.Origin+f.Normal*size,color,1.5f); }
        }
    }
}
