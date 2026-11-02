using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Core;

/// <summary>The latest kinematic or motion study of the assembly: shown as the paths the parts' centres of mass follow.</summary>
public sealed partial class CadSession
{
    public MotionResult? Motion { get; private set; }
    public int MotionSample { get; private set; }
    public bool ShowMotionPaths { get; set; } = true;

    /// <summary>The latest study, kept after its panel closes so its reaction forces can be turned into loads.</summary>
    public MotionResult? LastMotion { get; private set; }
    public void RememberMotion(MotionResult? result) => LastMotion = result;
    public bool ShowReactionArrows { get; set; } = true;

    public void ShowMotion(MotionResult? result, int sample = 0)
    {
        Motion = result; MotionSample = sample;
        Invalidate();
    }

    public void SetMotionSample(int sample)
    {
        if (Motion == null) return;
        MotionSample = Math.Clamp(sample, 0, Motion.Samples - 1);
        Invalidate();
    }

    static readonly uint[] TrailColors = [0xFF35B6FF, 0xFFFF8A1F, 0xFF3FCF8E, 0xFFB79CFF, 0xFFFFC53D, 0xFFFF6B6F];

    void BuildMotionOverlay(Overlay o)
    {
        if (Motion is { } mr && ShowReactionArrows && mr.ReactionJoints.Length > 0) DrawReactions(o, mr);
        if (Motion is not { } m || !ShowMotionPaths) return;
        int colour = 0;
        for (int b = 0; b < m.Bodies.Length; b++)
        {
            if (m.Bodies[b].Name.StartsWith('@')) continue;
            var path = m.Path[b];
            double length = 0; for (int i = 1; i < path.Length; i++) length += (path[i] - path[i - 1]).Length;
            if (length < 1) continue;                                           // parts that stay put have no path worth drawing
            uint c = TrailColors[colour++ % TrailColors.Length];
            o.AddPolyline(path, c, 1.8f);
            o.AddDot(path[Math.Clamp(MotionSample, 0, path.Length - 1)], 0xFFFFFFFF, 7);
            o.AddLabel(path[^1], m.Bodies[b].Name, true, 0);
        }
    }

    /// <summary>Arrows at each joint for the force it holds at the time shown. The longest is a fraction of the assembly's size; labels give the force in newtons.</summary>
    void DrawReactions(Overlay o, MotionResult m)
    {
        int i = Math.Clamp(MotionSample, 0, m.Samples - 1);
        var comps = Components.ToDictionary(c => c.Component!.Key, c => c.Component!.Placement);
        var values = m.PoseAt(i);
        var joints = JointObjects.Select(j => values.TryGetValue(j.joint.Key, out var v) ? j.joint with { Value = v.v1, Value2 = v.v2 } : j.joint).ToList();
        var grounded = Components.Where(c => c.Component!.Grounded).Select(c => c.Component!.Key).ToHashSet();
        var pose = AssemblyKinematics.Solve(comps, grounded, joints, Couplings, []);
        double size = 0; foreach (var c in Components) { var mesh = c.Mesh; size = Math.Max(size, (mesh.Max - mesh.Min).Length); }
        double peak = 0; for (int j = 0; j < m.ReactionJoints.Length; j++) foreach (var f in m.ReactionForce[j]) peak = Math.Max(peak, f.Length);
        if (peak < 1e-12) return;
        for (int j = 0; j < m.ReactionJoints.Length; j++)
        {
            var jo = joints.FirstOrDefault(x => x.Name == m.ReactionJoints[j]); if (jo == null || !pose.TryGetValue(jo.ParentKey, out var parent)) continue;
            var f = m.ReactionForce[j][i]; if (f.Length < peak * 0.02) continue;
            var at = (parent * jo.A).T; var tip = at + f / peak * (size * 0.35);
            o.AddLine(at, tip, 0xFFFF6B6F, 2.6f); var d = (tip - at).Normalized(); var side = Xform.AnyPerpendicular(d) * size * 0.02;
            o.AddLine(tip, tip - d * size * 0.05 + side, 0xFFFF6B6F, 2.6f); o.AddLine(tip, tip - d * size * 0.05 - side, 0xFFFF6B6F, 2.6f);
            o.AddLabel(tip, $"{f.Length:G3} N", true, 1);
        }
    }

    /// <summary>The mass of a component in kilograms from its material and its geometry; null when it has no material.</summary>
    public MotionBody BodyOf(SceneObject comp, out bool hasMaterial)
    {
        var ci = comp.Component ?? throw new InvalidOperationException("That is not a component.");
        var mesh = LocalShapeOf(comp).Tessellate();
        var mat = comp.Material; hasMaterial = mat != null;
        double density = (mat?.Density ?? 7.85) * 1e-6;                         // g/cm³ → kg/mm³ (steel when no material is set: only the centre matters then)
        var b = MotionMass.FromMesh(ci.Key, comp.Name, mesh.Positions, mesh.Indices, density, out double volume);
        if (volume < 1e-9) b = b with { Com = (mesh.Min + mesh.Max) * 0.5 };
        return b;
    }

    /// <summary>Springs, actuators and loads placed in the assembly.</summary>
    public IEnumerable<SpringInfo> Springs => Objects.Select(o => o.Asm).OfType<SpringInfo>();
    public IEnumerable<LoadInfo> Loads => Objects.Select(o => o.Asm).OfType<LoadInfo>();

    public SceneObject AddForceElement(AssemblyData info, string namePrefix, Vec3 at)
    {
        var obj = new SceneObject(Guid.NewGuid(), NextName(namePrefix), Occ.Line(at, at + new Vec3(0, 0, 0.01)), ObjKind.Joint, Palette.JointColor, true, null, null, null, null, null, info);
        Objects = Objects.Add(obj);
        RaiseScene();
        return obj;
    }

