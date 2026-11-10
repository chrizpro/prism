using System;
using System.Linq;
using System.Threading.Tasks;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Physical PCB geometry for enclosure integration; no copper or electrical connectivity.</summary>
public sealed class PcbBoardCommand : Command
{
    readonly NumberParam _width, _depth, _thickness, _z, _diameter, _inset;
    readonly BoolParam _holes;
    public PcbBoardCommand()
    {
        _width = Num("Board width (X)", 80, 1, 2000);
        _depth = Num("Board length (Y)", 50, 1, 2000);
        _thickness = Num("Board thickness", 1.6, .1, 20, .1);
        _z = Num("Bottom elevation (Z)", 0);
        _holes = Check("Four mounting holes", true);
        _diameter = Num("Mounting hole diameter", 3.2, .1, 100, .1);
        _inset = Num("Hole center inset from edges", 5, .1, 1000, .5);
    }
    public override string Title => "PCB Board";
    public override string Icon => "pcb";
    public override string Hint => "Create a rectangular board centered on the world XY origin. Elevation is its bottom face. Holes are mechanical through-holes; copper, plating and nets are not modeled.";
    protected override void UpdateVisibility() => _diameter.IsVisible = _inset.IsVisible = _holes.Value;
    protected override CommandOutput? Build()
    {
        Results.Clear();
        Shape board = Occ.Box(_width.Value, _depth.Value, _thickness.Value);
        if (_holes.Value)
        {
            double radius = _diameter.Value / 2, inset = _inset.Value;
            if (inset <= radius || inset >= Math.Min(_width.Value, _depth.Value) / 2 - radius)
                throw new FormatException("Hole centers must leave material at the board edge and between neighboring holes. Reduce the diameter or change the inset.");
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                using var cylinder = Occ.Cylinder(radius, _thickness.Value + 2);
                using var cutter = Occ.Translate(cylinder, new(x * (_width.Value / 2 - inset), y * (_depth.Value / 2 - inset), -1));
                var next = Occ.Cut(board, cutter); board.Dispose(); board = next;
            }
        }
        if (_z.Value != 0) { var moved = Occ.Translate(board, new(0, 0, _z.Value)); board.Dispose(); board = moved; }
        if (!board.IsValid) { board.Dispose(); throw new FormatException("The board geometry is invalid. Check its dimensions and mounting holes."); }
        Results.Add(new ResultLine("Board", $"{_width.Value:0.##} × {_depth.Value:0.##} × {_thickness.Value:0.###} mm"));
        var output = new CommandOutput();
        output.Add.Add(new(board, "PCB Board", ObjKind.Solid, 0xFF21734D, Look: Materials.Find("gfrp")!.Look));
        return output;
    }
}

