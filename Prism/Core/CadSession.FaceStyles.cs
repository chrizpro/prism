using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Color, finish and hidden state of single faces, and selecting faces by what they share. See the surface guide, "Faces as first-class things".</summary>
public sealed partial class CadSession
{
    /// <summary>Faces among these items that belong to a solid or surface, grouped by object.</summary>
    public Dictionary<Guid, List<int>> FacesByObject(IEnumerable<SelItem> items)
    {
        var map = new Dictionary<Guid, List<int>>();
        foreach (var i in items.Where(i => i.Kind == SelKind.Face))
        {
            if (Find(i.Obj) is not { } o || !o.HasBrep || o.Kind is not (ObjKind.Solid or ObjKind.Surface)) continue;
            if (!map.TryGetValue(o.Id, out var l)) map[o.Id] = l = [];
            if (!l.Contains(i.Index)) l.Add(i.Index);
        }
        return map;
    }

    /// <summary>
    /// The faces the Appearance page edits: faces selected on a body that is not itself selected as a whole.
    /// A whole selected body means the whole body is edited, as before.
    /// </summary>
    public Dictionary<Guid, List<int>> AppearanceFaces()
    {
        var wholeBodies = _selection.Where(i => i.Kind == SelKind.Object).Select(i => i.Obj).ToHashSet();
        var map = FacesByObject(_selection);
        foreach (var id in wholeBodies) map.Remove(id);
        return map;
    }

    /// <summary>Change the style of these faces in one step. Faces whose style ends up empty drop it.</summary>
    public void EditFaceStyles(Dictionary<Guid, List<int>> faces, Func<SceneObject, FaceStyle, FaceStyle> edit, string label)
    {
        if (faces.Count == 0) return;
        BeginGroup();
        try
        {
            foreach (var (id, list) in faces)
            {
                if (Find(id) is not { } obj) continue;
                Update(id, o => o.WithFaceStyles(FaceStyleMap.With(o.FaceStyles, o.Mesh.FaceCount, list, s => edit(o, s))), label: label);
            }
        }
        finally { EndGroup(label); }
    }

    /// <summary>Hide the selected faces. The hidden state is saved with the document, unlike Isolate. Returns how many faces were hidden.</summary>
    public int HideFaces()
    {
        var faces = FacesByObject(_selection);
        int n = faces.Values.Sum(l => l.Count);
        if (n == 0) { Say("Select one or more faces to hide.", true); return 0; }
        EditFaceStyles(faces, (_, s) => s with { Hidden = true }, "Hide faces");
        _selection.RemoveAll(i => i.Kind is SelKind.Face && faces.TryGetValue(i.Obj, out var l) && l.Contains(i.Index));
        SelectionChanged?.Invoke();
        Say($"Hid {n} face{(n == 1 ? "" : "s")}. Show Faces brings them back.");
        return n;
    }

    /// <summary>Show hidden faces again: those of the selected bodies and faces, or of every body when nothing is selected. Returns how many were shown.</summary>
    public int ShowHiddenFaces()
    {
        var ids = _selection.Select(i => i.Obj).ToHashSet();
        var map = new Dictionary<Guid, List<int>>();
        foreach (var o in Objects)
        {
            if (!o.HasFaceStyles || (ids.Count > 0 && !ids.Contains(o.Id))) continue;
            var hidden = Enumerable.Range(0, o.FaceStyles!.Length).Where(o.IsFaceHidden).ToList();
            if (hidden.Count > 0) map[o.Id] = hidden;
        }
        int n = map.Values.Sum(l => l.Count);
        if (n == 0) { Say("No faces are hidden."); return 0; }
        EditFaceStyles(map, (_, s) => s with { Hidden = false }, "Show faces");
        Say($"Showed {n} hidden face{(n == 1 ? "" : "s")}.");
        return n;
    }

    public bool AnyHiddenFaces => Objects.Any(o => o.FaceStyles != null && o.FaceStyles.Any(s => s?.Hidden == true));

    public enum FaceMatch { SameColor, Linked, Adjacent }

    /// <summary>Add to the selection the faces that share something with the selected faces.</summary>
    public int SelectSameFaces(FaceMatch match)
    {
        var picked = FacesByObject(_selection);
        if (picked.Count == 0) { Say("Select a face first.", true); return 0; }
        var add = new List<SelItem>();
        foreach (var (id, faces) in picked)
        {
            var o = Find(id)!; var mesh = o.Mesh;
            IEnumerable<int> found = match switch
            {
                FaceMatch.Linked => Enumerable.Range(0, mesh.FaceCount),
                FaceMatch.SameColor => Enumerable.Range(0, mesh.FaceCount).Where(f => faces.Any(p => SameLook(o, p, f))),
                _ => Neighbours(mesh, faces),
            };
            foreach (int f in found.Where(f => !o.IsFaceHidden(f))) add.Add(new SelItem(id, SelKind.Face, f));
        }
        var all = _selection.Concat(add).Distinct().ToList();
        int gained = all.Count - _selection.Count;
        SelectItems(all);
        Say(gained == 0 ? "No more faces match." : $"Added {gained} face{(gained == 1 ? "" : "s")}.");
        return gained;
    }

    /// <summary>Two faces look the same when their effective color and finish are equal (the hidden flag does not matter).</summary>
    public static bool SameLook(SceneObject o, int a, int b)
    {
        var (ca, la) = EffectiveLook(o, a); var (cb, lb) = EffectiveLook(o, b);
        return ca == cb && Equals(la, lb);
    }

