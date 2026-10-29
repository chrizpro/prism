using System;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

public sealed class RefoldCommand:Command
{
    readonly SelectionSlot _pattern,_curves;
    readonly BoolParam _face;
    readonly NumberParam _tolerance;
    public RefoldCommand()
    {
        _pattern=Slot(new SelectionSlot{Label="Original flat pattern",Filter=SelFilter.SurfaceObj|SelFilter.Face|SelFilter.CurveObj});
        _curves=Slot(new SelectionSlot{Label="Sketches or curves to refold",Filter=SelFilter.AnyCurve|SelFilter.Edge,Multi=true,Optional=true});
        _face=Check("Create a copy of the curved face",true);
        _face.Caption="Refold the flat face (including cuts)";
        _tolerance=Num("Curve tolerance",.00001,.000001,.1,.00001);
    }
    public override string Title=>"Refold";
    public override string Icon=>"warp";
    public override string OkText=>"Refold onto source face";
    public override string Hint=>"Select a cylindrical flat pattern made by Flatten. Refold restores its curved face with interior cutouts preserved. Optionally add sketches to wrap onto it. The original body and flat pattern are kept.";
    protected override CommandOutput? Build()
    {
        Results.Clear();if(!Complete)return null;
        var pattern=S.Find(_pattern.Items[0].Obj)??throw new FormatException("Select the original flat pattern.");
        var map=pattern.FlatMap??throw new FormatException("This pattern has no cylindrical return mapping. Flatten one untrimmed cylindrical face first.");
        // Reject moved/resized patterns instead of silently wrapping against an obsolete layout.
        var bounds=pattern.Shape.Props();
        if(Math.Abs(bounds.Min.X-map.FlatX)>.001||Math.Abs(bounds.Min.Y-map.FlatY)>.001||Math.Abs(bounds.Min.Z)>.001||Math.Abs(bounds.Max.Z)>.001||Math.Abs(bounds.Max.X-map.FlatX-map.Width)>.001||Math.Abs(bounds.Max.Y-map.FlatY-map.Height)>.001)
            throw new FormatException("The flat pattern was moved or resized. Use the original layout or flatten the source face again.");
        var output=new CommandOutput{Label="Refold cylindrical pattern",Icon=Icon};
        if(!_face.Value&&_curves.Items.Count==0)throw new FormatException("Enable face refolding or select a drawing to refold.");
        foreach(var item in _curves.Items)
        {
            if(item.Obj==pattern.Id)throw new FormatException("Select the drawing on the pattern, not the pattern outline itself.");
            using var flat=SurfaceUtil.CurveShape(S,item);
            using var wrapped=Occ.CylinderRefold(flat,map.Cylinder,map.FlatX,map.FlatY,_tolerance.Value);
            var joined=Occ.Profile([wrapped],false);
            if(!joined.IsValid)throw new FormatException("The refolded curve could not be made valid. Adjust the drawing or curve tolerance.");
            output.Add.Add(new(joined,"Refolded sketch",ObjKind.Curve,0xFFFFC53D));
        }
        if(_face.Value)
        {
            var face=Occ.CylinderRefoldFace(pattern.Shape,map.Cylinder,map.FlatX,map.FlatY,_tolerance.Value);
            output.Add.Add(new(face,"Refolded face",ObjKind.Surface));
            Results.Add(new ResultLine("Preserved cutouts",Math.Max(0,pattern.Shape.Count(SubKind.Wire)-1).ToString(),1));
            Results.Add(new ResultLine("Surface area",$"{face.Props().Area:0.###} mm²",1));
        }
        Results.Add(new ResultLine("Refolded drawings",_curves.Items.Count.ToString(),1));
        Results.Add(new ResultLine("Mapping","Original cylinder UV; arc length preserved",1));
        return output;
    }
}
