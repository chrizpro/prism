"""Owner-approved carved maple/walnut guitar body, #132.

Native design only, no hardware, CAM setup, render or test suite.
Canonical scope: docs/interface/gallery/README.md.
"""
import argparse
import json
import build_pavilion as p

cad=p.cad
OUT=p.ROOT/'artifacts/guitar'
# Tangent-aligned cubic spans give the waist, bouts and horns deliberate curvature.
# Sample the spans into one periodic native spline, rather than interpolating a
# crowded set of silhouette guesses that creates lumps between the points.
SPANS=[
 ((28,155),(29,125),(38,101),(64,105)),
 ((64,105),(85,108),(81,174),(99,174)),
 ((99,174),(119,174),(137,130),(133,100)),
 ((133,100),(132,63),(105,36),(105,0)),
 ((105,0),(105,-55),(166,-82),(166,-139)),
 ((166,-139),(166,-208),(104,-245),(0,-245)),
 ((0,-245),(-104,-245),(-166,-208),(-166,-139)),
 ((-166,-139),(-166,-79),(-107,-53),(-107,3)),
 ((-107,3),(-107,41),(-135,67),(-134,108)),
 ((-134,108),(-133,153),(-108,211),(-82,211)),
 ((-82,211),(-62,211),(-72,115),(-60,110)),
 ((-60,110),(-40,104),(-29,127),(-28,155)),
 ((-28,155),(-14,161),(14,161),(28,155))]
def outline():
    points=[]
    for a,b,c,e in SPANS:
        for j in range(10):
            t=j/10;u=1-t
            points.append(tuple(u*u*u*a[k]+3*u*u*t*b[k]+3*u*t*t*c[k]+t*t*t*e[k] for k in range(2)))
    return points
OUTLINE=outline()
TOP=dict(material='maple',color='#DEB579',roughness=.48,pattern='Plain',surface=dict(Coat=.22,Bump=0))

def curve(z,scale=1):
    return cad.run('curve_through',points=[[x*scale,-45+(y+45)*scale,z] for x,y in OUTLINE],closed=True)[0]

def rounded_tool(x,y,w,h,z,depth,r=3):
    with cad.sketch('XY') as sk:
        sk.line((x+r,y),(x+w-r,y));sk.arc((x+w-r,y),(x+w-r+r*.7071068,y+r-r*.7071068),(x+w,y+r))
        sk.line((x+w,y+r),(x+w,y+h-r));sk.arc((x+w,y+h-r),(x+w-r+r*.7071068,y+h-r+r*.7071068),(x+w-r,y+h))
        sk.line((x+w-r,y+h),(x+r,y+h));sk.arc((x+r,y+h),(x+r-r*.7071068,y+h-r+r*.7071068),(x,y+h-r))
        sk.line((x,y+h-r),(x,y+r));sk.arc((x,y+r),(x+r-r*.7071068,y+r-r*.7071068),(x+r,y))
    o=cad.extrude(sk.obj,distance=depth,operation='New body');cad.delete(sk.obj);cad.move(o,z=z)
    return o

def cut_all(parts,tool):
    for part in parts[:-1]:cad.run('subtract',slots={'Body to cut from':part,'Tool bodies':[tool]},keep_tool_bodies=True)
    cad.subtract(parts[-1],tool)

def checkpoint(name):
    cad.select();cad.save(str(OUT/(name+'.prism')));print('Saved',name,flush=True)

