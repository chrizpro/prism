// Prism simulation core: quadratic tetrahedral finite elements (linear elasticity) on top of Eigen.
// Units: mm, N, MPa (N/mm^2), tonne/mm^3 for density, mm/s^2 for acceleration.
#include <Eigen/Dense>
#include <Eigen/Sparse>
#include <array>
#include <cmath>
#include <cstring>
#include <map>
#include <string>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include <chrono>

#include "prism_exports.h"
#include "sim_validation.h"
#include <memory>

namespace {
thread_local std::string g_err;
void fail(const std::string& m) { g_err = m; }

typedef Eigen::Triplet<double> Trip;

struct FemMesh {
    std::vector<double> X;             // 3 per node (corner nodes first, then mid-edge nodes)
    std::vector<std::array<int, 10>> T; // tet10: 4 corners, then mids of edges 01,12,02,03,13,23
    std::vector<std::array<int, 6>> B;  // boundary tri6: 3 corners, then mids of edges 01,12,20
    int nCorner = 0;
};

// ---- element helpers --------------------------------------------------------------------
// gradients of the four barycentric coordinates for a straight tet; returns signed volume
double baryGradients(const double* p0, const double* p1, const double* p2, const double* p3, double g[4][3]) {
    Eigen::Matrix4d M;
    const double* p[4] = {p0, p1, p2, p3};
    for (int i = 0; i < 4; ++i) { M(i, 0) = 1; M(i, 1) = p[i][0]; M(i, 2) = p[i][1]; M(i, 3) = p[i][2]; }
    double det = M.determinant();
    sim_validation::require(std::isfinite(det) && det != 0, "Mesh contains a degenerate tetrahedron.");
    Eigen::Matrix4d Mi = M.inverse();            // columns: coefficients of L_a = Mi(0,a) + Mi(1,a) x + ...
    for (int a = 0; a < 4; ++a) for (int d = 0; d < 3; ++d) g[a][d] = Mi(d + 1, a);
    return det / 6.0;
}

// shape-function derivatives wrt barycentric coordinates at (L0..L3): dN[i][a]
void dShape(const double L[4], double dN[10][4]) {
    std::memset(dN, 0, sizeof(double) * 40);
    for (int i = 0; i < 4; ++i) dN[i][i] = 4 * L[i] - 1;
    static const int E[6][2] = {{0, 1}, {1, 2}, {0, 2}, {0, 3}, {1, 3}, {2, 3}};
    for (int e = 0; e < 6; ++e) { int a = E[e][0], b = E[e][1]; dN[4 + e][a] = 4 * L[b]; dN[4 + e][b] = 4 * L[a]; }
}

void shape(const double L[4], double N[10]) {
    for (int i = 0; i < 4; ++i) N[i] = L[i] * (2 * L[i] - 1);
    static const int E[6][2] = {{0, 1}, {1, 2}, {0, 2}, {0, 3}, {1, 3}, {2, 3}};
    for (int e = 0; e < 6; ++e) N[4 + e] = 4 * L[E[e][0]] * L[E[e][1]];
}

Eigen::Matrix<double, 6, 6> elasticity(double E, double nu) {
    double l = E * nu / ((1 + nu) * (1 - 2 * nu)), m = E / (2 * (1 + nu));
    Eigen::Matrix<double, 6, 6> D = Eigen::Matrix<double, 6, 6>::Zero();
    for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) D(i, j) = l + (i == j ? 2 * m : 0);
    D(3, 3) = D(4, 4) = D(5, 5) = m;
    return D;
}

// 4-point Gauss rule on the tet (degree 2)
const double GA = 0.5854101966249685, GB = 0.1381966011250105;
const double GP[4][4] = {{GA, GB, GB, GB}, {GB, GA, GB, GB}, {GB, GB, GA, GB}, {GB, GB, GB, GA}};

void bMatrix(const double dN[10][4], const double g[4][3], Eigen::Matrix<double, 6, 30>& B) {
    B.setZero();
    for (int i = 0; i < 10; ++i) {
        double gx = 0, gy = 0, gz = 0;
        for (int a = 0; a < 4; ++a) { gx += dN[i][a] * g[a][0]; gy += dN[i][a] * g[a][1]; gz += dN[i][a] * g[a][2]; }
        B(0, 3 * i) = gx; B(1, 3 * i + 1) = gy; B(2, 3 * i + 2) = gz;
        B(3, 3 * i) = gy; B(3, 3 * i + 1) = gx;
        B(4, 3 * i + 1) = gz; B(4, 3 * i + 2) = gy;
        B(5, 3 * i) = gz; B(5, 3 * i + 2) = gx;
    }
}

