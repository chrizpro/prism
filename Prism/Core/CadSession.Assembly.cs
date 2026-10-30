using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Bottom-up assemblies: parts are modelled on their own (in their own documents or files) and placed here as components.
/// Joints say how components connect; the solver walks outwards from the grounded components and positions each connected one.
/// </summary>
public sealed partial class CadSession
{
    /// <summary>Set by the application: the documents that are open (so a part can be inserted from another tab).</summary>
    public static Func<IReadOnlyList<(string Title, CadSession Session)>>? DocumentProvider { get; set; }

    public double ExplodeFactor { get; private set; }
    sealed record LocalCache(Shape World, Shape Local, Vec3 Center);
    readonly Dictionary<string, LocalCache> _locals = [];

    public IEnumerable<SceneObject> Components => Objects.Where(o => o.Component != null);
    public IEnumerable<(SceneObject obj, JointInfo joint)> JointObjects => Objects.Where(o => o.Asm is JointInfo).Select(o => (o, (JointInfo)o.Asm!));
    public SceneObject? ComponentByKey(string key) => Objects.FirstOrDefault(o => o.Component?.Key == key);
    public bool HasAssembly => Objects.Any(o => o.Asm != null);
    public static string NewKey() => Guid.NewGuid().ToString("N")[..10];

    // ------------------------------------------------------------ parts
    /// <summary>The geometry of a part: all the visible bodies of a document, as one compound (or the single body itself).</summary>
    public static (Shape shape, uint color, Appearance look)? PartGeometry(CadSession doc)
    {
        var bodies = doc.Objects.Where(o => o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface && o.HasBrep).ToList();
        if (bodies.Count == 0) return null;
        var shape = bodies.Count == 1 ? bodies[0].Shape : Occ.Compound(bodies.Select(b => b.Shape).ToList());
        return (shape, bodies[0].Color, bodies[0].Look);
    }

    /// <summary>The component's geometry in its own coordinates (cached; derived from where it sits now when needed).</summary>
    public Shape LocalShapeOf(SceneObject comp)
    {
        var ci = comp.Component ?? throw new InvalidOperationException("That is not a component.");
        if (_locals.TryGetValue(ci.Key, out var c) && ReferenceEquals(c.World, comp.Shape)) return c.Local;
        // undo the placement (and any exploded-view offset is only ever applied on top of a cached local, so none is present here)
        var local = ci.Placement.Inverse().Apply(comp.Shape);
        var m = local.Tessellate();
        _locals[ci.Key] = new LocalCache(comp.Shape, local, (m.Min + m.Max) * 0.5);
        return local;
    }

    Vec3 LocalCenter(SceneObject comp) { LocalShapeOf(comp); return _locals[comp.Component!.Key].Center; }

    // ------------------------------------------------------------ joint frames
    /// <summary>Origin and z direction of a picked face (centre + outward normal), edge (circle centre + axis, straight edge) or round face (axis), in world coordinates.</summary>
    public (Vec3 origin, Vec3 z)? FrameOf(SelItem it)
    {
        if (it.Kind == SelKind.Origin) return null;
        var sh = ShapeOf(it);
        if (sh != null && Occ.AxisOf(sh) is { } ax && ax.Dir.Length > 1e-9) return (ax.Origin, ax.Dir.Normalized());
        return null;
    }

