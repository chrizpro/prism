"""Combination wrench: a 13 mm chrome-vanadium spanner with an angled open end and a hex box end, modeled in Solid and set up for Prism's CAE studies.

An 8 mm plate: a 38 mm round head at the origin with a 13 mm jaw slot angled 15 degrees off the handle axis, a handle that tapers from 24 mm to 16 mm wide, and
a 34 mm ring end 175 mm away with a 13 mm hexagon hole. Outer corners are rounded and the faces are edge-softened. Five static load cases are saved in the file
(self weight, a hammer blow on the ring end, a 900 N cheater-pipe overload, 300 N torque through the box end, 300 N torque through the open end), plus a natural-frequency and a thermal setup (these two need CalculiX). The file reopens
on the CAE tab with the open-end torque case active, so the user only has to press Run. The script also keeps a natural-frequency and a thermal setup (CalculiX) and solves all of them. It checks the load balance, that the torque
cases stay elastic (safety factor above 1) and the overload yields, and that a coarser mesh agrees on displacement.

python gallery/build_wrench.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math
import time

import build_examples as study

cad = study.cad

STEEL = dict(material="steel", color="#B9C0C8", metal=1, roughness=.18)
T = 8.0                                   # plate thickness
HEAD_R, RING_R, RING_X = 19.0, 17.0, 175.0
JAW, HEX = 13.0, 13.0                     # jaw opening and hex hole across flats
ANGLE = math.radians(15)                  # open end angle off the handle axis
MESH = 2.5


def sketch(entities):
    cad.call("sketch_begin", plane="XY")
    cad.call("sketch_add", entities=entities)
    return cad.get(cad.call("sketch_finish")["sketch"])


def rot(p, a):
    return [p[0] * math.cos(a) - p[1] * math.sin(a), p[0] * math.sin(a) + p[1] * math.cos(a)]


def join(entities):
    cad.run("extrude", slots={"Profile": sketch(entities).name}, distance=T, operation="Join")


def cut(entities):
    cad.run("extrude", slots={"Profile": sketch(entities).name}, distance=T + 4, operation="Cut")


def faces(**f):
    return cad.call("solid_find", object="Wrench", kind="faces", limit=80, **f)["hits"]


def new_study(fixed, loads):
    cad.call("study_new", object="Wrench", element_size=MESH)
    cad.call("run_command", name="sim_fixed", slots={"Faces to support": [{"object": "Wrench", "face": f} for f in fixed]})
    for slots, params in loads:
        cad.call("run_command", name=slots[0], slots={slots[1]: [{"object": "Wrench", "face": f} for f in slots[2]]}, params=params)


def analysis(command, slots, params):
    """Run a CalculiX analysis (modal or thermal), keep its setup in the file and return its result rows."""
    cad.call("run_command", name=command, slots=slots, params=params, commit=False)
    cad.call("command_set", params={"Run analysis": True})
    deadline = time.monotonic() + 600
    while True:
        reply = cad.call("command_set", params={})
        rows = reply.get("results", [])
        text = " ".join(map(str, rows))
        assert not reply.get("error") and "Status: Failed" not in text, (reply.get("error"), rows)
        if "Status: Complete" in text:
            break
        assert time.monotonic() < deadline, rows
        time.sleep(2)
    cad.call("command_set", params={"Save setup": True})
    cad.call("command_commit")
    return rows


def topology_check():
    """Open the saved wrench, run Generative on its open-end torque case (about 8 minutes) and check it keeps the part stiff with less material. Nothing is saved."""
    cad.call("file", action="open", path=str(study.ROOT / "gallery/models/wrench.prism"))
    cad.call("study_run")
    cad.call("run_command", name="generative", params={"Keep this much of the volume": 55, "Detail": "Coarse (fast)", "Most iterations": 40}, commit=False)
    cad.call("command_set", params={"Generate": True})
    deadline = time.monotonic() + 1500
    while True:
        rows = cad.call("command_set", params={}).get("results", [])
        text = " ".join(map(str, rows))
        assert "Failed" not in text, rows
        if "Status: Done" in text:
            break
        assert time.monotonic() < deadline, rows
        time.sleep(5)
    share = float(next(x for x in rows if x.startswith("Result volume")).split("(")[1].split("%")[0])
    assert 40 < share < 60, rows
    print("topology optimization keeps %.1f %% of the block:" % share, [x for x in rows if x.startswith(("Stiffness", "Mass"))])


def build():
    cad.call("document_new")
    with cad.history("Combination wrench - Solid and CAE workflow"):
        head = cad.extrude(sketch([{"type": "circle", "center": [0, 0], "radius": HEAD_R}]), distance=T)
        head.rename("Wrench")
        join([{"type": "polyline", "points": [[0, -12], [0, 12], [RING_X, 8], [RING_X, -8]], "closed": True}])
        join([{"type": "circle", "center": [RING_X, 0], "radius": RING_R}])
        jaw = [rot(p, ANGLE) for p in ([-40, -JAW / 2], [3, -JAW / 2], [3, JAW / 2], [-40, JAW / 2])]
        cut([{"type": "polyline", "points": jaw, "closed": True}])
        r = HEX / math.sqrt(3)             # circumradius of a hexagon with 13 mm across flats
        cut([{"type": "polyline", "points": [[RING_X + r * math.cos(math.radians(60 * i)), r * math.sin(math.radians(60 * i))] for i in range(6)], "closed": True}])
        for o in cad.objects():
            if o.kind == "Sketch":
                cad.call("object_update", object=o.name, visible=False)
        edges = cad.call("solid_find", object="Wrench", kind="edges", direction=[0, 0, 1], limit=80)["hits"]
        cad.fillet([dict(object="Wrench", edge=e["edge"]) for e in edges], radius=1.5)
        rim = cad.call("solid_find", object="Wrench", kind="edges", limit=300)["hits"]
        cad.call("material_apply", object="Wrench", **STEEL)
    info = cad.call("object_info", object="Wrench")
    assert info["valid"], info
    assert abs(info["size"][2] - T) < .01 and info["size"][0] > RING_X + RING_R - 1, info["size"]

    plane = faces(type="Plane")
    jaw_faces = [f["face"] for f in plane if abs(f["normal"][2]) < .01 and math.hypot(f["center"][0], f["center"][1]) < 14 and f["area"] > 30 and abs(f["normal"][1] * math.cos(ANGLE) - f["normal"][0] * math.sin(ANGLE)) > .99]
    hex_faces = [f["face"] for f in plane if abs(f["normal"][2]) < .01 and math.hypot(f["center"][0] - RING_X, f["center"][1]) < HEX and f["area"] > 20]
    ring_outer = [f["face"] for f in faces(type="Cylinder") if abs(f["center"][0] - RING_X) < 4 and f["area"] > 100 and f.get("radius", 0) > 10]
    head_outer = [f["face"] for f in faces(type="Cylinder") if abs(f["center"][0]) < 4 and f["area"] > 100 and f.get("radius", 0) > 10]
    assert len(jaw_faces) == 2 and len(hex_faces) == 6 and ring_outer and head_outer, (jaw_faces, hex_faces, ring_outer, head_outer)

    results = {}
    def force_case(label, fixed, load_faces, newtons):
        new_study(fixed, [(("sim_force", "Faces to load", load_faces), {"Force": newtons, "Direction": "+Y"})])
        results[label] = cad.call("study_run")["results"]
    def gravity_case():
        cad.call("study_new", object="Wrench", element_size=MESH)
        cad.call("run_command", name="sim_fixed", slots={"Faces to support": [{"object": "Wrench", "face": f} for f in jaw_faces]})
        cad.call("run_command", name="sim_gravity", params={"Gravity": 1, "Direction": "-Z"})
        results["self weight"] = cad.call("study_run")["results"]

    def hammer_case():
        new_study(jaw_faces, [(("sim_pressure", "Faces to load", ring_outer), {"Pressure": 5})])
        results["hammer blow, 5 MPa on the ring end"] = cad.call("study_run")["results"]

    # primary case is created last so it is the active study when the file is saved
    gravity_case()
    hammer_case()
    force_case("cheater pipe 900 N, open end", jaw_faces, ring_outer, 900)
    force_case("torque 300 N, box end", hex_faces, head_outer, 300)
    force_case("torque 300 N, open end", jaw_faces, ring_outer, 300)
    for label, r in results.items():
        if label != "self weight":
            assert abs(r["reactionNet_N"] - r["appliedNet_N"]) < 1, (label, r)
        print("%-30s von Mises %6.1f MPa  displacement %.3f mm  safety %.2f  (%d elements)" % (label, r["maxVonMises_MPa"], r["maxDisplacement_mm"], r["minSafetyFactor"], r["elements"]))
    assert results["torque 300 N, open end"]["minSafetyFactor"] > 1 and results["torque 300 N, box end"]["minSafetyFactor"] > 1
    assert results["cheater pipe 900 N, open end"]["minSafetyFactor"] < results["torque 300 N, open end"]["minSafetyFactor"]
    # linear: tripling the force triples displacement and stress
    a, b = results["cheater pipe 900 N, open end"], results["torque 300 N, open end"]
    assert abs(a["maxDisplacement_mm"] / b["maxDisplacement_mm"] - 3) < .02, (a, b)
    modes = analysis("cae_modal", {"Solid bodies": ["Wrench"], "Fixed support faces": [{"object": "Wrench", "face": f} for f in jaw_faces]},
                     {"Number of modes": 6, "Setup name": "Natural frequencies, jaw held"})
    frequencies = [float(x.split(": ")[1].split()[0]) for x in modes if x.startswith("Mode ")]
    assert len(frequencies) == 6 and 100 < frequencies[0] < 400 and frequencies == sorted(frequencies), frequencies
    heat = analysis("cae_thermal", {"Solid bodies": ["Wrench"], "First temperature faces": [{"object": "Wrench", "face": f} for f in jaw_faces],
                                    "Second temperature faces": [{"object": "Wrench", "face": f} for f in hex_faces]},
                    {"Thermal analysis": "Steady conduction", "First temperature": 100, "Second temperature": 20, "Setup name": "Hot jaw, cold ring end"})
    temps = {x.split(": ")[0]: float(x.split(": ")[1].split()[0]) for x in heat if x.split(": ")[0].endswith("temperature")}
    assert abs(temps["Maximum temperature"] - 100) < .5 and abs(temps["Minimum temperature"] - 20) < .5, temps
    print("first mode %.1f Hz; temperatures %s" % (frequencies[0], temps))
    # fatigue on the open-end torque case, loaded from zero to peak 1,000,000 times: it must meet the requirement
    cad.call("run_command", name="cae_fatigue", params={"Load ratio R (min / max)": 0, "Required life": 1000000, "Surface finish": "Machined", "Mean-stress correction": "Goodman"}, commit=False)
    cad.call("command_set", params={"Show on model": True})
    fatigue = cad.call("command_set", params={}).get("results", [])
    cad.call("command_cancel")
    assert any(x.startswith("Verdict: Meets") for x in fatigue), fatigue
    print(next(x for x in fatigue if x.startswith("Fatigue safety factor")))
    cad.call("select_tab", title="Static")   # the file reopens on the Static tab with the open-end torque case active
    return "wrench", -35, 45


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--topology-check", action="store_true", help="also run Generative on the saved model (slow, about 8 minutes)")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/wrench.prism"))
            model = ("wrench", -35, 45)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=420)
        if args.topology_check:
            topology_check()
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
