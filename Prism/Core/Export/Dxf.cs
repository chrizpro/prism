using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Prism.Core.Sketching;

namespace Prism.Core.Export;

/// <summary>Reads and writes plain ASCII DXF (R12 style entities) that every CAD / laser / CNC program opens.</summary>
public static class Dxf
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    static string N(double v) => v.ToString("0.#####", Ci);

    public static void SaveOutlines(string path, IEnumerable<IReadOnlyList<(double x, double y)>> loops, bool closed = true)
    {
        var sb = new StringBuilder();
        void G(int code, string v) { sb.Append(code.ToString(Ci)).Append('\n').Append(v).Append('\n'); }
        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var loop in loops)
        {
            G(0, "POLYLINE"); G(8, "0"); G(66, "1"); G(70, closed ? "1" : "0");
            foreach (var (x, y) in loop) { G(0, "VERTEX"); G(8, "0"); G(10, N(x)); G(20, N(y)); G(30, "0"); }
            G(0, "SEQEND"); G(8, "0");
        }
        G(0, "ENDSEC"); G(0, "EOF");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    // ------------------------------------------------------------------ sketches out
    /// <summary>Sketch curves as true LINE / CIRCLE / ARC entities (ellipses and splines become polylines). Each drawing plane is its own layer.</summary>
    public static void SaveSketches(string path, IEnumerable<(string layer, SketchModel model)> sketches)
    {
        var sb = new StringBuilder();
        void G(int code, string v) { sb.Append(code.ToString(Ci)).Append('\n').Append(v).Append('\n'); }
        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var (layerName, model) in sketches)
        {
            string layer = new string(layerName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
            if (layer.Length == 0) layer = "0";
            foreach (var e in model.Entities.Where(e => !e.Construction))
            {
                switch (e.Type)
                {
                    case EntType.Line:
                    {
                        var a = model.P(e.A); var b = model.P(e.B);
                        G(0, "LINE"); G(8, layer); G(10, N(a.X)); G(20, N(a.Y)); G(30, "0"); G(11, N(b.X)); G(21, N(b.Y)); G(31, "0");
                        break;
                    }
                    case EntType.Circle:
                    {
                        var c = model.P(e.A);
                        G(0, "CIRCLE"); G(8, layer); G(10, N(c.X)); G(20, N(c.Y)); G(30, "0"); G(40, N(e.R));
                        break;
                    }
                    case EntType.Arc:
                    {
                        var (cx, cy, r, a0, sweep) = model.ArcGeometry(e);
                        double start = a0 * 180 / Math.PI, end = (a0 + sweep) * 180 / Math.PI;
                        start = ((start % 360) + 360) % 360; end = ((end % 360) + 360) % 360;
                        G(0, "ARC"); G(8, layer); G(10, N(cx)); G(20, N(cy)); G(30, "0"); G(40, N(r)); G(50, N(start)); G(51, N(end));
                        break;
                    }
                    case EntType.Ellipse or EntType.Spline:
                    {
                        var pts = e.Type == EntType.Ellipse
                            ? Enumerable.Range(0, 129).Select(i => model.EllipsePoint(e, i * 2 * Math.PI / 128)).ToList()
                            : model.SplineSamples(e);
                        if (pts.Count < 2) break;
                        bool closed = e.Type == EntType.Ellipse || e.Closed;
                        G(0, "POLYLINE"); G(8, layer); G(66, "1"); G(70, closed ? "1" : "0");
                        foreach (var (x, y) in pts) { G(0, "VERTEX"); G(8, layer); G(10, N(x)); G(20, N(y)); G(30, "0"); }
                        G(0, "SEQEND"); G(8, layer);
                        break;
                    }
                }
            }
        }
        G(0, "ENDSEC"); G(0, "EOF");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    // ------------------------------------------------------------------ sketches in
    /// <summary>Read LINE, CIRCLE, ARC, LWPOLYLINE and POLYLINE entities (millimetres) into a sketch. Returns the number of curves added.</summary>
    public static int LoadSketch(string path, SketchModel model, out string note)
    {
        var lines = File.ReadAllLines(path);
        var pairs = new List<(int code, string value)>();
        for (int i = 0; i + 1 < lines.Length; i += 2)
            if (int.TryParse(lines[i].Trim(), out int code)) pairs.Add((code, lines[i + 1].Trim()));
        double Num(string s) => double.TryParse(s, NumberStyles.Float, Ci, out var v) ? v : 0;
        int added = 0, skipped = 0;
        double unitScale = 1;     // $INSUNITS: 1 inch, 4 mm, 5 cm, 6 m
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].code == 9 && pairs[i].value == "$INSUNITS" && i + 1 < pairs.Count) unitScale = (int)Num(pairs[i + 1].value) switch { 1 => 25.4, 2 => 304.8, 5 => 10, 6 => 1000, _ => 1 };
        // entities only
        int start = pairs.FindIndex(p => p.code == 2 && p.value == "ENTITIES"); if (start < 0) start = 0;
        int p0 = start;
        (double x, double y) Pt(Dictionary<int, double> d, int cx, int cy) => (d.GetValueOrDefault(cx) * unitScale, d.GetValueOrDefault(cy) * unitScale);
        while (p0 < pairs.Count)
        {
            if (pairs[p0].code != 0) { p0++; continue; }
            string type = pairs[p0].value;
            if (type == "ENDSEC" || type == "EOF") break;
            int q = p0 + 1; var d = new Dictionary<int, double>(); var xs = new List<(double x, double y, double bulge)>(); int flags = 0;
            while (q < pairs.Count && pairs[q].code != 0)
            {
                var (code, value) = pairs[q];
                if (type == "LWPOLYLINE" && code == 10) xs.Add((Num(value) * unitScale, 0, 0));
                else if (type == "LWPOLYLINE" && code == 20 && xs.Count > 0) { var l = xs[^1]; xs[^1] = (l.x, Num(value) * unitScale, l.bulge); }
                else if (type == "LWPOLYLINE" && code == 42 && xs.Count > 0) { var l = xs[^1]; xs[^1] = (l.x, l.y, Num(value)); }
                else if (code == 70) flags = (int)Num(value);
                else d[code] = Num(value);
                q++;
            }
            switch (type)
            {
                case "LINE":
                {
                    var (ax, ay) = Pt(d, 10, 20); var (bx, by) = Pt(d, 11, 21);
                    if (Math.Abs(ax - bx) + Math.Abs(ay - by) > 1e-9) { model.AddLine(model.AddPoint(ax, ay), model.AddPoint(bx, by)); added++; }
                    break;
                }
                case "CIRCLE": { var (cx, cy) = Pt(d, 10, 20); if (d.GetValueOrDefault(40) > 0) { model.AddCircle(model.AddPoint(cx, cy), d[40] * unitScale); added++; } break; }
                case "ARC":
                {
                    var (cx, cy) = Pt(d, 10, 20); double r = d.GetValueOrDefault(40) * unitScale; if (r <= 0) break;
                    double a0 = d.GetValueOrDefault(50) * Math.PI / 180, a1 = d.GetValueOrDefault(51) * Math.PI / 180;
                    model.AddArc(model.AddPoint(cx, cy), model.AddPoint(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), model.AddPoint(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1))); added++;
                    break;
                }
                case "LWPOLYLINE":
                    added += AddPolyline(model, xs.Select(v => (v.x, v.y)).ToList(), (flags & 1) != 0);
                    break;
                case "POLYLINE":
                {
                    // vertices follow as separate VERTEX entities until SEQEND
                    var verts = new List<(double x, double y)>(); bool closed = (flags & 1) != 0;
                    int r = q;
                    while (r < pairs.Count)
                    {
                        if (pairs[r].code == 0 && pairs[r].value == "VERTEX")
                        {
                            double vx = 0, vy = 0; r++;
                            while (r < pairs.Count && pairs[r].code != 0) { if (pairs[r].code == 10) vx = Num(pairs[r].value) * unitScale; else if (pairs[r].code == 20) vy = Num(pairs[r].value) * unitScale; r++; }
                            verts.Add((vx, vy));
                        }
                        else if (pairs[r].code == 0 && pairs[r].value == "SEQEND") { r++; break; }
                        else r++;
                    }
                    added += AddPolyline(model, verts, closed);
                    q = r;
                    break;
                }
                case "SECTION" or "ENDSEC" or "VERTEX" or "SEQEND" or "TABLE" or "ENDTAB": break;
                default: skipped++; break;
            }
            p0 = Math.Max(q, p0 + 1);
        }
        note = skipped > 0 ? $"{skipped} unsupported entity(ies) were skipped (text, hatches, dimensions, 3D)." : "";
        return added;
    }

    static int AddPolyline(SketchModel model, List<(double x, double y)> pts, bool closed)
    {
        if (pts.Count < 2) return 0;
        var ids = pts.Select(p => model.AddPoint(p.x, p.y)).ToList();
        int count = pts.Count - 1;
        for (int i = 0; i < count; i++) model.AddLine(ids[i], ids[i + 1]);
        if (closed && pts.Count > 2) { model.AddLine(ids[^1], ids[0]); count++; }
        return count;
    }
}
