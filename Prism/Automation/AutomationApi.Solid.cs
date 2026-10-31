using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Automation;

// Read-only geometry queries on solids: find faces/edges by what they are (not by index), and cheap verification.
public sealed partial class AutomationApi
{
    /// <summary>Everything a face or edge query needs, computed once per query.</summary>
    sealed record Part(int Index, bool IsFace, string Type, Vec3 Center, Vec3 Min, Vec3 Max, double Size, double Radius, Vec3 Dir, bool HasDir, Vec3 A, Vec3 B);

    static bool IsBody(SceneObject o) => o.Form == null && o.Tri == null;

    SceneObject SolidObject(string key)
    {
        var o = FindObject(key);
        if (!IsBody(o)) throw new InvalidOperationException($"'{o.Name}' is a {(o.Form != null ? "freeform cage (use form_find)" : "mesh")}, not a solid or surface.");
        return o;
    }

    static Vec3 AvgFaceNormal(MeshData m, int face)
    {
        if (face + 1 >= m.FaceTriStart.Length) return default;
        Vec3 s = default;
        for (int t = m.FaceTriStart[face]; t < m.FaceTriStart[face + 1]; t++)
            for (int k = 0; k < 3; k++) { int v = m.Indices[3 * t + k] * 3; s += new Vec3(m.Normals[v], m.Normals[v + 1], m.Normals[v + 2]); }
        return s.Length > 1e-12 ? s.Normalized() : default;
    }

    List<Part> Parts(SceneObject o, bool faces)
    {
        var sh = o.Shape; var m = o.Mesh; var list = new List<Part>();
        int n = sh.Count(faces ? SubKind.Face : SubKind.Edge);
        for (int i = 0; i < n; i++)
        {
            using var sub = sh.Sub(faces ? SubKind.Face : SubKind.Edge, i);
            var pr = sub.Props();
            if (faces)
            {
                var fi = Occ.FaceInfoOf(sub);
                var nrm = fi.Normal.Length > 1e-9 ? fi.Normal : AvgFaceNormal(m, i);
                // A plane whose own axes are left-handed (a revolved cap, for one) reports its axis pointing into the body.
                // The tessellation always follows the face's true outward side, so a planar face takes that when the two disagree.
                if (fi.Type == "Plane" && AvgFaceNormal(m, i) is { } outward && outward.Length > 1e-9 && Vec3.Dot(nrm, outward) < 0) nrm = outward.Normalized();
                var c = pr.Center;
                try { c = Occ.Mass(sub).Cog; } catch { }
                list.Add(new(i, true, fi.Type, c, pr.Min, pr.Max, fi.Area, fi.R1, nrm, nrm.Length > 1e-9, default, default));
            }
            else
            {
                var ei = Occ.EdgeInfoOf(sub);
                bool line = ei.Type == "Line";
                var d = line ? (ei.End - ei.Start) : default;
                bool hasDir = line && d.Length > 1e-12;
                list.Add(new(i, false, ei.Type, pr.Center, pr.Min, pr.Max, ei.Length, ei.Radius, hasDir ? d.Normalized() : default, hasDir, ei.Start, ei.End));
            }
        }
        return list;
    }

    static bool Inside(Vec3 p, Vec3 lo, Vec3 hi, double tol = 1e-6) =>
        p.X >= lo.X - tol && p.X <= hi.X + tol && p.Y >= lo.Y - tol && p.Y <= hi.Y + tol && p.Z >= lo.Z - tol && p.Z <= hi.Z + tol;

    static double AngleDeg(Vec3 a, Vec3 b, bool signless)
    {
        double d = Math.Clamp(Vec3.Dot(a.Normalized(), b.Normalized()), -1, 1);
        if (signless) d = Math.Abs(d);
        return Math.Acos(d) * 180 / Math.PI;
    }

    JsonObject PartJson(SceneObject o, Part p)
    {
        var j = new JsonObject { [p.IsFace ? "face" : "edge"] = p.Index + 1, ["type"] = p.Type, ["center"] = Arr(p.Center) };
        if (p.IsFace) { j["area"] = Math.Round(p.Size, 4); if (p.HasDir) j["normal"] = Arr(p.Dir); if (p.Radius > 0) j["radius"] = Math.Round(p.Radius, 4); }
        else
        {
            j["length"] = Math.Round(p.Size, 4); if (p.Radius > 0) j["radius"] = Math.Round(p.Radius, 4);
            if (p.HasDir) j["direction"] = Arr(p.Dir);
            j["from"] = Arr(p.A); j["to"] = Arr(p.B);
        }
        return j;
    }

