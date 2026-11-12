using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Meshing;

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
    CadSession? _fromDoc;
    string _fromName = "";

    /// <summary>Insert the part that is open in another tab (used when you finish modeling a part from inside the assembly).</summary>
    public static InsertComponentCommand ForDocument(CadSession doc, string name) => new() { _fromDoc = doc, _fromName = name };

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
    public override GizmoSpec? Gizmo => GizmoSpec.At(new Vec3(_x.Value, _y.Value, _z.Value), _x, _y, _z);
    public override string Hint => "Bottom-up assembly: model each part in its own document, then insert it here. Choose an open document or a part file. The first component is grounded so the rest can be joined to it.";
    protected override void OnBegin()
    {
        _ground.Value = !S.Components.Any();
        if (_library != null || _fromDoc != null) _doc.IsVisible = _file.IsVisible = _link.IsVisible = false;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        CadSession src; string name; string? path = null; Guid? fromDoc = null;
        if (_library != null)
        {
            var (libShape, libColor, libLook) = PartsLibrary.Load(_library);
            string? linked = _library.Path != null && _link.Value && NativeDocumentFile.IsNative(_library.Path) ? _library.Path : null;
            var libPlace = Xform.Translation(new Vec3(_x.Value, _y.Value, _z.Value));
            var libInfo = new ComponentInfo(CadSession.NewKey(), _library.Name, linked, libPlace.ToArray(), _ground.Value, _library.Info?.WithoutThumbnail());
            Results.Add(new ResultLine("Part", _library.Name));
            Results.Add(new ResultLine("From", _library.Source));
            var libOut = new CommandOutput { Label = $"Insert {_library.Name}", Icon = "component" };
            libOut.Add.Add(new(libPlace.Apply(libShape), _library.Name, ObjKind.Solid, libColor, Look: libLook, Asm: libInfo));
            return libOut;
        }
        if (_fromDoc != null)
        {
            src = _fromDoc; name = _fromName; fromDoc = _fromDoc.DocId; path = _fromDoc.FilePath;
        }
        else if (!string.IsNullOrWhiteSpace(_file.Value))
        {
            if (!File.Exists(_file.Value)) throw new FormatException($"Cannot find {_file.Value}.");
            src = new CadSession(); src.Open(_file.Value); name = Path.GetFileNameWithoutExtension(_file.Value); path = _file.Value;
        }
        else
        {
            if (_docs.Count == 0) throw new FormatException("There are no other open documents. Open or create the part in another tab first, or type a file path.");
            var d = _docs[Math.Clamp(_doc.Index, 0, _docs.Count - 1)];
            if (ReferenceEquals(d.Session, S)) throw new FormatException("That is this assembly itself. Pick a different document.");
            src = d.Session; name = d.Title; fromDoc = d.Session.DocId;
            if (src.FilePath != null && _link.Value) path = src.FilePath;
        }
        if (!_link.Value) path = null;
        var part = CadSession.PartGeometry(src) ?? throw new FormatException($"{name} has no solid bodies to insert.");
        var place = Xform.Translation(new Vec3(_x.Value, _y.Value, _z.Value));
        var world = place.Apply(part.shape);
        var info = new ComponentInfo(CadSession.NewKey(), name, path, place.ToArray(), _ground.Value, src.PartInfo.WithoutThumbnail()) { SourceDoc = fromDoc };
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
    readonly ChoiceParam _placement;
    readonly ChoiceParam _flip;
    readonly BoolParam _limits;
    readonly NumberParam _min, _max, _min2, _max2;

    public JointCommand(JointType type)
    {
        _type = type;
        _a = Slot(new SelectionSlot { Label = "On the first part (face, circle edge or round face)", Caption = "First part (reference)", Filter = SelFilter.Face | SelFilter.Edge });
        _b = Slot(new SelectionSlot { Label = "On the part that moves", Caption = "Second part", Filter = SelFilter.Face | SelFilter.Edge });
        _flip = Choice("Direction", 0, "Automatic", "Flip", "Do not flip");
        // Snap moves the second part onto the first pick; Keep leaves both parts where they stand (the axis still comes from the first pick),
        // so a part never jumps inside or away from the other one when the picks are not the mating faces
        _placement = Choice("Placement", 0, "Snap to picks", type == JointType.Rigid ? "As Positioned" : "Keep where it is");
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
        JointType.Rigid when AsPositioned => "Pick two parts to lock their current relative positions.",
        JointType.Rigid => "Pick a face or circle on each part to snap them together. Choose As Positioned to lock two parts where they already sit.",
        _ when AsPositioned => "Pick the axis on the first part (a circle edge, round face or flat face), then anything on the part that moves. Both parts stay where they are.",
        JointType.Revolute => "Pick a circle edge or round face (a pin and its hole) on each part (a plain body becomes a part when you pick it). The second turns about that axis.",
        JointType.Slider => "Pick a face on each part (a plain body becomes a part when you pick it). The second slides along the direction of the picked face's normal (or a round feature's axis).",
        JointType.Ball => "Pick a point (a circle centre or a face) on each part (a plain body becomes a part when you pick it). The second pivots about that point: spin about the picked axis and tilt away from it.",
        JointType.Planar => "Pick a flat face on each part (a plain body becomes a part when you pick it). The second slides over the first in two directions, staying flat against it.",
        _ => "Pick a round feature on each part (a plain body becomes a part when you pick it). The second turns about the axis and slides along it.",
    };

    protected override void UpdateVisibility()
    {
        if (_min == null) return;
        _flip.IsVisible = !AsPositioned;
        bool rigidInPlace = AsPositioned && _type == JointType.Rigid;
        _a.Filter = rigidInPlace ? SelFilter.Solid : SelFilter.Face | SelFilter.Edge;
        _b.Filter = rigidInPlace ? SelFilter.Solid : AsPositioned ? SelFilter.Face | SelFilter.Edge | SelFilter.Solid : SelFilter.Face | SelFilter.Edge;
        _min.IsVisible = _max.IsVisible = _limits.Value && _type != JointType.Rigid;
        _min2.IsVisible = _max2.IsVisible = _limits.Value && _type is JointType.Cylindrical or JointType.Ball or JointType.Planar;
    }

    // what Build worked out, shown in the viewport
    bool AsPositioned => _placement.Index == 1;
    Xform? _frameA, _frameB, _axisFrame;
    (Vec3 at, string label)? _labelA, _labelB;

    readonly List<Guid> _madeParts = [];

    /// <summary>The part a pick is on. A plain solid body in this file becomes a part on the spot, so a mechanism can be modeled and jointed in one file.</summary>
    SceneObject Owner(SelectionSlot slot)
    {
        var o = S.Find(slot.Items[0].Obj);
        if (o?.Component != null) return o;
        if (o?.Kind != ObjKind.Solid) throw new FormatException("Pick a solid body or an assembly component.");
        _madeParts.Add(o.Id);
        return S.MakePart(o.Id);
    }

    public override void OnCancel()
    {
        foreach (var id in _madeParts) S.Update(id, v => v.WithAsm(null, v.Shape), false);
    }

    static Xform WorldFrame((Vec3 origin, Vec3 z) f) => Xform.FromFrame(f.origin, Xform.AnyPerpendicular(f.z), f.z);

    public JointInfo? Plan(out SceneObject? parent, out SceneObject? child)
    {
        parent = child = null;
        if (_a.Items.Count == 0 || _b.Items.Count == 0) return null;
        parent = Owner(_a); child = Owner(_b);
        if (parent.Component!.Key == child.Component!.Key) throw new FormatException("Pick two different components.");
        Xform Frame(SelItem pick, string which)
        {
            if (pick.Kind == SelKind.Object || S.FrameOf(pick) is not { } frame)
                throw new FormatException($"Pick a flat face, a circular edge or a round face on the {which} part.");
            return WorldFrame(frame);
        }
        // Rigid in place: the parent's own frame. Any other joint in place: the axis from the first pick, and the child keeps its position (its frame is the same world frame).
        var Aw = AsPositioned && _type == JointType.Rigid ? parent.Component.Placement : Frame(_a.Items[0], "first");
        var Bw = AsPositioned ? Aw : Frame(_b.Items[0], "second");
        bool flip = !AsPositioned && (_flip.Index switch { 1 => true, 2 => false, _ => Vec3.Dot(Aw.AxisZ, Bw.AxisZ) < 0 });
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
        Results.Clear(); _frameA = _frameB = _axisFrame = null; _labelA = _labelB = null;
        var j = Plan(out var parent, out var child);
        if (j == null) return null;
        var values = S.CloseLoops(new Dictionary<string, JointInfo>(), null, default, out var problem, added: j);
        if (problem != null) throw new InvalidOperationException(problem);
        var over = values == null ? null : S.JointObjects.Where(p => values.ContainsKey(p.joint.Key))
            .ToDictionary(p => p.joint.Key, p => p.joint with { Value = values[p.joint.Key].v1, Value2 = values[p.joint.Key].v2 });
        if (values != null && values.TryGetValue(j.Key, out var v)) j = j with { Value = v.v1, Value2 = v.v2 };
        var placements = S.SolvePlacements(over, out var notes, [j]);
        var o = new CommandOutput { Label = j.Name, Icon = Icon, ReplaceIsMove = true };
        var moved = new List<Guid>();
        // preview the snap, then let the real solver set the final positions
        foreach (var comp in S.Components)
        {
            var np = placements[comp.Component!.Key];
            if (np.DistanceTo(comp.Component.Placement) <= 1e-9) continue;
            o.Replace.Add((comp, np.Apply(S.LocalShapeOf(comp))));
            moved.Add(comp.Id);
        }
        var marker = Occ.Line(_frameA!.Value.T, _frameA.Value.T + _frameA.Value.AxisZ * 0.01);
        o.Add.Add(new(marker, j.Type.ToString(), ObjKind.Joint, Asm: j));
        o.After = s => { if (values == null) s.SolveAssembly(); else s.SetJointValues(values); };
        (Vec3 at, string label) ShowRole(SceneObject part, int number, string role)
        {
            string action = moved.Contains(part.Id) ? "moves" : "stays in place";
            Results.Add(new ResultLine(role, $"{part.Name} · {action}"));
            return ((part.Mesh.Min + part.Mesh.Max) * 0.5, $"{number} · {part.Name} · {action}");
        }
        _labelA = ShowRole(parent!, 1, "First part");
        _labelB = ShowRole(child!, 2, "Second part");
        if (AsPositioned) Results.Add(new ResultLine("Placement", "Both parts stay where they are"));
        else Results.Add(new ResultLine("Direction", j.Flip ? "flipped (faces meet)" : "same direction"));
        foreach (var note in notes) Results.Add(new ResultLine("Solver", note));
        if (_type != JointType.Rigid)
        {
            var frame = placements[j.ParentKey] * j.A;
            double azimuth = _type == JointType.Ball ? j.ZeroAngle + j.Clamped().v1 : j.ZeroAngle;
            _axisFrame = _type is JointType.Planar or JointType.Ball ? frame * Xform.RotZ(azimuth * Math.PI / 180) : frame;
        }
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_type is JointType.Revolute or JointType.Cylindrical && s.Hover is { } pick &&
            ActiveSlot is { } slot && s.Matches(pick, slot.PickFilter) &&
            s.Find(pick.Obj)?.Kind == ObjKind.Solid && s.FrameOf(pick) is { } candidate)
        {
            double extent = s.Camera.PixelSizeAt(candidate.origin) * 55;
            var end = candidate.origin + candidate.z * extent;
            o.AddLine(candidate.origin - candidate.z * extent, end, 0xFF35B6FF, 3f);
            o.AddLabel(end, "Pick axis");
        }
        if (!AsPositioned && _frameA is { } a) Triad(o, a, 12, 0xFFFFC53D);
        if (!AsPositioned && _frameB is { } b) Triad(o, b, 9, 0xFF35B6FF);
        if (_labelA is { } first) o.AddLabel(first.at, first.label);
        if (_labelB is { } second) o.AddLabel(second.at, second.label);
        if (_axisFrame is not { } axis) return;
        double length = s.Camera.PixelSizeAt(axis.T) * 75;
        void Axis(Vec3 dir, string label)
        {
            o.AddLine(axis.T - dir * length, axis.T + dir * length, 0xFFFFC53D, 3.5f);
            o.AddLabel(axis.T + dir * length, label);
        }
        switch (_type)
        {
            case JointType.Planar: Axis(axis.AxisX, "Slide X"); Axis(axis.AxisY, "Slide Y"); break;
            case JointType.Ball: Axis(axis.AxisZ, "Spin axis"); Axis(axis.AxisX, "Tilt axis"); o.AddLabel(axis.T, "Pivot point"); break;
            case JointType.Slider: Axis(axis.AxisZ, "Slide axis"); break;
            case JointType.Cylindrical: Axis(axis.AxisZ, "Turn and slide axis"); break;
            case JointType.Revolute: Axis(axis.AxisZ, "Hinge axis"); break;
        }
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
/// <summary>Turns bodies modeled in this file into parts where they stand, ready for joints. The first is grounded if nothing is.</summary>
public sealed class MakePartCommand : Command
{
    readonly SelectionSlot _bodies;
    public MakePartCommand() => _bodies = Slot(new SelectionSlot { Label = "Bodies", Filter = SelFilter.Solid, Multi = true });

    public override string Title => "Make Part";
    public override string Icon => "component";
    public override string OkText => "Make parts";
    public override string Hint => "Turn solid bodies in this file into parts, so joints can connect them. They stay where they are and keep their names. If nothing is grounded yet, the first one is. Picking a body with a joint tool does the same thing, so this is only needed to set the parts up first.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var ids = _bodies.Items.Select(i => i.Obj).Distinct().Where(id => S.Find(id)?.Component == null).ToList();
        if (ids.Count == 0) return null;
        Results.Add(new ResultLine("Parts to make", ids.Count.ToString()));
        return new CommandOutput { Label = Title, Icon = Icon, After = s => { foreach (var id in ids) s.MakePart(id); } };
    }
}

