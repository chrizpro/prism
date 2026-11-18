"""Build a Freeform clay study followed by three directly editable cubic surface studies."""
import argparse
import base64
import math
import subprocess
import sys
import time

import build_freeform_pair as pair
from build_shoe_pitcher import rings

cad = pair.cad
out = pair.study.ROOT / "docs/manta-and-nurbs"
clay = "#B56D4C"


def clay_finish(obj):
    cad.call("material_apply", object=obj.name, material="abs", color=clay, roughness=.86)
    return obj


def clay_turn(name, sections):
    grid = [[[x+a*math.cos(i*math.tau/24), y+b*math.sin(i*math.tau/24), z]
             for i in range(24)] for x,y,z,a,b in sections]
    return clay_finish(rings(grid,name,clay))


def tapered_path(name, points, start, end):
    path = cad.run("curve_through",points=points)[0]
    tube = cad.form_pipe(path,radius_at_the_start=start,radius_at_the_end=end,
                         sides=10,steps_along_the_path=24,close_the_ends=True)
    cad.delete(path)
    tube.rename(name)
    clay_finish(tube)


def manta():
    # Across the span, the leading and trailing edges have different sweep.
    span = [-155,-150,-135,-110,-80,-52,-32,-18,0,18,32,52,80,110,135,150,155]
    def wing(i,v,h):
        x=span[i]
        t=abs(x)/155
        fore=-51+68*t**1.6
        aft=67-64*t**.72+18*t**3
        y=fore+(aft-fore)*(v+1)/2
        camber=(1-v*v)*(5+6*(1-t))
        thickness=(2+16*math.exp(-(x/36)**2)+6*(1-t))*(1-.65*v*v)
        bank=(46 if x>0 else -9)*t**2.4
        return [x,y,78+bank+camber+h*thickness]
    clay_finish(pair.shaped_box("Banking wings and mantle",span,12,wing,clay))
    for sign,label in [(-1,"Left"),(1,"Right")]:
        tapered_path(label+" cephalic lobe",[(sign*21,-34,83),(sign*24,-47,84),
                     (sign*23,-59,83),(sign*18,-66,76),(sign*14,-67,73)],8,3)
    tapered_path("Trailing tail",[(0,46,83),(0,70,86),(-3,96,90),(-19,120,98),
                 (-48,128,108),(-68,115,118),(-71,92,121)],7,.95)
    clay_turn("Swept clay support",[(0,3,6,29,21),(0,3,9,27,19),
         (0,4,14,19,14),(0,8,33,11,10),(0,15,51,11,11),(0,17,68,19,17),(0,16,81,24,20)])
    clay_turn("Oval plinth",[(0,5,1,56,34),(0,5,1,61,38),(0,5,3,62,39),
                            (0,5,10,62,39),(0,5,12,60,37),(0,5,12,52,30)])


def patch(name, grid, color, material="abs", rough=.28, metal=0):
    nr,nc=len(grid),len(grid[0])
    obj=cad.run("nurbs_patch",points_across=nc,points_up=nr,degree=3)[0]
    net=cad.call("get_control_points",object=obj.name)
    assert (net["rows"],net["cols"])==(nr,nc), (net["rows"],net["cols"],nr,nc)
    cad.call("set_control_points",object=obj.name,points=[p for row in grid for p in row])
    return pair.study.look(obj,name,color=color,material=material,roughness=rough,metal=metal)


def spoon():
    # Each cross-section carries the rim height, basin depth and half-width.
    stations=[(-98,.25,20,0),(-98,13,21,1),(-90,32,23,9),(-72,45,25,18),
              (-45,49,25,22),(-14,46,22,19),(12,35,18,14),(35,21,15,7),
              (51,12,16,2),(68,11,20,1),(88,14,24,1),(101,10,26,.5),(103,.25,26,0)]
    for top in [True,False]:
        grid=[]
        for x,w,rim,depth in stations:
            row=[]
            for j in range(13):
                a=math.pi*j/12
                y=-w*math.cos(a)
                z=rim-depth*math.sin(a)**2
                z+=min(1,w/10)*((1.2 if top else -1.2)*math.sin(a) - (0 if top else 3.5*math.sin(a)**.5))
                row.append([x,y,z])
            grid.append(row if top else row[::-1])
        patch("Glazed basin" if top else "Porcelain underside",grid,"#EBDCC3",material="porcelain",rough=.19)


def bell():
    # Keep a single circumferential net per part. The seam poles share a tangent.
    brass="#BF944B"
    tilt=math.radians(105)
    def turned(name,sections,color,metal):
        grid=[]
        step=math.tau/32
        for r,z,flare in sections:
            row=[]
            for j in range(33):
                t=j*step
                radius=r*(1+flare*math.cos(3*t))
                px,py=radius*math.cos(t),radius*math.sin(t)
                if j in (1,31):
                    px=r*(1+flare)
                    py=px*step*(1 if j==1 else -1)
                row.append([z*math.sin(tilt)+px*math.cos(tilt),py,
                            52+z*math.cos(tilt)-px*math.sin(tilt)])
            grid.append(row)
        patch(name,grid,color,rough=.23,metal=metal)
    turned("Cast skirt",[(6,76,0),(10,76,0),(11,72,0),(12,59,.005),(17,40,.02),
           (27,22,.055),(41,8,.09),(47,5,.105),(48,4,.105),(48,2,.105),
           (46.5,1,.105),(45,3,.105),(39,7,.09),(26,18,.06),(14,37,.02),
           (9,58,0),(7,70,0),(2,71,0)],brass,.76)
    turned("Walnut grip",[(3,75,0),(9,75,0),(10,78,0),(9,82,0),(6,84,0),
           (6,96,0),(9,110,0),(12,123,0),(11,129,0),(8,133,0),(.1,134,0)],"#64412C",0)
    turned("Clapper",[(.1,7,0),(4,7,0),(7,11,0),(7,15,0),(4,19,0),(1.3,19,0),
           (1.3,45,0),(2,71,0)],brass,.7)


