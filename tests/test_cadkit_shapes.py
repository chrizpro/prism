"""Pure Python tests for cadkit.shapes (SVG outlines, section shapes, station skins); no live application required.

Requirements: docs/freeform-studies/agent-tooling-proposal.md (P4, P5)."""
import math
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Prism/Scripting/python"))
import cadkit as cad
from cadkit import shapes


def close(a, b, tol=1e-6):
    return abs(a - b) <= tol


class PathParsing(unittest.TestCase):
    def test_rectangle_path_has_four_corners_and_is_closed(self):
        sp = shapes.parse_path("M0 0 H10 V5 H0 Z")[0]
        self.assertTrue(sp["closed"])
        self.assertEqual(sp["points"], [(0, 0), (10, 0), (10, 5), (0, 5)])
        self.assertFalse(any(sp["curved"]))

    def test_relative_commands_and_implicit_lineto(self):
        sp = shapes.parse_path("m1 1 2 0 0 3")[0]
        self.assertEqual(sp["points"], [(1, 1), (3, 1), (3, 4)])

    def test_cubic_is_flattened_within_tolerance(self):
        sp = shapes.parse_path("M0 0 C0 10 10 10 10 0", tolerance=0.01)[0]
        pts = sp["points"]
        self.assertGreater(len(pts), 8)
        # the true curve's peak is at y = 7.5 (t = 0.5)
        self.assertTrue(close(max(p[1] for p in pts), 7.5, 0.02))
        self.assertTrue(all(sp["curved"]))

    def test_arc_with_packed_flags(self):
        # half circle of radius 5 from (0,0) to (10,0), sweep flag 1; flags written without separators
        sp = shapes.parse_path("M0 0a5 5 0 0110 0", tolerance=0.01)[0]
        pts = sp["points"]
        self.assertTrue(close(pts[-1][0], 10) and close(pts[-1][1], 0))
        radii = [math.hypot(p[0] - 5, p[1]) for p in pts]
        self.assertTrue(all(close(r, 5, 0.02) for r in radii))

    def test_bad_argument_count_is_an_actionable_error(self):
        with self.assertRaises(cad.CadError) as cm:
            shapes.parse_path("M0 0 L5")
        self.assertIn("groups of 2", str(cm.exception))


