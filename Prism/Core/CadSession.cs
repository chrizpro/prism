using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.Core;

public enum DisplayMode { Shaded, ShadedEdges, Wireframe }

public enum ViewLayout { Single, Dual, Quad }

public sealed class PreviewItem
{
    /// <summary>Dimmed surroundings (the rest of the assembly while one part is being edited): drawn faintly, never picked.</summary>
    public bool Context { get; init; }
    public PreviewItem(Shape shape, ObjKind kind, uint? solidColor = null) { Shape = shape; Kind = kind; SolidColor = solidColor; }
    public PreviewItem(Form.SubdMesh form, uint? solidColor = null) { FormCage = form; Kind = ObjKind.Form; SolidColor = solidColor; }
    public PreviewItem(Meshing.TriMesh tri, uint? solidColor = null) { Tri = tri; Kind = ObjKind.Mesh; SolidColor = solidColor; }
    /// <summary>A see-through helper volume (the tool that is cutting), drawn faintly on top of the result.</summary>
    public bool Ghost { get; init; }
    public Meshing.TriMesh? Tri { get; }
    public Shape? Shape { get; }
    public Form.SubdMesh? FormCage { get; }
    public ObjKind Kind { get; }
    /// <summary>Set when the preview replaces an existing object and should look like it (not like a ghost).</summary>
    public uint? SolidColor { get; }
    MeshData? _m;
    public MeshData Mesh => _m ??= Tri != null ? Tri.ToMeshData() : FormCage != null ? FormCage.BuildMesh() : Shape!.Tessellate();
}

/// <summary>
/// One state in the document history. History is a tree: editing after a rollback starts a new branch instead of
/// throwing the old future away, so nothing is ever lost.
/// </summary>
public sealed class HistoryNode
{
    public int Id { get; init; }
    public HistoryNode? Parent { get; init; }
    public string Label { get; set; } = "";
    public string Icon { get; init; } = "box";
    public DateTime Time { get; init; } = DateTime.Now;
    public ImmutableList<SceneObject> After { get; set; } = [];
    public ImmutableList<DocumentParameter> Parameters { get; set; } = [];
    /// <summary>Name given by the user (or "Saved") - a checkpoint you can find again.</summary>
    public string? Pin { get; set; }
    /// <summary>Drawing snapshot at this step; null inherits the preceding snapshot.</summary>
    public string? DrawingState { get; set; }
    /// <summary>How the command behind this step was set up, so the step can be edited later (null for steps made another way).</summary>
    public Commands.FeatureRecipe? Recipe { get; set; }
    public List<HistoryNode> Children { get; } = [];
    /// <summary>The branch Redo follows (the most recently visited child).</summary>
    public HistoryNode? Preferred { get; set; }
    public bool IsRoot => Parent == null;

    /// <summary>Objects added/changed by this step compared with its parent, and names of objects it removed.</summary>
    public (List<SceneObject> Added, List<SceneObject> Modified, List<string> Removed) Diff()
    {
        var before = Parent?.After ?? [];
        var byId = before.ToDictionary(o => o.Id);
        var added = new List<SceneObject>(); var modified = new List<SceneObject>();
        foreach (var o in After)
        {
            if (!byId.TryGetValue(o.Id, out var old)) added.Add(o);
            else if (!ReferenceEquals(old, o) && (old.Form != o.Form || !ReferenceEquals(old.Tri, o.Tri) || (old.Tri == null && !ReferenceEquals(old.Shape, o.Shape)) || old.Name != o.Name || old.Color != o.Color || old.Look != o.Look)) modified.Add(o);
        }
        var now = After.Select(o => o.Id).ToHashSet();
        var removed = before.Where(o => !now.Contains(o.Id)).Select(o => o.Name).ToList();
        return (added, modified, removed);
    }
}

/// <summary>Everything the user is working on: the document, selection, active command and viewport state.</summary>
public sealed partial class CadSession : ObservableObject
{
    // ------------------------------------------------------------ document
    /// <summary>The four view cameras: 0 is the main (free) view, 1-3 are Front, Right and Top used by the multi-view layouts.</summary>
    public Camera[] Cameras { get; } = [new() { Label = "Isometric" }, new() { Label = "Front" }, new() { Label = "Right" }, new() { Label = "Top" }];
    Camera? _activeCamera;
    /// <summary>The camera of the view being used right now (the one under the pointer); tools, picking and drawing all work through it.</summary>
    public Camera Camera { get => _activeCamera ??= Cameras[0]; set => _activeCamera = value; }
    public ViewLayout Layout { get; private set; }
    public bool LinkViews { get; set; } = true;
    public event Action? LayoutChanged;
    public int VisibleViews => Layout switch { ViewLayout.Single => 1, ViewLayout.Dual => 2, _ => 4 };
    public IEnumerable<Camera> VisibleCameras => Layout switch { ViewLayout.Single => [Cameras[0]], ViewLayout.Dual => [Cameras[0], Cameras[3]], _ => Cameras };

    static void Standard(Camera c, double yawDeg, double pitchDeg, bool ortho) { c.SetView(yawDeg, pitchDeg); c.Orthographic = ortho; c.Locked = true; }

    public void SetLayout(ViewLayout l)
    {
        Layout = l;
        var main = Cameras[0];
        Standard(Cameras[1], 0, 0, Ortho); Standard(Cameras[2], 90, 0, Ortho); Standard(Cameras[3], 0, 90, Ortho);
        foreach (var c in Cameras.Skip(1)) { c.Target = main.Target; c.Distance = main.Distance; }
        _activeCamera = main;
        if (l != ViewLayout.Single) ZoomExtents();
        LayoutChanged?.Invoke(); Invalidate();
    }

    /// <summary>Linked views follow each other: panning or zooming one moves the others to the same spot and scale.</summary>
    public void SyncViews(Camera src)
    {
        if (!LinkViews || Layout == ViewLayout.Single) return;
        foreach (var c in VisibleCameras) if (!ReferenceEquals(c, src)) { c.Target = src.Target; c.Distance = src.Distance; }
        Invalidate();
    }
    public ImmutableList<SceneObject> Objects { get; private set; } = [];
    readonly Dictionary<string, int> _counters = [];

    // ---- history tree
    int _nextNodeId = 1;
    public HistoryNode Root { get; private set; } = new() { Id = 0, Label = "Start", Icon = "new" };
    public HistoryNode Current { get; private set; }
    string? _pendingLabel, _pendingIcon;
    bool _navigating;
    public event Action? TimelineChanged;
    /// <summary>Objects to highlight while a history step is hovered.</summary>
    public HashSet<Guid> HighlightIds { get; } = [];

    // ---- origin (planes, axes, point) - selectable references that live in the browser
    public static readonly string[] OriginNames = ["XY Plane", "XZ Plane", "YZ Plane", "X Axis", "Y Axis", "Z Axis", "Origin"];
    public bool[] OriginVisible { get; } = new bool[7];
    public event Action? OriginChanged;
    public void SetOriginVisible(int i, bool v)
    {
        OriginVisible[i] = v;
        if (!v && ActiveCommand != null) { _suppCmd = ActiveCommand; _originSuppressed.Add(i); }       // hidden by the user while a command wanted it shown: respect that until the command ends
        OriginChanged?.Invoke(); Invalidate();
    }
    readonly HashSet<int> _originSuppressed = [];
    Command? _suppCmd;
    public static Plane3 OriginPlane(int i) => i == 0 ? Plane3.XY : i == 1 ? Plane3.XZ : Plane3.YZ;
    public static (Vec3 o, Vec3 dir) OriginAxis(int i) => (Vec3.Zero, i == 3 ? Vec3.UnitX : i == 4 ? Vec3.UnitY : Vec3.UnitZ);

    public event Action? SceneChanged;      // objects added/removed/modified
    public event Action? ViewChanged;       // redraw only
    public event Action? StateChanged;      // command / sketch / selection mode changed (UI refresh)

    [ObservableProperty] public partial string Status { get; set; } = "Ready";
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial DisplayMode Display { get; set; } = DisplayMode.ShadedEdges;
    /// <summary>See-through bodies: faces turn translucent so hidden edges and inner shapes show.</summary>
    [ObservableProperty] public partial bool XRay { get; set; }
    [ObservableProperty] public partial bool Zebra { get; set; }
    [ObservableProperty] public partial bool ShowGrid { get; set; } = AppSettings.Current.ShowGridOnStart;
    /// <summary>Sketch points land on the visible grid lines. Off by default: most CAD work is free placement plus dimensions.</summary>
    [ObservableProperty] public partial bool SketchGridSnap { get; set; }
    [ObservableProperty] public partial bool Ortho { get; set; }
    [ObservableProperty] public partial string? FilePath { get; set; }
    [ObservableProperty] public partial bool Dirty { get; set; }
    [ObservableProperty] public partial Vec3 Cursor3 { get; set; }
    public Vec3? ViewportCursor { get; set; }

    public CadSession() { Current = Root; }

    public bool CanUndo => Current.Parent != null;
    public bool CanRedo => Current.Preferred != null;

    Commands.FeatureRecipe? _pendingRecipe;
    public void Invalidate() => ViewChanged?.Invoke();
    void RaiseScene()
    {
        foreach(var study in Studies)if(study.Results is {SourceBody: {} source} solved && (!ReferenceEquals(Find(study.BodyId),source)||solved.OtherBodies.Any(o=>!ReferenceEquals(Find(o.Id),o)))){study.Results=null;Result=null;StudyChanged?.Invoke();}
        if (Result != null && (Find(Result.BodyId) == null || (Result.BodySnapshot != null && !ReferenceEquals(Find(Result.BodyId), Result.BodySnapshot)) || Result.OtherBodies.Any(o => !ReferenceEquals(Find(o.Id), o)))) { Result = null; StudyChanged?.Invoke(); }
        // A mutation that announced itself becomes a new history node; quiet edits (e.g. visibility) fold into the current one.
        if (!_navigating)
        {
            if (_pendingLabel != null)
            {
                var n = new HistoryNode { Id = _nextNodeId++, Parent = Current, Label = _pendingLabel, Icon = _pendingIcon ?? "box", After = Objects, Parameters = DocumentParameters, Recipe = _pendingRecipe };
                _pendingRecipe = null;
                Current.Children.Add(n); Current.Preferred = n; Current = n;
                _pendingLabel = null;
                TimelineChanged?.Invoke();
            }
            else { Current.After = Objects; Current.Parameters = DocumentParameters; }
        }
        SceneChanged?.Invoke(); ViewChanged?.Invoke();
    }
    public void RaiseState() { StateChanged?.Invoke(); ViewChanged?.Invoke(); }

    public sealed record LogEntry(DateTime Time, bool IsError, string Message);
    public List<LogEntry> Log { get; } = [];
    public event Action? LogChanged;
    public void Say(string msg, bool error = false)
    {
        Status = msg; StatusIsError = error;
        Log.Add(new(DateTime.Now, error, msg));
        if (Log.Count > 200) Log.RemoveRange(0, 100);
        LogChanged?.Invoke();
    }

    public string NextName(string prefix)
    {
        _counters[prefix] = _counters.GetValueOrDefault(prefix) + 1;
        return $"{prefix} {_counters[prefix]}";
    }

    public SceneObject? Find(Guid id) => Objects.FirstOrDefault(o => o.Id == id);

    /// <summary>While an image command is open, the picture is drawn with these values (a live preview before OK).</summary>
    public Dictionary<Guid, (Assembly.ImageInfo info, Plane3 plane)> ImageOverrides { get; } = [];

    // ------------------------------------------------------------ grouped history (a script is one undo step)
    HistoryNode? _groupStart; int _groupDepth;
    public void BeginGroup() { if (_groupDepth++ == 0) _groupStart = Current; }
    /// <summary>Fold every step made since BeginGroup into one history step with this label.</summary>
    public void EndGroup(string label)
    {
        if (_groupDepth == 0) return;
        if (--_groupDepth > 0) return;
        var start = _groupStart; _groupStart = null;
        if (start == null || Current == start || Current.Parent == null) return;
        var first = start.Preferred; if (first == null || first == Current) { if (first != null) first.Label = label; return; }
        var node = new HistoryNode { Id = _nextNodeId++, Parent = start, Label = label, Icon = "script", After = Objects, Parameters = DocumentParameters, DrawingState = Drawings.Serialize() };
        start.Children.Remove(first); start.Children.Add(node); start.Preferred = node; Current = node;
        TimelineChanged?.Invoke();
    }

    /// <summary>Announce that the next scene change is a history step with this label.</summary>
    void Checkpoint(string label = "Edit", string icon = "box") { _pendingLabel = label; _pendingIcon = icon; Dirty = true; }

    /// <summary>Make <paramref name="node"/> the current state. Redo will follow this path afterwards.</summary>
    public void GoTo(HistoryNode node)
    {
        if (node == Current) return;
        if (Sketch != null) { Say("Finish or cancel the sketch before navigating history.", true); return; }
        CancelCommand();
        _navigating = true;
        for (var n = node; n.Parent != null; n = n.Parent) n.Parent.Preferred = n;
        Current = node;
        Objects = node.After;
        DocumentParameters = node.Parameters;
        RestoreDrawings(node);
        Dirty = true;
        PruneSelection();
        RaiseScene();
        _navigating = false;
        TimelineChanged?.Invoke();
        Say(node.IsRoot ? "Back at the start" : $"History: {node.Label}");
    }

    public void Undo()
    {
        if (ActiveCommand?.TryUndo() == true) return;
        if (Sketch?.TryUndo(this) == true) return;
        if (Sketch != null) return;                       // never roll the document back while a sketch is open
        if (Current.Parent is { } p) GoTo(p);
    }
    public void Redo() { if (Current.Preferred is { } n) GoTo(n); }

    /// <summary>Root -> current -> (the branch redo would follow). This is what the timeline bar shows.</summary>
    public (List<HistoryNode> Past, List<HistoryNode> Future) HistoryPath()
    {
        var past = new List<HistoryNode>();
        for (var n = Current; n != null; n = n.Parent) past.Add(n);
        past.Reverse();
        var future = new List<HistoryNode>();
        for (var n = Current.Preferred; n != null; n = n.Preferred) future.Add(n);
        return (past, future);
    }

    public IEnumerable<HistoryNode> AllNodes()
    {
        var stack = new Stack<HistoryNode>(); stack.Push(Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop(); yield return n;
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }

    public void RaiseTimeline() => TimelineChanged?.Invoke();

    /// <summary>Replace the whole selection with these items (objects and/or origin references).</summary>
    public void SelectItems(IEnumerable<SelItem> items)
    {
        var list = items.ToList();
        if (list.SequenceEqual(_selection)) return;
        _selection.Clear(); _selection.AddRange(list.Distinct());
        SelectionChanged?.Invoke(); Invalidate();
    }

    public void ZoomToObjects(IEnumerable<Guid> ids)
    {
        var objs = ids.Select(Find).Where(o => o != null && o.Visible).ToList();
        if (objs.Count == 0) return;
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var o in objs)
        {
            var m = o!.Mesh;
            min = new(Math.Min(min.X, m.Min.X), Math.Min(min.Y, m.Min.Y), Math.Min(min.Z, m.Min.Z));
            max = new(Math.Max(max.X, m.Max.X), Math.Max(max.Y, m.Max.Y), Math.Max(max.Z, m.Max.Z));
        }
        Camera.Fit(min, max); Invalidate();
    }

