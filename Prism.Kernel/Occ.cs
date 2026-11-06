using System.Runtime.InteropServices;

namespace Prism.Kernel;

public sealed class KernelException(string message) : Exception(message);

public enum ShapeKind { Compound, CompSolid, Solid, Shell, Face, Wire, Edge, Vertex }
public enum SubKind { Solid = 0, Shell = 1, Face = 2, Wire = 3, Edge = 4, Vertex = 5 }

/// <summary>Immutable handle to an Open CASCADE shape. Safe to share between document snapshots.</summary>
public sealed class Shape : IDisposable
{
    internal IntPtr Handle;
    internal Shape(IntPtr h) => Handle = h;
    ~Shape() => Free();
    public void Dispose() { Free(); GC.SuppressFinalize(this); }
    void Free() { var h = Interlocked.Exchange(ref Handle, IntPtr.Zero); if (h != IntPtr.Zero) Native.ck_free(h); }

    public ShapeKind Kind => (ShapeKind)Native.ck_shape_type(Handle);
    public int Count(SubKind k) => Native.ck_count(Handle, (int)k);
    public Shape Sub(SubKind k, int index) => Occ.Wrap(Native.ck_sub(Handle, (int)k, index));
    public bool IsValid => Native.ck_is_valid(Handle) != 0;
    public bool HasSolids => Count(SubKind.Solid) > 0;
    public bool HasFaces => Count(SubKind.Face) > 0;

    public unsafe ShapeProps Props()
    {
        double* p = stackalloc double[8];
        Native.ck_props(Handle, p);
        return new(p[0], p[1], new(p[2], p[3], p[4]), new(p[5], p[6], p[7]));
    }

    /// <summary>Direct children of a compound (used when importing files).</summary>
    public unsafe List<Shape> Children()
    {
        var buf = new IntPtr[4096];
        fixed (IntPtr* p = buf)
        {
            int n = Native.ck_children(Handle, p, buf.Length);
            return buf.Take(Math.Min(n, buf.Length)).Select(h => new Shape(h)).ToList();
        }
    }

    /// <summary>Angular deflection of the on-screen mesh. Smooth shading uses exact surface normals, so a coarser angle looks the same and meshes several times faster than 0.04.</summary>
    public const double DisplayAngle = 0.1;

    /// <param name="angle">Angular deflection in radians. Left out, the display mesh (no explicit deflection) uses <see cref="DisplayAngle"/> and exact-size meshes for export or machining keep the finer 0.04.</param>
    public unsafe MeshData Tessellate(double? deflection = null, double? angle = null)
    {
        double ang = angle ?? (deflection == null ? DisplayAngle : 0.04);
        var pr = Props();
        double diag = (pr.Max - pr.Min).Length;
        double lin = deflection ?? Math.Max(diag * 0.0004, 1e-4);
        IntPtr m = Native.ck_mesh_create(Handle, lin, ang);
        if (m == IntPtr.Zero) throw new KernelException(Occ.LastError);
        try
        {
            int nv, nt, ns;
            Native.ck_mesh_sizes(m, &nv, &nt, &ns);
            var d = new MeshData(nv, nt, ns) { Min = pr.Min, Max = pr.Max, FaceCount = Count(SubKind.Face), EdgeCount = Count(SubKind.Edge) };
            fixed (float* pp = d.Positions) fixed (float* pn = d.Normals) fixed (int* pi = d.Indices)
            fixed (int* pf = d.TriFace) fixed (float* pe = d.EdgePoints) fixed (int* ps = d.SegEdge)
                Native.ck_mesh_fill(m, pp, pn, pi, pf, pe, ps);
            d.BuildRanges();
            return d;
        }
        finally { Native.ck_mesh_free(m); }
    }

    public unsafe string ToBrep()
    {
        int n = Native.ck_brep_write(Handle, null, 0);
        var buf = new byte[n];
        fixed (byte* p = buf) Native.ck_brep_write(Handle, p, n);
        return System.Text.Encoding.ASCII.GetString(buf);
    }
}

public readonly record struct ShapeProps(double Volume, double Area, Vec3 Min, Vec3 Max)
{
    public Vec3 Size => Max - Min;
    public Vec3 Center => (Min + Max) * 0.5;
}

/// <summary>Triangle soup + edge polylines produced by the kernel. Triangles are grouped by B-rep face, segments by B-rep edge.</summary>
public sealed class MeshData
{
    public float[] Positions, Normals, EdgePoints;
    /// <summary>Optional per-vertex value used for heat-map / category shading (analysis results).</summary>
    public float[]? Scalars;
    /// <summary>Bumped when Scalars change in place so the GPU copy is refreshed.</summary>
    public int ScalarsVersion;
    /// <summary>Cached smooth curvature (1/mm) for the curvature map; shown through Scalars while that analysis is on.</summary>
    public float[]? Curvature;
    public bool ScalarsAreCurvature;
    public int[] Indices, TriFace, SegEdge;
    public Vec3 Min, Max;
    public int FaceCount, EdgeCount;
    /// <summary>[face] -> first triangle; length FaceCount+1.</summary>
    public int[] FaceTriStart = [];
    /// <summary>[edge] -> first segment; length EdgeCount+1.</summary>
    public int[] EdgeSegStart = [];

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
    public int SegmentCount => SegEdge.Length;

