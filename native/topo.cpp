// Prism generative design core: SIMP topology optimization on linear tetrahedra.
// Minimizes compliance for a target volume fraction. One call to sim_topo_step is one optimization iteration (the host drives the loop).
// Units: mm, N, MPa.
#include <Eigen/Dense>
#include <Eigen/Sparse>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <string>
#include <unordered_map>
#include <vector>

#define SIM_API extern "C" __declspec(dllexport)

// the finite element mesh type lives in sim.cpp; this file only needs its public layout through these accessors
extern "C" int sim_mesh_counts_internal(void* h, int* nNodes, int* nCorner, int* nTets, int* nBtris);
extern "C" const double* sim_mesh_nodes_internal(void* h);
extern "C" const int* sim_mesh_tets_internal(void* h);     // 10 per tet
extern "C" const int* sim_mesh_btris_internal(void* h);    // 6 per boundary triangle

namespace {
thread_local std::string g_terr;

struct Topo {
    int nCorner = 0, ndof = 0, ne = 0;
    std::vector<std::array<int, 4>> tets;
    std::vector<Eigen::Matrix<double, 12, 12>> ke;   // stiffness for E = 1
    std::vector<double> vol;
    std::vector<Eigen::Vector3d> cen;
    std::vector<double> x;
    std::vector<char> passive;
    std::vector<char> fixed;
    std::vector<int> freeIdx; int nf = 0;
    Eigen::VectorXd f;
    double E0 = 1, Emin = 1e-9, penal = 3, volfrac = 0.3, totalVol = 0;
    std::vector<std::vector<std::pair<int, double>>> nb;   // filter neighbours with weights
    Eigen::SimplicialLDLT<Eigen::SparseMatrix<double>> solver;
    bool analyzed = false;
    int iter = 0;
};

void tetGradients(const double* p[4], double g[4][3], double& vol) {
    Eigen::Matrix4d M;
    for (int i = 0; i < 4; ++i) { M(i, 0) = 1; M(i, 1) = p[i][0]; M(i, 2) = p[i][1]; M(i, 3) = p[i][2]; }
    double det = M.determinant();
    Eigen::Matrix4d Mi = M.inverse();
    for (int a = 0; a < 4; ++a) for (int d = 0; d < 3; ++d) g[a][d] = Mi(d + 1, a);
    vol = std::fabs(det) / 6.0;
}

Eigen::Matrix<double, 6, 6> elasticity(double E, double nu) {
    double l = E * nu / ((1 + nu) * (1 - 2 * nu)), m = E / (2 * (1 + nu));
    Eigen::Matrix<double, 6, 6> D = Eigen::Matrix<double, 6, 6>::Zero();
    for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) D(i, j) = l + (i == j ? 2 * m : 0);
    D(3, 3) = D(4, 4) = D(5, 5) = m;
    return D;
}
}  // namespace

SIM_API const char* sim_topo_error() { return g_terr.c_str(); }

