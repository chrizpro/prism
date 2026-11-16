"""Refined Horizon H4 glazing, powerplant and rotors, owner-requested #133.

Default builds the refined model from scratch. --revise updates the saved model,
preserving the boom, fins, landing gear and lights. No render or test suite.
Canonical scope: docs/interface/gallery/README.md.
"""
import argparse
import base64
import math
import build_helicopter as h

cad,p=h.cad,h.p
GLAZING=dict(material='pc',color='#3B5D73',metal=.08,roughness=.24,pattern='Plain',
             surface=dict(Coat=.55,Transmission=0,Ior=1.58))
COWL=dict(h.TEAL,roughness=.34,surface=dict(Coat=.32))
BLADE=dict(h.DARK,metal=.12,roughness=.48,pattern='Plain',surface=dict(Coat=.08,Bump=0))

def prism(points,depth,y,curved=False):
    with cad.sketch('XZ') as sk:
        if curved:sk.spline(points,closed=True)
        else:sk.polyline(points,closed=True)
    tool=cad.extrude(sk.obj,distance=depth,operation='New body');cad.delete(sk.obj)
    p.place_min(tool,'y',y)
    return tool

def panel(skin,name,profile,y,depth,look=GLAZING,curved=False):
    o=h.copy(skin);cad.intersect(o,prism(profile,depth,y,curved))
    return h.done(o,name,look)

def cowling_shape():
    curves=[]
    rows=[(-78,12,10,312),(-45,45,35,332),(15,62,47,339),
          (90,61,47,337),(158,44,35,324),(202,14,17,301)]
    for x,w,t,z in rows:
        pts=[]
        for j in range(24):
            a=j*math.tau/24;c,s=math.cos(a),math.sin(a)
            pts.append([x,w*math.copysign(abs(c)**(2/3.2),c),z+t*math.copysign(abs(s)**(2/3.2),s)])
        curves.append(cad.run('curve_through',points=pts,closed=True)[0])
    o=cad.loft(curves,operation='New body',straight=False);cad.delete(*curves)
    return o

def cabin():
    master=h.loft_rows(h.ROWS);master.rename('Temporary cabin envelope');h.d.hide(master)
    h.done(h.copy(master),'01 Pearl cabin shell',h.WHITE)
    skin=h.copy(master);cad.scale(skin,1.035)
    inner=h.copy(master);cad.scale(inner,1.005);cad.subtract(skin,inner)
    cad.delete(master);h.d.hide(skin)
    wind=[(-310,186),(-70,186),(-52,324),(-310,324)]
    door=[(-48,173),(-49,244),(-39,274),(-23,287),(50,285),(73,273),(80,247),(77,190),(62,173),(-19,169)]
    rear=[(89,210),(87,247),(96,274),(122,273),(146,254),(152,234),(145,214),(122,206)]
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        panel(skin,label+' curved windshield',wind,-155 if side<0 else 8.8,146.2)
        panel(skin,label+' sculpted door glazing',door,-150 if side<0 else 0,150,curved=True)
        panel(skin,label+' swept quarter glazing',rear,-150 if side<0 else 0,150,curved=True)
        h.rod(label+' flush door handle',(25,side*65,157),(50,side*58,160),1.8,h.DARK)
    panel(skin,'Pearl windshield centre frame',[(-310,186),(-50,186),(-50,324),(-310,324)],-8,16,h.WHITE)
    panel(skin,'Pearl raked cockpit arch',[(-69,186),(-60,186),(-42,324),(-51,324)],-150,300,h.WHITE)
    cad.delete(skin)
    cowl=h.done(cowling_shape(),'Sculpted turbine cowling',COWL)
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        cutter=h.ellipsoid('Temporary intake cutter',(25,side*62,350),(30,11,12),h.DARK)
        cad.subtract(cowl,cutter);h.PARTS.remove(cutter.name)
        h.ellipsoid(label+' recessed oval intake',(25,side*55,350),(27,2,9),h.DARK)
        nozzle=h.rod(label+' hollow exhaust',(150,side*30,354),(214,side*30,360),13,h.SILVER)
        bore=h.rod('Temporary exhaust bore',(145,side*30,353.53),(219,side*30,360.47),10,h.DARK)
        cad.subtract(nozzle,bore);h.PARTS.remove(bore.name)
    h.checkpoint('05-refined-glazing-and-cowling')

def foil_points(radius,chord,sweep,pitch):
    pts=[];beta=math.radians(pitch)
    for sign,indices in [(1,range(12,-1,-1)),(-1,range(1,13))]:
        for j in indices:
            u=(1-math.cos(math.pi*j/12))/2
            v=chord*(u-.25)
            t=sign*5*.12*chord*(.2969*math.sqrt(u)-.126*u-.3516*u*u+.2843*u**3-.1015*u**4)
            pts.append([radius,sweep+v*math.cos(beta)-t*math.sin(beta),v*math.sin(beta)+t*math.cos(beta)])
    return pts

def airfoil_blade(stations):
    curves=[cad.run('curve_through',points=foil_points(*station),closed=True)[0] for station in stations]
    # Ruled spans keep the leading/trailing edges straight; no leaf-shaped bulge.
    blade=cad.loft(curves,operation='New body',straight=True);cad.delete(*curves)
    return blade

