"""Compare the saved horn B-splines, including rational weights, on a UV grid.

Run with two .prism paths. Writes a shared-scale bending map next to the latter.
Requires numpy, scipy and matplotlib.
"""
import json
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
from scipy.interpolate import BSpline


def sample(path):
    bell = next(o for o in json.loads(path.read_text(encoding="utf-8-sig"))["Objects"]
                if o["Name"] == "Calla bell")
    data = iter(bell["Brep"].split("\nSurfaces 1\n")[1].split())
    kind, ru, rv, pu, pv, du, dv, nu, nv, ku, kv = (int(next(data)) for _ in range(11))
    assert kind == 9 and not (pu or pv)
    cols = 4 if ru or rv else 3
    poles = np.fromiter((float(next(data)) for _ in range(nu*nv*cols)), float).reshape(nu,nv,cols)
    if cols == 3:
        poles = np.concatenate((poles, np.ones((nu,nv,1))), axis=2)
    poles[:,:,:3] *= poles[:,:,3:]
    # In the original loft U runs around the bell; the control grid uses V.
    around_u = nu > nv
    def basis(n, degree, count, resolution):
        knots = np.concatenate([np.repeat(float(next(data)), int(next(data))) for _ in range(count)])
        spline = BSpline(knots, np.eye(n), degree)
        t = knots[degree] + (np.arange(resolution)+.5)/resolution*(knots[-degree-1]-knots[degree])
        return [spline(t, nu=j) for j in range(3)]
    U = basis(nu,du,ku,360 if around_u else 180)
    V = basis(nv,dv,kv,180 if around_u else 360)
    def derivative(i,j):
        return np.einsum('ai,ijc,bj->abc',U[i],poles,V[j],optimize=True)
    def xyz(a): return a[:,:,:3]
    def weight(a): return a[:,:,3:]
    h,hu,hv = derivative(0,0),derivative(1,0),derivative(0,1)
    w = weight(h)
    p = xyz(h)/w
    u = (xyz(hu)-p*weight(hu))/w
    v = (xyz(hv)-p*weight(hv))/w
    huu,huv,hvv = derivative(2,0),derivative(1,1),derivative(0,2)
    uu = (xyz(huu)-p*weight(huu)-2*u*weight(hu))/w
    uv = (xyz(huv)-p*weight(huv)-u*weight(hv)-v*weight(hu))/w
    vv = (xyz(hvv)-p*weight(hvv)-2*v*weight(hv))/w
    def dot(a,b): return (a*b).sum(axis=-1)
    cross = np.cross(u,v)
    area = np.linalg.norm(cross,axis=-1)
    normal = cross/area[:,:,None]
    E,F,G = dot(u,u),dot(u,v),dot(v,v)
    e,f,g = dot(uu,normal),dot(uv,normal),dot(vv,normal)
    H = (e*G-2*f*F+g*E)/(2*area**2)
    K = (e*g-f**2)/area**2
    bend = abs(H)+np.sqrt(np.maximum(0,H**2-K))
    region = (p[:,:,2]>=130) & (p[:,:,2]<=230)
    values, weights = bend[region],area[region]
    order = np.argsort(values)
    pct95 = np.interp(.95,np.cumsum(weights[order])/weights.sum(),values[order])
    print(path.name, 'poles',nu,nv,'shoulder p95',pct95,'shoulder peak',values.max(),
          'whole peak',bend.max())
    return bend.T if around_u else bend


if __name__ == "__main__":
    original, revised = map(Path,sys.argv[1:])
    fig, axes = plt.subplots(1,2,figsize=(9,4.6),layout="constrained",sharey=True)
    for ax, path, title in zip(axes,[original,revised],["Original loft","Revised control grid"]):
        im = ax.imshow(sample(path),origin="lower",extent=(0,1,0,1),aspect="auto",
                       vmin=0,vmax=.10,cmap="magma",interpolation="nearest")
        ax.set_title(title)
        ax.set_xlabel("Around the bell (UV fraction)")
        ax.set_yticks([0,1],["Throat","Lip"])
    fig.colorbar(im, ax=axes, label="Maximum absolute principal curvature (1/mm)",extend="max")
    fig.savefig(revised.with_name("curvature.png"),dpi=145)
