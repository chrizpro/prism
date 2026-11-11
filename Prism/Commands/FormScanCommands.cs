using System;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Start a form cage around a scan: a box, ball or cylinder shrink-wrapped onto the mesh, ready to edit.</summary>
public sealed class FormFromScanCommand : MeshCommand
{
    readonly ChoiceParam _shape;
    readonly IntParam _res, _relax;
    readonly NumberParam _offset;

    public FormFromScanCommand() : base("Scan")
    {
        _shape = Choice("Start from", 1, "Box", "Ball (quadsphere)", "Cylinder");
        _res = Add(new IntParam { Label = "Cage resolution (quads per side)", Value = 8, Min = 2, Max = 30 });
        _relax = Add(new IntParam { Label = "Smooth the cage (passes)", Value = 6, Min = 0, Max = 60 });
        _offset = Num("Float above the scan", 0, -20, 20, 0.2);
    }

    public override string Title => "Freeform from Scan";
    public override string Hint => "Wraps a quad cage around the scan so you can keep editing it as a smooth form. Increase the resolution to capture more detail; use Edit Form's wrap and relax to refine.";
    public override string Icon => "form-scan";
    public override string OkText => "Create form";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!; var size = m.Size; var c = m.Center; int n = _res.Value;
        Row("Output", "Editable freeform cage; original sketches and feature history are not recovered");
        SubdMesh cage;
        double pad = 1.15;
        switch (_shape.Index)
        {
            case 0:
                cage = FormOps.Box(size.X * pad, size.Y * pad, size.Z * pad, n, n, n);
                for (int i = 0; i < cage.V.Count; i++) cage.V[i] += new Vec3(c.X, c.Y, c.Z - size.Z * pad / 2);
                break;
            case 1:
                cage = FormOps.Sphere(size.Length * 0.6, n);
                for (int i = 0; i < cage.V.Count; i++) cage.V[i] += c;
                break;
            default:
            {
                double r = Math.Max(size.X, size.Y) * 0.6;
                cage = FormOps.Cylinder(r, size.Z * pad, Math.Max(8, n * 2), n);
                for (int i = 0; i < cage.V.Count; i++) cage.V[i] += new Vec3(c.X, c.Y, c.Z - size.Z * pad / 2);
                break;
            }
        }
        // the scan under test is the one chosen here, not every mesh in the scene
        var wrapSession = new ScanScope(S, obj);
        wrapSession.Shrink(cage, c, _offset.Value);
        wrapSession.Relax(cage, _relax.Value, _offset.Value);
        cage.Level = 2;
        Row("Cage", $"{cage.V.Count} points, {cage.F.Count} quads", 1);
        var dev = cage.V.Average(v => m.Bvh.ClosestPoint(v)?.Dist ?? 0);
        Row("Average distance to the scan", N(dev, "mm"), dev < size.Length * 0.01 ? 1 : 2);
        var o = new CommandOutput { Label = "Form from scan", Icon = Icon };
        o.Add.Add(new(null, "Form", ObjKind.Form, null, null, cage));
        return o;
    }

    /// <summary>Wrap helpers limited to one chosen scan.</summary>
    sealed class ScanScope(CadSession s, SceneObject scan)
    {
        readonly TriMesh _m = scan.Tri!;
        (Vec3 p, Vec3 n)? Closest(Vec3 p) => _m.Bvh.ClosestPoint(p) is { } c ? (c.Point, _m.FaceNormal(c.Tri)) : null;

        public void Shrink(SubdMesh cage, Vec3 center, double offset)
        {
            for (int v = 0; v < cage.V.Count; v++)
            {
                var dir = (center - cage.V[v]).Normalized();
                if (_m.Bvh.Raycast(cage.V[v], dir) is { } h) cage.V[v] = cage.V[v] + dir * h.t + _m.FaceNormal(h.tri) * offset;
                else if (Closest(cage.V[v]) is { } c) cage.V[v] = c.p + c.n * offset;
            }
        }

        public void Relax(SubdMesh cage, int iterations, double offset)
        {
            var nb = new System.Collections.Generic.List<int>[cage.V.Count];
            for (int i = 0; i < nb.Length; i++) nb[i] = [];
            foreach (var (a, b) in cage.Edges()) { nb[a].Add(b); nb[b].Add(a); }
            for (int it = 0; it < iterations; it++)
            {
                var next = new Vec3[cage.V.Count];
                for (int v = 0; v < cage.V.Count; v++)
                    next[v] = nb[v].Count == 0 ? cage.V[v] : Vec3.Lerp(cage.V[v], nb[v].Aggregate(Vec3.Zero, (acc, u) => acc + cage.V[u]) / nb[v].Count, 0.5);
                for (int v = 0; v < cage.V.Count; v++) { cage.V[v] = next[v]; if (Closest(next[v]) is { } c) cage.V[v] = c.p + c.n * offset; }
            }
        }
    }
}
