"""Arc desk lamp: owner-approved concept, native Fold/Flange construction, no Revolve.

python gallery/build_arc_lamp.py [--phase shade|structure|hardware|finish|all] [--resume]
Canonical guide: docs/interface/gallery/README.md; issue #130.
"""
import argparse
import math
import os
import json
import time
from pathlib import Path
import build_examples as study

cad=study.cad
ROOT=study.ROOT
OUT=ROOT/'artifacts/arc-lamp'
T,R,K,L=1.5,68.,.44,240.
METAL=dict(material='5052',color='#BCC3CA',metal=.95,roughness=.27,pattern='Brushed',patternScale=3,surface=dict(Anisotropy=.35,Coat=.08))
CHROME=dict(material='304',color='#D5DCE0',metal=1,roughness=.22)
DARK=dict(material='abs',color='#20272B',roughness=.6)
PARTS=[]
FLATS=[]
KNOWN={}

def style(obj,name,look=METAL,flat=False):
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=obj.name,name=name,visible=False)),dict(tool='material_apply',args=dict(object=name,**look))])
    obj.name=name;KNOWN[name]=obj.info
    (FLATS if flat else PARTS).append(name)
    return obj

def hide(name):cad.call('object_update',object=name.name if hasattr(name,"name") else name,visible=False)
def show():cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in PARTS])
def checkpoint(name):
    show();cad.view('iso',fit=True);cad.save(str(OUT/(name+'.prism')))
    print('Saved',name,flush=True)
    for n in PARTS:hide(n)

def flatten(name,flatname):
    before={o.name for o in cad.objects()}
    cad.flat_pattern(name,k_factor=K)
    new=[o for o in cad.objects() if o.name not in before]
    flat=next(o for o in new if o.kind=='Surface')
    style(flat,flatname,flat=True)
    for o in new:
        if o.kind=='Curve':
            o.rename(flatname+' bend lines');cad.call('object_update',object=o.name,color='#D78443');hide(o)
    return flat

def shade():
    a=math.pi*(R+K*T);w=a+20
    with cad.sketch('XY') as s:
        s.polyline([(-w/2,-L/2),(w/2,-L/2),(w/2,L/2),(-w/2,L/2)],closed=True)
        s.line((0,-L/2),(0,L/2),construction=True)
    s.obj.rename('Shade rolling blank')
    made=cad.run('fold',slots={'Flat sketch':s.obj.name},thickness=T,bend_radius=R,bend_angle=180,k_factor=K)
    body=next(o for o in made if o.kind=='Solid');style(body,'Rolled shade')
    original=next(o for o in made if o.kind=='Surface')
    p=cad.get(original.name).info
    assert abs(p['area']-w*L)<.02,p
    expected=T*L*(20+math.pi*(R+T/2))
    assert abs(cad.get(body.name).info['volume']-expected)<.02,expected
    cad.delete(*[o for o in made if o.kind!='Solid']);hide(s.obj)
    cad.run('rotate',slots={'Objects':body.name},axis='Y',angle=90,pivot='World origin')
    cad.move(body,x=-(R+T),z=310+a/2)
    plain=flatten(body.name,'Unperforated shade development')
    assert abs(cad.get(plain.name).info['area']-w*L)<.1
    cad.delete(plain);FLATS.remove(plain.name);KNOWN.pop(plain.name,None)
    for o in cad.objects():
        if o.name.startswith('Unperforated shade development bend lines'):cad.delete(o)
    checkpoint('01-rolled-shade')
    # Each edge has a genuine Flange hem, fused to its matching ten-mm tangent lip.
    for side in [-1,1]:
        with cad.sketch('XZ') as s:s.line((side*(R+T),310),(side*(R+T),300))
        hem=cad.flange(s.obj,thickness=T,bend_radius=1.5,width=L,centred_on_the_sketch_plane=True,
                       hem_length_at_the_end=7,hem_gap=.5,the_metal_lies='Left of the profile' if side<0 else 'Right of the profile')
        hide(s.obj);cad.union(body,hem)
    checkpoint('02-hemmed-shade')
    cutters=[]
    for angle in [-38,0,38]:
        theta=math.radians(angle)
        for y in [-88,-53,-18,17,52,87]:
            with cad.sketch('XY') as s:s.slot((0,-10),(0,10),5)
            cutter=cad.extrude(s.obj,distance=12,operation='New body');cad.delete(s.obj)
            cad.run('rotate',slots={'Objects':cutter.name},axis='Y',angle=angle,pivot='World origin')
            cad.move(cutter,x=(R-5)*math.sin(theta),y=y,z=310+(R-5)*math.cos(theta))
            cutters.append(cutter);hide(cutter)
    before=cad.get(body.name).info['volume'];cad.subtract(body,*cutters)
    removed=before-cad.get(body.name).info['volume'];expected=18*(100+math.pi*2.5**2)*T
    assert abs(removed-expected)<expected*.015,(removed,expected)
    flat=flatten(body.name,'Shade development')
    info=cad.get(flat.name).info
    assert info['wires']==info['faces']+18,info
    checkpoint('03-slotted-shade')