public sealed class ComponentEnvelopeCommand : Command
{
    readonly TextParam _reference;
    readonly NumberParam _width, _depth, _height, _x, _y, _z, _angle, _clearance;
    readonly ChoiceParam _side;
    public ComponentEnvelopeCommand()
    {
        _reference = Text("Reference designator", "U1", "U1, J1, R1…");
        _width = Num("Body width (X)", 10, .1, 1000, .5);
        _depth = Num("Body length (Y)", 10, .1, 1000, .5);
        _height = Num("Body height", 3, .1, 1000, .5);
        _x = Num("Center X", 0); _y = Num("Center Y", 0);
        _z = Num("Board face elevation (Z)", 1.6);
        _angle = Num("Rotation about Z", 0, -360, 360, 15, "°");
        _side = Choice("Board side", 0, "Top (+Z)", "Bottom (−Z)");
        _clearance = Num("Clearance allowance", 0, 0, 100, .25);
        _clearance.HelpText = "Enlarges this envelope in X/Y and away from the board. It is a mechanical keepout, not an electrical clearance rule.";
    }
    public override string Title => "Component Envelope";
    public override string Icon => "component";
    public override string Hint => "Place an approximate package or keepout for enclosure checks. Coordinates use world XY; elevation is the mounting face. For accurate connectors or packages, import a STEP model.";
    protected override CommandOutput? Build()
    {
        Results.Clear();
        string reference = _reference.Value.Trim();
        if (reference.Length == 0 || reference.Length > 40) throw new FormatException("Enter a reference designator of 1–40 characters.");
        double margin = _clearance.Value, height = _height.Value + margin;
        Shape body = Occ.Box(_width.Value + 2 * margin, _depth.Value + 2 * margin, height);
        if (_angle.Value != 0) { var rotated = Occ.Rotate(body, Vec3.Zero, Vec3.UnitZ, _angle.Value); body.Dispose(); body = rotated; }
        var placed = Occ.Translate(body, new(_x.Value, _y.Value, _z.Value - (_side.Index == 1 ? height : 0))); body.Dispose();
        var output = new CommandOutput();
        output.Add.Add(new(placed, reference + (margin > 0 ? " keepout" : " envelope"), ObjKind.Solid, margin > 0 ? 0xFFE2A348 : 0xFF303B4B));
        Results.Add(new ResultLine("Envelope", margin > 0 ? $"Includes {margin:0.##} mm mechanical clearance" : "Approximate component body"));
        return output;
    }
}

public sealed class PcbOutlineCommand : Command
{
    readonly SelectionSlot _outline;
    readonly NumberParam _thickness;
    public PcbOutlineCommand()
    {
        _outline = Slot(new SelectionSlot { Label = "Closed planar outline", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Face });
        _thickness = Num("Board thickness", 1.6, .1, 20, .1);
    }
    public override string Title => "PCB from Outline";
    public override string Icon => "pcb";
    public override string Hint => "Create a board from a closed planar sketch, curve or face. Thickness follows the outline normal; the source outline is retained.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var (profile, normal) = ProfileUtil.Resolve(S, _outline.Items[0], true);
        if (profile.Count(SubKind.Face) != 1 || Occ.FaceInfoOf(profile.Sub(SubKind.Face, 0)).Type != "Plane")
            throw new FormatException("Choose one closed planar region, including any internal cutouts.");
        var board = Occ.Extrude(profile, normal * _thickness.Value);
        if (!board.IsValid || !board.HasSolids) { board.Dispose(); throw new FormatException("Choose a closed, planar outline that produces a valid board solid."); }
        var output = new CommandOutput();
        output.Add.Add(new(board, "PCB Board", ObjKind.Solid, 0xFF21734D, Look: Materials.Find("gfrp")!.Look));
        return output;
    }
}

static class BoardFace
{
    public static Xform Frame(CadSession session, SelItem item)
    {
        if (item.Kind != SelKind.Face || session.Find(item.Obj) is not { Kind: ObjKind.Solid } ||
            session.FaceFrame(item) is not { } frame)
            throw new FormatException("Pick a flat face of the board solid.");
        using var face = session.ShapeOf(item);
        if (face == null || Occ.FaceInfoOf(face).Type != "Plane")
            throw new FormatException("Pick a flat face of the board solid.");
        var center = face.Props().Center;
        center -= frame.normal * Vec3.Dot(center - frame.point, frame.normal);
        var x = Math.Abs(frame.normal.X) < .9 ? Vec3.UnitX : Vec3.UnitY;
        return Xform.FromFrame(center, x, frame.normal);
    }
}

public sealed class PlaceOnBoardCommand : Command
{
    readonly SelectionSlot _component, _board;
    readonly NumberParam _u, _v, _rotation;

    public PlaceOnBoardCommand()
    {
        _component = Slot(new SelectionSlot { Label = "Component body", Filter = SelFilter.Solid });
        _board = Slot(new SelectionSlot { Label = "Board face", Filter = SelFilter.Face });
        _u = Num("Board offset U", 0);
        _v = Num("Board offset V", 0);
        _rotation = Num("Rotation on board", 0, -360, 360, 5, "°");
    }

