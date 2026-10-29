using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Simulation;

/// <summary>Draws a study's fixtures and loads on the model (green supports, red arrows) so the setup can be checked at a glance.</summary>
public static class StudyOverlay
{
    const uint FixColor = 0xFF2EA043, LoadColor = 0xFFE5484D, TorqueColor = 0xFFF59E0B;

    public static (Vec3 center, Vec3 normal, List<Vec3> samples, double size)? FaceInfo(SceneObject o, int face)
    {
        var m = o.Mesh;
        if (face < 0 || face + 1 >= m.FaceTriStart.Length) return null;
        Vec3 c = Vec3.Zero, n = Vec3.Zero; double area = 0; var samples = new List<Vec3>();
        int t0 = m.FaceTriStart[face], t1 = m.FaceTriStart[face + 1];
        if (t1 <= t0) return null;
        for (int t = t0; t < t1; t++)
        {
            Vec3 P(int k) { int v = m.Indices[3 * t + k]; return new(m.Positions[3 * v], m.Positions[3 * v + 1], m.Positions[3 * v + 2]); }
            var a = P(0); var b = P(1); var d = P(2); var cr = Vec3.Cross(b - a, d - a); double ar = cr.Length * 0.5;
            c += (a + b + d) / 3 * ar; n += cr; area += ar;
        }
        if (area < 1e-12) return null;
        c /= area;
        int step = Math.Max(1, (t1 - t0) / 6);
        for (int t = t0; t < t1 && samples.Count < 7; t += step)
        {
            Vec3 P(int k) { int v = m.Indices[3 * t + k]; return new(m.Positions[3 * v], m.Positions[3 * v + 1], m.Positions[3 * v + 2]); }
            samples.Add((P(0) + P(1) + P(2)) / 3);
        }
        return (c, n.Normalized(), samples, Math.Sqrt(area));
    }

    static void Arrow(Overlay o, Vec3 tail, Vec3 head, double headSize, uint color, float w = 2.6f)
    {
        o.AddLine(tail, head, color, w);
        var d = (head - tail).Normalized();
        var side = Math.Abs(d.Z) < 0.9 ? Vec3.Cross(d, Vec3.UnitZ).Normalized() : Vec3.Cross(d, Vec3.UnitX).Normalized();
        var side2 = Vec3.Cross(d, side);
        foreach (var s in new[] { side, -side, side2, -side2 }) o.AddLine(head, head - d * headSize + s * headSize * 0.45, color, w);
    }

    public static void Draw(CadSession s, Overlay o)
    {
        var st = s.ActiveStudy; if (st == null) return;
        var body = s.Find(st.BodyId); if (body == null) return;
        double model = body.Mesh.Max.DistanceTo(body.Mesh.Min);
        foreach (var fx in st.Fixtures)
            foreach (var fr in fx.Faces.Where(f => f.Obj == body.Id))
                if (FaceInfo(body, fr.Face) is { } fi)
                {
                    double a = Math.Min(fi.size * 0.25, model * 0.04) + 0.5;
                    foreach (var p in fi.samples)
                    {
                        // a small tripod standing on the face: reads as "bolted down"
                        var tip = p + fi.normal * a;
                        o.AddLine(p, tip, FixColor, 2.2f);
                        o.AddDot(tip, FixColor, 6);
                        o.AddLine(p - Vec3.Cross(fi.normal, Vec3.UnitZ).Normalized() * a * 0.5, p + Vec3.Cross(fi.normal, Vec3.UnitZ).Normalized() * a * 0.5, FixColor, 2.2f);
                    }
                    o.AddLabel(fi.center + fi.normal * a * 1.6, fx.Name, true, 0);
                }
        double arrow = model * 0.12;
        foreach (var ld in st.Loads)
        {
            if (ld.Kind == LoadKind.Gravity)
            {
                var c = (body.Mesh.Min + body.Mesh.Max) * 0.5; var d = ld.Direction.Normalized();
                Arrow(o, c - d * arrow * 1.4, c + d * arrow * 0.2, arrow * 0.3, LoadColor, 3.2f);
                o.AddLabel(c - d * arrow * 1.6, $"{ld.Name}  {ld.Value:0.##} g", true, 0);
                continue;
            }
            foreach (var fr in ld.Faces.Where(f => f.Obj == body.Id))
                if (FaceInfo(body, fr.Face) is { } fi)
                {
                    if (ld.Kind == LoadKind.Torque)
                    {
                        double r = Math.Max(fi.size * 0.5, arrow * 0.5); var axis = fi.normal;
                        var u = Math.Abs(axis.Z) < 0.9 ? Vec3.Cross(axis, Vec3.UnitZ).Normalized() : Vec3.Cross(axis, Vec3.UnitX).Normalized(); var v = Vec3.Cross(axis, u);
                        var pts = new List<Vec3>();
                        for (int i = 0; i <= 24; i++) { double ang = i * 1.5 * Math.PI / 24; pts.Add(fi.center + axis * (arrow * 0.15) + (u * Math.Cos(ang) + v * Math.Sin(ang)) * r); }
                        o.AddPolyline(pts, TorqueColor, 3f);
                        Arrow(o, pts[^2], pts[^1], arrow * 0.25, TorqueColor, 3f);
                        o.AddLabel(fi.center + axis * arrow * 0.5, $"{ld.Name}  {ld.Value:0.##} N·mm", true, 0);
                        continue;
                    }
                    var pts2 = fi.samples.Count > 0 ? fi.samples : [fi.center];
                    foreach (var p in pts2)
                    {
                        var dir = ld.Kind == LoadKind.Pressure || ld.AlongNormal ? -fi.normal : ld.Direction.Normalized();
                        Arrow(o, p - dir * arrow, p, arrow * 0.28, LoadColor);
                    }
                    var lab = ld.Kind == LoadKind.Pressure ? $"{ld.Name}  {ld.Value:0.##} MPa" : $"{ld.Name}  {ld.Value:0.##} N";
                    var ldir = ld.Kind == LoadKind.Pressure || ld.AlongNormal ? -fi.normal : ld.Direction.Normalized();
                    o.AddLabel(fi.center - ldir * arrow * 1.15, lab, true, 0);
                }
        }
    }
}
