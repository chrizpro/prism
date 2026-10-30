using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Commands;

/// <summary>
/// Place and adjust lights for the render. A sun is a distant light (direction, lux, colour temperature); a lamp is a point or spot light with
/// physical units (lumens, inverse-square falloff, cone). Drag a light in the viewport, or type its numbers. Lights are saved with the document.
/// </summary>
public sealed class LightsCommand : Command
{
    readonly ChoiceParam _kind;
    readonly BoolParam _on;
    readonly NumberParam _lux, _lumens, _kelvin, _cone, _soft, _radius, _sunSize, _azimuth, _elevation, _x, _y, _z, _ax, _ay, _az;
    bool _syncing;

    public LightsCommand(LightKind? addKind = null)
    {
        _addKind = addKind;
        Add(new ActionParam { Label = "Add sun", Run = () => AddLight(LightKind.Sun) });
        Add(new ActionParam { Label = "Add lamp (point)", Run = () => AddLight(LightKind.Point) });
        Add(new ActionParam { Label = "Add spot", Run = () => AddLight(LightKind.Spot) });
        Add(new ActionParam { Label = "Select next light", Run = SelectNext });
        Add(new ActionParam { Label = "Delete this light", Run = () => { if (S.SelectedLight is { } l) { S.RemoveLight(l.Id); Sync(); } } });
        _on = Check("Light is on", true);
        _kind = Choice("Type", 1, "Sun (distant)", "Lamp (point)", "Spot");
        _lux = Num("Sunlight", 100000, 100, 150000, 1000, "lx");
        _lumens = Num("Power", 3000, 1, 1e7, 100, "lm");
        _kelvin = Num("Colour temperature", 5600, 1700, 12000, 100, "K");
        _cone = Num("Cone angle", 45, 5, 170, 1, "°");
        _soft = Num("Soft edge", 25, 0, 100, 5, "%");
        _radius = Num("Lamp size (radius)", 5, 0, 1000, 0.5, "mm");
        _sunSize = Num("Sun apparent size", 0.53, 0, 10, 0.05, "°");
        _azimuth = Num("Sun direction", 45, -360, 360, 5, "°");
        _elevation = Num("Sun height", 45, 1, 90, 5, "°");
        _x = Num("Position X", 0); _y = Num("Position Y", 0); _z = Num("Position Z", 0);
        _ax = Num("Aim X", 0); _ay = Num("Aim Y", 0); _az = Num("Aim Z", 0);
        Tool = new LightTool(this);
    }

    readonly LightKind? _addKind;
    public override string Title => "Lights";
    public override bool SupportsPreview => false;
    public override string Icon => "render";
    public override string OkText => "Done";
    public override string Hint => "Add a sun (direction, lux, colour temperature) or a lamp or spot (lumens, real distance falloff). Drag a light's dot in the viewport to move it; Ctrl keeps it level, and a spot's second dot aims it. Press Delete to remove the selected light. Lights shine in the next render, which should use \"Only my lights\" for exact control.";

    protected override void OnBegin()
    {
        if (_addKind is { } kind) S.AddLight(kind);
        else if (S.SelectedLight == null && S.Lights.Count > 0) S.SelectedLightId = S.Lights[0].Id;
        Sync();
    }

    public override void OnEnd() => S.Invalidate();

    void AddLight(LightKind kind) { S.AddLight(kind); Sync(); }

    void SelectNext()
    {
        if (S.Lights.Count == 0) return;
        int i = S.Lights.FindIndex(l => l.Id == S.SelectedLightId);
        S.SelectedLightId = S.Lights[(i + 1) % S.Lights.Count].Id;
        Sync(); S.Invalidate();
    }

    protected override void UpdateVisibility()
    {
        var kind = (LightKind)_kind.Index;
        bool has = S?.SelectedLight != null;
        foreach (var p in new Param[] { _on, _kind, _kelvin }) p.IsVisible = has;
        _lux.IsVisible = has && kind == LightKind.Sun;
        _sunSize.IsVisible = _azimuth.IsVisible = _elevation.IsVisible = has && kind == LightKind.Sun;
        _lumens.IsVisible = _radius.IsVisible = has && kind != LightKind.Sun;
        _x.IsVisible = _y.IsVisible = _z.IsVisible = has && kind != LightKind.Sun;
        _cone.IsVisible = _soft.IsVisible = _ax.IsVisible = _ay.IsVisible = _az.IsVisible = has && kind == LightKind.Spot;
    }