    public MeshData(int nv, int nt, int ns)
    {
        Positions = new float[nv * 3]; Normals = new float[nv * 3]; Indices = new int[nt * 3];
        TriFace = new int[nt]; EdgePoints = new float[ns * 6]; SegEdge = new int[ns];
    }

    internal void BuildRanges()
    {
        FaceTriStart = Starts(TriFace, FaceCount);
        EdgeSegStart = Starts(SegEdge, EdgeCount);
    }

    static int[] Starts(int[] owner, int count)
    {
        var s = new int[count + 1];
        int i = 0;
        for (int k = 0; k <= count; k++)
        {
            while (i < owner.Length && owner[i] < k) i++;
            s[k] = i;
        }
        return s;
    }
}

/// <summary>Friendly, exception-throwing facade over the native kernel.</summary>
public static unsafe class Occ
{
    public static string LastError => Marshal.PtrToStringUTF8(Native.ck_last_error()) ?? "";

    internal static Shape Wrap(IntPtr h) => h == IntPtr.Zero ? throw new KernelException(LastError) : new Shape(h);

    static double[] Flat(IReadOnlyList<Vec3> pts)
    {
        var a = new double[pts.Count * 3];
        for (int i = 0; i < pts.Count; i++) { a[3 * i] = pts[i].X; a[3 * i + 1] = pts[i].Y; a[3 * i + 2] = pts[i].Z; }
        return a;
    }
    static double[] V(Vec3 v) => [v.X, v.Y, v.Z];

    // ---- primitives
    public static Shape Box(double dx, double dy, double dz) => Wrap(Native.ck_box(dx, dy, dz));
    public static Shape Cylinder(double r, double h) => Wrap(Native.ck_cylinder(r, h));
    public static Shape Sphere(double r) => Wrap(Native.ck_sphere(r));
    public static Shape Cone(double r1, double r2, double h) => Wrap(Native.ck_cone(r1, r2, h));
    public static Shape Torus(double major, double minor) => Wrap(Native.ck_torus(major, minor));
    public static Shape ThreadGroove(double diameter, double pitch, double depth, bool leftHand = false)
        => Wrap(Native.ck_thread_groove(diameter, pitch, depth, leftHand ? 1 : 0));

    // ---- curves
    public static Shape Line(Vec3 a, Vec3 b) { fixed (double* pa = V(a)) fixed (double* pb = V(b)) return Wrap(Native.ck_line(pa, pb)); }
    public static Shape Circle(Vec3 c, Vec3 n, double r) { fixed (double* pc = V(c)) fixed (double* pn = V(n)) return Wrap(Native.ck_circle(pc, pn, r)); }
    /// <summary>Ellipse with semi-axes major >= minor; xdir is the direction of the major axis.</summary>
    public static Shape Ellipse(Vec3 c, Vec3 n, Vec3 xdir, double major, double minor) { fixed (double* pc = V(c)) fixed (double* pn = V(n)) fixed (double* px = V(xdir)) return Wrap(Native.ck_ellipse(pc, pn, px, major, minor)); }
    public static Shape Arc3(Vec3 a, Vec3 b, Vec3 c) { fixed (double* pa = V(a)) fixed (double* pb = V(b)) fixed (double* pc = V(c)) return Wrap(Native.ck_arc3(pa, pb, pc)); }
    public static Shape Polyline(IReadOnlyList<Vec3> pts, bool closed) { fixed (double* p = Flat(pts)) return Wrap(Native.ck_polyline(p, pts.Count, closed ? 1 : 0)); }
    public static Shape Spline(IReadOnlyList<Vec3> pts, bool closed = false) { fixed (double* p = Flat(pts)) return Wrap(Native.ck_spline(p, pts.Count, closed ? 1 : 0)); }
    /// <summary>Periodic cubic interpolation with identical knot spacing for corresponding Loft sections.</summary>
    public static Shape PeriodicSection(IReadOnlyList<Vec3> pts) { fixed (double* p = Flat(pts)) return Wrap(Native.ck_spline_periodic_uniform(p, pts.Count)); }
    /// <summary>Points and curvature vectors along every edge of a shape (the vector points at the centre of curvature; its length is 1/radius).</summary>
    public static List<(Vec3 p, Vec3 k)[]> CurveSamples(Shape s, int perEdge = 120, int maxEdges = 64)
    {
        var buf = new double[6 * perEdge * maxEdges];
        int n;
        fixed (double* b = buf) n = Native.ck_curve_sample(s.Handle, perEdge, b, maxEdges);
        var res = new List<(Vec3, Vec3)[]>();
        for (int e = 0; e < n; e++)
        {
            var arr = new (Vec3, Vec3)[perEdge];
            for (int i = 0; i < perEdge; i++) { int o = 6 * (e * perEdge + i); arr[i] = (new Vec3(buf[o], buf[o + 1], buf[o + 2]), new Vec3(buf[o + 3], buf[o + 4], buf[o + 5])); }
            res.Add(arr);
        }
        return res;
    }