public sealed class DriveJointsCommand : Command
{
    readonly List<(JointInfo joint, NumberParam v1, NumberParam? v2)> _rows = [];
    readonly Dictionary<string, (double v1, double v2)> _last = [];

    /// <summary>Drag a joint in the viewport: an arc to turn it, an arrow to slide it. It stops at its limits.</summary>
    public override IReadOnlyList<HandleSpec> Handles => JointHandles.Build(S, _rows);

    public override string Title => "Drive Joints";
    public override string Icon => "joint-revolute";
    public override string OkText => "Keep positions";
    public override string Hint => "Move the joints with their values, or drag the handles in the viewport, and watch the assembly follow. Limits are respected and drawn: the arc or ruler shows how far a joint can go. Keep positions stores these values; Cancel puts everything back.";

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
            _last[j.Key] = (p1.Value, p2?.Value ?? j.Value2);
        }
    }

    static void SetQuietly(NumberParam p, double value) { var changed = p.Changed; p.Changed = null; p.Value = value; p.Changed = changed; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_rows.Count == 0) { Results.Add(new ResultLine("Joints", "none yet. Create joints first (Joints tab).")); return null; }
        var over = _rows.ToDictionary(r => r.joint.Key, r => r.joint with { Value = r.v1.Value, Value2 = r.v2?.Value ?? r.joint.Value2 });
        // the joint just moved stays where it was put; the rest of its loop follows, as in a motion study
        string? moved = _rows.Where(r => Math.Abs(r.v1.Value - _last[r.joint.Key].v1) > 1e-12 || r.v2 != null && Math.Abs(r.v2.Value - _last[r.joint.Key].v2) > 1e-12).Select(r => r.joint.Key).FirstOrDefault();
        var closed = S.CloseLoops(over, moved, moved == null ? default : _last[moved], out var problem);
        if (problem != null)
        {
            foreach (var r in _rows)
            {
                var last = _last[r.joint.Key];
                over[r.joint.Key] = r.joint with { Value = last.v1, Value2 = last.v2 };
                SetQuietly(r.v1, last.v1);
                if (r.v2 != null) SetQuietly(r.v2, last.v2);
            }
            Results.Add(new ResultLine("Cannot go there", problem, 2));
        }
        else if (closed != null)
            foreach (var r in _rows)
                if (closed.TryGetValue(r.joint.Key, out var v))
                {
                    over[r.joint.Key] = r.joint with { Value = v.v1, Value2 = v.v2 };
                    SetQuietly(r.v1, v.v1); if (r.v2 != null) SetQuietly(r.v2, v.v2);
                }
        foreach (var r in _rows) _last[r.joint.Key] = (over[r.joint.Key].Value, over[r.joint.Key].Value2);
        var placements = S.SolvePlacements(over, out var notes);
        var o = new CommandOutput { Label = "Drive joints", Icon = "joint-revolute", ReplaceIsMove = true };
        foreach (var comp in S.Components)
        {
            var p = placements[comp.Component!.Key];
            if (p.DistanceTo(comp.Component.Placement) > 1e-9) o.Replace.Add((comp, p.Apply(S.LocalShapeOf(comp))));
        }
        var values = _rows.ToDictionary(r => r.joint.Key, r => (over[r.joint.Key].Value, over[r.joint.Key].Value2));
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

    Vec3? Centre(out double size)
    {
        size = 30;
        if (_comp.Items.Count == 0 || S.Find(_comp.Items[0].Obj) is not { } obj || obj.Component == null) return null;
        var m = obj.Mesh; size = Math.Max((m.Max - m.Min).Length * 0.5, 10);
        return (m.Min + m.Max) * 0.5;
    }

    public override IReadOnlyList<HandleSpec> Handles => [];
    public override HandleSpec? Handle => null;
    /// <summary>A component's place is a position and an orientation: the one shared gizmo, arrows and rings, riding on the typed move.</summary>
    public override GizmoSpec? Gizmo => Centre(out _) is not { } c ? null
        : GizmoSpec.At(c + new Vec3(_dx.Value, _dy.Value, _dz.Value), _dx, _dy, _dz, _rx, _ry, _rz);

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

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_comp.Items.Count == 0 || S.Find(_comp.Items[0].Obj) is not { } obj) return [];
            var m = obj.Mesh; var c = (m.Min + m.Max) * 0.5;
            if (_kind.Index == 0) return [new(c, Vec3.UnitX, _dx), new(c, Vec3.UnitY, _dy), new(c, Vec3.UnitZ, _dz)];
            var axis = _axis.Index switch { 0 => Vec3.UnitX, 1 => Vec3.UnitY, _ => Vec3.UnitZ };
            var through = new Vec3(_cx.Value, _cy.Value, _cz.Value);
            var foot = through + axis * Vec3.Dot(c - through, axis);
            var rel = c - foot; if (rel.Length < 1e-6) return [new(c, Vec3.UnitX, _cx), new(c, Vec3.UnitY, _cy), new(c, Vec3.UnitZ, _cz)];
            return [new(foot, axis, _angle, 1, rel.Normalized(), rel.Length)];
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

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

