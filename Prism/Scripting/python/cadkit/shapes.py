"""Describing shapes instead of vertices: SVG outlines to curves, section shapes, station-based Freeform skins, and a text readout of a shape.

Everything here that does not talk to the application (parsing SVG, building section outlines) is plain Python and can be tested without Prism.
"""
import math
import re
import xml.etree.ElementTree as ET

from ._conn import CadError, call


# ============================================================ 2D geometry helpers
def _flatten_cubic(p0, p1, p2, p3, tol, out, depth=0):
    """Append the points of a cubic Bezier after p0 (flat enough that the chord is within tol of the curve)."""
    # distance of the control points from the chord decides whether the curve is flat
    dx, dy = p3[0] - p0[0], p3[1] - p0[1]
    chord = math.hypot(dx, dy)

    def off(p):
        if chord < 1e-12:
            return math.hypot(p[0] - p0[0], p[1] - p0[1])
        return abs((p[0] - p0[0]) * dy - (p[1] - p0[1]) * dx) / chord

    if depth >= 12 or max(off(p1), off(p2)) <= tol:
        out.append(p3)
        return
    m01, m12, m23 = _mid(p0, p1), _mid(p1, p2), _mid(p2, p3)
    m012, m123 = _mid(m01, m12), _mid(m12, m23)
    m = _mid(m012, m123)
    _flatten_cubic(p0, m01, m012, m, tol, out, depth + 1)
    _flatten_cubic(m, m123, m23, p3, tol, out, depth + 1)


def _mid(a, b):
    return ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)