    public void PinCurrent(string name) { Current.Pin = string.IsNullOrWhiteSpace(name) ? null : name.Trim(); TimelineChanged?.Invoke(); }

    public void BranchHistory(string name)
    {
        if (Sketch != null || ActiveCommand != null) { Say("Finish the active operation before starting an alternative.", true); return; }
        if (string.IsNullOrWhiteSpace(name)) return;
        Checkpoint("Branch: " + name.Trim(), "new");
        RaiseScene(); Current.Pin = name.Trim(); TimelineChanged?.Invoke();
        Say($"Working on {name.Trim()}. Other continuations remain in design history.");
    }

    /// <summary>Selective rollback: bring one object back to how it was at <paramref name="node"/>, leaving everything else as is.</summary>
    public void RestoreObject(HistoryNode node, Guid id)
    {
        var old = node.After.FirstOrDefault(o => o.Id == id) ?? throw new InvalidOperationException("That object did not exist at this step.");
        Checkpoint($"Restore {old.Name}", "undo");
        int i = Objects.FindIndex(o => o.Id == id);
        Objects = i >= 0 ? Objects.SetItem(i, old) : Objects.Add(old);
        RaiseScene();
    }

    public void NewDocument()
    {
        CancelCommand(); ExitSketch(false);
        DocumentParameters = []; RebuildIssue = null; PartInfo = new(); PartInfoChanged?.Invoke();
        Objects = []; _nextNodeId = 1; Root = new HistoryNode { Id = 0, Label = "Start", Icon = "new" }; Current = Root; _pendingLabel = null;
        _counters.Clear(); ClearSelection(); ResetSimulation(); LoadRenderState(null, null);
        DataSheets.Clear(); DataChanged?.Invoke();
        Drawings = new(); _drawingRevisions.Clear(); DrawingsChanged?.Invoke();
        FilePath = null; Dirty = false; RaiseScene(); TimelineChanged?.Invoke(); Say("New document");
    }
    // ------------------------------------------------------------ mutation
    public SceneObject AddObject(Shape? shape, string namePrefix, ObjKind? kind = null, uint? color = null, Plane3? plane = null, bool checkpoint = true, Form.SubdMesh? form = null, Sketching.SketchModel? sketch = null)
    {
        var k = kind ?? (form != null ? ObjKind.Form : SceneObject.Classify(shape!));
        if (checkpoint) Checkpoint(k == ObjKind.Sketch ? "Sketch" : $"Create {namePrefix}", k == ObjKind.Sketch ? "sketch" : IconForKind(k));
        uint c = color ?? Palette.DefaultFor(k, Objects.Count(o => o.Kind == ObjKind.Solid));
        var obj = new SceneObject(Guid.NewGuid(), NextName(namePrefix), shape, k, c, true, plane, form, sketch);
        Objects = Objects.Add(obj);
        RaiseScene();
        return obj;
    }

    static string IconForKind(ObjKind k) => k switch { ObjKind.Mesh => "mesh", ObjKind.Surface => "nurbs-patch", ObjKind.Curve => "curve-through", ObjKind.Form => "form-box", ObjKind.Plane => "origin-plane", _ => "box" };

    public void Apply(CommandOutput output)
    {
        Checkpoint(output.Label ?? "Edit", output.Icon ?? "box");
        var list = Objects;
        foreach (var (old, nu) in output.Replace)
        {
            if (output.ReplaceIsMove && old.Component != null) continue;
            int i = list.FindIndex(o => o.Id == old.Id);
            if (i >= 0) list = list.SetItem(i, old.WithShape(nu));
        }
        foreach (var (old, form) in output.ReplaceForms)
        {
            int i = list.FindIndex(o => o.Id == old.Id);
            if (i >= 0) list = list.SetItem(i, old.WithForm(form));
        }
        foreach (var (old, tri) in output.ReplaceMeshes)
        {
            int i = list.FindIndex(o => o.Id == old.Id);
            if (i >= 0) list = list.SetItem(i, old.WithTri(tri));
        }
        foreach (var r in output.Remove) list = list.RemoveAll(o => o.Id == r.Id);
        var created = new List<SceneObject>();
        foreach (var n in output.Add)
        {
            var k = n.Tri != null ? ObjKind.Mesh : n.Kind ?? (n.Form != null ? ObjKind.Form : SceneObject.Classify(n.Shape!));
            uint c = n.Color ?? Palette.DefaultFor(k, list.Count(o => o.Kind == ObjKind.Solid));
            var obj = new SceneObject(Guid.NewGuid(), NextName(n.NamePrefix), n.Shape, k, c, true, n.Plane, n.Form, null, n.Look, n.Tri, n.Asm) { FlatMap=n.FlatMap };
            list = list.Add(obj); created.Add(obj);
        }
        Objects = list;
        if (created.Count > 0) { _selection.Clear(); foreach (var o in created) _selection.Add(SelItem.Of(o)); }
        PruneSelection();
        RaiseScene();
        SelectionChanged?.Invoke();
    }

    public void Update(Guid id, Func<SceneObject, SceneObject> change, bool checkpoint = true, string label = "Edit properties")
    {
        int i = Objects.FindIndex(o => o.Id == id);
        if (i < 0) return;
        if (checkpoint) Checkpoint(label, "sel-object");
        Objects = Objects.SetItem(i, change(Objects[i]));
        RaiseScene();
    }

    public void DeleteSelection()
    {
        // faces of a solid are selected: Delete removes the feature they belong to (a hole, slot, boss, fillet...), never the whole body
        var faces = _selection.Where(i => i.Kind == SelKind.Face).ToList();
        if (faces.Count > 0 && ActiveCommand == null && Sketch == null && faces.All(f => Find(f.Obj) is { Kind: ObjKind.Solid, HasBrep: true }))
        {
            var c = new DeleteFaceCommand { Factory = () => new DeleteFaceCommand() };
            StartCommand(c);
            if (ActiveCommand != c) return;
            c.ConfigureInputs(() => { foreach (var f in faces) c.Slots[0].TryAdd(f); });
            if (c.TryBuild() == null) { Say(c.Error ?? "Those faces cannot be removed on their own.", true); CancelCommand(); return; }
            CommitCommand(); ClearSelection();
            return;
        }
        var ids = _selection.Select(s => s.Obj).Distinct().ToHashSet();
        if (ids.Count == 0) return;
        Checkpoint("Delete", "delete");
        Objects = Objects.RemoveAll(o => ids.Contains(o.Id));
        ClearSelection();
        RaiseScene(); Say($"Deleted {ids.Count} object(s)");
    }

    public void Duplicate()
    {
        var objs = _selection.Select(s => Find(s.Obj)).Where(o => o != null).Distinct().ToList();
        if (objs.Count == 0) return;
        Checkpoint("Duplicate", "duplicate");
        var copies = new List<SceneObject>();
        foreach (var o in objs)
        {
            var name = NextName(StripNumber(o!.Name));
            var c = o.Tri != null
                ? new SceneObject(Guid.NewGuid(), name, null, ObjKind.Mesh, o.Color, true, null, null, null, o.Look, o.Tri)
                : o.Form != null
                ? new SceneObject(Guid.NewGuid(), name, null, ObjKind.Form, o.Color, true, null, o.Form.Clone(), null, o.Look)
                : new SceneObject(Guid.NewGuid(), name, Occ.Translate(o.Shape, Vec3.Zero), o.Kind, o.Color, true, o.SketchPlane, null, null, o.Look);
            Objects = Objects.Add(c); copies.Add(c);
        }
        _selection.Clear(); foreach (var c in copies) _selection.Add(SelItem.Of(c));
        RaiseScene(); SelectionChanged?.Invoke(); Say($"Duplicated {copies.Count} object(s)");
    }

    static string StripNumber(string n) { int i = n.LastIndexOf(' '); return i > 0 && int.TryParse(n[(i + 1)..], out _) ? n[..i] : n; }
    // ------------------------------------------------------------ selection
    readonly List<SelItem> _selection = [];
    public IReadOnlyList<SelItem> Selection => _selection;
    [ObservableProperty] public partial SelMode Mode { get; set; } = SelMode.Object;
    [ObservableProperty] public partial SelItem? Hover { get; set; }
    public event Action? SelectionChanged;

    partial void OnModeChanged(SelMode value) { ClearSelection(); RaiseState(); }

    public void ClearSelection() { if (_selection.Count == 0) return; _selection.Clear(); SelectionChanged?.Invoke(); Invalidate(); }
    public void SelectObjects(IEnumerable<Guid> ids, bool additive = false)
    {
        if (!additive) _selection.Clear();
        foreach (var id in ids) { var it = new SelItem(id, SelKind.Object, -1); if (!_selection.Contains(it)) _selection.Add(it); }
        SelectionChanged?.Invoke(); Invalidate();
    }
    public void SetSelection(SelItem item, bool additive)
    {
        if (additive) { if (!_selection.Remove(item)) _selection.Add(item); }
        else { _selection.Clear(); _selection.Add(item); }
        SelectionChanged?.Invoke(); Invalidate();
    }
    void PruneSelection()
    {
        int n = _selection.RemoveAll(s => s.Kind != SelKind.Origin && Find(s.Obj) == null);
        if (n > 0) SelectionChanged?.Invoke();
    }

    public IEnumerable<SceneObject> SelectedObjects => _selection.Select(s => Find(s.Obj)).Where(o => o != null).Distinct()!;

    public Sketching.SketchDatum? DatumOf(SelItem item) => item.Kind==SelKind.Datum?Find(item.Obj)?.SketchData?.Datums.ElementAtOrDefault(item.Index):null;

    public bool Matches(SelItem it, SelFilter f)
    {
        switch (it.Kind)
        {
            case SelKind.Datum: return DatumOf(it) is { } datum && (datum.Kind==Sketching.SketchDatumKind.Axis?f.HasFlag(SelFilter.Axis):datum.Kind==Sketching.SketchDatumKind.Plane&&f.HasFlag(SelFilter.Plane));
            case SelKind.Face: return f.HasFlag(SelFilter.Face);
            case SelKind.Edge: return f.HasFlag(SelFilter.Edge);
            case SelKind.Origin: return it.Index < 3 ? f.HasFlag(SelFilter.Plane) : it.Index < 6 && f.HasFlag(SelFilter.Axis);
            default:
                var o = Find(it.Obj);
                if (o == null) return false;
                return o.Kind switch
                {
                    ObjKind.Solid => f.HasFlag(SelFilter.Solid),
                    ObjKind.Mesh => f.HasFlag(SelFilter.MeshObj),
                    ObjKind.Surface => f.HasFlag(SelFilter.SurfaceObj),
                    ObjKind.Curve => f.HasFlag(SelFilter.CurveObj),
                    ObjKind.Form => f.HasFlag(SelFilter.Form),
                    ObjKind.Plane => f.HasFlag(SelFilter.Plane),
                    ObjKind.Joint or ObjKind.Animation or ObjKind.Toolpath => false,
                    _ => f.HasFlag(SelFilter.SketchObj),
                };
        }
    }

    public Shape? ShapeOf(SelItem it)
    {
        if (it.Kind == SelKind.Origin) return null;
        var o = Find(it.Obj);
        if (o == null || o.Tri != null) return null;
        return it.Kind switch
        {
            SelKind.Face => o.Shape.Sub(SubKind.Face, it.Index),
            SelKind.Edge => o.Shape.Sub(SubKind.Edge, it.Index),
            _ => o.Shape,
        };
    }

    public string Describe(SelItem it)
    {
        if(DatumOf(it) is { } datum)return datum.Name;
        if (it.Kind == SelKind.Origin) return OriginNames[Math.Clamp(it.Index, 0, 6)];
        var o = Find(it.Obj);
        if (o == null) return "?";
        return it.Kind switch { SelKind.Face => $"Face {it.Index + 1} of {o.Name}", SelKind.Edge => $"Edge {it.Index + 1} of {o.Name}", _ => o.Name };
    }

    /// <summary>Polyline of a picked edge (from the tessellation), in order.</summary>
    public List<Vec3> EdgePoints(SelItem it)
    {
        var res = new List<Vec3>();
        var o = Find(it.Obj); if (o == null) return res;
        var m = o.Mesh; var E = m.EdgePoints;
        if (it.Index + 1 >= m.EdgeSegStart.Length) return res;
        for (int s = m.EdgeSegStart[it.Index]; s < m.EdgeSegStart[it.Index + 1]; s++)
        {
            if (res.Count == 0) res.Add(new(E[6 * s], E[6 * s + 1], E[6 * s + 2]));
            res.Add(new(E[6 * s + 3], E[6 * s + 4], E[6 * s + 5]));
        }
        return res;
    }

    /// <summary>Average normal of a face from its tessellation, plus a point on it.</summary>
    public (Vec3 point, Vec3 normal)? FaceFrame(SelItem it)
    {
        var o = Find(it.Obj); if (o == null) return null;
        var m = o.Mesh;
        if (it.Index + 1 >= m.FaceTriStart.Length || m.FaceTriStart[it.Index] == m.FaceTriStart[it.Index + 1]) return null;
        int t = m.FaceTriStart[it.Index];
        int a = m.Indices[3 * t] * 3;
        var p = new Vec3(m.Positions[a], m.Positions[a + 1], m.Positions[a + 2]);
        var n = new Vec3(m.Normals[a], m.Normals[a + 1], m.Normals[a + 2]);
        return (p, n.Normalized());
    }

    // ------------------------------------------------------------ hover / picking
    public SelFilter ActiveFilter
    {
        get
        {
            if (ActiveCommand?.ActiveSlot is { } slot) return slot.PickFilter;
            return Mode switch { SelMode.Face => SelFilter.Face, SelMode.Edge => SelFilter.Edge, _ => SelFilter.AnyObject | SelFilter.Plane | SelFilter.Axis };
        }
    }

    /// <summary>Origin planes/axes are drawn when switched on in the browser, or while a command is asking for one.</summary>
    public bool OriginShown(int i)
    {
        if (OriginVisible[i]) return true;
        if (!ReferenceEquals(_suppCmd, ActiveCommand)) { _suppCmd = ActiveCommand; _originSuppressed.Clear(); }
        if (_originSuppressed.Contains(i)) return false;
        var f = ActiveCommand?.ActiveSlot?.PickFilter ?? SelFilter.None;
        return (i < 3 && f.HasFlag(SelFilter.Plane)) || (i is >= 3 and < 6 && f.HasFlag(SelFilter.Axis));
    }

