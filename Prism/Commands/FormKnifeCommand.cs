using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Cut a freeform's cage along a plane: every face the plane passes through is split along the cut, and the new points land where it crosses the edges.
/// The plane is a construction plane or flat face you pick, or one of the three world planes at a position you type.
/// </summary>
public sealed class FormKnifeCommand : Command
{
    readonly SelectionSlot _form, _plane;
    readonly ChoiceParam _across;
    readonly NumberParam _position, _crease;

    public FormKnifeCommand()
    {
        _form = Slot(new SelectionSlot { Label = "Freeform to cut", Filter = SelFilter.Form });
        _plane = Slot(new SelectionSlot { Label = "Cutting plane or flat face (optional)", Filter = SelFilter.Plane | SelFilter.Face, Optional = true });
        _across = Choice("Or cut across", 0, "Left to right (X)", "Front to back (Y)", "Bottom to top (Z)");
        _position = Num("Position along that direction", 0, -100000, 100000, .5);
        _crease = Num("Crease the cut", 0, 0, 5, .1, "");
        _crease.HelpText = "0 leaves the cut as plain edges. A sharpness above 0 makes it a crease, so the smooth surface keeps a line there.";
    }

    public override string Title => "Knife";
    public override string Icon => "splitbody";
    public override string Hint => "Cut a freeform with a plane. Pick the form, then a construction plane or a flat face to cut along, or choose a world direction and type where the cut goes. Every face the plane passes through is split, and the edges get a new point where it crosses them.";

    protected override void UpdateVisibility() { _across.IsVisible = _position.IsVisible = _plane.Items.Count == 0; }

    protected override void OnBegin()
    {
        if (_form.Items.Count == 0)
        {
            var forms = S.Objects.Where(o => o.Form != null && o.Visible).ToList();
            if (forms.Count == 1) _form.Items.Add(SelItem.Of(forms[0]));
        }
    }

    /// <summary>The plane being cut along, as a point on it and its normal.</summary>
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
        Results.Clear();
        if (!Complete) return null;
        var (origin, normal) = CutPlane();
        var output = new CommandOutput { Label = "Knife", Icon = Icon };
        int points = 0, faces = 0;
        foreach (var obj in ObjectsOf(_form))
        {
            if (obj.Form is not { } cage) continue;
            var work = cage.Clone();
            var added = FormOps.CutWithPlane(work, origin, normal, out int cut, _crease.Value);
            if (cut == 0 && added.Count == 0) continue;
            points += added.Count; faces += cut;
            output.ReplaceForms.Add((obj, work));
        }
        if (output.ReplaceForms.Count == 0) throw new FormatException("The plane does not cut through any face of the form. Move it, or turn it, so it crosses the cage.");
        Results.Add(new ResultLine("Faces cut", faces.ToString()));
        Results.Add(new ResultLine("New points", points.ToString()));
        return output;
    }
}
