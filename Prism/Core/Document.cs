using System;
using System.Collections.Generic;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Solid/Surface/Curve/Sketch are exact B-rep geometry; Form is a subdivision-surface cage that is converted to NURBS on demand.</summary>
public enum ObjKind { Solid, Surface, Curve, Sketch, Form, Mesh, Plane, Joint, Animation, Toolpath }

public static class Palette
{
    public const uint Solid = 0xFFB9C6D6;
    public const uint Surface = 0xFF7DC2D8;
    public const uint Curve = 0xFFDCE6F5;
    public const uint Sketch = 0xFF4C94FF;
    public const uint FormColor = 0xFF9FB4E6;
    public const uint Preview = 0xFF2E8BFF;
    public const uint ConstructionPlane = 0xFF9B8CFF;
    public const uint JointColor = 0xFFFFC53D;

    public static (float r, float g, float b) Unpack(uint c) => (((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

    static readonly uint[] Cycle = [0xFFB9C6D6, 0xFFD8C3A5, 0xFFA8CBB5, 0xFFCDB4DB, 0xFFE3B7A0, 0xFF9FC5E8];
    public static uint ForIndex(int i) => Cycle[i % Cycle.Length];
    public static uint DefaultFor(ObjKind k, int solidCount) => k switch
    {
        ObjKind.Solid => ForIndex(solidCount), ObjKind.Mesh => 0xFFC8D0DA, ObjKind.Surface => Surface, ObjKind.Sketch => Sketch, ObjKind.Plane => ConstructionPlane, ObjKind.Joint or ObjKind.Animation => JointColor, ObjKind.Toolpath => 0xFF3FCF8E, ObjKind.Form => FormColor, _ => Curve,
    };
}

/// <summary>An immutable entity in the document. Changing anything produces a new instance (cheap undo).</summary>
public sealed class SceneObject
{
    public Guid Id { get; }
    public string Name { get; }
    public ObjKind Kind { get; }
    public uint Color { get; }
    /// <summary>Assigned library material and finish (metal / gloss).</summary>
    public Appearance Look { get; }
    /// <summary>Triangle mesh for Mesh objects (scans, STL imports); null for every other kind.</summary>
    public Meshing.TriMesh? Tri { get; }
    public Material? Material => Look.PhysicalSnapshot ?? Materials.Find(Look.MaterialId);
    public bool Visible { get; }
    public Plane3? SketchPlane { get; }
    public FlatPatternMap? FlatMap { get; init; }
    /// <summary>Direct edits of this body are recorded as editable history steps. Off by default: they are plain undo steps unless the user turns tracking on.</summary>
    public bool TrackHistory { get; init; }
    /// <summary>A reference part, such as the part a mold is made around: shown and usable in the session, but not saved with the file and not a source for drawings.</summary>
    public bool IsReference { get; init; }
    public bool IsMoldHalf { get; init; }
    public SceneObject WithReference(bool on) => Copy(reference: on);
    public SceneObject WithMoldHalf() => Copy(moldHalf: true);
    /// <summary>Control cage for Form objects (null otherwise).</summary>
    public SubdMesh? Form { get; }
    /// <summary>Parametric definition for Sketch objects (so they can be edited again).</summary>
    public Sketching.SketchModel? SketchData { get; }
    /// <summary>Assembly role: a placed component, a joint or an animation (null for ordinary objects).</summary>
    public Assembly.AssemblyData? Asm { get; }
    /// <summary>Planes, joints and animations are drawn as annotations: they are not geometry to fit, export or analyse.</summary>
    public bool IsAnnotation => Kind is ObjKind.Plane or ObjKind.Joint or ObjKind.Animation or ObjKind.Toolpath;
    public Assembly.ComponentInfo? Component => Asm as Assembly.ComponentInfo;

    Shape? _shape;
    MeshData? _mesh;

    /// <summary>Exact geometry. For a Form this is generated (and cached) by fitting NURBS patches to the subdivision surface.</summary>
    public Shape Shape => _shape ??= Form != null ? FormToBrep.ToShape(Form) : throw new InvalidOperationException(Tri != null ? "This is a mesh: convert it to a solid first (Mesh tab > Mesh to Solid)." : "Object has no geometry.");
    public MeshData Mesh => _mesh ??= Tri != null ? Tri.ToMeshData() : Form != null ? Form.BuildMesh() : Shape.Tessellate();
    public bool HasBrep => Tri == null;
    /// <summary>Hand over a tessellation made elsewhere (a background import) so nothing re-meshes it on the UI thread.</summary>
    public void PrimeMesh(MeshData m) => _mesh ??= m;
    MeshData? _fine;
    /// <summary>A much finer tessellation, used only while a surface-quality display (zebra stripes, curvature map) is on: normals must vary smoothly across each triangle's neighbours or the stripes come out serrated.</summary>
    public MeshData FineMesh => _fine ??= Tri != null || Form != null ? Mesh : Shape.Tessellate(null, 0.012);

    public SceneObject(Guid id, string name, Shape? shape, ObjKind kind, uint color, bool visible = true, Plane3? plane = null, SubdMesh? form = null, Sketching.SketchModel? sketch = null, Appearance? look = null, Meshing.TriMesh? tri = null, Assembly.AssemblyData? asm = null)
    {
        Look = look ?? Appearance.Default; Tri = tri; Asm = asm;
        Id = id; Name = name; _shape = shape; Kind = kind; Color = color; Visible = visible; SketchPlane = plane; Form = form; SketchData = sketch;
    }

    public static ObjKind Classify(Shape s) => s.HasSolids ? ObjKind.Solid : s.HasFaces ? ObjKind.Surface : ObjKind.Curve;

    SceneObject Copy(string? name = null, uint? color = null, bool? visible = null, Appearance? look = null, bool? reference = null, bool? moldHalf = null, bool? tracking = null) => new(Id, name ?? Name, _shape, Kind, color ?? Color, visible ?? Visible, SketchPlane, Form, SketchData, look ?? Look, Tri, Asm)
    { _mesh = _mesh, FlatMap = FlatMap, IsReference = reference ?? IsReference, IsMoldHalf = moldHalf ?? IsMoldHalf, TrackHistory = tracking ?? TrackHistory };
    public SceneObject WithTracking(bool on) => Copy(tracking: on);
    public SceneObject WithName(string n) => Copy(name: n);
    public SceneObject WithColor(uint c) => Copy(color: c);
    public SceneObject WithVisible(bool v) => Copy(visible: v);
    /// <summary>Same identity and geometry, new construction/reference plane frame.</summary>
    public SceneObject WithPlane(Plane3 p) => new(Id, Name, _shape, Kind, Color, Visible, p, Form, SketchData, Look, Tri, Asm) { _mesh = _mesh, FlatMap = FlatMap, IsReference = IsReference, TrackHistory = TrackHistory };
    /// <summary>Same identity, new sketch model (used to append model-space datums without touching the shape).</summary>
    public SceneObject WithSketch(Sketching.SketchModel? m) => new(Id, Name, _shape, Kind, Color, Visible, SketchPlane, Form, m, Look, Tri, Asm) { _mesh = _mesh, FlatMap = FlatMap, IsReference = IsReference, TrackHistory = TrackHistory };
    public SceneObject WithLook(Appearance a) => Copy(look: a);
    /// <summary>Same identity, new triangle mesh.</summary>
    public SceneObject WithTri(Meshing.TriMesh t) => new(Id, Name, null, ObjKind.Mesh, Color, Visible, null, null, null, Look, t);
    /// <summary>Assign physical data; preserve an explicit visual override, including recognizable legacy finishes.</summary>
    public bool HasAppearanceOverride => Look.Surface?.Override == true || Look.Surface == null && Material is { } material &&
        (Color != material.Color || Look.Metal != material.Metal || Look.Gloss != material.Gloss || Look.Pattern != SurfacePattern.Automatic || Look.PatternScale != 12 || Look.PatternAngle != 0);
    public SceneObject WithMaterial(Material m) => HasAppearanceOverride
        ? Copy(look: Look with { MaterialId = m.Id, PhysicalSnapshot = m })
        : Copy(color: m.Color, look: m.Look);
    /// <summary>Same identity (id, name, colour), new exact geometry (a Form becomes a regular B-rep object).</summary>
    public SceneObject WithShape(Shape s) => new(Id, Name, s, Kind is ObjKind.Sketch or ObjKind.Plane or ObjKind.Joint or ObjKind.Animation or ObjKind.Toolpath ? Kind : Classify(s), Color, Visible, SketchPlane, null, null, Look, null, Asm) { FlatMap=FlatMap, TrackHistory = TrackHistory, IsMoldHalf = IsMoldHalf };
    /// <summary>Same identity with new assembly data (and optionally new geometry, e.g. after the component moved).</summary>
    public SceneObject WithAsm(Assembly.AssemblyData? a, Shape? shape = null) => new(Id, Name, shape ?? _shape, Kind, Color, Visible, SketchPlane, Form, SketchData, Look, Tri, a) { _mesh = shape == null ? _mesh : null, FlatMap = FlatMap, IsReference = IsReference, IsMoldHalf = IsMoldHalf, TrackHistory = TrackHistory };
    /// <summary>Same identity, new sketch geometry and definition.</summary>
    /// <summary>A picture on a plane: the plane object keeps the frame outline, the info holds the picture and how it is shown.</summary>
    public SceneObject WithImage(Assembly.ImageInfo info, Shape frame, Plane3 plane) => new(Id, Name, frame, ObjKind.Plane, Color, Visible, plane, null, null, Look, null, info);

    public SceneObject WithSketch(Shape s, Sketching.SketchModel model, Plane3 plane) => new(Id, Name, s, ObjKind.Sketch, Color, Visible, plane, null, model, Look);
    /// <summary>Same identity, new control cage.</summary>
    public SceneObject WithForm(SubdMesh f) => new(Id, Name, null, ObjKind.Form, Color, Visible, null, f, null, Look);
}

public enum SelKind { Object, Face, Edge, Origin, Datum, Entity }

public readonly record struct SelItem(Guid Obj, SelKind Kind, int Index, int Index2 = 0)
{
    public static SelItem Of(SceneObject o) => new(o.Id, SelKind.Object, -1);
}

[Flags]
public enum SelFilter
{
    None = 0, Solid = 1, SurfaceObj = 2, CurveObj = 4, SketchObj = 8, Face = 16, Edge = 32, Form = 64, Plane = 128, Axis = 256, MeshObj = 512,
    /// <summary>Datum annotations (points, axes, planes) hosted by sketch data.</summary>
    Datum = 1024,
    AnyObject = Solid | SurfaceObj | CurveObj | SketchObj | Form | MeshObj,
    AnyCurve = CurveObj | SketchObj,
    Any = AnyObject | Face | Edge | Plane | Axis | Datum,
}

public enum SelMode { Object, Face, Edge }
