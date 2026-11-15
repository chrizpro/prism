using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>How the form was seen when a point of the cut was clicked: a camera at <see cref="Eye"/> looking along <see cref="Direction"/>, or a parallel view along it.</summary>
public sealed record KnifeView(Vec3 Eye, Vec3 Direction, bool Orthographic);

/// <summary>
/// The knife: cut a cage along a line drawn across it on screen. Each stretch of the line between two clicked points is a cutting sheet through the eye
/// and both points (or through both points along the view, in a parallel view). The sheet cuts the faces it crosses between the two points, so the
/// cut runs across the form the way the line looks on screen. Bends in the line become points inside the faces they fall in.
/// </summary>
public static class FormKnife
{
    sealed class Stretch
    {
        public Vec3 A, B, N; public KnifeView View = null!;
        public bool LowerFree, UpperFree;
        public double Lower(Vec3 x) => View.Orthographic ? Vec3.Dot(x - A, B - A) : Vec3.Dot(Vec3.Cross(A - View.Eye, x - View.Eye), N);
        public double Upper(Vec3 x) => View.Orthographic ? Vec3.Dot(B - x, B - A) : Vec3.Dot(Vec3.Cross(x - View.Eye, B - View.Eye), N);
    }

    sealed record Cross(int Vertex, long Edge, double T, Vec3 P);                 // a place the sheet meets a face's outline: at a point, or part way along an edge (T from the lower-numbered end)
    sealed record Chord(int Stretch, Vec3 Start, Vec3 End, Cross? StartAt, Cross? EndAt);
    sealed record Cutline(Cross From, List<Vec3> Bends, Cross To);

    /// <summary>
    /// Cut <paramref name="m"/> along the line through <paramref name="points"/>. <paramref name="views"/> holds the view each point was clicked in (the stretch ending at a point uses its view).
    /// With <paramref name="extend"/> the line runs on past its first and last points to the edges; without it a face the line ends inside is left alone.
    /// With <paramref name="cutThrough"/> faces turned away from the view are cut too. Returns the new points; <paramref name="facesCut"/> counts the faces split and <paramref name="endedInside"/> the faces left uncut because the line ended inside.
    /// </summary>
    public static List<int> Cut(SubdMesh m, IReadOnlyList<Vec3> points, IReadOnlyList<KnifeView> views, bool extend, bool cutThrough, double crease, out int facesCut, out int endedInside)
    {
        facesCut = 0; endedInside = 0;
        if (points.Count < 2 || views.Count != points.Count || m.F.Count == 0) return [];
        var (lo, hi) = m.Bounds();
        double scale = Math.Max((hi - lo).Length, 1), eps = 1e-9 * scale, joinTolerance = 1e-6 * scale;

        var stretches = new List<Stretch>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var view = views[i + 1]; Vec3 a = points[i], b = points[i + 1];
            var n = view.Orthographic ? Vec3.Cross(view.Direction, b - a) : Vec3.Cross(a - view.Eye, b - view.Eye);
            if (n.Length < 1e-12 * scale * scale) continue;
            stretches.Add(new Stretch { A = a, B = b, N = n.Normalized(), View = view, LowerFree = extend && i == 0, UpperFree = extend && i == points.Count - 2 });
        }
        if (stretches.Count == 0) return [];

        var edgePoints = new Dictionary<long, List<(double t, int v)>>();
        var created = new List<int>();
        int EdgePoint(long edge, double t)
        {
            if (!edgePoints.TryGetValue(edge, out var list)) edgePoints[edge] = list = [];
            foreach (var (tt, v) in list) if (Math.Abs(tt - t) < 1e-9) return v;
            var (a, b) = SubdMesh.Unkey(edge);
            int id = m.V.Count; m.V.Add(m.V[a] + (m.V[b] - m.V[a]) * t);
            list.Add((t, id)); created.Add(id);
            return id;
        }
        int VertexOf(Cross c) => c.Vertex >= 0 ? c.Vertex : EdgePoint(c.Edge, c.T);

