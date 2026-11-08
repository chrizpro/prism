"""Meridian: a geared brass orrery, modeled and assembled natively through Prism MCP/cadkit.

The movement is real geometry, not a picture of one. A blued-steel layshaft carries five involute
gears that mesh with gears on six nested orbit tubes. Saturn is driven through a two-stage idler
from Jupiter's tube. Every arbor and orbit carrier turns on a revolute joint; planets ride on rigid joints.
Gear couplings use the modeled tooth counts, so turning the winding knob drives the planets at the ratios the
gears show.

python gallery/build_orrery.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math

import build_examples as study
import orrery_textures as tex

cad = study.cad

# ------------------------------------------------------------------ appearances
BRASS = dict(material="brass", color="#D9AE5F", metal=1, roughness=.2, pattern="Brushed", patternScale=1.5,
             surface=dict(Anisotropy=.35, Coat=.12))
GILT = dict(material="brass", color="#E6BE6E", metal=1, roughness=.14, surface=dict(Coat=.2, CoatRoughness=.08))
BLUED = dict(material="52100", color="#27406B", metal=.9, roughness=.18, surface=dict(Coat=.35, CoatRoughness=.1))
WALNUT = dict(material="walnut", color="#6B3F24", roughness=.32, pattern="Wood", patternScale=60, patternAngle=20,
              surface=dict(Coat=.75, CoatRoughness=.12, Bump=.05))
EBONY = dict(material="phenolic", color="#17110E", roughness=.25, surface=dict(Coat=.6))
SUN = dict(material="glass", color="#FFC46B", roughness=.32,
           surface=dict(Transmission=.92, Ior=1.45, AbsorptionColor=0xFFFF9A2E, AbsorptionDistance=45))

# ------------------------------------------------------------------ layout (mm, Z up)
PLINTH_TOP = 70
PLATE_T = 4
TOP_PLATE = 146          # bottom face of the top plate
SUN_Z, SUN_R = 320, 38   # orbital plane: every planet centre sits at SUN_Z
LAY_X, IDLER_X = 75, -63
GEAR_T = 4
KNOB_Z = TOP_PLATE + PLATE_T + 1.5   # 0.3 mm above the shaft collar; the rigid joint seats it

# name, carrier teeth, module, gear level, tube bore/outside radius, hub height, orbit radius, planet radius,
# layshaft teeth, display angle. Center distance is 75 mm on every layshaft mesh: m * (z1 + z2) / 2.
PLANETS = [
    ("Mercury", 20, 1.5, 80, 5.5, 7.5, 240, 68, 6.5, 80, -30),
    ("Venus", 38, 1.5, 89, 8.0, 10.0, 225, 98, 11, 62, -165),
    ("Earth", 50, 1.5, 98, 10.5, 12.5, 210, 140, 12, 50, 30),
    ("Mars", 65, 1.5, 107, 13.0, 15.0, 195, 185, 9, 35, 100),
    ("Jupiter", 138, 1.0, 116, 15.5, 17.5, 180, 235, 28, 12, -80),
    ("Saturn", 70, 1.0, 134, 18.0, 20.0, 165, 320, 21, None, 175),
]
# Saturn reduction: Jupiter tube gear A (42) -> idler B (84) | idler pinion C (56) -> Saturn gear D (70); 63 mm centres.
JUP_A, IDLER_B, IDLER_C, SAT_D = 42, 84, 56, 70
MOON_ORBIT, MOON_R, MOON_RATIO = 24, 3.6, (37, 3)   # moon carrier turns 37/3 = 12.33 times per Earth orbit
TEXTURE = dict(Mercury="mercury", Venus="venus", Earth="earth", Mars="mars", Jupiter="jupiter", Saturn="saturn")


# ------------------------------------------------------------------ helpers
SHOWN = []  # finished solids, hidden while each new body is created (see solo)


def _visible(names, on):
    if names:
        cad.call("batch", calls=[dict(tool="object_update", args=dict(object=n, visible=on)) for n in names])


def solo(make):
    """Create one body with the finished solids hidden. Extrude/Revolve/Pipe measure the exact distance from a new
    shape to every visible solid to choose New body or Cut; next to many-toothed wheels that takes minutes per preview."""
    _visible(SHOWN, False)
    try:
        body = make()
    finally:
        _visible(SHOWN, True)
    SHOWN.append(body.name)
    return body


def style(obj, look):
    cad.call("material_apply", object=obj.name if hasattr(obj, "name") else obj, **look)


def revolve(name, pts, x=0.0, y=0.0):
    """Closed (r, z) profile on the XZ plane revolved about world Z, then moved to (x, y)."""
    with cad.sketch("XZ") as s:
        s.polyline(pts, closed=True)
    body = solo(lambda: cad.revolve(s.obj, angle=360, axis="World Z", operation="New body"))
    body.rename(name)
    SHOWN[-1] = name
    cad.call("object_update", object=s.obj.name, visible=False)
    if x or y:
        cad.move(body, x=x, y=y)
    return body


def arc_pts(r, z, a0, a1, n):
    return [(r * math.cos(a0 + (a1 - a0) * i / n), z + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


def involute_entities(teeth, module, center=(0, 0), phase=0.0, backlash=.24):
    """Sketch entities for a 20-degree involute spur gear: one spline per tooth through sampled involute flanks
    and the tip, and a straight root land to the next tooth. Tooth k points at phase + k * pitch (radians).
    Teeth are thinned for backlash."""
    rp = teeth * module / 2
    ra, rf, rb = rp + module, rp - 1.25 * module, rp * math.cos(math.radians(20))
    inv = lambda a: math.tan(a) - a
    half = math.pi / (2 * teeth) + inv(math.acos(rb / rp)) - backlash / (2 * rp)
    r0 = max(rb, rf)
    flank = [(r, half - inv(math.acos(min(1.0, rb / r)))) for r in [r0 + (ra - r0) * i / 3 for i in range(4)]]
    pitch = 2 * math.pi / teeth
    at = lambda r, t: [center[0] + r * math.cos(t), center[1] + r * math.sin(t)]
    entities = []
    for k in range(teeth):
        c = phase + k * pitch
        pts = [at(rf, c - flank[0][1])] + [at(r, c - t) for r, t in flank[1:]] + [at(ra, c)]
        pts += [at(r, c + t) for r, t in reversed(flank[1:])] + [at(rf, c + flank[0][1])]
        entities.append(dict(type="spline", points=pts))
        entities.append(dict(type="line", points=[at(rf, c + flank[0][1]), at(rf, c + pitch - flank[0][1])]))
    return entities, rf


def gear(name, teeth, module, z0, bore, center=(0, 0), spokes=None, thick=GEAR_T, look=GILT, phase=0.0):
    """Involute gear sketched on XY at z0, with a bore and (for larger wheels) spoke windows. The generated
    sketch is deleted after the extrude: the script, not the sketch, is the gear's source."""
    entities, rf = involute_entities(teeth, module, center, phase)
    cx, cy = center
    entities.append(dict(type="circle", center=[cx, cy], radius=bore))
    rim_in, hub_out = rf - max(3.2, module * 2.2), bore + 5
    if spokes and rim_in - hub_out > 9:
        span = 2 * math.pi / spokes
        for k in range(spokes):
            win = []
            for r, sign in ((hub_out, 1), (rim_in, -1)):
                gap = 2.6 / r  # half spoke width in radians at this radius
                a0, a1 = k * span + gap, (k + 1) * span - gap
                seq = [a0 + (a1 - a0) * i / 10 for i in range(11)]
                win += [[cx + r * math.cos(a), cy + r * math.sin(a)] for a in (seq if sign > 0 else reversed(seq))]
            entities.append(dict(type="polyline", points=win, closed=True))
    cad.call("sketch_begin", plane="XY", offset=z0)
    cad.call("sketch_add", entities=entities)
    sketch = cad.get(cad.call("sketch_finish")["sketch"])
    body = solo(lambda: cad.extrude(sketch, distance=thick, operation="New body"))
    body.rename(name)
    SHOWN[-1] = name
    cad.delete(sketch)
    style(body, look)
    return body


