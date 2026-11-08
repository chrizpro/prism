"""Lotus: a silver dish folded from one flat sheet with Prism's Fold tool. Purely decorative.

A 70 mm square base carries four petals. Each petal is a rectangular stretch that rises 50 degrees from the base, then a trapezoid tip that curls
back outward by 30 degrees, so the tips are wider than they are long and narrow to 30 mm. A ring of eight holes and a centre hole pierce the base,
each petal has a slot, and each tip has a round hole. Each of the eight bends is a construction line across the flat sketch (a bend zone has to be a
clean rectangle of metal, which is why the trapezoids sit beyond the second bend). Fold also lays out the flat pattern beside the dish.

python gallery/build_lotus_dish.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math

import build_examples as study

cad = study.cad

SILVER = dict(material="5052", color="#BEC4CA", metal=.95, roughness=.3, pattern="Brushed", patternScale=3, surface=dict(Anisotropy=.45, Coat=.06))
BARE = dict(material="5052", color="#CDD2D7", metal=.95, roughness=.3, pattern="Brushed", patternScale=3, surface=dict(Anisotropy=.45, Coat=.06))

T, R, K = 1.5, 2.0, 0.44                 # sheet thickness, inside bend radius, K-factor
HALF = 35.0                              # half the base side
RISE, CURL = 50.0, -30.0                 # first bend up, second bend back out (degrees)
STRAIGHT, TIP = 25.0, 45.0               # rectangular stretch of a petal, length of its trapezoid tip
TIP_HALF = 15.0                          # half the width at the end of a tip


def rotate(p, quarter):
    x, y = p
    for _ in range(quarter % 4):
        x, y = -y, x
    return (x, y)


def build():
    cad.call("document_new")
    a1 = math.radians(RISE) * (R + K * T)                  # flat width of the first bend zone
    a2 = math.radians(abs(CURL)) * (R + K * T)             # and of the second
    y0 = HALF + a1 + STRAIGHT + a2                         # where the rectangle ends and the trapezoid begins
    y1 = y0 + TIP
    # outline: the +Y petal traced from the base corner, then the same rotated a quarter turn at a time
    arm = [(HALF, HALF), (HALF, y0), (TIP_HALF, y1), (-TIP_HALF, y1), (-HALF, y0)]
    outline = [rotate(p, k) for k in range(4) for p in arm]
    # bends, as construction lines; the order Fold numbers them in is left to right, then bottom to top, by each line's midpoint
    first, second = HALF + a1 / 2, HALF + a1 + STRAIGHT + a2 / 2
    lines = []
    for pos, angle in ((first, RISE), (second, CURL)):
        lines += [((-pos, -300), (-pos, 300), angle), ((pos, -300), (pos, 300), angle), ((-300, -pos), (300, -pos), angle), ((-300, pos), (300, pos), angle)]
    lines.sort(key=lambda l: (round((l[0][0] + l[1][0]) / 2, 3), round((l[0][1] + l[1][1]) / 2, 3)))
    angles = ", ".join(f"{l[2]:g}" for l in lines)

    holes = [dict(type="circle", center=[0, 0], radius=8)]
    holes += [dict(type="circle", center=[22 * math.cos(math.radians(45 * i)), 22 * math.sin(math.radians(45 * i))], radius=3.5) for i in range(8)]
    mid = HALF + a1 + STRAIGHT / 2                         # centre of each petal's rectangular stretch
    tip = y0 + TIP * 0.45
    for quarter in range(4):
        x, y = rotate((0, mid), quarter)
        # slot along the petal's axis
        ux, uy = rotate((0, 1), quarter)
        holes.append(dict(type="slot", center1=[x - ux * 5, y - uy * 5], center2=[x + ux * 5, y + uy * 5], width=6))
        tx, ty = rotate((0, tip), quarter)
        holes.append(dict(type="circle", center=[tx, ty], radius=3))

    with cad.history("Lotus dish - Fold workflow"):
        cad.call("sketch_begin", plane="XY")
        cad.call("sketch_add", entities=[dict(type="polyline", points=[list(p) for p in outline], closed=True)])
        cad.call("sketch_add", entities=holes)
        cad.call("sketch_add", entities=[dict(type="line", points=[list(l[0]), list(l[1])], construction=True) for l in lines])
        sketch = cad.call("sketch_finish")["sketch"]
        made = cad.run("fold", slots={"Flat sketch": sketch}, thickness=T, bend_radius=R, k_factor=K, angle_of_each_bend=angles)
        dish = next(o for o in made if o.kind == "Solid")
        flat = next(o for o in made if o.kind == "Surface")
        dish.rename("Lotus dish")
        flat.rename("Lotus dish flat pattern")
        for o in made:
            if o.kind == "Curve":
                o.rename("Bend lines")
                cad.call("object_update", object=o.name, color="#FF6B2C")
        cad.call("material_apply", object=dish.name, **SILVER)
        cad.call("material_apply", object=flat.name, **BARE)
        cad.call("object_update", object=sketch, visible=False)

    # the folded metal must match its own flat pattern: flat area minus the bend zones, plus the volume of the bend sectors
    info, fp = cad.call("object_info", object=dish.name), cad.call("object_info", object=flat.name)
    assert info["valid"] and fp["valid"]
    width = 2 * HALF
    sector = lambda deg: math.radians(abs(deg)) * T * (R + T / 2)               # per mm of bend length
    zones = 4 * (a1 * width + a2 * width)
    expected = T * (fp["area"] - zones) + 4 * width * (sector(RISE) + sector(CURL))
    assert abs(info["volume"] - expected) < 1e-3 * expected, (info["volume"], expected)
    assert fp["wires"] == fp["faces"] + 1 + 8 + 4 + 4, fp
    return "lotus-dish", -35, 32


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/lotus-dish.prism"))
            model = ("lotus-dish", -35, 32)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=700)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
