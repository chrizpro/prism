"""Maple and walnut observatory pavilion, owner-approved concept #132.

Wooden design only: no CAM setup, automated test suite or professional render.
Canonical scope: docs/interface/gallery/README.md.
"""
import argparse
import math
import json
import demo_session as d

cad,ROOT=d.cad,d.ROOT
OUT=ROOT/'artifacts/pavilion'
d.OUT=OUT
PARTS=[]
MAPLE=dict(material='maple',color='#E5CAA5',roughness=.55,pattern='Wood',patternScale=60,surface=dict(Coat=.1,Bump=0))
TRIM=dict(material='maple',color='#F0D9B6',roughness=.52,pattern='Wood',patternScale=80,surface=dict(Coat=.12,Bump=0))
WALNUT=dict(material='walnut',color='#785039',roughness=.47,pattern='Wood',patternScale=40,patternAngle=12,surface=dict(Coat=.15,Bump=.025))
DARK=dict(material='walnut',color='#503623',roughness=.58,pattern='Wood',patternScale=35)
CAP=dict(material='walnut',color='#FFFFFF',roughness=.45,pattern='Plain',surface=dict(ColorMap=d.image(ROOT/'gallery/textures/walnut-pavilion-512.jpg'),TextureSize=170,TextureAspect=.8,TextureRepeat=False,Coat=.14))

def done(o,name,look=MAPLE):
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,name=name,visible=False)),dict(tool='material_apply',args=dict(object=name,**look))])
    o.name=name;PARTS.append(name);return o

def box_raw(bounds):
    x,y,z,w,depth,h=bounds
    # Prism's box origin is centered in XY, with its bottom at Z.
    o=cad.box(width=w,depth=depth,height=h,position_x=x+w/2,position_y=y+depth/2,position_z=z,operation='New body')
    assert all(abs(a-b)<.001 for a,b in zip(o.info['min'],[x,y,z])),o.info
    return o

def box(name,bounds,look=MAPLE,bevel=0):
    o=box_raw(bounds)
    if bevel:
        edges=cad.call('solid_find',object=o.name,kind='edges',limit=30)['hits']
        cad.chamfer([dict(object=o.name,edge=e['edge']) for e in edges],distance=bevel)
    return done(o,name,look)

def profile(plane,points,depth,offset=0):
    with cad.sketch(plane,offset=offset) as sk:sk.polyline(points,closed=True)
    o=cad.extrude(sk.obj,distance=depth,operation='New body');cad.delete(sk.obj)
    return o

def place_min(o,axis,value):
    info=cad.get(o.name).info;delta=value-info['min']['xyz'.index(axis)]
    if abs(delta)>1e-6:cad.move(o,**{axis:delta})

def cutbox(body,bounds):
    tool=box_raw(bounds);cad.subtract(body,tool)

def checkpoint(name):
    cad.select()
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in PARTS])
    cad.call('view',yaw=32,pitch=25,fit=True,grid=False,display='shaded')
    cad.save(str(OUT/(name+'.prism')))
    cad.call('wait_idle',timeout_ms=5000)
    d.capture(OUT/(name+'.png'))
    print('Saved',name,flush=True)
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=False)) for n in PARTS])

def architecture():
    cad.call('file',action='new')
    box('Maple presentation plinth',(-150,-115,0,300,230,8),TRIM,.8)
    box('Central hall',(-58,-45,12,116,127,96))
    for side in [-1,1]:
        x=-133 if side<0 else 58;label='West' if side<0 else 'East'
        box(label+' wing',(x,-42,12,75,122,65))
        box(label+' foundation band',(x-1,-43,12,77,124,6),TRIM,.35)
        for i,(extra,z,h) in enumerate([(1,77,3),(3,80,3),(5,83,2)]):
            box(label+f' cornice {i+1}',(x-extra,-42-extra,z,75+2*extra,122+2*extra,h),TRIM,.25)
    box('Hall foundation band',(-59,-46,12,118,129,6),TRIM,.35)
    for i,(extra,z,h) in enumerate([(1,104,3),(3,107,3),(5,110,3)]):
        box(f'Hall cornice {i+1}',(-58-extra,-45-extra,z,116+2*extra,127+2*extra,h),TRIM,.25)
    for i,(half,y,z) in enumerate([(69,-109,8),(66,-103,11),(63,-96,14),(60,-88,17)]):
        box(f'Entry stair {i+1}',(-half,y,z,half*2,-42-y,3),TRIM,.3)
    checkpoint('01-halls-and-stairs')

