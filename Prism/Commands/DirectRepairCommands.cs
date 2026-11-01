using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>What is wrong with a solid or surface body: the checks behind Check &amp; Repair, usable without a command.</summary>
public sealed record RepairReport(
    bool Valid, bool IsSolid, int Faces, int FreeEdges, IReadOnlyList<int> TinyFaces, IReadOnlyList<int> SliverFaces,
    int TinyEdges, IReadOnlyList<(int Keep, int Copy)> DuplicateFaces, int NonManifoldEdges)
{
    public int Problems => (Valid ? 0 : 1) + (IsSolid && FreeEdges > 0 ? 1 : 0) + TinyFaces.Count + SliverFaces.Count + TinyEdges + DuplicateFaces.Count + NonManifoldEdges;
}

public static class DirectRepair
{
    /// <summary>Run every check on a body. Faces and edges thinner than <paramref name="tol"/> (mm) count as tiny; a long thin face is a sliver.</summary>
    public static RepairReport Analyse(SceneObject body, double tol)
    {
        var shape = body.Shape; var chk = Occ.Check(shape);
        var tiny = new List<int>(); var sliver = new List<int>();
        int nf = shape.Count(SubKind.Face);
        for (int f = 0; f < nf; f++)
        {
            var face = shape.Sub(SubKind.Face, f);
            double area, perim = 0, longest = 0;
            try { area = Occ.FaceInfoOf(face).Area; } catch (KernelException) { continue; }
            for (int e = 0; e < face.Count(SubKind.Edge); e++)
            {
                try { double l = Occ.EdgeInfoOf(face.Sub(SubKind.Edge, e)).Length; perim += l; longest = Math.Max(longest, l); }
                catch (KernelException) { }
            }
            if (perim < 1e-12) continue;
            if (2 * area / perim >= tol) continue;      // mean width of the face: a sliver is long and thin, a tiny face is short too
            (longest <= 10 * tol ? tiny : sliver).Add(f);
        }
        int tinyEdges = 0;
        for (int e = 0; e < shape.Count(SubKind.Edge); e++)
        {
            try { var ei = Occ.EdgeInfoOf(shape.Sub(SubKind.Edge, e)); if (ei.Length > 1e-9 && ei.Length < tol) tinyEdges++; }
            catch (KernelException) { }
        }
        var (dups, nonManifold) = MeshChecks(body);
        return new RepairReport(chk.Valid, chk.Solids > 0, nf, chk.FreeEdges, tiny, sliver, tinyEdges, dups, nonManifold);
    }

