"""Bellavista 1964, an original period GT built with Prism's live MCP/cadkit.

python gallery/build_bellavista.py [--phase body|detail|finish|verify|render] [--resume]
Use --discovery for an isolated app. No imported meshes or generated preview art.
Canonical specification: docs/interface/gallery/README.md; delivery issue #129.
"""
import argparse
import base64
import json
import math
import os
import time
from pathlib import Path

import build_examples as study

cad = study.cad
ROOT = study.ROOT
OUT = ROOT / 'artifacts/bellavista'
MODEL = ROOT / 'gallery/models/bellavista-1964.prism'
PAINT = dict(material='6061', color='#762E36', metal=.35, roughness=.22,
             surface=dict(Coat=.9, CoatRoughness=.12))
CHROME = dict(material='304', color='#D9E1E6', metal=1, roughness=.14)
RUBBER = dict(material='silicone', color='#161A1F', roughness=.72)
INK = dict(material='abs', color='#17222B', roughness=.35)
LEATHER = dict(material='abs', color='#B97642', roughness=.48, surface=dict(Bump=.08))
GLASS = dict(material='glass', color='#B8D5DC', roughness=.10,
             surface=dict(Transmission=.85, Ior=1.48, Coat=.3))
LAMP = dict(material='glass', color='#F5EEE0', roughness=.18, surface=dict(Transmission=.35, Ior=1.48))
RED = dict(material='abs', color='#A80F23', roughness=.22, surface=dict(Coat=.7))
AMBER = dict(material='abs', color='#E99830', roughness=.25)
KNOWN = {}


def done(obj, name, look):
    cad.call('batch', calls=[dict(tool='object_update', args=dict(object=obj.name, name=name, visible=False)),
                            dict(tool='material_apply', args=dict(object=name, **look))])
    obj.name = name
    KNOWN[name] = obj.info
    return obj


def existing(name):
    return cad.Obj(dict(KNOWN[name], name=name)) if name in KNOWN else None


def loft(name, sections, look, degree=3, ruled=False):
    if name in KNOWN: return existing(name)
    # Through-point splines are interpolated cubics; their hidden Degree field
    # does not turn them into polylines. Explicit degree-one control polygons
    # keep thin glass sections straight and closed without cubic overshoot.
    curves = [cad.run('curve_control', points=p+[p[0]], degree=1)[0] if degree==1
              else cad.run('curve_through', points=p, closed=True)[0] for p in sections]
    obj = cad.loft(curves, operation='New body', straight=ruled)
    cad.delete(*curves)
    return done(obj, name, look)


def pipe(name, points, radius, look=CHROME, closed=False, degree=3):
    if name in KNOWN: return existing(name)
    c = (cad.run('curve_control', points=points+([points[0]] if closed else []), degree=1)[0] if degree==1
         else cad.run('curve_through', points=points, closed=closed)[0])
    obj = cad.run('pipe', slots={'Path curve': c.name}, radius_at_the_start=radius, radius_at_the_end=radius)[0]
    cad.delete(c)
    return done(obj, name, look)


def box(name, size, pos, look, radius=0):
    if name in KNOWN: return existing(name)
    if radius and radius >= min(size)/2:
        raise ValueError(f'{name}: fillet radius must be less than half the smallest dimension')
    obj = cad.box(width=size[0], depth=size[1], height=size[2], position_x=pos[0], position_y=pos[1], position_z=pos[2], operation='New body')
    if radius:
        edges = cad.call('solid_find', object=obj.name, kind='edges', limit=20)['hits']
        cad.fillet([dict(object=obj.name, edge=e['edge']) for e in edges], radius=radius)
    return done(obj, name, look)


def disc(name, center, radius, depth, look, axis='Y', rounded=0):
    if name in KNOWN: return existing(name)
    o = cad.cylinder(radius=radius, height=depth, operation='New body')
    if rounded:
        edges = cad.call('solid_find', object=o.name, kind='edges', type='Circle', limit=4)['hits']
        cad.fillet([dict(object=o.name, edge=e['edge']) for e in edges], radius=rounded)
    cad.rotate(o, 90, axis='X' if axis == 'Y' else 'Y')
    cad.move(o, x=center[0], y=center[1], z=center[2]-depth/2)
    return done(o, name, look)