def mesh_phase(teeth, display_deg, partner_teeth, toward):
    """Phase for a wheel on the central axis, before its carrier turns by display_deg, so that it meshes with a
    fixed-axis partner lying in direction `toward`. Partner tooth 0 points at +X; it faces the central axis with a
    tooth when that direction is a whole number of its pitches, otherwise with a gap."""
    facing = (toward + math.pi) % (2 * math.pi)            # partner's direction back to the central axis
    partner_tooth_faces = abs(math.remainder(facing, 2 * math.pi / partner_teeth)) < 1e-9
    gap = 0.0 if not partner_tooth_faces else math.pi / teeth
    return toward - math.radians(display_deg) + gap


def pipe_path(name, segments, radius):
    """Pipe along a line/arc chain sketched on the XZ plane. segments: ('line', a, b) | ('arc', a, mid, b)."""
    with cad.sketch("XZ") as s:
        for seg in segments:
            if seg[0] == "line":
                s.line(seg[1], seg[2])
            else:
                s.arc(seg[1], seg[2], seg[3])
    body = solo(lambda: cad.run("pipe", slots={"Path curve": s.obj.name}, radius_at_the_start=radius, radius_at_the_end=radius)[0])
    body.rename(name)
    SHOWN[-1] = name
    cad.call("object_update", object=s.obj.name, visible=False)
    return body


