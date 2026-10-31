using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prism.Core.Sketching;

/// <summary>
/// Turns a string into closed outline polygons (in mm, baseline at v=0, starting at u=0) using the glyph outlines of an installed TrueType font.
/// Only the 'glyf' flavour of TrueType is read; that covers Arial, Segoe UI, Times New Roman, Consolas, Courier New and most system fonts.
/// </summary>
public static class TextOutline
{
    public static readonly string[] Fonts = ["Arial", "Segoe UI", "Times New Roman", "Consolas", "Courier New", "Verdana", "Calibri"];

    static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial"] = ["arial.ttf", "arialbd.ttf"], ["Segoe UI"] = ["segoeui.ttf", "segoeuib.ttf"], ["Times New Roman"] = ["times.ttf", "timesbd.ttf"],
        ["Consolas"] = ["consola.ttf", "consolab.ttf"], ["Courier New"] = ["cour.ttf", "courbd.ttf"], ["Verdana"] = ["verdana.ttf", "verdanab.ttf"],
        ["Calibri"] = ["calibri.ttf", "calibrib.ttf"],
    };
    static readonly Dictionary<string, Face?> Cache = [];

    /// <summary>Closed outlines for the text (one polygon per glyph contour). Throws a readable error when no font can be loaded.</summary>
    public static List<List<(double u, double v)>> Build(string text, string font, bool bold, double height, double tracking = 0)
    {
        var face = Load(font, bold) ?? throw new InvalidOperationException($"The font '{font}' could not be read. Choose another font.");
        var contours = new List<List<(double, double)>>();
        double scale = height / face.CapHeight, pen = 0, lineY = 0;
        foreach (var ch in text.Replace("\r", ""))
        {
            if (ch == '\n') { pen = 0; lineY -= height * 1.4; continue; }
            int g = face.GlyphOf(ch);
            foreach (var contour in face.Contours(g, 0))
            {
                var poly = new List<(double, double)>(contour.Count);
                foreach (var (x, y) in contour) poly.Add((pen + x * scale, lineY + y * scale));
                if (poly.Count >= 3) contours.Add(poly);
            }
            pen += face.Advance(g) * scale + tracking;
        }
        return contours;
    }

    /// <summary>One letter contour as clean sketch geometry: corner points joined by straight lines and smooth curved runs.</summary>
    public sealed class Contour
    {
        public List<(double u, double v)> Nodes = [];
        public List<Run> Runs = [];
    }
    /// <summary>A straight line, or a smooth curve through <see cref="Inner"/> points, from node Start to node End (indices into the contour's nodes).</summary>
    public sealed record Run(bool Curve, int Start, int End, bool Closed, List<(double u, double v)> Inner);

    /// <summary>Like <see cref="Build"/> but keeps the shape of the letters: straight edges stay lines and each smooth curve becomes one spline instead of dozens of short segments.</summary>
    public static List<Contour> BuildClean(string text, string font, bool bold, double height, double tracking = 0)
    {
        var face = Load(font, bold) ?? throw new InvalidOperationException($"The font '{font}' could not be read. Choose another font.");
        var result = new List<Contour>();
        double scale = height / face.CapHeight, pen = 0, lineY = 0;
        foreach (var ch in text.Replace("\r", ""))
        {
            if (ch == '\n') { pen = 0; lineY -= height * 1.4; continue; }
            int g = face.GlyphOf(ch);
            foreach (var raw in face.Raw(g, 0))
                if (Clean(raw, pen, lineY, scale) is { } c) result.Add(c);
            pen += face.Advance(g) * scale + tracking;
        }
        return result;
    }

    static Contour? Clean(List<Pt> raw, double ox, double oy, double k)
    {
        // explicit on-curve nodes; between two nodes there is either nothing (a line) or one quadratic control point
        int first = raw.FindIndex(q => q.On);
        var ring = new List<Pt>();
        if (first < 0) { ring.Add(new Pt((raw[0].X + raw[^1].X) / 2, (raw[0].Y + raw[^1].Y) / 2, true)); ring.AddRange(raw); }
        else for (int i = 0; i < raw.Count; i++) ring.Add(raw[(first + i) % raw.Count]);
        var nodes = new List<(double x, double y)>(); var ctrl = new List<(double x, double y)?>();
        nodes.Add((ring[0].X, ring[0].Y));
        int idx = 1;
        while (true)
        {
            if (idx >= ring.Count) { ctrl.Add(null); break; }                        // straight back to the first node
            var q = ring[idx];
            if (q.On) { ctrl.Add(null); nodes.Add((q.X, q.Y)); idx++; continue; }
            idx++;
            if (idx >= ring.Count) { ctrl.Add((q.X, q.Y)); break; }                   // the curve closes on the first node
            var next = ring[idx];
            ctrl.Add((q.X, q.Y));
            if (next.On) { nodes.Add((next.X, next.Y)); idx++; }
            else nodes.Add(((q.X + next.X) / 2, (q.Y + next.Y) / 2));                // two controls in a row: the node between them is implied
        }
        return Assemble(nodes, ctrl, ox, oy, k);
    }

    static Contour? Assemble(List<(double x, double y)> nodes, List<(double x, double y)?> ctrl, double ox, double oy, double k)
    {
        int n = nodes.Count;
        if (n < 2 || ctrl.Count != n) return null;
        (double, double) T((double x, double y) p) => (ox + p.x * k, oy + p.y * k);
        double Cos(int node)
        {
            var prevEdgeCtrl = ctrl[(node + n - 1) % n]; var p = nodes[(node + n - 1) % n]; var q = nodes[node]; var r = nodes[(node + 1) % n];
            var inV = prevEdgeCtrl is { } c1 ? (q.x - c1.x, q.y - c1.y) : (q.x - p.x, q.y - p.y);
            var outV = ctrl[node] is { } c2 ? (c2.x - q.x, c2.y - q.y) : (r.x - q.x, r.y - q.y);
            double l1 = Math.Sqrt(inV.Item1 * inV.Item1 + inV.Item2 * inV.Item2), l2 = Math.Sqrt(outV.Item1 * outV.Item1 + outV.Item2 * outV.Item2);
            return l1 < 1e-9 || l2 < 1e-9 ? 1 : (inV.Item1 * outV.Item1 + inV.Item2 * outV.Item2) / (l1 * l2);
        }
        bool Smooth(int node) => ctrl[(node + n - 1) % n] != null && ctrl[node] != null && Cos(node) > 0.985;
        var result = new Contour();
        foreach (var nd in nodes) result.Nodes.Add(T(nd));
        // start at a node that breaks a curve (any corner, or a change between line and curve)
        int start = Enumerable.Range(0, n).FirstOrDefault(i => !Smooth(i), -1);
        (double, double) Mid(int e) { var a = nodes[e]; var b = nodes[(e + 1) % n]; var c = ctrl[e]!.Value; return T((0.25 * a.x + 0.5 * c.x + 0.25 * b.x, 0.25 * a.y + 0.5 * c.y + 0.25 * b.y)); }
        if (start < 0)
        {
            var inner = new List<(double, double)>();
            for (int e = 0; e < n; e++) { inner.Add(Mid(e)); if (e + 1 < n) inner.Add(result.Nodes[e + 1]); }
            result.Runs.Add(new Run(true, 0, 0, true, inner));
            return result;
        }
        for (int step = 0; step < n;)
        {
            int e = (start + step) % n;
            if (ctrl[e] == null) { result.Runs.Add(new Run(false, e, (e + 1) % n, false, [])); step++; continue; }
            var inner = new List<(double, double)> { Mid(e) }; int endEdge = e; step++;
            while (step < n && Smooth((start + step) % n) && ctrl[(start + step) % n] != null)
            {
                int node = (start + step) % n; inner.Add(result.Nodes[node]); inner.Add(Mid(node)); endEdge = node; step++;
            }
            result.Runs.Add(new Run(true, e, (endEdge + 1) % n, false, inner));
        }
        return result;
    }

    static Face? Load(string font, bool bold)
    {
        string key = font + (bold ? "|b" : "");
        if (Cache.TryGetValue(key, out var hit)) return hit;
        Face? face = null;
        var names = Files.TryGetValue(font, out var f) ? f : [font.Replace(" ", "").ToLowerInvariant() + ".ttf"];
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var name in bold ? new[] { names[^1], names[0] } : new[] { names[0] })
        {
            foreach (var root in new[] { dir, "/usr/share/fonts/truetype/dejavu", "/Library/Fonts", "/System/Library/Fonts/Supplemental" })
            {
                var path = Path.Combine(root, name);
                if (!File.Exists(path)) continue;
                try { face = new Face(File.ReadAllBytes(path)); } catch (Exception e) when (e is not OutOfMemoryException) { face = null; }
                if (face != null) break;
            }
            if (face != null) break;
        }
        return Cache[key] = face;
    }

    internal record struct Pt(double X, double Y, bool On);

    internal sealed class Face
    {
        readonly byte[] _d;
        readonly Dictionary<string, (int off, int len)> _t = [];
        readonly int _unitsPerEm, _locFormat, _glyphs, _hMetrics;
        readonly List<(int start, int end, int delta, int rangeOffset, int rangeAt)> _seg4 = [];
        readonly List<(uint start, uint end, uint startGlyph)> _seg12 = [];
        public double CapHeight { get; }

        public Face(byte[] data)
        {
            _d = data;
            if (U32(0) is not (0x00010000 or 0x74727565)) throw new InvalidDataException("Not a TrueType font.");
            int n = U16(4);
            for (int i = 0; i < n; i++) { int r = 12 + i * 16; _t[System.Text.Encoding.ASCII.GetString(data, r, 4)] = ((int)U32(r + 8), (int)U32(r + 12)); }
            if (!_t.ContainsKey("glyf")) throw new InvalidDataException("No glyph outlines.");
            _unitsPerEm = U16(_t["head"].off + 18); _locFormat = S16(_t["head"].off + 50);
            _glyphs = U16(_t["maxp"].off + 4); _hMetrics = U16(_t["hhea"].off + 34);
            ReadCmap();
            // cap height from the outline of 'H' so a requested height means the height of capital letters
            double top = 0;
            foreach (var c in Contours(GlyphOf('H'), 0)) foreach (var (_, y) in c) top = Math.Max(top, y);
            CapHeight = top > 0 ? top : _unitsPerEm * 0.7;
        }

        int U16(int o) => (_d[o] << 8) | _d[o + 1];
        int S16(int o) => (short)U16(o);
        uint U32(int o) => (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3]);

        void ReadCmap()
        {
            int cm = _t["cmap"].off, n = U16(cm + 2), best = -1, bestRank = -1;
            for (int i = 0; i < n; i++)
            {
                int plat = U16(cm + 4 + i * 8), enc = U16(cm + 6 + i * 8), off = (int)U32(cm + 8 + i * 8);
                int fmt = U16(cm + off);
                int rank = plat == 3 && enc == 10 && fmt == 12 ? 4 : plat == 3 && enc == 1 && fmt == 4 ? 3 : plat == 0 && fmt is 4 or 12 ? 2 : 0;
                if (rank > bestRank) { bestRank = rank; best = cm + off; }
            }
            if (best < 0 || bestRank == 0) throw new InvalidDataException("No usable character map.");
            if (U16(best) == 12)
            {
                int groups = (int)U32(best + 12);
                for (int i = 0; i < groups; i++) { int g = best + 16 + i * 12; _seg12.Add((U32(g), U32(g + 4), U32(g + 8))); }
                return;
            }
            int segX2 = U16(best + 6), segs = segX2 / 2, endAt = best + 14, startAt = endAt + segX2 + 2, deltaAt = startAt + segX2, rangeAt = deltaAt + segX2;
            for (int i = 0; i < segs; i++) _seg4.Add((U16(startAt + i * 2), U16(endAt + i * 2), S16(deltaAt + i * 2), U16(rangeAt + i * 2), rangeAt + i * 2));
        }

        public int GlyphOf(int cp)
        {
            foreach (var (s, e, g) in _seg12) if (cp >= s && cp <= e) return (int)(g + (uint)cp - s);
            foreach (var (start, end, delta, ro, at) in _seg4)
            {
                if (cp < start || cp > end) continue;
                if (ro == 0) return (cp + delta) & 0xFFFF;
                int g = U16(at + ro + (cp - start) * 2);
                return g == 0 ? 0 : (g + delta) & 0xFFFF;
            }
            return 0;
        }

        public double Advance(int g) => U16(_t["hmtx"].off + Math.Min(g, _hMetrics - 1) * 4);

        (int off, int len) GlyphData(int g)
        {
            if (g < 0 || g >= _glyphs) return (0, 0);
            int loca = _t["loca"].off, a, b;
            if (_locFormat == 0) { a = U16(loca + g * 2) * 2; b = U16(loca + g * 2 + 2) * 2; }
            else { a = (int)U32(loca + g * 4); b = (int)U32(loca + g * 4 + 4); }
            return (_t["glyf"].off + a, b - a);
        }

        public List<List<(double x, double y)>> Contours(int g, int depth) => Raw(g, depth).ConvertAll(c => Flatten(c));

        public List<List<Pt>> Raw(int g, int depth)
        {
            var result = new List<List<Pt>>();
            if (depth > 4) return result;
            var (off, len) = GlyphData(g);
            if (len <= 0) return result;
            int nc = S16(off);
            if (nc < 0) { Composite(off + 10, depth, result); return result; }
            var ends = new int[nc];
            for (int i = 0; i < nc; i++) ends[i] = U16(off + 10 + i * 2);
            int count = nc == 0 ? 0 : ends[^1] + 1, p = off + 10 + nc * 2;
            p += 2 + U16(p);                                                        // skip hinting instructions
            var flags = new byte[count];
            for (int i = 0; i < count;)
            {
                byte f = _d[p++]; flags[i++] = f;
                if ((f & 8) != 0) { int rep = _d[p++]; while (rep-- > 0 && i < count) flags[i++] = f; }
            }
            var xs = new int[count]; var ys = new int[count]; int v = 0;
            for (int i = 0; i < count; i++)
            {
                if ((flags[i] & 2) != 0) { int dx = _d[p++]; v += (flags[i] & 16) != 0 ? dx : -dx; }
                else if ((flags[i] & 16) == 0) { v += S16(p); p += 2; }
                xs[i] = v;
            }
            v = 0;
            for (int i = 0; i < count; i++)
            {
                if ((flags[i] & 4) != 0) { int dy = _d[p++]; v += (flags[i] & 32) != 0 ? dy : -dy; }
                else if ((flags[i] & 32) == 0) { v += S16(p); p += 2; }
                ys[i] = v;
            }
            int start = 0;
            for (int c = 0; c < nc; c++)
            {
                var pts = new List<Pt>();
                for (int i = start; i <= ends[c]; i++) pts.Add(new Pt(xs[i], ys[i], (flags[i] & 1) != 0));
                start = ends[c] + 1;
                if (pts.Count >= 3) result.Add(pts);
            }
            return result;
        }

        void Composite(int p, int depth, List<List<Pt>> into)
        {
            while (true)
            {
                int flags = U16(p), glyph = U16(p + 2); p += 4;
                double dx, dy;
                if ((flags & 1) != 0) { dx = S16(p); dy = S16(p + 2); p += 4; } else { dx = (sbyte)_d[p]; dy = (sbyte)_d[p + 1]; p += 2; }
                if ((flags & 2) == 0) dx = dy = 0;
                double a = 1, b = 0, c = 0, d = 1;
                if ((flags & 8) != 0) { a = d = S16(p) / 16384.0; p += 2; }
                else if ((flags & 0x40) != 0) { a = S16(p) / 16384.0; d = S16(p + 2) / 16384.0; p += 4; }
                else if ((flags & 0x80) != 0) { a = S16(p) / 16384.0; b = S16(p + 2) / 16384.0; c = S16(p + 4) / 16384.0; d = S16(p + 6) / 16384.0; p += 8; }
                foreach (var contour in Raw(glyph, depth + 1))
                    into.Add(contour.ConvertAll(q => new Pt(a * q.X + c * q.Y + dx, b * q.X + d * q.Y + dy, q.On)));
                if ((flags & 0x20) == 0) break;
            }
        }

        /// <summary>Walk a TrueType contour (off-curve points are quadratic Bezier controls) into a polygon.</summary>
        internal static List<(double x, double y)> Flatten(List<Pt> pts)
        {
            int n = pts.Count, first = pts.FindIndex(q => q.On);
            var ring = new List<Pt>();
            if (first < 0) { var mid = new Pt((pts[0].X + pts[^1].X) / 2, (pts[0].Y + pts[^1].Y) / 2, true); ring.Add(mid); ring.AddRange(pts); }
            else for (int i = 0; i < n; i++) ring.Add(pts[(first + i) % n]);
            ring.Add(ring[0]);
            var poly = new List<(double, double)>();
            Pt cur = ring[0];
            poly.Add((cur.X, cur.Y));
            for (int i = 1; i < ring.Count; i++)
            {
                var q = ring[i];
                if (q.On) { poly.Add((q.X, q.Y)); cur = q; continue; }
                Pt next = ring[i + 1 < ring.Count ? i + 1 : 0], end = next.On ? next : new Pt((q.X + next.X) / 2, (q.Y + next.Y) / 2, true);
                for (int s = 1; s <= 4; s++)
                {
                    double t = s / 4.0, m = 1 - t;
                    poly.Add((m * m * cur.X + 2 * m * t * q.X + t * t * end.X, m * m * cur.Y + 2 * m * t * q.Y + t * t * end.Y));
                }
                cur = end;
                if (next.On) i++;
            }
            if (poly.Count > 1 && Math.Abs(poly[0].Item1 - poly[^1].Item1) < 1e-9 && Math.Abs(poly[0].Item2 - poly[^1].Item2) < 1e-9) poly.RemoveAt(poly.Count - 1);
            return poly;
        }
    }
}
