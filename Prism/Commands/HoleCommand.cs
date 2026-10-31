using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>A subtractive feature on any exact body, independent of how the body was created.</summary>
public sealed class HoleCommand : Command
{
    readonly SelectionSlot _target;
    readonly ChoiceParam _type, _size, _extent, _axis;
    readonly NumberParam _diameter, _pitch, _depth, _threadDepth, _u, _v, _wx, _wy, _wz;
    readonly ChoiceParam _position;
    readonly BoolParam _reverse, _leftHand;
    readonly TextParam _more;
    int _grab = -1;
    static readonly (double diameter, double pitch)[] Metric = [(3, .5), (4, .7), (5, .8), (6, 1), (8, 1.25), (10, 1.5), (12, 1.75)];
    (double d, double p, double h, bool left)? _grooveKey;
    Shape? _groove;
    bool _settingSize;

    public HoleCommand()
    {
        _target = Slot(new SelectionSlot { Label = "Body or entry face", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form | SelFilter.Face });
        _type = Choice("Hole type", 0, "Plain", "Threaded (modeled)");
        _size = Choice("Metric size", 4, "Custom", "M3 × 0.5", "M4 × 0.7", "M5 × 0.8", "M6 × 1", "M8 × 1.25", "M10 × 1.5", "M12 × 1.75");
        _diameter = Num("Diameter", 6, .01, 10000, .1);
        _pitch = Num("Thread pitch", 1, .05, 100, .05);
        _extent = Choice("Extent", 0, "Through all", "Blind");
        _depth = Num("Hole depth", 10, .01, 100000, .5);
        _threadDepth = Num("Thread depth (0 = full hole)", 0, 0, 100000, .5);
        _axis = Choice("Direction", 0, "Automatic / face normal", "World Z", "World X", "World Y");
        _u = Num("Center offset U", 0, -100000, 100000, .5);
        _v = Num("Center offset V", 0, -100000, 100000, .5);
        _position = Choice("Position", 0, "Face center + offset", "World point");
        _wx = Num("Point X", 0, -100000, 100000, .5);
        _wy = Num("Point Y", 0, -100000, 100000, .5);
        _wz = Num("Point Z", 0, -100000, 100000, .5);
        _more = Text("More holes (U, V offsets, one per line)", "", "e.g. 10, 5  - click the face again to add holes, drag a dot to move it");
        _reverse = Check("Reverse direction", false);
        _leftHand = Check("Left-hand thread", false);
        _size.Changed = () =>
        {
            if (_size.Index > 0 && _size.Index <= Metric.Length)
            {
                var size = Metric[_size.Index - 1];
                _settingSize = true;
                try { ConfigureInputs(() => { _diameter.Value = size.diameter; _pitch.Value = size.pitch; }); }
                finally { _settingSize = false; }
            }
            OnChanged();
        };
        void DimensionsChanged()
        {
            if (!_settingSize && _size.Index > 0 && _size.Index <= Metric.Length)
            {
                var size = Metric[_size.Index - 1];
                if (_diameter.Value != size.diameter || _pitch.Value != size.pitch) _size.SetSilently(0);
            }
            OnChanged();
        }
        _diameter.Changed = _pitch.Changed = DimensionsChanged;
    }

    public override string Title => "Hole";
    public override string Icon => "pcb-hole";
    public override string Hint => "Select a body or an entry face. The hole starts at the face center (body selections use the top center). Click the face where you want the hole, click again for more holes, drag a dot to move it (Ctrl+click an extra dot removes it). Offsets are in U/V. Choose a plain hole or a modeled 60° thread; Diameter is the thread's nominal major diameter. Closed forms become exact bodies. Meshes must first be converted with Mesh to Solid.";

    protected override void UpdateVisibility()
    {
        bool thread = _type.Index == 1;
        _size.IsVisible = _pitch.IsVisible = _threadDepth.IsVisible = _leftHand.IsVisible = thread;
        _depth.IsVisible = _extent.Index == 1;
        _diameter.Caption = thread ? "Nominal thread diameter" : "Hole diameter";
        bool atPoint = _position.Index == 1;
        _u.IsVisible = _v.IsVisible = !atPoint;
        _wx.IsVisible = _wy.IsVisible = _wz.IsVisible = atPoint;
        _u.HelpText = "Offsets are measured in the entry plane, relative to its center.";
        _wx.HelpText = "The hole is placed where this point, projected onto the entry plane, lies. Use it to drill at known coordinates.";
        _pitch.HelpText = "Truncated 60° geometry; no manufacturing allowance is added.";
    }

