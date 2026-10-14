#!/usr/bin/env python3
"""Build Storm weather sheets from selected painted imagegen sources."""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
SCALE = 4
RAMP = np.array([
    (20, 21, 46), (39, 40, 70), (58, 56, 102),
    (88, 86, 132), (122, 120, 184), (183, 186, 214),
], dtype=np.uint8)
WHITE = np.array((244, 246, 255), dtype=np.uint8)
YELLOW = np.array((246, 242, 184), dtype=np.uint8)


def crop_art(path):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)[..., 3]
    ys, xs = np.where(a > 16)
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))


def palette_and_alpha(im, cap=150):
    a = np.asarray(im).copy()
    rgb = a[..., :3].astype(float)
    lum = rgb @ np.array((.2126, .7152, .0722))
    index = np.clip(((lum - 12) / 190 * 6).astype(int), 0, 5)
    a[..., :3] = RAMP[index]
    # Small, rare flecks of storm white. Yellow stays on upper-left lit edges.
    high = (lum > 207) & (a[..., 3] > 85)
    yy, xx = np.indices(lum.shape)
    a[high & (((xx * 17 + yy * 13) % 19) == 0), :3] = WHITE
    a[high & (yy < lum.shape[0] * .47) & (((xx * 23 + yy * 11) % 149) == 0), :3] = YELLOW
    a[..., 3] = np.minimum(cap, (a[..., 3].astype(float) * .70).astype(np.uint8))
    return Image.fromarray(a, "RGBA")