def _arc_to_cubics(p0, rx, ry, phi_deg, large, sweep, p1):
    """SVG elliptical arc (endpoint form) as cubic Beziers, per the SVG implementation notes."""
    if rx == 0 or ry == 0 or p0 == p1:
        return [(p0, p0, p1, p1)]
    rx, ry = abs(rx), abs(ry)
    phi = math.radians(phi_deg)
    c, s = math.cos(phi), math.sin(phi)
    dx, dy = (p0[0] - p1[0]) / 2, (p0[1] - p1[1]) / 2
    x1, y1 = c * dx + s * dy, -s * dx + c * dy
    lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
    if lam > 1:
        k = math.sqrt(lam)
        rx, ry = rx * k, ry * k
    num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    coef = math.sqrt(max(0.0, num / den)) if den else 0.0
    if bool(large) == bool(sweep):
        coef = -coef
    cxp, cyp = coef * rx * y1 / ry, -coef * ry * x1 / rx
    cx, cy = c * cxp - s * cyp + (p0[0] + p1[0]) / 2, s * cxp + c * cyp + (p0[1] + p1[1]) / 2

    def ang(ux, uy, vx, vy):
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a

    th1 = ang(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry)
    dth = ang((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry)
    if not sweep and dth > 0:
        dth -= 2 * math.pi
    elif sweep and dth < 0:
        dth += 2 * math.pi
    n = max(1, int(math.ceil(abs(dth) / (math.pi / 2) - 1e-9)))
    step = dth / n
    t = 4 / 3 * math.tan(step / 4)
    out = []
    a0 = th1

    def pt(a):
        ca, sa = math.cos(a), math.sin(a)
        return (cx + c * rx * ca - s * ry * sa, cy + s * rx * ca + c * ry * sa)

    def der(a):
        ca, sa = math.cos(a), math.sin(a)
        return (-c * rx * sa - s * ry * ca, -s * rx * sa + c * ry * ca)

    for _ in range(n):
        a1 = a0 + step
        q0, q3 = pt(a0), pt(a1)
        d0, d1 = der(a0), der(a1)
        out.append((q0, (q0[0] + t * d0[0], q0[1] + t * d0[1]), (q3[0] - t * d1[0], q3[1] - t * d1[1]), q3))
        a0 = a1
    # land exactly on the requested end point
    last = out[-1]
    out[-1] = (last[0], last[1], last[2], p1)
    return out


_NUM = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
_CMD = re.compile(r"([MmLlHhVvCcSsQqTtAaZz])")


def _tokens(d):
    parts = _CMD.split(d)
    # parts: [prefix, cmd, args, cmd, args ...]
    for i in range(1, len(parts), 2):
        yield parts[i], parts[i + 1]


def _arc_numbers(text):
    """Arc arguments pack their two flags without separators ("a5 5 0 01 10 10"), so read flags as single digits."""
    vals, i, n, k = [], 0, len(text), 0
    while True:
        while i < n and text[i] in " ,\t\r\n":
            i += 1
        if i >= n:
            return vals
        if k % 7 in (3, 4):
            vals.append(float(text[i]))
            i += 1
        else:
            m = _NUM.match(text, i)
            if not m:
                raise CadError("Cannot read the arc numbers in SVG path data near %r" % text[i:i + 12])
            vals.append(float(m.group(0)))
            i = m.end()
        k += 1


def parse_path(d, tolerance=0.05):
    """Parse SVG path data into subpaths: a list of ``{"points": [(x, y)...], "closed": bool, "curved": [bool per segment]}``.

    Supports M L H V C S Q T A Z (absolute and relative). Curves are flattened so the polyline stays within ``tolerance`` (in SVG units).
    ``curved[i]`` says whether the segment from points[i] to points[i+1] came from a curve (True) or a straight command (False)."""
    subpaths, cur = [], None
    x = y = sx = sy = 0.0
    last_c = last_q = None

    def start(px, py):
        nonlocal cur
        cur = {"points": [(px, py)], "closed": False, "curved": []}
        subpaths.append(cur)

    def add(px, py, curved):
        if cur is None:
            start(x, y)
        cur["points"].append((px, py))
        cur["curved"].append(curved)

    for cmd, text in _tokens(d):
        rel = cmd.islower()
        c = cmd.upper()
        nums = _arc_numbers(text) if c == "A" else [float(v) for v in _NUM.findall(text)]
        step = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}[c]
        if c == "Z":
            if cur is not None:
                cur["closed"] = True
                x, y = sx, sy
            last_c = last_q = None
            continue
        if step == 0 or len(nums) % step:
            raise CadError("SVG path command %r needs groups of %d numbers, got %d." % (cmd, step, len(nums)))
        first = True
        for i in range(0, len(nums), step):
            a = nums[i:i + step]
            ox, oy = (x, y) if rel else (0.0, 0.0)
            if c == "M":
                x, y = a[0] + ox, a[1] + oy
                if first:
                    sx, sy = x, y
                    start(x, y)
                else:
                    add(x, y, False)       # extra coordinate pairs after M are implicit L commands
                last_c = last_q = None
            elif c == "L":
                x, y = a[0] + ox, a[1] + oy
                add(x, y, False); last_c = last_q = None
            elif c == "H":
                x = a[0] + (x if rel else 0.0)
                add(x, y, False); last_c = last_q = None
            elif c == "V":
                y = a[0] + (y if rel else 0.0)
                add(x, y, False); last_c = last_q = None
            elif c in "CS":
                if c == "C":
                    p1 = (a[0] + ox, a[1] + oy); p2 = (a[2] + ox, a[3] + oy); p3 = (a[4] + ox, a[5] + oy)
                else:
                    p1 = (2 * x - last_c[0], 2 * y - last_c[1]) if last_c else (x, y)
                    p2 = (a[0] + ox, a[1] + oy); p3 = (a[2] + ox, a[3] + oy)
                out = []
                _flatten_cubic((x, y), p1, p2, p3, tolerance, out)
                for p in out:
                    add(p[0], p[1], True)
                x, y = p3; last_c = p2; last_q = None
            elif c in "QT":
                if c == "Q":
                    q = (a[0] + ox, a[1] + oy); p3 = (a[2] + ox, a[3] + oy)
                else:
                    q = (2 * x - last_q[0], 2 * y - last_q[1]) if last_q else (x, y)
                    p3 = (a[0] + ox, a[1] + oy)
                p0 = (x, y)
                p1 = (p0[0] + 2 / 3 * (q[0] - p0[0]), p0[1] + 2 / 3 * (q[1] - p0[1]))
                p2 = (p3[0] + 2 / 3 * (q[0] - p3[0]), p3[1] + 2 / 3 * (q[1] - p3[1]))
                out = []
                _flatten_cubic(p0, p1, p2, p3, tolerance, out)
                for p in out:
                    add(p[0], p[1], True)
                x, y = p3; last_q = q; last_c = None
            elif c == "A":
                p3 = (a[5] + ox, a[6] + oy)
                for seg in _arc_to_cubics((x, y), a[0], a[1], a[2], a[3], a[4], p3):
                    out = []
                    _flatten_cubic(seg[0], seg[1], seg[2], seg[3], tolerance, out)
                    for p in out:
                        add(p[0], p[1], True)
                x, y = p3; last_c = last_q = None
            first = False
    return subpaths


