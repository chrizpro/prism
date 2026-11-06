"""Native crash regressions. Run after building: python tests/test_native_safety.py.

Uses the freshly built DLLs without replacing libraries used by a running app.
"""
import ctypes as c
import math
import os
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
DLL_DIRS = [os.add_dll_directory(str(p)) for p in
            [ROOT / "native/build/Release", ROOT / "native/bin"]]
sim = c.CDLL(str(ROOT / "native/build/Release/prism_sim.dll"))
kernel = c.CDLL(str(ROOT / "native/build/Release/prism_kernel.dll"))
P, I, D = c.c_void_p, c.c_int, c.c_double
PI, PD = c.POINTER(I), c.POINTER(D)


def bind(lib, name, result, *args):
    fn = getattr(lib, name)
    fn.restype, fn.argtypes = result, args
    return fn


mesh_create = bind(sim, "sim_mesh_from_tets", P, PD, I, PI, I, PI, I)
mesh_free = bind(sim, "sim_mesh_free", None, P)
error = bind(sim, "sim_last_error", c.c_char_p)
sizes = bind(sim, "sim_mesh_sizes", None, P, PI, PI, PI, PI)
solve = bind(sim, "sim_static", I, P, PD, I, PI, PI, PD, I, PI, PD,
             I, PI, PD, PD, PD, PD, PD, PD)
topo_create = bind(sim, "sim_topo_create", P, P, PD, I, PI, PI, I, PI, PD,
                   PD, D, D, D, D, PI)
topo_step = bind(sim, "sim_topo_step", I, P, PD, PD)
topo_free = bind(sim, "sim_topo_free", None, P)
topo_error = bind(sim, "sim_topo_error", c.c_char_p)
netgen = bind(sim, "sim_netgen_mesh", P, c.POINTER(c.c_float), I, D, D, I)
shape_count = bind(kernel, "ck_count", I, P, I)
shape_type = bind(kernel, "ck_shape_type", I, P)
kernel_error = bind(kernel, "ck_last_error", c.c_char_p)
box = bind(kernel, "ck_box", P, D, D, D)
shape_free = bind(kernel, "ck_free", None, P)
tessellate = bind(kernel, "ck_mesh_create", P, P, D, D)
tess_free = bind(kernel, "ck_mesh_free", None, P)


class NativeSafety(unittest.TestCase):
    def setUp(self):
        self.nodes = (D * 12)(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1)
        self.tets = (I * 4)(0, 1, 2, 3)
        self.mesh = mesh_create(self.nodes, 4, self.tets, 1, None, 0)
        self.assertTrue(self.mesh, error())
        self.mat = (D * 3)(1000, .3, 1e-9)
        self.u, self.stress, self.vm, self.info = (D * 30)(), (D * 60)(), (D * 10)(), (D * 8)()

    def tearDown(self):
        mesh_free(self.mesh)

    def static(self, fix, masks, forces=None, vectors=None):
        return solve(self.mesh, self.mat, len(fix), fix, masks, None,
                     len(forces) if forces is not None else 0, forces, vectors,
                     0, None, None, None, self.u, self.stress, self.vm, self.info)

    def test_bad_mesh_indices_counts_and_degenerate_tets(self):
        for ids in [(0, 1, 2, -1), (0, 1, 2, 4), (0, 1, 2, 2)]:
            self.assertFalse(mesh_create(self.nodes, 4, (I * 4)(*ids), 1, None, 0))
            self.assertTrue(error())
        self.assertFalse(mesh_create(None, 4, self.tets, 1, None, 0))
        self.assertFalse(mesh_create(self.nodes, -1, self.tets, 1, None, 0))

    def test_quadratic_mesh_and_static_solution(self):
        values = [I() for _ in range(4)]
        sizes(self.mesh, *(c.byref(v) for v in values))
        self.assertEqual([v.value for v in values], [10, 4, 1, 0])
        # All nodes on z=0 fixed. A unit vertical corner force has an exact
        # reaction resultant even though this tiny mesh is not a beam benchmark.
        fix = (I * 6)(0, 1, 2, 4, 5, 6)
        self.assertEqual(self.static(fix, (I * 6)(*[7] * 6), (I * 1)(3), (D * 3)(0, 0, 1)), 1, error())
        self.assertTrue(all(math.isfinite(v) for v in self.u))
        self.assertAlmostEqual(self.info[4], -1, places=8)
        self.assertGreater(self.u[11], 0)

    def test_all_prescribed_and_invalid_static_inputs(self):
        self.assertEqual(self.static((I * 10)(*range(10)), (I * 10)(*[7] * 10)), 1, error())
        self.assertTrue(all(v == 0 for v in self.u))
        self.assertEqual(self.static((I * 1)(-1), (I * 1)(7)), 0)
        self.assertIn(b"index", error())
        self.assertEqual(self.static((I * 1)(0), (I * 1)(7), (I * 1)(10), (D * 3)()), 0)
        self.mat[1] = .5
        self.assertEqual(self.static((I * 1)(0), (I * 1)(7)), 0)

    def test_null_handles(self):
        self.assertEqual(shape_count(None, 0), 0)
        self.assertTrue(kernel_error())
        self.assertEqual(shape_type(None), -1)
        self.assertEqual(topo_step(None, self.vm, self.info), 0)
        ne = I()
        self.assertFalse(topo_create(None, self.mat, 0, None, None, 0, None, None, None, .5, 3, 1, 0, c.byref(ne)))

    def test_topology_valid_invalid_and_extreme_filter(self):
        for fix, radius, expected in [((0, 1, 2), 1, True), ((-1,), 1, False),
                                      ((4,), 1, False), ((0, 1, 2), 1e-300, False)]:
            ne = I()
            ctx = topo_create(self.mesh, self.mat, len(fix), (I * len(fix))(*fix),
                              (I * len(fix))(*[7] * len(fix)), 0, None, None,
                              (D * 3)(0, 0, -1), .5, 3, radius, 0, c.byref(ne))
            self.assertEqual(bool(ctx), expected, topo_error())
            if ctx:
                try:
                    x = (D * ne.value)()
                    self.assertEqual(topo_step(ctx, x, self.info), 1, topo_error())
                    self.assertTrue(all(.001 <= v <= 1 for v in x))
                finally:
                    topo_free(ctx)

    def test_mesher_validation_does_not_mask_solver_error(self):
        self.assertFalse(netgen(None, 1, 1, .3, 3))
        self.assertIn(b"null", error())
        self.assertEqual(self.static((I * 1)(-1), (I * 1)(7)), 0)
        self.assertIn(b"index", error())

    def test_normal_geometry_tessellation(self):
        shape = box(2, 3, 4)
        self.assertTrue(shape)
        try:
            self.assertEqual(shape_count(shape, 0), 1)
            mesh = tessellate(shape, .1, .3)
            self.assertTrue(mesh, kernel_error())
            tess_free(mesh)
        finally:
            shape_free(shape)


if __name__ == "__main__":
    unittest.main()
