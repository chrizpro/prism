"""Actual Prism studio renders for the owner-approved November gallery additions."""
import sys,json,time,base64
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'gallery'))
import demo_session as d
from PIL import Image
OUT=ROOT/'artifacts/gallery-release';c=d.cad
SPECS=[('linen-t2-toaster',55,29),('serein-s1-phone',15,65),('arc-soap-mold-cam',22,52),('maple-observatory',32,25),('deco-mantel-clock',18,14),('maple-walnut-guitar',-20,58),('horizon-h4',-38,24)]
def write_preview(id):
 image=Image.open(OUT/(id+'-render.png')).convert('RGB')
 if id=='horizon-h4':
  w,h=image.size;image=image.crop((int(w*.15),int(h*.15),int(w*.85),int(h*.85)))
 image.thumbnail((810,504),Image.Resampling.LANCZOS);image.save(ROOT/'gallery/previews'/(id+'.png'))

b,l=d.connect(ROOT/'artifacts/guitar-v2/automation.json');OUT.mkdir(exist_ok=True)
try:
 for id,yaw,pitch in SPECS:
  if (OUT/(id+'-render.png')).exists():
   write_preview(id);continue
  print('Opening',id,flush=True);c.call('command_cancel');c.call('file',action='open',path=str(ROOT/'gallery/models'/(id+'.prism')));c.call('wait_idle',timeout_ms=5000);c.call('command_cancel')
  paths=[o for o in c.objects() if o.kind=='Toolpath']
  if paths:d.hide(*paths)
  c.select();c.call('view',yaw=yaw,pitch=pitch,fit=True,grid=False,display='shaded');c.call('wait_idle',timeout_ms=5000)
  state=c.call('get_state');(OUT/(id+'-camera.json')).write_text(json.dumps(state['camera'],indent=2))
  c.call('run_command',name='render',commit=False,params={'Picture size':'HD  1280 × 720','Quality (samples per pixel)':96,'Lighting':'Studio (soft white boxes)','Light bounces':6,'Background':'White','Denoise final image':True,'Ground plane with shadows':True,'Start render':True})
  deadline=time.monotonic()+1200
  while time.monotonic()<deadline:
   r=c.call('render_control',action='status')
   if r.get('error'):raise RuntimeError(r)
   if r.get('finished'):break
   time.sleep(2)
  assert r.get('finished'),r
  c.call('render_control',action='save',path=str(OUT/(id+'-render.png')));c.call('render_control',action='close');c.call('command_cancel')
  write_preview(id)
  print('Rendered',id,flush=True)
  for o in paths:c.call('object_update',object=o.name,visible=True)
finally:b.terminate();b.wait(timeout=10);l.close()