// mat: E (MPa), nu, density. Fixtures: node masks. Loads: boundary tri6 tractions (distributed to corner nodes) and optional gravity.
// keepRadius: elements within this distance of a support or loaded face are kept solid. Returns a context or null.
SIM_API void* sim_topo_create(void* h, const double* mat, int nFix, const int* fixNode, const int* fixMask,
                              int nTrac, const int* tracTri, const double* tracVec, const double* gravity,
                              double volfrac, double penal, double rmin, double keepRadius, int* nElems) {
    try {
        g_terr.clear();
        int nNodes, nCorner, nTets, nBtris;
        sim_mesh_counts_internal(h, &nNodes, &nCorner, &nTets, &nBtris);
        const double* X = sim_mesh_nodes_internal(h);
        const int* T10 = sim_mesh_tets_internal(h);
        const int* B6 = sim_mesh_btris_internal(h);
        auto* c = new Topo();
        c->nCorner = nCorner; c->ndof = 3 * nCorner; c->ne = nTets;
        c->E0 = mat[0]; c->penal = penal; c->volfrac = volfrac;
        auto D = elasticity(1.0, mat[1]);
        c->tets.resize(nTets); c->ke.resize(nTets); c->vol.resize(nTets); c->cen.resize(nTets);
        for (int e = 0; e < nTets; ++e) {
            for (int k = 0; k < 4; ++k) c->tets[e][k] = T10[10 * e + k];
            const double* p[4] = {&X[3 * c->tets[e][0]], &X[3 * c->tets[e][1]], &X[3 * c->tets[e][2]], &X[3 * c->tets[e][3]]};
            double g[4][3], vol; tetGradients(p, g, vol);
            Eigen::Matrix<double, 6, 12> B = Eigen::Matrix<double, 6, 12>::Zero();
            for (int a = 0; a < 4; ++a) {
                B(0, 3 * a) = g[a][0]; B(1, 3 * a + 1) = g[a][1]; B(2, 3 * a + 2) = g[a][2];
                B(3, 3 * a) = g[a][1]; B(3, 3 * a + 1) = g[a][0];
                B(4, 3 * a + 1) = g[a][2]; B(4, 3 * a + 2) = g[a][1];
                B(5, 3 * a) = g[a][2]; B(5, 3 * a + 2) = g[a][0];
            }
            c->ke[e] = vol * (B.transpose() * D * B);
            c->vol[e] = vol; c->totalVol += vol;
            Eigen::Vector3d cc = Eigen::Vector3d::Zero();
            for (int k = 0; k < 4; ++k) cc += Eigen::Vector3d(p[k][0], p[k][1], p[k][2]) * 0.25;
            c->cen[e] = cc;
        }
        // loads on the corner nodes
        c->f = Eigen::VectorXd::Zero(c->ndof);
        std::vector<Eigen::Vector3d> special;
        for (int i = 0; i < nTrac; ++i) {
            const int* b = &B6[6 * tracTri[i]];
            const double* p0 = &X[3 * b[0]]; const double* p1 = &X[3 * b[1]]; const double* p2 = &X[3 * b[2]];
            Eigen::Vector3d e1(p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]), e2(p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]);
            double area = 0.5 * e1.cross(e2).norm();
            for (int k = 0; k < 3; ++k) {
                for (int d = 0; d < 3; ++d) c->f[3 * b[k] + d] += tracVec[3 * i + d] * area / 3.0;
                special.emplace_back(X[3 * b[k]], X[3 * b[k] + 1], X[3 * b[k] + 2]);
            }
        }
        if (gravity) for (int e = 0; e < nTets; ++e) for (int k = 0; k < 4; ++k) for (int d = 0; d < 3; ++d) c->f[3 * c->tets[e][k] + d] += mat[2] * gravity[d] * c->vol[e] / 4.0;
        // supports (corner nodes only)
        c->fixed.assign(c->ndof, 0);
        for (int i = 0; i < nFix; ++i) {
            int nd = fixNode[i];
            special.emplace_back(X[3 * nd], X[3 * nd + 1], X[3 * nd + 2]);
            if (nd >= nCorner) continue;
            for (int d = 0; d < 3; ++d) if (fixMask[i] & (1 << d)) c->fixed[3 * nd + d] = 1;
        }
        c->freeIdx.assign(c->ndof, -1);
        for (int i = 0; i < c->ndof; ++i) if (!c->fixed[i]) c->freeIdx[i] = c->nf++;
        if (c->nf == c->ndof) { g_terr = "There are no supports: the part is free to move."; delete c; return nullptr; }
        // elements next to supports and loads stay solid
        c->passive.assign(nTets, 0);
        for (int e = 0; e < nTets && keepRadius > 0; ++e)
            for (auto& s : special) if ((c->cen[e] - s).norm() <= keepRadius) { c->passive[e] = 1; break; }
        // density filter neighbours (cone weights)
        c->nb.resize(nTets);
        if (rmin > 0) {
            std::unordered_map<long long, std::vector<int>> grid; const double cell = rmin;
            auto key = [&](int ix, int iy, int iz) { return ((long long)(ix + 100000) * 200003 + (iy + 100000)) * 200003 + (iz + 100000); };
            for (int e = 0; e < nTets; ++e) grid[key((int)std::floor(c->cen[e][0] / cell), (int)std::floor(c->cen[e][1] / cell), (int)std::floor(c->cen[e][2] / cell))].push_back(e);
            for (int e = 0; e < nTets; ++e) {
                int ix = (int)std::floor(c->cen[e][0] / cell), iy = (int)std::floor(c->cen[e][1] / cell), iz = (int)std::floor(c->cen[e][2] / cell);
                for (int a = -1; a <= 1; ++a) for (int b = -1; b <= 1; ++b) for (int d = -1; d <= 1; ++d) {
                    auto it = grid.find(key(ix + a, iy + b, iz + d)); if (it == grid.end()) continue;
                    for (int f : it->second) { double dist = (c->cen[e] - c->cen[f]).norm(); if (dist < rmin) c->nb[e].emplace_back(f, rmin - dist); }
                }
            }
        } else for (int e = 0; e < nTets; ++e) c->nb[e].emplace_back(e, 1.0);
        c->x.assign(nTets, volfrac);
        for (int e = 0; e < nTets; ++e) if (c->passive[e]) c->x[e] = 1.0;
        *nElems = nTets;
        return c;
    } catch (const std::exception& ex) { g_terr = ex.what(); return nullptr; }
}

