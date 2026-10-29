"""Pure Python API contract tests; no live application required."""
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Prism/Scripting/python"))
import cadkit as cad


class FreeformWrappers(unittest.TestCase):
    def test_curve_points_are_top_level_not_parameters(self):
        points = [(0, 0, 0), (10, 0, 20), (20, 0, 0)]
        with patch("cadkit.api.call", return_value={"created": []}) as call:
            cad.run("curve_through", points=points, closed=False)
        call.assert_called_once_with("run_command", name="curve_through", points=[list(p) for p in points], params={"closed": False})

    def test_form_loft_preserves_section_order(self):
        with patch("cadkit.api.call", return_value={"created": [{"name": "Freeform 1", "kind": "Form"}]}) as call:
            obj = cad.form_loft(["Curve 3", "Curve 1", "Curve 2"], points_per_section=12)
        self.assertEqual(obj.kind, "Form")
        self.assertEqual(call.call_args.kwargs["slots"]["Sections"], ["Curve 3", "Curve 1", "Curve 2"])
        self.assertEqual(call.call_args.kwargs["params"], {"points per section": 12})

    def test_bridge_forwards_zero_based_face_indices(self):
        obj = cad.Obj({"name": "Bottle", "kind": "Form"})
        with patch("cadkit.api.call", return_value={"closed": True}) as call:
            cad.form_op(obj, "bridge", faces=[0, 7])
        call.assert_called_once_with("form_op", object="Bottle", op="bridge", faces=[0, 7])

    def test_form_info_can_request_only_selected_vertices(self):
        with patch("cadkit.api.call", return_value={}) as call:
            cad.form_info("Bottle", only=[0, 12])
        call.assert_called_once_with("form_info", object="Bottle", summary=False, only=[0, 12])


if __name__ == "__main__":
    unittest.main()
