using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Raise a sketch (text, a logo outline, any closed shape) off a face, or engrave it into the body.</summary>
public sealed class EmbossCommand : BodyCommand
{
    readonly SelectionSlot _profile;
    readonly ChoiceParam _style;
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
    public override string Hint => "Sketch text or a shape on a face of your body, choose it here, and set how far it stands proud (Emboss) or sinks in (Engrave). Reverse direction flips it if it goes the wrong way.";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        Results.Clear();
        var (prof, n) = ProfileUtil.Resolve(S, _profile, true);
        double h = _height.Value;
        if (h < 1e-6) return null;
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
