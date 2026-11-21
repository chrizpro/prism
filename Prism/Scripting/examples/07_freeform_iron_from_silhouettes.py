"""A steam iron built the way an agent should build any Freeform: describe each part by outlines and sections, build, then measure.

No vertex is placed by hand. The soleplate and the shell are skinned along the iron's long axis from a top outline and a side outline
(cad.form_from_silhouettes), the handle is a pipe along a curve, the controls are small primitives. Every part is measured with cad.profile
and checked against the numbers below. See .claude/skills/prism-freeform/SKILL.md.

Run in Prism's Script tab, or with cadkit on PYTHONPATH while Prism is open. Opens its own document.
Set PRISM_STUDY_OUTPUT to a folder to save the .prism file and a four-view picture there.
"""
import base64
import os
from pathlib import Path

import cadkit as cad

LENGTH, X0 = 280.0, -140.0        # the heel is at -x, the tip points to +x
SOLE_HALF_WIDTH, SHELL_INSET = 56.0, 1.5


def top_outline(heel_half_width, tip_half_width, inset=0.0, n=48):
    """Plan view as (y, x) points: a broad rounded heel and a long taper to a rounded tip. Closed."""
    right, left = [], []
    for i in range(n + 1):
        t = i / n
        w = max(tip_half_width + (heel_half_width - tip_half_width) * (1 - t ** 2.2) ** 0.9 - inset, 0.5)
        right.append((w, X0 + LENGTH * t))
        left.append((-w, X0 + LENGTH * t))
    return right + left[::-1]


def side_outline(z_bottom, z_top, n=48):
    """Side view as (z, x) points from two functions of t (0 at the heel, 1 at the tip). Closed."""
    top = [(z_top(i / n), X0 + LENGTH * i / n) for i in range(n + 1)]
    bottom = [(z_bottom(i / n), X0 + LENGTH * i / n) for i in range(n + 1)]
    return top + bottom[::-1]


def shell_top(t):
    """Tall and rounded at the heel, sloping down to a nose that tapers to the soleplate instead of ending in a cliff."""
    heel = min(1.0, (t / 0.06) ** 0.5)
    nose = min(1.0, ((1 - t) / 0.12) ** 0.6)
    return 8 + (26 + 46 * (1 - t) ** 1.3) * heel * nose


def paint(obj, color, metal=0.0, gloss=0.4):
    cad.call("material_apply", object=obj.name, color=color, metal=metal, gloss=gloss)
    return obj


def build():
    cad.call("document_new")
    with cad.history("Freeform steam iron from silhouettes"):
        sole = cad.form_from_silhouettes(top_outline(SOLE_HALF_WIDTH, 3), side_outline(lambda t: 3 * t ** 6, lambda t: 8 + 3 * t ** 6),
                                         axis="X", count=14, exponent=5, name="Iron soleplate")
        shell = cad.form_from_silhouettes(top_outline(SOLE_HALF_WIDTH - 3, 2.5, inset=SHELL_INSET), side_outline(lambda t: 6.0, shell_top),
                                          axis="X", count=18, exponent=2.8, name="Iron shell")

        # handle: a round pipe along an arch whose ends sink into the shell, then flattened to an oval grip
        arch = cad.run("curve_through", points=[(-108, 0, 62), (-112, 0, 92), (-88, 0, 114), (-36, 0, 120), (2, 0, 100), (14, 0, 56)])[0]
        handle = cad.form_pipe(arch, radius_at_the_start=12, radius_at_the_end=11, sides=12, steps_along_the_path=22, close_the_ends=True)
        handle.rename("Iron handle")
        cad.form_op(handle, "scale", all=True, scale=[1.0, 1.45, 1.0])
        cad.call("object_update", object=arch.name, visible=False)

        dial = cad.form_cylinder(radius=17, height=9, sides=24, rings=3, position_x=30, position_z=41)
        dial.rename("Iron temperature dial")
        knob = cad.form_cylinder(radius=9, height=6, sides=20, rings=2, position_x=30, position_z=48)
        knob.rename("Iron dial knob")
        spray = cad.form_cylinder(radius=7, height=9, sides=16, rings=2, position_x=100, position_z=36)
        spray.rename("Iron spray nozzle")
        button = cad.form_cylinder(radius=9, height=6, sides=16, rings=2, position_x=66, position_z=38)
        button.rename("Iron spray button")
        steam = cad.form_cylinder(radius=8, height=6, sides=16, rings=2, position_x=-20, position_z=56)
        steam.rename("Iron steam button")

        paint(sole, "#B8BEC6", metal=0.9, gloss=0.55)
        paint(shell, "#EEF3F7")
        paint(handle, "#2B4C7E", gloss=0.3)
        for part in (dial,):
            paint(part, "#D9E2EC")
        for part in (knob, spray, button, steam):
            paint(part, "#2B4C7E")
    cad.select()

    # measure: the finished parts must match the brief (length, width, height) to a few millimetres
    sole_p = cad.profile(sole, axis="x", count=6, views=(), quiet=True)
    shell_p = cad.profile(shell, axis="x", count=6, views=(), quiet=True)
    handle_p = cad.profile(handle, axis="z", count=4, views=(), quiet=True)
    assert abs(sole_p["size"][0] - LENGTH) < 8, sole_p["size"]
    assert abs(sole_p["size"][1] - 2 * SOLE_HALF_WIDTH) < 4, sole_p["size"]
    assert shell_p["size"][2] > 60, shell_p["size"]
    assert any(c.get("outlines") == 2 for c in handle_p["sections"]["cuts"]), "the handle should be a loop (two legs through the same cut)"
    print("soleplate", [round(v, 1) for v in sole_p["size"]], "shell", [round(v, 1) for v in shell_p["size"]], "handle top", round(handle_p["max"][2], 1))

    output = os.environ.get("PRISM_STUDY_OUTPUT")
    if output:
        folder = Path(output).resolve()
        folder.mkdir(parents=True, exist_ok=True)
        cad.save(str(folder / "07-iron-from-silhouettes.prism"))
        sheet = cad.call("screenshot", views=["front", "top", "right", "iso"], cell_width=520)["_image"]["data"]
        (folder / "07-iron-from-silhouettes.png").write_bytes(base64.b64decode(sheet))


if __name__ == "__main__":
    build()
