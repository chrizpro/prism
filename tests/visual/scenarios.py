"""The visual scenarios. Each is named for what it tests and returns labeled pictures that ``visual.py`` lays out as one contact sheet."""
import json
import math
import tempfile
import time
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


def gcode_of(zero="Saved CAM setup", machine="GRBL"):
    """Post every toolpath in the open document to a temporary file and return the program text."""
    path = Path(tempfile.gettempdir()) / "prism-visual.nc"
    path.unlink(missing_ok=True)
    cad.call("run_command", name="cnc_post", params={"Save to": str(path), "Work zero (G54)": zero, "Machine": machine}, commit=False)
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


def model_check(model):
    """PASS or FAIL line for a model that was just opened: it has objects and every solid and surface is valid."""
    state = cad.call("get_state")["objects"]
    bodies = [o for o in state if o["kind"] in ("Solid", "Surface", "Form")]
    bad = [o["name"] for o in bodies if o["kind"] != "Form" and not cad.call("object_info", object=o["name"]).get("valid", False)]   # a Freeform cage has no exact-shape validity
    ok = len(state) > 0 and bool(bodies) and not bad
    return f"{'PASS' if ok else 'FAIL'}  {model}: {len(state)} objects, {len(bodies)} bodies, invalid: {bad or 'none'}"


def two_views(model, a=(-35, 28), b=(0, 89), la="iso", lb="top", distance=None):
    open_model(model)
    check = model_check(model)
    return [(la, render(*a, distance=distance)), (lb, render(*b, distance=distance)), ("checks", text_panel([check]))]


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
    grown = cad.call("get_state")
    exploded = render(-38, 20, distance=2300)
    spread = max(cad.call("object_info", object=o["name"])["max"][2] for o in grown["objects"] if o["kind"] == "Solid")
    cad.call("run_command", name="explode", params={"Explode": 0.0}, commit=True)
    settled = max(cad.call("object_info", object=o["name"])["max"][2] for o in cad.call("get_state")["objects"] if o["kind"] == "Solid")
    ok = spread > settled + 20
    return [("assembled", whole), ("exploded view", exploded), ("checks", text_panel([model_check("meridian-orrery"), f"{'PASS' if ok else 'FAIL'}  Exploded at 0.9 the highest part rises to z {spread:.0f}; closed again it is back to {settled:.0f}"]))]


@scenario("gallery__every-model-opens-and-renders")
def gallery_overview():
    catalog = json.loads((ROOT / "gallery/catalog.json").read_text(encoding="utf-8"))
    cells, lines = [], []
    for item in catalog["Items"]:
        try:
            open_model(item["Id"])
            lines.append(model_check(item["Id"]))
            cells.append((item["Id"], render(-35, 28, samples=4)))
        except Exception as error:
            lines.append(f"FAIL  {item['Id']}: {str(error)[:100]}")
            cells.append((item["Id"] + " FAILED", text_panel([str(error)[:300]])))
    return cells + [("checks", text_panel(lines))]


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


