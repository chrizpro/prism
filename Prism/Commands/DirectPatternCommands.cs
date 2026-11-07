using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Edit Pattern: click any hole, slot, pocket or boss that repeats in a line, grid or circle and change the whole pattern: spacing, number of instances,
/// or drop one instance. The clicked feature stays where it is; the others are filled in (or cut away) and made again.
/// </summary>
public sealed class EditPatternCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _spacing, _spacing2, _count, _count2, _angle, _remove;
    readonly double[] _filled = new double[5];
    int _filledFace = -1;

    public EditPatternCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "A feature of the pattern (click any hole, slot, pocket or boss in it)", Filter = SelFilter.Face, SmartPick = true, IntentIndex = (int)PickIntent.Pattern });
        _spacing = Num("Spacing", 0, 0, 1e6, 1); _spacing2 = Num("Spacing 2", 0, 0, 1e6, 1);
        _count = Num("Number of instances", 0, 0, 500, 1, ""); _count2 = Num("Number of instances 2", 0, 0, 500, 1, "");
        _angle = Num("Angular step", 0, 0, 360, 1, "°");
        _remove = Num("Remove instance (0 = none)", 0, 0, 100000, 1, "");
    }

    public override string Title => "Edit Pattern";
    public override string Icon => "pattern-linear";
    public override string OkText => "Apply";
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (Picked() is not { } pk) return [];
            var (_, feat, pat) = pk;
            if (pat.Kind == FeatureFinder.PatternKind.Circle)
            {
                var foot = pat.Centre + pat.Axis * Vec3.Dot(feat.Centre - pat.Centre, pat.Axis);
                var rel = feat.Centre - foot; if (rel.Length < 1e-6) return [];
                return [new(foot, pat.Axis.Normalized(), _angle, 1, rel.Normalized(), rel.Length)];
            }
            if (pat.Kind is FeatureFinder.PatternKind.Line or FeatureFinder.PatternKind.Grid)
            {
                var list = new List<HandleSpec> { new(feat.Centre, pat.V1.Normalized(), _spacing) };
                if (pat.Kind == FeatureFinder.PatternKind.Grid) list.Add(new(feat.Centre, pat.V2.Normalized(), _spacing2));
                return list;
            }
            return [];
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;
    public override string Hint => "Click any feature that belongs to a pattern. For a line or grid, set the spacing and the number of instances along each direction; for a circle, the angular step and the count. “Remove instance” deletes one instance by its number (counted from the first in the pattern, the clicked feature included). The clicked feature stays where it is. Leave a field at 0 to keep its current value.";

    (SceneObject body, Feat feat, FeatureFinder.PatternInfo pat)? Picked()
    {
        if (S == null || _faces.Items.Count == 0) return null;
        try
        {
            if (DirectHelpers.BodyOf(S, _faces.Items[0]) is not { } body || FeatureFinder.FeatureOf(body, _faces.Items[0].Index) is not { } feat) return null;
            return (body, feat, FeatureFinder.PatternOf(body, feat));
        }
        catch (KernelException) { return null; }
    }

    protected override void UpdateVisibility()
    {
        var pk = Picked(); var kind = pk?.pat.Kind ?? FeatureFinder.PatternKind.None;
        bool circle = kind == FeatureFinder.PatternKind.Circle, grid = kind == FeatureFinder.PatternKind.Grid;
        _spacing.IsVisible = kind is FeatureFinder.PatternKind.Line or FeatureFinder.PatternKind.Grid or FeatureFinder.PatternKind.None;
        _spacing2.IsVisible = _count2.IsVisible = grid;
        _angle.IsVisible = circle;
        if (pk == null || pk.Value.pat.Kind == FeatureFinder.PatternKind.None || _faces.Items[0].Index == _filledFace) return;
        // show the current values of the picked pattern, but keep numbers the user already typed
        _filledFace = _faces.Items[0].Index; var cur = Current(pk.Value.pat);
        NumberParam[] ps = [_spacing, _spacing2, _count, _count2, _angle];
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].Value == 0 || ps[i].Value == _filled[i]) ps[i].Value = cur[i];
            _filled[i] = cur[i];
        }
    }

    static double[] Current(FeatureFinder.PatternInfo p) => p.Kind switch
    {
        FeatureFinder.PatternKind.Circle => [0, 0, p.Cells.Max(c => c.A) + 1, 0, Math.Round(p.Step * 180 / Math.PI, 6)],
        FeatureFinder.PatternKind.Line => [p.V1.Length, 0, p.MaxA - p.MinA + 1, 1, 0],
        FeatureFinder.PatternKind.Grid => [p.V1.Length, p.V2.Length, p.MaxA - p.MinA + 1, p.MaxB - p.MinB + 1, 0],
        _ => [0, 0, 0, 0, 0],
    };

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (Picked() is not { } pk) return null;
        var (body, feat, pat) = pk;
        if (pat.Kind == FeatureFinder.PatternKind.None) throw new FormatException("That feature is not part of a pattern. Edit Feature can move, copy, resize or delete a single feature.");
        if (feat.Kind is FeatureKind.Fillet or FeatureKind.Face) throw new FormatException("Edit Pattern works on holes, slots, pockets and bosses.");
        bool boss = feat.Kind == FeatureKind.Boss;
        var cur = Current(pat);
        double Pick(NumberParam p, double keep) => p.Value > 0 ? p.Value : keep;
        double s1 = Pick(_spacing, cur[0]), s2 = Pick(_spacing2, cur[1]);
        int n1 = (int)Math.Round(Pick(_count, cur[2])), n2 = (int)Math.Round(Pick(_count2, cur[3]));
        double step = Pick(_angle, cur[4]) * Math.PI / 180;
        int remove = (int)Math.Round(_remove.Value);

        // the instances of the new layout, in order: (cell, placement of the picked feature's solid)
        var tool = FeatureEditCommand.ToolOf(body, feat);
        var cells = new List<(int A, int B)>();
        if (pat.Kind == FeatureFinder.PatternKind.Circle)
        {
            if (step <= 0 || step * n1 > 2 * Math.PI + 1e-6 + step) throw new FormatException("The angular step times the count must not go past a full turn.");
            for (int k = 0; k < n1; k++) cells.Add((k, 0));
        }
        else
        {
            n1 = Math.Max(n1, 1); n2 = pat.Kind == FeatureFinder.PatternKind.Grid ? Math.Max(n2, 1) : 1;
            int lo1 = pat.MinA, lo2 = pat.MinB;
            if (lo1 + n1 - 1 < 0) lo1 = -(n1 - 1);
            if (lo2 + n2 - 1 < 0) lo2 = -(n2 - 1);
            for (int b = 0; b < n2; b++) for (int a = 0; a < n1; a++) cells.Add((lo1 + a, lo2 + b));
        }
        if (remove > cells.Count) throw new FormatException($"The pattern has only {cells.Count} instances.");
        if (remove > 0 && cells[remove - 1] == (0, 0)) throw new FormatException("That instance is the feature you clicked. Remove another one, or use Edit Feature to delete it.");

        List<Shape> fill = [], strip = [], drill = [], grow = [];
        foreach (var m in pat.Others) (boss ? strip : fill).Add(FeatureEditCommand.ToolOf(body, m));
        for (int i = 0; i < cells.Count; i++)
        {
            var (a, b) = cells[i];
            if ((a, b) == (0, 0) || i + 1 == remove) continue;
            Shape moved;
            if (pat.Kind == FeatureFinder.PatternKind.Circle) moved = Occ.Rotate(tool, pat.Centre, pat.Axis, a * step * 180 / Math.PI);
            else
            {
                var off = pat.V1.Normalized() * (a * s1);
                if (pat.Kind == FeatureFinder.PatternKind.Grid) off += pat.V2.Normalized() * (b * s2);
                moved = Occ.Translate(tool, off);
            }
            (boss ? grow : drill).Add(moved);
        }
        var o = new CommandOutput { Label = "Edit pattern", Icon = "pattern-linear" };
        o.Replace.Add((body, DirectHelpers.Batch(DirectHelpers.Batch(body.Shape, fill, strip), grow, drill)));
        Results.Add(new ResultLine("Pattern", pat.Kind.ToString().ToLowerInvariant()));
        Results.Add(new ResultLine("Instances", $"{pat.Others.Count + 1} → {cells.Count - (remove > 0 ? 1 : 0)}"));
        return o;
    }
}