    /// <summary>
    /// Mesh-based checks, like the face adjacency in FeatureFinder: faces whose triangulation has the same size and centre are duplicates, and an edge segment whose
    /// two ends are shared by the triangulations of three or more faces means the edge is non-manifold.
    /// </summary>
    static (List<(int, int)> dups, int nonManifold) MeshChecks(SceneObject body)
    {
        var m = body.Mesh; int nf = body.Shape.Count(SubKind.Face); var dups = new List<(int, int)>();
        double q = Math.Max((m.Max - m.Min).Length * 2e-5, 1e-6);
        static (long, long, long) Key(double x, double y, double z, double q) => ((long)Math.Round(x / q), (long)Math.Round(y / q), (long)Math.Round(z / q));
        var seen = new Dictionary<(long, long, long, long), int>(); var facesAt = new Dictionary<(long, long, long), HashSet<int>>();
        for (int f = 0; f < nf && f + 1 < m.FaceTriStart.Length; f++)
        {
            double sx = 0, sy = 0, sz = 0, w = 0; var used = new HashSet<int>();
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            {
                int a = m.Indices[3 * t] * 3, b = m.Indices[3 * t + 1] * 3, c = m.Indices[3 * t + 2] * 3;
                var A = new Vec3(m.Positions[a], m.Positions[a + 1], m.Positions[a + 2]); var B = new Vec3(m.Positions[b], m.Positions[b + 1], m.Positions[b + 2]); var C = new Vec3(m.Positions[c], m.Positions[c + 1], m.Positions[c + 2]);
                double ar = Vec3.Cross(B - A, C - A).Length * 0.5; var ct = (A + B + C) * (1.0 / 3);
                sx += ct.X * ar; sy += ct.Y * ar; sz += ct.Z * ar; w += ar;
                foreach (int vi in new[] { m.Indices[3 * t], m.Indices[3 * t + 1], m.Indices[3 * t + 2] })
                {
                    if (!used.Add(vi)) continue;
                    var key = Key(m.Positions[3 * vi], m.Positions[3 * vi + 1], m.Positions[3 * vi + 2], q);
                    if (!facesAt.TryGetValue(key, out var set)) facesAt[key] = set = [];
                    set.Add(f);
                }
            }
            if (w < 1e-12) continue;
            double cq = Math.Max(q * 50, 1e-4);
            var k = ((long)Math.Round(sx / w / cq), (long)Math.Round(sy / w / cq), (long)Math.Round(sz / w / cq), (long)Math.Round(w / cq / cq));
            if (seen.TryGetValue(k, out int first)) dups.Add((first, f)); else seen[k] = f;
        }
        int bad = 0;
        for (int e = 0; e + 1 < m.EdgeSegStart.Length; e++)
        {
            int s0 = m.EdgeSegStart[e], s1 = m.EdgeSegStart[e + 1];
            if (s1 <= s0) continue;
            int s = s0 + (s1 - s0) / 2;                                    // a middle segment: the faces that hold both its ends share this edge
            var k0 = Key(m.EdgePoints[6 * s], m.EdgePoints[6 * s + 1], m.EdgePoints[6 * s + 2], q);
            var k1 = Key(m.EdgePoints[6 * s + 3], m.EdgePoints[6 * s + 4], m.EdgePoints[6 * s + 5], q);
            if (facesAt.TryGetValue(k0, out var f0) && facesAt.TryGetValue(k1, out var f1) && f0.Count(f1.Contains) >= 3) bad++;
        }
        return (dups, bad);
    }

    /// <summary>Report lines for a report; level colours follow ResultLine (1 good, 2 warning, 3 problem).</summary>
    public static IEnumerable<ResultLine> Lines(RepairReport r, double tol)
    {
        yield return new ResultLine("Shape check", r.Valid ? "valid" : "invalid: the kernel finds errors in it", r.Valid ? 1 : 3);
        yield return new ResultLine("Body type", r.IsSolid ? $"solid, {r.Faces} faces" : $"surface, {r.Faces} faces", 0);
        if (r.IsSolid) yield return new ResultLine("Open edges", r.FreeEdges == 0 ? "none" : $"{r.FreeEdges} (a solid should have none)", r.FreeEdges == 0 ? 1 : 3);
        else yield return new ResultLine("Open edges", r.FreeEdges == 0 ? "none: the surface is closed" : $"{r.FreeEdges} (boundary, or gaps between faces)", r.FreeEdges == 0 ? 1 : 2);
        yield return new ResultLine($"Tiny faces (under {tol:0.###} mm)", r.TinyFaces.Count == 0 ? "none" : string.Join(", ", r.TinyFaces.Take(8).Select(f => "#" + (f + 1))) + (r.TinyFaces.Count > 8 ? " …" : ""), r.TinyFaces.Count == 0 ? 1 : 2);
        yield return new ResultLine("Sliver faces", r.SliverFaces.Count == 0 ? "none" : string.Join(", ", r.SliverFaces.Take(8).Select(f => "#" + (f + 1))) + (r.SliverFaces.Count > 8 ? " …" : ""), r.SliverFaces.Count == 0 ? 1 : 2);
        yield return new ResultLine("Tiny edges", r.TinyEdges == 0 ? "none" : r.TinyEdges.ToString(), r.TinyEdges == 0 ? 1 : 2);
        yield return new ResultLine("Duplicate faces", r.DuplicateFaces.Count == 0 ? "none" : string.Join(", ", r.DuplicateFaces.Take(6).Select(d => $"#{d.Keep + 1} = #{d.Copy + 1}")), r.DuplicateFaces.Count == 0 ? 1 : 3);
        yield return new ResultLine("Non-manifold edges", r.NonManifoldEdges == 0 ? "none" : r.NonManifoldEdges.ToString(), r.NonManifoldEdges == 0 ? 1 : 3);
    }
}