    static (Vec3 Min, Vec3 Max) BoxArg(JsonNode n)
    {
        var a = n.AsArray(); if (a.Count != 2) throw new ArgumentException("box must be [[x0,y0,z0],[x1,y1,z1]].");
        var p = V3(a[0]!); var q = V3(a[1]!);
        return (new(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), Math.Max(p.Z, q.Z)));
    }

    void RegisterSolid()
    {
        Add("solid_find", "Find faces or edges of a solid/surface by geometry instead of by index. kind: faces (default) | edges. " +
            "Filters (all optional, AND): type (Plane, Cylinder, Cone, Sphere, Torus, ... for faces; Line, Circle, ... for edges; prefix match), " +
            "facing [dx,dy,dz] + angle (faces whose normal is within angle degrees, default 5), direction [dx,dy,dz] + angle (straight edges parallel to it, default 5), " +
            "radius (+ radius_tol, default 0.001), box [[x0,y0,z0],[x1,y1,z1]] (centre inside), near [x,y,z] + distance (centre within distance), " +
            "of_face N (edges bounding face N, 1-based), min_size / max_size (area for faces, length for edges). " +
            "sort: size | -size | x | -x | y | -y | z | -z (centre coordinate), limit (default 40). select:true selects the hits (and they are listed 1-based, ready for run_command slots {object, face|edge}). " +
            "summary:true returns only counts per type. Returns total, shown and the hits with type, centre, area/length, normal/direction, radius.",
            Schema(["object"], ("object", "string", "Object name."), ("kind", "string", "faces | edges."), ("type", "string", "Surface/curve type."),
                ("facing", "array", "Face normal direction."), ("direction", "array", "Straight-edge direction."), ("angle", "number", "Tolerance in degrees for facing/direction."),
                ("radius", "number", "Radius of cylindrical/spherical faces or circular edges."), ("radius_tol", "number", "Radius tolerance."),
                ("box", "array", "Centre must lie inside this box."), ("near", "array", "Point for distance filter."), ("distance", "number", "Max centre distance from near."),
                ("of_face", "integer", "Only edges of this face (1-based)."), ("min_size", "number", "Min area/length."), ("max_size", "number", "Max area/length."),
                ("sort", "string", "Sort order."), ("limit", "integer", "Max hits listed (default 40)."), ("select", "boolean", "Select the hits."), ("summary", "boolean", "Counts only.")), a =>
            {
                var o = SolidObject(Str(a, "object"));
                bool faces = !Str(a, "kind", "faces").StartsWith("e", StringComparison.OrdinalIgnoreCase);
                var all = Parts(o, faces); var parts = all.AsEnumerable();

                if (a["type"] is { } ty) { var t = ty.ToString(); parts = parts.Where(p => p.Type.StartsWith(t, StringComparison.OrdinalIgnoreCase)); }
                double ang = Dbl(a, "angle", 5);
                if (a["facing"] is { } fc && faces) { var v = V3(fc); parts = parts.Where(p => p.HasDir && AngleDeg(p.Dir, v, false) <= ang); }
                if (a["direction"] is { } dr && !faces) { var v = V3(dr); parts = parts.Where(p => p.HasDir && AngleDeg(p.Dir, v, true) <= ang); }
                if (a["radius"] != null) { double r = Dbl(a, "radius", 0), tol = Dbl(a, "radius_tol", 0.001); parts = parts.Where(p => p.Radius > 0 && Math.Abs(p.Radius - r) <= tol); }
                if (a["box"] is { } bx) { var (lo, hi) = BoxArg(bx); parts = parts.Where(p => Inside(p.Center, lo, hi)); }
                if (a["near"] is { } nr) { var c = V3(nr); double d = Dbl(a, "distance", 1); parts = parts.Where(p => (p.Center - c).Length <= d); }
                if (a["min_size"] != null) { double v = Dbl(a, "min_size", 0); parts = parts.Where(p => p.Size >= v); }
                if (a["max_size"] != null) { double v = Dbl(a, "max_size", 0); parts = parts.Where(p => p.Size <= v); }
                if (a["of_face"] != null)
                {
                    if (faces) throw new ArgumentException("of_face applies to kind=edges.");
                    int f = (int)Dbl(a, "of_face", 0) - 1;
                    if (f < 0 || f >= o.Shape.Count(SubKind.Face)) throw new ArgumentException($"of_face {f + 1} is out of range (1..{o.Shape.Count(SubKind.Face)}).");
                    var mine = EdgesOfFace(o, f, all);
                    parts = parts.Where(p => mine.Contains(p.Index));
                }

                var hits = parts.ToList();
                var sort = Str(a, "sort", "");
                if (sort.Length > 0)
                {
                    bool desc = sort.StartsWith('-'); var key = sort.TrimStart('-').ToLowerInvariant();
                    Func<Part, double> k = key switch { "size" or "area" or "length" => p => p.Size, "x" => p => p.Center.X, "y" => p => p.Center.Y, "z" => p => p.Center.Z, _ => throw new ArgumentException("sort must be size, x, y or z (prefix - for descending).") };
                    hits = (desc ? hits.OrderByDescending(k) : hits.OrderBy(k)).ToList();
                }

                var res = new JsonObject { ["object"] = o.Name, ["kind"] = faces ? "faces" : "edges", ["total"] = hits.Count };
                var byType = new JsonObject();
                foreach (var g in hits.GroupBy(p => p.Type)) byType[g.Key] = g.Count();
                res["byType"] = byType;
                if (Bool(a, "select", false))
                {
                    S.ClearSelection();
                    foreach (var h in hits) S.SetSelection(new SelItem(o.Id, faces ? SelKind.Face : SelKind.Edge, h.Index), true);
                    res["selected"] = hits.Count;
                }
                if (!Bool(a, "summary", false))
                {
                    int limit = (int)Dbl(a, "limit", 40);
                    res["shown"] = Math.Min(limit, hits.Count);
                    res["hits"] = new JsonArray(hits.Take(limit).Select(h => (JsonNode)PartJson(o, h)).ToArray());
                }
                return res;
            });

        Add("solid_check", "Cheap verification of a body, or measurement between two things. With only 'object': validity, volume, area, bounding box, size, centre of mass, counts of faces/edges/shells/solids and open (free) edges. " +
            "With 'a' and 'b' (each an object name or {object, face|edge}): the minimum distance between them and the two closest points; if they overlap, 'overlap' is true and the volume of the overlap is reported when both are solids.",
            Schema(["object"], ("object", "string", "Object to check (also used as 'a' when only 'b' is given)."), ("a", "any", "First item for a distance."), ("b", "any", "Second item for a distance.")), a =>
            {
                var o = SolidObject(Str(a, "object"));
                var res = new JsonObject { ["object"] = o.Name };
                if (a["a"] != null || a["b"] != null)
                {
                    var ia = a["a"] != null ? ParseItem(a["a"]!) : SelItem.Of(o);
                    var ib = ParseItem(a["b"] ?? throw new ArgumentException("Give both 'a' and 'b'."));
                    var sa = ItemShape(ia, out var oa); var sb = ItemShape(ib, out var ob);
                    var (d, pa, pb) = Occ.Distance(sa, sb);
                    res["distance"] = Math.Round(d, 5); res["pointA"] = Arr(pa); res["pointB"] = Arr(pb);
                    if (d < 1e-6 && oa.Shape.HasSolids && ob.Shape.HasSolids && ia.Kind == SelKind.Object && ib.Kind == SelKind.Object && oa.Id != ob.Id)
                    {
                        res["overlap"] = true;
                        try { res["overlapVolume"] = Math.Round(Occ.Common(oa.Shape, ob.Shape).Props().Volume, 5); } catch { }
                    }
                    return res;
                }
                var sh = o.Shape; var pr = sh.Props(); var ck = Occ.Check(sh);
                res["valid"] = ck.Valid; res["isSolid"] = sh.HasSolids;
                if (sh.HasSolids) res["volume"] = Math.Round(pr.Volume, 5);
                res["area"] = Math.Round(pr.Area, 5); res["min"] = Arr(pr.Min); res["max"] = Arr(pr.Max); res["size"] = Arr(pr.Size);
                try { res["centerOfMass"] = Arr(Occ.Mass(sh).Cog); } catch { res["centerOfMass"] = Arr(pr.Center); }
                res["faces"] = ck.Faces; res["edges"] = ck.Edges; res["shells"] = ck.Shells; res["solids"] = ck.Solids; res["freeEdges"] = ck.FreeEdges;
                return res;
            });
    }

    Shape ItemShape(SelItem it, out SceneObject o)
    {
        o = S.Find(it.Obj) ?? throw new ArgumentException("That item is no longer in the document.");
        if (!IsBody(o)) throw new InvalidOperationException($"'{o.Name}' is not a solid or surface.");
        return it.Kind switch
        {
            SelKind.Face => o.Shape.Sub(SubKind.Face, it.Index),
            SelKind.Edge => o.Shape.Sub(SubKind.Edge, it.Index),
            _ => o.Shape,
        };
    }

    /// <summary>Edge indices that bound a face, matched geometrically (the kernel gives no identity link between a face's edges and the body's edge list).</summary>
    HashSet<int> EdgesOfFace(SceneObject o, int face, List<Part> candidates)
    {
        var set = new HashSet<int>();
        using var f = o.Shape.Sub(SubKind.Face, face);
        int n = f.Count(SubKind.Edge);
        for (int i = 0; i < n; i++)
        {
            using var e = f.Sub(SubKind.Edge, i);
            var info = Occ.EdgeInfoOf(e); var c = e.Props().Center;
            var best = candidates.Where(p => Math.Abs(p.Size - info.Length) < 1e-5 + 1e-6 * info.Length).OrderBy(p => (p.Center - c).Length).FirstOrDefault();
            if (best != null && (best.Center - c).Length < 1e-4 + 1e-6 * info.Length) set.Add(best.Index);
        }
        return set;
    }
}
