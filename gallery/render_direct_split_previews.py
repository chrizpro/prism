"""Gallery previews for the Direct Modeling before/after studies (models/<id>.prism, an assembly of the STEP import and the revised part,
built by build_direct_before_after.py). The card is split by a diagonal: the import, whole, on the left (top-left triangle) and the revised
part, whole, on the right (bottom-right triangle). Both are rendered on a transparent background with the same camera direction and
distance, then placed at one scale on a neutral grey card, so a part that grew looks bigger.

    python gallery/render_direct_split_previews.py --discovery <automation.json> [--only pump-bracket-revised] [--compose-only]
"""
import argparse
import time
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

import build_examples as study

cad = study.cad
HERE = Path(__file__).resolve().parent
STUDIES = {
    # card id (= models/<id>.prism, an assembly of the two parts): camera direction and distance, the same for both sides
    "mounting-plate-before-after": {"yaw": -22, "pitch": 58, "distance": 253},
    "reduction-gearbox-before-after": {"yaw": 34, "pitch": 34, "distance": 470},
    "spindle-carrier-before-after": {"yaw": 18, "pitch": 18, "distance": 335},
    "pump-bracket-before-after": {"yaw": -38, "pitch": 24, "distance": 430},
    "motor-mount-direct-edit": {"yaw": -30, "pitch": 30, "distance": 230},
}
BEFORE, AFTER = "Before - STEP import", "After - Direct Modeling"
WIDTH, HEIGHT = 810, 504


def render_cutout(path, output, camera, show, samples=96):
    """A path-traced render of one part of the saved assembly (the other hidden) on a transparent background, the camera aimed at its middle."""
    cad.call("file", action="open", path=str(path))
    objects = {o.name: o for o in cad.objects()}
    assert set(objects) == {BEFORE, AFTER}, (path.name, list(objects))
    for name in objects:
        cad.call("object_update", object=name, visible=name == show)
    info = objects[show].info
    camera = dict(camera, target=[(info["min"][i] + info["max"][i]) / 2 for i in range(3)])
    cad.call("run_command", name="render", commit=False)
    cad.call("view", **camera, orthographic=False, grid=False)
    try:
        cad.call("command_set", params={
            "Picture size": "HD  1280 × 720", "Quality (samples per pixel)": samples, "Lighting": "Studio (soft white boxes)",
            "Background": "Transparent", "Ground plane with shadows": False, "Denoise final image": True, "Start render": True})
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


def cutout(path):
    with Image.open(path) as image:
        image = image.convert("RGBA")
        return image.crop(image.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())


def fit_scale(part, box):
    x0, y0, x1, y1 = box
    return min((x1 - x0) / part.width, (y1 - y0) / part.height)


def place(card, part, box, scale):
    """Scale the part and centre it in the box; a soft contact shadow sits under it."""
    x0, y0, x1, y1 = box
    part = part.resize((max(1, round(part.width * scale)), max(1, round(part.height * scale))), Image.Resampling.LANCZOS)
    x, y = round((x0 + x1 - part.width) / 2), round((y0 + y1 - part.height) / 2)
    shadow = Image.new("RGBA", card.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse((x + part.width * 0.08, y + part.height * 0.86, x + part.width * 0.92, y + part.height * 1.04), fill=(0, 0, 0, 70))
    card.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(12)))
    card.alpha_composite(part, (x, y))


def studio(width, height):
    """A light neutral grey card, brighter in the middle, like the studio renders of the other gallery cards."""
    card = Image.new("RGBA", (width, height))
    px = card.load()
    for j in range(height):
        for i in range(width):
            d = ((i - width / 2) ** 2 / (width * 0.75) ** 2 + (j - height / 2) ** 2 / (height * 0.75) ** 2) ** 0.5
            v = round(226 - 70 * min(d, 1.2))
            px[i, j] = (v, v + 1, v + 4, 255)
    return card


def compose(before, after, destination):
    card = studio(WIDTH, HEIGHT)
    # the split runs from the top-right corner to the bottom-left one; each part fits the largest box inside its own triangle
    # one scale for both (same camera), so a part that grew looks bigger
    first, second = cutout(before), cutout(after)
    top_left, bottom_right = (WIDTH * 0.02, HEIGHT * 0.11, WIDTH * 0.54, HEIGHT * 0.53), (WIDTH * 0.46, HEIGHT * 0.47, WIDTH * 0.98, HEIGHT * 0.88)
    scale = min(fit_scale(first, top_left), fit_scale(second, bottom_right))
    place(card, first, top_left, scale)
    place(card, second, bottom_right, scale)
    draw = ImageDraw.Draw(card)
    draw.line([(WIDTH + 2, -2), (-2, HEIGHT + 2)], fill="#FFFFFF", width=4)
    font = ImageFont.truetype(str(Path("C:/Windows/Fonts/segoeuib.ttf")), 19)
    for text, anchor, xy in (("BEFORE  ·  STEP import", "la", (16, 14)), ("AFTER  ·  Direct Modeling", "rd", (WIDTH - 16, HEIGHT - 14))):
        box = draw.textbbox(xy, text, font=font, anchor=anchor)
        draw.rounded_rectangle((box[0] - 10, box[1] - 7, box[2] + 10, box[3] + 7), radius=8, fill=(26, 28, 32))
        draw.text(xy, text, font=font, anchor=anchor, fill="#F3F3F3")
    card.convert("RGB").save(destination, optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery")
    parser.add_argument("--work", type=Path, default=HERE.parent / "artifacts/gallery/direct-split")
    parser.add_argument("--only", nargs="*")
    parser.add_argument("--compose-only", action="store_true", help="reuse renders already in --work")
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    chosen = {k: v for k, v in STUDIES.items() if not args.only or k in args.only}
    bridge = log = None
    if not args.compose_only:
        bridge, log = study.connect_mcp(args.discovery)
    try:
        for card, camera in chosen.items():
            before, after = args.work / f"{card}-before-cut.png", args.work / f"{card}-after-cut.png"
            if not args.compose_only:
                render_cutout(HERE / "models" / f"{card}.prism", before, camera, BEFORE)
                render_cutout(HERE / "models" / f"{card}.prism", after, camera, AFTER)
            compose(before, after, HERE / "previews" / f"{card}.png")
            print("preview", card, flush=True)
    finally:
        if bridge:
            bridge.terminate()
            bridge.wait(timeout=10)
            log.close()
