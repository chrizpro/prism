"""Render the validated STEP round-trip exports from RecognitionDemoStudy."""
import argparse
import time
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import build_examples as study

cad = study.cad


def render_model(path, output, *, camera=None, expected_objects=9, samples=128):
    cad.call("file", action="open", path=str(path))
    objects = cad.objects()
    assert len(objects) == expected_objects
    for obj in objects:
        assert cad.call("object_info", object=obj.info["id"])["valid"], obj.name
    cad.call("run_command", name="render", commit=False)
    view = camera or {"yaw": -22, "pitch": 58, "target": [75, 45, 7], "distance": 253}
    cad.call("view", **view, orthographic=False, grid=False)
    try:
        cad.call("command_set", params={
            "Picture size": "HD  1280 \u00d7 720", "Quality (samples per pixel)": samples,
            "Lighting": "Studio (soft white boxes)", "Background": "Studio gradient",
            "Ground plane with shadows": True, "Denoise final image": True, "Start render": True,
        })
        deadline = time.monotonic() + 600
        while time.monotonic() < deadline:
            state = cad.call("render_control", action="status")
            if state.get("error"):
                raise RuntimeError(state["error"])
            if state.get("finished"):
                cad.call("render_control", action="save", path=str(output))
                return
            time.sleep(2)
        raise TimeoutError(path.name)
    finally:
        cad.call("render_control", action="close")
        cad.call("command_cancel")


def comparison(before, after, destination):
    width, height = 640, 360
    sheet = Image.new("RGB", (2 * width + 2, height + 112), "#222426")
    draw = ImageDraw.Draw(sheet)
    fonts = Path("C:/Windows/Fonts")
    header = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 26)
    detail = ImageFont.truetype(str(fonts / "segoeui.ttf"), 18)
    for x, path, label, note, finish in (
        (0, before, "BEFORE  /  STEP import", "140 mm wide   /   M6   /   staggered slots", "Engraved text   /   4 mm rib   /   chamfer"),
        (width + 2, after, "AFTER  /  Direct Modeling", "150 mm wide   /   M8   /   aligned slots", "Embossed text   /   6 mm rib   /   1 mm round"),
    ):
        with Image.open(path) as image:
            sheet.paste(image.convert("RGB").resize((width, height), Image.Resampling.LANCZOS), (x, 0))
        draw.text((x + 26, height + 12), label, font=header, fill="#F3F3F3")
        draw.text((x + 26, height + 49), note, font=detail, fill="#CDD0D2")
        draw.text((x + 26, height + 75), finish, font=detail, fill="#CDD0D2")
    sheet.save(destination, optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--models", required=True, type=Path)
    parser.add_argument("--preview", required=True, type=Path)
    args = parser.parse_args()
    models = args.models.resolve()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        before, after = models / "before.png", models / "after.png"
        render_model(models / "mounting-plate-imported.prism", before)
        render_model(models / "mounting-plate-revised.prism", after)
        comparison(before, after, args.preview)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
