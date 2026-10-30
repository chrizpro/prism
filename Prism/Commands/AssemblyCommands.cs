using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

// =================================================================== Insert a part as a component
public sealed class InsertComponentCommand : Command
{
    readonly ChoiceParam _doc;
    readonly TextParam _file;
    readonly NumberParam _x, _y, _z;
    readonly BoolParam _ground, _link;
    readonly List<(string Title, CadSession Session)> _docs;

    LibraryPart? _library;

    /// <summary>Insert a part chosen in the parts library (a file in the library folder, or a built-in part).</summary>
    public static InsertComponentCommand ForLibrary(LibraryPart part) => new() { _library = part };

    public InsertComponentCommand()
    {
        _docs = CadSession.DocumentProvider?.Invoke().ToList() ?? [];
        string[] names = _docs.Count == 0 ? ["(no other documents)"] : [.. _docs.Select(d => d.Title)];
        _doc = Choice("Part from an open document", 0, names);
        _file = Text("…or from a part file", "", "full path of a .prism file");
        _x = Num("Place at X", 0); _y = Num("Place at Y", 0); _z = Num("Place at Z", 0);
        _ground = Check("Ground it (fix it in place)", false);
        _link = Check("Keep linked to the file (Refresh updates it)", true);
    }

    public override string Title => "Insert Component";
    public override string Icon => "component";
    public override string OkText => "Insert";
    public override string Hint => "Bottom-up assembly: model each part in its own document, then insert it here. Choose an open document or a part file. The first component is grounded so the rest can be joined to it.";
    protected override void OnBegin()
    {
        _ground.Value = !S.Components.Any();
        if (_library != null) _doc.IsVisible = _file.IsVisible = _link.IsVisible = false;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        CadSession src; string name; string? path = null;
        if (_library != null)
        {
            var (libShape, libColor, libLook) = PartsLibrary.Load(_library);
            string? linked = _library.Path != null && _link.Value && Path.GetExtension(_library.Path).ToLowerInvariant() is ".prism" or ".ourcad" ? _library.Path : null;
            var libPlace = Xform.Translation(new Vec3(_x.Value, _y.Value, _z.Value));
            var libInfo = new ComponentInfo(CadSession.NewKey(), _library.Name, linked, libPlace.ToArray(), _ground.Value);
            Results.Add(new ResultLine("Part", _library.Name));
            Results.Add(new ResultLine("From", _library.Source));
            var libOut = new CommandOutput { Label = $"Insert {_library.Name}", Icon = "component" };
            libOut.Add.Add(new(libPlace.Apply(libShape), _library.Name, ObjKind.Solid, libColor, Look: libLook, Asm: libInfo));
            return libOut;
        }
        if (!string.IsNullOrWhiteSpace(_file.Value))
        {
            if (!File.Exists(_file.Value)) throw new FormatException($"Cannot find {_file.Value}.");
            src = new CadSession(); src.Open(_file.Value); name = Path.GetFileNameWithoutExtension(_file.Value); path = _file.Value;
        }
        else
        {
            if (_docs.Count == 0) throw new FormatException("There are no other open documents. Open or create the part in another tab first, or type a file path.");
            var d = _docs[Math.Clamp(_doc.Index, 0, _docs.Count - 1)];
            if (ReferenceEquals(d.Session, S)) throw new FormatException("That is this assembly itself. Pick a different document.");
            src = d.Session; name = d.Title;
            if (src.FilePath != null && _link.Value) path = src.FilePath;
        }
        if (!_link.Value) path = null;
        var part = CadSession.PartGeometry(src) ?? throw new FormatException($"{name} has no solid bodies to insert.");
        var place = Xform.Translation(new Vec3(_x.Value, _y.Value, _z.Value));
        var world = place.Apply(part.shape);
        var info = new ComponentInfo(CadSession.NewKey(), name, path, place.ToArray(), _ground.Value);
        Results.Add(new ResultLine("Part", name));
        Results.Add(new ResultLine("Link", path == null ? "embedded copy" : Path.GetFileName(path)));
        var o = new CommandOutput { Label = $"Insert {name}", Icon = "component" };
        o.Add.Add(new(world, name, ObjKind.Solid, part.color, Look: part.look, Asm: info));
        return o;
    }
}

