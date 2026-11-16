"""Arc soap casting and two-part mold CAM, owner-authorized #136.

Canonical guides: docs/modeling/mold/README.md, docs/cam/README.md,
docs/interface/gallery/README.md. Native MCP solids, no Freeform or render.
"""
import argparse
import base64
import hashlib
import json
import demo_session as d
import build_pavilion as p
import build_toaster as t

cad, ROOT = d.cad, d.ROOT
OUT = ROOT / 'artifacts/arc-soap-mold'
MODEL = ROOT / 'gallery/models/arc-soap-mold-cam.prism'
SOAP = 'Arc soap - casting master'
HALVES = ['A - lower cavity', 'B - upper detail cavity']
SOAP_LOOK = dict(material='abs',color='#B8CCC0',roughness=.4,pattern='Plain',surface=dict(Coat=.15))
TOOL_LOOK = dict(material='6061',color='#B2BCC5',metal=.7,roughness=.4,pattern='Plain')


def checkpoint(name):
    cad.select(); cad.save(str(OUT/(name+'.prism')))
    print('Saved',name,flush=True)


def capture(name, **pose):
    cad.call('view',grid=False,display='shaded',**pose)
    cad.call('wait_idle',timeout_ms=5000)
    shot=cad.call('screenshot',viewport=False,max_width=1400,wait_ms=800)
    (OUT/name).write_bytes(base64.b64decode(shot['_image']['data']))


def geometry():
    cad.call('document_new')
    body=t.roundbox((-55,-35,13,110,70,24),10); body.rename(SOAP)
    # Shallow capsule recess and three short grip grooves, all accessible from +Z.
    for c1,c2,width in [((-23,0),(5,0),28)]+[((31,y),(37,y),5) for y in [-14,0,14]]:
        with cad.sketch('XY',offset=35.5) as sk: sk.slot(c1,c2,width)
        tool=cad.extrude(sk.obj,distance=5,operation='New body');cad.delete(sk.obj)
        cad.subtract(body,tool)
    cad.call('material_apply',object=SOAP,**SOAP_LOOK)
    info=cad.get(SOAP).info
    assert info['valid'] and info['volume']>100000,info
    checkpoint('01-soap-master')
    capture('arc-soap-master.png',yaw=30,pitch=46,target=[0,0,25],distance=230)
    block=p.box_raw((-75,-55,0,150,110,50));block.rename('Arc tooling blank')
    before=cad.get(block.name).info['volume']
    cad.run('mold',slots={'Mold block':block,'Part to cast':body},parting_direction='Z',split_at=50,explode_distance=40)
    halves=sorted([o for o in cad.objects() if o.kind=='Solid' and o.name!=SOAP],key=lambda o:o.info['min'][2])
    assert len(halves)==2
    assert abs(sum(cad.call('object_info',object=o.name)['volume'] for o in halves)-(before-info['volume']))<.01
    for o,name in zip(halves,HALVES):o.rename(name)
    checkpoint('02-exploded-mold')
    lower,upper=[cad.get(n) for n in HALVES]
    p.place_min(lower,'z',0)
    cad.run('rotate',slots={'Objects':upper},axis='X',angle=180,pivot='World origin')
    p.place_min(upper,'z',0);cad.move(upper,x=190)
    cad.move(body,x=95,y=-115,z=-13)
    cad.call('material_apply',object=SOAP,**SOAP_LOOK)
    for o,cx in [(lower,0),(upper,190)]:
        for x,y in [(-61,-41),(61,-41),(61,41),(-61,41)]:
            tool=cad.cylinder(radius=3,height=10,position_x=x+cx,position_y=y,position_z=17,operation='New body')
            cad.subtract(o,tool)
        cad.call('material_apply',object=o.name,**TOOL_LOOK)
        assert cad.get(o.name).info['valid']
    cad.call('workspace',id='make');cad.call('select_tab',title='Subtractive')
    checkpoint('03-cavity-up-tooling')
    capture('arc-soap-mold.png',yaw=22,pitch=52,target=[95,-25,10],distance=570)


