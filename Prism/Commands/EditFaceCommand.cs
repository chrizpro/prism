using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Edit Face (owner-approved 2026-11-17, docs/modeling/direct-modeling/README.md): a flat four-sided face of a solid becomes a grid of control points that
/// you move like the points of a NURBS surface. The outer ring of points is locked, so the outline stays where it was and the neighbouring faces meet the
/// new surface at their original edges (a sharp join). It reuses the control-point selection, gizmo and soft influence of Edit Control Points.
/// </summary>
public sealed class EditFaceCommand : EditPointsCommand
{
    readonly ChoiceParam _move;
    readonly IntParam _across, _up;
    readonly TextParam _offsets;
    SceneObject? _body;
    int _faceIndex = -1, _rows, _cols;
    Vec3[] _ref = [];
    Vec3 _normal;
    bool _moved;

    public EditFaceCommand() : base(new SelectionSlot { Label = "Face to reshape", Filter = SelFilter.Face, MaxCount = 1 }, false)
    {
        _move = Choice("Move points", 0, "Along the face normal", "Any direction");
        _across = Add(new IntParam { Label = "Grid cells across", Value = 3, Min = 2, Max = 8 });
        _up = Add(new IntParam { Label = "Grid cells up", Value = 3, Min = 2, Max = 8 });
        Add(new ActionParam { Label = "Reset points", Run = ResetPoints });
        // where the points are, kept in the step so Edit feature brings them back (offsets from the flat grid, inner points only)
        _offsets = Text("Point offsets", "", "kept by Edit feature");
        _offsets.IsVisible = false;
    }

    public override string Title => "Edit Face";
    public override string Icon => "edit-points";
    public override string OkText => "Apply";
    // the viewport tool holds no state of its own: the moved points are kept in the "Point offsets" value, so the step can be edited again
    public override bool CanEdit => true;
    public override string Hint => "Click a flat, four-sided face of a solid. It shows a grid of points: the grey outer ring is locked so the outline stays, and you move the inner points (click one, Shift adds, drag a box for several) with the gizmo. An influence radius makes neighbouring points follow. Apply puts the smooth surface back into the solid.";

    /// <summary>The outer ring of the grid stays where it is.</summary>
    public override bool IsLocked(int index) => _cols > 0 && (index / _cols == 0 || index / _cols == _rows - 1 || index % _cols == 0 || index % _cols == _cols - 1);

    (int face, int across, int up) _key = (-1, 0, 0);
    int _loadedFace = -1;
    protected override bool ReloadNeeded(SceneObject? item)
    {
        if (item == null) return Source != null;
        // the key is recorded as soon as a reload is asked for, so the reset the reload starts does not ask again
        var key = (SourceSlot.Items.Count > 0 ? SourceSlot.Items[0].Index : -1, _across.Value, _up.Value);
        if (item == Source && key == _key) return false;
        _key = key;
        return true;
    }

    protected override void LoadModel(SceneObject item)
    {
        if (SourceSlot.Items.Count == 0 || SourceSlot.Items[0].Kind != SelKind.Face) throw new KernelException("Pick a face of a solid.");
        var pick = SourceSlot.Items[0];
        var body = DirectHelpers.BodyOf(S, pick) ?? throw new KernelException("Edit Face works on a face of a solid body.");
        var original = S.ShapeOf(pick) ?? throw new KernelException("That face is gone.");
        var info = Occ.FaceInfoOf(original);
        if (info.Type != "Plane") throw new KernelException("Edit Face works on flat faces in this version. Curved faces are not supported yet.");
        if (original.Count(SubKind.Wire) != 1) throw new KernelException("This face has holes. Edit Face needs a face without holes.");
        int sides = original.Count(SubKind.Edge);
        if (sides != 4) throw new KernelException($"This face has {sides} sides. Edit Face needs a four-sided face.");

        // the four corners come from the face's own surface; the grid is the flat bilinear grid between them
        var corners = Occ.FaceNet(Occ.ToNurbs(original));
        Vec3 c00 = corners[0, 0], c10 = corners[0, corners.Cols - 1], c01 = corners[corners.Rows - 1, 0], c11 = corners[corners.Rows - 1, corners.Cols - 1];
        int cols = _across.Value + 1, rows = _up.Value + 1;
        var grid = new Vec3[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double u = (double)c / (cols - 1), v = (double)r / (rows - 1);
                grid[r * cols + c] = c00 * ((1 - u) * (1 - v)) + c10 * (u * (1 - v)) + c01 * ((1 - u) * v) + c11 * (u * v);
            }
        var surface = Occ.NurbsSurface(grid, rows, cols, Math.Min(3, cols - 1), Math.Min(3, rows - 1));
        var face = surface.Kind == ShapeKind.Face ? surface : surface.Sub(SubKind.Face, 0);

        bool newFace = pick.Index != _loadedFace || body.Id != _body?.Id;
        _body = body; _faceIndex = pick.Index; _loadedFace = pick.Index;
        _rows = rows; _cols = cols; _ref = grid; _normal = info.Normal.Normalized(); _moved = false;
        SetModel(NetModel.FromFace(face), body.Shape);
        if (newFace)
        {
            // a sensible soft radius for the face just picked: a third of its smaller side
            var size = original.Props().Size; var sides2 = new[] { size.X, size.Y, size.Z }.OrderByDescending(x => x).Take(2).ToArray();
            Radius.Value = Math.Max(0.5, Math.Round(sides2[1] / 3, 1));
        }
        RestoreOffsets();
    }

