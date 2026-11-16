"""Serein S1 native phone assembly, owner-authorized #137.

Canonical scope: docs/interface/gallery/README.md. Solid modeling and an original
procedural display bitmap; no Freeform, renderer or test suite.
"""
import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
import demo_session as d
import build_pavilion as p
import build_toaster as t

cad,ROOT=d.cad,d.ROOT
OUT=ROOT/'artifacts/serein-phone'
MODEL=ROOT/'gallery/models/serein-s1-phone.prism'
TEXTURE=ROOT/'gallery/textures/serein-display.png'
PARTS=[]
METAL=dict(material='6061',color='#B6BDB9',metal=.72,roughness=.32,pattern='Plain')
POLISH=dict(material='304',color='#D7DEDA',metal=.8,roughness=.2,pattern='Plain')
SAGE=dict(material='abs',color='#BDCEC5',metal=.05,roughness=.42,pattern='Plain',surface=dict(Coat=.2))
ISLAND=dict(material='6061',color='#7C968B',metal=.35,roughness=.38,pattern='Plain')
BLACK=dict(material='abs',color='#10171B',metal=.12,roughness=.28,pattern='Plain')
OPTIC=dict(material='pc',color='#192D43',metal=.5,roughness=.14,pattern='Plain',surface=dict(Coat=.5))
IRIS=dict(material='6061',color='#254C61',metal=.65,roughness=.22,pattern='Plain')
WHITE=dict(material='abs',color='#F2F4E8',roughness=.4,pattern='Plain')


def wallpaper():
    import numpy as np
    from PIL import Image
    w,h=540,1180
    yy,xx=np.mgrid[0:h,0:w];x=xx/w;y=yy/h
    a=np.zeros((h,w,3));a[:]=[20,38,47]
    glow=np.exp(-((x-.15)**2/.65+(y-.3)**2/.7))[:,:,None]
    a+=glow*np.array([13,32,31])
    # Three broad sweeping elliptical ribbons, original code-generated artwork.
    for cx,cy,rx,ry,width,c0,c1 in [(-.25,.92,1.21,.60,.19,[78,135,128],[184,214,191]),
                                    (1.3,.83,1.2,.45,.18,[33,82,88],[112,171,159]),
                                    (-.25,1.17,1.1,.56,.14,[126,139,113],[218,194,139])]:
        q=np.sqrt(((x-cx)/rx)**2+((y-cy)/ry)**2)
        mask=(q<1)&(q>1-width)
        blend=np.clip((q-(1-width))/width,0,1)[:,:,None]
        shade=.74+.26*np.sin(blend*math.pi)
        color=(np.array(c0)*(1-blend)+np.array(c1)*blend)*shade
        a[mask]=color[mask]
    # Keep the wallpaper abstract: labels and camera opening are native geometry.
    Image.fromarray(np.uint8(np.clip(a,0,255))).save(TEXTURE)


def done(o,name,look):
    o.rename(name)
    cad.call('material_apply',object=name,**look)
    d.hide(o);PARTS.append(name);return o


def plate(bounds,r,bevel=0):return t.rplate(bounds,r,bevel)


def cyl(x,y,z,r,h):
    return cad.cylinder(radius=r,height=h,position_x=x,position_y=y,position_z=z,operation='New body')


def ring(x,y,z,ro,ri,h):
    with cad.sketch('XY',offset=z) as sk:sk.circle((x,y),ro);sk.circle((x,y),ri)
    o=cad.extrude(sk.obj,distance=h,operation='New body');cad.delete(sk.obj);return o


def text_xy(text,at,z,height,name,look=WHITE,rear=False):
    with cad.sketch('XY') as sk:sk.text(text,at=at,height=height,font='Arial')
    o=cad.extrude(sk.obj,distance=.025,operation='New body');cad.delete(sk.obj)
    if rear:
        cad.run('rotate',slots={'Objects':o},axis='Y',angle=180,pivot='World origin')
    p.place_min(o,'z',z)
    return done(o,name,look)


