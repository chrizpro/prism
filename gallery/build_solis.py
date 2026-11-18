"""A low-part-count exterior study, built with the desktop Prism MCP bridge."""
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs/solis-coupe"
MODEL = OUT / "Solis.prism"
PAINT = dict(material="6061", color="#CE6235", metal=.48, roughness=.24,
             surface=dict(Coat=.85, CoatRoughness=.14))
GLASS = dict(material="abs", color="#23292D", metal=.36, roughness=.15)
TRIM = dict(material="abs", color="#212429", roughness=.42)
ALLOY = dict(material="6061", color="#C8C9C6", metal=.86, roughness=.26)
RUBBER = dict(material="silicone", color="#28282A", roughness=.63)
LENS = dict(material="abs", color="#FFF3C6", roughness=.16)
TAIL = dict(material="abs", color="#A32523", roughness=.18)


def patch(name, net, finish):
    obj, = cad.run("nurbs_patch", points_up=len(net), points_across=len(net[0]),
                   degree=3, initial_relief=0)
    cad.call("set_control_points", object=obj.name, points=[p for row in net for p in row])
    return study.look(obj, name, **finish)


def coachwork():
    # x, half beam, central hood/deck, fender crest, sweep at the corner.
    rows = [
        (-2180,690,185,185,245),(-2200,720,350,340,230),
        (-2090,805,530,560,180),(-1750,875,630,775,75),
        (-1350,930,736,864,15),(-1010,923,748,847,0),
        (-650,892,716,776,0),(-140,885,735,758,0),
        (420,889,760,788,0),(920,920,790,853,0),
        (1340,950,800,902,0),(1660,943,786,866,-15),
        (1960,881,745,793,-65),(2110,820,709,733,-115),
        (2140,804,470,478,-122),(2115,785,185,185,-130)]
    half = [(0,1,0),(.35,1,0),(.62,1,.30),(.79,1,.97),(.89,.975,1),
            (.97,.89,.90),(1.005,.72,.65),(1.005,.47,.30),(.98,.18,.06),(.965,0,0)]
    cross = [(-a,b,c) for a,b,c in reversed(half[1:])] + half
    net = []
    for idx,(x,w,crown,crest,sweep) in enumerate(rows):
        # At the bumper the transverse curve turns around in plan. Blend
        # out the sidewall undercut there to keep its corner from doubling back.
        blend = {0:1,1:.75,2:.3,13:.3,14:.75,15:1}.get(idx,0)
        row=[]
        for i,(a,b,c) in enumerate(cross):
            t=(i-9)/9
            end_y=math.sin(.5*math.pi*t)*.965
            y=(1-blend)*a+blend*end_y
            offset=(1-blend)*min(abs(a),1)**4+blend*(1-math.cos(.5*math.pi*t))
            row.append((x+sweep*offset,w*y,185+(crown-185)*b+(crest-crown)*c))
        net.append(row)
    shell = patch("Vermilion coachwork",net,PAINT)
    perimeter = [(-2168,0),(-2151,320),(-2085,580),(-1930,692),
                 (-1580,844),(-1250,888),(-610,853),(0,850),
                 (760,870),(1300,905),(1720,884),(1960,823),
                 (2020,736),(2100,470),(2112,0)]
    outline = perimeter + [(x,-y) for x,y in reversed(perimeter[1:-1])]
    with cad.sketch("XY",offset=130) as sill:
        sill.spline(outline,closed=True)
    floor = cad.extrude(sill.obj,distance=55,operation="New body")
    cad.delete(sill.obj)
    study.look(floor,"Graphite lower body",**TRIM)
    for x in (-1300,1290):
        with cad.sketch("XZ") as arch:
            arch.circle((x,336),365)
        tool = cad.extrude(arch.obj,distance=2400,extent="Symmetric",operation="New body")
        cut = cad.run_report("trim_surface",slots={"Surface to trim":shell,"Cutting tool":tool},
                             delete_the_cutting_tool_afterward=False)
        assert cut["changed"][0]["valid"],cut
        cad.subtract(floor,tool)
        cad.delete(arch.obj)
    return net


def canopy():
    # The perimeter lies slightly inside the coachwork. Its intersection
    # with the body gives the glazing a continuous belt line.
    rows = [(-907,624,684,170),(-790,678,815,155),(-462,698,1070,115),
            (-75,709,1240,54),(380,717,1288,0),(782,715,1252,-30),
            (1130,691,1130,-70),(1430,650,918,-100),(1590,619,744,-140)]
    cross = [(-1,0),(-.99,.18),(-.87,.67),(-.70,.94),(-.38,1),
             (0,1.025),(.38,1),(.70,.94),(.87,.67),(.99,.18),(1,0)]
    net = [[(x+sweep*abs(a)**3,w*a,675+(height-675)*b) for a,b in cross]
           for x,w,height,sweep in rows]
    return patch("Smoked canopy",net,GLASS)