// =================================================================== Joints
public sealed class JointCommand : Command
{
    readonly JointType _type;
    readonly SelectionSlot _a, _b;
    readonly ChoiceParam _flip;
    readonly BoolParam _limits;
    readonly NumberParam _min, _max, _min2, _max2;

    public JointCommand(JointType type)
    {
        _type = type;
        _a = Slot(new SelectionSlot { Label = "On the first part (face, circle edge or round face)", Filter = SelFilter.Face | SelFilter.Edge });
        _b = Slot(new SelectionSlot { Label = "On the part that moves", Filter = SelFilter.Face | SelFilter.Edge });
        _flip = Choice("Direction", 0, "Automatic", "Flip", "Do not flip");
        _limits = Check("Limit the motion", false);
        bool ang = type is JointType.Revolute or JointType.Cylindrical or JointType.Ball;
        _min = Num(ang ? "Minimum angle" : "Minimum travel", ang ? -90 : 0, -1e6, 1e6, 5, ang ? "°" : "mm");
        _max = Num(ang ? "Maximum angle" : "Maximum travel", ang ? 90 : 50, -1e6, 1e6, 5, ang ? "°" : "mm");
        bool ang2 = type == JointType.Ball;
        _min2 = Num(ang2 ? "Minimum tilt" : "Minimum travel", ang2 ? -45 : 0, -1e6, 1e6, 5, ang2 ? "°" : "mm"); _max2 = Num(ang2 ? "Maximum tilt" : "Maximum travel", ang2 ? 45 : 50, -1e6, 1e6, 5, ang2 ? "°" : "mm");
        UpdateVisibility();
    }

    public override string Title => _type switch { JointType.Rigid => "Rigid Joint", JointType.Revolute => "Revolute Joint", JointType.Slider => "Slider Joint", JointType.Ball => "Ball Joint", JointType.Planar => "Planar Joint", _ => "Cylindrical Joint" };
    public override string Icon => _type switch { JointType.Rigid => "joint-rigid", JointType.Revolute => "joint-revolute", JointType.Slider => "joint-slider", JointType.Ball => "joint-ball", JointType.Planar => "joint-planar", _ => "joint-cyl" };
    public override string OkText => "Create joint";
    public override string Hint => _type switch
    {
        JointType.Rigid => "Pick a face or circle on each component. The second component snaps so the two points coincide and then stays locked to the first.",
        JointType.Revolute => "Pick a circle edge or round face (a pin and its hole) on each component. The second turns about that axis.",
        JointType.Slider => "Pick a face on each component. The second slides along the direction of the picked face's normal (or a round feature's axis).",
        JointType.Ball => "Pick a point (a circle centre or a face) on each component. The second pivots about that point: spin about the picked axis and tilt away from it.",
        JointType.Planar => "Pick a flat face on each component. The second slides over the first in two directions, staying flat against it.",
        _ => "Pick a round feature on each component. The second turns about the axis and slides along it.",
    };

    protected override void UpdateVisibility()
    {
        if (_min == null) return;
        _min.IsVisible = _max.IsVisible = _limits.Value && _type != JointType.Rigid;
        _min2.IsVisible = _max2.IsVisible = _limits.Value && _type is JointType.Cylindrical or JointType.Ball or JointType.Planar;
    }

    // what Build worked out, shown in the viewport
    Xform? _frameA, _frameB;

    SceneObject Owner(SelectionSlot slot)
    {
        var o = S.Find(slot.Items[0].Obj);
        if (o?.Component == null) throw new FormatException("The pick must be on a component. Insert the parts first (Assemble tab > Insert).");
        return o;
    }

    static Xform WorldFrame((Vec3 origin, Vec3 z) f) => Xform.FromFrame(f.origin, Xform.AnyPerpendicular(f.z), f.z);

