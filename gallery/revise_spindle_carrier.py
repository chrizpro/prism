"""Import the spindle STEP, revise it in Prism and verify the saved solids."""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import build_examples as study
from render_direct_mounting_plate import render_model

cad = study.cad
BODY = "Spindle carrier"


def properties():
    body = cad.get(BODY).info
    assert body["valid"] and body["kind"] == "Solid" and body["volume"] > 0, body
    return body


def candidates(kind):
    return cad.call("solid_find", object=BODY, kind="faces", type=kind, limit=150)["hits"]


def select_face(kind, predicate):
    face, = [f for f in candidates(kind) if predicate(f)]
    return {"object": BODY, "face": face["face"]}


def cylinder_count(radius):
    return sum(abs(f["radius"] - radius) < .01 for f in candidates("Cylinder"))


def imported(path):
    previous = {obj.info["id"] for obj in cad.objects()}
    cad.call("file", action="import_step", path=str(path))
    return [obj for obj in cad.objects() if obj.info["id"] not in previous]


def revise(folder):
    cad.call("document_new")
    body, = imported(folder / "spindle-carrier.step")
    body.rename(BODY)
    cad.call("material_apply", object=BODY, color="#A6A9AE", metal=.32, roughness=.36)
    original = properties()
    assert cylinder_count(3.2) == cylinder_count(5.1) == 8
    assert cylinder_count(5.4) == 2 and cylinder_count(29) == 1
    cad.save(str(folder / "spindle-imported.prism"))

    stock = imported(folder / "spindle-stock.step")
    cad.union(BODY, *[part.name for part in stock])
    assert len(cad.objects()) == 1 and cylinder_count(3.2) == cylinder_count(5.1) == 0
    tools = imported(folder / "spindle-tools.step")
    cad.subtract(BODY, *[part.name for part in tools])
    assert len(cad.objects()) == 1
    assert cylinder_count(3) == 6 and cylinder_count(5.5) == 4 and cylinder_count(5.4) == 0
    assert len([f for f in candidates("Cone") if abs(f["radius"] - 5.9) < .01]) == 6
    cut = properties()
    assert cut["size"][0] == 202 and cut["volume"] > original["volume"], cut
    print("Stock and tooling complete. Six countersinks, two foot slots.", flush=True)

    bore = select_face("Cylinder", lambda f: abs(f["radius"] - 35) < .01)
    cad.call("run_command", name="offset_face",
             slots={"Faces to offset": [bore]},
             params={"Offset distance (positive moves faces out)": -2})
    assert cylinder_count(37) == 1 and cylinder_count(41) == 0
    after_bore = properties()
    assert after_bore["volume"] < cut["volume"]
    seat, = imported(folder / "spindle-seat.step")
    cad.subtract(BODY, seat.name)
    assert cylinder_count(41) == 1
    after_seat = properties()

    underside = select_face("Plane", lambda f: abs(f["center"][2]) < .01 and f["normal"][2] < -.999)
    cad.call("run_command", name="move_face",
             slots={"Flat faces to move": [underside]}, params={"Move Z": -3})
    final = properties()
    assert abs(final["min"][2] + 3) < .01 and final["volume"] > after_seat["volume"], final
    assert cylinder_count(37) == 1 and cylinder_count(3) == 6 and cylinder_count(5.5) == 4
    cad.save(str(folder / "spindle-revised.prism"))
    return {"source_sha256": hashlib.sha256((folder / "spindle-carrier.step").read_bytes()).hexdigest(),
            "before": original, "after_machining": cut,
            "after_bore_offset": after_bore, "after_seat": after_seat, "after": final}


def comparison(folder):
    rendered = []
    camera = {"yaw": 18, "pitch": 18, "target": [0, 0, 63], "distance": 335}
    for variant in ("imported", "revised"):
        path = folder / (variant + ".png")
        render_model(folder / f"spindle-{variant}.prism", path,
                     camera=camera, expected_objects=1, samples=96)
        rendered.append(path)
    panel_width, panel_height = 680, 383
    sheet = Image.new("RGB", (2 * panel_width + 2, panel_height + 91), "#222426")
    draw = ImageDraw.Draw(sheet)
    fonts = Path("C:/Windows/Fonts")
    title = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 25)
    note = ImageFont.truetype(str(fonts / "segoeui.ttf"), 17)
    for i, (label, description) in enumerate([
        ("STEP import", "134 mm base  /  Ø58 bore  /  8 counterbores"),
        ("Prism revision", "202 mm slotted base  /  Ø74 stepped bore  /  6 countersinks"),
    ]):
        x = i * (panel_width + 2)
        with Image.open(rendered[i]) as image:
            sheet.paste(image.convert("RGB").resize((panel_width,panel_height), Image.Resampling.LANCZOS), (x,0))
        draw.text((x + 24, panel_height + 12), label, font=title, fill="#F3F3F3")
        draw.text((x + 24, panel_height + 50), description, font=note, fill="#CDD0D2")
    sheet.save(folder / "spindle-comparison.png", optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--folder", required=True, type=Path)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    folder = args.folder.resolve()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if not args.render_only:
            report = revise(folder)
            (folder / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf8")
        if not args.no_render:
            comparison(folder)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