// =================================================================== Create a part from inside the assembly
/// <summary>Start a new part for this assembly: it opens in its own tab in the Design workspace, and comes back into the assembly when you are done.</summary>
public sealed class CreatePartCommand : Command
{
    public static readonly string[] Kinds = ["Solid", "Sheet metal", "Surface", "Freeform", "NURBS", "Mesh or scan"];
    readonly TextParam _name;
    readonly ChoiceParam _kind;

    public CreatePartCommand()
    {
        _name = Add(new TextParam { Label = "Part name", Hint = "for example Bracket" });
        _kind = Choice("What kind of part", 0, Kinds);
    }

    public override string Title => "Create Part";
    public override string Icon => "component";
    public override string OkText => "Create and model it";
    public override string Hint => "Model a new part in its own tab. Visible assembly components appear dimmed for reference. Press Finish part and return to insert the finished part.";

    protected override void OnBegin() { if (_name.Value.Length == 0) _name.Value = $"Part {S.Components.Count() + 1}"; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        string name = _name.Value.Trim().Length > 0 ? _name.Value.Trim() : $"Part {S.Components.Count() + 1}";
        int kind = _kind.Index;
        Results.Add(new ResultLine("Part", name)); Results.Add(new ResultLine("Starts in", Kinds[kind]));
        return new CommandOutput { NoOp = true, After = s => CadSession.CreatePartHandler?.Invoke(name, kind) };
    }
}