    public JointInfo? Plan(out SceneObject? parent, out SceneObject? child)
    {
        parent = child = null;
        if (_a.Items.Count == 0 || _b.Items.Count == 0) return null;
        parent = Owner(_a); child = Owner(_b);
        if (parent.Component!.Key == child.Component!.Key) throw new FormatException("Pick the two faces on two different components.");
        var fa = S.FrameOf(_a.Items[0]) ?? throw new FormatException("The first pick has no usable point or axis. Use a flat face, a circular edge or a round face.");
        var fb = S.FrameOf(_b.Items[0]) ?? throw new FormatException("The second pick has no usable point or axis. Use a flat face, a circular edge or a round face.");
        var Aw = WorldFrame(fa); var Bw = WorldFrame(fb);
        bool flip = _flip.Index switch { 1 => true, 2 => false, _ => Vec3.Dot(Aw.AxisZ, Bw.AxisZ) < 0 };
        // where the child frame sits relative to the parent frame right now: used to keep the part's turn about the axis
        var rel = Aw.Inverse() * Bw;
        var m = flip ? rel * Xform.RotX180 : rel;
        double zero = m.AngleAboutZ() * 180 / Math.PI;
        _frameA = Aw; _frameB = Bw;
        var pa = parent.Component.Placement.Inverse() * Aw; var pb = child.Component.Placement.Inverse() * Bw;
        string name = $"{_type} {S.JointObjects.Count(j => j.joint.Type == _type) + 1}";
        return new JointInfo(CadSession.NewKey(), name, _type, parent.Component.Key, child.Component.Key, JointInfo.Pack(pa), JointInfo.Pack(pb), flip,
            zero, 0, 0, _limits.Value && _type != JointType.Rigid,
            _min.Value, _max.Value, _min2.Value, _max2.Value);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _frameA = _frameB = null;
        var j = Plan(out var parent, out var child);
        if (j == null) return null;
        var placements = S.SolvePlacements(null, out _, [j]);
        var o = new CommandOutput { Label = j.Name, Icon = Icon, ReplaceIsMove = true };
        // preview the snap, then let the real solver set the final positions
        foreach (var comp in S.Components)
        {
            var np = placements[comp.Component!.Key];
            if (np.DistanceTo(comp.Component.Placement) > 1e-9) o.Replace.Add((comp, np.Apply(S.LocalShapeOf(comp))));
        }
        var marker = Occ.Line(_frameA!.Value.T, _frameA.Value.T + _frameA.Value.AxisZ * 0.01);
        o.Add.Add(new(marker, j.Type.ToString(), ObjKind.Joint, Asm: j));
        o.After = s => s.SolveAssembly();
        Results.Add(new ResultLine("Between", $"{parent!.Name}  →  {child!.Name}"));
        Results.Add(new ResultLine("Direction", j.Flip ? "flipped (faces meet)" : "same direction"));
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_frameA is { } a) Triad(o, a, 12, 0xFFFFC53D);
        if (_frameB is { } b) Triad(o, b, 9, 0xFF35B6FF);
    }

    public static void Triad(Overlay o, Xform f, double len, uint z)
    {
        o.AddLine(f.T, f.T + f.AxisX * len, 0xFFE5484D, 2.5f);
        o.AddLine(f.T, f.T + f.AxisY * len, 0xFF3FCF8E, 2.5f);
        o.AddLine(f.T, f.T + f.AxisZ * len * 1.4, z, 3.5f);
        o.AddDot(f.T, z, 8);
    }
}

// =================================================================== Drive joints (test the motion, set values)
public sealed class DriveJointsCommand : Command
{
    readonly List<(JointInfo joint, NumberParam v1, NumberParam? v2)> _rows = [];

    public override string Title => "Drive Joints";
    public override string Icon => "joint-revolute";
    public override string OkText => "Keep positions";
    public override string Hint => "Move the joints with their values and watch the assembly follow. Limits are respected. Keep positions stores these values; Cancel puts everything back.";