# ============================================================ SVG documents
_IDENT = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)


def _mul(m, n):
    """Affine matrices as (a, b, c, d, e, f): x' = a x + c y + e, y' = b x + d y + f. Returns m applied after n."""
    return (m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1], m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
            m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5])


def _parse_transform(text):
    m = _IDENT
    for name, args in re.findall(r"(\w+)\s*\(([^)]*)\)", text or ""):
        v = [float(t) for t in _NUM.findall(args)]
        if name == "translate":
            t = (1, 0, 0, 1, v[0], v[1] if len(v) > 1 else 0)
        elif name == "scale":
            t = (v[0], 0, 0, v[1] if len(v) > 1 else v[0], 0, 0)
        elif name == "rotate":
            a = math.radians(v[0]); c, s = math.cos(a), math.sin(a)
            t = (c, s, -s, c, 0, 0)
            if len(v) == 3:
                t = _mul(_mul((1, 0, 0, 1, v[1], v[2]), t), (1, 0, 0, 1, -v[1], -v[2]))
        elif name == "matrix" and len(v) == 6:
            t = tuple(v)
        elif name == "skewX":
            t = (1, 0, math.tan(math.radians(v[0])), 1, 0, 0)
        elif name == "skewY":
            t = (1, math.tan(math.radians(v[0])), 0, 1, 0, 0)
        else:
            raise CadError("Unsupported SVG transform %r." % name)
        m = _mul(m, t)
    return m


def _local(tag):
    return tag.rsplit("}", 1)[-1]


def _shape_to_path(el):
    """Convert a basic SVG shape element into path data, or None for elements that draw no outline."""
    tag = _local(el.tag)
    g = lambda k, d=0.0: float(_NUM.match(el.get(k, str(d)).strip()).group(0))
    if tag == "path":
        return el.get("d")
    if tag == "rect":
        x, y, w, h = g("x"), g("y"), g("width"), g("height")
        rx, ry = g("rx", 0), g("ry", 0)
        if el.get("rx") is None and el.get("ry") is not None:
            rx = ry
        if el.get("ry") is None and el.get("rx") is not None:
            ry = rx
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        if rx <= 0 or ry <= 0:
            return "M%g %gh%gv%gh%gz" % (x, y, w, h, -w)
        return ("M%g %gh%ga%g %g 0 0 1 %g %gv%ga%g %g 0 0 1 %g %gh%ga%g %g 0 0 1 %g %gv%ga%g %g 0 0 1 %g %gz"
                % (x + rx, y, w - 2 * rx, rx, ry, rx, ry, h - 2 * ry, rx, ry, -rx, ry, -(w - 2 * rx), rx, ry, -rx, -ry, -(h - 2 * ry), rx, ry, rx, -ry))
    if tag in ("circle", "ellipse"):
        cx, cy = g("cx"), g("cy")
        rx = g("r") if tag == "circle" else g("rx")
        ry = rx if tag == "circle" else g("ry")
        return "M%g %ga%g %g 0 1 1 %g %ga%g %g 0 1 1 %g %gz" % (cx + rx, cy, rx, ry, -2 * rx, 0, rx, ry, 2 * rx, 0)
    if tag in ("polygon", "polyline"):
        v = [float(t) for t in _NUM.findall(el.get("points", ""))]
        if len(v) < 4:
            return None
        body = "M%g %g" % (v[0], v[1]) + "".join("L%g %g" % (v[i], v[i + 1]) for i in range(2, len(v) - 1, 2))
        return body + ("z" if tag == "polygon" else "")
    if tag == "line":
        return "M%g %gL%g %g" % (g("x1"), g("y1"), g("x2"), g("y2"))
    return None