    public static (uint color, Appearance look) EffectiveLook(SceneObject o, int face)
    {
        var s = o.FaceStyleOf(face);
        return (s?.Color ?? o.Color, s?.Look ?? o.Look);
    }

    /// <summary>Faces that share an edge with any of these faces.</summary>
    static IEnumerable<int> Neighbours(MeshData mesh, IReadOnlyCollection<int> faces)
    {
        var edgesOf = FaceEdges(mesh);
        var edges = faces.Where(f => f >= 0 && f < edgesOf.Length).SelectMany(f => edgesOf[f]).ToHashSet();
        for (int f = 0; f < edgesOf.Length; f++)
            if (!faces.Contains(f) && edgesOf[f].Overlaps(edges)) yield return f;
    }

    /// <summary>For every face the edges that border it, found in one pass: an edge belongs to the faces that contain all its end points.</summary>
    static HashSet<int>[] FaceEdges(MeshData mesh)
    {
        var result = Enumerable.Range(0, mesh.FaceCount).Select(_ => new HashSet<int>()).ToArray();
        if (mesh.SegEdge.Length == 0 || mesh.FaceTriStart.Length < 2) return result;
        double size = (mesh.Max - mesh.Min).Length; double cell = Math.Max(size * 1e-6, 1e-9);
        (long, long, long) Key(float x, float y, float z) => ((long)Math.Round(x / cell), (long)Math.Round(y / cell), (long)Math.Round(z / cell));
        var owners = new Dictionary<(long, long, long), HashSet<int>>();
        var P = mesh.Positions; var I = mesh.Indices;
        for (int f = 0; f < mesh.FaceCount; f++)
            for (int t = mesh.FaceTriStart[f]; t < mesh.FaceTriStart[f + 1]; t++)
                for (int k = 0; k < 3; k++)
                {
                    int v = I[3 * t + k] * 3; var key = Key(P[v], P[v + 1], P[v + 2]);
                    if (!owners.TryGetValue(key, out var set)) owners[key] = set = [];
                    set.Add(f);
                }
        var E = mesh.EdgePoints;
        for (int e = 0; e + 1 < mesh.EdgeSegStart.Length; e++)
        {
            int first = mesh.EdgeSegStart[e], last = mesh.EdgeSegStart[e + 1];
            if (last <= first) continue;
            HashSet<int>? common = null;
            for (int s = first; s < last; s++)
                foreach (int o in new[] { 0, 3 })
                {
                    if (!owners.TryGetValue(Key(E[6 * s + o], E[6 * s + o + 1], E[6 * s + o + 2]), out var set)) { common = []; break; }
                    if (common == null) common = [.. set]; else common.IntersectWith(set);
                    if (common.Count == 0) break;
                }
            if (common != null) foreach (int f in common) result[f].Add(e);
        }
        return result;
    }

    /// <summary>
    /// After an edit, point fixtures and loads at the faces that replaced the ones they were on. A face that was split gives each piece the fixture or load;
    /// one with no successor is dropped from it, and the status line says so. A study that matched its body before the edit still matches afterwards.
    /// </summary>
    void CarryStudyFaces(SceneObject old, SceneObject? next)
    {
        if (next == null || !old.HasBrep || !next.HasBrep || ReferenceEquals(old, next)) return;
        var studies = Studies.Where(st => st.Has(old.Id) && st.Fixtures.Concat<object>(st.Loads).Any(x => FacesOf(x).Any(f => f.Obj == old.Id))).ToList();
        if (studies.Count == 0) return;
        int[] origin;
        try { origin = FaceStyleMap.Origins(old.Mesh, next.Mesh); } catch { return; }
        var successors = new Dictionary<int, List<int>>();
        for (int f = 0; f < origin.Length; f++) if (origin[f] >= 0) { if (!successors.TryGetValue(origin[f], out var l)) successors[origin[f]] = l = []; l.Add(f); }
        int dropped = 0;
        List<Simulation.FaceRef> Carry(List<Simulation.FaceRef> faces)
        {
            var result = new List<Simulation.FaceRef>();
            foreach (var r in faces)
            {
                if (r.Obj != old.Id) { result.Add(r); continue; }
                if (successors.TryGetValue(r.Face, out var to)) foreach (int f in to) { var n = new Simulation.FaceRef(r.Obj, f); if (!result.Contains(n)) result.Add(n); }
                else dropped++;
            }
            return result;
        }
        foreach (var st in studies)
        {
            string? before = null, after = null;
            try { if (st.ShapeSignature != null && st.BodyId == old.Id) { before = Simulation.StudySetup.Signature(old); after = Simulation.StudySetup.Signature(next); } } catch { }
            foreach (var f in st.Fixtures) f.Faces = Carry(f.Faces);
            foreach (var l in st.Loads) l.Faces = Carry(l.Faces);
            if (before != null && before == st.ShapeSignature) st.ShapeSignature = after;
        }
        if (dropped > 0) Say($"{dropped} fixture or load face{(dropped == 1 ? "" : "s")} had no matching face after this edit and {(dropped == 1 ? "was" : "were")} removed from the study.");
    }

    static List<Simulation.FaceRef> FacesOf(object x) => x switch { Simulation.Fixture f => f.Faces, Simulation.Load l => l.Faces, _ => [] };
}