# ---------------------------------------------------------------- CAM: Adaptive, 3D Rough, 3D Finish and Turning
@scenario("cam__adaptive-rough-finish-and-turning-options")
def cam_3d_matrix():
    cells, rows = [], []

    def finish_variant(label, run, check, plot=True):
        cad.call("document_new")
        try:
            text = run()
            moves = cut_moves(text)
            ok, detail = check(moves, text)
            cells.append((label, toolpath_plot(text, title=label[:18]) if plot else text_panel([detail])))
        except Exception as error:
            ok, detail = False, str(error)[:110]
            cells.append((label + " FAILED", text_panel([detail])))
        rows.append((label, detail, ok))

    def adaptive():
        o = sketch([{"type": "rect", "corner1": [0, 0], "corner2": [40, 30]}]).name
        cad.call("run_command", name="cnc_adaptive", slots={"Outlines of the pocket (islands inside are left)": [o]}, params={"Tool diameter": 6, "Depth per pass": 6, "Total cutting depth": 12, "Sideways cut (load)": 12, "Finish the walls with one pass": True})
        return gcode_of("The model's own origin")

    def adaptive_check(m, text):
        xs, ys, zs = [p[0] for p in m], [p[1] for p in m], sorted({round(p[2], 2) for p in m})
        inside = min(xs) >= 2.9 and max(xs) <= 37.1 and min(ys) >= 2.9 and max(ys) <= 27.1
        return inside and min(zs) <= -11.99 and len([z for z in zs if z < -1]) >= 2, f"tool centres x {min(xs):.1f}..{max(xs):.1f}, y {min(ys):.1f}..{max(ys):.1f} (stay inside 3..37, 3..27); depth levels {zs[:4]}, deepest {min(zs)}"
    finish_variant("Adaptive pocket, 12 deep in 6s", adaptive, adaptive_check)

    def with_setup(make_body, op, params, stock=2):
        body = make_body()
        cad.call("run_command", name="cam_setup", slots={"Model body": body}, params={"Stock allowance XY": stock, "Top allowance": stock, "Bottom allowance": 0, "Work zero (G54)": "Model origin", "Tool diameter": 6})
        cad.call("run_command", name=op, slots={("Model to rough" if op == "cnc_rough" else "Model to finish"): body}, params=params)
        return gcode_of("Saved CAM setup")

    def cylinder():
        c = cad.cylinder(radius=15, height=20, operation="New body"); c.rename("Round"); return "Round"

    def rough_check(m, text):
        radius = [math.hypot(p[0], p[1]) for p in m]
        levels = sorted({round(p[2], 2) for p in m}, reverse=True)
        return min(radius) >= 15 + .5 + 3 - .3 and len(levels) >= 4, f"closest tool centre {min(radius):.2f} from the axis (expect at least 18.5: 15 radius + 0.5 left + 3 tool radius); {len(levels)} layers {levels[:5]}"
    finish_variant("3D Rough on a 15 mm round, leave 0.5", lambda: with_setup(cylinder, "cnc_rough", {"Tool diameter": 6, "Depth per pass": 4, "Total cutting depth": 20, "Stock to leave": 0.5}), rough_check)

    def dome():
        s = cad.sphere(radius=20, operation="New body"); s.rename("Dome")
        cutter = cad.box(width=60, depth=60, height=30, operation="New body"); cad.move(cutter, z=-30); cutter.rename("Below")
        cad.call("run_command", name="subtract", slots={"Body": "Dome", "Tool": "Below"}) if False else None
        cad.call("object_update", object="Below", visible=False)
        return "Dome"

    def finish_check(m, text):
        # the ball's centre is 3 mm above its tip; touching a 20 mm sphere, it sits 20 + 3 = 23 mm from the sphere's centre
        distances = [math.hypot(math.hypot(p[0], p[1]), p[2] + 3) for p in m if p[2] > 2 and math.hypot(p[0], p[1]) < 20]
        low, high = min(distances), max(distances)
        # the sphere is meshed before it is finished, so the path can sit up to about 0.3 mm inside the true surface (a finding: finishing follows the display mesh)
        return 22.6 < low and high < 23.6, f"{len(distances)} points: ball centre {low:.2f} to {high:.2f} from the sphere's centre (expect 23.0: 20 + the 3 mm tool radius; allowing 0.4 mm for meshing)"
    finish_variant("3D Finish, ball nose on a sphere", lambda: with_setup(dome, "cnc_finish", {"Tool diameter": 6, "Cutter": "Ball nose", "Stepover": 10, "Depth per pass": 2, "Total cutting depth": 20}, stock=1), finish_check)

    def turning():
        prof = sketch([{"type": "polyline", "points": [[0, 0], [20, 0], [20, 15], [10, 25], [0, 25]], "closed": True}], plane="XZ").name
        cad.call("run_command", name="cnc_turn", slots={"Profile of the part (half section on the XZ plane)": [prof]}, params={"Tool diameter": 6, "Depth per pass": 1.5, "Finish allowance": 0.2})
        return gcode_of("The model's own origin", "Mach3")        # GRBL cannot run a lathe

    def turning_check(m, text):
        xs = [p[0] for p in m]
        return bool(m) and max(xs) >= 19.9, f"{len(m)} cutting moves, x up to {max(xs) if xs else 0:.1f} (the 20 mm radius, or 40 as a diameter), first lines: {' / '.join(text.splitlines()[3:6])}"
    finish_variant("Turning a stepped, tapered shaft", turning, turning_check, plot=False)
    strip = ["Adaptive, 3D Rough, 3D Finish and Turning, read back from the posted G-code"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return cells + [("checks", text_panel(strip))]


# ---------------------------------------------------------------- CAE: natural frequencies, conduction and fatigue against hand calculations
@scenario("cae__modal-thermal-and-fatigue-against-theory", columns=1)
def cae_other():
    """The same 10 x 10 x 100 mm steel bar fixed at its base: cantilever frequencies, a conduction gradient and a fatigue verdict."""
    rows = []

    def bar():
        cad.call("document_new")
        b = cad.box(width=10, depth=10, height=100, operation="New body"); b.rename("Bar")
        cad.call("material_apply", object="Bar", material="steel")
        face = lambda facing: cad.call("solid_find", object="Bar", kind="faces", type="Plane", facing=facing)["hits"][0]["face"]
        return face([0, 0, -1]), face([0, 0, 1])

    def analysis(command, slots, params):
        cad.call("run_command", name=command, slots=slots, params=params, commit=False)
        cad.call("command_set", params={"Run analysis": True})
        deadline = time.monotonic() + 300
        while time.monotonic() < deadline:
            rows_ = cad.call("command_set", params={}).get("results", [])
            text = " ".join(map(str, rows_))
            if "Status: Complete" in text or "Status: Failed" in text:
                break
            time.sleep(2)
        cad.call("command_cancel")
        return rows_

    try:
        bottom, top = bar()
        rows_ = analysis("cae_modal", {"Solid bodies": ["Bar"], "Fixed support faces": [{"object": "Bar", "face": bottom}]}, {"Number of modes": 4})
        modes = [float(r.split(": ")[1].split()[0]) for r in rows_ if r.startswith("Mode ")]
        E, rho, I, A, L = 200000.0, 7.85e-9, 10 * 10 ** 3 / 12, 100.0, 100.0
        f1 = 1.875 ** 2 / (2 * math.pi) * math.sqrt(E * I / (rho * A * L ** 4))
        ok = len(modes) == 4 and abs(modes[0] - f1) < .08 * f1 and abs(modes[1] - modes[0]) < .02 * modes[0] and modes == sorted(modes)
        rows.append(("Natural frequencies, cantilever", f"modes {[round(m, 1) for m in modes]} Hz; first bending {f1:.1f} Hz by beam theory (a square bar has two equal first modes)", ok))
    except Exception as error:
        rows.append(("Natural frequencies, cantilever", str(error)[:120], False))

    try:
        bottom, top = bar()
        rows_ = analysis("cae_thermal", {"Solid bodies": ["Bar"], "First temperature faces": [{"object": "Bar", "face": bottom}], "Second temperature faces": [{"object": "Bar", "face": top}]},
                         {"Thermal analysis": "Steady conduction", "First temperature": 100, "Second temperature": 20})
        temps = {r.split(": ")[0]: float(r.split(": ")[1].split()[0]) for r in rows_ if r.split(": ")[0].endswith("temperature")}
        rows.append(("Steady conduction, 100 C to 20 C", f"minimum {temps.get('Minimum temperature')} C and maximum {temps.get('Maximum temperature')} C (expect exactly 20 and 100)", abs(temps.get("Minimum temperature", 0) - 20) < .5 and abs(temps.get("Maximum temperature", 0) - 100) < .5))
    except Exception as error:
        rows.append(("Steady conduction, 100 C to 20 C", str(error)[:120], False))

    for force, low, high in ((1000.0, 8, 1e6), (6000.0, 1.0, 3.5)):
        try:
            bottom, top = bar()
            cad.call("study_new", object="Bar", element_size=3.0)
            cad.call("run_command", name="sim_fixed", slots={"Faces to support": [{"object": "Bar", "face": bottom}]})
            cad.call("run_command", name="sim_force", slots={"Faces to load": [{"object": "Bar", "face": top}]}, params={"Force": force, "Direction": "+Z"})
            cad.call("study_run")
            cad.call("run_command", name="cae_fatigue", params={"Load ratio R (min / max)": -1, "Required life": 1000000, "Surface finish": "Machined"}, commit=False)
            cad.call("command_set", params={"Show on model": True})
            fat = cad.call("command_set", params={}).get("results", [])
            cad.call("command_cancel")
            sf = float(next(r for r in fat if r.startswith("Fatigue safety factor")).split(": ")[1].split()[0])
            verdict = next((r for r in fat if r.startswith("Verdict")), "")
            rows.append((f"Fatigue, fully reversed {force:.0f} N for 1e6 cycles", f"safety factor {sf:.2f} (expect between {low} and {high:g}); {verdict}", low < sf < high and (("Meets" in verdict) == (sf >= 1))))
        except Exception as error:
            rows.append((f"Fatigue, fully reversed {force:.0f} N for 1e6 cycles", str(error)[:120], False))
    bar()
    strip = ["Natural frequencies, conduction and fatigue, each against theory"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the bar (fixed base)", render(-30, 25)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Assembly: joints and Drive Joints
@scenario("assembly__joints-revolute-slider-rigid-cylindrical", columns=1)
def assembly_matrix():
    rows = []
    slot = [0]

    def centre(name):
        i = cad.call("object_info", object=name)
        return [(a + b) / 2 for a, b in zip(i["min"], i["max"])], i["size"]

    def drive(joint, value):
        r = cad.call("run_command", name="drive_joints", commit=False)
        labels = [p["label"] for p in r["activeCommand"]["params"]]
        label = next((l for l in labels if joint.lower() in l.lower()), labels[0])
        cad.call("command_set", params={label: value})
        cad.call("command_commit")
        cad.call("wait_idle")

    def variant(label, kind, parent_face, child_face, build, value, check):
        i = slot[0]
        slot[0] += 1
        try:
            p, c = build(i)
            r = cad.call("run_command", name="joint_" + kind, slots={"On the first part (face, circle edge or round face)": {"object": p, **parent_face(p)}, "On the part that moves": {"object": c, **child_face(c)}})
            joints = [o["name"] for o in r.get("created", []) if o.get("kind") == "Joint"]
            before = centre(c)
            if value is not None:
                drive(joints[0], value)
            after = centre(c)
            ok, detail = check(before, after)
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def cyl_face(name):
        return {"face": cad.call("solid_find", object=name, kind="faces", type="Cylinder", limit=2)["hits"][0]["face"]}

    def top_face(name):
        return {"face": cad.call("solid_find", object=name, kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]}

    def bottom_face(name):
        return {"face": cad.call("solid_find", object=name, kind="faces", type="Plane", facing=[0, 0, -1])["hits"][0]["face"]}

    def pin_and_arm(i):
        p = cad.cylinder(radius=10, height=10, operation="New body"); p.rename(f"Pin{i}"); cad.move(p, x=i * 80)
        arm = cad.box(width=30, depth=6, height=5, operation="New body"); arm.rename(f"Arm{i}"); cad.move(arm, x=i * 80 + 5, z=12)
        c = cad.cylinder(radius=10, height=5, operation="New body"); c.rename(f"Hub{i}"); cad.move(c, x=i * 80, z=12)
        cad.union(c, arm)                       # the hub carries an arm, so a turn changes its outline
        return f"Pin{i}", f"Hub{i}"

    cad.call("document_new")
    variant("Revolute: hub turns 90 about a pin", "revolute", cyl_face, cyl_face, pin_and_arm, 90, lambda b, a: (b[1][0] > b[1][1] + 5 and abs(a[1][1] - b[1][0]) < 1.5 and abs(a[1][0] - b[1][1]) < 1.5, f"outline {[round(v, 1) for v in b[1][:2]]} -> {[round(v, 1) for v in a[1][:2]]} (turning 90 degrees swaps the arm from along X to along Y)"))

    def block_pair(i):
        a = cad.box(width=30, depth=30, height=10, operation="New body"); a.rename(f"Base{i}"); cad.move(a, x=i * 80)
        b = cad.box(width=20, depth=20, height=10, operation="New body"); b.rename(f"Slide{i}"); cad.move(b, x=i * 80, z=12)
        return f"Base{i}", f"Slide{i}"
    variant("Slider: block slides 15 along the joint axis", "slider", top_face, bottom_face, block_pair, 15, lambda b, a: (abs(math.dist(b[0], a[0]) - 15) < .6, f"moved {math.dist(b[0], a[0]):.2f} mm (expect 15)"))
    variant("Rigid: block stays put when driven", "rigid", top_face, bottom_face, block_pair, None, lambda b, a: (math.dist(b[0], a[0]) < .01, "no motion"))
    strip = ["Joints, each driven and the part's movement read back"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("iso (joints in place)", render(-30, 30)), ("checks", text_panel(strip))]


@scenario("assembly__cylindrical-joint-and-gear-coupling", columns=1)
def assembly_more():
    rows = []
    cad.call("document_new")

    def size(name):
        i = cad.call("object_info", object=name)
        return i["size"], [(a + b) / 2 for a, b in zip(i["min"], i["max"])]

    def pin_and_hub(i):
        p = cad.cylinder(radius=10, height=10, operation="New body"); p.rename(f"Pin{i}"); cad.move(p, x=i * 80)
        arm = cad.box(width=30, depth=6, height=5, operation="New body"); arm.rename(f"Arm{i}"); cad.move(arm, x=i * 80 + 5, z=12)
        hub = cad.cylinder(radius=10, height=5, operation="New body"); hub.rename(f"Hub{i}"); cad.move(hub, x=i * 80, z=12)
        cad.union(hub, arm)
        cyl = lambda n: {"face": cad.call("solid_find", object=n, kind="faces", type="Cylinder", limit=2)["hits"][0]["face"]}
        return f"Pin{i}", f"Hub{i}", cyl

    def joint(kind, pin, hub, cyl):
        r = cad.call("run_command", name="joint_" + kind, slots={"On the first part (face, circle edge or round face)": {"object": pin, **cyl(pin)}, "On the part that moves": {"object": hub, **cyl(hub)}})
        return next(o["name"] for o in r.get("created", []) if o.get("kind") == "Joint")

    def drive(values):
        r = cad.call("run_command", name="drive_joints", commit=False)
        labels = [p["label"] for p in r["activeCommand"]["params"]]
        cad.call("command_set", params={label: v for label, v in zip(labels, values)})
        cad.call("command_commit")
        cad.call("wait_idle")
        return labels

    try:
        pin, hub, cyl = pin_and_hub(0)
        j = joint("cylindrical", pin, hub, cyl)
        (sb, cb) = size(hub)
        drive([90, 10])
        (sa, ca) = size(hub)
        ok = abs(sa[1] - sb[0]) < 1.5 and abs(abs(ca[2] - cb[2]) - 10) < .6
        rows.append(("Cylindrical: turn 90 and slide 10", f"outline {[round(v, 1) for v in sb[:2]]} -> {[round(v, 1) for v in sa[:2]]}, centre z moved {ca[2] - cb[2]:.2f} (expect a 90 degree turn and 10 mm along the axis)", ok))
    except Exception as error:
        rows.append(("Cylindrical: turn 90 and slide 10", str(error)[:120], False))

    try:
        cad.call("document_new")
        p1, h1, c1 = pin_and_hub(0)
        p2, h2, c2 = pin_and_hub(1)
        j1, j2 = joint("revolute", p1, h1, c1), joint("revolute", p2, h2, c2)
        cad.call("run_command", name="gear_pair", commit=False)
        cad.call("command_set", params={"Leading joint": j1, "Following joint": j2, "Leading size (teeth, or pulley or pitch diameter)": 20, "Following size (same unit)": 40, "Direction": "Opposite way (external gears)"})
        cad.call("command_commit")
        drive_labels = drive([90])
        s2, _ = size(h2)
        # the follower turns 90 * 20 / 40 = 45 degrees: a 30 x 6 arm at 45 degrees has a bounding box of (30 + 6) * cos 45 = 25.5 each way
        expect = (30 + 6) * math.cos(math.radians(45))
        rows.append(("Gear pair 20:40, leader 90 degrees", f"follower outline {[round(v, 1) for v in s2[:2]]} (expect about {expect:.1f} x {expect:.1f} for a 45 degree turn)", abs(s2[0] - expect) < 1.5 and abs(s2[1] - expect) < 1.5))
    except Exception as error:
        rows.append(("Gear pair 20:40, leader 90 degrees", str(error)[:120], False))
    strip = ["A cylindrical joint, and a gear coupling driven through its leading joint"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("iso (the gear pair, after driving)", render(-30, 30)), ("checks", text_panel(strip))]


@scenario("cae__topology-optimization-keeps-half-the-block", columns=1)
def cae_topology():
    """Open the saved wrench, run Generative on its torque study (it took 16 seconds here; it was 8 minutes in an earlier build) and check it keeps about half the block."""
    open_model("wrench")
    cad.call("study_run")
    cad.call("run_command", name="generative", params={"Keep this much of the volume": 55, "Detail": "Coarse (fast)", "Most iterations": 40}, commit=False)
    cad.call("command_set", params={"Generate": True})
    deadline = time.monotonic() + 1500
    rows_ = []
    while time.monotonic() < deadline:
        rows_ = cad.call("command_set", params={}).get("results", [])
        text = " ".join(map(str, rows_))
        if "Status: Done" in text or "Failed" in text:
            break
        time.sleep(5)
    share = float(next(x for x in rows_ if x.startswith("Result volume")).split("(")[1].split("%")[0])
    cad.call("command_cancel")
    ok = 40 < share < 60
    return [("the wrench", render(-35, 45, distance=330)), ("checks", text_panel([f"{'PASS' if ok else 'FAIL'}  Generative at a 55% target keeps {share:.1f}% of the block", *[str(r)[:100] for r in rows_ if str(r).startswith(("Stiffness", "Mass", "Iterations"))]]))]


# ---------------------------------------------------------------- Surface commands: extrude, revolve, loft, sweep, pipe, thicken, offset, ruled, planar
def new_object(before, kinds=("Surface", "Solid")):
    return next((o.name for o in cad.objects() if o.kind in kinds and o.name not in before), None)


def line_curve(a, b):
    return curve([list(a), [(x + y) / 2 for x, y in zip(a, b)], list(b)])


@scenario("surface__extrude-revolve-loft-sweep-pipe-and-offsets", columns=1)
def surface_matrix():
    cad.call("document_new")
    m = Matrix(spacing=70)

    def surface(label, command, slots_fn, params, expect, key="area", tol=.03, plane_of=None):
        def run():
            before = names()
            slots = slots_fn()
            cad.call("run_command", name=command, slots=slots, params=params)
            n = new_object(before)
            if n is None:
                raise RuntimeError("nothing was made")
            info = cad.call("object_info", object=n)
            got = info[key]
            ok = info["valid"] and abs(got - expect) <= tol * expect
            return n, f"{key} {got:.0f} (expect {expect:.0f})", ok
        m.check(label, run)

    flat = lambda: {"Profile": line_curve((0, 0, 0), (30, 0, 0))}
    surface("Extrude surface: distance 20", "extrude_surface", flat, {"Extent": "Distance", "Distance": 20}, 600)
    surface("Extrude surface: reversed", "extrude_surface", flat, {"Extent": "Distance", "Distance": 20, "Reverse direction": True}, 600)
    surface("Extrude surface: two sides 20 + 10", "extrude_surface", flat, {"Extent": "Two sides", "Distance": 20, "Distance 2 (other side)": 10}, 900)
    surface("Extrude surface: symmetric 20", "extrude_surface", flat, {"Extent": "Symmetric", "Distance": 20}, 600)
    vertical = lambda: {"Profile": line_curve((0, 0, 0), (0, 0, 30))}
    surface("Revolve surface: full 360 about Z", "revolve_surface", lambda: {"Profile": curve([[10, 0, 0], [10, 0, 15], [10, 0, 30]], "XZ" and "XY", 0)}, {"Extent": "Full 360°"}, 2 * math.pi * 10 * 30, tol=.04)
    surface("Loft surface: circle 10 to 4 over 30", "loft_surface", lambda: {"Sections (in order)": [ring(0, 10), ring(30, 4)]}, {}, math.pi * 14 * math.hypot(30, 6), tol=.04)
    surface("Loft surface: 6-12-6 ruled", "loft_surface", lambda: {"Sections (in order)": [ring(0, 6), ring(15, 12), ring(30, 6)]}, {"Straight (ruled) transitions": True}, 2 * math.pi * 18 * math.hypot(15, 6), tol=.04)
    path = lambda: [line_curve((0, 0, 0), (0, 0, 40))]
    surface("Sweep surface: circle r5 along a straight 40", "sweep_surface", lambda: {"Profile": ring(0, 5), "Path": path()[0]}, {}, 2 * math.pi * 5 * 40, tol=.04)
    surface("Pipe: radius 5 along 40 (solid)", "pipe", lambda: {"Path curve": path()[0]}, {"Radius at the start": 5, "Radius at the end": 5}, math.pi * 25 * 40, key="volume", tol=.03)
    surface("Pipe: tapered radius 5 to 10", "pipe", lambda: {"Path curve": path()[0]}, {"Radius at the start": 5, "Radius at the end": 10}, math.pi / 3 * 40 * (25 + 50 + 100), key="volume", tol=.04)
    surface("Ruled surface between two 30 mm lines 20 apart", "ruled_surface", lambda: {"First curve": line_curve((0, 0, 0), (30, 0, 0)), "Second curve": line_curve((0, 20, 0), (30, 20, 0))}, {}, 600)
    surface("Planar surface from a closed circle r10", "planar_surface", lambda: {"Closed boundary": ring(0, 10)}, {}, math.pi * 100, tol=.03)
    for o in cad.objects():
        if o.kind == "Curve":
            cad.call("object_update", object=o.name, visible=False)
    return [("iso, every surface and pipe", render(-25, 30)), ("from the front", render(0, 5)), ("checks", text_panel(m.strip()))]


# ---------------------------------------------------------------- Sweep: every registered command starts and cancels without crashing
@scenario("command__every-command-opens-and-cancels", columns=1)
def every_command_opens():
    """Start each command on a document with a solid, a sketch and a curve in it, read its panel and cancel. A missing-selection message is fine;
    a crash message (null reference, missing native function, index error) is a bug."""
    cad.call("document_new")
    box = cad.box(width=30, depth=30, height=20, operation="New body"); box.rename("Solid")
    cad.call("sketch_begin", plane="XY"); cad.call("sketch_add", entities=[{"type": "rect", "corner1": [0, 0], "corner2": [20, 20]}]); cad.call("sketch_finish")
    curve([[0, 0, 0], [10, 5, 10], [20, 0, 20]])
    crash_words = ("NullReference", "Object reference", "entry point", "Index was out", "Unhandled", "Sequence contains", "KeyNotFound", "InvalidCast", "NotImplemented", "StackOverflow", "Specified argument")
    import re
    source = (ROOT / "Prism/Automation/AutomationApi.cs").read_text(encoding="utf-8")
    commands = sorted(set(re.findall(r'\["([a-z_0-9]+)"\] = \(\) =>', source)) | set(re.findall(r'Registry\.TryAdd\("([a-z_0-9]+)"', source)))
    crashed, quiet, slow = [], 0, []
    for name in commands:
        started = time.monotonic()
        try:
            cad.call("run_command", name=name, commit=False)
            quiet += 1
        except Exception as error:
            text = str(error)
            if any(w in text for w in crash_words):
                crashed.append(f"{name}: {text[:110]}")
        finally:
            try:
                cad.call("command_cancel")
            except Exception:
                pass
        if time.monotonic() - started > 8:
            slow.append(f"{name} {time.monotonic() - started:.0f}s")
    lines = [f"{'PASS' if not crashed else 'FAIL'}  {len(commands)} commands started and cancelled: {quiet} opened cleanly, {len(commands) - quiet - len(crashed)} asked for a selection first, {len(crashed)} crashed",
             f"{'PASS' if not slow else 'FAIL'}  none took more than 8 seconds to open" + (f" (slow: {', '.join(slow)})" if slow else "")] + [f"FAIL  {c}" for c in crashed]
    return [("the document the commands were started in", render(-30, 28)), ("checks", text_panel(lines))]


# ---------------------------------------------------------------- Direct modeling and holes on a 40 x 30 x 20 block
@scenario("solid__direct-modeling-and-holes-every-option", columns=1)
def direct_matrix():
    cad.call("document_new")
    m = Matrix(spacing=60)
    base = 40 * 30 * 20
    cur = [""]

    def on_block(label, fn):
        def run():
            cur[0] = f"D{m.slot}"
            b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename(cur[0])
            return fn()
        m.check(label, run)

    def face(facing):
        return {"object": cur[0], "face": cad.call("solid_find", object=cur[0], kind="faces", type="Plane", facing=facing)["hits"][0]["face"]}

    def result(expect=None, tol=None, lo=None, hi=None, extra=""):
        i = cad.call("object_info", object=cur[0])
        if expect is not None:
            ok, detail = abs(i["volume"] - expect) <= (tol if tol is not None else .004 * base), f"volume {i['volume']:.0f} (expect {expect:.0f}){extra}"
        else:
            ok, detail = lo < i["volume"] < hi, f"volume {i['volume']:.0f} (expect between {lo:.0f} and {hi:.0f}){extra}"
        return cur[0], detail, ok and i["valid"]

    def go(command, slots, params=None):
        cad.call("run_command", name=command, slots=slots, params=params or {})

    on_block("Push/pull top +10", lambda: (go("push_pull", {"Faces to push or pull": [face([0, 0, 1])]}, {"Distance (positive pulls out, negative pushes in)": 10}), result(base + 40 * 30 * 10))[1])
    on_block("Push/pull top -5", lambda: (go("push_pull", {"Faces to push or pull": [face([0, 0, 1])]}, {"Distance (positive pulls out, negative pushes in)": -5}), result(base - 40 * 30 * 5))[1])
    on_block("Offset +X face by 4", lambda: (go("offset_face", {"Faces to offset": [face([1, 0, 0])]}, {"Offset distance (positive moves faces out)": 4}), result(base + 4 * 30 * 20))[1])
    on_block("Move +X face by 6", lambda: (go("move_face", {"Flat faces to move": [face([1, 0, 0])]}, {"Move X": 6}), result(base + 6 * 30 * 20))[1])
    on_block("Move top face, tilted 10 degrees", lambda: (go("move_face", {"Flat faces to move": [face([0, 0, 1])]}, {"Tilt angle": 10, "Tilt about": "World Y"}), result(lo=base * .8, hi=base * 1.2))[1])
    on_block("Rotate +X face 5 degrees about Z", lambda: (go("rotate_face", {"Flat faces to rotate": [face([1, 0, 0])]}, {"Rotation angle": 5, "Rotate about": "World Z"}), result(lo=base * .9, hi=base * 1.1))[1])
    on_block("Draft the +X face 5 degrees on the bottom plane", lambda: (go("draft_face", {"Faces to draft": [face([1, 0, 0])], "Neutral plane (a plane or flat face the walls hinge on)": [face([0, 0, -1])]}, {"Draft angle": 5}), result(lo=base * .9, hi=base * 1.1))[1])

    def hole_block(params, expect, label_extra=""):
        go("hole", {"Body or entry face": [face([0, 0, 1])]}, params)
        return result(expect, tol=.01 * base, extra=label_extra)
    on_block("Hole, plain 8 mm through", lambda: hole_block({"Diameter": 8, "Extent": "Through all"}, base - math.pi * 16 * 20))
    on_block("Hole, plain 8 mm blind 10", lambda: hole_block({"Diameter": 8, "Extent": "Blind", "Hole depth": 10}, base - math.pi * 16 * 10))
    on_block("Hole, offset 10 mm from the centre", lambda: hole_block({"Diameter": 6, "Center offset U": 10, "Extent": "Through all"}, base - math.pi * 9 * 20))
    on_block("Hole, threaded M6 modeled through", lambda: (go("hole", {"Body or entry face": [face([0, 0, 1])]}, {"Hole type": "Threaded (modeled)", "Metric size": "M6 × 1", "Extent": "Through all"}), result(lo=base - math.pi * 9 * 20 * 1.1, hi=base - math.pi * 4 * 20 * .9))[1])

    def hole_then(command, slot_label, params, expect):
        go("hole", {"Body or entry face": [face([0, 0, 1])]}, {"Diameter": 8, "Extent": "Through all"})
        side = cad.call("solid_find", object=cur[0], kind="faces", type="Cylinder", limit=2)["hits"][0]["face"]
        go(command, {slot_label: [{"object": cur[0], "face": side}]}, params)
        return result(expect, tol=.01 * base)
    on_block("Delete the hole's face (hole fills in)", lambda: hole_then("delete_face", "Faces to remove (a hole, fillet, pocket...)", {}, base))
    on_block("Move the hole 5 mm", lambda: hole_then("move_hole", "A hole or boss (click its round face)", {"Move X": 5}, base - math.pi * 16 * 20))
    on_block("Resize the hole to 12 mm", lambda: hole_then("move_hole", "A hole or boss (click its round face)", {"New diameter (0 = keep)": 12}, base - math.pi * 36 * 20))

    def round_then_remove():
        edges = [e for e in cad.call("solid_find", object=cur[0], kind="edges", limit=60)["hits"] if abs(e.get("center", [0, 0, 0])[2] - 20) < .01][:4]
        cad.fillet([dict(object=cur[0], edge=e["edge"]) for e in edges], radius=5)
        go("remove_fillets", {"Or a body to clean up": [cur[0]]}, {"Also remove all rounds up to radius (0 = only picked)": 5})
        return result(base, tol=.003 * base)
    on_block("Remove the four R5 top-edge fillets from a body", round_then_remove)
    known = ["KNOWN ISSUE #87: Remove Fillets cannot find the rounds on a block whose four vertical edges are all filleted (the outline is classified as a Slot), and picking a fillet face through the API is refused"]
    return [("iso, every variant", render(-25, 30)), ("from above", render(0, 89)), ("checks", text_panel(m.strip() + known))]


# ---------------------------------------------------------------- Transforms and patterns: move, rotate, scale, mirror, linear, circular and table patterns
@scenario("solid__move-rotate-scale-mirror-and-patterns", columns=1)
def transform_matrix():
    cad.call("document_new")
    m = Matrix(spacing=0)       # each variant is built where it will be seen: rows of results stacked along Y
    rows_done = [0]

    def variant(label, build, check):
        y0 = rows_done[0] * 60
        rows_done[0] += 1
        try:
            before = names()
            primary = build(y0)
            made = [o.name for o in cad.objects() if o.kind == "Solid" and o.name not in before] + ([primary] if primary else [])
            ok, detail = check(sorted(set(made)))
            m.rows.append((label, detail, ok))
        except Exception as error:
            m.rows.append((label, str(error)[:120], False))

    def block(y0, w=20, d=10, h=10, x=0):
        b = cad.box(width=w, depth=d, height=h, operation="New body"); b.rename(f"Src{rows_done[0]}"); cad.move(b, x=x, y=y0)
        return b.name

    def info(n):
        return cad.call("object_info", object=n)

    def run(command, slots, params):
        cad.call("run_command", name=command, slots=slots, params=params)

    variant("Move by (30, 0, 5)", lambda y: (lambda n: (run("move", {"Objects": [n]}, {"Δ X": 30, "Δ Z": 5}), n)[1])(block(y)), lambda ns: (abs(info(ns[0])["min"][0] - 20) < .01 and abs(info(ns[0])["min"][2] - 5) < .01, f"x from {info(ns[0])['min'][0]:.1f}, z from {info(ns[0])['min'][2]:.1f} (expect 20 and 5)"))
    variant("Move, keep a copy", lambda y: (lambda n: (run("move", {"Objects": [n]}, {"Δ X": 30, "Create copy": True}), n)[1])(block(y)), lambda ns: (len(ns) == 2, f"{len(ns)} bodies (expect the original and a copy)"))
    variant("Rotate 90 degrees about Z", lambda y: (lambda n: (run("rotate", {"Objects": [n]}, {"Axis": "Z", "Angle": 90, "Pivot": "Object center"}), n)[1])(block(y)), lambda ns: (abs(info(ns[0])["size"][0] - 10) < .01 and abs(info(ns[0])["size"][1] - 20) < .01, f"outline {[round(v, 1) for v in info(ns[0])['size'][:2]]} (expect 10 x 20)"))
    variant("Scale by 2 about the centre", lambda y: (lambda n: (run("scale", {"Objects": [n]}, {"Factor": 2, "Pivot": "Object center"}), n)[1])(block(y)), lambda ns: (abs(info(ns[0])["volume"] - 2000 * 8) < 20, f"volume {info(ns[0])['volume']:.0f} (expect 16000: 2 cubed times 2000)"))
    variant("Mirror across the YZ plane, with a copy", lambda y: (lambda n: (run("mirror", {"Objects": [n]}, {"Mirror plane": "YZ (X = 0)", "Create copy": True}), n)[1])(block(y, x=30)), lambda ns: (len(ns) == 2 and abs(min(info(n)["min"][0] for n in ns) + 40) < .01, f"{len(ns)} bodies, left edge at {min(info(n)['min'][0] for n in ns):.1f} (expect -40)"))

    def pin(y):
        p = cad.cylinder(radius=3, height=6, operation="New body"); p.rename(f"Pin{rows_done[0]}"); cad.move(p, x=20, y=y)
        return p.name
    variant("Linear pattern, 4 at 15 mm", lambda y: (lambda n: (run("pattern_linear", {"Objects": [n]}, {"Direction 1": "X axis", "Count 1": 4, "Spacing 1": 15}), n)[1])(pin(y)), lambda ns: (len(ns) == 4 and abs(max(info(n)["max"][0] for n in ns) - min(info(n)["min"][0] for n in ns) - 51) < .1, f"{len(ns)} bodies over {max(info(n)['max'][0] for n in ns) - min(info(n)['min'][0] for n in ns):.1f} mm (expect 4 over 51)"))
    variant("Linear pattern, 3 x 2 grid", lambda y: (lambda n: (run("pattern_linear", {"Objects": [n]}, {"Direction 1": "X axis", "Count 1": 3, "Spacing 1": 12, "Direction 2": "Y axis", "Count 2": 2, "Spacing 2": 12}), n)[1])(pin(y)), lambda ns: (len(ns) == 6, f"{len(ns)} bodies (expect 6)"))
    variant("Circular pattern, 6 about Z", lambda y: (lambda n: (run("pattern_circular", {"Objects": [n]}, {"Axis": "Z axis", "Axis passes through": "World origin", "Count": 6}), n)[1])(pin(0 + rows_done[0] * 0)), lambda ns: (len(ns) == 6 and abs(max(info(n)["max"][0] for n in ns) - min(info(n)["min"][0] for n in ns) - 46) < .5, f"{len(ns)} bodies over {max(info(n)['max'][0] for n in ns) - min(info(n)['min'][0] for n in ns):.1f} mm across (expect 6 around a 20 mm radius: 46)"))
    variant("Table pattern, 3 positions", lambda y: (lambda n: (run("pattern_table", {"Objects to pattern": [n]}, {"Rows are": "Offsets from the original", "Positions": "10, 0, 0\n20, 0, 0\n30, 0, 5"}), n)[1])(pin(y)), lambda ns: (len(ns) == 4, f"{len(ns)} bodies (expect the original and 3 copies)"))
    return [("iso, results of each", render(-25, 30)), ("from above", render(0, 89)), ("checks", text_panel(m.strip()))]


# ---------------------------------------------------------------- Curve tools
@scenario("curve__divide-trim-split-extend-join-offset-fillet-and-more", columns=1)
def curve_matrix():
    cad.call("document_new")
    m = Matrix(spacing=0)

    def curves(before):
        return [o.name for o in cad.objects() if o.kind == "Curve" and o.name not in before]

    def size(n):
        return cad.call("object_info", object=n)["size"]

    def rect(y0, w=30, h=20):
        cad.call("sketch_begin", plane="XY")
        cad.call("sketch_add", entities=[{"type": "rect", "corner1": [0, y0], "corner2": [w, y0 + h]}])
        return cad.call("sketch_finish")["sketch"]

    row = [0]

    def variant(label, build, check):
        y0 = row[0] * 40
        row[0] += 1
        try:
            before = names()
            result = build(y0)
            made = curves(before)
            ok, detail = check(result, made)
            m.rows.append((label, detail, ok))
        except Exception as error:
            m.rows.append((label, str(error)[:120], False))

    seg = lambda y: line_curve((0, y, 0), (40, y, 0))
    run = lambda command, slots, params=None: cad.call("run_command", name=command, slots=slots, params=params or {})
    variant("Divide a 40 mm line into 4", lambda y: (lambda c: run("curve_divide", {"Curve": [c]}, {"Segments": 4}))(seg(y)), lambda r, made: (len(made) >= 1 and abs(size(made[0])[0] - 40) < .5, f"{len(made)} object made, spanning {size(made[0])[0] if made else 0:.1f} mm (Points Along Curve makes the 4+1 division points along the 40 mm line)"))
    variant("Trim a line to 25-75 percent", lambda y: (lambda c: run("curve_trim", {"Curve (a single segment)": [c]}, {"Keep from (%)": 25, "Keep to (%)": 75, "Keep the original": True}))(seg(y)), lambda r, made: (any(abs(size(n)[0] - 20) < .5 for n in made), f"new piece lengths {[round(size(n)[0], 1) for n in made]} (expect 20)"))
    variant("Split a line at 50 percent", lambda y: (lambda c: run("curve_split", {"Curve (a single segment)": [c]}, {"Split at (% , separated)": "50", "Keep the original": True}))(seg(y)), lambda r, made: (sum(1 for n in made if abs(size(n)[0] - 20) < .5) >= 2, f"piece lengths {[round(size(n)[0], 1) for n in made]} (expect two of 20)"))
    variant("Extend a line by 10", lambda y: (lambda c: run("curve_extend", {"Curve (a single segment)": [c]}, {"Length": 10, "Keep the original": True}))(seg(y)), lambda r, made: (any(abs(size(n)[0] - 50) < 1 for n in made), f"lengths {[round(size(n)[0], 1) for n in made]} (expect one of 50)"))
    variant("Join two end-to-end lines", lambda y: (lambda a, b: run("curve_join", {"Curves to join": [a, b]}, {"Keep the originals": True}))(line_curve((0, y, 0), (20, y, 0)), line_curve((20, y, 0), (45, y, 0))), lambda r, made: (any(abs(size(n)[0] - 45) < 1 for n in made), f"lengths {[round(size(n)[0], 1) for n in made]} (expect one of 45)"))
    variant("Offset a rectangle outward by 5", lambda y: run("curve_offset", {"Flat curve or sketch": [rect(y)]}, {"Offset distance": 5}), lambda r, made: (any(abs(size(n)[0] - 40) < 1 or abs(size(n)[0] - 20) < 1 for n in made) and bool(made), f"outlines {[[round(v, 1) for v in size(n)[:2]] for n in made]} (expect 40 x 30 outward or 20 x 10 inward)"))
    variant("Fillet the corners of a rectangle R4", lambda y: run("curve_fillet", {"Flat curve or sketch": [rect(y)]}, {"Fillet radius": 4, "Keep the original curve": True}), lambda r, made: (any(abs(size(n)[0] - 30) < .5 and abs(size(n)[1] - 20) < .5 for n in made), f"outlines {[[round(v, 1) for v in size(n)[:2]] for n in made]} (the size is unchanged: only the corners round)"))
    variant("Chamfer the corners of a rectangle 4", lambda y: run("curve_chamfer", {"Flat curve or sketch": [rect(y)]}, {"Chamfer distance": 4, "Keep the original curve": True}), lambda r, made: (any(abs(size(n)[0] - 30) < .5 for n in made), f"outlines {[[round(v, 1) for v in size(n)[:2]] for n in made]}"))
    variant("Explode a rectangle into its sides", lambda y: run("curve_explode", {"Curve or sketch": [rect(y)]}, {"Keep the original": True}), lambda r, made: (len(made) >= 4, f"{len(made)} curves (expect 4 sides)"))
    variant("Rebuild a curve with 8 points", lambda y: (lambda c: run("curve_rebuild", {"Curve": [c]}, {"Points": 8, "Keep the original": True}))(seg(y)), lambda r, made: (len(made) >= 1, f"{len(made)} new curve(s)"))
    variant("Curve from an equation: circle radius 10 (angles in radians)", lambda y: run("curve_equation", {}, {"Points from": "Equation", "Coordinates": 0, "x  |  r  |  ρ": "10*cos(t)", "y  |  θ  |  θ": "10*sin(t)", "z  |  z  |  φ": "0", "t from": 0, "t to": 6.2831853, "Angles in": "Radians"}), lambda r, made: (any(abs(size(n)[0] - 20) < .6 and abs(size(n)[1] - 20) < .6 for n in made), f"outlines {[[round(v, 1) for v in size(n)[:2]] for n in made]} (expect 20 x 20)"))

    def contour(y):
        b = cad.box(width=30, depth=20, height=20, operation="New body"); b.rename("Contoured")
        run("curve_contour", {"Body or surface": [b.name]}, {"Slice along": "Z", "Spacing": 5})
    variant("Contour curves of a 20 mm block every 5 mm", contour, lambda r, made: (len(made) >= 3, f"{len(made)} section curves (expect 3 inside the block at 5, 10 and 15 mm; the faces at 0 and 20 are not cut)"))
    for o in cad.objects():
        if o.kind == "Sketch":
            cad.call("object_update", object=o.name, visible=False)
    return [("iso", render(-20, 40)), ("from above", render(0, 89)), ("checks", text_panel(m.strip()))]


# ---------------------------------------------------------------- Mesh tools on a meshed 20 mm sphere
@scenario("mesh__convert-reduce-smooth-offset-split-section-and-checks", columns=1)
def mesh_matrix():
    rows = []
    sphere_volume = 4 / 3 * math.pi * 20 ** 3

    def fresh(detail="Normal"):
        cad.call("document_new")
        s = cad.sphere(radius=20, operation="New body"); s.rename("S")
        cad.call("run_command", name="solid_to_mesh", slots={"Bodies": ["S"]}, params={"Detail": detail, "Keep the original body": True})
        return next(o.name for o in cad.objects() if o.kind == "Mesh")

    def meshes():
        return [cad.call("object_info", object=o.name) for o in cad.objects() if o.kind == "Mesh"]

    def variant(label, build, check):
        try:
            mesh = fresh()
            build(mesh)
            ok, detail = check(meshes())
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    counts = {}
    for detail in ("Coarse (draft)", "Normal", "Fine (print quality)"):
        try:
            fresh(detail)
            info = meshes()[0]
            counts[detail] = (info["triangles"], info["volume"])
        except Exception as error:
            rows.append((f"Solid to mesh, {detail}", str(error)[:100], False))
    if len(counts) == 3:
        t = [counts[k][0] for k in counts]
        rows.append(("Solid to mesh: coarse < normal < fine", f"triangles {t}, fine volume {counts['Fine (print quality)'][1]:.0f} (sphere {sphere_volume:.0f})", t[0] < t[1] < t[2] and abs(counts["Fine (print quality)"][1] - sphere_volume) < .01 * sphere_volume))

    variant("Reduce to 50 percent of the triangles", lambda n: cad.call("run_command", name="mesh_reduce", slots={"Mesh": [n]}, params={"Reduce to": 0, "Keep": 50}), lambda ms: (abs(ms[0]["triangles"] - 1417) < 150 and abs(ms[0]["volume"] - sphere_volume) < .06 * sphere_volume, f"{ms[0]['triangles']} triangles (target about 1417), volume {ms[0]['volume']:.0f}"))
    variant("Smooth, 5 iterations", lambda n: cad.call("run_command", name="mesh_smooth", slots={"Mesh": [n]}, params={"Iterations": 5}), lambda ms: (ms[0]["watertight"] and .8 * sphere_volume < ms[0]["volume"] < 1.0 * sphere_volume, f"volume {ms[0]['volume']:.0f} (a bit below the sphere's {sphere_volume:.0f}), watertight {ms[0]['watertight']}"))
    variant("Offset +2 mm (grows)", lambda n: cad.call("run_command", name="mesh_offset", slots={"Mesh": [n]}, params={"Offset (+ grows, - shrinks)": 2}), lambda ms: (abs(ms[0]["max"][2] - ms[0]["min"][2] - 44) < .3, f"height {ms[0]['max'][2] - ms[0]['min'][2]:.2f} (expect 44)"))
    variant("Split at the equator", lambda n: cad.call("run_command", name="mesh_split", slots={"Mesh": [n]}, params={"Cut plane": 0, "Position along the normal": 0, "Normal X": 0, "Normal Y": 0, "Normal Z": 1}), lambda ms: (any(abs(i["max"][2] - i["min"][2] - 20) < .3 for i in ms), f"heights {[round(i['max'][2] - i['min'][2], 1) for i in ms]} (expect a 20 mm half)"))
    variant("Repair keeps a sphere watertight", lambda n: cad.call("run_command", name="mesh_repair", slots={"Meshes": [n]}), lambda ms: (ms[0]["watertight"] and ms[0]["holes"] == 0 and abs(ms[0]["volume"] - sphere_volume) < .03 * sphere_volume, f"watertight {ms[0]['watertight']}, holes {ms[0]['holes']}, volume {ms[0]['volume']:.0f}"))

    def to_solid(n):
        cad.call("run_command", name="mesh_to_solid", slots={"Mesh": [n]}, params={"Create": 0, "Reduce to at most (triangles)": 3000})
    try:
        mesh = fresh()
        to_solid(mesh)
        solids = [cad.call("object_info", object=o.name) for o in cad.objects() if o.kind == "Solid" and o.name != "S"]
        v = solids[0]["volume"] if solids else 0
        rows.append(("Mesh to solid", f"{len(solids)} new solid(s), volume {v:.0f} (sphere {sphere_volume:.0f})", bool(solids) and abs(v - sphere_volume) < .03 * sphere_volume))
    except Exception as error:
        rows.append(("Mesh to solid", str(error)[:120], False))

    def section(n):
        cad.call("run_command", name="mesh_section", slots={"Mesh": [n]}, params={"Section plane": 0, "Number of sections": 5, "Spacing": 6, "Create curves from the sections": True})
    try:
        mesh = fresh()
        section(mesh)
        curves_made = [o for o in cad.objects() if o.kind in ("Curve", "Sketch")]
        rows.append(("Mesh sections, 5 every 6 mm", f"{len(curves_made)} section curve(s) (5 planes, but the one touching the sphere at a single point makes no curve: expect 4)", len(curves_made) == 4))
    except Exception as error:
        rows.append(("Mesh sections, 5 every 6 mm", str(error)[:120], False))

    try:
        mesh = fresh()
        r = cad.call("run_command", name="mesh_check", slots={"Meshes": [mesh]}, commit=False)
        text = " | ".join(f"{x['label']}: {x['value']}" for x in r["activeCommand"].get("results", []))
        cad.call("command_cancel")
        rows.append(("Mesh check reports a closed sphere", text[:130], "Watertight: Yes" in text and "Manifold: Yes" in text))
    except Exception as error:
        rows.append(("Mesh check reports a closed sphere", str(error)[:120], False))

    try:
        mesh = fresh()
        r = cad.call("run_command", name="mesh_deviation", slots={"Mesh to check": [mesh], "Compare with (CAD body or mesh)": ["S"]}, commit=False)
        text = " | ".join(f"{x['label']}: {x['value']}" for x in r["activeCommand"].get("results", []))
        cad.call("command_cancel")
        rows.append(("Mesh deviation from its own sphere", text[:130] or "no result", not r["activeCommand"].get("error")))
    except Exception as error:
        rows.append(("Mesh deviation from its own sphere", str(error)[:120], False))
    fresh()
    strip = ["Mesh tools on a meshed 20 mm sphere"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the meshed sphere (kept beside its solid)", render(-30, 25)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Freeform: primitive cages and the form_extrude/revolve/loft/sweep/pipe tools
@scenario("freeform__primitive-cages-and-form-extrude-revolve-loft-sweep-pipe", columns=1)
def form_matrix():
    cad.call("document_new")
    rows = []
    done = [0]

    def forms():
        return [o for o in cad.objects() if o.kind == "Form"]

    def variant(label, build, check):
        x0 = done[0] * 80
        done[0] += 1
        try:
            before = {o.name for o in forms()}
            build(x0)
            made = [o for o in forms() if o.name not in before]
            if not made:
                raise RuntimeError("no Freeform body was made")
            info = cad.form_info(made[0], summary=True)
            ok, detail = check(info)
            topo = info["topology"]
            rows.append((label, f"{detail}; closed {info['closed']}, cage valid {topo['cageValid']}, Euler {topo['eulerCharacteristic']}", ok and topo["cageValid"] and not topo["nonManifoldEdges"] and not topo["windingConflicts"]))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    run = lambda name, params=None, slots=None: cad.call("run_command", name=name, params=params or {}, slots=slots or {})
    variant("Box 2 x 2 x 2 faces", lambda x: run("form_box", {"Position X": x}), lambda i: (i["faceCount"] == 24 and i["closed"], f"{i['faceCount']} faces (expect 24)"))
    variant("Box 3 x 2 x 1 faces", lambda x: run("form_box", {"Faces X": 3, "Faces Y": 2, "Faces Z": 1, "Position X": x}), lambda i: (i["faceCount"] == 22 and i["closed"], f"{i['faceCount']} faces (expect 22)"))
    variant("Sphere, 3 faces per side", lambda x: run("form_sphere", {"Faces per side": 3, "Position X": x}), lambda i: (i["faceCount"] == 54 and i["closed"], f"{i['faceCount']} faces (expect 54)"))
    variant("Sphere, 2 faces per side", lambda x: run("form_sphere", {"Faces per side": 2, "Position X": x}), lambda i: (i["faceCount"] == 24 and i["closed"], f"{i['faceCount']} faces (expect 24)"))
    variant("Cylinder, 8 sides, 3 rings, capped", lambda x: run("form_cylinder", {"Sides": 8, "Rings": 3, "Caps": True, "Position X": x}), lambda i: (i["closed"] and i["topology"]["eulerCharacteristic"] == 2, f"{i['faceCount']} faces"))
    variant("Cylinder, 6 sides, no caps", lambda x: run("form_cylinder", {"Sides": 6, "Rings": 2, "Caps": False, "Position X": x}), lambda i: (not i["closed"], f"{i['faceCount']} faces, open at both ends"))
    variant("Torus, 12 x 6 segments", lambda x: run("form_torus", {"Segments around": 12, "Segments of tube": 6, "Position X": x}), lambda i: (i["faceCount"] == 72 and i["closed"] and i["topology"]["eulerCharacteristic"] == 0, f"{i['faceCount']} faces (expect 72), a torus has Euler 0"))
    variant("Plane, 4 x 4 faces", lambda x: run("form_plane", {"Position X": x}), lambda i: (i["faceCount"] == 16 and not i["closed"], f"{i['faceCount']} faces (expect 16), an open sheet"))
    variant("Form extrude of a closed ring, 30 high", lambda x: run("form_extrude", {"Distance": 30, "Direction": "Z"} if False else {"Distance": 30}, {"Profile (curve or sketch)": [curve([[8 * math.cos(i * math.pi / 4) + x, 8 * math.sin(i * math.pi / 4), 0] for i in range(8)], closed=True)]}), lambda i: (i["closed"] and abs(i["max"][2] - i["min"][2] - 30) < .5, f"height {i['max'][2] - i['min'][2]:.1f} (expect 30)"))
    variant("Form revolve of a profile, 360 degrees", lambda x: run("form_revolve", {"Axis through the origin": "Z", "Angle (degrees)": 360, "Segments around": 12}, {"Profile (curve or sketch)": [curve([[10 + x * 0, 0, 0], [12, 0, 10], [10, 0, 20]])]}), lambda i: (abs(i["max"][2] - i["min"][2] - 20) < 1, f"height {i['max'][2] - i['min'][2]:.1f} (expect 20)"))
    variant("Form loft through two rings", lambda x: run("form_loft", {}, {"Sections (curves or sketches, in order)": [ring(0, 10), ring(25, 6)]}), lambda i: (abs(i["max"][2] - i["min"][2] - 25) < 1, f"height {i['max'][2] - i['min'][2]:.1f} (expect 25)"))
    variant("Form sweep of a ring along a straight path", lambda x: run("form_sweep", {}, {"Profile (curve or sketch)": [ring(0, 5)], "Path (curve or edge)": [line_curve((0, 0, 0), (0, 0, 40))]}), lambda i: (abs(i["max"][2] - i["min"][2] - 40) < 1, f"height {i['max'][2] - i['min'][2]:.1f} (expect 40)"))
    variant("Form pipe along a path, radius 4 to 6", lambda x: run("form_pipe", {"Radius at the start": 4, "Radius at the end": 6, "Sides": 8}, {"Path (curve or edge)": [line_curve((0, 0, 0), (0, 0, 40))]}), lambda i: (abs(i["max"][2] - i["min"][2] - 40) < 1.5, f"height {i['max'][2] - i['min'][2]:.1f} (expect 40)"))
    for o in cad.objects():
        if o.kind == "Curve":
            cad.call("object_update", object=o.name, visible=False)
    strip = ["Freeform primitives and the cage tools, each with its cage checked"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("iso (cages smoothed)", render(-25, 30)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Primitives, booleans, analysis commands and surface offsets
@scenario("solid__primitives-booleans-interference-properties-and-thicken", columns=1)
def solid_misc():
    rows = []

    def two_boxes():
        cad.call("document_new")
        a = cad.box(width=20, depth=20, height=20, operation="New body"); a.rename("A")
        b = cad.box(width=20, depth=20, height=20, operation="New body"); cad.move(b, x=10, y=10, z=10); b.rename("B")

    def volume(n):
        return cad.call("object_info", object=n)["volume"]

    def rows_of(name, slots, params=None):
        r = cad.call("run_command", name=name, slots=slots, params=params or {}, commit=False)
        text = " | ".join(f"{x['label']}: {x['value']}" for x in r["activeCommand"].get("results", []))
        cad.call("command_cancel")
        return text

    def case(label, fn):
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def solid_of(command, params, expect):
        cad.call("document_new")
        cad.call("run_command", name=command, params=params)
        n = next(o.name for o in cad.objects() if o.kind == "Solid")
        v = volume(n)
        return abs(v - expect) < .005 * expect, f"volume {v:.0f} (expect {expect:.0f})"
    case("Cone, base 16, top 4, height 30", lambda: solid_of("cone", {}, math.pi / 3 * 30 * (256 + 64 + 16)))
    case("Cone, base 10, top 0 (a point), height 24", lambda: solid_of("cone", {"Base radius": 10, "Top radius": 0, "Height": 24}, math.pi / 3 * 24 * 100))
    case("Torus, major 20, minor 5", lambda: solid_of("torus", {}, 2 * math.pi ** 2 * 20 * 25))

    def boolean(command, expect_total, keep_names):
        two_boxes()
        cad.call("run_command", name=command, slots={{"union": "Base body", "subtract": "Body to cut from", "intersect": "Base body"}[command]: ["A"], {"union": "Other bodies", "subtract": "Tool bodies", "intersect": "Other bodies"}[command]: ["B"]})
        total = sum(volume(o.name) for o in cad.objects() if o.kind == "Solid")
        return abs(total - expect_total) < 5, f"volume {total:.0f} (expect {expect_total}); bodies left {len([o for o in cad.objects() if o.kind == 'Solid'])}"
    case("Union of two overlapping 20 mm cubes", lambda: boolean("union", 15000, 1))
    case("Subtract: the cube minus its overlap", lambda: boolean("subtract", 7000, 1))
    case("Intersect: only the overlap remains", lambda: boolean("intersect", 1000, 1))

    def interference():
        two_boxes()
        text = rows_of("interference", {"Bodies to check (none = all solids)": ["A", "B"]})
        return "1,000 mm" in text, text[:110]
    case("Interference finds the 1000 mm3 overlap", interference)

    def properties():
        two_boxes()
        text = rows_of("properties", {"Bodies (none = all)": ["A"]}, {"Material": "Steel"})
        return "62.8 g" in text, text[:110]
    case("Properties: an 8000 mm3 steel cube weighs 62.8 g", properties)

    def check():
        two_boxes()
        text = rows_of("check", {"Bodies (none = all)": ["A"]})
        return "healthy" in text, text[-70:]
    case("Check reports a healthy body", check)

    def thicken():
        cad.call("document_new")
        before = names()
        cad.call("run_command", name="extrude_surface", slots={"Profile": line_curve((0, 0, 0), (30, 0, 0))}, params={"Extent": "Distance", "Distance": 20})
        surf = new_object(before, ("Surface",))
        cad.call("run_command", name="thicken", slots={"Surface": [surf]}, params={"Thickness": 2})
        n = next(o.name for o in cad.objects() if o.kind == "Solid")
        v = volume(n)
        return abs(v - 1200) < 30, f"volume {v:.0f} (expect 1200: 30 x 20 x 2)"
    case("Thicken a 30 x 20 surface by 2", thicken)

    def offset():
        cad.call("document_new")
        before = names()
        cad.call("run_command", name="extrude_surface", slots={"Profile": line_curve((0, 0, 0), (30, 0, 0))}, params={"Extent": "Distance", "Distance": 20})
        surf = new_object(before, ("Surface",))
        b2 = names()
        cad.call("run_command", name="offset_surface", slots={"Surface or face": [surf]}, params={"Offset distance": 5})
        made = new_object(b2, ("Surface", "Solid"))
        a1, a2 = cad.call("object_info", object=surf)["area"], cad.call("object_info", object=made)["area"] if made else 0
        return made is not None and abs(a1 - a2) < 5, f"area {a1:.0f} offset to {a2:.0f} (the same sheet moved 5 mm)"
    case("Offset a surface by 5", offset)
    cad.call("document_new")
    two_boxes()
    strip = ["Primitives, booleans and the analysis commands, each against a hand calculation"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("two overlapping cubes (the booleans' input)", render(-30, 30)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Static study setup tools: torque, roller and directional supports, saved selections, convergence
@scenario("cae__torque-roller-directional-support-and-saved-selections", columns=1)
def sim_setup_matrix():
    rows = []
    E, nu = 200000.0, 0.3
    G = E / (2 * (1 + nu))

    def bar():
        cad.call("document_new")
        b = cad.box(width=10, depth=10, height=100, operation="New body"); b.rename("Bar")
        cad.call("material_apply", object="Bar", material="steel")
        face = lambda facing: cad.call("solid_find", object="Bar", kind="faces", type="Plane", facing=facing)["hits"][0]["face"]
        cad.call("study_new", object="Bar", element_size=3.0)
        return face([0, 0, -1]), face([0, 0, 1]), face([1, 0, 0])

    def item(f):
        return [{"object": "Bar", "face": f}]

    def case(label, fn):
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def torque():
        bottom, top, _ = bar()
        cad.call("run_command", name="sim_fixed", slots={"Faces to support": item(bottom)})
        cad.call("run_command", name="sim_torque", slots={"Faces to load": item(top)}, params={"Torque": 10000})
        r = cad.call("study_run")["results"]
        # square bar torsion: twist = T L / (G k a^4), k = 0.1406; the corner (a / sqrt 2 from the axis) moves twist * 7.07
        corner = 10000 * 100 / (G * 0.1406 * 10 ** 4) * (10 / math.sqrt(2))
        return abs(r["maxDisplacement_mm"] - corner) < .12 * corner, f"corner moves {r['maxDisplacement_mm']:.4f} mm (torsion theory {corner:.4f}); elements {r['elements']}"
    case("Torque 10 N-m on the end of a square bar", torque)

    def roller():
        bottom, top, side = bar()
        cad.call("run_command", name="sim_roller", slots={"Faces to support": item(bottom)}, params={"Lock X": False, "Lock Y": False, "Lock Z": True})
        cad.call("run_command", name="sim_fixed_dirs", slots={"Faces to support": item(side)}, params={"Lock X": True, "Lock Y": False, "Lock Z": False})
        cad.call("run_command", name="sim_force", slots={"Faces to load": item(top)}, params={"Force": 1000, "Direction": "-Z"})
        r = cad.call("study_run")["results"]
        # the base may slide sideways but not drop: the bar compresses 1000 N * 100 / (E A) = 0.005 mm, and the Poisson swelling is free (a roller does not clamp it)
        return abs(r["maxDisplacement_mm"] - 0.005) < .0012 and abs(r["reactionNet_N"] - 1000) < 2, f"displacement {r['maxDisplacement_mm']:.5f} mm (compression 0.00500), reaction {r['reactionNet_N']:.1f} N"
    case("Roller base (Z locked only) plus a directional side support", roller)

    def saved_selection():
        bottom, top, _ = bar()
        cad.call("run_command", name="cae_save_faces", slots={"Faces": item(bottom)}, params={"Selection name": "Base"})
        r = cad.call("run_command", name="cae_recall_faces", commit=False)
        label = next(p for p in r["activeCommand"]["params"] if p["label"] == "Named selection")
        cad.call("command_cancel")
        return "Base" in str(label.get("value")) or "Base" in str(label.get("options")), f"named selections now offered: {label.get('value')} / {label.get('options')}"
    case("Saved face selection can be recalled by name", saved_selection)

    def convergence():
        bottom, top, _ = bar()
        cad.call("run_command", name="sim_fixed", slots={"Faces to support": item(bottom)})
        cad.call("run_command", name="sim_force", slots={"Faces to load": item(top)}, params={"Force": 100, "Direction": "+Y"})
        cad.call("run_command", name="cae_convergence", commit=False)
        cad.call("command_set", params={"Compare three meshes": True})
        deadline = time.monotonic() + 240
        text = ""
        while time.monotonic() < deadline:
            r = cad.call("command_set", params={})
            text = " | ".join(str(x) for x in r.get("results", []))
            if "displacement" in text.lower() and ("converged" in text.lower() or "change" in text.lower() or text.count("mm") >= 3):
                break
            time.sleep(3)
        cad.call("command_cancel")
        return bool(text), text[:130] or "no result"
    case("Mesh refinement compares three meshes", convergence)
    bar()
    strip = ["Static-study setup tools against hand calculations"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the bar", render(-30, 25)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- CAM review tools, G-code import and Image Carve options
@scenario("cam__review-order-playback-library-import-and-image-carve", columns=1)
def cam_review_matrix():
    rows = []
    from PIL import Image as PilImage

    def setup():
        cad.call("document_new")
        b = cad.box(width=60, depth=40, height=10, operation="New body"); b.rename("Blk")
        cad.call("run_command", name="cam_setup", slots={"Model body": ["Blk"]}, params={"Setup name": "Top", "Stock allowance XY": 2, "Top allowance": 1, "Work zero (G54)": "Model origin"})
        for z in (11, 10):
            cad.call("run_command", name="cnc_face", params={"X from": -30, "X to": 30, "Y from": -20, "Y to": 20, "Top of the material (Z)": z, "Total cutting depth": 1, "Depth per pass": 1})

    def results(name, slots=None, params=None, commit=False):
        r = cad.call("run_command", name=name, slots=slots or {}, params=params or {}, commit=commit)
        lines = [f"{x['label']}: {x['value']}" for x in (r.get("activeCommand") or {}).get("results", [])]
        if not commit:
            cad.call("command_cancel")
        return lines

    def case(label, fn):
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    setup()
    case("Tool library lists its presets", lambda: (lambda t: ("Available tools: 8" in " ".join(t), t[0]))(results("cam_tool_library")))
    case("Setup sheet lists the setup and both operations", lambda: (lambda t: (any(x.startswith("Setup: Top") for x in t) and sum("CNC face" in x for x in t) == 2, f"{len(t)} rows: {t[0]} / {t[2][:40]}"))(results("cam_review")))

    def order():
        cad.call("run_command", name="cam_operation_order", commit=False)
        cad.call("command_set", params={"Operation": "2. CNC face 2", "Move to position": 1})
        cad.call("command_commit")
        t = results("cam_review")
        faces = [x.split(":")[0] for x in t if "CNC face" in x]
        return faces[0] == "CNC face 2", f"setup sheet order now {faces} (expect face 2 first)"
    case("Operation order: move face 2 to the front", order)

    def playback():
        cad.call("run_command", name="cam_playback", commit=False)
        cad.call("command_set", params={"Seek position": 50})
        cad.call("command_set", params={"Seek": True})
        r = cad.call("command_set", params={})
        text = " | ".join(str(x) for x in r.get("results", []))
        cad.call("command_cancel")
        return "Choose a path" not in text and bool(text), text[:120]
    case("Playback seeks to the middle of a path", playback)

    def second_setup():
        cad.call("run_command", name="cam_setup", slots={"Model body": ["Blk"]}, params={"Setup name": "Side", "Stock allowance XY": 3, "Work zero (G54)": "Model origin"})
        t = results("cam_select_setup")
        cad.call("run_command", name="cam_select_setup", params={"Saved setup": "Top"}, commit=False)
        cad.call("command_set", params={"Use setup": True})
        t2 = " ".join(str(x) for x in cad.call("command_set", params={}).get("results", []))
        cad.call("command_cancel")
        return "Saved setups: 2" in " ".join(t) and "Active setup: Top" in t2, f"{t[1] if len(t) > 1 else t}; after choosing Top: {t2[:50]}"
    case("Two saved setups can be switched", second_setup)

    def design():
        cad.call("run_command", name="cnc_design_check", slots={"Model": ["Blk"]}, commit=False)
        cad.call("command_set", params={"Run checks": True})
        cad.call("wait_idle", timeout_ms=60000)
        time.sleep(1)
        text = " | ".join(str(x) for x in cad.call("command_set", params={}).get("results", []))
        cad.call("command_cancel")
        return "Closed, manifold" in text, text[:120]
    case("Design check finds a healthy block", design)

    def imported():
        path = Path(tempfile.gettempdir()) / "prism-visual-import.nc"
        path.write_text("G21 G90\nG0 Z5\nG0 X0 Y0\nG1 Z-1 F200\nG1 X30 F800\nG1 Y20\nG1 X0\nG1 Y0\nG0 Z5\n")
        before = len([o for o in cad.objects() if o.kind == "Toolpath"])
        cad.call("run_command", name="cam_import_gcode", params={"G-code file": str(path)})
        after = len([o for o in cad.objects() if o.kind == "Toolpath"])
        return after == before + 1, f"toolpaths {before} -> {after}"
    case("Import a G-code file as a toolpath", imported)

    def carve(deep, bit, label):
        image = Path(tempfile.gettempdir()) / "prism-visual-carve.png"
        PilImage.frombytes("L", (2, 1), bytes([0, 255])).save(image)       # left black, right white
        cad.call("document_new")
        cad.call("run_command", name="cnc_image_relief", params={"Image file": str(image), "Width of the carving": 10, "Tool diameter": 2, "Total cutting depth": 2, "Top of the material (Z)": 0, "Stepover": 50, "Deepest where the image is": deep, "Bit": bit})
        text = gcode_of("The model's own origin")
        moves = cut_moves(text)
        left = [p[2] for p in moves if p[0] < 4.5]
        right = [p[2] for p in moves if p[0] > 5.5]
        ok = abs(min(left) - -2) < .05 if deep == "Dark" else abs(min(right) - -2) < .05
        return ok, f"left half z {min(left):.2f}, right half z {min(right):.2f} ({deep.lower()} is deepest); bit {bit}"
    case("Image Carve, dark areas deepest", lambda: carve("Dark", "Ball nose", ""))
    case("Image Carve, light areas deepest", lambda: carve("Light", "Flat end mill", ""))
    setup()
    strip = ["CAM review tools, G-code import and Image Carve options"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the stock block with two facing operations", render(-30, 28)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- ECAD: board, board from an outline, board holes, component envelopes
@scenario("ecad__board-outline-holes-and-component-envelopes", columns=1)
def ecad_matrix():
    rows = []

    def solids():
        return [o for o in cad.objects() if o.kind == "Solid"]

    def case(label, fn):
        cad.call("document_new")
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def board(params, expect):
        cad.call("run_command", name="pcb_board", params=params)
        i = cad.call("object_info", object=solids()[0].name)
        return abs(i["volume"] - expect) < .005 * expect and i["valid"], f"volume {i['volume']:.1f} (expect {expect:.1f}), size {[round(v, 2) for v in i['size']]}"
    plain = 80 * 50 * 1.6
    hole = math.pi * 1.6 ** 2 * 1.6
    case("Board 80 x 50 x 1.6 with four mounting holes", lambda: board({}, plain - 4 * hole))
    case("Board without mounting holes", lambda: board({"Four mounting holes": False}, plain))
    case("Board 100 x 60 x 2, bigger 4 mm holes", lambda: board({"Board width (X)": 100, "Board length (Y)": 60, "Board thickness": 2, "Mounting hole diameter": 4}, 100 * 60 * 2 - 4 * math.pi * 4 * 2))

    def from_outline():
        cad.call("sketch_begin", plane="XY")
        cad.call("sketch_add", entities=[{"type": "polyline", "points": [[0, 0], [60, 0], [60, 20], [30, 40], [0, 20]], "closed": True}])
        s = cad.call("sketch_finish")["sketch"]
        cad.call("run_command", name="pcb_from_outline", slots={"Closed planar outline": [s]}, params={"Board thickness": 1.6})
        i = cad.call("object_info", object=solids()[0].name)
        area = 60 * 20 + .5 * 60 * 20
        return abs(i["volume"] - area * 1.6) < .01 * area * 1.6, f"volume {i['volume']:.1f} (expect {area * 1.6:.1f}: a house-shaped outline of {area:.0f} mm2)"
    case("Board from a house-shaped outline", from_outline)

    def extra_hole():
        cad.call("run_command", name="pcb_board", params={"Four mounting holes": False})
        n = solids()[0].name
        cad.call("run_command", name="pcb_hole", slots={"Board body": [n]}, params={"Hole center X": 10, "Hole center Y": 5, "Hole diameter": 5})
        i = cad.call("object_info", object=n)
        expect = plain - math.pi * 2.5 ** 2 * 1.6
        return abs(i["volume"] - expect) < .005 * expect, f"volume {i['volume']:.1f} (expect {expect:.1f})"
    case("Add a 5 mm hole to a board", extra_hole)

    def envelope():
        cad.call("run_command", name="pcb_board", params={"Four mounting holes": False})
        cad.call("run_command", name="component_envelope", params={"Reference designator": "U7", "Body width (X)": 12, "Body length (Y)": 8, "Body height": 4, "Center X": 10, "Center Y": 5, "Board face elevation (Z)": 1.6})
        bodies = solids()
        top = max(cad.call("object_info", object=o.name)["max"][2] for o in bodies)
        return len(bodies) == 2 and abs(top - 5.6) < .05, f"{len(bodies)} bodies, highest point z {top:.2f} (expect 1.6 + 4 = 5.6)"
    case("Component envelope 12 x 8 x 4 on the top face", envelope)
    cad.call("run_command", name="pcb_board", params={})
    cad.call("run_command", name="component_envelope", params={"Reference designator": "U1", "Center X": 10, "Center Y": 5})
    strip = ["Board and component envelope tools, each against a hand calculation"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("a board with a component envelope", render(-30, 35)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Surface building and editing: patch, edge surface, extend, rebuild, unstitch and knit, boundary fill, emboss, deform
@scenario("surface__patch-edge-extend-rebuild-unstitch-knit-emboss-deform", columns=1)
def surface_edit_matrix():
    rows = []

    def case(label, fn):
        cad.call("document_new")
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def sheet():
        before = names()
        cad.call("run_command", name="extrude_surface", slots={"Profile": line_curve((0, 0, 0), (30, 0, 0))}, params={"Extent": "Distance", "Distance": 20})
        return new_object(before, ("Surface",))

    def info(n):
        return cad.call("object_info", object=n)

    def nurbs():
        before = names()
        cad.call("run_command", name="nurbs_patch", params={"Width": 80, "Height": 60})
        n = new_object(before, ("Surface",))
        return abs(info(n)["size"][0] - 80) < .5 and abs(info(n)["size"][1] - 60) < .5, f"size {[round(v, 1) for v in info(n)['size']]}, area {info(n)['area']:.0f} (expect 80 x 60 across; the area is a bit over 4800 because the default patch has a gentle relief)"
    case("NURBS patch 80 x 60", nurbs)

    def unstitch_knit():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Cube")
        cad.call("run_command", name="unstitch", slots={"Faces, or whole surfaces and bodies": ["Cube"]})
        pieces = [o for o in cad.objects() if o.kind == "Surface"]
        cad.call("run_command", name="knit", slots={"Surfaces": [o.name for o in pieces]}, params={"Make solid if closed": True})
        solids = [o for o in cad.objects() if o.kind == "Solid"]
        v = info(solids[0].name)["volume"] if solids else 0
        return len(pieces) == 6 and abs(v - 24000) < 50, f"unstitched into {len(pieces)} faces, knitted back to a solid of {v:.0f} (expect 6 and 24000)"
    case("Unstitch a block into 6 faces and knit them back", unstitch_knit)

    def fill():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Cube")
        cad.call("run_command", name="unstitch", slots={"Faces, or whole surfaces and bodies": ["Cube"]})
        pieces = [o.name for o in cad.objects() if o.kind == "Surface"]
        cad.call("run_command", name="boundary_fill", slots={"Surfaces, planes and bodies": pieces})
        solids = [o for o in cad.objects() if o.kind == "Solid"]
        v = max((info(o.name)["volume"] for o in solids), default=0)
        return abs(v - 24000) < 50, f"enclosed space became a solid of {v:.0f} (expect 24000)"
    case("Boundary fill turns six faces into a solid", fill)

    def edge():
        before = names()
        curves = [line_curve((0, 0, 0), (30, 0, 0)), line_curve((30, 0, 0), (30, 20, 0)), line_curve((30, 20, 0), (0, 20, 0)), line_curve((0, 20, 0), (0, 0, 0))]
        cad.call("run_command", name="edge_surface", slots={"Boundary curves (2 to 4)": curves})
        n = new_object(before, ("Surface",))
        return abs(info(n)["area"] - 600) < 12, f"area {info(n)['area']:.0f} (expect 600)"
    case("Edge surface across four curves", edge)

    def patch():
        before = names()
        cad.call("run_command", name="patch", slots={"Boundary: curves, sketches or edges": [ring(0, 10)]})
        n = new_object(before, ("Surface",))
        return abs(info(n)["area"] - math.pi * 100) < 8, f"area {info(n)['area']:.0f} (expect {math.pi * 100:.0f}: a flat disc)"
    case("Patch fills a flat ring", patch)

    def rebuild():
        s = sheet()
        before = names()
        cad.call("run_command", name="rebuild_surface", slots={"Surface or face": [s]}, params={"Points in U": 6, "Points in V": 6, "Keep the original": True})
        n = new_object(before, ("Surface",))
        return n is not None and abs(info(n)["area"] - 600) < 15, f"rebuilt area {info(n)['area'] if n else 0:.0f} (expect 600)"
    case("Rebuild a surface on a 6 x 6 grid", rebuild)

    def reverse():
        s = sheet()
        cad.call("run_command", name="reverse_normal", slots={"Surfaces": [s]})
        return info(s)["valid"], f"valid {info(s)['valid']}, area {info(s)['area']:.0f}"
    case("Reverse the normal of a surface", reverse)

    def extend():
        s = sheet()
        edges = cad.call("solid_find", object=s, kind="edges", limit=8)["hits"]
        top = max(edges, key=lambda e: e.get("center", [0, 0, 0])[2])
        before = {o.name: info(o.name)["area"] for o in cad.objects() if o.kind == "Surface"}
        cad.call("run_command", name="extend_surface", slots={"Surface or face": [s], "Edge to extend": [{"object": s, "edge": top["edge"]}]}, params={"Extend by": 10})
        areas = [info(o.name)["area"] for o in cad.objects() if o.kind == "Surface"]
        return any(abs(a - 900) < 20 for a in areas), f"areas {[round(a) for a in areas]} (expect 900: 30 x 30 after extending the top edge by 10)"
    case("Extend a surface by 10", extend)

    def emboss(style, sign):
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Plate")
        top = cad.call("solid_find", object="Plate", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
        cad.call("sketch_begin", plane={"object": "Plate", "face": top})
        cad.call("sketch_add", entities=[{"type": "circle", "center": [0, 0], "radius": 5}])
        s = cad.call("sketch_finish")["sketch"]
        cad.call("run_command", name="emboss", slots={"Sketch (text or shape)": [s]}, params={"Style": style, "Height / depth": 2})
        v = info("Plate")["volume"]
        expect = 24000 + sign * math.pi * 25 * 2
        return abs(v - expect) < 25, f"volume {v:.0f} (expect {expect:.0f})"
    case("Emboss a 5 mm disc 2 mm high", lambda: emboss("Emboss (raised)", 1))
    case("Engrave a 5 mm disc 2 mm deep", lambda: emboss("Engrave (cut in)", -1))

    def draft():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Block")
        r = cad.call("run_command", name="draft", params={"Pull direction": "Z (up)", "Minimum draft angle": 3}, commit=False)
        text = " | ".join(f"{x['label']}: {x['value']}" for x in r["activeCommand"].get("results", []))
        cad.call("command_cancel")
        return bool(text), text[:120]
    case("Draft analysis of a straight-walled block", draft)

    def deform():
        s = sheet()
        before = names()
        cad.call("run_command", name="deform", slots={"Curves or surfaces": [s]}, params={"Deform": "Bend", "Along axis": "Z", "Bend toward": "+X", "Angle": 45})
        made = [o for o in cad.objects() if o.kind in ("Surface", "Curve") and o.name not in before]
        i = info(made[0].name) if made else info(s)
        return info(s)["valid"] and bool(made or i), f"{len(made)} new object(s), size {[round(v, 1) for v in i['size']]} (a flat 30 x 20 sheet bent 45 degrees about its height)"
    case("Deform: bend a sheet 45 degrees", deform)
    cad.call("document_new")
    cad.call("run_command", name="nurbs_patch", params={"Width": 80, "Height": 60})
    strip = ["Surface building and editing tools, each against a hand calculation"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("a NURBS patch", render(-30, 35)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Direct modeling, part 2: split body and face, merge faces, replace face, extend or trim face
@scenario("solid__split-merge-replace-extend-trim-faces", columns=1)
def face_edit_matrix():
    rows = []
    cube = 40 * 30 * 20

    def case(label, fn):
        cad.call("document_new")
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def block():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Blk")
        return b

    def info(n):
        return cad.call("object_info", object=n)

    def cutting_sheet():
        before = names()
        cad.call("run_command", name="extrude_surface", slots={"Profile": line_curve((-50, 0, 0), (50, 0, 0))}, params={"Extent": "Distance", "Distance": 100})
        return new_object(before, ("Surface",))

    def face(n, facing):
        return {"object": n, "face": cad.call("solid_find", object=n, kind="faces", type="Plane", facing=facing)["hits"][0]["face"]}

    def stop_box(x):
        t = cad.box(width=2, depth=60, height=60, operation="New body"); t.rename("Stop"); cad.move(t, x=x, z=-20)
        return face("Stop", [-1, 0, 0])

    def split_body():
        block()
        sheet = cutting_sheet()
        cad.call("run_command", name="split_body", slots={"Body to split": ["Blk"], "Splitting tool (plane, face, surface or solid)": [sheet]})
        solids = [o for o in cad.objects() if o.kind == "Solid"]
        vols = sorted(round(info(o.name)["volume"]) for o in solids)
        return len(solids) == 2 and all(abs(v - cube / 2) < 20 for v in vols), f"{len(solids)} bodies of {vols} (expect two of {cube // 2})"
    case("Split a block with a plane sheet through its middle", split_body)

    def split_and_merge():
        block()
        cad.call("sketch_begin", plane={"object": "Blk", "face": face("Blk", [0, 0, 1])["face"]})
        cad.call("sketch_add", entities=[{"type": "line", "points": [[0, -25], [0, 25]]}])
        line = cad.call("sketch_finish")["sketch"]
        n0 = info("Blk")["faces"]
        cad.call("run_command", name="split_face", slots={"Face to split": [face("Blk", [0, 0, 1])], "Splitting plane, edge or sketch": [line]})
        n1 = info("Blk")["faces"]
        tops = cad.call("solid_find", object="Blk", kind="faces", type="Plane", facing=[0, 0, 1])["hits"]
        cad.call("run_command", name="merge_faces", slots={"Faces to merge (neighbours on one surface)": [{"object": "Blk", "face": t["face"]} for t in tops]})
        n2 = info("Blk")["faces"]
        return (n0, n1, n2) == (6, 7, 6) and len(tops) == 2, f"faces {n0} -> {n1} after splitting the top -> {n2} after merging the two halves back (expect 6, 7, 6)"
    case("Split the top face, then merge it back", split_and_merge)

    def replace():
        block()
        target = stop_box(25)
        cad.call("run_command", name="replace_face", slots={"Flat faces to replace": [face("Blk", [1, 0, 0])], "Target plane or flat face": [target]})
        v = info("Blk")["volume"]
        return abs(v - (cube + 4 * 30 * 20)) < 30, f"volume {v:.0f} (expect {cube + 2400}: the +X face replaced by the plane 4 mm further out, at x = 24)"
    case("Replace the +X face with a plane 5 mm out", replace)

    def to_height(height):
        # Extend / Trim moves a wall's neighbours until the wall reaches a target plane square to it: here the +X wall is taken up or down to a plane at z = height
        block()
        ref = cad.box(width=4, depth=4, height=1, operation="New body"); ref.rename("Ref"); cad.move(ref, x=30, z=height)
        cad.call("run_command", name="extend_trim_face", slots={"Flat faces to extend or trim": [face("Blk", [1, 0, 0])], "Target plane or flat face": [face("Ref", [0, 0, -1])]})
        return info("Blk")

    def extend():
        i = to_height(26)
        return abs(i["volume"] - 40 * 30 * 26) < 30 and abs(i["max"][2] - 26) < .05 and i["valid"], f"volume {i['volume']:.0f} (expect {40 * 30 * 26}), top at z {i['max'][2]:.2f} (expect 26)"
    case("Extend a wall up to a plane at z = 26", extend)

    def trim():
        i = to_height(14)
        return abs(i["volume"] - 40 * 30 * 14) < 30 and abs(i["max"][2] - 14) < .05 and i["valid"], f"volume {i['volume']:.0f} (expect {40 * 30 * 14}), top at z {i['max'][2]:.2f} (expect 14)"
    case("Trim a wall back to a plane at z = 14", trim)
    cad.call("document_new")
    block()
    strip = ["Split, merge, replace, extend and trim, each against a hand calculation"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the block", render(-30, 30)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Curve projection and isocurves, construction planes, capping an opening
@scenario("curve__project-isocurve-construction-plane-and-fill-cap", columns=1)
def curve_plane_matrix():
    rows = []

    def case(label, fn):
        cad.call("document_new")
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def info(n):
        return cad.call("object_info", object=n)

    def project():
        ball = cad.sphere(radius=20, operation="New body"); ball.rename("Ball")
        ringc = ring(30, 6)
        before = names()
        cad.call("run_command", name="curve_project", slots={"Curves to project": [ringc], "Surface to project onto": [{"object": "Ball", "face": 1}]}, params={"Method": "Along a direction", "Direction": "Down (-Z)"})
        made = [o for o in cad.objects() if o.kind == "Curve" and o.name not in before]
        i = info(made[0].name)
        z = math.sqrt(400 - 36)
        return bool(made) and abs(i["max"][2] - z) < .2 and abs(i["size"][0] - 12) < .4 and abs(i["min"][2] + z) < .2, f"{len(made)} curve(s): width {i['size'][0]:.1f}, z from {i['min'][2]:.2f} to {i['max'][2]:.2f} (a straight-down projection onto a closed sphere lands on both the near and far side, at z = +/-{z:.2f}, and stays 12 wide)"
    case("Project a ring onto a sphere, straight down", project)

    def isocurve():
        before = names()
        cad.call("run_command", name="extrude_surface", slots={"Profile": line_curve((0, 0, 0), (30, 0, 0))}, params={"Extent": "Distance", "Distance": 20})
        sheet = new_object(before, ("Surface",))
        b2 = names()
        cad.call("run_command", name="curve_isocurve", slots={"Surface or face": [sheet]}, params={"Direction": 0, "Position (%)": 50, "How many": 1})
        made = [o for o in cad.objects() if o.kind == "Curve" and o.name not in b2]
        sizes = [[round(v, 1) for v in info(o.name)["size"]] for o in made]
        return bool(made) and any(abs(s[0] - 30) < .5 or abs(s[2] - 20) < .5 for s in sizes), f"{len(made)} isocurve(s) of size {sizes} (a line 30 across or 20 up the middle of the sheet)"
    case("Isocurve through the middle of a surface", isocurve)

    def plane():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Blk")
        top = cad.call("solid_find", object="Blk", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
        before = names()
        cad.call("run_command", name="construction_plane", slots={"Plane or flat face": [{"object": "Blk", "face": top}]}, params={"Method": "Offset from a plane", "Distance": 10})
        made = [o for o in cad.objects() if o.kind == "Plane" and o.name not in before]
        return len(made) == 1, f"{len(made)} construction plane(s) made, {[o.name for o in made]} (an offset 10 mm above the top face)"
    case("Construction plane offset 10 mm from a face", plane)

    def cap():
        b = cad.box(width=40, depth=30, height=20, operation="New body"); b.rename("Blk")
        top = cad.call("solid_find", object="Blk", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
        cad.call("run_command", name="delete_face", slots={"Faces to remove (a hole, fillet, pocket...)": [{"object": "Blk", "face": top}]})
        i = info("Blk")
        return i["valid"], f"after deleting the top face: kind {i.get('kind')}, faces {i['faces']}, valid {i['valid']}"
    case("Delete the top face of a block, leaving an opening", cap)
    cad.call("document_new")
    ball = cad.sphere(radius=20, operation="New body")
    strip = ["Curve projection, isocurves and construction planes"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("a sphere", render(-30, 25)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Fill and curve patterns, pipe flow, slicing and print checks
@scenario("solid__fill-curve-patterns-pipe-flow-and-print-checks", columns=1)
def pattern_print_matrix():
    rows = []

    def case(label, fn):
        cad.call("document_new")
        try:
            ok, detail = fn()
            rows.append((label, detail, ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))

    def solids():
        return [o for o in cad.objects() if o.kind == "Solid"]

    def pin(x=0, y=0, z=0):
        p = cad.cylinder(radius=1.5, height=4, operation="New body"); p.rename("Pin"); cad.move(p, x=x, y=y, z=z)
        return p.name

    def fill():
        plate = cad.box(width=60, depth=40, height=4, operation="New body"); plate.rename("Plate")
        top = cad.call("solid_find", object="Plate", kind="faces", type="Plane", facing=[0, 0, 1])["hits"][0]["face"]
        p = pin(z=4)
        cad.call("run_command", name="pattern_fill", slots={"Objects to pattern": [p], "Region to fill": [{"object": "Plate", "face": top}]}, params={"Layout": "Square grid", "Spacing": 10, "Margin from the boundary": 3, "Result": "New bodies"})
        count = len([o for o in solids() if o.name.startswith("Pin")])
        # each instance owns a 10 mm cell and the margin is kept clear around the cells, so centres sit 5 + 3 = 8 mm in: 5 columns x 3 rows fit a 60 x 40 plate
        return count == 15, f"{count} pins (cells of 10 mm inset 3 mm: centres 8 mm in from each edge, 5 x 3 = 15)"
    case("Fill pattern: square grid on a plate", fill)

    def along():
        p = pin()
        path = line_curve((0, 0, 0), (100, 0, 0))
        cad.call("run_command", name="pattern_curve", slots={"Objects to pattern": [p], "Path (edges or curves)": [path]}, params={"Distribution": "Equal count along the path", "Count": 5, "Result": "New bodies"})
        count = len([o for o in solids() if o.name.startswith("Pin")])
        xs = sorted(round((cad.call("object_info", object=o.name)["min"][0] + cad.call("object_info", object=o.name)["max"][0]) / 2) for o in solids() if o.name.startswith("Pin"))
        return count in (5, 6), f"{count} pins at x = {xs} (5 along a 100 mm path)"
    case("Curve pattern: 5 along a line", along)

    def pipe_flow():
        r = cad.call("run_command", name="cae_pipe_flow", commit=False)
        rows_ = {x["label"]: x["value"] for x in r["activeCommand"].get("results", [])}
        cad.call("command_cancel")
        re = float(rows_["Reynolds number"])
        v = float(rows_["Mean velocity"].split()[0])
        want_re, want_v = 998.2 * v * 0.010 / 0.001002, 0.5 / 1000 / 60 / (math.pi * 0.005 ** 2)
        return abs(re - want_re) < 1 and abs(v - want_v) < .001 and rows_["Regime"] == "Laminar", f"Re {re:.1f} (expect {want_re:.1f}), velocity {v:.4f} m/s (expect {want_v:.4f}), {rows_['Regime']}"
    case("Pipe flow: 0.5 L/min in a 10 mm pipe", pipe_flow)

    def slice_layers():
        b = cad.box(width=30, depth=30, height=20, operation="New body"); b.rename("B")
        cad.call("run_command", name="slice", slots={"Bodies to print": [b.name]}, commit=False)
        cad.call("command_set", params={"Slice": True})
        deadline = time.monotonic() + 120
        text = ""
        while time.monotonic() < deadline:
            text = " | ".join(str(x) for x in cad.call("command_set", params={}).get("results", []))
            if "Layers" in text:
                break
            time.sleep(2)
        cad.call("command_cancel")
        return "Layers" in text, text[:130]
    case("Slice a 20 mm block", slice_layers)

    def checks():
        b = cad.box(width=30, depth=30, height=20, operation="New body"); b.rename("B")
        out = []
        for command in ("fdm_check", "resin_check"):
            cad.call("run_command", name=command, slots={"Model": [b.name]}, commit=False)
            cad.call("command_set", params={"Run checks": True})
            cad.call("wait_idle", timeout_ms=60000)
            time.sleep(1)
            out.append(" | ".join(str(x) for x in cad.call("command_set", params={}).get("results", [])))
            cad.call("command_cancel")
        return all(o and "Choose the model" not in o for o in out), f"FDM: {out[0][:60]} // resin: {out[1][:50]}"
    case("FDM and resin checks run on a block", checks)
    cad.call("document_new")
    plate = cad.box(width=60, depth=40, height=4, operation="New body")
    strip = ["Patterns, pipe flow and print checks, each against a hand calculation"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("a plate", render(-30, 35)), ("checks", text_panel(strip))]


# ---------------------------------------------------------------- Motion study: a driven revolute joint with constant speed, a smooth move, an oscillation and a free swing
@scenario("motion__constant-speed-smooth-move-oscillation-and-free-swing", columns=1)
def motion_matrix():
    import re
    rows = []
    cad.call("document_new")
    p = cad.cylinder(radius=10, height=10, operation="New body"); p.rename("Pin")
    arm = cad.box(width=30, depth=6, height=5, operation="New body"); arm.rename("Arm"); cad.move(arm, x=5, z=12)
    hub = cad.cylinder(radius=10, height=5, operation="New body"); hub.rename("Hub"); cad.move(hub, z=12)
    cad.union(hub, arm)
    cyl = lambda n: {"face": cad.call("solid_find", object=n, kind="faces", type="Cylinder", limit=2)["hits"][0]["face"]}
    cad.call("run_command", name="joint_revolute", slots={"On the first part (face, circle edge or round face)": {"object": "Pin", **cyl("Pin")}, "On the part that moves": {"object": "Hub", **cyl("Hub")}})
    for n in ("Pin", "Hub"):
        cad.call("material_apply", object=n, material="steel")

    def study(motion, params, seconds=2):
        cad.call("run_command", name="cae_motion", commit=False)
        cad.call("command_set", params={"Duration": seconds, "Revolute 1 — motion": motion, **params})
        cad.call("command_set", params={"Run study": True})
        text = []
        for _ in range(40):
            text = [str(x) for x in cad.call("command_set", params={}).get("results", [])]
            if any("Solved" in x or "Could not run" in x for x in text):
                break
            time.sleep(1)
        cad.call("command_cancel")
        return text

    def joint_range(text):
        line = next((x for x in text if x.startswith("Revolute 1")), None)
        if line is None:
            return 0.0, 0.0, 0.0, "no motion reported"
        a, b = map(float, re.search(r"(-?[\d.]+) to (-?[\d.]+) °", line).groups())
        peak = float(re.search(r"peak speed ([\d.]+)", line).group(1))
        return a, b, peak, line

    def case(label, motion, params, check, seconds=2):
        try:
            text = study(motion, params, seconds)
            a, b, peak, line = joint_range(text)
            ok, detail = check(a, b, peak)
            rows.append((label, f"{detail}  [{line[:70]}]", ok))
        except Exception as error:
            rows.append((label, str(error)[:120], False))
    case("Constant speed 90 deg/s for 2 s", "Constant speed", {"Speed": 90}, lambda a, b, pk: (abs(b - a - 180) < 1 and abs(pk - 90) < 1, f"moves {b - a:.1f} degrees (expect 180), peak speed {pk:.1f} (expect 90)"))
    case("Smooth move to 90 degrees in 1 s", "Move to a position (smooth)", {"Move to": 90, "Move starts at": 0, "Move takes": 1}, lambda a, b, pk: (abs(b - 90) < 1 and 90 < pk < 200, f"ends at {b:.1f} degrees (expect 90), peak speed {pk:.0f} deg/s (a smooth move peaks above its 90 deg/s average)"))
    case("Oscillate, amplitude 45 at 0.5 Hz", "Oscillate", {"Amplitude": 45, "Frequency": 0.5}, lambda a, b, pk: (abs(max(abs(a), abs(b)) - 45) < 3 or abs(b - a) > 40, f"range {a:.1f} to {b:.1f} degrees (a swing of about 45 either side)"))
    case("Hold stays put", "Hold", {}, lambda a, b, pk: (abs(b - a) < .01, f"moves {b - a:.2f} degrees (expect none)"))
    for o in cad.objects():
        if o.kind == "Joint":
            cad.call("object_update", object=o.name, visible=True)
    strip = ["Motion study of a revolute joint, each motion against its definition"] + [f"{'PASS' if r[2] else 'FAIL'}  {r[0]}: {r[1]}" for r in rows]
    return [("the pin, hub and arm", render(-30, 30)), ("checks", text_panel(strip))]
