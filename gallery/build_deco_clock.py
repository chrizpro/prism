"""Owner-approved Art Deco wood study, #132. Native solids; no CAM or render.

Scope and verification: docs/interface/gallery/README.md.
"""
import math
import json
import build_pavilion as p

cad=p.cad
OUT=p.ROOT/'artifacts/deco-clock'
IVORY=dict(material='maple',color='#F3E8CC',roughness=.72,pattern='Plain')
BRASS=dict(material='brass',color='#CBA256',roughness=.3,metal=.9,pattern='Plain')
BLACK=dict(material='walnut',color='#202022',roughness=.52,pattern='Plain')

def rotate(o,angle,axis='X'):
    cad.run('rotate',slots={'Objects':o},axis=axis,angle=angle,pivot='World origin')

def cylinder_y(radius,depth,y,z=90,x=0):
    o=cad.cylinder(radius=radius,height=depth,operation='New body')
    rotate(o,90);p.place_min(o,'y',y);cad.move(o,x=x,z=z)
    return o

def checkpoint(name):
    cad.select();cad.save(str(OUT/(name+'.prism')))
    print('Saved',name,flush=True)

def slot(x,z0,z1,width,depth=6):
    r=width/2
    with cad.sketch('XZ') as sk:
        sk.line((x-r,z0+r),(x-r,z1-r))
        sk.arc((x-r,z1-r),(x,z1),(x+r,z1-r))
        sk.line((x+r,z1-r),(x+r,z0+r))
        sk.arc((x+r,z0+r),(x,z0),(x-r,z0+r))
    o=cad.extrude(sk.obj,distance=depth,operation='New body');cad.delete(sk.obj)
    p.place_min(o,'y',-35)
    return o

def build():
    cad.call('file',action='new');p.PARTS.clear()
    for i,(w,depth,z,h) in enumerate([(330,80,0,7),(318,74,7,6),(306,68,13,5)]):
        p.box(f'Stepped walnut base {i+1}',(-w/2,-depth/2,z,w,depth,h),p.CAP,.65)
    with cad.sketch('XZ') as sk:
        sk.line((-78,18),(78,18));sk.line((78,18),(78,162))
        sk.arc((78,162),(72.727922,174.727922),(60,180));sk.line((60,180),(-60,180))
        sk.arc((-60,180),(-72.727922,174.727922),(-78,162));sk.line((-78,162),(-78,18))
    tower=cad.extrude(sk.obj,distance=65,operation='New body');cad.delete(sk.obj);p.place_min(tower,'y',-32.5)
    p.done(tower,'Rounded walnut clock tower',p.CAP)
    for side in [-1,1]:
        for j,(a,b,h) in enumerate([(78,105,126),(105,130,96),(130,150,66)]):
            x=a if side==1 else -b
            wing=p.box_raw((x,-32.5,18,b-a,65,h))
            for i in range(3):cad.subtract(wing,slot(x+5.5+i*((b-a-11)/2),29,18+h-9,2.8))
            p.done(wing,f'{"Right" if side==1 else "Left"} fluted step {j+1}',p.CAP)
    cad.subtract(tower,cylinder_y(57.5,12,-36))
    cad.subtract(tower,cylinder_y(32,18,20))
    cad.subtract(tower,cylinder_y(3.5,80,-40))
    for i in range(11):
        a=math.radians(31+i*10.9);b=a+math.radians(7.1);mid=(a+b)/2
        def at(r,t):return (r*math.cos(t),90+r*math.sin(t))
        with cad.sketch('XZ') as sk:
            sk.arc(at(62,a),at(62,mid),at(62,b));sk.line(at(62,b),at(86,b))
            sk.arc(at(86,b),at(86,mid),at(86,a));sk.line(at(86,a),at(62,a))
        tool=cad.extrude(sk.obj,distance=3,operation='New body');cad.delete(sk.obj);p.place_min(tool,'y',-34)
        cad.subtract(tower,tool)
    checkpoint('01-carved-walnut-case')
    dial=cylinder_y(55.5,2,-35.8);p.done(dial,'Warm ivory dial',IVORY)
    with cad.sketch('XY') as sk:
        sk.circle((0,0),60);sk.circle((0,0),56.3)
    bezel=cad.extrude(sk.obj,distance=3.2,operation='New body');cad.delete(sk.obj)
    rotate(bezel,90);p.place_min(bezel,'y',-37);cad.move(bezel,z=90);p.done(bezel,'Brass dial bezel',BRASS)
    for h in range(1,13):
        a=math.radians(90-h*30);x=43*math.cos(a);z=90+43*math.sin(a)
        with cad.sketch('XZ') as sk:sk.text(str(h),at=(x-1.9*len(str(h)),z-3.2),height=6.5,font='Arial')
        o=cad.extrude(sk.obj,distance=.3,operation='New body');cad.delete(sk.obj);p.place_min(o,'y',-36.15)
        p.done(o,f'Applied hour numeral {h:02}',BLACK)
    for n in range(60):
        major=n%5==0;length=3 if major else 1.3;width=.8 if major else .32
        o=p.box_raw((-width/2,-36.2,51.5 if major else 53,width,.3,length))
        rotate(o,n*6,'Y');cad.move(o,z=90)
        p.done(o,f'{"Hour" if major else "Minute"} index {n:02}',BRASS if major else BLACK)
    for name,length,width,angle,y in [('Hour hand',30,3.6,-145,-37),('Minute hand',43,2.1,-30,-37.6)]:
        o=p.profile('XZ',[(-5,-width/2),(length-4,-width/2),(length,0),(length-4,width/2),(-5,width/2)],.6)
        rotate(o,angle,'Y');cad.move(o,z=90);p.place_min(o,'y',y);p.done(o,name,BLACK)
    p.done(cylinder_y(3.3,1.4,-38.4),'Brass hand pin',BRASS)
    p.done(cylinder_y(34,2,32.5),'Rear walnut mechanism cover',p.CAP)
    for i,a in enumerate([45,135,225,315]):
        t=math.radians(a);o=cylinder_y(1.5,.8,34.5,90+29*math.sin(t),29*math.cos(t))
        p.done(o,f'Rear cover screw {i+1}',BRASS)
    finish()

def finish():
    cad.select();cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in p.PARTS])
    info=[cad.get(n).info for n in p.PARTS]
    bad=[(i['name'],i.get('valid'),i.get('volume')) for i in info if not i.get('valid') or i.get('volume',0)<=0]
    assert not bad,bad
    cad.call('select_tab',title='Solid');cad.call('view',yaw=18,pitch=14,fit=True,grid=False,display='shaded')
    path=p.ROOT/'gallery/models/deco-mantel-clock.prism';cad.save(str(path))
    (OUT/'verification.json').write_text(json.dumps(dict(solids=len(info),valid=True,model=str(path),checks='Native solid validity and positive volume. No test suite, render or CAM setup.'),indent=2))
    print('Finished clock:',len(info),'valid solids',flush=True)

if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True);p.d.OUT=OUT
    b,l=p.d.connect(p.ROOT/'artifacts/pavilion/automation.json')
    try:build()
    finally:b.terminate();b.wait(timeout=10);l.close()