def operation(command,name,slots=None,**params):
    made=cad.run(command,slots=slots or {},**params)
    assert len(made)==1,made
    made[0].rename(name)
    checkpoint(name[:2]+'-cam-operation')
    return made[0]


def cam():
    cad.call('file',action='open',path=str(OUT/'03-cavity-up-tooling.prism'))
    cad.call('wait_idle',timeout_ms=5000)
    paths=[]
    for i,(name,cx) in enumerate(zip(HALVES,[0,190])):
        label='A' if i==0 else 'B'
        cad.cam_setup(name,name=label+' - Arc soap cavity up',stock_allowance_xy=0,top_allowance=1,bottom_allowance=0,
                      tool_diameter=6,cutting_feed=650,plunge_feed=180,spindle_speed=12000,clearance_above_stock=8)
        paths.append(operation('cnc_face',label+'1 - Face tooling blank',tool_diameter=12,x_from=cx-75,x_to=cx+75,
                      y_from=-55,y_to=55,top_of_the_material_z=26.01,total_cutting_depth=1.01,depth_per_pass=1))
        edges=cad.call('solid_find',object=name,kind='edges',limit=150)['hits']
        holes=[dict(object=name,edge=e['edge']) for e in edges if e['type']=='Circle' and abs(e.get('radius',0)-3)<.001 and abs(e['center'][2]-25)<.001]
        assert len(holes)==4,holes
        paths.append(operation('cnc_drill',label+'2 - Peck drill registration bores',{'Holes (round faces or circular edges)':holes},
                      tool_diameter=6,drill_each_hole_as_deep_as_it_is=False,peck_depth_0_straight_in=2,
                      top_of_the_material_z=26.01,total_cutting_depth=9.01,cutting_feed=200,plunge_feed=120))
        paths.append(operation('cnc_rough',label+'3 - Rough cavity',{'Model to rough':name},
                      tool_diameter=6,roughing_stepover=55,stock_to_leave=.4,top_of_the_material_z=26.01,
                      total_cutting_depth=14.01,depth_per_pass=2))
        paths.append(operation('cnc_finish',label+'4 - Ball finish cavity',{'Model to finish':name},
                      tool_diameter=3,cutter='Ball nose',stepover=20,scan_angle=0 if i==0 else 90,top_of_the_material_z=26.01))
    cad.call('command_cancel');cad.call('workspace',id='make');cad.call('select_tab',title='Subtractive')
    cad.save(str(MODEL))
    cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    saved=json.loads(MODEL.read_text(encoding='utf-8'))
    (OUT/'saved-structure.json').write_text(json.dumps({k:v for k,v in saved.items() if k!='Objects'},indent=2),encoding='utf-8')
    cad.call('run_command',name='cam_review',commit=False)
    review=cad.call('command_set',params={})
    (OUT/'cam-review.json').write_text(json.dumps(review,indent=2),encoding='utf-8')
    print('Review',json.dumps(review),flush=True)
    cad.call('command_cancel')
    capture('arc-soap-mold-cam.png',yaw=22,pitch=56,target=[95,0,10],distance=540)
    infos=[cad.get(n).info for n in [SOAP]+HALVES]
    evidence=dict(date='2026-11-16',branch='codex/v0.4-illustrated-docs',objects=infos,saveReopened=True,
                  sha256=hashlib.sha256(MODEL.read_bytes()).hexdigest(),bytes=MODEL.stat().st_size,
                  method='Actual Windows MCP solid modeling, Create Mold and cavity-up named CAM setups. No Freeform or renderer; no test suite run.')
    (OUT/'verification.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print('Saved final CAM mold',flush=True)
    verify()


def verify():
    """Review both saved setups without regenerating paths; export geometry-only mold."""
    cad.call('command_cancel')
    cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    reviews={};stocks={}
    for label,name,grid in [('A',HALVES[0],1),('B',HALVES[1],1.5)]:
        cad.call('run_command',name='cam_select_setup',commit=False,
                 params={'Saved setup':label+' - Arc soap cavity up','Use setup':True})
        cad.call('command_cancel')
        cad.call('run_command',name='cam_review',commit=False,
                 params={'Setup sheet folder':str(OUT/label),'Save setup sheet':True})
        reviews[label]=cad.call('command_set',params={})
        assert not reviews[label].get('error'),reviews[label]
        cad.call('command_cancel')
        cad.call('run_command',name='cnc_stock_review',commit=False,slots={'Model':name},
                 params={'Program position':100,'Grid size':grid,'Holder diameter':12,
                         'Flute / holder clearance length':35,'Comparison tolerance':.35})
        stocks[label]=cad.call('command_set',params={'Run checks':True})
        assert not stocks[label].get('error'),stocks[label]
        assert any('Possible gouges: 0 moves' in row for row in stocks[label]['results']),stocks[label]
        capture('arc-soap-stock-'+label+'.png',yaw=22,pitch=56,target=[0 if label=='A' else 190,0,12],distance=310)
        cad.call('command_cancel')
    # Mold-only Save As preserves the full project; no CAM or master in this copy.
    cad.save(str(ROOT/'gallery/models/arc-soap-tooling.prism_mld'))
    paths=[o for o in cad.objects() if o.kind=='Toolpath']
    assert len(paths)==8
    d.hide(*paths)
    capture('arc-soap-mold.png',yaw=22,pitch=52,target=[95,-25,10],distance=570)
    for o in paths:cad.call('object_update',object=o.name,visible=True)
    # Leave the CAM document ready for reviewing either saved setup.
    cad.call('run_command',name='cnc_stock_review',commit=False,slots={'Model':HALVES[1]},
             params={'Program position':100,'Grid size':1.5,'Holder diameter':12,
                     'Flute / holder clearance length':35,'Comparison tolerance':.35})
    cad.call('command_set',params={'Run checks':True})
    capture('arc-soap-mold-cam.png',yaw=22,pitch=52,target=[95,-25,10],distance=570)
    cad.save(str(MODEL))
    cad.call('command_cancel')
    cad.call('file',action='open',path=str(MODEL));cad.call('wait_idle',timeout_ms=5000)
    saved=json.loads(MODEL.read_text(encoding='utf-8'))
    paths=[o for o in saved['Objects'] if o['Kind']=='Toolpath']
    assert len(paths)==8 and len(saved['CamSetups'])==2
    assert all(o['Visible'] and len(o['Asm']['Moves'])>0 for o in paths)
    evidence=dict(date='2026-11-16',branch='codex/v0.4-illustrated-docs',desktopSource='c9a5773',
                  objects=[cad.call('object_info',object=n) for n in [SOAP]+HALVES],saveReopened=True,
                  sha256=hashlib.sha256(MODEL.read_bytes()).hexdigest(),bytes=MODEL.stat().st_size,
                  setups=saved['CamSetups'],reviews=reviews,stockReviews=stocks,
                  operations=[dict(name=o['Name'],setup=o['Asm']['SetupName'],operation=o['Asm']['Operation'],
                                   tool=o['Asm']['Tool'],diameter=o['Asm']['ToolDia'],moves=len(o['Asm']['Moves'])//4) for o in paths],
                  method='Actual Windows MCP solid modeling, Create Mold, two cavity-up CAM setups, eight saved operations; native reopen and source freshness checked. No Freeform, renderer or test suite.',
                  limits='Static visual tooling study without gates/runners/ejectors. Sampled stock review retains a conservative finishing band, particularly around small details; no machine prove-out.')
    assert all(o['valid'] and o['volume']>0 for o in evidence['objects'])
    (OUT/'verification.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print('Verified both setups, eight paths and mold-only export',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--phase',choices=['geometry','cam','verify'],default='geometry')
    parser.add_argument('--discovery',default=str(ROOT/'artifacts/guitar-v2/automation.json'));a=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);d.OUT=OUT;b,l=d.connect(a.discovery)
    try:
        if a.phase=='geometry':geometry()
        elif a.phase=='cam':cam()
        else:verify()
    finally:b.terminate();b.wait(timeout=10);l.close()
