using System;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

public sealed partial class SketchContext
{
    bool _planarGizmoEnabled;
    public bool PlanarGizmoActive => !Is3DEnabled && GeometryEditing && _planarGizmoEnabled;
    public bool GeometryEditing { get; private set; }
    public bool HasGeometrySelection => SelEntities.Count > 0 || SelPoints.Count > 0;
    bool _geometryDrag, _geometryValid = true;
    string _geometryError = "";
    readonly SelectTool _gizmoTargetPicker = new(selectionOnly:true);
    public bool GizmoTargetConfirmed { get; private set; }
    public double GeometryScale { get; private set; } = 1;
    readonly System.Collections.Generic.HashSet<int> _confirmedPoints = [], _confirmedEntities = [];
    Vec3 _confirmedCenter;
    int _confirmedPlane;
    SpaceSnapshot? _selectionMoveSnapshot;
    bool _selectionMoveValid;

    static System.Collections.Generic.List<(int pid,double tx,double ty)> RectangleCornerAnchors(SketchModel model,System.Collections.Generic.ICollection<int> points)
    {
        var anchors=new System.Collections.Generic.List<(int,double,double)>();
        if(points.Count!=1)return anchors;
        int corner=points.First();
        var group=model.Entities.FirstOrDefault(e=>e.Group?.StartsWith("Rectangle ",StringComparison.Ordinal)==true&&model.PointsOf(e).Contains(corner))?.Group;
        if(group==null)return anchors;
        var sides=model.Entities.Where(e=>e.Group==group&&e.Type==EntType.Line).ToList();
        var corners=sides.SelectMany(model.PointsOf).Distinct().ToList();if(sides.Count!=4||corners.Count!=4)return anchors;
        var neighbors=sides.Where(e=>model.PointsOf(e).Contains(corner)).SelectMany(model.PointsOf).ToHashSet();
        foreach(int id in corners.Where(id=>!neighbors.Contains(id))) { var p=model.P(id);anchors.Add((id,p.X,p.Y)); }
        return anchors;
    }

    internal void DragGeometryPoint(CadSession s,int id,double u,double v)
        => Solve(s,new[]{(id,u,v)}.Concat(RectangleCornerAnchors(Model,new[]{id})));

    public void ToggleGeometryEditing(CadSession s)
    {
        if(GeometryEditing) { SetTool(s,new SelectTool());ClearGizmoTarget(); }
        else SetTool(s,new SketchGeometryEditTool());
        s.RaiseState();s.Invalidate();
    }

    void PreviewGizmoTarget(CadSession s)
    {
        if(!GeometryEditing||!GizmoEditing||SelectionCenter() is not { } center)return;
        _gizmoFrame=new(center,_gizmoFrame.U,_gizmoFrame.V);
        s.NotifySketchChanged();s.Invalidate();
    }

    Vec3? SelectionCenter()
    {
        if(SelPoints.Count==0&&SelEntities.Count==1&&Model.EntityOrNull(SelEntities.First()) is { Type:EntType.Ellipse } ellipse)
        { return SketchModel.To3(Plane,Model.P(ellipse.A)); }
        var entities = Tool is SketchGeometryEditTool { OptionIndex:1 } && SelPoints.Count>0 ? Enumerable.Empty<int>() : SelEntities;
        var points=SelPoints.Concat(entities.Where(id=>Model.EntityOrNull(id)!=null).SelectMany(id=>Model.PointsOf(Model.E(id)))).Distinct().Select(Model.PointOrNull).Where(p=>p!=null).ToList();
        return points.Count==0?null:points.Select(p=>SketchModel.To3(Plane,p!)).Aggregate(Vec3.Zero,(a,b)=>a+b)/points.Count;
    }

    void RememberGizmoTargetCenter()
    {
        if (GizmoTargetConfirmed && SelectionCenter() is { } center) { _confirmedCenter=center; _confirmedPlane=_activeSection; }
    }

    void ClearGizmoTarget() { GeometryScale=1;GizmoTargetConfirmed=false;_confirmedPoints.Clear();_confirmedEntities.Clear(); }

    void LockCurrentGizmoTarget()
    {
        if(!GeometryEditing||SelectionCenter() is not { } center)return;
        GizmoTargetConfirmed=true;_confirmedPoints.Clear();_confirmedPoints.UnionWith(SelPoints);
        _confirmedEntities.Clear();_confirmedEntities.UnionWith(SelEntities);_confirmedPlane=_activeSection;_confirmedCenter=center;
    }

