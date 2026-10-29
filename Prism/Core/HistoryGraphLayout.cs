using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>Stable Git-style lanes: the first continuation stays in its lane; each fork gets a new lane.</summary>
public sealed class HistoryGraphLayout
{
    public sealed record Entry(HistoryNode Node, int Lane, int Column);
    public List<Entry> Entries { get; } = [];
    public int LaneCount { get; private set; } = 1;
    public int ColumnCount { get; private set; } = 1;

    public static HistoryGraphLayout Build(HistoryNode root)
    {
        var graph = new HistoryGraphLayout();
        // Allocate fork lanes by creation ID, rather than traversal order. A new
        // fork near the root must not move older, deeper alternatives down a lane.
        var nodes = new List<HistoryNode>();
        var scan = new Stack<HistoryNode>();
        scan.Push(root);
        while (scan.TryPop(out var node))
        {
            nodes.Add(node);
            for (int i = node.Children.Count - 1; i >= 0; i--) scan.Push(node.Children[i]);
        }
        var forkLanes = nodes.Where(n => n.Parent != null && n.Parent.Children[0] != n)
            .OrderBy(n => n.Id).Select((n, i) => (Node: n, Lane: i + 1))
            .ToDictionary(e => e.Node, e => e.Lane);
        graph.LaneCount = forkLanes.Count + 1;
        var pending = new Stack<Entry>();
        pending.Push(new(root, 0, 0));
        while (pending.TryPop(out var entry))
        {
            graph.Entries.Add(entry);
            graph.ColumnCount = System.Math.Max(graph.ColumnCount, entry.Column + 1);
            var children = entry.Node.Children;
            // Assign lanes in creation order, independent of Current and Preferred.
            var next = new List<Entry>();
            for (int i = 0; i < children.Count; i++)
                next.Add(new(children[i], i == 0 ? entry.Lane : forkLanes[children[i]], entry.Column + 1));
            for (int i = next.Count - 1; i >= 0; i--) pending.Push(next[i]);
        }
        return graph;
    }
}
