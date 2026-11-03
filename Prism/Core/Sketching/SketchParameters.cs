using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Prism.Core.Sketching;

/// <summary>A change to one dimension's name or formula, addressed by sketch object, drawing plane (0 = the root plane) and constraint id.</summary>
public sealed record SketchDimEdit(Guid ObjectId, int Section, int ConstraintId, string? Name, string? Formula);

/// <summary>
/// Parametric sketch dimensions. A dimension may carry a name and a formula, and the formula is written in one flat namespace that never lets
/// two sketches collide:
/// <list type="bullet">
/// <item><c>width</c> is this sketch's dimension called "width" and, when it has none, the document parameter "width" (the sketch's own name wins).</item>
/// <item><c>sketch1.width</c> is the dimension "width" of the sketch whose name, without spaces, is "Sketch 1" (so Sketch 1 and Sketch 2 can both have a "width").</item>
/// <item><c>doc.width</c> is always the document parameter, even when the sketch has a dimension with the same name.</item>
/// </list>
/// Lengths are in mm and angles in degrees, the same as everywhere else in the parameters panel.
/// </summary>
public static class SketchParameters
{
    static readonly string[] Reserved = ["t", "pi", "e", "tau", "mm", "cm", "inch", "deg", "doc"];
    static readonly Regex NamePattern = new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>What other formulas call a sketch: its object name in lower case without spaces or punctuation ("Sketch 1" becomes "sketch1").</summary>
    public static string ScopeKey(string objectName)
    {
        var key = new string(objectName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key.Length == 0 || char.IsDigit(key[0]) ? "s" + key : key;
    }

    public static bool ValidName(string name) => NamePattern.IsMatch(name) && !Reserved.Contains(name.ToLowerInvariant());

    /// <summary>Dimensions that a name or formula can drive. Fixed points are stored as dimensions but are not sizes.</summary>
    public static bool IsParametric(SkConstraint c) => c.IsDimension && c.Type != CType.Fixed;

    /// <summary>Every parametric dimension of a sketch: section 0 is the root plane, section n the n-th extra plane.</summary>
    public static IEnumerable<(int Section, SkConstraint C)> Dimensions(SketchModel root)
    {
        foreach (var c in root.Constraints) if (IsParametric(c)) yield return (0, c);
        for (int i = 0; i < root.Sections.Count; i++)
            foreach (var c in root.Sections[i].Model.Constraints) if (IsParametric(c)) yield return (i + 1, c);
    }

    public static SketchModel ModelOf(SketchModel root, int section) => section <= 0 ? root : root.Sections[section - 1].Model;
    public static IEnumerable<SketchModel> AllModels(SketchModel root) => new[] { root }.Concat(root.Sections.Select(s => s.Model));
    public static bool HasFormulas(SketchModel root) => Dimensions(root).Any(d => !string.IsNullOrWhiteSpace(d.C.Formula));

    /// <summary>How a dimension is shown in lists: its name, or its kind and number.</summary>
    public static string Label(SkConstraint c) => string.IsNullOrEmpty(c.Name) ? $"{SketchModel.Describe(c.Type)} {c.Id}" : c.Name;

    /// <summary>The value other formulas see for a named dimension (always positive for sizes; angles and signed distances by size).</summary>
    public static double? FindValue(SketchModel root, string dimensionName)
    {
        foreach (var d in Dimensions(root))
            if (string.Equals(d.C.Name, dimensionName, StringComparison.OrdinalIgnoreCase)) return Math.Abs(d.C.Value);
        return null;
    }

    /// <summary>Where names in a formula come from.</summary>
    public sealed class Scope
    {
        /// <summary>This sketch's own key (see <see cref="ScopeKey"/>); null for a sketch that has not been created yet.</summary>
        public string? OwnerKey { get; init; }
        /// <summary>A document parameter by name.</summary>
        public Func<string, double> Parameter { get; init; } = name => throw new FormatException($"Unknown parameter '{name}'.");
        /// <summary>A dimension of another sketch: (sketch key, dimension name).</summary>
        public Func<string, string, double> Other { get; init; } = (sketch, _) => throw new FormatException($"Unknown sketch '{sketch}'.");
    }

    /// <summary>Set a plain number as a dimension's value, keeping the direction that angles and signed distances carry in the sign.</summary>
    public static void SetLiteral(SkConstraint c, double v)
    {
        bool signed = c.Type is CType.Angle or CType.HDistance or CType.VDistance;
        c.Formula = null;
        c.Value = signed ? (v < 0 ? v : c.Value < 0 ? -v : v) : v;
    }

    /// <summary>Apply a formula's result to the dimension and report whether the value moved.</summary>
    static bool SetFromFormula(SkConstraint c, double x)
    {
        bool signed = c.Type is CType.Angle or CType.HDistance or CType.VDistance;
        if (!signed && x <= 0) throw new FormatException($"'{Label(c)}' must be positive (its formula gives {x.ToString("0.###", CultureInfo.InvariantCulture)}).");
        double value = signed ? (x < 0 ? x : c.Value < 0 ? -x : x) : x;
        if (Math.Abs(value - c.Value) <= 1e-9) return false;
        c.Value = value;
        return true;
    }

    /// <summary>
    /// Work out every formula-driven dimension of the sketch and store the results as their values (geometry is not touched here).
    /// Dimensions may use each other in any order; a loop is an error. Returns true when any value changed.
    /// </summary>
    public static bool EvaluateValues(SketchModel root, Scope scope)
    {
        var dims = Dimensions(root).ToList();
        var byName = new Dictionary<string, SkConstraint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, c) in dims)
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            if (!ValidName(c.Name)) throw new FormatException($"'{c.Name}' is not an available dimension name.");
            if (!byName.TryAdd(c.Name, c)) throw new FormatException($"Two dimensions of one sketch are named '{c.Name}'.");
        }
        var done = new Dictionary<SkConstraint, double>();
        var visiting = new HashSet<SkConstraint>();
        bool changed = false;