    // ------------------------------------------------------------ solving
    /// <summary>Placement of every component implied by the joints (and the given joint overrides). Components no joint reaches keep their placement.</summary>
    public Dictionary<string, Xform> SolvePlacements(IReadOnlyDictionary<string, JointInfo>? overrides, out List<string> notes, IEnumerable<JointInfo>? extra = null)
    {
        var log = new List<string>(); notes = log;
        var comps = Components.ToDictionary(o => o.Component!.Key, o => o);
        var placement = comps.ToDictionary(kv => kv.Key, kv => kv.Value.Component!.Placement);
        var joints = JointObjects.Select(j => overrides != null && overrides.TryGetValue(j.joint.Key, out var ov) ? ov : j.joint)
            .Concat(extra ?? []).Where(j => comps.ContainsKey(j.ParentKey) && comps.ContainsKey(j.ChildKey)).ToList();
        var placed = new HashSet<string>(comps.Values.Where(o => o.Component!.Grounded).Select(o => o.Component!.Key));
        var used = new HashSet<string>();

        bool Step()
        {
            foreach (var j in joints)
            {
                if (used.Contains(j.Key)) continue;
                bool p = placed.Contains(j.ParentKey), c = placed.Contains(j.ChildKey);
                if (p && c) { used.Add(j.Key); log.Add($"{j.Name}: both parts are already positioned by other joints (a closed loop), so this joint is not enforced."); continue; }
                if (!p && !c) continue;
                var jm = j.Motion();
                if (p) placement[j.ChildKey] = placement[j.ParentKey] * j.A * jm * j.B.Inverse();
                else placement[j.ParentKey] = placement[j.ChildKey] * j.B * jm.Inverse() * j.A.Inverse();
                placed.Add(p ? j.ChildKey : j.ParentKey); used.Add(j.Key);
                return true;
            }
            return false;
        }
        while (true)
        {
            while (Step()) { }
            // a group with nothing grounded: hold its first part where it is
            var seed = joints.FirstOrDefault(j => !used.Contains(j.Key));
            if (seed == null) break;
            placed.Add(seed.ParentKey);
        }
        return placement;
    }

    /// <summary>Move every component to where the joints put it (and apply the exploded-view offsets). Quiet: it folds into the history step in progress.</summary>
    public void SolveAssembly()
    {
        var placement = SolvePlacements(null, out var notes);
        ApplyPlacements(placement);
        foreach (var n in notes.Distinct()) Say(n, true);
    }

    bool _wasExploded;

    Dictionary<string, Vec3> ExplodeOffsets(Dictionary<string, Xform> placement, double factor)
    {
        var res = new Dictionary<string, Vec3>();
        if (factor < 1e-9) return res;
        var centers = new Dictionary<string, Vec3>();
        foreach (var o in Components) centers[o.Component!.Key] = placement[o.Component.Key].Apply(LocalCenter(o));
        if (centers.Count < 2) return res;
        var mean = centers.Values.Aggregate(Vec3.Zero, (a, b) => a + b) / centers.Count;
        double extent = centers.Values.Max(c => (c - mean).Length);
        // scale from the size of the whole assembly so the parts clear each other
        double size = 0; foreach (var o in Components) { var m = o.Mesh; size = Math.Max(size, (m.Max - m.Min).Length); }
        foreach (var (k, c) in centers)
        {
            var d = c - mean; if (d.Length < 1e-6) d = Vec3.UnitZ;
            res[k] = d.Normalized() * (factor * (extent + size) * 0.9) * (d.Length / Math.Max(extent, 1e-6) * 0.5 + 0.5);
        }
        return res;
    }

    // ---------------------------------------------------------------- animated pose of free parts
    /// <summary>How far a free part is moved (mm) and turned about its vertical axis (degrees) away from where it is placed. Used by animations; never stored with the part.</summary>
    public readonly record struct PartOffset(double X, double Y, double Z, double Turn);

    Dictionary<string, PartOffset> _partPose = [];
    bool _wasPosed;

    /// <summary>A part that no joint holds and that is not grounded: the parts an animation can move directly.</summary>
    public IEnumerable<SceneObject> FreeComponents => Components.Where(o => !o.Component!.Grounded && !JointObjects.Any(j => j.joint.ChildKey == o.Component.Key || j.joint.ParentKey == o.Component.Key));

    public static string PartKey(string componentKey, string channel) => $"@part:{componentKey}:{channel}";

    /// <summary>Part offsets from animation values keyed "@part:{component}:x|y|z|rz".</summary>
    public static Dictionary<string, PartOffset> PartPoseFrom(IReadOnlyDictionary<string, (double v1, double v2)> pose)
    {
        var res = new Dictionary<string, PartOffset>();
        foreach (var (key, v) in pose)
        {
            if (!key.StartsWith("@part:", StringComparison.Ordinal)) continue;
            int cut = key.LastIndexOf(':'); if (cut < 7) continue;
            string comp = key[6..cut]; var o = res.GetValueOrDefault(comp);
            res[comp] = key[(cut + 1)..] switch { "x" => o with { X = v.v1 }, "y" => o with { Y = v.v1 }, "z" => o with { Z = v.v1 }, "rz" => o with { Turn = v.v1 }, _ => o };
        }
        return res;
    }

