using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Cut a freeform the way Blender's knife does: click points across the form and the cut follows the line you draw on screen, splitting every face it
/// crosses. Clicks snap to the cage's points and edge midpoints. Options: cut through (also the faces turned away), run the cut on to the edges at both ends,
/// and constrain the angle in 45° steps. Or cut along a plane.
/// </summary>
public sealed class FormKnifeCommand : Command
{
    readonly SelectionSlot _form, _plane;
    readonly ChoiceParam _mode, _across;
    readonly NumberParam _position, _crease;
    readonly BoolParam _through, _extend, _snap, _angle;
    readonly TextParam _path;
    readonly List<Vec3> _preview = [];

    const string Line = "A line drawn across the form", PlaneMode = "A plane";

    public FormKnifeCommand()
    {
        _form = Slot(new SelectionSlot { Label = "Freeform to cut", Filter = SelFilter.Form });
        _mode = Choice("Cut with", 0, Line, PlaneMode);
        _path = Add(new TextParam { Label = "Points of the cut (set by clicking)", Value = "", IsAdvanced = true, HelpText = "One point per line: where you clicked, then where the camera was. Clear the text to start the line again." });
        _through = Check("Cut through (also the faces turned away)", false);
        _extend = Check("Run the cut on to the edges at both ends", true);
        _snap = Check("Snap to points and edge midpoints", true);
        _angle = Check("Constrain the angle (45° steps)", false);
        _plane = Slot(new SelectionSlot { Label = "Cutting plane or flat face (optional)", Filter = SelFilter.Plane | SelFilter.Face, Optional = true });
        _across = Choice("Or cut across", 0, "Left to right (X)", "Front to back (Y)", "Bottom to top (Z)");
        _position = Num("Position along that direction", 0, -100000, 100000, .5);
        _crease = Num("Crease the cut", 0, 0, 5, .1, "");
        _crease.HelpText = "0 leaves the cut as plain edges. A sharpness above 0 makes it a crease, so the smooth surface keeps a line there.";
    }

    bool LineMode => _mode.Index == 0;
    public override string Title => "Knife";
    public override string Icon => "splitbody";
    public override string Hint => "Click points across the form to draw the cut: it follows the line you see, splitting every face it crosses. Click the form first, then each point; clicks snap to points and edge midpoints. Backspace (or undo) takes the last point back, and OK makes the cut. Cut through also cuts the faces turned away. Or choose A plane to cut along a plane or a flat face.";

    protected override void UpdateVisibility()
    {
        bool line = LineMode;
        _path.IsVisible = _through.IsVisible = _extend.IsVisible = _snap.IsVisible = _angle.IsVisible = line;
        _plane.IsVisible = !line; _across.IsVisible = _position.IsVisible = !line && _plane.Items.Count == 0;
    }

    protected override void OnBegin()
    {
        if (_form.Items.Count == 0)
        {
            var forms = S.Objects.Where(o => o.Form != null && o.Visible).ToList();
            if (forms.Count == 1) _form.Items.Add(SelItem.Of(forms[0]));
        }
    }

    // ---------------------------------------------------------------- the clicked points
    static string Format(Vec3 p, KnifeView v) =>
        string.Join(" | ", [$"{p.X:0.######} {p.Y:0.######} {p.Z:0.######}", $"{v.Eye.X:0.######} {v.Eye.Y:0.######} {v.Eye.Z:0.######}", $"{v.Direction.X:0.######} {v.Direction.Y:0.######} {v.Direction.Z:0.######}", v.Orthographic ? "o" : "p"]);

    static bool TryVec(string text, out Vec3 v)
    {
        v = default; var f = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 3 || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
        v = new Vec3(x, y, z); return true;
    }

    List<(Vec3 point, KnifeView view)> Points()
    {
        var list = new List<(Vec3, KnifeView)>();
        foreach (var raw in _path.Value.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Split('|', StringSplitOptions.TrimEntries);
            if (part.Length != 4 || !TryVec(part[0], out var p) || !TryVec(part[1], out var eye) || !TryVec(part[2], out var dir)) throw new FormatException($"'{raw.Trim()}' is not a point of the cut. Clear the points and click them again.");
            list.Add((p, new KnifeView(eye, dir, part[3] == "o")));
        }
        return list;
    }

    void Store(List<(Vec3 point, KnifeView view)> points) =>
        ConfigureInputs(() => _path.Value = string.Join("\n", points.Select(p => Format(p.point, p.view))));

    /// <summary>The clicked spot pulled to a nearby point or edge midpoint of the cage, when that is close on screen.</summary>
    Vec3 Snapped(SceneObject form, Vec3 clicked)
    {
        if (!_snap.Value || form.Form is not { } cage || S.Camera.Project(clicked) is not { } at) return clicked;
        Vec3 best = clicked; double bestDistance = 10;
        void Try(Vec3 p)
        {
            if (S.Camera.Project(p) is not { } q) return;
            double d = Math.Sqrt((q.x - at.x) * (q.x - at.x) + (q.y - at.y) * (q.y - at.y));
            if (d < bestDistance) { bestDistance = d; best = p; }
        }
        foreach (var v in cage.V) Try(v);
        foreach (var (a, b) in cage.Edges()) Try((cage.V[a] + cage.V[b]) * .5);
        return best;
    }