    /// <summary>Hidden-line projection of a shape. <paramref name="dir"/> is the direction the viewer looks along, <paramref name="xdir"/> the drawing's right.
    /// Kinds: 0 visible sharp edge, 1 visible smooth edge, 2 visible outline, 3 hidden sharp edge, 4 hidden outline. Points are (x, y) in the drawing plane, mm.</summary>
    public static List<(int kind, (double x, double y)[] pts)> Hlr(Shape s, Vec3 dir, Vec3 xdir, double deflection = 0.05)
    {
        for (int cap = 1 << 18; ; cap *= 4)
        {
            var buf = new double[cap];
            int n;
            var dv = V(dir.Normalized()); var xv = V(xdir.Normalized());
            fixed (double* b = buf) fixed (double* pd = dv) fixed (double* px = xv) n = Native.ck_hlr(s.Handle, pd, px, deflection, b, cap);
            if (n == -1) throw new KernelException(LastError);
            if (n < -1) { if (cap > (1 << 26)) throw new InvalidOperationException("The projection is too large."); continue; }
            var res = new List<(int, (double, double)[])>();
            for (int i = 0; i < n;)
            {
                int kind = (int)buf[i], count = (int)buf[i + 1]; i += 2;
                var pts = new (double, double)[count];
                for (int k = 0; k < count; k++) pts[k] = (buf[i + 2 * k], buf[i + 2 * k + 1]);
                i += 2 * count; res.Add((kind, pts));
            }
            return res;
        }
    }

    /// <summary>A cylindrical face: holes, bosses, fillets. Hole = the material is outside the surface.</summary>
    public readonly record struct CylFace(Vec3 Origin, Vec3 Axis, double Radius, double V0, double V1, double Span, bool Hole)
    {
        public bool Full => Span > 2 * Math.PI - 0.05;
        public double Length => V1 - V0;
        public Vec3 Start => Origin + Axis * V0;
        public Vec3 End => Origin + Axis * V1;
    }

    public static List<CylFace> Cylinders(Shape s, int max = 512)
    {
        var buf = new double[11 * max];
        int n; fixed (double* b = buf) n = Native.ck_cylinders(s.Handle, b, max);
        var res = new List<CylFace>();
        for (int i = 0; i < n; i++) { int o = 11 * i; res.Add(new CylFace(new Vec3(buf[o], buf[o + 1], buf[o + 2]), new Vec3(buf[o + 3], buf[o + 4], buf[o + 5]), buf[o + 6], buf[o + 7], buf[o + 8], buf[o + 9], buf[o + 10] > 0.5)); }
        return res;
    }

    public static double[]? CylinderFrame(Shape face)
    {
        var data=new double[14];fixed(double* p=data)return Native.ck_cylinder_frame(face.Handle,p)==0?null:data;
    }
    public static Shape UnwrapSurface(Shape face, double x, double y, out double width, out double height)
    {
        var dims = new double[2]; Shape flat;
        fixed (double* p = dims) flat = Wrap(Native.ck_surface_unwrap(face.Handle, x, y, p));
        width = dims[0]; height = dims[1]; return flat;
    }
    public static Shape WrapSurface(Shape geometry, Shape reference, Shape target, double[] settings)
    {
        if (settings.Length != 10) throw new ArgumentException("Surface mapping needs ten settings.");
        fixed (double* p = settings) return Wrap(Native.ck_surface_wrap(geometry.Handle, reference.Handle, target.Handle, p));
    }
    public static Shape WrapAlongCurve(Shape geometry, Shape reference, Shape target, double[] settings)
    {
        if (settings.Length != 10) throw new ArgumentException("Curve mapping needs ten settings.");
        fixed (double* p = settings) return Wrap(Native.ck_curve_wrap(geometry.Handle, reference.Handle, target.Handle, p));
    }
    public static Vec3[] WireSamples(Shape wire, double tolerance=.01)
    {
        int count=Native.ck_wire_sample(wire.Handle,tolerance,null,0);
        if(count==0)throw new KernelException("Could not sample the flat outline.");
        var data=new double[3*count];fixed(double* p=data)Native.ck_wire_sample(wire.Handle,tolerance,p,count);
        return Enumerable.Range(0,count).Select(i=>new Vec3(data[3*i],data[3*i+1],data[3*i+2])).ToArray();
    }
    public static Shape CylinderRefold(Shape curves,double[] frame,double flatX,double flatY,double tolerance=.00001)
    {if(frame.Length!=14)throw new ArgumentException("Invalid cylindrical flat pattern mapping.");fixed(double* p=frame)return Wrap(Native.ck_cylinder_refold(curves.Handle,p,flatX,flatY,tolerance));}
    public static Shape CylinderRefoldFace(Shape flat,double[] frame,double flatX,double flatY,double tolerance=.00001)
    {if(frame.Length!=14)throw new ArgumentException("Invalid cylindrical flat pattern mapping.");fixed(double* p=frame)return Wrap(Native.ck_cylinder_refold_face(flat.Handle,p,flatX,flatY,tolerance));}