    public override string Title => "Place on Board";
    public override string Icon => "component";
    public override string Hint => "Pick an upright component solid, then click its location on a flat board face. The bottom center of the component lands there. Use Rotation on board to aim the connector.";

    public override bool PickPoint(PickResult hit, bool additive)
    {
        if (ActiveSlot != _board || hit.Item.Kind != SelKind.Face) return false;
        try
        {
            var point = BoardFace.Frame(S, hit.Item).Inverse().Apply(hit.Point);
            ConfigureInputs(() => { _u.Value = point.X; _v.Value = point.Y; Pick(hit.Item, additive); });
            OnChanged();
        }
        catch (Exception e) when (e is FormatException or KernelException) { S.Say(e.Message, true); }
        return true;
    }

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var component = S.Find(_component.Items[0].Obj) ?? throw new FormatException("Pick a component solid.");
        var board = _board.Items[0];
        if (board.Obj == component.Id) throw new FormatException("Pick a different solid for the board.");
        var frame = BoardFace.Frame(S, board);
        var bounds = component.Shape.Props();
        var baseCenter = new Vec3(bounds.Center.X, bounds.Center.Y, bounds.Min.Z);
        var placement = frame * Xform.Translation(new Vec3(_u.Value, _v.Value, 0))
            * Xform.RotZ(_rotation.Value * Math.PI / 180) * Xform.Translation(-baseCenter);
        var output = new CommandOutput();
        output.Replace.Add((component, placement.Apply(component.Shape)));
        return output;
    }
}

public sealed class PcbHoleCommand : Command
{
    readonly SelectionSlot _board;
    readonly NumberParam _x, _y, _diameter;
    public PcbHoleCommand()
    {
        _board = Slot(new SelectionSlot { Label = "Board body", Filter = SelFilter.Solid | SelFilter.Face });
        _x = Num("Hole center X", 0); _y = Num("Hole center Y", 0);
        _diameter = Num("Hole diameter", 3.2, .1, 100, .1);
    }
    public override string Title => "PCB Mounting Hole";
    public override string Icon => "pcb-hole";
    public override string Hint => "Click a flat board face where the hole should go. For a board in XY, you can also select its whole body and enter world X and Y.";

    protected override void UpdateVisibility()
    {
        bool face = _board.Items.Count > 0 && _board.Items[0].Kind == SelKind.Face;
        _x.Caption = face ? "Board offset U" : _x.Label;
        _y.Caption = face ? "Board offset V" : _y.Label;
    }

    public override bool PickPoint(PickResult hit, bool additive)
    {
        if (ActiveSlot != _board || hit.Item.Kind != SelKind.Face) return false;
        try
        {
            var point = BoardFace.Frame(S, hit.Item).Inverse().Apply(hit.Point);
            ConfigureInputs(() => { _x.Value = point.X; _y.Value = point.Y; Pick(hit.Item, additive); });
            OnChanged();
        }
        catch (Exception e) when (e is FormatException or KernelException) { S.Say(e.Message, true); }
        return true;
    }
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var board = S.Find(_board.Items[0].Obj)!; var bounds = board.Shape.Props();
        var item = _board.Items[0];
        var position = Xform.Translation(new Vec3(_x.Value, _y.Value, bounds.Min.Z - 1));
        double length = bounds.Size.Z + 2;
        if (item.Kind == SelKind.Face)
        {
            var face = BoardFace.Frame(S, item);
            position = Xform.FromFrame(face.Apply(new Vec3(_x.Value, _y.Value, 1)), face.AxisX, -face.AxisZ);
            length = bounds.Size.Length + 2;
        }
        using var hole = Occ.Cylinder(_diameter.Value / 2, length);
        using var cutter = position.Apply(hole);
        var result = Occ.Cut(board.Shape, cutter);
        if (!result.IsValid || bounds.Volume - result.Props().Volume < 1e-6)
        { result.Dispose(); throw new FormatException("The hole does not remove material from this board. Check the position and diameter."); }
        var output = new CommandOutput(); output.Replace.Add((board, result)); return output;
    }
}


