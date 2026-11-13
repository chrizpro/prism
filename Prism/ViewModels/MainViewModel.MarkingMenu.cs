using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Controls;
using Prism.Core;
using Prism.Kernel;
using Prism.Core.Assembly;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    MarkItem Act(string label, string icon, Action run, bool enabled = true) =>
        new(label, icon, () => { try { run(); } catch (Exception e) when (e is not OutOfMemoryException) { Session.Say(e.Message, true); } }, enabled);

    MarkItem Cm(string label, string icon, Func<Command> make) { CommandCatalog.Register(make); return Act(label, icon, () => Session.StartCommand(make)); }

    /// <summary>The eight entries of the right-click marking menu, from the context: top = most likely, bottom = leave or destroy.</summary>
    /// <summary>
    /// The ring: seven smart entries from 8 o'clock round to 4 o'clock, and at 6 o'clock a plain menu with the everyday things (undo, delete, view, display, settings).
    /// The old bottom entry (delete / cancel) moves to a free slot when there is one; otherwise it lives in that menu.
    /// </summary>
    public IReadOnlyList<MarkItem?> BuildMarkingItems()
    {
        var raw = RawMarking();
        var res = new MarkItem?[8];
        for (int i = 0; i < 8; i++) if (i != 4 && i < raw.Count) res[i] = raw[i];
        res[4] = new MarkItem("Menu", "", () => OpenTraditionalMenu?.Invoke());
        if (raw.Count > 4 && raw[4] is { } bottom)
            foreach (var k in new[] { 5, 3, 6, 2, 7, 1, 0 }) if (res[k] == null) { res[k] = bottom; break; }
        return res;
    }

    IReadOnlyList<MarkItem?> RawMarking()
    {
        var s = Session;
        if (s.Sketch != null) return SketchMarking();
        if (s.ActiveCommand != null)
            return [Act("OK", "finish", () => s.AcceptCommandPanel()), null, Act("Look at", "look-at", () => s.LookAtSelection()), null,
                    Act("Cancel", "cancel", () => s.CancelCommandPanel()), null, Act("Fit view", "zoom-extents", () => s.ZoomExtents()), null];
        var sel = s.Selection;
        if (sel.Count == 0) return EmptyMarking();
        if (sel[0].Kind == SelKind.Face) return s.Find(sel[0].Obj) is { Kind: ObjKind.Surface } ? SurfaceFaceMarking() : FaceMarking();
        if (sel[0].Kind == SelKind.Edge) return EdgeMarking();
        var objs = s.SelectedObjects.ToList();
        if (objs.Count == 0) return EmptyMarking();
        var hide = Act("Hide", "sel-object", () => { foreach (var o in objs) ToggleVisible(o.Id, false); });
        var del = Act("Delete", "delete", () => s.DeleteSelection());
        var o0 = objs[0];
        if (o0.Asm is ImageInfo) return [Cm("Edit image", "image-edit", () => new EditImageCommand()), Cm("Set scale", "image-scale", () => new CalibrateImageCommand()), hide, null, del, null, null, null];
        switch (o0.Kind)
        {
            case ObjKind.Plane:
                return [Cm("Create sketch", "sketch", () => new CreateSketchCommand()), Act("Look at", "look-at", () => s.LookAtSelection()), hide, null, del, null, null, null];
            case ObjKind.Sketch:
                return [Cm("Extrude", "extrude", () => new ExtrudeCommand()), Cm("Revolve", "revolve", () => new RevolveCommand()), Act("Edit sketch", "sketch-edit", EditSelectedSketch), null,
                        del, hide, null, Cm("Flange", "flange", () => new ContourFlangeCommand())];
            case ObjKind.Surface:
                return [Cm("Thicken", "thicken", () => new ThickenCommand()), Cm("Offset", "offset", () => new OffsetSurfaceCommand()), Cm("Reverse normal", "reverse-normal", () => new ReverseNormalCommand()), Cm("Trim", "trim", () => new TrimSurfaceCommand()),
                        del, hide, Cm("Unsew", "unstitch", () => new UnstitchCommand()), Cm("Extend", "extend-surface", () => new ExtendSurfaceCommand())];
            case ObjKind.Curve:
                return [Cm("Extrude", "extrude-surface", () => new ExtrudeCommand(true)), Cm("Revolve", "revolve-surface", () => new RevolveCommand(true)), Cm("Loft", "loft-surface", () => new LoftCommand(true)), Cm("Plane on curve", "origin-plane", () => new ConstructionPlaneCommand()),
                        del, hide, Cm("Measure", "measure", () => new MeasureCommand()), null];
            case ObjKind.Form:
                return [Cm("Edit freeform", "form-edit", () => new EditFormCommand()), Act("To NURBS", "to-nurbs", () => FreeformActions.ConvertToNurbs(Session)), Cm("Mirror", "mirror", () => new TransformCommand(TransformCommand.Kind.Mirror)), Cm("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move)),
                        del, hide, Act("Materials", "materials", OpenMaterials), Act("Duplicate", "duplicate", () => s.Duplicate())];
            case ObjKind.Mesh:
                return [Cm("Repair", "mesh-repair", () => new MeshRepairCommand()), Cm("Reduce", "mesh-reduce", () => new MeshDecimateCommand()), Cm("Smooth", "mesh-smooth", () => new MeshSmoothCommand()), Cm("Mesh to solid", "mesh-solid", () => new MeshToSolidCommand()),
                        del, hide, Cm("Measure", "measure", () => new MeasureCommand()), Cm("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move))];
        }
        if (objs.Count(o => o.Kind == ObjKind.Solid) >= 2)
            return [Cm("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union)), Cm("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract)), Cm("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect)), Cm("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move)),
                    del, hide, Cm("Measure", "measure", () => new MeasureCommand()), Act("Duplicate", "duplicate", () => s.Duplicate())];
        return [Cm("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move)), Cm("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate)), Cm("Hole", "pcb-hole", () => new HoleCommand()), Act("Duplicate", "duplicate", () => s.Duplicate()),
                del, hide, Act("Materials", "materials", OpenMaterials), Cm("Measure", "measure", () => new MeasureCommand())];
    }

    IReadOnlyList<MarkItem?> SketchMarking()
    {
        var s = Session;
        void Tool(SketchTool t) => s.Sketch?.SetTool(s, t);
        return [Act("Line", "line", () => Tool(new LineTool(SketchTool.Recall("Line")))), Act("Rectangle", "rect", () => Tool(new RectTool((RectMode)SketchTool.Recall("Rectangle")))), Act("Ellipse", "ellipse", () => Tool(new EllipseTool((EllipseMode)SketchTool.Recall("Ellipse")))),
                Act("Trim", "trim", () => Tool(new TrimTool())), Act("Finish sketch", "finish", () => s.ExitSketch(true)), Act("Dimension", "dimension", () => Tool(new DimensionTool((DimMode)SketchTool.Recall("Dimension")))),
                Act("Offset", "offset", () => Tool(new OffsetTool())), Act("Fillet", "fillet", () => Tool(new FilletTool(false)))];
    }

    IReadOnlyList<MarkItem?> FaceMarking()
    {
        var s = Session;
        return [Cm("Push / pull", "pushpull", () => new PushPullCommand()), Cm("Create sketch", "sketch", () => new CreateSketchCommand()), Cm("Shell", "shell", () => new ShellCommand()), Act("Look at", "look-at", () => s.LookAtSelection()),
                Cm("Delete face", "deleteface", () => new DeleteFaceCommand()), Cm("Hole", "pcb-hole", () => new HoleCommand()), Cm("Measure", "measure", () => new MeasureCommand()), Cm("Edit feature", "movehole", () => new FeatureEditCommand())];
    }

    /// <summary>A face of a surface: the edits that apply to a sheet (a solid's Push / pull, Shell and Hole do not).</summary>
    IReadOnlyList<MarkItem?> SurfaceFaceMarking()
    {
        var s = Session;
        return [Cm("Offset face", "pushpull", () => new OffsetFaceCommand()), Cm("Move face", "moveface", () => new MoveFaceCommand()), Cm("Draw on surface", "curve-through", () => new DrawOnSurfaceCommand()), Act("Delete face", "deleteface", () => s.DeleteSelection()),
                null, Act("Isolate", "isolate", IsolateSelected), Cm("Measure", "measure", () => new MeasureCommand()), Cm("Thicken", "thicken", () => new ThickenCommand())];
    }

    IReadOnlyList<MarkItem?> EdgeMarking() =>
        [Cm("Fillet", "fillet", () => new FilletCommand()), Cm("Chamfer", "chamfer", () => new ChamferCommand()), Cm("Measure", "measure", () => new MeasureCommand()), Cm("Construction plane", "origin-plane", () => new ConstructionPlaneCommand()),
         null, null, null, null];

    IReadOnlyList<MarkItem?> EmptyMarking()
    {
        var s = Session;
        return [Cm("Create sketch", "sketch", () => new CreateSketchCommand()), Cm("Box", "box", () => new BoxCommand()), Cm("Cylinder", "cylinder", () => new CylinderCommand()), Cm("Construction plane", "origin-plane", () => new ConstructionPlaneCommand()),
                Act("Fit view", "zoom-extents", () => s.ZoomExtents()), Act("Import image", "image", ImportImage), Cm("Measure", "measure", () => new MeasureCommand()), Cm("Section view", "section", () => new SectionCommand())];
    }

    /// <summary>With faces of a solid selected: what they belong to (a hole, slot, pocket, boss, fillet...) and what can be done to it.</summary>
    void AddFeatureMenu(List<MenuSpec> list)
    {
        var s = Session;
        if (s.Sketch != null || s.ActiveCommand != null) return;
        var faces = s.Selection.Where(i => i.Kind == SelKind.Face).ToList();
        if (faces.Count == 0 || faces.Any(f => s.Find(f.Obj) is not { Kind: ObjKind.Solid, HasBrep: true })) return;
        Feat? feat = null;
        try { if (s.Find(faces[0].Obj) is { } body) feat = FeatureFinder.FeatureOf(body, faces[0].Index); } catch (KernelException) { }
        bool isFeature = feat != null && feat.Kind != FeatureKind.Face; string name = isFeature ? feat!.Name.ToLowerInvariant() : "face";
        void Edit(int action) { var c = new FeatureEditCommand { Factory = () => new FeatureEditCommand() }; s.StartCommand(c); if (s.ActiveCommand == c) c.Preset(faces, action); }
        list.Add(MenuSpec.Sep);
        if (isFeature)
        {
            var body = s.Find(faces[0].Obj)!; int pattern = FeatureFinder.PatternSize(body, feat!);
            if (faces.Count < feat!.Faces.Count) list.Add(new($"Select the whole {name}", () => s.SelectItems(feat.Faces.Select(f => new SelItem(body.Id, SelKind.Face, f)))));
            if (pattern > 0) list.Add(new($"Select the whole pattern ({pattern + 1})", () => s.SelectItems(FeatureFinder.Expand(body, faces[0].Index, PickIntent.Pattern).Select(f => new SelItem(body.Id, SelKind.Face, f)))));
            if (feat.Kind != FeatureKind.Fillet) list.Add(new($"Move {name}…", () => Edit(0)));
            if (feat.Kind != FeatureKind.Fillet) list.Add(new($"Copy {name}…", () => Edit(2)));
            list.Add(new($"Resize {name}…", () => Edit(1)));
            if (feat.Kind is FeatureKind.Hole or FeatureKind.Counterbore or FeatureKind.Countersink) list.Add(new("Change hole type…", () => Edit(4)));
            list.Add(new($"Delete {name}", () => s.DeleteSelection(), Gesture: "Del"));
        }
        else
        {
            list.Add(new("Push / Pull…", () => s.StartCommand(() => new PushPullCommand())));
            list.Add(new("Move face…", () => s.StartCommand(() => new MoveFaceCommand())));
            list.Add(new("Delete face", () => s.DeleteSelection(), Gesture: "Del"));
        }
    }

    public event Action? OpenTraditionalMenu;
    /// <summary>Opens the Settings window; null or a page id (appearance, interaction, workspace, toolbars, ribbon, assistant, keyboard).</summary>
    public Action<string?>? RequestSettings;

    /// <summary>The ordinary menu behind the 6 o'clock entry. Changes with what is going on: a command or sketch open, something selected, hidden things.</summary>
    public IReadOnlyList<MenuSpec> BuildTraditionalMenu()
    {
        var s = Session; var list = new List<MenuSpec>();
        if (s.Sketch != null) list.Add(new("Finish sketch", () => s.ExitSketch(true)));
        if (s.ActiveCommand != null) { list.Add(new("OK", () => s.AcceptCommandPanel())); list.Add(new("Cancel", () => s.CancelCommandPanel())); }
        if (list.Count > 0) list.Add(MenuSpec.Sep);
        list.Add(new("Undo", () => s.Undo(), Enabled: s.CanUndo, Gesture: "Ctrl+Z"));
        list.Add(new("Redo", () => s.Redo(), Gesture: "Ctrl+Y"));
        AddFeatureMenu(list);
        var objs = s.Sketch == null && s.ActiveCommand == null ? s.SelectedObjects.ToList() : [];
        if (objs.Count > 0)
        {
            list.Add(MenuSpec.Sep);
            if (objs.Exists(o => o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form))
                list.Add(new("Hole…", () => s.StartCommand(() => new HoleCommand())));
            if (objs.Count == 1 && s.FeatureNodeFor(objs[0].Id) is { } fn) list.Add(new("Edit feature: " + fn.Label, () => s.EditFeature(fn)));
            var solids = objs.Where(o => o is { Kind: ObjKind.Solid, HasBrep: true }).ToList();
            if (solids.Count > 0)
            {
                bool tracked = solids.All(o => o.TrackHistory);
                list.Add(new(tracked ? "Stop tracking history" : "Track history", () => SetTrackHistory(solids.Select(o => o.Id), !tracked)));
            }
            list.Add(new("Duplicate", () => s.Duplicate()));
            list.Add(new("Hide", () => { foreach (var o in objs) ToggleVisible(o.Id, false); }));
            list.Add(new("Delete", () => s.DeleteSelection(), Gesture: "Del"));
            list.Add(new("Look at", () => s.LookAtSelection(), Gesture: "N"));
        }
        if (s.Objects.Any(o => !o.Visible))
            list.Add(new("Show everything hidden", () => { foreach (var o in s.Objects.Where(o => !o.Visible).ToList()) ToggleVisible(o.Id, true); }));
        list.Add(MenuSpec.Sep);
        void Look(double yaw, double pitch) { s.Cameras[0].SetView(yaw, pitch); s.Invalidate(); }
        list.Add(new("View", Sub:
        [
            new("Front", () => Look(0, 0)), new("Back", () => Look(180, 0)), new("Left", () => Look(-90, 0)), new("Right", () => Look(90, 0)),
            new("Top", () => Look(0, 90)), new("Bottom", () => Look(0, -90)), MenuSpec.Sep, new("Isometric", () => Look(-35, 28)),
        ]));
        list.Add(new("Display", Sub:
        [
            new("Shaded", () => s.Display = DisplayMode.Shaded), new("Shaded with edges", () => s.Display = DisplayMode.ShadedEdges), new("Wireframe", () => s.Display = DisplayMode.Wireframe),
            MenuSpec.Sep, new(s.XRay ? "X-ray: turn off" : "X-ray", () => s.XRay = !s.XRay), new(s.ShowGrid ? "Hide grid" : "Show grid", () => s.ShowGrid = !s.ShowGrid),
        ]));
        if (s.Sketch == null && s.ActiveCommand == null)
            list.Add(new("Select", Sub:
            [
                new("Whole objects", () => s.Mode = SelMode.Object), new("Faces", () => s.Mode = SelMode.Face), new("Edges", () => s.Mode = SelMode.Edge),
            ]));
        list.Add(new("Zoom to fit", () => s.ZoomExtents()));
        list.Add(MenuSpec.Sep);
        list.Add(new("Settings…", () => RequestSettings?.Invoke(null)));
        return list;
    }

    /// <summary>Asks the viewport to show the menu at a position (scripts and tests use this; the mouse gesture does it directly).</summary>
    public event Action<double, double>? OpenMarkingMenu;
    public void ShowMarkingMenu(double x, double y) => OpenMarkingMenu?.Invoke(x, y);

    /// <summary>Right-clicking something selects it first (like most CAD programs), so the menu matches what is under the pointer.</summary>
    public void PrepareMarkingContext()
    {
        var s = Session;
        if (s.Sketch != null || s.ActiveCommand != null) return;
        if (s.Hover is { } h && !s.Selection.Contains(h)) s.SetSelection(h, false);
    }
}

/// <summary>One line of an ordinary menu: a command, a submenu, or a separator.</summary>
public sealed record MenuSpec(string Label, Action? Run = null, IReadOnlyList<MenuSpec>? Sub = null, bool Enabled = true, string? Gesture = null)
{
    public static readonly MenuSpec Sep = new("-");
    public bool IsSeparator => Label == "-";
}