    public void ConfirmGizmoTarget(CadSession s)
    {
        if(!GeometryEditing||!GizmoEditing)return;
        if(SelectionCenter() is not { } center) { s.Say("Select a point, curve or shape first. Shift-click adds more targets; Enter confirms.",true);return; }
        _gizmoFrame=new(center,_gizmoFrame.U,_gizmoFrame.V);ActiveDatumId=null;
        GizmoTargetConfirmed=true;GeometryScale=1;_confirmedPoints.Clear();_confirmedPoints.UnionWith(SelPoints);_confirmedEntities.Clear();_confirmedEntities.UnionWith(SelEntities);
        _confirmedPlane=_activeSection;_confirmedCenter=center;GizmoEditing=false;_gizmoTargetPicker.Deactivate(s);
        s.Say("Gizmo target locked. Drag its arrows, rings or scale handle to transform the selected geometry. G edits only the gizmo.");
        s.NotifySketchChanged();s.RaiseState();s.Invalidate();
    }

    internal void BeginSelectionTranslation() { _selectionMoveSnapshot=CaptureSpace();_selectionMoveValid=true;_geometryError=""; }
    internal void PreviewSelectionTranslation(CadSession s,Vec3 delta)
    {
        if(_selectionMoveSnapshot is not { } snapshot)return;
        _selectionMoveValid=PreviewGeometryTransform(s,snapshot,new(snapshot.Gizmo.Origin+delta,snapshot.Gizmo.U,snapshot.Gizmo.V),1);
        s.NotifySketchChanged();s.Invalidate();
    }
    internal void EndSelectionTranslation(CadSession s,bool cancel=false)
    {
        if(_selectionMoveSnapshot is not { } snapshot)return;
        if(!cancel&&_selectionMoveValid){_undo.Push(snapshot);RememberGizmoTargetCenter();}
        else{RestoreSpace(snapshot);if(!cancel)s.Say(_geometryError.Length>0?_geometryError:"That move conflicts with the constraints.",true);}
        _selectionMoveSnapshot=null;s.NotifySketchChanged();s.Invalidate();
    }

    public void CenterGizmoOnSelection(CadSession s)
    {
        if (!GeometryEditing) return;
        if (!HasGeometrySelection) { ClearGizmoTarget();s.NotifySketchChanged();s.Invalidate();return; }
        if (GizmoEditing || SelectionCenter() is not { } center) return;
        if(GizmoTargetConfirmed&&_confirmedPlane==_activeSection&&_confirmedPoints.SetEquals(SelPoints)&&_confirmedEntities.SetEquals(SelEntities))
        {
            _gizmoFrame=new(_gizmoFrame.Origin+center-_confirmedCenter,_gizmoFrame.U,_gizmoFrame.V);_confirmedCenter=center;
            s.NotifySketchChanged();s.Invalidate();return;
        }
        ClearGizmoTarget();
        if(ActiveDatumId!=null)return;
        _gizmoFrame=new(center,_gizmoFrame.U,_gizmoFrame.V); s.NotifySketchChanged(); s.Invalidate();
    }

    void RestoreSpace(SpaceSnapshot snapshot)
    {
        _sections=snapshot.Sections.Select(section=>section.Clone()).ToList(); _activeSection=snapshot.Active;
        _gizmoFrame=snapshot.Gizmo; GizmoPlaneIndex=snapshot.GizmoPlane; GizmoSize=snapshot.Size; ActiveDatumId=snapshot.Datum; GeometryScale=snapshot.TargetScale;
        Result=SketchSolver.Solve(Model);
    }

