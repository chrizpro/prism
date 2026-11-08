"""Injection mold: a block with a bottle-cap shaped part inside it, turned into a two-part mold with the Mold tab's Create Mold. Purely illustrative.

A 120 x 90 x 70 mm steel block surrounds a 40 mm cap (a cylinder with a domed top). Create Mold cuts the cap out of the block, splits the block through the
cap's widest part (so it can be released) and pulls the halves 60 mm apart, the cap staying between them. The file reopens on the Mold tab with Create Mold
open. The script checks that the halves sit where the split is and that their volumes add up to the block minus the cap.

python gallery/build_mold.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse

import build_examples as study

cad = study.cad

STEEL = dict(material="steel", color="#8D96A0", roughness=.35, metal=1)
CAP = dict(material="pp", color="#2E7FD1", roughness=.35)
W, D, H = 120.0, 90.0, 70.0
SPLIT, GAP = 35.0, 60.0


def build():
    cad.call("document_new")
    with cad.history("Injection mold - Solid and Mold workflow"):
        block = cad.box(width=W, depth=D, height=H, operation="New body")
        block.rename("Mold block")
        body = cad.cylinder(radius=20, height=SPLIT, operation="New body")
        cad.move(body, x=0, y=0, z=SPLIT - 15)
        dome = cad.sphere(radius=20, operation="New body")
        cad.move(dome, x=0, y=0, z=SPLIT + 20 - 15)
        cad.union(body, dome)
        body.rename("Cap")
        cad.call("material_apply", object="Mold block", **STEEL)
        cad.call("material_apply", object="Cap", **CAP)
        before = cad.call("object_info", object="Mold block")["volume"]
        cap = cad.call("object_info", object="Cap")["volume"]
        cad.call("run_command", name="mold", slots={"Mold block": "Mold block", "Part to cast": "Cap"}, params={"Parting direction": "Z", "Split at": 100 * SPLIT / H, "Explode distance": GAP})
    for o in cad.objects():
        if o.kind == "Solid" and o.name != "Cap":
            cad.call("material_apply", object=o.name, **STEEL)
    halves = [o for o in cad.objects() if o.kind == "Solid" and o.name != "Cap"]
    assert len(halves) == 2, [o.name for o in cad.objects()]
    infos = sorted((cad.call("object_info", object=h.name) for h in halves), key=lambda i: i["min"][2])
    total = infos[0]["volume"] + infos[1]["volume"]
    assert abs(total - (before - cap)) < .002 * before, (total, before, cap)
    assert abs(infos[1]["min"][2] - infos[0]["max"][2] - GAP) < .05, (infos[0]["max"], infos[1]["min"])
    cad.call("select_tab", title="Mold", command="mold")
    return "injection-mold", -35, 25


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/injection-mold.prism"))
            model = ("injection-mold", -35, 25)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=520)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