    static Xform PartDelta(PartOffset o, Vec3 center) =>
        Xform.Translation(new Vec3(o.X, o.Y, o.Z)) * Xform.Translation(center) * Xform.RotZ(o.Turn * Math.PI / 180) * Xform.Translation(-center);

    /// <summary>Show free parts moved by these offsets (null puts them back).</summary>
    public void SetPartPose(IReadOnlyDictionary<string, PartOffset>? pose)
    {
        _partPose = pose == null ? [] : new(pose);
        if (_partPose.Count == 0) _wasPosed = true;          // put every part back where it belongs
        SolveAssembly();
    }

    void ApplyPlacements(Dictionary<string, Xform> placement)
    {
        var offsets = ExplodeOffsets(placement, ExplodeFactor);
        var list = Objects; bool changed = false;
        foreach (var o in Components.ToList())
        {
            var ci = o.Component!; var p = placement[ci.Key];
            var off = offsets.GetValueOrDefault(ci.Key, Vec3.Zero);
            if (ExplodeFactor < 1e-9 && !_wasExploded && _partPose.Count == 0 && !_wasPosed && p.DistanceTo(ci.Placement) < 1e-9) continue;       // nothing moved
            var local = LocalShapeOf(o);
            var posed = _partPose.TryGetValue(ci.Key, out var po) ? PartDelta(po, p.Apply(LocalCenter(o))) * p : p;
            var target = off.Length < 1e-9 ? posed : Xform.Translation(off) * posed;
            var world = target.Apply(local);
            int i = list.FindIndex(x => x.Id == o.Id);
            var next = o.WithAsm(ci.WithPlacement(p), world);
            list = list.SetItem(i, next); changed = true;
            _locals[ci.Key] = new LocalCache(world, local, _locals[ci.Key].Center);
        }
        _wasExploded = ExplodeFactor > 1e-9;
        _wasPosed = _partPose.Count > 0;
        if (!changed) return;
        Objects = list;
        RaiseScene();
    }

    // ---------------------------------------------------------------- CAM toolpaths
    /// <summary>Show toolpaths only up to this layer / depth pass (the layer preview slider). int.MaxValue shows everything.</summary>
    public int CamViewLayer { get; set; } = int.MaxValue;

    void BuildCamOverlay(Overlay o)
    {
        DrawCamSetup(o);DrawCamTool(o);
        foreach (var obj in Objects)
        {
            if (obj.Asm is not Cam.ToolpathInfo tp || !obj.Visible || HiddenIds.Contains(obj.Id)) continue;
            DrawToolpath(o, tp, _selection.Contains(SelItem.Of(obj)));
        }
    }

    public void DrawToolpath(Overlay o, Cam.ToolpathInfo tp, bool highlight)
    {
        int n = tp.MoveCount; if (n < 2) return;
        int limit = tp.LayerCount > 0 && CamViewLayer < tp.LayerCount ? (CamViewLayer + 1 < tp.LayerCount ? tp.LayerStarts[CamViewLayer + 1] : n) : n;
        limit = Math.Min(limit, n);
        bool playingPath=CamTimeline?.Path==tp;
        var play=playingPath?CamTimeline!.At(CamSeconds):default;
        if(playingPath)limit=Math.Min(limit,play.segment);
        uint ColorOf(Cam.MoveKind k) => k switch
        {
            Cam.MoveKind.Rapid => 0x70B0B8C8, Cam.MoveKind.Plunge => 0xFFFF8A1F, Cam.MoveKind.Cut => highlight ? 0xFFFFFFFF : 0xFF4C94FF,
            Cam.MoveKind.OuterWall => 0xFFFF6B6F, Cam.MoveKind.InnerWall => 0xFFFFC53D, Cam.MoveKind.Skin => 0xFF3FCF8E, Cam.MoveKind.Infill => 0xFF35B6FF, _ => 0xFFB79CFF,
        };
        if(n>40000)
        {
            // A sparse overview draws actual segments, never invented connecting
            // chords. Tool-tip timing still uses every programmed move.
            int stride=(int)Math.Ceiling(n/20000.0);
            for(int i=1;i<limit;i+=stride){var a=tp.Move(i-1);var b=tp.Move(i);o.AddLine(new(a.x,a.y,a.z),new(b.x,b.y,b.z),ColorOf(b.kind),1.3f);}
            if(playingPath&&limit==play.segment){var a=tp.Move(play.segment-1);o.AddLine(new(a.x,a.y,a.z),play.tip,0xFFFFC53D,2);}
            return;
        }
        // consecutive moves of the same kind become one polyline
        var run = new List<Vec3>(); var kind = Cam.MoveKind.Rapid; bool any = false;
        void Flush() { if (run.Count > 1) o.AddPolyline(run, ColorOf(kind), kind == Cam.MoveKind.Rapid ? 1f : 2.2f); run = []; }
        for (int i = 0; i < limit; i++)
        {
            var (x, y, z, k) = tp.Move(i);
            var p = new Vec3(x, y, z);
            if (!any || k != kind) { var last = run.Count > 0 ? run[^1] : p; Flush(); run = [last, p]; kind = k; any = true; if (i == 0) run = [p]; }
            else run.Add(p);
        }
        if(playingPath&&limit==play.segment&&run.Count>0)run.Add(play.tip);
        Flush();
    }

