// Prism kernel shim: a flat C API over Open CASCADE (OCCT) for P/Invoke from .NET.
// Every shape crosses the boundary as an opaque pointer (a heap-allocated TopoDS_Shape).
// All entry points are exception-safe: on failure they return null/0 and set ck_last_error().

#include <string>
#include <sstream>
#include <vector>
#include <algorithm>
#include <cmath>
#include <functional>
#include <BRepTools_Modification.hxx>
#include <BRepTools_Modifier.hxx>
#include <GeomAPI_ProjectPointOnSurf.hxx>
#include <GeomAPI_ProjectPointOnCurve.hxx>
#include <gp_Quaternion.hxx>
#include <Geom_RectangularTrimmedSurface.hxx>
#include <TColStd_HArray1OfReal.hxx>

#include <BRep_Builder.hxx>
#include <GeomLib.hxx>
#include <Geom_BoundedSurface.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <LocOpe_DPrism.hxx>
#include <Law_Linear.hxx>
#include <Law_Function.hxx>
#include <BRepPrimAPI_MakeHalfSpace.hxx>
#include <BRepLProp_SLProps.hxx>
#include <gp_Cylinder.hxx>
#include <HLRBRep_Algo.hxx>
#include <HLRBRep_HLRToShape.hxx>
#include <HLRAlgo_Projector.hxx>
#include <TopExp_Explorer.hxx>
#include <BRep_Tool.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <memory>
#include <Geom2d_Curve.hxx>
#include <GeomAPI.hxx>
#include <Geom2dConvert.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <Geom2d_TrimmedCurve.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <BRepLib.hxx>
#include <ShapeFix_Face.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepAlgoAPI_Section.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepExtrema_DistShapeShape.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GProp_PrincipalProps.hxx>
#include <GeomAbs_CurveType.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <gp_Pln.hxx>
#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_NurbsConvert.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepBuilderAPI_Copy.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepClass_FaceClassifier.hxx>
#include <BRepFill.hxx>
#include <BRepFill_Filling.hxx>
#include <BRepFilletAPI_MakeChamfer.hxx>
#include <BRepFilletAPI_MakeFillet.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepOffset_MakeOffset.hxx>
#include <BRepOffsetAPI_MakeOffsetShape.hxx>
#include <BRepOffsetAPI_MakePipeShell.hxx>
#include <BRepOffsetAPI_MakeThickSolid.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepOffsetAPI_DraftAngle.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepTools.hxx>
#include <Bnd_Box.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <GCPnts_QuasiUniformDeflection.hxx>
#include <GCPnts_TangentialDeflection.hxx>
#include <GProp_GProps.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_Curve.hxx>
#include <Geom_Surface.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom2d_Line.hxx>
#include <BRepLib.hxx>
#include <GeomAPI_Interpolate.hxx>
#include <GeomAPI_PointsToBSplineSurface.hxx>
#include <GeomConvert.hxx>
#include <Poly_Triangulation.hxx>
#include <Precision.hxx>
#include <STEPControl_Reader.hxx>
#include <STEPControl_Writer.hxx>
#include <BRepFilletAPI_MakeFillet2d.hxx>
#include <BRepProj_Projection.hxx>
#include <BRepOffsetAPI_NormalProjection.hxx>
#include <GeomFill_BSplineCurves.hxx>
#include <GeomFill.hxx>
#include <GCPnts_UniformAbscissa.hxx>
#include <BRepAdaptor_CompCurve.hxx>
#include <Geom_BoundedCurve.hxx>
#include <IGESControl_Reader.hxx>
#include <IGESControl_Writer.hxx>
#include <Interface_Static.hxx>
#include <ShapeAnalysis_FreeBounds.hxx>
#include <ShapeUpgrade_UnifySameDomain.hxx>
#include <BRepAlgoAPI_Defeaturing.hxx>
#include <BRepAlgoAPI_Splitter.hxx>
#include <BOPAlgo_MakerVolume.hxx>
#include <TopTools_ListOfShape.hxx>
#include <BRepOffsetAPI_MakeOffset.hxx>
#include <Standard_Failure.hxx>
#include <StlAPI_Writer.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_HArray1OfPnt.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_HSequenceOfShape.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopTools_IndexedDataMapOfShapeListOfShape.hxx>

#include <TopTools_MapOfShape.hxx>

#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Iterator.hxx>
#include <gp_Ax1.hxx>
#include <gp_Ax2.hxx>
#include <gp_Circ.hxx>
#include <gp_Pnt.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Trsf.hxx>
#include <gp_Vec.hxx>
#include <gp_Vec2d.hxx>

#ifdef _WIN32
#define CK_API extern "C" __declspec(dllexport)
#else
#define CK_API extern "C" __attribute__((visibility("default")))
#endif

namespace {

struct H { TopoDS_Shape s; };
thread_local std::string g_err;

void* wrap(const TopoDS_Shape& s) {
    if (s.IsNull()) { if (g_err.empty()) g_err = "Operation produced a null shape."; return nullptr; }
    return new H{ s };
}
const TopoDS_Shape& get(void* h) {
    if (!h) throw Standard_Failure("Shape handle is null (possibly disposed).");
    return static_cast<H*>(h)->s;
}
void fail(const char* m) { g_err = m; }

#define TRY try { g_err.clear();
#define CATCH(ret) } catch (Standard_Failure const& e) { g_err = e.GetMessageString() ? e.GetMessageString() : "OCCT failure"; return ret; } \
                     catch (std::exception const& e) { g_err = e.what(); return ret; } \
                     catch (...) { g_err = "Unknown kernel error"; return ret; }

TopoDS_Shape clean(const TopoDS_Shape& s) {
    try {
        ShapeUpgrade_UnifySameDomain u(s, true, true, true);
        u.Build();
        if (!u.Shape().IsNull()) return u.Shape();
    } catch (...) {}
    return s;
}

TopoDS_Shape transformed(const TopoDS_Shape& s, const gp_Trsf& t) {
    BRepBuilderAPI_Transform x(s, t, true);
    return x.Shape();
}

// Wire for a profile: wire as-is, face -> outer wire, edge -> wire of one edge.
TopoDS_Wire asWire(const TopoDS_Shape& s) {
    switch (s.ShapeType()) {
    case TopAbs_WIRE: return TopoDS::Wire(s);
    case TopAbs_FACE: return BRepTools::OuterWire(TopoDS::Face(s));
    case TopAbs_EDGE: return BRepBuilderAPI_MakeWire(TopoDS::Edge(s)).Wire();
    default: break;
    }
    TopExp_Explorer ex(s, TopAbs_WIRE);
    if (ex.More()) return TopoDS::Wire(ex.Current());
    TopExp_Explorer ee(s, TopAbs_EDGE);
    if (ee.More()) return BRepBuilderAPI_MakeWire(TopoDS::Edge(ee.Current())).Wire();
    throw Standard_Failure("Shape has no usable wire.");
}

void clampedKnots(int n, int deg, TColStd_Array1OfReal& k, TColStd_Array1OfInteger& m) {
    int spans = n - deg;
    for (int i = 0; i <= spans; ++i) {
        k(i + 1) = i;
        m(i + 1) = (i == 0 || i == spans) ? deg + 1 : 1;
    }
}

Handle(Geom_BSplineSurface) faceSurface(const TopoDS_Shape& s) {
    TopoDS_Shape f = s;
    if (f.ShapeType() != TopAbs_FACE) {
        TopExp_Explorer ex(f, TopAbs_FACE);
        if (!ex.More()) throw Standard_Failure("Shape has no face.");
        f = ex.Current();
    }
    TopLoc_Location loc;
    Handle(Geom_Surface) surf = BRep_Tool::Surface(TopoDS::Face(f), loc);
    if (surf.IsNull()) throw Standard_Failure("Face has no surface.");
    Handle(Geom_BSplineSurface) bs = Handle(Geom_BSplineSurface)::DownCast(surf);
    if (bs.IsNull()) {
        BRepBuilderAPI_NurbsConvert nc(f, true);
        TopExp_Explorer ex(nc.Shape(), TopAbs_FACE);
        TopLoc_Location l2;
        bs = Handle(Geom_BSplineSurface)::DownCast(BRep_Tool::Surface(TopoDS::Face(ex.Current()), l2));
        if (bs.IsNull()) throw Standard_Failure("Cannot convert face to NURBS.");
        bs = Handle(Geom_BSplineSurface)::DownCast(bs->Copy());
        if (!l2.IsIdentity()) bs->Transform(l2.Transformation());
        return bs;
    }
    bs = Handle(Geom_BSplineSurface)::DownCast(bs->Copy());
    if (!loc.IsIdentity()) bs->Transform(loc.Transformation());
    return bs;
}

Handle(Geom_BSplineCurve) edgeCurve(const TopoDS_Shape& s) {
    TopoDS_Shape e = s;
    if (e.ShapeType() != TopAbs_EDGE) {
        TopExp_Explorer ex(e, TopAbs_EDGE);
        if (!ex.More()) throw Standard_Failure("Shape has no edge.");
        e = ex.Current();
    }
    TopLoc_Location loc;
    double a, b;
    Handle(Geom_Curve) c = BRep_Tool::Curve(TopoDS::Edge(e), loc, a, b);
    if (c.IsNull()) throw Standard_Failure("Edge has no curve.");
    Handle(Geom_BSplineCurve) bs = Handle(Geom_BSplineCurve)::DownCast(c);
    if (bs.IsNull()) {
        Handle(Geom_Curve) t = new Geom_TrimmedCurve(c, a, b);
        bs = GeomConvert::CurveToBSplineCurve(t);
    }
    bs = Handle(Geom_BSplineCurve)::DownCast(bs->Copy());
    if (!loc.IsIdentity()) bs->Transform(loc.Transformation());
    return bs;
}

struct Mesh {
    std::vector<float> pos, nrm, edgePts;
    std::vector<int> idx, triFace, segEdge;
};

} // namespace

// ---------------------------------------------------------------- basics
CK_API const char* ck_last_error() { return g_err.c_str(); }
CK_API void ck_free(void* h) { delete static_cast<H*>(h); }
CK_API void* ck_clone(void* h) { TRY return h ? wrap(get(h)) : nullptr; CATCH(nullptr) }

// ---------------------------------------------------------------- primitives (base on XY plane, centered on origin)
CK_API void* ck_box(double dx, double dy, double dz) {
    TRY return wrap(BRepPrimAPI_MakeBox(gp_Ax2(gp_Pnt(-dx / 2, -dy / 2, 0), gp::DZ()), dx, dy, dz).Shape()); CATCH(nullptr)
}
CK_API void* ck_cylinder(double r, double h) {
    TRY return wrap(BRepPrimAPI_MakeCylinder(r, h).Shape()); CATCH(nullptr)
}
CK_API void* ck_sphere(double r) {
    TRY return wrap(BRepPrimAPI_MakeSphere(r).Shape()); CATCH(nullptr)
}
CK_API void* ck_cone(double r1, double r2, double h) {
    TRY return wrap(BRepPrimAPI_MakeCone(r1, r2, h).Shape()); CATCH(nullptr)
}
CK_API void* ck_torus(double R, double r) {
    TRY return wrap(BRepPrimAPI_MakeTorus(R, r).Shape()); CATCH(nullptr)
}

// ---------------------------------------------------------------- curves / sketch entities
CK_API void* ck_line(const double* a, const double* b) {
    TRY return wrap(BRepBuilderAPI_MakeEdge(gp_Pnt(a[0], a[1], a[2]), gp_Pnt(b[0], b[1], b[2])).Edge()); CATCH(nullptr)
}
CK_API void* ck_circle(const double* c, const double* n, double r) {
    TRY return wrap(BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(gp_Pnt(c[0], c[1], c[2]), gp_Dir(n[0], n[1], n[2])), r)).Edge()); CATCH(nullptr)
}
CK_API void* ck_ellipse(const double* c, const double* n, const double* xdir, double major, double minor) {
    TRY return wrap(BRepBuilderAPI_MakeEdge(gp_Elips(gp_Ax2(gp_Pnt(c[0], c[1], c[2]), gp_Dir(n[0], n[1], n[2]), gp_Dir(xdir[0], xdir[1], xdir[2])), major, minor)).Edge()); CATCH(nullptr)
}
CK_API void* ck_arc3(const double* a, const double* b, const double* c) {
    TRY
        GC_MakeArcOfCircle arc(gp_Pnt(a[0], a[1], a[2]), gp_Pnt(b[0], b[1], b[2]), gp_Pnt(c[0], c[1], c[2]));
        if (!arc.IsDone()) { fail("Three points are collinear; cannot make an arc."); return nullptr; }
        return wrap(BRepBuilderAPI_MakeEdge(arc.Value()).Edge());
    CATCH(nullptr)
}
CK_API void* ck_polyline(const double* xyz, int n, int closed) {
    TRY
        BRepBuilderAPI_MakePolygon p;
        for (int i = 0; i < n; ++i) p.Add(gp_Pnt(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]));
        if (closed) p.Close();
        return wrap(p.Wire());
    CATCH(nullptr)
}
#include <math_Gauss.hxx>
#include <math_Matrix.hxx>
#include <math_Vector.hxx>
// Closed cubic curve through the points, periodic with a knot at every point (chord-length parameters, closing chord included).
// OCCT's own periodic interpolation comes out lopsided, so the pole system is solved here.
static Handle(Geom_BSplineCurve) periodic_spline(const std::vector<gp_Pnt>& q, bool uniform = false) {
    const int n = (int)q.size();
    std::vector<double> t(n + 1, 0.0);
    for (int i = 0; i < n; ++i) t[i + 1] = t[i] + (uniform ? 1.0 : std::max(q[i].Distance(q[(i + 1) % n]), 1e-9));
    TColStd_Array1OfReal knots(1, n + 1);
    TColStd_Array1OfInteger mults(1, n + 1);
    for (int i = 0; i <= n; ++i) { knots(i + 1) = t[i]; mults(i + 1) = 1; }
    auto basis = [&](int j) {
        TColgp_Array1OfPnt poles(1, n);
        for (int k = 0; k < n; ++k) poles(k + 1) = gp_Pnt(k == j ? 1.0 : 0.0, 0, 0);
        return new Geom_BSplineCurve(poles, knots, mults, 3, true);
    };
    math_Matrix a(1, n, 1, n);
    for (int j = 0; j < n; ++j) { Handle(Geom_BSplineCurve) c = basis(j); for (int i = 0; i < n; ++i) a(i + 1, j + 1) = c->Value(t[i]).X(); }
    math_Gauss solver(a);
    if (!solver.IsDone()) return nullptr;
    math_Vector bx(1, n), by(1, n), bz(1, n), x(1, n), y(1, n), z(1, n);
    for (int i = 0; i < n; ++i) { bx(i + 1) = q[i].X(); by(i + 1) = q[i].Y(); bz(i + 1) = q[i].Z(); }
    solver.Solve(bx, x); solver.Solve(by, y); solver.Solve(bz, z);
    TColgp_Array1OfPnt poles(1, n);
    for (int k = 0; k < n; ++k) poles(k + 1) = gp_Pnt(x(k + 1), y(k + 1), z(k + 1));
    return new Geom_BSplineCurve(poles, knots, mults, 3, true);
}
// Curve interpolating the points (cubic NURBS).
CK_API void* ck_spline(const double* xyz, int n, int closed) {
    TRY
        if (n < 2) { fail("A spline needs at least two points."); return nullptr; }
        Handle(TColgp_HArray1OfPnt) pts = new TColgp_HArray1OfPnt(1, n);
        for (int i = 0; i < n; ++i) pts->SetValue(i + 1, gp_Pnt(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]));
        if (closed && n >= 3) {
            std::vector<gp_Pnt> q; for (int i = 0; i < n; ++i) q.push_back(pts->Value(i + 1));
            Handle(Geom_BSplineCurve) c = periodic_spline(q);
            if (!c.IsNull()) return wrap(BRepBuilderAPI_MakeEdge(c).Edge());
        }
        GeomAPI_Interpolate it(pts, closed != 0, 1e-6);
        it.Perform();
        if (!it.IsDone()) { fail("Spline interpolation failed."); return nullptr; }
        return wrap(BRepBuilderAPI_MakeEdge(it.Curve()).Edge());
    CATCH(nullptr)
}
// Control-point curve with a clamped uniform knot vector.
// Matching knots keep corresponding Loft sections compatible without a large
// union of slightly different chord-length knot sequences.
CK_API void* ck_spline_periodic_uniform(const double* xyz, int n) {
    TRY
        if (n < 4) { fail("A periodic section needs at least four points."); return nullptr; }
        std::vector<gp_Pnt> q;
        for (int i = 0; i < n; ++i) q.emplace_back(xyz[3*i], xyz[3*i+1], xyz[3*i+2]);
        Handle(Geom_BSplineCurve) c = periodic_spline(q, true);
        if (c.IsNull()) { fail("Periodic section interpolation failed."); return nullptr; }
        return wrap(BRepBuilderAPI_MakeEdge(c).Edge());
    CATCH(nullptr)
}
CK_API void* ck_nurbs_curve(const double* xyz, int n, int degree) {
    TRY
        int deg = std::min(degree, n - 1);
        if (deg < 1) { fail("Need at least two control points."); return nullptr; }
        TColgp_Array1OfPnt poles(1, n);
        for (int i = 0; i < n; ++i) poles(i + 1) = gp_Pnt(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]);
        int spans = n - deg;
        TColStd_Array1OfReal k(1, spans + 1);
        TColStd_Array1OfInteger m(1, spans + 1);
        clampedKnots(n, deg, k, m);
        Handle(Geom_BSplineCurve) c = new Geom_BSplineCurve(poles, k, m, deg);
        return wrap(BRepBuilderAPI_MakeEdge(c).Edge());
    CATCH(nullptr)
}