    /// <summary>Show the selected light's numbers in the panel (after it was dragged, selected or added).</summary>
    public void Sync()
    {
        if (S == null) return;
        ConfigureInputs(() =>
        {
            _syncing = true;
            if (S.SelectedLight is { } l)
            {
                _on.Value = l.Enabled; _kind.SetSilently((int)l.Kind); _kelvin.Value = l.Kelvin;
                if (l.Kind == LightKind.Sun)
                {
                    _lux.Value = l.Intensity; _sunSize.Value = l.Size;
                    var d = l.TowardLight;
                    _azimuth.Value = Math.Round(Math.Atan2(d.Y, d.X) * 180 / Math.PI, 1); _elevation.Value = Math.Round(Math.Asin(Math.Clamp(d.Z, -1, 1)) * 180 / Math.PI, 1);
                }
                else
                {
                    _lumens.Value = l.Intensity; _radius.Value = l.Size; _cone.Value = l.ConeAngle; _soft.Value = Math.Round(l.Softness * 100);
                    _x.Value = Math.Round(l.Position.X, 2); _y.Value = Math.Round(l.Position.Y, 2); _z.Value = Math.Round(l.Position.Z, 2);
                    _ax.Value = Math.Round(l.Aim.X, 2); _ay.Value = Math.Round(l.Aim.Y, 2); _az.Value = Math.Round(l.Aim.Z, 2);
                }
            }
            _syncing = false;
        });
        UpdateVisibility();
        Results.Clear();
        if (S.Lights.Count == 0) Results.Add(new ResultLine("Lights", "none yet: add a sun or a lamp"));
        foreach (var l in S.Lights)
            Results.Add(new ResultLine((l.Id == S.SelectedLightId ? "▶ " : "") + l.Name, (l.Enabled ? "" : "off, ") + (l.Kind == LightKind.Sun ? $"{l.Intensity:0} lx, {l.Kelvin:0} K" : $"{l.Intensity:0} lm, {l.Kelvin:0} K")));
        OnPropertyChanged(nameof(Results));
    }

