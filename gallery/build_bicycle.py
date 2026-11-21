"""Full-size road bicycle, native Prism solids built through stdio MCP + cadkit."""
import argparse, os, json
from pathlib import Path
import build_examples as study
cad=study.cad
call=cad.call
ROOT=study.ROOT
import math,base64
OUT=ROOT/'artifacts/bicycle'
ALL=[]
TEAL='#087E8B'; BLACK='#171D24'; SILVER='#B8C5CC'; GOLD='#B89152'
def rot(o,a,axis):
 if abs(a)>1e-7:cad.run('rotate',slots={'Objects':o.name},angle=a,axis=axis,pivot='World origin')
def finish(o,n,c,metal=.0):
 o.rename(n);call('material_apply',object=n,color=c,metal=metal,roughness=.28 if metal else .42)
 ALL.append(n);call('object_update',object=n,visible=False)
 return o
def rod(n,a,b,r,c=TEAL,metal=.5):
 d=[b[i]-a[i] for i in range(3)];L=math.sqrt(sum(v*v for v in d))
 o=cad.cylinder(radius=r,height=L,operation='New body')
 rot(o,math.degrees(math.acos(d[2]/L)),'Y');rot(o,math.degrees(math.atan2(d[1],d[0])),'Z');cad.move(o,x=a[0],y=a[1],z=a[2])
 return finish(o,n,c,metal)
def ring(n,x,y,z,R,r,c,metal=.5):
 o=cad.torus(major_radius=R,minor_radius=r,operation='New body');rot(o,90,'X');cad.move(o,x=x,y=y,z=z)
 return finish(o,n,c,metal)
def ell(n,p,s,c):
 o=cad.sphere(radius=1,operation='New body');cad.run('scale',slots={'Objects':o.name},scale_type='Per axis (stretch)',factor_x=s[0],factor_y=s[1],factor_z=s[2]);cad.move(o,x=p[0],y=p[1],z=p[2]);return finish(o,n,c)
def curve(n,pts,r,c,metal=0):
 path=cad.run('curve_through',points=pts)[0]
 o=cad.run('pipe',slots={'Path curve':path.name},radius_at_the_start=r,radius_at_the_end=r)[0]
 cad.delete(path);return finish(o,n,c,metal)
def show(on=True):
 call('batch',calls=[dict(tool='object_update',args=dict(object=n,visible=on)) for n in ALL])
def save(stage):
 show();call('view',yaw=165,pitch=12,fit=True,grid=False,orthographic=True);cad.save(str(OUT/(stage+'.prism')));print(stage,len(ALL),flush=True);show(False)