        double Resolve(SkConstraint c)
        {
            if (done.TryGetValue(c, out var known)) return known;
            if (string.IsNullOrWhiteSpace(c.Formula)) return done[c] = Math.Abs(c.Value);
            if (!visiting.Add(c)) throw new FormatException($"Circular dimension formula at '{Label(c)}'.");
            double x = ParameterValues.EvaluateFormula(c.Formula, Lookup);
            visiting.Remove(c);
            changed |= SetFromFormula(c, x);
            return done[c] = Math.Abs(c.Value);
        }

        double Lookup(string name)
        {
            int dot = name.IndexOf('.');
            if (dot < 0) return byName.TryGetValue(name, out var local) ? Resolve(local) : scope.Parameter(name);
            string head = name[..dot], tail = name[(dot + 1)..];
            if (head == "doc") return scope.Parameter(tail);
            if (scope.OwnerKey != null && head == scope.OwnerKey)
                return byName.TryGetValue(tail, out var own) ? Resolve(own) : throw new FormatException($"This sketch has no dimension named '{tail}'.");
            return scope.Other(head, tail);
        }

        foreach (var (_, c) in dims) Resolve(c);
        return changed;
    }

    /// <summary>Move the geometry of every plane to satisfy its (new) dimension values.</summary>
    public static void SolveAll(SketchModel root, string label)
    {
        foreach (var m in AllModels(root))
            if (!SketchSolver.Solve(m).Converged) throw new FormatException($"{label}: its constraints cannot be satisfied with these dimension values.");
    }

    /// <summary>Rename or re-formula dimensions as asked. A plain number is stored as the value itself, not as a formula.</summary>
    public static void ApplyEdits(SketchModel root, Guid objectId, IEnumerable<SketchDimEdit> edits)
    {
        foreach (var e in edits.Where(e => e.ObjectId == objectId))
        {
            if (e.Section < 0 || e.Section > root.Sections.Count) continue;
            var c = ModelOf(root, e.Section).Constraints.FirstOrDefault(x => x.Id == e.ConstraintId && IsParametric(x));
            if (c == null) continue;
            string? name = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name.Trim();
            if (name != null && !ValidName(name)) throw new FormatException($"'{name}' is not an available dimension name.");
            c.Name = name;
            string? formula = string.IsNullOrWhiteSpace(e.Formula) ? null : e.Formula.Trim();
            if (formula != null && double.TryParse(formula.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
            {
                if (literal <= 0 && c.Type is not (CType.Angle or CType.HDistance or CType.VDistance)) throw new FormatException($"'{Label(c)}' must be positive.");
                SetLiteral(c, literal);
            }
            else c.Formula = formula;
        }
    }

    static bool SameValues(SketchModel a, SketchModel b)
    {
        var x = Dimensions(a).ToList(); var y = Dimensions(b).ToList();
        if (x.Count != y.Count) return false;
        for (int i = 0; i < x.Count; i++)
            if (x[i].Section != y[i].Section || x[i].C.Id != y[i].C.Id || Math.Abs(x[i].C.Value - y[i].C.Value) > 1e-9) return false;
        return true;
    }

    /// <summary>True when two versions of a sketch have the same dimensions: the same names, formulas and values.</summary>
    public static bool SameDimensions(SketchModel a, SketchModel b)
    {
        if (!SameValues(a, b)) return false;
        var x = Dimensions(a).ToList(); var y = Dimensions(b).ToList();
        for (int i = 0; i < x.Count; i++)
            if (!string.Equals(x[i].C.Name ?? "", y[i].C.Name ?? "", StringComparison.OrdinalIgnoreCase) || (x[i].C.Formula ?? "").Trim() != (y[i].C.Formula ?? "").Trim()) return false;
        return true;
    }

    /// <summary>
    /// A copy of the sketch after the edits, with every formula evaluated and the geometry solved to match. The source is not changed.
    /// Throws FormatException when a formula is wrong or the constraints cannot hold with the new values.
    /// </summary>
    public static SketchModel Prepare(SketchModel source, Scope scope, string label, Guid objectId, IReadOnlyCollection<SketchDimEdit>? edits)
    {
        var work = source.Clone();
        if (edits is { Count: > 0 }) ApplyEdits(work, objectId, edits);
        EvaluateValues(work, scope);
        if (!SameValues(work, source)) SolveAll(work, label);
        return work;
    }
}