def basis(n,t):
    """Clamped cubic weights on the same uniform grid as Prism Control Grid."""
    knots = [0]*4 + [i/(n-3) for i in range(1,n-3)] + [1]*4
    b = [float(a <= t < z) for a,z in zip(knots,knots[1:])]
    for d in range(1,4):
        b = [(0 if knots[i+d]==knots[i] else (t-knots[i])*b[i]/(knots[i+d]-knots[i]))
             +(0 if knots[i+d+1]==knots[i+1] else (knots[i+d+1]-t)*b[i+1]/(knots[i+d+1]-knots[i+1]))
             for i in range(len(b)-1)]
    return b


def lamp(net,name,u0,u1,v0,v1,lift,finish,side):
    # Follow the actual bonnet curvature so a lamp doesn't float at its ends.
    grid = []
    for i in range(7):
        t=i/6
        v=v0+(v1-v0)*t
        if side<0:
            v=1-v
        row=[]
        for j in range(4):
            u=u0+.008*t+(u1-u0)*(.5+(j/3-.5)*(.65+.35*math.sin(math.pi*t)))
            a,b=basis(len(net),u),basis(len(net[0]),v)
            point=[sum(ai*bj*p[k] for ai,r in zip(a,net) for bj,p in zip(b,r))
                   for k in range(3)]
            point[0] += (-1 if u<.5 else 1)*lift
            point[2] += lift
            row.append(point)
        grid.append(row)
    patch(name,grid,finish)


def wheel(name,x,side):
    # Revolve a tire section with rounded shoulders and a recessed alloy dish.
    tire=[(251,-112),(301,-112),(324,-105),(333,-89),(336,-66),
          (336,66),(333,89),(324,105),(301,112),(251,112)]
    dish=[(0,74),(250,74),(256,90),(256,109),(252,115),(239,116),
          (232,112),(221,103),(203,95),(168,89),(124,88),(82,92),
          (57,101),(52,109),(44,112),(0,112)]
    for label,section,finish in [("tire",tire,RUBBER),("wheel",dish,ALLOY)]:
        with cad.sketch("XY") as sketch:
            points=[(r,side*y) for r,y in section]
            if label=="wheel":
                sketch.polyline(points[:7])
                sketch.spline(points[6:])
                sketch.line(points[-1],points[0])
            else:
                sketch.polyline(points,closed=True)
        obj=cad.revolve(sketch.obj,axis="World Y",operation="New body")
        cad.delete(sketch.obj)
        cad.move(obj,x=x,y=side*798,z=336)
        study.look(obj,name+" "+label,**finish)


def build():
    cad.call("document_new")
    net=coachwork()
    canopy()
    for side,label in [(-1,"Left"),(1,"Right")]:
        wheel(label+" front",-1300,side)
        wheel(label+" rear",1290,side)
        lamp(net,label+" running light",.051,.062,.565,.781,8,LENS,side)
        lamp(net,label+" tail lamp",.931,.952,.56,.77,4,TAIL,side)
    cad.save(str(MODEL))


def preview():
    cad.call("file",action="open",path=str(MODEL))
    objects=cad.objects()
    visible=[o for o in objects if o.info["visible"]]
    for o in visible:
        assert cad.get(o.name).info["valid"],o.name
    print("Reopened:",len(visible),"valid visible bodies",flush=True)
    cad.call("view",yaw=-40,pitch=13,target=[-55,0,557],distance=4590,
             orthographic=True,grid=False)
    cad.call("run_command",name="render",commit=False)
    try:
        cad.call("command_set",params={"Picture size":"HD  1280 × 720",
                 "Quality (samples per pixel)":96,"Light bounces":4,"Background":"White",
                 "Denoise final image":True,"Ground plane with shadows":True,"Start render":True})
        deadline=time.monotonic()+300
        while time.monotonic()<deadline:
            state=cad.call("render_control",action="status")
            if state.get("error"):
                raise RuntimeError(state)
            if state.get("finished"):
                cad.call("render_control",action="save",path=str(OUT/"Solis.png"))
                return
            time.sleep(2)
        raise TimeoutError("Coupe render")
    finally:
        cad.call("render_control",action="close")
        cad.call("command_cancel")


if __name__=="__main__":
    OUT.mkdir(parents=True,exist_ok=True)
    bridge,log=study.connect_mcp(study.ROOT/"artifacts/gallery/solis-automation.json")
    try:
        build()
        preview()
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
