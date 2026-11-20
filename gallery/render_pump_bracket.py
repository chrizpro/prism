"""Render the pump-bracket STEP import and its Direct Modeling revision with the same camera, side by side."""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import build_examples as study
from render_direct_mounting_plate import render_model

CAMERA = {"yaw": -38, "pitch": 24, "target": [0, -5, 80], "distance": 560}


def comparison(before, after, destination):
    width, height = 640, 360
    sheet = Image.new("RGB", (2 * width + 2, height + 112), "#222426")
    draw = ImageDraw.Draw(sheet)
    fonts = Path("C:/Windows/Fonts")
    header = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 26)
    detail = ImageFont.truetype(str(fonts / "segoeui.ttf"), 17)
    for x, path, label, note, finish in (
        (0, before, "BEFORE  /  STEP import", "220 x 130 x 150   /   6 x M6 on PCD 78   /   bore 60",
         "square tower   /   10 mm gussets   /   chamfered rim"),
        (width + 2, after, "AFTER  /  Direct Modeling", "280 x 130 x 190   /   8 x M8 on PCD 106   /   bore 80",
         "tapered, rounded tower   /   16 mm gussets   /   tapped, engraved"),
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
    parser.add_argument("--imported", required=True, type=Path)
    parser.add_argument("--revised", required=True, type=Path)
    parser.add_argument("--preview", required=True, type=Path)
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        folder = args.preview.resolve().parent
        before, after = folder / "pump-bracket-before.png", folder / "pump-bracket-after.png"
        render_model(args.imported.resolve(), before, camera=CAMERA, expected_objects=1, samples=96)
        render_model(args.revised.resolve(), after, camera=CAMERA, expected_objects=1, samples=96)
        comparison(before, after, args.preview)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
