"""Two editable subdivision studies, driven through Prism's stdio MCP bridge."""
import argparse
import base64
import json
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs/freeform-pair"


def shaped_box(name, rows, columns, shape, color):
    obj = cad.form_box(width=len(rows)-1, depth=2, height=2, faces_x=len(rows)-1,
                       faces_y=columns, faces_z=2)
    verts = cad.form_info(obj)["vertices"]
    xmin, ymin, zmin = [min(v[i] for v in verts) for i in range(3)]
    positions = {str(i):shape(round(x-xmin), y-ymin-1, z-zmin-1)
                 for i,(x,y,z) in enumerate(verts)}
    edit = cad.form_op(obj,"set_vertices",positions=positions)
    print(name,"edit feedback",edit.get("warnings"),flush=True)
    return study.look(obj,name,material="abs",color=color,roughness=.38)


def saddle():
    # x, half width, underside, cushion thickness, channel depth.
    rows = [(-137,7,47,8,0),(-134,14,45,12,0),(-119,17,42,15,1),
            (-76,17,40,16,2),(-37,23,39,19,5),(0,37,39,24,9),
            (36,60,40,28,12),(66,75,42,30,13),(91,78,44,31,13),
            (111,72,46,30,12),(128,56,49,24,10),(133,43,52,17,7)]
    def point(i,y,z):
        x,w,b,h,groove = rows[i]
        top = b+h-min(8,h*.38)*abs(y)**3-groove*math.exp(-(y/.25)**2)
        bottom = b+3*y*y
        # Sweep the rear corners forward while leaving a slight center notch.
        x -= [0,0,0,0,0,0,0,0,0,.3,.8,1][i]*(17*y*y+7*math.exp(-(y/.27)**2))
        assert top-bottom > 1, (i,y)
        return [x,w*y,bottom+(top-bottom)*(z+1)/2]
    seat=shaped_box("Oxblood saddle",rows,12,point,"#793B43")
    for side in [-1,1]:
        path=cad.run("curve_through",points=[(-104,side*9,46),(-92,side*11,29),
             (-62,side*20,15),(-16,side*21,14),(22,side*22,15),
             (54,side*31,26),(81,side*43,49)])[0]
        rail=cad.form_pipe(path,radius_at_the_start=3.6,radius_at_the_end=3.6,
                           sides=10,steps_along_the_path=24,close_the_ends=True)
        cad.delete(path)
        study.look(rail,"Rail "+("L" if side<0 else "R"),material="304",color="#45484C",metal=.8)
    return seat


def chair():
    # Centerline follows the foot back toward the front, up and around the seat.
    # x, z, half-width, lateral cupping, thickness.
    rows=[(223,7,170,0,14),(217,7,181,0,14),(171,7,185,0,14),
          (-136,7,179,0,14),(-203,7,168,0,14),(-234,20,157,0,15),
          (-239,54,148,1,17),(-221,135,140,2,18),(-174,340,153,4,19),
          (-161,396,177,5,19),(-144,431,197,5,17),(-111,448,214,6,16),
          (-45,449,224,9,15),(65,434,226,13,15),(147,434,222,16,15),
          (185,448,216,18,15),(207,480,214,19,15),(217,535,216,21,15),
          (238,647,221,23,14),(273,783,226,23,13),(280,808,223,21,13),
          (281,812,209,20,13)]
    def point(i,y,h):
        x,z,w,cup,thick=rows[i]
        prev,nxt=rows[max(0,i-1)],rows[min(len(rows)-1,i+1)]
        dx,dz=nxt[0]-prev[0],nxt[1]-prev[1]
        length=math.hypot(dx,dz)
        offset=h*thick/2+cup*y*y
        return [x-offset*dz/length,w*y,z+offset*dx/length]
    return shaped_box("Petrol ribbon",rows,8,point,"#246977")


def finish(key,body,camera,axis,stations):
    model=OUT/(key+".prism")
    cad.save(str(model))
    cad.call("file",action="open",path=str(model))
    profile=cad.call("solid_profile",object=body.name,axis=axis,at=stations,views=[])
    topology=cad.form_info(body.name,summary=True)
    print(key,"size",profile["size"],flush=True)
    (OUT/(key+"-inspection.json")).write_text(json.dumps(dict(profile=profile,cage=topology),indent=2))
    cad.call("view",**camera,grid=False,orthographic=True,display="shaded")
    cad.call("wait_idle")
    shot = cad.call("screenshot",viewport=True,max_width=1000)
    (OUT/(key+"-viewport.png")).write_bytes(base64.b64decode(shot["_image"]["data"]))
    cad.call("run_command",name="render",commit=False)
    try:
        cad.call("command_set",params={"Picture size":"HD  1280 × 720","Quality (samples per pixel)":16,
                 "Light bounces":4,"Background":"White","Ground plane with shadows":True,
                 "Denoise final image":False,"Start render":True})
        for _ in range(150):
            status=cad.call("render_control",action="status")
            if not status: raise RuntimeError("Renderer status disappeared")
            if status.get("error"): raise RuntimeError(status)
            if status.get("finished"):
                cad.call("render_control",action="save",path=str(OUT/(key+".png")))
                break
            time.sleep(2)
        else: raise TimeoutError(key)
    finally:
        cad.call("render_control",action="close")
        cad.call("command_cancel")


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--only",choices=["Chair","Saddle"])
    only=parser.parse_args().only
    OUT.mkdir(parents=True,exist_ok=True)
    bridge,log=study.connect_mcp(study.ROOT/"artifacts/gallery/freeform-pair-final.json")
    try:
        for key,build,cam,axis,stations in [
            ("Saddle",saddle,dict(yaw=35,pitch=29,target=[0,0,44],distance=316),"x",[-110,-40,36,90]),
            ("Chair",chair,dict(yaw=-25,pitch=9,target=[0,0,408],distance=1530),"z",[80,250,470,700])]:
            if only and key != only: continue
            cad.call("document_new")
            finish(key,build(),cam,axis,stations)
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
