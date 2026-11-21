"""Build and verify the planar coupe in a dedicated Prism automation session.

Run: python gallery/build_planar_coupe.py --discovery <dedicated discovery.json>
The build starts a new document in that session. Distances are millimetres.
"""
import argparse
import math
import time
from pathlib import Path

import build_bellavista as shapes
import build_examples as study

cad = study.cad
model = study.ROOT / "gallery/models/planar-coupe.prism"
preview = study.ROOT / "gallery/previews/planar-coupe.png"
paint = dict(material="6061", color="#22605E", metal=.44, roughness=.26,
             surface=dict(Coat=.72, CoatRoughness=.16))
trim = dict(material="abs", color="#222929", roughness=.42)
rubber = dict(material="silicone", color="#232526", roughness=.7)
alloy = dict(material="6061", color="#C6C4B9", metal=.84, roughness=.29)
glass = dict(material="glass", color="#A9BCC2", roughness=.08,
             surface=dict(Transmission=.83, Ior=1.49))
leather = dict(material="abs", color="#A7764F", roughness=.56)
headlight = dict(material="abs", color="#E5DFBF", roughness=.25)
taillight = dict(material="abs", color="#9F2D27", roughness=.22)
axles = (-1260, 1230)


def ruled(name, sections, look):
    return shapes.loft(name, sections, look, degree=1, ruled=True)


def plate(name, outline, offset, look):
    other = [[v+d for v, d in zip(p, offset)] for p in outline]
    return ruled(name, [outline, other], look)


def cut(body, cutters):
    cad.subtract(body, *cutters)
    for cutter in cutters:
        shapes.KNOWN.pop(cutter.name)


def coachwork():
    # Each station describes the same eight corners, preserving the crease.
    stations = [(-2110, 736, 476, 435, 295), (-1790, 822, 595, 555, 185),
                (-1260, 875, 724, 690, 165), (-680, 875, 770, 690, 165),
                (1230, 875, 770, 690, 165), (1710, 858, 790, 691, 192),
                (2010, 802, 745, 635, 290)]
    sections = []
    for x, width, deck, crease, sill in stations:
        half = [(width*.74, deck), (width, crease),
                (width-.08*(crease-sill-60), sill+60), (width-101, sill)]
        sections.append([[x,y,z] for y,z in half + [(-y,z) for y,z in reversed(half)]])
    body = ruled("Folded coachwork", sections, paint)
    initial_volume = cad.get(body.name).info["volume"]
    cut(body, [shapes.disc(f"Arch tool {x}", (x,0,315), 346, 2100, trim) for x in axles])
    arches = cad.get(body.name).info
    assert arches["valid"] and initial_volume - arches["volume"] > 500_000_000, arches
    well = shapes.box("Cabin tool", (1920,1240,800), (280,0,430), trim)
    cut(body, [well])
    assert cad.get(body.name).info["valid"]


def cabin():
    # One plane per cabin side contains the door glass and quarter light.
    def width(z):
        return 738 - .36*(z-751)

    def screen(name, bottom, top):
        x,z = bottom
        u,h = top
        plate(name, [[x,-width(z),z],[x,width(z),z],[u,width(h),h],[u,-width(h),h]],
              [0,0,-6], glass)

    screen("Windscreen", (-715,752), (26,1135))
    screen("Fastback glass", (1410,752), (705,1135))
    roof = [[0,-width(1135)-10,1135],[0,width(1135)+10,1135],
            [720,width(1135)+10,1135],[720,-width(1135)-10,1135]]
    plate("Roof", roof, [0,0,19], paint)
    for side in (-1,1):
        def side_panel(name, xz, look, depth):
            plate(f"{name} {side}", [[x,side*width(z),z] for x,z in xz],
                  [0,side*depth,0], look)
        side_panel("Door glass", [(-707,752),(25,1135),(602,1135),(684,752)], glass, 5)
        side_panel("Quarter light", [(684,752),(602,1135),(705,1135),(1408,752)], glass, 5)
        side_panel("A pillar", [(-730,752),(-670,752),(66,1135),(14,1135)], paint, 13)
        side_panel("Window divider", [(662,752),(693,752),(618,1135),(589,1135)], trim, 10)
        side_panel("Sail pillar", [(1322,752),(1450,752),(732,1135),(679,1135)], paint, 13)
        shapes.cushion(f"Seat cushion {side}", (430,425,105),(340,side*305,550),leather)
        shapes.cushion(f"Seat back {side}", (115,412,390),(535,side*305,762),leather,rake=.22)
        shapes.cushion(f"Head restraint {side}", (115,213,123),(588,side*305,958),leather)
    shapes.box("Instrument panel", (290,1170,101), (-397,0,727), trim, 23)
    shapes.ring("Steering wheel", (-176,-313,801), 136, 13, trim, axis="X")
    shapes.box("Steering crossbar", (23,259,26), (-175,-313,789),trim,6)


