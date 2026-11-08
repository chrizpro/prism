"""The visual scenarios. Each is named for what it tests and returns labeled pictures that ``visual.py`` lays out as one contact sheet."""
import json
import math
import tempfile
from pathlib import Path

from visual import cad, open_model, render, scenario, study, text_panel, toolpath_plot

ROOT = study.ROOT


def sketch(entities, plane="XY", offset=None):
    kw = {"plane": plane}
    if offset is not None:
        kw["offset"] = offset
    cad.call("sketch_begin", **kw)
    cad.call("sketch_add", entities=entities)
    return cad.get(cad.call("sketch_finish")["sketch"])


def gcode_of(zero="Saved CAM setup"):
    """Post every toolpath in the open document to a temporary file and return the program text."""
    path = Path(tempfile.gettempdir()) / "prism-visual.nc"
    path.unlink(missing_ok=True)
    cad.call("run_command", name="cnc_post", params={"Save to": str(path), "Work zero (G54)": zero}, commit=False)
    cad.call("command_set", params={"Save G-code": True})
    cad.call("command_cancel")
    return path.read_text() if path.exists() else ""


@scenario("solid__extrude-fillet-hole-pattern")
def solid_basics():
    cad.call("document_new")
    base = cad.extrude(sketch([{"type": "rect", "corner1": [-40, -25], "corner2": [40, 25]}]), distance=20)
    base.rename("Plate")
    edges = cad.call("solid_find", object="Plate", kind="edges", direction=[0, 0, 1], limit=10)["hits"]
    cad.fillet([dict(object="Plate", edge=e["edge"]) for e in edges], radius=8)
    cad.call("material_apply", object="Plate", material="steel", color="#9AA3AD")
    top = cad.call("solid_find", object="Plate", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
    cad.call("sketch_begin", plane=dict(object="Plate", face=top))
    cad.call("sketch_add", entities=[{"type": "circle", "center": [c, 0], "radius": 5} for c in (-22, 0, 22)])
    holes = cad.get(cad.call("sketch_finish")["sketch"])
    cad.run("extrude", slots={"Profile": holes.name}, distance=20, operation="Cut")
    info = cad.call("object_info", object="Plate")
    expected = (80 * 50 - (4 - math.pi) * 64 - 3 * math.pi * 25) * 20
    ok = info["valid"] and abs(info["volume"] - expected) < .01 * expected
    checks = ["Plate 80 x 50 x 20, R8 corners, three 10 mm holes", f"valid: {info['valid']}   volume {info['volume']:.0f} (expected {expected:.0f})", f"CHECK volume within 1%: {'PASS' if ok else 'FAIL'}"]
    return [("iso", render(-35, 30)), ("from the front, low", render(15, 8)), ("checks", text_panel(checks))]


@scenario("solid__revolve-bottle-and-shelled-box")
def revolve_shell():
    cad.call("document_new")
    prof = sketch([{"type": "polyline", "points": [[0, 0], [20, 0], [20, 40], [8, 55], [8, 75], [10, 75], [10, 80], [0, 80]], "closed": True}], plane="XZ")
    bottle = cad.revolve(prof, angle=360, axis="World Z")
    info1 = cad.call("object_info", object=cad.objects()[-1].name)
    first = render(-35, 20)
    cad.call("document_new")
    box = cad.extrude(sketch([{"type": "rect", "corner1": [-30, -20], "corner2": [30, 20]}]), distance=30)
    box.rename("Box")
    top = cad.call("solid_find", object="Box", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
    cad.shell([dict(object="Box", face=top)], thickness=3)
    info2 = cad.call("object_info", object="Box")
    expected = 60 * 40 * 30 - 54 * 34 * 27
    ok = info1["valid"] and info2["valid"] and abs(info2["volume"] - expected) < .01 * expected
    checks = [f"revolved bottle valid: {info1['valid']}, volume {info1['volume']:.0f}", f"shelled box volume {info2['volume']:.0f} (expected {expected:.0f})", f"CHECK both valid, shell volume within 1%: {'PASS' if ok else 'FAIL'}"]
    return [("revolved bottle profile", first), ("box shelled open at the top, 3 mm wall", render(-35, 40)), ("checks", text_panel(checks))]


@scenario("sketch__draw-on-curved-surface")
def sketch_on_surface():
    cad.call("document_new")
    ball = cad.sphere(radius=30)
    ball.rename("Ball")
    cad.call("run_command", name="create_sketch", slots={"Plane or flat face": [{"object": "Ball", "face": 1}]}, commit=True)
    cad.call("sketch_add", entities=[{"type": "circle", "center": [0, 0], "radius": 10}, {"type": "polyline", "points": [[-14, -14], [14, -14], [14, 14], [-14, 14]], "closed": True}])
    cad.call("sketch_finish")
    names = [o.name for o in cad.objects()]
    ok = any(n.startswith("Curve on surface") for n in names)
    curve = next(o for o in cad.objects() if o.name.startswith("Curve on surface"))
    box = cad.call("object_info", object=curve.name)
    far = max(math.dist([0, 0, 0], p) for p in (box["min"], box["max"]))
    ok = ok and 20 < far < 45
    names.append(f"curve box {[round(v, 1) for v in box['min']]} to {[round(v, 1) for v in box['max']]}")
    return [("curves drawn on the sphere", render(-30, 35, background="White")), ("seen from above", render(0, 89)),
            ("checks", text_panel(["Create Sketch on a curved face, draw a circle and a square, finish", f"objects: {names}", f"CHECK a 'Curve on surface' exists: {'PASS' if ok else 'FAIL'}"]))]


@scenario("mold__create-mold-exploded-reference-part")
def mold():
    cad.call("document_new")
    block = cad.box(width=100, depth=70, height=60)
    block.rename("Block")
    cap = cad.cylinder(radius=18, height=30)
    cad.move(cap, x=0, y=0, z=15)
    cap.rename("Cap")
    cad.call("run_command", name="mold", slots={"Mold block": "Block", "Part to cast": "Cap"}, params={"Parting direction": "Z", "Split at": 50, "Explode distance": 50, "Shrinkage allowance": 2, "Cavity offset (clearance)": 0.5})
    halves = [o for o in cad.objects() if o.kind == "Solid" and o.name != "Cap"]
    infos = [cad.call("object_info", object=h.name) for h in halves]
    return [("mold halves apart, part orange", render(-35, 22)), ("from below the upper half", render(-35, -30)), ("from the side", render(0, 4)),
            ("checks", text_panel([f"bodies: {[o.name for o in cad.objects()]}", f"halves: {[round(i['volume']) for i in infos]}", f"CHECK two halves + the part: {'PASS' if len(halves) == 2 else 'FAIL'}"]))]


def two_views(model, a=(-35, 28), b=(0, 89), la="iso", lb="top", distance=None):
    open_model(model)
    return [(la, render(*a, distance=distance)), (lb, render(*b, distance=distance))]


@scenario("sheetmetal__lotus-dish-folded-and-flat-pattern")
def lotus():
    return two_views("lotus-dish", (-35, 32), (0, 89), "folded dish and flat pattern", "from above", distance=330)


@scenario("sheetmetal__cardboard-box-and-die-line")
def cardboard():
    return two_views("cardboard-box", (-38, 30), (0, 89), "box and die line", "from above", distance=520)


@scenario("sheetmetal__tablet-stand-flange-hem-vents")
def stand():
    return two_views("fold-stand", (40, 24), (0, 89), "stand and flat pattern", "from above", distance=560)


@scenario("cam__serving-board-toolpath-plot-and-stock-review")
def cam_board():
    open_model("serving-board")
    part = render(-30, 38, distance=480)
    text = gcode_of()
    cad.call("run_command", name="cnc_stock_review", slots={"Model": "Serving board"}, commit=False)
    cad.call("command_set", params={"Run checks": True}); cad.call("wait_idle", timeout_ms=60000)
    rows = cad.call("command_set", params={}).get("results", [])
    cad.call("command_cancel")
    ok = any(r.startswith("Possible gouges: 0") for r in rows) and any(r.startswith("Rapid contacts: 0") for r in rows)
    return [("the part", part), ("toolpath from the posted G-code (deeper = redder)", toolpath_plot(text, title="board")),
            ("stock review", text_panel([str(r)[:80] for r in rows] + [f"CHECK no gouges or rapid contacts: {'PASS' if ok else 'FAIL'}"]))]


@scenario("cam__image-carve-lotus-toolpath-depth-plot")
def cam_relief():
    open_model("relief-plaque")
    text = gcode_of()
    zs = [float(w[1:]) for line in text.splitlines() for w in line.split() if w[:1] == "Z" and len(w) > 1 and line.startswith("G1")]
    deepest = min(zs) if zs else 0
    # the darkest pixel of the picture is 15% grey, so the deepest cut is 85% of the 3 mm carve
    checks = [f"cutting moves: {len(zs)}", f"deepest cut Z {deepest:.2f} (expected about -2.55: 85% of 3 mm, the picture's darkest pixel)", f"CHECK deepest cut within 0.1 of -2.55: {'PASS' if -2.65 < deepest < -2.45 else 'FAIL'}"]
    return [("toolpath from the posted G-code (deeper = redder)", toolpath_plot(text, title="lotus carve")), ("checks", text_panel(checks))]


@scenario("cae__wrench-static-cases-results-table")
def cae_wrench():
    open_model("wrench")
    shot = render(-35, 45, distance=330)
    lines = []
    ok = True
    result = cad.call("study_run")["results"]
    lines += [f"active study: {cad.call('study_state')['name']}", f"von Mises {result['maxVonMises_MPa']:.1f} MPa   displacement {result['maxDisplacement_mm']:.3f} mm",
              f"safety factor {result['minSafetyFactor']:.2f}   elements {result['elements']}", f"reaction {result['reactionNet_N']:.1f} N vs load {result['appliedNet_N']:.1f} N"]
    ok = abs(result["reactionNet_N"] - result["appliedNet_N"]) < 1 and 1 < result["minSafetyFactor"] < 3
    lines.append(f"CHECK reactions balance, 1 < safety factor < 3: {'PASS' if ok else 'FAIL'}")
    return [("the wrench", shot), ("open-end torque study, solved after reopening the file", text_panel(lines))]


@scenario("assembly__orrery-assembled-and-exploded")
def orrery():
    open_model("meridian-orrery")
    whole = render(-38, 20, distance=1250)
    cad.call("run_command", name="explode", params={"Explode": 0.9}, commit=True)
    cad.call("wait_idle")
    exploded = render(-38, 20, distance=2300)
    cad.call("run_command", name="explode", params={"Explode": 0.0}, commit=True)
    return [("assembled", whole), ("exploded view", exploded)]


@scenario("gallery__every-model-opens-and-renders")
def gallery_overview():
    catalog = json.loads((ROOT / "gallery/catalog.json").read_text(encoding="utf-8"))
    cells = []
    for item in catalog["Items"]:
        try:
            open_model(item["Id"])
            cells.append((item["Id"], render(-35, 28, samples=4)))
        except Exception as error:
            cells.append((item["Id"] + " FAILED", text_panel([str(error)[:300]])))
    return cells


# ---------------------------------------------------------------- feature matrices: every option of a tool, side by side in one document
VARIANTS = set(range(8))     # which Extrude variants to build; narrowed when hunting a bug


def new_solids(before):
    return [o for o in cad.objects() if o.kind == "Solid" and o.name not in before]


def names():
    return {o.name for o in cad.objects()}


def zrange(name):
    i = cad.call("object_info", object=name)
    return round(i["min"][2], 2), round(i["max"][2], 2), i


def close(a, b, tol=.05):
    return abs(a - b) <= tol


@scenario("solid__extrude-every-extent-and-operation", columns=1)
def extrude_matrix():
    """One 30 mm square profile per variant, 60 mm apart along X: each of Extrude's extents, reverse, taper and operations."""
    cad.call("document_new")
    rows = []      # (label, expectation text, passed)

    def profile(i, size=30, offset=None):
        return sketch([{"type": "rect", "corner1": [i * 60, 0], "corner2": [i * 60 + size, size]}], offset=offset).name

    def block(i, height=20):
        b = cad.extrude(sketch([{"type": "rect", "corner1": [i * 60, 0], "corner2": [i * 60 + 30, 30]}]), distance=height, operation="New body")
        b.rename(f"Base {i}")
        return b.name

    def extrude(i, label, params, expect, slots=None, operation="New body", on=None):
        if i not in VARIANTS:
            return
        before = names()
        p = {"Operation": operation, **params}
        try:
            cad.call("run_command", name="extrude", slots={"Profile": profile(i), **(slots or {})}, params=p)
        except Exception as error:
            rows.append((label, str(error)[:110], False)); return
        made = new_solids(before)
        target = on or (made[0].name if made else None)
        if target is None or target not in names():
            rows.append((label, "no solid made", False)); return
        lo, hi, info = zrange(target)
        ok, text = expect(lo, hi, info)
        rows.append((label, text, ok and info["valid"]))

    extrude(0, "Distance 20", {"Extent": "Distance", "Distance": 20}, lambda lo, hi, i: (close(lo, 0) and close(hi, 20), f"z {lo}..{hi} (expect 0..20)"))
    extrude(1, "Distance 20, reversed", {"Extent": "Distance", "Distance": 20, "Reverse direction": True}, lambda lo, hi, i: (close(lo, -20) and close(hi, 0), f"z {lo}..{hi} (expect -20..0)"))
    extrude(2, "Two sides 20 + 10", {"Extent": "Two sides", "Distance": 20, "Distance 2 (other side)": 10}, lambda lo, hi, i: (close(hi - lo, 30) and (close(lo, -10) or close(lo, -20)), f"z {lo}..{hi} (expect 30 tall, 20 one way and 10 the other)"))
    extrude(3, "Symmetric 20", {"Extent": "Symmetric", "Distance": 20}, lambda lo, hi, i: (close(lo, -hi) and hi > 0, f"z {lo}..{hi} (expect centred on 0)"))
    # to object: a plate hovering at z 40..44 over variant 4
    plate = cad.extrude(sketch([{"type": "rect", "corner1": [4 * 60 - 5, -5], "corner2": [4 * 60 + 35, 35]}], offset=40), distance=4)
    plate.rename("Stop plate")
    underside = cad.call("solid_find", object="Stop plate", kind="faces", type="Plane", facing=[0, 0, -1])["hits"][0]["face"]
    extrude(4, "To object (underside of a plate at z 40)", {"Extent": "To object"}, lambda lo, hi, i: (close(lo, 0) and close(hi, 40, .2), f"z {lo}..{hi} (expect 0..40)"),
            slots={"Extend to (face, plane or body)": [{"object": "Stop plate", "face": underside}]})
    straight = 30 * 30 * 20
    extrude(5, "Distance 20, taper 10 degrees", {"Extent": "Distance", "Distance": 20, "Taper angle": 10}, lambda lo, hi, i: (close(hi, 20) and i["volume"] < straight * .85, f"volume {i['volume']:.0f} vs straight {straight} (expect less)"))
    base = block(6)
    cut_profile = sketch([{"type": "circle", "center": [6 * 60 + 15, 15], "radius": 6}]).name
    expect = 30 * 30 * 20 - math.pi * 36 * 20
    try:
        preview = cad.call("run_command", name="extrude", slots={"Profile": cut_profile}, params={"Extent": "Through all, both ways", "Operation": "Cut"}, commit=False)
        notes = [str(r.get("value")) for r in (preview.get("activeCommand") or {}).get("results", [])]
        cad.call("command_commit")
        v = cad.call("object_info", object=base)["volume"]
        everything = {o.name: round(cad.call("object_info", object=o.name)["volume"]) for o in cad.objects() if o.kind == "Solid"}
        rows.append(("Cut, through all both ways (6 mm hole)", f"volume {v:.0f} (expect {expect:.0f})" + ("" if close(v, expect, expect * .01) else f"; preview said {notes}; all bodies {everything}"), close(v, expect, expect * .01)))
    except Exception as error:
        rows.append(("Cut, through all both ways (6 mm hole)", str(error)[:110], False))
    base = block(7)
    join_profile = sketch([{"type": "rect", "corner1": [7 * 60 + 8, 8], "corner2": [7 * 60 + 22, 22]}], offset=20).name
    try:
        cad.call("run_command", name="extrude", slots={"Profile": join_profile}, params={"Extent": "Distance", "Distance": 10, "Operation": "Join"})
        lo, hi, info = zrange(base)
        rows.append(("Join a 14 mm boss onto the base", f"z {lo}..{hi}, volume {info['volume']:.0f} (expect 0..30, {30 * 30 * 20 + 14 * 14 * 10})", close(hi, 30) and close(info["volume"], 30 * 30 * 20 + 14 * 14 * 10, 30)))
    except Exception as error:
        rows.append(("Join a 14 mm boss onto the base", str(error)[:110], False))
    for o in cad.objects():
        if o.kind == "Sketch":
            cad.call("object_update", object=o.name, visible=False)
    strip = ["Left to right: " + " | ".join(r[0] for r in rows)] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("iso, all variants", render(-25, 32, samples=None)), ("from the front: heights and the z = 0 base line", render(0, 2)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- revolve, loft and sweep matrices
def curve(points, plane="XY", offset=0, closed=False):
    """A curve object through points (Loft and Sweep take curves, not sketches). Give the points' real z: the Plane offset is ignored when points are supplied."""
    before = names()
    cad.call("run_command", name="curve_through", points=points, params={"Draw on plane": {"XY": "XY (ground)", "XZ": "XZ (front)", "YZ": "YZ (side)"}[plane], "Plane offset": offset, "Closed": closed})
    return next(o for o in cad.objects() if o.name not in before and o.kind == "Curve").name


def ring(z, r, n=16):
    return curve([[r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n), z] for i in range(n)], "XY", 0, closed=True)


class Matrix:
    """Collects (label, detail, passed) rows for the text strip; each variant is built at the origin, checked, then moved to its own slot along X."""

    def __init__(self, spacing=70):
        self.rows, self.slot, self.spacing = [], 0, spacing

    def check(self, label, fn):
        try:
            name, detail, ok = fn()
        except Exception as error:
            self.rows.append((label, str(error)[:120], False)); return
        if name:
            cad.move(cad.get(name), x=self.slot * self.spacing)
        self.slot += 1
        self.rows.append((label, detail, ok))

    def strip(self):
        return ["Left to right: " + " | ".join(r[0] for r in self.rows)[:140]] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in self.rows]


def made_since(before):
    return next((o.name for o in cad.objects() if o.kind == "Solid" and o.name not in before), None)


@scenario("solid__revolve-every-extent-axis-and-operation", columns=1)
def revolve_matrix():
    cad.call("document_new")
    m = Matrix()
    ring_area = math.pi * (20 ** 2 - 10 ** 2)
    full = ring_area * 30

    def rect(x0, x1, z0, z1, plane="XZ"):
        return sketch([{"type": "rect", "corner1": [x0, z0], "corner2": [x1, z1]}], plane=plane).name

    def revolve(label, params, expect, profile=None, tol=.01):
        def run():
            before = names()
            cad.call("run_command", name="revolve", slots={"Profile": profile or rect(10, 20, 0, 30)}, params={"Operation": "New body", **params})
            n = made_since(before)
            info = cad.call("object_info", object=n)
            return n, f"volume {info['volume']:.0f} (expect {expect:.0f})", info["valid"] and abs(info["volume"] - expect) <= tol * expect
        m.check(label, run)

    revolve("Full 360", {"Extent": "Full 360°"}, full)
    revolve("Angle 180", {"Extent": "Angle", "Angle": 180}, full / 2)
    revolve("Angle 90, reversed", {"Extent": "Angle", "Angle": 90, "Reverse direction": True}, full / 4)
    revolve("Two sides 90 + 45", {"Extent": "Two sides", "Angle": 90, "Angle 2 (other side)": 45}, full * 135 / 360)
    revolve("Symmetric 120", {"Extent": "Symmetric", "Angle": 120}, full * 120 / 360)
    revolve("About World X (profile becomes a disc)", {"Extent": "Full 360°", "Axis": "World X"}, math.pi * 30 ** 2 * 10)

    def groove():
        base = cad.cylinder(radius=25, height=30, operation="New body"); base.rename("Groove base")
        cad.call("run_command", name="revolve", slots={"Profile": rect(20, 25, 10, 20)}, params={"Extent": "Full 360°", "Operation": "Cut"})
        info = cad.call("object_info", object="Groove base")
        expect = math.pi * 25 ** 2 * 30 - math.pi * (25 ** 2 - 20 ** 2) * 10
        return "Groove base", f"volume {info['volume']:.0f} (expect {expect:.0f})", abs(info["volume"] - expect) < .01 * expect
    m.check("Cut a groove from a cylinder", groove)

    def boss():
        base = cad.cylinder(radius=10, height=10, operation="New body"); base.rename("Boss base")
        cad.call("run_command", name="revolve", slots={"Profile": rect(10, 15, 0, 10)}, params={"Extent": "Full 360°", "Operation": "Join"})
        info = cad.call("object_info", object="Boss base")
        expect = math.pi * 15 ** 2 * 10
        return "Boss base", f"volume {info['volume']:.0f} (expect {expect:.0f})", abs(info["volume"] - expect) < .01 * expect
    m.check("Join a ring onto a cylinder", boss)
    for o in cad.objects():
        if o.kind == "Sketch":
            cad.call("object_update", object=o.name, visible=False)
    return [("iso, all variants", render(-25, 30)), ("from above", render(0, 89)), ("checks", text_panel(m.strip()))]


@scenario("solid__loft-sections-ruled-and-bulge", columns=1)
def loft_matrix():
    cad.call("document_new")
    m = Matrix()
    cylinder = math.pi * 10 ** 2 * 30

    def loft(label, radii, expect, ruled=False, tol=.04):
        def run():
            before = names()
            sections = [ring(i * 30 / (len(radii) - 1), r) for i, r in enumerate(radii)]
            cad.call("run_command", name="loft", slots={"Sections (in order)": sections}, params={"Straight (ruled) transitions": ruled})
            n = made_since(before)
            info = cad.call("object_info", object=n)
            return n, f"volume {info['volume']:.0f} (expect about {expect:.0f}), height {info['size'][2]:.1f}", info["valid"] and abs(info["volume"] - expect) <= tol * expect and abs(info["size"][2] - 30) < .5
        m.check(label, run)

    loft("Two equal circles (cylinder)", [10, 10], cylinder)
    loft("Taper 10 to 4 (cone frustum)", [10, 4], math.pi / 3 * 30 * (100 + 40 + 16))
    loft("Three sections 6-12-6, ruled", [6, 12, 6], 2 * math.pi / 3 * 15 * (36 + 72 + 144), ruled=True)
    # a smooth loft through radii 6, 12, 6 follows the parabola r(u) = 6 + 24 u (1 - u): volume = 30 pi * integral of r^2 = 30 pi * 103.2
    loft("Three sections 6-12-6, smooth (parabolic bulge)", [6, 12, 6], 30 * math.pi * 103.2, tol=.02)
    for o in cad.objects():
        if o.kind == "Curve":
            cad.call("object_update", object=o.name, visible=False)
    return [("iso, all variants", render(-25, 30)), ("from the front", render(0, 5)), ("checks", text_panel(m.strip()))]


@scenario("solid__sweep-straight-and-curved-path", columns=1)
def sweep_matrix():
    cad.call("document_new")
    m = Matrix(spacing=90)
    area = math.pi * 25

    def sweep(label, path_points, length, plane="XY", tol=.03):
        def run():
            before = names()
            profile = ring(0, 5)
            path = curve(path_points, plane, 0)
            cad.call("run_command", name="sweep", slots={"Profile": profile, "Path": path})
            n = made_since(before)
            info = cad.call("object_info", object=n)
            return n, f"volume {info['volume']:.0f} (expect {area * length:.0f})", info["valid"] and abs(info["volume"] - area * length) <= tol * area * length
        m.check(label, run)

    sweep("Straight path 40", [[0, 0, 0], [0, 0, 20], [0, 0, 40]], 40)
    quarter = [[30 * (1 - math.cos(t * math.pi / 2 / 8)), 0, 30 * math.sin(t * math.pi / 2 / 8)] for t in range(9)]
    sweep("Quarter-circle path R30", quarter, 30 * math.pi / 2, tol=.05)
    for o in cad.objects():
        if o.kind == "Curve":
            cad.call("object_update", object=o.name, visible=False)
    return [("iso, both sweeps", render(-30, 30)), ("from above", render(0, 89)), ("checks", text_panel(m.strip()))]


@scenario("solid__fillet-chamfer-shell-every-option", columns=1)
def fillet_chamfer_shell():
    cad.call("document_new")
    m = Matrix(spacing=60)
    box_volume = 40 * 40 * 20
    corner = lambda size: (1 - math.pi / 4) * size ** 2 * 20      # material a round of this radius removes along a 20 mm edge

    cur = [""]

    def on_box(label, fn):
        def run():
            cur[0] = f"T{m.slot}"
            b = cad.box(width=40, depth=40, height=20, operation="New body"); b.rename(cur[0])
            return fn()
        m.check(label, run)

    def edges(vertical_only=True, count=None):
        hits = cad.call("solid_find", object=cur[0], kind="edges", limit=60, **({"direction": [0, 0, 1]} if vertical_only else {}))["hits"]
        return [dict(object=cur[0], edge=h["edge"]) for h in hits[:count]]

    def finish(expect=None, within=None, lo=None, hi=None):
        info = cad.call("object_info", object=cur[0])
        if expect is not None:
            ok = abs(info["volume"] - expect) <= (within or .005 * box_volume)
            detail = f"volume {info['volume']:.0f} (expect {expect:.0f})"
        else:
            ok = lo < info["volume"] < hi
            detail = f"volume {info['volume']:.0f} (expect between {lo:.0f} and {hi:.0f})"
        return cur[0], detail, ok and info["valid"]

    on_box("Fillet 1 edge R5", lambda: (cad.call("run_command", name="fillet", slots={"Edges": edges(count=1)}, params={"Radius": 5}), finish(box_volume - corner(5), 15))[1])
    on_box("Fillet 4 vertical edges R5", lambda: (cad.call("run_command", name="fillet", slots={"Edges": edges()}, params={"Radius": 5}), finish(box_volume - 4 * corner(5), 40))[1])
    on_box("Fillet 1 edge, 2 to 8 (variable)", lambda: (cad.call("run_command", name="fillet", slots={"Edges": edges(count=1)}, params={"Radius": 2, "End radius (0 = same as radius)": 8}), finish(lo=box_volume - corner(8), hi=box_volume - corner(2)))[1])
    on_box("Fillet all 12 edges R3", lambda: (cad.call("run_command", name="fillet", slots={"Edges": edges(False)}, params={"Radius": 3}), finish(lo=box_volume * .9, hi=box_volume * .99))[1])
    on_box("Chamfer 1 edge 5", lambda: (cad.call("run_command", name="chamfer", slots={"Edges": edges(count=1)}, params={"Distance": 5}), finish(box_volume - .5 * 25 * 20, 15))[1])
    on_box("Chamfer 4 vertical edges 5", lambda: (cad.call("run_command", name="chamfer", slots={"Edges": edges()}, params={"Distance": 5}), finish(box_volume - 4 * .5 * 25 * 20, 40))[1])

    def top():
        return cad.call("solid_find", object=cur[0], kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
    on_box("Shell inside 3, top open", lambda: (cad.call("run_command", name="shell", slots={"Faces to remove": [dict(object=cur[0], face=top())]}, params={"Wall thickness": 3, "Direction": "Inside"}), finish(box_volume - 34 * 34 * 17, 40))[1])
    on_box("Shell outside 3, top open", lambda: (cad.call("run_command", name="shell", slots={"Faces to remove": [dict(object=cur[0], face=top())]}, params={"Wall thickness": 3, "Direction": "Outside"}), finish(lo=46 * 46 * 23 - box_volume - 1000, hi=46 * 46 * 23 - box_volume))[1])   # an outside offset rounds the new outer edges, so a little less than the flat-cornered 46 x 46 x 23 box
    return [("iso, all variants", render(-25, 30)), ("from above", render(0, 89)), ("checks", text_panel(m.strip()))]


# ---------------------------------------------------------------- CAM: every option of the milling operations, read back from the posted G-code
def cut_moves(gcode):
    """The (x, y, z) end points of the cutting (G1) moves of a program."""
    x = y = z = 0.0
    out = []
    for raw in gcode.splitlines():
        words = {w[0]: w[1:] for w in raw.split("(")[0].split() if len(w) > 1 and w[0] in "GXYZ"}
        feed = words.get("G") in ("1", "01")
        x, y, z = (float(words[a]) if a in words else v for a, v in (("X", x), ("Y", y), ("Z", z)))
        if feed and any(a in words for a in "XYZ"):
            out.append((x, y, z))
    return out


def area(points):
    return .5 * sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(points, points[1:] + points[:1]))


@scenario("cam__milling-every-option-from-the-gcode")
def cam_matrix():
    cells, rows = [], []

    def variant(label, build, check):
        cad.call("document_new")
        try:
            outline = sketch([{"type": "rect", "corner1": [0, 0], "corner2": [40, 30]}]).name
            build(outline)
            text = gcode_of("The model's own origin")
            moves = cut_moves(text)
            ok, detail = check(moves)
            cells.append((label, toolpath_plot(text, title=label[:18])))
        except Exception as error:
            ok, detail = False, str(error)[:110]
            cells.append((label + " FAILED", text_panel([detail])))
        rows.append((label, detail, ok))

    def extents(m):
        xs, ys = [p[0] for p in m], [p[1] for p in m]
        return min(xs), max(xs), min(ys), max(ys)

    def near(got, want, tol=.3):
        return all(abs(a - b) <= tol for a, b in zip(got, want))

    def profile(params):
        return lambda outline: cad.call("run_command", name="cnc_profile", slots={"Outlines (curves, sketches, edges or flat faces)": [outline]}, params={"Tool diameter": 6, "Total cutting depth": 6, **params})

    variant("Profile, outside the line", profile({"Cut": "Outside the line"}), lambda m: (near(extents(m), (-3, 43, -3, 33)), f"extents {tuple(round(v, 1) for v in extents(m))} (expect -3..43, -3..33)"))
    variant("Profile, inside the line", profile({"Cut": "Inside the line"}), lambda m: (near(extents(m), (3, 37, 3, 27)), f"extents {tuple(round(v, 1) for v in extents(m))} (expect 3..37, 3..27)"))
    variant("Profile, on the line", profile({"Cut": "On the line"}), lambda m: (near(extents(m), (0, 40, 0, 30)), f"extents {tuple(round(v, 1) for v in extents(m))} (expect 0..40, 0..30)"))
    variant("Profile, climb (outside)", profile({"Cut": "Outside the line", "Direction": "Climb"}), lambda m: (area([p[:2] for p in m if abs(p[2] - m[-1][2]) < .01]) > 0, "outside climb runs counter-clockwise"))
    variant("Profile, conventional (outside)", profile({"Cut": "Outside the line", "Direction": "Conventional"}), lambda m: (area([p[:2] for p in m if abs(p[2] - m[-1][2]) < .01]) < 0, "outside conventional runs clockwise"))
    variant("Profile, 4 holding tabs", profile({"Cut": "Outside the line", "Holding tabs": 4, "Tab width": 6, "Tab height": 2}), lambda m: (any(abs(p[2] - (-4)) < .01 for p in m) and min(p[2] for p in m) <= -5.99, f"deepest {min(p[2] for p in m):.1f}, tab level z -4 {'present' if any(abs(p[2] + 4) < .01 for p in m) else 'missing'}"))
    variant("Pocket, tool 6, stepover 45", lambda o: cad.call("run_command", name="cnc_pocket", slots={"Outlines of the pocket (islands inside are left)": [o]}, params={"Tool diameter": 6, "Total cutting depth": 3, "Stepover": 45}), lambda m: (near(extents(m), (3, 37, 3, 27)), f"extents {tuple(round(v, 1) for v in extents(m))} (expect 3..37, 3..27), deepest {min(p[2] for p in m):.1f}"))

    def drill(o):
        plate = cad.box(width=40, depth=30, height=10, operation="New body"); plate.rename("Drill plate")
        cad.call("sketch_begin", plane="XY"); cad.call("sketch_add", entities=[{"type": "circle", "center": [0, 0], "radius": 2.5}]); hole = cad.get(cad.call("sketch_finish")["sketch"])
        cad.run("extrude", slots={"Profile": hole.name}, distance=20, operation="Cut", extent="Symmetric")
        face = cad.call("solid_find", object="Drill plate", kind="faces", type="Cylinder", limit=4)["hits"][0]["face"]
        cad.call("run_command", name="cnc_drill", slots={"Holes (round faces or circular edges)": [{"object": "Drill plate", "face": face}]}, params={"Tool diameter": 5, "Top of the material (Z)": 10, "Total cutting depth": 8, "Drill each hole as deep as it is": False, "Peck depth (0 = straight in)": 3})
    variant("Drill, 3 mm pecks to 8 below the top", drill, lambda m: (abs(min(p[2] for p in m) - 2) < .01 and len({round(p[2], 2) for p in m}) >= 3, f"deepest z {min(p[2] for p in m):.1f} (the plate top is z 10, so 8 deep ends at 2), {len({round(p[2], 2) for p in m})} depth levels from 3 mm pecks"))

    def face_ok(m):
        centres = sorted({round(p[0], 2) for p in m})            # passes run along Y, so each pass is one x
        gaps = [b - a for a, b in zip(centres, centres[1:])]
        covered = centres[0] - 6 <= 0 and centres[-1] + 6 >= 60 and all(g <= 12 + .01 for g in gaps)
        return covered, f"{len(centres)} passes, x centres {centres[0]}..{centres[-1]}, largest gap {max(gaps):.1f} (tool 12, so the 0..60 area is covered)"
    variant("Face along Y, stepover 70", lambda o: cad.call("run_command", name="cnc_face", params={"Tool diameter": 12, "X from": 0, "X to": 60, "Y from": 0, "Y to": 40, "Passes run along": "Y", "Stepover": 70}), face_ok)
    strip = ["Left to right, top to bottom: " + " | ".join(r[0] for r in rows)[:140]] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return cells + [("checks", text_panel(strip))]


# ---------------------------------------------------------------- sheet metal: Contour Flange, Edge Flange and Fold options
@scenario("sheetmetal__flange-edge-flange-and-fold-options", columns=1)
def sheetmetal_matrix():
    cad.call("document_new")
    m = Matrix(spacing=90)
    count = [0]

    def unique(prefix):
        count[0] += 1
        return f"{prefix}{count[0]}"

    def solid_info(name):
        return cad.call("object_info", object=name)

    def contour(label, params, check):
        def run():
            before = names()
            prof = sketch([{"type": "polyline", "points": [[0, 20], [0, 0], [60, 0]]}], plane="XZ").name
            cad.call("run_command", name="contour_flange", slots={"Profile sketch (straight lines)": [prof]}, params=params)
            n = made_since(before)
            cad.call("object_update", object=prof, visible=False)
            i = solid_info(n)
            ok, detail = check(i)
            return n, detail, ok and i["valid"]
        m.check(label, run)

    base = {}
    size = lambda i: tuple(round(v, 1) for v in i["size"])
    contour("Contour flange, width 30", {"Thickness": 2, "Bend radius (inside)": 2, "Width": 30}, lambda i: (abs(i["size"][1] - 30) < .1 and 59 < i["size"][0] < 64 and 19 < i["size"][2] < 24, f"size {size(i)} (expect 60 x 30 x 20)"))
    contour("Contour flange, bend radius 6", {"Thickness": 2, "Bend radius (inside)": 6, "Width": 30}, lambda i: (abs(i["size"][1] - 30) < .1, f"size {size(i)}, volume {i['volume']:.0f}"))
    contour("Contour flange, centred on the sketch plane", {"Thickness": 2, "Bend radius (inside)": 2, "Width": 30, "Centred on the sketch plane": True}, lambda i: (abs(i["min"][1] + 15) < .2 and abs(i["max"][1] - 15) < .2, f"y {i['min'][1]:.1f}..{i['max'][1]:.1f} (expect -15..15)"))
    contour("Contour flange, thicker 4 mm", {"Thickness": 4, "Bend radius (inside)": 2, "Width": 30}, lambda i: (i["volume"] > 1.5 * 2 * 30 * 78 * .9, f"volume {i['volume']:.0f} (about twice the 2 mm sheet)"))
    contour("Contour flange with a hem", {"Thickness": 2, "Bend radius (inside)": 2, "Width": 30, "Hem length at the end (0 = no hem)": 8, "Hem gap": 0.5}, lambda i: (i["volume"] > 4600, f"volume {i['volume']:.0f} (more than the plain flange's 4500: the hem adds a returned 8 mm lip)"))

    def edge(label, params, check):
        def run():
            n = unique("Sheet")
            sheet = cad.box(width=60, depth=40, height=2, operation="New body"); sheet.rename(n)
            end = [h["face"] for h in cad.call("solid_find", object=n, kind="faces", type="Plane", facing=[1, 0, 0])["hits"]][0]
            cad.call("run_command", name="edge_flange", slots={"Thin end face of the sheet": [{"object": n, "face": end}]}, params=params)
            i = solid_info(n)
            ok, detail = check(i)
            return n, detail, ok and i["valid"]
        m.check(label, run)
    edge("Edge flange 20, 90 degrees up", {"Flange length": 20, "Bend angle": 90, "Bend radius (inside)": 2}, lambda i: (21 < i["size"][2] < 27, f"height {i['size'][2]:.1f} (expect about 22 to 25)"))
    edge("Edge flange 20, 45 degrees up", {"Flange length": 20, "Bend angle": 45, "Bend radius (inside)": 2}, lambda i: (14 < i["size"][2] < 20, f"height {i['size'][2]:.1f} (expect about 16 to 18)"))
    edge("Edge flange 20, 90 degrees down", {"Flange length": 20, "Bend angle": 90, "Bend radius (inside)": 2, "Fold toward": "Down (the lower face)"}, lambda i: (i["min"][2] < -18, f"z {i['min'][2]:.1f}..{i['max'][2]:.1f} (expect it to hang below the sheet)"))

    def fold(label, angle, check):
        def run():
            before = names()
            cad.call("sketch_begin", plane="XY")
            cad.call("sketch_add", entities=[{"type": "polyline", "points": [[0, 0], [100, 0], [100, 40], [0, 40]], "closed": True}])
            cad.call("sketch_add", entities=[{"type": "line", "points": [[50, -300], [50, 300]], "construction": True}])
            flat = cad.call("sketch_finish")["sketch"]
            made = cad.run("fold", slots={"Flat sketch": flat}, thickness=2, bend_radius=2, k_factor=.44, angle_of_each_bend=str(angle))
            solid = next(o for o in made if o.kind == "Solid")
            n = unique("Fold")
            solid.rename(n)
            for o in made:
                if o.kind != "Solid":
                    cad.call("object_update", object=o.name, visible=False)
            cad.call("object_update", object=flat, visible=False)
            i = solid_info(n)
            ok, detail = check(i)
            return n, detail, ok and i["valid"]
        m.check(label, run)
    fold("Fold, one bend 90", 90, lambda i: (45 < i["size"][2] < 56 and abs(i["size"][1] - 40) < .1 and i["max"][2] > 45 and i["min"][2] > -3, f"z {i['min'][2]:.1f}..{i['max'][2]:.1f}, y size {i['size'][1]:.0f} (expect a wall rising 50 above the base)"))
    fold("Fold, one bend 45", 45, lambda i: (30 < i["size"][2] < 40, f"height {i['size'][2]:.1f} (expect about 35 to 37)"))
    fold("Fold, one bend -90", -90, lambda i: (45 < i["size"][2] < 56 and i["min"][2] < -45 and i["max"][2] < 3, f"z {i['min'][2]:.1f}..{i['max'][2]:.1f} (expect the wall to hang 50 below the base)"))
    for o in cad.objects():
        if o.kind in ("Curve", "Surface"):
            cad.call("object_update", object=o.name, visible=False)
    return [("iso, all variants", render(-25, 30)), ("from the front", render(0, 5)), ("checks", text_panel(m.strip()))]


# ---------------------------------------------------------------- CAE: static study loads against hand calculations
@scenario("cae__static-loads-against-hand-calculations", columns=1)
def cae_loads():
    """A 10 x 10 x 100 mm steel bar fixed at its base: each load type compared with beam and bar theory (E = 200 GPa)."""
    E = 200000.0
    area, inertia, length = 100.0, 10 * 10 ** 3 / 12, 100.0
    rows = []

    def case(label, load, expect, tol=.08, element=3.0):
        cad.call("document_new")
        b = cad.box(width=10, depth=10, height=100, operation="New body"); b.rename("Bar")
        cad.call("material_apply", object="Bar", material="steel")
        bottom = cad.call("solid_find", object="Bar", kind="faces", type="Plane", facing=[0, 0, -1])["hits"][0]["face"]
        top = cad.call("solid_find", object="Bar", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
        try:
            cad.call("study_new", object="Bar", element_size=element)
            cad.call("run_command", name="sim_fixed", slots={"Faces to support": [{"object": "Bar", "face": bottom}]})
            name, params = load
            slots = {} if name == "sim_gravity" else {"Faces to load": [{"object": "Bar", "face": top}]}
            cad.call("run_command", name=name, slots=slots, params=params)
            r = cad.call("study_run")["results"]
            got = r["maxDisplacement_mm"]
            ok = abs(got - expect) <= tol * expect and r["elements"] > 100
            rows.append((label, f"displacement {got:.5f} mm (hand calculation {expect:.5f}), {r['elements']} elements, reaction {r['reactionNet_N']:.1f} N", ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    case("Axial force 1000 N (along the bar)", ("sim_force", {"Force": 1000, "Direction": "+Z"}), 1000 * length / (E * area))
    case("Transverse force 100 N at the tip (bending)", ("sim_force", {"Force": 100, "Direction": "+Y"}), 100 * length ** 3 / (3 * E * inertia), tol=.12)
    case("Pressure 10 MPa on the end (= 1000 N)", ("sim_pressure", {"Pressure": 10}), 1000 * length / (E * area))
    case("Force into the face (along its normal), 1000 N", ("sim_force", {"Force": 1000}), 1000 * length / (E * area))
    # the Gravity field tops out at 100 g: a larger value is silently clamped to 100 (seen while writing this: 100000 gave a 77 N reaction)
    case("Gravity 100 g pulling down", ("sim_gravity", {"Gravity": 100, "Direction": "-Z"}), 7.85e-9 * 9810 * 100 * length ** 2 / (2 * E), tol=.1)
    cad.call("document_new")
    bar = cad.box(width=10, depth=10, height=100, operation="New body")
    strip = [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the bar (fixed base, loaded top)", render(-30, 25)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Mold: every option of Create Mold
@scenario("mold__create-mold-every-option", columns=1)
def mold_matrix():
    rows = []
    slot = [0]
    cap_volume = math.pi * 18 ** 2 * 30
    block_volume = 100 * 70 * 60

    def variant(label, params, slots_extra=None, own_block=True, check=None):
        i = slot[0]
        slot[0] += 1
        try:
            if own_block:
                blk = cad.box(width=100, depth=70, height=60, operation="New body"); blk.rename(f"Blk{i}")
            cap = cad.cylinder(radius=18, height=30, operation="New body"); cap.rename(f"Cap{i}")
            cad.move(cap, x=0, y=0, z=15)
            before = names()
            slots = {"Part to cast (solid, or a closed surface)": f"Cap{i}", **({"Mold block (optional: Prism makes one around the part)": f"Blk{i}"} if own_block else {})}
            cad.call("run_command", name="mold", slots=slots, params=params)
            halves = sorted([o for o in cad.objects() if o.kind == "Solid" and o.name not in before or (o.name == f"Blk{i}")], key=lambda o: (o.name))
            halves = [h for h in halves if not h.name.startswith("Cap")]
            infos = [cad.call("object_info", object=h.name) for h in halves]
            ok, detail = check(infos)
            for o in halves + [cad.get(f"Cap{i}")]:
                cad.move(o, x=i * 130)
            rows.append((label, detail, ok and len(halves) == 2 and all(x["valid"] for x in infos)))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    total = lambda infos: sum(x["volume"] for x in infos)
    variant("Split 50 percent along Z", {"Parting direction": "Z", "Split at": 50, "Explode distance": 40}, check=lambda x: (abs(total(x) - (block_volume - cap_volume)) < 80, f"halves {total(x):.0f} (expect {block_volume - cap_volume:.0f})"))
    variant("Shrinkage 2 percent", {"Shrinkage allowance": 2, "Explode distance": 40}, check=lambda x: (abs(total(x) - (block_volume - cap_volume * 1.02 ** 3)) < 120, f"halves {total(x):.0f} (expect {block_volume - cap_volume * 1.02 ** 3:.0f}: the cavity is 1.02 cubed larger)"))
    variant("Cavity offset 1 mm", {"Cavity offset (clearance)": 1, "Explode distance": 40}, check=lambda x: (block_volume - total(x) > cap_volume * 1.08 and block_volume - total(x) < math.pi * 19 ** 2 * 32, f"cavity {block_volume - total(x):.0f} (between {cap_volume * 1.08:.0f} and {math.pi * 19 ** 2 * 32:.0f})"))
    variant("Split 25 percent along Z", {"Split at": 25, "Explode distance": 40}, check=lambda x: (abs(min(i["volume"] for i in x) - 100 * 70 * 15 + 0) < 6000, f"smaller half {min(i['volume'] for i in x):.0f} (a quarter of the block is about 105000 minus the cavity)"))
    variant("Split along X", {"Parting direction": "X (left and right halves)", "Explode distance": 40}, check=lambda x: (abs((max(i["min"][0] for i in x) - min(i["max"][0] for i in x)) - 40) < .5, f"gap along X {max(i['min'][0] for i in x) - min(i['max'][0] for i in x):.1f} (expect 40)"))
    variant("Split along Y", {"Parting direction": "Y (front and back halves)", "Explode distance": 40}, check=lambda x: (abs((max(i["min"][1] for i in x) - min(i["max"][1] for i in x)) - 40) < .5, f"gap along Y {max(i['min'][1] for i in x) - min(i['max'][1] for i in x):.1f} (expect 40)"))
    variant("No block picked, margin 10", {"Block margin (when no block is picked)": 10, "Explode distance": 40}, own_block=False, check=lambda x: (abs(total(x) - (56 * 56 * 50 - cap_volume)) < 150, f"halves {total(x):.0f} (expect {56 * 56 * 50 - cap_volume:.0f}: a 56 x 56 x 50 block)"))
    strip = ["Left to right: " + " | ".join(r[0] for r in rows)[:140]] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("iso, every variant (part in the middle)", render(-25, 28)), ("from the front", render(0, 6)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Sketch: every constraint, dimension and entity type, read back from the solved sketch
@scenario("sketch__constraints-dimensions-and-entities", columns=1)
def sketch_matrix():
    cad.call("document_new")
    rows = []

    def state():
        return cad.call("sketch_state")

    def P(st):
        return {p["id"]: (p["u"], p["v"]) for p in st["points"]}

    def E(st):
        return {e["id"]: e for e in st["entities"]}

    def case(label, build, check):
        cad.call("sketch_begin", plane="XY")
        try:
            ids = build()
            st = state()
            ok, detail = check(st, ids)
            rows.append((label, detail, ok and st["converged"]))
        except Exception as error:
            rows.append((label, str(error)[:120], False))
        finally:
            try:
                cad.call("sketch_finish", keep=False)
            except Exception:
                pass

    def lines(*segs):
        cad.call("sketch_add", entities=[{"type": "line", "points": [list(a), list(b)]} for a, b in segs])
        st = state()
        return [e["id"] for e in st["entities"]], st

    def ang(a, b, c, d):
        v1, v2 = (b[0] - a[0], b[1] - a[1]), (d[0] - c[0], d[1] - c[1])
        return math.degrees(math.atan2(v1[0] * v2[1] - v1[1] * v2[0], v1[0] * v2[0] + v1[1] * v2[1]))

    def two_line_ends(st, e):
        p, en = P(st), E(st)[e]
        return p[en["points"][0]], p[en["points"][1]]

    def geometric(label, kind, segs, expect):
        def build():
            ids, st = lines(*segs)
            refs = [ids[0]] if kind in ("horizontal", "vertical") else ids[:2]
            cad.call("sketch_constrain", type=kind, refs=refs)
            return ids
        case(label, build, lambda st, ids: expect(st, ids))

    geometric("Horizontal", "horizontal", [((0, 0), (10, 3))], lambda st, ids: (abs((a := two_line_ends(st, ids[0]))[0][1] - a[1][1]) < 1e-6, f"end points v {two_line_ends(st, ids[0])[0][1]:.3f} and {two_line_ends(st, ids[0])[1][1]:.3f}"))
    geometric("Vertical", "vertical", [((0, 0), (3, 10))], lambda st, ids: (abs((a := two_line_ends(st, ids[0]))[0][0] - a[1][0]) < 1e-6, f"end points u {two_line_ends(st, ids[0])[0][0]:.3f} and {two_line_ends(st, ids[0])[1][0]:.3f}"))
    geometric("Perpendicular", "perpendicular", [((0, 0), (20, 2)), ((0, 10), (15, 18))], lambda st, ids: (abs(abs(ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1]))) - 90) < .01, f"angle {ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1])):.2f} degrees"))
    geometric("Parallel", "parallel", [((0, 0), (20, 2)), ((0, 10), (15, 18))], lambda st, ids: (abs(math.sin(math.radians(ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1]))))) < 1e-4, f"angle {ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1])):.3f} degrees"))
    geometric("Equal length", "equal", [((0, 0), (20, 2)), ((0, 10), (15, 18))], lambda st, ids: (abs(math.dist(*two_line_ends(st, ids[0])) - math.dist(*two_line_ends(st, ids[1]))) < 1e-4, f"lengths {math.dist(*two_line_ends(st, ids[0])):.3f} and {math.dist(*two_line_ends(st, ids[1])):.3f}"))
    geometric("Collinear", "collinear", [((0, 0), (20, 2)), ((5, 10), (15, 18))], lambda st, ids: (abs(ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1]))) < 1e-3 or abs(abs(ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1]))) - 180) < 1e-3, f"angle {ang(*two_line_ends(st, ids[0]), *two_line_ends(st, ids[1])):.3f} degrees"))

    def tangent():
        cad.call("sketch_add", entities=[{"type": "line", "points": [[0, 0], [40, 0]]}, {"type": "circle", "center": [20, 12], "radius": 6}])
        st = state()
        line = next(e["id"] for e in st["entities"] if e["type"] == "Line"); circle = next(e["id"] for e in st["entities"] if e["type"] == "Circle")
        cad.call("sketch_constrain", type="tangent", refs=[line, circle])
        return line, circle
    case("Tangent line and circle", tangent, lambda st, ids: (abs(abs(P(st)[E(st)[ids[1]]["points"][0]][1] - two_line_ends(st, ids[0])[0][1]) - E(st)[ids[1]]["radius"]) < 1e-3, f"centre to line {abs(P(st)[E(st)[ids[1]]['points'][0]][1] - two_line_ends(st, ids[0])[0][1]):.3f}, radius {E(st)[ids[1]]['radius']:.3f}"))

    def concentric():
        cad.call("sketch_add", entities=[{"type": "circle", "center": [0, 0], "radius": 10}, {"type": "circle", "center": [4, 3], "radius": 4}])
        ids = [e["id"] for e in state()["entities"]]
        cad.call("sketch_constrain", type="concentric", refs=ids)
        return ids
    case("Concentric circles", concentric, lambda st, ids: (math.dist(P(st)[E(st)[ids[0]]["points"][0]], P(st)[E(st)[ids[1]]["points"][0]]) < 1e-6, "the two centres coincide"))

    def dimension(label, entities, dim, pick, expect):
        def build():
            cad.call("sketch_add", entities=entities)
            st = state()
            cad.call("sketch_dimension", **dim(st))
            return None
        case(label, build, lambda st, ids: pick(st, expect))
    dimension("Dimension: line length 25", [{"type": "line", "points": [[0, 0], [10, 3]]}], lambda st: {"type": "length", "refs": [st["entities"][0]["id"]], "value": 25}, lambda st, v: (abs(math.dist(*two_line_ends(st, st["entities"][0]["id"])) - v) < 1e-4, f"length {math.dist(*two_line_ends(st, st['entities'][0]['id'])):.4f}"), 25)
    dimension("Dimension: circle radius 7", [{"type": "circle", "center": [0, 0], "radius": 3}], lambda st: {"type": "radius", "refs": [st["entities"][0]["id"]], "value": 7}, lambda st, v: (abs(st["entities"][0]["radius"] - v) < 1e-4, f"radius {st['entities'][0]['radius']:.4f}"), 7)
    dimension("Dimension: circle diameter 12", [{"type": "circle", "center": [0, 0], "radius": 3}], lambda st: {"type": "diameter", "refs": [st["entities"][0]["id"]], "value": 12}, lambda st, v: (abs(st["entities"][0]["radius"] * 2 - v) < 1e-4, f"diameter {st['entities'][0]['radius'] * 2:.4f}"), 12)
    dimension("Dimension: angle 60 between two lines", [{"type": "line", "points": [[0, 0], [20, 0]]}, {"type": "line", "points": [[0, 0], [15, 10]]}], lambda st: {"type": "angle", "refs": [e["id"] for e in st["entities"][:2]], "value": 60}, lambda st, v: (abs(abs(ang(*two_line_ends(st, st["entities"][0]["id"]), *two_line_ends(st, st["entities"][1]["id"]))) - v) < .01, f"angle {abs(ang(*two_line_ends(st, st['entities'][0]['id']), *two_line_ends(st, st['entities'][1]['id']))):.3f}"), 60)
    dimension("Dimension: horizontal distance 14", [{"type": "line", "points": [[0, 0], [10, 3]]}], lambda st: {"type": "hdistance", "refs": [p["id"] for p in st["points"][:2]], "value": 14}, lambda st, v: (abs(abs(st["points"][1]["u"] - st["points"][0]["u"]) - v) < 1e-4, f"du {abs(st['points'][1]['u'] - st['points'][0]['u']):.4f}"), 14)
    dimension("Dimension: vertical distance 9", [{"type": "line", "points": [[0, 0], [10, 3]]}], lambda st: {"type": "vdistance", "refs": [p["id"] for p in st["points"][:2]], "value": 9}, lambda st, v: (abs(abs(st["points"][1]["v"] - st["points"][0]["v"]) - v) < 1e-4, f"dv {abs(st['points'][1]['v'] - st['points'][0]['v']):.4f}"), 9)

    # entity types: one sketch with a rectangle, circle, slot and ellipse far apart, extruded 5 mm: the volume is the sum of the areas
    cad.call("document_new")
    cad.call("sketch_begin", plane="XY")
    cad.call("sketch_add", entities=[{"type": "rect", "corner1": [0, 0], "corner2": [20, 10]}, {"type": "circle", "center": [40, 5], "radius": 5},
                                      {"type": "slot", "center1": [60, 5], "center2": [74, 5], "width": 6}, {"type": "ellipse", "center": [100, 5], "major": [108, 5], "minor": 4}])
    prof = cad.get(cad.call("sketch_finish")["sketch"])
    body = cad.extrude(prof, distance=5, operation="New body")
    info = cad.call("object_info", object=body.name)
    expect = (200 + math.pi * 25 + 14 * 6 + math.pi * 9 + math.pi * 8 * 4) * 5
    rows.append(("Entities: rectangle, circle, slot, ellipse (extruded 5 mm)", f"volume {info['volume']:.0f} (expect {expect:.0f})", info["valid"] and abs(info["volume"] - expect) < .01 * expect))
    cad.call("object_update", object=prof.name, visible=False)
    strip = ["Constraints, dimensions and entities, each read back from the solved sketch"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the entity-types profile, extruded", render(-25, 35)), ("checks", text_panel(strip))]
