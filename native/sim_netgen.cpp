// Netgen (LGPL) tetrahedral mesher behind a tiny C interface.
#include <stddef.h>
// the shipped library exports everything inside namespace nglib, while the header declares it globally
namespace nglib {
#include <nglib.h>
}
using namespace nglib;
#include <string>
#include <vector>
#include <exception>

#include "prism_exports.h"
#include "sim_validation.h"
#include <memory>
#include <mutex>

extern "C" void sim_clear_error();

extern "C" void* sim_mesh_from_tets(const double* nodes, int nNodes, const int* tets, int nTets, const int* btris, int nBtris);
extern thread_local std::string g_err_ng;
thread_local std::string g_err_ng;
extern "C" void sim_set_error(const char* m) { g_err_ng = m; }

static const char* resultText(Ng_Result r) {
    switch (r) {
    case NG_SURFACE_INPUT_ERROR: return "The surface mesh input is not valid.";
    case NG_VOLUME_FAILURE: return "The volume mesh could not be generated: the body may not be closed or has tiny features. Try a larger element size.";
    case NG_STL_INPUT_ERROR: return "The body's surface could not be read for meshing.";
    case NG_SURFACE_FAILURE: return "The surface could not be meshed. Try a different element size.";
    default: return "Meshing failed.";
    }
}

SIM_API void* sim_netgen_mesh(const float* tris, int nTris, double maxh, double grading, int optSteps) {
    try {
        // Netgen owns process-global state, including STL construction buffers.
        static std::mutex gate;
        std::lock_guard<std::mutex> lock(gate);
        g_err_ng.clear();
        sim_clear_error();
        using namespace sim_validation;
        require(nTris > 0, "The surface has no triangles.");
        finite(tris, nTris, 9);
        require(std::isfinite(maxh) && maxh > 0 && std::isfinite(grading) && grading > 0 && grading <= 1 && optSteps >= 0,
                "Invalid meshing parameters.");
        static bool inited = false;
        if (!inited) { Ng_Init(); inited = true; }
        Ng_STL_Geometry* geom = Ng_STL_NewGeometry();
        for (int i = 0; i < nTris; ++i) {
            double p[3][3];
            for (int k = 0; k < 3; ++k) for (int d = 0; d < 3; ++d) p[k][d] = tris[9 * i + 3 * k + d];
            Ng_STL_AddTriangle(geom, p[0], p[1], p[2]);
        }
        std::unique_ptr<Ng_Mesh, decltype(&Ng_DeleteMesh)> owner(Ng_NewMesh(), &Ng_DeleteMesh);
        Ng_Mesh* mesh = owner.get();
        require(mesh != nullptr, "Could not allocate a Netgen mesh.");
        Ng_Meshing_Parameters mp;
        mp.uselocalh = 1; mp.maxh = maxh; mp.minh = 0; mp.fineness = 0.4; mp.grading = grading;
        mp.elementsperedge = 2; mp.elementspercurve = 2;
        mp.optsteps_2d = optSteps; mp.optsteps_3d = optSteps;
        mp.second_order = 0; mp.check_overlap = 0; mp.check_overlapping_boundary = 0;
        Ng_Result r = Ng_STL_InitSTLGeometry(geom);
        if (r != NG_OK) { g_err_ng = resultText(r); return nullptr; }
        r = Ng_STL_MakeEdges(geom, mesh, &mp);
        if (r != NG_OK) { g_err_ng = resultText(r); return nullptr; }
        r = Ng_STL_GenerateSurfaceMesh(geom, mesh, &mp);
        if (r != NG_OK) { g_err_ng = resultText(r); return nullptr; }
        r = Ng_GenerateVolumeMesh(mesh, &mp);
        if (r != NG_OK) { g_err_ng = resultText(r); return nullptr; }

        int np = Ng_GetNP(mesh), ne = Ng_GetNE(mesh), nse = Ng_GetNSE(mesh);
        if (ne <= 0) { g_err_ng = "The mesher produced no elements."; return nullptr; }
        count(np, 3); count(ne, 4); count(nse, 3);
        std::vector<double> nodes(3 * np); std::vector<int> tets(4 * ne), bt(3 * nse);
        for (int i = 0; i < np; ++i) Ng_GetPoint(mesh, i + 1, &nodes[3 * i]);
        for (int i = 0; i < ne; ++i) { int pi[8]; Ng_GetVolumeElement(mesh, i + 1, pi); for (int k = 0; k < 4; ++k) tets[4 * i + k] = pi[k] - 1; }
        for (int i = 0; i < nse; ++i) { int pi[8]; Ng_GetSurfaceElement(mesh, i + 1, pi); for (int k = 0; k < 3; ++k) bt[3 * i + k] = pi[k] - 1; }
        owner.reset();
        return sim_mesh_from_tets(nodes.data(), np, tets.data(), ne, bt.data(), nse);
    } catch (const std::exception& e) { g_err_ng = e.what(); return nullptr; }
    catch (...) { g_err_ng = "Meshing failed unexpectedly."; return nullptr; }
}

SIM_API const char* sim_netgen_error() { return g_err_ng.c_str(); }
