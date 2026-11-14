"""Generates the Hally app icon, Android adaptive icon layers and splash logos.

Run from mobile/:  python3 tool/generate_icons.py   (needs Pillow)
The mark is a white "H" in Bricolage Grotesque 800 on the brand hero gradient, with an apricot spark.
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / "assets/fonts/BricolageGrotesque-800.ttf"
GRADIENT = [(0x5A, 0x48, 0xC2), (0x7C, 0x6B, 0xD6), (0xB5, 0x7B, 0xD0)]
SPARK = (0xF6, 0xB2, 0x7A)
SS = 4  # supersampling


def gradient(size):
    """Diagonal three-stop gradient, top-left to bottom-right."""
    img = Image.new("RGB", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            if t < 0.5:
                a, b, k = GRADIENT[0], GRADIENT[1], t / 0.5
            else:
                a, b, k = GRADIENT[1], GRADIENT[2], (t - 0.5) / 0.5
            px[x, y] = tuple(round(a[i] + (b[i] - a[i]) * k) for i in range(3))
    return img


def spark(draw, cx, cy, r, fill):
    """Four-pointed spark with concave sides."""
    w = r * 0.28
    draw.polygon([(cx, cy - r), (cx + w, cy - w), (cx + r, cy), (cx + w, cy + w),
                  (cx, cy + r), (cx - w, cy + w), (cx - r, cy), (cx - w, cy - w)], fill=fill)


def mark(size, scale, colour=(255, 255, 255, 255), spark_colour=SPARK + (255,)):
    """Transparent layer with the H and spark; scale is the H height as a fraction of size."""
    big = size * SS
    layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    font = ImageFont.truetype(str(FONT), int(big * scale * 1.38))
    l, t, r, b = d.textbbox((0, 0), "H", font=font)
    w, h = r - l, b - t
    sr = h * 0.2
    x, y = (big - w) / 2 - l - sr * 0.45, (big - h) / 2 - t + big * 0.01  # optically centre H + spark
    d.text((x, y), "H", font=font, fill=colour)
    spark(d, (big + w) / 2 + sr * 0.1, (big - h) / 2 - sr * 0.15, sr, spark_colour)
    return layer.resize((size, size), Image.LANCZOS)


def app_icon(size):
    bg = gradient(size).convert("RGBA")
    bg.alpha_composite(mark(size, 0.46))
    return bg.convert("RGB")  # App Store icons must not have alpha


def rounded(img, radius):
    m = Image.new("L", (img.width * SS, img.height * SS), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, m.width - 1, m.height - 1), radius * SS, fill=255)
    out = img.convert("RGBA")
    out.putalpha(m.resize(img.size, Image.LANCZOS))
    return out


def main():
    master = app_icon(1024)

    # iOS
    ios = ROOT / "ios/Runner/Assets.xcassets/AppIcon.appiconset"
    for f in ios.glob("Icon-App-*.png"):
        pt, scale = f.stem.removeprefix("Icon-App-").split("@")
        px = round(float(pt.split("x")[0]) * int(scale.removesuffix("x")))
        master.resize((px, px), Image.LANCZOS).save(f)

    # Android legacy (rounded square) + adaptive layers (108dp canvas, mark inside the 66dp safe zone)
    res = ROOT / "android/app/src/main/res"
    densities = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}
    for name, k in densities.items():
        d = res / f"mipmap-{name}"
        legacy = round(48 * k)
        rounded(app_icon(legacy * 4).resize((legacy, legacy), Image.LANCZOS), legacy * 0.22).save(d / "ic_launcher.png")
        adaptive = round(108 * k)
        gradient(adaptive).save(d / "ic_launcher_background.png")
        mark(adaptive, 0.32).save(d / "ic_launcher_foreground.png")
        mark(adaptive, 0.32, spark_colour=(255, 255, 255, 255)).save(d / "ic_launcher_monochrome.png")
        # Splash logo: 96dp rounded icon
        logo = round(96 * k)
        rounded(app_icon(logo * 2).resize((logo, logo), Image.LANCZOS), logo * 0.24).save(d / "launch_image.png")

    # iOS launch image: 96pt rounded icon at 1x/2x/3x
    launch = ROOT / "ios/Runner/Assets.xcassets/LaunchImage.imageset"
    for suffix, k in (("", 1), ("@2x", 2), ("@3x", 3)):
        px = 96 * k
        rounded(app_icon(px * 2).resize((px, px), Image.LANCZOS), px * 0.24).save(launch / f"LaunchImage{suffix}.png")


if __name__ == "__main__":
    main()
