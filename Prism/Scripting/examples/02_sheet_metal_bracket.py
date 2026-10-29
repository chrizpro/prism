"""Sketch a cross-section, make bent sheet metal from it, then unfold it."""
import cadkit as cad

with cad.history("U bracket"):
    with cad.sketch("XZ") as s:                       # straight lines, sharp corners: the bends are added for you
        s.polyline([(0, 25), (0, 0), (50, 0), (50, 25)])
    body = cad.flange(s.obj, thickness=2, bend_radius=3, width=40)
    cad.flat_pattern(body, k_factor=0.44)

print("Made", body.name, "and its flat pattern")
