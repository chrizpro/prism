using System.Runtime.InteropServices;

namespace Prism.Kernel;

/// <summary>Raw P/Invoke surface of native/kernel.cpp. Use <see cref="Occ"/> instead.</summary>
internal static unsafe partial class Native
{
    const string Lib = "prism_kernel";

    [DllImport(Lib)] public static extern IntPtr ck_last_error();
    [DllImport(Lib)] public static extern void ck_free(IntPtr h);
    [DllImport(Lib)] public static extern IntPtr ck_clone(IntPtr h);

    [DllImport(Lib)] public static extern IntPtr ck_box(double dx, double dy, double dz);
    [DllImport(Lib)] public static extern IntPtr ck_cylinder(double r, double h);
    [DllImport(Lib)] public static extern IntPtr ck_sphere(double r);
    [DllImport(Lib)] public static extern IntPtr ck_cone(double r1, double r2, double h);
    [DllImport(Lib)] public static extern IntPtr ck_torus(double R, double r);
    [DllImport(Lib)] public static extern IntPtr ck_thread_groove(double diameter, double pitch, double depth, int leftHand);

    [DllImport(Lib)] public static extern IntPtr ck_line(double* a, double* b);
    [DllImport(Lib)] public static extern IntPtr ck_circle(double* c, double* n, double r);
    [DllImport(Lib)] public static extern IntPtr ck_ellipse(double* c, double* n, double* xdir, double major, double minor);
    [DllImport(Lib)] public static extern IntPtr ck_arc3(double* a, double* b, double* c);
    [DllImport(Lib)] public static extern IntPtr ck_polyline(double* xyz, int n, int closed);
    [DllImport(Lib)] public static extern IntPtr ck_spline(double* xyz, int n, int closed);
    [DllImport(Lib)] public static extern IntPtr ck_nurbs_curve(double* xyz, int n, int degree);
    [DllImport(Lib)] public static extern IntPtr ck_make_profile(IntPtr* shapes, int n, int wantFaces);

    [DllImport(Lib)] public static extern IntPtr ck_extrude(IntPtr profile, double dx, double dy, double dz);
    [DllImport(Lib)] public static extern IntPtr ck_revolve(IntPtr profile, double* o, double* axis, double angleDeg);
    [DllImport(Lib)] public static extern IntPtr ck_sweep(IntPtr profile, IntPtr path, int makeSolid);
    [DllImport(Lib)] public static extern IntPtr ck_extrude_draft(IntPtr profile, double* dir, double height, double angle);
    [DllImport(Lib)] public static extern IntPtr ck_sweep2(IntPtr profile, IntPtr path, int makeSolid, int mode, double endScale, double* binormal);
    [DllImport(Lib)] public static extern IntPtr ck_loft(IntPtr* sections, int n, int solid, int ruled);
    [DllImport(Lib)] public static extern IntPtr ck_ruled(IntPtr a, IntPtr b);
    [DllImport(Lib)] public static extern IntPtr ck_patch(IntPtr* edges, int n);
    [DllImport(Lib)] public static extern IntPtr ck_patch_supported(IntPtr source, int* edgeIds, int n, int order);
    [DllImport(Lib)] public static extern IntPtr ck_boundary_fill(IntPtr* hosts, int* ids, int* orders, double* weights, int n, IntPtr* freeCurves, int nf, IntPtr* guides, int ng);
    [DllImport(Lib)] public static extern IntPtr ck_patch_points(IntPtr* edges, int n, double* pts, int m, int degree, int nbPtsOnCur, int nbIter, double tol3d);

    [DllImport(Lib)] public static extern IntPtr ck_fuse(IntPtr a, IntPtr b);
    [DllImport(Lib)] public static extern IntPtr ck_cut(IntPtr a, IntPtr b);
    [DllImport(Lib)] public static extern IntPtr ck_common(IntPtr a, IntPtr b);
    [DllImport(Lib)] public static extern IntPtr ck_fillet(IntPtr s, int* ids, int n, double r);
    [DllImport(Lib)] public static extern IntPtr ck_chamfer(IntPtr s, int* ids, int n, double d);
    [DllImport(Lib)] public static extern IntPtr ck_shell(IntPtr s, int* ids, int n, double t);
    [DllImport(Lib)] public static extern IntPtr ck_offset(IntPtr s, double d);
    [DllImport(Lib)] public static extern IntPtr ck_thicken(IntPtr s, double d);
    [DllImport(Lib)] public static extern IntPtr ck_sew(IntPtr* shapes, int n, double tol, int toSolid);

