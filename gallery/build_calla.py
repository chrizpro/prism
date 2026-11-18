"""Build and preview an editable calla lily gramophone horn with Prism MCP."""
import math
import time

import build_examples as study

cad = study.cad
OUT = study.ROOT / "docs/calla-horn"
MODEL = OUT / "Calla.prism"
BRASS = dict(material="brass", color="#CB984B", metal=.82, roughness=.26)


def make_horn():
    # Section centers, tilt away from vertical, transverse radii and petal growth.
    stations = [
        (-85, 51, 52, 0, 10, 10, 0),
        (-90, 55, 78, 0, 11, 11, 0),
        (-100, 60, 116, 7, 13, 13, 0),
        (-102, 56, 152, 22, 18, 17, 0),
        (-84, 44, 184, 42, 30, 25, 0),
        (-56, 27, 206, 58, 49, 37, .04),
        (-26, 6, 225, 65, 75, 53, .16),
        (0, -15, 244, 69, 105, 72, .42),
        (18, -35, 264, 70, 133, 87, .73),
        (31, -54, 279, 69, 150, 94, 1),
    ]
    profiles = []
    for x, y, z, tilt, width, height, growth in stations:
        angle = math.radians(tilt)
        points = []
        for i in range(32):
            t = 2 * math.pi * i / 32
            # Narrow the peak progressively. Its very tip rolls back from the mouth.
            lobe = ((1 + math.cos(t - .94)) / 2) ** (5 + 9 * growth)
            rise = height * math.sin(t)
            points.append((x + width * math.cos(t) + 43 * growth * lobe,
                           y + rise * math.cos(angle) + 55 * growth**2 * lobe,
                           z + rise * math.sin(angle) + 120 * growth * lobe))
        profiles.append(cad.run("curve_through", points=points, closed=True)[0])
    horn, = cad.run("loft_surface", slots={"Sections": profiles})
    study.look(horn, "Calla bell", **BRASS)
    cad.delete(*profiles)


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
