"""Friendly wrappers over the application's tools.

Parameters are written in snake_case and matched to the on-screen labels by partial text, so
``cad.box(width=40)`` sets "Width" and ``cad.flange(bend_radius=3)`` sets "Bend radius (inside)".
Objects can be given by name ("Box 1"), or as the Obj that a call returned.
"""
import json
from contextlib import contextmanager

from ._conn import call, CadError


class Obj:
    """An object in the document. Returned by every modeling call; pass it (or its name) to the next one."""

    def __init__(self, info):
        self.info = info
        self.name = info["name"]
        self.kind = info.get("kind")

    def __str__(self):
        """One descriptive line, so print(obj) tells an agent what it made, not only its name."""
        i, parts = self.info, [self.name + " [" + str(self.kind) + "]"]
        if i.get("size"):
            parts.append("size " + " x ".join("%g" % round(v, 3) for v in i["size"]) + " mm")
        if i.get("volume") is not None:
            parts.append("volume %g mm3" % i["volume"])
        if i.get("faces") is not None:
            parts.append("%s faces" % i["faces"] + (", %s edges" % i["edges"] if i.get("edges") is not None else ""))
        if i.get("valid") is False:
            parts.append("INVALID")
        return ", ".join(parts)

    def __repr__(self):
        return "Obj(%r, %s)" % (self.name, self.kind)

    def to_dict(self):
        """Everything the application reported about this object (bounds, size, volume, faces ...)."""
        return self.info

    def json(self):
        return json.dumps(self.info, separators=(",", ":"))

    @property
    def size(self):
        return tuple(self.info.get("size", ()))

    @property
    def bounds(self):
        return self.info.get("min"), self.info.get("max")

    def refresh(self):
        self.info = call("object_info", object=self.name)
        return self

    def rename(self, new_name):
        call("object_update", object=self.name, name=new_name)
        self.name = new_name
        return self

    def delete(self):
        call("delete_objects", objects=[self.name])


def _name(x):
    if isinstance(x, Obj):
        return x.name
    if isinstance(x, dict):
        return x
    return str(x)


def _slot_value(v):
    if isinstance(v, (list, tuple)):
        return [_name(i) for i in v]
    return _name(v)


def _label(key):
    return key.replace("_", " ")


def run(command, slots=None, preview=False, points=None, **params):
    """Run any modeling command: ``run("fillet", slots={"Edges": [...]}, radius=2)``. Returns the new Obj list."""
    args = {"name": command}
    if slots:
        args["slots"] = {k: _slot_value(v) for k, v in slots.items()}
    if params:
        args["params"] = {_label(k): v for k, v in params.items()}
    if points is not None:
        args["points"] = [list(point) for point in points]
    if preview:
        args["commit"] = False
    r = call("run_command", **args)
    return [Obj(o) for o in (r.get("created") or [])]


def _one(objs, what):
    if not objs:
        raise CadError("%s did not make anything." % what)
    return objs[0] if len(objs) == 1 else objs


# ---------------------------------------------------------------- the document
def objects():
    """Every object in the document."""
    return [Obj(o) for o in call("get_state")["objects"]]


def get(name):
    return Obj(call("object_info", object=_name(name)))


def state():
    return call("get_state")


def undo(count=1):
    call("history", action="undo", count=count)


def redo(count=1):
    call("history", action="redo", count=count)


def delete(*objs):
    call("delete_objects", objects=[_name(o) for o in objs])


def select(*objs):
    call("select", items=[_name(o) for o in objs])


def save(path):
    call("file", action="save", path=path)


def view(name=None, fit=False):
    """Set the camera to a standard view ("front", "top", "iso" ...); fit=True zooms to everything."""
    args = {}
    if name:
        args["preset"] = name
    if fit:
        args["fit"] = True
    call("view", **args)


def screenshot(wait_ms=250):
    """Capture the application window (the application answers with the picture data)."""
    return call("screenshot", wait_ms=wait_ms)


@contextmanager
def history(label="Script"):
    """Group everything inside the block into one undo step: ``with cad.history("Make bracket"): ...``"""
    call("history_group", action="begin", label=label)
    try:
        yield
    finally:
        call("history_group", action="end", label=label)