class Outline:
    """One drawn outline from an SVG, in output units: ``runs`` are chains of points (split at sharp corners) that make up the outline."""

    def __init__(self, runs, closed, curved, name=""):
        self.runs, self.closed, self.curved, self.name = runs, closed, curved, name

    @property
    def points(self):
        pts = []
        for run in self.runs:
            pts.extend(run if not pts else run[1:])
        return pts

    @property
    def bounds(self):
        pts = self.points
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        return (min(xs), min(ys)), (max(xs), max(ys))

    def __repr__(self):
        return "Outline(%s%d runs, %s)" % (self.name + ", " if self.name else "", len(self.runs), "closed" if self.closed else "open")


def _simplify(points, tol):
    """Ramer-Douglas-Peucker on an open chain; keeps both ends."""
    if len(points) < 3:
        return list(points)
    a, b = points[0], points[-1]
    dx, dy = b[0] - a[0], b[1] - a[1]
    chord = math.hypot(dx, dy)
    best, at = -1.0, 0
    for i in range(1, len(points) - 1):
        p = points[i]
        d = math.hypot(p[0] - a[0], p[1] - a[1]) if chord < 1e-12 else abs((p[0] - a[0]) * dy - (p[1] - a[1]) * dx) / chord
        if d > best:
            best, at = d, i
    if best <= tol:
        return [a, b]
    return _simplify(points[:at + 1], tol)[:-1] + _simplify(points[at:], tol)


def _turn(a, b, c):
    """Direction change in degrees at b going a to b to c."""
    u = math.atan2(b[1] - a[1], b[0] - a[0])
    v = math.atan2(c[1] - b[1], c[0] - b[0])
    d = math.degrees(v - u)
    while d > 180:
        d -= 360
    while d < -180:
        d += 360
    return abs(d)


def svg_outlines(svg, scale=1.0, fit_width=None, fit_height=None, origin=(0.0, 0.0), flip_y=True, tolerance=0.05, corner_angle=35.0, center=False):
    """Read an SVG (a file path, or the SVG text) into a list of :class:`Outline`.

    ``scale`` multiplies SVG units into millimetres; ``fit_width`` / ``fit_height`` instead scale the whole drawing to that width or height (mm).
    ``flip_y`` turns SVG's downward y into an upward v (almost always wanted). ``origin`` is where the drawing's lower-left corner lands
    (or its centre when ``center=True``). ``tolerance`` is the flattening accuracy in mm. A direction change above ``corner_angle`` degrees is a sharp
    corner: the outline is cut into runs there so a spline never rounds it off. Handles path, rect, circle, ellipse, line, polygon, polyline and
    nested transforms; ignores everything else (text, images, gradients)."""
    text = svg
    if "<" not in svg:
        with open(svg, "r", encoding="utf-8") as f:
            text = f.read()
    try:
        root = ET.fromstring(text)
    except ET.ParseError as e:
        raise CadError("Cannot parse the SVG: %s" % e)
    raw = []

    def walk(el, m):
        m = _mul(m, _parse_transform(el.get("transform")))
        d = _shape_to_path(el)
        if d:
            raw.append((el.get("id", ""), m, d))
        for child in el:
            if _local(child.tag) not in ("defs", "clipPath", "mask", "symbol"):
                walk(child, m)

    walk(root, _IDENT)
    if not raw:
        raise CadError("The SVG has no path, rect, circle, ellipse, line, polygon or polyline to read.")
    # work in SVG units first, then scale, so the tolerance can be turned into SVG units
    probe = []
    for name, m, d in raw:
        for sp in parse_path(d, tolerance=1e9):
            probe.extend((m[0] * px + m[2] * py + m[4], m[1] * px + m[3] * py + m[5]) for px, py in sp["points"])
    xs, ys = [p[0] for p in probe], [p[1] for p in probe]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    k = scale
    if fit_width:
        k = fit_width / w if w > 1e-12 else scale
    elif fit_height:
        k = fit_height / h if h > 1e-12 else scale
    lx, ly = min(xs), min(ys)
    ox, oy = origin
    if center:
        ox -= w * k / 2
        oy -= h * k / 2

    def to_out(px, py):
        u = (px - lx) * k + ox
        v = ((max(ys) - py) if flip_y else (py - ly)) * k + oy
        return (u, v)

    outlines = []
    for name, m, d in raw:
        for sp in parse_path(d, tolerance=tolerance / max(k, 1e-12)):
            pts = [to_out(m[0] * px + m[2] * py + m[4], m[1] * px + m[3] * py + m[5]) for px, py in sp["points"]]
            curved = list(sp["curved"])
            # drop repeated points (a path that returns to its start, zero-length segments)
            clean, cflags = [pts[0]], []
            for i in range(1, len(pts)):
                if math.hypot(pts[i][0] - clean[-1][0], pts[i][1] - clean[-1][1]) > 1e-9:
                    clean.append(pts[i]); cflags.append(curved[i - 1])
            pts, curved = clean, cflags
            closed = sp["closed"]
            if closed and len(pts) > 1 and math.hypot(pts[0][0] - pts[-1][0], pts[0][1] - pts[-1][1]) < 1e-9:
                pts.pop()
                if curved:
                    curved.pop()
            if len(pts) < 2:
                continue
            # corners: indices where the path turns sharply
            n = len(pts)
            corners = []
            rng = range(n) if closed else range(1, n - 1)
            for i in rng:
                if _turn(pts[i - 1], pts[i], pts[(i + 1) % n]) > corner_angle:
                    corners.append(i)
            runs = []
            if not corners:
                runs.append(pts + ([pts[0]] if closed else []))
            else:
                cuts = corners if closed else [0] + corners + [n - 1]
                if closed:
                    # rotate so the first run starts at a corner
                    seq = pts[corners[0]:] + pts[:corners[0]] + [pts[corners[0]]]
                    cs = sorted((c - corners[0]) % n for c in corners) + [n]
                    for a, b in zip(cs, cs[1:]):
                        runs.append(seq[a:b + 1])
                else:
                    for a, b in zip(cuts, cuts[1:]):
                        if b > a:
                            runs.append(pts[a:b + 1])
            outlines.append(Outline(runs, closed, any(curved), name))
    return outlines