    [DllImport(Lib)] public static extern IntPtr ck_transform(IntPtr s, double* m, double* t);
    [DllImport(Lib)] public static extern IntPtr ck_compound(IntPtr* shapes, int n);
    [DllImport(Lib)] public static extern IntPtr ck_intersect(IntPtr a, IntPtr b);
    [DllImport(Lib)] public static extern IntPtr ck_defeature(IntPtr s, int* faces, int n);
    [DllImport(Lib)] public static extern IntPtr ck_unify_faces(IntPtr s, int* faces, int n);
    [DllImport(Lib)] public static extern IntPtr ck_offset_planar(IntPtr s, double dist, int join);
    [DllImport(Lib)] public static extern IntPtr ck_split(IntPtr s, IntPtr tool);
    [DllImport(Lib)] public static extern IntPtr ck_draft(IntPtr s, int* faces, int n, double* dir, double angle, double* po, double* pn);
    [DllImport(Lib)] public static extern IntPtr ck_make_volumes(IntPtr* shapes, int n);
    [DllImport(Lib)] public static extern IntPtr ck_copy(IntPtr s);
    [DllImport(Lib)] public static extern IntPtr ck_translate(IntPtr s, double x, double y, double z);
    [DllImport(Lib)] public static extern IntPtr ck_rotate(IntPtr s, double* o, double* axis, double deg);
    [DllImport(Lib)] public static extern IntPtr ck_scale(IntPtr s, double* o, double f);
    [DllImport(Lib)] public static extern IntPtr ck_mirror(IntPtr s, double* o, double* n);

    [DllImport(Lib)] public static extern int ck_count(IntPtr s, int kind);
    [DllImport(Lib)] public static extern IntPtr ck_sub(IntPtr s, int kind, int index);
    [DllImport(Lib)] public static extern int ck_children(IntPtr s, IntPtr* output, int max);
    [DllImport(Lib)] public static extern int ck_shape_type(IntPtr s);
    [DllImport(Lib)] public static extern int ck_is_valid(IntPtr s);
    [DllImport(Lib)] public static extern int ck_props(IntPtr s, double* output);

    [DllImport(Lib)] public static extern IntPtr ck_nurbs_surface(double* xyz, int rows, int cols, int degU, int degV);
    [DllImport(Lib)] public static extern IntPtr ck_surface_fit(double* xyz, int rows, int cols);
    [DllImport(Lib)] public static extern IntPtr ck_nurbs_convert(IntPtr s);
    [DllImport(Lib)] public static extern int ck_face_dims(IntPtr f, int* d);
    [DllImport(Lib)] public static extern IntPtr ck_surface_match(IntPtr source, int side, IntPtr reference, int refSide, int continuity, int reverse, double scale);
    [DllImport(Lib)] public static extern int ck_surface_continuity(IntPtr source, int side, IntPtr reference, int refSide, int reverse, int samples, double* result);
    [DllImport(Lib)] public static extern int ck_face_get_poles(IntPtr f, double* xyz);
    [DllImport(Lib)] public static extern IntPtr ck_face_set_poles(IntPtr f, double* xyz);
    [DllImport(Lib)] public static extern IntPtr ck_face_refine(IntPtr f);
    [DllImport(Lib)] public static extern IntPtr ck_face_elevate(IntPtr f);
    [DllImport(Lib)] public static extern int ck_curve_count(IntPtr e);
    [DllImport(Lib)] public static extern int ck_curve_get_poles(IntPtr e, double* xyz);
    [DllImport(Lib)] public static extern IntPtr ck_curve_set_poles(IntPtr e, double* xyz);
    [DllImport(Lib)] public static extern IntPtr ck_curve_refine(IntPtr e);
    [DllImport(Lib)] public static extern int ck_curve_sample(IntPtr s, int perEdge, double* output, int maxEdges);

