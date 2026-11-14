using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>Copy and paste of whole objects, and the copy that Alt-drag with the Move tool leaves behind.</summary>
public sealed partial class CadSession
{
    // shared by every open document, so a part can be pasted into another tab
    static List<SceneObject> _clipboard = [];

    static bool Copyable(SceneObject o) => !o.IsAnnotation && !o.IsReference && o.Asm == null;

    List<SceneObject> CopyableSelection() => _selection.Select(i => Find(i.Obj)).Where(o => o != null && Copyable(o!)).Select(o => o!).Distinct().ToList();

    /// <summary>True for an object Copy, Paste and Alt-drag can copy (not annotations, reference parts or assembly components).</summary>
    public bool IsCopyable(Guid id) => Find(id) is { } o && Copyable(o);

    public bool CanCopy => ActiveCommand == null && Sketch == null && CopyableSelection().Count > 0;
    public bool CanPaste => ActiveCommand == null && Sketch == null && _clipboard.Count > 0;

    /// <summary>Remember the selected objects. They are copied now, so later edits or deleting the originals change nothing.</summary>
    public bool CopySelection()
    {
        var objs = CopyableSelection();
        if (objs.Count == 0) { Say("Select an object to copy.", true); return false; }
        _clipboard = objs.Select(o => CloneObject(o, o.Name)).ToList();
        Say($"Copied {objs.Count} object(s). Paste with Ctrl+V.");
        return true;
    }

    /// <summary>Add copies of what was copied, in the same place, and select them so they can be moved away.</summary>
    public List<SceneObject> Paste()
    {
        if (_clipboard.Count == 0) { Say("Nothing has been copied.", true); return []; }
        Checkpoint("Paste", "duplicate");
        var copies = new List<SceneObject>();
        foreach (var o in _clipboard)
        {
            var c = CloneObject(o, NextName(StripNumber(o.Name)));
            Objects = Objects.Add(c); copies.Add(c);
        }
        _selection.Clear(); foreach (var c in copies) _selection.Add(SelItem.Of(c));
        RaiseScene(); SelectionChanged?.Invoke();
        Say($"Pasted {copies.Count} object(s) where they were copied from. Move them with the gizmo.");
        return copies;
    }

    /// <summary>Copies of whole objects left in place (the originals stay put while the copies are dragged away). Null when none can be copied.</summary>
    public List<SceneObject>? CopyForDrag(IEnumerable<Guid> ids)
    {
        var objs = ids.Select(Find).Where(o => o != null && Copyable(o!)).Select(o => o!).ToList();
        if (objs.Count == 0) return null;
        Checkpoint("Copy", "duplicate");
        var copies = new List<SceneObject>();
        foreach (var o in objs)
        {
            var c = CloneObject(o, NextName(StripNumber(o.Name)));
            Objects = Objects.Add(c); copies.Add(c);
        }
        RaiseScene();
        return copies;
    }
}