    Vec3 WorldPoint(string compKey, double[] local) => ComponentByKey(compKey)?.Component is { } c ? c.Placement.Apply(new Vec3(local[0], local[1], local[2])) : new Vec3(local[0], local[1], local[2]);

    /// <summary>Springs and loads, drawn where they act now: a line between a spring's ends, an arrow for a force, an arc for a torque.</summary>
    void BuildForceOverlay(Overlay o)
    {
        double size = 0; foreach (var c in Components) { var m = c.Mesh; size = Math.Max(size, (m.Max - m.Min).Length); }
        double len = Math.Max(size * 0.15, 8);
        foreach (var obj in Objects)
        {
            if (!obj.Visible) continue;
            bool sel = _selection.Contains(SelItem.Of(obj));
            switch (obj.Asm)
            {
                case SpringInfo sp:
                {
                    var a = WorldPoint(sp.CompA, sp.PointA); var b = sp.CompB != null ? WorldPoint(sp.CompB, sp.PointB) : new Vec3(sp.PointB[0], sp.PointB[1], sp.PointB[2]);
                    uint col = sel ? 0xFFFFFFFF : sp.Stiffness == 0 && sp.Push != 0 ? 0xFFFF8A1F : 0xFF3FCF8E;
                    var d = b - a; double L = d.Length; if (L < 1e-9) break;
                    var u = d / L; var side = Xform.AnyPerpendicular(u) * Math.Min(L * 0.06, len * 0.25);
                    var pts = new List<Vec3> { a };
                    if (sp.Stiffness > 0) { int coils = 8; for (int i = 1; i < coils * 2; i++) pts.Add(a + u * (L * (0.15 + 0.7 * i / (coils * 2.0))) + side * (i % 2 == 0 ? 1 : -1)); }
                    pts.Add(b); o.AddPolyline(pts, col, sel ? 3f : 2.2f);
                    o.AddDot(a, col, 7); o.AddDot(b, col, 7);
                    o.AddLabel((a + b) * 0.5 + side * 2, sp.Name, true, 1);
                    break;
                }
                case LoadInfo ld:
                {
                    var p = WorldPoint(ld.Comp, ld.Point);
                    var comp = ComponentByKey(ld.Comp)?.Component; var dir = SpringInfo.V(ld.Direction);
                    if (ld.BodyFixed && comp != null) dir = comp.Placement.Rotate(dir);
                    if (dir.Length < 1e-9) break; dir = dir.Normalized();
                    uint col = sel ? 0xFFFFFFFF : 0xFFFF6B6F;
                    if (ld.IsTorque)
                    {
                        var perp = Xform.AnyPerpendicular(dir); var third = Vec3.Cross(dir, perp); var ring = new List<Vec3>();
                        for (int i = 0; i <= 24; i++) { double ang = Math.PI * 1.5 * i / 24; ring.Add(p + (perp * Math.Cos(ang) + third * Math.Sin(ang)) * len * 0.5); }
                        o.AddPolyline(ring, col, 2.6f); var tip = ring[^1]; o.AddLine(tip, tip + dir * len * 0.2 + third * len * 0.1, col, 2.6f);
                    }
                    else
                    {
                        var from = ld.Magnitude >= 0 ? p - dir * len : p + dir * len; var head = Xform.AnyPerpendicular(dir) * len * 0.15;
                        o.AddLine(from, p, col, 2.8f); o.AddLine(p, p + (from - p).Normalized() * len * 0.3 + head, col, 2.8f); o.AddLine(p, p + (from - p).Normalized() * len * 0.3 - head, col, 2.8f);
                    }
                    o.AddDot(p, col, 6); o.AddLabel(p + dir * len * 0.2, ld.Name, true, 1);
                    break;
                }
            }
        }
    }

    /// <summary>The assembly as the motion engine sees it: every component with its mass, the joints and couplings that connect them, and the springs and loads acting on it.</summary>
    public MotionEngine BuildMotionEngine(IEnumerable<MotionDof> dofs, bool dynamic, out List<string> notes, out List<MotionBody> bodies, IEnumerable<MotionPoint>? points = null)
    {
        notes = []; bodies = [];
        var missing = new List<string>();
        foreach (var comp in Components)
        {
            var b = BodyOf(comp, out bool has);
            if (!has) { missing.Add(comp.Name); b = dynamic ? b : b with { Mass = 0 }; }
            bodies.Add(b);
        }
        if (dynamic && missing.Count > 0) throw new InvalidOperationException("Assign a material to " + string.Join(", ", missing) + " first (Materials). Motion dynamics needs each part's density.");
        var start = Components.ToDictionary(o => o.Component!.Key, o => o.Component!.Placement);
        var grounded = Components.Where(o => o.Component!.Grounded).Select(o => o.Component!.Key).ToHashSet();
        var forces = Springs.Select(s => s.ToForce()).Concat(Loads.Select(l => l.ToForce())).ToList();
        foreach (var sp in Springs) if (!start.ContainsKey(sp.CompA) || sp.CompB != null && !start.ContainsKey(sp.CompB)) notes.Add($"{sp.Name} is attached to a part that was removed, so it is left out.");
        foreach (var ld in Loads) if (!start.ContainsKey(ld.Comp)) notes.Add($"{ld.Name} acts on a part that was removed, so it is left out.");
        return new MotionEngine(start, grounded, JointObjects.Select(j => j.joint), Couplings, bodies, dofs, forces, points);
    }
}
