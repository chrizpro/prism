using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Animating a document parameter: the model is rebuilt with other values for its named parameters, to show or render a pose,
/// without touching the real document. The rebuild runs on a private copy of the history, so no steps appear in the real one.
/// </summary>
public sealed partial class CadSession
{
    public static string ParamKey(string name) => "@param:" + name;

    static readonly Regex LiteralFormula = new(@"^\s*(-?\d+(?:\.\d+)?)\s*(mm|cm|inch|deg)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The parameters that are plain numbers (not formulas of other parameters), with their value now and the unit they were written in.</summary>
    public List<(string Name, double Value, string Unit)> AnimatableParameters()
    {
        var res = new List<(string, double, string)>();
        if (DocumentParameters.Count == 0) return res;
        Dictionary<string, double> values;
        try { values = ParameterValues.Evaluate(DocumentParameters); } catch (FormatException) { return res; }
        foreach (var p in DocumentParameters)
            if (LiteralFormula.Match(p.Formula) is { Success: true } m && values.TryGetValue(p.Name, out var v))
                res.Add((p.Name, v, m.Groups[2].Value.Equals("deg", StringComparison.OrdinalIgnoreCase) ? "°" : "mm"));
        return res;
    }

    /// <summary>Parameter values from animation values keyed "@param:name".</summary>
    public static Dictionary<string, double> ParameterValuesFrom(IReadOnlyDictionary<string, (double v1, double v2)> pose)
    {
        var res = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, v) in pose) if (key.StartsWith("@param:", StringComparison.Ordinal)) res[key[7..]] = v.v1;
        return res;
    }

    // ---------------------------------------------------------------- the private copy the rebuilds run on
    CadSession? _probe;
    HistoryNode? _probeAt;
    object? _probeParameters;
    int _probeRuns;

    /// <summary>A copy of the document with its whole history, so parameters can be changed and the model rebuilt without touching this one.</summary>
    CadSession CloneForProbe()
    {
        var copy = new CadSession();
        var made = new Dictionary<HistoryNode, HistoryNode>();
        HistoryNode Copy(HistoryNode n, HistoryNode? parent)
        {
            var k = new HistoryNode { Id = n.Id, Parent = parent, Label = n.Label, Icon = n.Icon, After = n.After, Parameters = n.Parameters, Pin = n.Pin, Recipe = n.Recipe };
            made[n] = k;
            foreach (var child in n.Children) k.Children.Add(Copy(child, k));
            return k;
        }
        copy.Root = Copy(Root, null);
        copy.Current = made[Current];
        foreach (var n in made) if (n.Key.Preferred is { } p && made.TryGetValue(p, out var pk)) n.Value.Preferred = pk;
        copy._nextNodeId = _nextNodeId;
        foreach (var kv in _counters) copy._counters[kv.Key] = kv.Value;
        copy.Objects = Objects;
        copy.DocumentParameters = DocumentParameters;
        return copy;
    }

    /// <summary>
    /// The shapes the model's bodies would have with these parameter values (by name), or null when it cannot be rebuilt that way
    /// (a value outside what a feature allows, or a model with no parametric history). This document is not changed.
    /// </summary>
    public Dictionary<Guid, Shape>? ParameterPoseShapes(IReadOnlyDictionary<string, double> values)
    {
        if (values.Count == 0 || DocumentParameters.Count == 0) return null;
        // start from a fresh copy when the model changed, and now and then so the copy's own history does not pile up
        if (_probe == null || !ReferenceEquals(_probeAt, Current) || !ReferenceEquals(_probeParameters, DocumentParameters) || _probeRuns > 30)
        {
            _probe = CloneForProbe(); _probeAt = Current; _probeParameters = DocumentParameters; _probeRuns = 0;
        }
        _probeRuns++;
        var definitions = DocumentParameters.Select(p => values.TryGetValue(p.Name, out var v) ? p with { Formula = v.ToString("R", CultureInfo.InvariantCulture) } : p).ToList();
        if (!_probe.ApplyDocumentParameters(definitions)) { _probe = null; return null; }
        var rebuilt = new Dictionary<string, SceneObject>();
        foreach (var o in _probe.Objects) if (o.HasBrep) rebuilt[o.Name] = o;
        var res = new Dictionary<Guid, Shape>();
        foreach (var o in Objects)
            if (!o.IsAnnotation && o.Component == null && o.Kind is ObjKind.Solid or ObjKind.Surface && o.HasBrep && rebuilt.TryGetValue(o.Name, out var n) && !ReferenceEquals(n.Shape, o.Shape)) res[o.Id] = n.Shape;
        return res;
    }

    // ---------------------------------------------------------------- showing a pose in the document (for rendering frames)
    Dictionary<Guid, Shape>? _paramOriginals;

    /// <summary>Show the model with the parameter values of an animation pose. Call RestoreParameterShapes when done.</summary>
    public void ApplyParameterShapes(IReadOnlyDictionary<string, (double v1, double v2)> pose)
    {
        var values = ParameterValuesFrom(pose);
        if (values.Count == 0) return;
        var shapes = ParameterPoseShapes(values);
        if (shapes == null) return;
        _paramOriginals ??= [];
        foreach (var (id, shape) in shapes)
        {
            if (Find(id) is not { } o) continue;
            _paramOriginals.TryAdd(id, o.Shape);
            Update(id, x => x.WithShape(shape), false);
        }
    }

    public void RestoreParameterShapes()
    {
        if (_paramOriginals == null) return;
        var back = _paramOriginals; _paramOriginals = null;
        foreach (var (id, shape) in back) if (Find(id) != null) Update(id, x => x.WithShape(shape), false);
    }
}
