"""Linen T2 toaster: solid-modeled gallery assembly, owner-authorized #135.

Filleted solids, extruded slots and rings, Boolean pockets and cylindrical controls.
Canonical scope: docs/interface/gallery/README.md. No Freeform, render or test suite.
"""
import argparse
import base64
import hashlib
import json
import math
import demo_session as d
import build_pavilion as p

cad,ROOT=d.cad,d.ROOT
OUT=ROOT/'artifacts/toaster'
MODEL=ROOT/'gallery/models/linen-t2-toaster.prism'
PARTS=[]
IVORY=dict(material='6061',color='#EDE2CD',metal=.08,roughness=.32,pattern='Plain',surface=dict(Coat=.28,Bump=0))
STEEL=dict(material='304',color='#CDD4D5',metal=.85,roughness=.25,pattern='Plain',surface=dict(Bump=0))
BLACK=dict(material='abs',color='#25282A',metal=.05,roughness=.48,pattern='Plain')
WELL=dict(material='304',color='#202225',metal=.18,roughness=.62,pattern='Plain')
INK=dict(material='abs',color='#4B4842',roughness=.55,pattern='Plain')
RUBBER=dict(material='silicone',color='#191B1D',roughness=.8,pattern='Plain')
AMBER=dict(material='pc',color='#E6A144',roughness=.22,pattern='Plain',surface=dict(Coat=.5))

def done(o,name,look):
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,name=name,visible=False)),dict(tool='material_apply',args=dict(object=name,**look))])
    o.name=name;PARTS.append(name);return o

def edges(o):return cad.call('solid_find',object=o.name,kind='edges',limit=80)['hits']

def roundbox(bounds,r):
    o=p.box_raw(bounds)
    cad.fillet([dict(object=o.name,edge=e['edge']) for e in edges(o)],radius=r)
    return o

def rounded_rect(x,y,w,depth,r):
    with cad.sketch('XY') as sk:
        sk.line((x+r,y),(x+w-r,y));sk.arc((x+w-r,y),(x+w-r+r*.70710678,y+r-r*.70710678),(x+w,y+r))
        sk.line((x+w,y+r),(x+w,y+depth-r));sk.arc((x+w,y+depth-r),(x+w-r+r*.70710678,y+depth-r+r*.70710678),(x+w-r,y+depth))
        sk.line((x+w-r,y+depth),(x+r,y+depth));sk.arc((x+r,y+depth),(x+r-r*.70710678,y+depth-r+r*.70710678),(x,y+depth-r))
        sk.line((x,y+depth-r),(x,y+r));sk.arc((x,y+r),(x+r-r*.70710678,y+r-r*.70710678),(x+r,y))
    return sk.obj

def rplate(bounds,r,bevel=0):
    x,y,z,w,dep,t=bounds;sk=rounded_rect(x,y,w,dep,r)
    o=cad.extrude(sk,distance=t,operation='New body');cad.delete(sk);p.place_min(o,'z',z)
    if bevel:
        boundary=[dict(object=o.name,edge=e['edge']) for e in edges(o) if abs(e['center'][2]-z)<.001 or abs(e['center'][2]-z-t)<.001]
        cad.chamfer(boundary,distance=bevel)
    return o

def slot(y,width,z,height,centres=82):
    with cad.sketch('XY') as sk:sk.slot((-centres,y),(centres,y),width)
    o=cad.extrude(sk.obj,distance=height,operation='New body');cad.delete(sk.obj);p.place_min(o,'z',z)
    return o

def slotring(y,outer,inner,z,height,centres=82):
    with cad.sketch('XY') as sk:
        sk.slot((-centres,y),(centres,y),outer);sk.slot((-centres,y),(centres,y),inner)
    o=cad.extrude(sk.obj,distance=height,operation='New body');cad.delete(sk.obj);p.place_min(o,'z',z)
    return o

