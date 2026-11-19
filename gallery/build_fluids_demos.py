"""Build and exercise the fluids gallery studies in a dedicated Prism desktop."""
import argparse
import base64
import json
import math
import re
import time

import build_examples as study

cad = study.cad
ROOT = study.ROOT
DUCT = "laminar-flow-duct"
NETWORK = "symmetric-pipe-network"
FLUID = "Fluid volume - 20 mm bore"
SPEED = 40 * .001002 / (998.2 * .02)
FLOW = SPEED * math.pi * .01**2 * 60000


def open_model(model, command):
    path = ROOT / "gallery/models" / (model + ".prism")
    cad.call("workspace", id="cae")
    cad.call("select_tab", title="Fluids", command=command)
    cad.select()
    cad.save(str(path))
    saved = json.loads(path.read_bytes())
    assert (saved["Tab"], saved["OpenCommand"]) == ("Fluids", command), saved.keys()
    cad.call("file", action="open", path=str(path))
    for obj in cad.objects():
        assert cad.get(obj.name).info["valid"], obj.name
    # The file tool opens the CAD session directly. The gallery applies the opening command.
    cad.call("run_command", name=command, commit=False)
    reply = cad.call("command_set", params={})
    assert not reply.get("error"), reply
    return reply.get("results", [])


def capture(model, yaw, pitch, target, distance):
    cad.call("view", yaw=yaw, pitch=pitch, target=target, distance=distance,
             grid=False, orthographic=False, display="shaded")
    cad.call("wait_idle")
    image = cad.call("screenshot", viewport=True, max_width=1250)["_image"]["data"]
    (ROOT / "gallery/previews" / (model + ".png")).write_bytes(base64.b64decode(image))


def number(rows, label):
    row = next(r for r in rows if r.startswith(label + ": "))
    return float(re.search(r"-?[0-9][0-9,.]*(?:[eE][+-]?\d+)?", row.split(": ", 1)[1])[0].replace(",", ""))


def duct():
    cad.call("document_new")
    pipe = cad.cylinder(radius=10, height=120, operation="New body")
    pipe.rename(FLUID)
    cad.rotate(pipe, 90, axis="Y")
    cad.call("material_apply", object=FLUID, color="#83ACBD")
    open_model(DUCT, "cae_flow")
    faces = cad.call("solid_find", object=FLUID, kind="faces", type="Plane", limit=6)["hits"]
    inlet, outlet = sorted(faces, key=lambda face: face["center"][0])
    picks = {"Body": [FLUID], "Inlet face": {"object": FLUID, "face": inlet["face"]},
             "Outlet face": {"object": FLUID, "face": outlet["face"]}}
    cad.call("command_set", slots=picks, params={"Flow": 1, "Fluid": 1, "Speed": SPEED, "Detail": 1})
    cad.call("command_set", params={"Run flow": True})
    for _ in range(300):
        rows = cad.call("command_set", params={}).get("results", [])
        if any(r.startswith("Could not run:") for r in rows):
            raise RuntimeError(rows)
        if any(r.startswith("Pressure drop:") for r in rows):
            break
        time.sleep(2)
    else:
        raise TimeoutError(rows)
    print(rows, flush=True)
    assert number(rows, "Reynolds number") == 40 and "Settled: yes" in rows, rows
    flow_in, flow_out = [float(v.strip().split()[0]) for v in
                         next(r for r in rows if r.startswith("Flow in / out: ")).split(": ",1)[1].split(" / ")]
    assert abs(flow_out-flow_in) / flow_in < .08 and abs(flow_in/FLOW-1) < .08, rows
    assert abs(number(rows, "Peak speed")/SPEED-2) < .35, rows
    target = cad.get(FLUID).info["center"]
    cad.call("object_update", object=FLUID, visible=False)
    capture(DUCT, -19, 12, target, 175)
    cad.call("object_update", object=FLUID, visible=True)
    cad.call("command_cancel")
    cad.call("run_command", name="cae_pipe_flow", commit=False)
    analytical = cad.call("command_set", params={
        "Internal diameter": 20, "Pipe length": 120, "Volume flow": FLOW})["results"]
    expected = 32 * .001002 * SPEED * .12 / .02**2
    assert abs(number(analytical, "Pressure drop")/expected-1) < .0001, analytical
    cad.call("command_cancel")
    return {"setup": picks, "speed_m_s": SPEED, "flow_L_min": FLOW, "3d": rows,
            "fullyDevelopedPressureDrop_Pa": expected, "pipeFlow": analytical}


def network():
    cad.call("document_new")
    # Axis lengths match the opening panel. Draw cylinders at six times bore
    # diameter so the schematic reads clearly at full extent.
    for name, start, end, bore, color in [
        ("A to B - 40 mm bore", (0,0), (5000,0), 40, "#AD7951"),
        ("B to C - 25 mm bore", (5000,0), (9800,6400), 25, "#377C94"),
        ("B to D - 25 mm bore", (5000,0), (9800,-6400), 25, "#377C94"),
    ]:
        dx, dy = end[0]-start[0], end[1]-start[1]
        pipe = cad.cylinder(radius=bore*3, height=math.hypot(dx,dy), operation="New body")
        pipe.rename(name)
        cad.rotate(pipe, 90, axis="Y")
        cad.rotate(pipe, math.degrees(math.atan2(dy,dx)), axis="Z")
        center = cad.get(name).info["center"]
        cad.move(pipe, x=(start[0]+end[0])/2-center[0],
                 y=(start[1]+end[1])/2-center[1], z=-center[2])
        cad.call("material_apply", object=name, color=color)
    # End markers keep the junctions readable when the long pipes are fit on screen.
    for label, x, y, color in [("A - 200 kPa",0,0,"#AD7951"), ("B - split",5000,0,"#73AAA4"),
                               ("C - 0 kPa",9800,6400,"#377C94"), ("D - 0 kPa",9800,-6400,"#377C94")]:
        dot = cad.sphere(radius=240, position_x=x, position_y=y, operation="New body")
        dot.rename(label)
        cad.call("material_apply", object=label, color=color)
    rows = open_model(NETWORK, "cae_pipe_network")
    flows = [number(rows, f"{a} \u2192 {b}") for a, b in ("AB", "BC", "BD")]
    assert min(flows) > 0 and abs(flows[1]-flows[2]) < .01
    assert abs(flows[0]-sum(flows[1:])) < .01, rows
    cad.call("command_cancel")
    capture(NETWORK, 0, 89, [4900,0,0], 30500)
    return {"rows": rows, "branchFlows_L_min": flows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--model", choices=[DUCT, NETWORK], required=True)
    parser.add_argument("--desktop-source", required=True)
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        report = duct() if args.model == DUCT else network()
        report["desktopSource"] = args.desktop_source
        path = ROOT / "docs/interface/gallery/evidence" / (args.model + ".json")
        path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf8")
        print(json.dumps(report, indent=2), flush=True)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()