def ring(name, center, radius, tube, look=CHROME, axis='Y'):
    if name in KNOWN: return existing(name)
    o = cad.torus(major_radius=radius, minor_radius=tube, position_x=center[0], position_y=center[1], position_z=center[2], operation='New body')
    cad.rotate(o, 90, axis='X' if axis == 'Y' else 'Y')
    return done(o, name, look)


def cushion(name, size, pos, look=LEATHER, rake=0):
    if name in KNOWN: return existing(name)
    o = cad.form_box(width=size[0], depth=size[1], height=size[2], faces_x=4, faces_y=4, faces_z=3)
    positions = {str(i): [x+pos[0]+rake*z, y+pos[1], z+pos[2]]
                 for i, (x,y,z) in enumerate(cad.form_info(o)['vertices'])}
    cad.form_op(o, 'set_vertices', positions=positions)
    return done(o, name, look)


def visible(on):
    cad.call('batch', calls=[dict(tool='object_update', args=dict(object=n, visible=on)) for n in KNOWN])


def capture(name, yaw=-45, pitch=15, distance=None):
    cad.call('view', yaw=yaw, pitch=pitch, fit=True, orthographic=True, grid=False, display='shaded')
    if distance: cad.call('view', distance=distance)
    cad.call('wait_idle')
    shot = cad.call('screenshot', viewport=True, max_width=1400)
    (OUT / (name+'.png')).write_bytes(base64.b64decode(shot['_image']['data']))


def checkpoint(stage):
    visible(True)
    capture(stage)
    cad.save(str(OUT / (stage+'.prism')))
    print(stage, len(KNOWN), 'objects saved', flush=True)
    visible(False)


def build_body():
    # Longitudinal cross-sections: half-width, crown, shoulder and sill. The
    # smooth section loft establishes the bonnet/fender/haunch character.
    rows = [(-2250,620,590,470),(-2070,770,670,500),(-1350,855,785,540),
            (-650,810,700,525),(150,805,690,520),(1250,855,800,550),
            (1900,785,725,530),(2150,685,630,490)]
    sections=[]
    for x,w,top,shoulder in rows:
        yz=[(0,top),(.5*w,top+5),(.85*w,top-8),(w,shoulder),(.98*w,340),(.85*w,235),(0,225),
            (-.85*w,235),(-.98*w,340),(-w,shoulder),(-.85*w,top-8),(-.5*w,top+5)]
        sections.append([[x,y,z] for y,z in yz])
    body = loft('Rosso coachwork', sections, PAINT)
    if 'Wheel openings complete' not in KNOWN:
        cutters=[disc('Temporary arch '+str(x),(x,0,340),382,2200,INK) for x in [-1350,1250]]
        cad.subtract(body, *cutters)
        for c in cutters: KNOWN.pop(c.name, None)
        # A real cabin recess reveals seats through the separate glass panels.
        cutter=box('Temporary cabin recess',(1820,1190,1100),(590,0,430),INK)
        cad.subtract(body,cutter);KNOWN.pop(cutter.name,None)
        # A named hidden construction datum records the completed cut stage on resume.
        marker=cad.run('curve_through',points=[[0,0,0],[0,0,1]],degree=1)[0]
        done(marker,'Wheel openings complete',INK)
    box('Undertray',(3400,1240,35),(0,0,205),INK,10)
    # Thin closed transverse strips make a real roof panel and glazing solids.
    def strip(x,w,z,t=3):
        return [[x,-w,z],[x,w,z],[x,w,z-t],[x,-w,z-t]]
    if 'Painted roof' not in KNOWN:
        roof=cad.form_box(width=1120,depth=1140,height=28,faces_x=8,faces_y=6,faces_z=2)
        positions={str(i):[530+x,y,1140+15*(1-(x/560)**2)+30*(1-(y/570)**2)+z]
                   for i,(x,y,z) in enumerate(cad.form_info(roof)['vertices'])}
        cad.form_op(roof,'set_vertices',positions=positions)
        done(roof,'Painted roof',PAINT)
    loft('Laminated windscreen',[strip(-395,651,685),strip(55,532,1155)],GLASS,degree=1,ruled=True)
    loft('Rear screen',[strip(1010,514,1155),strip(1540,659,755)],GLASS,degree=1,ruled=True)
    for side in [-1,1]:
        sections=[]
        for x,w,bottom,top in [(-375,650,685,705),(60,535,680,1155),(1015,520,770,1155),(1515,655,755,770)]:
            y=side*w
            base_y=side*650
            sections.append([[x,base_y,bottom],[x,y,top],[x,y-side*3,top],[x,base_y-side*3,bottom]])
        loft('Side glazing '+str(side),sections,GLASS,degree=1,ruled=True)
        for label,x in [('Front',-1350),('Rear',1250)]:
            # Filleted tires retain exact outside radius 340 and contact Z=0.
            disc(label+f' {side} tire',(x,side*755,340),340,195,RUBBER,rounded=30)
            disc(label+f' {side} wheel center',(x,side*850,340),221,15,INK)
            ring(label+f' {side} polished rim',(x,side*861,340),221,9)
            ring(label+f' {side} sidewall bead',(x,side*848,340),290,3,dict(color='#B4B4A8',roughness=.6))
    checkpoint('01-coachwork')