double vonMises(const Eigen::Matrix<double, 6, 1>& s) {
    double a = s[0] - s[1], b = s[1] - s[2], c = s[2] - s[0];
    return std::sqrt(0.5 * (a * a + b * b + c * c) + 3 * (s[3] * s[3] + s[4] * s[4] + s[5] * s[5]));
}
}  // namespace

// =======================================================================================
//  mesh container (second order built from corner tets)
// =======================================================================================
extern "C" const char* sim_netgen_error();
SIM_API const char* sim_last_error() { const char* n = sim_netgen_error(); return !g_err.empty() ? g_err.c_str() : n; }
extern "C" void sim_clear_error() { g_err.clear(); }

SIM_API void* sim_mesh_from_tets(const double* nodes, int nNodes, const int* tets, int nTets, const int* btris, int nBtris) {
    try {
        g_err.clear();
        using namespace sim_validation;
        require(nNodes >= 4 && nTets > 0, "A mesh needs nodes and tetrahedra.");
        finite(nodes, nNodes, 3);
        indices(tets, nTets, nNodes, 4);
        indices(btris, nBtris, nNodes, 3);
        // Keep all node/DOF and output offsets representable by the C API's int indices.
        require(nNodes <= std::numeric_limits<int>::max() / 6, "Mesh is too large.");
        auto m = std::make_unique<FemMesh>();
        m->nCorner = nNodes;
        m->X.assign(nodes, nodes + 3 * nNodes);
        std::map<std::pair<int, int>, int> mid;
        auto Mid = [&](int a, int b) {
            auto key = std::make_pair(std::min(a, b), std::max(a, b));
            auto it = mid.find(key);
            if (it != mid.end()) return it->second;
            require(m->X.size() / 3 < std::numeric_limits<int>::max() / 6, "Mesh is too large.");
            int id = (int)(m->X.size() / 3);
            for (int d = 0; d < 3; ++d) m->X.push_back(0.5 * (m->X[3 * a + d] + m->X[3 * b + d]));
            mid[key] = id;
            return id;
        };
        for (int t = 0; t < nTets; ++t) {
            int a = tets[4 * t], b = tets[4 * t + 1], c = tets[4 * t + 2], d = tets[4 * t + 3];
            // make orientation positive
            double g[4][3];
            double v = baryGradients(&m->X[3 * a], &m->X[3 * b], &m->X[3 * c], &m->X[3 * d], g);
            if (v < 0) std::swap(c, d);
            m->T.push_back({a, b, c, d, Mid(a, b), Mid(b, c), Mid(a, c), Mid(a, d), Mid(b, d), Mid(c, d)});
        }
        for (int i = 0; i < nBtris; ++i) {
            int a = btris[3 * i], b = btris[3 * i + 1], c = btris[3 * i + 2];
            m->B.push_back({a, b, c, Mid(a, b), Mid(b, c), Mid(c, a)});
        }
        return m.release();
    } catch (const std::exception& e) { fail(e.what()); return nullptr; }
    catch (...) { fail("Mesh construction failed unexpectedly."); return nullptr; }
}

SIM_API void sim_mesh_sizes(void* h, int* nNodes, int* nCorner, int* nTets, int* nBtris) {
    if (!h || !nNodes || !nCorner || !nTets || !nBtris) { fail("Invalid mesh or output pointers."); return; }
    auto* m = (FemMesh*)h;
    *nNodes = (int)(m->X.size() / 3); *nCorner = m->nCorner; *nTets = (int)m->T.size(); *nBtris = (int)m->B.size();
}

SIM_API void sim_mesh_fill(void* h, double* nodes, int* tets10, int* btris6) {
    if (!h || !nodes || !tets10) { fail("Invalid mesh or output pointers."); return; }
    auto* m = (FemMesh*)h;
    if (!m->B.empty() && !btris6) { fail("Boundary output is null."); return; }
    std::copy(m->X.begin(), m->X.end(), nodes);
    for (size_t i = 0; i < m->T.size(); ++i) for (int k = 0; k < 10; ++k) tets10[10 * i + k] = m->T[i][k];
    for (size_t i = 0; i < m->B.size(); ++i) for (int k = 0; k < 6; ++k) btris6[6 * i + k] = m->B[i][k];
}

