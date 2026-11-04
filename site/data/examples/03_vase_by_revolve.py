"""A vase: draw half of the outline, spin it round the Z axis, then hollow it out."""
import cadkit as cad

with cad.history("Vase"):
    with cad.sketch("XZ") as s:
        s.polyline([(0, 0), (18, 0), (26, 25), (16, 60), (10, 80), (14, 95), (0, 95)], closed=True)
    vase = cad.revolve(s.obj, angle=360, axis="World Z")
print("Made", vase.name, "- size", vase.size)
cad.view("front", fit=True)