    protected override void OnBegin()
    {
        foreach (var (obj, j) in S.JointObjects)
        {
            if (j.Type == JointType.Rigid) continue;
            double lo = j.HasLimits ? j.Min : (j.UsesAngle ? -360 : -500), hi = j.HasLimits ? j.Max : (j.UsesAngle ? 360 : 500);
            var p1 = Add(new NumberParam { Label = $"{j.Name}  ({j.FirstLabel})", Value = Math.Clamp(j.Value, lo, hi), Min = lo, Max = hi, Step = j.UsesAngle ? 5 : 1, Unit = j.UsesAngle ? "°" : "mm" });
            NumberParam? p2 = null;
            if (j.HasSecond)
            {
                double lo2 = j.HasLimits ? j.Min2 : (j.SecondIsAngle ? -180 : -500), hi2 = j.HasLimits ? j.Max2 : (j.SecondIsAngle ? 180 : 500);
                p2 = Add(new NumberParam { Label = $"{j.Name}  ({j.SecondLabel})", Value = Math.Clamp(j.Value2, lo2, hi2), Min = lo2, Max = hi2, Step = j.SecondIsAngle ? 5 : 1, Unit = j.SecondIsAngle ? "°" : "mm" });
            }
            _rows.Add((j, p1, p2));
        }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_rows.Count == 0) { Results.Add(new ResultLine("Joints", "none yet. Create joints first (Joints tab).")); return null; }
        var over = _rows.ToDictionary(r => r.joint.Key, r => r.joint with { Value = r.v1.Value, Value2 = r.v2?.Value ?? r.joint.Value2 });
        var placements = S.SolvePlacements(over, out var notes);
        var o = new CommandOutput { Label = "Drive joints", Icon = "joint-revolute", ReplaceIsMove = true };
        foreach (var comp in S.Components)
        {
            var p = placements[comp.Component!.Key];
            if (p.DistanceTo(comp.Component.Placement) > 1e-9) o.Replace.Add((comp, p.Apply(S.LocalShapeOf(comp))));
        }
        var values = _rows.ToDictionary(r => r.joint.Key, r => (r.v1.Value, r.v2?.Value ?? r.joint.Value2));
        o.After = s => s.SetJointValues(values);
        Results.Add(new ResultLine("Moved parts", o.Replace.Count.ToString()));
        foreach (var n in notes.Distinct()) Results.Add(new ResultLine("Note", n, 1));
        return o;
    }
}

// =================================================================== Move a free component
public sealed class MoveComponentCommand : Command
{
    readonly SelectionSlot _comp;
    readonly NumberParam _dx, _dy, _dz, _rx, _ry, _rz;

    public MoveComponentCommand()
    {
        _comp = Slot(new SelectionSlot { Label = "Component", Filter = SelFilter.Solid });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _rx = Num("Turn about X", 0, -360, 360, 5, "°"); _ry = Num("Turn about Y", 0, -360, 360, 5, "°"); _rz = Num("Turn about Z", 0, -360, 360, 5, "°");
    }

    public override string Title => "Move Component";
    public override string Icon => "move";
    public override string Hint => "Position a free component: move it, or turn it about its own centre. A component held by a joint moves with the joint instead (use Drive Joints).";

    static Xform Rot(Vec3 axis, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a); var k = axis.Normalized();
        double x = k.X, y = k.Y, z = k.Z, t = 1 - c;
        return new([t * x * x + c, t * x * y - s * z, t * x * z + s * y, t * x * y + s * z, t * y * y + c, t * y * z - s * x, t * x * z - s * y, t * y * z + s * x, t * z * z + c], Vec3.Zero);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_comp.Items.Count == 0) return null;
        var obj = S.Find(_comp.Items[0].Obj);
        if (obj?.Component is not { } ci) throw new FormatException("Pick a component (a part inserted into this assembly).");
        if (S.JointObjects.Any(j => j.joint.ChildKey == ci.Key || j.joint.ParentKey == ci.Key) && !ci.Grounded)
            throw new FormatException("This component is held by a joint. Drive the joint to move it, or delete the joint.");
        if (ci.Grounded) throw new FormatException("This component is grounded (fixed). Unground it first.");
        var c = ci.Placement.Apply(S.LocalShapeOf(obj) is { } l ? l.Tessellate() is { } m ? (m.Min + m.Max) * 0.5 : Vec3.Zero : Vec3.Zero);
        var rot = Rot(Vec3.UnitZ, _rz.Value) * Rot(Vec3.UnitY, _ry.Value) * Rot(Vec3.UnitX, _rx.Value);
        // turn about the component's centre, then move
        var about = Xform.Translation(c) * rot * Xform.Translation(-c);
        var np = Xform.Translation(new Vec3(_dx.Value, _dy.Value, _dz.Value)) * about * ci.Placement;
        var o = new CommandOutput { Label = $"Move {obj.Name}", Icon = "move", ReplaceIsMove = true };
        o.Replace.Add((obj, np.Apply(S.LocalShapeOf(obj))));
        o.After = s => s.Update(obj.Id, x => x.WithAsm(ci.WithPlacement(np), np.Apply(s.LocalShapeOf(x))), false);
        return o;
    }
}