# ---------------------------------------------------------------- dialogs
def dialog(title, fields):
    """Show a dialog and wait for OK. ``fields`` are dicts: {"key", "label", "type": "number|text|check|choice", "value", ...}.
    Returns {key: value} or None if the user cancelled."""
    r = call("script_dialog", title=title, fields=list(fields))
    return r["values"] if r.get("ok") else None


def ask(title="Parameters", **defaults):
    """The quick way to ask for numbers and options. The kind of field follows the default you give:
    ``cad.ask("Plate", width=60, depth=40, name="Plate 1", rounded=True, material=["Steel", "Aluminum"])``.
    Returns a dict of the answers (numbers keep int or float as given), or None if cancelled."""
    fields = []
    for key, default in defaults.items():
        label = key.replace("_", " ").capitalize()
        if isinstance(default, bool):
            fields.append({"key": key, "label": label, "type": "check", "value": default})
        elif isinstance(default, (int, float)):
            fields.append({"key": key, "label": label, "type": "number", "value": float(default)})
        elif isinstance(default, (list, tuple)):
            fields.append({"key": key, "label": label, "type": "choice", "options": [str(o) for o in default], "value": str(default[0])})
        else:
            fields.append({"key": key, "label": label, "type": "text", "value": str(default)})
    values = dialog(title, fields)
    if values is None:
        return None
    for key, default in defaults.items():
        if isinstance(default, int) and not isinstance(default, bool) and isinstance(values.get(key), float) and values[key].is_integer():
            values[key] = int(values[key])
    return values


# ---------------------------------------------------------------- sketches
class Sketch:
    """A sketch being drawn. Use ``with cad.sketch("XY") as s:`` then s.line(...), s.rect(...), s.circle(...)."""

    def __init__(self, plane="XY", offset=0):
        self.plane, self.offset = plane, offset
        self.obj = None

    def __enter__(self):
        args = {"plane": _name(self.plane)}
        if self.offset:
            args["offset"] = self.offset
        call("sketch_begin", **args)
        return self

    def __exit__(self, exc_type, exc, tb):
        if exc_type is not None:
            try:
                call("sketch_finish", keep=False)
            except CadError:
                pass
            return False
        r = call("sketch_finish")
        name = r.get("sketch")
        if name:
            self.obj = get(name)
        return False

    def _add(self, *entities):
        return call("sketch_add", entities=list(entities))

    def line(self, a, b):
        return self._add({"type": "line", "points": [list(a), list(b)]})

    def polyline(self, points, closed=False):
        return self._add({"type": "polyline", "points": [list(p) for p in points], "closed": closed})

    def rect(self, corner1, corner2):
        return self._add({"type": "rect", "corner1": list(corner1), "corner2": list(corner2)})

    def circle(self, center, radius):
        return self._add({"type": "circle", "center": list(center), "radius": radius})

    def arc(self, start, through, end):
        return self._add({"type": "arc", "points": [list(start), list(through), list(end)]})

    def spline(self, points, closed=False):
        return self._add({"type": "spline", "points": [list(p) for p in points], "closed": closed})


def sketch(plane="XY", offset=0):
    """Start a sketch on 'XY', 'XZ', 'YZ', a construction plane's name, or a face ``{"object": "Box 1", "face": 6}``."""
    return Sketch(plane, offset)


# ---------------------------------------------------------------- primitives
def box(width=20, depth=20, height=20, **p):
    return _one(run("box", width=width, depth=depth, height=height, **p), "box")


def cylinder(radius=10, height=20, **p):
    return _one(run("cylinder", radius=radius, height=height, **p), "cylinder")


def sphere(radius=10, **p):
    return _one(run("sphere", radius=radius, **p), "sphere")


def cone(radius=10, height=20, **p):
    return _one(run("cone", **({"radius": radius, "height": height} | p)), "cone")


def torus(major_radius=20, minor_radius=5, **p):
    return _one(run("torus", major_radius=major_radius, minor_radius=minor_radius, **p), "torus")


