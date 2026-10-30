using System;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

public class RefoldCommand:Command
{
    readonly SelectionSlot _pattern,_curves, _target;
    readonly BoolParam _face, _keep, _mirror, _swap, _flip, _reblend;
    readonly NumberParam _tolerance;
    readonly NumberParam[] _scales, _offsets;
    readonly ChoiceParam? _operation;
    protected bool IsRefold => _operation == null || _operation.Index == 1;
    public RefoldCommand() : this(false) { }
    protected RefoldCommand(bool combined)
    {
        if (combined) _operation = Choice("Operation", 0, "Flatten", "Refold / wrap");
        _pattern=Slot(new SelectionSlot{Label="Original flat pattern",Caption="Flat pattern, planar reference face or reference curve",Filter=SelFilter.SurfaceObj|SelFilter.Face|SelFilter.AnyCurve|SelFilter.Edge});
        _curves=Slot(new SelectionSlot{Label="Sketches or curves to refold",Caption="Geometry to wrap (curves, sheets or solids)",Filter=SelFilter.AnyCurve|SelFilter.Edge|SelFilter.SurfaceObj|SelFilter.Solid,Multi=true,Optional=true});
        _target=Slot(new SelectionSlot{Label="Target face (optional)",Caption="Target face or curve (optional for a saved layout)",Filter=SelFilter.Face|SelFilter.SurfaceObj|SelFilter.AnyCurve|SelFilter.Edge,Optional=true});
        _face=Check("Create a copy of the curved face",true);
        _face.Caption="Refold the flat face (including cuts)";
        _tolerance=Num("Curve tolerance",.00001,.000001,.1,.00001);
        _tolerance.Caption="Mapping tolerance";
        _scales = new[] { Num("U scale",1,.001,100,.1,""), Num("V scale",1,.001,100,.1,""), Num("Normal scale",1,.001,100,.1,"") };
        _offsets = new[] { Num("U offset",0), Num("V offset",0), Num("Normal offset",0) };
        _mirror=Check("Mirror U",false); _swap=Check("Swap UV",false); _flip=Check("Flip normal",false);
        _keep=Check("Keep input geometry",true);
        _reblend=Check("Rebuild last fillet after wrapping",false);
        _reblend.HelpText="Reapply a final Fillet feature recorded in this document after wrapping its unfilleted body. This keeps its specified radius. Imported fillets and later geometry edits need manual filleting.";
    }
    public override string Title=>"Refold";
    public override string Icon=>"warp";
    public override string OkText=>"Refold onto source face";
    public override string Hint=>"Select a saved flat pattern or a planar reference face. Wrap curves, sheets or solids onto the original surface, or choose another target face. Scale, offset and flip the mapping; keep the inputs when you want another variation.";
    protected override void OnBegin()
    {
        if (_operation != null && S.Selection.Any(i => S.Find(i.Obj)?.FlatMap != null)) _operation.SetSilently(1);
    }
    protected override void UpdateVisibility()
    {
        if (_pattern == null) return;
        foreach (var slot in new[] { _pattern, _curves, _target }) slot.IsVisible=IsRefold;
        foreach (var p in new Param[] { _face, _tolerance, _mirror, _swap, _flip, _keep, _reblend }.Concat(_scales).Concat(_offsets)) p.IsVisible=IsRefold;
    }
    protected override CommandOutput? Build()
    {
        Results.Clear();if(!Complete)return null;
        var pattern=S.Find(_pattern.Items[0].Obj)??throw new FormatException("Select the original flat pattern.");
        var map=pattern.FlatMap;
        bool defaults = _scales.All(p=>p.Value==1) && _offsets.All(p=>p.Value==0) && !_mirror.Value && !_swap.Value && !_flip.Value;
        bool curvesOnly = _curves.Items.All(i => i.Kind==SelKind.Edge || S.Find(i.Obj)?.Kind is ObjKind.Curve or ObjKind.Sketch);
        if (map == null || map.Cylinder.Length != 14 || !defaults || !curvesOnly || _target.Items.Count>0 || _reblend.Value)
            return WrapGeneral(pattern, map);
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
        if (!_keep.Value) foreach (var obj in _curves.Items.Where(i=>i.Kind==SelKind.Object).Select(i=>S.Find(i.Obj)).OfType<SceneObject>().Distinct()) output.Remove.Add(obj);
        return output;
    }

