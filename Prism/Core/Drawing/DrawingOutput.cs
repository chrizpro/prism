using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prism.Core.Drawing;

/// <summary>Line styles of a drawing. Weights are in millimetres on the sheet.</summary>
public enum LineStyle { Visible, Thin, Hidden, Center, Cut, Dimension, Hatch }

/// <summary>Everything on a sheet is stored as these simple shapes (sheet millimetres, y down), then written as SVG, PDF or DXF.</summary>
public abstract record Prim;
public sealed record PolyP((double x, double y)[] Pts, LineStyle Style, bool Closed = false) : Prim;
public sealed record FillP((double x, double y)[] Pts) : Prim;
public sealed record TextP(double X, double Y, string S, double Size, string Anchor = "start", double Rot = 0, bool Bold = false) : Prim;

public static class DrawingOutput
{
    public static double Weight(LineStyle s) => s switch { LineStyle.Visible => 0.5, LineStyle.Cut => 0.7, LineStyle.Hidden => 0.25, LineStyle.Center => 0.18, LineStyle.Hatch => 0.15, _ => 0.2 };
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static void Save(string path, IReadOnlyList<Prim> prims, double w, double h)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".svg": File.WriteAllText(path, Svg(prims, w, h)); break;
            case ".pdf": File.WriteAllBytes(path, Pdf(prims, w, h)); break;
            case ".dxf": File.WriteAllText(path, Dxf(prims, w, h)); break;
            default: throw new ArgumentException("Use a .svg, .pdf or .dxf file name.");
        }
    }

    // ---------------------------------------------------------------- SVG
    static string Dash(LineStyle s) => s switch { LineStyle.Hidden => " stroke-dasharray=\"2.4 1.2\"", LineStyle.Center => " stroke-dasharray=\"9 1.5 1.5 1.5\"", LineStyle.Cut => " stroke-dasharray=\"10 2 2 2\"", _ => "" };

    public static string Svg(IReadOnlyList<Prim> prims, double w, double h)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w)}mm\" height=\"{F(h)}mm\" viewBox=\"0 0 {F(w)} {F(h)}\" font-family=\"Arial, Helvetica, sans-serif\">\n<rect width=\"{F(w)}\" height=\"{F(h)}\" fill=\"#fff\"/>\n");
        foreach (var g in prims.OfType<PolyP>().GroupBy(p => p.Style).OrderByDescending(g => g.Key == LineStyle.Hatch ? 99 : g.Key == LineStyle.Hidden ? 98 : 0))
        {
            sb.Append($"<path fill=\"none\" stroke=\"{(g.Key == LineStyle.Hidden ? "#444" : "#000")}\" stroke-width=\"{F(Weight(g.Key))}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"{Dash(g.Key)} d=\"");
            foreach (var p in g)
            {
                sb.Append($"M{F(p.Pts[0].x)} {F(p.Pts[0].y)}");
                for (int i = 1; i < p.Pts.Length; i++) sb.Append($"L{F(p.Pts[i].x)} {F(p.Pts[i].y)}");
                if (p.Closed) sb.Append('Z');
            }
            sb.Append("\"/>\n");
        }
        foreach (var f in prims.OfType<FillP>()) sb.Append($"<polygon points=\"{string.Join(' ', f.Pts.Select(p => F(p.x) + "," + F(p.y)))}\" fill=\"#000\"/>\n");
        foreach (var t in prims.OfType<TextP>())
            sb.Append($"<text x=\"{F(t.X)}\" y=\"{F(t.Y)}\" font-size=\"{F(t.Size)}\" text-anchor=\"{(t.Anchor == "middle" ? "middle" : t.Anchor == "end" ? "end" : "start")}\"{(t.Bold ? " font-weight=\"bold\"" : "")}{(t.Rot != 0 ? $" transform=\"rotate({F(-t.Rot)} {F(t.X)} {F(t.Y)})\"" : "")}>{Esc(t.S)}</text>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- DXF (R12 ASCII: opens in every CAD program)
    static string Layer(LineStyle s) => s switch { LineStyle.Visible => "VISIBLE", LineStyle.Hidden => "HIDDEN", LineStyle.Center => "CENTER", LineStyle.Cut => "CUT", LineStyle.Hatch => "HATCH", LineStyle.Dimension => "DIMENSION", _ => "THIN" };

    public static string Dxf(IReadOnlyList<Prim> prims, double w, double h)
    {
        var sb = new StringBuilder();
        void G(int code, string v) => sb.Append(code).Append('\n').Append(v).Append('\n');
        double Y(double y) => h - y;
        G(0, "SECTION"); G(2, "HEADER"); G(9, "$ACADVER"); G(1, "AC1009"); G(9, "$INSUNITS"); G(70, "4"); G(0, "ENDSEC");
        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LTYPE"); G(70, "4");
        G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); G(40, "0");
        G(0, "LTYPE"); G(2, "HIDDEN"); G(70, "0"); G(3, "Hidden"); G(72, "65"); G(73, "2"); G(40, "3.6"); G(49, "2.4"); G(49, "-1.2");
        G(0, "LTYPE"); G(2, "CENTER"); G(70, "0"); G(3, "Center"); G(72, "65"); G(73, "4"); G(40, "13"); G(49, "9"); G(49, "-1.5"); G(49, "1.5"); G(49, "-1.5");
        G(0, "LTYPE"); G(2, "PHANTOM"); G(70, "0"); G(3, "Cutting plane"); G(72, "65"); G(73, "4"); G(40, "16"); G(49, "10"); G(49, "-2"); G(49, "2"); G(49, "-2");
        G(0, "ENDTAB");
        var layers = new (string name, int color, string lt)[] { ("VISIBLE", 7, "CONTINUOUS"), ("HIDDEN", 8, "HIDDEN"), ("CENTER", 1, "CENTER"), ("CUT", 7, "PHANTOM"), ("HATCH", 8, "CONTINUOUS"), ("DIMENSION", 3, "CONTINUOUS"), ("THIN", 7, "CONTINUOUS"), ("TEXT", 7, "CONTINUOUS") };
        G(0, "TABLE"); G(2, "LAYER"); G(70, layers.Length.ToString());
        foreach (var (n, c, lt) in layers) { G(0, "LAYER"); G(2, n); G(70, "0"); G(62, c.ToString()); G(6, lt); }
        G(0, "ENDTAB"); G(0, "ENDSEC");
        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var p in prims.OfType<PolyP>())
        {
            var pts = p.Pts.ToList(); if (p.Closed) pts.Add(pts[0]);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                G(0, "LINE"); G(8, Layer(p.Style)); G(10, F(pts[i].x)); G(20, F(Y(pts[i].y))); G(30, "0"); G(11, F(pts[i + 1].x)); G(21, F(Y(pts[i + 1].y))); G(31, "0");
            }
        }
        foreach (var f in prims.OfType<FillP>())
        {
            var q = f.Pts; G(0, "SOLID"); G(8, "DIMENSION");
            G(10, F(q[0].x)); G(20, F(Y(q[0].y))); G(30, "0"); G(11, F(q[1].x)); G(21, F(Y(q[1].y))); G(31, "0"); G(12, F(q[2].x)); G(22, F(Y(q[2].y))); G(32, "0"); G(13, F(q[2].x)); G(23, F(Y(q[2].y))); G(33, "0");
        }
        foreach (var t in prims.OfType<TextP>())
        {
            G(0, "TEXT"); G(8, "TEXT"); G(10, F(t.X)); G(20, F(Y(t.Y))); G(30, "0"); G(40, F(t.Size * 0.9)); G(1, t.S.Replace("Ø", "%%c").Replace("°", "%%d").Replace("±", "%%p"));
            if (t.Rot != 0) G(50, F(t.Rot));
            if (t.Anchor != "start") { G(72, t.Anchor == "middle" ? "1" : "2"); G(11, F(t.X)); G(21, F(Y(t.Y))); G(31, "0"); }
        }
        G(0, "ENDSEC"); G(0, "EOF");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- PDF (single page, vector, Helvetica)
    static string PdfText(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\').Append(ch);
            else if (ch > 255) sb.Append('?');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    public static byte[] Pdf(IReadOnlyList<Prim> prims, double w, double h)
    {
        const double k = 72 / 25.4;
        var c = new StringBuilder();
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        double X(double x) => x * k; double Y(double y) => (h - y) * k;
        foreach (var p in prims.OfType<PolyP>())
        {
            c.Append($"{N(Weight(p.Style) * k)} w {(p.Style switch { LineStyle.Hidden => "[6.8 3.4] 0 d", LineStyle.Center => "[25.5 4.3 4.3 4.3] 0 d", LineStyle.Cut => "[28 5.7 5.7 5.7] 0 d", _ => "[] 0 d" })} {(p.Style == LineStyle.Hidden ? "0.27 G" : "0 G")} 1 J 1 j\n");
            c.Append($"{N(X(p.Pts[0].x))} {N(Y(p.Pts[0].y))} m ");
            for (int i = 1; i < p.Pts.Length; i++) c.Append($"{N(X(p.Pts[i].x))} {N(Y(p.Pts[i].y))} l ");
            c.Append(p.Closed ? "h S\n" : "S\n");
        }
        c.Append("0 g [] 0 d\n");
        foreach (var f in prims.OfType<FillP>())
        {
            c.Append($"{N(X(f.Pts[0].x))} {N(Y(f.Pts[0].y))} m ");
            for (int i = 1; i < f.Pts.Length; i++) c.Append($"{N(X(f.Pts[i].x))} {N(Y(f.Pts[i].y))} l ");
            c.Append("h f\n");
        }
        foreach (var t in prims.OfType<TextP>())
        {
            double size = t.Size * k, width = t.S.Length * size * (t.Bold ? 0.56 : 0.52);
            double dx = t.Anchor == "middle" ? -width / 2 : t.Anchor == "end" ? -width : 0, a = t.Rot * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
            double x = X(t.X) + dx * cs, y = Y(t.Y) + dx * sn;
            c.Append($"BT /{(t.Bold ? "F2" : "F1")} {N(size)} Tf {N(cs)} {N(sn)} {N(-sn)} {N(cs)} {N(x)} {N(y)} Tm ({PdfText(t.S)}) Tj ET\n");
        }
        var content = Encoding.Latin1.GetBytes(c.ToString());
        var ms = new MemoryStream(); var offs = new List<long>();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj(int n, string body) { offs.Add(ms.Position); W($"{n} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n");
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(w * k)} {N(h * k)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>");
        offs.Add(ms.Position); W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n"); ms.Write(content, 0, content.Length); W("\nendstream\nendobj\n");
        Obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        long xref = ms.Position;
        W($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