    /// <summary>Untrim a face: mode 0 restores its whole natural surface, mode 1 removes its holes and keeps the outer edge.</summary>
    public static Shape Untrim(Shape face, int mode) => Wrap(Native.ck_untrim(face.Handle, mode));
    /// <summary>The same shape with its normals turned around.</summary>
    public static Shape Reverse(Shape s) => Wrap(Native.ck_reverse(s.Handle));
    /// <summary>Extend the edge of a face that lies nearest to <paramref name="near"/> by <paramref name="length"/>, continuing the surface.</summary>
    public static Shape ExtendFace(Shape face, Vec3 near, double length) { var n = V(near); fixed (double* p = n) return Wrap(Native.ck_extend_face(face.Handle, p, length)); }

    /// <summary>The part of the shape on the negative side of the plane.</summary>
    public static Shape Halve(Shape s, Vec3 origin, Vec3 normal) { fixed (double* o = V(origin)) fixed (double* n = V(normal)) return Wrap(Native.ck_halve(s.Handle, o, n)); }

    public static Shape NurbsCurve(IReadOnlyList<Vec3> poles, int degree = 3) { fixed (double* p = Flat(poles)) return Wrap(Native.ck_nurbs_curve(p, poles.Count, degree)); }

    static IntPtr[] H(IReadOnlyList<Shape> s) => s.Select(x => x.Handle).ToArray();

    public static Shape Profile(IReadOnlyList<Shape> edges, bool faces = true) { fixed (IntPtr* p = H(edges)) return Wrap(Native.ck_make_profile(p, edges.Count, faces ? 1 : 0)); }

    // ---- construction
    public static Shape Extrude(Shape profile, Vec3 v) => Wrap(Native.ck_extrude(profile.Handle, v.X, v.Y, v.Z));
    public static Shape Revolve(Shape profile, Vec3 origin, Vec3 axis, double angleDeg) { fixed (double* o = V(origin)) fixed (double* a = V(axis)) return Wrap(Native.ck_revolve(profile.Handle, o, a, angleDeg)); }
    public static Shape Sweep(Shape profile, Shape path, bool solid) => Wrap(Native.ck_sweep(profile.Handle, path.Handle, solid ? 1 : 0));
    /// <summary>A prism with tapered sides (angle in degrees; positive narrows it as it goes).</summary>
    public static Shape ExtrudeDraft(Shape profile, Vec3 dir, double height, double angleDeg) { var d = V(dir.Normalized()); fixed (double* p = d) return Wrap(Native.ck_extrude_draft(profile.Handle, p, height / Math.Cos(angleDeg * Math.PI / 180), angleDeg * Math.PI / 180)); }     // the kernel's height runs along the slanted side
    /// <summary>Sweep with options: mode 0 follows the path, 1 keeps the profile parallel, 2 keeps a fixed binormal; endScale resizes the profile along the path.</summary>
    public static Shape Sweep2(Shape profile, Shape path, bool solid, int mode, double endScale, Vec3 binormal) { var b = V(binormal); fixed (double* p = b) return Wrap(Native.ck_sweep2(profile.Handle, path.Handle, solid ? 1 : 0, mode, endScale, p)); }
    public static Shape Loft(IReadOnlyList<Shape> sections, bool solid, bool ruled) { fixed (IntPtr* p = H(sections)) return Wrap(Native.ck_loft(p, sections.Count, solid ? 1 : 0, ruled ? 1 : 0)); }
    public static Shape Ruled(Shape a, Shape b) => Wrap(Native.ck_ruled(a.Handle, b.Handle));
    /// <summary>Smooth surface inside the outline edges that also passes close to the given points (e.g. scan data).</summary>
    public static Shape PatchThrough(IReadOnlyList<Shape> edges, IReadOnlyList<Vec3> points, int degree = 3, int ptsOnCurve = 15, int iterations = 3, double tol = 1e-3)
    {
        fixed (IntPtr* p = H(edges)) fixed (double* q = Flat(points)) return Wrap(Native.ck_patch_points(p, edges.Count, q, points.Count, degree, ptsOnCurve, iterations, tol));
    }

    public static Shape Patch(IReadOnlyList<Shape> edges) { fixed (IntPtr* p = H(edges)) return Wrap(Native.ck_patch(p, edges.Count)); }
    public static Shape PatchSupported(Shape source, int[] edges, int order) { fixed (int* p = edges) return Wrap(Native.ck_patch_supported(source.Handle, p, edges.Length, order)); }
    /// <summary>Boundary Surface: one fill over a loop mixing supported edges (host shape + edge index + G0/G1/G2 + weight) and free curves, with optional interior guide curves.</summary>
    public static Shape BoundaryFill(IReadOnlyList<Shape> hosts, int[] edgeIds, int[] orders, double[] weights,
        IReadOnlyList<Shape> freeCurves, IReadOnlyList<Shape> guides)
    {
        var hp = H(hosts); var fp = H(freeCurves); var gp = H(guides);
        fixed (IntPtr* h = hp) fixed (IntPtr* f = fp) fixed (IntPtr* g = gp)
        fixed (int* id = edgeIds) fixed (int* o = orders) fixed (double* w = weights)
            return Wrap(Native.ck_boundary_fill(h, id, o, w, edgeIds.Length, f, freeCurves.Count, g, guides.Count));
    }