def structure():
    # Four feet around a clipped-corner plinth. Reliefs separate the bends.
    a=math.radians(35)*(4+K*T)
    outline=[(70,100),(70,125),(-70,125),(-70,100),(-85,85),(-110,85),(-110,-85),(-85,-85),(-70,-100),(-70,-125),(70,-125),(70,-100),(85,-85),(110,-85),(110,85),(85,85)]
    with cad.sketch('XY') as s:
        s.polyline(outline,closed=True)
        for x in [-85-a/2,85+a/2]:s.line((x,-150),(x,150),construction=True)
        for y in [-100-a/2,100+a/2]:s.line((-150,y),(150,y),construction=True)
        for x in [57,73]:
            for y in [-13,13]:s.circle((x,y),2.5)
        s.circle((-45,65),4)
    s.obj.rename('Plinth blank with corner reliefs')
    made=cad.run('fold',slots={'Flat sketch':s.obj.name},thickness=T,bend_radius=4,bend_angle=35,
                 angle_of_each_bend='-35,-35,-35,-35',k_factor=K,fixed_region='The region over the sketch origin')
    base=next(o for o in made if o.kind=='Solid');style(base,'Folded plinth')
    lift=-cad.get(base.name).info['min'][2];cad.move(base,z=lift)
    flat=next(o for o in made if o.kind=='Surface');style(flat,'Plinth development',flat=True)
    for o in made:
        if o.kind=='Curve':o.rename('Plinth bend lines');hide(o)
    hide(s.obj)
    f=lift+T
    with cad.sketch('XZ') as s:s.polyline([(80,f),(45,f),(45,120),(0,148),(0,278),(60,313)])
    s.obj.rename('Stepped support profile')
    stem=cad.flange(s.obj,thickness=T,bend_radius=4,width=42,centred_on_the_sketch_plane=True,hem_length_at_the_end=10,hem_gap=.5,the_metal_lies='Right of the profile')
    style(stem,'Stepped hemmed support');hide(s.obj);flatten(stem.name,'Support development')
    with cad.sketch('XY') as s:s.polyline([(35,-120),(65,-120),(65,120),(35,120)])
    holder=cad.flange(s.obj,thickness=T,bend_radius=3,width=45,the_metal_lies='Right of the profile')
    cad.move(holder,z=300);style(holder,'Shade mounting yoke');hide(s.obj);flatten(holder.name,'Yoke development')
    checkpoint('04-structure')

def cylinder(name,p,r,h,axis='Z',look=CHROME):
    o=cad.cylinder(radius=r,height=h,operation='New body')
    if axis=='Y':
        cad.rotate(o,90,axis='X');cad.move(o,x=p[0],y=p[1],z=p[2]-h/2)
    else:cad.move(o,x=p[0],y=p[1],z=p[2])
    return style(o,name,look)

