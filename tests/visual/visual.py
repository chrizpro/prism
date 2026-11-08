"""Visual review scripts: build things in Prism through its API, take pictures and save them for a person to look at.

Run later without spending any AI tokens:

    python tests/visual/visual.py --discovery <discovery.json> [--only NAME ...] [--list] [--out DIR] [--fast]

Every scenario makes one *contact sheet* named for what it tests (for example ``sheetmetal__lotus-dish-folded-and-flat.png``): several labeled views in one
small picture, so a review is one look per feature. Sheets go to ``artifacts/visual/`` (ignored by git). When you approve a sheet, copy it to
``tests/visual/approved/``; a later test can compare new sheets with those. ``--compare`` reports, for every sheet that has an approved copy, how much the
pictures differ (mean absolute pixel difference, 0-255). A path-traced render is noisy, so only a big difference is worth a look.

Prism's renderer does not draw toolpaths, reference pictures or analysis colours, so CAM scenarios plot the toolpath from the posted G-code and CAE
scenarios print their numbers into the sheet.
"""
import argparse
import json
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "gallery"))
import build_examples as study  # noqa: E402  (connects to Prism's MCP bridge and exposes the cadkit API)
from PIL import Image, ImageDraw  # noqa: E402

cad = study.cad
SCENARIOS = {}
OUT = ROOT / "artifacts/visual"
CELL = (480, 270)
FAST = False


COLUMNS = {}


def scenario(name, columns=2):
    """Register ``fn() -> [(label, PIL.Image)]`` under a file-friendly name that says what is tested. ``columns=1`` gives wide pictures (a row of variants)."""
    def wrap(fn):
        SCENARIOS[name] = fn
        COLUMNS[name] = columns
        return fn
    return wrap


# ---------------------------------------------------------------- pictures
def render(yaw=-35, pitch=28, distance=None, samples=None, background="White", ground=True):
    """A path-traced render of what is on screen. Returns a PIL image."""
    import io
    import tempfile
    cad.call("view", yaw=yaw, pitch=pitch, fit=True, grid=False, display="shaded")
    if distance:
        cad.call("view", distance=distance)
    cad.call("wait_idle")
    cad.call("run_command", name="render", commit=False)
    cad.call("command_set", params={"Picture size": "Draft  960 × 540", "Quality (samples per pixel)": samples or (8 if FAST else 16), "Light bounces": 4,
                                    "Background": background, "Denoise final image": True, "Ground plane with shadows": ground, "Start render": True})
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        status = cad.call("render_control", action="status")
        if status.get("error"):
            raise RuntimeError(status)
        if status.get("finished"):
            break
        time.sleep(.5)
    path = Path(tempfile.gettempdir()) / f"prism-visual-{time.time_ns()}.png"
    cad.call("render_control", action="save", path=str(path))
    cad.call("render_control", action="close")
    cad.call("command_cancel")
    image = Image.open(path).convert("RGB")
    path.unlink(missing_ok=True)
    return image


def text_panel(lines, size=CELL):
    """A picture of text (numbers from an analysis, a check list) so it can be reviewed with the images."""
    image = Image.new("RGB", (2 * CELL[0], 12 + 14 * len(lines)), "white")
    image.strip = True            # laid out as a full-width strip under the pictures
    draw = ImageDraw.Draw(image)
    y = 6
    for line in lines:
        draw.text((10, y), str(line).replace("³", "3").replace("°", " deg").replace("·", "-")[:150], fill="black")
        y += 14
    return image


def toolpath_plot(gcode, size=CELL, title=""):
    """Top-down plot of a G-code program: cutting moves coloured by depth (deep = dark red), rapids in light grey."""
    x = y = z = 0.0
    segs = []
    rapid = False
    for raw in gcode.splitlines():
        line = raw.split("(")[0].strip().upper()
        if not line:
            continue
        words = {w[0]: w[1:] for w in line.split() if len(w) > 1 and w[0] in "GXYZ"}
        if "G" in words and words["G"] in ("0", "00", "1", "01"):
            rapid = words["G"] in ("0", "00")
        elif "G" in words:
            continue
        nx, ny, nz = (float(words[a]) if a in words else v for a, v in (("X", x), ("Y", y), ("Z", z)))
        if (nx, ny, nz) != (x, y, z) and ("X" in words or "Y" in words or "Z" in words):
            segs.append((x, y, z, nx, ny, nz, rapid))
        x, y, z = nx, ny, nz
    image = Image.new("RGB", size, "white")
    if not segs:
        return image
    xs = [v for s in segs for v in (s[0], s[3])]
    ys = [v for s in segs for v in (s[1], s[4])]
    zs = [min(s[2], s[5]) for s in segs if not s[6]] or [0]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    scale = min((size[0] - 20) / max(x1 - x0, 1e-6), (size[1] - 34) / max(y1 - y0, 1e-6))
    cut_depths = [min(s[2], s[5]) for s in segs if not s[6]]
    zlo, zhi = (min(cut_depths), max(cut_depths)) if cut_depths else (0, 0)
    draw = ImageDraw.Draw(image)
    for sx, sy, sz, ex, ey, ez, is_rapid in segs:
        a = (10 + (sx - x0) * scale, size[1] - 22 - (sy - y0) * scale)
        b = (10 + (ex - x0) * scale, size[1] - 22 - (ey - y0) * scale)
        if is_rapid:
            draw.line([a, b], fill=(215, 215, 215))
        else:
            t = 0 if zhi == zlo else min(1, max(0, (zhi - min(sz, ez)) / (zhi - zlo)))   # 0 = shallowest cut, 1 = deepest
            draw.line([a, b], fill=(int(60 + 195 * t), int(110 * (1 - t)), int(160 * (1 - t))))
    draw.text((10, size[1] - 14), f"{title}  X {x0:.0f}..{x1:.0f}  Y {y0:.0f}..{y1:.0f}  Z {zlo:.1f}..{zhi:.1f}  {len(segs)} moves", fill="black")
    return image