    // ---- booleans & modification
    public static Shape Fuse(Shape a, Shape b) => Wrap(Native.ck_fuse(a.Handle, b.Handle));
    public static Shape Cut(Shape a, Shape b) => Wrap(Native.ck_cut(a.Handle, b.Handle));
    public static Shape Common(Shape a, Shape b) => Wrap(Native.ck_common(a.Handle, b.Handle));
    public static Shape Fillet(Shape s, int[] edges, double r) { fixed (int* p = edges) return Wrap(Native.ck_fillet(s.Handle, p, edges.Length, r)); }
    public static Shape Chamfer(Shape s, int[] edges, double d) { fixed (int* p = edges) return Wrap(Native.ck_chamfer(s.Handle, p, edges.Length, d)); }
    public static Shape Shell(Shape s, int[] openFaces, double t) { fixed (int* p = openFaces) return Wrap(Native.ck_shell(s.Handle, p, openFaces.Length, t)); }
    public static Shape Offset(Shape s, double d) => Wrap(Native.ck_offset(s.Handle, d));
    public static Shape Thicken(Shape s, double d) => Wrap(Native.ck_thicken(s.Handle, d));
    public static Shape Sew(IReadOnlyList<Shape> shapes, double tol, bool solid) { fixed (IntPtr* p = H(shapes)) return Wrap(Native.ck_sew(p, shapes.Count, tol, solid ? 1 : 0)); }

    // ---- transforms
    /// <summary>An independent copy of the geometry with no stored triangulation.</summary>
    public static Shape Copy(Shape s) => Wrap(Native.ck_copy(s.Handle));
    public static Shape Translate(Shape s, Vec3 v) => Wrap(Native.ck_translate(s.Handle, v.X, v.Y, v.Z));
    /// <summary>Rotate by the row-major 3x3 matrix m, then move by t.</summary>
    public static Shape Transform(Shape s, double[] m, Vec3 t) { var tt = new[] { t.X, t.Y, t.Z }; fixed (double* pm = m) fixed (double* pt = tt) return Wrap(Native.ck_transform(s.Handle, pm, pt)); }
    public static Shape Compound(IReadOnlyList<Shape> shapes) { fixed (IntPtr* p = H(shapes)) return Wrap(Native.ck_compound(p, shapes.Count)); }
    public static Shape Rotate(Shape s, Vec3 origin, Vec3 axis, double deg) { fixed (double* o = V(origin)) fixed (double* a = V(axis)) return Wrap(Native.ck_rotate(s.Handle, o, a, deg)); }
    public static Shape Scale(Shape s, Vec3 origin, double f) { fixed (double* o = V(origin)) return Wrap(Native.ck_scale(s.Handle, o, f)); }
    public static Shape Mirror(Shape s, Vec3 origin, Vec3 normal) { fixed (double* o = V(origin)) fixed (double* n = V(normal)) return Wrap(Native.ck_mirror(s.Handle, o, n)); }

    // ---- freeform
    /// <summary>Control grid is row-major: grid[row * cols + col].</summary>
    public static Shape NurbsSurface(Vec3[] grid, int rows, int cols, int degU = 3, int degV = 3) { fixed (double* p = Flat(grid)) return Wrap(Native.ck_nurbs_surface(p, rows, cols, degU, degV)); }
    public static Shape FitSurface(Vec3[] grid, int rows, int cols) { fixed (double* p = Flat(grid)) return Wrap(Native.ck_surface_fit(p, rows, cols)); }
    public static Shape ToNurbs(Shape s) => Wrap(Native.ck_nurbs_convert(s.Handle));
    public static Shape MatchSurface(Shape source, int side, Shape reference, int refSide, int continuity, bool reverse = false, double scale = 1)
        => Wrap(Native.ck_surface_match(source.Handle, side, reference.Handle, refSide, continuity, reverse ? 1 : 0, scale));
    public static (double Gap, double RmsGap, double NormalAngle, double CurvatureDifference) SurfaceContinuity(Shape source, int side, Shape reference, int refSide, bool reverse = false, int samples = 51)
    {
        double* values = stackalloc double[4];
        if (Native.ck_surface_continuity(source.Handle, side, reference.Handle, refSide, reverse ? 1 : 0, samples, values) == 0) throw new KernelException(LastError);
        return (values[0], values[1], values[2], values[3]);
    }

    public static ControlNet FaceNet(Shape face)
    {
        int* d = stackalloc int[4];
        if (Native.ck_face_dims(face.Handle, d) == 0) throw new KernelException(LastError);
        var pts = new double[d[0] * d[1] * 3];
        fixed (double* p = pts) Native.ck_face_get_poles(face.Handle, p);
        var v = new Vec3[d[0] * d[1]];
        for (int i = 0; i < v.Length; i++) v[i] = new(pts[3 * i], pts[3 * i + 1], pts[3 * i + 2]);
        return new(v, d[0], d[1], d[2], d[3]);
    }
    public static Shape SetFaceNet(Shape face, Vec3[] poles) { fixed (double* p = Flat(poles)) return Wrap(Native.ck_face_set_poles(face.Handle, p)); }
    public static Shape RefineFace(Shape face) => Wrap(Native.ck_face_refine(face.Handle));
    public static Shape ElevateFace(Shape face) => Wrap(Native.ck_face_elevate(face.Handle));

