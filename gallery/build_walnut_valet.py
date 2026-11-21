"""Owner-approved walnut CNC study (#131). See docs/interface/gallery/README.md.

Native modeling, saved toolpaths and stock review through Prism's actual MCP.
"""
import argparse
import json
import math
from pathlib import Path
import demo_session as d
cad,ROOT,OUT=d.cad,d.ROOT,d.OUT
NAME='Walnut valet tray'
POCKETS=[
    [(24,25),(77,16),(132,24),(150,50),(140,86),(130,119),(140,157),(123,183),(69,193),(28,179),(17,130),(17,65)],
    [(171,24),(222,17),(277,27),(284,60),(268,88),(220,95),(169,84),(159,59)],
    [(167,117),(213,110),(267,116),(282,133),(273,151),(234,162),(190,154),(161,139)],
]

def edge_at(obj,z):
    edges=cad.call('solid_find',object=obj.name,kind='edges',limit=150)['hits']
    return [dict(object=obj.name,edge=e['edge']) for e in edges if abs(e['center'][2]-z)<.02]

def geometry():
    cad.call('document_new')
    with cad.sketch('XY') as s:s.rect((0,0),(300,210))
    body=cad.extrude(s.obj,distance=32,operation='New body');body.rename(NAME)
    edges=cad.call('solid_find',object=body.name,kind='edges',direction=[0,0,1],limit=12)['hits']
    cad.fillet([dict(object=body.name,edge=e['edge']) for e in edges],radius=20)
    d.hide(s.obj)
    cad.fillet(edge_at(body,32),radius=2.5)
    for i,points in enumerate(POCKETS):
        outline=cad.run('curve_through',points=[[x,y,32] for x,y in points],closed=True)[0]
        outline.rename(f'Pocket {i+1} machining boundary')
        tool=cad.extrude(outline,distance=18,reverse_direction=True,operation='New body')
        cad.subtract(body,tool);d.hide(outline)
        # Round each new pocket's bottom before making the next one.
        bottoms=edge_at(body,14)
        if bottoms:cad.fillet(bottoms,radius=8)
        d.checkpoint(f'walnut-pocket-{i+1}')
    mouths=edge_at(body,32)
    # Outer rim is already rounded; remaining horizontal loops are pocket mouths.
    if mouths:cad.fillet(mouths,radius=1.5)
    d.checkpoint('walnut-rounded-pockets')
    return decoration()

def decoration():
    body=cad.get(NAME)
    for i in range(5):
        a,b,c=(185+8*i,177+6*i),(226+5*i,179+5*i),(278,172+6*i)
        path=[];left=[];right=[]
        for j in range(31):
            t=j/30
            x=(1-t)**2*a[0]+2*(1-t)*t*b[0]+t*t*c[0]
            y=(1-t)**2*a[1]+2*(1-t)*t*b[1]+t*t*c[1]
            dx=2*((1-t)*(b[0]-a[0])+t*(c[0]-b[0]));dy=2*((1-t)*(b[1]-a[1])+t*(c[1]-b[1]));l=math.hypot(dx,dy)
            left.append([x-2.1*dy/l,y+2.1*dx/l,32]);right.append([x+2.1*dy/l,y-2.1*dx/l,32])
        pts=left+right[::-1]
        curve=cad.run('curve_control',points=pts+[pts[0]],degree=1)[0]
        curve.rename(f'Fan groove {i+1} boundary')
        tool=cad.extrude(curve,distance=1.4,reverse_direction=True,operation='New body')
        cad.move(tool,z=30.6-tool.info['min'][2])
        before=body.refresh().info['volume'];cad.subtract(body,tool);body.refresh();d.hide(curve)
        assert body.info['volume'] < before-100, f'Fan groove {i+1} missed the wood'
    texture=ROOT/'gallery/textures/walnut-valet-color.png'
    cad.call('material_apply',object=NAME,material='walnut',color='#FFFFFF',pattern='Plain',roughness=.35,
             surface=dict(ColorMap=d.image(texture),TextureSize=300,TextureAspect=.7,Projection=0,TextureAxis=2,TextureRepeat=False,Bump=.07,Coat=.32,CoatRoughness=.28))
    info=body.info;assert info['valid'] and 900000<info['volume']<1800000,info
    cad.call('view',preset='iso',fit=True,yaw=-25,pitch=48,distance=550)
    d.checkpoint('walnut-geometry')
    return body