    public double OriginSize()
    {
        double r = 0;
        foreach (var o in Objects) { if (!o.Visible || o.IsAnnotation) continue; var m = o.Mesh; r = Math.Max(r, Math.Max((m.Max - Vec3.Zero).Length, (m.Min - Vec3.Zero).Length)); }
        return Math.Max(r * 0.85, 60);
    }

    PickResult? PickOrigin(double px, double py, SelFilter filter)
    {
        var cam = Camera; double size = OriginSize();
        var (o, d) = cam.Ray(px, py);
        PickResult? best = null;
        if (filter.HasFlag(SelFilter.Plane))
            for (int i = 0; i < 3; i++)
            {
                if (!OriginShown(i)) continue;
                var pl = OriginPlane(i);
                if (pl.Intersect(o, d) is not { } hit) continue;
                var (u, v) = pl.Project(hit);
                if (Math.Abs(u) > size || Math.Abs(v) > size) continue;
                double depth = cam.Project(hit)?.depth ?? 1;
                if (best == null || depth < best.Value.Depth) best = new PickResult(new SelItem(Guid.Empty, SelKind.Origin, i), hit, pl.Normal, depth);
            }
        if (filter.HasFlag(SelFilter.Axis))
            for (int i = 3; i < 6; i++)
            {
                if (!OriginShown(i)) continue;
                var (ao, ad) = OriginAxis(i);
                var pa = cam.Project(ao - ad * size); var pb = cam.Project(ao + ad * size);
                if (pa == null || pb == null) continue;
                double dx = pb.Value.x - pa.Value.x, dy = pb.Value.y - pa.Value.y, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-9 ? 0 : Math.Clamp(((px - pa.Value.x) * dx + (py - pa.Value.y) * dy) / l2, 0, 1);
                double qx = pa.Value.x + dx * t - px, qy = pa.Value.y + dy * t - py;
                if (Math.Sqrt(qx * qx + qy * qy) < 8)
                {
                    double depth = (cam.Project(ao)?.depth ?? 1) - 0.01;   // axes win over planes they lie in
                    if (best == null || depth < best.Value.Depth) best = new PickResult(new SelItem(Guid.Empty, SelKind.Origin, i), ao, Vec3.UnitZ, depth);
                }
            }
        return best;
    }

    /// <summary>Half the width and height of a construction plane's drawn rectangle.</summary>
    public static (double hu, double hv) PlaneHalfSize(SceneObject o)
    {
        var pl = o.SketchPlane ?? Plane3.XY; var m = o.Mesh; double hu = 1, hv = 1;
        for (int i = 0; i + 2 < m.EdgePoints.Length; i += 3)
        {
            var (u, v) = pl.Project(new Vec3(m.EdgePoints[i], m.EdgePoints[i + 1], m.EdgePoints[i + 2]));
            hu = Math.Max(hu, Math.Abs(u)); hv = Math.Max(hv, Math.Abs(v));
        }
        return (hu, hv);
    }

    PickResult? PickPlaneObjects(double px, double py, SelFilter filter)
    {
        if (!filter.HasFlag(SelFilter.Plane)) return null;
        var cam = Camera; var (o, d) = cam.Ray(px, py);
        PickResult? best = null;
        foreach (var obj in Objects)
        {
            if (obj.Kind != ObjKind.Plane || !obj.Visible || HiddenIds.Contains(obj.Id) || obj.SketchPlane is not { } pl) continue;
            if (pl.Intersect(o, d) is not { } hit) continue;
            var (u, v) = pl.Project(hit); var (hu, hv) = PlaneHalfSize(obj);
            if (Math.Abs(u) > hu || Math.Abs(v) > hv) continue;
            double depth = cam.Project(hit)?.depth ?? 1;
            if (best == null || depth < best.Value.Depth) best = new PickResult(SelItem.Of(obj), hit, pl.Normal, depth);
        }
        return best;
    }

    /// <summary>The plane behind a selected origin plane, construction plane, flat face or sketch (null for anything else).</summary>
    public Plane3? PlaneOf(SelItem it)
    {
        if(it.Kind==SelKind.Datum)return DatumOf(it) is { Kind:Sketching.SketchDatumKind.Plane } datum?datum.Frame:null;
        if (it.Kind == SelKind.Origin) return it.Index < 3 ? OriginPlane(it.Index) : null;
        var o = Find(it.Obj); if (o == null) return null;
        if (it.Kind == SelKind.Object && o.Kind is ObjKind.Plane or ObjKind.Sketch) return o.SketchPlane;
        if (it.Kind == SelKind.Edge || it.Kind == SelKind.Object && o.Kind == ObjKind.Curve)
        {
            var mesh = o.Mesh.EdgePoints;
            var points = it.Kind == SelKind.Edge ? EdgePoints(it) : Enumerable.Range(0, mesh.Length / 3).Select(i => new Vec3(mesh[i * 3], mesh[i * 3 + 1], mesh[i * 3 + 2])).ToList();
            if (points.Count < 3) return null;
            var origin = points[0]; var u = points.OrderByDescending(p => p.DistanceTo(origin)).First() - origin;
            if (u.Length < 1e-9) return null;
            var cross = points.Select(p => Vec3.Cross(u, p - origin)).OrderByDescending(n => n.Length).First();
            if (cross.Length < u.Length * 1e-7) return null; // a straight line has no unique normal
            var normal = cross.Normalized();
            if (points.Any(p => Math.Abs(Vec3.Dot(p - origin, normal)) > Math.Max(1e-5, u.Length * 1e-6))) return null;
            var center = points.Aggregate(Vec3.Zero, (sum, p) => sum + p) / points.Count;
            return new(center, u.Normalized(), Vec3.Cross(normal, u.Normalized()));
        }
        if (it.Kind == SelKind.Face && FaceFrame(it) is { } f)
        {
            var n = f.normal;
            var u = Math.Abs(n.Z) < 0.99 ? Vec3.Cross(Vec3.UnitZ, n).Normalized() : Vec3.UnitX;
            return new Plane3(FaceMiddle(it) ?? f.point, u, Vec3.Cross(n, u));      // centred on the face, so planes made from it appear where the face is
        }
        return null;
    }