public sealed class BoardClearanceCommand : AnalysisCommand
{
    readonly SelectionSlot _component, _obstacle;
    readonly NumberParam _minimum;

    public BoardClearanceCommand()
    {
        _component = Slot(new SelectionSlot { Label = "Component or board", Filter = SelFilter.Solid });
        _obstacle = Slot(new SelectionSlot { Label = "Enclosure or obstacle", Filter = SelFilter.Solid });
        _minimum = Num("Required clearance", 1, .01, 1000, .1);
    }

    public override string Title => "Check Clearance";
    public override string Icon => "measure";
    public override string Hint => "Pick two solids and enter the minimum gap they need. Use Interference to identify actual overlap.";

    protected override void Analyze()
    {
        if (!Complete) return;
        var first = ObjectsOf(_component).Single();
        var second = ObjectsOf(_obstacle).Single();
        if (first.Id == second.Id) throw new FormatException("Pick two different bodies.");
        if (!first.Shape.IsValid || !second.Shape.IsValid) throw new FormatException("Check and repair the geometry before measuring clearance.");
        using var overlap = Occ.Common(first.Shape, second.Shape);
        double gap = overlap.HasSolids && overlap.Props().Volume > 1e-6 ? 0 : Occ.Distance(first.Shape, second.Shape).Distance;
        Row("Minimum gap", N(gap, "mm"));
        Row("Required gap", N(_minimum.Value, "mm"));
        bool passes = gap >= _minimum.Value - 1e-6;
        Row("Result", passes ? "Meets required clearance" : "Below required clearance", passes ? 1 : 3);
    }
}

public sealed class BoardStepExportCommand : AnalysisCommand
{
    readonly SelectionSlot _bodies;
    readonly Func<Task<string?>> _pickPath;
    SceneObject[] _reviewed = [];

    public BoardStepExportCommand(Func<Task<string?>> pickPath)
    {
        _pickPath = pickPath;
        _bodies = Slot(new SelectionSlot { Label = "Bodies to export (empty = visible bodies)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form,
            Multi = true, Optional = true });
        Add(new ActionParam { Label = "Save STEP", Run = async () => await Save() });
    }

    public override string Title => "Export STEP";
    public override string Icon => "export";
    public override string Hint => "Review the exact bodies in the STEP file. Pick specific bodies, or leave the list empty to export visible bodies.";

    SceneObject[] Bodies() => (_bodies.Items.Count == 0 ? S.Objects.Where(body => body.Visible) : ObjectsOf(_bodies))
        .Where(body => IsBody(body) && body.HasBrep).Distinct().ToArray();

    protected override void Analyze()
    {
        _reviewed = Bodies();
        Row("Scope", _bodies.Items.Count == 0 ? "Visible bodies" : "Picked bodies");
        foreach (var body in _reviewed) Row("Export body", body.Name);
        if (_reviewed.Length == 0) Row("Export", "No exact bodies to export.", 2);
    }

    async Task Save()
    {
        try
        {
            if (!_reviewed.SequenceEqual(Bodies())) { OnChanged(); return; }
            if (_reviewed.Length == 0) return;
            var session = S;
            var reviewed = _reviewed;
            string? path = await _pickPath();
            if (path == null || session.ActiveCommand != this) return;
            if (!reviewed.SequenceEqual(Bodies())) { OnChanged(); return; }
            Occ.ExportStep(reviewed.Select(body => body.Shape).ToList(), path);
            session.Say($"Exported {System.IO.Path.GetFileName(path)}");
            Row("Saved", path);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { S.Say(e.Message, true); }
    }
}
