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
    GizmoBinding? ActiveBinding=>Session.ActiveTool switch {FormEditTool tool=>tool.Binding,EditPointsTool tool=>tool.Binding,_=>null};
    public bool CanPlaceGizmo=>Session.ActiveCommand?.HasPanelStep!=true && (Session.ActiveCommand is EditFormCommand {Work:not null} or EditPointsCommand {Model:not null} || Session.Sketch!=null);
    public bool GizmoPositioning=>ActiveBinding?.Positioning==true || _sketchPlacementBefore!=null;
    public string[] GizmoOperations=>GizmoPositioning?PlacementOperations:AllGizmoOperations;
    public int GizmoActiveOperation
    {
        get=>Session.ActiveCommand switch {EditFormCommand form=>form.TransformMode.Index,EditPointsCommand points=>points.TransformMode.Index,_=> (int)(Session.Sketch?.GizmoOperation??Core.SketchGizmoOperation.Move)};
        set
        {
            if(value<0)return;
            value=Math.Clamp(value,0,2);
            if(value==GizmoActiveOperation)return;
            if(GizmoPositioning)value=Math.Min(value,1);
            if(Session.ActiveCommand is EditFormCommand form)form.ForceMode(value);
            else if(Session.ActiveCommand is EditPointsCommand points){points.TransformMode.Index=value;points.Touch();}
            else if(Session.Sketch is {} sketch){sketch.GizmoOperation=(SketchGizmoOperation)value;Session.Invalidate();}
            OnPropertyChanged();
        }
    }
    public string GizmoBindingStatus=>!CanPlaceGizmo?"Open Edit Freeform, Edit Control Points or a sketch to place and bind a gizmo. Display and snapping settings are shared across editors.":GizmoPositioning?"Placement mode: handles move only the gizmo. Select geometry, position and orient the pivot, then bind it.":ActiveBinding?.Bound==true || Session.Sketch?.GizmoTargetConfirmed==true?"Bound to the current selection. Handles transform geometry.":"Select geometry to transform, or enter placement mode to position its pivot first.";
    Vec3 GizmoOrigin=>ActiveBinding?.Frame?.Origin ?? Session.Sketch?.GizmoFrame.Origin ?? Session.ActiveCommand switch {EditFormCommand form=>form.SelectionCenter(),EditPointsCommand points when points.Tool is EditPointsTool tool=>tool.GizmoCentre()??Vec3.Zero,_=>Vec3.Zero};
    public double GizmoOriginX {get=>GizmoOrigin.X;set=>SetGizmoOrigin(new(value,GizmoOrigin.Y,GizmoOrigin.Z));}
    public double GizmoOriginY {get=>GizmoOrigin.Y;set=>SetGizmoOrigin(new(GizmoOrigin.X,value,GizmoOrigin.Z));}
    public double GizmoOriginZ {get=>GizmoOrigin.Z;set=>SetGizmoOrigin(new(GizmoOrigin.X,GizmoOrigin.Y,value));}
    void SetGizmoOrigin(Vec3 origin)
    {
        if(!GizmoPositioning || origin==GizmoOrigin)return;
        if(ActiveBinding is {} binding)binding.SetOrigin(origin);
        else Session.Sketch?.SetPlacementOrigin(Session,origin);
        Session.Invalidate();
    }
    public void OpenGizmoPanel()
    {
        if(!ShowGizmoPanel)_beforeGizmoPanel=SelectedRightPanel;
        OpenRightPanel("gizmo");RefreshGizmoPanel();
    }
    public void CloseGizmoPanel()
    {
        if(GizmoPositioning)CancelGizmoPlacement();
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
        foreach(var property in new[]{nameof(CanPlaceGizmo),nameof(GizmoPositioning),nameof(GizmoBindingStatus),nameof(GizmoOperations),nameof(GizmoActiveOperation),nameof(GizmoOriginX),nameof(GizmoOriginY),nameof(GizmoOriginZ)})OnPropertyChanged(property);
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