    /// <summary>The middle of a face (centre of its bounding box on the surface), or null if the face has no mesh.</summary>
    public Vec3? FaceMiddle(SelItem it)
    {
        var o = Find(it.Obj); if (o == null) return null;
        var m = o.Mesh;
        if (it.Index + 1 >= m.FaceTriStart.Length) return null;
        int t0 = m.FaceTriStart[it.Index], t1 = m.FaceTriStart[it.Index + 1];
        if (t1 <= t0) return null;
        Vec3 lo = new(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
        for (int t = t0; t < t1; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.Indices[3 * t + k] * 3;
                lo = new Vec3(Math.Min(lo.X, m.Positions[a]), Math.Min(lo.Y, m.Positions[a + 1]), Math.Min(lo.Z, m.Positions[a + 2]));
                hi = new Vec3(Math.Max(hi.X, m.Positions[a]), Math.Max(hi.Y, m.Positions[a + 1]), Math.Max(hi.Z, m.Positions[a + 2]));
            }
        var c = (lo + hi) * 0.5;
        if (FaceFrame(it) is { } fr) c -= fr.normal * Vec3.Dot(c - fr.point, fr.normal);      // keep it on the face plane
        return c;
    }

    // ------------------------------------------------------------ look at (normal to a plane)
    /// <summary>Turn the main view to look straight at the plane: the sketch being drawn, the command's work plane, or the selected plane / face / sketch.</summary>
    public bool LookAtSelection()
    {
        if (Sketch is { } sketch) { sketch.LookNormal(this); return true; }
        Plane3? target = Sketch?.Plane ?? ActiveCommand?.WorkPlane;
        if (target == null) foreach (var it in Selection) { target = PlaneOf(it); if (target != null) break; }
        if (target is not { } pl) { Say("Select a face, plane or sketch first, then press N to look straight at it.", true); return false; }
        LookAt(pl); Say("Looking normal to the plane  (press N again to look from the other side)");
        return true;
    }

    public void LookAt(Plane3 pl)
    {
        var cam = Cameras[0]; var n = pl.Normal;
        var cur = cam.EyeDirection;
        // already looking straight at it from this side? then flip to the back
        bool alreadyFront = Math.Abs(Vec3.Dot(cur, n)) > 0.9995;
        if (alreadyFront ? Vec3.Dot(cur, n) > 0 : Vec3.Dot(cur, n) < 0) n = -n;
        double pitch = Math.Asin(Math.Clamp(n.Z, -1, 1));
        double yaw = Math.Abs(n.Z) < 0.9999 ? Math.Atan2(n.X, -n.Y) : cam.Yaw;
        Camera = cam;
        AnimateView(yaw, pitch);
    }

    public PickResult? Pick(double px, double py, SelFilter filter)
    {
        var cam = Camera;
        bool wantEdges = filter.HasFlag(SelFilter.Edge) || (filter & SelFilter.AnyCurve) != 0;
        bool wantFaces = filter.HasFlag(SelFilter.Face) || filter.HasFlag(SelFilter.MeshObj) || filter.HasFlag(SelFilter.Solid) || filter.HasFlag(SelFilter.SurfaceObj) || filter.HasFlag(SelFilter.Form);
        // while a command is collecting picks its preview hides the original bodies; they stay pickable so more edges, faces or spots can be added
        bool picking = ActiveCommand?.ActiveSlot != null;
        var vis = Objects.Where(o => picking || !HiddenIds.Contains(o.Id)).ToList();

        var face = wantFaces ? Picking.RayFace(cam, vis, px, py, o => FaceEligible(o, filter)) : null;
        PickResult? fr = null;
        if (face is { } f)
        {
            var item = filter.HasFlag(SelFilter.Face) ? new SelItem(f.obj.Id, SelKind.Face, f.face) : SelItem.Of(f.obj);
            double depth = cam.Project(f.point)?.depth ?? 1;
            fr = new PickResult(item, f.point, f.normal, depth);
        }
        PickResult? er = null;
        if (wantEdges)
        {
            var e = Picking.NearestEdge(cam, vis, px, py, Picking.EdgeTolerancePx, o => EdgeEligible(o, filter));
            if (e is { } ee)
            {
                bool curveObj = (ee.obj.Kind == ObjKind.Curve && filter.HasFlag(SelFilter.CurveObj)) || (ee.obj.Kind == ObjKind.Sketch && filter.HasFlag(SelFilter.SketchObj));
                var item = curveObj ? SelItem.Of(ee.obj) : new SelItem(ee.obj.Id, SelKind.Edge, ee.edge);
                er = new PickResult(item, ee.point, Vec3.UnitZ, ee.depth);
            }
        }
        PickResult? best2 = fr;
        if (er != null && (fr == null || er.Value.Depth <= fr.Value.Depth + 2e-4)) best2 = er;
        foreach(var obj in Objects.Where(o=>o.Visible&&!HiddenIds.Contains(o.Id)&&o.SketchData!=null))
        for(int i=0;i<obj.SketchData!.Datums.Count;i++)
        {
            var datum=obj.SketchData.Datums[i];var item=new SelItem(obj.Id,SelKind.Datum,i);if(!Matches(item,filter))continue;
            var frame=datum.Frame;double extent=cam.PixelSizeAt(frame.Origin)*50*datum.Scale;Vec3? point=null;
            if(datum.Kind==Sketching.SketchDatumKind.Axis)
            {
                var a=cam.Project(frame.Origin-frame.U*extent);var b=cam.Project(frame.Origin+frame.U*extent);
                if(a!=null&&b!=null&&Picking.SegDist(px,py,a.Value.x,a.Value.y,b.Value.x,b.Value.y).Item2<10)point=frame.Origin;
            }
            else {var(rayOrigin,rayDirection)=cam.Ray(px,py);var hit=frame.Intersect(rayOrigin,rayDirection);if(hit is { } q){var uv=frame.Project(q);if(Math.Abs(uv.u)<=extent&&Math.Abs(uv.v)<=extent)point=q;}}
            if(point is { } target&&cam.Project(target) is { } projected&&(best2==null||projected.depth<=best2.Value.Depth+2e-4))best2=new PickResult(item,target,frame.Normal,projected.depth);
        }
        var org = PickOrigin(px, py, filter);
        if (org != null && (best2 == null || org.Value.Depth < best2.Value.Depth)) best2 = org;
        var cp = PickPlaneObjects(px, py, filter);
        if (cp != null && (best2 == null || cp.Value.Depth < best2.Value.Depth)) best2 = cp;
        return best2;
    }

    static bool FaceEligible(SceneObject o, SelFilter f) =>
        !o.IsAnnotation && f.HasFlag(SelFilter.Face) || (o.Kind == ObjKind.Solid && f.HasFlag(SelFilter.Solid)) || (o.Kind == ObjKind.Surface && f.HasFlag(SelFilter.SurfaceObj)) || (o.Kind == ObjKind.Form && f.HasFlag(SelFilter.Form)) || (o.Kind == ObjKind.Mesh && f.HasFlag(SelFilter.MeshObj));
    static bool EdgeEligible(SceneObject o, SelFilter f) => o.Kind switch
    {
        ObjKind.Curve => f.HasFlag(SelFilter.CurveObj) || f.HasFlag(SelFilter.Edge),
        ObjKind.Sketch => f.HasFlag(SelFilter.SketchObj) || f.HasFlag(SelFilter.Edge),
        ObjKind.Plane or ObjKind.Joint or ObjKind.Animation or ObjKind.Toolpath => false,
        _ => f.HasFlag(SelFilter.Edge),
    };

    public void UpdateHover(double px, double py)
    {
        if (ActiveTool != null && ActiveTool is not null && ToolOwnsPointer) { if (Hover != null) { Hover = null; Invalidate(); } return; }
        var r = Pick(px, py, ActiveFilter);
        var h = r?.Item;
        if (h != Hover) { Hover = h; _hoverPreviewFor = null; HoverPreview = []; Invalidate(); }
        UpdateHoverPreview(h);
    }

    /// <summary>While a direct tool's smart-pick slot is active, the other faces a click on the hovered face would also select (drawn lighter than the hover).</summary>
    public IReadOnlyList<SelItem> HoverPreview { get; private set; } = [];
    SelItem? _hoverPreviewFor;

    void UpdateHoverPreview(SelItem? h)
    {
        if (h == _hoverPreviewFor) return;
        _hoverPreviewFor = h;
        var faces = new List<SelItem>();
        if (h is { Kind: SelKind.Face } it && ActiveCommand?.ActiveSlot is { Multi: true, SmartPick: true } slot && Matches(it, slot.PickFilter)
            && Find(it.Obj) is { Kind: ObjKind.Solid, HasBrep: true } body)
        {
            try { faces = FeatureFinder.Expand(body, it.Index, slot.Intent).Where(i => i != it.Index).Select(i => new SelItem(it.Obj, SelKind.Face, i)).ToList(); }
            catch (Exception) { faces = []; }
        }
        if (faces.Count == 0 && HoverPreview.Count == 0) return;
        HoverPreview = faces; Invalidate();
    }

    // ------------------------------------------------------------ commands
    public Command? ActiveCommand { get; private set; }
    public List<PreviewItem> Preview { get; } = [];

    /// <summary>Dimmed, untouchable shapes shown with this document: the assembly around a part being edited from inside it.</summary>
    public IReadOnlyList<PreviewItem> ContextItems { get; private set; } = [];
    public void SetContext(IEnumerable<PreviewItem> items) { ContextItems = [.. items]; Invalidate(); }
    public HashSet<Guid> HiddenIds { get; } = [];
    [ObservableProperty] public partial string? CommandError { get; set; }

    public void StartCommand(Func<Command> make) { var c = make(); c.Factory = make; CommandCatalog.Register(make); StartCommand(c); }

    /// <summary>
    /// Set by the app. Switching away from a preview, changed inputs or a temporary options page
    /// parks the request until the user finishes, discards or keeps editing. Scripts may leave this off.
    /// </summary>
    public bool ProtectPendingCommand { get; set; }
    /// <summary>True while the Direct Modeling tab is open: edits made there are not recorded as history unless the body is tracked.</summary>
    public bool DirectEditing { get; set; }
    Action? _deferred;
    bool _pendingStepOnly;
    /// <summary>Plain-words explanation shown over the viewport while a request waits on an unfinished command; null when nothing waits.</summary>
    [ObservableProperty] public partial string? PendingNotice { get; set; }
    /// <summary>The editor or temporary page that the pending choice will finish or discard.</summary>
    [ObservableProperty] public partial string PendingTitle { get; set; } = "";

    Command? _suspendedCmd;

    /// <summary>Park the active command (its picks and values stay) while another command runs; when that command commits or is cancelled the parked one resumes. Newly created objects are selected on return.</summary>
    public void SuspendFor(Func<Command> make)
    {
        var c = ActiveCommand; if (c == null) return;
        var next = make(); next.Factory = make; CommandCatalog.Register(make);
        _suspendedCmd = c;
        ActiveCommand = null;
        c.OnEnd();
        ClearPreview(); Hover = null; _handleDrag = false;
        StartCommand(next);
        if (!ReferenceEquals(ActiveCommand, next)) _suspendedCmd = null;   // the hand-off was refused
    }

    void ResumeSuspended()
    {
        var c = _suspendedCmd; _suspendedCmd = null;
        if (c != null) StartCommand(c);
    }

    public void StartCommand(Command cmd)
    {
        if(ProtectPendingCommand && ActiveCommand is { } current && CommandCatalog.KeyOf(current)==CommandCatalog.KeyOf(cmd))
        {
            KeepPending();Say($"Continue {current.DisplayTitle} with its current options.");RaiseState();
            return;
        }
        if(DeferToolChange(cmd.Title,()=>StartCommand(cmd)))return;
        ClearPendingNotice();
        if (ActiveCommand != null) CancelCommand();
        if (Sketch != null) { Say("Finish or cancel the sketch first.", true); return; }
        ActiveCommand = cmd;
        cmd.Begin(this);
        Say(cmd.Hint.Length > 0 ? cmd.Hint : cmd.Title);
        RaiseState();
    }

    void ClearPendingNotice() { _deferred = null; _pendingStepOnly=false; PendingNotice = null; }

    bool DeferToolChange(string nextTitle,Action next,bool stepOnly=false)
    {
        if(!ProtectPendingCommand || ActiveCommand is not { } current || !(current.HasPanelStep || current.HasInputChanges || Preview.Count>0))return false;
        _deferred=next;_pendingStepOnly=stepOnly;PendingTitle=stepOnly?current.DisplayTitle:current.Title;
        PendingNotice=stepOnly
            ? $"Finish or discard {current.DisplayTitle} before opening {nextTitle}. {current.Title} will stay open."
            : current.HasPanelStep
            ? $"Open {nextTitle}? Finish or discard the whole {current.Title} session, including the open {current.DisplayTitle} changes, or keep editing."
            : $"{current.DisplayTitle} has unfinished work. Finish it or discard it before opening {nextTitle}, or keep editing.";
        Say(PendingNotice);return true;
    }

    internal void RequestPanelStep(string title,Action open)
    {
        if(DeferToolChange(title,open,true))return;
        ActiveCommand?.FinishPanelStep(false);open();
    }

    /// <summary>Finish or discard the explicitly named editor/page, then open the requested tool.</summary>
    public void ResolvePending(bool create)
    {
        var next = _deferred;bool stepOnly=_pendingStepOnly;
        if (ActiveCommand == null) { ClearPendingNotice(); return; }
        if(stepOnly){ActiveCommand.FinishPanelStep(create);ClearPendingNotice();next?.Invoke();return;}
        if (create)
        {
            var before = ActiveCommand;
            before.FinishPanelStep(true);
            CommitCommand();
            if (ActiveCommand == before) return;       // it could not be built: the reason is on the status line and the notice stays
        }
        else CancelCommand();
        ClearPendingNotice();
        next?.Invoke();
    }

    /// <summary>Close the notice and keep working on the unfinished command.</summary>
    public void KeepPending() => ClearPendingNotice();

    public void CancelCommand()
    {
        if (ActiveCommand == null) return;
        ClearPendingNotice();
        var c = ActiveCommand; ActiveCommand = null;
        _suspendedCmd = null;
        c.OnEnd(); c.OnCancel();
        ClearPreview();
        Hover = null;
        if (c.EditMode && c.ReturnNode != null) GoTo(c.ReturnNode);          // an abandoned edit puts the model back where it was
        RaiseState();
    }

    public void AcceptCommandPanel() { KeepPending();if (ActiveCommand?.FinishPanelStep(true) != true) CommitCommand(); }
    public void CancelCommandPanel()
    {
        KeepPending();
        if (ActiveCommand?.FinishPanelStep(false) != true)
        {
            var parked = _suspendedCmd;
            CancelCommand();
            if (parked != null) StartCommand(parked);
        }
    }

    public void CommitCommand()
    {
        var c = ActiveCommand; if (c == null) return;
        c.Committing = true;
        CommandOutput? res;
        try { res = c.TryBuild(); } finally { c.Committing = false; }
        if (res == null) { Say(c.Error ?? "Complete the required selections first.", true); return; }
        res.Label ??= c.Title; res.Icon ??= c.Icon;
        PendingNotice = null;
        ActiveCommand = null;
        c.OnEnd();
        ClearPreview();
        Hover = null;
        _handleDrag = false;
        // Direct edits are plain undo steps unless the body they change has history tracking turned on.
        bool tracked = res.Replace.Any(r => r.Old.TrackHistory);
        var recipe = c is Commands.DirectEditCommand gizmo ? (tracked ? gizmo.InnerRecipe : null) : DirectEditing && !tracked ? null : c.Capture();
        HistoryNode? edited = c.EditMode ? c.EditingNode : null;
        if (!res.NoOp) { _pendingRecipe = recipe; Apply(res); _pendingRecipe = null; }
        res.After?.Invoke(this);
        ResumeSuspended();
        if (edited != null && !res.NoOp) ReplayAfterEdit(edited, c.Title, c.RebuildEnd ?? c.ReturnNode!, c.RebuildMap);
        else if (edited != null && c.ReturnNode != null) { GoTo(c.ReturnNode); Say($"{c.Title}: no change"); }
        else Say($"{c.Title} complete");
        RaiseState();
    }

    // ------------------------------------------------------------ editing a feature
    static HistoryNode RebuildEndFor(HistoryNode source, HistoryNode current)
    {
        for (var n = current; n != null; n = n.Parent)
            if (n == source && current != source) return current;
        // Editing a step that was revisited from the graph rebuilds its existing continuation.
        var end = source;
        while (end.Preferred != null) end = end.Preferred;
        return end;
    }
    /// <summary>The latest step that made or changed this object with a command that can be run again.</summary>
    public HistoryNode? FeatureNodeFor(Guid objectId)
    {
        var (past, _) = HistoryPath();
        for (int i = past.Count - 1; i > 0; i--)
        {
            var n = past[i];
            if (n.Recipe == null) continue;
            var (added, modified, _) = n.Diff();
            if (added.Any(o => o.Id == objectId) || modified.Any(o => o.Id == objectId)) return n;
        }
        return null;
    }

    /// <summary>Roll the model back to just before a step and reopen its command with the same picks and numbers. OK re-applies it and rebuilds what came after.</summary>
    public bool EditFeature(HistoryNode node)
    {
        if (node.Recipe == null || node.Parent == null) { Say("That step was not made with a command that can be edited.", true); return false; }
        if (Sketch != null) { Say("Finish or cancel the sketch first.", true); return false; }
        if(DeferToolChange("Edit "+node.Label,()=>EditFeature(node)))return false;
        CancelCommand();
        var back = Current;
        var end = RebuildEndFor(node, back);
        GoTo(node.Parent);
        DocumentParameters = end.Parameters;
        var cmd = node.Recipe.Make(); cmd.Factory = node.Recipe.Make;
        cmd.EditMode = true; cmd.EditingNode = node; cmd.ReturnNode = back; cmd.RebuildEnd = end;
        StartCommand(cmd);
        cmd.Restore(node.Recipe);
        Say($"Editing {node.Label}. Change what you like, then OK: the steps after it are rebuilt.");
        return true;
    }

    /// <summary>After an edited step is committed, run the old later steps again on top of it, following the objects that were remade.</summary>
    void ReplayAfterEdit(HistoryNode oldNode, string title, HistoryNode end, Dictionary<Guid, Guid>? existingMap = null)
    {
        var map = existingMap == null ? new Dictionary<Guid, Guid>() : new Dictionary<Guid, Guid>(existingMap);
        void Learn(HistoryNode oldN, HistoryNode newN)
        {
            newN.Pin = oldN.Pin;
            var o = oldN.Diff().Added; var nw = newN.Diff().Added;
            for (int i = 0; i < Math.Min(o.Count, nw.Count); i++)
            {
                map[o[i].Id] = nw[i].Id;
                Update(nw[i].Id, x => x.WithName(o[i].Name).WithColor(o[i].Color).WithLook(o[i].Look), checkpoint: false);       // keep the names and colours you gave them
            }
        }
        Learn(oldNode, Current);
        var chain = new List<HistoryNode>();
        // Rebuild only the path that was actually displayed when editing began.
        // Preferred can point into a future or a different branch after navigation.
        for (var n = end; n != oldNode && n.Parent != null; n = n.Parent) chain.Add(n);
        if (end != oldNode && (chain.Count == 0 || chain[^1].Parent != oldNode)) chain.Clear();
        chain.Reverse();
        int done = 0;
        foreach (var step in chain)
        {
            bool ok;
            string why = "";
            List<SelItem>? failedInputs = null;
            Commands.FeatureRecipe? attemptedRecipe = null;
            if (step.Recipe != null)
            {
                var cmd = step.Recipe.Make(); cmd.Factory = step.Recipe.Make;
                try
                {
                    cmd.Begin(this); cmd.Restore(step.Recipe, map);
                    failedInputs = cmd.Slots.SelectMany(slot => slot.Items).ToList();
                    attemptedRecipe = cmd.Capture();
                    cmd.Committing = true;
                    var res = cmd.TryBuild();
                    cmd.Committing = false;
                    if (res == null) { ok = false; why = cmd.Error ?? "its inputs are no longer valid"; }
                    else
                    {
                        res.Label ??= cmd.Title; res.Icon ??= cmd.Icon;
                        _pendingRecipe = cmd.Capture();
                        Apply(res); _pendingRecipe = null;
                        res.After?.Invoke(this);
                        Learn(step, Current);
                        ok = true;
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException) { ok = false; why = e.Message; }
                finally { _pendingRecipe = null; cmd.Committing = false; cmd.OnEnd(); ClearPreview(); }
            }
            else
            {
                // Replay independent sketch definitions and property edits without replacing
                // regenerated body geometry with a stale snapshot.
                var (added, modified, removed) = step.Diff();
                var before = step.Parent!.After.ToDictionary(o => o.Id);
                bool safe = modified.All(o => o.Kind == ObjKind.Sketch && o.SketchData != null ||
                    before.TryGetValue(o.Id, out var prev) && prev.Form == o.Form && ReferenceEquals(prev.Tri, o.Tri) && (o.Tri != null || o.Form != null || ReferenceEquals(prev.Shape, o.Shape)));
                if (!safe) { ok = false; why = "it changed geometry without an editable feature definition"; }
                else if (!TryReevaluateSketches(added.Concat(modified), out var fresh, out var sketchProblem)) { ok = false; why = sketchProblem; }
                else
                {
                    Checkpoint(step.Label, step.Icon);
                    foreach (var old in modified)
                    {
                        var id = map.GetValueOrDefault(old.Id, old.Id);
                        int index = Objects.FindIndex(o => o.Id == id);
                        if (index < 0) continue;
                        var now = Objects[index]; var prev = before[old.Id];
                        if (old.Kind == ObjKind.Sketch && old.SketchData != null)
                            now = fresh.TryGetValue(old.Id, out var driven) ? now.WithSketch(driven.Shape, driven.Model, old.SketchPlane!.Value)       // formula-driven dimensions follow the parameters
                                : now.WithSketch(old.Shape, old.SketchData.Clone(), old.SketchPlane!.Value);
                        if (old.Name != prev.Name) now = now.WithName(old.Name);
                        if (old.Color != prev.Color) now = now.WithColor(old.Color);
                        if (old.Look != prev.Look) now = now.WithLook(old.Look);
                        if (old.Visible != prev.Visible) now = now.WithVisible(old.Visible);
                        Objects = Objects.SetItem(index, now);
                    }
                    var deleted = step.Parent.After.Where(o => step.After.All(a => a.Id != o.Id)).Select(o => map.GetValueOrDefault(o.Id, o.Id)).ToHashSet();
                    Objects = Objects.RemoveAll(o => deleted.Contains(o.Id));
                    Objects = Objects.AddRange(added.Where(a => Find(a.Id) == null).Select(a => fresh.TryGetValue(a.Id, out var drivenNew) && a.SketchPlane is { } drivenPlane ? a.WithSketch(drivenNew.Shape, drivenNew.Model, drivenPlane) : a));
                    RaiseScene(); Current.Pin = step.Pin; ok = true;
                }
            }
            if (!ok)
            {
                int left = chain.Count - done;
                RebuildIssue = new FeatureRebuildIssue(Current, step, why, failedInputs, attemptedRecipe, end, new(map));
                TimelineChanged?.Invoke(); // Include checkpoint metadata copied after the last successful step.
                Say($"{title} edited, but \"{step.Label}\" could not be repeated ({why}). {left} later step{(left == 1 ? "" : "s")} stayed on the old branch: open the History tree to get them back.", true);
                return;
            }
            done++;
        }
        RebuildIssue = null;
        TimelineChanged?.Invoke();
        Say(done == 0 ? $"{title} edited." : $"{title} edited. {done} later step{(done == 1 ? "" : "s")} rebuilt.");
    }

    void ClearPreview() { Preview.Clear(); HiddenIds.Clear(); CommandError = null; ViewChanged?.Invoke(); }

    public void RefreshPreview(Command cmd)
    {
        if (cmd != ActiveCommand) return;
        Preview.Clear(); HiddenIds.Clear();
        if (cmd.SupportsPreview && !cmd.PreviewEnabled)
        {
            CommandError = null;
            cmd.Results.Clear();
            ViewChanged?.Invoke();
            return;
        }
        var res = cmd.TryBuild();
        CommandError = cmd.Error;
        if (!cmd.TiltDone && cmd.Handle is { } hs) { cmd.TiltDone = true; TiltToHandle(hs); }
        if (res != null)
        {
            foreach (var n in res.Add) Preview.Add(n.Tri != null ? new PreviewItem(n.Tri, n.Color) : n.Form != null ? new PreviewItem(n.Form) : new PreviewItem(n.Shape!, n.Kind ?? SceneObject.Classify(n.Shape!)));
            foreach (var (old, tri) in res.ReplaceMeshes) { HiddenIds.Add(old.Id); Preview.Add(new PreviewItem(tri, old.Color)); }
            foreach (var (old, form) in res.ReplaceForms) { HiddenIds.Add(old.Id); Preview.Add(new PreviewItem(form, old.Color)); }
            foreach (var (old, nu) in res.Replace) { HiddenIds.Add(old.Id); Preview.Add(new PreviewItem(nu, old.Kind == ObjKind.Sketch ? ObjKind.Sketch : SceneObject.Classify(nu), old.Color)); }
            foreach (var r in res.Remove) HiddenIds.Add(r.Id);
            foreach (var gshape in res.Ghosts) Preview.Add(new PreviewItem(gshape, ObjKind.Solid) { Ghost = true });
        }
        ViewChanged?.Invoke();
    }

    // ------------------------------------------------------------ sketch
    public SketchContext? Sketch { get; private set; }
    [ObservableProperty] public partial string SketchStatus { get; set; } = "";
    [ObservableProperty] public partial bool SketchOk { get; set; } = true;
    public event Action? SketchChanged;
    HistoryNode? _sketchEditSource, _sketchReturnNode, _sketchRebuildEnd;

    public void NotifySketchChanged()
    {
        if (Sketch != null) { SketchStatus = Sketch.Result.Status; SketchOk = Sketch.Result.Converged; }
        SketchChanged?.Invoke();
        ViewChanged?.Invoke();
    }

    /// <summary>Ask the user for a short text/number (shown over the viewport). The callback receives null if cancelled.</summary>
    public sealed record InputRequest(string Prompt, string Initial, Action<string?> Done);
    public event Action<InputRequest>? InputRequested;
    public void RequestInput(string prompt, string initial, Action<string?> done) => InputRequested?.Invoke(new InputRequest(prompt, initial, done));

    bool? _orthoBeforeSketch;

    /// <summary>Switch the main view between orthographic and perspective for sketching; the projection from before the sketch is put back when it ends.</summary>
    public void SketchProjection(bool perspective)
    {
        _orthoBeforeSketch ??= Ortho;
        Ortho = !perspective; Camera.Orthographic = !perspective; Invalidate();
    }

    void RestoreProjection()
    {
        if (_orthoBeforeSketch is bool ortho) { Ortho = ortho; Camera.Orthographic = ortho; Invalidate(); }
        _orthoBeforeSketch = null;
    }

    public void EnterSketch(Plane3 plane, Sketching.SketchModel? model = null, Guid? editing = null, bool alignView = true)
    {
        if (Sketch != null) return;
        Sketch = new SketchContext(plane, model, editing);
        Camera.Locked = false;
        // A flat sketch is drawn looking straight at its plane without perspective; a 3D sketch keeps perspective so depth reads.
        if (!Sketch.IsDrawingFlat) SketchProjection(perspective: true);
        else
        {
            SketchProjection(perspective: false);
            if (alignView) { Camera.LookAlong(plane.Normal); Camera.Target = plane.Origin; }
        }
        ClearSelection();
        NotifySketchChanged();
        Sketch.SetTool(this, new SelectTool());
        Say(editing == null ? "Sketch: pick a drawing tool from the Sketch tab, or press Finish Sketch when done." : "Editing sketch: change geometry, constraints and dimensions, then Finish Sketch.");
        RaiseState();
    }

    /// <summary>Re-open an existing sketch object for editing (geometry, constraints and dimensions are preserved).</summary>
    public bool EditSketch(SceneObject obj, HistoryNode? source = null)
    {
        if (obj.Kind != ObjKind.Sketch || obj.SketchData == null || obj.SketchPlane is not { } plane) { Say("This sketch has no editable definition (it was created before constraints existed, or was transformed).", true); return false; }
        if (Sketch != null) { Say("Finish the current sketch first.", true); return false; }
        if(DeferToolChange("Edit "+obj.Name,()=>EditSketch(Find(obj.Id)??obj,source)))return false;
        CancelCommand();
        // Roll back before dependent features, keeping their original branch intact.
        // Visibility changes are folded into snapshots, so compare sketch definitions.
        _sketchReturnNode = Current;
        _sketchEditSource = source;
        for (var n = Current; n.Parent != null; n = n.Parent)
        {
            if (_sketchEditSource != null) break;
            var after = n.After.FirstOrDefault(o => o.Id == obj.Id);
            var before = n.Parent.After.FirstOrDefault(o => o.Id == obj.Id);
            if (after != null && (before?.SketchData == null || after.SketchData == null || !after.SketchData.SameDefinition(before.SketchData)))
            { _sketchEditSource = n; break; }
        }
        _sketchRebuildEnd = _sketchEditSource == null ? null : RebuildEndFor(_sketchEditSource, Current);
        if (_sketchEditSource != null) { GoTo(_sketchEditSource); DocumentParameters = _sketchRebuildEnd!.Parameters; }
        EnterSketch(plane, obj.SketchData.Clone(), obj.Id);
        return true;
    }

    public void ExitSketch(bool keep)
    {
        var sk = Sketch; if (sk == null) return;
        var source = _sketchEditSource; var back = _sketchReturnNode; var end = _sketchRebuildEnd;
        _sketchEditSource = _sketchReturnNode = _sketchRebuildEnd = null;
        bool committed = false;
        sk.Tool?.Deactivate(this);
        Sketch = null;
        RestoreProjection();
        SketchStatus = "";
        if (keep && sk.HasContent)
        {
            try
            {
                // dimensions driven by formulas are brought up to date first: other sketches or parameters may have moved since they were typed
                try { sk.RefreshFormulas(this); } catch (FormatException e) { Say(e.Message, true); }
                var model = sk.ExportModel();
                // construction curves are reference geometry: the view draws them dashed, and they stay out of the shape so a profile never picks them up
                var shapes = model.ToShapes(sk.RootPlane);
                var compound = shapes.Count > 0 ? Occ.Profile(shapes, faces: false) : Occ.Vertex(sk.RootPlane.Origin);
                if (sk.EditingId is { } id && Find(id) is { } old)
                {
                    Checkpoint("Edit sketch", "sketch");
                    int i = Objects.FindIndex(o => o.Id == id);
                    Objects = Objects.SetItem(i, old.WithSketch(compound, model, sk.RootPlane));
                    RaiseScene();
                    _selection.Clear(); _selection.Add(SelItem.Of(Find(id)!)); SelectionChanged?.Invoke();
                    Say($"{old.Name} updated");
                    committed = true;
                    if (source != null && end != null) ReplayAfterEdit(source, old.Name, end);
                    else if (Objects.Any(o => o.Kind == ObjKind.Solid))
                        Say($"{old.Name} updated. Existing bodies have no construction history to rebuild.");
                }
                else
                {
                    var obj = AddObject(compound, sk.SuggestedName ?? "Sketch", ObjKind.Sketch, plane: sk.RootPlane, sketch: model);
                    _selection.Clear(); _selection.Add(SelItem.Of(obj)); SelectionChanged?.Invoke();
                    Say($"{obj.Name} created");
                }
            }
            catch (KernelException e) { Say("Could not build the sketch curves: " + e.Message, true); }
        }
        else Say(keep ? "Empty sketch discarded" : "Sketch cancelled");
        if (!committed && back != null)
        {
            string status = Status; bool error = StatusIsError;
            GoTo(back);
            Say(status, error);
        }
        RaiseState();
    }
    // ------------------------------------------------------------ tools
    public IViewportTool? ActiveTool => ActiveCommand?.Tool ?? Sketch?.Tool;
    public bool ToolOwnsPointer => ActiveTool != null;

    /// <summary>Free-standing datum gizmo; visible only in plain model view.</summary>
    public DatumGizmoTool? Datum { get; private set; }
    public bool DatumVisible => Datum != null && ActiveCommand == null && Sketch == null;
    public void ShowDatumGizmo(Vec3 at)
    {
        Datum ??= new DatumGizmoTool();
        Datum.Place(at);
        Say("Datum gizmo placed. Drag its handles, or set exact values in the Gizmo panel.");
        RaiseState(); Invalidate();
    }
    public void HideDatumGizmo() { if (Datum == null) return; Datum = null; RaiseState(); Invalidate(); }

    public bool PointerDown(PointerInfo p)
    {
        if (ActiveCommand == null && Sketch?.SpacePointerDown(this, p) == true) return true;
        if (DatumVisible && Datum!.PointerDown(this, p)) return true;
        if (ActiveTool is { } t && t.PointerDown(this, p)) return true;
        if (Sketch != null) return true; // never fall back to selection while sketching
        if (ActiveCommand != null)
        {
            if (ActiveCommand.GrabAt(this, p.X, p.Y, p.Ctrl)) { _cmdGrab = true; return true; }
            var hl = ActiveCommand.Handles; for (int i = 0; i < hl.Count; i++) if (HandleHit(hl[i], p.X, p.Y)) { _handleDrag = true; _handleIdx = i; _typed = ""; return true; }
            if (TryBeginRegion(p)) return true;
            ClickAt(p);
            return true;
        }
        if (TryBeginRegion(p)) return true;
        ClickAt(p);
        return true;
    }

    public void PointerMove(PointerInfo p)
    {
        if (_region != null && p.Left && RegionMove(p)) return;
        if (ActiveCommand == null && Sketch?.SpacePointerMove(this, p) == true) return;
        if (DatumVisible) Datum!.PointerMove(this, p);
        if (_cmdGrab && p.Left && ActiveCommand != null) { ActiveCommand.GrabMove(this, p.X, p.Y, p.Alt); Invalidate(); return; }
        if (_handleDrag && p.Left && ActiveCommand?.Handles is { } hs0 && _handleIdx < hs0.Count) { DragHandle(hs0[_handleIdx], p); return; }
        if (ActiveCommand?.Handles is { Count: > 0 } hhs && ActiveTool == null)
        {
            bool over = hhs.Any(hh => HandleHit(hh, p.X, p.Y));
            if (over != _handleHover) { _handleHover = over; Invalidate(); }
        }
        ActiveTool?.PointerMove(this, p);
    }
    bool _cmdGrab;
    public void PointerUp(PointerInfo p) { _handleDrag = false; _cmdGrab = false; if (RegionUp(p)) return; if (DatumVisible) Datum!.PointerUp(this, p); if (Sketch?.EndGizmoDrag(this,p) != true) ActiveTool?.PointerUp(this, p); Invalidate(); }

    // ------------------------------------------------------------ grab handle (pull an arrow to set a distance)
    bool _handleDrag, _handleHover;
    int _handleIdx;
    string _typed = "";          // digits typed in the viewport for the active handle

    public static Vec3 HandleTip(HandleSpec h) => h.Ref is { } r ? h.Origin + Assembly.JointVisuals.Rotate(r.Normalized(), h.Dir, h.Param.Value) * h.Radius : h.Origin + h.Dir.Normalized() * (h.Param.Value / h.Mul);

    bool HandleHit(HandleSpec h, double px, double py)
    {
        if (!GizmoSettings.Current.Allows(0,0)) return false;
        var a = Camera.Project(h.Origin); var b = Camera.Project(HandleTip(h)); if (a == null || b == null) return false;
        var (_, d) = Picking.SegDist(px, py, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
        double dt = Math.Sqrt((b.Value.x - px) * (b.Value.x - px) + (b.Value.y - py) * (b.Value.y - py));
        return dt < GizmoSettings.Current.Tolerance * 2 || d < GizmoSettings.Current.Tolerance * .8;
    }

    void DragHandle(HandleSpec h, PointerInfo p)
    {
        var (o, d) = Camera.Ray(p.X, p.Y);
        if (h.Ref is { } refDir)
        {
            // a turning handle: where the pointer is on the plane the joint turns in, as an angle from the zero direction
            var n = h.Dir.Normalized(); double dn = Vec3.Dot(d, n);
            if (Math.Abs(dn) < 1e-4) return;                    // looking edge-on to the turn: no way to tell the angle
            var hit = o + d * (Vec3.Dot(h.Origin - o, n) / dn) - h.Origin;
            var r0 = refDir.Normalized(); var r90 = Vec3.Cross(n, r0);
            double target = Math.Atan2(Vec3.Dot(hit, r90), Vec3.Dot(hit, r0)) * 180 / Math.PI;
            double now = h.Param.Value, delta = ((target - now) % 360 + 540) % 360 - 180;       // the short way round, so the value does not jump by a full turn
            double asked = now + delta, aStep = p.Ctrl ? 0.1 : GizmoSettings.Current.Snap ? GizmoSettings.Current.AngleStep : 1;
            if (!double.IsFinite(aStep) || aStep <= 0) aStep = 1;
            h.Param.Value = p.Alt ? asked : Math.Round(asked / aStep) * aStep;           // the value stops at the joint's limits by itself
            Invalidate(); return;
        }
        var A = h.Dir.Normalized(); var w0 = h.Origin - o;
        double b = Vec3.Dot(A, d), dd = Vec3.Dot(A, w0), e = Vec3.Dot(d, w0), den = 1 - b * b;
        if (Math.Abs(den) < 1e-4) return;                       // looking straight along the arrow: no way to tell how far
        double t = (b * e - dd) / den;                          // distance along the arrow axis nearest the pointer
        double v = t * h.Mul, step = Camera.PixelSizeAt(HandleTip(h)) * 4;
        double snap = step > 5 ? 5 : step > 1 ? 1 : step > 0.2 ? 0.5 : 0.1;       // coarse when zoomed out, fine when zoomed in
        if (p.Ctrl) snap = 0.01;
        if (GizmoSettings.Current.Snap) snap = h.Param.Unit == "°" ? GizmoSettings.Current.AngleStep : GizmoSettings.Current.MoveStep;
        if (!double.IsFinite(snap) || snap <= 0) snap = .1;
        h.Param.Value = p.Alt ? v : Math.Round(v / snap) * snap;
        Invalidate();
    }

    /// <summary>Swing the view to a three-quarter angle when it looks straight along the pull direction (so the arrow has length on screen).</summary>
    void TiltToHandle(HandleSpec hs)
    {
        var cam = Camera; if (cam.Locked) return;
        var dir = hs.Dir.Normalized();
        // which side of the profile is the body on? We look at the face from outside, with the arrow pointing into the body (a cut) or out of it
        double sideSign = 1;
        {
            Vec3? center = null; double bestD = double.MaxValue;
            foreach (var b in Objects.Where(o => o.Kind == ObjKind.Solid && o.Visible))
            {
                var m = b.Mesh; var lo = m.Min; var hi = m.Max;
                double dx = Math.Max(Math.Max(lo.X - hs.Origin.X, 0), hs.Origin.X - hi.X), dy = Math.Max(Math.Max(lo.Y - hs.Origin.Y, 0), hs.Origin.Y - hi.Y), dz = Math.Max(Math.Max(lo.Z - hs.Origin.Z, 0), hs.Origin.Z - hi.Z);
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < bestD && d < 1e-2 + (hi - lo).Length * 0.01) { bestD = d; center = (lo + hi) * 0.5; }
            }
            if (center is { } c && Vec3.Dot(c - hs.Origin, dir) > 0) sideSign = -1;
        }
        // pull room: centre on the stretch the arrow can cover and back off a little, so the whole pull stays on screen
        cam.Target = hs.Origin + dir * (Math.Max(Math.Abs(hs.Param.Value / hs.Mul), 1) * 0.6);
        { double need = Math.Abs(hs.Param.Value / hs.Mul) * 3.5; if (cam.Distance < need) cam.Distance = need; }
        double along = Vec3.Dot(cam.EyeDirection, dir) * sideSign;
        if (along > 0.3 && along < 0.9) return;                 // already a good angle: keep the user's view
        // pick the three-quarter view (45 degrees round, 30 up) that shows the face from the right side and is closest to where the camera is now
        Vec3? best = null; double bestScore = double.MinValue;
        foreach (int k in new[] { 45, 135, -45, -135 })
            foreach (double pitch in new[] { 30.0, -30.0 })
            {
                double yaw = k * Math.PI / 180, pr = pitch * Math.PI / 180;
                var eye = new Vec3(Math.Sin(yaw) * Math.Cos(pr), -Math.Cos(yaw) * Math.Cos(pr), Math.Sin(pr));
                double side = Vec3.Dot(eye, dir) * sideSign;
                if (side < 0.3 || side > 0.9) continue;
                double score = (pitch > 0 ? 2 : 0) - Math.Acos(Math.Clamp(Vec3.Dot(eye, cam.EyeDirection), -1, 1));
                if (score > bestScore) { bestScore = score; best = eye; }
            }
        var e = best ?? (dir * (0.55 * sideSign) - cam.Right * 0.6 - cam.Up * 0.55).Normalized();
        AnimateView(Math.Atan2(e.X, -e.Y), Math.Asin(Math.Clamp(e.Z, -1, 1)));
    }

    Command? _handleCmd;
    void ResetTypingFor(Command? c) { if (!ReferenceEquals(_handleCmd, c)) { _handleCmd = c; _typed = ""; _handleIdx = 0; } }

    void BuildHandleOverlay(Overlay o)
    {
        ResetTypingFor(ActiveCommand);
        if (ActiveCommand?.Handles is not { Count: > 0 } hs) return;
        for (int hi = 0; hi < hs.Count; hi++) BuildOneHandle(o, hs[hi], hi == _handleIdx);
    }

    void BuildOneHandle(Overlay o, HandleSpec h, bool active)
    {
        if (!GizmoSettings.Current.Allows(0,0)) return;
        if (h.Ref != null)
        {
            // a turning handle: the allowed arc, the current angle and a grip at its end
            var grip = HandleTip(h); double cur = h.Param.Value;
            double lo = h.ShowRange ? h.Param.Min : double.NegativeInfinity, hi = h.ShowRange ? h.Param.Max : double.PositiveInfinity;
            Assembly.JointVisuals.Range(o, h.Origin, h.Dir, h.Ref, h.Radius, lo, hi, cur);
            bool hit = Assembly.JointVisuals.IsLimit(lo) && cur <= lo + 1e-6 || Assembly.JointVisuals.IsLimit(hi) && cur >= hi - 1e-6;
            uint colour = hit ? Assembly.JointVisuals.HitColor : _handleDrag || _handleHover ? 0xFFFFFFFF : h.Colour != 0 ? h.Colour : GizmoVisual.Accent;
            o.AddDot(grip, colour, _handleDrag ? 13 : 11);
            o.AddLabel(grip, active && _typed.Length > 0 ? $"{_typed}_ °  -  Enter to apply" : $"{cur:0.#}°{(hit ? "  (limit)" : "")}  -  drag or type", true, 4);
            return;
        }
        if (h.ShowRange && (Assembly.JointVisuals.IsLimit(h.Param.Min) || Assembly.JointVisuals.IsLimit(h.Param.Max)))
        {
            // a sliding handle with limits: the ruler of where it may go
            double hs = Camera.PixelSizeAt(h.Origin) * 70;
            Assembly.JointVisuals.Range(o, h.Origin, h.Dir, null, hs, h.Param.Min / h.Mul, h.Param.Max / h.Mul, h.Param.Value / h.Mul);
        }
        var tip = HandleTip(h); var A = h.Dir.Normalized();
        uint col = h.Colour != 0 ? h.Colour : GizmoVisual.Accent;
        double ps = Camera.PixelSizeAt(tip);
        o.AddDot(h.Origin, col, 7);
        o.AddLine(h.Origin, tip, col, GizmoSettings.Current.Stroke + (_handleDrag || _handleHover ? 1.5f : 0));
        // arrow head: a small cone at the tip pointing the way the value grows
        double factor = GizmoSettings.Current.Pixels / 96;
        double len = ps * 14 * factor * (h.Param.Value >= 0 ? 1 : -1), rad = ps * 5 * factor;
        var u = Math.Abs(A.Z) < 0.9 ? Vec3.Cross(A, Vec3.UnitZ).Normalized() : Vec3.Cross(A, Vec3.UnitX).Normalized();
        var v = Vec3.Cross(A, u); var apex = tip + A * len; const int n = 14;
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var p0 = tip + (u * Math.Cos(a0) + v * Math.Sin(a0)) * rad; var p1 = tip + (u * Math.Cos(a1) + v * Math.Sin(a1)) * rad;
            o.AddTri(apex, p0, p1, col); o.AddTri(tip, p1, p0, col);
        }
        string unit = h.Param.Unit.Length > 0 ? " " + h.Param.Unit : "";
        bool atEnd = Assembly.JointVisuals.IsLimit(h.Param.Min) && h.Param.Value <= h.Param.Min + 1e-6 || Assembly.JointVisuals.IsLimit(h.Param.Max) && h.Param.Value >= h.Param.Max - 1e-6;
        o.AddLabel(apex + A * (ps * 12), active && _typed.Length > 0 ? $"{_typed}_{unit}  -  Enter to apply" : $"{h.Param.Value:0.##}{unit}{(atEnd ? "  (limit)" : "")}  -  drag or type", true, 4);
    }

    /// <summary>Typing a number while a grab handle is showing sets its value (Tab switches handle, Enter applies, Esc cancels).</summary>
    bool TypeIntoHandle(string key)
    {
        ResetTypingFor(ActiveCommand);
        if (ActiveCommand?.Handles is not { Count: > 0 } hs || ActiveTool != null) return false;
        if (_handleIdx >= hs.Count) _handleIdx = 0;
        var h = hs[_handleIdx];
        bool digit = key.StartsWith("Char:") && key.Length == 6 && (char.IsDigit(key[5]) || key[5] == '.' || key[5] == ',');
        if (digit) { _typed += key[5] == ',' ? "." : key[5].ToString(); ApplyTyped(h); return true; }
        switch (key)
        {
            case "Minus": _typed = _typed.StartsWith('-') ? _typed[1..] : "-" + _typed; ApplyTyped(h); return true;
            case "Back": if (_typed.Length == 0) return false; _typed = _typed[..^1]; ApplyTyped(h); return true;
            case "Tab": if (hs.Count < 2) return false; _handleIdx = (_handleIdx + 1) % hs.Count; _typed = ""; Invalidate(); return true;
            case "Enter": if (_typed.Length == 0) return false; ApplyTyped(h); _typed = ""; return true;
            case "Escape": if (_typed.Length == 0) return false; _typed = ""; Invalidate(); return true;
        }
        return false;
    }

    void ApplyTyped(HandleSpec h)
    {
        if (double.TryParse(_typed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) { h.Param.Value = v; ActiveCommand?.OnChanged(); }
        Invalidate();
    }

    public bool Key(string key)
    {
        if(PendingNotice!=null && key is "Enter" or "Escape")
        {if(key=="Enter")ResolvePending(true);else KeepPending();return true;}
        if (ActiveCommand?.HasPanelStep == true && key is "Enter" or "Escape")
        { if (key == "Enter") AcceptCommandPanel(); else CancelCommandPanel(); return true; }
        if (ActiveCommand == null && Sketch?.SpaceKey(this, key) == true) return true;
        if (DatumVisible && Datum!.Key(this, key)) return true;
        if (ActiveTool?.Key(this, key) == true) return true;
        if (ActiveCommand == null && Sketch?.DrawingKey(this, key) == true) return true;
        if (TypeIntoHandle(key)) return true;
        switch (key)
        {
            case "LookAt": return LookAtSelection();
            case "Char:X": if (Sketch == null) return false; Sketch.ToggleConstructionMode(this); return true;
            case "Escape":
                if (ActiveCommand != null) { CancelCommandPanel(); return true; }
                if (Sketch != null) { if (Sketch.Tool is not SelectTool) Sketch.SetTool(this, new SelectTool()); return true; }
                ClearSelection(); return true;
            case "Enter":
                if (ActiveCommand != null) { AcceptCommandPanel(); return true; }
                if (Sketch != null) { ExitSketch(true); return true; }
                return false;
            case "Delete":
                if (ActiveCommand == null && Sketch is { } sketch) return sketch.DeleteSelected(this);
                if (ActiveCommand == null && Sketch == null) { DeleteSelection(); return true; }
                if (ActiveCommand is Commands.DirectEditCommand direct && Sketch == null) return direct.DeletePicked();
                return false;
        }
        return false;
    }

    void BuildOriginOverlay(Overlay o)
    {
        double size = OriginSize();
        uint[] tint = [0x4C94FF, 0x3FCF8E, 0xFF6B6F];
        string[] label = ["XY", "XZ", "YZ"];
        for (int i = 0; i < 3; i++)
        {
            if (!OriginShown(i)) continue;
            var it = new SelItem(Guid.Empty, SelKind.Origin, i);
            bool sel = _selection.Contains(it), hov = Hover == it;
            uint a = sel ? 0x66u : hov ? 0x55u : 0x26u;
            var pl = OriginPlane(i);
            double grow = hov ? 1.15 : 1;                                     // the plane under the pointer swells so the pick is obvious
            Vec3 P(double u, double v) => pl.PointAt(u * size * grow, v * size * grow);
            uint fill = (a << 24) | tint[i];
            o.AddTri(P(-1, -1), P(1, -1), P(1, 1), fill); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), fill);
            uint edge = (sel || hov ? 0xFFu : 0xB0u) << 24 | tint[i];
            o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], edge, sel ? 3f : hov ? 3.2f : 1.6f, true);
            o.AddLabel(P(0.92, 0.92), label[i], true);
        }
        uint[] ac = [0xFFE5484D, 0xFF3FCF8E, 0xFF4C94FF];
        for (int i = 3; i < 6; i++)
        {
            if (!OriginShown(i)) continue;
            var it = new SelItem(Guid.Empty, SelKind.Origin, i);
            bool sel = _selection.Contains(it) || Hover == it;
            var (ao, ad) = OriginAxis(i);
            o.AddLine(ao - ad * size, ao + ad * size, sel ? 0xFFFFC53D : ac[i - 3], sel ? 4f : 2.4f);
            o.AddDot(ao + ad * size, sel ? 0xFFFFC53D : ac[i - 3], 11);
            o.AddLabel(ao + ad * size, new[] { "X", "Y", "Z" }[i - 3], false);
        }
        if (OriginShown(6) || OriginVisible[6]) o.AddDot(Vec3.Zero, 0xFFFFFFFF, 12);
    }

    void BuildPlaneObjectsOverlay(Overlay o)
    {
        foreach (var obj in Objects)
        {
            if (obj.Kind != ObjKind.Plane || !obj.Visible || HiddenIds.Contains(obj.Id) || obj.SketchPlane is not { } pl) continue;
            var it = SelItem.Of(obj);
            bool sel = _selection.Contains(it), hov = Hover == it;
            var (hu, hv) = PlaneHalfSize(obj);
            uint tint = obj.Color & 0xFFFFFF;
            Vec3 P(double u, double v) => pl.PointAt(u * hu, v * hv);
            uint fill = ((sel ? 0x66u : hov ? 0x55u : 0x24u) << 24) | tint;
            o.AddTri(P(-1, -1), P(1, -1), P(1, 1), fill); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), fill);
            o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], ((sel ? 0xFFu : 0xC0u) << 24) | tint, sel ? 3f : 1.6f, true);
            o.AddLabel(P(0.9, 0.9), obj.Name, true);
        }
    }

    /// <summary>The principal plane facing the camera when the view is (nearly) axis-aligned, else the ground plane (the same rule the grid uses).</summary>
    Plane3 ViewPlane()
    {
        var e = Camera.EyeDirection; double ax = Math.Abs(e.X), ay = Math.Abs(e.Y), az = Math.Abs(e.Z);
        const double near = 0.95;
        if (ax >= near && ax >= ay && ax >= az) return Plane3.YZ;
        if (ay >= near && ay >= ax && ay >= az) return Plane3.XZ;
        return Plane3.XY;
    }

    /// <summary>In orthographic mode, and while sketching, both axes of the plane being looked at are drawn: X red, Y green, Z blue.</summary>
    void BuildPlaneAxesOverlay(Overlay o)
    {
        if (!Ortho && !Camera.Orthographic && Sketch == null) return;
        var pl = Sketch?.GridPlane ?? ActiveCommand?.WorkPlane ?? ViewPlane();
        double ext = Math.Max(Camera.Distance * 30, 400);
        uint ColorOf(Vec3 a) => Math.Abs(a.X) > 0.9 ? 0xFFE5484D : Math.Abs(a.Y) > 0.9 ? 0xFF3FCF8E : 0xFF4C94FF;
        o.AddLine(pl.Origin - pl.U * ext, pl.Origin + pl.U * ext, ColorOf(pl.U), 1.8f);
        o.AddLine(pl.Origin - pl.V * ext, pl.Origin + pl.V * ext, ColorOf(pl.V), 1.8f);
    }

    public void BuildOverlay(Overlay o)
    {
        BuildPlaneAxesOverlay(o);
        BuildOriginOverlay(o);
        BuildPlaneObjectsOverlay(o);
        BuildAssemblyOverlay(o);
        BuildCamOverlay(o);
        BuildMotionOverlay(o);
        BuildForceOverlay(o);
        BuildLightsOverlay(o);
        foreach (var obj in Objects.Where(x => x.Visible && x.Id != Sketch?.EditingId && x.SketchData != null))
        {
            if (obj.SketchPlane is not { } root || obj.SketchData is not { } document) continue;
            SketchContext.DrawDatumReferences(this,o,document.Datums);
            foreach (var (model, plane) in document.DrawingModels(root))
            {
                if (model.ShowConstruction)
                    foreach (var (entity, pts) in model.Polylines(plane).Where(x => x.ent.Construction))
                        SketchContext.AddDashed(o, pts, 0xFFB79CFF, 1.5f, Camera.PixelSizeAt(pts[0]));
                foreach (var entity in model.Entities.Where(x => x.Type == Sketching.EntType.Point && (!x.Construction || model.ShowConstruction)))
                {
                    var p = model.P(entity.A); o.AddDot(plane.PointAt(p.X, p.Y), 0xFFB79CFF, 6);
                }
            }
        }
        Sketch?.BuildOverlay(this, o);
        DefectOverlay?.Invoke(o);
        ActiveCommand?.BuildOverlay(this, o);
        BuildHandleOverlay(o);
        BuildRegionOverlay(o);
        ActiveTool?.BuildOverlay(this, o);
        if (DatumVisible) Datum!.BuildOverlay(this, o);
        (ActiveTool as ClickTool)?.BuildHud(this, o);
        if (ActiveStudy != null && (Result == null || Result.Kind == Simulation.ResultKind.MeshOnly)) Simulation.StudyOverlay.Draw(this, o);
        if (ShowBounds) BuildBoundsOverlay(o);
    }

    // ------------------------------------------------------------ files
    sealed record FormDto(double[] V, int[][] F, long[] CreaseKeys, double[] CreaseVals, int Level, long[]? Frozen = null, string? Sym = null);
    sealed record ObjDto(string Name, string Kind, uint Color, bool Visible, double[]? Plane, string? Brep, FormDto? Form = null, Sketching.SketchModel? Sketch = null, string? Material = null, float Metal = 0f, float Gloss = 0.35f, string? Mesh = null, Assembly.AssemblyData? Asm = null,
        SurfacePattern Pattern = SurfacePattern.Automatic, float PatternScale = 12, float PatternAngle = 0, Material? PhysicalMaterial = null, Guid? Id = null, FlatPatternMap? FlatMap = null, SurfaceStyle? Surface = null);
    sealed record FileDto(int Version, List<ObjDto> Objects, List<Simulation.StudyDef>? Studies = null, List<Simulation.NamedFaceSet>? FaceSets = null, List<Simulation.SavedCaeCase>? CaeCases = null, Guid? ActiveStudy = null, Cam.CamSetup? CamSetup = null, List<DocumentParameter>? Parameters = null, List<Rendering.RenderLight>? Lights = null, Rendering.RenderSetup? RenderSetup = null, PartInfo? Info = null, HistoryDto? History = null, List<Cam.CamSetup>? CamSetups = null, List<Data.DataSheet>? Sheets = null, string? Drawing = null);

    /// <summary>What this document is as a part: number, description, material, supplier, tags. Saved in the file.</summary>
    public PartInfo PartInfo { get; private set; } = new();

    /// <summary>Identifies this open document, so an assembly can find the part it was made from while that part is still open.</summary>
    public Guid DocId { get; } = Guid.NewGuid();

    public event Action? PartInfoChanged;

    public void SetPartInfo(PartInfo info)
    {
        if (info.Equals(PartInfo)) return;
        PartInfo = info; Dirty = true;
        TimelineChanged?.Invoke(); PartInfoChanged?.Invoke();
    }

    const int PartThumbSize = 112;

    /// <summary>A small picture of the part, saved with its info so the library can show it without opening the file.</summary>
    void RefreshPartThumbnail()
    {
        string? picture = null;
        try
        {
            if (PartGeometry(this) is { } part)
                picture = Convert.ToBase64String(Rendering.PngWriter.Encode(PartsLibrary.RenderThumbnail(part.shape, PartThumbSize, color: 0xFFB4BCC8), PartThumbSize, PartThumbSize));
        }
        catch (Exception) { /* a part that cannot be drawn simply has no picture */ }
        if (picture == PartInfo.Thumbnail) return;
        PartInfo = PartInfo with { Thumbnail = picture };
        PartInfoChanged?.Invoke();
    }

    static ObjDto ToDto(SceneObject o) =>
        new ObjDto(o.Name, o.Kind.ToString(), o.Color, o.Visible,
            o.SketchPlane is { } p ? [p.Origin.X, p.Origin.Y, p.Origin.Z, p.U.X, p.U.Y, p.U.Z, p.V.X, p.V.Y, p.V.Z] : null,
            o.Form == null && o.Tri == null ? o.Shape.ToBrep() : null,
            o.Form is { } f ? new FormDto(f.V.SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray(), f.F.ToArray(), f.Crease.Keys.ToArray(), f.Crease.Values.ToArray(), f.Level, f.Frozen.SelectMany(p => new[] { p.Item1, p.Item2, p.Item3 }).ToArray(), f.Sym == "off" ? null : f.Sym) : null,
            o.SketchData, o.Look.MaterialId, o.Look.Metal, o.Look.Gloss, o.Tri != null ? Meshing.MeshIO.ToBase64(o.Tri) : null, o.Asm,
            o.Look.Pattern,o.Look.PatternScale,o.Look.PatternAngle,o.Material,o.Id,o.FlatMap,o.Look.Surface);

    public void Save(string path)
    {
        RefreshPartThumbnail();
        if (ExplodeFactor > 1e-9) SetExplode(0);        // the file stores the assembled positions
        int version = Objects.Any(o=>o.FlatMap!=null)?4:Objects.Any(o => o.SketchData is { } sketch && (sketch.Sections.Count > 0 || sketch.Datums.Count > 0 || sketch.Points.Any(p => p.Z != 0))) ? 3 : 2;
        var history = BuildHistoryDto();
        if (history != null) version = 5;
        var dto = new FileDto(version, Objects.Where(Persisted).Select(ToDto).ToList(), Studies, FaceSets, CaeCases, ActiveStudy?.Id, CamSetup, DocumentParameters.ToList(), Lights.Count > 0 ? Lights : null, RenderSetup.Equals(new Rendering.RenderSetup()) ? null : RenderSetup, PartInfo.IsEmpty && PartInfo.Thumbnail == null ? null : PartInfo, history, CamSetups.Count > 0 ? CamSetups : null, DataSheets.Count > 0 ? DataSheets : null, Drawings.Sheets.Count > 0 ? Drawings.Serialize() : null);
        File.WriteAllText(path, JsonSerializer.Serialize(dto));
        FilePath = path; Dirty = false; Current.Pin ??= $"Saved {DateTime.Now:HH:mm}"; TimelineChanged?.Invoke(); Say($"Saved {Path.GetFileName(path)}");
    }

    static Appearance Look(ObjDto d) => new(d.Material, d.Metal, d.Gloss, d.Pattern, d.PatternScale, d.PatternAngle, d.PhysicalMaterial, d.Surface?.Validated());

    static SceneObject FromDto(ObjDto d)
    {
        Plane3? plane = d.Plane is { Length: 9 } q ? new(new(q[0], q[1], q[2]), new(q[3], q[4], q[5]), new(q[6], q[7], q[8])) : null;
        if (d.Mesh is { } mb) return new SceneObject(d.Id ?? Guid.NewGuid(), d.Name, null, ObjKind.Mesh, d.Color, d.Visible, null, null, null, Look(d), Meshing.MeshIO.FromBase64(mb));
        if (d.Form is { } fd)
        {
            var sm = new Form.SubdMesh { Level = fd.Level, Sym = fd.Sym ?? "off" };
            for (int i = 0; i + 2 < fd.V.Length; i += 3) sm.V.Add(new(fd.V[i], fd.V[i + 1], fd.V[i + 2]));
            foreach (var face in fd.F) sm.F.Add(face);
            for (int i = 0; i < fd.CreaseKeys.Length; i++) sm.Crease[fd.CreaseKeys[i]] = fd.CreaseVals[i];
            if (fd.Frozen is { } fz) for (int i = 0; i + 2 < fz.Length; i += 3) sm.Frozen.Add((fz[i], fz[i + 1], fz[i + 2]));
            return new SceneObject(d.Id ?? Guid.NewGuid(), d.Name, null, ObjKind.Form, d.Color, d.Visible, null, sm, null, Look(d));
        }
        var shape = Occ.FromBrep(d.Brep!);
        // older files stored construction curves inside the sketch's shape, where they showed as solid lines and joined the profile
        if (d.Sketch != null && plane is { } root && Enum.Parse<ObjKind>(d.Kind) == ObjKind.Sketch && d.Sketch.Entities.Any(e => e.Construction))
        {
            try { var shapes = d.Sketch.ToShapes(root); shape = shapes.Count > 0 ? Occ.Profile(shapes, faces: false) : Occ.Vertex(root.Origin); } catch (KernelException) { }
        }
        return new SceneObject(d.Id ?? Guid.NewGuid(), d.Name, shape, Enum.Parse<ObjKind>(d.Kind), d.Color, d.Visible, plane, null, d.Sketch, Look(d), null, d.Asm) { FlatMap = d.FlatMap };
    }

    public void Open(string path)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path)) ?? throw new InvalidDataException("Not an Prism file.");
        if(dto.Version is not (1 or 2 or 3 or 4 or 5))throw new InvalidDataException("Unsupported Prism document version.");
        var ids=dto.Objects.Where(o=>o.Id.HasValue).Select(o=>o.Id!.Value).ToList();
        if(ids.Any(i=>i==Guid.Empty)||ids.Distinct().Count()!=ids.Count)throw new InvalidDataException("Duplicate or empty object identities in document.");
        if (dto.Parameters != null) ParameterValues.Evaluate(dto.Parameters);
        NewDocument();
        DocumentParameters = dto.Parameters?.ToImmutableList() ?? [];
        var list = ImmutableList.CreateBuilder<SceneObject>();
        foreach (var d in dto.Objects)
        {
            list.Add(FromDto(d));
            _counters[StripNumber(d.Name)] = Math.Max(_counters.GetValueOrDefault(StripNumber(d.Name)), int.TryParse(d.Name[(d.Name.LastIndexOf(' ') + 1)..], out var n) ? n : 0);
        }
        Objects = list.ToImmutable();
        if (dto.History is { Steps.Count: > 0 } saved) { try { LoadHistory(saved); } catch (Exception) { Root.After = []; Current = Root; } }
        if(dto.Studies!=null)Studies.AddRange(dto.Studies);
        if(dto.FaceSets!=null)FaceSets.AddRange(dto.FaceSets);
        if(dto.CaeCases!=null)CaeCases.AddRange(dto.CaeCases);
        DataSheets.Clear(); if(dto.Sheets!=null)DataSheets.AddRange(dto.Sheets); DataChanged?.Invoke();
        Drawings = DrawingDocumentFromFile(dto.Drawing); DrawingsChanged?.Invoke();
        ActiveStudy=Studies.FirstOrDefault(st=>st.Id==dto.ActiveStudy)??Studies.FirstOrDefault();
        CamSetup=dto.CamSetup; if(dto.CamSetups!=null)CamSetups.AddRange(dto.CamSetups); RaiseStudy();
        LoadRenderState(dto.Lights, dto.RenderSetup);
        PartInfo = dto.Info ?? new(); PartInfoChanged?.Invoke();
        FilePath = path; Dirty = false;
        RaiseScene(); ZoomExtents(); Say($"Opened {Path.GetFileName(path)}");
    }

    public void ImportStep(string path)
    {
        AddImported(Occ.ImportStep(path), path);
    }

    /// <summary>Every solid, surface and curve inside an imported shape, with its on-screen mesh made in parallel (so the UI thread only has to add the objects).</summary>
    public sealed record PreparedImport(Shape Root, List<(Shape Shape, ObjKind Kind, MeshData? Mesh)> Parts)
    {
        /// <summary>How many imported solids the kernel's validity check rejects (open or inconsistent geometry), found while the file was being meshed.</summary>
        public int Invalid { get; init; }
    }

    /// <summary>Read a file and mesh its bodies away from the UI thread. Call <see cref="AddPrepared"/> with the result on the UI thread.</summary>
    public static PreparedImport PrepareImport(string path, IProgress<string>? progress = null)
    {
        progress?.Report($"Reading {Path.GetFileName(path)}…");
        Shape root = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".step" or ".stp" => Occ.ImportStep(path),
            ".iges" or ".igs" => Occ.ImportIges(path),
            _ => Occ.FromBrep(File.ReadAllText(path)),
        };
        var leaves = new List<Shape>();
        void Walk(Shape s) { if (s.Kind == ShapeKind.Compound) { foreach (var c in s.Children()) Walk(c); } else leaves.Add(s); }
        Walk(root);
        var parts = leaves.Select(s => (Shape: s, Kind: SceneObject.Classify(s), Mesh: (MeshData?)null)).ToList();
        int done = 0, invalid = 0, total = parts.Count(p => p.Kind != ObjKind.Curve);
        System.Threading.Tasks.Parallel.For(0, parts.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
        {
            if (parts[i].Kind == ObjKind.Curve) return;
            parts[i] = parts[i] with { Mesh = parts[i].Shape.Tessellate() };
            if (parts[i].Kind == ObjKind.Solid && !parts[i].Shape.IsValid) System.Threading.Interlocked.Increment(ref invalid);
            progress?.Report($"Meshing {System.Threading.Interlocked.Increment(ref done)} of {total}…");
        });
        return new PreparedImport(root, parts) { Invalid = invalid };
    }

    public void AddPrepared(PreparedImport prepared, string path) => AddImported(prepared.Root, path, prepared);

    /// <summary>Add every solid, surface and curve of an imported shape as its own object.</summary>
    void AddImported(Shape root, string path, PreparedImport? prepared = null)
    {
        Checkpoint();
        int count = 0;
        void Add(Shape s, ObjKind k, MeshData? mesh)
        {
            var name = NextName(k == ObjKind.Solid ? "Body" : k == ObjKind.Surface ? "Surface" : "Curve");
            uint col = k == ObjKind.Solid ? Palette.ForIndex(count) : k == ObjKind.Surface ? Palette.Surface : Palette.Curve;
            var o = new SceneObject(Guid.NewGuid(), name, s, k, col);
            if (mesh != null) o.PrimeMesh(mesh);
            Objects = Objects.Add(o);
            count++;
        }
        void Walk(Shape s)
        {
            if (s.Kind == ShapeKind.Compound) { foreach (var c in s.Children()) Walk(c); return; }
            Add(s, SceneObject.Classify(s), null);
        }
        if (prepared != null) foreach (var (s, k, m) in prepared.Parts) Add(s, k, m); else Walk(root);
        RaiseScene(); ZoomExtents(); Say($"Imported {count} object(s) from {Path.GetFileName(path)}" + (prepared is { Invalid: > 0 } ? $". {prepared.Invalid} of them {(prepared.Invalid == 1 ? "has" : "have")} problems: open Direct Modeling > Repair > Check and Repair." : ""));
    }

    public void ExportStep(string path)
    {
        var shapes = Objects.Where(o => o.Visible && o.HasBrep && !o.IsAnnotation && o.Kind != ObjKind.Sketch).Select(o => o.Shape).ToList();
        if (shapes.Count == 0) throw new InvalidOperationException("Nothing to export (meshes can be exported as STL / 3MF).");
        Occ.ExportStep(shapes, path); Say($"Exported {Path.GetFileName(path)}");
    }

    /// <summary>Triangle mesh of an object for printing or comparing: meshes as they are, solids and surfaces tessellated finely.</summary>
    public Meshing.TriMesh? PrintMesh(SceneObject o, double toleranceScale = 1)
    {
        if (o.Tri != null) return o.Tri;
        if (o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form) return Meshing.MeshAnalysis.FromMeshData(o.Shape.Tessellate(Math.Max(o.Mesh.Max.DistanceTo(o.Mesh.Min) * 0.0002 * toleranceScale, 1e-4)));
        return null;
    }

    /// <summary>Export to STL, 3MF, OBJ or PLY. Selected objects if any are selected, otherwise everything visible. 3MF keeps bodies separate and named.</summary>
    public void ExportMesh(string path)
    {
        var src = SelectedObjects.Any() ? SelectedObjects.ToList() : Objects.Where(o => o.Visible).ToList();
        var parts = src.Select(o => (o, m: PrintMesh(o))).Where(x => x.m != null && x.m.TriCount > 0).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("Nothing to export.");
        if (Path.GetExtension(path).Equals(".3mf", StringComparison.OrdinalIgnoreCase)) Meshing.MeshIO.Save3mf(parts.Select(p => p.m!).ToList(), path, parts.Select(p => p.o.Name).ToList());
        else Meshing.MeshIO.Save(Meshing.TriMesh.Merge(parts.Select(p => p.m!)), path);
        Say($"Exported {parts.Count} object(s) to {Path.GetFileName(path)}");
    }

    public void ExportStl(string path) => ExportMesh(path);

    /// <summary>Import a mesh file (STL, OBJ, PLY, 3MF) as a mesh object. Returns the first object when the file is split into several bodies (all of them are selected).</summary>
    public SceneObject ImportMesh(string path, Meshing.MeshImportOptions? options = null) => ImportMeshAll(path, options)[0];

    /// <summary>Synchronous import of every body in the file.</summary>
    public List<SceneObject> ImportMeshAll(string path, Meshing.MeshImportOptions? options = null) => AddImportedMesh(Meshing.MeshImporter.Import(path, options), path);

    /// <summary>Read and process on a background thread (progress and cancellation supported); the bodies are added on the calling thread.</summary>
    public async System.Threading.Tasks.Task<List<SceneObject>> ImportMeshAsync(string path, Meshing.MeshImportOptions? options = null, IProgress<Meshing.ImportProgress>? progress = null, System.Threading.CancellationToken ct = default)
        => AddImportedMesh(await Meshing.MeshImporter.ImportAsync(path, options, progress, ct), path);

    /// <summary>Add the bodies of a finished mesh import as one history step.</summary>
    public List<SceneObject> AddImportedMesh(Meshing.MeshImportResult r, string path)
    {
        if (r.Parts.Count == 0) throw new InvalidDataException("That file contains no triangles.");
        Checkpoint("Import mesh", "mesh-import");
        var created = new List<SceneObject>();
        foreach (var part in r.Parts)
        {
            string baseName = r.Parts.Count == 1 ? (Path.GetFileNameWithoutExtension(path) is { Length: > 0 } n ? n : "Mesh") : part.Name;
            var o = new SceneObject(Guid.NewGuid(), NextName(baseName), null, ObjKind.Mesh, part.Color ?? 0xFFC8D0DA, true, null, null, null, null, part.Mesh);
            Objects = Objects.Add(o); created.Add(o);
        }
        _selection.Clear(); foreach (var o in created) _selection.Add(SelItem.Of(o));
        RaiseScene(); SelectionChanged?.Invoke(); ZoomExtents();
        int tris = r.TriCount;
        var rep = Meshing.MeshAnalysis.Report(r.Parts.Count == 1 ? r.Parts[0].Mesh : Meshing.TriMesh.Merge(r.Parts.Select(p => p.Mesh)));
        string msg = $"Imported {tris:#,0} triangles" + (created.Count > 1 ? $" in {created.Count} bodies" : "") + $" from {Path.GetFileName(path)}";
        if (r.SizeWarning != null) msg += ". " + r.SizeWarning;
        else if (!rep.Printable) msg += " - the mesh has problems; try Mesh > Repair.";
        Say(msg);
        return created;
    }

    // ------------------------------------------------------------ analysis display state
    public enum AnalysisKind { None, Curvature, Draft }
    public sealed record ClipPlane(Vec3 Origin, Vec3 Normal);
    [ObservableProperty] public partial AnalysisKind Analysis { get; set; }
    [ObservableProperty] public partial bool ShowBounds { get; set; }
    /// <summary>Section view: everything on the positive side of this plane is hidden.</summary>
    public ClipPlane? Clip { get; set; }
    public Vec3 DraftDir { get; set; } = Vec3.UnitZ;
    public double DraftMin { get; set; } = 3;
    /// <summary>Curvature colour range in 1/mm (0 = automatic).</summary>
    public double CurvRange { get; set; }

    partial void OnAnalysisChanged(AnalysisKind value) { if (value != AnalysisKind.None) Zebra = false; Invalidate(); }
    partial void OnShowBoundsChanged(bool value) => Invalidate();

    /// <summary>Colour field painted on a mesh object by an analysis (thickness, deviation, overhang, fitted regions).</summary>
    public sealed class ScalarField
    {
        public required Meshing.TriMesh Source { get; init; }
        public required MeshData Display { get; init; }
        /// <summary>1 = heat map between Min and Max, 2 = categories.</summary>
        public int Mode { get; init; } = 1;
        public int Palette { get; init; }
        public float Min { get; init; }
        public float Max { get; init; } = 1;
        public bool IsSelection { get; init; }
    }
    public Dictionary<Guid, ScalarField> Fields { get; } = [];

    /// <summary>Triangle region picked on a mesh (Mesh > Select Region). Later tools fit surfaces to it, extract it or delete it.</summary>
    public Meshing.MeshSelection? RegionSel { get; private set; }
    public event Action? RegionChanged;

    /// <summary>The region selection for this mesh object, created on demand and discarded when the mesh changes.</summary>
    public Meshing.MeshSelection RegionFor(SceneObject o)
    {
        if (RegionSel == null || RegionSel.ObjectId != o.Id || !ReferenceEquals(RegionSel.Mesh, o.Tri)) { DropRegion(); RegionSel = new Meshing.MeshSelection(o.Id, o.Tri!); }
        return RegionSel;
    }

    public void DropRegion()
    {
        if (RegionSel == null) return;
        if (Fields.TryGetValue(RegionSel.ObjectId, out var f) && f.IsSelection) Fields.Remove(RegionSel.ObjectId);
        RegionSel = null; RegionChanged?.Invoke(); Invalidate();
    }

    /// <summary>Repaint the selection highlight after the selection changed.</summary>
    public void RefreshRegion()
    {
        if (RegionSel == null) return;
        var o = Find(RegionSel.ObjectId);
        if (o?.Tri == null || !ReferenceEquals(o.Tri, RegionSel.Mesh)) { DropRegion(); return; }
        if (RegionSel.IsEmpty) { if (Fields.TryGetValue(o.Id, out var ff) && ff.IsSelection) Fields.Remove(o.Id); }
        else if (!Fields.TryGetValue(o.Id, out var cur) || !cur.IsSelection)
            Fields[o.Id] = new ScalarField { Source = o.Tri, Display = RegionSel.Refresh(), Mode = 2, Palette = 2, Min = 0, Max = 1, IsSelection = true };
        else RegionSel.Refresh();
        RegionChanged?.Invoke(); Invalidate();
    }
    public void SetField(SceneObject o, Meshing.TriMesh source, MeshData display, int mode, float min, float max, int palette = 0)
    {
        display.Scalars ??= new float[display.VertexCount];
        Fields[o.Id] = new ScalarField { Source = source, Display = display, Mode = mode, Min = min, Max = max, Palette = palette };
        Invalidate();
    }
    public void ClearField(Guid id) { if (Fields.Remove(id)) Invalidate(); }

    /// <summary>Persistent mesh defect highlights (Mesh Quality); stays after the command closes until cleared.</summary>
    public Action<Overlay>? DefectOverlay { get; private set; }
    public void SetDefects(Action<Overlay>? draw) { DefectOverlay = draw; Invalidate(); }
    public void ClearDefects() => SetDefects(null);

    public void ClearAnalysis()
    {
        DefectOverlay = null;
        foreach (var k in Fields.Where(f => !f.Value.IsSelection).Select(f => f.Key).ToList()) Fields.Remove(k);
        Analysis = AnalysisKind.None; Zebra = false; Clip = null; ShowBounds = false;
        Invalidate(); Say("Analysis display cleared");
    }

    void BuildBoundsOverlay(Overlay o)
    {
        var objs = SelectedObjects.Where(x => x.Visible).ToList();
        if (objs.Count == 0) objs = Objects.Where(x => x.Visible && !x.IsAnnotation && x.Kind != ObjKind.Sketch).ToList();
        if (objs.Count == 0) return;
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var x in objs) { var m = x.Mesh; min = new(Math.Min(min.X, m.Min.X), Math.Min(min.Y, m.Min.Y), Math.Min(min.Z, m.Min.Z)); max = new(Math.Max(max.X, m.Max.X), Math.Max(max.Y, m.Max.Y), Math.Max(max.Z, m.Max.Z)); }
        AddBox(o, min, max, 0xFFFFC53D);
    }

    public static void AddBox(Overlay o, Vec3 min, Vec3 max, uint color)
    {
        Vec3 P(int i) => new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        int[][] edges = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
        foreach (var e in edges) o.AddLine(P(e[0]), P(e[1]), color, 1.6f);
        var s = max - min;
        o.AddLabel((P(0) + P(1)) * 0.5, $"{s.X:0.##}", true, 1); o.AddLabel((P(0) + P(2)) * 0.5, $"{s.Y:0.##}", true, 1); o.AddLabel((P(0) + P(4)) * 0.5, $"{s.Z:0.##}", true, 1);
    }

    // ------------------------------------------------------------ view helpers
    /// <summary>Fit the model in every visible view.</summary>
    public void ZoomExtents()
    {
        var keep = Camera;
        foreach (var c in VisibleCameras.ToList()) { Camera = c; ZoomExtentsActive(); }
        Camera = keep;
        if (VisibleViews > 1 && LinkViews)
        {
            // one shared scale so the views really line up: the largest fit wins
            double d = VisibleCameras.Max(c => c.Distance);
            foreach (var c in VisibleCameras) c.Distance = d;
        }
        Invalidate();
    }

    void ZoomExtentsActive()
    {
        if (Sketch is { } sk)
        {
            var pts = sk.Model.Points.Select(p => Sketching.SketchModel.To3(sk.Plane, p)).ToList();
            foreach (var e in sk.Model.Entities.Where(e => e.Type == Sketching.EntType.Circle)) { var c = sk.Model.P(e.A); pts.Add(sk.Plane.PointAt(c.X + e.R, c.Y)); pts.Add(sk.Plane.PointAt(c.X - e.R, c.Y)); pts.Add(sk.Plane.PointAt(c.X, c.Y + e.R)); pts.Add(sk.Plane.PointAt(c.X, c.Y - e.R)); }
            if (pts.Count > 1)
            {
                Camera.Fit(new Vec3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)), new Vec3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
                Camera.Distance *= 1.15; Invalidate(); return;
            }
        }
        var vis = Objects.Where(o => o.Visible && !o.IsAnnotation).ToList();
        var camBounds=CamBounds();
        if(vis.Count==0&&camBounds is{} onlyPath){Camera.Fit(onlyPath.min,onlyPath.max);Invalidate();return;}
        if (vis.Count == 0) { Camera.Target = Vec3.Zero; Camera.Distance = 220; Invalidate(); return; }
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var o in vis)
        {
            var m = o.Mesh;
            min = new(Math.Min(min.X, m.Min.X), Math.Min(min.Y, m.Min.Y), Math.Min(min.Z, m.Min.Z));
            max = new(Math.Max(max.X, m.Max.X), Math.Max(max.Y, m.Max.Y), Math.Max(max.Z, m.Max.Z));
        }
        if(camBounds is{} cb){min=new(Math.Min(min.X,cb.min.X),Math.Min(min.Y,cb.min.Y),Math.Min(min.Z,cb.min.Z));max=new(Math.Max(max.X,cb.max.X),Math.Max(max.Y,cb.max.Y),Math.Max(max.Z,cb.max.Z));}
        Camera.Fit(min, max); Invalidate();
    }

    // ---- animated view changes (shortest rotation, ease-out)
    Avalonia.Threading.DispatcherTimer? _anim;
    /// <summary>True while a view change is still easing in.</summary>
    public bool IsAnimating => _anim?.IsEnabled == true;
    public void StopViewAnimation() => _anim?.Stop();
    public void AnimateView(double yaw, double pitch, double? roll = null)
    {
        _anim?.Stop();
        var cam = Camera;
        double y0 = cam.Yaw, p0 = cam.Pitch, r0 = cam.Roll, dr = Math.IEEERemainder((roll ?? 0) - r0, 2 * Math.PI);
        double dy = Math.IEEERemainder(yaw - y0, 2 * Math.PI), dp = pitch - p0;
        var start = DateTime.UtcNow;
        const double ms = 260;
        _anim = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _anim.Tick += (_, _) =>
        {
            double t = Math.Min((DateTime.UtcNow - start).TotalMilliseconds / ms, 1);
            double e = 1 - Math.Pow(1 - t, 3);
            cam.Yaw = y0 + dy * e; cam.Pitch = p0 + dp * e; cam.Roll = r0 + dr * e;
            Invalidate();
            if (t >= 1) _anim!.Stop();
        };
        _anim.Start();
    }

    partial void OnOrthoChanged(bool value) { foreach (var c in Cameras) c.Orthographic = value; Invalidate(); }
    partial void OnDisplayChanged(DisplayMode value) => Invalidate();
    partial void OnXRayChanged(bool value) => Invalidate();
    partial void OnZebraChanged(bool value) => Invalidate();
    partial void OnShowGridChanged(bool value) => Invalidate();

    public Plane3 WorkPlane => Sketch?.Plane ?? ActiveCommand?.WorkPlane ?? Plane3.XY;
}
