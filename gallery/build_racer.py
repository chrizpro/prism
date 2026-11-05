"""A playful open-wheel racer, modeled natively through Prism MCP/cadkit."""
import argparse
import build_examples as study
from build_aircraft import interp

cad=study.cad
RED=dict(material="6061",color="#EA2729",metal=.12,roughness=.26,surface=dict(Coat=.8,CoatRoughness=.16))
BLUE=dict(material="6061",color="#168BB7",metal=.25,roughness=.28,surface=dict(Coat=.6))
YELLOW=dict(material="abs",color="#FFDA35",roughness=.3,surface=dict(Coat=.4))
BLACK=dict(material="silicone",color="#252A30",roughness=.7)
WHITE=dict(material="abs",color="#F4F4E9",roughness=.32,surface=dict(Coat=.3))
METAL=dict(material="6061",color="#B5BFC5",metal=.85,roughness=.3)


def sculpt_box(name,size,pos,style,shape=None):
    obj=cad.form_box(width=size[0],depth=size[1],height=size[2],faces_x=5,faces_y=3,faces_z=3)
    positions={}
    for i,(x,y,z) in enumerate(cad.form_info(obj)["vertices"]):
        if shape: x,y,z=shape(x,y,z)
        positions[str(i)]=[x+pos[0],y+pos[1],z+pos[2]]
    cad.form_op(obj,"set_vertices",positions=positions)
    return study.look(obj,name,**style)


def wheel(label,x,y,r,width):
    for suffix,radius,depth,style in [("tire",r,width,BLACK),("rim",r*.57,width+4,METAL),
                                     ("hub",r*.30,width+8,dict(material="6061",color="#465563",metal=.8,roughness=.35))]:
        obj=cad.cylinder(radius=radius,height=depth,operation="New body")
        obj.rename(label+" "+suffix)
        if suffix=="tire":
            edges=cad.call("solid_find",object=obj.name,kind="edges",type="Circle",limit=10)["hits"]
            cad.fillet([dict(object=obj.name,edge=e["edge"]) for e in edges],radius=25)
        cad.rotate(obj,90,axis="X")
        center=cad.call("object_info",object=obj.name)["center"]
        cad.move(obj,x=x-center[0],y=y-center[1],z=r-center[2])
        cad.call("material_apply",object=obj.name,**style)


def build():
    cad.call("document_new")
    with cad.history("Comet number one - stylized open-wheel racer"):
        study.rounded_box("Carbon floor",(1390,530,65),(130,0,105),15,material="cfrp",color="#29333A",roughness=.5,pattern="CarbonWeave",patternScale=18)
        nose=cad.form_cylinder(radius=1,height=950,sides=16,rings=12)
        positions={}
        rows=[[-1050,12,20,135],[-900,60,45,165],[-650,100,65,205],[-500,120,80,225],[-100,160,80,260]]
        for i,(x,y,z) in enumerate(cad.form_info(nose)["vertices"]):
            axial=z-1050;w,h,center=interp(axial,rows)
            positions[str(i)]=[axial,x*w,center+y*h]
        cad.form_op(nose,"set_vertices",positions=positions)
        study.look(nose,"Tapered red nose",**RED)
        for side in (-1,1):
            sculpt_box("Red sidepod "+str(side),(960,120,190),(260,side*218,165),RED,
                       lambda x,y,z:(x,y*(.85+.15*(x+480)/960),z+35*(x+480)/960))
        sculpt_box("Rear engine cowling",(390,390,320),(645,0,205),RED,
                   lambda x,y,z:(x,y*(1-.55*z/320),z*(.75+.25*(195-x)/390)))
        sculpt_box("Open cockpit seat",(440,290,100),(90,0,230),BLACK)
        sculpt_box("Reclined seat back",(100,275,270),(325,0,290),BLACK,
                   lambda x,y,z:(x+z*.22,y*(1-.12*z/270),z))
        # Red shoulder fairing behind the seat gives the reference's tall, compact silhouette.
        sculpt_box("Red roll fairing",(200,330,340),(540,0,285),RED,
                   lambda x,y,z:(x+z*.12,y*(1-.6*z/340),z))
        # A deliberately simple steering wheel in front of the open seat.
        steering=cad.torus(major_radius=67,minor_radius=11,position_x=-70,position_z=355)
        steering.rename("Steering wheel");cad.rotate(steering,65,axis="Y")
        cad.call("material_apply",object=steering.name,**BLACK)
        for x,y,r,width,label in [(-480,-475,225,185,"Front left"),(-480,475,225,185,"Front right"),
                                  (650,-475,270,230,"Rear left"),(650,475,270,230,"Rear right")]:
            wheel(label,x,y,r,width)
        for x,z in [(-480,225),(650,270)]:
            study.rounded_box("Axle "+str(x),(55,900,45),(x,0,z-22.5),8,material="304",color="#545E65",metal=.8,roughness=.3)
        study.rounded_box("Blue front wing",(190,1170,40),(-920,0,100),12,**BLUE)
        for side in (-1,1):
            sculpt_box("Yellow front endplate "+str(side),(220,35,120),(-920,side*585,65),YELLOW)
            sculpt_box("Blue rear pylon "+str(side),(90,45,390),(670,side*200,330),BLUE,
                       lambda x,y,z:(x+z*.22,y,z))
            sculpt_box("Blue rear endplate "+str(side),(225,35,155),(775,side*575,685),BLUE)
        study.rounded_box("Blue rear wing",(210,1150,45),(775,0,745),15,**BLUE)
        # Raised graphic panels are native solids, so they remain visible without external textures.
        panel=study.rounded_box("Hood number panel",(220,190,7),(-470,0,310),2,**WHITE)
        cad.rotate(panel,-5,axis="Y")
        for name,size,x,y in [("stem",(135,25,5),-470,0),("foot",(22,85,5),-525,0),("flag",(30,45,5),-411,12)]:
            z=319+(x+470)*.0875
            number=study.rounded_box("Blue number one "+name,size,(x,y,z),1.5,**BLUE)
            cad.rotate(number,-5,axis="Y")
        for side in (-1,1):
            study.rounded_box("Side number panel "+str(side),(230,7,145),(220,side*274,220),2,**WHITE)
            study.rounded_box("Side number one "+str(side),(28,9,102),(220,side*279,241),2,**BLUE)
    return "comet-racer",-125,22


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--discovery",required=True)
    parser.add_argument("--render-only",action="store_true");args=parser.parse_args()
    bridge,log=study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file",action="open",path=str(study.ROOT/"gallery/models/comet-racer.prism"))
            model=("comet-racer",-125,22)
        else:model=build()
        study.verify_and_save(*model,distance=3500)
    finally:bridge.terminate();bridge.wait(timeout=10);log.close()
