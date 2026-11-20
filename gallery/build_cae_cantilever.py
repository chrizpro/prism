"""Build, solve and reopen the gallery's three-study cantilever in Prism."""
import argparse
import base64
import csv
import hashlib
import json
import math
from pathlib import Path

import build_examples as study
from build_wrench import analysis

cad = study.cad
MODEL = study.ROOT / "gallery/models/cae-cantilever.prism"
PREVIEW = study.ROOT / "gallery/previews/cae-cantilever.png"
EVIDENCE = study.ROOT / "docs/interface/gallery/evidence/cae-cantilever.json"
BODY = "A36 steel cantilever"
LENGTH, WIDTH, HEIGHT = 200, 25, 12
MESH, FORCE, YOUNG = 4, 50, 200_000       # mm, N, N/mm²
DENSITY = 7850                           # kg/m³
MODAL = "Clamped bar - natural frequencies"
THERMAL = "Hot clamp to cold tip - steady conduction"


def end_faces():
    faces = cad.call("solid_find", object=BODY, kind="faces", type="Plane", limit=10)["hits"]
    picks = []
    for end_x in (-LENGTH / 2, LENGTH / 2):
        face, = [f for f in faces if abs(f["center"][0] - end_x) < 1e-6
                 and abs(f["area"] - WIDTH * HEIGHT) < 1e-6]
        picks.append([{"object": BODY, "face": face["face"]}])
    return picks


def static_check():
    result = cad.call("study_run", show="displacement")["results"]
    inertia = WIDTH * HEIGHT**3 / 12
    expected = FORCE * LENGTH**3 / (3 * YOUNG * inertia)
    assert abs(result["maxDisplacement_mm"] / expected - 1) < .05, result
    for key in ("appliedNet_N", "reactionNet_N"):
        assert abs(result[key] - FORCE) < .1, result
    return {"expectedTip_mm": expected, **result}


def verify(source):
    cad.call("file", action="open", path=str(MODEL))
    saved = cad.call("workflow_state")
    current, = saved["staticStudies"]
    assert current["active"] and current["body"] == BODY, saved
    assert {s["name"]: s["kind"] for s in saved["caeSetups"]} == {
        MODAL: "Modal", THERMAL: "SteadyThermal"}, saved
    assert cad.get(BODY).info["valid"]
    assert "results" not in cad.call("study_state"), "The study should reopen ready to run."
    static = static_check()
    cad.call("view", yaw=-20, pitch=19, target=[0, 0, 6],
             distance=300, grid=False, orthographic=False, display="shaded")
    cad.call("wait_idle")
    shot = cad.call("screenshot", viewport=True, max_width=960)
    PREVIEW.write_bytes(base64.b64decode(shot["_image"]["data"]))
    return {"desktopSource": source, "modelSha256": hashlib.sha256(MODEL.read_bytes()).hexdigest(),
            "savedStudies": saved, "static": static}


def build(source):
    cad.call("document_new")
    body = cad.box(width=LENGTH, depth=WIDTH, height=HEIGHT, operation="New body")
    body.rename(BODY)
    cad.call("material_apply", object=BODY, material="a36")
    fixed, tip = end_faces()
    cad.call("workspace", id="cae")
    cad.call("study_new", object=BODY, element_size=MESH)
    cad.call("run_command", name="sim_fixed", slots={"Faces to support": fixed})
    cad.call("run_command", name="sim_force", slots={"Faces to load": tip},
             params={"Force": FORCE, "Direction": "-Z"})
    static_check()
    print("Static bending passed", flush=True)

    common_slots = {"Solid bodies": [BODY]}
    common_params = {"Element size (0 = automatic)": MESH}
    modal_rows = analysis("cae_modal", {**common_slots, "Fixed support faces": fixed},
                          {**common_params, "Number of modes": 3, "Setup name": MODAL})
    modes = [float(r.split(": ", 1)[1].split()[0]) for r in modal_rows if r.startswith("Mode ")]
    inertia_si = WIDTH * HEIGHT**3 / 12 * 1e-12
    area_si = WIDTH * HEIGHT * 1e-6
    expected_hz = 1.875104**2 / (2 * math.pi * (LENGTH / 1000)**2) * math.sqrt(
        YOUNG * 1e6 * inertia_si / (DENSITY * area_si))
    assert len(modes) == 3 and modes[0] < modes[1] < modes[2], modes
    assert abs(modes[0] / expected_hz - 1) < .05, modes
    print("Modal results:", modes, flush=True)

    heat_rows = analysis("cae_thermal", {
        **common_slots, "First temperature faces": fixed, "Second temperature faces": tip},
        {**common_params, "Thermal analysis": "Steady conduction",
         "First temperature": 100, "Second temperature": 20, "Setup name": THERMAL})
    folder = next(Path(r.split(": ", 1)[1]) for r in heat_rows if r.startswith("Result files: "))
    with (folder / "temperatures.csv").open(newline="") as file:
        nodes = [(float(r["x_mm"]), float(r["temperature_C"])) for r in csv.DictReader(file)]
    mid_x, mid_temp = min(nodes, key=lambda n: abs(n[0]))
    max_error = max(abs(t - (60 - 0.4 * x)) for x, t in nodes)
    assert max_error < .1 and abs(mid_x) < 3, (max_error, mid_x, mid_temp)
    print(f"Thermal gradient passed at {len(nodes)} nodes", flush=True)

    cad.call("select_tab", title="Static")
    cad.select()
    cad.save(str(MODEL))
    report = verify(source)
    report["modal"] = {"expectedFirst_Hz": expected_hz, "frequencies_Hz": modes}
    report["thermal"] = {"nodes": len(nodes), "midpointSample_mm": mid_x,
                         "midpointSample_C": mid_temp, "maxLinearProfileError_C": max_error}
    EVIDENCE.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--desktop-source", required=True, help="Git revision of the running desktop build")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        build(args.desktop_source)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