def hip_roof(name,x0,x1,y0,y1,z,h):
    o=box_raw((x0,y0,z,x1-x0,y1-y0,h));cx=(x0+x1)/2
    # Four planar trimming wedges make a genuine hipped roof with a narrow ridge.
    for points in [[(x0,z),(cx-1,z+h),(cx-1,z+h+20),(x0,z+h+20)],[(cx+1,z+h),(x1,z),(x1,z+h+20),(cx+1,z+h+20)]]:
        tool=profile('XZ',points,y1-y0+10);place_min(tool,'y',y0-5);cad.subtract(o,tool)
    for points in [[(y0,z),(y0+34,z+h),(y0+34,z+h+20),(y0,z+h+20)],[(y1-34,z+h),(y1,z),(y1,z+h+20),(y1-34,z+h+20)]]:
        tool=profile('YZ',points,x1-x0+10);place_min(tool,'x',x0-5);cad.subtract(o,tool)
    return done(o,name,CAP)

def roofs():
    hip_roof('West walnut hipped roof',-138,-53,-47,85,85,18)
    hip_roof('East walnut hipped roof',53,138,-47,85,85,18)
    drum=cad.cylinder(radius=46,height=19,position_z=113,operation='New body');drum.rename('Recessed maple dome drum')
    cutter=box_raw((-3.5,-49,116,7,8,12))
    tools=[cutter]+cad.circular_pattern(cutter,count=16)
    cad.subtract(drum,*tools);cad.move(drum,y=20);done(drum,'Recessed maple dome drum')
    for name,r,z,h in [('Drum lower moulding',49,112,3),('Drum upper moulding',49,131,3)]:
        o=cad.cylinder(radius=r,height=h,position_y=20,position_z=z,operation='New body');done(o,name,TRIM)
    with cad.sketch('XZ') as sk:
        sk.line((0,0),(45,0));sk.arc((45,0),(45/math.sqrt(2),45/math.sqrt(2)),(0,45));sk.line((0,45),(0,0))
    dome=cad.revolve(sk.obj,axis='World Z',operation='New body');cad.delete(sk.obj);cad.move(dome,y=20,z=134)
    done(dome,'Walnut hemispherical dome',CAP)
    checkpoint('02-roofs-and-dome')

def window(body,x,y,z,normal='front',name='Window'):
    if normal in ['front','rear']:
        cutbox(body,(x-6.5,y-4,z,13,8,29))
        inset_y=y+3.1 if normal=='front' else y-3.9
        box(name+' walnut recess',(x-6.25,inset_y,z+.3,12.5,.8,28.4),DARK)
        with cad.sketch('XZ') as sk:
            sk.rect((x-8.5,z-2),(x+8.5,z+31));sk.rect((x-6.5,z),(x+6.5,z+29))
        frame=cad.extrude(sk.obj,distance=2.2,operation='New body');cad.delete(sk.obj)
        place_min(frame,'y',y-1.8 if normal=='front' else y-.4);done(frame,name+' maple frame',TRIM)
        box(name+' projecting sill',(x-10,y-3 if normal=='front' else y-.5,z-2.5,20,3.5,2),TRIM,.2)
    else:
        cutbox(body,(x-4,y-6.5,z,8,13,29))
        inset_x=x-3.9 if normal=='right' else x+3.1
        box(name+' walnut recess',(inset_x,y-6.25,z+.3,.8,12.5,28.4),DARK)
        with cad.sketch('YZ') as sk:
            sk.rect((y-8.5,z-2),(y+8.5,z+31));sk.rect((y-6.5,z),(y+6.5,z+29))
        frame=cad.extrude(sk.obj,distance=2.2,operation='New body');cad.delete(sk.obj)
        place_min(frame,'x',x-.4 if normal=='right' else x-1.8);done(frame,name+' maple frame',TRIM)
        box(name+' projecting sill',(x-.5 if normal=='right' else x-3,y-10,z-2.5,3.5,20,2),TRIM,.2)

def facade():
    for side,label in [(-1,'West'),(1,'East')]:
        body=cad.get(label+' wing')
        for i,x in enumerate([side*v for v in [72,95.5,119]]):
            window(body,x,-42,33,name=label+f' front window {i+1}')
            window(body,x,80,33,'rear',label+f' rear window {i+1}')
        for i,y in enumerate([-20,16,52]):window(body,side*133,y,33,'right' if side>0 else 'left',label+f' side window {i+1}')
        checkpoint('03-'+label.lower()+'-windows')
    for i,x in enumerate([-35,0,35]):window(cad.get('Central hall'),x,82,55,'rear',f'Hall rear window {i+1}')
    checkpoint('04-all-window-detail')

def arch_profile(r,bottom,spring):
    with cad.sketch('XZ') as sk:
        sk.line((-r,bottom),(r,bottom));sk.line((r,bottom),(r,spring))
        sk.arc((r,spring),(0,spring+r),(-r,spring));sk.line((-r,spring),(-r,bottom))
    return sk.obj

