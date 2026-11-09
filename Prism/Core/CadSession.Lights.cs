using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Core;

/// <summary>Lights and the picture backdrop: kept on the document, drawn in the viewport, used by the next render.</summary>
public sealed partial class CadSession
{
    /// <summary>UI hook: opens the assistant with the render-background task (set by the view; null in headless hosts).</summary>
    public Action? GenerateBackgroundAi;
    public List<RenderLight> Lights { get; private set; } = [];
    public RenderSetup RenderSetup { get; set; } = new();
    public Guid? SelectedLightId { get; set; }
    public bool ShowLights { get; set; } = true;
    public RenderLight? SelectedLight => Lights.FirstOrDefault(l => l.Id == SelectedLightId);
    public event Action? LightsChanged;

    public void LoadRenderState(List<RenderLight>? lights, RenderSetup? setup)
    {
        Lights = lights ?? []; RenderSetup = setup ?? new(); SelectedLightId = null; LightsChanged?.Invoke();
    }

    /// <summary>The centre and radius of everything visible: where new lights are put.</summary>
    public (Vec3 center, double radius) SceneSphere()
    {
        Vec3? lo = null, hi = null;
        foreach (var o in Objects)
        {
            if (!o.Visible || o.IsAnnotation || HiddenIds.Contains(o.Id)) continue;
            try
            {
                var m = o.Mesh; if (m.Indices.Length == 0) continue;
                lo = lo is { } a ? new(Math.Min(a.X, m.Min.X), Math.Min(a.Y, m.Min.Y), Math.Min(a.Z, m.Min.Z)) : m.Min;
                hi = hi is { } b ? new(Math.Max(b.X, m.Max.X), Math.Max(b.Y, m.Max.Y), Math.Max(b.Z, m.Max.Z)) : m.Max;
            }
            catch { }
        }
        if (lo is not { } l || hi is not { } h) return (Camera.Target, Math.Max(Camera.Distance / 3, 10));
        return ((l + h) * .5, Math.Max((h - l).Length * .5, 1));
    }

    /// <summary>Drop a new light up and to the left of the model as seen from the current view. A lamp gets the power that lights the model about as brightly as the studio lights do.</summary>
    public RenderLight AddLight(LightKind kind)
    {
        var (center, radius) = SceneSphere();
        var cam = Camera;
        var toward = (cam.EyeDirection * .55 + cam.Up * .75 - cam.Right * .55).Normalized();
        var light = new RenderLight { Kind = kind, Aim = center };
        light.Name = $"{RenderLight.KindName(kind)} {Lights.Count(l => l.Kind == kind) + 1}";
        if (kind == LightKind.Sun)
        {
            light.Position = center + toward * (radius * 3);
            light.Intensity = 100000; light.Kelvin = 5600; light.Size = Photometry.SunDiameterDegrees;
        }
        else
        {
            double distance = radius * 2.6;
            light.Position = center + toward * distance;
            light.Kelvin = 4300; light.Size = Math.Max(radius * .03, .5);
            light.ConeAngle = kind == LightKind.Spot ? 45 : 50;
            light.Intensity = Math.Max(10, Math.Round(Photometry.LumensFor(kind, 1.5, distance, light.ConeAngle) / 10) * 10);
        }
        Lights.Add(light); SelectedLightId = light.Id; EditedLights();
        return light;
    }

    public void RemoveLight(Guid id)
    {
        if (Lights.RemoveAll(l => l.Id == id) == 0) return;
        if (SelectedLightId == id) SelectedLightId = Lights.LastOrDefault()?.Id;
        EditedLights();
    }

    /// <summary>Call after changing a light: marks the document as changed and redraws.</summary>
    public void EditedLights() { Dirty = true; LightsChanged?.Invoke(); Invalidate(); }

    const uint LightWarm = 0xFFFFC53D, LightOff = 0xFF8A8F98;
    void BuildLightsOverlay(Overlay o)
    {
        if (!ShowLights && ActiveCommand is not Commands.LightsCommand) return;
        foreach (var l in Lights)
        {
            bool sel = l.Id == SelectedLightId;
            uint col = l.Enabled ? LightWarm : LightOff;
            float w = sel ? 3f : 1.6f;
            double px = Camera.PixelSizeAt(l.Position), arm = px * (sel ? 12 : 9);
            if (l.Kind == LightKind.Sun)
            {
                var d = -l.TowardLight;
                var u = Math.Abs(d.Z) < .9 ? Vec3.Cross(d, Vec3.UnitZ).Normalized() : Vec3.UnitX; var v = Vec3.Cross(d, u);
                double span = (l.Position - l.Aim).Length;
                foreach (var (a, b) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var start = l.Position + (u * a + v * b) * (px * 14);
                    o.AddLine(start, start + d * (span * (a == 0 && b == 0 ? 1 : .3)), col, a == 0 && b == 0 ? w : 1.2f);
                }
                for (int i = 0; i < 12; i++)
                {
                    double ang = 2 * Math.PI * i / 12; var r = u * Math.Cos(ang) + v * Math.Sin(ang);
                    o.AddLine(l.Position + r * (px * 8), l.Position + r * (px * 16), col, 1.6f);
                }
            }
            else
            {
                o.AddLine(l.Position - Vec3.UnitX * arm, l.Position + Vec3.UnitX * arm, col, w);
                o.AddLine(l.Position - Vec3.UnitY * arm, l.Position + Vec3.UnitY * arm, col, w);
                o.AddLine(l.Position - Vec3.UnitZ * arm, l.Position + Vec3.UnitZ * arm, col, w);
                if (l.Kind == LightKind.Spot)
                {
                    var axis = l.Axis; double length = (l.Aim - l.Position).Length;
                    var u = Math.Abs(axis.Z) < .9 ? Vec3.Cross(axis, Vec3.UnitZ).Normalized() : Vec3.UnitX; var v = Vec3.Cross(axis, u);
                    double r = length * Math.Tan(Math.Clamp(l.ConeAngle, 1, 175) * Math.PI / 360);
                    var ring = new List<Vec3>();
                    for (int i = 0; i < 40; i++) { double a = 2 * Math.PI * i / 40; ring.Add(l.Aim + (u * Math.Cos(a) + v * Math.Sin(a)) * r); }
                    o.AddPolyline(ring, col, 1.4f, true);
                    for (int i = 0; i < 40; i += 10) o.AddLine(l.Position, ring[i], col, 1.2f);
                    o.AddDot(l.Aim, col, sel ? 9 : 6);
                }
            }
            o.AddDot(l.Position, col, sel ? 12 : 9);
            o.AddLabel(l.Position + Vec3.UnitZ * (px * 18), l.Kind == LightKind.Sun ? $"{l.Name}  {l.Intensity:0} lx" : $"{l.Name}  {l.Intensity:0} lm", true, sel ? 6 : 0);
        }
    }
}