# ---------------------------------------------------------------- features
def flatten(source, **params):
    """Create a persistent flat layout. Select it later with refold() to return."""
    return _one(run("flatten", slots={"Faces or a surface to flatten": source}, **params), "flatten")


def refold(pattern, geometry=None, target=None, **params):
    """Refold a saved layout; optionally wrap curves, sheets or solids onto it."""
    slots = {"Original flat pattern": pattern}
    if geometry is not None:
        slots["Sketches or curves to refold"] = geometry
    if target is not None:
        slots["Target face (optional)"] = target
    return _one(run("refold", slots=slots, **params), "refold")


def wrap(geometry, reference, target, **params):
    """Map geometry between a planar reference and a surface, or between curves.

    U/V/normal scales and offsets, mirror_u, swap_uv, flip_normal,
    keep_input_geometry and curve_tolerance are accepted keyword arguments.
    """
    params.setdefault("create_a_copy_of_the_curved_face", False)
    return refold(reference, geometry=geometry, target=target, **params)


def extrude(profile, distance=20, extent=None, operation=None, **p):
    """Extrude a sketch or face. ``extent`` can be "Two sides", "Symmetric", "To object", "Through all" ...; ``operation`` "Cut" etc."""
    if extent:
        p["extent"] = extent
    if operation:
        p["operation"] = operation
    return _one(run("extrude", slots={"Profile": profile}, distance=distance, **p), "extrude")


def revolve(profile, angle=360, axis="World Z", **p):
    return _one(run("revolve", slots={"Profile": profile}, angle=angle, axis=axis, **p), "revolve")


def sweep(profile, path, **p):
    return _one(run("sweep", slots={"Profile": profile, "Path": path}, **p), "sweep")


def loft(sections, rails=None, **p):
    slots = {"Sections": list(sections)}
    if rails:
        slots["Guide rails"] = list(rails)
    return _one(run("loft", slots=slots, **p), "loft")


def fillet(edges, radius=2):
    """``edges`` as in ``{"object": "Box 1", "edge": 3}`` or a list of them (edge numbers start at 1)."""
    run("fillet", slots={"Edges": edges}, radius=radius)


def chamfer(edges, distance=1.5):
    run("chamfer", slots={"Edges": edges}, distance=distance)


def shell(faces, thickness=2, **p):
    run("shell", slots={"Faces to remove": faces}, wall_thickness=thickness, **p)


def union(body, *tools):
    run("union", slots={"Body": body, "Tool bodies": list(tools)})


def subtract(body, *tools):
    run("subtract", slots={"Body to cut from": body, "Tool bodies": list(tools)})


def intersect(body, *tools):
    run("intersect", slots={"Body": body, "Tool bodies": list(tools)})


# ---------------------------------------------------------------- transforms
def move(objs, x=0, y=0, z=0):
    run("move", slots={"Objects": objs}, x=x, y=y, z=z)


def rotate(objs, angle, axis="Z"):
    run("rotate", slots={"Objects": objs}, angle=angle, axis=axis)


def scale(objs, factor):
    run("scale", slots={"Objects": objs}, factor=factor)


def mirror(objs, plane="XY"):
    run("mirror", slots={"Objects": objs}, plane=plane)


# ---------------------------------------------------------------- reference pictures
def image(path, on="Front plane (XZ)", width=200, opacity=60, **p):
    """Put a picture on a plane as a reference. ``on`` is "Front plane (XZ)", "Top plane (XY)" or "Right plane (YZ)"."""
    return _one(run("place_image", image_file=path, place_it_on=on, width=width, opacity=opacity, **p), "image")


def edit_image(img, **p):
    """Change a picture: opacity, width, turn, move_right, move_up, mirror_left_to_right ..."""
    run("edit_image", slots={"Picture": img}, **p)

# ---------------------------------------------------------------- sheet metal and drawings
def flange(profile, thickness=2, bend_radius=2, width=30, **p):
    """Bent sheet metal from a sketch of the cross-section (straight lines, sharp corners)."""
    return _one(run("contour_flange", slots={"Profile sketch": profile}, thickness=thickness, bend_radius=bend_radius, width=width, **p), "flange")