def build_details():
    for side in [-1,1]:
        # Window surrounds follow the raked cabin. Separate B pillars divide
        # door glass from the quarter lights, with a fine lower chrome rail.
        pipe('Window surround '+str(side),[[-390,side*653,685],[55,side*540,1160],[1015,side*523,1160],[1540,side*660,755]],7,degree=1)
        pipe('Window waist '+str(side),[[-390,side*653,685],[60,side*653,690],[1015,side*653,770],[1540,side*660,755]],6,degree=1)
        pipe('B pillar '+str(side),[[735,side*650,753],[800,side*542,1182]],13,PAINT,degree=1)
        pipe('Rocker trim '+str(side),[[-925,side*808,280],[100,side*811,275],[830,side*828,285]],8)
        pipe('Door shutline '+str(side),[[-370,side*786,692],[-460,side*804,530],[-450,side*807,350],[680,side*814,350],[790,side*813,525],[795,side*798,700]],2,INK,degree=1)
        box('Door handle '+str(side),(120,24,16),(650,side*817,650),CHROME,6)
        for label,x in [('Front',-1350),('Rear',1250)]:
            y=side*867
            # Ten sculpted magnesium spokes, grouped in one repeatable array.
            for i in range(10):
                a=math.tau*i/10
                pipe(label+f' {side} spoke {i}',[[x+58*math.cos(a),y,340+58*math.sin(a)],
                     [x+204*math.cos(a+.12),y,340+204*math.sin(a+.12)]],11,CHROME,degree=1)
            disc(label+f' {side} knockoff hub',(x,side*885,340),62,24,CHROME,rounded=6)
            for i in range(3):
                a=math.tau*i/3
                pipe(label+f' {side} spinner ear {i}',[[x+34*math.cos(a),side*904,340+34*math.sin(a)],
                    [x+93*math.cos(a+.3),side*904,340+93*math.sin(a+.3)]],10,CHROME,degree=1)
        # Headlamps are forward-facing, with separate bezels and convex lenses.
        for y,label,r in [(side*555,'Main',108),(side*305,'Driving',74)]:
            disc(label+' headlamp socket '+str(side),(-2258,y,574),r+13,34,INK,axis='X')
            ring(label+' headlamp bezel '+str(side),(-2280,y,574),r,7,CHROME,axis='X')
            disc(label+' headlamp lens '+str(side),(-2290,y,574),r-7,18,LAMP,axis='X',rounded=7)
        box('Front indicator '+str(side),(26,118,34),(-2270,side*540,407),AMBER,10)
        # Tall slim rear lenses and twin exhaust tips give the rear its own character.
        box('Rear lamp chrome '+str(side),(25,100,155),(2140,side*565,515),CHROME,12)
        box('Rear red lens '+str(side),(29,78,118),(2156,side*565,532),RED,12)
        ring('Exhaust tip '+str(side),(2130,side*530,253),35,5,CHROME,axis='X')
        disc('Exhaust darkness '+str(side),(2115,side*530,253),29,5,INK,axis='X')
        pipe('Mirror stalk '+str(side),[[-370,side*724,760],[-420,side*863,828]],9,CHROME,degree=1)
        cushion('Chrome mirror '+str(side),(105,55,65),(-430,side*875,810),CHROME)
        for i in range(4):
            pipe('Fender vent '+str(side)+' '+str(i),[[-805+i*38,side*808,595],[-780+i*38,side*815,470]],7,INK,degree=1)
    # Wide, low grille and a pair of wrapped chrome bumpers.
    box('Grille shadow',(28,1040,160),(-2242,0,390),INK,10)
    for i in range(9):
        box('Grille vertical '+str(i),(22,7,123),(-2263,(i-4)*102,405),CHROME,2)
    for z in [420,485]:
        pipe('Grille crossbar '+str(z),[[-2271,-482,z],[-2278,0,z],[-2271,482,z]],4)
    pipe('Front bumper',[[-2125,-755,335],[-2280,-640,335],[-2310,0,330],[-2280,640,335],[-2125,755,335]],20)
    pipe('Rear bumper',[[2030,-730,337],[2190,-600,337],[2225,0,332],[2190,600,337],[2030,730,337]],18)
    box('Rear number plate',(16,330,80),(2180,0,480),INK,6)
    box('Rear plate bright border',(12,346,96),(2169,0,472),CHROME,4)
    # Bonnet opening and restrained center badge.
    pipe('Bonnet seam',[[-2020,-490,670],[-1100,-565,766],[-500,-540,707],[-480,0,709],[-500,540,707],[-1100,565,766],[-2020,490,670]],2,INK)
    box('Nose badge',(25,45,9),(-2090,0,680),CHROME,3)
    checkpoint('02-exterior')


