using System;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

// ================================================================= Remesh
public sealed class MeshRemeshCommand : MeshCommand
{
    readonly BoolParam _auto, _sharp, _boundary, _keep;
    readonly NumberParam _len, _angle;
    readonly IntParam _iter, _target;
    readonly ChoiceParam _mode, _reduceTo;
    readonly NumberParam _percent;

    public MeshRemeshCommand() : base("Mesh")
    {
        _auto = Check("Use the mesh's average edge length", true);
        _len = Num("Target edge length", 1, 0.001, 10000, 0.1);
        _iter = Add(new IntParam { Label = "Iterations", Value = 5, Min = 1, Max = 30 });
        _sharp = Check("Keep sharp edges", true);
        _angle = Num("Sharp edge angle", 35, 5, 120, 5, "°");
        _boundary = Check("Keep open edges in place", true);
        _keep = Check("Keep the original too", false);
        _mode = Choice("Mode", 0, "Redistribute triangles", "Reduce Faces");
        _reduceTo = Choice("Reduce to", 0, "A percentage of the triangles", "A triangle count");
        _percent = Num("Keep", 25, 0.5, 100, 5, "%");
        _target = Add(new IntParam { Label = "Triangles", Value = 20000, Min = 4, Max = 5000000 });
        Src.Section = _mode.Section = "Mesh";
        PreviewButton();
    }

    public override string Title => "Remesh";
    public override string Hint => "Redistribute triangles by edge length or reduce the triangle count. Preview to check how the operation affects the mesh.";
    public override string Icon => "mesh-remesh";
    public override string OkText => "Remesh";
    protected override void UpdateVisibility()
    {
        bool reduce = _mode.Index == 1;
        _auto.IsVisible = _iter.IsVisible = _sharp.IsVisible = !reduce;
        _len.IsVisible = !reduce && !_auto.Value;
        _angle.IsVisible = !reduce && _sharp.Value;
        _reduceTo.IsVisible = reduce;
        _percent.IsVisible = reduce && _reduceTo.Index == 0;
        _target.IsVisible = reduce && _reduceTo.Index == 1;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        if (_mode.Index == 1)
        {
            if (m.TriCount == 0) return null;
            int target = _reduceTo.Index == 0 ? (int)(m.TriCount * _percent.Value / 100) : _target.Value;
            target = Math.Clamp(target, Math.Min(4, m.TriCount), m.TriCount);
            var reduced = Cached(Key("reduce", obj.Id, m.TriCount, target, _boundary.Value), () => MeshOps.Decimate(m, target, _boundary.Value));
            if (reduced == null) return null;
            foreach (var row in MeshDecimateCommand.ReductionReport(m, reduced)) Results.Add(row);
            return Output(obj, reduced, "Reduce mesh", "Reduced");
        }

        double mean = MeshRemesh.MeanEdge(m);
        Row("Average edge now", N(mean, "mm")); Row("Triangles now", Count(m.TriCount)); Row("Smallest angle now", $"{MeshRemesh.MinAngleDeg(m):0.#}°");
        double len = _auto.Value ? mean : _len.Value;
        if (len <= 0) return null;
        var opt = new MeshRemesh.Options(len, _iter.Value, _sharp.Value, _angle.Value, _boundary.Value);
        var res = Cached(Key(obj.Id, m.TriCount, len, _iter.Value, _sharp.Value, _angle.Value, _boundary.Value), () => MeshRemesh.Remesh(m, opt));
        if (res == null) return null;
        var before = MeshAnalysis.Report(m); var after = MeshAnalysis.Report(res);
        Row("Triangles", $"{Count(m.TriCount)} → {Count(res.TriCount)}", 1);
        Row("Smallest angle", $"{MeshRemesh.MinAngleDeg(m):0.#}° → {MeshRemesh.MinAngleDeg(res):0.#}°", 1);
        Row("Edges within the target band", $"{100 * MeshRemesh.EdgeUniformity(res, len):0}%");
        if (before.Closed) Row("Volume change", $"{(before.Volume > 0 ? 100 * (after.Volume - before.Volume) / before.Volume : 0):0.##}%");
        if (after.NonManifoldEdges > 0 || (before.Closed && !after.Closed)) Row("Result", "Not watertight - try fewer iterations or a smaller length", 3);
        return Output(obj, res, "Remesh", "Remeshed");
    }

    CommandOutput Output(SceneObject obj, TriMesh res, string label, string name)
    {
        var o = new CommandOutput { Label = label, Icon = Icon };
        if (_keep.Value) o.Add.Add(new(null, name, ObjKind.Mesh, obj.Color, null, null, obj.Look, res));
        else o.ReplaceMeshes.Add((obj, res));
        return o;
    }
}

// ================================================================= Boolean
[Legacy("Use Convert Mesh and the editing tools for its output.")]
public sealed class MeshBooleanCommand : MeshCommand
{
    readonly SelectionSlot _b;
    readonly ChoiceParam _op;
    readonly BoolParam _keep;

    public MeshBooleanCommand() : base("First mesh")
    {
        _b = Slot(new SelectionSlot { Label = "Second mesh", Filter = SelFilter.MeshObj });
        _op = Choice("Operation", 0, "Union (join)", "Subtract (first minus second)", "Intersect (common part)");
        _keep = Check("Keep the originals", false);
        PreviewButton();
    }

    public override string Title => "Mesh Boolean";
    public override string Hint => "Join two closed meshes, cut one out of the other, or keep only where they overlap. Both meshes must be watertight - run Repair first if they are not.";
    public override string Icon => "mesh-boolean";
    public override string OkText => "Combine";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var a = Meshes().FirstOrDefault(); if (a == null || _b.Items.Count == 0) return null;
        var b = S.Find(_b.Items[0].Obj);
        if (b?.Tri == null) return null;
        if (b.Id == a.Id) { Row("Second mesh", "pick a different mesh", 2); return null; }
        var op = (BooleanOp)_op.Index;
        var res = Cached(Key(a.Id, a.Tri!.TriCount, b.Id, b.Tri.TriCount, _op.Index), () => MeshBoolean.Run(a.Tri!, b.Tri, op));
        if (res == null) return null;
        if (res.Mesh == null) { Row("Cannot combine", res.Error ?? "failed", 3); return null; }
        Row("Triangles", Count(res.Mesh.TriCount), 1);
        Row("Volume", N(res.Mesh.Volume() / 1000, "cm³"), 1);
        if (res.Warning != null) Row("Note", res.Warning, 2);
        else Row("Result", "Closed and manifold", 1);
        var name = op switch { BooleanOp.Union => "Union", BooleanOp.Subtract => "Difference", _ => "Intersection" };
        var o = new CommandOutput { Label = "Mesh " + name.ToLowerInvariant(), Icon = Icon };
        o.Add.Add(new(null, name, ObjKind.Mesh, a.Color, null, null, a.Look, res.Mesh));
        if (!_keep.Value) { o.Remove.Add(a); o.Remove.Add(b); }
        return o;
    }
}