def wheels():
    for side in (-1,1):
        for x in axles:
            label = ("Front" if x < 0 else "Rear") + (" left" if side < 0 else " right")
            shapes.disc(label+" tyre",(x,side*762,315),315,208,rubber,rounded=32)
            wheel = shapes.disc(label+" alloy",(x,side*867,315),206,24,alloy,rounded=5)
            # Real holes reveal the dark tire behind the machined wheel face.
            cutters = []
            for i in range(12):
                angle = i*math.tau/12
                ctr = (x+157*math.cos(angle),side*867,315+157*math.sin(angle))
                cutters.append(shapes.disc(f"Vent tool {i}",ctr,19,44,trim))
            cut(wheel, cutters)
            shapes.ring(label+" rim",(x,side*878,315),198,6,alloy)
            shapes.disc(label+" hub",(x,side*882,315),67,18,alloy,rounded=6)


def exterior():
    for side in (-1,1):
        def flank(name, xz, look, depth=3):
            plate(f"{name} {side}", [[x,side*(875-.08*(690-z)+1),z] for x,z in xz],
                  [0,side*depth,0],look)
        flank("Intake surround",[(680,638),(1010,638),(847,307),(597,307)],trim)
        for i in range(5):
            z = 350+53*i
            left = 597+(z-307)*83/331+6
            right = 847+(z-307)*163/331-6
            flank(f"Intake vane {i}",[(left,z),(right,z),(right+2,z+6),(left+2,z+6)],
                  dict(color="#424B49",roughness=.46), 7)
        path = [(-650,690),(-694,308),(-649,261),(480,261),(630,690)]
        for i, ((x,z),(u,h)) in enumerate(zip(path,path[1:])):
            length = math.hypot(u-x,h-z)
            dx,dz = -(h-z)*4/length,(u-x)*4/length
            flank(f"Door reveal {i}",[(x,z),(u,h),(u+dx,h+dz),(x+dx,z+dz)],trim)
        flank("Flush door pull",[(416,626),(523,626),(528,638),(421,638)],trim,5)
        shapes.box(f"Tail lens {side}",(12,442,66),(2011,side*475,510),taillight,4)
        shapes.box(f"Headlamp {side}",(10,356,66),(-2121,side*502,360),headlight,4)
        for i in range(5):
            shapes.box(f"Lens flute {side} {i}",(3,3,57),(-2128,side*502+(i-2)*58,365),
                       dict(color="#B5AA8F",roughness=.3))
    shapes.box("Recessed front mask",(15,1396,95),(-2110,0,346),trim,5)
    for z in (363,386,409):
        shapes.box(f"Grille blade {z}",(10,609,4),(-2120,0,z),dict(color="#52605E",metal=.5))
    for i in range(5):
        shapes.box(f"Engine cover louvre {i}",(16,888,7),(1480+i*40,0,781+i*1.7),trim)


def finish_details():
    for side in (-1,1):
        # The sill follows the glazed footprint down across the sloping shoulder.
        sections = []
        for x,width in [(-730,738), (1448,738)]:
            sections.append([[x,side*(width-16),754],[x,side*(width+13),754],
                             [x,side*(width+13),724],[x,side*(width-16),724]])
        ruled(f"Glazing sill {side}", sections, trim)
        for x in axles:
            label = ("Front" if x < 0 else "Rear") + (" left" if side < 0 else " right")
            shapes.ring(label+" dished rim",(x,side*879,315),183,5,alloy)
            shapes.ring(label+" hub register",(x,side*880,315),92,3,trim)
            for i in range(5):
                angle = math.tau*i/5
                ctr = (x+82*math.cos(angle),side*883,315+82*math.sin(angle))
                shapes.disc(label+f" fastener {i}",ctr,9,9,trim,rounded=2)


def front_quarter():
    cad.call("view", yaw=-31, pitch=10, target=[-230, 0, 480], distance=5500,
             orthographic=False, grid=False, display="shaded")


def preview_model():
    cad.call("run_command", name="render", commit=False)
    front_quarter()
    cad.call("command_set", params={
        "Picture size": "HD  1280 × 720", "Quality (samples per pixel)": 128,
        "Lighting": "Studio (soft white boxes)", "Light bounces": 6,
        "Background": "White", "Ground plane with shadows": True,
        "Denoise final image": True, "Start render": True,
    })
    deadline = time.monotonic() + 600
    try:
        while time.monotonic() < deadline:
            status = cad.call("render_control", action="status")
            if status.get("error"):
                raise RuntimeError(status["error"])
            if status.get("finished"):
                cad.call("render_control", action="save", path=str(preview))
                return
            time.sleep(1)
        raise TimeoutError("The coupe render did not finish within ten minutes")
    finally:
        cad.call("render_control", action="close")
        cad.call("command_cancel")


def verify():
    cad.call("file", action="open", path=str(model))
    objects = cad.call("get_state")["objects"]
    names = [o["name"] for o in objects]
    assert set(names) == set(shapes.KNOWN), (names, shapes.KNOWN.keys())
    for name in names:
        obj = cad.call("object_info",object=name)
        if obj["kind"] == "Form":
            assert cad.form_info(name,summary=True)["closed"], name
        else:
            assert obj["valid"] and obj["volume"] > 0, name
        if name.endswith(" tyre"):
            assert abs(obj["min"][2]) < .02 and abs(obj["size"][0]-630)<.02, obj
    print(f"Reopened and verified {len(names)} bodies.",flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", type=Path, required=True)
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        cad.call("document_new")
        for build in (coachwork,cabin,wheels,exterior,finish_details):
            build()
            print(build.__name__,flush=True)
        shapes.visible(True)
        cad.select()
        front_quarter()
        cad.save(str(model))
        verify()
        preview_model()
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
