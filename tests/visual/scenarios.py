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


def gcode_of(object_name_hint=None):
    """Post every toolpath in the open document to a temporary file and return the program text."""
    path = Path(tempfile.gettempdir()) / "prism-visual.nc"
    path.unlink(missing_ok=True)
    cad.call("run_command", name="cnc_post", params={"Save to": str(path), "Save G-code": True}, commit=False)
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
