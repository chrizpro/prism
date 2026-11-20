"""Shared MCP connection/render helpers for the approved #131 demo studies."""
import base64
import json
import os
import time
from pathlib import Path
import build_examples as study

cad = study.cad
ROOT = study.ROOT
OUT = ROOT / 'artifacts/cnc-wheel'

def connect(discovery=None):
    OUT.mkdir(parents=True, exist_ok=True)
    current=ROOT/'artifacts/cnc-wheel/final-bin/Prism.Desktop.exe'
    os.environ.setdefault('PRISM_EXE', str(current if current.exists() else ROOT/'artifacts/arc-lamp/bin/Prism.Desktop.exe'))
    # the bridge from the current build wins; the arc-lamp copy is only a fallback when none has been built
    fresh = [ROOT/f'Prism.Mcp/bin/{c}/net10.0/Prism.Mcp.dll' for c in ('Release', 'Debug')]
    if not any(p.is_file() for p in fresh) and 'PRISM_MCP' not in os.environ:
        os.environ['PRISM_MCP'] = str(ROOT/'artifacts/arc-lamp/mcp/Prism.Mcp.dll')
    return study.connect_mcp(discovery or OUT/'automation.json')

def hide(*objects):
    cad.call('batch', calls=[dict(tool='object_update', args=dict(object=o.name if hasattr(o,'name') else o, visible=False)) for o in objects])

def finish(obj, name, **look):
    obj.rename(name)
    if look: cad.call('material_apply', object=name, **look)
    hide(obj)
    return obj

def checkpoint(name):
    cad.save(str(OUT/(name+'.prism')))
    print('Saved', name, flush=True)

def image(path):
    return base64.b64encode(Path(path).read_bytes()).decode()

def capture(path):
    result=cad.call('screenshot',viewport=True,max_width=1100)
    data=result.get('data') or result.get('base64') or result.get('_image',{}).get('data')
    if data: Path(path).write_bytes(base64.b64decode(data))
    else: print('Screenshot result:',str(result)[:250],flush=True)

def render(id,yaw,pitch,distance,samples=96):
    cad.call('command_cancel')
    cad.call('view',preset='iso',fit=True,yaw=yaw,pitch=pitch)
    cad.call('view',distance=distance)
    cad.call('run_command',name='render',commit=False,params={'Picture size':'HD  1280 × 720','Quality (samples per pixel)':samples,'Lighting':'Studio (soft white boxes)','Light bounces':6,'Background':'White','Denoise final image':True,'Ground plane with shadows':True,'Start render':True})
    deadline=time.monotonic()+600
    while time.monotonic()<deadline:
        state=cad.call('render_control',action='status')
        if state.get('error') or not state: raise RuntimeError(state)
        if state.get('finished'):break
        time.sleep(1)
    assert state.get('finished'),state
    cad.call('render_control',action='save',path=str(OUT/(id+'-render.png')))
    cad.call('render_control',action='close');cad.call('command_cancel')
    print('Rendered',id,flush=True)

if __name__=='__main__':
    import sys
    bridge,log=connect()
    try:
        print(json.dumps(cad.call(sys.argv[1],**json.loads(sys.argv[2] if len(sys.argv)>2 else '{}')),indent=2))
    finally:bridge.terminate();bridge.wait(timeout=10);log.close()