    /// <summary>With the angle constraint, a new point is moved around the last one to the nearest 45° direction on screen and put back on the form.</summary>
    Vec3 Constrained(Vec3 previous, Vec3 clicked, SceneObject form)
    {
        if (!_angle.Value || S.Camera.Project(previous) is not { } from || S.Camera.Project(clicked) is not { } to) return clicked;
        double dx = to.x - from.x, dy = to.y - from.y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return clicked;
        double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        var hit = S.Pick(from.x + length * Math.Cos(angle), from.y + length * Math.Sin(angle), SelFilter.Form);
        return hit is { } h && h.Item.Obj == form.Id ? h.Point : clicked;
    }

    public override bool PickPoint(PickResult hit, bool additive)
    {
        if (!LineMode) return false;
        if (_form.Items.Count == 0 && !Pick(hit.Item, false)) return false;
        if (_form.Items.Count == 0 || hit.Item.Obj != _form.Items[0].Obj || S.Find(hit.Item.Obj) is not { Form: not null } form) return false;
        var points = Points();
        var p = Snapped(form, hit.Point);
        if (points.Count > 0) p = Constrained(points[^1].point, p, form);
        var cam = S.Camera;
        points.Add((p, new KnifeView(cam.Eye, -cam.EyeDirection, cam.Orthographic)));
        Store(points); OnChanged();
        return true;
    }

    public override bool TryUndo()
    {
        if (!LineMode || string.IsNullOrWhiteSpace(_path.Value)) return false;
        var points = Points(); if (points.Count == 0) return false;
        points.RemoveAt(points.Count - 1); Store(points); OnChanged();
        return true;
    }

    // ---------------------------------------------------------------- the plane
    (Vec3 origin, Vec3 normal) CutPlane()
    {
        if (_plane.Items.Count > 0)
        {
            var plane = S.PlaneOf(_plane.Items[0]) ?? throw new FormatException("Pick a plane or a flat face to cut along.");
            return (plane.Origin, plane.Normal);
        }
        var axis = _across.Index switch { 0 => Vec3.UnitX, 1 => Vec3.UnitY, _ => Vec3.UnitZ };
        return (axis * _position.Value, axis);
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_form.Items.Count == 0) return;
        try
        {
            if (LineMode)
            {
                var points = Points();
                if (points.Count > 0) o.AddPolyline([.. points.Select(p => p.point)], 0xFFFF8A1F, 2.5f);
                foreach (var (p, _) in points) o.AddDot(p, 0xFFFF8A1F, 8);
                foreach (var p in _preview) o.AddDot(p, 0xFF4FC3F7, 6);
                return;
            }
            var (origin, normal) = CutPlane();
            var reference = Math.Abs(normal.X) < .9 ? Vec3.UnitX : Vec3.UnitY;
            var u = Vec3.Cross(normal, reference).Normalized(); var v = Vec3.Cross(normal, u).Normalized();
            double r = 0; Vec3 middle = origin;
            foreach (var obj in ObjectsOf(_form)) if (obj.Form is { } cage) { var (a, b) = cage.Bounds(); r = Math.Max(r, (b - a).Length * .6); middle = (a + b) * .5; }
            if (r <= 0) return;
            var c = middle - normal * Vec3.Dot(middle - origin, normal);      // the middle of the form, brought onto the plane
            o.AddPolyline([c + (u + v) * r, c + (u - v) * r, c + (-u - v) * r, c + (v - u) * r, c + (u + v) * r], 0xFFFF8A1F, 2);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException) { }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _preview.Clear();
        if (!Complete) return null;
        var output = new CommandOutput { Label = "Knife", Icon = Icon };
        int points = 0, faces = 0, ended = 0;
        List<(Vec3 point, KnifeView view)>? path = null;
        if (LineMode)
        {
            path = Points();
            if (path.Count < 2) { Results.Add(new ResultLine("Draw the cut", "click two or more points across the form", 1)); return null; }
        }
        foreach (var obj in ObjectsOf(_form))
        {
            if (obj.Form is not { } cage) continue;
            var work = cage.Clone();
            List<int> added; int cut, stop = 0;
            if (path != null) added = FormKnife.Cut(work, [.. path.Select(p => p.point)], [.. path.Select(p => p.view)], _extend.Value, _through.Value, _crease.Value, out cut, out stop);
            else { var (origin, normal) = CutPlane(); added = FormOps.CutWithPlane(work, origin, normal, out cut, _crease.Value); }
            ended += stop;
            if (cut == 0 && added.Count == 0) continue;
            points += added.Count; faces += cut;
            foreach (var v in added) _preview.Add(work.V[v]);
            output.ReplaceForms.Add((obj, work));
        }
        if (ended > 0) Results.Add(new ResultLine("Faces the line ended inside", $"{ended}, left uncut. Turn on Run the cut on to the edges, or draw it across.", 2));
        if (output.ReplaceForms.Count == 0)
            throw new FormatException(path != null
                ? "The line does not cross any face. Draw it across edges of the form, or turn on Cut through if the faces are turned away."
                : "The plane does not cut through any face of the form. Move it, or turn it, so it crosses the cage.");
        Results.Add(new ResultLine("Faces cut", faces.ToString()));
        Results.Add(new ResultLine("New points", points.ToString()));
        return output;
    }
}