def svg_to_sketch(svg, plane="XY", offset=0, max_points=24, **options):
    """Draw an SVG's outlines as a sketch (curves ready to extrude, loft or sweep). Options are those of :func:`svg_outlines`.

    Straight runs become polylines; curved runs become fit-point splines with at most ``max_points`` points, so the sketch stays light.
    A closed outline with no corners is one closed spline. Returns the Sketch Obj, and prints nothing."""
    from .api import sketch
    tol = options.get("tolerance", 0.05)
    outlines = svg_outlines(svg, **options)
    with sketch(plane, offset) as s:
        for o in outlines:
            if len(o.runs) == 1 and o.closed and o.curved:
                pts = _decimate(o.runs[0][:-1], tol, max_points)
                if len(pts) >= 3:
                    s.spline(pts, closed=True)
                    continue
            for run in o.runs:
                simple = _simplify(run, 1e-6)
                if len(simple) <= 2 or not o.curved:
                    s.polyline(simple, closed=o.closed and len(o.runs) == 1)
                else:
                    s.spline(_decimate(run, tol, max_points), closed=False)
    return s.obj


def _decimate(points, tol, max_points):
    """Fewest points that keep the chain within a growing tolerance, never above max_points."""
    t = max(tol, 1e-6)
    pts = _simplify(points, t)
    while len(pts) > max_points:
        t *= 1.4
        pts = _simplify(points, t)
    return pts


# ============================================================ section shapes
def superellipse(width, depth, exponent=2.0, points=24, center=(0.0, 0.0), rotate=0.0):
    """Closed outline |x/a|^n + |y/b|^n = 1 as ``points`` points starting on the +x axis, counter-clockwise.

    exponent 2 is an ellipse; 3 to 4 is a soft rounded rectangle (bottles, handles); above 6 is nearly square. ``width`` (x) and ``depth`` (y) are full sizes in mm.
    Use the same point count on every section of a loft so the seams line up."""
    a, b, n = width / 2.0, depth / 2.0, exponent
    pts = []
    for i in range(points):
        t = 2 * math.pi * i / points
        c, s = math.cos(t), math.sin(t)
        x = a * math.copysign(abs(c) ** (2.0 / n), c)
        y = b * math.copysign(abs(s) ** (2.0 / n), s)
        if rotate:
            r = math.radians(rotate)
            x, y = x * math.cos(r) - y * math.sin(r), x * math.sin(r) + y * math.cos(r)
        pts.append((x + center[0], y + center[1]))
    return pts


