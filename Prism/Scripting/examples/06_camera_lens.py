"""A 50 mm camera lens built only with Solid tools: revolve, fillet, chamfer, patterns and booleans.

Each part is its own body so it can have its own material: anodised barrel, rubber focus ring,
polished steel mount, red accent ring, coated glass and printed distance marks.
"""
import cadkit as cad

cad.call("file", action="new")


def turned(name, profile):
    """Revolve a closed (radius, height) profile drawn on XZ about the Z axis."""
    with cad.sketch("XZ") as s:
        s.polyline(profile, closed=True)
    body = cad.revolve(s.obj, angle=360, axis="World Z")
    cad.call("object_update", object=s.obj.name, visible=False)
    return body.rename(name)


def circle_edges(body, radius, z):
    """Edges of a turned part found by radius and height, so the script does not depend on edge numbers."""
    hits = cad.call("solid_find", object=body, kind="edges", type="Circle", limit=60)["hits"]
    return [{"object": body, "edge": h["edge"]} for h in hits
            if abs(h["radius"] - radius) < 0.01 and abs(h["center"][2] - z) < 0.01]


def ring_of(tool, count, target=None, total_angle=360, result="Cut from a body"):
    """Repeat a small body around the Z axis, cutting it from (or joining it to) a target."""
    slots = {"Objects to pattern": [tool.name]}
    if target:
        slots["Target body"] = target
    cad.run("pattern_circular", slots=slots, axis="Z axis", count=count,
            angle="Total angle", angle_value=total_angle, result=result)


with cad.history("Camera lens"):
    # Turned parts: (radius, height) profiles in millimetres, front of the lens at the top.
    turned("Barrel", [(22, 6), (31, 6), (31, 9), (33, 9), (33, 22), (32, 22), (32, 48), (34, 48),
                      (34, 62), (32.5, 66), (27.5, 66), (27.5, 61), (22, 61)])
    turned("Focus ring", [(32, 22.6), (35.6, 22.6), (35.6, 47.4), (32, 47.4)])
    turned("Mount", [(20, 0), (29.5, 0), (29.5, 6), (20, 6)])
    turned("Accent ring", [(34, 54.4), (34.45, 54.4), (34.45, 55.4), (34, 55.4)])

    # Soften the outside edges.
    cad.fillet(circle_edges("Barrel", 31, 6) + circle_edges("Barrel", 33, 9) + circle_edges("Barrel", 33, 22)
               + circle_edges("Barrel", 34, 48) + circle_edges("Barrel", 32.5, 66), radius=0.8)
    cad.fillet(circle_edges("Focus ring", 35.6, 22.6) + circle_edges("Focus ring", 35.6, 47.4), radius=1.4)
    cad.chamfer(circle_edges("Mount", 29.5, 0), distance=0.8)
    cad.chamfer(circle_edges("Mount", 20, 0), distance=0.6)

    # Knurled focus grip and finer ribs on the aperture ring.
    ring_of(cad.box(width=1.2, depth=1.5, height=19, position_x=35.5, position_z=25.6), 72, target="Focus ring")
    ring_of(cad.box(width=1.0, depth=0.8, height=8.6, position_x=33.0, position_z=11.2), 96, target="Barrel")

    # Front element: a spherical cap that fills the 27.3 mm opening with a 3.6 mm rise.
    a, rise, top = 27.3, 3.6, 64.6
    radius = (a * a + rise * rise) / (2 * rise)
    disc = cad.cylinder(radius=a, height=5, position_z=61)
    cad.intersect(disc, cad.sphere(radius=radius, position_z=top - radius))
    disc.rename("Front glass")

    # Distance scale: 13 raised marks over 96 degrees, joined into one body and turned to face the front.
    before = {o.name for o in cad.objects()}
    ring_of(cad.box(width=1.0, depth=0.55, height=2.6, position_x=34.1, position_z=49.6), 13,
            total_angle=96, result="New bodies")
    marks = [o.name for o in cad.objects() if o.name not in before]
    cad.union(marks[0], *marks[1:])
    cad.get(marks[0]).rename("Markings")
    cad.run("rotate", slots={"Objects": ["Markings"]}, axis="Z", angle=-75, pivot="World origin")

    # Appearance. Library materials also give the parts real densities for mass properties.
    looks = {
        "Barrel": dict(material="6061", color="#1B1C20", metal=0.55, gloss=0.45),
        "Focus ring": dict(material="silicone", color="#141416", metal=0.0, gloss=0.12),
        "Mount": dict(material="304", color="#D9DCE1", metal=1.0, gloss=0.95),
        "Accent ring": dict(material="6061", color="#C8102E", metal=0.5, gloss=0.6),
        "Front glass": dict(material="glass", color="#20414F", metal=0.1, gloss=1.0),
        "Markings": dict(color="#F2F2F2", metal=0.0, gloss=0.3),
    }
    for part, look in looks.items():
        cad.call("material_apply", object=part, **look)

for part in ("Barrel", "Focus ring", "Mount", "Accent ring", "Front glass", "Markings"):
    print(cad.get(part))
cad.call("view", yaw=35, pitch=25, target=[0, 0, 33], distance=190)