// =================================================================== Check & Repair
/// <summary>Check a solid or surface body for problems and fix what can be fixed: sew gaps, close open surfaces into a solid, remove tiny faces and merge split faces.</summary>
public sealed class CheckRepairCommand : Command
{
    readonly SelectionSlot _body;
    readonly NumberParam _tiny, _gap;
    readonly BoolParam _heal, _remove, _solid;

    public CheckRepairCommand()
    {
        _body = Slot(new SelectionSlot { Label = "Body to check (solid or surface)", Filter = SelFilter.Solid | SelFilter.SurfaceObj });
        _tiny = Num("Tiny size (faces and edges thinner than this)", 0.05, 1e-6, 1e4, 0.01);
        _gap = Num("Sewing gap (closes gaps up to this)", 0.01, 1e-6, 1e3, 0.01);
        _heal = Check("Heal: sew gaps, drop duplicate faces, merge split faces", false);
        _remove = Check("Remove tiny and sliver faces", false);
        _solid = Check("Close an open surface into a solid when it seals", false);
    }

    public override string Title => "Check & Repair";
    public override string Icon => "check";
    public override string OkText => "Repair";
    public override string Hint => "Pick an imported solid or surface body. The panel lists problems: an invalid shape, open edges, tiny or sliver faces, duplicate faces and non-manifold edges. Tick what to fix, then Repair: Heal sews gaps up to the sewing gap, drops duplicate faces and merges faces split on the same surface. Remove tiny faces deletes faces thinner than the tiny size and lets their neighbours close the gap. Closing turns a sealed set of surfaces into a solid. With nothing ticked it only reports.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_body.Items.Count == 0) return null;
        var body = S.Find(_body.Items[0].Obj);
        if (body == null || !body.HasBrep || body.Kind is not (ObjKind.Solid or ObjKind.Surface)) throw new FormatException("Check & Repair works on solid and surface bodies.");
        var before = DirectRepair.Analyse(body, _tiny.Value);
        foreach (var l in DirectRepair.Lines(before, _tiny.Value)) Results.Add(l);
        if (!_heal.Value && !_remove.Value && !_solid.Value) { Results.Add(new ResultLine("Repair", "tick an option to fix problems")); return null; }
        var shape = Repair(body, before, out var done);
        foreach (var d in done) Results.Add(d);
        if (ReferenceEquals(shape, body.Shape)) { Results.Add(new ResultLine("Repair", "nothing could be changed", 2)); return null; }
        var o = new CommandOutput { Label = "Check & repair", Icon = "check" };
        o.Replace.Add((body, shape));
        var pr = shape.Props(); var chk = Occ.Check(shape);
        Results.Add(new ResultLine("After repair", $"{(shape.IsValid ? "valid" : "still invalid")}, {chk.Faces} faces, {chk.FreeEdges} open edges, volume {pr.Volume:0.###} mm³", shape.IsValid && (chk.FreeEdges == 0 || chk.Solids == 0) ? 1 : 2));
        return o;
    }

    /// <summary>Apply the ticked fixes in order (sew, remove tiny faces, merge); each is kept only if the shape is no worse for it.</summary>
    Shape Repair(SceneObject body, RepairReport before, out List<ResultLine> done)
    {
        done = []; var shape = body.Shape;
        if (_heal.Value || _solid.Value)
        {
            bool wantSolid = _solid.Value && !before.IsSolid;
            bool needed = wantSolid || _heal.Value && (before.FreeEdges > 0 || !before.Valid || before.DuplicateFaces.Count > 0);
            if (needed)
            {
                var drop = _heal.Value ? before.DuplicateFaces.Select(d => d.Copy).ToHashSet() : [];
                var faces = Enumerable.Range(0, shape.Count(SubKind.Face)).Where(f => !drop.Contains(f)).Select(f => shape.Sub(SubKind.Face, f)).ToList();
                try
                {
                    var sewn = Occ.Sew(faces, _gap.Value, wantSolid || before.IsSolid);
                    var c0 = Occ.Check(shape); var c1 = Occ.Check(sewn);
                    bool better = sewn.IsValid && (!before.Valid || c1.FreeEdges < c0.FreeEdges || (wantSolid && sewn.HasSolids) || drop.Count > 0);
                    if (better && sewn.HasFaces)
                    {
                        shape = sewn;
                        done.Add(new ResultLine("Sewn", $"open edges {c0.FreeEdges} → {c1.FreeEdges}" + (drop.Count > 0 ? $", {drop.Count} duplicate face(s) dropped" : "") + (wantSolid && sewn.HasSolids ? ", now a solid" : ""), 1));
                    }
                    else if (wantSolid && !sewn.HasSolids) done.Add(new ResultLine("Close into solid", $"the surfaces do not seal within {_gap.Value:0.###} mm: {c1.FreeEdges} open edges remain", 2));
                    else done.Add(new ResultLine("Sew", "no improvement", 2));
                }
                catch (KernelException e) { done.Add(new ResultLine("Sew", e.Message, 3)); }
            }
        }
        if (_remove.Value)
        {
            var bad = TinyIndices(shape);
            if (bad.Length > 0)
            {
                try { var r = Occ.Defeature(shape, bad); if (r.IsValid) { shape = r; done.Add(new ResultLine("Tiny faces removed", bad.Length.ToString(), 1)); } else done.Add(new ResultLine("Tiny faces", "removing them gave an invalid shape, so it was skipped", 2)); }
                catch (KernelException)
                {
                    int ok = 0;
                    for (int i = 0; i < bad.Length; i++)                  // one at a time, re-measuring after each since face numbers change
                    {
                        var now = TinyIndices(shape); if (now.Length == 0) break;
                        try { var r = Occ.Defeature(shape, [now[0]]); if (r.IsValid) { shape = r; ok++; } } catch (KernelException) { }
                    }
                    done.Add(new ResultLine("Tiny faces removed", $"{ok} of {bad.Length}", ok == bad.Length ? 1 : 2));
                }
            }
            else done.Add(new ResultLine("Tiny faces", "none to remove", 1));
        }
        if (_heal.Value)
        {
            try
            {
                var merged = Occ.Defeature(shape, []);               // removes nothing, but the kernel merges faces on the same surface afterwards
                int n0 = shape.Count(SubKind.Face), n1 = merged.Count(SubKind.Face);
                if (merged.IsValid && n1 < n0) { shape = merged; done.Add(new ResultLine("Split faces merged", $"{n0} → {n1} faces", 1)); }
            }
            catch (KernelException) { }
        }
        return shape;
    }

    int[] TinyIndices(Shape shape)
    {
        var list = new List<int>();
        for (int f = 0; f < shape.Count(SubKind.Face); f++)
        {
            var face = shape.Sub(SubKind.Face, f); double perim = 0;
            try
            {
                double area = Occ.FaceInfoOf(face).Area;
                for (int e = 0; e < face.Count(SubKind.Edge); e++) perim += Occ.EdgeInfoOf(face.Sub(SubKind.Edge, e)).Length;
                if (perim > 1e-12 && 2 * area / perim < _tiny.Value) list.Add(f);
            }
            catch (KernelException) { }
        }
        return [.. list];
    }
}