    protected override CommandOutput? Build()
    {
        if (_syncing || S.SelectedLight is not { } l) return new CommandOutput { NoOp = true };
        var before = l.Clone();
        l.Enabled = _on.Value; l.Kind = (LightKind)_kind.Index; l.Kelvin = _kelvin.Value;
        if (l.Kind == LightKind.Sun)
        {
            if (before.Kind != LightKind.Sun) { l.Intensity = _lux.Value = 100000; l.Size = _sunSize.Value; }
            else { l.Intensity = _lux.Value; l.Size = _sunSize.Value; }
            double az = _azimuth.Value * Math.PI / 180, el = _elevation.Value * Math.PI / 180;
            double reach = Math.Max((l.Position - l.Aim).Length, 1);
            var dir = new Vec3(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
            // only re-aim when the angles were typed (keeps a dragged sun where it is)
            var shown = before.TowardLight;
            double shownAz = Math.Round(Math.Atan2(shown.Y, shown.X) * 180 / Math.PI, 1), shownEl = Math.Round(Math.Asin(Math.Clamp(shown.Z, -1, 1)) * 180 / Math.PI, 1);
            if (Math.Abs(shownAz - _azimuth.Value) > 1e-6 || Math.Abs(shownEl - _elevation.Value) > 1e-6 || before.Kind != LightKind.Sun) l.Position = l.Aim + dir * reach;
        }
        else
        {
            if (before.Kind == LightKind.Sun) { var (c, r) = S.SceneSphere(); l.Position = c + before.TowardLight * r * 2.6; _lumens.Value = l.Intensity = Math.Round(Photometry.LumensFor(l.Kind, 1.5, r * 2.6, l.ConeAngle) / 10) * 10; }
            else l.Intensity = _lumens.Value;
            l.Size = _radius.Value; l.ConeAngle = _cone.Value; l.Softness = _soft.Value / 100;
            var typed = new Vec3(_x.Value, _y.Value, _z.Value);
            if (typed.DistanceTo(new Vec3(Math.Round(before.Position.X, 2), Math.Round(before.Position.Y, 2), Math.Round(before.Position.Z, 2))) > 1e-9) l.Position = typed;
            var aim = new Vec3(_ax.Value, _ay.Value, _az.Value);
            if (aim.DistanceTo(new Vec3(Math.Round(before.Aim.X, 2), Math.Round(before.Aim.Y, 2), Math.Round(before.Aim.Z, 2))) > 1e-9) l.Aim = aim;
        }
        if (!Same(before, l)) { S.EditedLights(); if (before.Kind != l.Kind) Sync(); }
        return new CommandOutput { NoOp = true };
    }

    static bool Same(RenderLight a, RenderLight b) => a.Enabled == b.Enabled && a.Kind == b.Kind && a.Intensity == b.Intensity && a.Kelvin == b.Kelvin
        && a.ConeAngle == b.ConeAngle && a.Softness == b.Softness && a.Size == b.Size && a.Position == b.Position && a.Aim == b.Aim;
}

/// <summary>Drag a light's dot (or a spot's aim dot) in the viewport.</summary>
public sealed class LightTool(LightsCommand cmd) : IViewportTool
{
    RenderLight? _light; bool _aim;
    Vec3 _planeOrigin, _planeNormal;

    public string Prompt => "Drag a light to move it. Ctrl keeps it level. Delete removes the selected light.";

    (RenderLight light, bool aim)? Find(CadSession s, double x, double y)
    {
        (RenderLight, bool)? best = null; double bestDistance = 16;
        foreach (var l in s.Lights)
        {
            foreach (var aim in l.Kind == LightKind.Spot ? new[] { false, true } : new[] { false })
            {
                if (s.Camera.Project(aim ? l.Aim : l.Position) is not { } p) continue;
                double d = Math.Sqrt((p.x - x) * (p.x - x) + (p.y - y) * (p.y - y));
                if (d < bestDistance) { bestDistance = d; best = (l, aim); }
            }
        }
        return best;
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left || Find(s, p.X, p.Y) is not { } hit) return false;
        _light = hit.light; _aim = hit.aim; s.SelectedLightId = _light.Id;
        _planeOrigin = _aim ? _light.Aim : _light.Position; _planeNormal = s.Camera.EyeDirection;
        cmd.Sync(); s.Invalidate();
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (_light == null || !p.Left) return;
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        Vec3? target;
        if (_light.Kind == LightKind.Sun)
        {
            // slide around the sphere the sun sits on
            var c = _light.Aim; double r = Math.Max((_light.Position - c).Length, 1);
            var oc = o - c; double b = Vec3.Dot(oc, d), disc = b * b - (Vec3.Dot(oc, oc) - r * r);
            Vec3 point = disc >= 0 ? o + d * (-b - Math.Sqrt(disc)) : o + d * Math.Max(-b, 0);
            target = c + (point - c).Normalized() * r;
        }
        else
        {
            var n = p.Ctrl ? Vec3.UnitZ : _planeNormal;
            double den = Vec3.Dot(d, n);
            target = Math.Abs(den) < 1e-6 ? null : o + d * (Vec3.Dot(_planeOrigin - o, n) / den);
        }
        if (target is not { } t) return;
        if (_light.Kind == LightKind.Sun) _light.Position = t; else if (_aim) _light.Aim = t; else _light.Position = t;
        s.EditedLights(); cmd.Sync();
    }

    public void PointerUp(CadSession s, PointerInfo p) => _light = null;

    public bool Key(CadSession s, string key)
    {
        if (key != "Delete" || s.SelectedLight is not { } l) return false;
        s.RemoveLight(l.Id); cmd.Sync(); return true;
    }

    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { }
}
