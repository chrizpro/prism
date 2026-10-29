using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Export;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>
/// Flatten: unfold faces or a surface into a flat pattern (a UV layout) that can be sketched on, dimensioned, exported to DXF or cut from sheet.
/// Bent sheet, cylinders and cones unroll exactly; other surfaces are flattened with the least distortion and the stretch is reported.
/// </summary>
public sealed class FlattenCommand : Command
{
    readonly SelectionSlot _src;
    readonly BoolParam _join;
    readonly ChoiceParam _place, _result;
    readonly NumberParam _gap, _tol;
    readonly TextParam _dxf;

    List<List<(double x, double y)>> _laidOut = [];
    List<List<Vec3>> _overlay = [];

    public FlattenCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Faces or a surface to flatten", Filter = SelFilter.Face | SelFilter.SurfaceObj, Multi = true });
        _join = Check("Join the picked faces into one piece", true);
        _place = Choice("Place the flat pattern", 0, "Beside the model (on the ground plane)", "At the origin (on the ground plane)");
        _gap = Num("Gap between pieces", 10, 0, 1e5, 1);
        _tol = Num("Outline tolerance", 0.01, 0, 100, 0.01);
        _result = Choice("Create", 0, "Flat surface and outline", "Flat surface only", "Outline curve only");
        _dxf = Text("Export DXF to (blank = Documents\\Prism\\Exports)", "", "full path of a .dxf file");
        Add(new ActionParam { Label = "Export the outline as DXF", Run = ExportDxf });
    }

    public override string Title => "Flatten";
    public override string Icon => "flatten";
    public override string OkText => "Create flat pattern";
    public override string Hint => "Pick the faces (or a whole surface) to unfold. They open up into a flat pattern on the ground plane, with the stretch measured for you. Pick faces that form one open piece: a closed box or sphere has to be left open along one face. Sheet metal and developable surfaces flatten without any stretch.";

    protected override void UpdateVisibility() { }

    // ------------------------------------------------------------ gather triangles of the picks
    (Vec3[] pos, int[] tri, Vec3 min, Vec3 max) Gather()
    {
        var positions = new List<Vec3>(); var tris = new List<int>();
        var weld = new Dictionary<(long, long, long), List<(int face, int gid)>>();
        Vec3 mn = new(double.MaxValue, double.MaxValue, double.MaxValue), mx = -mn;
        int faceId = 0;
        double size = 1;
        foreach (var it in _src.Items) { var o = S.Find(it.Obj); if (o != null) { var m = o.Mesh; size = Math.Max(size, (m.Max - m.Min).Length); } }
        double tol = size * 1e-6;
        (long, long, long) Q(double x, double y, double z) => ((long)Math.Round(x / tol), (long)Math.Round(y / tol), (long)Math.Round(z / tol));
        var done = new HashSet<(Guid, int)>();
        foreach (var it in _src.Items)
        {
            var o = S.Find(it.Obj); if (o == null) continue;
            MeshData m = o.Mesh;
            IEnumerable<int> faces = it.Kind == SelKind.Face ? [it.Index] : Enumerable.Range(0, Math.Max(0, m.FaceTriStart.Length - 1));
            foreach (int f in faces)
            {
                if (!done.Add((o.Id, f)) || f + 1 >= m.FaceTriStart.Length) continue;
                int myFace = _join.Value ? faceId : faceId;   // every face is its own "side" for welding
                var local = new Dictionary<int, int>();
                for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
                    for (int c = 0; c < 3; c++)
                    {
                        int vi = m.Indices[3 * t + c];
                        if (!local.TryGetValue(vi, out int gid))
                        {
                            var p = new Vec3(m.Positions[3 * vi], m.Positions[3 * vi + 1], m.Positions[3 * vi + 2]);
                            gid = -1;
                            if (_join.Value)
                            {
                                // join to a vertex of ANOTHER face at the same spot; never to this face's own seam copy
                                if (weld.TryGetValue(Q(p.X, p.Y, p.Z), out var list)) foreach (var (fc, g) in list) if (fc != myFace) { gid = g; break; }
                            }
                            if (gid < 0)
                            {
                                gid = positions.Count; positions.Add(p);
                                var key = Q(p.X, p.Y, p.Z);
                                if (!weld.TryGetValue(key, out var l2)) weld[key] = l2 = [];
                                l2.Add((myFace, gid));
                            }
                            local[vi] = gid;
                            mn = new(Math.Min(mn.X, p.X), Math.Min(mn.Y, p.Y), Math.Min(mn.Z, p.Z)); mx = new(Math.Max(mx.X, p.X), Math.Max(mx.Y, p.Y), Math.Max(mx.Z, p.Z));
                        }
                        tris.Add(gid);
                    }
                faceId++;
            }
        }
        return (positions.ToArray(), tris.ToArray(), mn, mx);
    }

    // ------------------------------------------------------------ build
    protected override CommandOutput? Build()
    {
        Results.Clear(); _laidOut = []; _overlay = [];
        if (_src.Items.Count == 0) return null;
        if (CylinderLayout() is { } cylinder) return cylinder;
        var (pos, tri, mn, mx) = Gather();
        if (tri.Length == 0) throw new FormatException("Those picks have no faces to flatten.");
        var res = Flattener.Flatten(pos, tri);
        if (res.Patches.Count == 0) throw new FormatException(res.Notes.Count > 0 ? res.Notes[0] : "Nothing could be flattened.");

        double x0 = _place.Index == 0 ? mx.X + Math.Max(_gap.Value, 1) : 0, y0 = _place.Index == 0 ? mn.Y : 0, cursor = x0;
        var o = new CommandOutput { Label = "Flatten", Icon = "flatten" };
        int k = 0;
        foreach (var patch in res.Patches)
        {
            k++;
            double dx = cursor - patch.Min.u, dy = y0 - patch.Min.v;
            var loops3 = patch.Loops.Select(l => Flattener.Simplify(l, _tol.Value)).Select(l => l.Select(p => new Vec3(p.u + dx, p.v + dy, 0)).ToList()).ToList();
            _overlay.AddRange(loops3);
            _laidOut.AddRange(loops3.Select(l => l.Select(p => (p.X, p.Y)).ToList()));
            var wires = loops3.Select(l => Occ.Polyline(l, true)).ToList();
            if (_result.Index != 2) o.Add.Add(new(Occ.Profile(wires, true), "Flat pattern", ObjKind.Surface));
            if (_result.Index != 1) foreach (var w in wires) o.Add.Add(new(w, "Flat outline", ObjKind.Curve));
            cursor += (patch.Max.u - patch.Min.u) + _gap.Value;

            string tag = res.Patches.Count > 1 ? $"Piece {k}  " : "";
            Results.Add(new ResultLine($"{tag}Size", $"{patch.Max.u - patch.Min.u:0.##} × {patch.Max.v - patch.Min.v:0.##} mm"));
            Results.Add(new ResultLine($"{tag}Area", $"{patch.Area3D:#,0.##} mm² → {patch.Area2D:#,0.##} mm²", 1));
            Results.Add(new ResultLine($"{tag}Stretch", patch.MaxStrain < 0.005 ? "none: lays flat exactly" : $"average {patch.MeanStrain * 100:0.#} %, worst {patch.MaxStrain * 100:0.#} %", patch.MaxStrain < 0.005 ? 1 : 2));
            if (patch.Loops.Count > 1) Results.Add(new ResultLine($"{tag}Holes", (patch.Loops.Count - 1).ToString(), 1));
        }
        foreach (var n in res.Notes) Results.Add(new ResultLine("Note", n, 2));
        return o;
    }

    CommandOutput? CylinderLayout()
    {
        if(_src.Items.Count!=1)return null;
        var item=_src.Items[0];var source=S.Find(item.Obj);if(source==null)return null;
        using var face=item.Kind==SelKind.Face?source.Shape.Sub(SubKind.Face,item.Index):Occ.Compound([source.Shape]);
        if(face.Count(SubKind.Face)!=1)return null;
        var cylinder=Occ.CylinderFrame(face);if(cylinder==null)return null;
        double width=cylinder[9]*(cylinder[11]-cylinder[10]),height=cylinder[13]-cylinder[12];
        // A trimmed cylindrical face with holes needs a trimmed layout, not a bounding rectangle.
        if(width<=0||height<=0||Math.Abs(face.Props().Area-width*height)>Math.Max(1e-5,width*height*1e-6))return null;
        var bounds=face.Props();double x=_place.Index==0?bounds.Max.X+Math.Max(_gap.Value,1):0,y=_place.Index==0?bounds.Min.Y:0;
        List<Vec3> outline=[new(x,y,0),new(x+width,y,0),new(x+width,y+height,0),new(x,y+height,0)];
        _overlay.Add(outline);_laidOut.Add(outline.Select(p=>(p.X,p.Y)).ToList());
        var wire=Occ.Polyline(outline,true);
        var map=new FlatPatternMap(source.Id,item.Kind==SelKind.Face?item.Index:0,cylinder,x,y);
        var output=new CommandOutput{Label="Flatten cylindrical face",Icon=Icon};
        if(_result.Index!=2)output.Add.Add(new(Occ.Profile([wire],true),"Flat pattern",ObjKind.Surface,Plane:Plane3.XY,FlatMap:map));
        if(_result.Index!=1)output.Add.Add(new(wire,"Flat outline",ObjKind.Curve,Plane:Plane3.XY,FlatMap:map));
        Results.Add(new ResultLine("Selected faces","1 (remaining body kept)",1));
        Results.Add(new ResultLine("Size",$"{width:0.###} × {height:0.###} mm"));
        Results.Add(new ResultLine("Stretch","none: analytic cylindrical unroll",1));
        Results.Add(new ResultLine("Return workflow","Sketch on the flat face, then use Refold",1));
        return output;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var l in _overlay) o.AddPolyline(l, 0xFF35B6FF, 2.6f, true);
    }

    void ExportDxf()
    {
        try
        {
            if (_laidOut.Count == 0) { S.Say("Pick faces first so there is a flat outline to export.", true); return; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Exports"); Directory.CreateDirectory(dir);
            string path = string.IsNullOrWhiteSpace(_dxf.Value) ? Path.Combine(dir, $"flat_{DateTime.Now:yyyyMMdd_HHmmss}.dxf") : _dxf.Value;
            Dxf.SaveOutlines(path, _laidOut);
            S.Say($"Saved {path}");
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }
}