    (Vec3 center, Vec3 x, Vec3 y, Vec3 outward) Base(SceneObject body, SelItem item)
    {
        var bounds = body.Shape.Props();
        Vec3 outward = _axis.Index switch { 2 => Vec3.UnitX, 3 => Vec3.UnitY, _ => Vec3.UnitZ };
        Vec3 center = bounds.Center;
        if (item.Kind == SelKind.Face)
        {
            var face = S.ShapeOf(item) ?? throw new FormatException("The entry face no longer exists.");
            var info = Occ.FaceInfoOf(face);
            if (_axis.Index == 0 && info.Type != "Plane")
                throw new FormatException("Select a planar entry face, or choose a world direction for a curved face.");
            center = face.Props().Center;
            if (_axis.Index == 0)
            {
                outward = S.FaceFrame(item)?.normal ?? throw new FormatException("Could not read the entry face normal.");
                // The bounding-box center of a tilted face may lie off its plane.
                var point = S.FaceFrame(item)!.Value.point;
                center -= outward * Vec3.Dot(center - point, outward);
            }
        }
        else
        {
            var half = bounds.Size * .5;
            double distance = Math.Abs(outward.X) * half.X + Math.Abs(outward.Y) * half.Y + Math.Abs(outward.Z) * half.Z;
            center += outward * distance * (_reverse.Value ? -1 : 1);
        }
        var reference = Math.Abs(outward.X) < .9 ? Vec3.UnitX : Vec3.UnitY;
        var x = (reference - outward * Vec3.Dot(reference, outward)).Normalized();
        var y = Vec3.Cross(outward, x).Normalized();
        return (center, x, y, outward);
    }

    (Vec3 origin, Vec3 x, Vec3 direction) Frame(SceneObject body, SelItem item)
    {
        var (center, x, y, outward) = Base(body, item);
        var at = center + x * _u.Value + y * _v.Value;
        if (_position.Index == 1)
        {
            var point = new Vec3(_wx.Value, _wy.Value, _wz.Value);
            at = point - outward * Vec3.Dot(point - center, outward);
        }
        return (at, x, outward * (_reverse.Value ? 1 : -1));
    }

