"""Direct Modeling showcase: rework an imported pump-mount bracket into a different part, with no history.

Starts from the STEP made by BuildPumpBracketStep.cs and replays the full edit sequence on a private Prism desktop
instance, checking each step by measurement. Steps that only work through a workaround say so, and the behaviour
that forced the workaround is recorded as a known issue with what was measured. Writes a JSON report, and with
--save also the imported and revised native files and the revised STEP.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

import build_examples as study

cad = study.cad
BODY = "Pump bracket"
STEEL = dict(color="#9EA2A8", metal=.35, roughness=.38)
FEATURE = "Feature to edit (click a hole, slot, pocket, boss, fillet, chamfer or text)"
HOLE = "A hole or boss (click its round face)"
MIRROR = "Features to mirror (click a hole, slot, pocket or boss)"
report = {"steps": [], "known_issues": []}


# ---------------------------------------------------------------- lookups (face and edge indices change after every edit)
def info():
    shape = cad.get(BODY).info
    assert shape["kind"] == "Solid" and shape["valid"] and shape["volume"] > 0, shape
    return shape


def faces(kind=None):
    args = dict(object=BODY, kind="faces", limit=600)
    if kind:
        args["type"] = kind
    return cad.call("solid_find", **args)["hits"]


def edges():
    return cad.call("solid_find", object=BODY, kind="edges", limit=900)["hits"]


def ref(f):
    return {"object": BODY, "face": f["face"]}


def eref(e):
    return {"object": BODY, "edge": e["edge"]}


def pick(kind, predicate):
    hits = [f for f in faces(kind) if predicate(f)]
    assert len(hits) == 1, (kind, len(hits))
    return ref(hits[0])


def flat(axis, coordinate):
    return pick("Plane", lambda f: abs(f["center"][axis] - coordinate) < .05 and abs(f["normal"][axis]) > .999)


def cyl(radius, near, tol=6):
    return pick("Cylinder", lambda f: abs(f["radius"] - radius) < .05 and math.dist(f["center"], near) < tol)


def along(e, axis):
    d = [e["to"][i] - e["from"][i] for i in range(3)]
    return abs(d[axis]) / (math.hypot(*d) or 1) > .999


def at_z(e, z):
    return abs(e["from"][2] - z) < .05 and abs(e["to"][2] - z) < .05


def cmd(name, slots, params=None, commit=True):
    result = cad.call("run_command", name=name, slots=slots, params=params or {}, commit=commit)
    assert not result.get("statusIsError") and not result.get("error"), result
    return result


def typed(name, slots, params, commit_params):
    """Commands that report their measurement first, then take the typed value (Edit Distance, Edit Pattern)."""
    cmd(name, slots, params or {}, commit=False)
    rows = cad.call("command_set", params=commit_params).get("results", [])
    cad.call("command_commit")
    return rows


def step(label, action, check=None, workaround=None):
    before = info()
    detail = action()
    after = info()
    ok = check(before, after) if check else True
    entry = {"step": label, "status": "passed" if ok else "failed", "faces": after["faces"],
             "volume_mm3": round(after["volume"], 1), "size_mm": after["size"]}
    if workaround:
        entry["workaround"] = workaround
    if isinstance(detail, dict):
        detail = detail.get("status")
    if detail:
        entry["notes"] = detail
    report["steps"].append(entry)
    print(json.dumps(entry, ensure_ascii=False), flush=True)
    assert ok, label
    return after


def known(issue, observed):
    entry = {"issue": issue, "observed": observed}
    report["known_issues"].append(entry)
    print("KNOWN ISSUE:", json.dumps(entry, ensure_ascii=False), flush=True)


def grew(a, b):
    return b["volume"] > a["volume"] + 1


def same_volume(a, b):
    return abs(a["volume"] - b["volume"]) < 1


def import_bracket(step_path):
    cad.call("document_new")
    cad.call("file", action="import_step", path=str(step_path))
    body, = cad.objects()
    body.rename(BODY)
    cad.call("material_apply", object=BODY, **STEEL)
    shape = info()
    assert shape["faces"] == 63 and shape["size"] == [220, 130, 150], shape
    return shape


# ---------------------------------------------------------------- behaviour that forced a workaround (each on a fresh import)
def probe_known_issues(step_path):
    start = import_bracket(step_path)
    cmd("move_face", {"Flat faces to move": [flat(0, 110)]}, {"Move X": 30})
    gained = info()["volume"] - start["volume"]
    known("Move Face on an end face bounded by corner rounds and a chamfer pulls out a tab only as wide as the flat strip; the rounds and chamfer stay behind",
          f"volume gained {gained:.0f} mm3 = 30 mm x {gained / 30:.0f} mm2 (the flat strip alone; the whole end is 2340 mm2)")
    cad.undo(1)

    rounds = [ref(f) for f in faces("Cylinder") if abs(f["radius"] - 12) < .01 and f["center"][0] > 0]
    try:
        cmd("direct_edit", {"Click a face or feature (a hole, slot, pocket, boss or fillet)": [flat(0, 110)] + rounds}, {"Move X": 30})
        cad.undo(1)
        known("(fixed?) Direct Edit accepted an end face with its corner rounds", "no error")
    except cad.CadError as e:
        cad.call("command_cancel")
        known("Direct Edit refuses an end face together with its corner rounds, so a rounded end cannot be stretched in one step", str(e)[:160])

    rim = [f for f in faces("Plane") if f["center"][2] > 16 and 0.6 < abs(f["normal"][2]) < 0.8 and f["center"][2] < 18.5]
    try:
        cmd("feature_edit", {FEATURE: [ref(rim[0])]}, {"Action": "Delete"})
        cad.undo(1)
        known("(fixed?) the rim chamfer around the rounded outline is recognised", "Edit Feature accepted it")
    except cad.CadError as e:
        cad.call("command_cancel")
        known("A chamfer that runs around a rounded outline is not recognised as a chamfer (Edit Feature: plain face)", str(e)[:120])

    gusset = [f for f in faces("Plane") if 60 < f["center"][0] < 80 and f["center"][2] > 18.5 and abs(f["normal"][0]) > .99]
    try:
        cmd("feature_edit", {FEATURE: [ref(gusset[0])]}, {"Action": "Resize", "New rib thickness (0 = keep)": 16})
        cad.undo(1)
        known("(fixed?) a triangular gusset is recognised as a rib", "Resize accepted")
    except cad.CadError as e:
        cad.call("command_cancel")
        known("Triangular gussets are not recognised as ribs, so Edit Feature cannot resize them", str(e)[:120])


# ---------------------------------------------------------------- the rework
def rework(step_path):
    import_bracket(step_path)

    # Base: 220 -> 280 mm. The rounds and rim chamfer are taken off first, then the ends move, then the corners are rounded again.
    step("Remove Fillets: four R12 base corners",
         lambda: cmd("remove_fillets", {"Fillets to remove (click one: the whole chain is picked)":
                                        [ref(f) for f in faces("Cylinder") if abs(f["radius"] - 12) < .01]}),
         lambda a, b: b["faces"] == a["faces"] - 4)
    step("Delete Face: the rim chamfer (8 faces)",
         lambda: cmd("delete_face", {"Faces to remove (a hole, fillet, pocket...)":
                                     [ref(f) for f in faces() if f["center"][2] < 18.5 and (
                                         (f["type"] == "Cone" and abs(f.get("radius", 0) - 12) < .1) or
                                         (f["type"] == "Plane" and 0.6 < abs(f["normal"][2]) < 0.8))]}),
         lambda a, b: b["faces"] == a["faces"] - 8 and grew(a, b),
         workaround="the chamfer is not recognised, so its faces are deleted and healed")
    for s in (1, -1):
        step(f"Move Face: base end {'+' if s > 0 else '-'}30 mm",
             lambda s=s: cmd("move_face", {"Flat faces to move": [flat(0, s * 110)]}, {"Move X": s * 30}),
             lambda a, b: abs(b["volume"] - a["volume"] - 30 * 130 * 18) < 1)
    for s in (1, -1):
        step(f"Edit Feature Move: foot slot and counterbore {'+' if s > 0 else '-'}30 mm",
             lambda s=s: cmd("feature_edit", {FEATURE: [cyl(6, (s * 95, -64.9, 9), 3), cyl(9, (s * 94, 40, 15.5), 4)]},
                             {"Action": "Move", "Move X": s * 30}),
             same_volume)

    # Tower and flange
    step("Push / Pull: tower top +40 mm",
         lambda: cmd("push_pull", {"Faces to push or pull": [flat(2, 150)]}, {"Distance (positive pulls out, negative pushes in)": 40}),
         lambda a, b: b["size"][2] == 190)
    step("Edit Feature Move: boss, bore and bolt circle up 25 mm",
         lambda: cmd("feature_edit", {FEATURE: [cyl(48, (0, 18.5, 92), 8), cyl(30, (0, 29.5, 92), 6)] +
                                      [ref(f) for f in faces("Cylinder") if abs(f["radius"] - 3.3) < .01]},
                     {"Action": "Move", "Move Z": 25}),
         same_volume)
    step("Edit Pattern: bolt circle 6 -> 8",
         lambda: typed("edit_pattern", {"A feature of the pattern (click any hole, slot, pocket or boss in it)":
                                        [cyl(3.3, (0, 29.5, 156), 3)]}, {}, {"Number of instances": 8, "Angular step": 45}),
         lambda a, b: len([f for f in faces("Cylinder") if abs(f["radius"] - 3.3) < .01]) == 8)

    def spread_holes():
        for k in range(8):
            target = math.radians(45 * k - 180)
            hits = [f for f in faces("Cylinder") if abs(f["radius"] - 3.3) < .01]
            hole = min(hits, key=lambda f: abs((math.atan2(f["center"][2] - 117, f["center"][0]) - target + 3 * math.pi) % (2 * math.pi) - math.pi))
            cmd("move_hole", {HOLE: [ref(hole)]}, {"Move X": round(14 * math.cos(target), 4), "Move Z": round(14 * math.sin(target), 4)})
    step("Move Hole: each bolt hole 14 mm outward (PCD 78 -> 106)", spread_holes,
         lambda a, b: all(abs(math.hypot(f["center"][0], f["center"][2] - 117) - 53) < 1
                          for f in faces("Cylinder") if abs(f["radius"] - 3.3) < .01))
    step("Move Hole: bore 60 -> 80 mm",
         lambda: cmd("move_hole", {HOLE: [cyl(30, (0, 29.5, 117), 6)]}, {"New diameter (0 = keep)": 80}),
         lambda a, b: abs(a["volume"] - b["volume"] - math.pi * (40 ** 2 - 30 ** 2) * 35) < 5)

    before = info()
    step("Offset Face: boss 96 -> 130 mm",
         lambda: cmd("offset_face", {"Faces to offset": [cyl(48, (0, 18.5, 117), 8)]}, {"Offset distance (positive moves faces out)": 17}),
         grew, workaround="the bore and bolt holes inside the boss are drilled again below")
    filled = info()["volume"] - before["volume"] - math.pi * (65 ** 2 - 48 ** 2) * 13
    known("Growing a boss (Offset Face, and Edit Feature Resize) fills every hole that passes through it",
          f"{filled:.0f} mm3 more than the added ring; the bore inside the boss alone is {math.pi * 40 ** 2 * 13:.0f} mm3")

    def drill(x, z, diameter):
        cmd("hole", {"Body or entry face": flat(1, 12)}, {"Position": "World point", "Point X": x, "Point Y": 12, "Point Z": z,
                                                          "Metric size": "Custom", "Diameter": diameter, "Extent": "Through all"})
    step("Hole: bore drilled again, 80 mm", lambda: drill(0, 117, 80),
         lambda a, b: abs(a["volume"] - b["volume"] - math.pi * 40 ** 2 * 13) < 5)
    step("Hole: 8 x 9 mm (M8 clearance) on PCD 106",
         lambda: [drill(round(53 * math.cos(math.radians(45 * k)), 4), round(117 + 53 * math.sin(math.radians(45 * k)), 4), 9) for k in range(8)] and None,
         lambda a, b: abs(a["volume"] - b["volume"] - 8 * (math.pi * 4.5 ** 2 * 35 - math.pi * 3.3 ** 2 * 22)) < 5)
    step("Draft: tower sides 3 degrees inward",
         lambda: cmd("draft_face", {"Faces to draft": [flat(0, 80), flat(0, -80)],
                                    "Neutral plane (a plane or flat face the walls hinge on)": flat(2, 18)},
                     {"Draft angle": 3, "Reverse pull direction": True}),
         lambda a, b: b["volume"] < a["volume"])
    for s in (1, -1):
        step(f"Move Face: gusset {'right' if s > 0 else 'left'} 10 -> 16 mm",
             lambda s=s: cmd("move_face", {"Flat faces to move": [pick("Plane", lambda f: abs(f["center"][0] - s * 74) < .1
                                                                   and f["normal"][0] * s > .99 and f["center"][2] > 18.5)]}, {"Move X": s * 6}),
             grew, workaround="gussets are not recognised as ribs, so the outer face is moved")
    step("Push / Pull: flange spigot +12 mm",
         lambda: cmd("push_pull", {"Faces to push or pull": [flat(1, 12)]}, {"Distance (positive pulls out, negative pushes in)": 12}),
         lambda a, b: abs(b["volume"] - a["volume"] - 12 * math.pi * (65 ** 2 - 40 ** 2 - 8 * 4.5 ** 2)) < 5)

    step("Fillet: tower top corners R35",
         lambda: cmd("fillet", {"Edges": [eref(e) for e in edges() if e["type"] == "Line" and along(e, 1) and at_z(e, 190) and abs(e["from"][0]) > 60]},
                     {"Radius": 35}))
    step("Fillet: base corners R20",
         lambda: cmd("fillet", {"Edges": [eref(e) for e in edges() if e["type"] == "Line" and along(e, 2) and abs(abs(e["from"][0]) - 140) < .1]},
                     {"Radius": 20}))
    step("Chamfer: flange and bore rims 2 mm",
         lambda: cmd("chamfer", {"Edges": [eref(e) for e in edges() if e["type"] == "Circle" and abs(e["center"][1]) < .1 and e["radius"] in (65, 40)]},
                     {"Distance": 2}))

    # Base features
    step("Edit Feature Move: slots 3 mm back",
         lambda: cmd("feature_edit", {FEATURE: [cyl(6, (125, -64.9, 9), 3), cyl(6, (-125, -64.9, 9), 3)]}, {"Action": "Move", "Move Y": 3}),
         same_volume)
    step("Edit Feature Resize: slots longer",
         lambda: cmd("feature_edit", {FEATURE: [cyl(6, (125, -61.9, 9), 3), cyl(6, (-125, -61.9, 9), 3)]},
                     {"Action": "Resize", "New slot length (0 = keep)": 32}),
         lambda a, b: b["volume"] < a["volume"])
    step("Edit Feature Change type: counterbores -> countersinks",
         lambda: cmd("feature_edit", {FEATURE: [cyl(9, (124, 40, 15.5), 4), cyl(9, (-126, 40, 15.5), 4)]},
                     {"Action": "Change type", "New hole type": "Countersink", "Countersink diameter": 18, "Countersink angle": 90}))
    step("Edit Feature Change type: dowels -> M8 tapped",
         lambda: cmd("feature_edit", {FEATURE: [cyl(3, (40, -15, 9), 2), cyl(3, (-40, -15, 9), 2)]},
                     {"Action": "Change type", "New hole type": "Threaded (modeled)", "Thread diameter": 8, "Thread pitch": 1.25}))
    bores = [f for f in faces("Cylinder") if abs(abs(f["center"][0]) - 40) < 2 and abs(f["center"][1] + 15) < 2]
    known("Edit Feature Change type -> 'Threaded (modeled)' makes a plain bore at the minor diameter, with no thread",
          f"{len(bores)} single cylinder faces of radius {bores[0]['radius']:.3f} mm")
    step("Edit Feature Delete: cable pocket",
         lambda: cmd("feature_edit", {FEATURE: [cyl(5, (28, -66, 14), 3)]}, {"Action": "Delete"}), grew)
    step("Thread: cut the M8 threads",
         lambda: cmd("thread", {"Round faces to thread": [ref(f) for f in faces("Cylinder")
                                                          if abs(abs(f["center"][0]) - 40) < 2 and abs(f["center"][1] + 15) < 2]}),
         lambda a, b: b["faces"] > a["faces"] + 10, workaround="Change type did not model the thread")

    step("Hole: M10 tapped lifting hole in the tower top",
         lambda: cmd("hole", {"Body or entry face": flat(2, 190)},
                     {"Hole type": "Threaded (modeled)", "Metric size": "M10 × 1.5", "Position": "World point",
                      "Point X": 20, "Point Y": 36, "Point Z": 190, "Extent": "Blind", "Hole depth": 15}),
         lambda a, b: b["faces"] > a["faces"] + 5)
    step("Mirror Feature: second lifting hole",
         lambda: cmd("mirror_feature", {MIRROR: [ref(next(f for f in faces("Cylinder") if f["center"][2] > 170 and f["center"][0] > 10 and f["radius"] < 5))],
                                        "Mirror plane or flat face": {"origin": "YZ"}}),
         lambda a, b: b["faces"] > a["faces"] + 5)
    step("Hole: 14 mm sensor port",
         lambda: cmd("hole", {"Body or entry face": flat(2, 18)}, {"Metric size": "Custom", "Diameter": 14, "Position": "World point",
                                                                   "Point X": 100, "Point Y": -10, "Point Z": 18, "Extent": "Through all"}))
    step("Mirror Feature: second sensor port",
         lambda: cmd("mirror_feature", {MIRROR: [cyl(7, (100, -10, 9), 3)], "Mirror plane or flat face": {"origin": "YZ"}}),
         lambda a, b: len([f for f in faces("Cylinder") if abs(f["radius"] - 7) < .01]) == 2)
    for s in (1, -1):
        step(f"Edit Distance: {'right' if s > 0 else 'left'} port 40 -> 28 mm from the end",
             lambda s=s: typed("edit_distance", {"Move (a flat face, or a hole, slot, pocket or boss)": [cyl(7, (s * 100, -10, 9), 3)],
                                                 "Measured from (a flat face or a feature)": [flat(0, s * 140)]}, {}, {"Distance": 28}),
             lambda a, b, s=s: any(abs(f["center"][0] - s * 112) < 2 for f in faces("Cylinder") if abs(f["radius"] - 7) < .01))

    def tilt_front():
        bottom = next(e for e in edges() if e["type"] == "Line" and all(abs(p[1] + 75) < .1 and abs(p[2]) < .1 for p in (e["from"], e["to"])))
        cmd("rotate_face", {"Flat faces to rotate": [flat(1, -75)], "Axis: a straight edge (optional)": eref(bottom)}, {"Rotation angle": -20})
        rounds = [f for f in faces("Cylinder") if abs(f["radius"] - 20) < .01 and f["center"][1] < 0]
        known("Rotate Face tilts the front face but leaves the R20 corner rounds upright, leaving a notch at each corner",
              f"{len(rounds)} corner rounds still vertical cylinders after a 20 degree tilt; the tilt was undone")
        cad.undo(1)
    tilt_front()

    def engrave():
        with cad.sketch(flat(1, 47)) as s:
            s.text("PX-280", at=(-31.4, -72), height=14, font="Arial", bold=True)
        cmd("emboss", {"Sketch (text or shape)": [s.obj.name], "Target body (found automatically)": BODY,
                       "Face to emboss onto (optional)": flat(1, 47)}, {"Style": "Engrave (cut in)", "Height / depth": 1.5})
        cad.delete(s.obj)
    step("Emboss: engrave PX-280 on the back of the tower", engrave, lambda a, b: b["volume"] < a["volume"] and b["faces"] > a["faces"] + 20)

    step("Fillet: tower root R6",
         lambda: cmd("fillet", {"Edges": [eref(e) for e in edges() if e["type"] == "Line" and along(e, 0) and at_z(e, 18)
                                          and any(abs(e["from"][1] - y) < .05 for y in (25, 47))]}, {"Radius": 6}))
    step("Fillet: gusset roots R4",
         lambda: cmd("fillet", {"Edges": [eref(e) for e in edges() if e["type"] == "Line" and along(e, 1) and at_z(e, 18)
                                          and any(abs(abs(e["from"][0]) - x) < .05 for x in (64, 80)) and min(e["from"][1], e["to"][1]) < -20]},
                     {"Radius": 4}))
    step("Fillet: base top rim R1",
         lambda: cmd("fillet", {"Edges": [eref(e) for e in edges() if at_z(e, 18) and (
             all(abs(abs(p[0]) - 140) < .05 for p in (e["from"], e["to"])) or all(abs(p[1] + 75) < .05 for p in (e["from"], e["to"])) or
             all(abs(p[1] - 55) < .05 for p in (e["from"], e["to"])) or (e["type"] == "Circle" and abs(e.get("radius", 0) - 20) < .05))]},
                     {"Radius": 1}))

    check = cad.call("solid_check", object=BODY)
    assert check["valid"] and check["isSolid"] and check["freeEdges"] == 0 and check["solids"] == 1, check
    report["final"] = {k: check[k] for k in ("volume", "faces", "edges", "size", "freeEdges")}
    return check


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True, help="automation.json of a private Prism desktop instance")
    parser.add_argument("--step", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path, help="JSON report")
    parser.add_argument("--save", action="store_true", help="also save imported/revised .prism and the revised STEP next to the report")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        step_path = args.step.resolve()
        report["source_sha256"] = hashlib.sha256(step_path.read_bytes()).hexdigest()
        probe_known_issues(step_path)
        folder = args.out.resolve().parent
        if args.save:
            import_bracket(step_path)
            cad.save(str(folder / "pump-bracket-imported.prism"))
        rework(step_path)
        if args.save:
            cad.save(str(folder / "pump-bracket-revised.prism"))
            cad.call("file", action="export_step", path=str(folder / "pump-bracket-revised.step"))
        args.out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
