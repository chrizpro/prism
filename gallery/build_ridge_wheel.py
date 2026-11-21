"""Ridge all-terrain tire and six split-spoke wheel, approved #131.

Actual Circular Pattern features, exact solids and retained seed geometry.
Canonical specification: docs/interface/gallery/README.md.
"""
import argparse
import demo_session as d
cad,ROOT,OUT=d.cad,d.ROOT,d.OUT
RUBBER=dict(material='silicone',color='#24272B',roughness=.67,surface=dict(Bump=.09))
SILVER=dict(material='6061',color='#BFC6CE',metal=1,roughness=.26,pattern='Brushed',patternScale=2,surface=dict(Anisotropy=.35,Coat=.12))
BRIGHT=dict(material='6061',color='#DDE3E9',metal=1,roughness=.18)
DARK=dict(material='304',color='#525A64',metal=1,roughness=.36)

def revolve_section(points,name,look):
    with cad.sketch('XZ') as sk:sk.polyline(points,closed=True)
    body=cad.revolve(sk.obj,axis='World Z',operation='New body')
    d.hide(sk.obj);sk.obj.rename(name+' section')
    return d.finish(body,name,**look)

def patterned(seed,name,count):
    copies=cad.circular_pattern(seed,count=count,axis='Z axis')
    allparts=[seed]+copies
    assert len(allparts)==count,(name,len(allparts))
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,name=f'{name} {i+1:02d}',visible=False)) for i,o in enumerate(allparts)])
    for i,o in enumerate(allparts):o.name=f'{name} {i+1:02d}'
    print('Patterned',name,count,flush=True)
    return allparts

def seed_block(poly,root,top,name):
    with cad.sketch('YZ',offset=root) as sk:sk.polyline(poly,closed=True)
    body=cad.extrude(sk.obj,distance=top-root,operation='New body');cad.delete(sk.obj)
    body.rename(name)
    cad.move(body,x=root-body.info['min'][0]);cad.select()
    edges=cad.call('solid_find',object=name,kind='edges',limit=60)['hits']
    picks=[dict(object=name,edge=e['edge']) for e in edges if abs(e['center'][0]-top)<.02]
    cad.chamfer(picks,distance=.9)
    assert body.refresh().info['valid'] and body.info['volume']>0,name
    return body

def slit(body,points,start,depth=8):
    with cad.sketch('YZ',offset=start) as sk:sk.polyline(points,closed=True)
    tool=cad.extrude(sk.obj,distance=depth,operation='New body');cad.delete(sk.obj);cad.subtract(body,tool)
    assert body.refresh().info['valid'] and body.info['volume']>0,body.name

def carcass():
    with cad.sketch('XZ') as sk:
        sk.line((215,-97),(218,-112))
        sk.arc((218,-112),(275,-127),(337,-110))
        sk.arc((337,-110),(352,0),(337,110))
        sk.arc((337,110),(275,127),(218,112))
        sk.line((218,112),(215,97));sk.line((215,97),(224,92))
        sk.arc((224,92),(275,110),(320,103))
        sk.arc((320,103),(335,0),(320,-103))
        sk.arc((320,-103),(275,-110),(224,-92))
        sk.line((224,-92),(215,-97))
    body=cad.revolve(sk.obj,axis='World Z',operation='New body')
    d.hide(sk.obj);sk.obj.rename('Tire radial section')
    return d.finish(body,'Ridge tire carcass',**RUBBER)

def tire(resume=False):
    if not resume:cad.call('document_new')
    if not resume:carcass()
    d.checkpoint('ridge-carcass')
    poly=[(-25,-45),(0,-45),(30,0),(0,45),(-25,45),(5,0)]
    seed=seed_block(poly,345,359.8,'Chevron seed')
    for offset in [11,21]:
        pts=[(offset-.667*abs(z),z) for z in [-38,0,38]]+[(offset-1.3-.667*abs(z),z) for z in [38,0,-38]]
        slit(seed,pts,356.8)
    cad.call('material_apply',object=seed.name,**RUBBER);patterned(seed,'Chevron tread',32)
    d.checkpoint('ridge-chevron-pattern')
    for side in [-1,1]:
        poly=[(-17,side*54),(14,side*59),(22,side*83),(-8,side*87)]
        seed=seed_block(poly,340,354,'Intermediate seed')
        for off in [-3,7]:slit(seed,[(off-1,side*60),(off+1,side*60),(off+7,side*80),(off+5,side*80)],351)
        cad.rotate(seed,5.625,axis='Z')
        cad.call('material_apply',object=seed.name,**RUBBER);patterned(seed,'Intermediate '+('L' if side<0 else 'R'),32)
        poly=[(-21,side*91),(13,side*91),(23,side*116),(-11,side*121)]
        seed=seed_block(poly,323,344,'Shoulder seed')
        slit(seed,[(-4,side*96),(-2.5,side*96),(5,side*115),(3.5,side*115)],341)
        cad.call('material_apply',object=seed.name,**RUBBER);patterned(seed,'Shoulder '+('L' if side<0 else 'R'),32)
        d.checkpoint('ridge-tread-'+str(side))
    ribs()

def ribs():
    for side in [-1,1]:
        for r in [237,244,304,312]:
            z=side*(118.5 if r<250 else 124)
            ring=cad.torus(major_radius=r,minor_radius=.9,position_z=z,operation='New body')
            d.finish(ring,f'Sidewall rib {side} {r}',**RUBBER)
    solids=[o for o in cad.objects() if o.kind=='Solid']
    cad.select()
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,visible=True)) for o in solids])
    cad.call('view',yaw=-25,pitch=24,fit=True,grid=False)
    d.checkpoint('ridge-tire')