// One iteration. xOut: densities (ne). info: [0] compliance (N mm), [1] volume fraction, [2] largest density change, [3] seconds, [4] passive element count.
SIM_API int sim_topo_step(void* ctx, double* xOut, double* info) {
    try {
        g_terr.clear();
        auto* c = (Topo*)ctx;
        auto t0 = std::chrono::steady_clock::now();
        const int ne = c->ne;
        // stiffness for the current densities
        std::vector<Eigen::Triplet<double>> trips; trips.reserve((size_t)ne * 144);
        for (int e = 0; e < ne; ++e) {
            double Ee = c->Emin + std::pow(c->x[e], c->penal) * (c->E0 - c->Emin);
            for (int i = 0; i < 4; ++i) for (int a = 0; a < 3; ++a) {
                int gi = 3 * c->tets[e][i] + a; int ri = c->freeIdx[gi]; if (ri < 0) continue;
                for (int j = 0; j < 4; ++j) for (int b = 0; b < 3; ++b) {
                    int gj = 3 * c->tets[e][j] + b; int rj = c->freeIdx[gj]; if (rj < 0) continue;
                    trips.emplace_back(ri, rj, Ee * c->ke[e](3 * i + a, 3 * j + b));
                }
            }
        }
        Eigen::SparseMatrix<double> K(c->nf, c->nf); K.setFromTriplets(trips.begin(), trips.end());
        Eigen::VectorXd rhs(c->nf);
        for (int i = 0; i < c->ndof; ++i) if (c->freeIdx[i] >= 0) rhs[c->freeIdx[i]] = c->f[i];
        if (!c->analyzed) { c->solver.analyzePattern(K); c->analyzed = true; }
        c->solver.factorize(K);
        if (c->solver.info() != Eigen::Success) { g_terr = "The stiffness matrix could not be factorized (the part may be under-constrained)."; return 0; }
        Eigen::VectorXd u = c->solver.solve(rhs);
        Eigen::VectorXd U = Eigen::VectorXd::Zero(c->ndof);
        for (int i = 0; i < c->ndof; ++i) if (c->freeIdx[i] >= 0) U[i] = u[c->freeIdx[i]];
        double compliance = c->f.dot(U);

        // sensitivities of compliance, then the neighbourhood filter
        std::vector<double> dc(ne);
        for (int e = 0; e < ne; ++e) {
            Eigen::Matrix<double, 12, 1> ue;
            for (int i = 0; i < 4; ++i) for (int a = 0; a < 3; ++a) ue[3 * i + a] = U[3 * c->tets[e][i] + a];
            double strainEnergy = ue.dot(c->ke[e] * ue);
            dc[e] = -c->penal * std::pow(std::max(c->x[e], 1e-3), c->penal - 1) * (c->E0 - c->Emin) * strainEnergy;
        }
        std::vector<double> dcf(ne);
        for (int e = 0; e < ne; ++e) {
            double s = 0, w = 0;
            for (auto& [f, h] : c->nb[e]) { s += h * c->x[f] * dc[f]; w += h; }
            dcf[e] = s / (std::max(c->x[e], 1e-3) * std::max(w, 1e-12));
        }
        // optimality-criteria update with a bisection on the volume constraint
        double l1 = 0, l2 = 1e12, move = 0.2; std::vector<double> xn(ne);
        double target = c->volfrac * c->totalVol;
        for (int it = 0; it < 80 && (l2 - l1) / (l1 + l2 + 1e-30) > 1e-5; ++it) {
            double lm = 0.5 * (l1 + l2), v = 0;
            for (int e = 0; e < ne; ++e) {
                double be = std::max(-dcf[e], 1e-30) / (lm * c->vol[e]);
                double xe = c->x[e] * std::sqrt(be);
                xe = std::min(std::min(c->x[e] + move, 1.0), std::max(std::max(c->x[e] - move, 1e-3), xe));
                if (c->passive[e]) xe = 1.0;
                xn[e] = xe; v += xe * c->vol[e];
            }
            if (v > target) l1 = lm; else l2 = lm;
        }
        double change = 0, vsum = 0; int np = 0;
        for (int e = 0; e < ne; ++e) { change = std::max(change, std::fabs(xn[e] - c->x[e])); c->x[e] = xn[e]; vsum += xn[e] * c->vol[e]; np += c->passive[e]; xOut[e] = xn[e]; }
        c->iter++;
        info[0] = compliance; info[1] = vsum / c->totalVol; info[2] = change;
        info[3] = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count(); info[4] = np;
        return 1;
    } catch (const std::exception& ex) { g_terr = ex.what(); return 0; }
}

SIM_API void sim_topo_volumes(void* ctx, double* vol) { auto* c = (Topo*)ctx; for (int e = 0; e < c->ne; ++e) vol[e] = c->vol[e]; }
SIM_API void sim_topo_free(void* ctx) { delete (Topo*)ctx; }