    public SceneObject AddAnimation(AnimInfo info, bool checkpoint = false)
    {
        if (checkpoint) Checkpoint("New animation", "animation");
        var marker = Occ.Line(Vec3.Zero, new Vec3(0, 0, 0.01));
        var obj = new SceneObject(Guid.NewGuid(), NextName("Animation"), marker, ObjKind.Animation, Palette.JointColor, true, null, null, null, null, null, info);
        Objects = Objects.Add(obj);
        RaiseScene();
        return obj;
    }

    /// <summary>Joint frames, drawn where the solver put them.</summary>
    void BuildAssemblyOverlay(Overlay o)
    {
        if (!Objects.Any(x => x.Asm is JointInfo)) return;
        double size = 0; foreach (var c in Components) { var m = c.Mesh; size = Math.Max(size, (m.Max - m.Min).Length); }
        double len = Math.Max(size * 0.07, 6);
        foreach (var (obj, j) in JointObjects)
        {
            if (!obj.Visible) continue;
            var parent = ComponentByKey(j.ParentKey); var child = ComponentByKey(j.ChildKey);
            if (parent?.Component == null || child?.Component == null) continue;
            var a = parent.Component.Placement * j.A;
            bool sel = _selection.Contains(SelItem.Of(obj));
            JointFrame(o, a, len * (sel ? 1.5 : 1), sel ? 0xFFFFFFFF : 0xFFFFC53D);
            string ico = j.Type switch { JointType.Rigid => "rigid", JointType.Revolute => "revolute", JointType.Slider => "slider", JointType.Ball => "ball", JointType.Planar => "planar", _ => "cylindrical" };
            o.AddLabel(a.T + a.AxisZ * len * 1.6, $"{j.Name}", true, 1);
        }
    }

    static void JointFrame(Overlay o, Xform f, double len, uint z)
    {
        o.AddLine(f.T, f.T + f.AxisX * len * 0.7, 0xFFE5484D, 2.2f);
        o.AddLine(f.T, f.T + f.AxisY * len * 0.7, 0xFF3FCF8E, 2.2f);
        o.AddLine(f.T, f.T + f.AxisZ * len * 1.3, z, 3.2f);
        o.AddDot(f.T, z, 8);
    }

    /// <summary>The world shape every component would have for these joint values and this exploded-view factor (used to preview poses without changing the document).</summary>
    public Dictionary<Guid, Shape> PoseShapes(IReadOnlyDictionary<string, JointInfo>? overrides, double explode, out List<string> notes, IReadOnlyDictionary<string, PartOffset>? partPose = null)
    {
        var placement = SolvePlacements(overrides, out notes);
        var offsets = ExplodeOffsets(placement, explode);
        var res = new Dictionary<Guid, Shape>();
        foreach (var o in Components)
        {
            var ci = o.Component!; var p = placement[ci.Key]; var off = offsets.GetValueOrDefault(ci.Key, Vec3.Zero);
            bool posed = partPose != null && partPose.TryGetValue(ci.Key, out var asked) && asked != default;
            bool unchanged = off.Length < 1e-9 && ExplodeFactor < 1e-9 && !_wasExploded && !posed && _partPose.Count == 0 && !_wasPosed && p.DistanceTo(ci.Placement) < 1e-9;
            if (unchanged) continue;
            var at = posed ? PartDelta(partPose![ci.Key], p.Apply(LocalCenter(o))) * p : p;
            res[o.Id] = (off.Length < 1e-9 ? at : Xform.Translation(off) * at).Apply(LocalShapeOf(o));
        }
        return res;
    }