def wheel():
    d.hide(*[o for o in cad.objects() if o.kind=='Solid'])
    # The barrel, hub and spoke solids overlap at their joints as a visual assembly.
    revolve_section([(212,-103),(226,-103),(230,-98),(230,-88),(220,-83),(213,-65),(205,-35),(205,80),(219,88),(230,91),(230,100),(226,104),(212,104),(201,82),(197,-35),(205,-70)],'Alloy barrel and rim',SILVER)
    revolve_section([(214,-104),(227,-104),(231,-100),(231,-95),(226,-94),(214,-97)],'Machined outer lip',BRIGHT)
    revolve_section([(0,-54),(48,-54),(65,-61),(79,-68),(79,-82),(68,-86),(49,-75),(0,-75)],'Six-lug wheel hub',SILVER)
    # Six Y-spoke sectors; stem is broad at the root, forks open toward the rim.
    poly=[(66,-17),(100,-20),(197,-66),(215,-66),(214,-43),(133,-5),(133,5),(214,43),(215,66),(197,66),(100,20),(66,17)]
    # Face lies toward negative Z. Concavity varies with radial distance.
    with cad.sketch('XY') as sk:sk.polyline(poly,closed=True)
    spoke=cad.extrude(sk.obj,distance=16,operation='New body');cad.delete(sk.obj);spoke.rename('Split spoke seed')
    edges=cad.call('solid_find',object=spoke.name,kind='edges',limit=80)['hits']
    top=[dict(object=spoke.name,edge=e['edge']) for e in edges if abs(e['center'][2]-16)<.02]
    cad.fillet(top,radius=1.8)
    # Long recessed pockets in the two branches, exact lofted cutting tools.
    for sign in [-1,1]:
        p=[(116,sign*13),(194,sign*51),(202,sign*48),(130,sign*12)]
        for z,reverse in [(0,False),(16,True)]:
            with cad.sketch('XY',offset=z) as sk:sk.polyline(p,closed=True)
            cutter=cad.extrude(sk.obj,distance=3,reverse_direction=reverse,operation='New body');cad.delete(sk.obj);cad.subtract(spoke,cutter)
    cad.rotate(spoke,13,axis='Y');cad.move(spoke,z=-66)
    cad.call('material_apply',object=spoke.name,**SILVER);patterned(spoke,'Split spoke',6)
    d.checkpoint('ridge-six-spoke-wheel')
    hub=cad.get('Six-lug wheel hub')
    hole=cad.cylinder(radius=8.5,height=40,position_x=58,position_y=0,position_z=-100,operation='New body')
    # Patterned cutting tools go through the hub; retain no fake surface circles.
    tools=patterned(hole,'Lug bore tool',6)
    cad.subtract(hub,*tools)
    cap=cad.cylinder(radius=42,height=5,position_z=-81,operation='New body');d.finish(cap,'Machined hub cap',**BRIGHT)
    nut=cad.cylinder(radius=6.4,height=10,position_x=58,position_z=-83,operation='New body');cad.call('material_apply',object=nut.name,**DARK);patterned(nut,'Recessed lug nut',6)
    # Finished object stands upright with axle along Y and tread on the ground.
    solids=[o for o in cad.objects() if o.kind=='Solid']
    cad.run('rotate',slots={'Objects':[o.name for o in solids]},axis='X',angle=90,pivot='World origin')
    cad.move([o.name for o in solids],z=361)
    reports=cad.call('batch',calls=[dict(tool='object_info',args=dict(object=o.name)) for o in solids])
    bottom=min(r['result']['min'][2] for r in reports)
    cad.move([o.name for o in solids],z=-bottom)
    cad.select()
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,visible=True)) for o in solids])
    d.checkpoint('ridge-assembled')

def finish(render=False):
    cad.select()
    solids=[o for o in cad.objects() if o.kind=='Solid']
    cad.call('batch',calls=[dict(tool='object_update',args=dict(object=o.name,visible=True)) for o in solids])
    reports=cad.call('batch',calls=[dict(tool='object_info',args=dict(object=o.name)) for o in solids])
    invalid=[(r['result']['name'],r['result']['valid'],r['result']['volume']) for r in reports if not r['result']['valid'] or r['result']['volume']<=0]
    assert not invalid,invalid
    cad.call('view',yaw=128,pitch=20,fit=True,grid=False)
    cad.call('select_tab',title='Solid')
    path=ROOT/'gallery/models/ridge-all-terrain.prism';cad.save(str(path))
    if render:d.render('ridge-all-terrain',128,20,1500,128);cad.save(str(path))
    print('Verified',len(solids),'solids',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--phase',choices=['tire','ribs','wheel','finish','all'],default='all');p.add_argument('--render',action='store_true');p.add_argument('--resume',action='store_true');args=p.parse_args()
    b,l=d.connect(OUT/'tire-automation.json')
    try:
        if args.phase in ['tire','all']:
            if args.resume:cad.call('file',action='open',path=str(OUT/'ridge-carcass.prism'))
            tire(args.resume)
        if args.phase=='ribs':
            cad.call('file',action='open',path=str(OUT/'ridge-tread-1.prism'));ribs()
        if args.phase in ['wheel','all']:wheel()
        if args.phase in ['finish','all']:finish(args.render)
    finally:b.terminate();b.wait(timeout=10);l.close()