def entrance():
    hall=cad.get('Central hall')
    sk=arch_profile(16,20,50);tool=cad.extrude(sk,distance=7,operation='New body');cad.delete(sk);place_min(tool,'y',-47);cad.subtract(hall,tool)
    sk=arch_profile(15.6,20.3,50);door=cad.extrude(sk,distance=3,operation='New body');cad.delete(sk);place_min(door,'y',-42)
    cutbox(door,(-.22,-42.2,21,.44,1,44));done(door,'Arched walnut double door',WALNUT)
    with cad.sketch('XZ') as sk:
        for r,bottom in [(18.5,18.5),(16,20)]:
            sk.line((-r,bottom),(r,bottom));sk.line((r,bottom),(r,50));sk.arc((r,50),(0,50+r),(-r,50));sk.line((-r,50),(-r,bottom))
    frame=cad.extrude(sk.obj,distance=2.5,operation='New body');cad.delete(sk.obj);place_min(frame,'y',-46.3);done(frame,'Maple arched door surround',TRIM)
    for side in [-1,1]:
        x=-13 if side<0 else 1.5
        for i,(z,h) in enumerate([(24,10),(37,10),(50,9)]):box(f'Door leaf {side} raised panel {i+1}',(x,-42.35,z,11.5,.6,h),WALNUT,.16)
        o=cad.sphere(radius=.65,position_x=side*2,position_y=-42.8,position_z=41,operation='New body');done(o,f'Walnut door pull {side}',DARK)
    checkpoint('05-arched-entrance')

def portico():
    section=[(0,0),(7.5,0),(7.5,2),(6.3,3.5),(5.8,5),(5.35,7),(4.9,36),(5.5,37.5),(6.8,39),(7.5,40),(7.5,42),(8,42),(8,43),(0,43)]
    with cad.sketch('XZ') as sk:sk.polyline(section,closed=True)
    for i,x in enumerate([-44,-15,15,44]):
        o=cad.revolve(sk.obj,axis='World Z',operation='New body')
        cad.move(o,x=x,y=-70,z=22)
        done(o,f'Turned maple column {i+1}',TRIM)
        box(f'Column {i+1} square plinth',(x-7,-77,20,14,14,2),TRIM,.25)
        box(f'Column {i+1} abacus',(x-8,-78,65,16,16,2),TRIM,.3)
    cad.delete(sk.obj)
    for i,(extra,z,h) in enumerate([(0,67,4),(1,71,2),(3,73,2)]):
        box(f'Portico entablature {i+1}',(-60-extra,-85-extra,z,120+2*extra,43+2*extra,h),TRIM,.25)
    gable=profile('XZ',[(-64,75),(0,104),(64,75)],42);place_min(gable,'y',-86)
    tool=profile('XZ',[(-54,78),(0,101),(54,78)],1.2);place_min(tool,'y',-86.2);cad.subtract(gable,tool)
    done(gable,'Recessed maple pediment',TRIM)
    roof=profile('XZ',[(-64,75),(0,104),(64,75),(64,78.5),(0,107.5),(-64,78.5)],46);place_min(roof,'y',-88)
    done(roof,'Walnut portico roof',CAP)
    checkpoint('06-complete-pavilion')

def finish():
    if not PARTS:PARTS.extend(o.name for o in cad.objects() if o.kind=='Solid')
    cad.select();cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in PARTS])
    reports=cad.call('batch',calls=[dict(tool='object_info',args=dict(object=n)) for n in PARTS])
    info=[r['result'] for r in reports];bad=[(r['name'],r['valid'],r['volume']) for r in info if not r['valid'] or r['volume']<=0]
    assert not bad,bad
    cad.call('select_tab',title='Solid');cad.call('view',yaw=32,pitch=25,fit=True,grid=False,display='shaded')
    cad.call('view',distance=540)
    # Keep the design assembly separate from future manufacturing placements.
    cad.run('make_part',slots={'Bodies':PARTS})
    model=ROOT/'gallery/models/maple-observatory.prism';cad.save(str(model))
    cad.call('file',action='open',path=str(model));cad.call('view',yaw=32,pitch=25,fit=True,grid=False,display='shaded');cad.call('view',distance=540)
    d.capture(OUT/'pavilion-front-quarter.png')
    (OUT/'verification.json').write_text(json.dumps(dict(solids=len(PARTS),valid=True,objects=len(cad.objects()),checks='Native validity, positive volume, save/reopen and viewport inspection; no automated test suite, CAM or professional render.'),indent=2))
    print('Verified and reopened',len(PARTS),'solids',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--phase',choices=['architecture','roofs','facade','entrance','portico','finish','all'],default='all');p.add_argument('--resume',action='store_true');a=p.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);b,l=d.connect(OUT/'automation.json')
    try:
        if a.resume:PARTS.extend(o.name for o in cad.objects() if o.kind=='Solid')
        for phase,fn in [('architecture',architecture),('roofs',roofs),('facade',facade),('entrance',entrance),('portico',portico),('finish',finish)]:
            if a.phase in [phase,'all']:fn()
    finally:b.terminate();b.wait(timeout=10);l.close()
