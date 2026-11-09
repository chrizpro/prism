"""Maple serving board: a wooden product made on a CNC router, modeled in Solid and planned with Prism's CAM tools. Purely decorative.

The 300 x 160 x 18 mm board has R25 corners, a 4 mm deep stadium-shaped juice well and a 70 x 16 mm hand slot through the handle end. The CAM plan
(saved in the model) uses a 6 mm flat end mill from the wood preset on a 3 mm stock allowance with G54 at the stock's lower-left top corner: face the
top, pocket the well, pocket the hand slot through, then profile the outline with four holding tabs. Stock review and the design check are run on it.

python gallery/build_serving_board.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math
import time

import build_examples as study

cad = study.cad

MAPLE = dict(material="maple", color="#D9B27C", roughness=.5)
W, D, T = 300.0, 160.0, 18.0            # board length, width, thickness
R = 25.0                                # corner radius
WELL_DEPTH, WELL_W = 4.0, 90.0          # juice well: depth and width of its stadium shape (centres 95 and 185 along the board)
HAND_W = 16.0                           # hand slot width (centres 250 and 270)
TOOL = "6 mm flat end mill, wood / MDF"
ALLOWANCE = 3.0                         # stock allowance around the board


def outline_arcs():
    return [{"type": "line", "points": [[R, 0], [W - R, 0]]}, {"type": "arc", "center": [W - R, R], "start": [W - R, 0], "end": [W, R]},
            {"type": "line", "points": [[W, R], [W, D - R]]}, {"type": "arc", "center": [W - R, D - R], "start": [W, D - R], "end": [W - R, D]},
            {"type": "line", "points": [[W - R, D], [R, D]]}, {"type": "arc", "center": [R, D - R], "start": [R, D], "end": [0, D - R]},
            {"type": "line", "points": [[0, D - R], [0, R]]}, {"type": "arc", "center": [R, R], "start": [0, R], "end": [R, 0]}]


def sketch(entities, offset=None):
    kw = {"plane": "XY"}
    if offset is not None:
        kw["offset"] = offset
    cad.call("sketch_begin", **kw)
    cad.call("sketch_add", entities=entities)
    return cad.call("sketch_finish")["sketch"]


def operation(name, slots=None, params=None):
    cad.call("run_command", name=name, slots=slots or {}, params=params or {}, commit=False)
    made = cad.call("command_commit")
    return [c["name"] for c in made.get("created", [])]


def review(name, slot):
    """Run a manufacturing check through its Run checks button and return its result rows."""
    cad.call("run_command", name=name, slots={"Model": slot}, commit=False)
    cad.call("command_set", params={"Run checks": True})
    cad.call("wait_idle", timeout_ms=60000)
    time.sleep(1)
    rows = cad.call("command_set", params={}).get("results", [])
    cad.call("command_cancel")
    return rows


def build():
    cad.call("document_new")
    with cad.history("Serving board - Solid and CAM workflow"):
        blank = cad.get(sketch([{"type": "rect", "corner1": [0, 0], "corner2": [W, D]}]))
        board = cad.extrude(blank, distance=T)
        board.rename("Serving board")
        edges = cad.call("solid_find", object=board.name, kind="edges", direction=[0, 0, 1], limit=10)["hits"]
        cad.fillet([dict(object=board.name, edge=e["edge"]) for e in edges], radius=R)
        well = cad.get(sketch([{"type": "slot", "center1": [95, D / 2], "center2": [185, D / 2], "width": WELL_W}], offset=T))
        cad.run("extrude", slots={"Profile": well.name}, distance=WELL_DEPTH, operation="Cut", reverse_direction=True)
        hand = cad.get(sketch([{"type": "slot", "center1": [250, D / 2], "center2": [270, D / 2], "width": HAND_W}], offset=T))
        cad.run("extrude", slots={"Profile": hand.name}, distance=T + 7, operation="Cut", reverse_direction=True)
        for s in (blank, well, hand):
            cad.call("object_update", object=s.name, visible=False)
        cad.call("material_apply", object=board.name, **MAPLE)
        info = cad.call("object_info", object=board.name)
        expected = (W * D - (4 - math.pi) * R * R) * T - (math.pi * (WELL_W / 2) ** 2 + 90 * WELL_W) * WELL_DEPTH - (math.pi * (HAND_W / 2) ** 2 + 20 * HAND_W) * (T - WELL_DEPTH)
        assert info["valid"] and abs(info["volume"] - expected) < .01 * expected, (info["volume"], expected)

        # CAM: setup, then the four operations. The cutting sketches are hidden afterwards.
        cad.call("run_command", name="cam_setup", slots={"Model body": board.name}, params={
            "Setup name": "Maple board", "Stock allowance XY": ALLOWANCE, "Top allowance": 1, "Bottom allowance": 0, "Work zero (G54)": "Stock lower-left, top",
            "Tool diameter": 6, "Cutting feed": 1500, "Plunge feed": 500, "Spindle speed": 16000, "Clearance above stock": 5})
        well_outline = sketch([{"type": "slot", "center1": [95, D / 2], "center2": [185, D / 2], "width": WELL_W}])
        hand_outline = sketch([{"type": "slot", "center1": [250, D / 2], "center2": [270, D / 2], "width": HAND_W}])
        outline = sketch(outline_arcs())
        top = T + 1                                            # top of the stock
        made = operation("cnc_face", params={"Tool from the library": TOOL, "X from": -ALLOWANCE, "X to": W + ALLOWANCE, "Y from": -ALLOWANCE, "Y to": D + ALLOWANCE,
                                             "Top of the material (Z)": top, "Total cutting depth": 1, "Depth per pass": 1})
        made += operation("cnc_pocket", slots={"Outlines of the pocket": well_outline}, params={"Tool from the library": TOOL, "Top of the material (Z)": T, "Total cutting depth": WELL_DEPTH})
        made += operation("cnc_pocket", slots={"Outlines of the pocket": hand_outline}, params={"Tool from the library": TOOL, "Top of the material (Z)": T, "Total cutting depth": T + .5})
        made += operation("cnc_profile", slots={"Outlines": outline}, params={"Tool from the library": TOOL, "Top of the material (Z)": T, "Total cutting depth": T + .5, "Holding tabs": 4, "Tab width": 8, "Tab height": 2})
        assert len(made) == 4, made
        for s in (well_outline, hand_outline, outline):
            cad.call("object_update", object=s, visible=False)

    # the stock review must find no gouges or collisions, and the design check must see a healthy mesh
    rows = review("cnc_stock_review", board.name)
    text = "\n".join(map(str, rows))
    for want in ("Possible gouges: 0 moves", "Rapid contacts: 0 sampled moves", "Holder contacts: 0 sampled moves"):
        assert want in text, text
    assert "Closed, manifold" in "\n".join(map(str, review("cnc_design_check", board.name)))
    cad.call("select_tab", title="Subtractive", command="cam_playback")   # the file reopens on the CAM tab with Playback open
    return "serving-board", -30, 38


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/serving-board.prism"))
            model = ("serving-board", -30, 38)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=520)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