def cyl_x(radius,length,x,y,z,bevel=0):
    o=cad.cylinder(radius=radius,height=length,operation='New body')
    if bevel:
        circles=[dict(object=o.name,edge=e['edge']) for e in edges(o) if e['type']=='Circle']
        cad.chamfer(circles,distance=bevel)
    cad.run('rotate',slots={'Objects':o},axis='Y',angle=90,pivot='World origin')
    cad.move(o,x=x,y=y,z=z);return o

def ring_x(ro,ri,x,y,z,thickness):
    with cad.sketch('YZ') as sk:sk.circle((y,z),ro);sk.circle((y,z),ri)
    o=cad.extrude(sk.obj,distance=thickness,operation='New body');cad.delete(sk.obj);p.place_min(o,'x',x)
    return o

def cut_many(bodies,tool):
    for o in bodies[:-1]:cad.run('subtract',slots={'Body to cut from':o,'Tool bodies':[tool]},keep_tool_bodies=True)
    cad.subtract(bodies[-1],tool)

def checkpoint(name):
    cad.select();cad.save(str(OUT/(name+'.prism')));print('Saved',name,len(PARTS),'parts',flush=True)

def build():
    cad.call('document_new')
    shell=done(roundbox((-135,-85,20,270,170,175),26),'01 Ivory enamel casing',IVORY)
    roof=done(rplate((-119,-67,190,238,134,6),22,.65),'Brushed stainless top deck',STEEL)
    for y,label in [(-32,'Front'),(32,'Rear')]:
        cut_many([shell,roof],slot(y,27,106,106))
        cup=slot(y,26,110,85.5);cad.subtract(cup,slot(y,20,116,94))
        done(cup,label+' dark toasting-well liner',WELL)
        rim=slotring(y,35,27,196,2.2)
        top=[dict(object=rim.name,edge=e['edge']) for e in edges(rim) if e['center'][2]>198]
        cad.chamfer(top,distance=.45);done(rim,label+' polished slot rim',STEEL)
        done(slotring(y,17,14,165,1.8,65),label+' bread-lift support',STEEL)
    checkpoint('01-case-and-open-slots')
    base=done(rplate((-132,-82,12,264,164,14),24,1),'Graphite lower plinth',BLACK)
    outer=rplate((-134.5,-84.5,25,269,169,4),25)
    cad.subtract(outer,rplate((-128,-78,24,256,156,6),20));done(outer,'Polished lower waist trim',STEEL)
    p.cutbox(base,(-89,-87,15,178,10,9))
    tray=roundbox((-84,-84,16,168,2.5,6.5),1)
    pull=roundbox((-20,-90,17,40,7,4.5),1.8);cad.union(tray,pull);done(tray,'Stainless crumb-tray face and pull',STEEL)
    for i,(x,y) in enumerate([(-95,-52),(-95,52),(95,-52),(95,52)]):
        o=cad.cylinder(radius=10,height=14,position_x=x,position_y=y,operation='New body')
        circular=[dict(object=o.name,edge=e['edge']) for e in edges(o) if e['type']=='Circle'];cad.fillet(circular,radius=1.5)
        done(o,f'Rubber foot {i+1}',RUBBER)
    checkpoint('02-plinth-and-crumb-tray')
    with cad.sketch('YZ') as sk:sk.slot((0,118),(0,160),6)
    track=cad.extrude(sk.obj,distance=16,operation='New body');cad.delete(sk.obj);p.place_min(track,'x',130);cad.subtract(shell,track)
    with cad.sketch('YZ') as sk:sk.slot((0,118),(0,160),5.7)
    backing=cad.extrude(sk.obj,distance=1,operation='New body');cad.delete(sk.obj);p.place_min(backing,'x',130.3);done(backing,'Recessed carriage-lever track',BLACK)
    done(cyl_x(2.5,20,129,0,146),'Stainless carriage-lever stem',STEEL)
    done(roundbox((140,-14,141,13,28,10),4),'Graphite lift-lever paddle',BLACK)
    cad.subtract(shell,cyl_x(25.2,13,132,0,75))
    done(cyl_x(24.7,1.5,132.8,0,75),'Browning-dial shadow seat',BLACK)
    done(ring_x(27,23,135.4,0,75,2.2),'Polished dial bezel',STEEL)
    dial=cyl_x(22.5,10.8,134,0,75,.7)
    p.cutbox(dial,(144,-.8,86,2,1.6,9));done(dial,'Graphite browning selector',BLACK)
    done(p.box_raw((144.3,-.65,86.2,.5,1.3,8.6)),'Ivory dial index',IVORY)
    for y,label in [(-40,'Cancel'),(40,'Warm')]:
        done(cyl_x(6.5,3.8,135,y,128,.5),label+' push button',BLACK)
    led=cad.sphere(radius=1.8,position_x=136.2,position_y=40,position_z=143,operation='New body');done(led,'Amber status lens',AMBER)
    checkpoint('03-controls')
    with cad.sketch('YZ') as sk:
        for n in range(1,7):
            a=math.radians(210-(n-1)*48);y=34*math.cos(a);z=75+34*math.sin(a)
            sk.text(str(n),at=(y-1.5,z-2),height=5,font='Arial')
        sk.text('STOP',at=(-46,113),height=4,font='Arial')
        sk.text('WARM',at=(33,113),height=4,font='Arial')
    markings=cad.extrude(sk.obj,distance=.3,operation='New body');cad.delete(sk.obj);p.place_min(markings,'x',135.15);done(markings,'Control-panel printed legends',INK)
    with cad.sketch('XZ') as sk:sk.text('PRISM',at=(-26,111),height=10,font='Arial',spacing=.7)
    brand=cad.extrude(sk.obj,distance=.35,operation='New body');cad.delete(sk.obj);p.place_min(brand,'y',-85.4);done(brand,'Front PRISM wordmark',INK)
    finish()

