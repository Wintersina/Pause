#!/usr/bin/env python3
"""Assemble Tide weather from the selected painted imagegen sources."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
S = 4
RAMP = np.array([
    (14, 42, 48),    # deep teal-black water
    (39, 85, 88),
    (76, 151, 143),
    (143, 208, 196),  # pale teal
    (124, 242, 192),  # mint aqua, used sparingly
    (191, 239, 228),
    (240, 255, 250),  # sea-foam white, a few crest pixels
], dtype=np.uint8)


def art_crop(name):
    im = Image.open(SRC / name).convert("RGBA")
    a = np.asarray(im)[..., 3]
    yy, xx = np.where(a > 24)
    return im.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))


def clean(im, kind, cap):
    a = np.asarray(im).copy()
    rgb = a[..., :3].astype(float)
    lum = rgb @ np.array([.2126, .7152, .0722])
    # Fixed painterly tones; the painted light and water texture determine placement.
    ix = np.digitize(lum, [48, 77, 109, 148, 183, 227])
    a[..., :3] = RAMP[ix]
    if kind == "whirlpool":
        yy, xx = np.indices(lum.shape)
        r = np.hypot(xx - 63.5, yy - 63.5)
        air = np.clip((r - 9) / 29, .07, 1)
        strength = np.interp(lum, [0, 70, 150, 255], [.20, .28, .45, .54])
        alpha = a[..., 3] / 255 * cap * strength * air
    elif kind == "surge":
        yy = np.arange(a.shape[0])[:, None]
        taper = np.minimum(np.clip((yy - 26) / 11, 0, 1), np.clip((104 - yy) / 13, 0, 1))
        strength = np.interp(lum, [0, 70, 150, 255], [.22, .31, .45, .55])
        alpha = a[..., 3] / 255 * cap * strength * taper
    else:
        alpha = a[..., 3] / 255 * cap
    a[..., 3] = np.clip(alpha, 0, 255).astype(np.uint8)
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def whirlpool():
    source = art_crop("whirlpool_candidate_2.png")
    source = source.resize((108, 108), Image.Resampling.NEAREST)
    base = Image.new("RGBA", (128, 128))
    base.alpha_composite(source, (10, 10))
    base = clean(base, "whirlpool", 255)
    a = np.asarray(base).astype(float)
    premul = a[..., :3] * a[..., 3:4] / 255
    # The four equivalent quarter-turns retain the painted crests and close the loop.
    al = np.mean([np.rot90(a[..., 3], k) for k in range(4)], axis=0)
    col = np.mean([np.rot90(premul, k) for k in range(4)], axis=0)
    out = np.zeros((128, 128, 4), dtype=np.uint8)
    out[..., :3] = np.clip(col * 255 / np.maximum(al[..., None], 1), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(al * 1.45, 0, 145).astype(np.uint8)
    out[out[..., 3] == 0, :3] = 0
    return Image.fromarray(out, "RGBA")


def periodic_band(im, period=64):
    a = np.asarray(im).copy()
    # Two repeats allow lateral travel and a seamless wrap. Blend the selected
    # painted segment over 5 native pixels before repeating it.
    tile = a[:, 24:24 + period].copy().astype(float)
    for j in range(5):
        f = (j + 1) / 6
        v = tile[:, j] * f + tile[:, period - 5 + j] * (1 - f)
        tile[:, j] = v
        tile[:, period - 5 + j] = v
    tile = tile.astype(np.uint8)
    out = np.concatenate([tile, tile], axis=1)
    out[:, -1] = out[:, 0]
    return Image.fromarray(out, "RGBA")


def surge():
    source = art_crop("surge_candidate_1.png")
    source = source.resize((128, 75), Image.Resampling.NEAREST)
    base = Image.new("RGBA", (128, 128))
    base.alpha_composite(source, (0, 27))
    return periodic_band(clean(base, "surge", 255))


def warning(im, amount, front=False):
    a = np.asarray(im).copy()
    rgb = a[..., :3].astype(float)
    lum = rgb @ np.array([.2126, .7152, .0722])
    yy, xx = np.indices(lum.shape)
    # Preview a thin foam contour and occasional water strokes, around one
    # third of the live body's alpha mass.
    hashval = (xx * 13 + yy * 29 + xx * yy) % 23
    if front:
        retain = ((lum > 204) & (yy < 77) & (hashval < 20)) | ((lum > 130) & (hashval < 5))
    else:
        radius = np.hypot(xx - 63.5, yy - 63.5)
        retain = (radius > 28) & (((lum > 205) & (hashval < 20)) | ((lum > 130) & (hashval < 5)))
    a[..., 3] = (a[..., 3] * amount * retain).astype(np.uint8)
    if front:
        a[:, :4, 3] = 0
        a[:, -4:, 3] = 0
    return Image.fromarray(a, "RGBA")


def expand(im):
    return im.resize((im.width * S, im.height * S), Image.Resampling.NEAREST)


def ambient_source(part):
    # Four separated imagegen motifs, in source order: ring, rain, spray, fleck.
    boxes = [(180, 180, 460, 490), (610, 165, 865, 555),
             (1070, 190, 1455, 510), (1630, 210, 1930, 465)]
    return Image.open(SRC / "ambient_source.png").convert("RGBA").crop(boxes[part])


def ambient_particle(kind, variant):
    source = ambient_source(kind)
    sizes = [(17, 22, 27, 23), (22, 26, 31, 28),
             (26, 30, 25, 32), (20, 25, 28, 23)][kind]
    size = sizes[variant]
    aspect = source.width / source.height
    if kind == 1:
        w, h = max(9, int(size * aspect * .75)), size
    else:
        w, h = size, max(10, int(size / aspect))
    motif = source.resize((w, h), Image.Resampling.NEAREST)
    if kind == 0:
        # Keep the source ring and its top-left glint; clear specks in its eye.
        a = np.asarray(motif).copy()
        yy, xx = np.indices((h, w))
        cx, cy = w * .49, h * .51
        hole = ((xx - cx) / max(w * .21, 1))**2 + ((yy - cy) / max(h * .22, 1))**2 < 1
        a[hole, 3] = 0
        motif = Image.fromarray(a, "RGBA")
    if kind == 3:
        # The painted fleck is deliberately slight and angular at this scale.
        motif = motif.rotate((0, 8, -5, 12)[variant], Image.Resampling.NEAREST, expand=True)
    motif = clean(motif, "ambient", (166, 183, 175, 190)[variant])
    cell = Image.new("RGBA", (64, 64))
    cell.alpha_composite(motif, ((64 - motif.width) // 2, (64 - motif.height) // 2))
    return expand(cell)


def main():
    hpath, apath = ROOT / "hazard.png", ROOT / "ambient.png"
    if (hpath.exists() or apath.exists()) and "--rebuild" not in sys.argv:
        raise SystemExit("Refusing to overwrite an existing deliverable")
    whirl, front = whirlpool(), surge()
    sheet = Image.new("RGBA", (3072, 1536))
    for i in range(6):
        swirl_frame = whirl.rotate(-15 * i, Image.Resampling.NEAREST)
        band = np.roll(np.asarray(front), (0, 10, 21, 32, 43, 53)[i], axis=1).copy()
        band[:, -1] = band[:, 0]
        sheet.alpha_composite(expand(swirl_frame), (512 * i, 0))
        sheet.alpha_composite(expand(Image.fromarray(band, "RGBA")), (512 * i, 512))
    for i, angle in enumerate((0, 30)):
        sheet.alpha_composite(expand(warning(whirl.rotate(-angle, Image.Resampling.NEAREST), .64)), (512 * i, 1024))
    for i, shift in enumerate((0, 21)):
        band = np.roll(np.asarray(front), shift, axis=1).copy()
        sheet.alpha_composite(expand(warning(Image.fromarray(band, "RGBA"), .64, True)), (512 * (i + 2), 1024))
    sheet.save(hpath)

    ambient = Image.new("RGBA", (2048, 512))
    for kind in range(4):
        for variant in range(4):
            n = kind * 4 + variant
            ambient.alpha_composite(ambient_particle(kind, variant), ((n % 8) * 256, (n // 8) * 256))
    ambient.save(apath)
    for name, im in (("whirlpool", whirl), ("surge", front)):
        alpha = np.asarray(im)[..., 3]
        active = alpha > 8
        print(name, "mean active alpha", round(float(alpha[active].mean()) / 255, 3),
              "mean all alpha", round(float(alpha.mean()) / 255, 3), "max", int(alpha.max()))


if __name__ == "__main__":
    main()