def bent_arm(name, r0, z0, R, z1, radius, bend=16):
    """Horizontal arm from the hub that turns up through a quarter arc into a vertical stem."""
    c = (R - bend, z0 + bend)
    mid = (c[0] + bend * math.cos(-math.pi / 4), c[1] + bend * math.sin(-math.pi / 4))
    return pipe_path(name, [("line", (r0, z0), (R - bend, z0)), ("arc", (R - bend, z0), mid, (R, z0 + bend)),
                            ("line", (R, z0 + bend), (R, z1))], radius)


def unite(base, *others):
    name = base.name
    cad.union(base, *others)
    for o in others:
        if o.name in SHOWN:
            SHOWN.remove(o.name)
    return cad.get(name)


def planet_body(name, r, zc, x, neck=2.2, equator=False):
    """Sphere revolved with a short neck: the neck's circular edge seats on the stem and carries the joint.
    With equator=True the profile is split at the equator so a ring can be jointed to the circular edge there."""
    zt = zc - r - 2.0
    zs = zc - math.sqrt(r * r - neck * neck)
    pts = [(0, zt), (neck, zt), (neck, zs)]
    a0 = math.asin((zs - zc) / r)
    if equator:
        pts += arc_pts(r, zc, a0, 0, 10)[1:] + arc_pts(r, zc, 0, math.pi / 2, 12)[1:]
    else:
        pts += arc_pts(r, zc, a0, math.pi / 2, 22)[1:]
    pts = [(max(p[0], 0.0), p[1]) for p in pts]
    pts[-1] = (0, zc + r)
    return revolve(name, pts, x=x)


def circle_edge(obj, radius, z, near_xy=None, tol=.02):
    """Index of the circular edge with this radius lying in the plane z (optionally centred near near_xy)."""
    hits = cad.call("solid_find", object=obj, kind="edges", type="Circle", radius=radius, radius_tol=tol,
                    box=[[-1e4, -1e4, z - .01], [1e4, 1e4, z + .01]], limit=40)["hits"]
    if near_xy is not None:
        hits = [h for h in hits if math.hypot(h["center"][0] - near_xy[0], h["center"][1] - near_xy[1]) <= radius + .05]
    assert hits, (obj, radius, z)
    return hits[0]["edge"]


def joint(kind, parent, pedge, child, cedge):
    r = cad.call("run_command", name="joint_" + kind,
                 slots={"On the first part (face, circle edge or round face)": {"object": parent, "edge": pedge},
                        "On the part that moves": {"object": child, "edge": cedge}})
    assert not r.get("statusIsError"), r
    made = [o["name"] for o in r.get("created", []) if o.get("kind") == "Joint"]
    assert made, r
    return made[0]


def couple(leader, follower, lead_teeth, follow_teeth, same_way=False):
    cad.call("run_command", name="gear_pair", commit=False)
    cad.call("command_set", params={"Leading joint": leader, "Following joint": follower,
                                    "Leading size (teeth, or pulley or pitch diameter)": lead_teeth,
                                    "Following size (same unit)": follow_teeth,
                                    "Direction": "Same way (belt, chain, internal gear)" if same_way else "Opposite way (external gears)"})
    r = cad.call("command_commit")
    assert not r.get("statusIsError"), r


