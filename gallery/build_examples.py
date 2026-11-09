"""Build three native gallery studies through the real Prism MCP stdio bridge.

Run from the checkout: python gallery/build_examples.py --discovery artifacts/gallery/automation.json
Prism/Scripting/python must be available (added below). No imported geometry or third-party Python packages.
"""
import argparse
import base64
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Prism/Scripting/python"))
import cadkit as cad
import cadkit.api as api


def connect_mcp(discovery):
    env = dict(os.environ, PRISM_AUTOMATION_DISCOVERY=str(Path(discovery).resolve()))
    bridge = subprocess.Popen(["dotnet", str(ROOT / "artifacts/gallery/bin/Prism.Mcp.dll")],
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                              text=True, encoding="utf-8", env=env, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    log = open(ROOT / "artifacts/gallery/modeling-mcp.jsonl", "a", encoding="utf-8")
    request_id = 0

    def request(method, params):
        nonlocal request_id
        request_id += 1
        bridge.stdin.write(json.dumps(dict(jsonrpc="2.0", id=request_id, method=method, params=params)) + "\n")
        bridge.stdin.flush()
        envelope = json.loads(bridge.stdout.readline())
        if "error" in envelope:
            raise RuntimeError(envelope["error"])
        return envelope["result"]

    request("initialize", dict(protocolVersion="2024-11-05", capabilities={}, clientInfo=dict(name="Prism gallery study", version="1")))
    bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
    bridge.stdin.flush()

    def call(tool, **args):
        # Obj instances and tuples are normalized by cadkit before reaching here.
        result = request("tools/call", dict(name="call_api", arguments=dict(name=tool, arguments=args)))
        text = next((c["text"] for c in result["content"] if c["type"] == "text"), "{}")
        log.write(json.dumps(dict(tool=tool, args=args, result=result), ensure_ascii=False) + "\n")
        log.flush()
        if result.get("isError"):
            raise cad.CadError(text)
        value = json.loads(text)
        for c in result["content"]:
            if c["type"] == "image":
                value["_image"] = c
        return value

    cad.call = api.call = call
    return bridge, log


def look(obj, name, **style):
    obj.rename(name)
    cad.call("material_apply", object=name, **style)
    return obj


def rounded_box(name, size, pos, radius, **style):
    obj = cad.box(width=size[0], depth=size[1], height=size[2],
                  position_x=pos[0], position_y=pos[1], position_z=pos[2], operation="New body")
    obj.rename(name)
    if radius:
        edges = cad.call("solid_find", object=name, kind="edges", limit=100)["hits"]
        cad.fillet([dict(object=name, edge=e["edge"]) for e in edges], radius=radius)
    cad.call("material_apply", object=name, **style)
    return obj


def side_table():
    cad.call("document_new")
    wood = dict(material="oak", color="#A87542", roughness=.38, pattern="Wood", patternScale=95,
                patternAngle=0, surface=dict(Coat=.32, CoatRoughness=.25, Bump=.13))
    steel = dict(material="a36", color="#28333B", metal=.65, roughness=.37,
                 surface=dict(Coat=.25, Bump=.04))
    with cad.history("Atelier side table — Solid workflow"):
        rounded_box("Oak top", (620, 420, 32), (0, 0, 465), 10, **wood)
        rounded_box("Oak lower shelf", (520, 326, 18), (0, 0, 120), 5, **wood)
        for x in (-260, 260):
            for y in (-163, 163):
                rounded_box(f"Steel leg {x} {y}", (24, 24, 455), (x, y, 10), 2, **steel)
                rounded_box(f"Rubber foot {x} {y}", (26, 26, 12), (x, y, 0), 3,
                            material="silicone", color="#14191F", roughness=.8)
        for y in (-163, 163):
            rounded_box(f"Top rail {y}", (520, 18, 28), (0, y, 432), 2, **steel)
        for x in (-260, 260):
            rounded_box(f"Cross rail {x}", (18, 326, 28), (x, 0, 432), 2, **steel)
    return "atelier-table", -35, 23


def pendant():
    cad.call("document_new")
    with cad.history("Halo pendant — Surface workflow"):
        # Open curve -> exact revolved sheet -> offset solid. The source sheet and profile are retained.
        profile = cad.run("curve_through", points=[[145, 0, 0], [149, 0, 12], [143, 0, 43], [120, 0, 82],
                      [84, 0, 116], [43, 0, 138], [24, 0, 154], [22, 0, 170]])[0]
        profile.rename("Shade meridian — source curve")
        sheet = cad.run("revolve_surface", slots={"Profile": profile.name}, axis="World Z", angle=360)[0]
        sheet.rename("Shade — source surface")
        work = cad.run("move", slots={"Objects": [sheet.name]}, x=0, y=0, z=0, create_copy=True)[0]
        body = cad.thicken(work, thickness=1.4)
        if isinstance(body, list):
            body = body[0]
        look(body, "Glazed teal shade", material="5052", color="#0D6F77", metal=.18, roughness=.27,
             surface=dict(Gradient=1, Axis=2, Color2=0xFF78C6BD, GradientSize=180, Coat=.7, CoatRoughness=.12, Bump=.03))
        for obj in (profile, sheet):
            cad.call("object_update", object=obj.name, visible=False)
        look(cad.cylinder(radius=23, height=22, position_z=167, operation="New body"), "Brass collar",
             material="brass", color="#CCA76B", metal=1, roughness=.25, pattern="Brushed", patternScale=2,
             surface=dict(Anisotropy=.55, Coat=.15))
        look(cad.cylinder(radius=2.5, height=155, position_z=189, operation="New body"), "Suspension cable",
             material="silicone", color="#293338", roughness=.8)
        look(cad.cylinder(radius=35, height=14, position_z=344, operation="New body"), "Ceiling canopy",
             material="5052", color="#263C40", roughness=.3, surface=dict(Coat=.55))
        look(cad.sphere(radius=35, position_z=65, operation="New body"), "Frosted bulb",
             material="glass", color="#FFF0CF", roughness=.55, surface=dict(Transmission=.3, Ior=1.45))
    return "halo-pendant", -35, -12


def lounge_chair():
    cad.call("document_new")
    upholstery = dict(material="tpu", color="#CD622F", roughness=.62,
                      surface=dict(Gradient=1, Axis=2, Color2=0xFFEB9C61, GradientSize=600, Bump=.2, Coat=.08))
    with cad.history("Cove lounge chair — Freeform workflow"):
        seat = cad.form_box(width=580, depth=500, height=100, faces_x=6, faces_y=6, faces_z=3)
        positions = {}
        for i, (x, y, z) in enumerate(cad.form_info(seat)["vertices"]):
            v = z / 100
            positions[str(i)] = [x * (1 - .045 * abs(y) / 250), y,
                                340 + z - 30 * (y / 250) + 9 * (x / 290)**2 * v]
        cad.form_op(seat, "set_vertices", positions=positions)
        look(seat, "Sculpted seat cushion", **upholstery)
        back = cad.form_box(width=580, depth=105, height=390, faces_x=6, faces_y=3, faces_z=6)
        positions = {}
        for i, (x, y, z) in enumerate(cad.form_info(back)["vertices"]):
            t = z / 390
            positions[str(i)] = [x * (1 - .10 * t), 205 + y + 110*t + 15*math.sin(math.pi*t),
                                380 + z + 18*(x/290)**2]
        cad.form_op(back, "set_vertices", positions=positions)
        look(back, "Sculpted back cushion", **upholstery)
        for x in (-307, 307):
            arm = cad.form_box(width=74, depth=510, height=78, faces_x=3, faces_y=6, faces_z=3)
            positions = {}
            for i, (xx, y, z) in enumerate(cad.form_info(arm)["vertices"]):
                positions[str(i)] = [x+xx, y+25, 522+z+30*(y/255)]
            cad.form_op(arm, "set_vertices", positions=positions)
            look(arm, "Soft arm " + str(x), **upholstery)
        metal = dict(material="304", color="#8C999D", metal=1, roughness=.27, pattern="Brushed", patternScale=3,
                     surface=dict(Anisotropy=.65, Coat=.12))
        # Exact steel frame carries the editable upholstered cages.
        for x in (-250, 250):
            for y in (-190, 220):
                rounded_box(f"Frame leg {x} {y}", (25, 25, 350), (x, y, 0), 3, **metal)
            rounded_box(f"Seat side rail {x}", (25, 450, 24), (x, 15, 328), 3, **metal)
            # Put the posts under the arms, outside the back cushion's full cage bounds.
            arm_x = -307 if x < 0 else 307
            for y in (-145, 160):
                rounded_box(f"Arm outrigger {x} {y}", (75, 18, 18), ((x + arm_x)/2, y, 340), 2, **metal)
                rounded_box(f"Arm support {arm_x} {y}", (18, 18, 220), (arm_x, y, 345), 2, **metal)
        for y in (-190, 220):
            rounded_box(f"Seat cross rail {y}", (500, 25, 24), (0, y, 328), 3, **metal)
    return "cove-chair", -35, 15


def verify_and_save(id, yaw, pitch, render=True, distance=None):
    cad.select()
    cad.call("view", yaw=yaw, pitch=pitch, fit=True, grid=False, display="shaded")
    if distance is not None: cad.call("view", distance=distance)
    cad.call("wait_idle")
    path = ROOT / "gallery/models" / (id + ".prism")
    cad.save(str(path))
    reports = []
    for obj in cad.objects():
        info = cad.call("object_info", object=obj.name)
        if obj.kind == "Form":
            cage = cad.form_info(obj, summary=True)
            assert cage["closed"] and not cage["topology"]["windingConflicts"] and not cage["topology"]["nonManifoldEdges"], cage
            info["cage"] = cage
        if obj.kind in ("Solid", "Surface"):
            assert info["valid"], info
        reports.append(info)
    cad.call("file", action="open", path=str(path))
    reopened = cad.objects()
    assert len(reopened) == len(reports)
    for obj in reopened:
        if obj.kind in ("Solid", "Surface"):
            assert cad.call("object_info", object=obj.name)["valid"]
    cad.call("view", yaw=yaw, pitch=pitch, fit=True, grid=False, display="shaded")
    if distance is not None: cad.call("view", distance=distance)
    cad.call("wait_idle")
    try:
        screenshot = cad.call("screenshot", viewport=True, max_width=1200)
        (ROOT / "artifacts/gallery" / (id + "-viewport.png")).write_bytes(base64.b64decode(screenshot["_image"]["data"]))
    except cad.CadError as error:  # window capture is implemented on Windows only
        print("viewport screenshot skipped:", error, flush=True)
    (ROOT / "artifacts/gallery" / (id + "-validation.json")).write_text(json.dumps(reports, indent=2))
    if render:
        cad.call("run_command", name="render", commit=False)
        cad.call("command_set", params={"Picture size": "Draft  960 × 540", "Quality (samples per pixel)": 96,
                 "Light bounces": 6, "Background": "White", "Denoise final image": True,
                 "Ground plane with shadows": id != "halo-pendant", "Start render": True})
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            status = cad.call("render_control", action="status")
            if status.get("error"):
                raise RuntimeError(status)
            if status.get("finished"):
                break
            time.sleep(.5)
        assert status.get("finished"), status
        cad.call("render_control", action="save", path=str(ROOT / "artifacts/gallery" / (id + "-render.png")))
        cad.call("render_control", action="close")
        cad.call("command_cancel")
        cad.save(str(path))
    print(id, "saved, reopened and verified", len(reports), "objects", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--only", choices=["table", "pendant", "chair"])
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = connect_mcp(args.discovery)
    try:
        for key, build in [("table", side_table), ("pendant", pendant), ("chair", lounge_chair)]:
            if not args.only or key == args.only:
                print("Building", key, flush=True)
                verify_and_save(*build(), render=not args.no_render)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
