import os, math
from pathlib import Path
import build_examples as study
cad=study.cad
bridge,log=study.connect_mcp(Path(os.environ['LOCALAPPDATA'])/'Prism/automation.json')
metal=dict(material='5052',color='#BEC4CA',metal=.95,roughness=.3,pattern='Brushed',patternScale=3)
parts=[('cascade-ribbon','Cascade ribbon',[(0,0),(45,0),(75,30),(105,30),(135,0),(165,0),(195,30),(225,30),(255,0),(300,0)],90),('zigzag-channel','Zigzag channel',[(0,0),(0,60),(50,60),(50,95),(110,95),(110,60),(160,60),(160,0),(210,0)],110)]
try:
 for id,name,pts,width in parts:
  cad.call('document_new')
  with cad.sketch('XZ') as s:s.polyline(pts)
  s.obj.rename(name+' profile')
  obj=cad.flange(s.obj,thickness=1.5,bend_radius=3,width=width,centred_on_the_sketch_plane=True,the_metal_lies='Right of the profile')
  obj.rename(name);cad.call('material_apply',object=name,**metal);cad.call('object_update',object=s.obj.name,visible=False)
  cad.save(str(study.ROOT/'artifacts/gallery'/(id+'-checkpoint.prism')))
  # Punch staggered rows through the flat lands; verify removed material.
  lands = [(20,0),(90,30),(210,30),(280,0)] if id == 'cascade-ribbon' else [(25,60),(80,95),(135,60),(185,0)]
  cad.call('object_update',object=name,visible=False)
  cutters=[]
  for x,z in lands:
   for y in (-30,-10,10,30):
    cutter=cad.cylinder(radius=3,height=16,position_x=x,position_y=y,position_z=z-8,operation='New body')
    cutters.append(cutter)
    cad.call('object_update',object=cutter.name,visible=False)
  before=cad.call('object_info',object=name)['volume']
  cad.subtract(obj,*cutters)
  after=cad.call('object_info',object=name)['volume']
  assert abs((before-after)-len(cutters)*math.pi*9*1.5)<2, (before,after)
  cad.call('object_update',object=name,visible=True)
  cad.flat_pattern(name,k_factor=.44)
  flat=next(o for o in cad.objects() if o.kind=='Surface');flat.rename(name+' flat pattern');cad.call('material_apply',object=flat.name,**metal)
  for o in cad.objects():
   if o.kind=='Curve':cad.call('object_update',object=o.name,color='#FF6B2C')
  study.verify_and_save(id,-35,25,distance=1000)
finally:bridge.terminate();bridge.wait(timeout=10);log.close()

