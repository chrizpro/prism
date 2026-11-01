using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Rendering;
using Prism.Ribbon;
using Prism.Simulation;

namespace Prism.ViewModels;

public sealed partial class PropertyLine(string label, string value) : ObservableObject
{
    public string Label { get; } = label;
    public string Value { get; } = value;
}

public sealed partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<RibbonTab> Tabs { get; } = [];
    public ObservableCollection<PropertyLine> Properties { get; } = [];
    public Func<string, string[], System.Threading.Tasks.Task<string?>>? PickOpenFile { get; set; }
    public Func<string, string, string, System.Threading.Tasks.Task<string?>>? PickSaveFile { get; set; }

    [ObservableProperty] public partial RibbonTab? SelectedTab { get; set; }
    [ObservableProperty] public partial Command? ActiveCommand { get; set; }
    [ObservableProperty] public partial string Prompt { get; set; } = "";
    [ObservableProperty] public partial string DocumentTitle { get; set; } = "Untitled";
    [ObservableProperty] public partial string CursorText { get; set; } = "X 0.00   Y 0.00   Z 0.00";
    [ObservableProperty] public partial bool IsSketching { get; set; }
    [ObservableProperty] public partial bool RibbonCollapsed { get; set; }
    [ObservableProperty] public partial string SelectionTitle { get; set; } = "Nothing selected";
    [ObservableProperty] public partial bool HasSelection { get; set; }
    [ObservableProperty] public partial string RenameText { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AssistanceStatus))] public partial string? CommandError { get; set; }

    partial void OnSelectedTabChanged(RibbonTab? oldValue, RibbonTab? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) { newValue.IsSelected = true; if (!newValue.IsFile && !newValue.IsContextual) _lastNormalTab = newValue; }
    }

    public void SelectTab(RibbonTab tab) { SelectedTab = tab; RibbonCollapsed = false; }
    public void ToggleRibbon() => RibbonCollapsed = !RibbonCollapsed;

    RibbonTab _sketchTab = null!;
    RibbonTab _lastNormalTab = null!;

    public MainViewModel()
    {
        BuildRibbon();
        HookMaterials();
        AttachSession(Session);
        InitDocuments();
        StartStatusTimer();
        InitHome();
        SelectedTab = Tabs[1];
        _lastNormalTab = Tabs[1];
        InitWorkspaces();
        RebuildBrowser(); RebuildHistory();
        RefreshRibbon();
        UpdateTitle();
        if (Environment.GetEnvironmentVariable("PRISM_DEMO") == "1") Guard(Demo);
    }

    /// <summary>Smoke-test scene: exercises primitives, booleans, fillets and a freeform patch.</summary>
    void Demo()
    {
        var s = Session;
        var box = Occ.Box(60, 40, 25);
        var hole = Occ.Translate(Occ.Cylinder(8, 60), new(10, 0, -5));
        var body = Occ.Cut(box, hole);
        var edges = Enumerable.Range(0, body.Count(SubKind.Edge)).Where(i => i % 3 == 0).Take(3).ToArray();
        try { body = Occ.Fillet(body, edges, 2); } catch (KernelException) { }
        s.AddObject(body, "Body", ObjKind.Solid, checkpoint: false);
        s.AddObject(Occ.Translate(Occ.Sphere(14), new(55, 10, 14)), "Sphere", ObjKind.Solid, checkpoint: false);
        var grid = new Vec3[5 * 6];
        for (int r = 0; r < 5; r++) for (int c = 0; c < 6; c++)
            grid[r * 6 + c] = new(-40 + c * 16, -90 + r * 16, 6 * Math.Sin(c * 0.9) * Math.Cos(r * 0.8) + 8);
        s.AddObject(Occ.NurbsSurface(grid, 5, 6), "Patch", ObjKind.Surface, checkpoint: false);
        s.ZoomExtents();
        s.Say("Demo scene loaded");
    }

    // ------------------------------------------------------------ state sync
    void OnStateChanged()
    {
        var s = Session;
        ActiveCommand = s.ActiveCommand;
        IsSketching = s.Sketch != null;
        RebuildQuickBar();
        UpdatePanels(); RebuildSketchPanel(); RefreshToolOptions();
        Prompt = s.ActiveCommand?.HasPanelStep==true ? s.ActiveCommand.PanelHint : s.ActiveTool?.Prompt ?? (s.ActiveCommand != null ? s.ActiveCommand.Hint : s.Sketch != null ? "Pick a sketch tool from the ribbon." : "");
        UpdateActiveToolInstruction();
        if (IsSketching && !_sketchTab.IsVisible) { _sketchTab.IsVisible = true; _lastNormalTab = SelectedTab ?? _lastNormalTab; SelectedTab = _sketchTab; }
        else if (!IsSketching && _sketchTab.IsVisible)
        {
            _sketchTab.IsVisible = false;
            if (SelectedTab == _sketchTab) SelectedTab = _lastNormalTab;
        }
        RefreshRibbon();
    }

    public void RefreshRibbon()
    {
        foreach (var t in Tabs) foreach (var b in t.Buttons) b.Refresh();
        foreach (var b in QuickBar.OfType<RibbonButton>()) b.Refresh();
    }

    void UpdateTitle() => DocumentTitle = (Session.FilePath is { } p ? System.IO.Path.GetFileNameWithoutExtension(p) : "Untitled") + (Session.Dirty ? " •" : "");

    public void UpdateSelectionInfo()
    {
        SyncPartInfo();
        Properties.Clear();
        var objs = Session.SelectedObjects.ToList();
        OnPropertyChanged(nameof(IsAnimationSelected));
        HasSelection = objs.Count > 0;
        UpdateAnimationSummary();
        if (objs.Count == 0) { SelectionTitle = "Nothing selected"; RenameText = ""; return; }
        if (objs.Count > 1) { SelectionTitle = $"{objs.Count} objects selected"; RenameText = ""; return; }
        var o = objs[0];
        SelectionTitle = o.Name; RenameText = o.Name;
        try
        {
            if (o.Form is { } f)
            {
                var (mn, mx) = f.Bounds(); var sz = mx - mn;
                Properties.Add(new("Type", "Freeform (subdivision surface)"));
                Properties.Add(new("Cage", $"{f.V.Count} vertices, {f.F.Count} faces"));
                Properties.Add(new("Size", $"{sz.X:0.##} × {sz.Y:0.##} × {sz.Z:0.##} mm"));
                Properties.Add(new("Closed", f.IsClosed() ? "Yes (can become a solid)" : "No (open surface)"));
                Properties.Add(new("Creased edges", f.Crease.Count(c => c.Value > 0).ToString()));
                return;
            }
            if (o.Tri is { } tm)
            {
                var rep = Meshing.MeshAnalysis.Report(tm);
                Properties.Add(new("Type", "Triangle mesh"));
                Properties.Add(new("Triangles", $"{rep.Triangles:#,0}   ({rep.Vertices:#,0} vertices)"));
                Properties.Add(new("Size", $"{rep.Size.X:0.##} × {rep.Size.Y:0.##} × {rep.Size.Z:0.##} mm"));
                if (rep.Closed) Properties.Add(new("Volume", $"{rep.Volume:#,0.##} mm³"));
                Properties.Add(new("Area", $"{rep.Area:#,0.##} mm²"));
                Properties.Add(new("Watertight", rep.Closed ? "Yes" : $"No - {rep.HoleLoops} hole(s), {rep.BoundaryEdges} open edges"));
                if (rep.NonManifoldEdges > 0) Properties.Add(new("Non-manifold", $"{rep.NonManifoldEdges} edges"));
                if (rep.Shells > 1) Properties.Add(new("Shells", rep.Shells.ToString()));
                Properties.Add(new("Printable", rep.Printable ? "Yes" : "Needs repair"));
                return;
            }
            if (o.Kind == ObjKind.Plane && o.SketchPlane is { } cpl)
            {
                var n = cpl.Normal;
                Properties.Add(new("Type", "Construction plane"));
                Properties.Add(new("Origin", $"{cpl.Origin.X:0.##}, {cpl.Origin.Y:0.##}, {cpl.Origin.Z:0.##}"));
                Properties.Add(new("Normal", $"{n.X:0.###}, {n.Y:0.###}, {n.Z:0.###}"));
                var (hu, hv) = CadSession.PlaneHalfSize(o);
                Properties.Add(new("Size", $"{2 * hu:0.##} × {2 * hv:0.##} mm"));
                return;
            }
            if (o.Asm is AnimInfo anim)
            {
                Properties.Add(new("Type", "Animation"));
                Properties.Add(new("Length", $"{anim.Duration:0.##} s"));
                Properties.Add(new("Loop", anim.Loop ? "Yes" : "No"));
                Properties.Add(new("Easing", anim.Ease ? "Ease in and out" : "Linear"));
                Properties.Add(new("Tracks", anim.Tracks.Count.ToString()));
                Properties.Add(new("Keys", anim.Tracks.Sum(t => t.Keys.Count).ToString()));
                return;
            }
            var p = o.Shape.Props();
            Properties.Add(new("Type", o.Kind.ToString()));
            if (o.Kind == ObjKind.Solid) Properties.Add(new("Volume", $"{p.Volume:#,0.##} mm³"));
            if (o.Kind is ObjKind.Solid or ObjKind.Surface) Properties.Add(new("Area", $"{p.Area:#,0.##} mm²"));
            Properties.Add(new("Size", $"{p.Size.X:0.##} × {p.Size.Y:0.##} × {p.Size.Z:0.##} mm"));
            if (o.Material is { } mat)
            {
                Properties.Add(new("Material", mat.Name));
                if (o.Kind == ObjKind.Solid) { double g = p.Volume * mat.Density / 1000.0; Properties.Add(new("Mass", g >= 1000 ? $"{g / 1000:0.###} kg" : $"{g:0.##} g")); }
            }
            if (o.Kind != ObjKind.Curve && o.Kind != ObjKind.Sketch) Properties.Add(new("Faces / edges", $"{o.Shape.Count(SubKind.Face)} / {o.Shape.Count(SubKind.Edge)}"));
            else Properties.Add(new("Edges", $"{o.Shape.Count(SubKind.Edge)}"));
            Properties.Add(new("Valid", o.Shape.IsValid ? "Yes" : "No — check geometry"));
        }
        catch (Exception e) { Properties.Add(new("Error", e.Message)); }
    }

    public bool IsAnimationSelected => Session.SelectedObjects.Count() == 1 && Session.SelectedObjects.First().Kind == ObjKind.Animation;

    public void Rename(string text)
    {
        var o = Session.SelectedObjects.FirstOrDefault();
        if (o == null || string.IsNullOrWhiteSpace(text) || text == o.Name) return;
        Session.Update(o.Id, x => x.WithName(text.Trim()), label: "Rename");
    }

    public void SetColor(uint color)
    {
        foreach (var o in Session.SelectedObjects.ToList()) Session.Update(o.Id, x => x.WithColor(color), label: "Color");
    }

    public void ToggleVisible(Guid id, bool visible) => Session.Update(id, o => o.WithVisible(visible), checkpoint: false);

    // ------------------------------------------------------------ files
    public void NewFile() => NewDocumentTab();

    public async void OpenFile()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Open", OpenPatterns);
        if (path == null) return;
        OpenPath(path);
    }

    public async void SaveFile(bool saveAs = false)
    {
        var path = Session.FilePath;
        if (path == null || saveAs)
        {
            if (PickSaveFile == null) return;
            path = await PickSaveFile("Save", Session.FilePath == null ? NextPartName() : System.IO.Path.GetFileName(Session.FilePath), "prism");
            if (path == null) return;
        }
        Guard(() => Session.Save(path));
        if (Session.FilePath == path) AddRecent(path);
    }

    public async void ImportMesh()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import mesh", Meshing.MeshIO.ReadPatterns);
        if (path != null) StartMeshImport(path);
    }

    /// <summary>Open the import options step for a mesh file (or import straight away with the remembered options when the user turned the step off).</summary>
    void StartMeshImport(string path) => Guard(() => Session.StartCommand(new ImportMeshCommand(path, !Commands.MeshImportPrefs.Load().Ask)));

    /// <summary>One Import button for every supported format (STEP, IGES, BREP, DXF, STL, OBJ, PLY, 3MF).</summary>
    public async void ImportFile()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import file", CadSession.ImportPatterns);
        if (path != null) await ImportPath(path);
    }

    /// <summary>
    /// STEP, IGES and BREP files are read and meshed on background threads (large assemblies took a minute with the window frozen); the status bar shows progress
    /// and the finished bodies are added in one go. Other formats are quick and load directly.
    /// </summary>
    async Task ImportPath(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".stl" or ".obj" or ".ply" or ".3mf") { StartMeshImport(path); return; }
        if (Path.GetExtension(path).ToLowerInvariant() is not (".step" or ".stp" or ".iges" or ".igs" or ".brep")) { Guard(() => Session.ImportFile(path)); return; }
        bool finished = false;
        var progress = new Progress<string>(m => { if (!finished) Session.Say(m); });
        try
        {
            Session.Say($"Importing {Path.GetFileName(path)}…");
            var prepared = await Task.Run(() => CadSession.PrepareImport(path, progress));
            finished = true;
            Session.AddPrepared(prepared, path);
        }
        catch (Exception e) { finished = true; Session.Say(e.Message, true); }
    }

    public async void ImportStep()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import STEP", ["*.step", "*.stp"]);
        if (path != null) await ImportPath(path);
    }

    public async void Export(string kind)
    {
        if (PickSaveFile == null) return;
        var path = await PickSaveFile($"Export {kind.ToUpper()}", "Export." + kind, kind);
        if (path == null) return;
        Guard(() =>
        {
            switch (kind)
            {
                case "step": Session.ExportStep(path); break;
                case "iges": Session.ExportIges(path); break;
                case "brep": Session.ExportBrep(path); break;
                case "glb": Session.ExportGlb(path); break;
                case "dxf": Session.ExportDxf(path); break;
                default: Session.ExportMesh(path); break;
            }
        });
    }

    void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Session.Say(e.Message, true); }
    }

    // ------------------------------------------------------------ ribbon
    RibbonButton Btn(string label, string icon, Action exec, string desc = "", RibbonSize size = RibbonSize.Large,
        Func<bool>? can = null, Func<bool>? chk = null, string shortcut = "") => new()
    {
        Label = label, Icon = icon, Execute = exec, Description = desc, Size = size, CanExecute = can, GetChecked = chk,
        IsToggle = chk != null, Shortcut = shortcut,
    };

    RibbonButton Cmd(string label, string icon, Func<Command> make, string desc, RibbonSize size = RibbonSize.Large)
    {
        CommandCatalog.Register(make);
        var button = Btn(label, icon, () => Session.StartCommand(make), desc, size, () => !IsSketching, () => ReferenceEquals(Session.ActiveCommand?.Factory, make));
        // Some unrelated tools share an icon (for example Sweep and Sweep 2 rails).
        button.ExampleResolver = () => Help.ToolHelp.ForCommand(make())?.Icon;
        return button;
    }

    /// <summary>A sketch tool button (shows as pressed while any mode of that tool family is active).</summary>
    RibbonButton Tool(string label, string icon, string family, Func<SketchTool> make, string desc, RibbonSize size = RibbonSize.Large) =>
        Btn(label, icon, () => Session.Sketch?.SetTool(Session, make()), desc, size, () => IsSketching, () => Session.Sketch?.Tool?.Name == family);

    /// <summary>A dropdown of tool variants (e.g. Circle: center / 2-point / 3-point).</summary>
    RibbonButton Drop(string label, string icon, string family, string desc, params RibbonButton[] variants) => new()
    {
        Label = label, Icon = icon, Description = desc, Size = RibbonSize.Large, Menu = [.. variants],
        CanExecute = () => IsSketching, GetChecked = () => Session.Sketch?.Tool?.Name == family, IsToggle = true,
    };

    RibbonButton Variant(string label, string icon, Func<SketchTool> make, string desc) =>
        Btn(label, icon, () => Session.Sketch?.SetTool(Session, make()), desc, RibbonSize.Large, () => IsSketching);

    RibbonButton Constrain(string label, string icon, CType type, string desc) =>
        Btn(label, icon, () => Session.Sketch?.SetTool(Session, new ConstraintTool(type)), desc, RibbonSize.Small, () => IsSketching,
            () => Session.Sketch?.Tool is ConstraintTool ct && ct.Mode == type.ToString());
    RibbonButton Sel(string label, string icon, SelMode m, string desc) =>
        Btn(label, icon, () => SetSelectionMode(m), desc, RibbonSize.Small, () => !IsSketching && Session.ActiveCommand?.HasPanelStep!=true,()=>IsSelectionMode(m));

    public void SetSelectionMode(SelMode mode)
    {
        if(Session.ActiveCommand?.HasPanelStep==true)return;
        if(Session.ActiveCommand is EditFormCommand form)form.SetSelectionMode(mode switch {SelMode.Object=>FormMode.Object,SelMode.Edge=>FormMode.Edge,_=>FormMode.Face});
        else Session.Mode=mode;
    }
    public bool IsSelectionMode(SelMode mode)=>Session.ActiveCommand is EditFormCommand form ? form.SelMode==(mode switch {SelMode.Object=>FormMode.Object,SelMode.Edge=>FormMode.Edge,_=>FormMode.Face}) : Session.Mode==mode;

    public bool IsVertexMode=>Session.ActiveCommand is EditFormCommand {SelMode:FormMode.Vertex};
    public void SelectVertices()
    {
        if(Session.ActiveCommand?.HasPanelStep==true)return;
        if(Session.ActiveCommand==null && Session.SelectedObjects.Any(o=>o.Form!=null))Session.StartCommand(()=>new EditFormCommand());
        if(Session.ActiveCommand is EditFormCommand form)form.SetSelectionMode(FormMode.Vertex);
        else Session.Say("Vertex selection is for Freeform cages: select a Freeform form first.");
    }
    RibbonButton VertexSelection() => Btn("Vertex","sel-vertex",SelectVertices,"Select cage vertices (1 while editing Freeform).",RibbonSize.Small,
    ()=>!IsSketching && (Session.ActiveCommand is EditFormCommand {HasPanelStep:false} || Session.ActiveCommand==null && Session.SelectedObjects.Any(o=>o.Form!=null)),
    ()=>Session.ActiveCommand is EditFormCommand {SelMode:FormMode.Vertex});

    RibbonButton View(string label, string icon, double yaw, double pitch) =>
        Btn(label, icon, () => { Session.Cameras[0].SetView(yaw, pitch); Session.Invalidate(); }, $"Look at the model from the {label.ToLower()}.", RibbonSize.Small);

    RibbonButton Disp(string label, string icon, DisplayMode m, string desc) =>
        Btn(label, icon, () => Session.Display = m, desc, RibbonSize.Small, null, () => Session.Display == m);

    /// <summary>A button that runs one of the Edit Form panel actions (only enabled while Edit Form is open).</summary>
    RibbonButton FormAct(string label, string icon, string paramLabel, string desc, RibbonSize size = RibbonSize.Large, Func<bool>? chk = null) =>
        Btn(label, icon, () =>
        {
            if(paramLabel=="Apply crease" && Session.ActiveCommand is EditFormCommand crease){crease.OpenCrease();return;}
            if(paramLabel=="Subdivide" && Session.ActiveCommand is EditFormCommand sub){sub.OpenSubdivide();return;}
            if(chk?.Invoke()==true && Session.ActiveCommand is EditFormCommand {LoopArmed:true} armed){armed.DisarmLoop();return;}
            if (Session.ActiveCommand is EditFormCommand e && e.Params.OfType<ActionParam>().FirstOrDefault(a => a.Label.StartsWith(paramLabel, StringComparison.OrdinalIgnoreCase)) is { } act) act.Run();
        }, desc, size, () => Session.ActiveCommand is EditFormCommand { Work: not null,HasPanelStep:false,Tool:FormEditTool {Binding.Positioning:false} }, chk);

    RibbonButton EditFeatureBtn() => Btn("Edit\nFeature", "feature-edit", EditSelectedFeature,
        "Go back into a feature you made (Extrude, Revolve, Fillet, Shell...) and change its numbers or picks. The model rolls back to just before it; OK puts it back and rebuilds the steps that came after. You can also right-click a step in the History.",
        RibbonSize.Large, () => CanEditFeature);

    static RibbonGroup G(string title, params RibbonItem[] items) => new() { Title = title, Items = [.. items] };

    RibbonGroup DatumGroup() => G("Datum", Split("split:datum", "Open gizmo options or choose a sketch datum.",
        Btn("Gizmo", "move", OpenGizmoFromRibbon, "Position and bind the gizmo; adjust its handles and snapping."),
        Tool("Point", "sketch-point", "Point", () => new PointTool(), "Place a reference point on the active drawing plane of a 2D or 3D sketch."),
        Btn("Axis", "origin-axis", () => SketchSpaceAction("save-axis"), "Save the selected gizmo axis as a datum in a 2D or 3D sketch.", RibbonSize.Small, () => IsSketching),
        Btn("Plane", "origin-plane", () => SketchSpaceAction("save-plane"), "Save the current sketch datum plane.", RibbonSize.Small, () => IsSketching),
        Btn("Save gizmo", "origin-plane", () => SketchSpaceAction("save-gizmo"), "Save the current gizmo as a sketch datum.", RibbonSize.Small, () => IsSketching)));

    void BuildRibbon()
    {
        var small = RibbonSize.Small;
        Func<bool> hasSel = () => Session.Selection.Count > 0 && !IsSketching;

        // Groups every modeling tab needs, so nobody has to switch tabs for the everyday tools (each call builds fresh buttons).
        RibbonGroup SketchGrp() => G("Sketch",
            Cmd("Create\nSketch", "sketch", () => new CreateSketchCommand(), "Draw constrained curves on one or more planes in space. G positions the drawing gizmo; E edits curves; N looks normal."));
        RibbonGroup PlanesGrp() => G("Planes",
            Cmd("Construction\nPlane", "origin-plane", () => new ConstructionPlaneCommand(), "Make planes: offset, angled, midway, square to a curve, or through three points. Sketch on them or use them as references."),
            Cmd("Intersection\nCurve", "section", () => new IntersectCurvesCommand(), "The curve where two planes, faces or bodies cross each other."),
            Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Turn the view to look straight at the selected plane, face or sketch.", RibbonSize.Large, null, null, "N"));
        RibbonGroup CurvesGrp() => G("Curves",
            Cmd("Curve\nIntersect", "section", () => new CurveIntersectCommand(), "Combine planar curves from two different planes into their shared 3D curve."),
            Cmd("Control\nCurve", "curve-cp", () => new CurveByPointsCommand(false), "Place control points; the curve is pulled toward them."),
            Cmd("Curve through\nPoints", "curve-through", () => new CurveByPointsCommand(true), "Place points the curve must pass through."),
            Cmd("Curve from\nEquation", "curve-equation", () => new EquationCurveCommand(false), "Define a curve with formulas of t."),
            Cmd("Curve from\nTable", "curve-table", () => new EquationCurveCommand(true), "Make a curve from a table of x,y,z points."));
        RibbonGroup SelectGrp() => G("Select",
            Sel("Object", "sel-object", SelMode.Object, "Pick whole bodies, surfaces and curves."),
            Followed(Split(RibbonSize.Small, "split:selection-components", "Select faces, edges or Freeform vertices. Click the name to choose a mode.",
                Sel("Face", "sel-face", SelMode.Face, "Pick individual faces."),
                Sel("Edge", "sel-edge", SelMode.Edge, "Pick individual edges."),
                VertexSelection())));
        RibbonGroup TransformGrp() => G("Transform",
            Cmd("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move), "Translate objects."),
            Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Rotate objects around an axis.", small),
            Cmd("Scale", "scale", () => new TransformCommand(TransformCommand.Kind.Scale), "Resize objects uniformly.", small),
            Cmd("Mirror", "mirror", () => new TransformCommand(TransformCommand.Kind.Mirror), "Reflect objects across a plane.", small),
            Btn("Duplicate", "duplicate", Session.Duplicate, "Copy the selected objects.", small, hasSel));
        RibbonGroup AppearanceGrp() => G("Appearance",
            Btn("Materials", "materials", OpenMaterials, "Open the material library: colors, finishes and engineering data you can apply to bodies.", RibbonSize.Large, () => !IsSketching));
        RibbonGroup InspectGrp() => G("Inspect",
            Cmd("Measure", "measure", () => new MeasureCommand(), "Measure sizes, distances and angles between faces, edges, bodies or points."),
            Cmd("Properties", "properties", () => new PropertiesCommand(), "Volume, mass, center of mass and moments of inertia."),
            Cmd("Section\nAnalysis", "section", () => new SectionCommand(), "Cut the model with a plane to see inside."));
        RibbonGroup ShareGrp() => G("Share",
            ToolMenu("AR & sharing", "ar", "Export a model for augmented reality or manage phone sharing.",
                Cmd("AR\nExport", "ar", () => new ArCommand(), "Export GLB and USDZ, choose real or tabletop scale, and share to a phone on your Wi-Fi."),
                Btn("Stop\nSharing", "cancel", () => { Core.Export.ArExport.StopShare(); Session.Say("Sharing stopped."); }, "Stop serving the AR page to phones.", can: () => Core.Export.ArExport.Sharing)));

        // ---- File
        var file = new RibbonTab
        {
            Title = "File", IsFile = true,
            Groups =
            [
                G("Document",
                    Btn("Home", "home", OpenHome, "The home screen: your workspace folder, recent parts, new part."),
                    Btn("New", "new", NewFile, "Start an empty document.", shortcut: "Ctrl+N"),
                    Btn("Open", "open", OpenFile, "Open an Prism or STEP file.", shortcut: "Ctrl+O"),
                    Btn("Save", "save", () => SaveFile(), "Save the document.", shortcut: "Ctrl+S"),
                    Btn("Save As", "saveas", () => SaveFile(true), "Save under a new name.", small)),
                G("Exchange",
                    Btn("Import\nFile", "import", ImportFile, "Bring in a file by its type: STEP, IGES, BREP, DXF (as a sketch), STL, OBJ, PLY or 3MF."),
                    Btn("Import\nSTEP", "import", ImportStep, "Bring a STEP (.step/.stp) model into the document."),
                    Btn("Export\nSTEP", "step", () => Export("step"), "Write all visible objects as a STEP file."),
                    Btn("Export\nIGES", "step", () => Export("iges"), "Write all visible solids and surfaces as an IGES file (.iges).", small),
                    Btn("Export\nBREP", "step", () => Export("brep"), "Write the exact geometry as an Open CASCADE BREP file.", small),
                    Btn("Export\nglTF", "stl", () => Export("glb"), "Write a binary glTF (.glb) for viewers, games and the web: Y-up, metres, one colour per body. Exports the selection, or everything if nothing is selected.", small),
                    Btn("Export\nDXF", "step", () => Export("dxf"), "Write the selected sketches (or every visible one) as a DXF of lines, circles and arcs, for laser, waterjet and CNC.", small),
                    Btn("Import\nMesh", "mesh-import", ImportMesh, "Bring in a scan or print file: STL, OBJ, PLY or 3MF."),
                    Btn("Export\n3MF", "stl", () => Export("3mf"), "Write a 3MF file for printing: keeps each body separate, in millimetres. Exports the selection, or everything if nothing is selected."),
                    Btn("Export\nSTL", "stl", () => Export("stl"), "Write a triangle mesh (STL). Exports the selection, or everything if nothing is selected."),
                    Btn("Export\nOBJ", "stl", () => Export("obj"), "Write a Wavefront OBJ mesh.", small),
                    Btn("Export\nPLY", "stl", () => Export("ply"), "Write a PLY mesh.", small)),
            ],
        };

        // ---- Home
        var home = new RibbonTab
        {
            Title = "Home",
            Groups =
            [
                G("Sketch",
                    Cmd("Create\nSketch", "sketch", () => new CreateSketchCommand(), "Draw constrained curves on one or more planes in space. G positions the drawing gizmo; E edits curves; N looks normal.")),
                G("Planes",
                    Cmd("Construction\nPlane", "origin-plane", () => new ConstructionPlaneCommand(), "Make planes: offset (one or a whole stack), angled, midway, square to a curve, or through three points. Sketch on them or use them as references."),
                    Cmd("Intersection\nCurve", "section", () => new IntersectCurvesCommand(), "The curve where two planes, faces or bodies cross each other."),
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Turn the view to look straight at the selected plane, face or sketch (or the sketch you are drawing).", RibbonSize.Large, null, null, "N")),
                G("Create", PrimitiveSplit()),
                G("Features",
                    Cmd("Extrude", "extrude", () => new ExtrudeCommand(), "Push a sketch profile or face into a solid."),
                    Cmd("Revolve", "revolve", () => new RevolveCommand(), "Spin a profile around an axis."),
                    Cmd("Emboss", "emboss", () => new EmbossCommand(), "Raise text or a sketched shape off a face, or engrave it into the body.")),
                G("Modify",
                    Cmd("Hole", "hole", () => new HoleCommand(), "Create plain or modeled threaded holes on a body or face, with blind or through-all depth."),
                    Cmd("Fillet", "fillet", () => new FilletCommand(), "Round off selected edges."),
                    Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Bevel selected edges.")),
                G("Combine",
                    Cmd("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union), "Merge bodies into one.", small),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body out of another.", small),
                    Cmd("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect), "Keep only the shared volume.", small)),
                G("Transform",
                    Cmd("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move), "Translate objects.", small),
                    Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Rotate objects around an axis.", small),
                    Cmd("Scale", "scale", () => new TransformCommand(TransformCommand.Kind.Scale), "Resize objects uniformly.", small),
                    Cmd("Mirror", "mirror", () => new TransformCommand(TransformCommand.Kind.Mirror), "Reflect objects across a plane.", small),
                    Btn("Duplicate", "duplicate", Session.Duplicate, "Copy the selected objects.", small, hasSel)),
                G("Appearance",
                    Btn("Materials", "materials", OpenMaterials, "Open the material library: engineering data for steels, aluminum, plastics and more, with colors and finishes you can apply to bodies.", RibbonSize.Large, () => !IsSketching)),
                G("Image",
                    Btn("Import\nImage", "image", ImportImage, "Bring in a picture to trace or measure against (PNG, JPG, BMP, GIF). It sits behind the model.", RibbonSize.Large, () => !IsSketching),
                    Cmd("Edit\nImage", "image-edit", () => new EditImageCommand(), "Change a picture's opacity, size, position, turn or mirroring."),
                    Cmd("Set\nScale", "image-scale", () => new CalibrateImageCommand(), "Make a picture true size: click two points on it and say how far apart they really are.")),
                G("Pattern",
                    Cmd("Linear", "pattern-linear", () => new LinearPatternCommand(), "Repeat objects along one, two or three directions.", small),
                    Cmd("Circular", "pattern-circular", () => new CircularPatternCommand(), "Repeat objects around an axis.", small),
                    Cmd("Along Curve", "pattern-curve", () => new CurvePatternCommand(), "Repeat objects along a curve or edge chain.", small),
                    Cmd("Fill", "pattern-fill", () => new FillPatternCommand(), "Fill a face or closed sketch with copies.", small),
                    Cmd("Table", "pattern-table", () => new TablePatternCommand(), "Place copies at positions from a table.", small)),
                G("Select",
                    Sel("Object", "sel-object", SelMode.Object, "Pick whole bodies, surfaces and curves."),
                    Sel("Face", "sel-face", SelMode.Face, "Pick individual faces."),
                    Sel("Edge", "sel-edge", SelMode.Edge, "Pick individual edges.")),
            ],
        };

        // ---- Solid
        var solid = new RibbonTab
        {
            Title = "Solid",
            Groups =
            [
                SketchGrp(), PlanesGrp(),
                G("Primitives", PrimitiveSplit()),
                G("Features",
                    Cmd("Extrude", "extrude", () => new ExtrudeCommand(), "Push a sketch profile or face into a solid. Join, cut or intersect with an existing body."),
                    Cmd("Emboss", "emboss", () => new EmbossCommand(), "Raise text or a sketched shape off a face, or engrave it into the body."),
                    Cmd("Rib", "rib", () => new RibCommand(), "Add a reinforcing rib from a planar sketch to a solid body."),
                    Cmd("Revolve", "revolve", () => new RevolveCommand(), "Spin a profile around an axis.")),
                G("Modify",
                    Cmd("Hole", "hole", () => new HoleCommand(), "Create plain or modeled threaded holes on a body or face, with blind or through-all depth."),
                    Cmd("Fillet", "fillet", () => new FilletCommand(), "Round off selected edges."),
                    Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Bevel selected edges."),
                    Cmd("Shell", "shell", () => new ShellCommand(), "Hollow out a body, optionally opening faces."),
                    Cmd("Draft", "draft", () => new DraftFaceCommand(), "Lean walls away from a pull direction."),
                    Cmd("Split\nBody", "splitbody", () => new SplitBodyCommand(), "Cut a body into separate pieces with a plane, face or surface.")),
                G("Direct",
                    Cmd("Hole", "hole", () => new HoleCommand(), "Create plain or modeled threaded holes on any exact body, including imported parts."),
                    Cmd("Push /\nPull", "pushpull", () => new PushPullCommand(), "Drag flat faces out or in by a distance. No history needed: works on imported parts too."),
                    Cmd("Move Hole\n/ Boss", "movehole", () => new MoveHoleCommand(), "Slide a hole or boss to a new place (or change its diameter) in one step."),
                    Cmd("Delete\nFace", "deleteface", () => new DeleteFaceCommand(), "Remove a hole, fillet, pocket or boss and let the neighbouring faces close the gap.")),
                G("Combine",
                    Cmd("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union), "Merge bodies into one."),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body out of another."),
                    Cmd("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect), "Keep only the shared volume.")),
                TransformGrp(),
                G("Pattern",
                    Cmd("Linear", "pattern-linear", () => new LinearPatternCommand(), "Repeat objects along one, two or three directions (grids and blocks)."),
                    Cmd("Circular", "pattern-circular", () => new CircularPatternCommand(), "Repeat objects around an axis, with optional concentric rings."),
                    Cmd("Along\nCurve", "pattern-curve", () => new CurvePatternCommand(), "Repeat objects along a curve or edge chain, turning each copy to follow it."),
                    Cmd("Fill", "pattern-fill", () => new FillPatternCommand(), "Fill a face or closed sketch with copies: square, hex, ring or spiral layouts, with a margin from the edge."),
                    Cmd("Table", "pattern-table", () => new TablePatternCommand(), "Place copies at positions you type: irregular layouts.")),
                SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        var direct = new RibbonTab
        {
            Title = "Direct Modeling",
            Groups =
            [
                G("Add",
                    Cmd("Hole", "hole", () => new HoleCommand(), "Add a new plain or threaded hole to an exact body, including imported parts."),
                    Cmd("Fillet", "fillet", () => new FilletCommand(), "Add a round to selected edges."),
                    Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Add a bevel to selected edges."),
                    Cmd("Shell", "shell", () => new ShellCommand(), "Hollow out a body, optionally opening faces."),
                    Cmd("Split\nBody", "splitbody", () => new SplitBodyCommand(), "Cut a body into separate pieces with a plane, face or surface.")),
                G("Edit",
                    Cmd("Push /\nPull", "pushpull", () => new PushPullCommand(), "Edit existing faces, holes and bosses by dragging them out or in. No history needed. Can include matching features or the whole pattern."),
                    Cmd("Move\nFace", "moveface", () => new MoveFaceCommand(), "Edit existing flat faces: slide and tilt them; the body stretches to follow. Can include matching features or the whole pattern."),
                    Cmd("Move Hole\n/ Boss", "movehole", () => new MoveHoleCommand(), "Edit an existing hole or boss: move it or change its diameter. Can include matching features or the whole pattern."),
                    Cmd("Replace\nFace", "replaceface", () => new ReplaceFaceCommand(), "Edit existing faces so they lie on another plane or face."),
                    Cmd("Draft", "draft", () => new DraftFaceCommand(), "Edit existing walls: lean them away from a pull direction."),
                    Cmd("Delete\nFace", "deleteface", () => new DeleteFaceCommand(), "Remove an existing hole, fillet, pocket or boss and let the neighbouring faces close the gap. Can include matching features or the whole pattern."),
                    Cmd("Edit\nFreeform", "form-edit", () => new EditFormCommand(), "Freeform bodies are shaped by their cage: pick faces, edges or points and extrude, inset, delete or move them.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Surface
        var surface = new RibbonTab
        {
            Title = "Surface",
            Groups =
            [
                SketchGrp(), PlanesGrp(), CurvesGrp(),
                G("Create",
                    Cmd("Extrude", "extrude-surface", () => new ExtrudeCommand(true), "Extrude a curve or sketch into a surface."),
                    Cmd("Revolve", "revolve-surface", () => new RevolveCommand(true), "Revolve a curve into a surface."),
                    Split("split:sweep-pipe", "Build a surface by carrying a section along a path: sweep a profile, or pipe a round tube. Click the name (or right-click) for the others.",
                        Cmd("Sweep", "sweep-surface", () => new SweepCommand(true), "Sweep a curve along a path."),
                        Cmd("Pipe", "sweep-surface", () => new PipeCommand(), "A round tube along a path curve; set the radius, or make it a solid.")),
                    Cmd("Loft", "loft-surface", () => new LoftCommand(true), "Blend a surface through several curves."),
                    Cmd("Boundary\nSurface", "patch", () => new BoundarySurfaceCommand(), "Span a surface over curves and surface edges with G0/G1/G2 continuity per edge, a weight per edge, and optional guide curves.")),
                G("Fill",
                    Cmd("Planar", "planar", () => new PlanarSurfaceCommand(), "Fill closed sketches with a flat surface."),
                    Cmd("Ruled", "ruled", () => new RuledSurfaceCommand(), "Join two curves with straight rulings."),
                    Cmd("Patch", "patch", () => new PatchCommand(), "Fill a boundary loop with a smooth patch.")),
                G("Modify",
                    Cmd("Offset", "offset", () => new OffsetSurfaceCommand(), "Create a parallel copy of a surface or face."),
                    Cmd("Thicken", "thicken", () => new ThickenCommand(), "Give a surface thickness to make a solid."),
                    Cmd("Sew", "knit", () => new SewCommand(), "Sew (stitch) surfaces together; closed shells become solids."),
                    Cmd("Unsew", "unstitch", () => new UnstitchCommand(), "Take a sewn shell, or just some of its faces, apart into separate surfaces."),
                    Cmd("Trim", "trim", () => new TrimSurfaceCommand(), "Cut a surface with a plane, another surface or a solid and keep the side you want."),
                    Cmd("Untrim", "untrim", () => new UntrimCommand(), "Undo a trim: restore the whole natural surface, or just fill the holes in a face."),
                    Cmd("Extend", "extend-surface", () => new ExtendSurfaceCommand(), "Grow a surface past one of its edges, continuing its shape."),
                    Cmd("Reverse\nNormal", "reverse-normal", () => new ReverseNormalCommand(), "Turn a surface around so its other side faces out."),
                    Cmd("Enclosed\nBody", "fill", () => new BoundaryFillCommand(), "Make solids from the spaces enclosed by surfaces, planes and bodies."),
                    Cmd("Flatten /\nRefold", "flatten", () => new FlattenCommand(), "Flatten a surface and keep the layout, or return to wrap curves, sheets and solids onto its source or another surface."),
                    Cmd("Warp", "warp", () => new WarpCommand(), "Bend, twist, taper or stretch bodies, surfaces, curves, forms or meshes along an axis.")),
                G("Convert", Btn("Convert to\nNURBS", "to-nurbs", () => FreeformActions.ConvertToNurbs(Session), "Convert the selection to editable NURBS geometry.", RibbonSize.Large, hasSel)),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Form (subdivision modeling)
        var form = new RibbonTab
        {
            Title = "Freeform",
            Groups =
            [
                SketchGrp(), PlanesGrp(), CurvesGrp(),
                G("Create",
                    Split("split:form-shape", "A starting shape for freeform modeling: plane, box, sphere, cylinder or torus. Click the name (or right-click) for the others.",
                        Cmd("Plane", "form-plane", () => new FormPlaneCommand(), "A flat grid of faces to push, pull and sculpt."),
                        Cmd("Box", "form-box", () => new FormBoxCommand(), "A box cage that smooths into a rounded block."),
                        Cmd("Sphere", "form-sphere", () => new FormSphereCommand(), "A quad-ball: a sphere made of evenly sized quads."),
                        Cmd("Cylinder", "form-cylinder", () => new FormCylinderCommand(), "A cylinder cage with optional caps."),
                        Cmd("Torus", "form-torus", () => new FormTorusCommand(), "A ring-shaped cage.")),
                    Cmd("Create\nFace", "form-face", () => new CreateFaceCommand(), "Draw a face corner by corner. Connect to an existing form by clicking its corners."),
                    Split("split:form-profile", "Build a form from curves or sketches: loft, sweep, pipe, extrude or revolve. Click the name (or right-click) for the others.",
                        Cmd("Loft", "form-loft", () => new FormLoftCommand(), "Build a form through two or more curves or sketches."),
                        Cmd("Sweep", "form-sweep", () => new FormSweepCommand(), "Carry a profile along a path to build a form (pipes, handles, limbs)."),
                        Cmd("Pipe", "form-sweep", () => new FormPipeCommand(), "A round, taperable tube along a path (limbs, tails, stems)."),
                        Cmd("Extrude\nProfile", "form-extrude", () => new FormExtrudeCommand(), "Pull a profile out into a form, optionally tapering."),
                        Cmd("Revolve", "form-revolve", () => new FormRevolveCommand(), "Spin a profile around an axis into a form (vases, knobs, domes)."))),
                G("Modify",
                    Cmd("Edit\nFreeform", "form-edit", () => new EditFormCommand(), "Select vertices, edges or faces and move them with the manipulator."),
                    FormAct("Extrude", "form-extrude", "Extrude", "Extrude the selected faces (or open edges), then drag the arrows."),
                    FormAct("Slide", "form-loop", "Slide selected edge loop", "Move the selected edge loop along its neighbouring edges without changing the shape much. The amount is in the panel."),
                    FormAct("Subdivide", "form-subdivide", "Subdivide", "Split the selected faces into smaller faces, or every face when none are selected."),
                    ToolMenu("More", "form-edit", "Less frequent editing tools. Selection helpers such as Grow and Shrink stay in the Edit Freeform panel.",
                        FormAct("Chamfer", "chamfer", "Chamfer selected edges", "Cut a flat bevel along the selected inner edges. The width is 'Bevel width' in the panel.", small),
                        FormAct("Delete", "form-delete", "Delete selection", "Delete the selected faces, edges or points.", small),
                        FormAct("Merge faces", "form-weld", "Merge selected faces", "Join the selected touching faces into one.", small),
                        FormAct("Collapse", "form-weld", "Collapse selected edges", "Shrink the selected edges to single points.", small),
                        FormAct("Split face", "form-subdivide", "Split face between", "Cut a face in two between two selected corners.", small),
                        FormAct("Bevel", "form-crease", "Bevel selected edges", "Add support loops beside the selected edges to tighten them.", small),
                        FormAct("Smooth", "form-flip", "Smooth selection", "Relax the selected points toward their neighbours.", small),
                        FormAct("Flatten", "form-face", "Flatten selection", "Press the selected points onto a plane.", small),
                        FormAct("Fill holes", "form-face", "Fill holes", "Cap open borders with faces.", small),
                        FormAct("Thicken", "form-box", "Thicken", "Give the cage wall thickness (Bevel width).", small))),
                G("Loops",
                    FormAct("Insert\nEdge", "form-loop", "Insert edge", "Click an edge to add an edge loop across it. The tool stays on for more clicks until you press Esc or pick another tool. Count and position are in the panel.", RibbonSize.Large, () => Session.ActiveCommand is EditFormCommand {LoopArmed:true}),
                    FormAct("Inset", "form-face", "Inset selected faces", "Shrink the selected faces into a smaller face with a ring of new faces around it. Amount, depth and 'each face on its own' are in the panel."),
                    FormAct("Select\nLoop", "form-loop", "Select edge loop or ring", "Select a whole edge loop or edge ring from the selected edge. Choose Loop (end to end) or Ring (parallel across quads) under 'Edge selection' in the panel; Alt+L and Alt+R pick directly."),
                    FormAct("Bridge", "form-bridge", "Bridge", "Join two selected groups of open edges with a strip of faces.")),
                G("Lock and align",
                    FormAct("Freeze", "form-freeze", "Freeze selection", "Lock the selected faces, edges or points so they cannot move (shown in blue)."),
                    FormAct("Unfreeze", "form-freeze", "Unfreeze selection", "Release the selected points."),
                    FormAct("Align to\nCurve", "form-align", "Align to the curve", "Pull the selected points (or all) onto a curve: pick it in 'Curve to align to'.")),
                G("Components",
                    FormAct("Smooth\nsurface", "form-flip", "Smooth surface", "Switch between the smoothed surface and the plain cage faces. Levels are under Smoothness in the panel.", small, () => Session.ActiveCommand is EditFormCommand {SmoothOn:true}),
                    FormAct("Weld", "form-weld", "Weld", "Merge the selected vertices into one.", small),
                    FormAct("Crease", "form-crease", "Apply crease", "Make the selected edges sharp.", small),
                    FormAct("Merge\nedges", "form-weld", "Merge selected edges", "Remove the selected inner edges, joining the faces on either side.", small),
                    FormAct("Unweld\nedges", "form-weld", "Unweld selected edges", "Split the form along the selected edges so each side can move on its own.", small),
                    FormAct("Pull", "form-flip", "Pull points", "Pull the selected points (or all) onto the smooth surface the cage produces.", small),
                    FormAct("Flip normals", "form-flip", "Flip", "Reverse which side of the form is the outside.", small)),
                G("Symmetry",
                    FormAct("Mirror\nand weld", "form-mirror", "Mirror", "Reflect the form across a plane and join the halves at the seam.")),
                G("Quality",
                    Cmd("Check\nQuality", "form-edit", () => new FormQualityCommand(), "Mark poles, triangles and n-gons, see how stretched the quads are and how the curvature varies, and color the surface by its distance to a reference.", RibbonSize.Large)),
                G("Finish",
                    Btn("Convert\nfewer faces", "form-nurbs", () => FreeformActions.ConvertToNurbs(Session, fewerFaces: true), "Like Convert to NURBS, but each irregular face becomes one fitted patch instead of four: a lighter solid that is quicker for booleans and fillets, a little less exact near poles.", RibbonSize.Large, hasSel),
                    Btn("Convert to\nNURBS", "form-nurbs", () => FreeformActions.ConvertToNurbs(Session), "Turn the selected form into exact NURBS surfaces / a solid for CAD operations.", RibbonSize.Large, hasSel)),
                TransformGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Freeform
        var freeform = new RibbonTab
        {
            Title = "NURBS",
            Groups =
            [
                G("Sketch", Btn("Draw (K)", "sketch", StartFluidSketch, "Draw flat, then shape curves in space.")), PlanesGrp(),
                G("Curves",
                    Cmd("Curve\nIntersect", "section", () => new CurveIntersectCommand(), "Combine planar curves from two different planes into their shared 3D curve."),
                    Cmd("Control\nCurve", "curve-cp", () => new CurveByPointsCommand(false), "Place control points; the curve is pulled toward them."),
                    Cmd("Curve through\nPoints", "curve-through", () => new CurveByPointsCommand(true), "Place points the curve must pass through."),
                    Cmd("Curve from\nEquation", "curve-equation", () => new EquationCurveCommand(false), "Define a curve with formulas of t: Cartesian, cylindrical or spherical. Springs, spirals, cams, wave profiles."),
                    Cmd("Curve from\nTable", "curve-table", () => new EquationCurveCommand(true), "Make a curve from a table of x,y,z points typed, pasted or read from a file.")),
                G("Edit curves",
                    Cmd("Fillet\nCurve", "fillet", () => new CurveFilletCommand(false), "Round the corners of a flat curve or sketch."),
                    Cmd("Chamfer\nCurve", "chamfer", () => new CurveFilletCommand(true), "Cut the corners of a flat curve or sketch."),
                    Cmd("Offset\nCurve", "offset", () => new OffsetCurveCommand(), "Copy a flat curve parallel to itself."),
                    Cmd("Blend\nCurve", "curve-through", () => new BlendCurveCommand(), "Join two curves with a curve that keeps their position, direction or bend."),
                    Cmd("Join", "knit", () => new JoinCurvesCommand(), "Join curves that touch end to end into one."),
                    Cmd("Explode", "unstitch", () => new ExplodeCurveCommand(), "Break a curve or sketch into its segments."),
                    Cmd("Trim\nCurve", "trim", () => new TrimCurveCommand(), "Keep part of a curve."),
                    Cmd("Split\nCurve", "trim", () => new SplitCurveCommand(), "Cut a curve in several pieces."),
                    Cmd("Extend\nCurve", "extend-surface", () => new ExtendCurveCommand(), "Grow a curve past its end, keeping it smooth."),
                    Cmd("Rebuild\nCurve", "raise-degree", () => new RebuildCurveCommand(), "Redraw a curve with a chosen number of points."),
                    Cmd("Divide", "origin-point", () => new DivideCurveCommand(), "Points at equal distances along a curve; reports the length.")),
                G("Curves from surfaces",
                    Cmd("Project", "section", () => new ProjectCurveCommand(), "Drop curves onto a surface, straight or by pulling to the nearest point."),
                    Cmd("Isocurves", "curve-through", () => new IsoCurveCommand(), "Pull curves of constant U or V out of a surface."),
                    Cmd("Intersect", "section", () => new IntersectSurfacesCommand(), "Curves where two surfaces or bodies cross."),
                    Cmd("Contour", "section", () => new ContourCommand(), "Slice a body into evenly spaced section curves."),
                    Cmd("Duplicate\nEdge", "curve-through", () => new DuplicateEdgeCommand(), "Copy the edges you click as curves."),
                    Cmd("Curvature\nGraph", "curvature", () => new CurveCurvatureCommand(), "Show a comb of hairs along a curve to judge how smooth it is.")),
                G("Surfaces",
                    Cmd("Boundary\nSurface", "patch", () => new BoundarySurfaceCommand(), "Span a surface over curves and surface edges with G0/G1/G2 continuity per edge, a weight per edge, and optional guide curves."),
                    Cmd("Blend\nSurface", "patch", () => new BlendSurfaceCommand(), "A surface between two surface edges with tangent or curvature continuity."),
                    Cmd("Pipe", "sweep-surface", () => new PipeCommand(), "A round, optionally tapered pipe along a curve."),
                    Cmd("Sweep\n2 rails", "sweep-surface", () => new SweepTwoRailsCommand(), "Carry a profile along two rails."),
                    Cmd("Rebuild\nSurface", "raise-degree", () => new RebuildSurfaceCommand(), "Redraw a surface as one patch with a chosen grid of points."),
                    Cmd("Network\nSurface", "patch", () => new NetworkSurfaceCommand(), "A surface inside a closed outline that also passes through curves running across it."),
                    Cmd("NURBS\nPatch", "nurbs-patch", () => new NurbsPatchCommand(), "Create a grid of control points to shape freely."),
                    Cmd("Extrude", "extrude-surface", () => new ExtrudeCommand(true), "Extrude a curve or sketch into a surface."),
                    Cmd("Revolve", "revolve-surface", () => new RevolveCommand(true), "Revolve a curve into a surface."),
                    Cmd("Sweep", "sweep-surface", () => new SweepCommand(true), "Sweep a curve along a path."),
                    Cmd("Loft", "loft-surface", () => new LoftCommand(true), "Blend a surface through curves."),
                    Cmd("Patch", "patch", () => new PatchCommand(), "Fill a boundary loop with a smooth patch.")),
                G("Shape",
                    Cmd("Edit\nPoints", "edit-points", () => new EditPointsCommand(), "Drag NURBS control points, with soft influence on neighbours."),
                    Cmd("Sculpt", "sculpt", () => new SculptCommand(), "Brush the surface: pull, push, move or smooth."),
                    Cmd("Deform", "sculpt", () => new DeformCommand(), "Bend, twist, taper or stretch curves and surfaces along an axis.")),
                G("Detail",
                    Btn("Add\nDetail", "add-detail", () => FreeformActions.AddDetail(Session), "Insert knots so the selected curve/surface has more control points without changing its shape.", RibbonSize.Large, hasSel),
                    Btn("Raise\nDegree", "raise-degree", () => FreeformActions.RaiseDegree(Session), "Increase the polynomial degree for smoother, more flexible surfaces.", RibbonSize.Large, hasSel),
                    Btn("Convert to\nNURBS", "to-nurbs", () => FreeformActions.ConvertToNurbs(Session), "Convert the selection to editable NURBS geometry.", RibbonSize.Large, hasSel)),
                G("Analyze",
                    Btn("Zebra\nStripes", "zebra", () => Session.Zebra = !Session.Zebra, "Show reflection stripes: smooth, continuous stripes mean a smooth surface.", RibbonSize.Large, null, () => Session.Zebra),
                    Cmd("Curvature\nMap", "curvature", () => new CurvatureCommand(), "Color surfaces by curvature.")),
                G("Finish",
                    Cmd("Sew", "knit", () => new SewCommand(), "Sew (stitch) surfaces together; closed shells become solids."),
                    Cmd("Unsew", "unstitch", () => new UnstitchCommand(), "Take a sewn shell, or just some of its faces, apart into separate surfaces."),
                    Cmd("Thicken", "thicken", () => new ThickenCommand(), "Give a surface thickness to make a solid."),
                    Cmd("Trim", "trim", () => new TrimSurfaceCommand(), "Cut a surface with a plane, another surface or a solid and keep the side you want."),
                    Cmd("Untrim", "untrim", () => new UntrimCommand(), "Undo a trim: restore the whole natural surface, or just fill the holes in a face."),
                    Cmd("Extend", "extend-surface", () => new ExtendSurfaceCommand(), "Grow a surface past one of its edges, continuing its shape."),
                    Cmd("Reverse\nNormal", "reverse-normal", () => new ReverseNormalCommand(), "Turn a surface around so its other side faces out."),
                    Cmd("Boundary\nSurface", "patch", () => new BoundarySurfaceCommand(), "Fill openings or span a boundary of edges and curves, with G0/G1/G2 continuity per edge."),
                    Cmd("Offset", "offset", () => new OffsetSurfaceCommand(), "Create a parallel copy of a surface or face.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Sheet metal (the essentials: flange, flat pattern, cut-outs)
        var sheet = new RibbonTab
        {
            Title = "Sheet Metal",
            Groups =
            [
                SketchGrp(), PlanesGrp(),
                G("Sheet metal",
                    Cmd("Flange", "flange", () => new ContourFlangeCommand(), "Make bent sheet metal from a sketch of its cross-section: set thickness, bend radius and width. Corners become bends."),
                    Cmd("Flat\nPattern", "flat-pattern", () => new FlatPatternCommand(), "Unfold a bent body into its flat pattern with bend lines; save it as DXF for laser or waterjet cutting.")),
                G("Cut-outs",
                    Cmd("Extrude", "extrude", () => new ExtrudeCommand(), "Extrude a sketch, and set Operation to Cut to make holes and slots through the metal. Use Through all to go through every layer."),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body out of another."),
                    Cmd("Fillet", "fillet", () => new FilletCommand(), "Round the corners of flanges and cut-outs."),
                    Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Bevel selected edges.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Script (write Python in the app, or in your own editor)
        var scriptTab = new RibbonTab
        {
            Title = "Script",
            Groups =
            [
                G("Write",
                    Btn("New\nScript", "script-new", NewScript, "Start a new script from a small example.", RibbonSize.Large),
                    Btn("Run\nScript...", "script-run", RunScriptFile, "Pick a script file and run it.", RibbonSize.Large)),
                G("Your own editor",
                    Btn("VS Code", "script", () => OpenScriptInEditor("vscode"), "Open the current script in Visual Studio Code. The scripting library sits next to your scripts so it just works there.", RibbonSize.Large),
                    Btn("PyCharm", "script", () => OpenScriptInEditor("pycharm"), "Open the current script in PyCharm.", RibbonSize.Large),
                    Btn("Scripts\nFolder", "open", OpenScriptsFolder, "Open the folder that holds your scripts and the examples.", RibbonSize.Large)),
            ],
        };

        // ---- Analyze
        var analyze = new RibbonTab
        {
            Title = "Analyze",
            Groups =
            [
                G("Drawing",
                    Cmd("Make\nDrawing", "drawing", () => new DrawingCommand(), "Make an engineering drawing (PDF, SVG or DXF): views, dimensions, hole callouts, sections, title block and bill of materials.")),
                SelectGrp(),
                G("Inspect",
                    Cmd("Measure", "measure", () => new MeasureCommand(), "Measure sizes, distances and angles between faces, edges, bodies or points."),
                    Cmd("Properties", "properties", () => new PropertiesCommand(), "Volume, mass (with materials), center of mass and moments of inertia.")),
                G("Section",
                    Cmd("Section\nAnalysis", "section", () => new SectionCommand(), "Cut the model with a plane to see inside; reports section area."),
                    Btn("Bounding\nBox", "bbox", () => Session.ShowBounds = !Session.ShowBounds, "Show the overall size box of the selection (or everything).", RibbonSize.Large, null, () => Session.ShowBounds)),
                G("Check",
                    Cmd("Interference", "interference", () => new InterferenceCommand(), "Find where solids overlap."),
                    Cmd("Check\nGeometry", "check", () => new CheckCommand(), "Test for invalid geometry and open edges.")),
                G("Surface quality",
                    Btn("Zebra\nStripes", "zebra", () => { Session.Analysis = CadSession.AnalysisKind.None; Session.Zebra = !Session.Zebra; }, "Reflection stripes: smooth, continuous stripes mean a smooth surface.", RibbonSize.Large, null, () => Session.Zebra),
                    Cmd("Curvature\nMap", "curvature", () => new CurvatureCommand(), "Color surfaces by curvature."),
                    Cmd("Draft\nAnalysis", "draft", () => new DraftCommand(), "Check faces for mold release in a pull direction.")),
                G("Display",
                    Btn("Clear\nAnalysis", "clear-analysis", () => Session.ClearAnalysis(), "Remove section, curvature, draft, zebra and bounding box displays.", RibbonSize.Large)),
            ],
        };

        // ---- Sketch (contextual)
        _sketchTab = new RibbonTab
        {
            Title = "Sketch", ContextLabel = "SKETCH TOOLS", AccentColor = "#2BC4D1", IsVisible = false,
            Groups =
            [
                G("Select",
                    Tool("Select", "sel-object", "Select", () => new SelectTool(), "Select geometry and drag it. Points and curves move while every constraint stays satisfied.")),
                G("Draw",
                    Tool("Line", "line", "Line", () => new LineTool(SketchTool.Recall("Line")), "Draw connected lines, or lines from their midpoint (options bar). Horizontal and vertical lines are detected and constrained automatically."),
                    Tool("Rectangle", "rect", "Rectangle", () => new RectTool((RectMode)SketchTool.Recall("Rectangle")), "Draw rectangles. Choose 2-point, center or rotated 3-point in the options bar."),
                    Tool("Ellipse", "ellipse", "Ellipse", () => new EllipseTool((EllipseMode)SketchTool.Recall("Ellipse")), "Draw an ellipse. Choose center, 2-point (a box it fits in) or 3-point (long axis, then the other radius) in the options bar. Hold Shift (or press L) for a circle."),
                    Tool("Arc", "arc", "Arc", () => new ArcTool((ArcMode)SketchTool.Recall("Arc")), "Draw arcs. Choose 3-point, center or tangent in the options bar."),
                    Tool("Polygon", "polygon", "Polygon", () => new PolygonTool(SketchTool.Recall("Polygon") == 1), "Draw regular polygons. Choose inscribed or circumscribed and the number of sides in the options bar."),
                    Tool("Slot", "slot", "Slot", () => new SlotTool(), "A slot: two arc centers, then the width."),
                    Tool("Text", "text", "Text", () => new SketchTextTool(), "Type words, pick a font and size, then click to place them as closed outlines you can extrude or emboss."),
                    Tool("Spline", "spline", "Spline", () => new SplineTool(SketchTool.Recall("Spline") == 1), "Draw splines through fit points or shaped by control points.")),
                G("References"),
                G("Modify",
                    Tool("Trim", "trim", "Trim", () => new TrimTool(), "Cut a line, arc or circle back to the curves that cross it. Click the part to remove."),
                    Tool("Extend", "extend", "Extend", () => new ExtendTool(), "Lengthen a line or arc until it meets the next curve. Click near the end you want to extend."),
                    Tool("Offset", "offset", "Offset", () => new OffsetTool(), "Copy a curve (or a whole connected chain) a set distance to one side. Select curves (Shift-click adds), press Enter to finish selecting, then move to a side and click, or type the distance and press Enter."),
                    Tool("Fillet", "fillet", "Fillet", () => new FilletTool(false), "Round the corner where two lines meet. Type the radius, then click the two lines."),
                    Tool("Chamfer", "chamfer", "Chamfer", () => new FilletTool(true), "Cut the corner where two lines meet with a straight edge. Type the distance, then click the two lines."),
                    Tool("Mirror", "mirror", "Mirror", () => new SketchMirrorTool(SketchTool.Recall("Mirror")), "Mirror curves across a line. Pick the curves, press Enter, click the line. Live mode keeps both sides symmetric."),
                    Tool("Pattern", "pattern-linear", "Pattern", () => SketchPatternTool.Make(SketchTool.Recall("Pattern")), "Repeat sketch curves along a direction or around a center. Choose linear or circular in the options bar.")),
                G("Reference",
                    Tool("Project /\nInclude", "project", "Project", () => new ProjectTool(SketchTool.Recall("Project")), "Project edges or bodies, or intersect sketch curves with the active plane to create loft rail anchors. Choose As construction for references that stay out of profiles."),
                    Btn("To construction", "construction", () => Session.Sketch?.ToggleConstruction(Session), "Turn the selected curves into reference (construction) geometry, or back into normal curves.", small, () => IsSketching),
                    Btn("Construction", "construction", () => Session.Sketch?.ToggleConstructionMode(Session), "Construction mode (X): while it is pressed, new curves are reference geometry. Click again to go back to normal drawing.", small, () => IsSketching, () => Session.Sketch?.Construction == true)),
                G("Constrain",
                    Constrain("Horizontal", "c-horizontal", CType.Horizontal, "Make a line horizontal, or line two points up horizontally."),
                    Constrain("Vertical", "c-vertical", CType.Vertical, "Make a line vertical, or line two points up vertically."),
                    Constrain("Coincident", "c-coincident", CType.Coincident, "Join two points, or put a point on a curve."),
                    Constrain("Parallel", "c-parallel", CType.Parallel, "Make two lines parallel."),
                    Constrain("Perpendicular", "c-perp", CType.Perpendicular, "Make two lines meet at 90°."),
                    Constrain("Tangent", "c-tangent", CType.Tangent, "Make a line and circle/arc (or two circles) touch smoothly."),
                    Constrain("Equal", "c-equal", CType.Equal, "Make two lines the same length, or two circles/arcs the same radius."),
                    Constrain("Midpoint", "c-midpoint", CType.Midpoint, "Put a point at the middle of a line."),
                    Constrain("Concentric", "c-concentric", CType.Concentric, "Give two circles/arcs the same center."),
                    Constrain("Collinear", "c-collinear", CType.Collinear, "Put two lines on the same infinite line."),
                    Constrain("Symmetric", "c-symmetric", CType.Symmetric, "Mirror two points across a line."),
                    Constrain("Fix", "c-fix", CType.Fixed, "Lock a point or curve in place.")),
                G("Dimension",
                    Tool("Dimension", "dimension", "Dimension", () => new DimensionTool((DimMode)SketchTool.Recall("Dimension")), "Drive sizes with numbers: lengths, diameters, radii, distances, angles. Options: smart, horizontal, vertical.")),
                G("Display",
                    Btn("Curvature", "curvature", () => { SketchContext.ShowCurvature = !SketchContext.ShowCurvature; Session.RaiseState(); Session.Invalidate(); }, "Curvature comb: hairs along splines, ellipses and arcs that show how sharply they bend. An even comb means a fair curve.", small, () => IsSketching, () => SketchContext.ShowCurvature)),
                G("Exit",
                    Btn("Finish\nSketch", "finish", () => Session.ExitSketch(true), "Keep the sketch and return to modeling.", RibbonSize.Large, () => IsSketching, shortcut: "Enter"),
                    Btn("Cancel", "cancel", () => Session.ExitSketch(false), "Discard the sketch.", RibbonSize.Large, () => IsSketching)),
            ],
        };
        // ---- Simulate
        RibbonButton SimCmd(string label, string icon, Func<Command> make, string desc, RibbonSize size = RibbonSize.Large)
        {
            CommandCatalog.Register(make);
            var button = Btn(label, icon, () => Session.StartCommand(make), desc, size, () => !IsSketching && Session.ActiveStudy != null);
            button.ExampleResolver = () => Help.ToolHelp.ForCommand(make())?.Icon;
            return button;
        }
        var simulate = new RibbonTab
        {
            Title = "Static",
            Groups =
            [
                SelectGrp(),
                G("Study",
                    Btn("New\nStudy", "sim-study", NewStudy, "Start a static stress study on the selected solid body.", RibbonSize.Large, () => !IsSketching),
                    Btn("Materials", "materials", OpenMaterials, "Pick the material: stiffness and strength come from the library.", RibbonSize.Large, () => !IsSketching)),
                G("Supports",
                    SimCmd("Fixed\nSupport", "sim-fixed", () => new FixtureCommand(FixtureKind.Fixed), "Faces that cannot move at all."),
                    SimCmd("Roller /\nSymmetry", "sim-roller", () => new FixtureCommand(FixtureKind.Slider), "Faces that slide in their plane but cannot move through it."),
                    SimCmd("Fixed\nDirections", "sim-directional", () => new FixtureCommand(FixtureKind.Directional), "Lock only chosen global directions on faces.")),
                G("Loads",
                    SimCmd("Force", "sim-force", () => new LoadCommand(LoadKind.Force), "A force spread over faces."),
                    SimCmd("Pressure", "sim-pressure", () => new LoadCommand(LoadKind.Pressure), "Pressure acting on faces."),
                    SimCmd("Torque", "sim-torque", () => new LoadCommand(LoadKind.Torque), "A twisting moment on a flat face."),
                    SimCmd("Gravity", "sim-gravity", () => new LoadCommand(LoadKind.Gravity), "The part's own weight.")),
                G("Solve",
                    Btn("Create\nMesh", "sim-mesh", MeshStudy, "Generate the tetrahedral mesh and look at it.", RibbonSize.Large, () => Session.ActiveStudy != null && !SimBusy),
                    Btn("Run\nStudy", "sim-run", RunStudy, "Mesh (if needed) and solve.", RibbonSize.Large, () => Session.ActiveStudy != null && !SimBusy)),
                G("Generative",
                    SimCmd("Generative\nDesign", "generative", () => new GenerativeCommand(), "Let the software find the lightest stiff shape for the supports and loads you set: it removes the material that carries the least. The result is a new mesh body.")),
                G("Results",
                    Btn("von Mises\nStress", "sim-stress", () => SetResultKind(ResultKind.VonMises), "Color by equivalent stress.", RibbonSize.Large, () => HasResult),
                    Btn("Displace-\nment", "sim-displacement", () => SetResultKind(ResultKind.Displacement), "Color by how far each point moves.", RibbonSize.Large, () => HasResult),
                    Btn("Factor of\nSafety", "sim-safety", () => SetResultKind(ResultKind.SafetyFactor), "Yield strength divided by stress: below 1 means yielding.", RibbonSize.Large, () => HasResult),
                    Btn("Probe", "sim-probe", () => Session.StartCommand(new ProbeCommand()), "Click the result to read values.", RibbonSize.Large, () => HasResult),
                    Btn("Clear\nResults", "clear-analysis", ClearResults, "Show the normal model again.", RibbonSize.Large, () => Session.Result != null)),
            ],
        };

        // ---- Mesh (3D printing and reverse engineering)
        var mesh = new RibbonTab
        {
            Title = "Mesh",
            Groups =
            [
                G("Get a mesh",
                    Btn("Import\nMesh", "mesh-import", ImportMesh, "Bring in a scan or print file: STL, OBJ, PLY or 3MF.", RibbonSize.Large, () => !IsSketching),
                    Cmd("Solid to\nMesh", "mesh-from-solid", () => new SolidToMeshCommand(), "Turn CAD bodies into triangle meshes.")),
                G("Repair",
                    Cmd("Repair", "mesh-repair", () => new MeshRepairCommand(), "Merge vertices, remove junk, fix normals and fill holes."),
                    Cmd("Fill\nHoles", "mesh-holes", () => new MeshRepairCommand(true), "Close the open holes in a mesh."),
                    Cmd("Print\nCheck", "mesh-check", () => new MeshCheckCommand(), "Pre-flight check: watertight, fits your printer, supports, weight.")),
                G("Edit",
                    Cmd("Reduce", "mesh-reduce", () => new MeshDecimateCommand(), "Simplify a dense mesh while keeping its shape."),
                    Cmd("Smooth", "mesh-smooth", () => new MeshSmoothCommand(), "Remove scanner noise without shrinking the part."),
                    Cmd("Offset", "mesh-offset", () => new MeshOffsetCommand(), "Grow or shrink the surface (tolerances, clearances)."),
                    Cmd("Split", "mesh-cut", () => new MeshCutCommand(), "Cut with a plane and cap the halves.")),
                G("Select & trace",
                    Cmd("Select\nRegion", "mesh-select", () => new MeshRegionCommand(), "Pick triangles on a scan: smart patch, brush, rectangle or lasso."),
                    Cmd("Fit Surface\nto Region", "mesh-patch", () => new MeshFitSurfaceCommand(), "Turn the selected region into a plane, cylinder, sphere, cone or smooth NURBS patch."),
                    Cmd("Freeform from\nScan", "form-scan", () => new FormFromScanCommand(), "Wrap an editable quad cage around the scan.")),
                G("Print analysis",
                    Cmd("Wall\nThickness", "mesh-thickness", () => new MeshThicknessCommand(), "Color the part by wall thickness."),
                    Cmd("Overhang &\nOrientation", "mesh-overhang", () => new MeshOverhangCommand(), "Find what needs supports and the best way to lay the part down.")),
                G("Reverse engineering",
                    Cmd("Segment\n& Fit", "mesh-fit", () => new MeshSegmentCommand(), "Find planes, cylinders, spheres and cones in a scan and create CAD geometry from them."),
                    Cmd("Section\nCurves", "mesh-section", () => new MeshSectionCommand(), "Slice the mesh into curves to rebuild with extrude, loft or sweep."),
                    Cmd("Align", "mesh-align", () => new MeshAlignCommand(), "Drop on the bed, lay a face flat, or best-fit onto a CAD body."),
                    Cmd("Deviation", "mesh-deviation", () => new MeshDeviationCommand(), "Compare a scan with its CAD model and show the error as colors."),
                    Cmd("Mesh to\nSolid", "mesh-solid", () => new MeshToSolidCommand(), "Convert a mesh into a CAD body (one face per triangle).")),
                TransformGrp(),
                G("Display",
                    Btn("Clear\nColors", "clear-analysis", () => Session.ClearAnalysis(), "Remove analysis colors from meshes.", RibbonSize.Large)),
                G("Inspect",
                    Cmd("Measure", "measure", () => new MeasureCommand(), "Measure sizes and distances on meshes and bodies."),
                    Cmd("Section\nAnalysis", "section", () => new SectionCommand(), "Cut the model with a plane to see inside.")),
            ],
        };

        // ---- Materials (contextual; opened from Home > Materials)
        _materialsTab = new RibbonTab
        {
            Title = "Materials", ContextLabel = "MATERIALS", AccentColor = "#E8A33D", IsVisible = false,
            Groups =
            [
                G("Apply",
                    Btn("Apply\nMaterial", "material-apply", () => ApplyMaterial(), "Give the highlighted material to the selected bodies: sets color, finish and density for mass.", RibbonSize.Large, () => HasMaterial),
                    Btn("Remove\nMaterial", "material-clear", ClearMaterial, "Take the material off the selected bodies (their color stays).", RibbonSize.Large)),
                G("Finish",
                    Btn("Matte", "finish-matte", () => SetFinish(0, 0.08), "Flat, non-reflective surface.", small),
                    Btn("Satin", "finish-satin", () => SetFinish(0, 0.4), "Soft sheen, like painted or molded plastic.", small),
                    Btn("Glossy", "finish-glossy", () => SetFinish(0, 0.9), "Hard, shiny highlights, like polished plastic or glass.", small),
                    Btn("Brushed metal", "finish-brushed", () => SetFinish(1, 0.35, SurfacePattern.Brushed), "Metal with a soft, broad reflection.", small),
                    Btn("Polished metal", "finish-polished", () => SetFinish(1, 0.9, SurfacePattern.Plain), "Mirror-like metal.", small)),
                G("Library",
                    Btn("Find\nAssigned", "material-find", ShowAssignedMaterial, "Jump the list to the material of the selected body.", RibbonSize.Large),
                    Btn("Compare", "material-compare", ToggleCompare, "Add the highlighted material to the side-by-side comparison (up to three).", RibbonSize.Large, () => HasMaterial),
                    Btn("Clear\nCompare", "clear-analysis", ClearCompare, "Empty the comparison table.", RibbonSize.Large)),
                G("Close",
                    Btn("Done", "finish", CloseMaterials, "Close the Materials tab and return to modeling.", RibbonSize.Large)),
            ],
        };
        // ---- View
        var view = new RibbonTab
        {
            Title = "View",
            Groups =
            [
                G("Orientation",
                    View("Front", "view-front", 0, 0), View("Back", "view-back", 180, 0), View("Left", "view-left", -90, 0),
                    View("Right", "view-right", 90, 0), View("Top", "view-top", 0, 90), View("Bottom", "view-bottom", 0, -90),
                    Btn("Isometric", "view-iso", () => { Session.Cameras[0].SetView(-35, 28); Session.Invalidate(); }, "A three-quarter view.", RibbonSize.Large),
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Look straight at the selected plane, face or sketch.", RibbonSize.Large, null, null, "N")),
                G("Navigate",
                    Btn("Zoom\nExtents", "zoom-extents", () => Session.ZoomExtents(), "Fit everything in the window.", RibbonSize.Large, null, null, "Home"),
                    Btn("Orthographic", "ortho", () => Session.Ortho = true, "No perspective: parallel projection.", small, null, () => Session.Ortho),
                    Btn("Perspective", "perspective", () => Session.Ortho = false, "Natural perspective projection.", small, null, () => !Session.Ortho)),
                G("Layout",
                    Btn("Single", "view-single", () => Session.SetLayout(ViewLayout.Single), "One large view.", RibbonSize.Large, null, () => Session.Layout == ViewLayout.Single),
                    Btn("Two\nViews", "view-dual", () => Session.SetLayout(ViewLayout.Dual), "The model view next to a locked Top view.", RibbonSize.Large, null, () => Session.Layout == ViewLayout.Dual),
                    Btn("Four\nViews", "view-quad", () => Session.SetLayout(ViewLayout.Quad), "Front, Right, Top and a free model view at once.", RibbonSize.Large, null, () => Session.Layout == ViewLayout.Quad),
                    Btn("Link\nViews", "view-link", () => { Session.LinkViews = !Session.LinkViews; if (Session.LinkViews) Session.SyncViews(Session.Cameras[0]); RefreshRibbon(); }, "Panning or zooming one view moves them all.", RibbonSize.Large, null, () => Session.LinkViews)),
                G("Inspect",
                    Cmd("Section\nView", "section", () => new SectionCommand(), "Cut the model with a plane to look inside. Works in every view."),
                    Btn("Clear\nSection", "clear-analysis", () => { Session.Clip = null; Session.Invalidate(); }, "Remove the section cut.", RibbonSize.Large, () => Session.Clip != null)),
                G("Display",
                    Disp("Shaded", "shaded", DisplayMode.Shaded, "Surfaces only."),
                    Disp("Shaded + edges", "shaded-edges", DisplayMode.ShadedEdges, "Surfaces with edge lines."),
                    Disp("Wireframe", "wireframe", DisplayMode.Wireframe, "Edges only."),
                    Btn("X-ray", "xray", () => Session.XRay = !Session.XRay, "Toggle translucent faces in shaded styles to see geometry behind them. Wireframe already shows all edges without faces.", small, null, () => Session.XRay)),
                G("Show",
                    Btn("Hide", "visibility-off", () => HideSelected(), "Hide the selected objects. Show All brings them back.", small, () => Session.SelectedObjects.Any()),
                    Btn("Isolate", "isolate", () => IsolateSelected(), "Hide everything except the selected objects.", small, () => Session.SelectedObjects.Any()),
                    Btn("Show All", "visibility-on", () => ShowAll(), "Show every hidden object.", small, () => Session.Objects.Any(o => !o.Visible)),
                    Btn("Grid", "grid", () => Session.ShowGrid = !Session.ShowGrid, "Show the construction grid.", small, null, () => Session.ShowGrid),
                    Btn("Zebra stripes", "zebra", () => Session.Zebra = !Session.Zebra, "Reflection stripes for surface quality.", small, null, () => Session.Zebra)),
            ],
        };

        // ---- Assemble / Joints / Animate
        RibbonButton AsmCmd(string label, string icon, Func<Command> make, string desc, RibbonSize size = RibbonSize.Large, Func<bool>? enabled = null)
        {
            CommandCatalog.Register(make);
            return Btn(label, icon, () => Session.StartCommand(make), desc, size, () => !IsSketching && (enabled?.Invoke() ?? true));
        }
        bool HasComponents() => Session.Components.Any();
        bool HasJoints() => Session.JointObjects.Any();
        var assemble = new RibbonTab
        {
            Title = "Assemble",
            Groups =
            [
                SelectGrp(),
                G("Components",
                    Btn("Parts\nLibrary", "component", OpenLibrary, "Browse a folder of STEP or Prism parts (and built-in fasteners, washers, bearings and more) as pictures. Double-click a part to insert it.", RibbonSize.Large, () => !IsSketching),
                    AsmCmd("Insert\nComponent", "component", () => new InsertComponentCommand(), "Place a part from another open document or a part file. Model each part on its own first (bottom-up)."),
                    AsmCmd("Move\nComponent", "move", () => new MoveComponentCommand(), "Position a free component: move it or turn it about its centre.", RibbonSize.Large, HasComponents),
                    AsmCmd("Create\nPart", "component", () => new CreatePartCommand(), "Start a new part inside this assembly. It opens in the Design workspace; press Back to assembly when it is done.", RibbonSize.Large),
                    Btn("Edit\nPart", "component", () => EditComponentPart(), "Open the selected component's part for editing in the Design workspace. Back to assembly brings the changes in.", RibbonSize.Large, () => !IsSketching && SelectedComponent != null),
                    AsmCmd("Mate", "joint-rigid", () => new MateCommand(), "Snap a free component onto another by two faces: touching, flush or a set distance apart.", RibbonSize.Large, HasComponents),
                    AsmCmd("Component\nPattern", "pattern-linear", () => new ComponentPatternCommand(), "Repeat a component in a line or around an axis.", RibbonSize.Large, HasComponents),
                    AsmCmd("Parts\nList", "component", () => new PartsListCommand(), "Count every part, with material, mass and cost from each part's info. Save it as a CSV.", RibbonSize.Large, HasComponents),
                    Btn("Ground", "ground", ToggleGround, "Fix the selected component in place. Joints are solved outwards from grounded components. The first component is grounded for you.", RibbonSize.Large, () => HasComponents() && !IsSketching, () => SelectedComponent?.Component?.Grounded == true),
                    Btn("Refresh", "refresh", () => { int n = Session.RefreshComponentsFromSource(); Session.Say(n == 0 ? "Nothing to refresh: no linked part files changed." : $"Refreshed {n} component(s) from their files."); }, "Reload components from the part files they were inserted from, keeping their positions.", RibbonSize.Large, () => HasComponents() && !IsSketching)),
                G("Review",
                    AsmCmd("Exploded\nView", "explode", () => new ExplodeCommand(), "Pull the parts apart to see how they fit. The real positions are untouched.", RibbonSize.Large, HasComponents),
                    AsmCmd("Interference", "interference", () => new InterferenceCommand(), "Find where components overlap.", RibbonSize.Large, HasComponents),
                    AsmCmd("Properties", "properties", () => new PropertiesCommand(), "Volume, mass, center of mass and moments of inertia of the selection.", RibbonSize.Large, HasComponents),
                    AsmCmd("Measure", "measure", () => new MeasureCommand(), "Measure distances and angles between components.", RibbonSize.Large, HasComponents)),
            ],
        };
        var joints = new RibbonTab
        {
            Title = "Joints",
            Groups =
            [
                SelectGrp(),
                G("Joint",
                    AsmCmd("Rigid", "joint-rigid", () => new JointCommand(JointType.Rigid), "Lock one component to another: no motion.", RibbonSize.Large, HasComponents),
                    AsmCmd("Revolute", "joint-revolute", () => new JointCommand(JointType.Revolute), "A hinge or pin: the second component turns about the axis.", RibbonSize.Large, HasComponents),
                    AsmCmd("Slider", "joint-slider", () => new JointCommand(JointType.Slider), "The second component slides along one direction.", RibbonSize.Large, HasComponents),
                    AsmCmd("Cylindrical", "joint-cyl", () => new JointCommand(JointType.Cylindrical), "Turns about an axis and slides along it (a bolt in a hole).", RibbonSize.Large, HasComponents),
                    AsmCmd("Ball", "joint-ball", () => new JointCommand(JointType.Ball), "Pivots about a point: spin and tilt (a ball and socket).", RibbonSize.Large, HasComponents),
                    AsmCmd("Planar", "joint-planar", () => new JointCommand(JointType.Planar), "Slides over a flat face in two directions.", RibbonSize.Large, HasComponents)),
                G("Motion",
                    AsmCmd("Drive\nJoints", "joint-revolute", () => new DriveJointsCommand(), "Move joints and watch the assembly follow, within their limits.", RibbonSize.Large, HasJoints),
                    AsmCmd("Interference", "interference", () => new InterferenceCommand(), "Check the current pose for overlapping parts.", RibbonSize.Large, HasComponents)),
            ],
        };
        var animate = new RibbonTab
        {
            Title = "Animate",
            Groups =
            [
                G("Animation",
                    Btn("New\nAnimation", "animation", () => { Session.AddAnimation(new AnimInfo(5, [], false), true); Session.Say("New animation created. Open the editor to key joint values."); }, "Create an empty animation (5 seconds).", RibbonSize.Large, () => !IsSketching),
                    AsmCmd("Edit /\nPlay", "animation", () => new AnimationCommand(), "Open the animation timeline: scrub, key joint values or the exploded view, and play the motion.", RibbonSize.Large),
                    AsmCmd("Exploded\nView", "explode", () => new ExplodeCommand(), "Pull the parts apart.", RibbonSize.Large, HasComponents)),
                G("Output",
                    Cmd("Render\nAnimation", "render", () => new RenderAnimationCommand(), "Turn the animation into pictures, a GIF and (with ffmpeg) an MP4, optionally with the camera circling the model.")),
            ],
        };

        var render = new RibbonTab
        {
            Title = "Render",
            Groups =
            [
                G("Render",
                    Cmd("Render\nImage", "render", () => new RenderCommand(), "Make a realistic picture of what is on screen: soft shadows, reflections, studio or sky light. Built in, nothing to install."),
                    Cmd("Render\nAnimation", "animation", () => new RenderAnimationCommand(), "Render an animation (joint motion, exploded view) and/or a camera turntable as pictures, a GIF and, with ffmpeg installed, an MP4."),
                    Btn("Save\nPicture", "save", () => { Session.SaveRender(); }, "Save the last render as a PNG in Documents\\Prism\\Renders.", RibbonSize.Large, () => Session.Render != null),
                    Btn("Close\nPicture", "cancel", () => Session.CloseRender(), "Hide the rendered picture and return to the model.", RibbonSize.Large, () => Session.Render != null)),
                G("Lighting",
                    Cmd("Sun", "light-sun", () => new LightsCommand(LightKind.Sun), "Drop in a global sun: direction, illuminance in lux and colour temperature. Drag it in the viewport."),
                    Cmd("Lamp", "light-lamp", () => new LightsCommand(LightKind.Point), "Drop in a point lamp in lumens with real inverse-square falloff and soft shadows. Drag it anywhere."),
                    Cmd("Spot", "light-spot", () => new LightsCommand(LightKind.Spot), "Drop in a spotlight: lumens, cone angle and soft edge. Drag the lamp and its aim point."),
                    Cmd("Edit\nLights", "light-edit", () => new LightsCommand(), "Select, drag, tune or delete the lights in this document."),
                    Btn("Show\nLights", "light-lamp", () => { Session.ShowLights = !Session.ShowLights; Session.Invalidate(); }, "Show or hide the light markers in the viewport.", RibbonSize.Large, null, () => Session.ShowLights)),
                G("Look",
                    Btn("Materials", "materials", () => OpenMaterials(), "Colours and finishes: metals and glossy plastics reflect in the render.", RibbonSize.Large),
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Look straight at the selected plane, face or sketch (N).", RibbonSize.Large, null, null, "N")),
            ],
        };
        var cam = new RibbonTab
        {
            Title = "Subtractive",
            Groups =
            [
                G("Setup", Cmd("CAM setup", "cnc", () => new CamSetupCommand(), "Stock, G54 work zero and shared tool settings."),
                    Cmd("Select setup", "cnc", () => new SelectCamSetupCommand(), "Switch named stock and work-zero setups."),
                    Cmd("Tool library", "cnc", () => new CamToolLibraryCommand(), "Save named tools with material-specific cutting feeds and speeds."),
                    Cmd("Design check", "check", () => new CncDesignCheckCommand(), "Screen top-down access, reach and thin walls on CAD bodies or meshes.")),
                G("CNC", ToolMenu("Toolpaths", "cnc-profile", "Create a milling or drilling operation.",
                    Cmd("Profile", "cnc-profile", () => new ProfileCommand(), "Profile milling with depth passes and holding tabs."),
                    Cmd("Pocket", "cnc-pocket", () => new PocketCommand(), "Pocket milling with islands."),
                    Cmd("Adaptive", "cnc-pocket", () => new AdaptiveClearCommand(), "Clear a pocket with even cutter load: deeper and faster, no slotting."),
                    Cmd("Turning", "cnc", () => new TurnCommand(), "Lathe: face, rough and finish a round part from a half-section sketch."),
                    Cmd("Drill", "cnc-drill", () => new DrillCommand(), "Drilling and peck cycles."),
                    Cmd("Face", "cnc-face", () => new FaceMillCommand(), "Face the stock with zigzag passes."),
                    Cmd("3D Rough", "cnc", () => new Rough3DCommand(), "Layered raster roughing of setup stock, leaving an allowance around the model."),
                    Cmd("3D\nFinish", "cnc", () => new ParallelFinishCommand(), "Finish a 3D shape with parallel passes that follow its surface (ball nose or flat end mill).")),
                    Cmd("Post\nG-code", "gcode", () => new CncPostCommand(), "Post existing CNC toolpaths for GRBL, Mach3 or LinuxCNC.")),
                G("Review", Cmd("Playback", "animation", () => new CamPlaybackCommand(), "Animate a cutter or nozzle along a path."),
                    Cmd("Operation order", "toolpath", () => new CamProgramOrderCommand(), "Set the machining sequence used by posting and stock review."),
                    Cmd("Stock review", "cnc", () => new CncStockCommand(), "Sample material removal, remaining stock, gouges, rapid and holder contacts."),
                    Cmd("Setup sheet", "check", () => new CamReviewCommand(), "Review operation sources and export a setup sheet."),
                    Cmd("Pass view", "toolpath", () => new ToolpathViewCommand(), "Inspect depth passes.")),
            ],
        };
        surface.Groups.Insert(3, G("Continuity",
            Cmd("Match\nSurface", "nurbs-patch", () => new MatchSurfaceCommand(), "Match position, tangency or curvature to a fixed patch boundary."),
            Cmd("Check\nSeam", "curvature", () => new SurfaceContinuityCommand(), "Measure sampled seam gap, normal angle and cross-boundary curvature.")));
        freeform.Groups.Insert(3, G("Continuity",
            Cmd("Match\nSurface", "nurbs-patch", () => new MatchSurfaceCommand(), "Match compatible natural patch boundaries with G0, G1 or G2 constraints."),
            Cmd("Check\nSeam", "curvature", () => new SurfaceContinuityCommand(), "Inspect sampled continuity between two natural patch boundaries.")));
        form.Groups.Insert(3, G("Validate", Cmd("Check\nCage", "check", () => new CheckCageCommand(), "Check manifold topology, winding, degenerate faces and extraordinary points.")));
        var additive = new RibbonTab {Title="3D FDM",Groups=[
            G("Preparation",Cmd("FDM check","print3d",()=>new FdmCheckCommand(),"Check nozzle-based walls, overhangs, bed contact, build fit and filament weight on CAD bodies or meshes."),
                Cmd("Orient part","rotate",()=>new TransformCommand(TransformCommand.Kind.Rotate),"Rotate the model for bed contact, supports and layer strength."),
                Cmd("Split mesh","mesh",()=>new MeshCutCommand(),"Split an imported mesh to fit the build volume.")),
            G("Export",Cmd("Export for printing","print3d",()=>new PrintExportCommand(),"Write 3MF, STL, OBJ or PLY and open in your slicer."),Cmd("Check geometry","check",()=>new CheckCommand(),"Inspect model geometry.")),
            G("Nozzle review",Cmd("Import nozzle path","gcode",()=>new ImportGcodeCommand(),"Read linear G0/G1 output from an external slicer."),Cmd("Playback","animation",()=>new CamPlaybackCommand(),"Animate nozzle travel and extrusion paths."),Cmd("Layer view","toolpath",()=>new ToolpathViewCommand(),"Inspect imported print layers."))]};
        var resin = new RibbonTab {Title="3D Resin",Groups=[
            G("Resin preparation",Cmd("Resin check","print3d",()=>new ResinCheckCommand(),"Screen cavities, drainage, potential cups, islands, thin features and build fit."),
                Cmd("Hollow body","shell",()=>new ShellCommand(),"Hollow an exact CAD body with a chosen wall thickness; add drainage and recheck."),
                Cmd("Drain / vent hole","pcb-hole",()=>new HoleCommand(),"Cut a plain drainage or vent hole through a selected face or exact body.")),
            G("Orientation & inspection",Cmd("Tilt part","rotate",()=>new TransformCommand(TransformCommand.Kind.Rotate),"Tilt the model, then rerun resin checks to compare cups, islands and peak layer area."),
                Cmd("Section","section",()=>new SectionCommand(),"Inspect hollow walls and drainage paths."),Cmd("Check geometry","check",()=>new CheckCommand(),"Validate exact geometry.")),
            G("Export",Cmd("Export for resin","print3d",()=>new PrintExportCommand(true),"Export 3MF/STL to your resin slicer for supports and exposure settings."))]};
        var tooling = new RibbonTab {Title="Tooling",Groups=[G("Mold inspection",Cmd("Draft analysis","draft",()=>new DraftCommand(),"Check pull-direction draft."),Cmd("Section analysis","section",()=>new SectionCommand(),"Inspect cavities and walls."),Cmd("Check geometry","check",()=>new CheckCommand(),"Check tooling geometry."))]};
        simulate.Groups.Add(G("Study workflow",ToolMenu("Selections & setups","sim-fixed","Save and reuse boundary selections and analysis setups.",
            Cmd("Select static study","sim-new",()=>new SelectStudyCommand(),"Select a saved static study."),
            Cmd("Save face selection","sim-fixed",()=>new NamedFacesCommand(),"Name selected boundary faces."),Cmd("Recall face selection","sim-fixed",()=>new RecallFacesCommand(),"Recall a named face group."),Cmd("Saved CAE setups","sim-run",()=>new SavedCaeCommand(),"Rerun saved thermal/modal setups.")),
            Cmd("Mesh refinement","sim-mesh",()=>new ConvergenceCommand(),"Compare three meshes using displacement."),Cmd("Export results","export",()=>new ExportStudyCommand(),"Export static nodal results and metadata.")));
        var thermal = new RibbonTab {Title="Thermal",Groups=
        [G("Conduction",Cmd("Thermal\nAnalysis","cae-thermal",()=>new CaeSolveCommand(true),"Steady or transient conduction with prescribed temperatures in one homogeneous solid."),
            Btn("Materials","materials",OpenMaterials,"Assign conductivity, density and heat capacity.")),
         G("Review",Btn("Clear\nResults","clear-analysis",ClearResults,"Return to normal model shading."))]};
        var dynamics = new RibbonTab {Title="Dynamics",Groups=
        [G("Vibration",Cmd("Natural\nFrequencies","cae-modal",()=>new CaeSolveCommand(false),"Linear modal frequencies and arbitrary-amplitude mode shapes; fixed faces on one homogeneous solid."),
            Btn("Materials","materials",OpenMaterials,"Assign elastic modulus, Poisson ratio and density.")),
         G("Review",Btn("Clear\nResults","clear-analysis",ClearResults,"Return to normal model shading."))]};
        var fluids = new RibbonTab {Title="Fluids",Groups=
        [G("Pipe flow",Cmd("Pipe\nFlow","cae-fluid",()=>new PipeFlowCommand(),"Incompressible circular-pipe pressure drop and Reynolds number. This is a reduced-order calculation, not general 3D CFD."))]};
        var ecad = new RibbonTab
        {
            Title = "ECAD",
            Groups =
            [
                G("Board", ToolMenu("Create board", "pcb", "Create physical board geometry for mechanical integration.",
                    Cmd("Rectangular board", "pcb", () => new PcbBoardCommand(), "Board thickness, elevation and four optional mounting holes."),
                    Cmd("Board from outline", "pcb", () => new PcbOutlineCommand(), "Extrude a closed planar sketch, curve or face into a board.")),
                    Cmd("Mounting\nHole", "pcb-hole", () => new PcbHoleCommand(), "Cut a mechanical mounting hole along world Z.")),
                G("Components", Cmd("Component\nEnvelope", "component", () => new ComponentEnvelopeCommand(), "Place a package envelope or mechanical keepout, with top/bottom side and rotation."),
                    ToolMenu("Place & align", "move", "Position imported component models or envelope bodies.",
                        Cmd("Move", "move", () => new TransformCommand(TransformCommand.Kind.Move), "Position board or component geometry."),
                        Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Orient a component or the completed assembly."),
                        Btn("Duplicate", "duplicate", Session.Duplicate, "Copy a selected component envelope.", can: hasSel))),
                G("Mechanical checks", ToolMenu("Check fit", "interference", "Check component and enclosure geometry. These are mechanical checks, not electrical design rules.",
                    Cmd("Interference", "interference", () => new InterferenceCommand(), "Find overlapping components or enclosure bodies."),
                    Cmd("Measure", "measure", () => new MeasureCommand(), "Measure board size, connector positions and gaps."),
                    Cmd("Section analysis", "section", () => new SectionCommand(), "Inspect internal component clearances."))),
                G("Exchange", Btn("Import\nSTEP", "import", ImportStep, "Import board/component models exported by an ECAD application."),
                    Btn("Export\nSTEP", "export", () => Export("step"), "Export visible board and component geometry for mechanical exchange.")),
            ],
        };
        foreach(var tab in new[]{thermal,dynamics})tab.Groups.Add(G("Saved workflow",Cmd("Saved setups","sim-run",()=>new SavedCaeCommand(),"Run a saved thermal or modal case."),Cmd("Recall faces","sim-fixed",()=>new RecallFacesCommand(),"Recall named boundary faces.")));
        foreach (var tab in new[] { home, assemble, render, animate }) tab.Groups.Add(ShareGrp());
        foreach (var t in new[] { file, home, solid, surface, form, freeform, direct, sheet, ecad, scriptTab, cam, additive, resin, analyze, mesh, simulate, thermal, dynamics, fluids, assemble, joints, animate, render, tooling, _materialsTab, _sketchTab, view }) Tabs.Add(t);
        // Retired from the ribbon but kept in the catalog so history steps saved by older files still re-edit.
        CommandCatalog.Register(() => new EdgeSurfaceCommand());
        CommandCatalog.Register(() => new FillOpeningCommand());
        OrganizeRibbons(TransformGrp, InspectGrp, AppearanceGrp, SelectGrp);
        // Buttons that are not commands (and the shared selection and materials buttons) get their help by icon.
        foreach (var t in Tabs.Where(t => !t.IsContextual && !t.IsFile))
            foreach (var b in t.Buttons)
                if (b.ExampleResolver == null) { var owner = t.Title; var button = b; b.ExampleResolver = () => Help.ToolHelp.ForButton(owner, button.Icon); }
        FinishRibbon();
    }
}
