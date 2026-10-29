"""A plate with a ring of holes: loops and maths work like in any Python program."""
import math
import cadkit as cad

with cad.history("Plate with holes"):          # everything in this block is ONE undo step
    plate = cad.box(width=80, depth=80, height=6)
    for i in range(8):
        a = math.radians(i * 45)
        hole = cad.cylinder(radius=4, height=20, position_x=28 * math.cos(a), position_y=28 * math.sin(a))
        cad.subtract(plate, hole)
    centre = cad.cylinder(radius=10, height=20)
    cad.subtract(plate, centre)

print("Done:", plate.name)
cad.view("iso", fit=True)
