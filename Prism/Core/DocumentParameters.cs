using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Prism.Commands;

namespace Prism.Core;

public sealed record DocumentParameter(string Name, string Formula, bool Pinned = false);

/// <summary>Named scalar expressions. Length literals use mm; angle literals use degrees.</summary>
public static class ParameterValues
{
    public static Dictionary<string, double> Evaluate(IEnumerable<DocumentParameter> definitions)
    {
        var source = new Dictionary<string, DocumentParameter>(StringComparer.OrdinalIgnoreCase);
        string[] reserved = ["t", "pi", "e", "tau", "mm", "cm", "inch", "deg"];
        foreach (var p in definitions)
        {
            if (!Regex.IsMatch(p.Name, "^[a-zA-Z][a-zA-Z0-9_]*$") || reserved.Contains(p.Name.ToLowerInvariant()))
                throw new FormatException($"'{p.Name}' is not an available parameter name.");
            if (!source.TryAdd(p.Name, p)) throw new FormatException($"Duplicate parameter '{p.Name}'.");
        }
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double Resolve(string name)
        {
            if (result.TryGetValue(name, out var value)) return value;
            if (!source.TryGetValue(name, out var p)) throw new FormatException($"Unknown parameter '{name}'.");
            if (!visiting.Add(name)) throw new FormatException($"Circular dependency at '{name}'.");
            if (visiting.Count > 128) throw new FormatException("Parameter dependencies are too deep.");
            value = EvaluateFormula(p.Formula, Resolve);
            visiting.Remove(name); result[name] = value;
            return value;
        }
        foreach (var name in source.Keys) Resolve(name);
        return result;
    }

    public static double EvaluateFormula(string formula, Func<string, double> resolve)
    {
        var value = Expression.Parse(formula).Eval(name => name switch
        {
            "mm" or "deg" => 1, "cm" => 10, "inch" => 25.4, _ => resolve(name),
        });
        if (!double.IsFinite(value)) throw new FormatException("The expression must have a finite result.");
        return value;
    }
}

public sealed partial class CadSession
{
    public sealed record FeatureRebuildIssue(HistoryNode At, HistoryNode FailedStep, string Reason, List<SelItem>? Inputs = null,
        FeatureRecipe? AttemptedRecipe = null, HistoryNode? End = null, Dictionary<Guid, Guid>? ObjectMap = null);
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial FeatureRebuildIssue? RebuildIssue { get; set; }
    public ImmutableList<DocumentParameter> DocumentParameters { get; private set; } = [];
    public bool EditFailedFeature()
    {
        if (RebuildIssue is not { AttemptedRecipe: { } recipe } issue || issue.At != Current || Sketch != null || ActiveCommand != null) return false;
        var command = recipe.Make(); command.Factory = recipe.Make;
        command.EditMode = true; command.EditingNode = issue.FailedStep; command.ReturnNode = Current;
        command.RebuildEnd = issue.End ?? issue.FailedStep; command.RebuildMap = issue.ObjectMap;
        StartCommand(command); command.Restore(recipe);
        Say($"Repairing {issue.FailedStep.Label} on its changed input geometry.");
        return true;
    }
    public double ParameterValue(string formula)
    {
        var values = ParameterValues.Evaluate(DocumentParameters);
        return ParameterValues.EvaluateFormula(formula, name => values.TryGetValue(name, out var v) ? v : throw new FormatException($"Unknown parameter '{name}'."));
    }

    public bool ApplyDocumentParameters(IEnumerable<DocumentParameter> definitions)
    {
        if (Sketch != null || ActiveCommand != null) { Say("Finish the active operation before applying document parameters.", true); return false; }
        var proposed = definitions.ToImmutableList();
        try
        {
            var values = ParameterValues.Evaluate(proposed);
            double Eval(string formula) => ParameterValues.EvaluateFormula(formula, name => values.TryGetValue(name, out var v) ? v : throw new FormatException($"Unknown parameter '{name}'."));
            var path = HistoryPath().Past;
            HistoryNode? first = null;
            foreach (var node in path.Where(n => n.Recipe?.Expressions?.Count > 0))
            {
                var recipe = node.Recipe!; var command = recipe.Make();
                foreach (var (index, formula) in recipe.Expressions!)
                {
                    double value = Eval(formula);
                    if (index >= command.Params.Count || command.Params[index] is not NumberParam number || value < number.Min || value > number.Max)
                        throw new FormatException($"{node.Label}: '{formula}' is outside the allowed range.");
                    if (recipe.Values[index] is double old && Math.Abs(value - old) > 1e-10) first ??= node;
                }
            }
            if (DocumentParameters.SequenceEqual(proposed)) return true;
            var back = Current;
            if (first != null) GoTo(first.Parent!);
            DocumentParameters = proposed;
            Checkpoint("Document parameters", "feature-edit"); RaiseScene();
            if (first != null)
            {
                var recipe = first.Recipe!; var command = recipe.Make(); command.Factory = recipe.Make;
                command.EditMode = true; command.EditingNode = first; command.ReturnNode = back; command.RebuildEnd = back;
                StartCommand(command); command.Restore(recipe); CommitCommand();
                if (ActiveCommand != null)
                {
                    string why = Status; CancelCommand(); Say(why, true); return false;
                }
                return !StatusIsError;
            }
            Say("Document parameters updated."); return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        { Say(e.Message, true); return false; }
    }
}