    public void SetExplode(double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        if (Math.Abs(factor - ExplodeFactor) < 1e-9) return;
        foreach (var o in Components) LocalShapeOf(o);            // make sure each part's own-coordinate geometry is cached before anything is offset
        ExplodeFactor = factor;
        ApplyPlacements(SolvePlacements(null, out _));
    }

    /// <summary>Joint values changed (drive, animation): update the joint objects and re-solve.</summary>
    public void SetJointValues(IReadOnlyDictionary<string, (double v1, double v2)> values, bool checkpoint = false, string label = "Drive joints")
    {
        if (checkpoint) Checkpoint(label, "joint-revolute");
        var list = Objects;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Asm is JointInfo j && values.TryGetValue(j.Key, out var v)) list = list.SetItem(i, list[i].WithAsm(j with { Value = v.v1, Value2 = v.v2 }));
        Objects = list;
        SolveAssembly();
        RaiseScene();
    }

    public void SetGround(string key, bool grounded)
    {
        var o = ComponentByKey(key); if (o == null) return;
        Checkpoint(grounded ? "Ground component" : "Unground component", "ground");
        Update(o.Id, x => x.WithAsm(x.Component!.WithPlacementKeep(grounded)), false);
    }

    /// <summary>The other components, as dimmed shapes in the coordinates of one of them (or the world, with no key): the surroundings for modeling a part in context.</summary>
    public List<PreviewItem> ContextGhostsFor(string? excludeKey)
    {
        var inverse = excludeKey != null && ComponentByKey(excludeKey) is { Component: { } mine } ? mine.Placement.Inverse() : (Xform?)null;
        var items = new List<PreviewItem>();
        foreach (var o in Components)
        {
            if (o.Component!.Key == excludeKey || !o.Visible) continue;
            items.Add(new PreviewItem(inverse is { } inv ? inv.Apply(o.Shape) : o.Shape, ObjKind.Solid) { Context = true });
        }
        return items;
    }

    /// <summary>Asked to start a new part for the assembly (name, kind). The window answers by opening a new tab in the Design workspace.</summary>
    public static Action<string, int>? CreatePartHandler { get; set; }

    /// <summary>Bring one component up to date with the part document it was made from (the part is open in another tab).</summary>
    public bool RefreshComponentFromDocument(string key, CadSession doc)
    {
        var o = ComponentByKey(key); if (o?.Component is not { } ci) return false;
        if (PartGeometry(doc) is not { } part) return false;
        var world = ci.Placement.Apply(part.shape);
        var m = part.shape.Tessellate();
        _locals[ci.Key] = new LocalCache(world, part.shape, (m.Min + m.Max) * 0.5);
        Update(o.Id, x => x.WithAsm(ci with { Info = doc.PartInfo.WithoutThumbnail() ?? ci.Info, SourcePath = doc.FilePath ?? ci.SourcePath, SourceDoc = doc.DocId }, world), false);
        SolveAssembly();
        return true;
    }

    /// <summary>Reload components from the part files they came from.</summary>
    public int RefreshComponentsFromSource()
    {
        int n = 0;
        foreach (var o in Components.ToList())
        {
            var ci = o.Component!;
            if (ci.SourcePath is not { } path || !System.IO.File.Exists(path)) continue;
            try
            {
                var doc = new CadSession(); doc.Open(path);
                if (PartGeometry(doc) is not { } part) continue;
                var world = ci.Placement.Apply(part.shape);
                var m = part.shape.Tessellate();
                _locals[ci.Key] = new LocalCache(world, part.shape, (m.Min + m.Max) * 0.5);
                Update(o.Id, x => x.WithAsm(ci with { Info = doc.PartInfo.WithoutThumbnail() ?? ci.Info }, world), false);
                n++;
            }
            catch (Exception e) { Say($"Could not refresh {ci.PartName}: {e.Message}", true); }
        }
        if (n > 0) SolveAssembly();
        return n;
    }
}

public static class ComponentInfoExtensions
{
    public static ComponentInfo WithPlacementKeep(this ComponentInfo ci, bool grounded) => ci with { Grounded = grounded };
}
