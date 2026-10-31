using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>The model browser: Origin (planes, axes), then bodies, forms, surfaces, sketches and curves.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<BrowserNode> Browser { get; } = [];
    [ObservableProperty] public partial string BrowserFilter { get; set; } = "";
    readonly Dictionary<string, bool> _folderOpen = new() { ["Origin"] = false, ["Planes"] = true, ["Axes"] = true, ["Points"] = true, ["Bodies"] = true, ["Freeform"] = true, ["Surfaces"] = true, ["Sketches"] = true, ["Curves"] = true, ["Construction Planes"] = true, ["Components"] = true, ["Joints"] = true, ["Animations"] = true, ["Toolpaths"] = true };

    partial void OnBrowserFilterChanged(string value) => RebuildBrowser();

    static string IconFor(ObjKind k) => k switch { ObjKind.Solid => "box", ObjKind.Surface => "nurbs-patch", ObjKind.Curve => "curve-through", ObjKind.Form => "form-box", ObjKind.Mesh => "mesh", ObjKind.Plane => "origin-plane", ObjKind.Joint => "joint-revolute", ObjKind.Animation => "animation", ObjKind.Toolpath => "toolpath", _ => "sketch" };

    public void RebuildBrowser()
    {
        var s = Session;
        string filter = BrowserFilter.Trim();
        bool filtering = filter.Length > 0;
        bool Match(string name) => !filtering || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        BrowserNode Folder(string title, string icon) => new() { Kind = NodeKind.Folder, Title = title, Icon = icon, ExpansionKey = title, IsExpanded = filtering || _folderOpen.GetValueOrDefault(title, true), VisibilityEdited = OnNodeVisibility };

        var roots = new List<BrowserNode>();
        BrowserNode ConstructionNode(Guid id, Prism.Core.Sketching.SketchModel model)
        {
            var node = new BrowserNode { Kind = NodeKind.Construction, ObjectId = id, Title = "Construction", Icon = "construction", Badge = model.Entities.Count(e => e.Construction).ToString(), VisibilityEdited = OnNodeVisibility };
            node.SetVisibleSilently(model.ShowConstruction);
            return node;
        }
        IEnumerable<BrowserNode> DatumNodes(Guid owner, IReadOnlyList<Prism.Core.Sketching.SketchDatum> datums)
        {
            for (int i = 0; i < datums.Count; i++)
            {
                var d = datums[i];
                if (!Match(d.Name) && !Match("Saved datums")) continue;
                string icon = d.Kind switch { Prism.Core.Sketching.SketchDatumKind.Plane => "origin-plane", Prism.Core.Sketching.SketchDatumKind.Axis => "origin-axis", Prism.Core.Sketching.SketchDatumKind.Point => "origin-point", _ => "origin-axis" };
                yield return new BrowserNode { Kind = NodeKind.Datum, ObjectId = owner, OriginIndex = i, DatumKind = d.Kind, Title = d.Name, Icon = icon, Badge = d.Kind.ToString() };
            }
        }
        if (s.Sketch is { } active && (active.Model.Entities.Any(e => e.Construction) || active.Datums.Count > 0) && (Match("Construction") || Match("Active sketch") || active.Datums.Count > 0))
        {
            var activeFolder = Folder("Active sketch", "sketch");
            if (active.Model.Entities.Any(e => e.Construction)) activeFolder.Children.Add(ConstructionNode(active.EditingId ?? Guid.Empty, active.Model));
            foreach (var datumNode in DatumNodes(Guid.Empty, active.Datums)) activeFolder.Children.Add(datumNode);
            if (active.Model.Entities.Any(e => e.Construction)) activeFolder.SetVisibleSilently(active.Model.ShowConstruction);
            roots.Add(activeFolder);
        }

        // ---- origin: Planes, Axes, Points (nested)
        var origin = Folder("Origin", "grid");
        string[] icons = ["origin-plane", "origin-plane", "origin-plane", "origin-axis", "origin-axis", "origin-axis", "origin-point"];
        void Sub(string title, string icon, params int[] idx)
        {
            var sub = Folder(title, icon);
            foreach (int i in idx)
                if (Match(CadSession.OriginNames[i]) || (filtering && "origin".Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    var n = new BrowserNode { Kind = NodeKind.Origin, OriginIndex = i, Title = CadSession.OriginNames[i], Icon = icons[i], VisibilityEdited = OnNodeVisibility };
                    n.SetVisibleSilently(s.OriginVisible[i]);
                    sub.Children.Add(n);
                }
            if (sub.Children.Count == 0) return;
            sub.Badge = sub.Children.Count.ToString();
            sub.SetVisibleSilently(sub.Children.All(c => c.IsVisible));
            origin.Children.Add(sub);
        }
        Sub("Planes", "origin-plane", 0, 1, 2);
        Sub("Axes", "origin-axis", 3, 4, 5);
        Sub("Points", "origin-point", 6);
        if (origin.Children.Count > 0) { origin.SetVisibleSilently(origin.Children.All(c => c.IsVisible)); roots.Add(origin); }

        // ---- objects, grouped by kind
        void Group(string title, string icon, ObjKind kind, Func<SceneObject, bool>? only = null)
        {
            var items = s.Objects.Where(o => o.Kind == kind && (only != null ? only(o) : (o.Component == null || kind != ObjKind.Solid))).ToList();
            if (items.Count == 0) return;
            var f = Folder(title, icon);
            f.Badge = items.Count.ToString();
            foreach (var o in items)
            {
                var n = new BrowserNode
                {
                    Kind = NodeKind.Object, ObjectId = o.Id, ObjKind = o.Kind, Title = o.Name, Icon = IconFor(o.Kind), Color = o.Color,
                    ExpansionKey = o.Id.ToString(), IsExpanded = filtering || _folderOpen.GetValueOrDefault(o.Id.ToString(), true),
                    Badge = o.Form is { } fm ? $"{fm.F.Count} faces" : o.Tri is { } tm ? $"{tm.TriCount / 1000.0:0.#}k tris" : "",
                    VisibilityEdited = OnNodeVisibility, RenameEdited = OnNodeRenamed,
                };
                n.SetVisibleSilently(o.Visible);
                if (o.SketchData is { } model && model.Entities.Any(e => e.Construction) && o.Id != s.Sketch?.EditingId)
                    n.Children.Add(ConstructionNode(o.Id, model));
                if (o.SketchData is { } saved && o.Id != s.Sketch?.EditingId)
                    foreach (var datumNode in DatumNodes(o.Id, saved.Datums)) n.Children.Add(datumNode);
                f.Children.Add(n);
            }
            f.SetVisibleSilently(f.Children.All(c => c.IsVisible));
            roots.Add(f);
        }
        Group("Components", "component", ObjKind.Solid, o => o.Component != null);
        Group("Bodies", "box", ObjKind.Solid);
        Group("Freeform", "form-box", ObjKind.Form);
        Group("Meshes", "mesh", ObjKind.Mesh);
        Group("Surfaces", "nurbs-patch", ObjKind.Surface);
        Group("Sketches", "sketch", ObjKind.Sketch);
        Group("Curves", "curve-through", ObjKind.Curve);
        Group("Construction Planes", "origin-plane", ObjKind.Plane);
        Group("Joints", "joint-revolute", ObjKind.Joint);
        Group("Animations", "animation", ObjKind.Animation);
        Group("Toolpaths", "toolpath", ObjKind.Toolpath);

        // Show actual feature inputs under their owning body. Shared inputs remain references to the same object.
        var nodesById = roots.SelectMany(r => r.Children).Where(n => n.IsObject).ToDictionary(n => n.ObjectId);
        var placed = new HashSet<Guid>();
        for (var step = s.Current; step.Parent != null; step = step.Parent)
        {
            if (step.Recipe == null) continue;
            var inputs = step.Recipe.Slots.SelectMany(items => items).Select(it => s.Find(it.Obj))
                .Where(o => o != null && o.Kind is ObjKind.Sketch or ObjKind.Curve or ObjKind.Plane).DistinctBy(o => o!.Id).ToList();
            var (added, modified, _) = step.Diff();
            foreach (var body in added.Concat(modified).Where(o => o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form))
            {
                if (!nodesById.TryGetValue(body.Id, out var parent)) continue;
                foreach (var input in inputs)
                {
                    if (input!.Id == body.Id || !nodesById.TryGetValue(input.Id, out var child) || !placed.Add(input.Id)) continue;
                    string name = input.Kind == ObjKind.Sketch ? "Sketches" : "References";
                    var group = parent.Children.FirstOrDefault(n => n.IsFolder && n.Title == name);
                    if (group == null)
                    {
                        string key = body.Id + "/" + name;
                        group = new BrowserNode { Kind = NodeKind.Folder, Title = name, Icon = input.Kind == ObjKind.Sketch ? "sketch" : "construction", ExpansionKey = key,
                            IsExpanded = filtering || _folderOpen.GetValueOrDefault(key, true), VisibilityEdited = OnNodeVisibility };
                        parent.Children.Add(group);
                    }
                    foreach (var root in roots) root.Children.Remove(child);
                    group.Children.Add(child); group.Badge = group.Children.Count.ToString();
                    group.SetVisibleSilently(group.Children.All(c => c.IsVisible));
                }
            }
        }
        foreach (var root in roots) if (root.IsFolder) root.Badge = root.Children.Count.ToString();
        roots.RemoveAll(r => r.IsFolder && r.Children.Count == 0);
        var document = new BrowserNode { Kind = NodeKind.Folder, Title = s.FilePath is { } path ? System.IO.Path.GetFileName(path) : "Untitled model", Icon = "component", ExpansionKey = "Document",
            IsExpanded = filtering || _folderOpen.GetValueOrDefault("Document", true), VisibilityEdited = OnNodeVisibility };
        foreach (var root in roots)
        {
            if (root.Title is "Bodies" or "Components") foreach (var body in root.Children) document.Children.Add(body);
            else document.Children.Add(root);
        }
        bool Prune(BrowserNode node)
        {
            if (Match(node.Title)) return true;
            foreach (var child in node.Children.ToList()) if (!Prune(child)) node.Children.Remove(child);
            return node.Children.Count > 0;
        }
        if (filtering) foreach (var child in document.Children.ToList()) if (!Prune(child)) document.Children.Remove(child);
        Browser.Clear();
        if (document.Children.Count > 0) Browser.Add(document);
        void Track(BrowserNode n)
        {
            n.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BrowserNode.IsExpanded) && !filtering) _folderOpen[n.ExpansionKey.Length > 0 ? n.ExpansionKey : n.Title] = n.IsExpanded; };
            foreach (var c in n.Children) Track(c);
        }
        Track(document);
        BrowserRebuilt?.Invoke();
    }

    public event Action? BrowserRebuilt;

    // ---- node edits
    void OnNodeVisibility(BrowserNode node)
    {
        var s = Session;
        void Apply(BrowserNode n, bool v)
        {
            if (n.Kind == NodeKind.Origin) s.SetOriginVisible(n.OriginIndex, v);
            else if (n.Kind == NodeKind.Construction)
            {
                if (s.Sketch is { } active && n.ObjectId == (active.EditingId ?? Guid.Empty))
                {
                    active.Model.ShowConstruction = v;
                    active.SelEntities.RemoveWhere(id => active.Model.EntityOrNull(id) is { Construction: true });
                    active.SelPoints.RemoveWhere(id => !active.PointVisible(id));
                    active.HoverEntity = active.HoverPoint = -1; active.Cursor = null;
                    s.NotifySketchChanged();
                }
                else if (s.Find(n.ObjectId) is { SketchData: { } model, SketchPlane: { } plane } obj)
                {
                    var copy = model.Clone(); copy.ShowConstruction = v;
                    var shapes = copy.ToShapes(plane, includeConstruction: v);
                    var compound = shapes.Count > 0 ? Prism.Kernel.Occ.Profile(shapes, faces: false) : Prism.Kernel.Occ.Vertex(plane.Origin);
                    s.Update(obj.Id, x => x.WithSketch(compound, copy, plane), checkpoint: false);
                }
                s.Invalidate();
            }
            else if (n.Kind == NodeKind.Object) s.Update(n.ObjectId, o => o.WithVisible(v), checkpoint: false);
        }
        void Cascade(BrowserNode n, bool v)
        {
            foreach (var c in n.Children)
            {
                c.SetVisibleSilently(v);
                if (!c.IsFolder) Apply(c, v);
                if (c.Children.Count > 0) Cascade(c, v);
            }
        }
        if (node.IsFolder) Cascade(node, node.IsVisible);
        else Apply(node, node.IsVisible);
    }

    void OnNodeRenamed(BrowserNode node)
    {
        var text = node.EditText.Trim();
        if (text.Length == 0 || text == node.Title) return;
        Session.Update(node.ObjectId, o => o.WithName(text), label: "Rename");
    }

    public IEnumerable<BrowserNode> AllBrowserNodes()
    {
        IEnumerable<BrowserNode> Walk(BrowserNode n) => new[] { n }.Concat(n.Children.SelectMany(Walk));
        return Browser.SelectMany(Walk);
    }

    public void DeleteNodes(IEnumerable<BrowserNode> nodes)
    {
        var ids = nodes.Where(n => n.IsObject).Select(n => n.ObjectId).ToList();
        if (ids.Count == 0) return;
        Session.SelectObjects(ids);
        Session.DeleteSelection();
    }

    public void DuplicateNodes(IEnumerable<BrowserNode> nodes)
    {
        var ids = nodes.Where(n => n.IsObject).Select(n => n.ObjectId).ToList();
        if (ids.Count == 0) return;
        Session.SelectObjects(ids);
        Session.Duplicate();
    }

    public void ZoomToNodes(IEnumerable<BrowserNode> nodes) => Session.ZoomToObjects(nodes.Where(n => n.IsObject).Select(n => n.ObjectId));

    public void Isolate(IEnumerable<BrowserNode> nodes)
    {
        var keep = nodes.Where(n => n.IsObject).Select(n => n.ObjectId).ToHashSet();
        if (keep.Count == 0) return;
        foreach (var o in Session.Objects.ToList()) Session.Update(o.Id, x => x.WithVisible(keep.Contains(x.Id)), checkpoint: false);
    }

    public void HideSelected()
    {
        foreach (var o in Session.SelectedObjects.ToList()) Session.Update(o.Id, x => x.WithVisible(false), checkpoint: false);
    }

    public void IsolateSelected()
    {
        var keep = Session.SelectedObjects.Select(o => o.Id).ToHashSet();
        if (keep.Count == 0) return;
        foreach (var o in Session.Objects.ToList()) Session.Update(o.Id, x => x.WithVisible(keep.Contains(x.Id)), checkpoint: false);
    }

    public void ShowAll()
    {
        foreach (var o in Session.Objects.ToList()) if (!o.Visible) Session.Update(o.Id, x => x.WithVisible(true), checkpoint: false);
    }

    public void SketchOnPlane(BrowserNode node)
    {
        if (node.Kind != NodeKind.Origin || node.OriginIndex > 2) return;
        Session.StartCommand(new CreateSketchCommand());
        Session.ActiveCommand?.Pick(new SelItem(Guid.Empty, SelKind.Origin, node.OriginIndex), false);
    }

    /// <summary>Saved gizmo/plane/axis/point rows: in the sketch being edited they become the active reference; on a finished sketch a saved plane can host a new sketch.</summary>
    public void UseDatumNode(BrowserNode node)
    {
        if (node.Kind != NodeKind.Datum) return;
        if (node.IsActiveSketchDatum)
        {
            if (Session.Sketch is { } sk && sk.Datums.ElementAtOrDefault(node.OriginIndex) is { } datum) sk.UseDatum(Session, datum.Id);
        }
        else if (node.DatumKind == Prism.Core.Sketching.SketchDatumKind.Plane && !IsSketching)
        {
            Session.StartCommand(new CreateSketchCommand());
            Session.ActiveCommand?.Pick(new SelItem(node.ObjectId, SelKind.Datum, node.OriginIndex), false);
        }
    }

    public void EditSketchNode(BrowserNode node)
    {
        if (!node.IsObject || node.ObjKind != ObjKind.Sketch) return;
        if (Session.Find(node.ObjectId) is { } o) Session.EditSketch(o);
    }

    public bool CanEditSketch => !IsSketching && Session.SelectedObjects.Count(o => o.Kind == ObjKind.Sketch) == 1;
    public void EditSelectedSketch() { if (Session.SelectedObjects.FirstOrDefault(o => o.Kind == ObjKind.Sketch) is { } o) Session.EditSketch(o); }

    public void ConvertNodeToNurbs(BrowserNode node)
    {
        if (!node.IsObject) return;
        Session.SelectObjects([node.ObjectId]);
        FreeformActions.ConvertToNurbs(Session);
    }
}