def rounded_rect(width, depth, radius, points=24, center=(0.0, 0.0)):
    """Closed rounded-rectangle outline sampled at ``points`` points (a multiple of 4 keeps it symmetric), starting on the +x side."""
    a, b = width / 2.0, depth / 2.0
    r = min(radius, a, b)
    # walk the perimeter by arc length starting at the middle of the +x side
    corner = math.pi * r / 2
    sx, sy = 2 * (b - r), 2 * (a - r)
    total = 2 * sx + 2 * sy + 4 * corner
    pts = []
    for i in range(points):
        d = (i / points) * total + sx / 2
        segs = [("e", sx), ("c", corner), ("n", sy), ("c", corner), ("w", sx), ("c", corner), ("s", sy), ("c", corner)]
        k = 0
        quarter = 0
        for kind, length in segs:
            if d <= length + 1e-12:
                break
            d -= length
            if kind == "c":
                quarter += 1
        if kind == "e":
            x, y = a, -sx / 2 + d
        elif kind == "n":
            x, y = (a - r) - d, b
        elif kind == "w":
            x, y = -a, (sx / 2) - d
        elif kind == "s":
            x, y = -(a - r) + d, -b
        else:
            ang = [0, math.pi / 2, math.pi, 3 * math.pi / 2][quarter % 4] + (d / corner) * math.pi / 2
            ccx = [(a - r), -(a - r), -(a - r), (a - r)][quarter % 4]
            ccy = [(b - r), (b - r), -(b - r), -(b - r)][quarter % 4]
            x, y = ccx + r * math.cos(ang), ccy + r * math.sin(ang)
        pts.append((x + center[0], y + center[1]))
    return pts


# ============================================================ station-based Freeform skin
_PLANE = {"X": "YZ", "Y": "XZ", "Z": "XY"}


def form_skin(stations, axis="Z", points=24, symmetry=None, name=None, close_ends=True, smoothness=3, keep_sections=False):
    """Make an editable Freeform by describing proportions: a list of stations along an axis, each one a cross-section.

    A station is a dict with ``at`` (position along the axis, mm) and either ``width`` and ``depth`` (full sizes; ``exponent`` shapes the corners,
    2 = ellipse, 3-4 = soft rounded box; ``rotate`` degrees) or ``outline`` (a closed list of (u, v) points). ``dx`` and ``dy`` shift the section's centre
    in the section plane (u, v are x,y for axis Z; y,z for axis X; x,z for axis Y). Give each station a distinct ``at``; a station may also say
    ``points``. ``points`` is the default count per section: keep it the same on all stations (a multiple of 4) so the loft does not twist.
    ``symmetry`` ("x", "y", "z") sets persistent mirror symmetry on the result; ``name`` renames it; ``keep_sections=True`` keeps the section sketches. Returns the Freeform Obj.

        cad.form_skin([{"at": 0, "width": 40, "depth": 28, "exponent": 3},
                       {"at": 60, "width": 46, "depth": 30, "exponent": 3.5},
                       {"at": 120, "width": 18, "depth": 14, "exponent": 2.2}], axis="Z", symmetry="x", name="Bottle")
    """
    from .api import sketch, form_loft, delete, call as _call, Obj
    axis = axis.upper()
    if axis not in _PLANE:
        raise CadError("axis must be X, Y or Z.")
    if len(stations) < 2:
        raise CadError("form_skin needs at least two stations.")
    ordered = sorted(stations, key=lambda s: s["at"])
    for a, b in zip(ordered, ordered[1:]):
        if abs(a["at"] - b["at"]) < 1e-6:
            raise CadError("Two stations share the position %g; give each a distinct 'at'." % a["at"])
    sections = []
    for st in ordered:
        n = int(st.get("points", points))
        if "outline" in st:
            pts = [tuple(p) for p in st["outline"]]
            pts = [(p[0] + st.get("dx", 0.0), p[1] + st.get("dy", 0.0)) for p in pts]
        else:
            if "width" not in st or "depth" not in st:
                raise CadError("Station at %g needs width and depth, or an outline." % st["at"])
            pts = superellipse(st["width"], st["depth"], st.get("exponent", 2.0), n, (st.get("dx", 0.0), st.get("dy", 0.0)), st.get("rotate", 0.0))
        off = -st["at"] if axis == "Y" else st["at"]      # the XZ sketch plane looks along -Y
        with sketch(_PLANE[axis], off) as s:
            s.spline(pts, closed=True)
        sections.append(s.obj)
    obj = form_loft(sections, points_per_section=points, close_the_ends=close_ends, smoothness=smoothness)
    if not keep_sections:
        # the Freeform no longer depends on them; leaving 20 sketches per part would bury the model tree
        _call("delete_objects", objects=[s.name for s in sections])
    if name:
        obj.rename(name)
    if symmetry:
        _call("form_op", object=obj.name, op="set_symmetry", symmetry=symmetry)
    return obj