# ------------------------------------------------------------------ the model
def frame():
    """Walnut plinth, enamel zodiac ring and the grounded brass movement frame."""
    plinth = revolve("Walnut plinth", [(0, 0), (262, 0), (262, 9), (256, 13), (256, 18)] + [(250, 22), (244, 30), (238, 40), (236, 48), (240, 54), (240, 58), (232, 62), (228, PLINTH_TOP), (0, PLINTH_TOP)])
    style(plinth, WALNUT)
    bezel = revolve("Brass plinth bezel", [(226, PLINTH_TOP - 1.5), (234, PLINTH_TOP - 1.5), (236, PLINTH_TOP + 1), (232, PLINTH_TOP + 2.5), (226, PLINTH_TOP + 2.5)])
    style(bezel, GILT)

    # Lower movement plate with pivot holes, stepped fixed arbor, six turned pillars, top plate with windows.
    with cad.sketch("XY", offset=PLINTH_TOP) as s:
        s.circle((0, 0), 168)
        s.circle((LAY_X, 0), 2.5)
    lower = solo(lambda: cad.extrude(s.obj, distance=PLATE_T, operation="New body"))
    lower.rename("Brass movement frame")
    SHOWN[-1] = lower.name
    cad.call("object_update", object=s.obj.name, visible=False)
    # The arbor steps down 0.2 mm at every gear level, hidden inside the bores: each step edge locates one orbit joint.
    steps = [(5.3, PLINTH_TOP + PLATE_T)] + [(5.3 - .2 * (i + 1), p[3]) for i, p in enumerate(PLANETS)]
    prof = [(0, PLINTH_TOP + 1)]
    for i, (r, z) in enumerate(steps):
        z_next = steps[i + 1][1] if i + 1 < len(steps) else 262
        prof += [(r, z if i else PLINTH_TOP + 1), (r, z_next)]
    # Sun column: a slim turned stem, a bead and a tulip cup that cradles the sun.
    prof += [(5.2, 262), (5.2, 266), (4.1, 268), (4.1, 274)] + [(4.1 + 17 * (t / 8) ** 1.6, 274 + 14 * t / 8) for t in range(1, 9)]
    prof += [(19.5, 288.6), (17.6, 286), (0, 286)]
    arbor = revolve("Fixed arbor", prof)
    pillars = []
    for k in range(6):
        a = math.radians(30 + 60 * k)
        pillars.append(revolve(f"Pillar {k + 1}", [(0, PLINTH_TOP + PLATE_T - .5), (5.5, PLINTH_TOP + PLATE_T - .5), (5.5, 80), (4, 84), (4.2, 100),
                                                  (6.4, 110), (4.2, 120), (4, 136), (5.5, 140), (5.5, TOP_PLATE + .5), (0, TOP_PLATE + .5)],
                               x=150 * math.cos(a), y=150 * math.sin(a)))
    with cad.sketch("XY", offset=TOP_PLATE) as s:
        s.circle((0, 0), 168)
        s.circle((0, 0), 20.8)
        s.circle((LAY_X, 0), 4.3)
        s.circle((IDLER_X, 0), 2.5)
        for k in range(6):
            mid = math.radians(30 + 60 * k)
            win = [(r * math.cos(mid + d), r * math.sin(mid + d)) for r, ds in ((34, 1), (138, -1))
                   for d in ([math.radians(x) for x in range(-24, 25, 4)][::ds])]
            s.polyline(win, closed=True)
    top = solo(lambda: cad.extrude(s.obj, distance=PLATE_T, operation="New body"))
    top.rename("Top plate")
    SHOWN[-1] = top.name
    cad.call("object_update", object=s.obj.name, visible=False)
    frame_body = unite(lower, arbor, top, *pillars)
    style(frame_body, BRASS)

    # Zodiac ring: brass band, celestial enamel inlay, raised degree marks and twelve sign medallions.
    ring = revolve("Zodiac ring", [(316, TOP_PLATE), (358, TOP_PLATE), (360, TOP_PLATE + 2), (358, TOP_PLATE + 4), (316, TOP_PLATE + 4), (314, TOP_PLATE + 2)])
    style(ring, GILT)
    inlay = revolve("Celestial enamel", [(324, TOP_PLATE + 3.6), (350, TOP_PLATE + 3.6), (350, TOP_PLATE + 4.8), (324, TOP_PLATE + 4.8)])
    style(inlay, dict(material="porcelain", color="#FFFFFF", roughness=.2,
                      surface=dict(ColorMap=tex.encoded("enamel"), Projection=1, TextureSize=180, Coat=1, CoatRoughness=.04)))
    tick = cad.box(width=7, depth=1.0, height=1.6, position_x=343.5, position_z=TOP_PLATE + 3.8, operation="New body")
    cad.run("pattern_circular", slots={"Objects to pattern": [tick.name], "Target body": ring.name}, axis="Z axis",
            axis_passes_through="World origin", count=72, result="Join into a body")
    major = cad.box(width=26, depth=1.4, height=1.8, position_x=337, position_z=TOP_PLATE + 3.8, operation="New body")
    cad.run("pattern_circular", slots={"Objects to pattern": [major.name], "Target body": ring.name}, axis="Z axis",
            axis_passes_through="World origin", count=12, result="Join into a body")
    medal = cad.cylinder(radius=4.2, height=2.2, position_x=334 * math.cos(math.radians(15)), position_y=334 * math.sin(math.radians(15)),
                         position_z=TOP_PLATE + 3.8, operation="New body")
    cad.run("pattern_circular", slots={"Objects to pattern": [medal.name], "Target body": ring.name}, axis="Z axis",
            axis_passes_through="World origin", count=12, result="Join into a body")
    # Six S-curved brackets carry the ring from the plinth.
    for k in range(6):
        b = pipe_path(f"Ring bracket {k + 1}", [("line", (222, PLINTH_TOP - 4), (222, PLINTH_TOP + 18)),
                                                ("arc", (222, PLINTH_TOP + 18), (222 + 40 * (1 - math.cos(math.pi / 4)), PLINTH_TOP + 18 + 40 * math.sin(math.pi / 4)), (262, PLINTH_TOP + 58)),
                                                ("line", (262, PLINTH_TOP + 58), (296, PLINTH_TOP + 58)),
                                                ("arc", (296, PLINTH_TOP + 58), (296 + 16 * math.sin(math.pi / 4), PLINTH_TOP + 58 + 16 * (1 - math.cos(math.pi / 4))), (312, PLINTH_TOP + 74)),
                                                ("line", (312, PLINTH_TOP + 74), (312, TOP_PLATE + .5))], 3.2)
        cad.run("rotate", slots={"Objects": [b.name]}, axis="Z", angle=60 * k, pivot="World origin")
        style(b, GILT)

    sun = solo(lambda: cad.sphere(radius=SUN_R, position_z=SUN_Z, operation="New body"))
    sun.rename("Sun")
    SHOWN[-1] = "Sun"
    style(sun, SUN)
    return frame_body