// Collapse loose edges/wires into a profile: closed loops become planar faces (nested loops become holes),
// open chains stay as wires. Returns a compound.
CK_API void* ck_make_profile(void** shapes, int n, int wantFaces) {
    TRY
        Handle(TopTools_HSequenceOfShape) edges = new TopTools_HSequenceOfShape();
        for (int i = 0; i < n; ++i)
            for (TopExp_Explorer ex(get(shapes[i]), TopAbs_EDGE); ex.More(); ex.Next()) edges->Append(ex.Current());
        Handle(TopTools_HSequenceOfShape) wires;
        ShapeAnalysis_FreeBounds::ConnectEdgesToWires(edges, 1e-4, false, wires);
        BRep_Builder bb;
        TopoDS_Compound out;
        bb.MakeCompound(out);
        struct Loop { TopoDS_Face face; double area; int depth = 0; int parent = -1; };
        std::vector<Loop> loops;
        for (int i = 1; i <= wires->Length(); ++i) {
            TopoDS_Wire w = TopoDS::Wire(wires->Value(i));
            if (wantFaces && BRep_Tool::IsClosed(w)) {
                BRepBuilderAPI_MakeFace mf(w, true);
                if (mf.IsDone()) {
                    GProp_GProps gp; BRepGProp::SurfaceProperties(mf.Face(), gp);
                    loops.push_back({ mf.Face(), gp.Mass() });
                    continue;
                }
            }
            bb.Add(out, w);
        }
        auto firstVertex = [](const TopoDS_Face& f) {
            TopExp_Explorer ex(f, TopAbs_VERTEX);
            return BRep_Tool::Pnt(TopoDS::Vertex(ex.Current()));
        };
        for (size_t i = 0; i < loops.size(); ++i) {
            double best = 1e300;
            gp_Pnt p = firstVertex(loops[i].face);
            for (size_t j = 0; j < loops.size(); ++j) {
                if (i == j || loops[j].area <= loops[i].area) continue;
                BRepClass_FaceClassifier fc(loops[j].face, p, 1e-6);
                if (fc.State() == TopAbs_IN) {
                    loops[i].depth++;
                    if (loops[j].area < best) { best = loops[j].area; loops[i].parent = (int)j; }
                }
            }
        }
        for (size_t i = 0; i < loops.size(); ++i) {
            if (loops[i].depth % 2) continue;
            TopoDS_Shape f = loops[i].face;
            for (size_t j = 0; j < loops.size(); ++j)
                if (loops[j].depth == loops[i].depth + 1 && loops[j].parent == (int)i)
                    f = BRepAlgoAPI_Cut(f, loops[j].face).Shape();
            bb.Add(out, f);
        }
        return wrap(out);
    CATCH(nullptr)
}

// ---------------------------------------------------------------- shape construction
CK_API void* ck_extrude(void* profile, double dx, double dy, double dz) {
    TRY return wrap(BRepPrimAPI_MakePrism(get(profile), gp_Vec(dx, dy, dz)).Shape()); CATCH(nullptr)
}
CK_API void* ck_revolve(void* profile, const double* o, const double* axis, double angleDeg) {
    TRY
        gp_Ax1 ax(gp_Pnt(o[0], o[1], o[2]), gp_Dir(axis[0], axis[1], axis[2]));
        double a = angleDeg * M_PI / 180.0;
        if (std::abs(angleDeg) >= 359.999) return wrap(BRepPrimAPI_MakeRevol(get(profile), ax).Shape());
        return wrap(BRepPrimAPI_MakeRevol(get(profile), ax, a).Shape());
    CATCH(nullptr)
}
CK_API void* ck_sweep(void* profile, void* path, int makeSolid) {
    TRY
        BRepOffsetAPI_MakePipeShell ps(asWire(get(path)));
        ps.SetMode(false); // corrected Frenet
        ps.Add(asWire(get(profile)));
        ps.Build();
        if (!ps.IsDone()) { fail("Sweep failed."); return nullptr; }
        if (makeSolid) ps.MakeSolid();
        return wrap(ps.Shape());
    CATCH(nullptr)
}
// A truncated 60-degree internal-thread groove, along local +Z. The bore is cut separately.
CK_API void* ck_thread_groove(double diameter, double pitch, double depth, int leftHand) {
    TRY
        if (!std::isfinite(diameter) || !std::isfinite(pitch) || !std::isfinite(depth) ||
            pitch <= 0 || diameter <= 1.082532 * pitch || depth <= 0 || depth / pitch > 200) {
            fail("Invalid thread dimensions (maximum 200 turns)."); return nullptr;
        }
        const double pi = 3.14159265358979323846;
        double major = diameter / 2, minor = major - .541266 * pitch;
        double z0 = -pitch, hand = leftHand ? -1.0 : 1.0;
        Handle(Geom_CylindricalSurface) surface = new Geom_CylindricalSurface(gp_Ax3(gp::XOY()), minor);
        gp_Dir2d slope(hand * 2 * pi, pitch);
        Handle(Geom2d_Line) line = new Geom2d_Line(gp_Pnt2d(0, z0), slope);
        double length = (depth + 2 * pitch) * std::sqrt(4 * pi * pi + pitch * pitch) / pitch;
        TopoDS_Edge edge = BRepBuilderAPI_MakeEdge(line, surface, 0, length).Edge();
        BRepLib::BuildCurves3d(edge);
        TopoDS_Wire spine = BRepBuilderAPI_MakeWire(edge).Wire();
        // Slightly overlap the bore so the Boolean has no coincident inner faces.
        BRepBuilderAPI_MakePolygon polygon;
        polygon.Add(gp_Pnt(minor - .01 * pitch, 0, z0 - .375 * pitch));
        polygon.Add(gp_Pnt(major, 0, z0 - .0625 * pitch));
        polygon.Add(gp_Pnt(major, 0, z0 + .0625 * pitch));
        polygon.Add(gp_Pnt(minor - .01 * pitch, 0, z0 + .375 * pitch));
        polygon.Close();
        BRepOffsetAPI_MakePipeShell pipe(spine);
        pipe.SetMode(true); // Frenet frame keeps the section radial along the helix.
        pipe.Add(polygon.Wire()); pipe.Build();
        if (!pipe.IsDone() || !pipe.MakeSolid()) { fail("Thread sweep failed."); return nullptr; }
        BRepAlgoAPI_Common clip(pipe.Shape(), BRepPrimAPI_MakeCylinder(major + pitch, depth).Shape());
        clip.Build();
        if (!clip.IsDone() || !BRepCheck_Analyzer(clip.Shape()).IsValid()) { fail("Thread groove is invalid."); return nullptr; }
        return wrap(clip.Shape());
    CATCH(nullptr)
}
CK_API void* ck_loft(void** sections, int n, int solid, int ruled) {
    TRY
        BRepOffsetAPI_ThruSections ts(solid != 0, ruled != 0);
        for (int i = 0; i < n; ++i) {
            const TopoDS_Shape& s = get(sections[i]);
            if (s.ShapeType() == TopAbs_VERTEX) ts.AddVertex(TopoDS::Vertex(s));
            else ts.AddWire(asWire(s));
        }
        ts.Build();
        if (!ts.IsDone()) { fail("Loft failed."); return nullptr; }
        return wrap(ts.Shape());
    CATCH(nullptr)
}
CK_API void* ck_ruled(void* a, void* b) {
    TRY
        TopoDS_Shape sa = get(a), sb = get(b);
        if (sa.ShapeType() == TopAbs_EDGE && sb.ShapeType() == TopAbs_EDGE)
            return wrap(BRepFill::Face(TopoDS::Edge(sa), TopoDS::Edge(sb)));
        return wrap(BRepFill::Shell(asWire(sa), asWire(sb)));
    CATCH(nullptr)
}
// Fill a boundary (loop of edges) with a smooth patch.
CK_API void* ck_patch(void** edges, int n) {
    TRY
        BRepFill_Filling f;
        for (int i = 0; i < n; ++i)
            for (TopExp_Explorer ex(get(edges[i]), TopAbs_EDGE); ex.More(); ex.Next())
                f.Add(TopoDS::Edge(ex.Current()), GeomAbs_C0);
        f.Build();
        if (!f.IsDone()) { fail("Could not fill the boundary."); return nullptr; }
        // A plate surface over a large curved opening can balloon far outside its own outline and still be "valid". Refuse that instead of handing back a wrong surface.
        Bnd_Box outline, patch;
        for (int i = 0; i < n; ++i) BRepBndLib::Add(get(edges[i]), outline);
        BRepBndLib::Add(f.Face(), patch);
        double ox0, oy0, oz0, ox1, oy1, oz1, px0, py0, pz0, px1, py1, pz1;
        outline.Get(ox0, oy0, oz0, ox1, oy1, oz1); patch.Get(px0, py0, pz0, px1, py1, pz1);
        double diag = std::sqrt((ox1 - ox0) * (ox1 - ox0) + (oy1 - oy0) * (oy1 - oy0) + (oz1 - oz0) * (oz1 - oz0));
        double minDim = std::min({ ox1 - ox0, oy1 - oy0, oz1 - oz0 });
        double over = std::max({ ox0 - px0, oy0 - py0, oz0 - pz0, px1 - ox1, py1 - oy1, pz1 - oz1, 0.0 });
        if (over > 0.2 * minDim + 1e-3 * diag) {
            fail("The filled surface bulges far outside its outline. Split the opening into smaller parts, or build the cap from curves with Patch.");
            return nullptr;
        }
        return wrap(f.Face());
    CATCH(nullptr)
}

// Fill an opening while constraining its edges to their supporting faces.
CK_API void* ck_patch_supported(void* source, const int* edgeIds, int n, int order) {
    TRY
        if (order != 1 || n < 1) { fail("Opening fill supports tangent continuity only."); return nullptr; }
        TopTools_IndexedMapOfShape edges; TopExp::MapShapes(get(source), TopAbs_EDGE, edges);
        TopTools_IndexedDataMapOfShapeListOfShape faces;
        TopExp::MapShapesAndAncestors(get(source), TopAbs_EDGE, TopAbs_FACE, faces);
        BRepFill_Filling f(3, 15, 3, Standard_False, 1e-5, 1e-4, 0.01, 0.1, 8, 9);
        for (int i = 0; i < n; ++i) {
            if (edgeIds[i] < 0 || edgeIds[i] >= edges.Extent()) { fail("Edge index out of range."); return nullptr; }
            TopoDS_Edge edge = TopoDS::Edge(edges(edgeIds[i] + 1));
            if (!faces.Contains(edge) || faces.FindFromKey(edge).Extent() != 1) {
                fail("Each opening edge must have exactly one supporting face."); return nullptr;
            }
            f.Add(edge, TopoDS::Face(faces.FindFromKey(edge).First()), GeomAbs_G1);
        }
        f.Build();
        if (!f.IsDone() || !BRepCheck_Analyzer(f.Face()).IsValid()) { fail("Could not fill the opening with the requested continuity."); return nullptr; }
        return wrap(f.Face());
    CATCH(nullptr)
}

// Points just inside a supporting face along an edge. They pull the fill toward the face's shape:
// the deeper they sit, the stronger the fill hugs the neighbour (the "weight" of the boundary edge).
static void addInteriorPoints(const TopoDS_Edge& edge, const TopoDS_Face& face, double w, std::vector<gp_Pnt>& out) {
    Standard_Real fp, lp;
    Handle(Geom2d_Curve) pc = BRep_Tool::CurveOnSurface(edge, face, fp, lp);
    Handle(Geom_Surface) base = BRep_Tool::Surface(face);
    if (pc.IsNull() || base.IsNull()) return;
    Handle(Geom_Surface) surf = Handle(Geom_Surface)::DownCast(base->Transformed(face.Location().Transformation()));
    if (surf.IsNull()) return;
    BRepAdaptor_Curve ac(edge);
    double depth = w * 0.15 * GCPnts_AbscissaPoint::Length(ac);
    const int M = 9;
    for (int i = 0; i < M; ++i) {
        double t = fp + (lp - fp) * (i + 0.5) / M;
        gp_Pnt2d uv; gp_Vec2d tg;
        pc->D1(t, uv, tg);
        if (tg.Magnitude() < 1e-9) continue;
        tg.Normalize();
        gp_Pnt pu; gp_Vec d1u, d1v;
        surf->D1(uv.X(), uv.Y(), pu, d1u, d1v);
        bool done = false;
        for (int side = 0; side < 2 && !done; ++side) {
            gp_Vec2d inward = side == 0 ? gp_Vec2d(-tg.Y(), tg.X()) : gp_Vec2d(tg.Y(), -tg.X());
            gp_Vec step3 = d1u.Multiplied(inward.X()) + d1v.Multiplied(inward.Y());
            double scale = step3.Magnitude();
            if (scale < 1e-9) continue;
            double h = depth;
            for (int r = 0; r < 6; ++r) {
                gp_Pnt2d p2 = uv.Translated(inward * (h / scale));
                BRepClass_FaceClassifier cls(face, p2, Precision::Confusion());
                if (cls.State() == TopAbs_IN) { out.push_back(surf->Value(p2.X(), p2.Y())); done = true; break; }
                h *= 0.5;
            }
        }
    }
}

// Fill one boundary loop made of surface edges and free curves (Boundary Surface).
// hosts/ids/orders/weights[i]: edge ids[i] of shape hosts[i]; orders 0/1/2 constrain it to its single
// supporting face (position/tangent/curvature) and weights[i] adds pull toward that face. An edge with
// no unique supporting face falls back to position only. freeCurves: wire/edge shapes whose edges join
// the boundary at C0. guides: curves inside the outline the surface should pass through.
CK_API void* ck_boundary_fill(void** hosts, const int* ids, const int* orders, const double* weights, int n,
                              void** freeCurves, int nf, void** guides, int ng) {
    TRY
        if (n < 1 && nf < 1) { fail("Pick the edges or curves that bound the surface."); return nullptr; }
        BRepFill_Filling f(3, 15, 3, Standard_False, 1e-5, 1e-4, 0.01, 0.1, 8, 9);
        std::vector<gp_Pnt> inside;
        for (int i = 0; i < n; ++i) {
            TopTools_IndexedMapOfShape em; TopExp::MapShapes(get(hosts[i]), TopAbs_EDGE, em);
            if (ids[i] < 0 || ids[i] >= em.Extent()) { fail("Edge index out of range."); return nullptr; }
            TopoDS_Edge edge = TopoDS::Edge(em(ids[i] + 1));
            int order = orders == nullptr ? 0 : orders[i];
            if (order <= 0) { f.Add(edge, GeomAbs_C0); continue; }
            TopTools_IndexedDataMapOfShapeListOfShape af;
            TopExp::MapShapesAndAncestors(get(hosts[i]), TopAbs_EDGE, TopAbs_FACE, af);
            if (!af.Contains(edge) || af.FindFromKey(edge).Extent() != 1) { f.Add(edge, GeomAbs_C0); continue; }
            TopoDS_Face face = TopoDS::Face(af.FindFromKey(edge).First());
            // OCCT's GeomPlate order ints are 0/1/2 = G0/G1/G2 but BRepFill_Filling forwards the raw
            // GeomAbs enum: GeomAbs_G2 (3) is rejected, so G2 is requested as GeomAbs_C1 (value 2).
            f.Add(edge, face, order == 1 ? GeomAbs_G1 : GeomAbs_C1);
            double w = weights == nullptr ? 0 : weights[i];
            if (w > 0.001) addInteriorPoints(edge, face, w, inside);
        }
        for (int i = 0; i < nf; ++i)
            for (TopExp_Explorer ex(get(freeCurves[i]), TopAbs_EDGE); ex.More(); ex.Next())
                f.Add(TopoDS::Edge(ex.Current()), GeomAbs_C0);
        for (int i = 0; i < ng; ++i)
            for (TopExp_Explorer ex(get(guides[i]), TopAbs_EDGE); ex.More(); ex.Next())
                f.Add(TopoDS::Edge(ex.Current()), GeomAbs_C0, Standard_False);
        for (const gp_Pnt& p : inside) f.Add(p);
        f.Build();
        if (!f.IsDone() || !BRepCheck_Analyzer(f.Face()).IsValid()) { fail("Could not fill the boundary with the requested continuity."); return nullptr; }
        return wrap(f.Face());
    CATCH(nullptr)
}


