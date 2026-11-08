"""Open-end wrench: a steel hand tool modeled in Solid and checked with Prism's static structural analysis (CAE). Purely illustrative.

A 6 mm thick flat steel wrench for 13 mm hex nuts: a 36 mm round head with a 13 mm jaw slot, joined to a 190 mm x 18 mm handle. The study fixes the two
jaw flats, pushes the handle end sideways with 200 N and solves with 10-node tetrahedra. The script compares the result with beam theory (a cantilever of
the handle: tip deflection F L^3 / 3 E I and root stress M c / I), repeats the solve on a coarser mesh to show convergence, and requires a safety factor above 1.

python gallery/build_wrench.py --discovery <discovery.json> [--render-only] [--no-render]
"""
import argparse
import math

import build_examples as study

cad = study.cad

STEEL = "steel"                       # Mild Steel (A36): E 200 GPa, yield 250 MPa
T, HEAD_R, JAW = 6.0, 18.0, 13.0      # thickness, head radius, jaw opening (nut across flats)
LENGTH, WIDTH = 190.0, 18.0           # handle length from the head centre, handle width
FORCE = 200.0                         # N, sideways at the handle end
MESH = 2.0                            # mm element size


def sketch(entities):
    cad.call("sketch_begin", plane="XY")
    cad.call("sketch_add", entities=entities)
    return cad.get(cad.call("sketch_finish")["sketch"])


def solve(size):
    cad.call("study_new", object="Wrench", element_size=size)
    cad.call("run_command", name="sim_fixed", slots={"Faces to support": [{"object": "Wrench", "face": f} for f in jaw_faces]})
    cad.call("run_command", name="sim_force", slots={"Faces to load": [{"object": "Wrench", "face": end_face}]}, params={"Force": FORCE, "Direction": "+Y"})
    return cad.call("study_run", show="von Mises")["results"]


def build():
    global jaw_faces, end_face
    cad.call("document_new")
    with cad.history("Wrench - Solid and CAE workflow"):
        head = cad.extrude(sketch([{"type": "circle", "center": [0, 0], "radius": HEAD_R}]), distance=T)
        head.rename("Wrench")
        handle = sketch([{"type": "rect", "corner1": [0, -WIDTH / 2], "corner2": [LENGTH, WIDTH / 2]}])
        cad.run("extrude", slots={"Profile": handle.name}, distance=T, operation="Join")
        jaw = sketch([{"type": "rect", "corner1": [-30, -JAW / 2], "corner2": [4, JAW / 2]}])
        cad.run("extrude", slots={"Profile": jaw.name}, distance=T + 2, operation="Cut")
        for o in cad.objects():
            if o.kind == "Sketch":
                cad.call("object_update", object=o.name, visible=False)
        cad.call("material_apply", object="Wrench", material=STEEL, color="#9AA3AD", roughness=.3)
    info = cad.call("object_info", object="Wrench")
    assert info["valid"] and abs(info["size"][0] - (LENGTH + math.sqrt(HEAD_R ** 2 - (JAW / 2) ** 2))) < .01 and abs(info["size"][1] - 2 * HEAD_R) < .01 and abs(info["size"][2] - T) < .01, info["size"]
    faces = cad.call("solid_find", object="Wrench", kind="faces", type="Plane", limit=40)["hits"]
    jaw_faces = [f["face"] for f in faces if abs(f["normal"][1]) > .99 and f["center"][0] < 0 and abs(abs(f["center"][1]) - JAW / 2) < .01]
    end_face = next(f["face"] for f in faces if f["normal"][0] > .99 and abs(f["center"][0] - LENGTH) < .01)
    assert len(jaw_faces) == 2, jaw_faces

    # Beam theory for the handle as a cantilever from the jaw: tip deflection F L^3 / (3 E I), root stress F L (w / 2) / I, I = t w^3 / 12
    E, I = 200000.0, T * WIDTH ** 3 / 12
    deflection = FORCE * LENGTH ** 3 / (3 * E * I)
    stress = FORCE * LENGTH * (WIDTH / 2) / I
    coarse = solve(3.0)
    result = solve(MESH)
    assert abs(result["reactionNet_N"] - FORCE) < 1, result
    # the real part is stiffer than the cantilever idealisation (the head carries load too), so deflection is a little lower; peak stress adds the neck and support
    assert .8 * deflection < result["maxDisplacement_mm"] < 1.05 * deflection, (result["maxDisplacement_mm"], deflection)
    assert .9 * stress < result["maxVonMises_MPa"] < 1.8 * stress, (result["maxVonMises_MPa"], stress)
    assert abs(coarse["maxDisplacement_mm"] - result["maxDisplacement_mm"]) < .05 * result["maxDisplacement_mm"], (coarse, result)
    assert result["minSafetyFactor"] > 1, result
    print("beam theory: deflection %.3f mm, root stress %.1f MPa | Prism: %.3f mm, %.1f MPa, safety %.2f" %
          (deflection, stress, result["maxDisplacement_mm"], result["maxVonMises_MPa"], result["minSafetyFactor"]))
    return "wrench", -35, 40


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        if args.render_only:
            cad.call("file", action="open", path=str(study.ROOT / "gallery/models/wrench.prism"))
            model = ("wrench", -35, 40)
        else:
            model = build()
        study.verify_and_save(*model, render=not args.no_render, distance=420)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