def layshaft():
    """Blued-steel layshaft with the five drive gears and a knurled ebony winding knob."""
    parts = []
    for name, teeth, module, z, *_rest in PLANETS:
        lay = _rest[-2]
        if lay is None:
            continue
        parts.append(gear(f"Layshaft {name} wheel", lay, module, z, 3.9, center=(LAY_X, 0), spokes=6 if lay * module > 60 else None, look=GILT))
    # Wheels first, then the shaft through them: a new solid that touches a many-toothed body makes Prism's automatic
    # New body/Cut check slow, so separate parts keep small gaps (backlash, knob clearance) until the joints close them.
    shaft = revolve("Layshaft", [(0, PLINTH_TOP), (2.5, PLINTH_TOP), (2.5, PLINTH_TOP + PLATE_T), (4, PLINTH_TOP + PLATE_T + .6),
                                 (4, TOP_PLATE + PLATE_T + 1), (5, TOP_PLATE + PLATE_T + 1.2), (0, TOP_PLATE + PLATE_T + 1.2)], x=LAY_X)
    body = unite(shaft, *parts)
    style(body, BLUED)
    # Shaft and wheels are one blued-steel part, as in a regulator movement; the ebony knob is a separate rigid part.
    knob = gear("Winding knob", 48, .6, KNOB_Z, 1.5, center=(LAY_X, 0), thick=6.5, look=EBONY)
    cap = revolve("Knob cap", [(0, KNOB_Z + 6.5), (11, KNOB_Z + 6.5), (11.6, KNOB_Z + 7.2), (9, KNOB_Z + 8.2), (0, KNOB_Z + 8.2)], x=LAY_X)
    knob = unite(knob, cap)
    style(knob, EBONY)
    return body, knob


def idler():
    # Hangs from the top plate and stops 2 mm above Jupiter's 138-tooth wheel, which sweeps past the idler axis.
    shaft = revolve("Saturn idler", [(0, 122), (2.6, 122), (3.5, 123), (3.5, TOP_PLATE), (2.5, TOP_PLATE),
                                     (2.5, TOP_PLATE + PLATE_T), (0, TOP_PLATE + PLATE_T)], x=IDLER_X)
    b = gear("Idler wheel", IDLER_B, 1.0, 125, 3.4, center=(IDLER_X, 0), spokes=5)
    c = gear("Idler pinion", IDLER_C, 1.0, 134, 3.4, center=(IDLER_X, 0), spokes=5)
    body = unite(shaft, b, c)
    style(body, BLUED)
    return body


