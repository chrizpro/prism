"""Procedural planet maps for the Meridian orrery (Pillow + numpy, deterministic).

Each map is a cylindrical wrap: u runs once around the planet's Z axis and v runs
from the bottom to the top of the sphere, matching Prism's Cylindrical texture projection.
"""
import base64
import io
import math

import numpy as np
from PIL import Image, ImageFilter

W, H = 512, 256


def _noise(seed, octaves=5, base=4):
    """Tileable-in-u value noise built from smoothly upsampled random grids."""
    rng = np.random.default_rng(seed)
    out = np.zeros((H, W))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        gx, gy = base * 2**o, max(2, base * 2**o // 2)
        grid = rng.random((gy + 1, gx))
        grid = np.concatenate([grid, grid[:, :1]], axis=1)  # wrap around u
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)
        out += amp * np.asarray(img, float) / 255
        total += amp
        amp *= .5
    return out / total


def _lat():
    return np.linspace(1, -1, H)[:, None] * np.ones((1, W))  # +1 top row (north)


def _img(rgb):
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")


def _mix(a, b, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.array(a, float) * (1 - t) + np.array(b, float) * t


def earth():
    n = _noise(11, 6, 4)
    lat = _lat()
    land = n > .5
    ocean = _mix((14, 54, 112), (36, 104, 168), (n - .25) * 2.2)
    green = _mix((58, 104, 48), (150, 130, 78), (n - .53) * 4 + np.abs(lat) * .3)
    rgb = np.where(land[..., None], green, ocean)
    ice = np.abs(lat) > .82 + (n - .5) * .15
    rgb = np.where(ice[..., None], _mix((228, 236, 242), (255, 255, 255), n), rgb)
    clouds = np.clip((_noise(21, 5, 6) - .58) * 3.2, 0, .8)
    return _img(rgb * (1 - clouds[..., None]) + 250 * clouds[..., None])


def jupiter():
    lat = _lat()
    n = _noise(31, 5, 8)
    warp = lat + (n - .5) * .09
    bands = .5 + .5 * np.sin(warp * 23 + np.sin(warp * 7) * 1.6)
    rgb = _mix((232, 212, 178), (176, 118, 76), bands ** 1.4)
    rgb = _mix(rgb, (120, 82, 60), np.clip((np.abs(lat) - .78) * 4, 0, 1))
    yy, xx = np.mgrid[0:H, 0:W]
    spot = ((xx - W * .32) / 34) ** 2 + ((yy - H * .64) / 15) ** 2
    rgb = _mix(rgb, (196, 92, 58), np.clip(1.4 - spot, 0, 1))
    return _img(rgb)


def saturn():
    lat = _lat()
    n = _noise(41, 4, 8)
    w = lat + (n - .5) * .04
    bands = .5 + .5 * np.sin(w * 19)
    rgb = _mix((240, 222, 176), (200, 164, 108), bands ** 1.8)
    return _img(_mix(rgb, (176, 150, 112), np.clip((np.abs(lat) - .8) * 4, 0, 1)))


def mars():
    n = _noise(51, 6, 4)
    lat = _lat()
    rgb = _mix((168, 74, 38), (214, 128, 72), n * 1.4 - .2)
    rgb = _mix(rgb, (92, 44, 30), np.clip((.42 - n) * 5, 0, 1))
    return _img(_mix(rgb, (242, 236, 228), np.clip((np.abs(lat) - .86) * 9, 0, 1)))


def venus():
    lat = _lat()
    n = _noise(61, 5, 5)
    swirl = .5 + .5 * np.sin(lat * 9 + n * 6)
    return _img(_mix((236, 206, 148), (204, 160, 96), swirl * .8))


def mercury():
    n = _noise(71, 6, 6)
    rgb = _mix((92, 88, 84), (178, 172, 164), n)
    rng = np.random.default_rng(72)
    yy, xx = np.mgrid[0:H, 0:W]
    for _ in range(70):
        cx, cy, r = rng.random() * W, rng.random() * H, 2 + rng.random() ** 3 * 16
        d = np.sqrt(np.minimum((xx - cx) ** 2, (W - np.abs(xx - cx)) ** 2) + (yy - cy) ** 2) / r
        rgb = _mix(rgb, (70, 66, 62), np.clip(1 - d, 0, 1) * .6)
        rgb = _mix(rgb, (205, 200, 192), np.clip(1 - np.abs(d - 1) * 4, 0, 1) * .4)
    return _img(rgb)


def moon():
    n = _noise(81, 6, 5)
    rgb = _mix((120, 120, 118), (210, 208, 202), n)
    return _img(_mix(rgb, (88, 88, 90), np.clip((.38 - _noise(82, 3, 3)) * 5, 0, 1)))


def saturn_ring():
    """Planar map: concentric ring bands, centred, with Cassini division."""
    size = 256
    yy, xx = np.mgrid[0:size, 0:size]
    r = np.hypot(xx - size / 2 + .5, yy - size / 2 + .5) / (size / 2)
    t = (r - .58) / .42
    bands = .55 + .3 * np.sin(t * 60) + .15 * np.sin(t * 157)
    rgb = _mix((150, 128, 96), (236, 220, 184), bands)
    rgb = _mix(rgb, (60, 52, 44), np.clip(1 - np.abs(t - .64) * 30, 0, 1))
    return _img(rgb)


def enamel():
    """Deep celestial-blue enamel with faint stars for the zodiac band (planar, tiled)."""
    size = 512
    rng = np.random.default_rng(91)
    base = np.zeros((size, size, 3)) + np.array((16, 34, 86), float)
    n = np.asarray(Image.fromarray((rng.random((9, 9)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), float) / 255
    base = _mix(base, (30, 64, 140), n * .8)
    img = _img(base)
    px = img.load()
    for _ in range(220):
        x, y = int(rng.random() * size), int(rng.random() * size)
        b = int(160 + rng.random() * 95)
        px[x, y] = (b, b, int(b * .92))
    return img.filter(ImageFilter.GaussianBlur(.6))


MAPS = dict(earth=earth, jupiter=jupiter, saturn=saturn, mars=mars, venus=venus, mercury=mercury, moon=moon,
            ring=saturn_ring, enamel=enamel)


def encoded(name, quality=88):
    buffer = io.BytesIO()
    MAPS[name]().save(buffer, "JPEG", quality=quality, optimize=True)
    return base64.b64encode(buffer.getvalue()).decode("ascii")


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    for key, make in MAPS.items():
        make().save(f"{out}/{key}.png")
    print("wrote", ", ".join(MAPS))
