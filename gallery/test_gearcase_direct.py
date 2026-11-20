"""Run Direct Modeling command checks on independent copies of the imported gearbox."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import build_examples as study

cad = study.cad
BODY = "Reduction gearbox housing"
GRAY = "#A6A9AE"


def import_housing(path):
    cad.call("document_new")
    cad.call("file", action="import_step", path=str(path))
    body, = cad.objects()
    body.rename(BODY)
    cad.call("material_apply", object=BODY, color=GRAY, metal=.28, roughness=.37)
    return info()


def info():
    shape = cad.get(BODY).info
    assert shape["kind"] == "Solid" and shape["valid"] and shape["volume"] > 0, shape
    return shape


def faces(kind):
    return cad.call("solid_find", object=BODY, kind="faces", type=kind, limit=300)["hits"]


def pick(kind, predicate):
    face, = [f for f in faces(kind) if predicate(f)]
    return {"object": BODY, "face": face["face"]}


def flat(axis, coordinate):
    return pick("Plane", lambda f: abs(f["center"][axis] - coordinate) < .01
                and abs(f["normal"][axis]) > .999)


def cylinder(radius, x, y, z):
    return pick("Cylinder", lambda f: abs(f["radius"] - radius) < .01
                and math.dist(f["center"], (x, y, z)) < 4)


def cmd(name, slots, params=None, commit=True):
    result = cad.call("run_command", name=name, slots=slots, params=params or {}, commit=commit)
    assert not result.get("statusIsError") and not result.get("error"), result
    return result


def push_pad():
    cmd("push_pull", {"Faces to push or pull": [flat(2, 67)]},
        {"Distance (positive pulls out, negative pushes in)": 4})


def widen_base():
    cmd("edit_distance", {
        "Move (a flat face, or a hole, slot, pocket or boss)": [flat(0, 122)],
        "Measured from (a flat face or a feature)": [flat(0, -122)]},
        commit=False)
    cad.call("command_set", params={"Distance": 260})
    cad.call("command_commit")


def raise_flange():
    cmd("offset_face", {"Faces to offset": [flat(2, 83)]},
        {"Offset distance (positive moves faces out)": 3})


def move_hole():
    cmd("move_hole", {"A hole or boss (click its round face)": [cylinder(2.25, 34, 35, 64)]},
        {"Move Y": 3, "New diameter (0 = keep)": 5.5})


def remove_hole():
    cmd("delete_face", {"Faces to remove (a hole, fillet, pocket...)": [cylinder(4, 106, 31, 9)]})


def mirror_locator():
    return cmd("mirror_feature", {"Features to mirror (click a hole, slot, pocket or boss)": [cylinder(4, 106, 31, 9)],
                                  "Mirror plane or flat face": {"origin": "YZ"}})


def counterbore_locator():
    return cmd("feature_edit", {"Feature to edit (click a hole, slot, pocket, boss, fillet, chamfer or text)":
                                [cylinder(4, 106, 31, 9)]},
               {"Action": "Change type", "New hole type": "Counterbore",
                "Counterbore diameter": 13, "Counterbore depth": 4})


def edit_pattern(pick_values, params):
    label = "A feature of the pattern (click any hole, slot, pocket or boss in it)"
    cmd("edit_pattern", {label: [cylinder(*pick_values)]}, commit=False)
    reply = cad.call("command_set", params=params)
    cad.call("command_commit")
    return reply.get("results", [])


def change_pad_spacing():
    return edit_pattern((2.25, 34, 35, 64), {"Spacing": 34})


def change_circle():
    return edit_pattern((3.3, -38.46, -39.73, 69.5),
                        {"Number of instances": 6, "Angular step": 60})


def split_merge():
    before = info()
    cmd("split_face", {"Face to split": flat(0, 122), "Splitting plane, edge or sketch": {"origin": "XZ"}})
    after = info()
    assert after["faces"] == before["faces"] + 1 and same_volume(before, after)
    halves = [{"object": BODY, "face": f["face"]} for f in faces("Plane")
              if abs(f["center"][0] - 122) < .01 and f["normal"][0] > .999]
    assert len(halves) == 2
    cmd("merge_faces", {"Faces to merge (neighbours on one surface)": halves})
    assert info()["faces"] == before["faces"] and same_volume(before, info())


def split_body():
    before = info()
    cmd("split_body", {"Body to split": BODY,
                       "Splitting tool (plane, face, surface or solid)": {"origin": "XZ"}})
    props = [cad.get(o.name).info for o in cad.objects()]
    assert len(props) == 2 and all(o["valid"] and o["volume"] > 0 for o in props), props
    assert abs(sum(o["volume"] for o in props) - before["volume"]) < 1
    return [o["volume"] for o in props]


def same_volume(a, b):
    return abs(a["volume"] - b["volume"]) < .1


def check_repair():
    cmd("check_repair", {"Body to check (solid or surface)": BODY}, commit=False)
    rows = cad.call("command_set", params={}).get("results", [])
    cad.call("command_cancel")
    assert rows, "No inspection results"
    return rows


def rounded_corners():
    cmd("remove_fillets", {"Or a body to clean up": BODY},
        {"Also remove all rounds up to radius (0 = only picked)": 8})


def replace():
    cmd("replace_face", {"Flat faces to replace": [flat(2, 67)],
                         "Target plane or flat face": flat(2, 65)})


def extend_trim():
    cmd("extend_trim_face", {"Flat faces to extend or trim": [flat(2, 67)],
                             "Target plane or flat face": flat(0, 92)})


def match():
    cmd("match_face", {"Faces to change": [flat(2, 67)],
                        "Face to match (flat, round or conical)": flat(2, 65)})


def run_matrix(step):
    def count(radius):
        return sum(abs(f["radius"] - radius) < .01 for f in faces("Cylinder"))

    def valid_volume_change(before):
        return abs(info()["volume"] - before["volume"]) > 1

    tests = [
        ("Check & Repair (inspect)", check_repair, lambda b: same_volume(b, info())),
        ("Push / Pull pad +4 mm", push_pad, lambda b: flat(2, 71) and info()["volume"] > b["volume"]),
        ("Edit Distance 244 to 260 mm", widen_base, lambda b: abs(info()["size"][0] - 260) < .01),
        ("Offset Face main flange +3 mm", raise_flange, lambda b: flat(2, 86) and info()["volume"] > b["volume"]),
        ("Move Hole / Boss and resize", move_hole, lambda b: cylinder(2.75, 34, 38, 64)),
        ("Delete Face removes the dowel bore", remove_hole,
         lambda b: not any(abs(f["radius"] - 4) < .01 and f["center"][2] > 8 for f in faces("Cylinder"))
         and abs(info()["volume"] - b["volume"] - math.pi * 4**2 * 18) < .1),
        ("Mirror Feature duplicates the dowel bore", mirror_locator, lambda b: cylinder(4, -106, 31, 9)),
        ("Edit Feature adds a counterbore", counterbore_locator, lambda b: cylinder(6.5, 106, 31, 16)),
        ("Edit Pattern changes the pad hole spacing", change_pad_spacing,
         lambda b: len([f for f in faces("Cylinder") if abs(f["radius"] - 2.25) < .01]) == 2
         and abs(info()["volume"] - b["volume"]) < 1
         and abs(max(f["center"][0] for f in faces("Cylinder") if abs(f["radius"]-2.25)<.01) - 67.72) < .1),
        ("Edit Pattern 8 to 6 counterbores", change_circle, lambda b: count(3.3) == count(5.7) == 6),
        ("Split Face and Merge Faces", split_merge, lambda b: info()["faces"] == b["faces"] and same_volume(b, info())),
        ("Split Body on the center plane", split_body, lambda b: True),
        ("Replace Face on the pad", replace, valid_volume_change),
        ("Extend / Trim Face on the pad", extend_trim, valid_volume_change),
        ("Match Face on the pad", match, valid_volume_change),
        ("Remove Fillets to R8", rounded_corners, valid_volume_change),
        ("Move Face +6 mm",
         lambda: cmd("move_face", {"Flat faces to move": [flat(0, 122)]}, {"Move X": 6}),
         lambda b: abs(info()["size"][0] - 250) < .01),
        ("Rotate Face 3 degrees",
         lambda: cmd("rotate_face", {"Flat faces to rotate": [flat(0, 122)]},
                     {"Rotation angle": 3, "Rotate about": "World Y"}),
         lambda b: info()["valid"]),
        ("Draft a wall",
         lambda: cmd("draft_face", {"Faces to draft": [flat(0, 122)],
                     "Neutral plane (a plane or flat face the walls hinge on)": {"origin": "XY"}},
                     {"Draft angle": 3}),
         valid_volume_change),
    ]
    results = []
    for label, action, check in tests:
        before = import_housing(step)
        try:
            detail = action()
            assert check(before), (
                f"Geometry check failed (volume delta {info()['volume']-before['volume']:.4f} mm3). "
                f"Reported: {detail}")
            props = [cad.get(o.name).info for o in cad.objects()]
            assert all(p["valid"] for p in props)
            result = {"case": label, "status": "passed", "notes": detail,
                      "volumes_mm3": [p["volume"] for p in props]}
        except (AssertionError, cad.CadError, ValueError, KeyError) as exc:
            cad.call("command_cancel")
            result = {"case": label, "status": "unverified", "notes": str(exc)[:480]}
        results.append(result)
        print(result, flush=True)
    return results


def make_preview(step, folder):
    original, revised = folder / "gearcase-imported.prism", folder / "gearcase-revised.prism"
    baseline = import_housing(step)
    assert baseline["faces"] == 144 and baseline["size"] == [244, 168, 90]
    cad.save(str(original))
    widen_base()
    raise_flange()
    push_pad()
    mirror_locator()
    counterbore_locator()
    assert info()["size"] == [260, 168, 90]
    flat(2, 86)
    flat(2, 71)
    cylinder(4, -106, 31, 9)
    cylinder(6.5, 106, 31, 16)
    cad.save(str(revised))

    render_comparison(folder)


def render_comparison(folder):
    from PIL import Image, ImageDraw, ImageFont
    from render_direct_mounting_plate import render_model

    original, revised = folder / "gearcase-imported.prism", folder / "gearcase-revised.prism"
    camera = {"yaw": 34, "pitch": 34, "target": [8, 0, 31], "distance": 470}
    before, after = folder / "gearcase-before.png", folder / "gearcase-after.png"
    for model, output in ((original, before), (revised, after)):
        render_model(model, output, camera=camera, expected_objects=1, samples=72)

    width, height = 640, 360
    sheet = Image.new("RGB", (width * 2 + 2, height + 95), "#222426")
    draw = ImageDraw.Draw(sheet)
    fonts = Path("C:/Windows/Fonts")
    title = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 25)
    note = ImageFont.truetype(str(fonts / "segoeui.ttf"), 17)
    for x, path, label, detail in (
        (0, before, "STEP import", "244 mm base  /  original flange and service pad"),
        (width + 2, after, "Direct Modeling revision", "260 mm base  /  raised flange and pad  /  mirrored dowel"),
    ):
        with Image.open(path) as photo:
            sheet.paste(photo.convert("RGB").resize((width, height), Image.Resampling.LANCZOS), (x, 0))
        draw.text((x + 24, height + 14), label, font=title, fill="#F2F2F2")
        draw.text((x + 24, height + 53), detail, font=note, fill="#CECFD0")
    sheet.save(folder / "gearcase-comparison.png", optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--step", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--preview", action="store_true", help="Save native models and render the comparison")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        step = args.step.resolve()
        report = {"source_sha256": hashlib.sha256(step.read_bytes()).hexdigest(),
                  "cases": run_matrix(step)}
        args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        if args.preview:
            make_preview(step, args.out.resolve().parent)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