// =================================================================== Pattern a component
/// <summary>Copies of a component in a line or around an axis. Each copy is its own free component of the same part.</summary>
public sealed class ComponentPatternCommand : Command
{
    readonly SelectionSlot _comp;
    readonly ChoiceParam _kind, _axis;
    readonly NumberParam _count, _dx, _dy, _dz, _angle, _cx, _cy, _cz;

    public ComponentPatternCommand()
    {
        _comp = Slot(new SelectionSlot { Label = "Component to repeat", Filter = SelFilter.Solid });
        _kind = Choice("Pattern", 0, "Linear", "Circular");
        _count = Num("Number of copies", 3, 1, 500, 1, "");
        _dx = Num("Spacing X", 20); _dy = Num("Spacing Y", 0); _dz = Num("Spacing Z", 0);
        _axis = Choice("Turn about", 2, "X axis", "Y axis", "Z axis");
        _angle = Num("Angle between copies", 90, -360, 360, 5, "°");
        _cx = Num("Axis passes through X", 0); _cy = Num("Axis passes through Y", 0); _cz = Num("Axis passes through Z", 0);
        UpdateVisibility();
    }

    public override string Title => "Component Pattern";
    public override string Icon => "pattern-linear";
    public override string OkText => "Create copies";
    public override string Hint => "Pick a component and repeat it in a line (spacing in X, Y, Z) or around an axis. Each copy is a free component of the same part, so it can be jointed or animated on its own.";

    protected override void UpdateVisibility()
    {
        if (_kind == null) return;
        bool lin = _kind.Index == 0;
        _dx.IsVisible = _dy.IsVisible = _dz.IsVisible = lin;
        _axis.IsVisible = _angle.IsVisible = _cx.IsVisible = _cy.IsVisible = _cz.IsVisible = !lin;
    }

    static Xform Turn(Vec3 axis, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a); var k = axis.Normalized();
        double x = k.X, y = k.Y, z = k.Z, t = 1 - c;
        return new([t * x * x + c, t * x * y - s * z, t * x * z + s * y, t * x * y + s * z, t * y * y + c, t * y * z - s * x, t * x * z - s * y, t * y * z + s * x, t * z * z + c], Vec3.Zero);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_comp.Items.Count == 0) return null;
        var obj = S.Find(_comp.Items[0].Obj);
        if (obj?.Component is not { } ci) throw new FormatException("Pick a component (a part inserted into this assembly).");
        int n = (int)Math.Round(_count.Value);
        var o = new CommandOutput { Label = $"Pattern {obj.Name}", Icon = "pattern-linear" };
        var axis = _axis.Index switch { 0 => Vec3.UnitX, 1 => Vec3.UnitY, _ => Vec3.UnitZ };
        var through = new Vec3(_cx.Value, _cy.Value, _cz.Value);
        for (int i = 1; i <= n; i++)
        {
            Xform move = _kind.Index == 0
                ? Xform.Translation(new Vec3(_dx.Value, _dy.Value, _dz.Value) * i)
                : Xform.Translation(through) * Turn(axis, _angle.Value * i) * Xform.Translation(-through);
            var placed = move * ci.Placement;
            var info = new ComponentInfo(CadSession.NewKey(), ci.PartName, ci.SourcePath, placed.ToArray(), false);
            o.Add.Add(new(move.Apply(obj.Shape), obj.Name, ObjKind.Solid, obj.Color, Look: obj.Look, Asm: info));
        }
        Results.Add(new ResultLine("Copies", n.ToString()));
        return o;
    }
}

// =================================================================== Mate two faces
/// <summary>Snap one free component onto another by two faces: face to face (touching), flush (same direction) or at a distance.</summary>
public sealed class MateCommand : Command
{
    readonly SelectionSlot _a, _b;
    readonly ChoiceParam _kind;
    readonly NumberParam _offset;

    public MateCommand()
    {
        _a = Slot(new SelectionSlot { Label = "Face on the part that stays", Filter = SelFilter.Face });
        _b = Slot(new SelectionSlot { Label = "Face on the part that moves", Filter = SelFilter.Face });
        _kind = Choice("Mate", 0, "Face to face (touching)", "Flush (same direction)");
        _offset = Num("Distance between the faces", 0);
    }

