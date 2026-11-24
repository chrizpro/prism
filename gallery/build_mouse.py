"""Generate a right-handed desktop mouse through the Prism MCP bridge."""
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs/mouse-study"
MODEL = OUT / "Mouse.prism"


def cover(name, drop, color):
    # X, half-width, crown height, sweep of the end across the width, thumb relief.
    stations = [(-60,21,0,13,0),(-59,24,10,14,0),(-52,28,17,12,1),
                (-36,31,21,1,4),(-14,32,29,0,8),(8,34,37,0,5),
                (27,35,38,-1,1),(44,32,32,-3,0),(53,27,20,-10,0),
                (58,22,9,-13,0),(60,19,0,-13,0)]
    # Sidewall poles rise almost vertically before turning into the palm crown.
    cross = [(-1,0),(-1.025,.24),(-.99,.58),(-.80,.88),(-.44,1.01),
             (0,1.03),(.44,.97),(.80,.81),(.99,.53),(1.025,.22),(1,0)]
    points = []
    for x, width, height, sweep, thumb in stations:
        for side, rise in cross:
            scoop = thumb * max(0, -side)**2 * math.sin(math.pi * min(rise,1))
            points.append((x+sweep*side**4,width*side+scoop,4+height*rise-drop))
    obj, = cad.run("nurbs_patch", points_up=len(stations), points_across=len(cross),
                   degree=3, initial_relief=0)
    cad.call("set_control_points", object=obj.name, points=points)
    return study.look(obj, name, material="abs", color=color, roughness=.36)


def trim(shell, tool):
    report = cad.run_report("trim_surface", slots={"Surface to trim":shell,"Cutting tool":tool})
    assert report["changed"][0]["valid"] and report["changed"][0]["areaDelta"] < 0, report


def build():
    cad.call("document_new")
    top = cover("Lagoon upper shell",0,"#317678")
    edges = [{"object":top.name,"edge":i} for i in range(1,5)]
    base = cad.extrude(edges,distance=-4,operation="New body")
    if base.info["min"][2] > 1:
        cad.move(base,z=-4)
    study.look(base,"Graphite base",material="abs",color="#242C30",roughness=.5)
    liner = cover("Recess liner",1.6,"#212D30")
    # Back only the button opening. A full vertically offset skin can break
    # through the upper shell wherever its walls curve back under the palm.
    pocket = cad.box(width=51, depth=12, height=48, position_x=-33, operation="New body")
    cad.run_report("trim_surface", slots={"Surface to trim":liner, "Cutting tool":pocket},
                   keep="The back side")
    # The click split opens at the nose and stops just behind the scroll well.
    trim(top,cad.box(width=65,depth=1.1,height=55,position_x=-42,operation="New body"))
    with cad.sketch("XY") as slot:
        slot.slot((-41,0),(-28,0),8)
    cutter = cad.extrude(slot.obj,distance=55,operation="New body")
    trim(top,cutter)
    cad.delete(slot.obj)
    # A scalloped profile gives the copper wheel a tactile tread.
    with cad.sketch("XZ") as wheel:
        points = []
        for i in range(120):
            t = i*math.tau/120
            radius = 7.8+.18*math.cos(30*t)
            points.append((-34.5+radius*math.cos(t),22+radius*math.sin(t)))
        wheel.polyline(points,closed=True)
    rotor = cad.extrude(wheel.obj,distance=6.4,extent="Symmetric",operation="New body")
    cad.delete(wheel.obj)
    study.look(rotor,"Fluted scroll wheel",material="6061",color="#CB824D",metal=.72,roughness=.32)
    cad.save(str(MODEL))


def preview():
    cad.call("file",action="open",path=str(MODEL))
    for name in ("Lagoon upper shell","Graphite base","Recess liner","Fluted scroll wheel"):
        obj = cad.get(name)
        assert obj.info["valid"], obj.info
        print(name,obj.bounds,flush=True)
    cad.call("view",yaw=-36,pitch=26,target=[0,0,17],distance=151,orthographic=True,grid=False)
    cad.call("run_command",name="render",commit=False)
    try:
        cad.call("command_set",params={"Picture size":"HD  1280 × 720",
                 "Quality (samples per pixel)":48,"Light bounces":4,"Background":"Studio gradient",
                 "Denoise final image":True,"Ground plane with shadows":True,"Start render":True})
        deadline = time.monotonic()+240
        while time.monotonic()<deadline:
            status = cad.call("render_control",action="status")
            if status.get("error"):
                raise RuntimeError(status)
            if status.get("finished"):
                cad.call("render_control",action="save",path=str(OUT/"Mouse.png"))
                return
            time.sleep(2)
        raise TimeoutError("Mouse render")
    finally:
        cad.call("render_control",action="close")
        cad.call("command_cancel")


if __name__ == "__main__":
    OUT.mkdir(parents=True,exist_ok=True)
    bridge, log = study.connect_mcp(study.ROOT/"artifacts/gallery/mouse-automation.json")
    try:
        build()
        preview()
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