SIM_API void sim_mesh_free(void* h) { delete (FemMesh*)h; }

// raw views of the mesh for the topology optimizer (same DLL)
extern "C" int sim_mesh_counts_internal(void* h, int* nNodes, int* nCorner, int* nTets, int* nBtris) {
    if (!h) return 0;
    auto* m = (FemMesh*)h; *nNodes = (int)(m->X.size() / 3); *nCorner = m->nCorner; *nTets = (int)m->T.size(); *nBtris = (int)m->B.size(); return 1;
}
extern "C" const double* sim_mesh_nodes_internal(void* h) { return ((FemMesh*)h)->X.data(); }
extern "C" const int* sim_mesh_tets_internal(void* h) { return reinterpret_cast<const int*>(((FemMesh*)h)->T.data()); }
extern "C" const int* sim_mesh_btris_internal(void* h) { return reinterpret_cast<const int*>(((FemMesh*)h)->B.data()); }

// Element quality: min and mean of (6 sqrt(2) V / edgeRMS^3), 1 = regular tet.
SIM_API void sim_mesh_quality(void* h, double* minQ, double* meanQ, double* minVol) {
    if (!h || !minQ || !meanQ || !minVol) { fail("Invalid mesh or output pointers."); return; }
    auto* m = (FemMesh*)h; double mn = 1e30, sum = 0, mv = 1e30;
    for (auto& t : m->T) {
        double g[4][3]; double v = baryGradients(&m->X[3 * t[0]], &m->X[3 * t[1]], &m->X[3 * t[2]], &m->X[3 * t[3]], g);
        double e2 = 0; static const int E[6][2] = {{0, 1}, {1, 2}, {0, 2}, {0, 3}, {1, 3}, {2, 3}};
        for (auto& e : E) { double s = 0; for (int d = 0; d < 3; ++d) { double x = m->X[3 * t[e[0]] + d] - m->X[3 * t[e[1]] + d]; s += x * x; } e2 += s; }
        double rms = std::sqrt(e2 / 6);
        double q = 6 * std::sqrt(2.0) * std::fabs(v) / (rms * rms * rms);
        mn = std::min(mn, q); sum += q; mv = std::min(mv, std::fabs(v));
    }
    *minQ = mn; *meanQ = m->T.empty() ? 0 : sum / m->T.size(); *minVol = mv;
}