# ============================================================ reading a shape back as text
def profile(obj, axis="z", count=8, views=("front", "right", "top"), width=48, shade=False, at=None, quiet=False, rows=26):
    """Describe a shape in text: a table of cross-sections along ``axis`` and ASCII silhouettes. Prints it (unless ``quiet``) and returns the raw dict.

    Read the table as "at <offset>: x from a to b, z from c to d, area, outlines". An empty row means the shape does not reach that offset;
    ``outlines`` above 1 means the cut splits into separate pieces (a handle loop, a gap)."""
    if isinstance(obj, (list, tuple)):
        target = [getattr(o, "name", str(o)) for o in obj]
    else:
        target = getattr(obj, "name", str(obj))
    args = {"object": target, "axis": axis, "count": count, "views": list(views), "width": width, "rows": rows, "shade": shade}
    if at is not None:
        args["at"] = list(at)
    r = call("solid_profile", **args)
    if not quiet:
        print(format_profile(r))
    return r


def format_profile(r):
    """The text form of a solid_profile reply."""
    out = ["%s  size %s mm   min %s  max %s" % (", ".join(r["objects"]), " x ".join("%g" % v for v in r["size"]), r["min"], r["max"])]
    sec = r.get("sections")
    if sec:
        out.append("sections along %s: %s" % (sec["axis"], sec["order"]))
        for c in sec["cuts"]:
            if c.get("empty"):
                out.append("  at %-9g empty" % c["at"])
                continue
            keys = [k for k in c if k not in ("at", "area", "centroid", "outlines")]
            ext = "  ".join("%s %g..%g" % (k, c[k][0], c[k][1]) for k in keys)
            out.append("  at %-9g %s  area %g  centroid %s  outlines %d" % (c["at"], ext, c["area"], c["centroid"], c["outlines"]))
    for pic in r.get("silhouettes", []):
        out.append("%s view (%s): %.3g mm per column, %.3g mm per row; left edge %g, top edge %g" % (pic["view"], pic["axes"], pic["mmPerColumn"], pic["mmPerRow"], pic["leftEdge"], pic["topEdge"]))
        out.extend("|" + row for row in pic["rows"])
    return "\n".join(out)


# ============================================================ silhouettes to a Freeform
def slice_extents(points, v):
    """Where a closed outline (list of (u, v) points) is crossed by the horizontal line at ``v``: returns (min_u, max_u), or None when it is not crossed.
    The extremes of every crossing are used, so a notch or hole inside the outline does not narrow the result."""
    hits = []
    n = len(points)
    for i in range(n):
        a, b = points[i], points[(i + 1) % n]
        if (a[1] - v) * (b[1] - v) > 0 or a[1] == b[1]:
            continue
        t = (v - a[1]) / (b[1] - a[1])
        if 0.0 <= t <= 1.0:
            hits.append(a[0] + (b[0] - a[0]) * t)
    return (min(hits), max(hits)) if hits else None


def _as_points(shape):
    """An outline given as an Outline, a list of (u, v) points, or SVG text / a file path (the first outline is used)."""
    if isinstance(shape, Outline):
        return shape.points
    if isinstance(shape, str):
        return svg_outlines(shape)[0].points
    return [tuple(p) for p in shape]


def swap_uv(points):
    """Turn (a, b) points into (b, a): use it when an outline is drawn with the long axis first and the stations need it second."""
    return [(p[1], p[0]) for p in _as_points(points)]


_SECTION_KEYS = {"X": ("y", "z"), "Y": ("x", "z"), "Z": ("x", "y")}


