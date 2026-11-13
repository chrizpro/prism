using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>
/// Groups: a name shared by several objects. The browser shows each group as a parent folder over its members, so a set of
/// objects can be hidden, shown, selected, coloured or removed together. A group is one level deep and lives on the objects
/// themselves, so undo, copies of edits and saved files keep it without anything extra.
/// </summary>
public sealed partial class CadSession
{
    /// <summary>The names of the groups that have members, in the order their first member appears.</summary>
    public IReadOnlyList<string> GroupNames => Objects.Select(o => o.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().Select(g => g!).ToList();

    /// <summary>The objects in a group.</summary>
    public IEnumerable<SceneObject> GroupMembers(string group) => Objects.Where(o => o.Group == group);

    string NewGroupName()
    {
        for (int i = 1; ; i++)
        {
            var name = "Group " + i;
            if (!Objects.Any(o => o.Group == name)) return name;
        }
    }

    /// <summary>The objects a group or ungroup would act on: whole objects in the selection.</summary>
    List<Guid> GroupableSelection() => _selection.Where(i => i.Kind == SelKind.Object).Select(i => i.Obj).Distinct()
        .Where(id => Find(id) is { IsReference: false }).ToList();

    public bool CanGroup => ActiveCommand == null && Sketch == null && GroupableSelection().Count >= 2;
    public bool CanUngroup => ActiveCommand == null && Sketch == null && GroupableSelection().Any(id => Find(id)?.Group != null);

    /// <summary>Put the selected objects in a new group (or the named one). Returns the group's name, or null when fewer than two objects are selected.</summary>
    public string? GroupSelection(string? name = null)
    {
        var ids = GroupableSelection();
        if (ids.Count < 2) { Say("Select two or more objects to group them.", true); return null; }
        name = string.IsNullOrWhiteSpace(name) ? NewGroupName() : name.Trim();
        Checkpoint("Group", "component");
        foreach (var id in ids)
        {
            int i = Objects.FindIndex(o => o.Id == id);
            if (i >= 0) Objects = Objects.SetItem(i, Objects[i].WithGroup(name));
        }
        RaiseScene();
        Say($"{ids.Count} objects grouped as {name}. Ungroup (Ctrl+Shift+G) takes selected objects out again.");
        return name;
    }

    /// <summary>Take the selected objects out of their groups, or, with names, dissolve those whole groups. The objects stay.</summary>
    public void Ungroup(IEnumerable<string>? groups = null)
    {
        List<Guid> members; string what;
        if (groups != null)
        {
            var names = groups.ToHashSet();
            members = Objects.Where(o => o.Group != null && names.Contains(o.Group)).Select(o => o.Id).ToList();
            what = names.Count == 1 ? $"{names.First()} ungrouped." : $"{names.Count} groups ungrouped.";
        }
        else
        {
            members = GroupableSelection().Where(id => Find(id)?.Group != null).ToList();
            what = members.Count == 1 ? "Object taken out of its group." : $"{members.Count} objects taken out of their groups.";
        }
        if (members.Count == 0) { Say("Nothing selected is in a group.", true); return; }
        Checkpoint("Ungroup", "component");
        foreach (var id in members)
        {
            int i = Objects.FindIndex(o => o.Id == id);
            if (i >= 0) Objects = Objects.SetItem(i, Objects[i].WithGroup(null));
        }
        RaiseScene();
        Say(what);
    }

    /// <summary>Rename a group. A name another group already has merges the two.</summary>
    public void RenameGroup(string from, string to)
    {
        to = to.Trim();
        if (to.Length == 0 || to == from) return;
        var members = Objects.Where(o => o.Group == from).Select(o => o.Id).ToList();
        if (members.Count == 0) return;
        Checkpoint("Rename group", "component");
        foreach (var id in members)
        {
            int i = Objects.FindIndex(o => o.Id == id);
            if (i >= 0) Objects = Objects.SetItem(i, Objects[i].WithGroup(to));
        }
        RaiseScene();
    }

    /// <summary>Select every member of a group.</summary>
    public void SelectGroup(string group, bool additive = false) => SelectObjects(GroupMembers(group).Select(o => o.Id).ToList(), additive);
}
