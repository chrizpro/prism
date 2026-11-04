"""Ask the user for the sizes first: a dialog appears, then the plate is built from the answers."""
import cadkit as cad

answers = cad.ask("Mounting plate",
                  width=80, depth=50, thickness=6,
                  hole_diameter=8.5, holes=True, material=["Steel", "Aluminum", "Brass"])
if answers is None:
    raise SystemExit("Cancelled")                 # the user pressed Cancel

with cad.history("Mounting plate"):
    plate = cad.box(width=answers["width"], depth=answers["depth"], height=answers["thickness"])
    if answers["holes"]:
        r = answers["hole_diameter"] / 2
        for sx in (-1, 1):
            for sy in (-1, 1):
                hole = cad.cylinder(radius=r, height=answers["thickness"] * 3,
                                    position_x=sx * (answers["width"] / 2 - 10), position_y=sy * (answers["depth"] / 2 - 10))
                cad.subtract(plate, hole)

print("Made a", answers["material"], "plate:", plate.name)
cad.view("iso", fit=True)