def checkpoint(name):
    cad.select();cad.save(str(OUT/(name+'.prism')));print('Saved',name,len(PARTS),'parts',flush=True)


def build():
    wallpaper();cad.call('document_new')
    frame=plate((-37,-78,.65,74,156,7.1),11,.4);frame.rename('Titanium perimeter chassis')
    # Real shallow front/back rebates leave a consistent metal rail.
    cad.subtract(frame,plate((-35.3,-76.3,7.15,70.6,152.6,2),9.6))
    cad.subtract(frame,plate((-35.3,-76.3,-1,70.6,152.6,2.15),9.6))
    # Charging-port mouth is a real rounded opening in the lower rail.
    port=plate((-4.7,-2,-1.6,9.4,4,3.2),1.5)
    cad.run('rotate',slots={'Objects':port},axis='X',angle=90,pivot='World origin')
    cad.move(port,y=-77,z=4.2);cad.subtract(frame,port)
    # Ten real speaker / microphone perforations along the bottom edge.
    for x in [-24,-20.5,-17,-13.5,-10,10,13.5,17,20.5,24]:
        tool=cyl(x,0,0,.8,4)
        cad.run('rotate',slots={'Objects':tool},axis='X',angle=90,pivot='World origin')
        cad.move(tool,y=-75,z=4.2);cad.subtract(frame,tool)
    done(frame,'Titanium perimeter chassis',METAL)
    done(plate((-35.1,-76.1,.35,70.2,152.2,.85),9.4,.15),'Satin sage rear panel',SAGE)
    done(plate((-35.1,-76.1,7.18,70.2,152.2,.72),9.4,.12),'Black glass perimeter seal',BLACK)
    display=plate((-33.5,-73.5,7.91,67,147,.22),8.6,.05)
    done(display,'Embedded abstract OLED display',dict(material='abs',color='#FFFFFF',roughness=.3,pattern='Plain',surface=dict(
        ColorMap=d.image(TEXTURE),Projection=1,TextureAxis=2,TextureSize=67,TextureAspect=147/67,TextureRepeat=False,Coat=.15)))
    # Clear perimeter is represented by a fine polished glass edge, not an opaque slab over the display.
    rim=plate((-34.3,-74.3,7.89,68.6,148.6,.18),9)
    cad.subtract(rim,plate((-33.5,-73.5,7.7,67,147,.7),8.6))
    done(rim,'Polished cover-glass edge',POLISH)
    checkpoint('01-chassis-and-display')
    text_xy('9:41',(-22,35),8.145,16,'Lock-screen clock')
    text_xy('MONDAY 16',(-15,27),8.145,3.6,'Lock-screen date')
    text_xy('S E R E I N',(-12,-54),8.145,2.8,'Lock-screen signature')
    done(plate((-10,-68.5,8.145,20,.9,.035),.44),'Gesture indicator',WHITE)
    done(cyl(0,65.8,8.14,2.0,.12),'Front camera aperture',BLACK)
    done(cyl(0,65.8,8.26,1.05,.035),'Front camera optical element',OPTIC)
    done(plate((-7,73.9,7.77,14,.65,.1),.3),'Upper earpiece slit',BLACK)
    # Side buttons are separate rounded solids, with a restrained visible stand-off.
    for y,length,label in [(12,18,'Power button'),(44,8,'Camera action button')]:
        done(t.roundbox((36.65,y-length/2,3,1.0,length,2.8),.45),label,POLISH)
    for y,label in [(24,'Volume up'),(6,'Volume down')]:
        done(t.roundbox((-37.65,y-5,3,1.0,10,2.8),.45),label,POLISH)
    for x in [-37.03,36.78]:
        for y in [-58,58]:done(p.box_raw((x,y,.95,.25,.9,6.5)),f'Antenna break {x} {y}',WHITE)
    # Dark port liner and gold contact tongue sit behind the actual opening.
    done(p.box_raw((-4.5,-76.2,2.7,9,1,3)), 'USB-C dark inner liner',BLACK)
    done(p.box_raw((-3.3,-77.7,4,6.6,1.7,.38)),'USB-C center tongue',METAL)
    checkpoint('02-front-and-controls')
    island=plate((-31.5,24,-.9,40,48,1.25),9,.25)
    done(island,'Sage camera island',ISLAND)
    for i,(x,y) in enumerate([(-21,59),(-21,40),(-3,49.5)],1):
        done(cyl(x,y,-1.75,8.1,1.0),f'Camera {i} graphite barrel',BLACK)
        done(ring(x,y,-2.05,8.15,6.75,.35),f'Camera {i} polished bezel',POLISH)
        done(ring(x,y,-2.14,6.75,5.9,.18),f'Camera {i} inner retaining ring',BLACK)
        done(cyl(x,y,-2.13,5.9,.2),f'Camera {i} recessed sapphire window',OPTIC)
        done(ring(x,y,-2.155,3.9,2.6,.025),f'Camera {i} optical iris',IRIS)
        done(cyl(x,y,-2.17,2.55,.03),f'Camera {i} dark pupil',BLACK)
    done(cyl(-3,66,-1.13,2.6,.25),'Rear flash diffuser',WHITE)
    done(cyl(-3,32,-1.15,2.0,.25),'Rear depth sensor',BLACK)
    done(cyl(3.7,37,-1.13,.7,.25),'Rear microphone',BLACK)
    text_xy('S E R E I N',(-12,-51),.30,3.2,'Rear Serein wordmark',ISLAND,rear=True)
    checkpoint('03-complete-exterior')
    finish()


