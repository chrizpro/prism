using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Sketch dimensions driven by formulas. The parameters panel, the sketch's own dimension box and other sketches all feed the same
/// evaluation (<see cref="SketchParameters"/>); here it is wired to this document's parameters and sketches, and to history replay so that
/// changing a parameter rebuilds the sketches and every feature built on them.
/// </summary>
public sealed partial class CadSession
{
    /// <summary>Dimension renames and formula changes made in the parameters panel, applied to each sketch version while history is replayed.</summary>
    IReadOnlyCollection<SketchDimEdit>? _sketchEdits;

    /// <summary>A named sketch's definition as formulas see it.</summary>
    sealed record SketchEntry(string Name, SketchModel Model);

    static double OtherSketchValue(IReadOnlyDictionary<Guid, SketchEntry> pool, Guid self, string key, string dimension)
    {
        var hits = pool.Where(p => p.Key != self && SketchParameters.ScopeKey(p.Value.Name) == key).Select(p => p.Value).ToList();
        if (hits.Count == 0) throw new FormatException($"Unknown sketch '{key}'. Refer to a sketch by its name without spaces, e.g. sketch1.{dimension}, and only to sketches made earlier.");
        if (hits.Count > 1) throw new FormatException($"More than one sketch is called '{key}'. Rename one of them.");
        return SketchParameters.FindValue(hits[0].Model, dimension) ?? throw new FormatException($"{hits[0].Name} has no dimension named '{dimension}'.");
    }

    static SketchParameters.Scope MakeScope(string? ownerName, Guid self, IReadOnlyDictionary<Guid, SketchEntry> pool, IReadOnlyDictionary<string, double> parameters) => new()
    {
        OwnerKey = ownerName == null ? null : SketchParameters.ScopeKey(ownerName),
        Parameter = name => parameters.TryGetValue(name, out var v) ? v : throw new FormatException($"Unknown parameter '{name}'."),
        Other = (key, dimension) => OtherSketchValue(pool, self, key, dimension),
    };

    Dictionary<Guid, SketchEntry> SketchPool(IEnumerable<SceneObject> objects)
    {
        var pool = new Dictionary<Guid, SketchEntry>();
        foreach (var o in objects) if (o.SketchData != null) pool[o.Id] = new(o.Name, o.SketchData);
        return pool;
    }

    /// <summary>The scope a sketch being edited evaluates its formulas in: the document parameters now, and the sketches that exist before this one.</summary>
    public SketchParameters.Scope SketchScopeFor(Guid? editing)
    {
        var self = editing ?? Guid.Empty;
        var owner = editing is { } id ? Find(id)?.Name : null;
        return MakeScope(owner, self, SketchPool(Objects), ParameterValues.Evaluate(DocumentParameters));
    }

    /// <summary>The curves of a sketch model, built the way finishing a sketch builds them.</summary>
    static Shape SketchCurves(SketchModel model, Plane3 plane)
    {
        var shapes = model.ToShapes(plane);
        return shapes.Count > 0 ? Occ.Profile(shapes, faces: false) : Occ.Vertex(plane.Origin);
    }

    /// <summary>
    /// While history is replayed: the sketches of one step whose dimensions are driven by formulas (or by pending panel edits) are evaluated again
    /// against the current parameters and the sketches already rebuilt, so a changed parameter flows through them. Fails with a reason, not an exception.
    /// </summary>
    bool TryReevaluateSketches(IEnumerable<SceneObject> sketches, out Dictionary<Guid, (SketchModel Model, Shape Shape)> fresh, out string problem)
    {
        fresh = new(); problem = "";
        var todo = sketches.Where(o => o.Kind == ObjKind.Sketch && o.SketchData != null && o.SketchPlane != null
            && (SketchParameters.HasFormulas(o.SketchData) || _sketchEdits?.Any(e => e.ObjectId == o.Id) == true)).ToList();
        if (todo.Count == 0) return true;
        try
        {
            var values = ParameterValues.Evaluate(DocumentParameters);
            var pool = SketchPool(Objects);
            foreach (var o in sketches) if (o.SketchData != null) pool[o.Id] = new(o.Name, o.SketchData);      // what this very step defines wins over the older version
            foreach (var o in todo)
            {
                var model = SketchParameters.Prepare(o.SketchData!, MakeScope(o.Name, o.Id, pool, values), o.Name, o.Id, _sketchEdits);
                pool[o.Id] = new(o.Name, model);
                fresh[o.Id] = (model, SketchCurves(model, o.SketchPlane!.Value));
            }
            return true;
        }
        catch (Exception e) when (e is FormatException or KernelException) { problem = e.Message; return false; }
    }

    /// <summary>
    /// Would these definitions and dimension edits change this step's sketches? Used before anything is touched, so a bad formula
    /// or an unsolvable sketch is refused up front. <paramref name="seen"/> carries the already-evaluated versions of earlier sketches.
    /// </summary>
    bool SketchesMoved(HistoryNode node, IReadOnlyDictionary<string, double> values, IReadOnlyCollection<SketchDimEdit>? edits, Dictionary<Guid, SketchEntry> seen)
    {
        var (added, modified, _) = node.Diff();
        bool moved = false;
        foreach (var o in added.Concat(modified))
        {
            if (o.Kind != ObjKind.Sketch || o.SketchData == null) continue;
            if (!SketchParameters.HasFormulas(o.SketchData) && edits?.Any(e => e.ObjectId == o.Id) != true) { seen[o.Id] = new(o.Name, o.SketchData); continue; }
            var pool = SketchPool(node.After);
            foreach (var (id, entry) in seen) pool[id] = entry;
            pool[o.Id] = new(o.Name, o.SketchData);
            var model = SketchParameters.Prepare(o.SketchData, MakeScope(o.Name, o.Id, pool, values), o.Name, o.Id, edits);
            seen[o.Id] = new(o.Name, model);
            if (!SketchParameters.SameDimensions(model, o.SketchData)) moved = true;
        }
        return moved;
    }

    /// <summary>
    /// What every sketch dimension would be with these parameter definitions and edits, keyed by (sketch, plane, constraint). Nothing is changed.
    /// Throws FormatException with the reason when a formula or sketch cannot be evaluated.
    /// </summary>
    public Dictionary<(Guid Sketch, int Section, int Constraint), double> PreviewSketchDimensions(IEnumerable<DocumentParameter> definitions, IReadOnlyCollection<SketchDimEdit>? edits)
    {
        var values = ParameterValues.Evaluate(definitions);
        var pool = SketchPool(Objects);
        var result = new Dictionary<(Guid Sketch, int Section, int Constraint), double>();
        foreach (var o in Objects)
        {
            if (o.Kind != ObjKind.Sketch || o.SketchData == null) continue;
            var model = SketchParameters.Prepare(o.SketchData, MakeScope(o.Name, o.Id, pool, values), o.Name, o.Id, edits);
            pool[o.Id] = new(o.Name, model);
            foreach (var (section, c) in SketchParameters.Dimensions(model)) result[(o.Id, section, c.Id)] = Math.Abs(c.Value);
        }
        return result;
    }
}