/// <summary>Mirror Feature: add a mirrored copy of picked holes, slots, pockets or bosses across a plane or flat face.</summary>
public sealed class MirrorFeatureCommand : Command
{
    readonly SelectionSlot _faces, _plane;

    public MirrorFeatureCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Features to mirror (click a hole, slot, pocket or boss)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _plane = Slot(new SelectionSlot { Label = "Mirror plane or flat face", Filter = SelFilter.Plane | SelFilter.Face });
    }

    public override string Title => "Mirror Feature";
    public override string Icon => "mirror";
    public override string OkText => "Apply";
    public override string Hint => "Pick the holes, slots, pockets or bosses to copy, then the plane or flat face to mirror them across. Each gets a mirrored twin on the other side; the originals stay. “Select by” can pick every feature of the same size or a whole pattern.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || _plane.Items.Count == 0) return null;
        var pl = S.PlaneOf(_plane.Items[0]) ?? throw new FormatException("Pick a plane or a flat face to mirror across.");
        var o = new CommandOutput { Label = "Mirror feature", Icon = "mirror" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Pick faces on a solid body.");
            var feats = grp.Select(i => FeatureFinder.FeatureOf(body, i.Index) ?? throw new FormatException("That face is gone.")).Distinct().ToList();
            List<Shape> grow = [], drill = [];
            foreach (var f in feats)
            {
                if (f.Kind is FeatureKind.Face or FeatureKind.Fillet) throw new FormatException("Mirror Feature works on holes, slots, pockets and bosses.");
                var twin = Occ.Mirror(FeatureEditCommand.ToolOf(body, f), pl.Origin, pl.Normal);
                (f.Kind == FeatureKind.Boss ? grow : drill).Add(twin);
            }
            o.Replace.Add((body, DirectHelpers.Batch(body.Shape, grow, drill)));
            Results.Add(new ResultLine("Features mirrored", feats.Count.ToString()));
        }
        return o;
    }
}