// =================================================================== Parts list
/// <summary>The assembly's parts list: each distinct part with its quantity, material, mass and cost, from the part info saved with every part.</summary>
public sealed class PartsListCommand : Command
{
    readonly TextParam _path;

    public PartsListCommand()
    {
        _path = Add(new TextParam { Label = "Save to (blank = Documents\\Prism\\Reports)", Hint = "full path of the .csv file" });
        Add(new ActionParam { Label = "Save parts list (CSV)", Run = Save });
    }

    public override string Title => "Parts List";
    public override string Icon => "component";
    public override string OkText => "Done";
    public override string Hint => "Every part in the assembly, counted. Parts with the same part number are one line. Fill in part number, material, supplier and cost in each part's Part info (Properties panel) and they appear here. Mass needs a material applied to the part.";

    void Save()
    {
        try
        {
            var rows = PartsList.Build(S);
            if (rows.Count == 0) { S.Say("There are no components in this assembly yet.", true); return; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Reports"); Directory.CreateDirectory(dir);
            string name = S.FilePath != null ? Path.GetFileNameWithoutExtension(S.FilePath) : "assembly";
            string file = string.IsNullOrWhiteSpace(_path.Value) ? Path.Combine(dir, $"parts_list_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv") : _path.Value;
            File.WriteAllText(file, PartsList.ToCsv(rows));
            S.Say($"Saved {file}");
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var rows = PartsList.Build(S);
        if (rows.Count == 0) { Results.Add(new ResultLine("Parts", "none yet. Insert components first (Assemble tab).")); return new CommandOutput { NoOp = true }; }
        foreach (var r in rows)
        {
            string detail = string.Join("   ", new[] { r.PartNumber, r.Material, r.TotalMassGrams is { } g ? $"{g:0.##} g" : "", r.TotalCost is { } c ? $"cost {c:0.##}" : "" }.Where(x => x.Length > 0));
            Results.Add(new ResultLine($"{r.Quantity} ×  {r.Name}", detail.Length > 0 ? detail : "no part info yet"));
        }
        Results.Add(new ResultLine("Distinct parts", rows.Count.ToString()));
        Results.Add(new ResultLine("Total components", rows.Sum(r => r.Quantity).ToString()));
        double mass = rows.Sum(r => r.TotalMassGrams ?? 0); double cost = rows.Sum(r => r.TotalCost ?? 0);
        if (mass > 0) Results.Add(new ResultLine("Total mass", mass >= 1000 ? $"{mass / 1000:0.###} kg" : $"{mass:0.##} g"));
        if (cost > 0) Results.Add(new ResultLine("Total cost", $"{cost:0.##}"));
        return new CommandOutput { NoOp = true };
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