def carrier(spec):
    name, teeth, module, zg, rin, rout, zh, R, pr, lay, angle = spec
    tube = revolve(f"{name} carrier", [(rin, zg), (rout, zg), (rout, zh - 5), (rout + 5, zh - 5), (rout + 5.8, zh), (rout + 5, zh + 5), (rin, zh + 5)])
    # The carrier is turned to its display angle afterwards, so phase each wheel now to seat a tooth in the
    # gap of its partner along the line of centres (layshaft at +X, idler at -X).
    pieces = [gear(f"{name} wheel", teeth, module, zg, rin, spokes=5 if teeth * module > 60 else None,
                   phase=mesh_phase(teeth, angle, partner_teeth=lay if lay else IDLER_C, toward=0 if lay else math.pi))]
    if name == "Jupiter":
        pieces.append(gear("Jupiter Saturn-drive wheel", JUP_A, 1.0, 125, rin, spokes=None,
                           phase=mesh_phase(JUP_A, angle, partner_teeth=IDLER_B, toward=math.pi)))
    stem_top = SUN_Z - pr - 2.0
    pieces.append(bent_arm(f"{name} arm", rout + 2, zh, R, stem_top, 3.2 if R > 200 else 2.8))
    if name == "Earth":
        # Bearing collar on the stem: its top edge locates the Moon's joint.
        pieces.append(revolve("Earth moon bearing", [(0, stem_top - 22), (4.6, stem_top - 22), (4.6, stem_top - 16), (0, stem_top - 16)], x=R))
    body = unite(tube, *pieces)
    style(body, GILT)
    return body, stem_top