    List<(double u, double v)> Extras()
    {
        var list = new List<(double, double)>();
        foreach (var line in _more.Value.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 2 && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var u) && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) list.Add((u, v));
        }
        return list;
    }

    void SetExtras(List<(double u, double v)> list)
        => _more.Value = string.Join("\n", list.Select(p => $"{p.u.ToString("0.###", CultureInfo.InvariantCulture)}, {p.v.ToString("0.###", CultureInfo.InvariantCulture)}"));

    List<Vec3> Origins(SceneObject body, SelItem item, Vec3 first)
    {
        var list = new List<Vec3> { first };
        var extras = Extras();
        if (extras.Count == 0) return list;
        var (center, x, y, _) = Base(body, item);
        foreach (var (u, v) in extras) list.Add(center + x * u + y * v);
        return list;
    }

    bool TryFrame(out SceneObject body, out SelItem item, out (Vec3 center, Vec3 x, Vec3 y, Vec3 outward) frame)
    {
        body = null!; item = default; frame = default;
        if (_target.Items.Count == 0) return false;
        item = _target.Items[0];
        var b = S.Find(item.Obj);
        if (b == null || b.Tri != null || !b.Shape.HasFaces) return false;
        try { frame = Base(b, item); body = b; return true; }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }

    static (double u, double v) UV(Vec3 p, (Vec3 center, Vec3 x, Vec3 y, Vec3 outward) f)
        => (Math.Round(Vec3.Dot(p - f.center, f.x), 3), Math.Round(Vec3.Dot(p - f.center, f.y), 3));

    /// <summary>The first click picks the face and puts the hole where it landed; further clicks on that face add more holes.</summary>
    public override bool PickPoint(PickResult hit, bool additive)
    {
        if (_target.Items.Count == 0)
        {
            if (!Pick(hit.Item, additive)) return false;
            if (hit.Item.Kind == SelKind.Face && TryFrame(out _, out _, out var f0))
            {
                var (u, v) = UV(hit.Point, f0);
                ConfigureInputs(() => { _position.SetSilently(0); _u.Value = u; _v.Value = v; });
                OnChanged();
            }
            return true;
        }
        if (hit.Item.Kind != SelKind.Face) return false;
        if (!TryFrame(out _, out var item, out var f)) return false;
        var n = hit.Normal.Normalized();
        bool same = hit.Item.Obj == item.Obj && (item.Kind == SelKind.Face ? hit.Item.Index == item.Index : Vec3.Dot(n, f.outward * (_reverse.Value ? -1 : 1)) > .98);
        if (!same)
        {
            ConfigureInputs(() => { _more.Value = ""; _u.Value = 0; _v.Value = 0; });
            _target.Items.Clear();
            return PickPoint(hit, additive);
        }
        var (nu, nv) = UV(hit.Point, f);
        var extras = Extras(); extras.Add((nu, nv)); SetExtras(extras);
        return true;
    }

    public override bool GrabAt(CadSession s, double px, double py, bool ctrl)
    {
        _grab = -1;
        if (!TryFrame(out var body, out var item, out _)) return false;
        List<Vec3> origins;
        try { origins = Origins(body, item, Frame(body, item).origin); } catch (Exception e) when (e is not OutOfMemoryException) { return false; }
        int best = -1; double bestD = 12;
        for (int i = 0; i < origins.Count; i++)
        {
            if (s.Camera.Project(origins[i]) is not { } a) continue;
            double d = Math.Sqrt((a.x - px) * (a.x - px) + (a.y - py) * (a.y - py));
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0)
        {
            // the original body is hidden while the preview shows, so click it directly: another spot on the face adds a hole
            if (Picking.RayFace(s.Camera, [body], px, py) is { } h)
                return PickPoint(new PickResult(new SelItem(body.Id, SelKind.Face, h.face), h.point, h.normal, h.t), false);
            return false;
        }
        if (ctrl && best > 0) { var ex = Extras(); if (best - 1 < ex.Count) { ex.RemoveAt(best - 1); SetExtras(ex); } return true; }
        _grab = best;
        return true;
    }

    public override void GrabMove(CadSession s, double px, double py, bool alt)
    {
        if (_grab < 0 || !TryFrame(out _, out _, out var f)) return;
        var (o, d) = s.Camera.Ray(px, py);
        double dn = Vec3.Dot(d, f.outward);
        if (Math.Abs(dn) < 1e-4) return;
        var p = o + d * (Vec3.Dot(f.center - o, f.outward) / dn);
        var (u, v) = UV(p, f);
        if (!alt) { u = Math.Round(u * 10) / 10; v = Math.Round(v * 10) / 10; }
        if (_grab == 0)
        {
            if (_position.Index == 1)
            {
                var w = f.center + f.x * u + f.y * v;
                ConfigureInputs(() => { _wx.Value = w.X; _wy.Value = w.Y; _wz.Value = w.Z; });
            }
            else ConfigureInputs(() => { _u.Value = u; _v.Value = v; });
            OnChanged();
        }
        else
        {
            var ex = Extras();
            if (_grab - 1 < ex.Count) { ex[_grab - 1] = (u, v); SetExtras(ex); }
        }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (!TryFrame(out var body, out var item, out _)) return;
        try
        {
            var origins = Origins(body, item, Frame(body, item).origin);
            for (int i = 0; i < origins.Count; i++)
            {
                o.AddDot(origins[i], 0xFFFF8A1F, 8);
                if (origins.Count > 1) o.AddLabel(origins[i], (i + 1).ToString(), false, 1);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
    }

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_target.Items.Count == 0) return [];
            try
            {
                var item = _target.Items[0];
                var body = S.Find(item.Obj);
                if (body == null || body.Tri != null || !body.Shape.HasFaces) return [];
                var (origin, x, direction) = Frame(body, item);
                var list = new List<HandleSpec> { new(origin, x, _diameter, 2) };
                if (_extent.Index == 1) list.Add(new HandleSpec(origin, direction, _depth));
                return list;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (!Complete) return null;
        var item = _target.Items[0];
        var body = S.Find(item.Obj) ?? throw new FormatException("The target no longer exists.");
        if (body.Tri != null || body.IsAnnotation || !body.Shape.HasFaces)
            throw new FormatException("Choose an exact body or surface. Convert a mesh to a solid first.");
        bool threaded = _type.Index == 1, solid = body.Shape.HasSolids;
        if (threaded && !solid) throw new FormatException("Threaded holes need a closed solid. Thicken or close this surface first.");
        var (origin0, x, direction) = Frame(body, item);
        var origins = Origins(body, item, origin0);
        var bounds = body.Shape.Props();
        double depth = 0, threadDepth = 0;
        // Use a right-handed placement frame for either drilling direction.
        var localY = Vec3.Cross(direction, x).Normalized();
        double[] matrix = [x.X, localY.X, direction.X, x.Y, localY.Y, direction.Y, x.Z, localY.Z, direction.Z];
        double bore = threaded ? _diameter.Value - 1.082532 * _pitch.Value : _diameter.Value;
        var result = body.Shape; bool owned = false;
        try
        {
            for (int h = 0; h < origins.Count; h++)
            {
                var origin = origins[h];
                double d = _depth.Value;
                if (_extent.Index == 0)
                {
                    d = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vec3((i & 1) == 0 ? bounds.Min.X : bounds.Max.X, (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                        d = Math.Max(d, Vec3.Dot(corner - origin, direction));
                    }
                    d += .01;
                }
                double td = _threadDepth.Value == 0 ? d : _threadDepth.Value;
                if (h == 0) { depth = d; threadDepth = td; }
                if (threaded && (td > d + 1e-6 || td / _pitch.Value > 200 || _diameter.Value <= 1.082532 * _pitch.Value))
                    throw new FormatException("Thread depth must fit inside the hole, with at most 200 turns, and pitch must be smaller than diameter / 1.083.");
                using var cylinder = Occ.Cylinder(bore / 2, d + .01);
                using var placed = Occ.Transform(cylinder, matrix, origin - direction * .01);
                var next = Occ.Cut(result, placed); if (owned) result.Dispose(); result = next; owned = true;
                if (threaded)
                {
                    var key = (_diameter.Value, _pitch.Value, td, _leftHand.Value);
                    if (_grooveKey != key)
                    {
                        var groove = Occ.ThreadGroove(key.Item1, key.Item2, key.Item3, key.Item4);
                        _groove?.Dispose(); _groove = groove; _grooveKey = key;
                    }
                    using var groovePlaced = Occ.Transform(_groove!, matrix, origin);
                    var cut = Occ.Cut(result, groovePlaced); result.Dispose(); result = cut;
                }
            }
            var props = result.Props();
            if (!result.IsValid || !result.HasFaces || (solid && (!result.HasSolids || bounds.Volume - props.Volume < 1e-7 || props.Volume <= 1e-7)) || (!solid && bounds.Area - props.Area < 1e-7))
                throw new FormatException("The hole must intersect the target and leave a valid body. Check its position, direction and dimensions.");
            var output = new CommandOutput { Label = threaded ? $"Threaded hole Ø{_diameter.Value:0.###} × {_pitch.Value:0.###}" : "Hole", Icon = Icon };
            output.Replace.Add((body, result));
            if (origins.Count > 1) Results.Add(new("Holes", origins.Count.ToString()));
            Results.Add(new("Bore diameter", $"{bore:0.###} mm"));
            Results.Add(new("Depth", $"{depth:0.###} mm"));
            if (threaded) Results.Add(new("Thread", $"Ø{_diameter.Value:0.###} × {_pitch.Value:0.###}, {(_leftHand.Value ? "left" : "right")}-hand, {threadDepth:0.###} mm"));
            return output;
        }
        catch { if (owned) result.Dispose(); throw; }
    }

    public override void OnEnd() { _groove?.Dispose(); _groove = null; _grooveKey = null; }
}