def operation(command,name,slots=None,**params):
    made=cad.run(command,slots=slots or {},**params)
    assert len(made)==1,made
    made[0].rename(name)
    return made[0]

def review(position,capture=False):
    cad.call('run_command',name='cnc_stock_review',commit=False,slots={'Model':NAME},params={'Program position':position,'Grid size':1.8,'Holder diameter':8,'Flute / holder clearance length':40,'Comparison tolerance':.3})
    result=cad.call('command_set',params={'Run checks':True})
    rows=result.get('results',[])
    (OUT/f'walnut-stock-{position}.json').write_text(json.dumps(rows,indent=2))
    print('Stock',position,rows,flush=True)
    if capture:d.capture(OUT/f'walnut-stock-{position}.png')
    cad.call('command_cancel')
    return rows

def cam():
    cad.call('file',action='open',path=str(OUT/'walnut-geometry.prism'))
    cad.cam_setup(NAME,name='Walnut blank — top side',stock_allowance_xy=3,top_allowance=1,bottom_allowance=0,tool_diameter=8,cutting_feed=1500,plunge_feed=350,spindle_speed=16000,clearance_above_stock=6)
    operation('cnc_face','01 · Face the walnut blank',tool_diameter=8,x_from=-3,x_to=303,y_from=-3,y_to=213,top_of_the_material_z=33.01,total_cutting_depth=1.01,depth_per_pass=1)
    operation('cnc_rough','02 · Rough the three basins',{'Model to rough':NAME},tool_diameter=8,roughing_stepover=65,stock_to_leave=.5,top_of_the_material_z=33.01,total_cutting_depth=33,depth_per_pass=3)
    operation('cnc_finish','03 · Ball finish pockets and fan',{'Model to finish':NAME},tool_diameter=4,cutter='Ball nose',stepover=20,scan_angle=0,top_of_the_material_z=33.01)
    import build_serving_board as outline
    outline.W,outline.D,outline.R=300,210,20
    boundary=outline.sketch(outline.outline_arcs())
    operation('cnc_profile','04 · Profile the outside with holding tabs',{'Outlines':boundary},tool_diameter=8,top_of_the_material_z=33.01,total_cutting_depth=33,depth_per_pass=4,holding_tabs=4,tab_width=8,tab_height=2)
    d.hide(boundary)
    d.checkpoint('walnut-cam')
    cad.call('view',yaw=-25,pitch=52,fit=True,grid=False)
    for position in [0,35,70,100]:review(position,True)
    cad.call('select_tab',title='Subtractive',command='cnc_stock_review')
    cad.save(str(ROOT/'gallery/models/walnut-valet.prism'))
    print('CAM fixture saved',flush=True)

def finish(render=False):
    path=ROOT/'gallery/models/walnut-valet.prism'
    cad.call('file',action='open',path=str(path));cad.call('command_cancel')
    paths=[o for o in cad.objects() if o.kind=='Toolpath']
    d.hide(*paths)
    if render:d.render('walnut-valet',-25,48,500,128)
    for o in paths:cad.call('object_update',object=o.name,visible=True)
    cad.call('select_tab',title='Subtractive',command='cnc_stock_review');cad.save(str(path))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--phase',choices=['geometry','decoration','cam','finish'],default='geometry');p.add_argument('--render',action='store_true');args=p.parse_args()
    bridge,log=d.connect()
    try:
        if args.phase=='geometry':geometry()
        elif args.phase=='decoration':
            cad.call('file',action='open',path=str(OUT/'walnut-rounded-pockets.prism'));decoration()
        elif args.phase=='cam':cam()
        else:finish(args.render)
    finally:bridge.terminate();bridge.wait(timeout=10);log.close()