def build_interior():
    box('Cabin carpet',(1700,1150,22),(580,0,435),INK,8)
    cushion('Rear parcel shelf',(440,1170,65),(1290,0,735),INK)
    for side in [-1,1]:
        cushion('Seat base '+str(side),(450,420,105),(570,side*315,475))
        cushion('Seat back '+str(side),(95,420,445),(820,side*315,525),rake=.30)
        for i in [-1,0,1]:
            pipe('Seat piping '+str(side)+' '+str(i),[[430,side*315+i*87,565],[720,side*315+i*87,565]],3,dict(color='#704424',roughness=.6),degree=1)
        cushion('Door card '+str(side),(980,35,290),(400,side*580,450),LEATHER)
        box('Interior door pull '+str(side),(160,24,30),(310,side*554,650),CHROME,8)
    cushion('Dashboard upper',(240,1170,65),(-140,0,750),INK)
    box('Walnut fascia',(28,1100,145),(-8,0,680),dict(material='walnut',color='#73482B',roughness=.3,pattern='Wood',patternScale=80),10)
    for y,r in [(-380,64),(-210,48),(40,32),(150,32)]:
        disc('Instrument face '+str(y),(9,y,752),r,5,INK,axis='X')
        ring('Instrument bezel '+str(y),(16,y,752),r,3,CHROME,axis='X')
        pipe('Instrument needle '+str(y),[[21,y,752],[21,y+r*.5,752+r*.35]],1.5,dict(color='#F2E4CC'),degree=1)
    ring('Wood steering wheel',(120,-315,850),168,12,dict(material='walnut',color='#713D21',roughness=.3),axis='X')
    for i in range(3):
        a=math.tau*i/3
        pipe('Steering spoke '+str(i),[[120,-315,850],[120,-315+158*math.cos(a),850+158*math.sin(a)]],7,CHROME,degree=1)
    disc('Steering boss',(120,-315,850),35,28,INK,axis='X')
    box('Center console',(590,140,100),(400,0,465),INK,20)
    pipe('Gear lever',[[480,0,555],[445,0,690]],7,CHROME,degree=1)
    cushion('Gear knob',(48,48,48),(445,0,672),INK)
    checkpoint('03-complete')