def main_blade():
    # A planar working span gives crisp straight silhouettes in the CAD viewport.
    blade=p.profile('XY',[(58,-16),(470,-16),(508,-8),(510,-4),(510,13),(503,18),(470,22),(58,22)],3.2)
    p.place_min(blade,'z',-1.6)
    edges=cad.call('solid_find',object=blade.name,kind='edges',limit=50)['hits']
    long_top=[dict(object=blade.name,edge=e['edge']) for e in edges
              if e.get('from') and e.get('to') and abs(e['from'][0]-e['to'][0])>350 and e['center'][2]>1]
    if long_top:cad.chamfer(long_top,distance=.35)
    h.rotate(blade,5,'X')
    return blade

def mast():
    o=h.rod('Main rotor mast',(-12,0,380),(-12,0,388),23,h.SILVER)
    for r,z0,z1 in [(18,386,407),(10,405,432)]:
        part=h.rod('Temporary gearbox step',(-12,0,z0),(-12,0,z1),r,h.SILVER)
        cad.union(o,part);h.PARTS.remove(part.name)
    return o

def tail_blades():
    for i,angle in enumerate([32,212]):
        blade=airfoil_blade([(12,21,0,5),(38,21,0,4),(100,17,-2,2)])
        h.rotate(blade,90,'X');h.rotate(blade,angle,'Y')
        cad.move(blade,x=625,y=-37.5,z=352);h.done(blade,f'Tail rotor blade {i+1}',BLADE)

def rotor(lights=True):
    mast()
    head=h.rod('Main rotor head',(-12,0,416),(-12,0,433),20,h.SILVER)
    cap=h.ellipsoid('Temporary rotor cap',(-12,0,434),(22,22,6),h.SILVER)
    cad.union(head,cap);h.PARTS.remove(cap.name)
    for i,angle in enumerate([22,112,202,292]):
        a=math.radians(angle)
        arm=h.rod('Temporary feathering arm',(-12,0,431),(-12+66*math.cos(a),66*math.sin(a),431),6.5,h.SILVER)
        cad.union(head,arm);h.PARTS.remove(arm.name)
        blade=main_blade()
        h.rotate(blade,angle,'Z');cad.move(blade,x=-12,z=431)
        h.done(blade,f'Main rotor blade {i+1}',BLADE)
    if lights:
        h.ellipsoid('Port navigation light',(-16,-84,250),(4,4,4),h.RED)
        h.ellipsoid('Starboard navigation light',(-16,84,250),(4,4,4),h.GREEN)
        h.ellipsoid('Anti-collision beacon',(125,0,375),(5,5,6),h.RED)
    h.checkpoint('06-refined-rotors')

def revise():
    cad.call('file',action='open',path=str(h.MODEL))
    keep={'Tapered teal tail boom','Swept teal vertical fin','Pearl horizontal stabilizer','Tail rotor gearbox',
          'Port curved landing skid','Starboard curved landing skid','Forward landing gear cross-strut',
          'Rear landing gear cross-strut','Port navigation light','Starboard navigation light','Anti-collision beacon'}
    cad.delete(*[o for o in cad.objects() if o.name not in keep])
    h.PARTS[:]=[o.name for o in cad.objects() if o.kind=='Solid']
    # Keep the existing beacon attached to the taller powerplant casing.
    beacon=cad.get('Anti-collision beacon');cad.move(beacon,z=375-beacon.info['center'][2])
    cabin();tail_blades();rotor(lights=False)

def inspection_views():
    names=[f'Main rotor blade {i}' for i in range(1,5)]
    cad.call('view',yaw=-38,pitch=24,fit=True,grid=False,display='shaded');cad.call('view',distance=1600)
    cad.call('wait_idle',timeout_ms=5000)
    shot=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
    (h.OUT/'horizon-h4-prism.png').write_bytes(base64.b64decode(shot['_image']['data']))
    try:
        cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=False)) for n in names])
        cad.call('view',yaw=-70,pitch=19,target=[-45,0,275],distance=950);cad.call('wait_idle',timeout_ms=5000)
        shot=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
        (h.OUT/'horizon-h4-detail.png').write_bytes(base64.b64decode(shot['_image']['data']))
    finally:
        cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in names])
        cad.call('view',yaw=-38,pitch=24,fit=True,grid=False,display='shaded');cad.call('view',distance=1600)
    cad.save(str(h.MODEL))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--discovery',default=str(h.ROOT/'artifacts/guitar-v2/automation.json'));parser.add_argument('--revise',action='store_true');a=parser.parse_args()
    h.OUT=h.ROOT/'artifacts/helicopter-v2';h.OUT.mkdir(parents=True,exist_ok=True);h.d.OUT=h.OUT
    b,l=h.d.connect(a.discovery)
    try:
        cad.call('document_new')
        if a.revise:revise()
        else:
            cabin();h.tail();h.gear()
            for i in [1,2]:
                n=f'Tail rotor blade {i}';cad.delete(cad.get(n));h.PARTS.remove(n)
            tail_blades();rotor()
        h.finish();inspection_views()
    finally:b.terminate();b.wait(timeout=10);l.close()
