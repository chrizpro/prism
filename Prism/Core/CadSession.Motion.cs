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
        if (Motion is not { } m || !ShowMotionPaths) return;
        int colour = 0;
        for (int b = 0; b < m.Bodies.Length; b++)
        {
            var path = m.Path[b];
            double length = 0; for (int i = 1; i < path.Length; i++) length += (path[i] - path[i - 1]).Length;
            if (length < 1) continue;                                           // parts that stay put have no path worth drawing
            uint c = TrailColors[colour++ % TrailColors.Length];
            o.AddPolyline(path, c, 1.8f);
            o.AddDot(path[Math.Clamp(MotionSample, 0, path.Length - 1)], 0xFFFFFFFF, 7);
            o.AddLabel(path[^1], m.Bodies[b].Name, true, 0);
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

    /// <summary>The assembly as the motion engine sees it: every component with its mass, and the joints and couplings that connect them.</summary>
    public MotionEngine BuildMotionEngine(IEnumerable<MotionDof> dofs, bool dynamic, out List<string> notes, out List<MotionBody> bodies)
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
        return new MotionEngine(start, grounded, JointObjects.Select(j => j.joint), Couplings, bodies, dofs);
    }
}
