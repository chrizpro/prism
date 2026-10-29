using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Expands real B-rep edge picks. Branch junctions stop a chain; a partial loop has two explicit end picks.</summary>
public static class SelectionChains
{
    const double Tolerance = 1e-5;
    static bool Near(Vec3 a, Vec3 b) => (a - b).Length < Tolerance;
    public static IReadOnlyList<SelItem> Expand(CadSession session, SelItem seed, bool tangent)
    {
        var obj = session.Find(seed.Obj) ?? throw new KernelException("The selected object no longer exists.");
        var edges = Enumerable.Range(0, obj.Shape.Count(SubKind.Edge)).Select(i => Occ.EdgeInfoOf(obj.Shape.Sub(SubKind.Edge, i))).ToArray();
        if (seed.Index < 0 || seed.Index >= edges.Length) throw new KernelException("The selected edge no longer exists.");
        var chosen = new HashSet<int> { seed.Index }; var pending = new Queue<int>(); pending.Enqueue(seed.Index);
        while (pending.TryDequeue(out int current))
        {
            foreach (var end in new[] { edges[current].Start, edges[current].End })
            {
                var next = Enumerable.Range(0, edges.Length).Where(i => i != current && (Near(end, edges[i].Start) || Near(end, edges[i].End))).ToList();
                if (next.Count != 1) continue; // Stop at intersections rather than guessing an outgoing branch.
                int i = next[0];
                if (tangent && !Smooth(session, seed.Obj, current, i, end)) continue;
                if (chosen.Add(i)) pending.Enqueue(i);
            }
        }
        return chosen.OrderBy(i => i).Select(i => new SelItem(seed.Obj, SelKind.Edge, i)).ToArray();
    }
    static bool Smooth(CadSession s, Guid id, int a, int b, Vec3 joint)
    {
        Vec3 Direction(int i)
        {
            var points = s.EdgePoints(new SelItem(id, SelKind.Edge, i));
            if (points.Count < 2) return Vec3.Zero;
            return Near(points[0], joint) ? (points[1] - points[0]).Normalized() : (points[^2] - points[^1]).Normalized();
        }
        var u = Direction(a); var v = Direction(b);
        return u.Length > .5 && v.Length > .5 && Vec3.Dot(u, v) < -Math.Cos(5 * Math.PI / 180);
    }
    public static void Pick(CadSession s, SelectionSlot slot, SelItem item)
    {
        if (slot.IsPartialLoop)
        {
            if (slot.ChainStart == null || slot.ChainEnd != null)
            {
                slot.ChainStart = item; slot.ChainEnd = null; slot.Items.Clear(); slot.TryAdd(item);
                s.Say("Pick the last edge of the partial loop. Use Other path to choose the other side.");
            }
            else
            {
                if (slot.ChainStart.Value.Obj != item.Obj) throw new KernelException("Pick both ends on the same sketch or curve.");
                slot.ChainEnd = item;
                try { UpdatePartial(s, slot); }
                catch { slot.ChainEnd = null; throw; }
            }
            return;
        }
        var expanded = Expand(s, item, slot.EdgeRule == 2);
        if (expanded.Count + slot.Items.Count(x => !expanded.Contains(x)) > slot.MaxCount)
            throw new KernelException($"This chain exceeds the selection limit of {slot.MaxCount}.");
        foreach (var edge in expanded) slot.TryAdd(edge);
    }
    public static void UpdatePartial(CadSession s, SelectionSlot slot)
    {
        if (slot.ChainStart is not { } start || slot.ChainEnd is not { } end) return;
        var obj = s.Find(start.Obj) ?? throw new KernelException("The profile no longer exists.");
        if (obj.Shape.Count(SubKind.Edge) > 2048) throw new KernelException("This object is too large for partial-loop selection. Use Single edges or Connected chain.");
        var edges = Enumerable.Range(0, obj.Shape.Count(SubKind.Edge)).Select(i => Occ.EdgeInfoOf(obj.Shape.Sub(SubKind.Edge, i))).ToArray();
        var neighbors = Enumerable.Range(0, edges.Length).Select(i => Enumerable.Range(0, edges.Length).Where(j => i != j &&
            (Near(edges[i].Start, edges[j].Start) || Near(edges[i].Start, edges[j].End) || Near(edges[i].End, edges[j].Start) || Near(edges[i].End, edges[j].End))).ToArray()).ToArray();
        if (start.Index < 0 || end.Index < 0 || start.Index >= edges.Length || end.Index >= edges.Length) throw new KernelException("A selected edge no longer exists.");
        var paths = new List<List<int>>();
        void Walk(int i, List<int> path)
        {
            if (paths.Count >= 2 || path.Count > 2048) return;
            if (i == end.Index) { paths.Add([.. path]); return; }
            if (neighbors[i].Length > 2) throw new KernelException("Partial loop needs a continuous loop without branch junctions. Use Single edges at an intersection.");
            foreach (int next in neighbors[i]) if (!path.Contains(next)) { path.Add(next); Walk(next, path); path.RemoveAt(path.Count - 1); }
        }
        Walk(start.Index, [start.Index]);
        if (paths.Count == 0) throw new KernelException("These edges are not connected.");
        var ordered = paths.OrderBy(p => p.Count).ToList();
        var chosen = slot.OtherPath ? ordered[^1] : ordered[0];
        if (chosen.Count > slot.MaxCount) throw new KernelException("The partial loop exceeds this selection limit.");
        var picks = chosen.Select(i => new SelItem(start.Obj, SelKind.Edge, i)).ToList();
        if (slot.Items.SequenceEqual(picks)) return;
        slot.Items.Clear(); foreach (var pick in picks) slot.TryAdd(pick);
    }
}
