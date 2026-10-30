using System;
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
    public override string Hint => "Select a body or an entry face. The hole starts at the face center (body selections use the top center). Position it with U/V offsets. Choose a plain hole or a modeled 60° thread; Diameter is the thread's nominal major diameter. Closed forms become exact bodies. Meshes must first be converted with Mesh to Solid.";

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

    (Vec3 origin, Vec3 x, Vec3 direction) Frame(SceneObject body, SelItem item)
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
        var at = center + x * _u.Value + y * _v.Value;
        if (_position.Index == 1)
        {
            var point = new Vec3(_wx.Value, _wy.Value, _wz.Value);
            at = point - outward * Vec3.Dot(point - center, outward);
        }
        return (at, x, outward * (_reverse.Value ? 1 : -1));
    }

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
        var (origin, x, direction) = Frame(body, item);
        var bounds = body.Shape.Props();
        double depth = _depth.Value;
        if (_extent.Index == 0)
        {
            depth = 0;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vec3((i & 1) == 0 ? bounds.Min.X : bounds.Max.X, (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                depth = Math.Max(depth, Vec3.Dot(corner - origin, direction));
            }
            depth += .01;
        }
        double threadDepth = _threadDepth.Value == 0 ? depth : _threadDepth.Value;
        if (threaded && (threadDepth > depth + 1e-6 || threadDepth / _pitch.Value > 200 || _diameter.Value <= 1.082532 * _pitch.Value))
            throw new FormatException("Thread depth must fit inside the hole, with at most 200 turns, and pitch must be smaller than diameter / 1.083.");
        // Use a right-handed placement frame for either drilling direction.
        var localY = Vec3.Cross(direction, x).Normalized();
        double[] matrix = [x.X, localY.X, direction.X, x.Y, localY.Y, direction.Y, x.Z, localY.Z, direction.Z];
        double bore = threaded ? _diameter.Value - 1.082532 * _pitch.Value : _diameter.Value;
        using var cylinder = Occ.Cylinder(bore / 2, depth + .01);
        using var placed = Occ.Transform(cylinder, matrix, origin - direction * .01);
        var result = Occ.Cut(body.Shape, placed);
        try
        {
            if (threaded)
            {
                var key = (_diameter.Value, _pitch.Value, threadDepth, _leftHand.Value);
                if (_grooveKey != key)
                {
                    var groove = Occ.ThreadGroove(key.Item1, key.Item2, key.Item3, key.Item4);
                    _groove?.Dispose(); _groove = groove; _grooveKey = key;
                }
                using var groovePlaced = Occ.Transform(_groove!, matrix, origin);
                var cut = Occ.Cut(result, groovePlaced); result.Dispose(); result = cut;
            }
            var props = result.Props();
            if (!result.IsValid || !result.HasFaces || (solid && (!result.HasSolids || bounds.Volume - props.Volume < 1e-7 || props.Volume <= 1e-7)) || (!solid && bounds.Area - props.Area < 1e-7))
                throw new FormatException("The hole must intersect the target and leave a valid body. Check its position, direction and dimensions.");
            var output = new CommandOutput { Label = threaded ? $"Threaded hole Ø{_diameter.Value:0.###} × {_pitch.Value:0.###}" : "Hole", Icon = Icon };
            output.Replace.Add((body, result));
            Results.Add(new("Bore diameter", $"{bore:0.###} mm"));
            Results.Add(new("Depth", $"{depth:0.###} mm"));
            if (threaded) Results.Add(new("Thread", $"Ø{_diameter.Value:0.###} × {_pitch.Value:0.###}, {(_leftHand.Value ? "left" : "right")}-hand, {threadDepth:0.###} mm"));
            return output;
        }
        catch { result.Dispose(); throw; }
    }

    public override void OnEnd() { _groove?.Dispose(); _groove = null; _grooveKey = null; }
}
