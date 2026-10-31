using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    string _beforeGizmoPanel="context";
    Plane3? _sketchPlacementBefore;
    SketchContext? _placementSketch;
    static readonly string[] AllGizmoOperations=["Move","Rotate","Scale"],PlacementOperations=["Move","Rotate"];
    // in a sketch the operations carry their keys, so the list doubles as the shortcut reminder
    static readonly string[] SketchGizmoOperationNames=["Move (W)","Rotate (R)","Scale (S)"],SketchPlacementOperationNames=["Move (W)","Rotate (R)"];
    GizmoBinding? ActiveBinding=>Session.ActiveTool switch {FormEditTool tool=>tool.Binding,EditPointsTool tool=>tool.Binding,_=>Session.DatumVisible?Session.Datum!.Binding:null};
    public bool CanPlaceGizmo=>Session.ActiveCommand?.HasPanelStep!=true && (Session.ActiveCommand is EditFormCommand {Work:not null} or EditPointsCommand {Model:not null} || Session.Sketch!=null || Session.DatumVisible);
    public bool GizmoIsDatum=>Session.DatumVisible;
    public bool GizmoIsNotDatum=>!Session.DatumVisible;
    public bool GizmoPositioning=>ActiveBinding?.Positioning==true || _sketchPlacementBefore!=null;
    public string[] GizmoOperations=>Session.Sketch!=null&&!GizmoIsDatum?(GizmoPositioning?SketchPlacementOperationNames:SketchGizmoOperationNames):GizmoPositioning?PlacementOperations:AllGizmoOperations;
    public int GizmoActiveOperation
    {
        get=>Session.ActiveCommand switch {EditFormCommand form=>form.TransformMode.Index,EditPointsCommand points=>points.TransformMode.Index,_=>Session.DatumVisible?Session.Datum!.Mode:(int)(Session.Sketch?.GizmoOperation??Core.SketchGizmoOperation.Move)};
        set
        {
            if(value<0)return;
            value=Math.Clamp(value,0,2);
            if(value==GizmoActiveOperation)return;
            if(GizmoPositioning)value=Math.Min(value,1);
            if(Session.DatumVisible){Session.Datum!.Mode=Math.Min(value,1);Session.Invalidate();}
            else if(Session.ActiveCommand is EditFormCommand form)form.ForceMode(value);
            else if(Session.ActiveCommand is EditPointsCommand points){points.TransformMode.Index=value;points.Touch();}
            else if(Session.Sketch is {} sketch){sketch.GizmoOperation=(SketchGizmoOperation)value;Session.Invalidate();}
            OnPropertyChanged();
        }
    }
    public string GizmoBindingStatus=>GizmoIsDatum?"Datum gizmo: drag its arrows, blocks or rings, or type exact values. It does not change geometry. Press Esc or Hide to remove it.":!CanPlaceGizmo?"Open Edit Freeform, Edit Control Points or a sketch to place and bind a gizmo. Display and snapping settings are shared across editors.":GizmoPositioning?"Placement mode: handles move only the gizmo. Select geometry, position and orient the pivot, then bind it.":ActiveBinding?.Bound==true || Session.Sketch?.GizmoTargetConfirmed==true?"Bound to the current selection. Handles transform geometry.":"Select geometry to transform, or enter placement mode to position its pivot first.";
    Vec3 GizmoOrigin=>ActiveBinding?.Frame?.Origin ?? Session.Sketch?.GizmoFrame.Origin ?? Session.ActiveCommand switch {EditFormCommand form=>form.SelectionCenter(),EditPointsCommand points when points.Tool is EditPointsTool tool=>tool.GizmoCentre()??Vec3.Zero,_=>Vec3.Zero};
    public double GizmoOriginX {get=>GizmoOrigin.X;set=>SetGizmoOrigin(new(value,GizmoOrigin.Y,GizmoOrigin.Z));}
    public double GizmoOriginY {get=>GizmoOrigin.Y;set=>SetGizmoOrigin(new(GizmoOrigin.X,value,GizmoOrigin.Z));}
    public double GizmoOriginZ {get=>GizmoOrigin.Z;set=>SetGizmoOrigin(new(GizmoOrigin.X,GizmoOrigin.Y,value));}
    Plane3 GizmoFrame=>ActiveBinding?.Frame ?? Session.Sketch?.GizmoFrame ?? new Plane3(GizmoOrigin,Vec3.UnitX,Vec3.UnitY);
    Vec3 GizmoRotation
    {
        get {var f=GizmoFrame;double p=Math.Asin(Math.Clamp(-f.U.Z,-1,1));double r=Math.Abs(Math.Cos(p))>1e-7?Math.Atan2(f.V.Z,f.Normal.Z):0;double y=Math.Abs(Math.Cos(p))>1e-7?Math.Atan2(f.U.Y,f.U.X):Math.Atan2(-f.V.X,f.V.Y);return new(r*180/Math.PI,p*180/Math.PI,y*180/Math.PI);}
    }
    public double GizmoRotationX {get=>GizmoRotation.X;set=>SetGizmoRotation(new(value,GizmoRotation.Y,GizmoRotation.Z));}
    public double GizmoRotationY {get=>GizmoRotation.Y;set=>SetGizmoRotation(new(GizmoRotation.X,value,GizmoRotation.Z));}
    public double GizmoRotationZ {get=>GizmoRotation.Z;set=>SetGizmoRotation(new(GizmoRotation.X,GizmoRotation.Y,value));}
    void SetGizmoRotation(Vec3 rotation)
    {
        if(ActiveBinding!=null&&!EnsurePlacement())return;
        if(ActiveBinding is {} binding)binding.SetRotation(rotation);
        else Session.Sketch?.SetGizmoTransform(Session,GizmoOrigin,rotation);
        Session.Invalidate();
    }
    public bool HasGizmoPlanes=>Session.Sketch!=null;
    // Scale has three fields. Bound to geometry they multiply the selection along the gizmo's own axes (Blender-style: 1 means unchanged,
    // 0.5 on Y squeezes two points on a vertical line together). Unbound they size the drawing plane: width and height, and none for depth.
    bool GizmoScaleBound=>Session.Sketch is {GeometryEditing:true,GizmoEditing:false,HasGeometrySelection:true}&&!GizmoPositioning;
    public string GizmoScaleHeader=>GizmoScaleBound?"Scale the selection (about the gizmo, along its axes)":"Scale the plane (width and height)";
    public bool GizmoScaleZEnabled=>GizmoScaleBound&&Session.Sketch is {FreeEditing:true};
    public string GizmoScaleZLabel=>GizmoScaleZEnabled?"Z":"Z (n/a)";
    public double GizmoScaleX {get=>GizmoScaleBound?1:(Session.Sketch?.GizmoSize??1);set=>SetGizmoScale(0,value);}
    public double GizmoScaleY {get=>GizmoScaleBound?1:(Session.Sketch is {} sketch?sketch.GizmoSize*sketch.GizmoStretch:1);set=>SetGizmoScale(1,value);}
    public double GizmoScaleZ {get=>1;set=>SetGizmoScale(2,value);}
    void SetGizmoScale(int axis,double value)
    {
        if(Session.Sketch is {} sketch&&double.IsFinite(value)&&value>0)
        {
            if(GizmoScaleBound)sketch.ScaleBoundGeometry(Session,new Vec3(axis==0?value:1,axis==1?value:1,axis==2?value:1));
            else if(axis==0)sketch.SetGizmoPlaneSize(Session,value,sketch.GizmoSize*sketch.GizmoStretch);
            else if(axis==1)sketch.SetGizmoPlaneSize(Session,sketch.GizmoSize,value);
        }
        foreach(var property in new[]{nameof(GizmoScaleX),nameof(GizmoScaleY),nameof(GizmoScaleZ)})OnPropertyChanged(property);
    }
    public double GizmoPlaneSize {get=>Session.Sketch?.GizmoSize??1;set {if(Session.Sketch is {} sketch)sketch.SetGizmoSize(Session,value);}}
    void SetGizmoOrigin(Vec3 origin)
    {
        if(origin==GizmoOrigin)return;
        // typing a location moves what the gizmo is doing now: a bound gizmo moves its geometry, an unbound one moves alone
        if(ActiveBinding is {} binding){if(!EnsurePlacement())return;binding.SetOrigin(origin);}
        else if(Session.Sketch is {} sketch){if(GizmoPositioning)sketch.SetPlacementOrigin(Session,origin);else sketch.MoveDrawingPlane(Session,origin);}
        Session.Invalidate();
    }
    // typing a location or angle edits the gizmo itself, so it enters placement mode on demand
    bool EnsurePlacement()
    {
        if(!GizmoPositioning&&CanPlaceGizmo)BeginGizmoPlacement();
        return GizmoPositioning;
    }
    public void OpenGizmoPanel()
    {
        if(!ShowGizmoPanel)_beforeGizmoPanel=SelectedRightPanel;
        OpenRightPanel("gizmo");RefreshGizmoPanel();
    }
    public void OpenGizmoFromRibbon()
    {
        if(Session.ActiveCommand==null&&Session.Sketch==null){Session.ShowDatumGizmo(Vec3.Zero);OpenGizmoPanel();return;}
        bool existing=ActiveBinding?.Bound==true || Session.Sketch?.GizmoTargetConfirmed==true;
        OpenGizmoPanel();
        if(Session.Sketch!=null)return;     // a sketch's gizmo is never unbound just by opening the panel: Unbind is the user's choice
        if(!existing&&CanPlaceGizmo&&!GizmoPositioning)
        {
            BeginGizmoPlacement();
            SetGizmoOrigin(Vec3.Zero);
        }
    }
    public void OpenGizmoFromHotkey()
    {
        if(Session.ActiveCommand==null&&Session.Sketch==null){Session.ShowDatumGizmo(Session.ViewportCursor??Vec3.Zero);OpenGizmoPanel();return;}
        bool bound=ActiveBinding?.Bound==true || Session.Sketch?.GizmoTargetConfirmed==true;
        OpenGizmoPanel();
        if(Session.Sketch!=null||bound)return;     // opening the panel never unbinds a gizmo; only a gizmo with nothing bound yet starts being placed
        if(!GizmoPositioning)BeginGizmoPlacement();
        if(GizmoPositioning)SetGizmoOrigin(Session.ViewportCursor??Vec3.Zero);
    }
    // Done keeps what was edited: pending placement is bound to the selection, or discarded if nothing is selected
    public void FinishGizmoPanel()
    {
        if(GizmoPositioning&&!GizmoIsDatum)
        {
            BindGizmoSelection();
            if(!GizmoPositioning)return;
        }
        CloseGizmoPanel();
    }
    public void CloseGizmoPanel()
    {
        if(GizmoPositioning&&!GizmoIsDatum)CancelGizmoPlacement();
        CloseRightPanel("gizmo");SelectedRightPanel=_beforeGizmoPanel;
    }
    public void BeginGizmoPlacement()
    {
        if(!CanPlaceGizmo||GizmoPositioning)return;
        if(ActiveBinding is {} binding)
        {
            var center=GizmoOrigin;
            if(Session.ActiveCommand is EditFormCommand {Work:{} mesh} form&&!form.MovingVertices().Any())center=mesh.V.Aggregate(Vec3.Zero,(a,b)=>a+b)/Math.Max(mesh.V.Count,1);
            if(Session.ActiveCommand is EditPointsCommand {Model:{} model,Tool:EditPointsTool pointTool} && pointTool.Selected.Count==0)center=model.Points.Aggregate(Vec3.Zero,(a,b)=>a+b)/Math.Max(model.Points.Length,1);
            binding.Begin(center);
        }
        else if(Session.Sketch is {} sketch){_placementSketch=sketch;_sketchPlacementBefore=sketch.GizmoFrame;sketch.BeginPlacement(Session);}
        GizmoActiveOperation=0;Session.Say("Place the gizmo without moving geometry, then bind it to the selected target.");RefreshGizmoPanel();Session.Invalidate();
    }
    public void BindGizmoSelection()
    {
        if(GizmoIsDatum){CloseGizmoPanel();return;}
        bool bound=Session.ActiveCommand switch
        {
            EditFormCommand form=>ActiveBinding?.Bind(form.MovingVertices())==true,
            EditPointsCommand points when points.Tool is EditPointsTool tool=>tool.Binding.Bind(tool.Selected),
            _=>false,
        };
        if(Session.Sketch is {} sketch){sketch.ConfirmGizmoTarget(Session,preservePlacement:true);bound=sketch.GizmoTargetConfirmed&&!sketch.GizmoEditing;if(bound)_sketchPlacementBefore=null;}
        if(!bound){Session.Say("Select geometry before binding the gizmo.",true);return;}
        Session.Say("Gizmo bound to the current selection.");RefreshGizmoPanel();CloseGizmoPanel();Session.Invalidate();
    }
    public void CancelGizmoPlacement()
    {
        if(GizmoIsDatum){Session.HideDatumGizmo();RefreshGizmoPanel();return;}
        if(ActiveBinding?.Positioning==true)ActiveBinding.Cancel();
        if(_sketchPlacementBefore is {} frame){Session.Sketch?.CancelPlacement(Session,frame);_sketchPlacementBefore=null;}
        RefreshGizmoPanel();Session.Invalidate();
    }
    public void ResetGizmoBinding()
    {
        if(ActiveBinding is {} binding){bool placing=binding.Positioning;binding.Reset();if(placing)BeginGizmoPlacement();}
        if(Session.Sketch is {} sketch){if(!GizmoPositioning)BeginGizmoPlacement();sketch.CenterPlacementOnSelection(Session);}
        RefreshGizmoPanel();Session.Invalidate();
    }
    void RefreshGizmoPanel()
    {
        if(_placementSketch!=null && (!ReferenceEquals(_placementSketch,Session.Sketch)||!_placementSketch.IndependentGizmoPlacement)){_sketchPlacementBefore=null;_placementSketch=null;}
        if(!ShowGizmoPanel)return;
        foreach(var property in new[]{nameof(CanPlaceGizmo),nameof(GizmoIsDatum),nameof(GizmoIsNotDatum),nameof(GizmoPositioning),nameof(GizmoBindingStatus),nameof(GizmoOperations),nameof(GizmoActiveOperation),nameof(GizmoOriginX),nameof(GizmoOriginY),nameof(GizmoOriginZ),nameof(GizmoRotationX),nameof(GizmoRotationY),nameof(GizmoRotationZ),nameof(HasGizmoPlanes),nameof(GizmoPlaneSize),nameof(GizmoScaleHeader),nameof(GizmoScaleZEnabled),nameof(GizmoScaleZLabel),nameof(GizmoScaleX),nameof(GizmoScaleY),nameof(GizmoScaleZ)})OnPropertyChanged(property);
    }
    public string[] GizmoDisplayModes { get; } = ["Active tool only", "Combined handles", "Custom visibility"];
    public int GizmoDisplayMode { get => GizmoSettings.Current.DisplayMode; set => ChangeGizmo(g => g.DisplayMode = Math.Clamp(value, 0, 2)); }
    public bool GizmoCustomVisibility => GizmoDisplayMode == 2;
    public bool GizmoShowMove { get => GizmoSettings.Current.Move; set => ChangeGizmo(g => g.Move = value); }
    public bool GizmoShowRotate { get => GizmoSettings.Current.Rotate; set => ChangeGizmo(g => g.Rotate = value); }
    public bool GizmoShowScale { get => GizmoSettings.Current.Scale; set => ChangeGizmo(g => g.Scale = value); }
    public bool GizmoShowPlanes { get => GizmoSettings.Current.PlaneHandles; set => ChangeGizmo(g => g.PlaneHandles = value); }
    public bool GizmoShowLabels { get => GizmoSettings.Current.Labels; set => ChangeGizmo(g => g.Labels = value); }
    public bool GizmoShowWorkPlane { get => GizmoSettings.Current.WorkPlane; set => ChangeGizmo(g => g.WorkPlane = value); }
    public bool GizmoShowX {get=>GizmoSettings.Current.AxisX;set=>ChangeGizmo(g=>g.AxisX=value);}
    public bool GizmoShowY {get=>GizmoSettings.Current.AxisY;set=>ChangeGizmo(g=>g.AxisY=value);}
    public bool GizmoShowZ {get=>GizmoSettings.Current.AxisZ;set=>ChangeGizmo(g=>g.AxisZ=value);}
    public double GizmoScreenSize { get => GizmoSettings.Current.Pixels; set { if (double.IsFinite(value)) ChangeGizmo(g => g.ScreenSize = Math.Clamp(value, 56, 180)); } }
    public double GizmoLineWidth { get => GizmoSettings.Current.Stroke; set { if (double.IsFinite(value)) ChangeGizmo(g => g.LineWidth = Math.Clamp(value, 1, 5)); } }
    public double GizmoHitTolerance { get => GizmoSettings.Current.Tolerance; set { if (double.IsFinite(value)) ChangeGizmo(g => g.HitTolerance = Math.Clamp(value, 4, 18)); } }
    public bool GizmoSnapping { get => GizmoSettings.Current.Snap; set => ChangeGizmo(g => g.Snap = value); }
    public double GizmoMoveStep { get => GizmoSettings.Current.MoveStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.MoveStep = Math.Clamp(value, .001, 10000)); } }
    public double GizmoAngleStep { get => GizmoSettings.Current.AngleStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.AngleStep = Math.Clamp(value, .1, 180)); } }
    public double GizmoScaleStep { get => GizmoSettings.Current.ScaleStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.ScaleStep = Math.Clamp(value, .1, 100)); } }

    void ChangeGizmo(Action<GizmoSettings> change, [CallerMemberName] string property = "")
    {
        var settings = GizmoSettings.Current; string before = JsonSerializer.Serialize(settings);
        change(settings);
        if (before == JsonSerializer.Serialize(settings)) return;
        AppSettings.Save(notify: false);
        OnPropertyChanged(property); OnPropertyChanged(nameof(GizmoCustomVisibility));
        Session.Invalidate();
    }
    public void ResetGizmoDisplay()
    {
        AppSettings.Current.Gizmo = new(); AppSettings.Save(notify: false);
        foreach(string name in new[]{nameof(GizmoShowX),nameof(GizmoShowY),nameof(GizmoShowZ)})OnPropertyChanged(name);
        foreach (string name in new[] { nameof(GizmoDisplayMode), nameof(GizmoCustomVisibility), nameof(GizmoShowMove), nameof(GizmoShowRotate), nameof(GizmoShowScale), nameof(GizmoShowPlanes), nameof(GizmoShowLabels), nameof(GizmoShowWorkPlane), nameof(GizmoScreenSize), nameof(GizmoLineWidth), nameof(GizmoHitTolerance), nameof(GizmoSnapping), nameof(GizmoMoveStep), nameof(GizmoAngleStep), nameof(GizmoScaleStep) }) OnPropertyChanged(name);
        Session.Invalidate();
    }
}