if __name__=='__main__':
 parser=argparse.ArgumentParser()
 parser.add_argument('--discovery',default=str(Path(os.environ.get('LOCALAPPDATA',''))/'Prism/automation.json'))
 args=parser.parse_args()
 OUT.mkdir(parents=True,exist_ok=True)
 bridge,log=study.connect_mcp(args.discovery)
 call=cad.call
 # Keep the owner's existing documents intact in their own tabs.
 call('document_new')
 B=(410,0,280);S=(270,0,790);H0=(860,0,725);H1=(820,0,865)
 rod('01 Frame - seat tube',B,S,16)
 rod('02 Frame - down tube',B,H0,21)
 rod('03 Frame - top tube',S,H1,16)
 rod('04 Head tube',H0,H1,23)
 rod('05 Bottom bracket shell',(410,-43,280),(410,43,280),26)
 for side in [-1,1]:
  y=side*58
  rod(f'Chainstay {side}',(410,side*26,280),(0,y,340),10)
  rod(f'Seatstay {side}',(270,side*12,772),(0,y,340),7)
  # swept fork legs, subtly raked toward the front axle
  curve(f'Fork blade {side}',[(850,side*28,754),(899,side*40,620),(950,side*47,450),(1000,side*50,340)],13,TEAL,.5)
  rod(f'Fork dropout {side}',(1000,side*44,340),(1000,side*59,340),18,BLACK,.5)
 rod('Seatpost',(270,0,775),(226,0,940),12,SILVER,.85)
 ring('Seat collar',270,0,788,18,3,BLACK)
 rod('Saddle rail left',(160,-25,950),(300,-25,950),3,SILVER,.9)
 rod('Saddle rail right',(160,25,950),(300,25,950),3,SILVER,.9)
 ell('Saddle rear',(190,0,968),(83,72,19),BLACK)
 ell('Saddle tapered nose',(277,0,967),(87,27,14),BLACK)
 rod('Stem riser',H1,(807,0,912),16,BLACK,.6)
 rod('Stem extension',(807,0,910),(902,0,925),15,BLACK,.6)
 rod('Handlebar center',(902,-180,925),(902,180,925),12,BLACK,.6)
 for side in [-1,1]:
  curve(f'Drop bar wrapped {side}',[(902,side*170,925),(952,side*200,920),(1001,side*211,865),(996,side*215,808),(946,side*215,785),(883,side*215,789)],14,BLACK)
  ell(f'Brake hood {side}',(955,side*199,921),(31,20,28),BLACK)
  rod(f'Brake lever {side}',(976,side*201,905),(985,side*208,835),4,SILVER,.9)
 save('01-frame-and-cockpit')
 for x,label in [(0,'Rear'),(1000,'Front')]:
  ring(label+' tire',x,0,340,326,14,BLACK,0)
  for y in [-10,10]:
   ring(label+f' tan sidewall {y}',x,y*1.5,340,326,3.5,'#B99A71',0)
   ring(label+f' rim edge {y}',x,y,340,308,3,SILVER,.85)
  ring(label+' deep section rim',x,0,340,306,9,BLACK,.6)
  rod(label+' hub barrel',(x,-32,340),(x,32,340),17,BLACK,.7)
  rod(label+' axle',(x,-70,340),(x,70,340),5,SILVER,.9)
  for y in [-30,30]:
   rod(label+f' hub flange {y}',(x,y-2,340),(x,y+2,340),29,SILVER,.9)
  for i in range(28):
   a=2*math.pi*i/28;hub=a+(.32 if i%2 else -.32);y=30 if i%2 else -30
   rod(label+f' spoke {i+1:02}',(x+27*math.cos(hub),y,340+27*math.sin(hub)),(x+303*math.cos(a),0,340+303*math.sin(a)),1.1,SILVER,.95)
  rod(label+' valve',(x,0,631),(x,0,650),2.4,SILVER,.9)
  # disc rotor carrier and floating braking track
  ring(label+' disc rotor',x,-43,340,66,6,SILVER,.9)
  for i in range(6):
   a=math.tau*i/6
   rod(label+f' rotor arm {i}',(x+17*math.cos(a),-43,340+17*math.sin(a)),(x+61*math.cos(a+.25),-43,340+61*math.sin(a+.25)),3,SILVER,.9)
  ell(label+' disc caliper',(x+53,-47,391),(16,13,22),BLACK)
 save('02-complete-wheels')
 # Drive side (+Y): double chainring, 9-speed cassette, crank and pedals.
 for R,y in [(91,65),(72,58)]:
  ring(f'Chainring {R}',410,y,280,R,4,SILVER,.9)
  for i in range(5):
   a=math.tau*i/5
   rod(f'Chainring {R} spider {i}',(410+19*math.cos(a),y,280+19*math.sin(a)),(410+(R-4)*math.cos(a),y,280+(R-4)*math.sin(a)),5,BLACK,.8)
  for i in range(48 if R==91 else 36):
   a=math.tau*i/(48 if R==91 else 36)
   rod(f'Chainring {R} tooth {i}',(410+(R-1)*math.cos(a),y,280+(R-1)*math.sin(a)),(410+(R+5)*math.cos(a),y,280+(R+5)*math.sin(a)),2,SILVER,.9)
 for i in range(9):ring(f'Cassette sprocket {i+1}',0,36+i*3,340,48-i*3.4,2,SILVER,.9)
 # two tangent chain runs plus the wrap arcs, faceted into individual roller links
 pts=[]
 for i in range(31):
  a=math.radians(-85+180*i/30);pts.append((410+96*math.cos(a),69,280+96*math.sin(a)))
 for i in range(25):
  a=math.radians(95+180*i/24);pts.append((53*math.cos(a),69,340+53*math.sin(a)))
 # A continuous swept chain envelope keeps this visual study compact.
 path=cad.run('curve_through',points=pts,degree=1,closed=True)[0]
 chain=cad.run('pipe',slots={'Path curve':path.name},radius_at_the_start=2.7,radius_at_the_end=2.7)[0]
 cad.delete(path);finish(chain,'Drive chain',GOLD,.85)
 for side in [-1,1]:
  end=(455 if side==1 else 365,side*85,116 if side==1 else 444)
  rod(f'Crank arm {side}',(410,side*78,280),end,10,BLACK,.75)
  rod(f'Pedal spindle {side}',end,(end[0],side*135,end[2]),5,SILVER,.9)
  ell(f'Pedal platform {side}',(end[0],side*145,end[2]),(43,32,9),BLACK)
 rod('Rear derailleur cage',(-18,75,315),(20,75,227),6,BLACK,.7)
 for x,z in [(-18,308),(20,227)]:ring(f'Jockey pulley {z}',x,75,z,17,4,BLACK,.6)
 for side in [-1,1]:
  curve(f'Brake hose {side}',[(965,side*190,908),(875,side*135,875),(798,side*35,820),(840,side*20,737)],2,BLACK)
 rod('Bottle cage mount upper',(570,-4,430),(570,-15,430),4,SILVER,.8)
 rod('Bottle cage mount lower',(500,-4,370),(500,-15,370),4,SILVER,.8)
 save('03-drivetrain')
 cad.run('rotate',slots={'Objects':'Seat collar'},axis='X',angle=90,pivot='Object center')
 show()
 # Make Part retains placement and gives the model a real assembly parts list.
 call('run_command',name='make_part',slots={'Bodies':ALL})
 call('view',yaw=165,pitch=12,fit=True,grid=False,orthographic=True)
 cad.save(str(ROOT/'gallery/models/aster-road.prism'))
 state=call('get_state');(OUT/'final-state.json').write_text(json.dumps(state,indent=2))
 print('FINISHED',len(ALL),flush=True)
 bridge.terminate();bridge.wait(timeout=10);log.close()
