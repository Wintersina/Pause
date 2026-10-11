#!/usr/bin/env python3
"""Build the Desert rail-mine row from the selected image-painted concept.

Only nearest-neighbour resampling is used. The three telegraph states add
pixel-drawn lens and heat effects to the same painted mine and fixed pivot.
"""
from __future__ import annotations

from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
SOURCE = HERE / "candidate_1.png"
OUTPUT = ROOT / "rail_mines_neon_desert.png"
EDGES = (0, 313, 627, 940, 1254)
SIZE = (313, 314)
PIVOT = (215, 158)
INK = (17, 17, 31, 255)
SUN = (255, 241, 200, 255)
HOT = (255, 250, 231, 255)
GLASS = (159, 201, 192, 255)
BONE = (230, 220, 192, 255)
SAND = (191, 174, 128, 255)


def painted_body() -> Image.Image:
    src = Image.open(SOURCE).convert("RGBA")
    a = np.asarray(src)
    ys, xs = np.where(a[:, :, 3] >= 128)
    # The generated concept already has transparency; trim faint stray pixels.
    crop = src.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    crop = crop.resize((241, 215), Image.Resampling.NEAREST)
    rgba = np.array(crop)
    hsv = np.array(crop.convert("HSV"))
    hue = hsv[:, :, 0].astype(np.float32) * 360 / 255
    sat = hsv[:, :, 1].astype(np.float32) / 255
    val = hsv[:, :, 2].astype(np.float32) / 255
    active = rgba[:, :, 3] >= 128
    neutral = sat < .12
    copper = active & ~neutral & (((hue < 40) | (hue >= 345)) & ~((val > .68) & (sat < .42)))
    sand = active & ~neutral & (((hue >= 40) & (hue < 75)) | ((hue < 40) & (val > .68) & (sat < .42)))
    glass = active & ~neutral & (hue >= 75) & (hue < 215)
    shadow = active & ~neutral & (hue >= 215) & (hue < 345)
    # Remap only material classes. Source luminance variation and local texture
    # survive; the copper is deliberately matte and the sand desaturated.
    hue[copper] = 26
    sat[copper] = np.minimum(np.maximum(sat[copper], .22), .57)
    val[copper] = np.minimum(val[copper] * .86, .59)
    hue[sand] = 44
    sat[sand] = np.minimum(sat[sand], .31)
    val[sand] = np.minimum(val[sand], .91)
    hue[glass] = 166
    sat[glass] = np.minimum(sat[glass], .20)
    val[glass] = np.minimum(val[glass], .64)
    hue[shadow] = 238
    sat[shadow] = np.minimum(np.maximum(sat[shadow], .27), .42)
    val[shadow] = np.minimum(val[shadow], .29)
    # Near-neutral ink stays dark; pale neutral glints take a bone tint.
    hue[neutral & (val > .40)] = 44
    sat[neutral & (val > .40)] = .11
    hsv[:, :, 0] = np.round(hue * 255 / 360).astype(np.uint8)
    hsv[:, :, 1] = np.round(sat * 255).astype(np.uint8)
    hsv[:, :, 2] = np.round(val * 255).astype(np.uint8)
    rgba[:, :, :3] = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    rgba[:, :, 3] = np.where(active, 255, 0).astype(np.uint8)
    # Byte rounding can reintroduce forbidden hues in near-neutral dark paint.
    # Correct those exact RGB results before the 1 px boundary is inked.
    corrected = np.array(Image.fromarray(rgba, "RGBA").convert("HSV"))
    ch = corrected[:, :, 0].astype(np.float32) * 360 / 255
    cs = corrected[:, :, 1].astype(np.float32) / 255
    cv = corrected[:, :, 2].astype(np.float32) / 255
    cold = active & (ch >= 250) & (ch < 345)
    amber = active & (ch >= 30) & (ch <= 48) & (cs > .45)
    copper_high = active & (ch >= 22) & (ch <= 30) & (cv > .60)
    copper_hot = active & (ch >= 22) & (ch <= 30) & (cs > .62)
    corrected[cold, 0] = round(238 * 255 / 360)
    corrected[cold, 1] = np.minimum(corrected[cold, 1], 102)
    corrected[cold, 2] = np.minimum(corrected[cold, 2], 76)
    corrected[amber | copper_high, 0] = round(44 * 255 / 360)
    corrected[amber | copper_high, 1] = np.minimum(corrected[amber | copper_high, 1], 76)
    corrected[copper_hot, 1] = np.minimum(corrected[copper_hot, 1], 145)
    rgba[:, :, :3] = np.array(Image.fromarray(corrected, "HSV").convert("RGB"))
    # At values below 0.08 HSV cannot retain precise saturation in 8-bit RGB.
    nearly_black = active & (cv < .08)
    rgba[nearly_black, :3] = (17, 17, 31)
    body = Image.new("RGBA", SIZE)
    body.paste(Image.fromarray(rgba, "RGBA"), (58, 49))
    # A single dark boundary pixel makes the outline reliable after recelling.
    arr = np.array(body)
    mask = arr[:, :, 3] > 0
    interior = mask.copy()
    interior[1:, :] &= mask[:-1, :]
    interior[:-1, :] &= mask[1:, :]
    interior[:, 1:] &= mask[:, :-1]
    interior[:, :-1] &= mask[:, 1:]
    arr[mask & ~interior, :3] = INK[:3]
    return Image.fromarray(arr, "RGBA")


