"""Capture real 0.4 command results from an explicitly selected, isolated Prism instance.

python tools/capture_doc_tool_results.py --discovery E:/.../automation.json --out E:/.../captures
The target instance must be disposable: this script creates new documents in it.
"""
import argparse
import base64
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Prism/Scripting/python'))
import cadkit as cad

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--discovery', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
args = parser.parse_args()
args.out.mkdir(parents=True, exist_ok=True)
c = json.loads(args.discovery.read_text(encoding='utf-8-sig'))
cad.connect(c['port'], c['token'])
records = []

def call(tool, **kwargs):
    result = cad.call(tool, **kwargs)
    if isinstance(result, dict) and result.get('statusIsError'):
        raise RuntimeError(result)
    if tool != 'screenshot':
        with (args.out / 'result-calls.jsonl').open('a', encoding='utf-8') as f:
            f.write(json.dumps(dict(tool=tool, args=kwargs, result=result)) + '\n')
    return result

def run(command, slots=None, **params):
    result = call('run_command', name=command, slots=slots or {}, params=params)
    return result.get('created', [])

def curve(name, points, closed=False):
    obj = run('curve_equation', **{'Points from':'Table or file', 'Points':'; '.join(','.join(map(str,p)) for p in points),
                                 'Curve':'Straight segments', 'Closed':closed})[0]['name']
    call('object_update', object=obj, name=name)
    return name

def sheet(name, points):
    boundary = curve(name+' outline', points, True)
    obj = run('boundary_surface', slots={'Boundary':boundary}, **{'Mode':'Fill boundary'})[0]['name']
    call('object_update', object=obj, name=name)
    call('object_update', object=boundary, visible=False)
    return name

def shot(name, caption, distance=None):
    call('command_cancel')
    call('select', items=[])
    call('view', preset='iso', fit=True, grid=False, display='shaded_edges')
    state = call('get_state')
    call('view', distance=distance or state['camera']['distance']*1.35)
    call('wait_idle', extra_ms=450)
    result = call('screenshot', viewport=True, max_width=1200, wait_ms=250)
    data = base64.b64decode(result['_image']['data'])
    (args.out / (name+'.png')).write_bytes(data)
    state = call('get_state')
    call('file', action='save_as', path=str(args.out / (name+'.prism')))
    records.append(dict(name=name, caption=caption, sha256=hashlib.sha256(data).hexdigest(), state=state))
    (args.out / 'result-captures.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    print('Captured', name, flush=True)

call('document_new')
run('curve_equation', **{'Points from':'Helix','Helix radius':12,'Pitch (rise per turn)':8,'Turns':5})
shot('helix-result', 'A real Curve from Data result: radius 12 mm, pitch 8 mm and five turns give 40 mm of axial rise.')

call('document_new')
profile = curve('Section', [(-8,-5,0),(8,-5,0),(8,5,0),(-8,5,0)], True)
path = curve('Path', [(0,0,0),(0,0,55)])
run('sweep_surface', slots={'Profile':profile,'Path':path}, **{'Scale along the path (optional)':'0:1, 0.5:1.6, 1:0.7'})
for name in [profile,path]: call('object_update', object=name, visible=False)
shot('scaled-sweep-result', 'Surface Sweep using the scale table 0:1, 0.5:1.6, 1:0.7: the section grows at mid-path and narrows at the end.')

call('document_new')
left = sheet('Left half', [(0,0,0),(25,0,0),(25,30,0),(0,30,0)])
right = sheet('Right half', [(25,0,0),(50,0,0),(50,30,0),(25,30,0)])
shot('sew-before', 'Two coplanar input sheets, with a visible seam between them.')
run('knit', slots={'Surfaces':[left,right]}, **{'Result':'Merge into one patch where they lie on the same surface'})
shot('sew-merge-result', 'Sew with Merge combines the two coplanar halves into one face; the middle seam is gone.')

call('document_new')
floor = sheet('Floor', [(0,0,0),(30,0,0),(30,30,0),(0,30,0)])
wall = sheet('Wall', [(30,0,0),(30,30,0),(30,30,30),(30,0,30)])
edges = []
for name in [floor,wall]:
    hits = call('solid_find', object=name, kind='edges', limit=100)['hits']
    edge = next(h for h in hits if abs(h['from'][0]-30)<1e-4 and abs(h['to'][0]-30)<1e-4 and abs(h['from'][2])<1e-4 and abs(h['to'][2])<1e-4)
    edges.append({'object':name,'edge':edge['edge']})
shot('surface-fillet-before', 'Two separate perpendicular sheets touching along an edge, before rounding.')
run('curve_fillet', slots={'Flat curve or sketch, or edges of a surface':edges}, **{'Fillet radius':6})
shot('surface-fillet-result', 'Fillet / Chamfer joins two touching sheets and rounds their shared corner with a 6 mm radius.')

call('document_new')
body = run('box', Width=50, Depth=40, Height=15)[0]['name']
top = call('solid_find', object=body, kind='faces', type='Plane', facing=[0,0,1], angle=2)['hits'][0]['face']
loop = run('curve_equation', **{'x  |  r  |  ρ':'9*cos(t*360)','y  |  θ  |  θ':'9*sin(t*360)', 'z  |  z  |  φ':'15','Closed':True})[0]['name']
run('split_face', slots={'Face to split':{'object':body,'face':top},'Splitting plane, edge or sketch':loop}, **{'Mode':'Imprint (mark curves on the face)'})
call('object_update', object=loop, visible=False)
shot('imprint-result', 'A closed circle imprinted on the solid top creates a disc face without removing material.')

(args.out / 'result-captures.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
