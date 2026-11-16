"""Horizon H4 utility helicopter, owner-authorized #133.

Native solid assembly and viewport evidence; no renderer or test suite.
Canonical content scope: docs/interface/gallery/README.md.
"""
import argparse
import base64
import json
import math
from pathlib import Path
import demo_session as d
import build_pavilion as p

cad,ROOT=d.cad,d.ROOT
OUT=ROOT/'artifacts/helicopter'
MODEL=ROOT/'gallery/models/horizon-h4.prism'
PARTS=[]
WHITE=dict(material='6061',color='#E5EBE8',metal=.25,roughness=.32,surface=dict(Coat=.35))
TEAL=dict(material='6061',color='#087B86',metal=.3,roughness=.29,surface=dict(Coat=.45))
GLASS=dict(material='glass',color='#173745',metal=.32,roughness=.18,pattern='Plain',surface=dict(Coat=.55,Transmission=.12,Ior=1.48))
DARK=dict(material='6061',color='#273139',metal=.5,roughness=.4)
SILVER=dict(material='304',color='#BAC6CA',metal=.85,roughness=.3)
RED=dict(material='abs',color='#C43135',roughness=.25,surface=dict(Coat=.6))
GREEN=dict(material='abs',color='#24A47E',roughness=.25,surface=dict(Coat=.6))
ROWS=[(-285,2,5,194),(-273,35,40,193),(-245,66,77,195),(-195,86,98,202),
      (-110,94,104,206),(0,90,96,212),(110,65,74,230),(185,30,42,255),(218,12,20,266)]

def done(o,name,look):
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,name=name,visible=False)),dict(tool='material_apply',args=dict(object=name,**look))])
    o.name=name;PARTS.append(name);return o

def rotate(o,angle,axis):
    cad.run('rotate',slots={'Objects':o},axis=axis,angle=angle,pivot='World origin')

def copy(o):
    return cad.run('move',slots={'Objects':o},x=0,y=0,z=0,create_copy=True)[0]

def loft_rows(rows):
    curves=[]
    for x,w,h,z in rows:
        pts=[[x,w*math.cos(a*math.tau/16),z+h*math.sin(a*math.tau/16)] for a in range(16)]
        curves.append(cad.run('curve_through',points=pts,closed=True)[0])
    o=cad.loft(curves,operation='New body',straight=False);cad.delete(*curves)
    return o

def rod(name,a,b,r,look=DARK):
    delta=[b[i]-a[i] for i in range(3)];length=math.sqrt(sum(v*v for v in delta))
    o=cad.cylinder(radius=r,height=length,operation='New body')
    rotate(o,math.degrees(math.acos(delta[2]/length)),'Y');rotate(o,math.degrees(math.atan2(delta[1],delta[0])),'Z')
    cad.move(o,x=a[0],y=a[1],z=a[2]);return done(o,name,look)

def tube(name,pts,r,look=DARK,control=False):
    path=cad.run('curve_control' if control else 'curve_through',points=pts,**({'degree':3} if control else {}))[0]
    o=cad.run('pipe',slots={'Path curve':path},radius_at_the_start=r,radius_at_the_end=r)[0]
    cad.delete(path);return done(o,name,look)

def ellipsoid(name,center,radii,look):
    o=cad.sphere(radius=1,operation='New body')
    cad.run('scale',slots={'Objects':o},scale_type='Per axis (stretch)',factor_x=radii[0],factor_y=radii[1],factor_z=radii[2],pivot='World origin')
    cad.move(o,x=center[0],y=center[1],z=center[2]);return done(o,name,look)

def show():
    cad.select();cad.call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=True)) for n in PARTS])

def checkpoint(stage):
    cad.select();cad.save(str(OUT/(stage+'.prism')));print('Saved',stage,len(PARTS),'parts',flush=True)

def glazing(master,body,name,bounds):
    o=copy(master);cad.scale(o,1.013)
    tool=p.box_raw(bounds)
    # Round the clipping volume, giving each side pane a restrained corner radius.
    if name!='Wraparound cockpit glazing':
        edges=cad.call('solid_find',object=tool.name,kind='edges',limit=20)['hits']
        cad.fillet([dict(object=tool.name,edge=e['edge']) for e in edges],radius=5)
    cad.intersect(o,tool)
    cad.run('subtract',slots={'Body to cut from':body,'Tool bodies':[o]},keep_tool_bodies=True)
    return done(o,name,GLASS)

def cabin():
    master=loft_rows(ROWS);master.rename('Cabin construction envelope');d.hide(master)
    body=copy(master);done(body,'01 Pearl cabin shell',WHITE)
    canopy=glazing(master,body,'Wraparound cockpit glazing',(-295,-130,200,230,260,150))
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        glazing(master,body,label+' cabin window',(-47,-130 if side<0 else 64,218,119,66,65))
        glazing(master,body,label+' rear quarter window',(86,-120 if side<0 else 40,235,57,80,48))
        rod(label+' flush door handle',(22,side*85.5,205),(46,side*83.5,207),1.8,DARK)
    cad.delete(master)
    spine=copy(canopy);cad.scale(spine,1.006)
    cad.intersect(spine,p.box_raw((-295,-2.6,200,231,5.2,150)))
    done(spine,'Pearl windshield centre post',WHITE)
    # Separate the windshield panes around the centre frame.
    cad.subtract(canopy,p.box_raw((-300,-2.5,199,240,5,160)))
    tube('Pearl cockpit rear arch',[(-64,96*math.cos(t*math.pi/12),208+105*math.sin(t*math.pi/12)) for t in range(13)],2.2,WHITE)
    done(loft_rows([(-80,8,9,310),(-55,35,27,321),(40,48,35,321),(125,37,31,316),(185,8,12,299)]),'Teal turbine cowling',TEAL)
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        intake=p.box_raw((-8,side*46-1.5,317,80,3,19));done(intake,label+' engine intake',DARK)
        exhaust=rod(label+' hollow exhaust',(135,side*30,330),(194,side*30,334),11,SILVER)
        bore=rod('Temporary exhaust bore',(130,side*30,329.66),(199,side*30,334.34),8,DARK)
        cad.subtract(exhaust,bore);PARTS.remove(bore.name)
    checkpoint('01-cabin-and-powerplant')

