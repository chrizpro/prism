"""Build the Kingfisher shell study through the Prism MCP stdio bridge."""
import base64
import json
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs" / "kingfisher-study"
MODEL = OUT / "Kingfisher.prism"
# x, half-beam, sheer height, keel depth below sheer, crown above sheer (mm).
STATIONS = [
    (-2400, 1, 180, 0, 0), (-2200, 30, 152, 44, 34),
    (-1730, 108, 128, 113, 76), (-1120, 202, 122, 174, 92),
    (-530, 273, 123, 208, 83), (0, 302, 121, 223, 64),
    (540, 294, 115, 211, 44), (1120, 242, 113, 169, 35),
    (1700, 156, 126, 114, 32), (2120, 58, 159, 55, 26),
    (2350, 1, 200, 0, 0)
]


def finish(obj, name, color):
    if obj.info.get("valid") is False:
        raise ValueError(f"{name} has invalid topology: {obj.info}")
    obj.rename(name)
    cad.call("material_apply", object=name, material="abs", color=color, roughness=.32)
    return obj


def skin(deck):
    curves = []
    for x, beam, sheer, depth, crown in STATIONS:
        points = []
        for t in [-1, -.9, -.65, -.33, 0, .33, .65, .9, 1]:
            if deck:
                peak = max(0, min(1, -x / 1100))
                rise = crown * ((1-peak)*(1-t*t) + peak*(1-abs(t))**1.25)
            else:
                rise = -depth * math.sqrt(1-t*t) * (1-.13*abs(t))
            points.append((x, t*beam, sheer+rise))
        curves.append(cad.run("curve_through", points=points)[0])
    shell = cad.run("loft_surface", slots={"Sections":curves})[0]
    cad.delete(*curves)
    return shell


def build():
    cad.call("document_new")
    finish(skin(False), "Copper hull", "#C87742")
    deck = finish(skin(True), "Kingfisher deck", "#117785")
    with cad.sketch("XY", offset=-115) as opening:
        opening.ellipse((220, 0), (665, 0), 205)
    tool = cad.extrude(opening.obj, distance=470, operation="New body")
    trim = cad.run_report("trim_surface", slots={"Surface to trim": deck, "Cutting tool": tool})
    change, = trim["changed"]
    if change["name"] != deck.name or not change["valid"] or change["areaDelta"] >= 0:
        raise ValueError(f"Cockpit trim did not remove deck area: {trim}")
    cad.delete(opening.obj)
    # Trim a second copy of the crown to an annular footprint. This keeps the
    # lip flush even where the deck falls away at the sides of the opening.
    band = skin(True)
    with cad.sketch("XY", offset=-115) as footprint:
        footprint.ellipse((220, 0), (681, 0), 221)
        footprint.ellipse((220, 0), (657, 0), 195)
    annulus = cad.extrude(footprint.obj, distance=470, operation="New body")
    cad.run("trim_surface", slots={"Surface to trim":band, "Cutting tool":annulus},
            keep="The back side")
    cad.delete(footprint.obj)
    rim = cad.thicken(band, thickness=10)
    finish(rim, "Cockpit coaming", "#26373C")
    cad.move(rim, z=7)
    # Molded seat pan: transverse splines form a shallow scoop and raised backrest.
    curves = []
    for x, width, floor, lip in [(-75,72,31,7),(0,152,12,24),(185,179,-15,48),
                                  (340,161,-8,49),(430,143,47,32),(477,113,105,12)]:
        pts = [(x, width*t, floor+lip*t*t) for t in (-1,-.65,0,.65,1)]
        curves.append(cad.run("curve_through", points=pts)[0])
    seat = cad.run("loft_surface", slots={"Sections":curves})[0]
    cad.delete(*curves)
    finish(seat, "Molded seat", "#38454A")
    cad.call("view", yaw=-56,pitch=24,fit=True,orthographic=True,grid=False,display="shaded")
    cad.save(str(MODEL))


def inspect():
    cad.call("file", action="open", path=str(MODEL))
    names = ["Copper hull","Kingfisher deck","Cockpit coaming","Molded seat"]
    checks = {name: cad.call("object_info", object=name) for name in names}
    assert all(body["valid"] for body in checks.values()), checks
    assert checks["Cockpit coaming"]["kind"] == "Solid" and checks["Cockpit coaming"]["volume"] > 0, checks
    profile = cad.call("solid_profile", object=names[:2], axis="x", at=[-1730,0,1700])
    (OUT/"inspection.json").write_text(json.dumps(dict(checks=checks, profile=profile), indent=2))
    shot = cad.call("screenshot", views=["top", "front"], object=names, columns=1, cell_width=1000)
    (OUT/"plan.png").write_bytes(base64.b64decode(shot["_image"]["data"]))

    cad.call("view", yaw=-44,pitch=24,distance=3950,target=[-25,0,68],orthographic=True,grid=False)
    cad.call("run_command", name="render", commit=False)
    try:
        cad.call("command_set", params={"Picture size":"HD  1280 \u00d7 720",
                 "Quality (samples per pixel)":192, "Light bounces":4,
                 "Background":"White","Denoise final image":True,
                 "Ground plane with shadows":True,"Start render":True})
        deadline = time.monotonic()+600
        while time.monotonic() < deadline:
            status = cad.call("render_control", action="status")
            if status.get("error"):
                raise RuntimeError(status)
            if status.get("finished"):
                break
            time.sleep(1)
        else:
            raise TimeoutError("Kingfisher render")
        cad.call("render_control", action="save",path=str(OUT/"overview.png"))
    finally:
        cad.call("render_control",action="close")
        cad.call("command_cancel")


if __name__ == "__main__":
    OUT.mkdir(parents=True,exist_ok=True)
    discovery = study.ROOT / "artifacts/gallery/kingfisher-automation.json"
    bridge, log = study.connect_mcp(discovery)
    try:
        build()
        inspect()
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
