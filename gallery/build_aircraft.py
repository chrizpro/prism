"""Skylark: a simplified light-aircraft concept made through Prism's actual MCP bridge."""
import argparse
import math
import build_examples as study

cad = study.cad
WHITE = dict(material="6061", color="#E5ECEB", metal=.18, roughness=.28, surface=dict(Coat=.7, CoatRoughness=.15))
TEAL = dict(material="6061", color="#087D91", metal=.3, roughness=.27, surface=dict(Coat=.65))
ORANGE = dict(material="6061", color="#F48A38", metal=.15, roughness=.3, surface=dict(Coat=.55))
DARK = dict(material="6061", color="#263743", metal=.7, roughness=.32)


def interp(x, rows):
    for a, b in zip(rows, rows[1:]):
        if x <= b[0]:
            t = max(0, (x-a[0])/(b[0]-a[0]))
            return [u+(v-u)*t for u,v in zip(a[1:], b[1:])]
    return list(rows[-1][1:])


def airfoil(name, leading, chord, span, z, thickness=.12, vertical=False):
    # Cosine-spaced symmetric NACA-like section, rounded trailing edge; retained as hidden construction.
    pts=[]
    for side, indices in [(1, range(24,-1,-1)),(-1, range(1,24))]:
        for i in indices:
            u=(1-math.cos(math.pi*i/24))/2
            h=5*thickness*(.2969*math.sqrt(u)-.126*u-.3516*u*u+.2843*u**3-.1015*u**4)*chord
            pts.append([leading+u*chord, side*h if vertical else span, z if not vertical else span])
            if not vertical: pts[-1][2]=z+side*h
    curve=cad.run("curve_through", points=pts, closed=True)[0]
    curve.rename(name)
    return curve


def wing(name, stations, style, vertical=False):
    curves=[airfoil(name+f" section {i}", *s, vertical=vertical) for i,s in enumerate(stations)]
    obj=cad.loft(curves)
    study.look(obj,name,**style)
    for curve in curves: cad.call("object_update",object=curve.name,visible=False)
    return obj


def build():
    cad.call("document_new")
    with cad.history("Skylark light aircraft"):
        body=cad.form_cylinder(radius=1,height=1800,sides=16,rings=16)
        rows=[[-900,65,65,380],[-800,140,135,390],[-550,155,160,395],[-250,160,170,400],
              [100,145,160,400],[400,105,120,410],[700,50,65,430],[900,12,20,445]]
        positions={}
        for i,(x,y,z) in enumerate(cad.form_info(body)["vertices"]):
            axial=z-900;w,h,center=interp(axial,rows)
            positions[str(i)]=[axial,x*w,center+y*h]
        cad.form_op(body,"set_vertices",positions=positions)
        study.look(body,"Pearl fuselage",**WHITE)
        canopy=cad.form_box(width=570,depth=250,height=200,faces_x=6,faces_y=4,faces_z=3)
        positions={}
        for i,(x,y,z) in enumerate(cad.form_info(canopy)["vertices"]):
            v=z/200;u=(x+285)/570
            positions[str(i)]=[70+x*(1-.35*v)+30*v,y*(1-.35*v),510+z*(.7+.3*math.sin(math.pi*u))]
        cad.form_op(canopy,"set_vertices",positions=positions)
        study.look(canopy,"Smoked blue canopy",material="glass",color="#1D5265",metal=.2,roughness=.15,
                   surface=dict(Coat=.7,Transmission=.2,Ior=1.48))
        for side,label in [(-1,"Port"),(1,"Starboard")]:
            wing(label+" main wing",[(-270,490,side*90,345,.13),(-120,330,side*1120,400,.11)],WHITE)
            wing(label+" teal wingtip",[(-120,330,side*1120,400,.11),(-65,250,side*1360,425,.08)],TEAL)
            wing(label+" tailplane",[(560,275,side*25,445,.10),(680,170,side*460,470,.09)],TEAL)
        wing("Swept vertical fin",[(550,320,440,0,.10),(730,170,800,0,.08)],TEAL,vertical=True)
        # Nose propeller: two carefully separated slim blades, embedded at the orange spinner.
        spinner=cad.form_cylinder(radius=1,height=140,sides=16,rings=5)
        positions={}
        for i,(x,y,z) in enumerate(cad.form_info(spinner)["vertices"]):
            radius=62*(.08+.92*z/140)
            positions[str(i)]=[-1020+z,x*radius,380+y*radius]
        cad.form_op(spinner,"set_vertices",positions=positions)
        study.look(spinner,"Orange spinner",**ORANGE)
        for sign in (-1,1):
            blade=cad.form_box(width=24,depth=65,height=280,faces_x=2,faces_y=3,faces_z=5)
            positions={}
            for i,(x,y,z) in enumerate(cad.form_info(blade)["vertices"]):
                t=z/280
                positions[str(i)]=[-930+x,y*(1-.5*t)+sign*24*t,380+sign*(38+z)]
            cad.form_op(blade,"set_vertices",positions=positions)
            study.look(blade,"Propeller blade "+str(sign),**DARK)
        # Three-wheel gear: cylinders start on Z and are rotated to put their axles across Y.
        for name,x,y,r in [("Nose",-625,0,65),("Port",160,-310,85),("Starboard",160,310,85)]:
            wheel=cad.cylinder(radius=r,height=52,operation="New body")
            wheel.rename(name+" tire")
            cad.rotate(wheel,90,axis="X")
            # Center after rotation is z=26; translate its center onto the chosen wheel position.
            info=cad.call("object_info",object=wheel.name);center=info["center"]
            cad.move(wheel,x=x-center[0],y=y-center[1],z=r-center[2])
            cad.call("material_apply",object=wheel.name,material="silicone",color="#202830",roughness=.76)
            # Visible hub on each side, deliberately small against the rubber wheel.
            hub=cad.cylinder(radius=r*.46,height=56,operation="New body")
            hub.rename(name+" wheel hub");cad.rotate(hub,90,axis="X")
            center=cad.call("object_info",object=hub.name)["center"]
            cad.move(hub,x=x-center[0],y=y-center[1],z=r-center[2])
            cad.call("material_apply",object=hub.name,**ORANGE)
            study.rounded_box(name+" gear strut",(20,22,240 if name=="Nose" else 235),(x,y,r),4,**DARK)
        # Main gear crossmember reaches the low fuselage and both struts.
        study.rounded_box("Main gear crossmember",(30,640,24),(160,0,290),5,**DARK)
    return "skylark-aircraft", -125, 22


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--discovery",required=True)
    parser.add_argument("--render-only",action="store_true");args=parser.parse_args()
    bridge,log=study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file",action="open",path=str(study.ROOT/"gallery/models/skylark-aircraft.prism"))
            model=("skylark-aircraft",-125,22)
        else: model=build()
        study.verify_and_save(*model,distance=3200)
    finally: bridge.terminate();bridge.wait(timeout=10);log.close()