    public static ControlNet CurveNet(Shape edge)
    {
        int n = Native.ck_curve_count(edge.Handle);
        if (n == 0) throw new KernelException(LastError);
        var pts = new double[n * 3];
        fixed (double* p = pts) Native.ck_curve_get_poles(edge.Handle, p);
        var v = new Vec3[n];
        for (int i = 0; i < n; i++) v[i] = new(pts[3 * i], pts[3 * i + 1], pts[3 * i + 2]);
        return new(v, n, 1, 0, 0);
    }
    public static Shape SetCurveNet(Shape edge, Vec3[] poles) { fixed (double* p = Flat(poles)) return Wrap(Native.ck_curve_set_poles(edge.Handle, p)); }
    public static Shape RefineCurve(Shape edge) => Wrap(Native.ck_curve_refine(edge.Handle));

    // ---- analysis
    public static Shape Vertex(Vec3 p) => Wrap(Native.ck_vertex(p.X, p.Y, p.Z));

    /// <summary>Volume, area, centre of mass and principal moments of inertia (unit density, mm units).</summary>
    public static MassProps Mass(Shape s)
    {
        double* d = stackalloc double[9];
        if (Native.ck_mass(s.Handle, d) == 0) throw new KernelException(LastError);
        return new(d[0], d[1], new(d[2], d[3], d[4]), new(d[5], d[6], d[7]), d[8] > 0.5);
    }

    /// <summary>Shortest distance between two shapes and the closest points on each.</summary>
    public static (double Distance, Vec3 A, Vec3 B) Distance(Shape a, Shape b)
    {
        double* d = stackalloc double[7];
        if (Native.ck_distance(a.Handle, b.Handle, d) == 0) throw new KernelException(LastError);
        return (d[0], new(d[1], d[2], d[3]), new(d[4], d[5], d[6]));
    }

    public static Shape Intersect(Shape a, Shape b) => Wrap(Native.ck_intersect(a.Handle, b.Handle));
    /// <summary>Remove faces (a hole, boss, fillet, pocket...) and heal the surrounding faces over the gap.</summary>
    public static Shape Defeature(Shape s, int[] faces) { fixed (int* p = faces) return Wrap(Native.ck_defeature(s.Handle, p, faces.Length)); }
    /// <summary>Merge neighbouring faces that lie on one surface, only along edges between two of the listed faces; every other edge stays.</summary>
    public static Shape UnifyFaces(Shape s, int[] faces) { fixed (int* p = faces) return Wrap(Native.ck_unify_faces(s.Handle, p, faces.Length)); }
    /// <summary>Offset a planar face or wire inside its own plane (negative = inward). Returns the resulting wires.</summary>
    public static Shape OffsetPlanar(Shape faceOrWire, double dist, bool sharpCorners = false) => Wrap(Native.ck_offset_planar(faceOrWire.Handle, dist, sharpCorners ? 1 : 0));
    /// <summary>Cut a shape into pieces along another shape.</summary>
    public static Shape Draft(Shape s, int[] faces, Vec3 pull, double angleDeg, Vec3 planeOrigin, Vec3 planeNormal) { fixed (int* f = faces) fixed (double* d = V(pull.Normalized())) fixed (double* o = V(planeOrigin)) fixed (double* n = V(planeNormal.Normalized())) return Wrap(Native.ck_draft(s.Handle, f, faces.Length, d, angleDeg * Math.PI / 180, o, n)); }
    public static Shape Split(Shape s, Shape tool) => Wrap(Native.ck_split(s.Handle, tool.Handle));
    /// <summary>The enclosed spaces between faces, shells and solids, as a compound of solids.</summary>
    public static Shape MakeVolumes(IReadOnlyList<Shape> shapes) { fixed (IntPtr* p = H(shapes)) return Wrap(Native.ck_make_volumes(p, shapes.Count)); }
    public static Shape Section(Shape s, Vec3 origin, Vec3 normal) { fixed (double* o = V(origin)) fixed (double* n = V(normal)) return Wrap(Native.ck_section(s.Handle, o, n)); }

    /// <summary>Edge indices (0-based, matching the tessellation) that belong to only one face.</summary>
    public static int[] FreeEdges(Shape s)
    {
        var buf = new int[4096];
        int n;
        fixed (int* p = buf) n = Native.ck_free_edges(s.Handle, p, buf.Length);
        return buf.Take(Math.Min(n, buf.Length)).ToArray();
    }

    public static CheckInfo Check(Shape s)
    {
        double* d = stackalloc double[8];
        if (Native.ck_check(s.Handle, d) == 0) throw new KernelException(LastError);
        return new(d[0] > 0.5, (int)d[1], (int)d[2], (int)d[3], (int)d[4], (int)d[5], (int)d[6]);
    }

    static readonly string[] SurfTypes = ["Plane", "Cylinder", "Cone", "Sphere", "Torus", "Bezier surface", "B-spline surface", "Surface of revolution", "Extrusion surface", "Offset surface", "Other surface"];
    static readonly string[] CurveTypes = ["Line", "Circle", "Ellipse", "Hyperbola", "Parabola", "Bezier curve", "B-spline curve", "Offset curve", "Other curve"];