class SvgOutlines(unittest.TestCase):
    SVG = '<svg xmlns="http://www.w3.org/2000/svg"><rect x="10" y="20" width="40" height="20"/></svg>'

    def test_y_is_flipped_and_drawing_starts_at_origin(self):
        o = shapes.svg_outlines(self.SVG)[0]
        lo, hi = o.bounds
        self.assertTrue(close(lo[0], 0) and close(lo[1], 0))
        self.assertTrue(close(hi[0], 40) and close(hi[1], 20))
        self.assertTrue(o.closed)
        self.assertEqual(len(o.runs), 4)    # four sharp corners, four straight runs

    def test_fit_width_scales_the_whole_drawing(self):
        o = shapes.svg_outlines(self.SVG, fit_width=80)[0]
        lo, hi = o.bounds
        self.assertTrue(close(hi[0] - lo[0], 80) and close(hi[1] - lo[1], 40))

    def test_center_puts_the_middle_on_the_origin(self):
        lo, hi = shapes.svg_outlines(self.SVG, center=True)[0].bounds
        self.assertTrue(close(lo[0], -20) and close(hi[0], 20) and close(lo[1], -10) and close(hi[1], 10))

    def test_circle_is_one_smooth_closed_run_without_corners(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><circle cx="0" cy="0" r="10"/></svg>'
        o = shapes.svg_outlines(svg, tolerance=0.02)[0]
        self.assertEqual(len(o.runs), 1)
        self.assertTrue(o.closed and o.curved)
        lo, hi = o.bounds
        self.assertTrue(close(hi[0] - lo[0], 20, 0.05))

    def test_transforms_nest(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><g transform="translate(100 0)"><g transform="scale(2)"><line x1="0" y1="0" x2="5" y2="0"/></g></g></svg>'
        o = shapes.svg_outlines(svg, flip_y=False)[0]
        pts = o.points
        self.assertTrue(close(abs(pts[-1][0] - pts[0][0]), 10))

    def test_rounded_corners_keep_corner_count_low(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><rect width="40" height="20" rx="5"/></svg>'
        o = shapes.svg_outlines(svg, tolerance=0.02, corner_angle=35)[0]
        self.assertTrue(o.curved)
        # tangent-continuous arcs: no sharp corners, so the outline stays one closed run
        self.assertEqual(len(o.runs), 1)

    def test_no_drawable_elements_is_an_error(self):
        with self.assertRaises(cad.CadError):
            shapes.svg_outlines('<svg xmlns="http://www.w3.org/2000/svg"><text>hi</text></svg>')

    def test_unparseable_input_is_an_error(self):
        with self.assertRaises(cad.CadError):
            shapes.svg_outlines("<svg><path")


class SectionShapes(unittest.TestCase):
    def test_superellipse_hits_its_extents(self):
        pts = shapes.superellipse(40, 20, 3, 24)
        self.assertEqual(len(pts), 24)
        self.assertTrue(close(max(p[0] for p in pts), 20) and close(min(p[0] for p in pts), -20))
        self.assertTrue(close(max(p[1] for p in pts), 10) and close(min(p[1] for p in pts), -10))

    def test_exponent_two_is_an_ellipse(self):
        for x, y in shapes.superellipse(40, 20, 2, 16):
            self.assertTrue(close((x / 20) ** 2 + (y / 10) ** 2, 1.0, 1e-9))

    def test_higher_exponent_is_squarer(self):
        def area(pts):
            return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(pts, pts[1:] + pts[:1]))) / 2
        self.assertGreater(area(shapes.superellipse(40, 40, 4, 64)), area(shapes.superellipse(40, 40, 2, 64)))

    def test_center_offsets_every_point(self):
        pts = shapes.superellipse(10, 10, 2, 8, center=(5, -3))
        self.assertTrue(close(sum(p[0] for p in pts) / 8, 5) and close(sum(p[1] for p in pts) / 8, -3))

    def test_rounded_rect_stays_inside_its_box_and_has_requested_count(self):
        pts = shapes.rounded_rect(40, 20, 4, 32)
        self.assertEqual(len(pts), 32)
        self.assertTrue(all(abs(p[0]) <= 20 + 1e-9 and abs(p[1]) <= 10 + 1e-9 for p in pts))
        self.assertTrue(close(max(p[0] for p in pts), 20))


class FormSkin(unittest.TestCase):
    def fake_call(self):
        calls = []

        def call(tool, **args):
            calls.append((tool, args))
            if tool == "sketch_finish":
                return {"sketch": "Sketch %d" % sum(1 for c in calls if c[0] == "sketch_finish")}
            if tool == "object_info":
                return {"name": args["object"], "kind": "Sketch"}
            if tool == "run_command":
                return {"created": [{"name": "Freeform 1", "kind": "Form"}]}
            return {}
        return calls, call

    def test_sections_sit_on_planes_at_their_stations_in_order(self):
        calls, fake = self.fake_call()
        with patch("cadkit.api.call", fake), patch("cadkit.shapes.call", fake):
            cad.form_skin([{"at": 60, "width": 20, "depth": 10}, {"at": 0, "width": 30, "depth": 20, "exponent": 3}], axis="Z")
        begins = [a for t, a in calls if t == "sketch_begin"]
        self.assertEqual([b["plane"] for b in begins], ["XY", "XY"])
        self.assertEqual([b.get("offset") for b in begins], [None, 60])   # sorted by position; a zero offset is omitted
        loft = next(a for t, a in calls if t == "run_command")
        self.assertEqual(loft["name"], "form_loft")
        self.assertEqual(loft["slots"]["Sections"], ["Sketch 1", "Sketch 2"])

    def test_y_axis_uses_the_xz_plane_with_a_flipped_offset(self):
        calls, fake = self.fake_call()
        with patch("cadkit.api.call", fake), patch("cadkit.shapes.call", fake):
            cad.form_skin([{"at": 10, "width": 5, "depth": 5}, {"at": 30, "width": 5, "depth": 5}], axis="Y")
        begins = [a for t, a in calls if t == "sketch_begin"]
        self.assertEqual([b["plane"] for b in begins], ["XZ", "XZ"])
        self.assertEqual([b["offset"] for b in begins], [-10, -30])

    def test_symmetry_is_set_on_the_result(self):
        calls, fake = self.fake_call()
        with patch("cadkit.api.call", fake), patch("cadkit.shapes.call", fake):
            cad.form_skin([{"at": 0, "width": 5, "depth": 5}, {"at": 9, "width": 5, "depth": 5}], symmetry="x", name="Thing")
        op = next(a for t, a in calls if t == "form_op")
        self.assertEqual((op["op"], op["symmetry"], op["object"]), ("set_symmetry", "x", "Thing"))

    def test_duplicate_stations_and_missing_sizes_are_actionable_errors(self):
        with self.assertRaises(cad.CadError) as cm:
            cad.form_skin([{"at": 5, "width": 1, "depth": 1}, {"at": 5, "width": 2, "depth": 2}])
        self.assertIn("distinct", str(cm.exception))
        with self.assertRaises(cad.CadError):
            cad.form_skin([{"at": 0, "width": 1}, {"at": 5, "width": 2, "depth": 2}])
        with self.assertRaises(cad.CadError):
            cad.form_skin([{"at": 0, "width": 1, "depth": 1}])


class SilhouetteStations(unittest.TestCase):
    # a vase-like front outline (x, z): wide at z=0, narrowing to z=100, and a side outline (y, z) half as deep
    FRONT = [(-30, 0), (30, 0), (20, 60), (10, 100), (-10, 100), (-20, 60)]
    SIDE = [(-15, 0), (15, 0), (10, 60), (5, 100), (-5, 100), (-10, 60)]

    def test_slice_extents_crosses_the_outline_where_expected(self):
        lo, hi = cad.slice_extents(self.FRONT, 30)
        self.assertTrue(close(lo, -25) and close(hi, 25))
        self.assertIsNone(cad.slice_extents(self.FRONT, 150))

    def test_a_gap_between_two_legs_does_not_narrow_the_extent(self):
        u_shape = [(0, 0), (10, 0), (10, 10), (7, 10), (7, 4), (3, 4), (3, 10), (0, 10)]   # a U: two legs above z=4
        lo, hi = cad.slice_extents(u_shape, 7)
        self.assertTrue(close(lo, 0) and close(hi, 10))

    def test_stations_follow_the_outlines_and_bunch_at_the_ends(self):
        st = cad.stations_from_silhouettes(self.FRONT, self.SIDE, count=9)
        self.assertEqual(len(st), 9)
        self.assertLess(st[0]["at"], 3)
        self.assertGreater(st[-1]["at"], 97)
        # wide at the bottom, narrower at the top, depth half the width
        self.assertGreater(st[0]["width"], st[-1]["width"] * 2)
        for s in st:
            self.assertTrue(close(s["depth"], s["width"] / 2, 0.01))
        gaps = [b["at"] - a["at"] for a, b in zip(st, st[1:])]
        self.assertLess(gaps[0], gaps[len(gaps) // 2])          # cosine spacing: finer near the ends

    def test_off_centre_outline_gives_a_centre_offset(self):
        leaning = [(x + 10, z) for x, z in self.FRONT]
        st = cad.stations_from_silhouettes(leaning, self.SIDE, count=4)
        self.assertTrue(all(close(s["dx"], 10, 0.01) for s in st))

    def test_outlines_that_do_not_overlap_are_an_actionable_error(self):
        raised = [(x, z + 500) for x, z in self.SIDE]
        with self.assertRaises(cad.CadError) as cm:
            cad.stations_from_silhouettes(self.FRONT, raised)
        self.assertIn("overlap", str(cm.exception))

    def test_swap_uv_turns_xy_into_yx(self):
        self.assertEqual(cad.swap_uv([(1, 2), (3, 4)]), [(2, 1), (4, 3)])

    def test_axis_x_refinement_reads_the_sections_it_asked_for(self):
        """form_from_silhouettes rescales stations by target/measured: simulate a surface that comes out 5% small."""
        top = [(10, -50), (10, 50), (-10, 50), (-10, -50)]     # (y, x): 20 wide
        side = [(5, -50), (5, 50), (-5, 50), (-5, -50)]        # (z, x): 10 tall
        built = []

        def fake(tool, **a):
            if tool == "sketch_finish":
                return {"sketch": "S%d" % (len(built) + 1)}
            if tool == "object_info":
                return {"name": a["object"], "kind": "Sketch"}
            if tool == "run_command":
                built.append(1)
                return {"created": [{"name": "F%d" % len(built), "kind": "Form"}]}
            if tool == "solid_profile":
                k = 0.95 if len(built) == 1 else 1.0               # the first build is 5% small, the corrected one exact
                return {"sections": {"axis": "x", "order": "", "cuts": [{"at": at, "y": [-10 * k, 10 * k], "z": [-5 * k, 5 * k], "area": 1, "centroid": [0, 0], "outlines": 1} for at in a["at"]]}}
            return {}
        with patch("cadkit.api.call", fake), patch("cadkit.shapes.call", fake):
            cad.form_from_silhouettes(top, side, axis="X", count=4, refine=1)
        self.assertEqual(len(built), 2)                            # one rebuild after the first measurement


class ProfileText(unittest.TestCase):
    REPLY = {
        "objects": ["Bottle"], "min": [-20, -10, 0], "max": [20, 10, 100], "size": [40, 20, 100],
        "sections": {"axis": "z", "order": "[min,max] extents along x and y; centroid is [x,y]; area in mm2", "cuts": [
            {"at": 5, "x": [-18, 18], "y": [-9, 9], "area": 500.5, "centroid": [0, 0], "outlines": 1},
            {"at": 105, "empty": True}]},
        "silhouettes": [{"view": "front", "axes": "x right, z up", "mmPerColumn": 1.0, "mmPerRow": 2.0, "leftEdge": -20, "topEdge": 100, "rows": ["  ##  ", " #### "]}],
    }

    def test_text_names_every_number_an_agent_needs(self):
        text = cad.format_profile(self.REPLY)
        for needle in ("Bottle", "40 x 20 x 100", "at 5", "x -18..18", "area 500.5", "outlines 1", "empty", "front view", "|  ##  "):
            self.assertIn(needle, text)

    def test_profile_sends_object_names_and_options(self):
        with patch("cadkit.shapes.call", return_value=self.REPLY) as call:
            cad.profile(cad.Obj({"name": "Bottle", "kind": "Form"}), count=4, quiet=True)
        args = call.call_args.kwargs
        self.assertEqual((args["object"], args["count"], args["views"]), ("Bottle", 4, ["front", "right", "top"]))


if __name__ == "__main__":
    unittest.main()