def ring(draw: ImageDraw.ImageDraw, r: int, color: tuple, width: int = 1,
         start: int = 0, end: int = 360) -> None:
    x, y = PIVOT
    draw.arc((x-r, y-r, x+r, y+r), start, end, fill=color, width=width)


def shard(draw: ImageDraw.ImageDraw, angle: float, inner: float,
          outer: float, width: float, color: tuple) -> None:
    x, y = PIVOT
    c, s = math.cos(angle), math.sin(angle)
    # A burst ray must terminate inside its own atlas cell, even on the right.
    limits = [outer]
    if c > .001:
        limits.append((SIZE[0] - 9 - x) / c)
    elif c < -.001:
        limits.append((x - 9) / -c)
    if s > .001:
        limits.append((SIZE[1] - 9 - y) / s)
    elif s < -.001:
        limits.append((y - 9) / -s)
    outer = min(limits)
    px, py = -s, c
    pts = [
        (round(x+c*inner+px*width), round(y+s*inner+py*width)),
        (round(x+c*outer), round(y+s*outer)),
        (round(x+c*inner-px*width), round(y+s*inner-py*width)),
    ]
    draw.polygon(pts, fill=color)


def state(body: Image.Image, index: int) -> Image.Image:
    frame = body.copy()
    if index == 0:
        return frame
    fx = Image.new("RGBA", SIZE)
    d = ImageDraw.Draw(fx)
    x, y = PIVOT
    if index == 1:
        # Tiny glass shimmer and a slow lens aperture opening.
        ring(d, 44, (159, 201, 192, 170), 2, 208, 325)
        ring(d, 36, (230, 220, 192, 170), 1, 10, 78)
        d.ellipse((x-13, y-13, x+13, y+13), fill=(64, 77, 84, 255))
        d.ellipse((x-8, y-8, x+8, y+8), fill=(159, 201, 192, 255))
        d.ellipse((x-4, y-4, x+4, y+4), fill=SUN)
        d.line((x-18, y-17, x-12, y-21), fill=GLASS, width=2)
        d.point((x+22, y-26), fill=HOT)
        for px, py in [(47, 109), (294, 74), (276, 277), (128, 40)]:
            d.line((px-2, py, px+2, py), fill=BONE, width=1)
            d.line((px, py-2, px, py+2), fill=GLASS, width=1)
    elif index == 2:
        # Crisp stepped bloom bands, with the beam exit at the true pivot.
        ring(d, 57, (230, 220, 192, 105), 2, 34, 146)
        ring(d, 57, (230, 220, 192, 105), 2, 214, 325)
        ring(d, 45, (255, 241, 200, 190), 3)
        ring(d, 37, (159, 201, 192, 200), 2)
        for k in range(8):
            ang = (k * 45 + 16) * math.pi / 180
            shard(d, ang, 44, 69 if k % 2 else 77, 3, BONE if k % 2 else SUN)
        d.ellipse((x-24, y-24, x+24, y+24), fill=(230, 220, 192, 220))
        d.ellipse((x-17, y-17, x+17, y+17), fill=SUN)
        d.ellipse((x-9, y-9, x+9, y+9), fill=HOT)
        d.ellipse((x-4, y-4, x+4, y+4), fill=(255, 255, 248, 255))
        for dx, dy in [(-70, -18), (70, 12), (-55, 53), (44, -67)]:
            d.rectangle((x+dx, y+dy, x+dx+2, y+dy+2), fill=BONE)
        # Sparse outer heat distortion makes this silhouette grow toward Burst.
        d.arc((32, 25, 303, 294), 122, 202, fill=(230, 220, 192, 180), width=2)
        d.arc((32, 25, 303, 294), 224, 291, fill=(230, 220, 192, 180), width=2)
        d.arc((32, 25, 303, 294), 8, 57, fill=(230, 220, 192, 180), width=2)
        for px, py in [(34, 102), (301, 81), (190, 27), (254, 289)]:
            d.rectangle((px, py, px+2, py+2), fill=SUN)
    else:
        # The painted body and clamp remain underneath the lens rupture.
        ring(d, 92, (230, 220, 192, 150), 3, 2, 162)
        ring(d, 92, (230, 220, 192, 150), 3, 188, 348)
        ring(d, 75, (255, 241, 200, 220), 3, 15, 177)
        ring(d, 75, (255, 241, 200, 220), 3, 196, 354)
        ring(d, 59, (255, 250, 231, 255), 2)
        for k in range(12):
            ang = (k * 30 + 7) * math.pi / 180
            shard(d, ang, 18, 135 + (k % 3) * 8, 10 if k % 2 else 7,
                  SUN if k % 3 else HOT)
            shard(d, ang, 55, 138 + (k % 2) * 8, 3, HOT)
        d.ellipse((x-43, y-43, x+43, y+43), fill=SUN)
        d.ellipse((x-30, y-30, x+30, y+30), fill=HOT)
        d.ellipse((x-16, y-16, x+16, y+16), fill=(255, 255, 248, 255))
        for dx, dy in [(-184, -43), (-170, 58), (72, -82), (78, 53), (-14, -143), (31, 138)]:
            d.polygon([(x+dx, y+dy-4), (x+dx+5, y+dy), (x+dx-2, y+dy+3)], fill=SAND)
    return Image.alpha_composite(frame, fx)


