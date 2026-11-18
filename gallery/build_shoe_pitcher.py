"""Build two closed, editable Freeform models via Prism MCP."""
import argparse
import math

import build_freeform_pair as pair

cad = pair.cad
OUT = pair.study.ROOT / "docs/freeform-shoe-pitcher"


def rings(points, name, color):
    n = len(points[0])
    obj = cad.form_cylinder(radius=1,height=len(points)-1,sides=n,rings=len(points)-1,caps=True)
    cad.form_op(obj,"set_vertices",positions={str(i*n+j):v for i,row in enumerate(points)
                                              for j,v in enumerate(row)})
    return pair.study.look(obj,name,material="abs",color=color,roughness=.32)


def pitcher():
    # Cross the rounded lip, descend the inner wall, and cap the inside floor.
    outer = [(46,2),(60,2),(73,9),(90,28),(99,57),(95,84),(82,109),
             (72,134),(75,160),(79,173),(79,178)]
    inner = [(76,179),(74,174),(71,160),(68,134),(78,109),(91,84),
             (95,57),(86,30),(69,13),(52,10),(32,10)]
    n=20
    grid=[]
    for r,z in outer+inner:
        row=[]
        for j in range(n):
            a=math.tau*(j-.5)/n
            lip=max(0,-math.cos(a))**7 * max(0,(z-119)/60)**2
            row.append([r*math.cos(a)-37*lip,.89*r*math.sin(a),z+13*lip])
        grid.append(row)
    jug=rings(grid,"Celadon pitcher","#B5C399")
    # Extend a lower outside quad upward around the opening. Each original
    # corner keeps its correspondence as the ring turns through 180 degrees.
    lower,upper=3*n,7*n
    for x,z,tilt in [(112,45,10),(137,53,34),(154,75,67),(159,107,90),
                     (154,140,116),(139,159,150),(114,162,179),(94,155,199)]:
        cad.form_op(jug,"extrude_faces",faces=[lower],distance=4)
        ids=cad.form_info(jug)["faces"][lower]
        t=math.radians(tilt)
        coords=[[x-k*math.sin(t),y,z+k*math.cos(t)]
                for y,k in [(-15,-14),(15,-14),(15,14),(-15,14)]]
        cad.form_op(jug,"set_vertices",positions={str(i):p for i,p in zip(ids,coords)})
    cad.form_op(jug,"bridge",faces=[lower,upper])
    info=cad.form_info(jug,summary=True)
    print("Handle topology",info["topology"],flush=True)
    assert info["closed"] and info["topology"]["eulerCharacteristic"]==0
    return jug


# Toe, heel, half beam, average height at forefoot, side and heel.
STATIONS=[(-130,119,45,11,17,23),(-131,120,46,15,20,28),
          (-124,121,47,21,28,36),(-100,122,45,35,43,49),
          (-64,121,40,57,55,63),(-29,119,35,87,67,78),
          (-12,116,32,99,70,89),(-9,114,31,100,71,91)]
def shoe_point(row,a):
    fore,aft,width,front,side,rear=row
    c,s=math.cos(a),math.sin(a)
    x=(fore+aft)/2+(aft-fore)*c/2
    y=width*s*(1-.30*c)-9*((1-c)/2)**3
    arch=17*math.exp(-((x-18)/56)**2)
    return [x,y,side+(front-side)*max(0,-c)**2+(rear-side)*max(0,c)**2+arch]


def shoe():
    n=28
    inside=[(-7,111,28,97,69,88),(0,106,26,81,56,73),
            (9,99,24,49,36,48),(14,92,20,35,28,37)]
    upper=rings([[shoe_point(row,j*math.tau/n) for j in range(n)]
                 for row in STATIONS+inside],"Ochre upper","#C48329")
    rand=[]
    for band,offset in [(-2,-2),(-1,3),(0,4),(1,4),(2,4),(3,4),(3,2),(3,-3)]:
        row=[]
        for j in range(n):
            a=j*math.tau/n
            c=math.cos(a)
            base=shoe_point(STATIONS[max(0,band)],a)
            if band<0:
                base[2]=2+15*math.exp(-((base[0]-18)/56)**2)+(band+2)*2
            if band==3:
                # Sweep the cap higher over the toe and heel.
                mix=.65*abs(c)**3
                next_point=shoe_point(STATIONS[4],a)
                base=[v+(w-v)*mix for v,w in zip(base,next_point)]
            row.append([base[0]+offset*c,base[1]+offset*math.sin(a),base[2]])
        rand.append(row)
    rings(rand,"Charcoal rubber rand","#282B2E")
    # Strap crosses the sloping vamp diagonally; its ends turn down the flanks.
    def strap(i,y,h):
        t=i/14
        a=1.33+3.68*t
        mix=.56+.47*y+.21*(t-.5)
        row=[v+(w-v)*mix for v,w in zip(STATIONS[4],STATIONS[5])]
        x,b,z=shoe_point(row,a)
        return [x+math.cos(a)*(2+h),b+math.sin(a)*(2+h),z+2+h]
    pair.shaped_box("Graphite instep strap",range(15),4,strap,"#35383A")
    return upper


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--only",choices=["Shoe","Pitcher"])
    only=parser.parse_args().only
    OUT.mkdir(parents=True,exist_ok=True)
    pair.OUT=OUT
    bridge,log=pair.study.connect_mcp(pair.study.ROOT/"artifacts/gallery/freeform-shoe-pitcher.json")
    try:
        for key,fn,cam,axis,at in [
            ("Pitcher",pitcher,dict(yaw=-15,pitch=17,target=[24,0,92],distance=396),"z",[40,92,150]),
            ("Shoe",shoe,dict(yaw=-30,pitch=19,target=[-4,0,45],distance=307),"x",[-100,-50,30,90])]:
            if only and key!=only: continue
            cad.call("document_new")
            pair.finish(key,fn(),cam,axis,at)
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
