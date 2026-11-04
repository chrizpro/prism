"""An editable product study built with cadkit, without importing a prebuilt mesh.

Run in Prism's Script tab, or with cadkit on PYTHONPATH while Prism is open.
Opens its own document. Set PRISM_STUDY_OUTPUT to save the result and screenshot.
"""
import base64
import math
import os
from pathlib import Path

import cadkit as cad


def finish(obj, name, color, metal=0, gloss=0.35):
    obj.rename(name)
    cad.call("material_apply", object=obj.name, color=color, metal=metal, gloss=gloss)
    return obj


def hull(name, height, base, color, sole=False):
    obj = cad.form_box(width=240, depth=110, height=height, faces_x=8, faces_y=4, faces_z=3)
    cage = cad.form_info(obj)
    positions = {}
    for i, (x, y, z) in enumerate(cage["vertices"]):
        t = (x + 120) / 240
        v = z / height
        width = 55 * (1 - 0.92 * t ** 2.4)
        if sole:
            zz = [0, 1, 5, 6][round(v * 3)]
            positions[str(i)] = [x, y / 55 * width, zz]
        else:
            positions[str(i)] = [
                x - 35 * t * v ** 1.1,
                y / 55 * width * (1 - 0.17 * v),
                base + v * (52 - 12 * t + 9 * math.sin(math.pi * t)),
            ]
    cad.form_op(obj, "set_vertices", positions=positions)
    return finish(obj, name, color, metal=0.8 if sole else 0.05, gloss=0.45)


def build():
    cad.call("document_new")
    with cad.history("Freeform steam iron product study"):
        hull("03 Glide - soleplate", 6, 0, "#414A58", sole=True)
        hull("03 Glide - water housing", 70, 7, "#80C4D0")
        points = [[-88, 0, 51], [-92, 0, 83], [-66, 0, 111], [-16, 0, 119], [31, 0, 105], [46, 0, 58]]
        path = cad.run("curve_through", points=points)[0]
        path.rename("03 Glide - handle path")
        handle = cad.form_pipe(path, radius_at_the_start=11, radius_at_the_end=10,
                               sides=8, steps_along_the_path=18, close_the_ends=True)
        finish(handle, "03 Glide - soft grip", "#283748", gloss=0.22)
        cad.call("object_update", object=path.name, visible=False)
        dial = cad.form_cylinder(radius=19, height=8, sides=16, rings=3, position_x=-12, position_z=62)
        finish(dial, "03 Glide - temperature dial", "#E8E0CF", gloss=0.3)
        inlet = cad.form_cylinder(radius=9, height=6, sides=12, rings=3, position_x=68, position_z=52)
        finish(inlet, "03 Glide - water inlet", "#E8E0CF", gloss=0.3)
    cad.select()
    cad.call("view", yaw=-32, pitch=22, fit=True, grid=False, display="shaded")
    cad.call("wait_idle")
    for obj in cad.objects():
        if obj.kind != "Form":
            continue
        report = cad.form_info(obj, summary=True)
        topology = report["topology"]
        assert report["closed"], (obj.name, "open cage")
        assert not topology["nonManifoldEdges"] and not topology["windingConflicts"], (obj.name, topology)
        print(obj.name, report["vertexCount"], "vertices", topology)
    output = os.environ.get("PRISM_STUDY_OUTPUT")
    if output:
        folder = Path(output).resolve()
        folder.mkdir(parents=True, exist_ok=True)
        cad.save(str(folder / "03-glide-steam-iron.prism"))
        picture = cad.screenshot()["_image"]["data"]
        (folder / "03-glide-steam-iron.png").write_bytes(base64.b64decode(picture))


if __name__ == "__main__":
    build()