def sheet(cells, columns=2):
    """Lay out labeled pictures in a grid and shrink the colours so the file stays small."""
    CELL = (960, 300) if columns == 1 else (480, 270)
    cells = [(label, im) for label, im in cells if im is not None]
    strips = [im for label, im in cells if getattr(im, "strip", False)]
    cells = [(label, im) for label, im in cells if not getattr(im, "strip", False)]
    rows = (len(cells) + columns - 1) // columns
    out = Image.new("RGB", (columns * CELL[0], rows * (CELL[1] + 16) + sum(im.height for im in strips)), "white")
    draw = ImageDraw.Draw(out)
    for i, (label, im) in enumerate(cells):
        x, y = (i % columns) * CELL[0], (i // columns) * (CELL[1] + 16)
        out.paste(im.resize(CELL, Image.LANCZOS), (x, y + 16))
        draw.rectangle([x, y, x + CELL[0], y + 15], fill=(236, 240, 244))
        draw.text((x + 4, y + 2), label, fill="black")
    y = rows * (CELL[1] + 16)
    for im in strips:
        out.paste(im.resize((columns * CELL[0], im.height)) if im.width != columns * CELL[0] else im, (0, y))
        y += im.height
    return out


def save(name, image):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"{name}.png"
    image.quantize(colors=192, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(path, optimize=True)
    return path


def open_model(name):
    cad.call("file", action="open", path=str(ROOT / "gallery/models" / f"{name}.prism"))
    cad.call("wait_idle")


def difference(a, b):
    """Mean absolute pixel difference between two sheets (0 = identical, 255 = opposite)."""
    from PIL import ImageChops, ImageStat
    a, b = a.convert("RGB"), b.convert("RGB").resize(a.size)
    return sum(ImageStat.Stat(ImageChops.difference(a, b)).mean) / 3


def main():
    global OUT, FAST
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--discovery")
    parser.add_argument("--only", nargs="*", help="scenario names (substrings are fine)")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--out")
    parser.add_argument("--fast", action="store_true", help="fewer samples per render: quicker and noisier")
    parser.add_argument("--compare", action="store_true", help="compare each new sheet with tests/visual/approved/")
    args = parser.parse_args()
    if args.out:
        OUT = Path(args.out)
    FAST = args.fast
    names = [n for n in SCENARIOS if not args.only or any(o in n for o in args.only)]
    if args.list:
        print("\n".join(SCENARIOS))
        return 0
    if not args.discovery:
        parser.error("--discovery is required")
    OUT.mkdir(parents=True, exist_ok=True)
    lock = OUT / ".running"
    if lock.exists():
        try:
            import os
            os.kill(int(lock.read_text()), 0)
            parser.error(f"another visual run (pid {lock.read_text()}) is using Prism: two runs on one app mix up their pictures. Wait for it, or delete {lock}.")
        except (ProcessLookupError, ValueError):
            pass      # a stale lock from a run that was stopped
    import os
    lock.write_text(str(os.getpid()))
    bridge, log = study.connect_mcp(args.discovery)
    report = {}
    try:
        for name in names:
            t = time.monotonic()
            try:
                path = save(name, sheet(SCENARIOS[name](), COLUMNS[name]))
                report[name] = {"file": path.name, "kb": round(path.stat().st_size / 1024), "seconds": round(time.monotonic() - t)}
                if args.compare and (HERE / "approved" / path.name).exists():
                    report[name]["difference"] = round(difference(Image.open(path), Image.open(HERE / "approved" / path.name)), 2)
                print(name, report[name], flush=True)
            except Exception as error:  # a broken scenario is a finding, not a reason to stop the run
                report[name] = {"error": f"{type(error).__name__}: {error}"[:300]}
                print(name, "FAILED", report[name]["error"], flush=True)
    finally:
        lock.unlink(missing_ok=True)
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "report.json").write_text(json.dumps(report, indent=2))
    return 1 if any("error" in r for r in report.values()) else 0


if __name__ == "__main__":
    import visual  # run the importable copy, so the scenarios register on the module they import from
    import scenarios  # noqa: F401
    sys.exit(visual.main())