    [DllImport(Lib)] public static extern IntPtr ck_untrim(IntPtr face, int mode);
    [DllImport(Lib)] public static extern IntPtr ck_reverse(IntPtr s);
    [DllImport(Lib)] public static extern IntPtr ck_extend_face(IntPtr face, double* nearPt, double length);
    [DllImport(Lib)] public static extern int ck_cylinders(IntPtr s, double* output, int maxFaces);
    [DllImport(Lib)] public static extern int ck_cylinder_frame(IntPtr s, double* output);
    [DllImport(Lib)] public static extern IntPtr ck_cylinder_refold(IntPtr s, double* frame, double flatX, double flatY, double tolerance);
    [DllImport(Lib)] public static extern IntPtr ck_cylinder_refold_face(IntPtr s, double* frame, double flatX, double flatY, double tolerance);
    [DllImport(Lib)] public static extern IntPtr ck_surface_unwrap(IntPtr s, double x, double y, double* dimensions);
    [DllImport(Lib)] public static extern IntPtr ck_surface_wrap(IntPtr s, IntPtr reference, IntPtr target, double* settings);
    [DllImport(Lib)] public static extern IntPtr ck_curve_wrap(IntPtr s, IntPtr reference, IntPtr target, double* settings);
    [DllImport(Lib)] public static extern int ck_wire_sample(IntPtr s, double tolerance, double* output, int capacity);
    [DllImport(Lib)] public static extern IntPtr ck_halve(IntPtr s, double* o, double* n);
    [DllImport(Lib)] public static extern int ck_hlr(IntPtr s, double* dir, double* xdir, double defl, double* output, int maxDoubles);
    [DllImport(Lib)] public static extern IntPtr ck_vertex(double x, double y, double z);
    [DllImport(Lib)] public static extern int ck_mass(IntPtr s, double* output);
    [DllImport(Lib)] public static extern int ck_distance(IntPtr a, IntPtr b, double* output);
    [DllImport(Lib)] public static extern IntPtr ck_section(IntPtr s, double* o, double* n);
    [DllImport(Lib)] public static extern int ck_free_edges(IntPtr s, int* output, int max);
    [DllImport(Lib)] public static extern int ck_check(IntPtr s, double* output);
    [DllImport(Lib)] public static extern int ck_face_info(IntPtr f, double* output);
    [DllImport(Lib)] public static extern int ck_edge_info(IntPtr e, double* output);
    [DllImport(Lib)] public static extern int ck_axis(IntPtr s, double* output);
    [DllImport(Lib)] public static extern IntPtr ck_mesh_to_shape(float* v, int nv, int* t, int nt, double tol, int makeSolid);

    [DllImport(Lib)] public static extern IntPtr ck_mesh_create(IntPtr shape, double lin, double ang);
    [DllImport(Lib)] public static extern void ck_mesh_sizes(IntPtr m, int* nVerts, int* nTris, int* nSegs);
    [DllImport(Lib)] public static extern void ck_mesh_fill(IntPtr m, float* pos, float* nrm, int* idx, int* triFace, float* edgePts, int* segEdge);
    [DllImport(Lib)] public static extern void ck_mesh_free(IntPtr m);

    [DllImport(Lib)] public static extern int ck_export_step(IntPtr* shapes, int n, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern IntPtr ck_import_step([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern IntPtr ck_wire_fillet2d(IntPtr s, double r, int chamfer, int* idx, int n);
    [DllImport(Lib)] public static extern IntPtr ck_project_curve(IntPtr curves, IntPtr target, double* dir, int mode);
    [DllImport(Lib)] public static extern int ck_surface_sample(IntPtr face, int nu, int nv, double* o);
    [DllImport(Lib)] public static extern IntPtr ck_isocurve(IntPtr face, int dir, double t);
    [DllImport(Lib)] public static extern IntPtr ck_edge_surface(IntPtr* edges, int n);
    [DllImport(Lib)] public static extern IntPtr ck_fillet_var(IntPtr s, int* ids, int n, double r1, double r2);
    [DllImport(Lib)] public static extern IntPtr ck_sweep_rails(IntPtr profile, IntPtr rail1, IntPtr rail2, int solid);
    [DllImport(Lib)] public static extern IntPtr ck_curve_trim(IntPtr s, double t0, double t1);
    [DllImport(Lib)] public static extern IntPtr ck_curve_extend(IntPtr s, int atEnd, double length);
    [DllImport(Lib)] public static extern int ck_curve_divide(IntPtr s, int count, double* o, double* length);
    [DllImport(Lib)] public static extern int ck_edge_frames(IntPtr shape, int edgeIdx, int n, double* o);
    [DllImport(Lib)] public static extern IntPtr ck_blend_surface(IntPtr a, int edgeA, IntPtr b, int edgeB, int order);
    [DllImport(Lib)] public static extern int ck_export_iges(IntPtr* shapes, int n, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern IntPtr ck_import_iges([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern int ck_export_stl(IntPtr* shapes, int n, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, double lin);
    [DllImport(Lib)] public static extern int ck_brep_write(IntPtr s, byte* buf, int cap);
    [DllImport(Lib)] public static extern IntPtr ck_brep_read(byte* data, int len);
}