    bool PreviewGeometryTransform(CadSession s, SpaceSnapshot snapshot, Plane3 target, double factor)
    {
        _sections=snapshot.Sections.Select(section=>section.Clone()).ToList(); _activeSection=snapshot.Active;
        var original = snapshot.Sections[snapshot.Active]; var source = original.Model;
        var entities=(Tool is SketchGeometryEditTool { OptionIndex:1 } && SelPoints.Count>0 ? Enumerable.Empty<int>() : SelEntities).Where(id=>source.EntityOrNull(id)!=null).ToHashSet();
        // Selecting every defining point of a line/outline makes that complete curve a transform target too.
        if (Tool is not SketchGeometryEditTool { OptionIndex:1 })
            entities.UnionWith(source.Entities.Where(e=>source.PointsOf(e).Distinct().Count()>=2&&source.PointsOf(e).All(SelPoints.Contains)).Select(e=>e.Id));
        var points=SelPoints.Concat(entities.SelectMany(id=>source.PointsOf(source.E(id)))).ToHashSet();
        if(points.Count==0)return false;
        Vec3 Turn(Vec3 v)=>target.U*Vec3.Dot(v,snapshot.Gizmo.U)+target.V*Vec3.Dot(v,snapshot.Gizmo.V)+target.Normal*Vec3.Dot(v,snapshot.Gizmo.Normal);
        Vec3 Transform(Vec3 p)=>target.Origin+Turn(p-snapshot.Gizmo.Origin)*factor;
        bool shared=source.Entities.Any(e=>!entities.Contains(e.Id)&&source.PointsOf(e).Any(points.Contains));
        bool mixedConstraint=source.Constraints.Any(c=>
        {
            var pointRefs=SketchModel.RefIsPoint(c.Type,c.Refs.Length);
            var inside=c.Refs.Select((id,i)=>pointRefs[i]?points.Contains(id):entities.Contains(id)).ToList();
            return inside.Any(b=>b)&&inside.Any(b=>!b);
        });
        if(entities.Count>0&&!shared&&!mixedConstraint)
        {
            var moved=source.Clone(); moved.Sections=[]; moved.Datums=[];
            moved.Entities.RemoveAll(e=>!entities.Contains(e.Id));
            moved.Points.RemoveAll(p=>p.Id!=0&&!points.Contains(p.Id));
            moved.Constraints.RemoveAll(c=>
            {
                var pointRefs=SketchModel.RefIsPoint(c.Type,c.Refs.Length);
                return c.Refs.Select((id,i)=>pointRefs[i]?points.Contains(id)||id==0:entities.Contains(id)).Any(b=>!b);
            });
            foreach(var point in moved.Points){point.X*=factor;point.Y*=factor;point.Z*=factor;}
            foreach(var entity in moved.Entities)entity.R*=factor;
            foreach(var constraint in moved.Constraints)
            {
                if(constraint.Type==CType.Fixed){constraint.Value*=factor;constraint.V2*=factor;}
                else if(constraint.IsDimension&&constraint.Type!=CType.Angle)constraint.Value*=factor;
            }
            var frame=new Plane3(Transform(original.Plane.Origin),Turn(original.Plane.U).Normalized(),Turn(original.Plane.V).Normalized());
            if(entities.Count==source.Entities.Count)
            {
                moved.Datums=source.Datums.Select(d=>d.Clone()).ToList();
                _sections[_activeSection].Model=moved; _sections[_activeSection].Plane=frame;
            }
            else
            {
                foreach(int entity in entities)_sections[_activeSection].Model.RemoveEntity(entity);
                _sections.Add(new(){Name=$"Edited shape {_sections.Count+1}",Plane=frame,Model=moved}); _activeSection=_sections.Count-1;
            }
            Result=SketchSolver.Solve(Model);
        }
        else
        {
            var targets=points.Select(id=>(id,point:Transform(SketchModel.To3(original.Plane,source.P(id))),z0:source.P(id).Z)).ToList();
            double Lift(Vec3 world)=>Vec3.Dot(world-original.Plane.Origin,original.Plane.Normal);
            if(!FreeEditing&&targets.Any(p=>Math.Abs(Lift(p.point)-p.z0)>1e-6))
            { _geometryError="Turn on Free 3D editing, or select a complete independent shape, to move it out of its plane. Individual points and connected geometry must satisfy their planar constraints."; return false; }
            var drags=targets.Select(p=>{var uv=original.Plane.Project(p.point);return(p.id,uv.u,uv.v);}).ToList();
            drags.AddRange(RectangleCornerAnchors(source,points));
            Result=SketchSolver.Solve(Model,drags);
            if(drags.Any(p=>Math.Abs(Model.P(p.id).X-p.u)>1e-3||Math.Abs(Model.P(p.id).Y-p.v)>1e-3))
            {_geometryError="The selected geometry cannot reach that position while satisfying its constraints.";return false;}
            if(FreeEditing)foreach(var moved in targets)Model.P(moved.id).Z=Lift(moved.point);
        }
        _gizmoFrame=target; GizmoSize=snapshot.Size;GeometryScale=snapshot.TargetScale*factor;
        return Result.Converged;
    }

    bool TransformGeometry(CadSession s,Plane3 target,double factor)
    {
        var snapshot=CaptureSpace();
        if(!PreviewGeometryTransform(s,snapshot,target,factor))
        {RestoreSpace(snapshot);s.Say(_geometryError.Length>0?_geometryError:"That transform conflicts with the constraints.",true);s.NotifySketchChanged();return false;}
        _undo.Push(snapshot); RememberGizmoTargetCenter();s.NotifySketchChanged();s.RaiseState();s.Invalidate();return true;
    }
}