def hardware():
    for side in [-1,1]:
        for z,x in [(320,52),(338,62)]:
            cylinder(f'Shade fastener {side} {z}',(x,side*124,z),3.8,5,'Y')
    base=cad.get('Folded plinth').info
    for x in [57,73]:
        for y in [-13,13]:cylinder(f'Base fastener {x} {y}',(x,y,base['max'][2]+1),3.5,2)
    # A slender diffuser and two retainers complete the interior of the shade.
    o=cad.box(width=20,depth=204,height=6,position_x=0,position_y=0,position_z=326,operation='New body')
    style(o,'Warm diffuser',dict(material='pc',color='#FFE3A6',roughness=.4))
    for y in [-105,105]:
        o=cad.box(width=25,depth=5,height=5,position_x=0,position_y=y,position_z=326,operation='New body');style(o,'Diffuser retainer '+str(y))
    cad.call('run_command',name='lights',commit=False)
    for y in [-70,0,70]:
        cad.call('command_set',params={'Add lamp (point)':True})
        cad.call('command_set',params={'Position X':0,'Position Y':y,'Position Z':322,'Power':1500,'Colour temperature':2700,'Lamp size (radius)':6})
    cad.call('command_commit')
    checkpoint('05-complete')

def finish(render=True):
    for name,target in [('Shade development',(260,-140)),('Plinth development',(260,180)),('Support development',(700,-140)),('Yoke development',(700,180))]:
        info=cad.get(name).info
        names=[name]+[o.name for o in cad.objects() if o.name.startswith(name+' bend lines') or (name=='Plinth development' and o.name=='Plinth bend lines')]
        cad.move(names,x=target[0]-info['min'][0],y=target[1]-info['min'][1],z=-info['min'][2])
        for n in names:hide(n)
    show();cad.select();cad.call('view',yaw=-38,pitch=18,fit=True,grid=False)
    cad.call('select_tab',title='Sheet Metal')
    cad.call('run_command',name='render',commit=False)
    cad.call('command_set',params={'Lighting':'Studio (soft white boxes)','Background':'White','Denoise final image':True,'Ground plane with shadows':True})
    cad.call('command_cancel')
    # The development drawings stay hidden for the hero; all are in the file.
    study.verify_and_save('arc-desk-lamp',-38,18,render=False,distance=900)
    if render:
        cad.call('run_command',name='render',commit=False)
        cad.call('command_set',params={'Picture size':'HD  1280 × 720','Quality (samples per pixel)':128,
            'Light bounces':6,'Background':'White','Denoise final image':True,'Ground plane with shadows':True,'Start render':True})
        deadline=time.monotonic()+600
        while time.monotonic()<deadline:
            status=cad.call('render_control',action='status')
            if not status:raise RuntimeError('The renderer has no active job; inspect the application status before retrying.')
            if status.get('error'):raise RuntimeError(status)
            if status.get('finished'):break
            time.sleep(1)
        assert status.get('finished'),status
        cad.call('render_control',action='save',path=str(OUT/'arc-desk-lamp-render.png'))
        cad.call('render_control',action='close');cad.call('command_cancel')
        cad.save(str(ROOT/'gallery/models/arc-desk-lamp.prism'))
        print('Lamp render saved',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--phase',choices=['shade','structure','hardware','finish','all'],default='all');p.add_argument('--resume',action='store_true');p.add_argument('--no-render',action='store_true')
    p.add_argument('--discovery',default=str(OUT/'verified-automation.json'));args=p.parse_args();OUT.mkdir(parents=True,exist_ok=True)
    candidate=ROOT/'artifacts/arc-lamp/bin/Prism.Desktop.exe'
    if candidate.exists():os.environ.setdefault('PRISM_EXE',str(candidate))
    bridge_binary=ROOT/'artifacts/arc-lamp/mcp/Prism.Mcp.dll'
    if bridge_binary.exists():os.environ.setdefault('PRISM_MCP',str(bridge_binary))
    bridge,log=study.connect_mcp(args.discovery)
    try:
        if not args.resume:cad.call('document_new')
        else:
            KNOWN.update({o['name']:o for o in cad.call('get_state')['objects']})
            PARTS.extend(n for n,o in KNOWN.items() if o['kind']=='Solid')
            FLATS.extend(n for n,o in KNOWN.items() if o['kind']=='Surface')
        for phase,fn in [('shade',shade),('structure',structure),('hardware',hardware)]:
            if args.phase in [phase,'all']:fn()
        if args.phase in ['finish','all']:finish(not args.no_render)
    finally:bridge.terminate();bridge.wait(timeout=10);log.close()