def train():
    # A C1 Hermite profile in the longitudinal direction. Split the roof
    # into paint/glass/paint at exact section boundaries.
    sections=[(-132,8,9,6),(-126,17,12,8),(-106,30,20,13),(-72,42,32,20),
              (-40,48,45,29),(0,51,72,36),(29,52,89,41),(56,52,96,44),
              (96,52,97,46),(127,52,97,46)]
    paint="#205363"
    glass="#23363E"
    def ring(i,band):
        x,w,roof,shoulder=sections[i]
        if band=="roof":
            return [[x,w*s,roof-(roof-shoulder)*abs(s)**3] for s in [-1,-.84,-.56,-.25,0,.25,.56,.84,1]]
        sign=1 if band=="starboard" else -1
        return [[x,sign*w*y,z] for y,z in [(1,shoulder),(1.06,shoulder*.83),
                (1.065,shoulder*.45),(1.04,5),(.99,2),(.90,1)]]
    def slope(i,band):
        a=max(0,i-1); b=min(len(sections)-1,i+1)
        dx=sections[b][0]-sections[a][0]
        return [[(v-u)/dx for u,v in zip(p,q)] for p,q in zip(ring(a,band),ring(b,band))]
    for i in range(len(sections)-1):
        dx=(sections[i+1][0]-sections[i][0])/3
        for band in ["roof","port","starboard"]:
            a,b=ring(i,band),ring(i+1,band)
            c=[[v+dx*d for v,d in zip(p,t)] for p,t in zip(a,slope(i,band))]
            e=[[v-dx*d for v,d in zip(p,t)] for p,t in zip(b,slope(i+1,band))]
            window=band=="roof" and i in [4,5]
            patch(("Windscreen" if window else band.title())+" "+str(i+1),
                  [a,c,e,b],glass if window else paint,rough=.17 if window else .25,metal=.4)

    # Close the prow and aft section with the same transverse roof poles.
    for i,label in [(0,"Nose"),(len(sections)-1,"Rear bulkhead")]:
        section=ring(i,"roof")
        rows=[]
        side=ring(i,"starboard")
        _,w,_,shoulder=sections[i]
        for j,(_,side_y,side_z) in enumerate(side):
            crown=1-j/(len(side)-1)
            rows.append([[x,y*side_y/w,side_z+crown*(z-shoulder)] for x,y,z in section])
        patch(label,rows,paint,metal=.4)
    # Flush marker lamps follow the change in shoulder width and elevation.
    for side,label in [(-1,"Port"),(1,"Starboard")]:
        rows=[]
        for x,w,z,k in [(-103,31.6,13.8,.15),(-101,32.5,14.1,.8),
                         (-92,35.8,16.1,1),(-82,39.2,18.2,.85),(-80,39.8,18.6,.15)]:
            rows.append([[x,side*(w+1.2+.25*math.cos(a)),z-1.5+k*1.1*math.sin(a)]
                         for a in [-math.pi/2,-.52,.52,math.pi/2]])
        patch(label+" running lamp",rows,"#FFEBC7",rough=.15)


models={"Manta":(manta,-12,25,[0,12,68],440),"Spoon":(spoon,-20,27,[0,0,14],280),
        "Bell":(bell,-45,20,[55,0,37],238),"Train":(train,-33,15,[0,0,45],389)}


def capture(key):
    cad.call("wait_idle")
    shot=cad.call("screenshot",viewport=True,max_width=1000)
    (out/(key+"-viewport.png")).write_bytes(base64.b64decode(shot["_image"]["data"]))
    cad.call("run_command",name="render",commit=False)
    try:
        cad.call("command_set",params={"Picture size":"HD  1280 × 720","Quality (samples per pixel)":12,
                 "Light bounces":4,"Background":"White","Ground plane with shadows":True,
                 "Denoise final image":True,"Start render":True})
        for _ in range(150):
            status=cad.call("render_control",action="status")
            if status.get("finished"):
                cad.call("render_control",action="save",path=str(out/(key+".png")))
                return
            if status.get("error"): raise RuntimeError(status)
            time.sleep(2)
        raise TimeoutError(key)
    finally:
        cad.call("render_control",action="close")
        cad.call("command_cancel")


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("keys",nargs="*")
    args=parser.parse_args()
    keys=args.keys or list(models)
    if len(keys)>1:
        for key in keys:
            subprocess.run([sys.executable,__file__,key],check=True)
        sys.exit(0)
    out.mkdir(parents=True,exist_ok=True)
    bridge,log=pair.study.connect_mcp(pair.study.ROOT/("artifacts/gallery/manta-nurbs"+"-".join(args.keys)+".json"))
    try:
        for key in keys:
            build,yaw,pitch,target,distance=models[key]
            cad.call("document_new")
            build()
            view=dict(yaw=yaw,pitch=pitch,target=target,distance=distance,grid=False,orthographic=True,display="shaded")
            cad.call("view",**view)
            path=str(out/(key+".prism"))
            cad.save(path)
            cad.call("file",action="open",path=path)
            time.sleep(2)
            cad.call("view",**view)
            capture(key)
            print(key,"saved, reopened and rendered",flush=True)
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