def build():
    cad.call('file',action='new');p.PARTS.clear()
    c=curve(0);back=cad.extrude(c,distance=34,operation='New body');cad.delete(c)
    # The clockwise outline's inferred extrusion normal is -Z.
    p.place_min(back,'z',0)
    p.done(back,'Sculpted walnut back',p.CAP)
    # Several matching spline sections form a convex carved top and a thin pale rim.
    sections=[curve(z,s) for z,s in [(34,1),(35,1),(38,.994),(42,.98),(44,.96)]]
    top=cad.loft(sections,operation='New body',straight=False)
    cad.delete(*sections)
    # Smooth loft interpolation can bow below its first section. Trim the joint flat.
    p.cutbox(top,(-250,-300,10,500,600,24))
    assert cad.get(top.name).info['valid'],'Invalid carved top'
    p.done(top,'Carved maple top',TOP)
    checkpoint('01-double-cutaway-carved-blank')
    parts=[back,top]
    cut_all(parts,rounded_tool(-28,105,56,130,26,35,4))
    for name,y in [('Neck',53),('Bridge',-48)]:
        cut_all(parts,rounded_tool(-37,y,74,39,20,40,3.5))
        # Deeper mounting ears show distinct milling depths inside each pickup cavity.
        for x in [-43,37]:
            cut_all(parts,rounded_tool(x,y+10,6,19,17,43,2))
    for x in [-37,37]:
        cut_all(parts,cad.cylinder(radius=4,height=40,position_x=x,position_y=-105,position_z=17,operation='New body'))
    for x,y in [(100,-85),(118,-120),(109,-158)]:
        cut_all(parts,cad.cylinder(radius=4,height=60,position_x=x,position_y=y,position_z=-1,operation='New body'))
    # Wire channels connect the routed pickup cavities and the rear control chamber.
    p.cutbox(back,(-4,-69,20,8,130,8))
    p.cutbox(back,(0,-73,20,99,8,8))
    rear=rounded_tool(63,-183,65,122,-1,25,14);cad.subtract(back,rear)
    # A recessed cover ledge, with clearance around the removable wooden plate.
    ledge=rounded_tool(58,-188,75,132,-1,3.5,17);cad.subtract(back,ledge)
    cover=rounded_tool(58.6,-187.4,73.8,130.8,.3,1.8,16.4)
    for i,(x,y) in enumerate([(71,-174),(120,-174),(71,-69),(120,-69)]):
        hole=cad.cylinder(radius=1.6,height=8,position_x=x,position_y=y,position_z=-1,operation='New body');cad.subtract(cover,hole)
        screw=cad.cylinder(radius=2.1,height=.6,position_x=x,position_y=y,position_z=-.3,operation='New body')
        p.done(screw,f'Rear cover screw {i+1}',dict(material='brass',color='#A88950',roughness=.4,pattern='Plain'))
    p.done(cover,'Recessed walnut control cavity cover',p.CAP)
    checkpoint('02-pockets-and-wiring')
    # Ellipsoidal subtraction makes a flowing forearm relief on the lower treble bout.
    tool=cad.sphere(radius=1,operation='New body')
    cad.run('scale',slots={'Objects':tool},scale_type='Per axis (stretch)',factor_x=200,factor_y=145,factor_z=105,pivot='World origin')
    cad.move(tool,x=270,y=-120,z=118)
    cut_all(parts,tool)
    # A second ellipsoid scoops the back's bass-side waist for a belly contour.
    tool=cad.sphere(radius=1,operation='New body')
    cad.run('scale',slots={'Objects':tool},scale_type='Per axis (stretch)',factor_x=150,factor_y=130,factor_z=65,pivot='World origin')
    cad.move(tool,x=-190,y=-15,z=-45);cad.subtract(back,tool)
    finish()

def finish():
    cad.select();cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in p.PARTS])
    info=[r['result'] for r in cad.call('batch',calls=[dict(tool='object_info',args=dict(object=n)) for n in p.PARTS])]
    bad=[(i['name'],i.get('valid'),i.get('volume')) for i in info if not i.get('valid') or i.get('volume',0)<=0]
    assert not bad,bad
    cad.call('select_tab',title='Solid');cad.call('view',yaw=-20,pitch=58,fit=True,grid=False,display='shaded')
    path=p.ROOT/'gallery/models/maple-walnut-guitar.prism';cad.save(str(path))
    (OUT/'verification.json').write_text(json.dumps(dict(solids=len(info),valid=True,model=str(path),checks='Native solid validity and positive volume. No test suite, render or CAM setup.'),indent=2))
    print('Finished guitar:',len(info),'valid solids',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--discovery',default=str(p.ROOT/'artifacts/pavilion/automation.json'));args=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);p.d.OUT=OUT
    b,l=p.d.connect(args.discovery)
    try:build()
    finally:b.terminate();b.wait(timeout=10);l.close()