def stations_from_silhouettes(first, second, count=12, exponent=3.0, margin=0.01, axis="Z"):
    """Cross-section stations (for :func:`form_skin`) read off two outlines in world mm, each given as points (across, along):
    ``along`` is the coordinate on the skin ``axis`` and ``across`` the coordinate of the section plane that outline measures.

    Axis Z: ``first`` is the front view (x, z) and ``second`` the side view (y, z). Axis X (a long object lying along x): ``first`` is the top view as (y, x)
    and ``second`` the side view as (z, x); use :func:`swap_uv` if you drew them as (x, y) and (x, z). Axis Y: first (x, y), second (z, y).
    At each of ``count`` positions the width and the centre of the section along its first plane axis come from ``first``, the depth and centre along the
    second from ``second``. Stations bunch up near the ends where shapes change fastest. ``exponent`` is the corner roundness of every section (2 ellipse,
    3-4 soft box); it may be a function of the position."""
    fp, sp = _as_points(first), _as_points(second)
    lo = max(min(p[1] for p in fp), min(p[1] for p in sp))
    hi = min(max(p[1] for p in fp), max(p[1] for p in sp))
    if hi - lo < 1e-6:
        raise CadError("The two outlines do not overlap along the %s axis (%g..%g vs %g..%g); give each as (across, along) points in the same units and position."
                       % (axis, min(p[1] for p in fp), max(p[1] for p in fp), min(p[1] for p in sp), max(p[1] for p in sp)))
    out = []
    for i in range(count):
        t = 0.5 - 0.5 * math.cos(math.pi * i / (count - 1))        # cosine spacing
        z = lo + (hi - lo) * (margin + (1 - 2 * margin) * t)
        fx, sy = slice_extents(fp, z), slice_extents(sp, z)
        if fx is None or sy is None:
            continue
        e = exponent(z) if callable(exponent) else exponent
        out.append({"at": z, "width": max(fx[1] - fx[0], 0.2), "depth": max(sy[1] - sy[0], 0.2), "dx": (fx[0] + fx[1]) / 2, "dy": (sy[0] + sy[1]) / 2, "exponent": e})
    if len(out) < 2:
        raise CadError("Fewer than two positions cross both outlines; check that both are closed and in the same units and position.")
    return out


def form_from_silhouettes(first, second, axis="Z", count=12, exponent=3.0, points=24, symmetry=None, name=None, refine=1, tolerance=0.5, margin=0.01):
    """Build an editable Freeform whose two views match two outlines, then correct it against its real surface. See :func:`stations_from_silhouettes`
    for how the outlines are given (axis Z: front (x, z) and side (y, z)).

    The smooth surface of a Freeform pulls in from its cage, so the first build comes out a little small. With ``refine`` (default 1) the result is
    measured at every station and the stations are rescaled by target / actual, then rebuilt; the worst remaining error in mm is printed when it is above
    ``tolerance``. ``margin`` is the fraction of the length the end stations sit inside the outlines (default 1%). Returns the Freeform Obj."""
    from .api import call as _call
    axis = axis.upper()
    ku, kv = _SECTION_KEYS[axis]
    stations = stations_from_silhouettes(first, second, count, exponent, margin=margin, axis=axis)
    target = [dict(s) for s in stations]
    obj = None
    for rnd in range(max(refine, 0) + 1):
        if obj is not None:
            _call("delete_objects", objects=[obj.name])
        obj = form_skin(stations, axis, points, name=name)
        err, scaled = 0.0, []
        r = _call("solid_profile", object=obj.name, axis=axis.lower(), at=[s["at"] for s in target], views=[])
        for st, tg, cut in zip(stations, target, r["sections"]["cuts"]):
            if cut.get("empty"):
                scaled.append(dict(st)); continue
            w, d = cut[ku][1] - cut[ku][0], cut[kv][1] - cut[kv][0]
            err = max(err, abs(w - tg["width"]), abs(d - tg["depth"]))
            ns = dict(st)
            ns["width"] = st["width"] * tg["width"] / max(w, 1e-6)
            ns["depth"] = st["depth"] * tg["depth"] / max(d, 1e-6)
            scaled.append(ns)
        if err <= tolerance or rnd == max(refine, 0):
            if err > tolerance:
                print("form_from_silhouettes: worst section error %.2f mm after %d refinement(s); add stations or raise refine." % (err, rnd))
            break
        stations = scaled
    if symmetry:
        _call("form_op", object=obj.name, op="set_symmetry", symmetry=symmetry)
    return obj
