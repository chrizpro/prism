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
        if (newValue != null) { newValue.IsSelected = true; if (!newValue.IsFile && !newValue.IsContextual) _lastNormalTab = newValue; Session.OpeningTab = newValue.Title; }
    }

    /// <summary>After a file opens, go to the tab it was saved on, so a ready-made study, CAM plan or animation is in front of the user.</summary>
    public void ShowOpeningTab()
    {
        if (string.Equals(Path.GetExtension(Session.FilePath), NativeDocumentFile.Drawing, StringComparison.OrdinalIgnoreCase))
        {
            SetWorkspace("drawing");
            return;
        }
        var title = Session.OpeningTab;
        if (title != null && Tabs.FirstOrDefault(t => t.Title == title && !t.IsFile && !t.IsContextual) is { } tab)
        {
            EnsureTabVisible(tab);
            SelectTab(tab);
            Session.OpeningTab = title;
        }
        if (Session.OpeningCommand is { } name && Session.ActiveCommand == null && Prism.Automation.AutomationApi.CreateCommand(name) is { } command) { var keep = Session.OpeningCommand; Session.StartCommand(command); Session.OpeningCommand = keep; }
    }

    public void SelectTab(RibbonTab tab) { SelectedTab = tab; RibbonCollapsed = false; }
    public void ToggleRibbon() => RibbonCollapsed = !RibbonCollapsed;

    RibbonTab _sketchTab = null!;
    RibbonTab _directTab = null!;
    RibbonTab _lastNormalTab = null!;

    public MainViewModel()
    {
        MoldSources.Documents = () => Documents.Where(d => !ReferenceEquals(d, ActiveDocument)).Select(d => (d.Title, d.Session)).ToList();
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
        if (IsDrawingWorkspace && (s.ActiveCommand != null || s.Sketch != null)) SetWorkspace("design");
        ActiveCommand = s.ActiveCommand;
        IsSketching = s.Sketch != null;
        RebuildQuickBar();
        UpdatePanels(); RebuildSketchPanel(); RefreshToolOptions(); RebuildBrowser();
        Prompt = s.ActiveCommand?.HasPanelStep==true ? s.ActiveCommand.PanelHint : s.ActiveTool?.Prompt ?? (s.ActiveCommand != null ? s.ActiveCommand.Hint : s.Sketch != null ? "Pick a sketch tool from the ribbon." : "");
        UpdateActiveToolInstruction();
        if (IsSketching && !_sketchTab.IsVisible) { _sketchTab.IsVisible = true; _lastNormalTab = SelectedTab ?? _lastNormalTab; SelectedTab = _sketchTab; }
        else if (!IsSketching && _sketchTab.IsVisible)
        {
            _sketchTab.IsVisible = false;
            if (SelectedTab == _sketchTab) SelectedTab = _lastNormalTab;
        }
        StartDirectGizmo();
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
        if (ActiveDocument is { } tab) await SaveDocumentAsync(tab, saveAs);
    }

    public async void ImportMesh()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import mesh or point cloud", [.. Meshing.MeshIO.ReadPatterns, "*.xyz", "*.pts", "*.xyzn", "*.csv"]);
        if (path == null) return;
        try { if (await Commands.PointCloudCommands.TryBeginAsync(Session, path)) return; } catch (Exception e) { Session.Say(e.Message, true); return; }
        StartMeshImport(path);
    }

    /// <summary>Pick a scan (XYZ, PTS, CSV or point-only PLY) and open Reconstruct Surface on it.</summary>
    public async void ImportPointCloud()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import point cloud", Commands.PointCloudCommands.ImportPatterns);
        if (path == null) return;
        try { if (!await Commands.PointCloudCommands.TryBeginAsync(Session, path)) Session.Say("That PLY file has faces; use Import Mesh.", true); }
        catch (Exception e) { Session.Say(e.Message, true); }
    }

    /// <summary>Reconstruct a surface from the last imported cloud, asking for a file when there is none.</summary>
    public void ReconstructSurface()
    {
        if (Commands.PointCloudStore.Current == null) ImportPointCloud(); else Session.StartCommand(() => new Commands.ReconstructSurfaceCommand());
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
        var importSession = Session;
        using var work = importSession.Work.Begin("Import CAD");
        var progress = new Progress<string>(m => { if (!finished) { importSession.Say(m); work.Report(m); } });
        try
        {
            importSession.Say($"Importing {Path.GetFileName(path)}…");
            var prepared = await Task.Run(() => CadSession.PrepareImport(path, progress));
            finished = true;
            importSession.AddPrepared(prepared, path);
        }
        catch (Exception e) { finished = true; importSession.Say(e.Message, true); }
    }

    /// <summary>Import a part from disk for a mold. It becomes a reference part: shown here, not saved with the file, left out of drawings.</summary>
    public async Task ImportMoldPart()
    {
        if (PickOpenFile == null) return;
        var session = Session;
        try
        {
            var path = await PickOpenFile("Import the part for a mold", [.. NativePatterns, "*.step", "*.stp", "*.iges", "*.igs", "*.brep"]);
            if (path == null) return;
            if (NativeDocumentFile.IsNative(path))
            {
                var source = new CadSession();
                source.Open(path);
                session.Apply(MoldSources.CopyReferenceBodies(source, "Part from file"));
                session.ZoomExtents();
                session.Say("Imported reference bodies. Choose Create Mold to use them.");
            }
            else
            {
                var prepared = await Task.Run(() => CadSession.PrepareImport(path));
                session.AddPrepared(prepared, path, asMoldReference: true);
            }
        }
        catch (Exception e) { session.Say(e.Message, true); }
    }

    public async Task ImportManufacturingMold()
    {
        if (PickOpenFile == null) return;
        var session = Session;
        try
        {
            var path = await PickOpenFile("Import mold solids", ["*" + NativeDocumentFile.Mold]);
            if (path != null) session.ImportMold(path);
        }
        catch (Exception e) { session.Say(e.Message, true); }
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
        var session = Session;
        string name = kind == "prism"
            ? System.IO.Path.GetFileNameWithoutExtension(session.FilePath ?? NextPartName()) + " copy.prism"
            : "Export." + kind;
        var path = await PickSaveFile($"Export {kind.ToUpperInvariant()}", name, kind);
        if (path == null) return;
        try
        {
            switch (kind)
            {
                case "prism": session.ExportPrismCopy(path); break;
                case "step": session.ExportStep(path); break;
                case "iges": session.ExportIges(path); break;
                case "brep": session.ExportBrep(path); break;
                case "glb": session.ExportGlb(path); break;
                case "dxf": session.ExportDxf(path); break;
                default: session.ExportMesh(path); break;
            }
        }
        catch (Exception e) { session.Say(e.Message, true); }
    }

    RibbonButton ExportAsMenu()
    {
        bool HasExactGeometry() => Session.ExactExportObjects.Any();
        bool HasMeshGeometry(bool excludeAnnotations = false) => Session.MeshExportObjects.Any(o =>
            (!excludeAnnotations || !o.IsAnnotation) &&
            (o.Tri is { TriCount: > 0 } || o.Tri == null && (o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form) && o.Mesh.TriangleCount > 0));
        RibbonButton Format(string label, string extension, string icon, string description, Func<bool> available)
        {
            var button = Btn(label, icon, () => Export(extension), description, can: available, id: "export-as:" + extension);
            button.ExampleIcon = "export-as|" + extension;      // each file type has its own hover help
            return button;
        }
        const string exact = "Requires visible exact geometry, such as a solid or surface.";
        const string mesh = "Requires a mesh, solid or surface in the selection, or visible in the document when nothing is selected.";
        return new RibbonButton
        {
            Label = "Export As", Icon = "step", Id = "menu:export-as",
            Description = "Write a copy in a supported file format.",
            Menu =
            [
                Format("Prism document (.prism)", "prism", "saveas", "Write a general editable Prism copy. Finish any open edit and close the exploded view first.", () => Session.CanExportPrismCopy),
                Format("STEP (.step)", "step", "step", exact, HasExactGeometry),
                Format("IGES (.iges)", "iges", "step", exact, HasExactGeometry),
                Format("BREP (.brep)", "brep", "step", exact, HasExactGeometry),
                Format("Sketch DXF (.dxf)", "dxf", "step", "Requires a selected or visible sketch.", () => Session.SketchExportObjects.Any()),
                Format("glTF (.glb)", "glb", "stl", mesh, () => HasMeshGeometry(excludeAnnotations: true)),
                Format("3MF (.3mf)", "3mf", "stl", mesh, () => HasMeshGeometry()),
                Format("STL (.stl)", "stl", "stl", mesh, () => HasMeshGeometry()),
                Format("OBJ (.obj)", "obj", "stl", mesh, () => HasMeshGeometry()),
                Format("PLY (.ply)", "ply", "stl", mesh, () => HasMeshGeometry()),
            ],
        };
    }

    void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Session.Say(e.Message, true); }
    }

    // ------------------------------------------------------------ ribbon
    RibbonButton Btn(string label, string icon, Action exec, string desc = "", RibbonSize size = RibbonSize.Large,
        Func<bool>? can = null, Func<bool>? chk = null, string shortcut = "", string id = "") => new()
    {
        Label = label, Icon = icon, Execute = exec, Description = desc, Size = size, CanExecute = can, GetChecked = chk,
        IsToggle = chk != null, Shortcut = shortcut, Id = id,
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
        (Btn(label, icon, () => Session.Sketch?.SetTool(Session, make()), desc, size, () => IsSketching, () => Session.Sketch?.Tool?.Name == family));

    /// <summary>A sketch selection-method menu item: switches the active select/edit tool's region method, or arms Select with it.</summary>
    /// <summary>In a sketch the Select group's drag methods (rectangle, lasso, paint) set the sketch Select or Edit tool's region method, or arm Select with it.</summary>
    void SetSketchSelectMethod(int option)
    {
        if (Session.Sketch?.Tool is { } tool && tool is SelectTool or SketchGeometryEditTool { SplinePoints: false }) tool.SetOption(Session, option);
        else { SketchTool.Remembered["Select"] = option; Session.Sketch?.SetTool(Session, new SelectTool()); }
    }
    bool IsSketchSelectMethod(int option) =>
        Session.Sketch?.Tool is { } t && t.OptionIndex == option && t is SelectTool or SketchGeometryEditTool { SplinePoints: false };

    /// <summary>A dropdown of tool variants (e.g. Circle: center / 2-point / 3-point).</summary>
    RibbonButton Drop(string label, string icon, string family, string desc, params RibbonButton[] variants) => (new()
    {
        Label = label, Icon = icon, Description = desc, Size = RibbonSize.Large, Menu = [.. variants],
        CanExecute = () => IsSketching, GetChecked = () => Session.Sketch?.Tool?.Name == family, IsToggle = true,
    });

    RibbonButton Variant(string label, string icon, Func<SketchTool> make, string desc) =>
        (Btn(label, icon, () => Session.Sketch?.SetTool(Session, make()), desc, RibbonSize.Large, () => IsSketching));

    RibbonButton Constrain(string label, string icon, CType type, string desc) =>
        (Btn(label, icon, () => Session.Sketch?.SetTool(Session, new ConstraintTool(type)), desc, RibbonSize.Small, () => IsSketching,
            () => Session.Sketch?.Tool is ConstraintTool ct && ct.Mode == type.ToString()));

    /// <summary>A pick-level button. The Select group is the same in a sketch, where Object arms the sketch Select tool and the body levels are unavailable.</summary>
    RibbonButton Sel(string label, string icon, SelMode m, string desc) =>
        Btn(label, icon, () => { if (!IsSketching) SetSelectionMode(m); else SetSketchPickLevel(m == SelMode.Object ? SketchPick.Object : SketchPick.Edge); }, desc, RibbonSize.Small,
            () => IsSketching ? m != SelMode.Face : Session.ActiveCommand?.HasPanelStep!=true,
            () => IsSketching ? m != SelMode.Face && Session.Sketch is { Tool: SelectTool } sk && sk.PickLevel == (m == SelMode.Object ? SketchPick.Object : SketchPick.Edge) : IsSelectionMode(m));

    /// <summary>In a sketch the Select group chooses what a click, box, lasso or paint takes: a whole shape, one curve, or points.</summary>
    public void SetSketchPickLevel(SketchPick level)
    {
        if (Session.Sketch is not { } sk) return;
        sk.PickLevel = level;
        sk.SetTool(Session, new SelectTool());
        Session.Say(level switch { SketchPick.Object => "Select whole shapes: a rectangle is taken as its four sides.", SketchPick.Edge => "Select single curves. Shift-click adds more.", _ => "Select points." });
        RefreshRibbon();
    }

    public void SetSelectionMode(SelMode mode)
    {
        if(Session.ActiveCommand?.HasPanelStep==true)return;
        if(Session.ActiveCommand is EditFormCommand form)form.SetSelectionMode(mode switch {SelMode.Object=>FormMode.Object,SelMode.Edge=>FormMode.Edge,SelMode.Vertex=>FormMode.Vertex,_=>FormMode.Face});
        else Session.Mode=mode;
    }
    public bool IsSelectionMode(SelMode mode)=>Session.ActiveCommand is EditFormCommand form ? form.SelMode==(mode switch {SelMode.Object=>FormMode.Object,SelMode.Edge=>FormMode.Edge,_=>FormMode.Face}) : Session.Mode==mode;

    public bool IsVertexMode=>Session.ActiveCommand is EditFormCommand {SelMode:FormMode.Vertex};
    public void SelectVertices()
    {
        if(IsSketching){SetSketchPickLevel(SketchPick.Point);return;}
        if(Session.ActiveCommand?.HasPanelStep==true)return;
        if(Session.ActiveCommand==null && Session.SelectedObjects.Any(o=>o.Form!=null))Session.StartCommand(()=>new EditFormCommand());
        if(Session.ActiveCommand is EditFormCommand form)form.SetSelectionMode(FormMode.Vertex);
        else { Session.Mode=SelMode.Vertex; Session.Say("Vertex: click a corner point of a solid, surface or curve. Its position shows here; Measure takes two for an exact distance."); }
    }
    RibbonButton VertexSelection() => Btn("Vertex","sel-vertex",SelectVertices,"Pick corner points of solids, surfaces and curves. With a Freeform form selected it picks cage vertices (1 while editing Freeform); in a sketch it picks points.",RibbonSize.Small,
    ()=>IsSketching || Session.ActiveCommand is EditFormCommand {HasPanelStep:false} || Session.ActiveCommand==null,
    ()=>IsSketching ? Session.Sketch is { Tool: SelectTool } sk && sk.PickLevel == SketchPick.Point : Session.ActiveCommand is EditFormCommand {SelMode:FormMode.Vertex} || Session.ActiveCommand==null && Session.Mode==SelMode.Vertex);

    /// <summary>Show the main view from a standard direction named in the part's axes (they follow the home orientation).</summary>
    void ShowStandardView(double yawDeg, double pitchDeg)
    {
        var (yaw, pitch, roll) = Session.HomeViewAngles(yawDeg * Math.PI / 180, pitchDeg * Math.PI / 180);
        var cam = Session.Cameras[0]; cam.Yaw = yaw; cam.Pitch = pitch; cam.Roll = roll;
        Session.Invalidate();
    }

    RibbonButton View(string label, string icon, double yaw, double pitch) =>
        Btn(label, icon, () => ShowStandardView(yaw, pitch), $"Look at the model from the {label.ToLower()}.", RibbonSize.Small);

    /// <summary>One button for the part orientation: Set Home while it is original, Reset Home once it has been changed.</summary>
    RibbonButton HomeButton()
    {
        var button = new RibbonButton
        {
            Label = "Set\nHome", Icon = "home-set", Size = RibbonSize.Large,
            Execute = () => { if (Session.HomeIsOriginal) Session.SetHomeToCurrentView(); else Session.ResetHome(); },
            Description = "Make the view you see now the home view, and turn Front, Top, Right and the view cube to match it. Useful when an imported mesh or an awkwardly modeled part sits at an odd angle. Once set, this button puts the original orientation back.",
        };
        void Sync() { button.Label = Session.HomeIsOriginal ? "Set\nHome" : "Reset\nHome"; button.Icon = Session.HomeIsOriginal ? "home-set" : "home-reset"; }
        Sync();
        Session.HomeChanged += Sync;
        return button;
    }

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

    RibbonGroup DatumGroup() => G("Datum", Split("split:datum", "Open gizmo options or place datum points, axes and planes.",
        Btn("Gizmo", "gizmo", OpenGizmoFromRibbon, "Position and bind the gizmo; adjust its handles and snapping.", RibbonSize.Large, null, () => Session.FreeTool is DatumPickTool { Kind: SketchDatumKind.Gizmo }, id: "move|Gizmo"),
        Btn("Point", "sketch-point", () => StartDatumTool(SketchDatumKind.Point), "Place datum points: click a face, edge or empty space, and keep clicking to add more.", RibbonSize.Large, null, () => Session.FreeTool is DatumPickTool { Kind: SketchDatumKind.Point }),
        Btn("Axis", "origin-axis", () => StartDatumTool(SketchDatumKind.Axis), "Place a datum axis on an edge or face normal; empty space uses the work-plane normal.", RibbonSize.Small, null, () => Session.FreeTool is DatumPickTool { Kind: SketchDatumKind.Axis }),
        Btn("Plane", "origin-plane", () => StartDatumTool(SketchDatumKind.Plane), "Place a datum plane on a face, plane or the work plane.", RibbonSize.Small, null, () => Session.FreeTool is DatumPickTool { Kind: SketchDatumKind.Plane })));

    void BuildRibbon()
    {
        var small = RibbonSize.Small;
        Func<bool> hasSel = () => Session.Selection.Count > 0 && !IsSketching;

        // Groups every modeling tab needs, so nobody has to switch tabs for the everyday tools (each call builds fresh buttons).
        RibbonGroup SketchGrp() => G("Sketch",
            Cmd("Create\nSketch", "sketch", () => new CreateSketchCommand(), "Draw constrained curves on one or more planes in space. G positions the drawing gizmo; E edits curves; N looks normal."));
        RibbonGroup PlanesGrp(bool intersection = true) => G("Planes", new RibbonItem?[]
        {
            Cmd("Construction\nPlane", "origin-plane", () => new ConstructionPlaneCommand(), "Make planes: offset, angled, midway, square to a curve, or through three points. Sketch on them or use them as references."),
            intersection ? Cmd("Intersection\nCurve", "section", () => new IntersectCurvesCommand(), "The curve where two planes, faces or bodies cross each other.") : null,
            Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Turn the view to look straight at the selected plane, face or sketch.", RibbonSize.Large, null, null, "N"),
        }.OfType<RibbonItem>().ToArray());
        // Curves and curve editing are one definition, so rails, paths and guides are made the same way wherever you model.
        // Solid and Freeform take the curves that feed their own tools (paths, profiles, rails); the ones that read from surfaces stay with Surface and NURBS.
        RibbonButton LegacyCurveId(RibbonButton button, string id) { button.Id = id; return button; }
        RibbonButton CurveMenu(string id, string label, string icon, string description, params RibbonButton[] tools) => new()
        {
            Id = id, Label = label, Icon = icon, Description = description, Menu = [.. tools],
        };
        // One dropdown holds every curve tool that works outside a sketch, so the modeling ribbons stay short. Flat curves are
        // filleted, chamfered, offset, trimmed and extended with the Sketch tab's own tools while a sketch is open.
        // The menu is one level deep so search and customization can reach every tool.
        RibbonGroup CurvesGrp(bool fromSurfaces = true, bool fillet = true) => G("Curves", CurveMenu("menu:curves", "Curves", "curve-through",
            "Create and edit curves outside a sketch: draw through points or on a surface, take curves from surfaces, then blend, join, trim, extend, offset or rebuild them.",
            new RibbonButton?[]
            {
                Cmd("Curve", "curve-through", () => new CurveByPointsCommand(true), "Click points in space. The curve passes through them, or is pulled toward them as control points."),
                Cmd("Curve from\nData", "curve-equation", () => new EquationCurveCommand(false), "Make a curve from formulas of t or a table of x,y,z points typed, pasted or read from a file."),
                Cmd("Intersection\nCurve", "section", () => new IntersectCurvesCommand(), "The curve where two planes, faces, surfaces or bodies cross, or the shared 3D curve of two flat curves on different planes."),
                fromSurfaces ? Cmd("Project", "section", () => new ProjectCurveCommand(), "Drop curves onto a surface, straight or by pulling to the nearest point.") : null,
                fromSurfaces ? Cmd("Draw on\nSurface", "curve-through", () => new DrawOnSurfaceCommand(), "Click points on any surface, surface body, freeform form or NURBS surface. The curve follows the surface, over edges onto neighbouring faces. It can also split the face along the curve.") : null,
                fromSurfaces ? Cmd("Isocurves", "curve-through", () => new IsoCurveCommand(), "Pull curves of constant U or V out of a surface.") : null,
                fromSurfaces ? Cmd("Contour", "section", () => new ContourCommand(), "Slice a body into evenly spaced section curves.") : null,
                Cmd("Blend\nCurve", "curve-through", () => new BlendCurveCommand(), "Join two curves with a curve that keeps their position, direction or bend."),
                Cmd("Join", "knit", () => new JoinCurvesCommand(), "Join curves that touch end to end into one."),
                Cmd("Explode", "unstitch", () => new ExplodeCurveCommand(), "Break a curve or sketch into its segments."),
                LegacyCurveId(Cmd("Extract Edges", "curve-through", () => new DuplicateEdgeCommand(), "Copy selected edges as independent curves, joining touching edges and keeping the source body."), "curve-through|Duplicate Edge"),
                Cmd("Trim\nCurve", "trim", () => new TrimCurveCommand(), "Keep part of a curve, or cut it into pieces."),
                Cmd("Extend\nCurve", "extend-surface", () => new ExtendCurveCommand(), "Grow a curve past its end, keeping it smooth."),
                Cmd("Offset\nCurve", "offset", () => new OffsetCurveCommand(), "Copy a flat curve parallel to itself."),
                fillet ? Cmd("Fillet /\nChamfer", "fillet", () => new CurveFilletCommand(false), "Round or cut the corners of a flat curve or sketch, or the edges where the faces of a surface meet.") : null,
                Cmd("Rebuild\nCurve", "raise-degree", () => new RebuildCurveCommand(), "Redraw a curve with a chosen number of points."),
                LegacyCurveId(Cmd("Points Along\nCurve", "origin-point", () => new DivideCurveCommand(), "Place points at equal distances along a curve; keep the curve intact and report its length."), "origin-point|Divide"),
            }.OfType<RibbonButton>().ToArray()));
        RibbonButton SweepSplit(string id) => Split(id, "Build a surface by carrying a section along a path: sweep a profile, with an optional second rail, or pipe a round tube. Click the name (or right-click) for the other.",
            Cmd("Sweep", "sweep-surface", () => new SweepCommand(true), "Carry a profile along a path. Add a second rail to steer how it turns."),
            Cmd("Pipe", "sweep-surface", () => new PipeCommand(), "A round, optionally tapered tube along a path curve; set the radius, or make it a solid."));
        RibbonGroup SelectGrp() => G("Select",
            Sel("Object", "sel-object", SelMode.Object, "Pick whole bodies, surfaces and curves."),
            Followed(Split(RibbonSize.Small, "split:selection-components", "Select faces, edges or Freeform vertices. Click the name to choose a mode.",
                Sel("Face", "sel-face", SelMode.Face, "Pick individual faces."),
                Sel("Edge", "sel-edge", SelMode.Edge, "Pick individual edges."),
                VertexSelection())),
            Followed(Split(RibbonSize.Small, "split:selection-method", "Select by dragging in the view: a rectangle, a lasso or a paint stroke. A rectangle dragged left to right takes what is fully inside it; right to left (or a lasso) takes whatever it touches; paint takes whatever the stroke crosses. Shift adds, Ctrl removes. With Through on, it also picks what is hidden behind.",
                SelMethod("Rectangle", "sel-rect", SelectMethod.Rectangle, "Drag a rectangle. Left to right takes only what is completely inside; right to left takes whatever it touches. Click the button again to go back to clicking."),
                SelMethod("Lasso", "sel-lasso", SelectMethod.Lasso, "Draw around what you want to select. Whatever the loop touches is picked."),
                SelMethod("Paint", "sel-paint", SelectMethod.Paint, "Drag a stroke across the model: whatever the stroke crosses is selected. Click the button again to go back to clicking."))),
            Followed(Split(RibbonSize.Small, "split:group", "Put the selected objects in a group: one parent row in the browser that hides, shows, selects or removes them together. Click the name for Group; the arrow offers Ungroup.",
                Btn("Group", "group", () => Session.GroupSelection(), "Put the selected objects in a group: one parent row in the browser that hides, shows, selects or removes them together. Ctrl+G.", small, () => Session.CanGroup),
                Btn("Ungroup", "ungroup", () => Session.Ungroup(), "Take the selected objects out of their group, or dissolve the group whose row is selected. The objects stay. Ctrl+Shift+G.", small, () => Session.CanUngroup))),
            Btn("Invert", "sel-invert", () => Session.InvertSelection(), "Select everything that is not selected. Faces and edges invert within the bodies you picked from, so you can pick a few to leave out, invert, and have the rest. With nothing selected it selects everything.", small, () => !IsSketching),
            Followed(Split(RibbonSize.Small, "split:select-same", "Add to the selection the faces that share something with the selected faces. Click the name for Same Color; the arrow offers the others.",
                Btn("Same Color", "sel-same-color", () => Session.SelectSameFaces(CadSession.FaceMatch.SameColor), "Add every face of the same body that has the same color and finish as a selected face.", small, () => !IsSketching && Session.Selection.Any(i => i.Kind == SelKind.Face)),
                Btn("Adjacent", "sel-same-adjacent", () => Session.SelectSameFaces(CadSession.FaceMatch.Adjacent), "Add the faces that share an edge with a selected face.", small, () => !IsSketching && Session.Selection.Any(i => i.Kind == SelKind.Face)),
                Btn("Linked", "sel-same-linked", () => Session.SelectSameFaces(CadSession.FaceMatch.Linked), "Add every face of the body a selected face belongs to.", small, () => !IsSketching && Session.Selection.Any(i => i.Kind == SelKind.Face)))),
            Btn("Through", "xray", () => Session.XRay = !Session.XRay, "Select through the part: rectangle and lasso also pick faces, edges and bodies hidden behind others, on both sides. Alt-click steps through what is under the pointer, front to back. Turns X-ray display on while it is active.", small, () => !IsSketching, () => Session.XRay));
        RibbonButton SelMethod(string label, string icon, SelectMethod method, string desc) =>
            Btn(label, icon, () => { if (IsSketching) SetSketchSelectMethod((int)method - 1); else Session.Method = Session.Method == method ? SelectMethod.Click : method; }, desc, RibbonSize.Small,
                () => IsSketching || Session.ActiveCommand?.HasPanelStep != true, () => IsSketching ? IsSketchSelectMethod((int)method - 1) : Session.Method == method);
        RibbonButton MoveBtn(RibbonSize size) => Btn("Move", "move", MoveWithGizmo, "Grab anything — objects, meshes, datums — and drag it with the gizmo, or type exact values in the panel. Hold Alt while dragging to drag a copy and leave the original.", size, () => !IsSketching, () => Session.Datum is { MoveMode: true } && Session.DatumVisible);
        RibbonGroup TransformGrp() => G("Transform",
            MoveBtn(RibbonSize.Large),
            Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Rotate objects around an axis.", small),
            Cmd("Scale", "scale", () => new TransformCommand(TransformCommand.Kind.Scale), "Resize objects evenly, or stretch them along each axis.", small),
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
                G("Document",
                    Btn("Save As", "saveas", () => SaveFile(true), "Save the editable Prism document with the matching native file type."),
                    ExportAsMenu()),
                G("Sketch",
                    Cmd("Create\nSketch", "sketch", () => new CreateSketchCommand(), "Draw constrained curves on one or more planes in space. G positions the drawing gizmo; E edits curves; N looks normal.")),
                G("Planes",
                    Cmd("Construction\nPlane", "origin-plane", () => new ConstructionPlaneCommand(), "Make planes: offset (one or a whole stack), angled, midway, square to a curve, or through three points. Sketch on them or use them as references."),
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Turn the view to look straight at the selected plane, face or sketch (or the sketch you are drawing).", RibbonSize.Large, null, null, "N")),
                // Keep menus one level deep so search and customization can reach every curve tool.
                CurvesGrp(),
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
                    MoveBtn(small),
                    Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Rotate objects around an axis.", small),
                    Cmd("Scale", "scale", () => new TransformCommand(TransformCommand.Kind.Scale), "Resize objects evenly, or stretch them along each axis.", small),
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
                SelectGrp(),
            ],
        };

        // ---- Solid
        var solid = new RibbonTab
        {
            Title = "Solid",
            Groups =
            [
                SketchGrp(), PlanesGrp(false), CurvesGrp(fromSurfaces: false),
                G("Primitives", PrimitiveSplit()),
                G("Features",
                    Cmd("Extrude", "extrude", () => new ExtrudeCommand(), "Push a sketch profile or face into a solid. Join, cut or intersect with an existing body."),
                    Cmd("Revolve", "revolve", () => new RevolveCommand(), "Spin a profile around an axis."),
                    Split("split:solid-detail", "Add a rib, or raise or engrave a shape. Click the name (or right-click) for the other.",
                        Cmd("Rib", "rib", () => new RibCommand(), "Add a reinforcing rib from a planar sketch to a solid body."),
                        Cmd("Emboss", "emboss", () => new EmbossCommand(), "Raise text or a sketched shape off a face, or engrave it into the body."))),
                G("Modify",
                    Cmd("Hole", "hole", () => new HoleCommand(), "Create plain or modeled threaded holes on a body or face, with blind or through-all depth."),
                    Cmd("Fillet", "fillet", () => new FilletCommand(), "Round off selected edges."),
                    Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Bevel selected edges."),
                    Cmd("Shell", "shell", () => new ShellCommand(), "Hollow out a body, optionally opening faces."),
                    Cmd("Draft", "draft", () => new DraftFaceCommand(), "Lean walls away from a pull direction."),
                    Cmd("Split\nBody", "splitbody", () => new SplitBodyCommand(), "Cut a body into separate pieces with a plane, face or surface.")),
                G("Direct Edit",
                    Btn("Direct\nEdit", "pushpull", EnterDirectEdit, "Edit the geometry you already have, with no history needed: drag faces, holes and bosses with a gizmo. Works on imported parts too.", RibbonSize.Large, () => !IsSketching)),
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

        // Opened by the Direct Edit button on the Solid tab, like Sketch: its own tab with its own Finish button.
        _directTab = new RibbonTab
        {
            Title = "Direct Modeling", ContextLabel = "DIRECT EDIT", AccentColor = "#E8743D", IsVisible = false,
            Groups =
            [
                G("Edit",
                    Cmd("Edit\nFeature", "movehole", () => new FeatureEditCommand(), "Pick a hole, slot, pocket, boss or fillet: move, copy, resize, change the type (plain, counterbore, countersink, modeled thread) or delete it. One click picks the whole feature."),
                    Split("split:direct-replace", "Make faces meet a target. Click the icon to run the one shown; click the name (or right-click) for the others.",
                        Cmd("Replace Face", "replaceface", () => new ReplaceFaceCommand(), "Make faces lie on another plane or face."),
                        Cmd("Match Face", "replaceface", () => new MatchFaceCommand(), "Give faces the plane, cylinder radius or cone angle of a source face."),
                        Cmd("Extend / Trim Face", "pushpull", () => new ExtendTrimFaceCommand(), "Grow or shrink flat faces until they meet a target plane or face.")),
                    Split("split:direct-split", "Divide a face or a body. Click the icon to run the one shown; click the name (or right-click) for the other.",
                        Cmd("Split Face", "splitbody", () => new SplitFaceCommand(), "Divide a face along a plane, edge or sketch; the shape stays the same."),
                        Cmd("Split Body", "splitbody", () => new SplitBodyCommand(), "Cut a body into separate pieces with a plane, face or surface.")),
                    Cmd("Draft", "draft", () => new DraftFaceCommand(), "Lean walls away from a pull direction."),
                    Split("split:direct-pattern", "Change or copy repeated features. Click the icon to run the one shown; click the name (or right-click) for the other.",
                        Cmd("Edit Pattern", "pattern-linear", () => new EditPatternCommand(), "Change the spacing or count of a line, grid or circle of features, or drop one."),
                        Cmd("Mirror Feature", "mirror", () => new MirrorFeatureCommand(), "Copy holes, slots, pockets or bosses across a plane or flat face."))),
                G("Repair",
                    Cmd("Check and\nRepair", "check", () => new CheckRepairCommand(), "Check an imported body for open edges, tiny or sliver faces, duplicates and bad edges, then heal, sew or clean it up."),
                    Cmd("Fill /\nCap", "patch", () => new FillCapCommand(), "Cap an open loop of edges with a flat face or smooth patch so the body closes."),
                    Cmd("Merge\nFaces", "replaceface", () => new MergeFacesCommand(), "Join neighbouring faces on the same surface into one.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
                G("Exit", Btn("Finish\nEditing", "finish", ExitDirectEdit, "Leave Direct Edit and go back to where you were.", RibbonSize.Large)),
            ],
        };
        var direct = _directTab;

        // ---- Surface
        var surface = new RibbonTab
        {
            Title = "Surface",
            Groups =
            [
                SketchGrp(), PlanesGrp(false), CurvesGrp(fillet: false),
                G("Create",
                    Cmd("Extrude", "extrude-surface", () => new ExtrudeCommand(true), "Extrude a curve or sketch into a surface."),
                    Cmd("Revolve", "revolve-surface", () => new RevolveCommand(true), "Revolve a curve into a surface."),
                    SweepSplit("split:sweep-pipe"),
                    Cmd("Loft", "loft-surface", () => new LoftCommand(true), "Blend a surface through several curves."),
                    Cmd("Boundary\nSurface", "patch", () => new BoundarySurfaceCommand(), "Fill or span a boundary of curves, sketches and surface edges. A flat loop gives a flat surface; curves across the inside and G0/G1/G2 per edge shape the rest. Two separate surface edges are blended.")),
                G("Modify",
                    Cmd("Trim", "trim", () => new TrimSurfaceCommand(), "Cut a surface with a plane, another surface or a solid and keep the side you want."),
                    Cmd("Split\nSurface", "splitbody", () => new SplitSurfaceCommand(), "Divide a surface into faces with a plane, face, surface, solid or a sketch/curve (split along its projection). The faces stay one surface, or each piece becomes its own surface."),
                    Cmd("Fillet /\nChamfer", "fillet", () => new CurveFilletCommand(false), "Round or cut the edges where the faces of a surface meet, or the corners of a flat curve or sketch."),
                    Cmd("Sew", "knit", () => new SewCommand(), "Sew (stitch) surfaces together; closed shells become solids."),
                    Cmd("Offset", "offset", () => new OffsetSurfaceCommand(), "Create a parallel copy of a surface or face."),
                    Cmd("Thicken", "thicken", () => new ThickenCommand(), "Give a surface thickness to make a solid."),
                    Cmd("Unsew", "unstitch", () => new UnstitchCommand(), "Take a sewn shell, or just some of its faces, apart into separate surfaces."),
                    Cmd("Untrim", "untrim", () => new UntrimCommand(), "Undo a trim: restore the whole natural surface, or just fill the holes in a face."),
                    Cmd("Extend", "extend-surface", () => new ExtendSurfaceCommand(), "Grow a surface past one of its edges, continuing its shape."),
                    Cmd("Reverse\nNormal", "reverse-normal", () => new ReverseNormalCommand(), "Turn a surface around so its other side faces out."),
                    Cmd("Enclosed\nBody", "fill", () => new BoundaryFillCommand(), "Make solids from the spaces enclosed by surfaces, planes and bodies."),
                    Cmd("Flatten /\nRefold", "flatten", () => new FlattenCommand(), "Flatten a surface and keep the layout, or return to wrap curves, sheets and solids onto its source or another surface."),
                    Cmd("Warp", "warp", () => new WarpCommand(), "Bend, twist, taper or stretch bodies, surfaces, curves, forms or meshes along an axis.")),
                G("Convert", Btn("Convert to\nNURBS", "to-nurbs", () => FreeformActions.ConvertToNurbs(Session), "Convert the selection to editable NURBS geometry.", RibbonSize.Large, hasSel)),
                G("Combine",
                    Cmd("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union), "Merge bodies and surfaces into one."),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body or surface out of another."),
                    Cmd("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect), "Keep only the shared volume.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Form (subdivision modeling)
        var form = new RibbonTab
        {
            Title = "Freeform",
            Groups =
            [
                SketchGrp(), PlanesGrp(false), CurvesGrp(fromSurfaces: false),
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
                        FormAct("Bevel", "chamfer", "Bevel selection", "Bevel the selected edges: flat, or tight with support loops either side. Type and width are in the panel.", small),
                        FormAct("Delete", "form-delete", "Delete selection", "Delete the selected faces, edges or points.", small),
                        FormAct("Split face", "form-subdivide", "Split face between", "Cut a face in two between two selected corners.", small),
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
                    FormAct("Freeze", "form-freeze", "Freeze or unfreeze", "Lock the selected faces, edges or points so they cannot move (shown in blue). Press it on a frozen selection to release it, or with nothing selected to release everything."),
                    FormAct("Align to\nCurve", "form-align", "Align to the curve", "Pull the selected points (or all) onto a curve: pick it in 'Curve to align to'.")),
                G("Components",
                    FormAct("Smooth\nsurface", "form-flip", "Smooth surface", "Switch between the smoothed surface and the plain cage faces. Levels are under Smoothness in the panel.", small, () => Session.ActiveCommand is EditFormCommand {SmoothOn:true}),
                    FormAct("Weld", "form-weld", "Weld selection", "Pull the selection into one point: selected points weld together; edges and faces collapse.", small),
                    FormAct("Crease", "form-crease", "Apply crease", "Make the selected edges sharp.", small),
                    FormAct("Merge", "form-weld", "Merge selection", "Join selected faces into one, or remove selected inner edges and join the faces either side.", small),
                    FormAct("Unweld\nedges", "form-weld", "Unweld selected edges", "Split the form along the selected edges so each side can move on its own.", small),
                    FormAct("Pull", "form-flip", "Pull points", "Pull the selected points (or all) onto the smooth surface the cage produces.", small),
                    FormAct("Flip normals", "form-flip", "Flip", "Reverse which side of the form is the outside.", small)),
                G("Symmetry",
                    FormAct("Mirror\nand weld", "form-mirror", "Mirror", "Reflect the form across a plane and join the halves at the seam.")),
                G("Quality",
                    Cmd("Check\nQuality", "form-edit", () => new FormQualityCommand(), "Mark poles, triangles and n-gons, see how stretched the quads are and how the curvature varies, and color the surface by its distance to a reference.", RibbonSize.Large)),
                G("Finish",
                    Btn("Convert to\nNURBS", "form-nurbs", () => FreeformActions.ConvertToNurbs(Session), "Turn the selected form into exact NURBS surfaces / a solid for CAD operations.", RibbonSize.Large, hasSel),
                    Btn("Convert\nfewer faces", "form-nurbs", () => FreeformActions.ConvertToNurbs(Session, fewerFaces: true), "Like Convert to NURBS, but each irregular face becomes one fitted patch instead of four: a lighter solid that is quicker for booleans and fillets, a little less exact near poles.", RibbonSize.Large, hasSel)),
                TransformGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Freeform
        var freeform = new RibbonTab
        {
            Title = "NURBS",
            Groups =
            [
                G("Sketch", Btn("Create\nSketch", "sketch", StartFluidSketch, "Draw flat, then shape curves in space.", RibbonSize.Large, null, null, "K")), PlanesGrp(false), CurvesGrp(fillet: false),
                G("Surfaces",
                    Cmd("Control\nGrid", "nurbs-patch", () => new NurbsPatchCommand(), "Create a grid of control points to shape freely."),
                    Cmd("Extrude", "extrude-surface", () => new ExtrudeCommand(true), "Extrude a curve or sketch into a surface."),
                    Cmd("Revolve", "revolve-surface", () => new RevolveCommand(true), "Revolve a curve into a surface."),
                    SweepSplit("split:nurbs-sweep"),
                    Cmd("Loft", "loft-surface", () => new LoftCommand(true), "Blend a surface through curves."),
                    Cmd("Boundary\nSurface", "patch", () => new BoundarySurfaceCommand(), "Fill or span a boundary of curves, sketches and surface edges. A flat loop gives a flat surface; curves across the inside and G0/G1/G2 per edge shape the rest. Two separate surface edges are blended.")),
                G("Shape",
                    Cmd("Edit\nPoints", "edit-points", () => new EditPointsCommand(), "Drag NURBS control points, with soft influence on neighbours."),
                    Cmd("Sculpt", "sculpt", () => new SculptCommand(), "Brush the surface: pull, push, move or smooth."),
                    Cmd("Rebuild\nSurface", "raise-degree", () => new RebuildSurfaceCommand(), "Redraw a surface as one patch with a chosen grid of points."),
                    Cmd("Warp", "warp", () => new WarpCommand(), "Bend, twist, taper or stretch bodies, surfaces, curves, forms or meshes along an axis.")),
                G("Detail",
                    Btn("Add\nDetail", "add-detail", () => FreeformActions.AddDetail(Session), "Insert knots so the selected curve/surface has more control points without changing its shape.", RibbonSize.Large, hasSel),
                    Btn("Raise\nDegree", "raise-degree", () => FreeformActions.RaiseDegree(Session), "Increase the polynomial degree for smoother, more flexible surfaces.", RibbonSize.Large, hasSel)),
                G("Finish",
                    Cmd("Sew", "knit", () => new SewCommand(), "Sew (stitch) surfaces together; closed shells become solids."),
                    Cmd("Unsew", "unstitch", () => new UnstitchCommand(), "Take a sewn shell, or just some of its faces, apart into separate surfaces."),
                    Cmd("Thicken", "thicken", () => new ThickenCommand(), "Give a surface thickness to make a solid."),
                    Cmd("Trim", "trim", () => new TrimSurfaceCommand(), "Cut a surface with a plane, another surface or a solid and keep the side you want."),
                    Cmd("Split\nSurface", "splitbody", () => new SplitSurfaceCommand(), "Divide a surface into faces with a plane, face, surface, solid or a sketch/curve (split along its projection). The faces stay one surface, or each piece becomes its own surface."),
                    Cmd("Fillet /\nChamfer", "fillet", () => new CurveFilletCommand(false), "Round or cut the edges where the faces of a surface meet, or the corners of a flat curve or sketch."),
                    Cmd("Untrim", "untrim", () => new UntrimCommand(), "Undo a trim: restore the whole natural surface, or just fill the holes in a face."),
                    Cmd("Extend", "extend-surface", () => new ExtendSurfaceCommand(), "Grow a surface past one of its edges, continuing its shape."),
                    Cmd("Reverse\nNormal", "reverse-normal", () => new ReverseNormalCommand(), "Turn a surface around so its other side faces out."),
                    Cmd("Offset", "offset", () => new OffsetSurfaceCommand(), "Create a parallel copy of a surface or face."),
                    Cmd("Enclosed\nBody", "fill", () => new BoundaryFillCommand(), "Make solids from the spaces enclosed by surfaces, planes and bodies.")),
                G("Combine",
                    Cmd("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union), "Merge bodies and surfaces into one."),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body or surface out of another."),
                    Cmd("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect), "Keep only the shared volume.")),
                TransformGrp(), SelectGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Sheet metal (the essentials: flange, flat pattern, cut-outs)
        var sheet = new RibbonTab
        {
            Title = "Sheet Metal",
            Groups =
            [
                SketchGrp(), PlanesGrp(false),
                G("Sheet metal",
                    Cmd("Flange", "flange", () => new ContourFlangeCommand(), "Make bent sheet metal from a sketch of its cross-section: set thickness, bend radius and width. Corners become bends."),
                    Cmd("Fold", "fold", () => new FoldCommand(), "Fold a flat sketch of a sheet along its bend lines: draw the outline, mark each bend with a construction line, and set thickness, radius and angle. Makes trays, boxes and parts bent in several directions."),
                    Cmd("Edge\nFlange", "edge-flange", () => new EdgeFlangeCommand(), "Add a flange along an edge of a sheet-metal body: pick the thin end face, then set the leg length, bend angle and inside radius."),
                    Cmd("Flat\nPattern", "flat-pattern", () => new FlatPatternCommand(), "Unfold a bent body into its flat pattern with bend lines; save it as DXF for laser or waterjet cutting.")),
                G("Cut-outs",
                    Cmd("Extrude", "extrude", () => new ExtrudeCommand(), "Extrude a sketch, and set Operation to Cut to make holes and slots through the metal. Use Through all to go through every layer."),
                    Cmd("Hole", "hole", () => new HoleCommand(), "Cut a round or threaded hole through the metal."),
                    Split("split:sheet-edge", "Round or bevel the corners of flanges and cut-outs. Click the name (or right-click) for the other.",
                        Cmd("Fillet", "fillet", () => new FilletCommand(), "Round the corners of flanges and cut-outs."),
                        Cmd("Chamfer", "chamfer", () => new ChamferCommand(), "Bevel selected edges."))),
                G("Combine",
                    Cmd("Union", "union", () => new BooleanCommand(BooleanCommand.Kind.Union), "Merge bodies into one."),
                    Cmd("Subtract", "subtract", () => new BooleanCommand(BooleanCommand.Kind.Subtract), "Cut one body out of another."),
                    Cmd("Intersect", "intersect", () => new BooleanCommand(BooleanCommand.Kind.Intersect), "Keep only the shared volume.")),
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
                    Btn("Script\nEditor", "script", ToggleScriptPanel, "Show or hide the script editor and its console.", RibbonSize.Large, null, () => ShowScriptPanel),
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
                // The same Select group as every other tab: Object arms the sketch Select tool and the drag methods apply to it.
                SelectGrp(),
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
                    Btn("Transform", "move", TransformSketchSelection, "Move, rotate or scale the selected points and curves with the gizmo, or type exact values in its panel. Select first (shortcut G); R rotates and S scales while the gizmo is showing.", RibbonSize.Large, () => IsSketching),
                    Tool("Insert\nPoint", "origin-point", "Insert point", () => new InsertPointTool(), "Add a point to a curve you already drew: click a line, arc, circle or spline. A line becomes two lines, an arc or circle two arcs, and a spline gains a point."),
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
                G("Brainstorm",
                    Split("split:grease-pencil", "Freehand brainstorming marks and eraser. Click the name (or right-click) to choose.",
                        Tool("Grease\nPencil", "grease-pencil", "Grease pencil", () => new GreasePencilTool(), "In orthographic view, drag to sketch planar freehand brainstorming marks. Marks are saved annotations, separate from modeling curves."),
                        Tool("Eraser", "grease-eraser", "Grease eraser", () => new GreaseEraserTool(), "Click or drag over grease pencil marks to erase whole strokes. Undo restores an erase gesture."))),
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
                G("Setup",
                    Btn("New\nStudy", "sim-study", NewStudy, "Start a static stress study on the selected solid body. Select several touching bodies to study them together, each with its own material.", RibbonSize.Large, () => !IsSketching),
                    Btn("Select Study", "sim-new", () => Session.StartCommand(() => new SelectStudyCommand()), "Choose a saved static study.", small, id: "sim-new|Select static study"),
                    Btn("Materials", "materials", OpenMaterials, "Pick the material: stiffness and strength come from the library.", small, () => !IsSketching)),
                G("Supports",
                    SimCmd("Fixed\nSupport", "sim-fixed", () => new FixtureCommand(FixtureKind.Fixed), "Faces that cannot move at all."),
                    SimCmd("Roller /\nSymmetry", "sim-roller", () => new FixtureCommand(FixtureKind.Slider), "Faces that slide in their plane but cannot move through it."),
                    SimCmd("Fixed\nDirections", "sim-directional", () => new FixtureCommand(FixtureKind.Directional), "Lock only chosen global directions on faces.")),
                G("Loads",
                    SimCmd("Force", "sim-force", () => new LoadCommand(LoadKind.Force), "A force spread over faces."),
                    SimCmd("Pressure", "sim-pressure", () => new LoadCommand(LoadKind.Pressure), "Pressure acting on faces.", small),
                    SimCmd("Torque", "sim-torque", () => new LoadCommand(LoadKind.Torque), "A twisting moment on a flat face."),
                    SimCmd("Gravity", "sim-gravity", () => new LoadCommand(LoadKind.Gravity), "The part's own weight.")),
                G("Solve",
                    Btn("Create Mesh", "sim-mesh", MeshStudy, "Generate the tetrahedral mesh and look at it.", small, () => Session.ActiveStudy != null && !SimBusy, id: "sim-mesh|Create Mesh"),
                    Btn("Run\nStudy", "sim-run", RunStudy, "Mesh (if needed) and solve.", RibbonSize.Large, () => Session.ActiveStudy != null && !SimBusy)),
                G("Results",
                    Btn("von Mises\nStress", "sim-stress", () => SetResultKind(ResultKind.VonMises), "Color by equivalent stress.", RibbonSize.Large, () => HasResult),
                    Btn("Displacement", "sim-displacement", () => SetResultKind(ResultKind.Displacement), "Color by how far each point moves.", small, () => HasResult, id: "sim-displacement|Displace- ment"),
                    Btn("Safety Factor", "sim-safety", () => SetResultKind(ResultKind.SafetyFactor), "Yield strength divided by stress: below 1 means yielding.", small, () => HasResult, id: "sim-safety|Factor of Safety"),
                    Btn("Probe", "sim-probe", () => Session.StartCommand(new ProbeCommand()), "Click the result to read values.", small, () => HasResult),
                    Btn("Clear\nResults", "clear-analysis", ClearResults, "Show the normal model again.", RibbonSize.Large, () => Session.Result != null, id: "clear-analysis|Clear Results")),
            ],
        };

        // ---- Mesh (3D printing and reverse engineering)
        RibbonButton RegionBtn() => Cmd("Select\nRegion", "mesh-select", () => new MeshRegionCommand(), "Pick triangles on a mesh with smart patch, brush, rectangle or lasso, then delete and patch them, or extract them.");
        RibbonButton ClearColorsBtn() => Btn("Clear\nColors", "clear-analysis", () => Session.ClearAnalysis(), "Remove analysis colors from meshes.", RibbonSize.Large);
        var mesh = new RibbonTab
        {
            Title = "Mesh",
            Groups =
            [
                G("Import & Convert",
                    Btn("Import\nMesh", "mesh-import", ImportMesh, "Bring in a scan or print file: STL, OBJ, PLY or 3MF.", RibbonSize.Large, () => !IsSketching),
                    Cmd("Solid to\nMesh", "mesh-from-solid", () => new SolidToMeshCommand(), "Turn CAD bodies into triangle meshes."),
                    Split("split:mesh-cloud", "Bring in a scan as points and turn it into a mesh. Click the name (or right-click) for the other.",
                        Btn("Import Point\nCloud", "pointcloud-import", ImportPointCloud, "Bring in a scan as points (XYZ, PTS, CSV, PLY) and turn it into a mesh.", RibbonSize.Large, () => !IsSketching),
                        Btn("Reconstruct\nSurface", "mesh-reconstruct", ReconstructSurface, "Build a mesh from the imported point cloud: clean up, estimate normals, set the detail and trim.", RibbonSize.Large, () => !IsSketching))),
                G("Check",
                    Cmd("Mesh\nQuality", "mesh-quality", () => new MeshQualityCommand(), "Triangle quality, self-intersections and persistent defect highlights."),
                    Cmd("Print\nCheck", "mesh-check", () => new MeshCheckCommand(), "Pre-flight check: watertight, fits your printer, supports, weight.")),
                G("Repair",
                    Cmd("Repair", "mesh-repair", () => new MeshRepairCommand(), "Repair the entire mesh or a selected region; preview the holes to fill."),
                    RegionBtn()),
                G("Reshape",
                    Cmd("Reduce", "mesh-reduce", () => new MeshDecimateCommand(), "Simplify a dense mesh while keeping its shape."),
                    Cmd("Remesh", "mesh-remesh", () => new MeshRemeshCommand(), "Rebuild with even, well-shaped triangles of a chosen size."),
                    Cmd("Smooth", "mesh-smooth", () => new MeshSmoothCommand(), "Remove scanner noise without shrinking the part.")),
                G("Modify",
                    Cmd("Split", "mesh-cut", () => new MeshCutCommand(), "Cut with a plane and cap the halves."),
                    Cmd("Offset", "mesh-offset", () => new MeshOffsetCommand(), "Grow or shrink the surface (tolerances, clearances)."),
                    Cmd("Mesh\nBoolean", "mesh-boolean", () => new MeshBooleanCommand(), "Join, subtract or intersect two closed meshes.")),
                G("Print analysis",
                    Cmd("Wall\nThickness", "mesh-thickness", () => new MeshThicknessCommand(), "Color the part by wall thickness."),
                    Cmd("Overhang &\nOrientation", "mesh-overhang", () => new MeshOverhangCommand(), "Find what needs supports and the best way to lay the part down."),
                    ClearColorsBtn()),
                TransformGrp(), InspectGrp(), AppearanceGrp(),
            ],
        };

        // ---- Reverse Engineer
        var reverse = new RibbonTab
        {
            Title = "Reverse Engineer",
            Groups =
            [
                G("Prepare",
                    Cmd("Align", "mesh-align", () => new MeshAlignCommand(), "Drop on the bed, lay a face flat, or best-fit onto a CAD body."),
                    RegionBtn()),
                G("Sketch from Scan",
                    Cmd("Section\nSketch", "mesh-section", () => new MeshSectionSketchCommand(), "Create editable lines, arcs and circles from a scan section. Add dimensions and constraints, then extrude or revolve to build parametric features.")),
                G("Fit Surfaces",
                    Cmd("Segment\n& Fit", "mesh-fit", () => new MeshSegmentCommand(), "Identify and measure regions across a scan; optionally create separate fitted CAD surfaces."),
                    Cmd("Fit Surface\nto Region", "mesh-patch", () => new MeshFitSurfaceCommand(), "Choose a region and fit one exact surface or smooth NURBS patch. This does not recover a feature history.")),
                G("Rebuild",
                    Cmd("Rebuild\nfrom Scan", "mesh-fit", () => new MeshRebuildCommand(), "Fit faces and attempt a shell or direct-edit solid. This does not recover the original sketches or feature history."),
                    Cmd("Freeform\nfrom Scan", "form-scan", () => new FormFromScanCommand(), "Create an editable freeform cage around the scan for manual shape refinement."),
                    new RibbonButton { Label = "Fallback", Icon = "mesh-solid", Description = "Triangle-by-triangle conversion when fitted rebuilding is unsuitable.", Menu =
                        [Cmd("Mesh to\nSolid", "mesh-solid", () => new MeshToSolidCommand(), "Last resort: one CAD face per triangle. Heavy, hard to edit, and no recovered feature history.")] }),
                G("Check",
                    Cmd("Deviation", "mesh-deviation", () => new MeshDeviationCommand(), "Compare a scan with its CAD model and show the error as colors."),
                    ClearColorsBtn()),
                TransformGrp(), InspectGrp(), AppearanceGrp(),
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
                    Btn("Isometric", "view-iso", () => ShowStandardView(-35, 28), "A three-quarter view.", RibbonSize.Large),
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Look straight at the selected plane, face or sketch.", RibbonSize.Large, null, null, "N")),
                G("Navigate",
                    Btn("Zoom\nExtents", "zoom-extents", () => Session.ZoomExtents(), "Fit everything in the window.", RibbonSize.Large, null, null, "F"),
                    HomeButton(),
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
                    Btn("Hide", "visibility-off", () => HideSelected(), "Hide the selected objects. With faces selected, hide just those faces; this is saved with the document. Show All brings them back.", small, () => Session.SelectedObjects.Any()),
                    Btn("Isolate", "isolate", () => IsolateSelected(), "Hide everything except the selected objects. With faces selected, show only those faces; Show All brings the rest back.", small, () => Session.SelectedObjects.Any()),
                    Btn("Show All", "visibility-on", () => ShowAll(), "Show every hidden object, every hidden face and every face hidden by Isolate.", small, () => Session.FacesIsolated || Session.AnyHiddenFaces || Session.Objects.Any(o => !o.Visible)),
                    Btn("Grid", "grid", () => Session.ShowGrid = !Session.ShowGrid, "Show the construction grid.", small, null, () => Session.ShowGrid),
                    Btn("Data\nTable", "table", ToggleDataPanel, "Show or hide the Data panel: spreadsheets saved with the document. Paste from Excel or open a CSV, text or Excel file, then use a sheet for points, positions or motion with @ and its name.", RibbonSize.Large, null, () => IsDataPanelOpen),
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
        bool HasBodies() => Session.Objects.Any(o => o.Kind == ObjKind.Solid);       // a joint tool makes a part of any body it picks
        bool CanCouple() => Session.JointObjects.Count(j => j.joint.Type is JointType.Revolute or JointType.Cylindrical or JointType.Slider) >= 2;
        RibbonButton MakePartBtn() => AsmCmd("Make\nPart", "component", () => new MakePartCommand(), "Turn solid bodies in this file into parts, so joints can connect them. Picking a body with a joint tool does this for you.", RibbonSize.Large, HasBodies);
        RibbonButton CoupleBtn() => AsmCmd("Couple", "joint-gear", () => new CouplingCommand(), "Make one joint follow another at a ratio: gears, belts and chains, a rack, pulley or screw, or a lever or cable.", RibbonSize.Large, CanCouple);
        RibbonButton[] JointTools() =>
        [
            AsmCmd("Rigid", "joint-rigid", () => new JointCommand(JointType.Rigid), "Lock one component to another: no motion.", RibbonSize.Large, HasBodies),
            AsmCmd("Revolute", "joint-revolute", () => new JointCommand(JointType.Revolute), "A hinge or pin: the second component turns about the axis.", RibbonSize.Large, HasBodies),
            AsmCmd("Slider", "joint-slider", () => new JointCommand(JointType.Slider), "The second component slides along one direction.", RibbonSize.Large, HasBodies),
            AsmCmd("Cylindrical", "joint-cyl", () => new JointCommand(JointType.Cylindrical), "Turns about an axis and slides along it (a bolt in a hole).", RibbonSize.Large, HasBodies),
            AsmCmd("Ball", "joint-ball", () => new JointCommand(JointType.Ball), "Pivots about a point: spin and tilt (a ball and socket).", RibbonSize.Large, HasBodies),
            AsmCmd("Planar", "joint-planar", () => new JointCommand(JointType.Planar), "Slides over a flat face in two directions.", RibbonSize.Large, HasBodies),
        ];
        var assemble = new RibbonTab
        {
            Title = "Assemble",
            Groups =
            [
                SelectGrp(),
                G("Add Parts",
                    AsmCmd("Insert\nComponent", "component", () => new InsertComponentCommand(), "Place a part from another open document or a part file. Model each part on its own first (bottom-up)."),
                    Btn("Parts\nLibrary", "component", OpenLibrary, "Browse a folder of STEP or Prism parts (and built-in fasteners, washers, bearings and more) as pictures. Double-click a part to insert it.", RibbonSize.Large, () => !IsSketching),
                    AsmCmd("Create\nPart", "component", () => new CreatePartCommand(), "Start a new part inside this assembly. It opens in the Design workspace; press Finish part and return when it is done.", RibbonSize.Large),
                    MakePartBtn()),
                G("Assemble",
                    Btn("Ground", "ground", ToggleGround, "Fix the selected component in place. Joints are solved outwards from grounded components. The first component is grounded for you.", RibbonSize.Large, () => HasComponents() && !IsSketching, () => SelectedComponent?.Component?.Grounded == true),
                    AsmCmd("Move\nComponent", "move", () => new MoveComponentCommand(), "Position a free component: move it or turn it about its centre.", RibbonSize.Large, HasComponents),
                    AsmCmd("Mate", "joint-rigid", () => new MateCommand(), "Snap a free component onto another by two faces: touching, flush or a set distance apart.", RibbonSize.Large, HasComponents)),
                G("Manage Parts",
                    Btn("Edit\nPart", "component", () => EditComponentPart(), "Open the selected component's part for editing in the Design workspace. Back to assembly brings the changes in.", RibbonSize.Large, () => !IsSketching && SelectedComponent != null),
                    AsmCmd("Component\nPattern", "pattern-linear", () => new ComponentPatternCommand(), "Repeat a component in a line or around an axis.", RibbonSize.Large, HasComponents),
                    Btn("Refresh", "refresh", () => { int n = Session.RefreshComponentsFromSource(); Session.Say(n == 0 ? "Nothing to refresh: no linked part files changed." : $"Refreshed {n} component(s) from their files."); }, "Reload components from the part files they were inserted from, keeping their positions.", RibbonSize.Large, () => HasComponents() && !IsSketching)),
                G("Review",
                    AsmCmd("Interference", "interference", () => new InterferenceCommand(), "Find where components overlap.", RibbonSize.Large, HasComponents),
                    AsmCmd("Measure", "measure", () => new MeasureCommand(), "Measure distances and angles between components.", RibbonSize.Large, HasComponents),
                    AsmCmd("Properties", "properties", () => new PropertiesCommand(), "Volume, mass, center of mass and moments of inertia of the selection.", RibbonSize.Large, HasComponents),
                    AsmCmd("Parts\nList", "component", () => new PartsListCommand(), "Count every part, with material, mass and cost from each part's info. Save it as a CSV.", RibbonSize.Large, HasComponents),
                    AsmCmd("Exploded\nView", "explode", () => new ExplodeCommand(), "Pull the parts apart to see how they fit. The real positions are untouched.", RibbonSize.Large, HasComponents)),
            ],
        };
        var joints = new RibbonTab
        {
            Title = "Joints",
            Groups =
            [
                SelectGrp(),
                G("Joint", JointTools()),
                G("Couple", CoupleBtn()),
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
                    AsmCmd("New\nAnimation", "animation", () => new AnimationCommand(true), "Open a new five-second animation in the timeline."),
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
                    Cmd("Render\nAnimation", "animation", () => new RenderAnimationCommand(), "Render an animation (joint motion, exploded view) and/or a camera turntable as pictures, a GIF and, with ffmpeg installed, an MP4.")),
                G("Output",
                    Btn("Save\nPicture", "save", () => { Session.SaveRender(); }, "Save the last render as a PNG in Documents\\Prism\\Renders.", RibbonSize.Large, () => Session.Render != null),
                    Btn("Close\nPicture", "cancel", () => Session.CloseRender(), "Hide the rendered picture and return to the model.", RibbonSize.Large, () => Session.Render != null)),
                G("Lighting",
                    Cmd("Sun", "light-sun", () => new LightsCommand(LightKind.Sun), "Drop in a global sun: direction, illuminance in lux and colour temperature. Drag it in the viewport."),
                    Cmd("Lamp", "light-lamp", () => new LightsCommand(LightKind.Point), "Drop in a point lamp in lumens with real inverse-square falloff and soft shadows. Drag it anywhere."),
                    Cmd("Spot", "light-spot", () => new LightsCommand(LightKind.Spot), "Drop in a spotlight: lumens, cone angle and soft edge. Drag the lamp and its aim point."),
                    Cmd("Edit\nLights", "light-edit", () => new LightsCommand(), "Select, drag, tune or delete the lights in this document."),
                    Btn("Show\nLights", "light-lamp", () => { Session.ShowLights = !Session.ShowLights; Session.Invalidate(); }, "Show or hide the light markers in the viewport.", RibbonSize.Large, null, () => Session.ShowLights)),
                G("Materials", Btn("Materials", "materials", () => OpenMaterials(), "Colours and finishes: metals and glossy plastics reflect in the render.", RibbonSize.Large)),
                G("Camera",
                    Btn("Look\nAt", "look-at", () => Session.LookAtSelection(), "Look straight at the selected plane, face or sketch (N).", RibbonSize.Large, null, null, "N")),
            ],
        };
        var cam = new RibbonTab
        {
            Title = "Subtractive",
            Groups =
            [
                G("Setup", Btn("Import mold", "mold-import", () => _ = ImportManufacturingMold(), "Import exact mold solids from a .prism_mld file."),
                    Cmd("CAM setup", "cnc", () => new CamSetupCommand(), "Stock, G54 work zero and shared tool settings."),
                    Cmd("Select setup", "cnc", () => new SelectCamSetupCommand(), "Switch named stock and work-zero setups."),
                    Cmd("Tool library", "cnc", () => new CamToolLibraryCommand(), "Save named tools with material-specific cutting feeds and speeds."),
                    Cmd("Design check", "check", () => new CncDesignCheckCommand(), "Screen top-down access, reach and thin walls on CAD bodies or meshes.")),
                G("Toolpaths", ToolMenu("Toolpaths", "cnc-profile", "Create a milling, drilling or turning operation.",
                    Cmd("Profile", "cnc-profile", () => new ProfileCommand(), "Profile milling with depth passes and holding tabs."),
                    Cmd("Pocket", "cnc-pocket", () => new PocketCommand(), "Pocket milling with islands."),
                    Cmd("Adaptive", "cnc-pocket", () => new AdaptiveClearCommand(), "Clear a pocket with even cutter load: deeper and faster, no slotting."),
                    Cmd("Turning", "cnc", () => new TurnCommand(), "Lathe: face, rough and finish a round part from a half-section sketch."),
                    Cmd("Drill", "cnc-drill", () => new DrillCommand(), "Drilling and peck cycles."),
                    Cmd("Face", "cnc-face", () => new FaceMillCommand(), "Face the stock with zigzag passes."),
                    Cmd("Image\nCarve", "cnc-image", () => new ImageReliefCommand(), "Carve a picture into the stock: depth follows brightness."),
                    Cmd("3D Rough", "cnc", () => new Rough3DCommand(), "Layered raster roughing of setup stock, leaving an allowance around the model."),
                    Cmd("3D\nFinish", "cnc", () => new ParallelFinishCommand(), "Finish a 3D shape with parallel passes that follow its surface (ball nose or flat end mill)."),
                    Cmd("Constant-Z\nFinish", "cnc", () => new ConstantZFinishCommand(), "Finish steep walls with horizontal contours at successive heights."))),
                G("Review", Cmd("Playback", "animation", () => new CamPlaybackCommand(), "Animate a cutter or nozzle along a path."),
                    Cmd("Operation order", "toolpath", () => new CamProgramOrderCommand(), "Set the machining sequence used by posting and stock review."),
                    Cmd("Stock review", "cnc", () => new CncStockCommand(), "Sample material removal, remaining stock, gouges, rapid and holder contacts."),
                    Cmd("Setup sheet", "check", () => new CamReviewCommand(), "Review operation sources and export a setup sheet."),
                    Cmd("Pass view", "toolpath", () => new ToolpathViewCommand(), "Inspect depth passes.")),
                G("Export", Cmd("Post\nG-code", "gcode", () => new CncPostCommand(), "Post reviewed CNC toolpaths for GRBL, Mach3 or LinuxCNC.")),
            ],
        };
        freeform.Groups.Insert(3, G("Continuity",
            Cmd("Match\nSurface", "nurbs-patch", () => new MatchSurfaceCommand(), "Match compatible natural patch boundaries with G0, G1 or G2 constraints."),
            Cmd("Check\nSeam", "curvature", () => new SurfaceContinuityCommand(), "Inspect sampled continuity between two natural patch boundaries.")));
        form.Groups.Insert(3, G("Validate", Cmd("Check\nCage", "check", () => new CheckCageCommand(), "Check manifold topology, winding, degenerate faces and extraordinary points.")));
        var additive = new RibbonTab {Title="3D FDM",Groups=[
            G("Prepare",Cmd("Place on bed","rotate",()=>new PlaceOnBedCommand(),"Pick the flat face of a CAD solid to set it down at Z = 0."),
                Cmd("FDM check","print3d",()=>new FdmCheckCommand(),"Check walls, overhangs, bed contact and fit against your build dimensions.")),
            G("Troubleshoot",new RibbonButton
            {
                Id="menu:fdm-troubleshoot",Label="Troubleshoot geometry",Icon="mesh-quality",
                Description="Repair imported meshes and investigate geometry warnings.",
                Menu=[
                    Cmd("Repair","mesh-repair",()=>new MeshRepairCommand(),"Merge vertices, fix normals and fill holes in imported meshes."),
                    Cmd("Mesh Quality","mesh-quality",()=>new MeshQualityCommand(),"Find thin triangles, open edges and self-intersections."),
                    Cmd("Overhang & Orientation","mesh-overhang",()=>new MeshOverhangCommand(),"Find overhangs and compare orientations."),
                    Cmd("Wall Thickness","mesh-thickness",()=>new MeshThicknessCommand(),"Color the part by wall thickness."),
                    Cmd("Check geometry","check",()=>new CheckCommand(),"Inspect model geometry.")
                ]
            }),
            G("Export",Cmd("Export for printing","print3d",()=>new PrintExportCommand(),"Review the bodies and write a model for your slicer."))]};
        var resin = new RibbonTab {Title="3D Resin",Groups=[
            G("Prepare",Btn("Import mold","mold-import",() => _ = ImportManufacturingMold(),"Import exact mold solids from a .prism_mld file."),
                Cmd("Tilt part","rotate",()=>new TransformCommand(TransformCommand.Kind.Rotate),"Tilt the model, then rerun resin checks to compare cups, islands and peak layer area."),
                new RibbonButton
                {
                    Id="menu:resin-hollow-drain",Label="Hollow & Drain",Icon="shell",
                    Description="Optionally hollow the CAD body and add drain or vent openings.",
                    Menu=[
                        Cmd("Hollow body","shell",()=>new ShellCommand(),"Hollow a CAD body with a chosen wall thickness."),
                        Cmd("Drain / vent hole","pcb-hole",()=>new HoleCommand(),"Cut a plain drain or vent hole from an entry face into the cavity.")
                    ]
                },
                Cmd("Resin check","print3d",()=>new ResinCheckCommand(),"Screen cavities, drainage, potential cups, islands, thin features and build fit.")),
            G("Troubleshoot",new RibbonButton
            {
                Id="menu:resin-troubleshoot",Label="Troubleshoot geometry",Icon="mesh-quality",
                Description="Inspect hollow walls, repair imported meshes and investigate geometry warnings.",
                Menu=[
                    Cmd("Section","section",()=>new SectionCommand(),"Inspect hollow walls and drainage paths."),
                    Cmd("Repair","mesh-repair",()=>new MeshRepairCommand(),"Merge vertices, remove junk, fix normals and fill holes."),
                    Cmd("Mesh Quality","mesh-quality",()=>new MeshQualityCommand(),"Find thin triangles, open edges and self-intersections."),
                    Cmd("Wall Thickness","mesh-thickness",()=>new MeshThicknessCommand(),"Color the part by wall thickness."),
                    Cmd("Check geometry","check",()=>new CheckCommand(),"Validate exact geometry.")
                ]
            }),
            G("Export",Cmd("Export for resin","print3d",()=>new PrintExportCommand(true),"Review the bodies and write a model for your resin slicer."))]};
        var mold = new RibbonTab { Title = "Mold", Groups = [G("Part", Btn("Import\nfrom file", "mold-file", () => _ = ImportMoldPart(), "Bring a part in from a Prism, STEP, IGES or BREP file as a reference part, ready for a mold."), Cmd("From\nopen tab", "mold-tab", () => new MoldPartFromTabCommand(), "Bring a part in from another open tab as a reference part.")), G("Mold", Cmd("Create\nMold", "mold", () => new MoldCommand(), "Cut a part out of a block, split the block and pull the halves apart."))] };
        mold.Groups.Add(G("Inspect", Cmd("Draft analysis", "draft", () => new DraftCommand(), "Check pull-direction draft."), Cmd("Section analysis", "section", () => new SectionCommand(), "Inspect cavities and walls."), Cmd("Check geometry", "check", () => new CheckCommand(), "Check tooling geometry.")));
        simulate.Groups.Single(g => g.Title == "Solve").Items.Insert(1,
            Btn("Refinement", "sim-mesh", () => Session.StartCommand(() => new ConvergenceCommand()), "Compare three meshes using displacement.", small, id: "sim-mesh|Mesh refinement"));
        simulate.Groups.Add(G("Advanced", new RibbonButton
        {
            Label = "Advanced", Icon = "generative", Id = "menu:static-advanced",
            Menu =
            [
                Btn("Fatigue", "cae-fatigue", () => Session.StartCommand(new FatigueCommand()), "Inspect fatigue under repeated loading.", can: () => HasResult),
                SimCmd("Generative\nDesign", "generative", () => new GenerativeCommand(), "Find a lighter shape from the study supports and loads."),
            ],
        }));
        simulate.Groups.Add(G("Reuse & Export", new RibbonButton
        {
            Label = "Face Selections", Icon = "sim-fixed", Id = "menu:static-face-selections", Size = RibbonSize.Small,
            Menu =
            [
                Cmd("Save face selection","sim-fixed",()=>new NamedFacesCommand(),"Name selected boundary faces."),
                Cmd("Recall face selection","sim-fixed",()=>new RecallFacesCommand(),"Recall a named face group."),
                Cmd("Saved CAE setups","sim-run",()=>new SavedCaeCommand(),"Rerun saved thermal/modal setups."),
            ],
        }, Cmd("Export results","export",()=>new ExportStudyCommand(),"Export static nodal results and metadata.", small)));
        var thermal = new RibbonTab {Title="Thermal",Groups=
        [G("Setup",Cmd("Thermal\nAnalysis","cae-thermal",()=>new CaeSolveCommand(true),"Set up steady or transient heating and cooling."),
            Btn("Materials","materials",OpenMaterials,"Assign conductivity, density and heat capacity.")),
         G("Results",Btn("Clear\nResults","clear-analysis",ClearResults,"Return to normal model shading."))]};
        var dynamics = new RibbonTab {Title="Dynamics",Groups=
        [G("Setup",Cmd("Natural\nFrequencies","cae-modal",()=>new CaeSolveCommand(false),"Set up natural frequencies and vibration modes."),
            Btn("Materials","materials",OpenMaterials,"Assign elastic modulus, Poisson ratio and density.")),
         G("Results",
            Btn("Show\nMode", "cae-modal", () =>
            {
                Session.StartCommand(() => new CaeSolveCommand(false));
                if (Session.ActiveCommand is CaeSolveCommand command)
                    foreach (var section in command.WorkflowSections) section.IsExpanded = section.Title == "Results";
            }, "Open the returned mode controls.", can: () => Session.Cae?.Outcome?.Kind == CaeKind.Modal),
            Btn("Clear\nResults","clear-analysis",ClearResults,"Return to normal model shading."))]};
        var motion = new RibbonTab { Title = "Motion", Groups =
        [
            G("Setup",
                Cmd("Motion\nStudy", "motion-dynamics", () => new MotionStudyCommand(), "Set joint drives and inspect movement, forces and reactions."),
                Btn("Materials", "materials", OpenMaterials, "Assign materials when calculating masses and forces.")),
            G("Loads",
                Cmd("Spring /\nActuator", "motion-spring", () => new MotionSpringCommand(), "Add a spring, damper or actuator between points."),
                Cmd("Force /\nTorque", "motion-force", () => new MotionLoadCommand(), "Apply a constant, pulsed or alternating force or torque.")),
            G("Results",
                Btn("Clear\nResults", "clear-analysis", ClearResults, "Remove the motion paths from the model."),
                Cmd("Load from\nMotion", "motion-force", () => new MotionLoadToStaticCommand(), "Use a calculated joint reaction in a new Static study.")),
        ] };
        var fluids = new RibbonTab {Title="Fluids",Groups=
        [G("Pipe Calculations",Cmd("Pipe\nFlow","cae-fluid",()=>new PipeFlowCommand(),"Incompressible circular-pipe pressure drop and Reynolds number for one pipe."),Cmd("Pipe\nNetwork","cae-network",()=>new PipeNetworkCommand(),"Steady flow through pipes and pumps: how the flow splits, the pressure at each point and where a pump settles.")),
         G("3D Flow",Cmd("Flow\nStudy","cae-flow",()=>new FlowStudyCommand(),"Basic 3D flow around a body or through a duct: colors, streamlines, drag and pressure drop. Dependable for slow, smooth flow; a picture for fast, turbulent flow."))]};
        var ecad = new RibbonTab
        {
            Title = "ECAD",
            Groups =
            [
                SketchGrp(), PlanesGrp(false),
                G("Board",
                    Btn("Import\nboard STEP", "import", ImportStep, "Import a board and component geometry from a STEP file.", id: "import|Import STEP"),
                    new RibbonButton
                    {
                        Id = "menu:Create board:pcb|Rectangular board,pcb|Board from outline",
                        Label = "Create board", Icon = "pcb", Description = "Create physical board geometry for mechanical integration.",
                        Menu = [
                            Cmd("Rectangular board", "pcb", () => new PcbBoardCommand(), "Board thickness, elevation and four optional mounting holes."),
                            Cmd("Board from outline", "pcb", () => new PcbOutlineCommand(), "Extrude a closed planar sketch, curve or face into a board.")
                        ]
                    },
                    Cmd("Mounting\nHole", "pcb-hole", () => new PcbHoleCommand(), "Click a board face to cut a mounting hole.")),
                G("Components", Cmd("Component\nEnvelope", "component", () => new ComponentEnvelopeCommand(), "Create a package envelope or mechanical keepout."),
                    Cmd("Place on\nBoard", "component", () => new PlaceOnBoardCommand(), "Click a flat board face to position a component solid."),
                    new RibbonButton
                    {
                        Id = "menu:ECAD:Transform", Label = "Transform", Icon = "move",
                        Description = "Position component geometry manually.",
                        Menu = [
                            MoveBtn(RibbonSize.Large),
                            Cmd("Rotate", "rotate", () => new TransformCommand(TransformCommand.Kind.Rotate), "Orient a component."),
                            Btn("Duplicate", "duplicate", Session.Duplicate, "Copy a selected component.", can: hasSel)
                        ]
                    }),
                G("Fit check",
                    Cmd("Interference", "interference", () => new InterferenceCommand(), "Find overlapping solids."),
                    Cmd("Check\nClearance", "measure", () => new BoardClearanceCommand(), "Compare the gap between two solids with a required clearance."),
                    Cmd("Section\nView", "section", () => new SectionCommand(), "Look inside the board and enclosure.")),
                G("Export", Cmd("Export\nSTEP", "export", () => new BoardStepExportCommand(
                    () => PickSaveFile?.Invoke("Export STEP", "Board.step", "step") ?? System.Threading.Tasks.Task.FromResult<string?>(null)),
                    "Review which bodies will be included, then export STEP.")),
                AppearanceGrp(),
            ],
        };
        foreach(var tab in new[]{thermal,dynamics})tab.Groups.Add(G("Reuse",Cmd("Saved setups","sim-run",()=>new SavedCaeCommand(),"Run a saved thermal or modal case."),Cmd("Recall faces","sim-fixed",()=>new RecallFacesCommand(),"Recall named boundary faces.")));
        foreach (var tab in new[] { home, assemble, render, animate }) tab.Groups.Add(ShareGrp());
        foreach (var t in new[] { file, home, solid, surface, form, freeform, direct, sheet, ecad, scriptTab, cam, additive, resin, analyze, mesh, reverse, simulate, thermal, dynamics, motion, fluids, assemble, joints, animate, render, mold, _materialsTab, _sketchTab, view }) Tabs.Add(t);
        // Retired from the ribbon but kept in the catalog so history steps saved by older files still re-edit.
        // The ribbon's Move buttons arm the move gizmo instead of a command; keep the param-driven
        // transform reachable from command search.
        CommandCatalog.Register(() => new TransformCommand(TransformCommand.Kind.Move));
        CommandCatalog.Register(() => new EdgeSurfaceCommand());
        foreach (Func<Command> make in new Func<Command>[] { () => new MeshRepairCommand(true), () => new MeshSectionCommand(), () => new MeshSectionFitCommand() }) CommandCatalog.Register(make);
        // folded into other tools on the ribbon; scripts and saved steps still reach them
        foreach (Func<Command> make in new Func<Command>[] { () => new SweepTwoRailsCommand(), () => new NetworkSurfaceCommand(), () => new BlendSurfaceCommand(), () => new PatchCommand(), () => new PlanarSurfaceCommand(), () => new RuledSurfaceCommand(), () => new SplitCurveCommand(), () => new DeformCommand(), () => new IntersectSurfacesCommand(), () => new CurveIntersectCommand() }) CommandCatalog.Register(make);
        CommandCatalog.Register(() => new FillOpeningCommand());
        // Preserve recipe loading for documents saved with the former Solid tools.
        CommandCatalog.Register(() => new SweepCommand());
        CommandCatalog.Register(() => new LoftCommand());
        OrganizeRibbons(TransformGrp, InspectGrp, AppearanceGrp, SelectGrp);
        // Every button gets the help written for it on its tab; a command's own topic and the icon's topic are fallbacks.
        // Buttons shared between tabs (selection, materials) resolve on the last tab they appear on.
        foreach (var t in Tabs)
            foreach (var b in t.Buttons)
            {
                var owner = t.Title; var button = b; var command = b.ExampleResolver; b.ExampleResolver = () => Help.ToolHelp.ForRibbon(owner, button.Icon, button.Label, command);
                b.TabTitle = owner; foreach (var item in b.Menu ?? []) item.TabTitle = owner;       // Learn More opens this tool's row on this ribbon's page of the website
            }
        FinishRibbon();
    }
}
