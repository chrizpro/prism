"""Fold: a one-piece sheet-metal tablet stand, modeled through Prism MCP/cadkit.

One Flange profile makes three bends, two of them at 110 degrees rather than 90, plus a closed hem on the
top edge. Chamfered corners, a grid of 21 round vent holes in the tilted back rest and a cable hole in the
base are cut through the bent body. Flat Pattern then unfolds the finished part beside it, holes included.
Slots and rounded corners are deliberately avoided: Flat Pattern reads any arc-shaped cylinder as a bend and
refuses the part (see the gallery guide for the finding). The tablet is a placeholder body for scale only.

python gallery/build_sheetmetal.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math

import build_examples as study

cad = study.cad

PAINT = dict(material="5052", color="#1D7C8E", metal=.2, roughness=.42, surface=dict(Coat=.45, CoatRoughness=.25, Bump=.02))
BARE = dict(material="5052", color="#C9D0D6", metal=.9, roughness=.3, pattern="Brushed", patternScale=2, surface=dict(Anisotropy=.4))
GLASS = dict(material="pc", color="#10151B", roughness=.06, surface=dict(Coat=.9, CoatRoughness=.03))

THICKNESS, BEND_R, WIDTH, HEM = 2.0, 3.0, 140.0, 8.0
LIP, BASE, REST, TILT = 22.0, 62.0, 110.0, 70.0          # front ledge height, base length, back-rest length, rest angle from horizontal
TABLET = (7.0, 230.0, 160.0)                              # thickness, height, width (portrait), mm
GAP = .3                                                  # clearance between tablet and sheet


def solid_info(name):
    return cad.call("object_info", object=name)


def find_faces(name, **filters):
    return cad.call("solid_find", object=name, kind="faces", type="Plane", **filters)["hits"]


def cut(name, face, entities, depth=6):
    """Cut round holes through the sheet from a face sketch (u across the part, v along the face).

    The default Extrude direction follows the sketch normal, which can point away from the metal: on the tilted back
    rest the cut then silently removes nothing (and leaves zero-depth discs). The removed volume is checked against the
    hole area times the thickness, and the cut is undone and repeated reversed when it fell short.
    """
    cad.call("sketch_begin", plane=dict(object=name, face=face))
    cad.call("sketch_add", entities=entities)
    sketch = cad.get(cad.call("sketch_finish")["sketch"])
    expected = sum(math.pi * e["radius"] ** 2 for e in entities) * THICKNESS
    before = solid_info(name)["volume"]
    for reverse in (False, True):
        cad.run("extrude", slots={"Profile": sketch.name}, distance=depth, operation="Cut", reverse_direction=reverse)
        removed = before - solid_info(name)["volume"]
        if abs(removed - expected) < .02 * expected:
            break
        cad.undo()
    else:
        raise AssertionError(f"cut removed {removed:.1f} mm3, expected {expected:.1f}")
    cad.call("object_update", object=sketch.name, visible=False)
    return expected


def build():
    cad.call("document_new")
    angle = math.radians(TILT)
    with cad.history("Fold tablet stand - Sheet Metal workflow"):
        with cad.sketch("XZ") as s:
            s.polyline([(0, LIP), (0, 0), (BASE, 0), (BASE + REST * math.cos(angle), REST * math.sin(angle))])
        profile = s.obj
        profile.rename("Stand profile (cross-section)")
        stand = cad.flange(profile, thickness=THICKNESS, bend_radius=BEND_R, width=WIDTH, centred_on_the_sketch_plane=True,
                           hem_length_at_the_end=HEM, hem_gap=.5, the_metal_lies="Right of the profile")
        name = "Fold stand"
        stand.rename(name)
        assert solid_info(name)["valid"]

        # Corners: chamfers keep the part unfoldable (rounded corners are arc-shaped cylinders, which Flat Pattern rejects).
        lip_top = find_faces(name, facing=[0, 0, 1], box=[[-3, -WIDTH, LIP - 2], [1, WIDTH, LIP + 2]])[0]["face"]
        corners = cad.call("solid_find", object=name, kind="edges", of_face=lip_top, direction=[1, 0, 0])["hits"]
        cad.chamfer([dict(object=name, edge=e["edge"]) for e in corners], distance=7)
        end_face = find_faces(name, facing=[-math.cos(math.radians(TILT)), 0, -math.sin(math.radians(TILT))], min_size=200, max_size=400)
        assert end_face, "hem end face not found"
        edges = cad.call("solid_find", object=name, kind="edges", of_face=end_face[0]["face"], max_size=THICKNESS + .5)["hits"]
        cad.chamfer([dict(object=name, edge=e["edge"]) for e in edges], distance=7)

        # Vents: three offset rows of round holes through the tilted back rest.
        rest_normal = [-math.sin(angle), 0, math.cos(angle)]
        rest = find_faces(name, facing=rest_normal, min_size=8000)[0]["face"]
        vents = [dict(type="circle", center=[u + (8 if row % 2 else 0), v], radius=3.5)
                 for row, v in enumerate((-26, -6, 14, 34)) for u in range(-56, 57, 16) if abs(u + (8 if row % 2 else 0)) <= 60]
        cut(name, rest, vents)
        # Cable hole in the base.
        base_face = find_faces(name, facing=[0, 0, 1], min_size=5000)[0]["face"]
        cut(name, base_face, [dict(type="circle", center=[-6, 0], radius=11)])
        info = solid_info(name)
        assert info["valid"]

        cad.call("object_update", object=profile.name, visible=False)
        cad.call("material_apply", object=name, **PAINT)

        # Flat Pattern: the unfolded sheet is placed beside the part, with the holes and a line at each bend.
        cad.flat_pattern(name, k_factor=.44)
        flat = next(o for o in cad.objects() if o.kind == "Surface")
        flat.rename("Flat pattern")
        cad.call("material_apply", object=flat.name, **BARE)
        flat_info = solid_info(flat.name)
        assert flat_info["wires"] == flat_info["faces"] + len(vents) + 1, "the flat pattern lost holes"
        assert abs(flat_info["size"][1] - WIDTH) < .01
        for o in cad.objects():
            if o.kind == "Curve":
                o.rename("Bend lines")
                cad.call("object_update", object=o.name, color="#FF6B2C")

        # Placeholder tablet, tilted to the back rest and standing on the base against the lip.
        thick, height, width = TABLET
        tablet = cad.box(width=thick, depth=width, height=height, operation="New body")
        tablet.rename("Tablet (placeholder)")
        edges = cad.call("solid_find", object=tablet.name, kind="edges", limit=40)["hits"]
        cad.fillet([dict(object=tablet.name, edge=e["edge"]) for e in edges], radius=3)
        cad.call("material_apply", object=tablet.name, **GLASS)
        cad.rotate(tablet, 90 - TILT, axis="Y")
        # Slide it so the back face sits GAP off the rest face and the lowest corner stands 1 mm above the base.
        rest_info = find_faces(name, facing=rest_normal, min_size=8000)[0]
        centre = rest_info["center"]
        back = find_faces(tablet.name, facing=[-n for n in rest_normal])
        back = max(back, key=lambda f: f["area"])
        distance = sum(rest_normal[i] * (back["center"][i] - centre[i]) for i in range(3))
        move = [-(distance - GAP) * rest_normal[i] for i in range(3)]
        cad.move(tablet, x=move[0], y=-solid_info(tablet.name)["center"][1], z=move[2])
        along = [math.cos(angle), 0, math.sin(angle)]
        lowest = solid_info(tablet.name)["min"][2]
        shift = (1.0 - lowest) / along[2]
        cad.move(tablet, x=along[0] * shift, z=along[2] * shift)
    return "fold-stand", 40, 24


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/fold-stand.prism"))
            model = ("fold-stand", 40, 24)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=820)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