    public static FaceInfo FaceInfoOf(Shape face)
    {
        double* d = stackalloc double[8];
        if (Native.ck_face_info(face.Handle, d) == 0) throw new KernelException(LastError);
        return new(SurfTypes[Math.Clamp((int)d[0], 0, SurfTypes.Length - 1)], d[1], d[2], d[3], new(d[4], d[5], d[6]));
    }

    /// <summary>Faceted B-rep (one planar face per triangle, sewn). Heavy for big meshes: decimate first.</summary>
    public static Shape FromTriangles(float[] v, int[] t, double tol, bool solid)
    {
        fixed (float* pv = v) fixed (int* pt = t) return Wrap(Native.ck_mesh_to_shape(pv, v.Length / 3, pt, t.Length / 3, tol, solid ? 1 : 0));
    }

    /// <summary>Axis of a cylindrical/conical/toroidal face, a circular edge or a straight edge (planar faces: centroid and normal). Null if the shape has none.</summary>
    public static (Vec3 Origin, Vec3 Dir)? AxisOf(Shape s)
    {
        double* d = stackalloc double[6];
        return Native.ck_axis(s.Handle, d) == 0 ? null : (new Vec3(d[0], d[1], d[2]), new Vec3(d[3], d[4], d[5]));
    }

    public static EdgeInfo EdgeInfoOf(Shape edge)
    {
        double* d = stackalloc double[9];
        if (Native.ck_edge_info(edge.Handle, d) == 0) throw new KernelException(LastError);
        return new(CurveTypes[Math.Clamp((int)d[0], 0, CurveTypes.Length - 1)], d[1], d[2], new(d[3], d[4], d[5]), new(d[6], d[7], d[8]));
    }

    // ---- I/O
    public static void ExportStep(IReadOnlyList<Shape> shapes, string path)
    {
        fixed (IntPtr* p = H(shapes)) if (Native.ck_export_step(p, shapes.Count, path) == 0) throw new KernelException("STEP export failed. " + LastError);
    }
    // ---- NURBS toolkit
    /// <summary>Round (or chamfer) the corners of a flat curve. <paramref name="corners"/> are 0-based corner numbers (null = all). Open curves keep their ends.</summary>
    public static Shape WireFillet(Shape wire, double radius, bool chamfer = false, int[]? corners = null)
    { fixed (int* p = corners ?? [0]) return Wrap(Native.ck_wire_fillet2d(wire.Handle, radius, chamfer ? 1 : 0, p, corners?.Length ?? 0)); }
    public static Shape ProjectCurve(Shape curves, Shape target, Vec3 direction, bool normalToSurface)
    { var d = V(direction); fixed (double* p = d) return Wrap(Native.ck_project_curve(curves.Handle, target.Handle, p, normalToSurface ? 1 : 0)); }
    /// <summary>Points of a face's surface on an nu x nv grid over its parameter range (index i * nv + j).</summary>
    public static Vec3[] SurfaceSamples(Shape face, int nu, int nv)
    {
        var buf = new double[3 * nu * nv]; int n;
        fixed (double* b = buf) n = Native.ck_surface_sample(face.Handle, nu, nv, b);
        if (n != nu * nv) throw new KernelException(LastError);
        var res = new Vec3[n]; for (int i = 0; i < n; i++) res[i] = new Vec3(buf[3 * i], buf[3 * i + 1], buf[3 * i + 2]);
        return res;
    }
    public static Shape Isocurve(Shape face, bool alongV, double fraction) => Wrap(Native.ck_isocurve(face.Handle, alongV ? 1 : 0, fraction));
    public static Shape EdgeSurface(IReadOnlyList<Shape> curves) { fixed (IntPtr* p = H(curves)) return Wrap(Native.ck_edge_surface(p, curves.Count)); }
    public static Shape FilletVariable(Shape s, int[] edges, double r1, double r2) { fixed (int* p = edges) return Wrap(Native.ck_fillet_var(s.Handle, p, edges.Length, r1, r2)); }
    public static Shape SweepRails(Shape profile, Shape rail1, Shape rail2, bool solid) => Wrap(Native.ck_sweep_rails(profile.Handle, rail1.Handle, rail2.Handle, solid ? 1 : 0));
    public static Shape CurveTrim(Shape curve, double from, double to) => Wrap(Native.ck_curve_trim(curve.Handle, from, to));
    public static Shape CurveExtend(Shape curve, bool atEnd, double length) => Wrap(Native.ck_curve_extend(curve.Handle, atEnd ? 1 : 0, length));
    /// <summary>count+1 points evenly spaced along a curve or wire, and its total length.</summary>
    public static (Vec3[] points, double length) CurveDivide(Shape curve, int count)
    {
        var buf = new double[3 * (count + 2)]; double len; int n;
        fixed (double* b = buf) n = Native.ck_curve_divide(curve.Handle, count, b, &len);
        if (n <= 0 && count > 0) throw new KernelException(LastError);
        var pts = new Vec3[n]; for (int i = 0; i < n; i++) pts[i] = new Vec3(buf[3 * i], buf[3 * i + 1], buf[3 * i + 2]);
        return (pts, len);
    }
    public static double CurveLength(Shape curve) { double len; var b = new double[3]; fixed (double* p = b) Native.ck_curve_divide(curve.Handle, 0, p, &len); return len; }
    /// <summary>Samples along an edge of a body or surface: point, unit direction leaving the surface across the edge, and the surface normal.</summary>
    public static (Vec3 p, Vec3 outward, Vec3 normal)[] EdgeFrames(Shape shape, int edge, int count)
    {
        var buf = new double[9 * count]; int n;
        fixed (double* b = buf) n = Native.ck_edge_frames(shape.Handle, edge, count, b);
        if (n != count) throw new KernelException(LastError);
        var res = new (Vec3, Vec3, Vec3)[n];
        for (int i = 0; i < n; i++) res[i] = (new Vec3(buf[9 * i], buf[9 * i + 1], buf[9 * i + 2]), new Vec3(buf[9 * i + 3], buf[9 * i + 4], buf[9 * i + 5]), new Vec3(buf[9 * i + 6], buf[9 * i + 7], buf[9 * i + 8]));
        return res;
    }