def capture(name,**pose):
    cad.call('view',grid=False,display='shaded',**pose);cad.call('wait_idle',timeout_ms=5000)
    r=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
    (OUT/name).write_bytes(base64.b64decode(r['_image']['data']))


def finish():
    cad.select()
    for n in PARTS:cad.call('object_update',object=n,visible=True)
    cad.run('make_part',slots={'Bodies':PARTS})
    cad.call('workspace',id='assemble');cad.call('select_tab',title='Assemble')
    cad.save(str(MODEL));cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    capture('serein-front.png',yaw=15,pitch=65,target=[0,0,4],distance=295)
    # Temporarily turn the assembly over for well-lit rear viewport evidence.
    # Reopen the saved original rather than retaining a presentation transform.
    cad.run('rotate',slots={'Objects':PARTS},axis='Y',angle=180,pivot='World origin')
    capture('serein-rear.png',yaw=-15,pitch=65,target=[0,0,-4],distance=295)
    cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    cad.call('view',yaw=15,pitch=65,target=[0,0,4],distance=295)
    cad.save(str(MODEL))
    saved=json.loads(MODEL.read_text(encoding='utf-8'))['Objects']
    infos=[cad.call('object_info',object=n) for n in PARTS]
    assert all(o['valid'] and o['volume']>0 for o in infos)
    assert len(saved)==len(PARTS) and len({o['Asm']['Key'] for o in saved})==len(PARTS)
    assert sum(bool(o['Asm']['Grounded']) for o in saved)==1
    evidence=dict(date='2026-11-16',branch='codex/v0.4-illustrated-docs',desktopSource='c9a5773',
      parts=len(PARTS),saveReopened=True,valid=True,sha256=hashlib.sha256(MODEL.read_bytes()).hexdigest(),
      bytes=MODEL.stat().st_size,objects=infos,method='Actual Windows MCP/cadkit native solid model; Make Part assembly, validity and save/reopen. Embedded original procedural bitmap. No Freeform, renderer or test suite.')
    (OUT/'verification.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print('Finished Serein S1:',len(PARTS),'valid components',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--discovery',default=str(ROOT/'artifacts/guitar-v2/automation.json'));a=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);d.OUT=OUT;b,l=d.connect(a.discovery)
    try:build()
    finally:b.terminate();b.wait(timeout=10);l.close()
