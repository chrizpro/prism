"""Cardboard: a brown shipping box with a hinged lid and a front lip, folded from one flat die-line blank with Prism's Fold tool.

The blank is a plus-shaped outline (base, four walls, and on the back wall a lid and a front lip) with handle slots in the two short walls. Each
bend is a construction line across it. Fold turns the blank into the box and also lays the die line out beside it, so the same sketch gives both
the 3D part and the flat pattern. The sheet is 3 mm corrugated board with a tight 0.5 mm score as the inside radius.

python gallery/build_cardboard_box.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math

import build_examples as study

cad = study.cad

KRAFT = dict(material="cardboard", color="#B8895A", roughness=.92, surface=dict(Bump=.14, PatchSize=3))
KRAFT_FLAT = dict(material="cardboard", color="#D2AF82", roughness=.92, surface=dict(Bump=.08, PatchSize=3))

L, W, H = 150.0, 100.0, 60.0            # inside length, width and wall height
T, R, K = 3.0, 0.5, 0.33               # board thickness, inside bend radius (a score), K-factor
GAP, LIP = 1.0, 25.0                    # clearance between lip and front wall, height of the front lip


def build():
    cad.call("document_new")
    a = math.pi / 2 * (R + K * T)                         # flat width of every 90 degree bend zone
    lid = W + T + GAP                                     # flat lid length: from over the back wall to just past the front wall's outside face
    back = a + H + a + lid + a + LIP                      # the back arm: wall, hinge, lid, hinge, lip
    front = a + H
    side = a + H
    outline = [(L / 2, -W / 2), (L / 2 + side, -W / 2), (L / 2 + side, W / 2), (L / 2, W / 2), (L / 2, W / 2 + back), (-L / 2, W / 2 + back),
               (-L / 2, W / 2), (-L / 2 - side, W / 2), (-L / 2 - side, -W / 2), (-L / 2, -W / 2), (-L / 2, -W / 2 - front), (L / 2, -W / 2 - front)]
    bends = [  # left to right, then bottom to top: the order Fold numbers them
        ((-(L / 2 + a / 2), -200), (-(L / 2 + a / 2), 400)),
        ((-300, -(W / 2 + a / 2)), (300, -(W / 2 + a / 2))),
        ((-300, W / 2 + a / 2), (300, W / 2 + a / 2)),
        ((-300, W / 2 + a + H + a / 2), (300, W / 2 + a + H + a / 2)),
        ((-300, W / 2 + a + H + a + lid + a / 2), (300, W / 2 + a + H + a + lid + a / 2)),
        ((L / 2 + a / 2, -200), (L / 2 + a / 2, 400)),
    ]
    slot_x = L / 2 + a + H / 2
    with cad.history("Cardboard box - Fold workflow"):
        cad.call("sketch_begin", plane="XY")
        cad.call("sketch_add", entities=[dict(type="polyline", points=[list(p) for p in outline], closed=True)])
        cad.call("sketch_add", entities=[dict(type="slot", center1=[sx, -16], center2=[sx, 16], width=9) for sx in (slot_x, -slot_x)])
        cad.call("sketch_add", entities=[dict(type="line", points=[list(p), list(q)], construction=True) for p, q in bends])
        sketch = cad.call("sketch_finish")["sketch"]
        made = cad.run("fold", slots={"Flat sketch": sketch}, thickness=T, bend_radius=R, k_factor=K, fixed_region="The region over the sketch origin")
        box = next(o for o in made if o.kind == "Solid")
        flat = next(o for o in made if o.kind == "Surface")
        box.rename("Cardboard box")
        flat.rename("Box blank (die line)")
        for o in made:
            if o.kind == "Curve":
                o.rename("Fold lines")
                cad.call("object_update", object=o.name, color="#6B4A2B")
        cad.call("material_apply", object=box.name, **KRAFT)
        cad.call("material_apply", object=flat.name, **KRAFT_FLAT)
        cad.call("object_update", object=sketch, visible=False)
    info = cad.call("object_info", object=box.name)
    assert info["valid"]
    # outside: x and y walls at half the inside size + radius + board; the lip hangs outside the front wall; the lid top is two boards above the wall tops
    top = T + R + H + R + T
    expect = (L + 2 * (R + T), (W / 2 + R + T) + (W / 2 + R + T + GAP + T), top)
    for got, want, name in zip(info["size"], expect, "xyz"):
        assert abs(got - want) < 0.01, f"box {name} size {got:.3f}, expected {want:.3f}"
    return "cardboard-box", -38, 30


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/cardboard-box.prism"))
            model = ("cardboard-box", -38, 30)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=900)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
