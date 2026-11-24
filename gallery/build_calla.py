"""Build and preview an editable calla lily gramophone horn with Prism MCP."""
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs/calla-horn"
MODEL = OUT / "Calla.prism"
BRASS = dict(material="brass", color="#CB984B", metal=.82, roughness=.26)


def make_horn():
    # These are control rings rather than interpolated sections. Each ring
    # steers the nearby surface; no prescribed section has to be hit exactly.
    # x, y, z, tilt, width, height, petal growth
    rings = [
        (-85, 51, 52, 0, 10.2, 10.2, 0),
        (-85, 51, 77, 0, 10.6, 10.6, 0),
        (-98, 55, 123, 8, 14, 13, 0),
        (-98, 54, 164, 22, 22, 19, 0),
        (-79, 44, 198, 39, 38, 30, .035),
        (-47, 23, 223, 54, 64, 46, .15),
        (-12, -4, 245, 63, 96, 64, .37),
        (17, -35, 266, 68, 128, 83, .67),
        (34, -59, 281, 70, 153, 96, 1),
    ]
    count = 24
    poles = []
    for x, y, z, tilt, width, height, growth in rings:
        angle = math.radians(tilt)
        ring = []
        for i in range(count):
            t = 2 * math.pi * i / count
            lobe = ((1 + math.cos(t - .94)) / 2) ** 7
            rise = height * math.sin(t)
            ring.append((x + width * math.cos(t) + 42 * growth * lobe,
                         y + rise * math.cos(angle) + 44 * growth**2 * lobe,
                         z + rise * math.sin(angle) + 128 * growth * lobe))
        # Clamp a uniform periodic cubic ring at the seam. The two ends
        # have identical position, first derivative and second derivative.
        last, first, second = ring[-1], ring[0], ring[1]
        seam = tuple((a + 4*b + c)/6 for a,b,c in zip(last, first, second))
        after = tuple((2*b + c)/3 for b,c in zip(first, second))
        before = tuple((a + 2*b)/3 for a,b in zip(last, first))
        poles.extend([seam, after, *ring[1:], before, seam])

    horn, = cad.run("nurbs_patch", points_up=len(rings), points_across=count+3,
                    degree=3, initial_relief=0)
    cad.call("set_control_points", object=horn.name, points=poles)
    study.look(horn, "Calla bell", **BRASS)


def build():
    cad.call("document_new")
    study.rounded_box("Walnut cabinet", (252, 186, 36), (0, 0, 0), 5,
                      material="oak", color="#654029", roughness=.34, pattern="Wood", patternScale=75)
    study.rounded_box("Cabinet top", (258, 192, 7), (0, 0, 36), 2,
                      material="oak", color="#855538", roughness=.32, pattern="Wood", patternScale=75)
    for name, x, y, z, radius, height, style in [
        ("Horn socket", -85, 51, 43, 16, 10, BRASS),
        ("Platter", 22, -5, 43, 80, 5, BRASS),
        ("Record", 22, -5, 48, 78, 2, dict(material="abs", color="#202322", roughness=.27)),
        ("Record label", 22, -5, 50, 21, .5, dict(material="abs", color="#B6793A", roughness=.55)),
        ("Spindle", 22, -5, 50.5, 2, 4, BRASS),
    ]:
        solid = cad.cylinder(radius=radius, height=height, position_x=x, position_y=y,
                             position_z=z, operation="New body")
        study.look(solid, name, **style)
    make_horn()
    cad.save(str(MODEL))


def preview():
    cad.call("file", action="open", path=str(MODEL))
    info = cad.call("object_info", object="Calla bell")
    assert info["kind"] == "Surface" and info["valid"], info
    print("Reopened bell:", info["faces"], "face(s), dimensions", info["size"], flush=True)
    render()


def render():
    cad.call("view", yaw=-18, pitch=12, target=[18, 0, 235], distance=870,
             grid=False, orthographic=True, display="shaded")
    cad.call("run_command", name="render", commit=False)
    try:
        cad.call("command_set", params={"Picture size":"HD  1280 × 720",
                 "Quality (samples per pixel)":24, "Light bounces":4,
                 "Background":"Studio gradient", "Denoise final image":True,
                 "Ground plane with shadows":True, "Start render":True})
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            status = cad.call("render_control", action="status")
            if status.get("error"):
                raise RuntimeError(status)
            if status.get("finished"):
                cad.call("render_control", action="save", path=str(OUT / "Calla.png"))
                return
            time.sleep(2)
        raise TimeoutError("Horn preview")
    finally:
        cad.call("render_control", action="close")
        cad.call("command_cancel")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    bridge, log = study.connect_mcp(study.ROOT / "artifacts/gallery/calla-automation.json")
    try:
        build()
        preview()
    finally:
        bridge.stdin.close()
        bridge.wait()
        log.close()