    /// <summary>
    /// Surface from an edge of <paramref name="a"/> to an edge of <paramref name="b"/> (0-based edge numbers). order 0 meets both edges (position), 1 also leaves
    /// each surface along its tangent plane (cubic Hermite), 2 additionally has zero bend at both ends (quintic Hermite; exact curvature continuity for flat faces).
    /// <paramref name="fullness"/> scales how far the blend keeps to the surfaces' direction before turning.
    /// </summary>
    public static Shape BlendSurface(Shape a, int edgeA, Shape b, int edgeB, int order, double fullness = 1)
    {
        const int N = 33, M = 13;
        var fa = EdgeFrames(a, edgeA, N); var fb = EdgeFrames(b, edgeB, N);
        double straight = fa[0].p.DistanceTo(fb[0].p) + fa[^1].p.DistanceTo(fb[^1].p), crossed = fa[0].p.DistanceTo(fb[^1].p) + fa[^1].p.DistanceTo(fb[0].p);
        if (crossed < straight) Array.Reverse(fb);
        var grid = new Vec3[N * M];
        for (int i = 0; i < N; i++)
        {
            Vec3 A = fa[i].p, B = fb[i].p, ta = fa[i].outward, tb = fb[i].outward * -1;
            double L = A.DistanceTo(B) * fullness;
            for (int j = 0; j < M; j++)
            {
                double v = j / (M - 1.0), v2 = v * v, v3 = v2 * v, v4 = v3 * v, v5 = v4 * v;
                Vec3 p;
                if (order <= 0) p = A + (B - A) * v;
                else if (order == 1) p = A * (2 * v3 - 3 * v2 + 1) + ta * (L * (v3 - 2 * v2 + v)) + B * (-2 * v3 + 3 * v2) + tb * (L * (v3 - v2));
                else p = A * (1 - 10 * v3 + 15 * v4 - 6 * v5) + ta * (L * (v - 6 * v3 + 8 * v4 - 3 * v5)) + tb * (L * (-4 * v3 + 7 * v4 - 3 * v5)) + B * (10 * v3 - 15 * v4 + 6 * v5);
                grid[i * M + j] = p;
            }
        }
        return FitSurface(grid, N, M);
    }    public static Shape ImportStep(string path) => Wrap(Native.ck_import_step(path));
    public static void ExportIges(IReadOnlyList<Shape> shapes, string path)
    {
        try { fixed (IntPtr* p = H(shapes)) if (Native.ck_export_iges(p, shapes.Count, path) == 0) throw new KernelException("IGES export failed. " + LastError); }
        catch (EntryPointNotFoundException) { throw new KernelException("This build of the kernel has no IGES support yet. Rebuild native/ (build.ps1) to add it."); }
    }
    public static Shape ImportIges(string path)
    {
        try { return Wrap(Native.ck_import_iges(path)); }
        catch (EntryPointNotFoundException) { throw new KernelException("This build of the kernel has no IGES support yet. Rebuild native/ (build.ps1) to add it."); }
    }
    public static void ExportStl(IReadOnlyList<Shape> shapes, string path, double deflection = 0.05)
    {
        fixed (IntPtr* p = H(shapes)) if (Native.ck_export_stl(p, shapes.Count, path, deflection) == 0) throw new KernelException("STL export failed. " + LastError);
    }
    public static Shape FromBrep(string text)
    {
        var b = System.Text.Encoding.ASCII.GetBytes(text);
        fixed (byte* p = b) return Wrap(Native.ck_brep_read(p, b.Length));
    }
}

public readonly record struct MassProps(double Volume, double Area, Vec3 Cog, Vec3 Moments, bool Solid);
public readonly record struct CheckInfo(bool Valid, int Faces, int Edges, int Shells, int Solids, int FreeEdges, int Vertices);
public readonly record struct FaceInfo(string Type, double Area, double R1, double R2, Vec3 Normal);
public readonly record struct EdgeInfo(string Type, double Length, double Radius, Vec3 Start, Vec3 End);

/// <summary>Control points of a NURBS curve (Cols = 1) or surface (row-major, Rows x Cols).</summary>
public sealed record ControlNet(Vec3[] Points, int Rows, int Cols, int DegU, int DegV)
{
    public Vec3 this[int r, int c] => Points[r * Cols + c];
}