// Plate surface bounded by the given edges and pulled through extra points (a smooth fit to scanned data).
CK_API void* ck_patch_points(void** edges, int n, const double* pts, int m, int degree, int nbPtsOnCur, int nbIter, double tol3d) {
    TRY
        BRepFill_Filling f(degree, nbPtsOnCur, nbIter, false, 1e-5, tol3d);
        for (int i = 0; i < n; ++i)
            for (TopExp_Explorer ex(get(edges[i]), TopAbs_EDGE); ex.More(); ex.Next())
                f.Add(TopoDS::Edge(ex.Current()), GeomAbs_C0);
        for (int i = 0; i < m; ++i) f.Add(gp_Pnt(pts[3*i], pts[3*i+1], pts[3*i+2]));
        f.Build();
        if (!f.IsDone()) { fail("Could not fit a surface through the outline."); return nullptr; }
        return wrap(f.Face());
    CATCH(nullptr)
}

// ---------------------------------------------------------------- booleans & modification
static void* boolResult(BRepAlgoAPI_BooleanOperation& op) {
    op.Build();
    if (!op.IsDone() || op.HasErrors()) { fail("Boolean operation failed."); return nullptr; }
    return wrap(clean(op.Shape()));
}
CK_API void* ck_fuse(void* a, void* b)   { TRY BRepAlgoAPI_Fuse op(get(a), get(b));   return boolResult(op); CATCH(nullptr) }
CK_API void* ck_cut(void* a, void* b)    { TRY BRepAlgoAPI_Cut op(get(a), get(b));    return boolResult(op); CATCH(nullptr) }
CK_API void* ck_common(void* a, void* b) { TRY BRepAlgoAPI_Common op(get(a), get(b)); return boolResult(op); CATCH(nullptr) }

