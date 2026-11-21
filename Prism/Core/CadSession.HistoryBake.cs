using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>
/// Baking and removing history, for documents whose history has grown heavy. Baking makes a step the new start: everything up to it becomes
/// one starting state, the steps before it can no longer be undone or edited, and the steps after it stay as they were (editable, with their
/// recipes). Removing history from a body keeps its shape and makes it a plain body, as if it had been imported: one step swaps it for a copy
/// that no earlier step made, so editing an earlier feature of another body leaves it alone.
/// </summary>
public sealed partial class CadSession
{
    /// <summary>How many steps the whole history holds (every branch, the start not counted).</summary>
    public int HistoryStepCount()
    {
        int count = 0; var stack = new Stack<HistoryNode>(); stack.Push(Root);
        while (stack.Count > 0) { var n = stack.Pop(); foreach (var c in n.Children) { count++; stack.Push(c); } }
        return count;
    }

    /// <summary>How many steps baking at <paramref name="node"/> would merge away: every step not after it (side branches included), the step itself included.</summary>
    public int StepsBakedAt(HistoryNode node)
    {
        int after = 0; var stack = new Stack<HistoryNode>(); stack.Push(node);
        while (stack.Count > 0) { var n = stack.Pop(); foreach (var c in n.Children) { after++; stack.Push(c); } }
        return HistoryStepCount() - after;
    }

    /// <summary>
    /// Makes <paramref name="node"/> (a step on the path to the current one) the new start of the history. Returns false, with a message, when
    /// it cannot: a sketch or tool is open, the step is already the start, or it is not on the current path.
    /// </summary>
    public bool BakeHistory(HistoryNode node)
    {
        if (Sketch != null || ActiveCommand != null) { Say("Finish or cancel the current edit before baking the history.", true); return false; }
        if (node.IsRoot) { Say("That is already the start of the history.", true); return false; }
        bool onPath = false; for (var n = Current; n != null; n = n.Parent) if (n == node) { onPath = true; break; }
        if (!onPath) { Say("Bake history from a step on the current path (go to it first).", true); return false; }
        int merged = StepsBakedAt(node);
        string? drawing = null; for (var n = node; n != null && drawing == null; n = n.Parent) drawing = n.DrawingState;
        var start = new HistoryNode
        {
            Id = node.Id, Label = $"Baked history ({merged} step{(merged == 1 ? "" : "s")})", Icon = "new", Time = node.Time,
            After = node.After, Parameters = node.Parameters, Pin = node.Pin, DrawingState = drawing,
        };
        var map = new Dictionary<HistoryNode, HistoryNode> { [node] = start };
        foreach (var child in node.Children) start.Children.Add(CopyStep(child, start, map));
        start.Preferred = node.Preferred != null && map.TryGetValue(node.Preferred, out var pref) ? pref : null;
        Root = start;
        Current = map[Current];
        // the saved state, if it was baked away, no longer exists: the document counts as changed
        _cleanNode = map.TryGetValue(_cleanNode, out var clean) ? clean : start;
        Dirty = true;
        TimelineChanged?.Invoke();
        Say($"Baked {merged} step{(merged == 1 ? "" : "s")} into the start. Steps after it are still editable.");
        return true;
    }

    static HistoryNode CopyStep(HistoryNode src, HistoryNode parent, Dictionary<HistoryNode, HistoryNode> map)
    {
        var copy = new HistoryNode
        {
            Id = src.Id, Parent = parent, Label = src.Label, Icon = src.Icon, Time = src.Time, After = src.After, Parameters = src.Parameters,
            Pin = src.Pin, DrawingState = src.DrawingState, Recipe = src.Recipe,
        };
        map[src] = copy;
        foreach (var c in src.Children) copy.Children.Add(CopyStep(c, copy, map));
        copy.Preferred = src.Preferred != null && map.TryGetValue(src.Preferred, out var p) ? p : null;
        return copy;
    }

    /// <summary>
    /// Keeps these bodies' shapes and makes them plain: one history step replaces each with an identical copy that no earlier step made. The
    /// earlier steps stay in the history and stay editable for the other bodies, but editing them no longer changes these bodies. Returns the
    /// new ids (the copies keep name, colour, material and appearance).
    /// </summary>
    public List<Guid> RemoveHistoryFrom(IEnumerable<Guid> ids)
    {
        var bodies = ids.Select(Find).Where(o => o is { Kind: ObjKind.Solid or ObjKind.Surface, HasBrep: true }).Cast<SceneObject>().ToList();
        if (bodies.Count == 0) { Say("Pick a solid or surface to remove its history.", true); return []; }
        if (Sketch != null || ActiveCommand != null) { Say("Finish or cancel the current edit first.", true); return []; }
        Checkpoint(bodies.Count == 1 ? $"Remove history: {bodies[0].Name}" : $"Remove history: {bodies.Count} bodies", "new");
        var fresh = new List<Guid>();
        var list = Objects;
        foreach (var b in bodies)
        {
            int at = list.FindIndex(o => o.Id == b.Id);
            var copy = b.AsPlainCopy();
            list = list.SetItem(at, copy);
            fresh.Add(copy.Id);
        }
        Objects = list;
        PruneSelection();
        RaiseScene();
        Say(bodies.Count == 1 ? $"{bodies[0].Name} is now a plain body; earlier steps no longer change it." : $"{bodies.Count} bodies are now plain bodies.");
        return fresh;
    }
}