    CommandOutput WrapGeneral(SceneObject pattern, FlatPatternMap? map)
    {
        using var target = _target.Items.Count>0 ? FaceOf(_target.Items[0])
            : map?.SourceFaceBrep is { } brep ? Occ.FromBrep(brep)
            : map != null && S.Find(map.SourceId) is { } original ? original.Shape.Sub(SubKind.Face,map.FaceIndex)
            : throw new FormatException("Choose a target face for this planar reference.");
        bool alongCurve=map==null && pattern.Shape.Count(SubKind.Face)==0;
        using var reference = alongCurve ? FaceOf(_pattern.Items[0]) : map?.ReferenceBrep is { } flat ? Occ.FromBrep(flat)
            : _pattern.Items[0].Kind == SelKind.Face ? FaceOf(_pattern.Items[0])
            : pattern.Shape.Count(SubKind.Face)==1 ? Occ.Compound([pattern.Shape])
            : throw new FormatException("Select a flat surface as the reference, rather than an outline.");
        var settings=_scales.Select(p=>p.Value).Concat(_offsets.Select(p=>p.Value)).Concat(new[] { _mirror.Value?1d:0, _swap.Value?1d:0, _flip.Value?1d:0, _tolerance.Value }).ToArray();
        var output=new CommandOutput {Label="Refold / wrap",Icon=Icon};
        foreach(var item in _curves.Items)
        {
            var obj=S.Find(item.Obj)??throw new FormatException("The input geometry is gone.");
            if(obj.Id==pattern.Id)throw new FormatException("Select geometry on the layout separately from the reference pattern.");
            if(_reblend.Value && item.Kind!=SelKind.Object)throw new FormatException("Select the whole filleted body when rebuilding its fillet.");
            var fillet = _reblend.Value ? LastFillet(obj) : null;
            using var geometry=item.Kind==SelKind.Edge ? S.ShapeOf(item)! : Occ.Compound([fillet?.before.Shape ?? obj.Shape]);
            var wrapped=alongCurve ? Occ.WrapAlongCurve(geometry,reference,target,settings) : Occ.WrapSurface(geometry,reference,target,settings);
            if(fillet is { } dress)
            {
                using var core=wrapped;
                wrapped=dress.end>0 && Math.Abs(dress.radius-dress.end)>1e-9 ? Occ.FilletVariable(core,dress.edges,dress.radius,dress.end) : Occ.Fillet(core,dress.edges,dress.radius);
                Results.Add(new ResultLine("Rebuilt fillet",$"{dress.radius:0.###} mm",1));
            }
            output.Add.Add(new(wrapped,"Wrapped",wrapped.HasSolids?ObjKind.Solid:wrapped.Count(SubKind.Face)>0?ObjKind.Surface:ObjKind.Curve));
            if(!_keep.Value && item.Kind==SelKind.Object && !output.Remove.Contains(obj))output.Remove.Add(obj);
        }
        if(_face.Value && !alongCurve) output.Add.Add(new(Occ.WrapSurface(pattern.Shape,reference,target,settings),"Refolded face",ObjKind.Surface));
        if(output.Add.Count==0)throw new FormatException("Select geometry to wrap or enable face refolding.");
        Results.Add(new ResultLine("Mapping",alongCurve?"Curve arc length with transported cross-section frames":"Surface UV with normal thickness; approximation checked against mapping tolerance",1));
        Results.Add(new ResultLine("Objects mapped",output.Add.Count.ToString(),1));
        return output;
    }

    Shape FaceOf(SelItem item)
    {
        var obj=S.Find(item.Obj)??throw new FormatException("The target face is gone.");
        return item.Kind==SelKind.Face ? obj.Shape.Sub(SubKind.Face,item.Index) : item.Kind==SelKind.Edge ? obj.Shape.Sub(SubKind.Edge,item.Index) : Occ.Compound([obj.Shape]);
    }

    (SceneObject before,int[] edges,double radius,double end)? LastFillet(SceneObject obj)
    {
        for(var node=S.Current;node.Parent!=null;node=node.Parent)
        {
            var after=node.After.FirstOrDefault(o=>o.Id==obj.Id);var before=node.Parent.After.FirstOrDefault(o=>o.Id==obj.Id);
            if(after==null || !ReferenceEquals(after.Shape,obj.Shape) || before==null || ReferenceEquals(before.Shape,after.Shape))continue;
            if(node.Recipe is {Title:"Fillet",Values.Count:>=1,Slots.Count:>=1} recipe && recipe.Values[0] is double radius)
            {
                var edges=recipe.Slots[0].Where(i=>i.Obj==obj.Id && i.Kind==SelKind.Edge).Select(i=>i.Index).ToArray();
                if(edges.Length>0)return(before,edges,radius,recipe.Values.Count>1 && recipe.Values[1] is double end ? end : 0);
            }
            break;
        }
        throw new FormatException("Rebuild fillet requires a final Fillet feature recorded for this object. Disable it to deform the existing fillets, or wrap the unfilleted body and fillet afterward.");
    }
}