CK_API void* ck_fillet(void* s, const int* edgeIds, int n, double r) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_EDGE, m);
        BRepFilletAPI_MakeFillet mk(get(s));
        for (int i = 0; i < n; ++i) mk.Add(r, TopoDS::Edge(m(edgeIds[i] + 1)));
        mk.Build();
        if (!mk.IsDone()) { fail("Fillet failed. Try a smaller radius."); return nullptr; }
        if (!BRepCheck_Analyzer(mk.Shape()).IsValid()) { fail("Fillet produced invalid geometry. Try a smaller radius or different edges."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
CK_API void* ck_chamfer(void* s, const int* edgeIds, int n, double d) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_EDGE, m);
        BRepFilletAPI_MakeChamfer mk(get(s));
        for (int i = 0; i < n; ++i) mk.Add(d, TopoDS::Edge(m(edgeIds[i] + 1)));
        mk.Build();
        if (!mk.IsDone()) { fail("Chamfer failed. Try a smaller distance."); return nullptr; }
        if (!BRepCheck_Analyzer(mk.Shape()).IsValid()) { fail("Chamfer produced invalid geometry. Try a smaller distance or different edges."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
// Hollow a solid; the listed faces are removed (opened). Positive thickness grows outward.
CK_API void* ck_shell(void* s, const int* faceIds, int n, double thickness) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_FACE, m);
        TopTools_ListOfShape faces;
        for (int i = 0; i < n; ++i) faces.Append(m(faceIds[i] + 1));
        BRepOffsetAPI_MakeThickSolid mk;
        mk.MakeThickSolidByJoin(get(s), faces, thickness, 1e-3);
        mk.Build();
        if (!mk.IsDone()) { fail("Shell failed. Try a thinner wall."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
CK_API void* ck_offset(void* s, double d) {
    TRY
        BRepOffsetAPI_MakeOffsetShape mk;
        mk.PerformByJoin(get(s), d, 1e-4);
        if (!mk.IsDone()) { fail("Offset failed."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
// Give a surface thickness, producing a solid.
CK_API void* ck_thicken(void* s, double d) {
    TRY
        BRepOffset_MakeOffset mk;
        mk.Initialize(get(s), d, 1e-4, BRepOffset_Skin, true, false, GeomAbs_Intersection, true);
        mk.MakeOffsetShape();
        if (!mk.IsDone()) { fail("Thicken failed."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
CK_API void* ck_sew(void** shapes, int n, double tol, int toSolid) {
    TRY
        BRepBuilderAPI_Sewing sw(tol);
        for (int i = 0; i < n; ++i) sw.Add(get(shapes[i]));
        sw.Perform();
        TopoDS_Shape r = sw.SewedShape();
        if (toSolid) {
            TopExp_Explorer ex(r, TopAbs_SHELL);
            if (ex.More() && BRep_Tool::IsClosed(ex.Current())) {       // an open shell stays a surface: a "solid" with a hole in it is invalid
                BRepBuilderAPI_MakeSolid ms(TopoDS::Shell(ex.Current()));
                if (ms.IsDone()) {
                    TopoDS_Solid so = ms.Solid();
                    GProp_GProps gp; BRepGProp::VolumeProperties(so, gp);
                    if (gp.Mass() < 0) so.Reverse();
                    r = so;
                }
            }
        }
        return wrap(r);
    CATCH(nullptr)
}


// Faceted B-rep from a triangle mesh: every triangle becomes a planar face, sewn together (optionally into a solid).
CK_API void* ck_mesh_to_shape(const float* v, int nv, const int* t, int nt, double tol, int makeSolid) {
    TRY
        BRepBuilderAPI_Sewing sw(tol);
        int added = 0;
        for (int i = 0; i < nt; ++i) {
            int a = t[3*i], b = t[3*i+1], c = t[3*i+2];
            if (a < 0 || b < 0 || c < 0 || a >= nv || b >= nv || c >= nv) continue;
            gp_Pnt pa(v[3*a], v[3*a+1], v[3*a+2]), pb(v[3*b], v[3*b+1], v[3*b+2]), pc(v[3*c], v[3*c+1], v[3*c+2]);
            if (pa.Distance(pb) < 1e-9 || pb.Distance(pc) < 1e-9 || pa.Distance(pc) < 1e-9) continue;
            BRepBuilderAPI_MakePolygon poly(pa, pb, pc, true);
            if (!poly.IsDone()) continue;
            BRepBuilderAPI_MakeFace face(poly.Wire(), true);
            if (!face.IsDone()) continue;
            sw.Add(face.Face()); ++added;
        }
        if (added == 0) { fail("The mesh has no usable triangles."); return nullptr; }
        sw.Perform();
        TopoDS_Shape r = sw.SewedShape();
        if (makeSolid) {
            TopExp_Explorer ex(r, TopAbs_SHELL);
            if (ex.More()) {
                BRepBuilderAPI_MakeSolid ms(TopoDS::Shell(ex.Current()));
                if (ms.IsDone()) {
                    TopoDS_Solid so = ms.Solid();
                    GProp_GProps gp; BRepGProp::VolumeProperties(so, gp);
                    if (gp.Mass() < 0) so.Reverse();
                    r = so;
                }
            }
        }
        return wrap(r);
    CATCH(nullptr)
}

// ---------------------------------------------------------------- transforms
// Independent copy of the geometry without any stored triangulation, so it can be meshed at a coarser deflection.
CK_API void* ck_copy(void* s) {
    TRY return wrap(BRepBuilderAPI_Copy(get(s), Standard_True, Standard_False).Shape()); CATCH(nullptr)
}
CK_API void* ck_translate(void* s, double x, double y, double z) {
    TRY gp_Trsf t; t.SetTranslation(gp_Vec(x, y, z)); return wrap(transformed(get(s), t)); CATCH(nullptr)
}
// Rigid placement: m = 3x3 rotation (row-major) followed by the translation t.
CK_API void* ck_transform(void* s, const double* m, const double* t) {
    TRY gp_Trsf tr; tr.SetValues(m[0], m[1], m[2], t[0], m[3], m[4], m[5], t[1], m[6], m[7], m[8], t[2]); return wrap(transformed(get(s), tr)); CATCH(nullptr)
}
CK_API void* ck_compound(void** shapes, int n) {
    TRY BRep_Builder bb; TopoDS_Compound c; bb.MakeCompound(c); for (int i = 0; i < n; ++i) bb.Add(c, get(shapes[i])); return wrap(c); CATCH(nullptr)
}
CK_API void* ck_rotate(void* s, const double* o, const double* axis, double deg) {
    TRY gp_Trsf t; t.SetRotation(gp_Ax1(gp_Pnt(o[0], o[1], o[2]), gp_Dir(axis[0], axis[1], axis[2])), deg * M_PI / 180.0);
        return wrap(transformed(get(s), t)); CATCH(nullptr)
}
CK_API void* ck_scale(void* s, const double* o, double f) {
    TRY gp_Trsf t; t.SetScale(gp_Pnt(o[0], o[1], o[2]), f); return wrap(transformed(get(s), t)); CATCH(nullptr)
}
CK_API void* ck_mirror(void* s, const double* o, const double* n) {
    TRY gp_Trsf t; t.SetMirror(gp_Ax2(gp_Pnt(o[0], o[1], o[2]), gp_Dir(n[0], n[1], n[2]))); return wrap(transformed(get(s), t)); CATCH(nullptr)
}

// ---------------------------------------------------------------- sub-shapes
// kind: 0 solid, 1 shell, 2 face, 3 wire, 4 edge, 5 vertex
static TopAbs_ShapeEnum kindOf(int k) {
    static const TopAbs_ShapeEnum t[] = { TopAbs_SOLID, TopAbs_SHELL, TopAbs_FACE, TopAbs_WIRE, TopAbs_EDGE, TopAbs_VERTEX };
    return t[std::clamp(k, 0, 5)];
}
CK_API int ck_count(void* s, int kind) {
    TRY TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), kindOf(kind), m); return m.Extent(); CATCH(0)
}
CK_API void* ck_sub(void* s, int kind, int index) {
    TRY TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), kindOf(kind), m);
        if (index < 0 || index >= m.Extent()) { fail("Sub-shape index out of range."); return nullptr; }
        return wrap(m(index + 1)); CATCH(nullptr)
}
// Direct children of a compound (non-recursive).
CK_API int ck_children(void* s, void** out, int max) {
    TRY int n = 0;
        for (TopoDS_Iterator it(get(s)); it.More(); it.Next()) { if (n < max) out[n] = wrap(it.Value()); ++n; }
        return n; CATCH(0)
}
CK_API int ck_shape_type(void* s) { TRY return (int)get(s).ShapeType(); CATCH(-1) } // TopAbs_ShapeEnum: 0 compound,1 compsolid,2 solid,3 shell,4 face,5 wire,6 edge,7 vertex
CK_API int ck_is_valid(void* s) { TRY return BRepCheck_Analyzer(get(s)).IsValid() ? 1 : 0; CATCH(0) }

// Integrate spline knot spans separately to keep volume stable across trimmed faces.
static void volumeProperties(const TopoDS_Shape& shape, GProp_GProps& props, bool moments = false) {
    if (TopExp_Explorer(shape, TopAbs_SOLID).More()) {
        for (TopExp_Explorer faces(shape, TopAbs_FACE); faces.More(); faces.Next()) {
            auto type = BRepAdaptor_Surface(TopoDS::Face(faces.Current())).GetType();
            if (type == GeomAbs_BSplineSurface || type == GeomAbs_BezierSurface) {
                if (BRepGProp::VolumePropertiesGK(shape, props, 1e-8, /*OnlyClosed=*/false,
                    /*IsUseSpan=*/true, /*CGFlag=*/moments, /*IFlag=*/moments) < 0) {
                    props = GProp_GProps();
                    BRepGProp::VolumeProperties(shape, props, 1e-9);
                }
                return;
            }
        }
    }
    BRepGProp::VolumeProperties(shape, props);
}

// props: [volume, area, minx,miny,minz, maxx,maxy,maxz]
CK_API int ck_props(void* s, double* out) {
    TRY
        GProp_GProps v, a;
        volumeProperties(get(s), v);
        BRepGProp::SurfaceProperties(get(s), a, 1e-9);
        // tight box (BRepBndLib::Add alone follows NURBS control polygons, which stick out far past the real surface)
        // Measurement bounds must come from the geometry. Display triangulations carry
        // deflection padding and can inflate NURBS dimensions after the first render.
        Bnd_Box b; BRepBndLib::AddOptimal(get(s), b, false, false);
        out[0] = v.Mass(); out[1] = a.Mass();
        if (b.IsVoid()) { for (int i = 2; i < 8; ++i) out[i] = 0; }
        else b.Get(out[2], out[3], out[4], out[5], out[6], out[7]);
        return 1;
    CATCH(0)
}

// ---------------------------------------------------------------- freeform
CK_API void* ck_nurbs_surface(const double* xyz, int rows, int cols, int degU, int degV) {
    TRY
        int du = std::min(degU, rows - 1), dv = std::min(degV, cols - 1);
        if (du < 1 || dv < 1) { fail("Need at least a 2 x 2 grid of control points."); return nullptr; }
        TColgp_Array2OfPnt p(1, rows, 1, cols);
        for (int i = 0; i < rows; ++i)
            for (int j = 0; j < cols; ++j) {
                const double* q = xyz + 3 * (i * cols + j);
                p(i + 1, j + 1) = gp_Pnt(q[0], q[1], q[2]);
            }
        TColStd_Array1OfReal uk(1, rows - du + 1), vk(1, cols - dv + 1);
        TColStd_Array1OfInteger um(1, rows - du + 1), vm(1, cols - dv + 1);
        clampedKnots(rows, du, uk, um);
        clampedKnots(cols, dv, vk, vm);
        Handle(Geom_BSplineSurface) s = new Geom_BSplineSurface(p, uk, vk, um, vm, du, dv);
        return wrap(BRepBuilderAPI_MakeFace(s, Precision::Confusion()).Face());
    CATCH(nullptr)
}
// Surface passing through a grid of points.
CK_API void* ck_surface_fit(const double* xyz, int rows, int cols) {
    TRY
        if (rows < 2 || cols < 2) { fail("Need at least a 2 x 2 grid."); return nullptr; }
        TColgp_Array2OfPnt p(1, rows, 1, cols);
        for (int i = 0; i < rows; ++i)
            for (int j = 0; j < cols; ++j) {
                const double* q = xyz + 3 * (i * cols + j);
                p(i + 1, j + 1) = gp_Pnt(q[0], q[1], q[2]);
            }
        GeomAPI_PointsToBSplineSurface fit;
        fit.Interpolate(p);
        if (!fit.IsDone()) { fail("Surface fitting failed."); return nullptr; }
        return wrap(BRepBuilderAPI_MakeFace(fit.Surface(), Precision::Confusion()).Face());
    CATCH(nullptr)
}
CK_API void* ck_nurbs_convert(void* s) {
    TRY return wrap(BRepBuilderAPI_NurbsConvert(get(s), true).Shape()); CATCH(nullptr)
}
// dims: [rows, cols, degU, degV]
CK_API int ck_face_dims(void* f, int* d) {
    TRY auto s = faceSurface(get(f));
        d[0] = s->NbUPoles(); d[1] = s->NbVPoles(); d[2] = s->UDegree(); d[3] = s->VDegree(); return 1; CATCH(0)
}
CK_API int ck_face_get_poles(void* f, double* xyz) {
    TRY auto s = faceSurface(get(f));
        int R = s->NbUPoles(), C = s->NbVPoles();
        for (int i = 0; i < R; ++i) for (int j = 0; j < C; ++j) {
            gp_Pnt p = s->Pole(i + 1, j + 1); double* q = xyz + 3 * (i * C + j);
            q[0] = p.X(); q[1] = p.Y(); q[2] = p.Z();
        }
        return 1; CATCH(0)
}
CK_API void* ck_face_set_poles(void* f, const double* xyz) {
    TRY auto s = faceSurface(get(f));
        int R = s->NbUPoles(), C = s->NbVPoles();
        for (int i = 0; i < R; ++i) for (int j = 0; j < C; ++j) {
            const double* q = xyz + 3 * (i * C + j);
            s->SetPole(i + 1, j + 1, gp_Pnt(q[0], q[1], q[2]));
        }
        return wrap(BRepBuilderAPI_MakeFace(s, Precision::Confusion()).Face());
    CATCH(nullptr)
}
// Insert a knot at the middle of every span in U and V: same shape, 2x the control points to sculpt with.
CK_API void* ck_face_refine(void* f) {
    TRY auto s = faceSurface(get(f));
        std::vector<double> us, vs;
        for (int i = 1; i < s->NbUKnots(); ++i) us.push_back(0.5 * (s->UKnot(i) + s->UKnot(i + 1)));
        for (int i = 1; i < s->NbVKnots(); ++i) vs.push_back(0.5 * (s->VKnot(i) + s->VKnot(i + 1)));
        for (double u : us) s->InsertUKnot(u, 1, 1e-9);
        for (double v : vs) s->InsertVKnot(v, 1, 1e-9);
        return wrap(BRepBuilderAPI_MakeFace(s, Precision::Confusion()).Face());
    CATCH(nullptr)
}
CK_API void* ck_face_elevate(void* f) {
    TRY auto s = faceSurface(get(f));
        s->IncreaseDegree(std::min(s->UDegree() + 1, 8), std::min(s->VDegree() + 1, 8));
        return wrap(BRepBuilderAPI_MakeFace(s, Precision::Confusion()).Face());
    CATCH(nullptr)
}
CK_API int ck_curve_count(void* e) { TRY return edgeCurve(get(e))->NbPoles(); CATCH(0) }
// Work in a local frame where the chosen natural boundary is U-min.
static Handle(Geom_BSplineSurface) matchPatch(const TopoDS_Shape& shape, int side) {
    if (side < 0 || side > 3) throw Standard_Failure("Choose U min, U max, V min or V max.");
    TopTools_IndexedMapOfShape faces; TopExp::MapShapes(shape, TopAbs_FACE, faces);
    if (faces.Extent() != 1) throw Standard_Failure("Pick one standalone NURBS patch or one face.");
    auto f = TopoDS::Face(faces(1)); auto s = faceSurface(f);
    if (s->IsUPeriodic() || s->IsVPeriodic() || s->IsURational() || s->IsVRational())
        throw Standard_Failure("Matching currently needs non-rational, non-periodic patches.");
    double u0,u1,v0,v1,a,b,c,d; s->Bounds(u0,u1,v0,v1); BRepTools::UVBounds(f,a,b,c,d);
    TopTools_IndexedMapOfShape edges; TopExp::MapShapes(f, TopAbs_EDGE, edges);
    if (edges.Extent() != 4 || std::abs(a-u0)>1e-7 || std::abs(b-u1)>1e-7 || std::abs(c-v0)>1e-7 || std::abs(d-v1)>1e-7)
        throw Standard_Failure("Matching needs an untrimmed rectangular patch. Extract and untrim it first.");
    for(int i=1;i<=edges.Extent();i++) {
        double first,last; auto curve=BRep_Tool::CurveOnSurface(TopoDS::Edge(edges(i)),f,first,last);
        if(curve.IsNull()) throw Standard_Failure("The patch boundary has no parameter curve.");
        bool on[4]={true,true,true,true};
        for(int k=0;k<=8;k++) {
            auto uv=curve->Value(first+(last-first)*k/8);
            on[0]&=std::abs(uv.X()-u0)<1e-7; on[1]&=std::abs(uv.X()-u1)<1e-7;
            on[2]&=std::abs(uv.Y()-v0)<1e-7; on[3]&=std::abs(uv.Y()-v1)<1e-7;
        }
        if(!on[0]&&!on[1]&&!on[2]&&!on[3]) throw Standard_Failure("Untrim this face before matching its natural boundary.");
    }
    if (side >= 2) s->ExchangeUV();
    if (side % 2) s->UReverse();
    if (s->UMultiplicity(1) != s->UDegree()+1 || s->VMultiplicity(1) != s->VDegree()+1 || s->VMultiplicity(s->NbVKnots()) != s->VDegree()+1)
        throw Standard_Failure("Matching needs clamped patch boundaries.");
    return s;
}
static std::pair<double,double> matchSpans(const Handle(Geom_BSplineSurface)& s) {
    int p=s->UDegree(); TColStd_Array1OfReal knots(1,s->NbUPoles()+p+1); s->UKnotSequence(knots);
    return {knots(p+2)-knots(1),knots(p+3)-knots(1)};
}
CK_API void* ck_surface_match(void* source, int side, void* reference, int refSide, int continuity, int reverse, double scale) {
    TRY
        if (continuity<0 || continuity>2 || !std::isfinite(scale) || scale<=0)
            throw Standard_Failure("Choose G0, G1 or G2 and a positive tangent scale.");
        auto s=matchPatch(get(source),side), r=matchPatch(get(reference),refSide);
        if (reverse) r->VReverse();
        if (s->NbVPoles()!=r->NbVPoles() || s->VDegree()!=r->VDegree() || s->NbVKnots()!=r->NbVKnots())
            throw Standard_Failure("Boundary control counts and degrees must match. Use compatible patches.");
        double sr=s->VKnot(s->NbVKnots())-s->VKnot(1), rr=r->VKnot(r->NbVKnots())-r->VKnot(1);
        for(int j=1;j<=s->NbVKnots();j++)
            if(s->VMultiplicity(j)!=r->VMultiplicity(j) || std::abs((s->VKnot(j)-s->VKnot(1))/sr-(r->VKnot(j)-r->VKnot(1))/rr)>1e-9)
                throw Standard_Failure("Boundary knot spacing must match. Refine compatible patches together.");
        if(s->UDegree()<continuity || r->UDegree()<continuity || s->NbUPoles()<continuity+2)
            throw Standard_Failure("This continuity needs higher cross-boundary degree and enough rows to retain the opposite boundary.");
        auto [h1,h2]=matchSpans(s); auto [k1,k2]=matchSpans(r);
        double p=s->UDegree(), q=r->UDegree();
        for(int j=1;j<=s->NbVPoles();j++) {
            gp_Pnt p0=r->Pole(1,j); gp_Vec d1;
            if(continuity>=1) d1=gp_Vec(r->Pole(1,j),r->Pole(2,j))*(q/k1);
            s->SetPole(1,j,p0);
            if(continuity>=1) {
                gp_Vec wanted=d1*(-scale); gp_Pnt p1=p0.Translated(wanted*(h1/p)); s->SetPole(2,j,p1);
                if(continuity>=2) {
                    gp_Vec d2=(gp_Vec(r->Pole(2,j),r->Pole(3,j))*(q/k2)-d1)*((q-1)/k1);
                    s->SetPole(3,j,p1.Translated((wanted+d2*(scale*scale*h1/(p-1)))*(h2/p)));
                }
            }
        }
        if(side%2) s->UReverse(); if(side>=2) s->ExchangeUV();
        auto result=BRepBuilderAPI_MakeFace(s,Precision::Confusion()).Face();
        TopTools_IndexedMapOfShape originals; TopExp::MapShapes(get(source),TopAbs_FACE,originals);
        result.Orientation(originals(1).Orientation());
        if(!BRepCheck_Analyzer(result).IsValid()) throw Standard_Failure("Matching produced invalid topology. Try a different scale or boundary.");
        return wrap(result);
    CATCH(nullptr)
}
// Sampled natural-boundary diagnostics: maximum gap, RMS gap, normal angle (degrees),
// and cross-boundary normal-curvature difference (1/mm). Not a global continuity certificate.
CK_API int ck_surface_continuity(void* source,int side,void* reference,int refSide,int reverse,int samples,double* out) {
    TRY
        if(samples<3 || samples>1001) throw Standard_Failure("Use 3 to 1001 seam samples.");
        auto s=matchPatch(get(source),side), r=matchPatch(get(reference),refSide); if(reverse) r->VReverse();
        double a,b,c,d,e,f,g,h; s->Bounds(a,b,c,d); r->Bounds(e,f,g,h);
        for(int i=0;i<4;i++) out[i]=0;
        for(int i=0;i<samples;i++) {
            double t=(double)i/(samples-1); gp_Pnt ps,pr; gp_Vec su,sv,suu,svv,suv,ru,rv,ruu,rvv,ruv;
            s->D2(a,c+(d-c)*t,ps,su,sv,suu,svv,suv); r->D2(e,g+(h-g)*t,pr,ru,rv,ruu,rvv,ruv);
            auto ns=su.Crossed(sv), nr=ru.Crossed(rv);
            if(ns.Magnitude()<1e-12 || nr.Magnitude()<1e-12 || su.SquareMagnitude()<1e-18 || ru.SquareMagnitude()<1e-18)
                throw Standard_Failure("The seam contains a singular tangent. Repair the patch before checking continuity.");
            ns.Normalize(); nr.Normalize(); if(ns.Dot(nr)<0) ns.Reverse();
            double gap=ps.Distance(pr); out[0]=std::max(out[0],gap); out[1]+=gap*gap;
            out[2]=std::max(out[2],std::acos(std::clamp(ns.Dot(nr),-1.0,1.0))*180/3.141592653589793);
            out[3]=std::max(out[3],std::abs(suu.Dot(ns)/su.SquareMagnitude()-ruu.Dot(nr)/ru.SquareMagnitude()));
        }
        out[1]=std::sqrt(out[1]/samples); return 1;
    CATCH(0)
}
CK_API int ck_curve_get_poles(void* e, double* xyz) {
    TRY auto c = edgeCurve(get(e));
        for (int i = 1; i <= c->NbPoles(); ++i) { gp_Pnt p = c->Pole(i); xyz[3*(i-1)] = p.X(); xyz[3*(i-1)+1] = p.Y(); xyz[3*(i-1)+2] = p.Z(); }
        return 1; CATCH(0)
}
CK_API void* ck_curve_set_poles(void* e, const double* xyz) {
    TRY auto c = edgeCurve(get(e));
        for (int i = 1; i <= c->NbPoles(); ++i) c->SetPole(i, gp_Pnt(xyz[3*(i-1)], xyz[3*(i-1)+1], xyz[3*(i-1)+2]));
        return wrap(BRepBuilderAPI_MakeEdge(c).Edge());
    CATCH(nullptr)
}
// Samples every edge of a shape perEdge times (even steps in the curve's own parameter). out: 6 doubles per sample: point, then the
// curvature vector (points toward the centre of curvature, length 1/radius). Returns the number of edges written.
CK_API int ck_curve_sample(void* s, int perEdge, double* out, int maxEdges) {
    TRY
        int edges = 0;
        for (TopExp_Explorer ex(get(s), TopAbs_EDGE); ex.More() && edges < maxEdges; ex.Next(), ++edges) {
            BRepAdaptor_Curve c(TopoDS::Edge(ex.Current()));
            double f = c.FirstParameter(), l = c.LastParameter();
            for (int i = 0; i < perEdge; ++i) {
                double u = f + (l - f) * i / (perEdge - 1);
                gp_Pnt p; gp_Vec d1, d2; c.D2(u, p, d1, d2);
                double* o = out + 6 * ((size_t)edges * perEdge + i);
                o[0] = p.X(); o[1] = p.Y(); o[2] = p.Z();
                double l1 = d1.Magnitude(); gp_Vec k(0, 0, 0);
                if (l1 > 1e-12) { gp_Vec t = d1 / l1; gp_Vec perp = d2 - t * d2.Dot(t); k = perp / (l1 * l1); }
                o[3] = k.X(); o[4] = k.Y(); o[5] = k.Z();
            }
        }
        return edges;
    CATCH(0)
}
CK_API void* ck_curve_refine(void* e) {
    TRY auto c = edgeCurve(get(e));
        std::vector<double> ks;
        for (int i = 1; i < c->NbKnots(); ++i) ks.push_back(0.5 * (c->Knot(i) + c->Knot(i + 1)));
        for (double k : ks) c->InsertKnot(k, 1, 1e-9);
        return wrap(BRepBuilderAPI_MakeEdge(c).Edge());
    CATCH(nullptr)
}

// ---------------------------------------------------------------- analysis
CK_API void* ck_vertex(double x, double y, double z) {
    TRY return wrap(BRepBuilderAPI_MakeVertex(gp_Pnt(x, y, z)).Vertex()); CATCH(nullptr)
}

// out: [volume, area, cogx, cogy, cogz, Ixx, Iyy, Izz (principal moments about the centre of mass, unit density), isSolid]
CK_API int ck_mass(void* s, double* out) {
    TRY
        GProp_GProps v, a;
        volumeProperties(get(s), v, true);
        BRepGProp::SurfaceProperties(get(s), a, 1e-9);
        bool solid = std::abs(v.Mass()) > 1e-9;
        GProp_GProps& g = solid ? v : a;
        gp_Pnt c = g.CentreOfMass();
        GProp_PrincipalProps pp = g.PrincipalProperties();
        double i1, i2, i3; pp.Moments(i1, i2, i3);
        out[0] = std::abs(v.Mass()); out[1] = a.Mass();
        out[2] = c.X(); out[3] = c.Y(); out[4] = c.Z();
        out[5] = i1; out[6] = i2; out[7] = i3; out[8] = solid ? 1 : 0;
        return 1;
    CATCH(0)
}

// out: [distance, ax, ay, az, bx, by, bz]
CK_API int ck_distance(void* a, void* b, double* out) {
    TRY
        BRepExtrema_DistShapeShape d(get(a), get(b));
        if (!d.IsDone() || d.NbSolution() == 0) { fail("Could not measure the distance between these shapes."); return 0; }
        gp_Pnt p = d.PointOnShape1(1), q = d.PointOnShape2(1);
        out[0] = d.Value(); out[1] = p.X(); out[2] = p.Y(); out[3] = p.Z(); out[4] = q.X(); out[5] = q.Y(); out[6] = q.Z();
        return 1;
    CATCH(0)
}

// Curves where a plane cuts the shape.
CK_API void* ck_section(void* s, const double* o, const double* n) {
    TRY
        gp_Pln pl(gp_Pnt(o[0], o[1], o[2]), gp_Dir(n[0], n[1], n[2]));
        BRepAlgoAPI_Section sec(get(s), pl, Standard_False);
        sec.Build();
        if (!sec.IsDone() || sec.Shape().IsNull()) { fail("The plane does not cut this shape."); return nullptr; }
        return wrap(sec.Shape());
    CATCH(nullptr)
}

// Curves where two shapes (faces, shells, solids) cross each other.
CK_API void* ck_intersect(void* a, void* b) {
    TRY
        BRepAlgoAPI_Section sec(get(a), get(b), Standard_False);
        sec.Build();
        if (!sec.IsDone() || sec.Shape().IsNull()) { fail("Those two do not intersect."); return nullptr; }
        return wrap(sec.Shape());
    CATCH(nullptr)
}

// Remove faces (holes, bosses, fillets, pockets...) and heal the neighbours over the gap.
CK_API void* ck_defeature(void* s, const int* faces, int n) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_FACE, m);
        BRepAlgoAPI_Defeaturing d;
        d.SetShape(get(s));
        for (int i = 0; i < n; ++i) { if (faces[i] < 0 || faces[i] >= m.Extent()) { fail("Face index out of range."); return nullptr; } d.AddFaceToRemove(m(faces[i] + 1)); }
        d.SetRunParallel(Standard_True);
        d.Build();
        if (d.HasErrors() || d.Shape().IsNull()) { fail("The faces could not be removed cleanly."); return nullptr; }
        return wrap(clean(d.Shape()));
    CATCH(nullptr)
}

// Merge neighbouring faces on one surface, but only along edges whose two faces are both in the given list: every other edge is kept as it is.

CK_API void* ck_unify_faces(void* s, const int* faces, int n) {

    TRY

        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_FACE, m);

        TopTools_MapOfShape pick;

        for (int i = 0; i < n; ++i) { if (faces[i] < 0 || faces[i] >= m.Extent()) { fail("Face index out of range."); return nullptr; } pick.Add(m(faces[i] + 1)); }

        TopTools_IndexedDataMapOfShapeListOfShape anc; TopExp::MapShapesAndAncestors(get(s), TopAbs_EDGE, TopAbs_FACE, anc);

        TopTools_MapOfShape keep;

        for (int i = 1; i <= anc.Extent(); ++i) {

            int inside = 0;

            for (const TopoDS_Shape& f : anc(i)) if (pick.Contains(f)) ++inside;

            if (inside < 2) keep.Add(anc.FindKey(i));

        }

        ShapeUpgrade_UnifySameDomain u(get(s), false, true, false);

        u.KeepShapes(keep);

        u.Build();

        if (u.Shape().IsNull()) { fail("The faces could not be merged."); return nullptr; }

        return wrap(u.Shape());

    CATCH(nullptr)

}



// Offset the outlines of a planar face (or a wire) inside its own plane. Negative distances go inward. Returns the offset wires.
CK_API void* ck_offset_planar(void* s, double dist, int join) {
    TRY
        const TopoDS_Shape& sh = get(s);
        GeomAbs_JoinType jt = join == 1 ? GeomAbs_Intersection : GeomAbs_Arc;
        BRepOffsetAPI_MakeOffset mk;
        if (sh.ShapeType() == TopAbs_FACE) { mk.Init(TopoDS::Face(sh), jt, Standard_False); }
        else if (sh.ShapeType() == TopAbs_WIRE) { mk.Init(jt, Standard_False); mk.AddWire(TopoDS::Wire(sh)); }
        else { fail("Offset needs a planar face or a wire."); return nullptr; }
        mk.Perform(dist);
        if (!mk.IsDone() || mk.Shape().IsNull()) { fail("The outline cannot be offset by that much."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}

// Split a shape by a tool (faces, shells, solids). Returns the pieces as a compound.
CK_API void* ck_split(void* s, void* tool) {
    TRY
        BRepAlgoAPI_Splitter sp;
        TopTools_ListOfShape args, tools; args.Append(get(s)); tools.Append(get(tool));
        sp.SetArguments(args); sp.SetTools(tools); sp.SetRunParallel(Standard_True);
        sp.Build();
        if (sp.HasErrors() || sp.Shape().IsNull()) { fail("The cutting shape does not split this surface."); return nullptr; }
        return wrap(sp.Shape());
    CATCH(nullptr)
}

// The closed spaces enclosed by a set of faces, shells and solids, as a compound of solids.
CK_API void* ck_make_volumes(void** shapes, int n) {
    TRY
        BOPAlgo_MakerVolume mv;
        TopTools_ListOfShape args; for (int i = 0; i < n; ++i) args.Append(get(shapes[i]));
        mv.SetArguments(args); mv.SetIntersect(Standard_True); mv.SetRunParallel(Standard_True);
        mv.Perform();
        if (mv.HasErrors() || mv.Shape().IsNull()) { fail("Those shapes do not enclose a space."); return nullptr; }
        return wrap(mv.Shape());
    CATCH(nullptr)
}

static bool isFreeEdge(const TopoDS_Edge& e, const TopTools_ListOfShape& faces) {
    if (BRep_Tool::Degenerated(e)) return false;
    std::vector<TopoDS_Shape> distinct;
    for (auto it = faces.cbegin(); it != faces.cend(); ++it) {
        bool seen = false; for (auto& d : distinct) if (d.IsSame(*it)) { seen = true; break; }
        if (!seen) distinct.push_back(*it);
    }
    if (distinct.size() != 1) return distinct.empty() ? false : false;
    return !BRep_Tool::IsClosed(e, TopoDS::Face(distinct[0]));
}

// Open edges (used by exactly one face). Indices match the edge numbering of the tessellation. Returns the count (fills up to max).
CK_API int ck_free_edges(void* s, int* out, int max) {
    TRY
        TopTools_IndexedMapOfShape edges; TopExp::MapShapes(get(s), TopAbs_EDGE, edges);
        TopTools_IndexedDataMapOfShapeListOfShape anc; TopExp::MapShapesAndAncestors(get(s), TopAbs_EDGE, TopAbs_FACE, anc);
        int n = 0;
        for (int i = 1; i <= edges.Extent(); ++i) {
            int idx = anc.FindIndex(edges(i));
            if (idx == 0) continue;
            if (isFreeEdge(TopoDS::Edge(edges(i)), anc.FindFromIndex(idx))) { if (n < max) out[n] = i - 1; ++n; }
        }
        return n;
    CATCH(0)
}

// out: [valid, faces, edges, shells, solids, freeEdges, vertices]
CK_API int ck_check(void* s, double* out) {
    TRY
        out[0] = BRepCheck_Analyzer(get(s)).IsValid() ? 1 : 0;
        TopTools_IndexedMapOfShape m;
        TopExp::MapShapes(get(s), TopAbs_FACE, m); out[1] = m.Extent(); m.Clear();
        TopExp::MapShapes(get(s), TopAbs_EDGE, m); out[2] = m.Extent(); m.Clear();
        TopExp::MapShapes(get(s), TopAbs_SHELL, m); out[3] = m.Extent(); m.Clear();
        TopExp::MapShapes(get(s), TopAbs_SOLID, m); out[4] = m.Extent(); m.Clear();
        TopExp::MapShapes(get(s), TopAbs_VERTEX, m); out[6] = m.Extent();
        std::vector<int> tmp(100000);
        out[5] = ck_free_edges(s, tmp.data(), (int)tmp.size());
        return 1;
    CATCH(0)
}

// Surface info: [type (0 plane,1 cylinder,2 cone,3 sphere,4 torus,5 bezier,6 bspline,7 revolution,8 extrusion,9 offset,10 other), area, r1, r2, nx, ny, nz]
CK_API int ck_face_info(void* f, double* out) {
    TRY
        const TopoDS_Face face = TopoDS::Face(get(f));
        BRepAdaptor_Surface sf(face);
        GProp_GProps g; BRepGProp::SurfaceProperties(face, g, 1e-9);
        for (int i = 0; i < 8; ++i) out[i] = 0;
        out[1] = g.Mass();
        switch (sf.GetType()) {
        case GeomAbs_Plane: { out[0] = 0; gp_Dir d = sf.Plane().Axis().Direction(); if (face.Orientation() == TopAbs_REVERSED) d.Reverse(); out[4] = d.X(); out[5] = d.Y(); out[6] = d.Z(); break; }
        case GeomAbs_Cylinder: out[0] = 1; out[2] = sf.Cylinder().Radius(); break;
        case GeomAbs_Cone: out[0] = 2; out[2] = sf.Cone().RefRadius(); out[3] = sf.Cone().SemiAngle(); break;
        case GeomAbs_Sphere: out[0] = 3; out[2] = sf.Sphere().Radius(); break;
        case GeomAbs_Torus: out[0] = 4; out[2] = sf.Torus().MajorRadius(); out[3] = sf.Torus().MinorRadius(); break;
        case GeomAbs_BezierSurface: out[0] = 5; break;
        case GeomAbs_BSplineSurface: out[0] = 6; break;
        case GeomAbs_SurfaceOfRevolution: out[0] = 7; break;
        case GeomAbs_SurfaceOfExtrusion: out[0] = 8; break;
        case GeomAbs_OffsetSurface: out[0] = 9; break;
        default: out[0] = 10; break;
        }
        return 1;
    CATCH(0)
}

// Curve info: [type (0 line,1 circle,2 ellipse,3 hyperbola,4 parabola,5 bezier,6 bspline,7 offset,8 other), length, radius, startxyz, ...]
CK_API int ck_edge_info(void* e, double* out) {
    TRY
        BRepAdaptor_Curve c(TopoDS::Edge(get(e)));
        for (int i = 0; i < 9; ++i) out[i] = 0;
        out[1] = GCPnts_AbscissaPoint::Length(c);
        switch (c.GetType()) {
        case GeomAbs_Line: out[0] = 0; break;
        case GeomAbs_Circle: out[0] = 1; out[2] = c.Circle().Radius(); break;
        case GeomAbs_Ellipse: out[0] = 2; out[2] = c.Ellipse().MajorRadius(); break;
        case GeomAbs_Hyperbola: out[0] = 3; break;
        case GeomAbs_Parabola: out[0] = 4; break;
        case GeomAbs_BezierCurve: out[0] = 5; break;
        case GeomAbs_BSplineCurve: out[0] = 6; break;
        case GeomAbs_OffsetCurve: out[0] = 7; break;
        default: out[0] = 8; break;
        }
        gp_Pnt a = c.Value(c.FirstParameter()), b = c.Value(c.LastParameter());
        out[3] = a.X(); out[4] = a.Y(); out[5] = a.Z(); out[6] = b.X(); out[7] = b.Y(); out[8] = b.Z();
        return 1;
    CATCH(0)
}


// Axis of a revolved/linear feature: [ox,oy,oz, dx,dy,dz]. Cylinder/cone/torus faces, circle and line edges; planar faces give centroid + normal.
CK_API int ck_axis(void* s, double* out) {
    TRY
        const TopoDS_Shape& sh = get(s);
        if (sh.ShapeType() == TopAbs_FACE) {
            const TopoDS_Face face = TopoDS::Face(sh);
            BRepAdaptor_Surface sf(face);
            gp_Ax1 ax;
            switch (sf.GetType()) {
            case GeomAbs_Cylinder: ax = sf.Cylinder().Axis(); break;
            case GeomAbs_Cone: ax = sf.Cone().Axis(); break;
            case GeomAbs_Torus: ax = sf.Torus().Axis(); break;
            case GeomAbs_Plane: {
                GProp_GProps g; BRepGProp::SurfaceProperties(face, g, 1e-9);
                gp_Dir d = sf.Plane().Axis().Direction(); if (face.Orientation() == TopAbs_REVERSED) d.Reverse();
                ax = gp_Ax1(g.CentreOfMass(), d); break; }
            default: return 0;
            }
            out[0] = ax.Location().X(); out[1] = ax.Location().Y(); out[2] = ax.Location().Z();
            out[3] = ax.Direction().X(); out[4] = ax.Direction().Y(); out[5] = ax.Direction().Z();
            return 1;
        }
        if (sh.ShapeType() == TopAbs_EDGE) {
            BRepAdaptor_Curve c(TopoDS::Edge(sh));
            gp_Ax1 ax;
            if (c.GetType() == GeomAbs_Circle) ax = c.Circle().Axis();
            else if (c.GetType() == GeomAbs_Line) ax = c.Line().Position();
            else return 0;
            out[0] = ax.Location().X(); out[1] = ax.Location().Y(); out[2] = ax.Location().Z();
            out[3] = ax.Direction().X(); out[4] = ax.Direction().Y(); out[5] = ax.Direction().Z();
            return 1;
        }
        return 0;
    CATCH(0)
}

// ---------------------------------------------------------------- tessellation
CK_API void* ck_mesh_create(void* shape, double lin, double ang) {
    TRY
        const TopoDS_Shape& sh = get(shape);
        BRepMesh_IncrementalMesh(sh, lin, false, ang, true);
        auto m = std::make_unique<Mesh>();
        TopTools_IndexedMapOfShape faces, edges;
        TopExp::MapShapes(sh, TopAbs_FACE, faces);
        TopExp::MapShapes(sh, TopAbs_EDGE, edges);
        for (int fi = 1; fi <= faces.Extent(); ++fi) {
            const TopoDS_Face& face = TopoDS::Face(faces(fi));
            TopLoc_Location loc;
            Handle(Poly_Triangulation) tri = BRep_Tool::Triangulation(face, loc);
            if (tri.IsNull()) continue;
            bool rev = face.Orientation() == TopAbs_REVERSED;
            int base = (int)m->pos.size() / 3, nn = tri->NbNodes();
            gp_Trsf tr = loc.Transformation();
            std::vector<gp_Vec> nrm(nn, gp_Vec(0, 0, 0));
            std::vector<char> exact(nn, 0);                 // true where the normal comes from the surface itself (not from averaging triangles)
            bool hasN = tri->HasNormals();
            // Exact surface normals at every node (from the face's own parameters) keep reflections and curvature smooth even on irregular triangulations.
            bool hasUV = tri->HasUVNodes();
            std::unique_ptr<BRepAdaptor_Surface> surf; if (hasUV) { try { surf = std::make_unique<BRepAdaptor_Surface>(face); } catch (...) { hasUV = false; } }
            for (int i = 1; i <= nn; ++i) {
                gp_Pnt p = tri->Node(i).Transformed(tr);
                m->pos.push_back((float)p.X()); m->pos.push_back((float)p.Y()); m->pos.push_back((float)p.Z());
                if (hasUV) {
                    try {
                        gp_Pnt2d uv = tri->UVNode(i); gp_Pnt pp; gp_Vec du, dv; surf->D1(uv.X(), uv.Y(), pp, du, dv);
                        gp_Vec n = du ^ dv;
                        if (n.Magnitude() > 1e-9) { n.Normalize(); n.Transform(tr); nrm[i - 1] = n; exact[i - 1] = 1; continue; }
                    } catch (...) {}
                }
                if (hasN) { gp_Dir d = tri->Normal(i); d.Transform(tr); nrm[i - 1] = gp_Vec(d); exact[i - 1] = 1; }
            }
            for (int i = 1; i <= tri->NbTriangles(); ++i) {
                int a, b, c; tri->Triangle(i).Get(a, b, c);
                if (rev) std::swap(b, c);
                m->idx.push_back(base + a - 1); m->idx.push_back(base + b - 1); m->idx.push_back(base + c - 1);
                m->triFace.push_back(fi - 1);
                {
                    gp_Pnt pa(m->pos[3*(base+a-1)], m->pos[3*(base+a-1)+1], m->pos[3*(base+a-1)+2]);
                    gp_Pnt pb(m->pos[3*(base+b-1)], m->pos[3*(base+b-1)+1], m->pos[3*(base+b-1)+2]);
                    gp_Pnt pc(m->pos[3*(base+c-1)], m->pos[3*(base+c-1)+1], m->pos[3*(base+c-1)+2]);
                    gp_Vec n = gp_Vec(pa, pb) ^ gp_Vec(pa, pc);
                    for (int v : { a, b, c }) if (!exact[v - 1]) nrm[v - 1] += n;
                }
            }
            for (int i = 0; i < nn; ++i) {
                gp_Vec n = nrm[i];
                if (exact[i] && rev) n.Reverse();
                if (n.Magnitude() > 1e-12) n.Normalize();
                m->nrm.push_back((float)n.X()); m->nrm.push_back((float)n.Y()); m->nrm.push_back((float)n.Z());
            }
        }
        // Edges between two smooth freeform (B-spline / Bezier) faces that meet without a visible crease are only patch seams, e.g. the
        // hundreds of patch borders of a subdivision surface converted to NURBS. They are not drawn (the edge keeps its index, it just has no segments).
        TopTools_IndexedDataMapOfShapeListOfShape edgeFaces;
        TopExp::MapShapesAndAncestors(sh, TopAbs_EDGE, TopAbs_FACE, edgeFaces);
        auto isSoftSeam = [&](const TopoDS_Edge& e) -> bool {
            try {
                int ix = edgeFaces.FindIndex(e); if (ix == 0) return false;
                const TopTools_ListOfShape& fl = edgeFaces(ix);
                if (fl.Extent() != 2) return false;
                TopoDS_Face f1 = TopoDS::Face(fl.First()), f2 = TopoDS::Face(fl.Last());
                if (f1.IsSame(f2)) return false;
                auto freeform = [](const TopoDS_Face& f) { GeomAbs_SurfaceType t = BRepAdaptor_Surface(f).GetType(); return t == GeomAbs_BSplineSurface || t == GeomAbs_BezierSurface; };
                if (!freeform(f1) || !freeform(f2)) return false;
                double f, l; Handle(Geom2d_Curve) c1 = BRep_Tool::CurveOnSurface(e, f1, f, l);
                double g, m2; Handle(Geom2d_Curve) c2 = BRep_Tool::CurveOnSurface(e, f2, g, m2);
                if (c1.IsNull() || c2.IsNull()) return false;
                BRepAdaptor_Surface s1(f1), s2(f2);
                for (double t : { 0.2, 0.5, 0.8 }) {
                    gp_Pnt2d uv1 = c1->Value(f + (l - f) * t), uv2 = c2->Value(g + (m2 - g) * t);
                    gp_Pnt p; gp_Vec a1, b1, a2, b2;
                    s1.D1(uv1.X(), uv1.Y(), p, a1, b1); s2.D1(uv2.X(), uv2.Y(), p, a2, b2);
                    gp_Vec n1 = a1 ^ b1, n2 = a2 ^ b2;
                    if (n1.Magnitude() < 1e-12 || n2.Magnitude() < 1e-12) return false;
                    if (f1.Orientation() == TopAbs_REVERSED) n1.Reverse();
                    if (f2.Orientation() == TopAbs_REVERSED) n2.Reverse();
                    if (n1.Angle(n2) > 0.07) return false;       // ~4 degrees: a real crease stays visible
                }
                return true;
            } catch (...) { return false; }
        };
        for (int ei = 1; ei <= edges.Extent(); ++ei) {
            const TopoDS_Edge& e = TopoDS::Edge(edges(ei));
            if (BRep_Tool::Degenerated(e)) continue;
            if (isSoftSeam(e)) continue;
            BRepAdaptor_Curve c(e);
            std::vector<gp_Pnt> pts;
            GCPnts_TangentialDeflection d(c, 0.05, lin * 0.5, 2);
            if (d.NbPoints() >= 2) for (int i = 1; i <= d.NbPoints(); ++i) pts.push_back(d.Value(i));
            else { pts.push_back(c.Value(c.FirstParameter())); pts.push_back(c.Value(c.LastParameter())); }
            for (size_t i = 0; i + 1 < pts.size(); ++i) {
                for (const gp_Pnt* p : { &pts[i], &pts[i + 1] }) {
                    m->edgePts.push_back((float)p->X()); m->edgePts.push_back((float)p->Y()); m->edgePts.push_back((float)p->Z());
                }
                m->segEdge.push_back(ei - 1);
            }
        }
        return m.release();
    CATCH(nullptr)
}
CK_API void ck_mesh_sizes(void* h, int* nVerts, int* nTris, int* nSegs) {
    if (!h || !nVerts || !nTris || !nSegs) { fail("Invalid mesh or output pointers."); return; }
    auto* m = static_cast<Mesh*>(h);
    *nVerts = (int)m->pos.size() / 3; *nTris = (int)m->idx.size() / 3; *nSegs = (int)m->segEdge.size();
}
CK_API void ck_mesh_fill(void* h, float* pos, float* nrm, int* idx, int* triFace, float* edgePts, int* segEdge) {
    if (!h) { fail("Mesh handle is null."); return; }
    auto* m = static_cast<Mesh*>(h);
    if ((!m->pos.empty() && (!pos || !nrm)) || (!m->idx.empty() && (!idx || !triFace)) ||
        (!m->edgePts.empty() && (!edgePts || !segEdge))) { fail("Mesh output pointers are null."); return; }
    std::copy(m->pos.begin(), m->pos.end(), pos);
    std::copy(m->nrm.begin(), m->nrm.end(), nrm);
    std::copy(m->idx.begin(), m->idx.end(), idx);
    std::copy(m->triFace.begin(), m->triFace.end(), triFace);
    std::copy(m->edgePts.begin(), m->edgePts.end(), edgePts);
    std::copy(m->segEdge.begin(), m->segEdge.end(), segEdge);
}
CK_API void ck_mesh_free(void* h) { delete static_cast<Mesh*>(h); }

// ---------------------------------------------------------------- file I/O
CK_API int ck_export_step(void** shapes, int n, const char* path) {
    TRY
        STEPControl_Writer w;
        for (int i = 0; i < n; ++i) w.Transfer(get(shapes[i]), STEPControl_AsIs);
        return w.Write(path) == IFSelect_RetDone ? 1 : 0;
    CATCH(0)
}
CK_API void* ck_import_step(const char* path) {
    TRY
        STEPControl_Reader r;
        if (r.ReadFile(path) != IFSelect_RetDone) { fail("Could not read STEP file."); return nullptr; }
        r.TransferRoots();
        return wrap(r.OneShape());
    CATCH(nullptr)
}
CK_API int ck_export_iges(void** shapes, int n, const char* path) {
    TRY
        IGESControl_Writer w("MM", 1);   // mode 1: solids as BREP entities, so they come back as solids
        for (int i = 0; i < n; ++i) w.AddShape(get(shapes[i]));
        w.ComputeModel();
        return w.Write(path) ? 1 : 0;
    CATCH(0)
}
CK_API void* ck_import_iges(const char* path) {
    TRY
        IGESControl_Reader r;
        if (r.ReadFile(path) != IFSelect_RetDone) { fail("Could not read IGES file."); return nullptr; }
        r.TransferRoots();
        return wrap(r.OneShape());
    CATCH(nullptr)
}
CK_API int ck_export_stl(void** shapes, int n, const char* path, double lin) {
    TRY
        BRep_Builder bb; TopoDS_Compound c; bb.MakeCompound(c);
        for (int i = 0; i < n; ++i) bb.Add(c, get(shapes[i]));
        BRepMesh_IncrementalMesh(c, lin, false, 0.5, true);
        StlAPI_Writer w;
        return w.Write(c, path) ? 1 : 0;
    CATCH(0)
}
// Native persistence: BREP text. Returns required size; fills buf when cap is large enough.
CK_API int ck_brep_write(void* s, char* buf, int cap) {
    TRY
        std::ostringstream os;
        // Persist/hash exact geometry independently of mutable display triangulations.
        BRepTools::Write(get(s), os, Standard_False, Standard_False, TopTools_FormatVersion_CURRENT);
        std::string str = os.str();
        if ((int)str.size() <= cap && buf) std::copy(str.begin(), str.end(), buf);
        return (int)str.size();
    CATCH(0)
}
CK_API void* ck_brep_read(const char* data, int len) {
    TRY
        std::istringstream is(std::string(data, len));
        TopoDS_Shape s; BRep_Builder bb;
        BRepTools::Read(s, is, bb);
        return wrap(s);
    CATCH(nullptr)
}

// ---------------------------------------------------------------- drawings
// Hidden-line projection of a shape onto a plane. dir = the direction the viewer looks along (view-plane normal points back at the viewer),
// xdir = the drawing's "right" direction. Output: repeated [kind, nPoints, x0, y0, x1, y1, ...] polylines in drawing coordinates (mm, model scale).
// kind: 0 visible sharp edge, 1 visible smooth edge, 2 visible outline, 3 hidden sharp edge, 4 hidden outline.
// Returns the number of doubles written, or -(needed) when the buffer is too small, or -1 on failure.
static void hlrWrite(const TopoDS_Shape& comp, int kind, double defl, std::vector<double>& out) {
    if (comp.IsNull()) return;
    for (TopExp_Explorer ex(comp, TopAbs_EDGE); ex.More(); ex.Next()) {
        TopoDS_Edge e = TopoDS::Edge(ex.Current());
        BRepAdaptor_Curve c(e);
        std::vector<gp_Pnt> pts;
        if (c.GetType() == GeomAbs_Line) { pts.push_back(c.Value(c.FirstParameter())); pts.push_back(c.Value(c.LastParameter())); }
        else {
            GCPnts_QuasiUniformDeflection d(c, defl);
            if (d.IsDone()) for (int i = 1; i <= d.NbPoints(); ++i) pts.push_back(d.Value(i));
            if (pts.size() < 2) { pts.clear(); pts.push_back(c.Value(c.FirstParameter())); pts.push_back(c.Value(c.LastParameter())); }
        }
        out.push_back(kind); out.push_back((double)pts.size());
        for (auto& p : pts) { out.push_back(p.X()); out.push_back(p.Y()); }
    }
}
CK_API int ck_hlr(void* s, const double* dir, const double* xdir, double defl, double* output, int maxDoubles) {
    TRY
        Handle(HLRBRep_Algo) algo = new HLRBRep_Algo();
        algo->Add(get(s));
        gp_Ax2 cs(gp_Pnt(0, 0, 0), gp_Dir(-dir[0], -dir[1], -dir[2]), gp_Dir(xdir[0], xdir[1], xdir[2]));
        algo->Projector(HLRAlgo_Projector(cs));
        algo->Update();
        algo->Hide();
        HLRBRep_HLRToShape h(algo);
        std::vector<double> out;
        hlrWrite(h.VCompound(), 0, defl, out);
        hlrWrite(h.Rg1LineVCompound(), 1, defl, out);
        hlrWrite(h.OutLineVCompound(), 2, defl, out);
        hlrWrite(h.HCompound(), 3, defl, out);
        hlrWrite(h.OutLineHCompound(), 4, defl, out);
        if ((int)out.size() > maxDoubles) return -(int)out.size();
        std::copy(out.begin(), out.end(), output);
        return (int)out.size();
    CATCH(-1)
}

// Cylindrical faces (holes, bosses, fillets): 11 doubles each = location (3), axis direction (3), radius, v min, v max (along the axis from location),
// angular span (radians), hole flag (1 = material lies outside the surface, i.e. a hole or concave fillet). Returns the number of faces.
CK_API int ck_cylinders(void* s, double* out, int maxFaces) {
    TRY
        int n = 0;
        for (TopExp_Explorer ex(get(s), TopAbs_FACE); ex.More() && n < maxFaces; ex.Next()) {
            TopoDS_Face f = TopoDS::Face(ex.Current());
            BRepAdaptor_Surface su(f);
            if (su.GetType() != GeomAbs_Cylinder) continue;
            gp_Cylinder cy = su.Cylinder();
            double u1 = su.FirstUParameter(), u2 = su.LastUParameter(), v1 = su.FirstVParameter(), v2 = su.LastVParameter();
            gp_Pnt loc = cy.Location(); gp_Dir ax = cy.Axis().Direction();
            BRepLProp_SLProps pr(su, 0.5 * (u1 + u2), 0.5 * (v1 + v2), 1, 1e-6);
            double hole = 0;
            if (pr.IsNormalDefined()) {
                gp_Dir nrm = pr.Normal(); if (f.Orientation() == TopAbs_REVERSED) nrm.Reverse();
                gp_Pnt p = pr.Value();
                gp_Vec rad(loc, p); rad -= gp_Vec(ax) * rad.Dot(gp_Vec(ax));
                hole = (gp_Vec(nrm).Dot(rad) < 0) ? 1 : 0;
            }
            double* o = out + 11 * n++;
            o[0] = loc.X(); o[1] = loc.Y(); o[2] = loc.Z(); o[3] = ax.X(); o[4] = ax.Y(); o[5] = ax.Z();
            o[6] = cy.Radius(); o[7] = v1; o[8] = v2; o[9] = u2 - u1; o[10] = hole;
        }
        return n;
    CATCH(0)
}

// Analytic cylinder coordinates retained by Flatten for an invertible layout.
CK_API int ck_cylinder_frame(void* shape, double* out) {
    TRY
        TopExp_Explorer faces(get(shape),TopAbs_FACE);if(!faces.More())return 0;
        auto face = TopoDS::Face(faces.Current());
        BRepAdaptor_Surface surface(face);
        if (surface.GetType() != GeomAbs_Cylinder) return 0;
        auto cylinder = surface.Cylinder(); auto origin = cylinder.Location();
        auto axis = cylinder.Axis().Direction(); auto x = cylinder.Position().XDirection();
        out[0]=origin.X();out[1]=origin.Y();out[2]=origin.Z();
        out[3]=axis.X();out[4]=axis.Y();out[5]=axis.Z();
        out[6]=x.X();out[7]=x.Y();out[8]=x.Z();out[9]=cylinder.Radius();
        BRepTools::UVBounds(face,out[10],out[11],out[12],out[13]);
        return 1;
    CATCH(0)
}

// Affinely map flat curve p-curves into cylinder UV, preserving conics/splines.
CK_API void* ck_cylinder_refold(void* flat, const double* frame, double flatX, double flatY, double tolerance) {
    TRY
        gp_Ax3 axes(gp_Pnt(frame[0],frame[1],frame[2]),gp_Dir(frame[3],frame[4],frame[5]),gp_Dir(frame[6],frame[7],frame[8]));
        Handle(Geom_CylindricalSurface) surface=new Geom_CylindricalSurface(axes,frame[9]);
        BRep_Builder builder;TopoDS_Compound result;builder.MakeCompound(result);int count=0;
        for(TopExp_Explorer ex(get(flat),TopAbs_EDGE);ex.More();ex.Next()) {
            double first,last;auto curve=BRep_Tool::Curve(TopoDS::Edge(ex.Current()),first,last);
            if(curve.IsNull())continue;
            for(int i=0;i<=64;i++) {
                auto p=curve->Value(first+(last-first)*i/64.0);
                double u=(p.X()-flatX)/frame[9]+frame[10],v=p.Y()-flatY+frame[12];
                if(std::abs(p.Z())>tolerance || u<frame[10]-tolerance/frame[9] || u>frame[11]+tolerance/frame[9] || v<frame[12]-tolerance || v>frame[13]+tolerance) {
                    fail("The drawing must lie inside the original flat pattern on Z = 0.");return nullptr;
                }
            }
            auto planar=GeomAPI::To2d(curve,gp_Pln(gp::XOY()));
            auto spline=Geom2dConvert::CurveToBSplineCurve(new Geom2d_TrimmedCurve(planar,first,last));
            for(int i=1;i<=spline->NbPoles();i++) {
                auto p=spline->Pole(i);spline->SetPole(i,gp_Pnt2d((p.X()-flatX)/frame[9]+frame[10],p.Y()-flatY+frame[12]));
            }
            BRepBuilderAPI_MakeEdge edge(spline,surface);
            if(!edge.IsDone()){fail("Could not refold a curve onto the cylinder.");return nullptr;}
            builder.Add(result,edge.Edge());count++;
        }
        if(count==0){fail("Select a sketch or curve to refold.");return nullptr;}
        BRepLib::BuildCurves3d(result,tolerance);
        return wrap(result);
    CATCH(nullptr)
}

// Restore the actual trimmed flat face, rather than copying its uncut source.
CK_API void* ck_cylinder_refold_face(void* flat, const double* frame, double flatX, double flatY, double tolerance) {
    TRY
        TopExp_Explorer faces(get(flat),TopAbs_FACE);
        if(!faces.More()){fail("Select the flat surface, not its outline.");return nullptr;}
        auto face=TopoDS::Face(faces.Current());faces.Next();
        if(faces.More()){fail("Refold one connected flat face at a time.");return nullptr;}
        auto outer=BRepTools::OuterWire(face);
        BRepBuilderAPI_MakeFace outerFlat(outer,true);GProp_GProps outerProps;
        if(!outerFlat.IsDone()){fail("The flat face has no valid outer boundary.");return nullptr;}
        BRepGProp::SurfaceProperties(outerFlat.Face(),outerProps);
        double rectangleArea=frame[9]*(frame[11]-frame[10])*(frame[13]-frame[12]);
        if(std::abs(outerProps.Mass()-rectangleArea)>std::max(1e-5,rectangleArea*1e-7)) {
            fail("Refold currently supports interior cutouts; keep the original outer boundary of the flat pattern.");return nullptr;
        }
        gp_Ax3 axes(gp_Pnt(frame[0],frame[1],frame[2]),gp_Dir(frame[3],frame[4],frame[5]),gp_Dir(frame[6],frame[7],frame[8]));
        Handle(Geom_CylindricalSurface) surface=new Geom_CylindricalSurface(axes,frame[9]);
        BRepBuilderAPI_MakeFace result(surface,frame[10],frame[11],frame[12],frame[13],tolerance);
        for(TopExp_Explorer wires(face,TopAbs_WIRE);wires.More();wires.Next()) {
            auto wire=TopoDS::Wire(wires.Current());if(wire.IsSame(outer))continue;
            BRepBuilderAPI_MakeWire mapped;
            for(BRepTools_WireExplorer edges(wire,face);edges.More();edges.Next()) {
                auto original=edges.Current();double first,last;auto curve=BRep_Tool::Curve(original,first,last);
                if(curve.IsNull()){fail("A cutout boundary has no curve.");return nullptr;}
                for(int i=0;i<=64;i++) {
                    auto p=curve->Value(first+(last-first)*i/64.0);
                    double u=(p.X()-flatX)/frame[9]+frame[10],v=p.Y()-flatY+frame[12];
                    if(std::abs(p.Z())>tolerance||u<=frame[10]||u>=frame[11]||v<=frame[12]||v>=frame[13]) {
                        fail("Cutouts must stay inside the original flat boundary, away from the cylinder seam.");return nullptr;
                    }
                }
                auto planar=GeomAPI::To2d(curve,gp_Pln(gp::XOY()));
                auto spline=Geom2dConvert::CurveToBSplineCurve(new Geom2d_TrimmedCurve(planar,first,last));
                for(int i=1;i<=spline->NbPoles();i++) {
                    auto p=spline->Pole(i);spline->SetPole(i,gp_Pnt2d((p.X()-flatX)/frame[9]+frame[10],p.Y()-flatY+frame[12]));
                }
                BRepBuilderAPI_MakeEdge make(spline,surface);if(!make.IsDone()){fail("Could not map a cutout edge.");return nullptr;}
                auto edge=make.Edge();edge.Orientation(original.Orientation());mapped.Add(edge);
            }
            if(!mapped.IsDone()){fail("A refolded cutout did not form a closed wire.");return nullptr;}
            auto hole=mapped.Wire();BRepLib::BuildCurves3d(hole,tolerance);result.Add(hole);
        }
        if(!result.IsDone()){fail("Could not build the cut cylindrical face.");return nullptr;}
        ShapeFix_Face fix(result.Face());fix.SetPrecision(tolerance);fix.Perform();auto restored=fix.Face();
        if(!BRepCheck_Analyzer(restored).IsValid()){fail("The refolded cut face is invalid; check the cutout boundaries.");return nullptr;}
        GProp_GProps flatProps,foldedProps;BRepGProp::SurfaceProperties(face,flatProps,1e-9);BRepGProp::SurfaceProperties(restored,foldedProps,1e-9);
        if(std::abs(flatProps.Mass()-foldedProps.Mass())>std::max(.001,flatProps.Mass()*1e-6)) {
            auto message=std::string("Refolding changed the cut surface area (")+std::to_string(flatProps.Mass())+" to "+std::to_string(foldedProps.Mass())+"); the result was rejected.";fail(message.c_str());return nullptr;
        }
        return wrap(restored);
    CATCH(nullptr)
}

#include "surface_mapping.inc"

// The part of a shape on the negative side of a plane (for section views).
CK_API void* ck_halve(void* s, const double* o, const double* nrm) {
    TRY
        gp_Pln pl(gp_Pnt(o[0], o[1], o[2]), gp_Dir(nrm[0], nrm[1], nrm[2]));
        TopoDS_Face pf = BRepBuilderAPI_MakeFace(pl).Face();
        gp_Pnt away(o[0] + nrm[0], o[1] + nrm[1], o[2] + nrm[2]);
        BRepPrimAPI_MakeHalfSpace hs(pf, away);
        BRepAlgoAPI_Cut cut(get(s), hs.Solid());
        cut.Build();
        if (!cut.IsDone() || cut.Shape().IsNull()) { fail("Could not cut the model for the section view."); return nullptr; }
        return wrap(cut.Shape());
    CATCH(nullptr)
}

// ---------------------------------------------------------------- deeper extrude / sweep
// A prism with drafted (tapered) sides. dir = the direction to pull in; angle in radians (positive narrows the shape as it goes).
CK_API void* ck_extrude_draft(void* profile, const double* dir, double height, double angle) {
    TRY
        TopoDS_Shape s = get(profile); TopoDS_Face f;
        if (s.ShapeType() == TopAbs_FACE) f = TopoDS::Face(s);
        else { TopExp_Explorer ex(s, TopAbs_FACE); if (!ex.More()) { fail("A tapered extrude needs a closed profile (a face)."); return nullptr; } f = TopoDS::Face(ex.Current()); }
        gp_Vec d(dir[0], dir[1], dir[2]);
        Bnd_Box fb; BRepBndLib::Add(f, fb);
        gp_Pnt fc((fb.CornerMin().X() + fb.CornerMax().X()) / 2, (fb.CornerMin().Y() + fb.CornerMax().Y()) / 2, (fb.CornerMin().Z() + fb.CornerMax().Z()) / 2);
        for (int attempt = 0; attempt < 2; ++attempt) {
            LocOpe_DPrism dp(f, height, angle);
            TopoDS_Shape r = dp.Shape();
            if (r.IsNull()) { fail("The taper is too steep for this profile."); return nullptr; }
            Bnd_Box rb; BRepBndLib::Add(r, rb);
            gp_Pnt rc((rb.CornerMin().X() + rb.CornerMax().X()) / 2, (rb.CornerMin().Y() + rb.CornerMax().Y()) / 2, (rb.CornerMin().Z() + rb.CornerMax().Z()) / 2);
            if (gp_Vec(fc, rc).Dot(d) >= 0) return wrap(r);
            f.Reverse();                                   // the prism grew the wrong way: flip the face and try again
        }
        fail("Could not taper in that direction."); return nullptr;
    CATCH(nullptr)
}

// Sweep with control: mode 0 = follow the path (corrected Frenet), 1 = keep the profile's orientation (parallel), 2 = follow with a fixed binormal direction (bx, by, bz).
// endScale resizes the profile linearly along the path (1 = none).
CK_API void* ck_sweep2(void* profile, void* path, int makeSolid, int mode, double endScale, const double* binormal) {
    TRY
        TopoDS_Wire spine = asWire(get(path)), prof = asWire(get(profile));
        BRepOffsetAPI_MakePipeShell ps(spine);
        if (mode == 1) {
            Bnd_Box pb; BRepBndLib::Add(prof, pb);
            gp_Pnt c((pb.CornerMin().X() + pb.CornerMax().X()) / 2, (pb.CornerMin().Y() + pb.CornerMax().Y()) / 2, (pb.CornerMin().Z() + pb.CornerMax().Z()) / 2);
            BRepAdaptor_Curve first(TopoDS::Edge(BRepTools_WireExplorer(spine).Current()));
            gp_Pnt p; gp_Vec t; first.D1(first.FirstParameter(), p, t);
            ps.SetMode(gp_Ax2(p, gp_Dir(t)));
        }
        else if (mode == 2) ps.SetMode(gp_Dir(binormal[0], binormal[1], binormal[2]));
        else ps.SetMode(false);
        if (std::abs(endScale - 1.0) > 1e-6) {
            Handle(Law_Linear) lin = new Law_Linear(); lin->Set(0.0, 1.0, 1.0, endScale);
            ps.SetLaw(prof, lin, Standard_False, Standard_False);
        } else ps.Add(prof);
        ps.Build();
        if (!ps.IsDone()) { fail("Sweep failed."); return nullptr; }
        if (makeSolid) ps.MakeSolid();
        return wrap(ps.Shape());
    CATCH(nullptr)
}

// ---------------------------------------------------------------- surface tools: untrim, reverse, extend
static TopoDS_Face firstFace(const TopoDS_Shape& s) {
    if (s.ShapeType() == TopAbs_FACE) return TopoDS::Face(s);
    TopExp_Explorer ex(s, TopAbs_FACE);
    if (!ex.More()) throw Standard_Failure("There is no face to work on.");
    return TopoDS::Face(ex.Current());
}

// Untrim: mode 0 gives the face its whole natural surface back, mode 1 keeps the outer edge and removes the holes.
CK_API void* ck_untrim(void* face, int mode) {
    TRY
        TopoDS_Face f = firstFace(get(face));
        TopLoc_Location loc;
        Handle(Geom_Surface) surf = BRep_Tool::Surface(f, loc);
        if (surf.IsNull()) { fail("That face has no surface."); return nullptr; }
        TopoDS_Face out;
        if (mode == 0) {
            Standard_Real u1, u2, v1, v2; surf->Bounds(u1, u2, v1, v2);
            if (Precision::IsInfinite(u1) || Precision::IsInfinite(u2) || Precision::IsInfinite(v1) || Precision::IsInfinite(v2)) {
                fail("This surface has no natural edge (it is a plane or an endless surface). Use 'Fill the holes' instead."); return nullptr;
            }
            BRepBuilderAPI_MakeFace mk(surf, u1, u2, v1, v2, Precision::Confusion());
            out = mk.Face();
        } else {
            TopoDS_Wire ow = BRepTools::OuterWire(f);
            BRepBuilderAPI_MakeFace mk(surf, ow, Standard_True);
            if (!mk.IsDone()) { fail("Could not remove the holes."); return nullptr; }
            out = mk.Face();
            if (f.Orientation() == TopAbs_REVERSED) out.Reverse();
        }
        if (!loc.IsIdentity()) out.Move(loc);
        return wrap(out);
    CATCH(nullptr)
}

CK_API void* ck_reverse(void* s) {
    TRY return wrap(get(s).Reversed()); CATCH(nullptr)
}

// Extend the edge of a face nearest to the point 'nearPt' by 'length', continuing the surface smoothly.
CK_API void* ck_extend_face(void* face, const double* nearPt, double length) {
    TRY
        TopoDS_Face f = firstFace(get(face));
        TopLoc_Location loc;
        Handle(Geom_Surface) s0 = BRep_Tool::Surface(f, loc);
        Handle(Geom_BSplineSurface) bs;
        if (!s0.IsNull() && !Handle(Geom_BSplineSurface)::DownCast(s0).IsNull()) bs = Handle(Geom_BSplineSurface)::DownCast(s0->Copy());
        else {
            BRepBuilderAPI_NurbsConvert nc(f, Standard_True);
            TopoDS_Face f2 = firstFace(nc.Shape());
            TopLoc_Location loc2;
            Handle(Geom_Surface) s2 = BRep_Tool::Surface(f2, loc2);
            if (s2.IsNull()) { fail("That surface cannot be extended."); return nullptr; }
            Handle(Geom_BSplineSurface) b2 = Handle(Geom_BSplineSurface)::DownCast(s2);
            if (b2.IsNull()) { fail("That surface cannot be extended."); return nullptr; }
            bs = Handle(Geom_BSplineSurface)::DownCast(b2->Copy()); loc = loc2;
        }
        if (bs.IsNull()) { fail("That surface cannot be extended."); return nullptr; }
        if (!loc.IsIdentity()) bs->Transform(loc.Transformation());
        Standard_Real u1, u2, v1, v2; bs->Bounds(u1, u2, v1, v2);
        gp_Pnt q(nearPt[0], nearPt[1], nearPt[2]);
        double best = 1e300; bool inU = true, after = false;
        for (int side = 0; side < 4; ++side) {
            double d = 1e300;
            for (int k = 0; k <= 8; ++k) {
                double t = k / 8.0;
                gp_Pnt p = side == 0 ? bs->Value(u1, v1 + (v2 - v1) * t) : side == 1 ? bs->Value(u2, v1 + (v2 - v1) * t)
                         : side == 2 ? bs->Value(u1 + (u2 - u1) * t, v1) : bs->Value(u1 + (u2 - u1) * t, v2);
                d = std::min(d, p.Distance(q));
            }
            if (d < best) { best = d; inU = side < 2; after = (side == 1 || side == 3); }
        }
        Handle(Geom_BoundedSurface) bsurf = bs;
        GeomLib::ExtendSurfByLength(bsurf, length, 1, inU, after);
        BRepBuilderAPI_MakeFace mk(bsurf, Precision::Confusion());
        return wrap(mk.Face());
    CATCH(nullptr)
}

// ================================================================= NURBS toolkit additions
namespace {
TopoDS_Edge firstEdgeOf(const TopoDS_Shape& s) {
    if (s.ShapeType() == TopAbs_EDGE) return TopoDS::Edge(s);
    TopExp_Explorer ex(s, TopAbs_EDGE);
    if (!ex.More()) throw Standard_Failure("Shape has no curve.");
    return TopoDS::Edge(ex.Current());
}
Handle(Geom_BSplineCurve) bsplineOfEdge(const TopoDS_Edge& e) {
    double f, l; Handle(Geom_Curve) c = BRep_Tool::Curve(e, f, l);
    if (c.IsNull()) throw Standard_Failure("Edge has no curve.");
    Handle(Geom_TrimmedCurve) t = new Geom_TrimmedCurve(c, f, l);
    Handle(Geom_BSplineCurve) b = GeomConvert::CurveToBSplineCurve(t);
    if (e.Orientation() == TopAbs_REVERSED) b->Reverse();
    return b;
}
}

// Round (or chamfer) the corners of a flat wire. idx = 0-based corner numbers, n = 0 means every corner. Open wires keep their two ends.
CK_API void* ck_wire_fillet2d(void* s, double r, int chamfer, const int* idx, int n) {
    TRY
        TopoDS_Wire w = asWire(get(s));
        bool closed = BRep_Tool::IsClosed(w);
        TopoDS_Vertex vA, vB; TopoDS_Wire wc = w;
        if (!closed) {
            TopExp::Vertices(w, vA, vB);
            if (vA.IsSame(vB)) closed = true;
            else {
                TopoDS_Edge closing = BRepBuilderAPI_MakeEdge(vB, vA).Edge();
                BRepBuilderAPI_MakeWire mw; mw.Add(w); mw.Add(closing); wc = mw.Wire();
            }
        }
        BRepBuilderAPI_MakeFace mf(wc, Standard_True);
        if (!mf.IsDone()) { fail("The curve has to be flat (planar) to round its corners."); return nullptr; }
        TopoDS_Face face = mf.Face();
        BRepFilletAPI_MakeFillet2d mk(face);
        TopTools_IndexedMapOfShape vm; TopExp::MapShapes(face, TopAbs_VERTEX, vm);
        TopTools_IndexedDataMapOfShapeListOfShape va; TopExp::MapShapesAndAncestors(face, TopAbs_VERTEX, TopAbs_EDGE, va);
        int added = 0;
        for (int i = 1; i <= vm.Extent(); ++i) {
            if (n > 0) { bool in = false; for (int k = 0; k < n; ++k) if (idx[k] + 1 == i) in = true; if (!in) continue; }
            TopoDS_Vertex v = TopoDS::Vertex(vm(i));
            if (!closed && (v.IsSame(vA) || v.IsSame(vB))) continue;
            if (chamfer) {
                const TopTools_ListOfShape& es = va.FindFromKey(v);
                if (es.Extent() != 2) continue;
                mk.AddChamfer(TopoDS::Edge(es.First()), TopoDS::Edge(es.Last()), r, r);
            } else mk.AddFillet(v, r);
            ++added;
        }
        if (added == 0) { fail("There are no corners to round."); return nullptr; }
        mk.Build();
        if (!mk.IsDone()) { fail("The radius is too large for these segments."); return nullptr; }
        TopoDS_Wire out = BRepTools::OuterWire(TopoDS::Face(mk.Shape()));
        if (closed) return wrap(out);
        // drop the closing edge again: it is the one joining the two original ends
        gp_Pnt pA = BRep_Tool::Pnt(vA), pB = BRep_Tool::Pnt(vB);
        Handle(TopTools_HSequenceOfShape) edges = new TopTools_HSequenceOfShape();
        for (TopExp_Explorer ex(out, TopAbs_EDGE); ex.More(); ex.Next()) {
            TopoDS_Edge e = TopoDS::Edge(ex.Current()); TopoDS_Vertex a, b; TopExp::Vertices(e, a, b);
            gp_Pnt qa = BRep_Tool::Pnt(a), qb = BRep_Tool::Pnt(b);
            bool joins = (qa.Distance(pA) < 1e-6 && qb.Distance(pB) < 1e-6) || (qa.Distance(pB) < 1e-6 && qb.Distance(pA) < 1e-6);
            if (!joins) edges->Append(e);
        }
        Handle(TopTools_HSequenceOfShape) wires;
        ShapeAnalysis_FreeBounds::ConnectEdgesToWires(edges, 1e-4, false, wires);
        if (wires.IsNull() || wires->Length() == 0) { fail("Could not rebuild the rounded curve."); return nullptr; }
        return wrap(wires->Value(1));
    CATCH(nullptr)
}

// Project curves onto a surface or shell. mode 0: along dir (tried both ways), mode 1: normal to the surface ("pull").
CK_API void* ck_project_curve(void* curves, void* target, const double* dir, int mode) {
    TRY
        BRep_Builder bb; TopoDS_Compound out; bb.MakeCompound(out); int count = 0;
        if (mode == 1) {
            BRepOffsetAPI_NormalProjection np(get(target));
            for (TopExp_Explorer ex(get(curves), TopAbs_EDGE); ex.More(); ex.Next()) np.Add(ex.Current());
            np.SetLimit(Standard_True);
            np.Compute3d();
            np.Build();
            if (!np.IsDone() || np.Shape().IsNull()) { fail("The curve could not be pulled onto that surface."); return nullptr; }
            return wrap(np.Shape());
        }
        for (int sign = 1; sign >= -1 && count == 0; sign -= 2) {
            gp_Dir d(dir[0] * sign, dir[1] * sign, dir[2] * sign);
            for (TopExp_Explorer ex(get(curves), TopAbs_EDGE); ex.More(); ex.Next()) {
                BRepProj_Projection pr(ex.Current(), get(target), d);
                if (!pr.IsDone()) continue;
                for (TopExp_Explorer re(pr.Shape(), TopAbs_EDGE); re.More(); re.Next()) { bb.Add(out, re.Current()); ++count; }
            }
        }
        if (count == 0) { fail("The curve does not land on that surface in this direction."); return nullptr; }
        return wrap(out);
    CATCH(nullptr)
}

// Points of a face's own surface on an nu x nv grid across its parameter range (row-major, u first).
CK_API int ck_surface_sample(void* face, int nu, int nv, double* out) {
    TRY
        TopoDS_Shape fs = get(face);
        if (fs.ShapeType() != TopAbs_FACE) { TopExp_Explorer ex(fs, TopAbs_FACE); if (!ex.More()) { fail("Shape has no face."); return 0; } fs = ex.Current(); }
        BRepAdaptor_Surface ad(TopoDS::Face(fs), Standard_True);
        double u0 = ad.FirstUParameter(), u1 = ad.LastUParameter(), v0 = ad.FirstVParameter(), v1 = ad.LastVParameter();
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                gp_Pnt p = ad.Value(u0 + (u1 - u0) * i / std::max(nu - 1, 1), v0 + (v1 - v0) * j / std::max(nv - 1, 1));
                double* o = out + 3 * ((size_t)i * nv + j); o[0] = p.X(); o[1] = p.Y(); o[2] = p.Z();
            }
        return nu * nv;
    CATCH(0)
}

// A curve of constant U (dir 0) or constant V (dir 1) at fraction t (0..1) of the surface's range.
CK_API void* ck_isocurve(void* face, int dir, double t) {
    TRY
        TopoDS_Shape fs = get(face);
        if (fs.ShapeType() != TopAbs_FACE) { TopExp_Explorer ex(fs, TopAbs_FACE); if (!ex.More()) { fail("Shape has no face."); return nullptr; } fs = ex.Current(); }
        TopoDS_Face f = TopoDS::Face(fs);
        double u0, u1, v0, v1; BRepTools::UVBounds(f, u0, u1, v0, v1);
        TopLoc_Location loc; Handle(Geom_Surface) g = BRep_Tool::Surface(f, loc);
        if (g.IsNull()) { fail("Face has no surface."); return nullptr; }
        Handle(Geom_Curve) c; double a, b;
        if (dir == 0) { c = g->UIso(u0 + (u1 - u0) * t); a = v0; b = v1; } else { c = g->VIso(v0 + (v1 - v0) * t); a = u0; b = u1; }
        if (c.IsNull()) { fail("No iso curve there."); return nullptr; }
        TopoDS_Shape e = BRepBuilderAPI_MakeEdge(c, a, b).Edge();
        if (!loc.IsIdentity()) e.Move(loc);
        return wrap(e);
    CATCH(nullptr)
}

// A surface spanning two, three or four boundary curves (Coons style). The curves may come in any order and direction.
CK_API void* ck_edge_surface(void** edges, int n) {
    TRY
        if (n < 2 || n > 4) { fail("Edge surface needs two, three or four curves."); return nullptr; }
        std::vector<Handle(Geom_BSplineCurve)> cs;
        double scale = 1;
        for (int i = 0; i < n; ++i) {
            TopoDS_Edge e = firstEdgeOf(get(edges[i])); cs.push_back(bsplineOfEdge(e));
            Bnd_Box bx; BRepBndLib::Add(e, bx); double x0, y0, z0, x1, y1, z1; bx.Get(x0, y0, z0, x1, y1, z1);
            scale = std::max(scale, std::sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0)));
        }
        double tol = std::max(1e-3, scale * 2e-3);
        if (n == 2) {
            // a ruled surface between the two curves, the second one turned to the nearer direction
            if (cs[0]->StartPoint().Distance(cs[1]->StartPoint()) + cs[0]->EndPoint().Distance(cs[1]->EndPoint()) >
                cs[0]->StartPoint().Distance(cs[1]->EndPoint()) + cs[0]->EndPoint().Distance(cs[1]->StartPoint())) cs[1]->Reverse();
            Handle(Geom_Surface) rs = GeomFill::Surface(cs[0], cs[1]);
            return wrap(BRepBuilderAPI_MakeFace(rs, Precision::Confusion()).Face());
        }
        // chain the curves end to end
        std::vector<Handle(Geom_BSplineCurve)> chain{ cs[0] }; std::vector<bool> used(n, false); used[0] = true;
        std::vector<int> chainIdx{ 0 }; // input position of each chained curve, for the error messages
        auto gapMsg = [](const char* what, int a, int b, double gap) {
            char buf[200];
            snprintf(buf, sizeof buf, "%s: curve %d and curve %d are %.4g apart (model units). Move their ends together or trim them to meet.", what, a, b, gap);
            return std::string(buf);
        };
        for (int k = 1; k < n; ++k) {
            gp_Pnt tail = chain.back()->EndPoint(); int pick = -1; bool flip = false; double best = 1e300;
            for (int i = 0; i < n; ++i) {
                if (used[i]) continue;
                double ds = cs[i]->StartPoint().Distance(tail), de = cs[i]->EndPoint().Distance(tail);
                if (ds < best) { best = ds; pick = i; flip = false; }
                if (de < best) { best = de; pick = i; flip = true; }
            }
            if (pick < 0 || best > tol) {
                // name the closest curve to the open end and how far off it is
                fail(gapMsg("The curves do not meet end to end", chainIdx.back() + 1, pick + 1, best).c_str());
                return nullptr;
            }
            if (flip) cs[pick]->Reverse();
            used[pick] = true; chain.push_back(cs[pick]); chainIdx.push_back(pick);
        }
        if (n == 4) {
            double closeGap = chain.back()->EndPoint().Distance(chain[0]->StartPoint());
            if (closeGap > tol) { fail(gapMsg("The four curves do not close into a loop", chainIdx.back() + 1, chainIdx[0] + 1, closeGap).c_str()); return nullptr; }
        }
        Handle(Geom_BSplineSurface) surf;
        // some boundary sets are rejected by one filling style but accepted by another
        const GeomFill_FillingStyle styles[3] = { GeomFill_CoonsStyle, GeomFill_CurvedStyle, GeomFill_StretchStyle };
        for (GeomFill_FillingStyle st : styles) {
            try {
                if (n == 3) { GeomFill_BSplineCurves f(chain[0], chain[1], chain[2], st); surf = f.Surface(); }
                else { GeomFill_BSplineCurves f(chain[0], chain[1], chain[2], chain[3], st); surf = f.Surface(); }
                if (!surf.IsNull()) break;
            } catch (Standard_Failure const&) { surf.Nullify(); }
        }
        if (!surf.IsNull()) return wrap(BRepBuilderAPI_MakeFace(surf, Precision::Confusion()).Face());
        // last resort: a plate surface pinned to all the boundary curves
        BRepFill_Filling plate(3, 15, 3, Standard_False, 1e-5, 1e-4);
        for (int i = 0; i < n; ++i) plate.Add(firstEdgeOf(get(edges[i])), GeomAbs_C0);
        plate.Build();
        if (!plate.IsDone()) { fail("Could not build a surface from those curves."); return nullptr; }
        return wrap(plate.Face());
    CATCH(nullptr)
}

// Fillet whose radius changes linearly from r1 at one end of each edge to r2 at the other.
CK_API void* ck_fillet_var(void* s, const int* edgeIds, int n, double r1, double r2) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_EDGE, m);
        BRepFilletAPI_MakeFillet mk(get(s));
        for (int i = 0; i < n; ++i) mk.Add(r1, r2, TopoDS::Edge(m(edgeIds[i] + 1)));
        mk.Build();
        if (!mk.IsDone()) { fail("Fillet failed. Try smaller radii."); return nullptr; }
        if (!BRepCheck_Analyzer(mk.Shape()).IsValid()) { fail("Fillet produced invalid geometry. Try smaller radii or different edges."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}

// Two-rail sweep: the profile follows the first rail while the second rail steers its orientation and scale.
CK_API void* ck_sweep_rails(void* profile, void* rail1, void* rail2, int makeSolid) {
    TRY
        BRepOffsetAPI_MakePipeShell ps(asWire(get(rail1)));
        ps.SetMode(asWire(get(rail2)), Standard_False);
        ps.Add(asWire(get(profile)));
        ps.Build();
        if (!ps.IsDone()) { fail("Two-rail sweep failed. Check that the profile touches both rails."); return nullptr; }
        if (makeSolid) ps.MakeSolid();
        return wrap(ps.Shape());
    CATCH(nullptr)
}

// Part of a single curve between the fractions t0 and t1 of its parameter range.
CK_API void* ck_curve_trim(void* s, double t0, double t1) {
    TRY
        TopoDS_Edge e = firstEdgeOf(get(s)); double f, l; Handle(Geom_Curve) c = BRep_Tool::Curve(e, f, l);
        if (c.IsNull()) { fail("Edge has no curve."); return nullptr; }
        if (t0 > t1) std::swap(t0, t1);
        if (t1 - t0 < 1e-9) { fail("That part of the curve has no length."); return nullptr; }
        double a = f + (l - f) * t0, b = f + (l - f) * t1;
        return wrap(BRepBuilderAPI_MakeEdge(c, a, b).Edge());
    CATCH(nullptr)
}

// Lengthen a curve at its start (atEnd 0) or end (1) by length, continuing along its tangent.
CK_API void* ck_curve_extend(void* s, int atEnd, double length) {
    TRY
        Handle(Geom_BSplineCurve) b = bsplineOfEdge(firstEdgeOf(get(s)));
        gp_Pnt p; gp_Vec d; double u = atEnd ? b->LastParameter() : b->FirstParameter(); b->D1(u, p, d);
        if (d.Magnitude() < 1e-12) { fail("The curve has no direction at that end."); return nullptr; }
        gp_Vec dir = d.Normalized(); if (!atEnd) dir.Reverse();
        gp_Pnt target = p.Translated(dir * length);
        Handle(Geom_BoundedCurve) bc = b;
        GeomLib::ExtendCurveToPoint(bc, target, 1, atEnd ? Standard_True : Standard_False);
        return wrap(BRepBuilderAPI_MakeEdge(bc).Edge());
    CATCH(nullptr)
}

// count+1 points spaced evenly along a curve (or a whole wire); the length is returned in out[0..] after the points when room allows.
CK_API int ck_curve_divide(void* s, int count, double* out, double* length) {
    TRY
        TopoDS_Wire w = asWire(get(s)); BRepAdaptor_CompCurve cc(w);
        if (length) *length = GCPnts_AbscissaPoint::Length(cc);
        if (count < 1) return 0;
        GCPnts_UniformAbscissa ua(cc, count + 1);
        if (!ua.IsDone()) { fail("Could not divide that curve."); return 0; }
        int n = ua.NbPoints();
        for (int i = 1; i <= n; ++i) { gp_Pnt p = cc.Value(ua.Parameter(i)); double* o = out + 3 * (i - 1); o[0] = p.X(); o[1] = p.Y(); o[2] = p.Z(); }
        return n;
    CATCH(0)
}

// n evenly spaced samples along an edge of a body or surface: point, the unit direction leaving the surface across that edge
// (in the surface's tangent plane), and the surface normal. out holds 9 numbers per sample. Returns n, or 0 on failure.
CK_API int ck_edge_frames(void* shape, int edgeIdx, int n, double* out) {
    TRY
        TopTools_IndexedMapOfShape me; TopExp::MapShapes(get(shape), TopAbs_EDGE, me);
        if (edgeIdx < 0 || edgeIdx >= me.Extent() || n < 2) { fail("Edge index out of range."); return 0; }
        TopoDS_Edge e = TopoDS::Edge(me(edgeIdx + 1));
        TopTools_IndexedDataMapOfShapeListOfShape anc; TopExp::MapShapesAndAncestors(get(shape), TopAbs_EDGE, TopAbs_FACE, anc);
        if (!anc.Contains(e) || anc.FindFromKey(e).Extent() == 0) { fail("That edge does not belong to a face."); return 0; }
        TopoDS_Face face = TopoDS::Face(anc.FindFromKey(e).First());
        // the edge as the face sees it (its orientation in the face's boundary decides which side is the inside)
        TopAbs_Orientation eo = e.Orientation();
        for (TopExp_Explorer ex(face, TopAbs_EDGE); ex.More(); ex.Next()) if (ex.Current().IsSame(e)) { eo = ex.Current().Orientation(); break; }
        double f, l; Handle(Geom2d_Curve) pc = BRep_Tool::CurveOnSurface(e, face, f, l);
        if (pc.IsNull()) { fail("The edge has no curve on its face."); return 0; }
        BRepAdaptor_Curve c(e); BRepAdaptor_Surface ad(face);
        // somewhere inside the face, to settle which way is "out"
        double u0, u1, v0, v1; BRepTools::UVBounds(face, u0, u1, v0, v1);
        gp_Pnt centre = ad.Value((u0 + u1) / 2, (v0 + v1) / 2);
        for (int i = 0; i < n; ++i) {
            double t = f + (l - f) * i / (n - 1);
            gp_Pnt2d uv = pc->Value(t);
            gp_Pnt p; gp_Vec du, dv; ad.D1(uv.X(), uv.Y(), p, du, dv);
            gp_Vec nrm = du.Crossed(dv); if (nrm.Magnitude() < 1e-14) nrm = gp_Vec(0, 0, 1); nrm.Normalize();
            if (face.Orientation() == TopAbs_REVERSED) nrm.Reverse();
            gp_Pnt q; gp_Vec d1; c.D1(c.FirstParameter() + (c.LastParameter() - c.FirstParameter()) * i / (n - 1), q, d1);
            if (d1.Magnitude() < 1e-14) d1 = gp_Vec(1, 0, 0);
            gp_Vec tangent = d1.Normalized(); if (eo == TopAbs_REVERSED) tangent.Reverse();
            gp_Vec inward = nrm.Crossed(tangent);                    // the inside lies to the left of the boundary direction
            gp_Vec outward = inward.Reversed(); if (outward.Magnitude() < 1e-12) outward = gp_Vec(0, 0, 1);
            outward.Normalize();
            if (outward.Dot(gp_Vec(p, centre)) > 0) outward.Reverse();    // safety: never point towards the middle of the face
            double* o = out + 9 * (size_t)i;
            o[0] = p.X(); o[1] = p.Y(); o[2] = p.Z(); o[3] = outward.X(); o[4] = outward.Y(); o[5] = outward.Z(); o[6] = nrm.X(); o[7] = nrm.Y(); o[8] = nrm.Z();
        }
        return n;
    CATCH(0)
}

// Surface between an edge of shape A and an edge of shape B that meets each with the chosen continuity (0 position, 1 tangent, 2 curvature).
CK_API void* ck_blend_surface(void* a, int edgeA, void* b, int edgeB, int order) {
    TRY
        TopTools_IndexedMapOfShape ma, mb; TopExp::MapShapes(get(a), TopAbs_EDGE, ma); TopExp::MapShapes(get(b), TopAbs_EDGE, mb);
        if (edgeA < 0 || edgeA >= ma.Extent() || edgeB < 0 || edgeB >= mb.Extent()) { fail("Edge index out of range."); return nullptr; }
        TopoDS_Edge ea = TopoDS::Edge(ma(edgeA + 1)), eb = TopoDS::Edge(mb(edgeB + 1));
        TopTools_IndexedDataMapOfShapeListOfShape fa, fb;
        TopExp::MapShapesAndAncestors(get(a), TopAbs_EDGE, TopAbs_FACE, fa); TopExp::MapShapesAndAncestors(get(b), TopAbs_EDGE, TopAbs_FACE, fb);
        GeomAbs_Shape cont = order <= 0 ? GeomAbs_C0 : order == 1 ? GeomAbs_G1 : GeomAbs_G2;
        BRepFill_Filling f(3, 15, 3, Standard_False, 1e-5, 1e-4, 0.01, 0.1, 8, 9);
        if (cont != GeomAbs_C0 && (!fa.Contains(ea) || !fb.Contains(eb))) { fail("Tangent or curvature continuity requires a supporting face at each edge."); return nullptr; }
        if (cont == GeomAbs_C0) { f.Add(ea, GeomAbs_C0); f.Add(eb, GeomAbs_C0); }
        else {
            f.Add(ea, TopoDS::Face(fa.FindFromKey(ea).First()), cont);
            f.Add(eb, TopoDS::Face(fb.FindFromKey(eb).First()), cont);
        }
        TopoDS_Vertex a0, a1, b0, b1; TopExp::Vertices(ea, a0, a1); TopExp::Vertices(eb, b0, b1);
        gp_Pnt pa0 = BRep_Tool::Pnt(a0), pa1 = BRep_Tool::Pnt(a1), pb0 = BRep_Tool::Pnt(b0), pb1 = BRep_Tool::Pnt(b1);
        if (pa0.Distance(pb0) + pa1.Distance(pb1) > pa0.Distance(pb1) + pa1.Distance(pb0)) std::swap(pb0, pb1);
        if (pa0.Distance(pb0) > 1e-7) f.Add(BRepBuilderAPI_MakeEdge(pa0, pb0).Edge(), GeomAbs_C0);
        if (pa1.Distance(pb1) > 1e-7) f.Add(BRepBuilderAPI_MakeEdge(pa1, pb1).Edge(), GeomAbs_C0);
        f.Build();
        if (!f.IsDone()) { fail("Could not build a blend between those edges."); return nullptr; }
        return wrap(f.Face());
    CATCH(nullptr)
}

// Tilt faces so they lean away from a pull direction by an angle (mold release / draft). The neutral plane is the hinge: faces pivot about the line where they meet it.
CK_API void* ck_draft(void* s, const int* faceIds, int n, const double* dir, double angle, const double* po, const double* pn) {
    TRY
        TopTools_IndexedMapOfShape m; TopExp::MapShapes(get(s), TopAbs_FACE, m);
        BRepOffsetAPI_DraftAngle mk(get(s));
        gp_Pln neutral(gp_Pnt(po[0], po[1], po[2]), gp_Dir(pn[0], pn[1], pn[2]));
        gp_Dir d(dir[0], dir[1], dir[2]);
        for (int i = 0; i < n; ++i) {
            if (faceIds[i] < 0 || faceIds[i] >= m.Extent()) { fail("Face index out of range."); return nullptr; }
            mk.Add(TopoDS::Face(m(faceIds[i] + 1)), d, angle, neutral, Standard_True);
            if (!mk.AddDone()) { fail("That face cannot be drafted (it must be flat, round or conical and not parallel to the pull direction)."); return nullptr; }
        }
        mk.Build();
        if (!mk.IsDone()) { fail("Draft failed. Try a smaller angle or a different neutral plane."); return nullptr; }
        if (!BRepCheck_Analyzer(mk.Shape()).IsValid()) { fail("Draft produced invalid geometry. Try a smaller angle."); return nullptr; }
        return wrap(mk.Shape());
    CATCH(nullptr)
}