// =======================================================================================
//  static linear elasticity
// =======================================================================================
// fixMask bit0/1/2 = x/y/z constrained to fixVal. Forces: nodal vectors. Traction: per boundary tri6 index, a traction (N/mm^2).
// gravity: acceleration (mm/s^2) or null. Outputs: u (3n), stress (6n: xx yy zz xy yz zx), vm (n), info[0]=iterations or 0, info[1]=sum |reaction|, info[2..4]=reaction sum xyz, info[5]=applied sum |F|
SIM_API int sim_static(void* h, const double* mat, int nFix, const int* fixNode, const int* fixMask, const double* fixVal,
                       int nForce, const int* forceNode, const double* forceVec,
                       int nTrac, const int* tracTri, const double* tracVec, const double* gravity,
                       double* u, double* stress, double* vm, double* info) {
    try {
        g_err.clear();
        using namespace sim_validation;
        require(h && u && stress && vm && info, "Invalid mesh or output pointers.");
        auto* m = (FemMesh*)h;
        const int n = (int)(m->X.size() / 3), ndof = 3 * n;
        material(mat);
        indices(fixNode, nFix, n); array(fixMask, nFix);
        if (fixVal) finite(fixVal, nFix, 3);
        indices(forceNode, nForce, n); finite(forceVec, nForce, 3);
        indices(tracTri, nTrac, (int)m->B.size()); finite(tracVec, nTrac, 3);
        if (gravity) finite(gravity, 3);
        const double E = mat[0], nu = mat[1], rho = mat[2];
        auto D = elasticity(E, nu);
        auto t0 = std::chrono::steady_clock::now();
        auto lap = [&]() { auto t1 = std::chrono::steady_clock::now(); double d = std::chrono::duration<double>(t1 - t0).count(); t0 = t1; return d; };

        // ---- assemble K and body-force vector
        std::vector<Trip> trips; trips.reserve(m->T.size() * 900);
        Eigen::VectorXd f = Eigen::VectorXd::Zero(ndof);
        for (auto& t : m->T) {
            double g[4][3];
            double vol = baryGradients(&m->X[3 * t[0]], &m->X[3 * t[1]], &m->X[3 * t[2]], &m->X[3 * t[3]], g);
            double av = std::fabs(vol);
            Eigen::Matrix<double, 30, 30> Ke = Eigen::Matrix<double, 30, 30>::Zero();
            Eigen::Matrix<double, 30, 1> fe = Eigen::Matrix<double, 30, 1>::Zero();
            for (int q = 0; q < 4; ++q) {
                double dN[10][4], N[10]; dShape(GP[q], dN); shape(GP[q], N);
                Eigen::Matrix<double, 6, 30> B; bMatrix(dN, g, B);
                Ke += (av / 4.0) * (B.transpose() * D * B);
                if (gravity) for (int i = 0; i < 10; ++i) for (int d = 0; d < 3; ++d) fe[3 * i + d] += (av / 4.0) * N[i] * rho * gravity[d];
            }
            for (int i = 0; i < 10; ++i) {
                for (int d = 0; d < 3; ++d) f[3 * t[i] + d] += fe[3 * i + d];
                for (int j = 0; j < 10; ++j) for (int a = 0; a < 3; ++a) for (int b = 0; b < 3; ++b)
                    trips.emplace_back(3 * t[i] + a, 3 * t[j] + b, Ke(3 * i + a, 3 * j + b));
            }
        }
        Eigen::SparseMatrix<double> K(ndof, ndof); K.setFromTriplets(trips.begin(), trips.end());

        // ---- applied loads
        double applied = 0;
        for (int i = 0; i < nForce; ++i) for (int d = 0; d < 3; ++d) { f[3 * forceNode[i] + d] += forceVec[3 * i + d]; applied += std::fabs(forceVec[3 * i + d]); }
        for (int i = 0; i < nTrac; ++i) {
            auto& b = m->B[tracTri[i]];
            const double* p0 = &m->X[3 * b[0]]; const double* p1 = &m->X[3 * b[1]]; const double* p2 = &m->X[3 * b[2]];
            Eigen::Vector3d e1(p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]), e2(p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]);
            double area = 0.5 * e1.cross(e2).norm();
            // straight tri6: a uniform traction gives zero to corners and area/3 to every mid-side node
            for (int k = 3; k < 6; ++k) for (int d = 0; d < 3; ++d) { double v = tracVec[3 * i + d] * area / 3.0; f[3 * b[k] + d] += v; applied += std::fabs(v); }
        }

        double tAssemble = lap();
        // ---- constraints
        std::vector<char> fixed(ndof, 0); std::vector<double> prescribed(ndof, 0.0);
        for (int i = 0; i < nFix; ++i) for (int d = 0; d < 3; ++d) if (fixMask[i] & (1 << d)) { fixed[3 * fixNode[i] + d] = 1; prescribed[3 * fixNode[i] + d] = fixVal ? fixVal[3 * i + d] : 0.0; }
        std::vector<int> freeIdx(ndof, -1); int nf = 0;
        for (int i = 0; i < ndof; ++i) if (!fixed[i]) freeIdx[i] = nf++;
        if (nf == ndof) { fail("The study has no fixtures: the part is free to move."); return 0; }

        Eigen::VectorXd up = Eigen::VectorXd::Zero(ndof);
        for (int i = 0; i < ndof; ++i) if (fixed[i]) up[i] = prescribed[i];
        Eigen::VectorXd rhsFull = f - K * up;
        std::vector<Trip> rt; rt.reserve(K.nonZeros());
        for (int c = 0; c < K.outerSize(); ++c) for (Eigen::SparseMatrix<double>::InnerIterator it(K, c); it; ++it)
            if (freeIdx[it.row()] >= 0 && freeIdx[it.col()] >= 0) rt.emplace_back(freeIdx[it.row()], freeIdx[it.col()], it.value());
        Eigen::SparseMatrix<double> Kr(nf, nf); Kr.setFromTriplets(rt.begin(), rt.end());
        Eigen::VectorXd rhs(nf);
        for (int i = 0; i < ndof; ++i) if (freeIdx[i] >= 0) rhs[freeIdx[i]] = rhsFull[i];

        // ---- solve
        Eigen::VectorXd x;
        double iters = 0;
        if (nf == 0) {
            x.resize(0); // All DOFs are prescribed: only reactions and stresses remain.
        } else if (nf <= 70000) {
            Eigen::SimplicialLDLT<Eigen::SparseMatrix<double>> solver; solver.compute(Kr);
            if (solver.info() != Eigen::Success) { fail("The stiffness matrix could not be factorized: the part is probably under-constrained."); return 0; }
            x = solver.solve(rhs);
            if (solver.info() != Eigen::Success) { fail("The solver failed."); return 0; }
        } else {
            Eigen::ConjugateGradient<Eigen::SparseMatrix<double>, Eigen::Lower | Eigen::Upper, Eigen::IncompleteCholesky<double>> cg;
            cg.setMaxIterations(8000); cg.setTolerance(1e-9); cg.compute(Kr);
            if (cg.info() != Eigen::Success) { fail("The iterative solver could not be initialized."); return 0; }
            x = cg.solve(rhs); iters = (double)cg.iterations();
            if (cg.info() != Eigen::Success) { fail("The iterative solver failed."); return 0; }
        }
        if (!x.allFinite()) { fail("The solver produced non-finite displacements."); return 0; }
        double tSolve = lap();
        Eigen::VectorXd U = up;
        for (int i = 0; i < ndof; ++i) if (freeIdx[i] >= 0) U[i] = x[freeIdx[i]];
        for (int i = 0; i < ndof; ++i) u[i] = U[i];

        // ---- reactions
        Eigen::VectorXd R = K * U - f; double rs = 0, rx = 0, ry = 0, rz = 0;
        for (int i = 0; i < ndof; ++i) if (fixed[i]) { rs += std::fabs(R[i]); if (i % 3 == 0) rx += R[i]; else if (i % 3 == 1) ry += R[i]; else rz += R[i]; }
        double fx = 0, fy = 0, fz = 0;
        for (int i = 0; i < n; ++i) { fx += f[3 * i]; fy += f[3 * i + 1]; fz += f[3 * i + 2]; }
        info[0] = iters; info[1] = rs; info[2] = rx; info[3] = ry; info[4] = rz; info[5] = std::sqrt(fx * fx + fy * fy + fz * fz);
        info[6] = tAssemble; info[7] = tSolve;
        (void)applied;

        // ---- stresses at the element corners, averaged to nodes (mid-side nodes take the mean of their corners)
        std::vector<Eigen::Matrix<double, 6, 1>> acc(n, Eigen::Matrix<double, 6, 1>::Zero()); std::vector<int> cnt(n, 0);
        for (auto& t : m->T) {
            double g[4][3];
            baryGradients(&m->X[3 * t[0]], &m->X[3 * t[1]], &m->X[3 * t[2]], &m->X[3 * t[3]], g);
            Eigen::Matrix<double, 30, 1> ue;
            for (int i = 0; i < 10; ++i) for (int d = 0; d < 3; ++d) ue[3 * i + d] = U[3 * t[i] + d];
            for (int c = 0; c < 4; ++c) {
                double L[4] = {0, 0, 0, 0}; L[c] = 1;
                double dN[10][4]; dShape(L, dN);
                Eigen::Matrix<double, 6, 30> B; bMatrix(dN, g, B);
                acc[t[c]] += D * (B * ue); cnt[t[c]]++;
            }
        }
        static const int EDG[6][2] = {{0, 1}, {1, 2}, {0, 2}, {0, 3}, {1, 3}, {2, 3}};
        std::vector<Eigen::Matrix<double, 6, 1>> S(n, Eigen::Matrix<double, 6, 1>::Zero());
        for (int i = 0; i < m->nCorner; ++i) if (cnt[i] > 0) S[i] = acc[i] / cnt[i];
        std::vector<char> done(n, 0);
        for (auto& t : m->T) for (int e = 0; e < 6; ++e) { int id = t[4 + e]; if (done[id]) continue; S[id] = 0.5 * (S[t[EDG[e][0]]] + S[t[EDG[e][1]]]); done[id] = 1; }
        for (int i = 0; i < n; ++i) { for (int k = 0; k < 6; ++k) stress[6 * i + k] = S[i][k]; vm[i] = vonMises(S[i]); }
        return 1;
    } catch (const std::exception& e) { fail(e.what()); return 0; }
    catch (...) { fail("Static solve failed unexpectedly."); return 0; }
}