def finish():
    visible(True)
    cad.select()
    capture('front-quarter',-48,17,5700)
    capture('side',0,0,5800)
    capture('rear-quarter',140,18,5700)
    cad.call('view',yaw=-48,pitch=17,distance=5700,target=[-30,0,610],grid=False)
    cad.call('select_tab',title='Solid')
    cad.save(str(MODEL))
    print('Native gallery model saved',flush=True)


def verify():
    before=cad.call('get_state')['objects']
    cad.call('file',action='open',path=str(MODEL))
    after=cad.call('get_state')['objects']
    assert len(after)==len(before),(len(before),len(after))
    infos=[r['result'] for r in cad.call('batch',calls=[dict(tool='object_info',args=dict(object=o['name'])) for o in after])]
    for o in infos:
        if o['kind']=='Solid':assert o['valid'] and o['volume']>0,o['name']
        if o['kind']=='Form':
            cage=cad.form_info(o['name'],summary=True)
            assert cage['closed'] and not cage['topology']['windingConflicts'] and not cage['topology']['nonManifoldEdges'],cage
    byname={o['name']:o for o in infos}
    for label,x in [('Front',-1350),('Rear',1250)]:
        for side in [-1,1]:
            o=byname[label+f' {side} tire']
            assert abs(o['min'][2])<.01,o
            assert all(abs(a-b)<.02 for a,b in zip(o['size'],[680,195,680])),o
            assert abs(o['center'][0]-x)<.01 and abs(o['center'][1]-side*755)<.01,o
    (OUT/'verification.json').write_text(json.dumps(dict(objects=infos,wheelbase=2600,track=1510,arch_radius=382,tire_radius=340),indent=2))
    print('Saved/reopened geometry and four-wheel placement verified',len(infos),flush=True)


def render():
    cad.call('view',yaw=-48,pitch=17,distance=5700,target=[-30,0,610],grid=False)
    cad.call('run_command',name='render',commit=False)
    cad.call('command_set',params={'Picture size':'HD  1280 × 720','Quality (samples per pixel)':128,
        'Light bounces':7,'Background':'White','Denoise final image':True,'Ground plane with shadows':True,'Start render':True})
    deadline=time.monotonic()+1200
    while time.monotonic()<deadline:
        s=cad.call('render_control',action='status')
        if s.get('error'):raise RuntimeError(s)
        if s.get('finished'):break
        time.sleep(1)
    assert s.get('finished'),s
    cad.call('render_control',action='save',path=str(OUT/'bellavista-render.png'))
    cad.call('render_control',action='close');cad.call('command_cancel')
    cad.save(str(MODEL))
    print('Render saved',flush=True)


if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--discovery',default=str(Path(os.environ.get('LOCALAPPDATA',''))/'Prism/automation.json'))
    p.add_argument('--phase',choices=['body','detail','interior','finish','verify','render','all'],default='all')
    p.add_argument('--resume',action='store_true')
    args=p.parse_args();OUT.mkdir(parents=True,exist_ok=True)
    bridge,log=study.connect_mcp(args.discovery)
    try:
        if not args.resume and args.phase in ['body','all']:cad.call('document_new')
        KNOWN.update({o['name']:o for o in cad.call('get_state')['objects']})
        for phase,fn in [('body',build_body),('detail',build_details),('interior',build_interior),('finish',finish),('verify',verify),('render',render)]:
            if args.phase in [phase,'all']:fn()
    finally:
        bridge.terminate();bridge.wait(timeout=10);log.close()
