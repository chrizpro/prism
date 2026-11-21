using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Raise a sketch (text, a logo outline, any closed shape) off a face, or engrave it into the body. On a flat face that is a straight extrude of the shape.
/// On a curved face the shape is carried onto the surface: it is seen straight on, the part of the face it covers is lifted (or sunk) by the height along the face's own
/// normals, so the top of every letter follows the surface at the same height all over, which a straight extrude cannot do.
/// </summary>
public sealed class EmbossCommand : BodyCommand
{
    readonly SelectionSlot _profile, _onto;
    readonly ChoiceParam _style, _method;
    readonly NumberParam _height;
    readonly BoolParam _flip;

    public EmbossCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Sketch (text or shape)", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Face | SelFilter.Edge, Multi = true, ProfileSelection = true });
        _style = Choice("Style", 0, "Emboss (raised)", "Engrave (cut in)");
        _style.Caption = "Style";
        _height = Num("Height / depth", 1, 0.01, 1e4, 0.5);
        _flip = Check("Reverse direction", false);
        AddOperation();
        Op!.IsVisible = false;
        Op.SetSilently(1);
        _style.Changed = () => { Op.SetSilently(_style.Index == 0 ? 1 : 2); UpdateVisibility(); OnChanged(); };
        // added after the operation, so a step saved before them keeps its values
        _method = Choice("Method", 0, "Automatic", "Straight (extrude)", "Follow the surface");
        _method.HelpText = "Automatic follows the surface on a curved face and extrudes straight on a flat one. Follow the surface carries the shape onto the face whatever its shape; Straight always extrudes it flat along the sketch's direction.";
        _onto = Slot(new SelectionSlot { Label = "Face to emboss onto (optional)", Filter = SelFilter.Face, Optional = true });
    }

    (SelItem? item, int count, Vec3 origin, Vec3 n) _hc;

    /// <summary>The arrow in the viewport: drag it to set the height or depth, like Extrude.</summary>
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_profile.Items.Count == 0) return [];
            var first = _profile.Items[0];
            if (!Equals(_hc.item, first) || _hc.count != _profile.Items.Count)
            {
                try { var (prof, n) = ProfileUtil.Resolve(S, _profile, true); _hc = (first, _profile.Items.Count, CenterOf(prof), n); }
                catch (Exception e) when (e is not OutOfMemoryException) { return []; }
            }
            return [new HandleSpec(_hc.origin, _hc.n * (_flip.Value ? -1 : 1), _height, 1)];
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    public override string Title => "Emboss";
    public override string Hint => "Sketch text or a shape in front of a face of your body (or on it), choose it here, and set how far it stands proud (Emboss) or sinks in (Engrave). On a curved face the shape follows the surface at an even height; on a flat face it is a straight extrude. Pick a face to limit it to that face. Reverse direction flips a straight extrude that goes the wrong way.";

    // ---------------------------------------------------------------- following a curved surface
    const double Sink = 0.05;                       // how far the raised shape starts inside the body (an engraving starts outside it), so the join has no coincident faces

    /// <summary>The body the shape goes onto: the one picked as a face or as the target, else the nearest to the shape.</summary>
    SceneObject BodyFor(Shape prof)
    {
        if (_onto.Items.Count > 0 && S.Find(_onto.Items[0].Obj) is { Kind: ObjKind.Solid } owner) return owner;
        if (ObjectsOf(Target!).FirstOrDefault() is { } picked) return picked;
        SceneObject? best = null; double nearest = double.MaxValue;
        foreach (var body in Bodies())
        {
            try { var d = Occ.Distance(body.Shape, prof).Distance; if (d < nearest) { nearest = d; best = body; } }
            catch (KernelException) { }
        }
        return best ?? throw new FormatException("No body to emboss onto. Pick the target body.");
    }

    static (Vec3 centre, Vec3 normal) Mean(Shape face)
    {
        var m = face.Tessellate(); Vec3 c = Vec3.Zero, n = Vec3.Zero; int count = m.VertexCount;
        for (int i = 0; i < count; i++)
        {
            c += new Vec3(m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2]);
            n += new Vec3(m.Normals[3 * i], m.Normals[3 * i + 1], m.Normals[3 * i + 2]);
        }
        return count == 0 ? (Vec3.Zero, Vec3.Zero) : (c / count, n.Length < 1e-9 ? Vec3.Zero : n.Normalized());
    }

    /// <summary>The parts of the body's faces that lie inside the shape seen straight on, and face the sketch.</summary>
    List<Shape> Pieces(SceneObject body, Shape prof, Vec3 n)
    {
        var origin = CenterOf(prof); var size = body.Shape.Props();
        double reach = (size.Max - size.Min).Length + prof.Props().Size.Length + 10;
        using var lower = Occ.Translate(prof, n * -reach);
        using var prism = Occ.Extrude(lower, n * (2 * reach));
        var bounds = prism.Props();
        var pieces = new List<Shape>();
        IEnumerable<int> faces = _onto.Items.Count > 0 && _onto.Items[0].Obj == body.Id ? [_onto.Items[0].Index] : Enumerable.Range(0, body.Shape.Count(SubKind.Face));
        foreach (int index in faces)
        {
            using var face = body.Shape.Sub(SubKind.Face, index);
            var b = face.Props();
            if (b.Max.X < bounds.Min.X || bounds.Max.X < b.Min.X || b.Max.Y < bounds.Min.Y || bounds.Max.Y < b.Min.Y || b.Max.Z < bounds.Min.Z || bounds.Max.Z < b.Min.Z) continue;
            Shape common;
            try { common = Occ.Common(face, prism); } catch (KernelException) { continue; }
            for (int k = 0; k < common.Count(SubKind.Face); k++)
            {
                var piece = common.Sub(SubKind.Face, k);
                var (centre, normal) = Mean(piece);
                // keep the side the sketch looks at: a cylinder is crossed by the shape twice, at the front and at the back
                if (normal.Length > 0 && Vec3.Dot(normal, origin - centre) > -1e-6) pieces.Add(piece); else piece.Dispose();
            }
        }
        return pieces;
    }

    /// <summary>One lifted (or sunk) patch: the piece moved a little into the body (out of it for an engraving), then thickened to the height by the face's own normals.</summary>
    static Shape Slab(Shape piece, double height, bool engrave)
    {
        var (c0, normal) = Mean(piece);
        double want = engrave ? Sink : -Sink;                    // where the base starts: just inside the body for a raised shape, just outside for an engraving
        var basis = Occ.Offset(piece, want);
        if (Vec3.Dot(Mean(basis).centre - c0, normal) * want < 0) { basis.Dispose(); basis = Occ.Offset(piece, -want); }
        double thickness = height + Sink;
        var slab = Occ.Thicken(basis, thickness);
        var (cb, _) = Mean(basis); var (cs, _) = Mean(slab);
        if ((Vec3.Dot(cs - cb, normal) > 0) == engrave) { slab.Dispose(); slab = Occ.Thicken(basis, -thickness); }
        basis.Dispose();
        if (!slab.IsValid || !slab.HasSolids) throw new KernelException("The surface could not be thickened there.");
        return slab;
    }

    /// <summary>The raised (or engraved) shape following the surface, or null when the face under it is flat and a straight extrude does the job.</summary>
    Shape? Follow(Shape prof, Vec3 n, double height, bool engrave, bool force)
    {
        var body = BodyFor(prof);
        var pieces = Pieces(body, prof, n);
        if (pieces.Count == 0)
        {
            if (force) throw new FormatException("The shape does not fall on a face of the body. Move the sketch in front of the face you want, or pick the face.");
            return null;
        }
        if (!force && pieces.All(p => Occ.FaceInfoOf(p) is { Type: "Plane" } info && Math.Abs(Vec3.Dot(info.Normal, n)) > 0.999)) { foreach (var p in pieces) p.Dispose(); return null; }
        var slabs = new List<Shape>(); int failed = 0;
        foreach (var piece in pieces)
        {
            try { slabs.Add(Slab(piece, height, engrave)); } catch (KernelException) { failed++; }
            piece.Dispose();
        }
        if (slabs.Count == 0) throw new FormatException("The shape could not be carried onto that surface. Try a smaller height, a simpler shape, or Straight (extrude).");
        Results.Add(new ResultLine("Follows the surface", $"{slabs.Count} patch(es)" + (failed > 0 ? $", {failed} could not be built" : ""), failed > 0 ? 2 : 0));
        return slabs.Count == 1 ? slabs[0] : Occ.Compound(slabs);
    }

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        Results.Clear();
        var (prof, n) = ProfileUtil.Resolve(S, _profile, true);
        double h = _height.Value;
        if (h < 1e-6) return null;
        bool engrave = _style.Index == 1;
        if (_method.Index != 1 && Follow(prof, n, h, engrave, _method.Index == 2) is { } followed)
        {
            Results.Add(new ResultLine(engrave ? "Engraved" : "Raised by", $"{h:0.##} mm"));
            return Finish(followed, engrave ? "Engrave" : "Emboss", null);
        }
        var dir = n * (_flip.Value ? -1 : 1);
        Shape Make(Vec3 d) => Occ.Extrude(prof, d * h);
        var tool = Make(dir);
        if (_style.Index == 0 && !_flip.Value)
        {
            // a raised shape must stand out of the body: if the sketch normal points into it, turn the shape round
            try
            {
                var target = ResolveTarget(tool);
                double vol = Math.Abs(Occ.Mass(tool).Volume);
                if (vol > 1e-9 && Overlap(target, tool) / vol > 0.5) tool = Make(-dir);
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        Results.Add(new ResultLine(_style.Index == 0 ? "Raised by" : "Engraved", $"{h:0.##} mm"));
        return Finish(tool, _style.Index == 0 ? "Emboss" : "Engrave", null, () => Make(-dir));
    }
}
