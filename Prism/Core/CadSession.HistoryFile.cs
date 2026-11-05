using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Prism.Commands;

namespace Prism.Core;

/// <summary>
/// The feature history, saved with the document: the steps that lead to the model on screen (not the side branches that were rolled back),
/// each with the command and values that made it, so a file opened later can still be re-edited or driven by its parameters.
/// A step stores only what it changed: the objects it added (by id), the earlier version of what it modified and what it removed.
/// The latest geometry is the document itself, and the earlier states are rebuilt from it by undoing the steps one by one.
/// </summary>
public sealed partial class CadSession
{
    sealed record SelItemDto(Guid Obj, int Kind, int Index);
    sealed record RecipeDto(string Key, string Title, List<string?> Values, List<List<SelItemDto>> Slots, Dictionary<int, string>? Expressions);
    sealed record RemovedDto(int Index, ObjDto Object);
    sealed record StepDto(string Label, string Icon, string? Pin, RecipeDto? Recipe, List<DocumentParameter> Parameters, List<Guid> Added, List<ObjDto> Modified, List<RemovedDto> Removed, string? DrawingState = null);
    sealed record HistoryDto(List<StepDto> Steps);

    static bool Persisted(SceneObject o) => o.Asm is not Cam.ToolpathInfo { Transient: true };

    static string? EncodeValue(object? v) => v switch
    {
        double d => "d|" + d.ToString("R", CultureInfo.InvariantCulture), int i => "i|" + i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "b|1" : "b|0", string s => "s|" + s, _ => null,
    };

    static object? DecodeValue(string? text)
    {
        if (text == null || text.Length < 2) return null;
        string body = text[2..];
        return text[0] switch
        {
            'd' => double.Parse(body, CultureInfo.InvariantCulture), 'i' => int.Parse(body, CultureInfo.InvariantCulture), 'b' => body == "1", 's' => body, _ => null,
        };
    }

    static RecipeDto? RecipeToDto(FeatureRecipe? r)
    {
        if (r == null) return null;
        try
        {
            string key = CommandCatalog.KeyOf(r.Make());
            if (CommandCatalog.Find(key) == null) return null;           // not a command that can be found again, so the step is kept without its recipe
            return new RecipeDto(key, r.Title, [.. r.Values.Select(EncodeValue)], [.. r.Slots.Select(s => s.Select(i => new SelItemDto(i.Obj, (int)i.Kind, i.Index)).ToList())], r.Expressions is { Count: > 0 } ? new(r.Expressions) : null);
        }
        catch (Exception) { return null; }
    }

    static FeatureRecipe? DtoToRecipe(RecipeDto? r)
    {
        if (r == null || CommandCatalog.Find(r.Key) is not { } make) return null;
        try { return new FeatureRecipe(make, r.Title, [.. r.Values.Select(DecodeValue)], [.. r.Slots.Select(s => s.Select(i => new SelItem(i.Obj, (SelKind)i.Kind, i.Index)).ToList())], r.Expressions == null ? null : new(r.Expressions)); }
        catch (Exception) { return null; }
    }

    HistoryDto? BuildHistoryDto()
    {
        var (past, _) = HistoryPath();
        if (past.Count < 2) return null;
        // the state after each step; the last one is the document itself
        var states = past.Select((n, i) => (i == past.Count - 1 ? Objects : n.After).Where(Persisted).ToList()).ToList();
        var steps = new List<StepDto>();
        for (int i = 1; i < past.Count; i++)
        {
            var before = states[i - 1]; var after = states[i];
            var oldById = before.ToDictionary(o => o.Id); var afterIds = after.Select(o => o.Id).ToHashSet();
            var added = after.Where(o => !oldById.ContainsKey(o.Id)).Select(o => o.Id).ToList();
            var modified = after.Where(o => oldById.TryGetValue(o.Id, out var old) && !ReferenceEquals(old, o)).Select(o => ToDto(oldById[o.Id])).ToList();
            var removed = before.Select((o, index) => (o, index)).Where(x => !afterIds.Contains(x.o.Id)).Select(x => new RemovedDto(x.index, ToDto(x.o))).ToList();
            var node = past[i];
            steps.Add(new StepDto(node.Label, node.Icon, node.Pin, RecipeToDto(node.Recipe), [.. node.Parameters], added, modified, removed, node.DrawingState));
        }
        return new HistoryDto(steps);
    }

    /// <summary>Rebuild the history from a file: undo each saved step from the loaded model to find the state before it.</summary>
    void LoadHistory(HistoryDto history)
    {
        var steps = history.Steps;
        var afters = new ImmutableList<SceneObject>[steps.Count + 1];
        afters[steps.Count] = Objects;
        for (int i = steps.Count; i >= 1; i--)
        {
            var step = steps[i - 1]; var objects = afters[i].ToList();
            var addedHere = step.Added.ToHashSet(); objects.RemoveAll(o => addedHere.Contains(o.Id));
            var earlier = step.Modified.Where(m => m.Id.HasValue).ToDictionary(m => m.Id!.Value);
            for (int k = 0; k < objects.Count; k++) if (earlier.TryGetValue(objects[k].Id, out var old)) objects[k] = FromDto(old);
            foreach (var gone in step.Removed.OrderBy(r => r.Index)) objects.Insert(Math.Min(gone.Index, objects.Count), FromDto(gone.Object));
            afters[i - 1] = [.. objects];
        }
        Root.After = afters[0];
        var previous = Root;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var node = new HistoryNode { Id = _nextNodeId++, Parent = previous, Label = step.Label, Icon = step.Icon, Pin = step.Pin, Recipe = DtoToRecipe(step.Recipe), Parameters = [.. step.Parameters], After = afters[i + 1], DrawingState = step.DrawingState };
            previous.Children.Add(node); previous.Preferred = node; previous = node;
        }
        Current = previous;
    }
}
