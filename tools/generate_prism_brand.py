"""Regenerate Prism's vector logo and platform icons. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Prism" / "Assets"
FACES = [
    ("#7762ED", [(232, 94), (294, 148), (418, 380), (356, 326)]),
    ("#354FA8", [(108, 326), (356, 326), (418, 380), (170, 380)]),
    ("#B5F2FF", [(232, 94), (108, 326), (232, 250)]),
    ("#39C8ED", [(232, 94), (232, 250), (356, 326)]),
    ("#4088ED", [(108, 326), (356, 326), (232, 250)]),
]


def svg(icon=False):
    background = '<rect width="512" height="512" rx="112" fill="#142338"/>' if icon else ''
    faces = ''.join(f'<polygon fill="{color}" points="' + ' '.join(f'{x},{y}' for x, y in points) + '"/>' for color, points in FACES)
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="Prism">{background}{faces}</svg>\n'


def raster(icon=False, size=1024):
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    scale = size / 512
    if icon:
        draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=112 * scale, fill="#142338")
    for color, points in FACES:
        draw.polygon([(x * scale, y * scale) for x, y in points], fill=color)
    return image


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    (ASSETS / "prism-mark.svg").write_text(svg(), encoding="utf-8")
    (ASSETS / "prism-icon.svg").write_text(svg(True), encoding="utf-8")
    mark = raster()
    icon = raster(True)
    mark.save(ASSETS / "prism-mark.png")
    icon.save(ASSETS / "prism-icon.png")
    icon.save(ASSETS / "prism.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    logo = Image.new("RGBA", (1500, 440))
    logo.alpha_composite(mark.resize((440, 440), Image.Resampling.LANCZOS), (0, 0))
    font = ImageFont.truetype("C:/Windows/Fonts/seguisb.ttf", 230)
    ImageDraw.Draw(logo).text((460, 65), "Prism", font=font, fill="#142338")
    logo.save(ASSETS / "prism-logo.png")
    wordmark = svg().split('>', 1)[1].rsplit('</svg', 1)[0]
    (ASSETS / "prism-logo.svg").write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1500 440" role="img" aria-label="Prism">'
        '<g transform="scale(.859375)">' + wordmark + '</g>'
        '<text x="460" y="315" font-family="Segoe UI, sans-serif" font-size="230" font-weight="600" fill="#142338">Prism</text></svg>\n', encoding="utf-8")
    icon.resize((192, 192), Image.Resampling.LANCZOS).save(ROOT / "Prism.Android" / "Icon.png")
    android_paths = ''.join(f'<path android:fillColor="{color}" android:pathData="M' + ' L'.join(f'{x},{y}' for x, y in points) + ' Z"/>' for color, points in FACES)
    (ROOT / "Prism.Android" / "Resources" / "drawable" / "prism_mark.xml").write_text(
        '<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="128dp" android:height="128dp" android:viewportWidth="512" android:viewportHeight="512">' + android_paths + '</vector>\n')
    icon.save(ROOT / "Prism.Browser" / "wwwroot" / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64)])
    (ROOT / "Prism.Browser" / "wwwroot" / "prism-icon.svg").write_text(svg(True), encoding="utf-8")
    catalog = ROOT / "Prism.iOS" / "Assets.xcassets" / "AppIcon.appiconset"
    catalog.mkdir(parents=True, exist_ok=True)
    ios_icon = Image.new("RGBA", icon.size, "#142338")
    ios_icon.alpha_composite(icon)
    ios_icon.convert("RGB").save(catalog / "prism-icon.png")
    (catalog / "Contents.json").write_text('{"images":[{"filename":"prism-icon.png","idiom":"universal","platform":"ios","size":"1024x1024"}],"info":{"author":"Prism","version":1}}\n')
    preview = Image.new("RGB", (1200, 650), "#ECF1F8")
    preview.paste(logo.resize((900, 264), Image.Resampling.LANCZOS), (120, 20), logo.resize((900, 264), Image.Resampling.LANCZOS))
    tile = icon.resize((230, 230), Image.Resampling.LANCZOS)
    preview.paste(tile, (190, 340), tile)
    draw = ImageDraw.Draw(preview)
    draw.rounded_rectangle((540, 320, 1090, 590), radius=24, fill="#142338")
    draw.text((625, 370), "Prism", font=ImageFont.truetype("C:/Windows/Fonts/seguisb.ttf", 76), fill="#ECF1F8")
    for offset, size in enumerate([16, 24, 32, 48]):
        small = icon.resize((size, size), Image.Resampling.LANCZOS)
        preview.paste(small, (625 + offset * 90, 500), small)
    preview.save(ASSETS / "prism-brand-preview.png")


if __name__ == "__main__":
    main()