    public override string Title => "Mate";
    public override string Icon => "joint-rigid";
    public override string OkText => "Mate";
    public override string Hint => "Pick a flat or round face on each of two components. The second component turns and slides so the faces meet, or sit a distance apart. It is a one-time positioning move: use a joint to keep parts connected.";

    static Xform Turn(Vec3 axis, double rad)
    {
        double c = Math.Cos(rad), s = Math.Sin(rad); var k = axis.Normalized();
        double x = k.X, y = k.Y, z = k.Z, t = 1 - c;
        return new([t * x * x + c, t * x * y - s * z, t * x * z + s * y, t * x * y + s * z, t * y * y + c, t * y * z - s * x, t * x * z - s * y, t * y * z + s * x, t * z * z + c], Vec3.Zero);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_a.Items.Count == 0 || _b.Items.Count == 0) return null;
        var objA = S.Find(_a.Items[0].Obj); var objB = S.Find(_b.Items[0].Obj);
        if (objA?.Component == null || objB?.Component is not { } ci) throw new FormatException("Pick a face on each of two components (parts inserted into this assembly).");
        if (objA.Id == objB.Id) throw new FormatException("Pick the two faces on two different components.");
        if (ci.Grounded) throw new FormatException("The part that moves is grounded (fixed). Unground it, or swap the two picks.");
        if (S.JointObjects.Any(j => j.joint.ChildKey == ci.Key || j.joint.ParentKey == ci.Key))
            throw new FormatException("The part that moves is held by a joint. Drive the joint instead, or delete it.");
        var fa = S.FrameOf(_a.Items[0]) ?? throw new FormatException("The first pick has no usable point or direction.");
        var fb = S.FrameOf(_b.Items[0]) ?? throw new FormatException("The second pick has no usable point or direction.");
        var na = fa.z.Normalized(); var nb = fb.z.Normalized();
        var target = _kind.Index == 0 ? -na : na;                       // the direction the moving face ends up facing
        double dot = Math.Clamp(Vec3.Dot(nb, target), -1, 1);
        var axis = Vec3.Cross(nb, target);
        Xform turn;
        if (axis.Length > 1e-9) turn = Turn(axis, Math.Acos(dot));
        else if (dot > 0) turn = Xform.Identity;
        else turn = Turn(Xform.AnyPerpendicular(nb), Math.PI);
        // turn about the picked point on the moving part, then bring that point to the other face (plus the gap)
        var about = Xform.Translation(fb.origin) * turn * Xform.Translation(-fb.origin);
        var landed = about.Apply(fb.origin);
        var gap = na * _offset.Value;
        var np = Xform.Translation(fa.origin + gap - landed) * about * ci.Placement;
        var o = new CommandOutput { Label = $"Mate {objB.Name}", Icon = "joint-rigid", ReplaceIsMove = true };
        o.Replace.Add((objB, np.Apply(S.LocalShapeOf(objB))));
        o.After = s => s.Update(objB.Id, x => x.WithAsm(ci.WithPlacement(np), np.Apply(s.LocalShapeOf(x))), false);
        Results.Add(new ResultLine("Moves", objB.Name));
        return o;
    }
}

// =================================================================== Exploded view
public sealed class ExplodeCommand : Command
{
    readonly NumberParam _f;
    double _before;

    public ExplodeCommand() { _f = Num("Explode", 0, 0, 1, 0.05, ""); _f.Value = 0.6; }
    public override string Title => "Exploded View";
    public override string Icon => "explode";
    public override string OkText => "Done";
    public override string Hint => "Pull the parts apart radially to see how they fit together. The assembly keeps its real positions; set Explode back to 0 (or press Done with 0) to close it.";

    protected override void OnBegin() { _before = S.ExplodeFactor; _f.Value = S.ExplodeFactor > 1e-9 ? S.ExplodeFactor : 0.6; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        S.SetExplode(_f.Value);
        Results.Add(new ResultLine("Parts", S.Components.Count().ToString()));
        return new CommandOutput { NoOp = true };
    }

    public override void OnCancel() => S.SetExplode(_before);
}