    // ------------------------------------------------------------ moving points
    public override void ApplyPoints(Vec3[] pts)
    {
        if (Model == null) return;
        for (int i = 0; i < pts.Length; i++)
        {
            if (IsLocked(i)) pts[i] = _ref[i];
            else if (_move.Index == 0) pts[i] = _ref[i] + _normal * Vec3.Dot(pts[i] - _ref[i], _normal);       // each point stays on its own line along the normal
        }
        _moved = Enumerable.Range(0, pts.Length).Any(i => pts[i].DistanceTo(_ref[i]) > 1e-9);
        base.ApplyPoints(pts);
        _offsets.Value = _moved ? string.Join(";", Enumerable.Range(0, pts.Length).Where(i => !IsLocked(i) && pts[i].DistanceTo(_ref[i]) > 1e-9)
            .Select(i => FormattableString.Invariant($"{i}:{pts[i].X - _ref[i].X:R},{pts[i].Y - _ref[i].Y:R},{pts[i].Z - _ref[i].Z:R}"))) : "";
    }

    void RestoreOffsets()
    {
        if (string.IsNullOrEmpty(_offsets.Value) || Model == null) return;
        var pts = (Vec3[])_ref.Clone();
        try
        {
            foreach (var part in _offsets.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var halves = part.Split(':'); var xyz = halves[1].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                int i = int.Parse(halves[0], CultureInfo.InvariantCulture);
                if (i >= 0 && i < pts.Length && !IsLocked(i)) pts[i] = _ref[i] + new Vec3(xyz[0], xyz[1], xyz[2]);
            }
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException) { return; }
        ApplyPoints(pts);
    }

    void ResetPoints()
    {
        if (Model == null) { S.Say("Pick a face first.", true); return; }
        base.ApplyPoints((Vec3[])_ref.Clone());
        _moved = false; _offsets.Value = "";
        Touch();
    }

    // ------------------------------------------------------------ the result: the new face sewn back into the solid
    protected override CommandOutput? Out()
    {
        if (Model == null || _body == null || !_moved) return null;
        // a point pushed further into the solid than it is deep would carry the face through the far side
        double depth = _body.Mesh.Positions.Length < 3 ? 0 : Enumerable.Range(0, _body.Mesh.Positions.Length / 3)
            .Max(v => Vec3.Dot(_ref[0] - new Vec3(_body.Mesh.Positions[3 * v], _body.Mesh.Positions[3 * v + 1], _body.Mesh.Positions[3 * v + 2]), _normal));
        for (int i = 0; i < _ref.Length; i++)
            if (Vec3.Dot(Model.Points[i] - _ref[i], _normal) < -depth * 0.98)
                throw new FormatException("A point is pushed through the other side of the solid, so nothing was changed. Move it less far.");
        var pieces = new List<Shape> { Model.Source };
        int faces = _body.Shape.Count(SubKind.Face);
        for (int i = 0; i < faces; i++) if (i != _faceIndex) pieces.Add(_body.Shape.Sub(SubKind.Face, i));
        Shape result;
        try { result = Occ.Sew(pieces, 0.01, true); }
        catch (KernelException e) { throw new FormatException("The reshaped face could not be joined back into the solid: " + e.Message); }
        if (!result.HasSolids || !result.IsValid)
            throw new FormatException("The reshaped face does not close into a valid solid, so nothing was changed. Move the points less far.");
        Results.Clear();
        Results.Add(new ResultLine("Volume", $"{_body.Shape.Props().Volume:0.##}  →  {result.Props().Volume:0.##} mm³"));
        Results.Add(new ResultLine("Outline", "kept where it was; the neighbouring faces meet the new surface at a sharp edge"));
        var output = new CommandOutput { Label = "Edit face", Icon = "edit-points" };
        output.Replace.Add((_body, result));
        return output;
    }
}