def build():
    cad.call("document_new")
    SHOWN.clear()
    with cad.history("Meridian orrery - geared brass assembly"):
        fixed = frame()
        print("frame", flush=True)
        lay, knob = layshaft()
        print("layshaft", flush=True)
        sat_idler = idler()
        carriers, extras = {}, {}
        for spec in PLANETS:
            name, R, pr, angle = spec[0], spec[7], spec[8], spec[10]
            body, stem_top = carrier(spec)
            planet = planet_body(name, pr, SUN_Z, R, neck=2.4 if pr > 12 else 1.8, equator=name == "Saturn")
            style(planet, dict(material="abs", color="#FFFFFF", roughness=.42 if name != "Earth" else .3,
                               surface=dict(ColorMap=tex.encoded(TEXTURE[name]), Projection=2, TextureSize=2 * pr + 2, TextureOffsetZ=0, Coat=.35)))
            group = [body.name, planet.name]
            extra = {"planet": planet.name, "stem_top": stem_top}
            if name == "Saturn":
                rz = SUN_Z
                rings = revolve("Saturn rings", [(pr, rz), (pr + 4, rz + .9), (pr + 21, rz + .5), (pr + 24, rz), (pr + 21, rz - .5), (pr + 4, rz - .9)], x=R)
                style(rings, dict(material="porcelain", color="#FFFFFF", roughness=.45,
                                  surface=dict(ColorMap=tex.encoded("ring"), Projection=1, TextureSize=2 * (pr + 24),
                                               TextureOffsetX=0, TextureOffsetY=0, TextureRepeat=False, Coat=.2)))
                group.append(rings.name)
                extra["rings"] = rings.name
            if name == "Earth":
                zm = stem_top - 16
                collar = revolve("Moon carrier", [(2.9, zm), (6.5, zm), (6.5, zm + 6), (2.9, zm + 6)], x=R)
                arm = pipe_path("Moon arm", [("line", (R + 5, zm + 3), (R + MOON_ORBIT - 6, zm + 3)),
                                             ("arc", (R + MOON_ORBIT - 6, zm + 3), (R + MOON_ORBIT - 6 + 6 * math.sin(math.pi / 4), zm + 3 + 6 * (1 - math.cos(math.pi / 4))), (R + MOON_ORBIT, zm + 9)),
                                             ("line", (R + MOON_ORBIT, zm + 9), (R + MOON_ORBIT, SUN_Z - MOON_R - 2))], 1.3)
                moon_carrier = unite(collar, arm)
                style(moon_carrier, GILT)
                moon = planet_body("Moon", MOON_R, SUN_Z, R + MOON_ORBIT, neck=1.1)
                style(moon, dict(material="abs", color="#FFFFFF", roughness=.6,
                                 surface=dict(ColorMap=tex.encoded("moon"), Projection=2, TextureSize=2 * MOON_R + 2)))
                group += [moon_carrier.name, moon.name]
                extra.update(moon_carrier=moon_carrier.name, moon=moon.name, zm=zm)
            cad.run("rotate", slots={"Objects": group}, axis="Z", angle=angle, pivot="World origin")
            carriers[name], extras[name] = body.name, extra
            print("carrier", name, flush=True)

        # ---------------- assembly: parts, joints and gear couplings
        cad.call("run_command", name="make_part", slots={"Bodies": [fixed.name]})  # first part: grounded
        lay_j = joint("revolute", fixed.name, circle_edge(fixed.name, 2.5, PLINTH_TOP + PLATE_T, (LAY_X, 0)),
                      lay.name, circle_edge(lay.name, 2.5, PLINTH_TOP + PLATE_T))
        knob_top = TOP_PLATE + PLATE_T + 1.2
        joint("rigid", lay.name, circle_edge(lay.name, 5, knob_top), knob.name, circle_edge(knob.name, 1.5, KNOB_Z))
        idl_j = joint("revolute", fixed.name, circle_edge(fixed.name, 2.5, TOP_PLATE, (IDLER_X, 0)),
                      sat_idler.name, circle_edge(sat_idler.name, 2.5, TOP_PLATE))
        orbit = {}
        for i, spec in enumerate(PLANETS):
            name, teeth, _m, zg, rin = spec[:5]
            step_r = 5.3 - .2 * (i + 1)
            orbit[name] = joint("revolute", fixed.name, circle_edge(fixed.name, step_r, zg), carriers[name], circle_edge(carriers[name], rin, zg))
            ex = extras[name]
            R, angle = spec[7], math.radians(spec[10])
            stem_xy = (R * math.cos(angle), R * math.sin(angle))
            stem_r = 3.2 if R > 200 else 2.8
            joint("rigid", carriers[name], circle_edge(carriers[name], stem_r, ex["stem_top"], stem_xy),
                  ex["planet"], circle_edge(ex["planet"], 2.4 if spec[8] > 12 else 1.8, ex["stem_top"]))
            if "rings" in ex:
                joint("rigid", ex["planet"], circle_edge(ex["planet"], spec[8], SUN_Z), ex["rings"], circle_edge(ex["rings"], spec[8], SUN_Z))
            if "moon" in ex:
                zm = ex["zm"]
                moon_j = joint("revolute", carriers[name], circle_edge(carriers[name], 4.6, zm, stem_xy),
                               ex["moon_carrier"], circle_edge(ex["moon_carrier"], 2.9, zm))
                ma = angle
                moon_xy = (stem_xy[0] + MOON_ORBIT * math.cos(ma), stem_xy[1] + MOON_ORBIT * math.sin(ma))
                joint("rigid", ex["moon_carrier"], circle_edge(ex["moon_carrier"], 1.3, SUN_Z - MOON_R - 2, moon_xy),
                      ex["moon"], circle_edge(ex["moon"], 1.1, SUN_Z - MOON_R - 2))
        print("joints", flush=True)
        for spec in PLANETS:
            if spec[9] is not None:
                couple(lay_j, orbit[spec[0]], spec[9], spec[1])
        couple(orbit["Jupiter"], idl_j, JUP_A, IDLER_B)
        couple(idl_j, orbit["Saturn"], IDLER_C, SAT_D)
        couple(orbit["Earth"], moon_j, MOON_RATIO[0], MOON_RATIO[1], same_way=True)
        # A warm lamp inside the frosted glass sun makes it glow in renders.
        cad.call("run_command", name="lights", commit=False)
        cad.call("command_set", params={"Add lamp (point)": True})
        cad.call("command_set", params={"Position X": 0, "Position Y": 0, "Position Z": SUN_Z, "Power": 30000,
                                        "Colour temperature": 3200, "Lamp size (radius)": 12})
        cad.call("command_commit")
    return "meridian-orrery", -38, 20


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/meridian-orrery.prism"))
            model = ("meridian-orrery", -38, 20)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=1250)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