def tornado_base():
    art = crop_art(SRC / "tornado_candidate_2.png")
    # Native pixels are composed first; the final x4 pass preserves hard edges.
    art.thumbnail((112, 112), Image.Resampling.NEAREST)
    native = Image.new("RGBA", (128, 128))
    native.alpha_composite(art, ((128 - art.width) // 2, (128 - art.height) // 2))
    x = np.asarray(palette_and_alpha(native, 170)).astype(float)
    # Fourfold balance allows a quarter-turn in six frames with a clean loop.
    premul = x[..., :3] * x[..., 3:4] / 255
    alpha = x[..., 3]
    ap = np.mean([np.rot90(alpha, k) for k in range(4)], axis=0)
    cp = np.mean([np.rot90(premul, k) for k in range(4)], axis=0)
    out = np.zeros((128, 128, 4), dtype=np.uint8)
    out[..., :3] = np.clip(cp * 255 / np.maximum(ap[..., None], 1), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(ap * 1.16, 0, 170).astype(np.uint8)
    yy, xx = np.indices((128, 128))
    radius = np.hypot(xx - 63.5, yy - 63.5)
    # Keep the eye and inner air clearly see-through.
    out[..., 3] = (out[..., 3] * np.clip((radius - 9) / 27, .16, 1)).astype(np.uint8)
    funnel = Image.fromarray(out, "RGBA")
    detail = Image.new("RGBA", (128, 128))
    d = ImageDraw.Draw(detail)
    for px, py in ((94, 29), (37, 41), (20, 78), (102, 86), (75, 112), (44, 103)):
        d.polygon(((px, py), (px + 3, py - 1), (px + 5, py + 1), (px + 2, py + 3)), fill=(20, 21, 46, 145))
        d.line((px, py, px + 3, py - 1), fill=(122, 120, 184, 115), width=1)
    for x, y in ((27, 55), (86, 42), (94, 98)):
        d.line((x, y, x + 6, y + 2), fill=(58, 56, 102, 130), width=2)
        d.point((x, y), fill=(183, 186, 214, 105))
    for pts in (((37, 73), (39, 83), (44, 91)), ((81, 31), (89, 35), (94, 42)),
                ((92, 82), (85, 91), (77, 95)), ((36, 48), (44, 39), (53, 35))):
        d.line(pts, fill=(183, 186, 214, 80), width=1)
    funnel.alpha_composite(detail)
    x = np.asarray(funnel).copy()
    x[..., 3] = np.minimum(x[..., 3], 170)
    funnel = Image.fromarray(x, "RGBA")
    return funnel


def front_base():
    art = crop_art(SRC / "gust_candidate_2.png")
    # The whole painted front retains its irregular cloud rhythm.
    painted = art.resize((128, 67), Image.Resampling.NEAREST)
    band = Image.new("RGBA", (128, 128))
    band.alpha_composite(painted, (0, 31))
    band = palette_and_alpha(band, 145)
    x = np.asarray(band).copy()
    yy = np.arange(128)[:, None]
    taper = np.minimum(np.clip((yy - 31) / 12, 0, 1), np.clip((101 - yy) / 15, 0, 1))
    x[..., 3] = (x[..., 3] * taper).astype(np.uint8)
    # Periodic 8 px edge transition; exact matching alpha and color at the seam.
    left = x[:, :9].astype(float).copy()
    right = x[:, -9:].astype(float).copy()
    seam = ((left[:, 0] + right[:, -1]) * .5).astype(np.uint8)
    for k in range(9):
        mix = k / 8
        x[:, k] = (seam * (1 - mix) + left[:, k] * mix).astype(np.uint8)
        x[:, -1-k] = (seam * (1 - mix) + right[:, -1-k] * mix).astype(np.uint8)
    band = Image.fromarray(x, "RGBA")
    wind = Image.new("RGBA", (128, 128))
    d = ImageDraw.Draw(wind)
    # Broken lateral filaments and rain make the roll read as moving weather.
    for i in range(21):
        sx = (i * 37 + 11) % 121 + 3
        sy = 53 + (i * 17) % 27
        ln = 4 + (i * 7) % 12
        d.line((sx, sy, min(124, sx + ln), sy - (i % 3 == 0)), fill=(183, 186, 214, 62 + (i % 3) * 15), width=1)
    for i in range(25):
        sx = (i * 29 + 15) % 119 + 4
        sy = 80 + (i * 11) % 16
        d.line((sx, sy, sx + 2, min(102, sy + 5)), fill=(122, 120, 184, 65 + (i % 3) * 12), width=1)
    for sx, sy in ((19, 58), (51, 54), (83, 60), (108, 56)):
        d.line((sx, sy, sx + 2, sy - 1, sx + 3, sy + 1, sx + 5, sy), fill=(122, 120, 184, 108), width=1)
    band.alpha_composite(wind)
    x = np.asarray(band).copy()
    x[..., 3] = np.minimum(x[..., 3], 170)
    x[:, -1] = x[:, 0]
    return Image.fromarray(x, "RGBA")


def rolled_front(front, shift):
    x = np.roll(np.asarray(front), shift, axis=1).copy()
    x[:, -1] = x[:, 0]
    return Image.fromarray(x, "RGBA")


def expand(native):
    return native.resize((native.width * SCALE, native.height * SCALE), Image.Resampling.NEAREST)


def warning(native, amount=.36, front=False):
    a = np.asarray(native).copy()
    yy, xx = np.indices(a.shape[:2])
    # The pale painted contours make a thin recognizable weather preview.
    lum = a[..., :3].astype(float) @ np.array((.2126, .7152, .0722))
    keep = (lum > 105) | ((lum > 76) & (((xx * 7 + yy * 23) % 13) < 4))
    specks = ((xx * 17 + yy * 23 + xx * yy * 7 + (xx // 5) * 19 + (yy // 7) * 11) % 29) < 8
    weight = np.where(keep, amount, np.where(specks, amount * .55, 0))
    a[..., 3] = (a[..., 3] * weight).astype(np.uint8)
    if front:
        a[:, :3, 3] = 0
        a[:, -3:, 3] = 0
    return Image.fromarray(a, "RGBA")


def ambient_particle(kind, variant):
    im = Image.new("RGBA", (64, 64))
    d = ImageDraw.Draw(im)
    off = (-3, -1, 2, 4)[variant]
    pale = (183, 186, 214, 155 + variant * 9)
    lit = (244, 246, 255, 162 + variant * 8)
    dim = (122, 120, 184, 110 + variant * 12)
    if kind == 0:  # long, slender slanted rain
        length = 18 + variant * 2
        d.line((32 + off - 5, 32 - length // 2, 32 + off + 4, 32 + length // 2), fill=dim, width=2)
        d.line((32 + off - 5, 32 - length // 2, 32 + off + 3, 31 + length // 2), fill=pale, width=1)
        d.point((27 + off, 32 - length // 2), fill=lit)
    elif kind == 1:  # heavier short streak
        length = 11 + variant
        d.line((30 + off, 32 - length // 2, 34 + off, 32 + length // 2), fill=dim, width=3)
        d.line((29 + off, 32 - length // 2, 33 + off, 31 + length // 2), fill=pale, width=2)
        d.point((29 + off, 32 - length // 2), fill=lit)
    elif kind == 2:  # small curled wind wisp
        pts = [(20 + off, 34), (26 + off, 31), (34 + off, 30 + variant % 2),
               (42 + off, 31), (45 + off, 28), (42 + off, 25)]
        d.line(pts, fill=dim, width=3, joint="curve")
        d.line(pts[:-1], fill=pale, width=1, joint="curve")
        d.point((21 + off, 34), fill=lit)
    else:  # fine spray droplets, scattered but a single tiny particle cluster
        dots = [(25, 32, 2), (31, 28, 2), (35, 34, 3), (40, 30, 2), (30, 39, 2), (43, 37, 1)]
        for i, (x, y, r) in enumerate(dots):
            x += off + (variant % 2) * (i % 2)
            y += (variant - 1) * ((i + 1) % 2)
            d.rectangle((x, y, x + r, y + r), fill=dim if i % 2 else pale)
            d.point((x, y), fill=lit)
    return expand(im)


def main():
    tornado = tornado_base()
    front = front_base()
    sheet = Image.new("RGBA", (3072, 1536))
    for i in range(6):
        angle = i * 15
        t = tornado.rotate(-angle, Image.Resampling.NEAREST)
        f = rolled_front(front, (0, 4, 8, 12, 8, 4)[i])
        sheet.alpha_composite(expand(t), (i * 512, 0))
        sheet.alpha_composite(expand(f), (i * 512, 512))
    for i, angle in enumerate((0, 30)):
        w = warning(tornado.rotate(-angle, Image.Resampling.NEAREST), .65)
        sheet.alpha_composite(expand(w), (i * 512, 1024))
    for i, shift in enumerate((0, 8)):
        f = rolled_front(front, shift)
        sheet.alpha_composite(expand(warning(f, .64, front=True)), ((i + 2) * 512, 1024))
    sheet.save(ROOT / "hazard.png")

    ambient = Image.new("RGBA", (2048, 512))
    for kind in range(4):
        for variant in range(4):
            n = kind * 4 + variant
            ambient.alpha_composite(ambient_particle(kind, variant), ((n % 8) * 256, (n // 8) * 256))
    ambient.save(ROOT / "ambient.png")

    for name, image in (("tornado", tornado), ("gust", front)):
        aa = np.asarray(image)[..., 3]
        active = aa > 8
        print(name, "mean active alpha", round(float(aa[active].mean()) / 255, 3), "max", int(aa.max()))


if __name__ == "__main__":
    main()