        var cuts = new Dictionary<int, List<(int from, List<int> bends, int to)>>();
        int original = m.F.Count;
        var snapshot = m.V.ToList();                                      // positions before any new point is added
        for (int fi = 0; fi < original; fi++)
        {
            var face = m.F[fi];
            var normal = m.FaceNormal(fi); var centre = m.FaceCenter(fi);
            var chords = new List<Chord>();
            foreach (var (st, index) in stretches.Select((s, i) => (s, i)))
            {
                if (!cutThrough && !(st.View.Orthographic ? Vec3.Dot(normal, -st.View.Direction) > 0 : Vec3.Dot(normal, st.View.Eye - centre) > 0)) continue;
                // where the sheet meets the face's outline
                var dist = face.Select(v => Vec3.Dot(snapshot[v] - st.A, st.N)).ToArray();
                if (dist.All(d => Math.Abs(d) <= eps)) continue;
                var crossings = new List<Cross>();
                for (int k = 0; k < face.Length; k++)
                {
                    int a = face[k], b = face[(k + 1) % face.Length]; double da = dist[k], db = dist[(k + 1) % face.Length];
                    if (Math.Abs(da) <= eps) crossings.Add(new Cross(a, -1, 0, snapshot[a]));
                    else if (Math.Abs(db) > eps && da * db < 0)
                    {
                        double t = da / (da - db); if (a > b) t = 1 - t;
                        crossings.Add(new Cross(-1, SubdMesh.Key(a, b), t, snapshot[a] + (snapshot[b] - snapshot[a]) * (da / (da - db))));
                    }
                }
                if (crossings.Count < 2) continue;
                var line = Vec3.Cross(st.N, normal);
                var ordered = crossings.OrderBy(c => Vec3.Dot(c.P, line)).ToList();
                for (int k = 0; k + 1 < ordered.Count; k += 2)
                {
                    var c0 = ordered[k]; var c1 = ordered[k + 1];
                    if (Vec3.Dot(c1.P - c0.P, st.B - st.A) < 0) (c0, c1) = (c1, c0);      // run each chord the way the line was drawn
                    if (c0.Vertex >= 0 && c1.Vertex >= 0 && Adjacent(face, c0.Vertex, c1.Vertex)) continue;          // the sheet runs along an edge
                    // keep the part of the chord between the two ends of this stretch of the line
                    (double u0, double u1, bool ok) Range(bool useFreeEnds)
                    {
                        double a0 = 0, a1 = 1; bool good = true;
                        void Clip(Func<Vec3, double> g, bool free)
                        {
                            if (free || !good) return;
                            double g0 = g(c0.P), g1 = g(c1.P), tol = eps * scale;
                            if (Math.Abs(g1 - g0) < 1e-15) { if (g0 < -tol) good = false; return; }
                            double root = -g0 / (g1 - g0);
                            if (g1 > g0) a0 = Math.Max(a0, root); else a1 = Math.Min(a1, root);
                        }
                        Clip(st.Lower, useFreeEnds && st.LowerFree); Clip(st.Upper, useFreeEnds && st.UpperFree);
                        return (a0, a1, good && a1 - a0 >= 1e-9);
                    }
                    // Running the cut on to the edges only carries it across the faces the drawn line itself reaches. Without this the
                    // extended sheet also cut every other face it happened to pass through, beyond the ends of the line.
                    if ((st.LowerFree || st.UpperFree) && !Range(false).ok) continue;
                    var (u0, u1, ok) = Range(true);
                    if (!ok) continue;
                    var p0 = c0.P + (c1.P - c0.P) * u0; var p1 = c0.P + (c1.P - c0.P) * u1;
                    chords.Add(new Chord(index, p0, p1, u0 <= 1e-9 ? c0 : null, u1 >= 1 - 1e-9 ? c1 : null));
                }
            }
            if (chords.Count == 0) continue;

            // join the chords of neighbouring stretches at the bends, and keep the lines that run from one side of the face to the other
            var lines = new List<Cutline>(); var current = new List<Chord>();
            bool dangling = false;
            void Flush()
            {
                if (current.Count == 0) return;
                if (current[0].StartAt is { } from && current[^1].EndAt is { } to)
                    lines.Add(new Cutline(from, current.Skip(1).Select(c => c.Start).ToList(), to));
                else dangling = true;
                current.Clear();
            }
            foreach (var chord in chords.OrderBy(c => c.Stretch))
            {
                if (current.Count > 0 && current[^1].EndAt == null && chord.StartAt == null && (current[^1].End - chord.Start).Length <= joinTolerance && chord.Stretch == current[^1].Stretch + 1) current.Add(chord);
                else { Flush(); current.Add(chord); }
            }
            Flush();
            if (dangling && lines.Count == 0) { endedInside++; continue; }
            if (lines.Count == 0) continue;
            cuts[fi] = lines.Select(l => (VertexOf(l.From), l.Bends.Select(b => { int id = m.V.Count; m.V.Add(b); created.Add(id); return id; }).ToList(), VertexOf(l.To))).ToList();
        }
        if (created.Count == 0 && cuts.Count == 0) return [];