def main() -> None:
    body = painted_body()
    sheet = Image.new("RGBA", (1254, 314))
    for i in range(4):
        pose = state(body, i)
        sheet.alpha_composite(pose, (EDGES[i], 0))
    # Semi-transparent bone rings blend with copper; keep those overlaps pale.
    rgba = np.array(sheet)
    hsv = np.array(sheet.convert("HSV"))
    h = hsv[:, :, 0].astype(np.float32) * 360 / 255
    v = hsv[:, :, 2].astype(np.float32)
    hot_copper = (rgba[:, :, 3] > 0) & (h >= 22) & (h <= 30) & (v > 153)
    rgba[hot_copper, 0] = np.round(v[hot_copper]).astype(np.uint8)
    rgba[hot_copper, 1] = np.round(v[hot_copper] * .94).astype(np.uint8)
    rgba[hot_copper, 2] = np.round(v[hot_copper] * .78).astype(np.uint8)
    sheet = Image.fromarray(rgba, "RGBA")
    sheet.save(OUTPUT)
    # Art-only preview, 2x nearest on the requested dark background.
    preview = Image.new("RGBA", sheet.size, (11, 11, 26, 255))
    preview.alpha_composite(sheet)
    preview.resize((2508, 628), Image.Resampling.NEAREST).convert("RGB").save(ROOT / "preview.png")


if __name__ == "__main__":
    main()