def tail():
    done(loft_rows([(144,29,29,262),(212,24,26,278),(350,18,20,305),(510,12,14,327),(628,8,11,340)]),'Tapered teal tail boom',TEAL)
    fin=p.profile('XZ',[(550,323),(632,320),(686,467),(650,474),(601,383)],14);p.place_min(fin,'y',-7)
    done(fin,'Swept teal vertical fin',TEAL)
    stab=p.profile('XY',[(470,-133),(515,-137),(553,-30),(553,30),(515,137),(470,133),(500,0)],5)
    p.place_min(stab,'z',319);done(stab,'Pearl horizontal stabilizer',WHITE)
    rod('Tail rotor gearbox',(625,-38,352),(625,9,352),14,SILVER)
    for i,angle in enumerate([32,212]):
        o=p.profile('XZ',[(11,-7),(95,-9),(105,-3),(105,5),(25,11),(11,7)],3)
        rotate(o,angle,'Y');cad.move(o,x=625,y=-35,z=352);done(o,f'Tail rotor blade {i+1}',DARK)
    checkpoint('02-tail-and-tail-rotor')

def gear():
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        pts=[(-255,side*115,49),(-247,side*115,18),(-219,side*115,6),(-160,side*115,6),(100,side*115,6),(180,side*115,6),(211,side*115,17)]
        tube(label+' curved landing skid',pts,6,DARK,True)
    for label,x,h in [('Forward',-120,133),('Rear',105,183)]:
        tube(label+' landing gear cross-strut',[(x,-115,18),(x,-80,62),(x,-43,h),(x,43,h),(x,80,62),(x,115,18)],5.5,WHITE,True)
    checkpoint('03-landing-gear')

def rotor():
    rod('Main rotor mast',(-30,0,338),(-30,0,410),8,SILVER)
    ellipsoid('Main rotor head',(-30,0,412),(30,30,10),SILVER)
    for i,angle in enumerate([22,112,202,292]):
        blade=loft_rows([(24,13,3,0),(110,19,3,0),(445,14,2,0),(510,8,1.4,0)])
        rotate(blade,angle,'Z');cad.move(blade,x=-30,z=412);done(blade,f'Main rotor blade {i+1}',DARK)
    ellipsoid('Port navigation light',(-16,-84,250),(4,4,4),RED)
    ellipsoid('Starboard navigation light',(-16,84,250),(4,4,4),GREEN)
    ellipsoid('Anti-collision beacon',(125,0,350),(5,5,6),RED)
    checkpoint('04-complete-exterior')

def finish(assembled=False):
    show()
    if not assembled:cad.run('make_part',slots={'Bodies':PARTS})
    cad.call('wait_idle',timeout_ms=5000)
    cad.call('workspace',id='assemble')
    cad.call('select_tab',title='Assemble')
    cad.call('view',yaw=-38,pitch=24,fit=True,grid=False,display='shaded');cad.call('view',distance=1600)
    cad.save(str(MODEL));cad.call('file',action='open',path=str(MODEL))
    cad.call('wait_idle',timeout_ms=5000)
    cad.call('workspace',id='assemble');cad.call('select_tab',title='Assemble')
    cad.call('view',yaw=-38,pitch=24,fit=True,grid=False,display='shaded');cad.call('view',distance=1600)
    cad.call('wait_idle',timeout_ms=5000)
    info=[r['result'] for r in cad.call('batch',calls=[dict(tool='object_info',args=dict(object=n)) for n in PARTS])]
    assert all(r['valid'] and r['volume']>0 for r in info),[(r['name'],r['valid']) for r in info]
    saved=json.loads(MODEL.read_text(encoding='utf-8'))['Objects']
    assert len(saved)==len(PARTS) and all(o.get('Asm',{}).get('$t')=='component' for o in saved)
    assert sum(bool(o['Asm'].get('Grounded')) for o in saved)==1
    (OUT/'verification.json').write_text(json.dumps(dict(parts=len(info),valid=True,saveReopened=True,objects=info),indent=2))
    # Whole-window capture includes the active document even when other tabs exist.
    shot=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
    (OUT/'horizon-h4-prism.png').write_bytes(base64.b64decode(shot['_image']['data']))
    print('Finished helicopter:',len(info),'valid assembly components',flush=True)

if __name__=='__main__':
    # Keep this entry point on the current owner-requested exterior revision.
    import runpy
    runpy.run_module('refine_helicopter',run_name='__main__')