        // a crease on a split edge carries over to its pieces
        foreach (var (edge, list) in edgePoints)
            if (m.Crease.TryGetValue(edge, out var c) && c > 0)
            {
                var (a, b) = SubdMesh.Unkey(edge); m.Crease.Remove(edge);
                var chain = new List<int> { a }; chain.AddRange(list.OrderBy(x => x.t).Select(x => x.v)); chain.Add(b);
                for (int i = 0; i + 1 < chain.Count; i++) m.Crease[SubdMesh.Key(chain[i], chain[i + 1])] = c;
            }

        var result = new List<int[]>();
        for (int fi = 0; fi < original; fi++)
        {
            var face = m.F[fi];
            var poly = new List<int>();
            for (int i = 0; i < face.Length; i++)
            {
                int a = face[i], b = face[(i + 1) % face.Length];
                poly.Add(a);
                if (edgePoints.TryGetValue(SubdMesh.Key(a, b), out var list))
                    poly.AddRange((a < b ? list.OrderBy(x => x.t) : list.OrderByDescending(x => x.t)).Select(x => x.v));
            }
            if (!cuts.TryGetValue(fi, out var lines)) { result.Add(poly.ToArray()); continue; }
            var pieces = new List<List<int>> { poly };
            bool split = false;
            foreach (var (from, bends, to) in lines)
            {
                var piece = pieces.FirstOrDefault(p => p.Contains(from) && p.Contains(to));
                if (piece == null || from == to) continue;
                int iu = piece.IndexOf(from), iw = piece.IndexOf(to);
                if (bends.Count == 0 && ((iw - iu + piece.Count) % piece.Count == 1 || (iu - iw + piece.Count) % piece.Count == 1)) continue;
                var a = new List<int>(); for (int i = iu; ; i = (i + 1) % piece.Count) { a.Add(piece[i]); if (i == iw) break; }
                var b = new List<int>(); for (int i = iw; ; i = (i + 1) % piece.Count) { b.Add(piece[i]); if (i == iu) break; }
                a.AddRange(Enumerable.Reverse(bends)); b.AddRange(bends);
                pieces.Remove(piece); pieces.Add(a); pieces.Add(b);
                split = true;
                if (crease > 0)
                {
                    var path = new List<int> { from }; path.AddRange(bends); path.Add(to);
                    for (int i = 0; i + 1 < path.Count; i++) m.Crease[SubdMesh.Key(path[i], path[i + 1])] = crease;
                }
            }
            if (split) facesCut++;
            foreach (var p in pieces) result.Add(p.ToArray());
        }
        m.F.Clear(); m.F.AddRange(result);
        return created;
    }

    static bool Adjacent(int[] face, int a, int b)
    {
        int ia = Array.IndexOf(face, a), ib = Array.IndexOf(face, b);
        if (ia < 0 || ib < 0) return false;
        int d = Math.Abs(ia - ib); return d == 1 || d == face.Length - 1;
    }
}
