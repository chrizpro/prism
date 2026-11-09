"""Carved lotus plaque: a maple block with a picture carved into its top by CNC, planned with Prism's Image Carve tool. Purely decorative.

A 150 x 150 x 18 mm maple block gets a CAM setup (G54 at the stock's lower-left top corner) and one Image Carve operation: a 3 mm ball nose bit follows the
brightness of `extras/relief-lotus.png` (dark areas are cut up to 3 mm deep) over a 130 mm square. The file reopens on the CAM tab with Playback open, so
the user only presses play to watch the bit carve the pattern. The script checks that the operation was made (CAM-H005 checks the depth against the image).

python gallery/build_relief_plaque.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse

import build_examples as study

cad = study.cad

MAPLE = dict(material="maple", color="#D9B27C", roughness=.5)
SIDE, THICK, CARVE, DEPTH, MARGIN = 150.0, 18.0, 130.0, 3.0, 10.0
IMAGE = study.ROOT / "gallery/extras/relief-lotus.png"


def build():
    cad.call("document_new")
    with cad.history("Carved lotus plaque - Solid and CAM workflow"):
        block = cad.box(width=SIDE, depth=SIDE, height=THICK, operation="New body")
        block.rename("Plaque")
        cad.call("material_apply", object="Plaque", **MAPLE)
        cad.call("run_command", name="cam_setup", slots={"Model body": "Plaque"}, params={
            "Setup name": "Lotus plaque", "Stock allowance XY": 0, "Top allowance": 0, "Bottom allowance": 0, "Work zero (G54)": "Stock lower-left, top",
            "Tool diameter": 3, "Cutting feed": 1000, "Plunge feed": 300, "Spindle speed": 16000, "Clearance above stock": 5})
        info = cad.call("object_info", object="Plaque")
        x0, y0 = info["min"][0] + MARGIN, info["min"][1] + MARGIN
        cad.call("run_command", name="cnc_image_relief", params={
            "Image file": str(IMAGE), "X from": x0, "Y from": y0, "Width of the carving": CARVE, "Top of the material (Z)": THICK, "Total cutting depth": DEPTH,
            "Tool diameter": 3, "Cutting feed": 1000, "Plunge feed": 300, "Spindle speed": 16000, "Stepover": 15, "Deepest where the image is": "Dark", "Bit": "Ball nose"})
    # the picture, lying on the block at the carving's position, shows what the toolpath will cut
    cad.image(str(IMAGE), on="Top plane (XY)", width=CARVE, opacity=100, move_it_back_or_forward=THICK + .05)
    path = [o for o in cad.objects() if o.kind == "Toolpath"]
    assert len(path) == 1, path
    cad.call("select_tab", title="Subtractive", command="cam_playback")
    return "relief-plaque", -35, 40


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/relief-plaque.prism"))
            model = ("relief-plaque", -35, 40)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=520)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