def finish():
    cad.select();cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in PARTS])
    cad.run('make_part',slots={'Bodies':PARTS});cad.call('workspace',id='assemble');cad.call('select_tab',title='Assemble')
    info=[r['result'] for r in cad.call('batch',calls=[dict(tool='object_info',args=dict(object=n)) for n in PARTS])]
    assert all(o['valid'] and o['volume']>0 for o in info),[(o['name'],o['valid']) for o in info]
    cad.save(str(MODEL));cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    cad.call('workspace',id='assemble');cad.call('select_tab',title='Assemble')
    for name,pose in [('linen-t2-prism.png',dict(yaw=55,pitch=29,target=[5,0,99],distance=650)),
                      ('linen-t2-top.png',dict(yaw=40,pitch=75,target=[0,0,125],distance=600))]:
        cad.call('view',grid=False,display='shaded',**pose);cad.call('wait_idle',timeout_ms=5000)
        shot=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
        (OUT/name).write_bytes(base64.b64decode(shot['_image']['data']))
    cad.call('view',yaw=55,pitch=29,target=[5,0,99],distance=650)
    saved=json.loads(MODEL.read_text(encoding='utf-8'))['Objects']
    assert len(saved)==len(PARTS) and len({o['Asm']['Key'] for o in saved})==len(PARTS)
    assert sum(bool(o['Asm']['Grounded']) for o in saved)==1
    evidence=dict(date='2026-11-16',branch='codex/v0.4-illustrated-docs',parts=len(PARTS),valid=True,saveReopened=True,
      method='Actual Windows MCP/API solid primitives, fillets, extrusions, Boolean slots/pockets and Make Part assembly. Native validity, positive volume, component records and save/reopen checked. No Freeform, renderer or test suite.',
      bytes=MODEL.stat().st_size,sha256=hashlib.sha256(MODEL.read_bytes()).hexdigest(),objects=info,
      components=[dict(name=o['Name'],grounded=o['Asm']['Grounded']) for o in saved])
    (OUT/'verification.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
    print('Finished Linen T2:',len(PARTS),'valid assembly components',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--discovery',default=str(ROOT/'artifacts/guitar-v2/automation.json'));a=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);d.OUT=OUT;b,l=d.connect(a.discovery)
    try:build()
    finally:b.terminate();b.wait(timeout=10);l.close()