def flat_pattern(body, k_factor=0.44):
    return run("flat_pattern", slots={"Sheet metal body": body}, k_factor=k_factor)


def drawing(path, **options):
    """Write an engineering drawing (.svg, .pdf or .dxf); see the drawing_export tool for the options."""
    return call("drawing_export", path=path, **options)


# ---------------------------------------------------------------- editable subdivision forms
def form_box(**params):
    """Create an editable subdivision box; accepts width, depth, height, faces_x/y/z."""
    return _one(run("form_box", **params), "Freeform box")


def form_cylinder(**params):
    """Create an editable cylinder cage; accepts radius, height, sides, rings, caps."""
    return _one(run("form_cylinder", **params), "Freeform cylinder")


def form_loft(sections, **params):
    """Loft an editable cage through ordered curve/sketch sections."""
    return _one(run("form_loft", slots={"Sections": list(sections)}, **params), "Freeform loft")


def form_pipe(path, **params):
    """Build an editable round tube along a curve/edge."""
    return _one(run("form_pipe", slots={"Path": path}, **params), "Freeform pipe")


def form_info(obj, summary=False, only=None):
    """Read a cage and its topology diagnostics; component indices are zero-based."""
    args = {"object": _name(obj), "summary": summary}
    if only is not None:
        args["only"] = list(only)
    return call("form_info", **args)


def form_op(obj, op, **options):
    """Undoable cage edit: e.g. bridge, set_vertices, inset, crease, move or insert_loop."""
    return call("form_op", object=_name(obj), op=op, **options)


def form_to_nurbs(obj):
    """Convert a cage to NURBS in place; save the editable form first if needed."""
    return Obj(call("form_to_nurbs", object=_name(obj)))


# ---------------------------------------------------------------- looking at the model and finding things
def _plain(x):
    if isinstance(x, Obj):
        return x.info
    if isinstance(x, (list, tuple)):
        return [_plain(i) for i in x]
    if isinstance(x, dict):
        return {k: _plain(v) for k, v in x.items()}
    return x


def show(*things):
    """Print objects or results as compact JSON: the most complete thing an agent can read back."""
    for thing in things:
        print(json.dumps(_plain(thing), separators=(",", ":"), default=str))


def faces(obj, **filters):
    """Find faces by geometry instead of index: faces(b, type="Plane", facing=(0, 0, 1)). Returns the tool's JSON
    (indices are 1-based, ready for fillet/chamfer/shell/hole slots). See help("solid_find") for every filter."""
    return call("solid_find", object=_name(obj), kind="faces", **filters)


def edges(obj, **filters):
    """Find edges by geometry: edges(b, direction=(0, 0, 1), min_size=10). Same filters as faces()."""
    return call("solid_find", object=_name(obj), kind="edges", **filters)


def check(obj, other=None, **kw):
    """Verify a body (validity, volume, closed, thin walls ...), or measure between obj and other. Returns JSON."""
    if other is not None:
        kw["a"], kw["b"] = _name(obj), _name(other)
        return call("solid_check", object=_name(obj), **kw)
    return call("solid_check", object=_name(obj), **kw)


# ---------------------------------------------------------------- discovering what exists
def commands(query=""):
    """Names run() accepts, optionally those containing query."""
    return call("list_commands", query=query)


def describe(command):
    """The slots (what to select) and parameters (name, type, default, range, options) of a command, as JSON."""
    return call("describe_command", name=command)


def help(query=""):
    """Search the commands and the cadkit functions for query and print a short answer."""
    q = query.lower()
    names = commands(query)
    print("commands (%d): %s" % (names["count"], ", ".join(names["names"][:40])))
    funcs = [(n, (f.__doc__ or "").strip().splitlines()[0] if (f.__doc__ or "").strip() else "") for n, f in sorted(globals().items())
             if callable(f) and not n.startswith("_") and getattr(f, "__module__", "") == __name__ and (not q or q in n.lower())]
    for n, d in funcs:
        print("cad.%s: %s" % (n, d))
    print("details: cad.describe(<command>) | cad.call(<tool>, ...) | describe_api(names=[...]) for tool schemas")
