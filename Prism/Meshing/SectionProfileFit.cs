using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Turns a measured outline (a mesh section, a region border, a silhouette) into the few sketch curves a designer would draw:
/// lines, arcs and circles from <see cref="LineArcFit"/>, then every run of short pieces that meet smoothly is replaced by one
/// spline when the spline follows the points within the tolerance and is the simpler description. Straight edges, true arcs and
/// sharp corners are left alone. Mesh guide, MSH-P06.
/// </summary>
public static class SectionProfileFit
{
    /// <summary>Largest bend (degrees) between two fitted pieces for them to be merged (a short leftover, or a chopped arc).</summary>
    public const double SmoothJointDeg = 35;

    /// <summary>
    /// Largest turn (degrees) of the measured outline across a joint for the pieces either side to count as one smooth run. The
    /// turn is measured on the points themselves, over <see cref="TurnWindow"/> tolerances each side: a curve that was chopped into
    /// lines turns only a little there, while a designed corner, even a mild one, turns by its whole angle.
    /// </summary>
    public const double SmoothTurnDeg = 12;
    public const double TurnWindow = 20;

    public static List<SketchSeg> Fit(IReadOnlyList<(double x, double y)> input, bool closed, double tol)
    {
        // a hole traced along triangle edges zig-zags around its circle: still one circle
        if (closed && input.Count >= 12 && WholeCircle(input, tol) is { } circle) return circle;
        var segs = LineArcFit.Fit(input, closed, tol);
        if (segs.Count < 2 && !(closed && segs.Count == 1)) return segs;
        if (closed && segs.Count == 2 && segs.All(s => s.IsArc)) return segs;
        var pts = input.ToList();
        if (closed && pts.Count > 1 && Dist(pts[0], pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
        var owner = Owners(pts, segs);
        int m = segs.Count;
        double window = Math.Max(TurnWindow * tol, 0.5);
        var smooth = Enumerable.Range(0, m).Select(k => k < (closed ? m : m - 1) && !Corner(pts, closed, segs[k].P1, window)).ToArray();
        bool Smooth(int k) => k >= 0 && k < m && smooth[k];
        // a blob: every joint is smooth. One closed spline when it needs fewer points than the pieces.
        if (closed && Enumerable.Range(0, m).All(Smooth))
        {
            if (ClosedSpline(pts, tol) is { } loop && loop.Through!.Count + 1 < Cost(segs)) return [loop];
            return AbsorbShort(ArcRuns(segs, pts, closed, tol), pts, closed, tol);
        }
        // walk the joints from a corner so no run straddles the seam of a closed outline
        int start = 0;
        if (closed) start = Enumerable.Range(0, m).First(k => !Smooth(k)) + 1;
        var order = Enumerable.Range(0, m).Select(k => (k + start) % m).ToList();
        var result = new List<SketchSeg>();
        int i = 0;
        while (i < m)
        {
            int j = i;
            while (j + 1 < m && Smooth(order[j])) j++;
            result.AddRange(Simplify(order.GetRange(i, j - i + 1), segs, pts, owner, tol));
            i = j + 1;
        }
        // after the splines: tight arcs chopped into lines become arcs again, short leftovers join a neighbour, and shallow
        // arcs the scan left at corners go
        result = AbsorbShort(ArcRuns(result, pts, closed, tol), pts, closed, tol);
        result = SharpenCorners(result, closed, window, p => Corner(pts, closed, p, window));
        return TangentFillets(result, closed, tol);
    }

    /// <summary>Largest bend (degrees) at a line-arc joint for the arc to be made exactly tangent to the line.</summary>
    public const double FilletBendDeg = 12;

    /// <summary>
    /// The greedy fit lets a straight edge run a little way into a fillet, so they meet at a slight angle. An arc between two
    /// such lines gets the classic fillet construction: its centre where the two lines, offset by the radius, cross, and its ends
    /// at the tangent points. With a line on one side only, the arc keeps its centre and takes the line's distance as radius.
    /// Applied only when the result stays close to the fitted arc, so tangency then holds without moving the measurement.
    /// </summary>
    static List<SketchSeg> TangentFillets(List<SketchSeg> segs, bool closed, double tol)
    {
        int m = segs.Count;
        for (int k = 0; k < m; k++)
        {
            var arc = segs[k];
            if (!arc.IsArc || m < 2) continue;
            int ib = (k + m - 1) % m, ia = (k + 1) % m;
            bool hasB = closed || k > 0, hasA = closed || k < m - 1;
            bool lineB = hasB && ib != k && IsLine(segs[ib]) && Bend(segs[ib], arc) <= FilletBendDeg;
            bool lineA = hasA && ia != k && IsLine(segs[ia]) && Bend(arc, segs[ia]) <= FilletBendDeg;
            if (!lineB && !lineA) continue;
            var c = arc.Center; double r = arc.Radius;
            if (lineB && lineA)
            {
                // centre: where both lines, moved towards the arc's centre by the radius, cross
                var (pb, db) = (segs[ib].P0, Unit((segs[ib].P1.x - segs[ib].P0.x, segs[ib].P1.y - segs[ib].P0.y)));
                var (pa, da) = (segs[ia].P0, Unit((segs[ia].P1.x - segs[ia].P0.x, segs[ia].P1.y - segs[ia].P0.y)));
                var ob = Toward(pb, db, c, r); var oa = Toward(pa, da, c, r);
                if (Intersect(ob, db, oa, da) is not { } c2 || Dist(c2, c) > Math.Max(3 * tol, 0.05 * r)) continue;
                c = c2;
            }
            else
            {
                var line = lineB ? segs[ib] : segs[ia];
                double r2 = Dist(c, Foot(line, c));
                if (Math.Abs(r2 - r) > 2 * tol) continue;
                r = r2;
            }
            var start = lineB ? Foot(segs[ib], c) : OnCircle(arc.P0, (c.x, c.y, r));
            var end = lineA ? Foot(segs[ia], c) : OnCircle(arc.P1, (c.x, c.y, r));
            segs[k] = ArcThrough(start, end, (c.x, c.y, r), arc.SweepDeg);
            if (hasB && ib != k) segs[ib] = Moved(segs[ib], end: start);
            if (hasA && ia != k) segs[ia] = Moved(segs[ia], start: end);
        }
        return segs;
    }

    static bool IsLine(SketchSeg s) => !s.IsArc && !s.IsSpline;

    /// <summary>The foot of the perpendicular from p onto a line piece's infinite line.</summary>
    static (double x, double y) Foot(SketchSeg line, (double x, double y) p)
    {
        var d = Unit((line.P1.x - line.P0.x, line.P1.y - line.P0.y));
        double t = (p.x - line.P0.x) * d.x + (p.y - line.P0.y) * d.y;
        return (line.P0.x + d.x * t, line.P0.y + d.y * t);
    }

    /// <summary>A point of the line through p along d, moved sideways by r towards c.</summary>
    static (double x, double y) Toward((double x, double y) p, (double x, double y) d, (double x, double y) c, double r)
    {
        var n = (x: -d.y, y: d.x);
        double side = Math.Sign((c.x - p.x) * n.x + (c.y - p.y) * n.y);
        return (p.x + n.x * r * side, p.y + n.y * r * side);
    }

    /// <summary>One smooth run: a spline when it needs fewer points than the pieces; otherwise split at its longest straight piece
    /// (or widest arc) and try each side.</summary>
    static IEnumerable<SketchSeg> Simplify(List<int> run, List<SketchSeg> segs, List<(double x, double y)> pts, int[] owner, double tol)
    {
        if (run.Count < 3) return run.Select(k => segs[k]);
        var pieces = run.Select(k => segs[k]).ToList();
        var span = Span(run, pts, owner);
        int most = Cost(pieces) - 2;
        if (span.Count >= 8 && most >= 1 && OpenSpline(span, pieces[0].P0, pieces[^1].P1, tol, most) is { } spline) return [spline];
        int longest = -1; double best = -1;
        for (int k = 0; k < pieces.Count; k++)
        {
            double score = pieces[k].IsArc ? 0.001 * Math.Abs(pieces[k].SweepDeg) : 1 + Length(pieces[k]);
            if (score > best) { best = score; longest = k; }
        }
        return Simplify(run.GetRange(0, longest), segs, pts, owner, tol)
            .Append(pieces[longest])
            .Concat(Simplify(run.GetRange(longest + 1, run.Count - longest - 1), segs, pts, owner, tol));
    }

    /// <summary>How many points a designer handles: one per line end, two per arc (end and centre), shared ends counted once.</summary>
    static int Cost(IReadOnlyList<SketchSeg> pieces) => pieces.Sum(p => p.IsArc ? 2 : 1) + 1;

    /// <summary>
    /// Shallow arcs (under 45 degrees) shorter than a few turn windows that touch a corner are the scan rounding that corner, not
    /// design: they go, and their neighbours meet at a sharp corner. Their centres were the stray dots near the outline.
    /// </summary>
    static List<SketchSeg> SharpenCorners(List<SketchSeg> segs, bool closed, double window, Func<(double x, double y), bool> isCorner)
    {
        for (int k = 0; k < segs.Count && segs.Count > 2; k++)
        {
            var s = segs[k];
            if (!s.IsArc || Math.Abs(s.SweepDeg) >= 45 || Length(s) >= 4 * window) continue;
            int m = segs.Count;
            bool hasBefore = closed || k > 0, hasAfter = closed || k < m - 1;
            if (!hasBefore || !hasAfter) continue;
            if (!isCorner(s.P0) && !isCorner(s.P1)) continue;
            int before = (k + m - 1) % m, after = (k + 1) % m;
            var a = segs[before]; var b = segs[after];
            // the neighbours meet where their end tangents cross: the sharp corner the scan rounded off
            (double x, double y) joint = s.Mid;
            if (Intersect(a.P1, Direction(a, atEnd: true), b.P0, Direction(b, atEnd: false)) is { } x && Dist(x, s.Mid) < 2 * Length(s)) joint = x;
            segs[before] = Moved(a, end: joint);
            segs[after] = Moved(b, start: joint);
            segs.RemoveAt(k);
            k--;
        }
        return segs;
    }

    static (double x, double y)? Intersect((double x, double y) pa, (double x, double y) da, (double x, double y) pb, (double x, double y) db)
    {
        double cr = da.x * db.y - da.y * db.x;
        if (Math.Abs(cr) < 1e-9) return null;
        double t = ((pb.x - pa.x) * db.y - (pb.y - pa.y) * db.x) / cr;
        return (pa.x + da.x * t, pa.y + da.y * t);
    }

    /// <summary>A closed outline that stays within 1.5 tolerances of one circle all the way round, as two half arcs.</summary>
    static List<SketchSeg>? WholeCircle(IReadOnlyList<(double x, double y)> input, double tol)
    {
        if (LineArcFit.FitCircleFast(input.ToList()) is not { } c || c.r < 4 * tol) return null;
        var residuals = input.Select(p => Math.Abs(Math.Sqrt((p.x - c.cx) * (p.x - c.cx) + (p.y - c.cy) * (p.y - c.cy)) - c.r)).ToArray();
        if (LineArcFit.RobustDeviation(residuals) > 1.5 * tol) return null;
        // it must go all the way round, not just be a shallow arc closed by a chord
        double turned = 0, prev = Math.Atan2(input[0].y - c.cy, input[0].x - c.cx);
        foreach (var p in input.Skip(1))
        {
            double a = Math.Atan2(p.y - c.cy, p.x - c.cx), d = a - prev;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            turned += d; prev = a;
        }
        if (Math.Abs(turned) < 1.9 * Math.PI) return null;
        double a0 = Math.Atan2(input[0].y - c.cy, input[0].x - c.cx), sg = Math.Sign(turned);
        (double, double) At(double a) => (c.cx + c.r * Math.Cos(a), c.cy + c.r * Math.Sin(a));
        return [new SketchSeg(true, At(a0), At(a0 + sg * Math.PI / 2), At(a0 + sg * Math.PI), (c.cx, c.cy), c.r, sg * 180),
                new SketchSeg(true, At(a0 + sg * Math.PI), At(a0 + sg * 1.5 * Math.PI), At(a0), (c.cx, c.cy), c.r, sg * 180)];
    }

    /// <summary>
    /// A tight arc the greedy fit chopped into short lines (each joint bending gently): the longest stretch of such pieces that one
    /// circle follows within the tolerance becomes that arc. Joints either side move onto the circle so the outline stays connected.
    /// </summary>
    static List<SketchSeg> ArcRuns(List<SketchSeg> segs, List<(double x, double y)> pts, bool closed, double tol)
    {
        double size = LineArcFit.Size(pts);
        for (int start = 0; start < segs.Count; start++)
        {
            int m = segs.Count;
            if (m < 3) break;
            SketchSeg? best = null; int bestEnd = -1;
            int[]? owner = null;
            for (int end = start + 1; end < (closed ? start + m - 1 : m); end++)
            {
                var prev = segs[(end - 1) % m]; var cur = segs[end % m];
                if (prev.IsSpline || cur.IsSpline) break;
                if (Bend(prev, cur) > SmoothJointDeg || Math.Sign(Turning(prev, cur)) != Math.Sign(Turning(segs[start % m], segs[(start + 1) % m]))) break;
                owner ??= Owners(pts, segs);
                var idx = Enumerable.Range(start, end - start + 1).Select(k => k % m).ToHashSet();
                var span = Enumerable.Range(0, pts.Count).Where(i => idx.Contains(owner[i])).Select(i => pts[i]).ToList();
                if (span.Count < 6 || LineArcFit.FitCircleFast(span) is not { } c || c.r > 0.5 * size) continue;
                var first = segs[start % m]; var last = cur;
                var arc = ArcThrough(OnCircle(first.P0, c), OnCircle(last.P1, c), c, Turning(first, segs[(start + 1) % m]));
                if (Math.Abs(arc.SweepDeg) < 15) continue;
                if (LineArcFit.RobustDeviation(span.Select(q => LineArcFit.MaxDeviation([q], [arc])).ToArray()) > tol) continue;
                best = arc; bestEnd = end;
            }
            if (best == null || bestEnd - start < 2) continue;      // only stretches of three or more pieces
            int count = bestEnd - start + 1, mm = segs.Count;
            int before = (start + mm - 1) % mm, after = (bestEnd + 1) % mm;
            if (closed || start > 0) segs[before] = Moved(segs[before], end: best.P0);
            if (closed || bestEnd + 1 < mm) segs[after] = Moved(segs[after], start: best.P1);
            var indices = Enumerable.Range(start, count).Select(k => k % mm).OrderByDescending(k => k).ToList();
            int insertAt = start % mm;
            foreach (int k in indices) segs.RemoveAt(k);
            insertAt = Math.Min(insertAt, segs.Count);
            if (indices.Any(k => k < start % mm)) insertAt = 0;      // the stretch wrapped round the seam of a closed outline
            segs.Insert(insertAt, best);
        }
        return segs;
    }

    /// <summary>Signed turn from one piece into the next (positive = left).</summary>
    static double Turning(SketchSeg a, SketchSeg b)
    {
        var da = Direction(a, atEnd: true); var db = Direction(b, atEnd: false);
        return da.x * db.y - da.y * db.x;
    }

    /// <summary>
    /// A short line left between two smooth neighbours (where the greedy fit let a line run a little into a curve) is merged into
    /// the longer neighbour: one arc through both ends, or one line, when that still follows the points within the tolerance.
    /// </summary>
    static List<SketchSeg> AbsorbShort(List<SketchSeg> segs, List<(double x, double y)> pts, bool closed, double tol)
    {
        bool changed = true;
        while (changed && segs.Count > 1)
        {
            changed = false;
            int m = segs.Count, joints = closed ? m : m - 1;
            for (int k = 0; k < joints && !changed; k++)
            {
                var a = segs[k]; var b = segs[(k + 1) % m];
                if (a.IsSpline || b.IsSpline || Bend(a, b) > SmoothJointDeg) continue;
                bool aShort = !a.IsArc && Length(a) < 0.25 * Length(b), bShort = !b.IsArc && Length(b) < 0.25 * Length(a);
                if (!aShort && !bShort) continue;
                var owner = Owners(pts, segs);
                var span = Enumerable.Range(0, pts.Count).Where(i => owner[i] == k || owner[i] == (k + 1) % m).Select(i => pts[i]).ToList();
                if (span.Count < 3) continue;
                SketchSeg? merged = null;
                var longer = aShort ? b : a;
                if (longer.IsArc && LineArcFit.FitCircleFast(span) is { } c)
                {
                    // the free circle; the outer joints move onto it and the neighbours follow, so the outline stays connected
                    var arc = ArcThrough(OnCircle(a.P0, c), OnCircle(b.P1, c), c, longer.SweepDeg);
                    if (LineArcFit.RobustDeviation(span.Select(q => LineArcFit.MaxDeviation([q], [arc])).ToArray()) <= tol) merged = arc;
                }
                else if (!longer.IsArc)
                {
                    // the least squares line; the outer joints move onto it
                    var (c0, d) = LeastSquaresLine(span);
                    (double x, double y) On((double x, double y) q) { double t = (q.x - c0.x) * d.x + (q.y - c0.y) * d.y; return (c0.x + d.x * t, c0.y + d.y * t); }
                    var p0 = On(a.P0); var p1 = On(b.P1);
                    var line = new SketchSeg(false, p0, ((p0.x + p1.x) / 2, (p0.y + p1.y) / 2), p1, (0, 0), 0, 0);
                    if (LineArcFit.RobustDeviation(span.Select(q => LineArcFit.MaxDeviation([q], [line])).ToArray()) <= tol) merged = line;
                }
                if (merged == null) continue;
                int before = (k + m - 1) % m, after = (k + 2) % m;
                if (closed || k > 0) segs[before] = Moved(segs[before], end: merged.P0);
                if (closed || k + 2 < m) segs[after] = Moved(segs[after], start: merged.P1);
                segs[k] = merged;
                segs.RemoveAt((k + 1) % m);
                changed = true;
            }
        }
        return segs;
    }

    static ((double x, double y) c, (double x, double y) d) LeastSquaresLine(List<(double x, double y)> p)
    {
        double mx = p.Average(q => q.x), my = p.Average(q => q.y), sxx = 0, sxy = 0, syy = 0;
        foreach (var q in p) { double dx = q.x - mx, dy = q.y - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
        double th = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return ((mx, my), (Math.Cos(th), Math.Sin(th)));
    }

    static (double x, double y) OnCircle((double x, double y) p, (double cx, double cy, double r) c)
    {
        double d = Math.Sqrt((p.x - c.cx) * (p.x - c.cx) + (p.y - c.cy) * (p.y - c.cy));
        return d < 1e-12 ? p : (c.cx + (p.x - c.cx) * c.r / d, c.cy + (p.y - c.cy) * c.r / d);
    }

    /// <summary>A neighbouring piece with one end moved to a shifted joint (lines stay straight, arcs keep their centre).</summary>
    static SketchSeg Moved(SketchSeg s, (double x, double y)? start = null, (double x, double y)? end = null)
    {
        var p0 = start ?? s.P0; var p1 = end ?? s.P1;
        if (s.IsSpline) return s with { P0 = p0, P1 = p1 };
        if (!s.IsArc) return s with { P0 = p0, P1 = p1, Mid = ((p0.x + p1.x) / 2, (p0.y + p1.y) / 2) };
        return ArcThrough(p0, p1, (s.Center.x, s.Center.y, s.Radius), s.SweepDeg);
    }

    static SketchSeg ArcThrough((double x, double y) a, (double x, double y) b, (double cx, double cy, double r) c, double sweepSign)
    {
        double a0 = Math.Atan2(a.y - c.cy, a.x - c.cx), a1 = Math.Atan2(b.y - c.cy, b.x - c.cx), sw = a1 - a0;
        if (sweepSign >= 0) { while (sw < 0) sw += 2 * Math.PI; } else { while (sw > 0) sw -= 2 * Math.PI; }
        double am = a0 + sw / 2;
        return new SketchSeg(true, a, (c.cx + c.r * Math.Cos(am), c.cy + c.r * Math.Sin(am)), b, (c.cx, c.cy), c.r, sw * 180 / Math.PI);
    }

    static double Length(SketchSeg s) => s.IsArc ? Math.Abs(s.SweepDeg) * Math.PI / 180 * s.Radius : Dist(s.P0, s.P1);

    /// <summary>The measured points that belong to a run, in order along the outline.</summary>
    static List<(double x, double y)> Span(List<int> run, List<(double x, double y)> pts, int[] owner)
    {
        var set = run.ToHashSet();
        int n = pts.Count;
        // start where the run's first piece begins, so a run wrapping the seam stays in order
        int first = Enumerable.Range(0, n).FirstOrDefault(i => owner[i] == run[0] && !set.Contains(owner[(i + n - 1) % n]), -1);
        if (first < 0) first = Array.IndexOf(owner, run[0]);
        var span = new List<(double x, double y)>();
        for (int k = 0; k < n; k++)
        {
            int i = (first + k) % n;
            if (set.Contains(owner[i])) span.Add(pts[i]);
            else if (span.Count > 0) break;
        }
        return span;
    }

    /// <summary>For each measured point, the fitted piece it lies closest to.</summary>
    static int[] Owners(List<(double x, double y)> pts, List<SketchSeg> segs)
    {
        var owner = new int[pts.Count];
        var polys = segs.Select(s => s.Polyline().ToList()).ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            double best = double.MaxValue;
            for (int k = 0; k < polys.Count; k++)
                for (int q = 0; q + 1 < polys[k].Count; q++)
                {
                    double d = LineArcFit.SegmentDistance(pts[i], polys[k][q], polys[k][q + 1]);
                    if (d < best) { best = d; owner[i] = k; }
                }
        }
        return owner;
    }

    /// <summary>
    /// A corner turns by more than <see cref="SmoothTurnDeg"/> across the window and does most of that turning right at the point:
    /// over a third of the window it still turns by at least 60% as much. A curve turns about a third as much over a third.
    /// </summary>
    static bool Corner(List<(double x, double y)> pts, bool closed, (double x, double y) at, double window)
    {
        double whole = Turn(pts, closed, at, window);
        return whole > SmoothTurnDeg && Turn(pts, closed, at, window / 3) >= 0.6 * whole;
    }

    /// <summary>How far (degrees) the measured outline turns across a point: the directions of the points within the window before
    /// and after it, each fitted as a straight line.</summary>
    static double Turn(List<(double x, double y)> pts, bool closed, (double x, double y) at, double window)
    {
        int n = pts.Count, c = 0; double best = double.MaxValue;
        for (int i = 0; i < n; i++) { double d = Dist(pts[i], at); if (d < best) { best = d; c = i; } }
        List<(double x, double y)> Side(int dir)
        {
            var side = new List<(double x, double y)> { pts[c] }; double walked = 0;
            for (int k = 1; k < n; k++)
            {
                int i = c + dir * k;
                if (closed) i = ((i % n) + n) % n; else if (i < 0 || i >= n) break;
                walked += Dist(side[^1], pts[i]); side.Add(pts[i]);
                if (walked >= window && side.Count >= 3) break;
                if (side.Count > n / 3) break;
            }
            return side;
        }
        var before = Side(-1); before.Reverse(); var after = Side(1);
        // too few points to tell (a sparse CAD outline): treat it as a corner, so nothing is merged across it
        if (before.Count < 3 || after.Count < 3) return 180;
        var da = Heading(before); var db = Heading(after);
        return Math.Acos(Math.Clamp(da.x * db.x + da.y * db.y, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>Direction of travel along points, from a total least squares line.</summary>
    static (double x, double y) Heading(List<(double x, double y)> p)
    {
        double mx = p.Average(q => q.x), my = p.Average(q => q.y), sxx = 0, sxy = 0, syy = 0;
        foreach (var q in p) { double dx = q.x - mx, dy = q.y - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
        double th = 0.5 * Math.Atan2(2 * sxy, sxx - syy); var d = (x: Math.Cos(th), y: Math.Sin(th));
        if ((p[^1].x - p[0].x) * d.x + (p[^1].y - p[0].y) * d.y < 0) d = (-d.x, -d.y);
        return d;
    }

    /// <summary>Angle (degrees) between the end direction of one piece and the start direction of the next.</summary>
    static double Bend(SketchSeg a, SketchSeg b)
    {
        var da = Direction(a, atEnd: true); var db = Direction(b, atEnd: false);
        double cos = Math.Clamp(da.x * db.x + da.y * db.y, -1, 1);
        return Math.Acos(cos) * 180 / Math.PI;
    }

    static (double x, double y) Direction(SketchSeg s, bool atEnd)
    {
        if (s.Samples is { Count: > 1 } q)
            return atEnd ? Unit((q[^1].x - q[^2].x, q[^1].y - q[^2].y)) : Unit((q[1].x - q[0].x, q[1].y - q[0].y));
        if (s.IsArc)
        {
            var p = atEnd ? s.P1 : s.P0; double rx = p.x - s.Center.x, ry = p.y - s.Center.y, sg = Math.Sign(s.SweepDeg);
            return Unit((-ry * sg, rx * sg));
        }
        return Unit((s.P1.x - s.P0.x, s.P1.y - s.P0.y));
    }

    // ------------------------------------------------------------ spline fitting

    /// <summary>Spline from a to b through as few interior points as follow the samples within the tolerance; null when none do.</summary>
    public static SketchSeg? OpenSpline(List<(double x, double y)> samples, (double x, double y) a, (double x, double y) b, double tol, int most = 24)
        => Search(m => FitStations(samples, a, b, closed: false, tol, m), 1, most);

    /// <summary>A closed spline through as few points as follow a closed outline within the tolerance; null when none do.</summary>
    public static SketchSeg? ClosedSpline(List<(double x, double y)> samples, double tol, int most = 24)
        => samples.Count < 12 ? null : Search(m => FitStations(samples, samples[0], samples[0], closed: true, tol, m), 3, most);

    /// <summary>The fewest stations that fit: double the count until one fits, then bisect back down.</summary>
    static SketchSeg? Search(Func<int, SketchSeg?> fit, int least, int most)
    {
        if (most < least) return null;
        int lo = least - 1, hi = least; SketchSeg? found;
        while (true)
        {
            int m = Math.Min(hi, most);
            if (fit(m) is { } ok) { found = ok; hi = m; break; }
            if (m == most) return null;
            lo = m; hi = Math.Min(most, Math.Max(m + 1, m * 2));
        }
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (fit(mid) is { } ok) { found = ok; hi = mid; } else lo = mid;
        }
        return found;
    }

    /// <summary>
    /// Each candidate count starts from local averages of the samples (which cancel noise) and is corrected a few times by the
    /// mean offset of the nearby samples from the curve, so the curve is a least-squares fit rather than one through raw points.
    /// </summary>
    static SketchSeg? FitStations(List<(double x, double y)> input, (double x, double y) a, (double x, double y) b, bool closed, double tol, int m)
    {
        int step = Math.Max(1, input.Count / 1500);
        var pts = input.Where((_, i) => i % step == 0).ToList();
        var s = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) s[i] = s[i - 1] + Dist(pts[i - 1], pts[i]);
        double length = s[^1] + (closed ? Dist(pts[^1], pts[0]) : 0);
        if (length < 1e-9) return null;
        {
            // closed: m stations spread round the whole loop; open: m interior stations between a and b
            double gap = closed ? length / m : length / (m + 1);
            var stations = new List<(double x, double y)>(); var windows = new List<int[]>();
            for (int j = 0; j < m; j++)
            {
                double at = closed ? gap * j : gap * (j + 1);
                int[] near = Enumerable.Range(0, pts.Count).Where(i => Circular(s[i], at, length, closed) <= gap / 2).ToArray();
                if (near.Length == 0) near = [Enumerable.Range(0, pts.Count).MinBy(i => Circular(s[i], at, length, closed))];
                windows.Add(near);
                stations.Add(Mean(near.Select(i => pts[i])));
            }
            List<(double x, double y)>? curve = null; double dev = double.MaxValue;
            for (int pass = 0; pass < 4; pass++)
            {
                curve = Curve(a, stations, b, closed);
                if (curve == null) break;
                dev = LineArcFit.RobustDeviation(pts.Select(q => DistToPolyline(q, curve)).ToArray());
                if (pass == 3 || dev <= tol) break;
                for (int j = 0; j < m; j++)
                {
                    double ox = 0, oy = 0;
                    foreach (int i in windows[j]) { var c = Closest(pts[i], curve); ox += pts[i].x - c.x; oy += pts[i].y - c.y; }
                    stations[j] = (stations[j].x + ox / windows[j].Length, stations[j].y + oy / windows[j].Length);
                }
            }
            if (curve == null || dev > tol) return null;
            if (closed) return new SketchSeg(false, stations[0], stations[m / 2], stations[0], (0, 0), 0, 0) { Through = stations.Skip(1).ToList(), Samples = curve, Closed = true };
            return new SketchSeg(false, a, stations[m / 2], b, (0, 0), 0, 0) { Through = stations, Samples = curve };
        }
    }

    static double Circular(double si, double at, double length, bool closed)
    {
        double d = Math.Abs(si - at);
        return closed ? Math.Min(d, length - d) : d;
    }

    static (double x, double y) Mean(IEnumerable<(double x, double y)> pts)
    {
        double x = 0, y = 0; int n = 0;
        foreach (var p in pts) { x += p.x; y += p.y; n++; }
        return (x / n, y / n);
    }

    static List<(double x, double y)>? Curve((double x, double y) a, List<(double x, double y)> stations, (double x, double y) b, bool closed)
    {
        try
        {
            var through = new List<Vec3>();
            if (!closed) through.Add(new Vec3(a.x, a.y, 0));
            through.AddRange(stations.Select(q => new Vec3(q.x, q.y, 0)));
            if (!closed) through.Add(new Vec3(b.x, b.y, 0));
            using var spline = Occ.Spline(through, closed);
            return Occ.CurveSamples(spline, 40 * (stations.Count + 1)).SelectMany(e => e).Select(q => (q.p.X, q.p.Y)).ToList();
        }
        catch (KernelException) { return null; }
    }

    static double DistToPolyline((double x, double y) p, List<(double x, double y)> line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++) best = Math.Min(best, LineArcFit.SegmentDistance(p, line[i], line[i + 1]));
        return best;
    }

    static (double x, double y) Closest((double x, double y) p, List<(double x, double y)> line)
    {
        (double x, double y) best = line[0]; double bd = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
            double f = l2 < 1e-24 ? 0 : Math.Clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / l2, 0, 1);
            var q = (a.x + dx * f, a.y + dy * f); double d = Dist(p, q);
            if (d < bd) { bd = d; best = q; }
        }
        return best;
    }

    static (double x, double y) Unit((double x, double y) v) { double l = Math.Sqrt(v.x * v.x + v.y * v.y); return l < 1e-12 ? (1, 0) : (v.x / l, v.y / l); }
    static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
}
