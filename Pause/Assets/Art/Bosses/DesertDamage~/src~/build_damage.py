"""Assemble image-generated Desert boss damage art on the approved 384 px grid.

All source paintings live beside this script. Only nearest-neighbour resampling is
used; the small drawn marks connect and animate the painted mechanical sources.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
CELL = 384
R = Image.Resampling.NEAREST
WHITE = (255, 241, 200, 255)
PALE = (232, 220, 192, 255)
INK = (8, 10, 19, 255)


def clean_palette(im: Image.Image) -> Image.Image:
    """Remove generated chromatic fringes and enforce Desert material limits."""
    im = im.convert("RGBA")
    rgb = im.convert("RGB")
    hsv = np.array(rgb.convert("HSV"), dtype=np.uint8)
    h, s, v = (hsv[:, :, i].copy() for i in range(3))
    # Generated anti-alias fringes sometimes invent red, magenta and yellow.
    # Include two guard bins: Pillow's HSV round-trip can shift 15-17° copper
    # down into the forbidden 0-15° band.
    red = (((h <= 12) | (h > 244)) & (s > 95))
    h[red], s[red], v[red] = 19, np.minimum(s[red], 112), np.minimum(v[red], 150)
    magenta = ((h >= 214) & (h <= 239) & (s > 70))
    h[magenta], s[magenta] = 32, np.minimum(s[magenta], 50)
    violet = ((h >= 177) & (h <= 213) & (s > 55))
    h[violet], s[violet] = 169, np.minimum(s[violet], 94)
    alien = ((h >= 57) & (h <= 175) & (s > 60))
    h[alien], s[alien] = 31, np.minimum(s[alien], 45)
    amber = ((h >= 20) & (h <= 35) & (s > 106))
    s[amber] = 75
    bright_copper = ((h >= 12) & (h < 25) & (s > 85) & (v > 154))
    v[bright_copper] = 150
    hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2] = h, s, v
    cleaned = Image.fromarray(hsv, "HSV").convert("RGB").convert("RGBA")
    cleaned.putalpha(im.getchannel("A"))
    return cleaned


def painting(name: str, box=(21, 21, 362, 363), threshold=80) -> Image.Image:
    im = Image.open(SRC / f"{name}_candidate.png").convert("RGBA")
    a = np.asarray(im.getchannel("A"))
    good = Image.fromarray(np.where(a >= threshold, 255, 0).astype("uint8"))
    bounds = good.getbbox()
    if bounds is None:
        raise ValueError(f"Empty painting: {name}")
    im = im.crop(bounds).resize((box[2] - box[0], box[3] - box[1]), R)
    arr = np.array(im)
    arr[:, :, 3] = np.where(arr[:, :, 3] >= threshold, 255, 0)
    im = clean_palette(Image.fromarray(arr, "RGBA"))
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.paste(im, box[:2])
    return canvas


def cut_poly(im: Image.Image, pts):
    mask = Image.new("L", im.size)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    im.paste((0, 0, 0, 0), (0, 0), mask)


def patch(dst: Image.Image, source: Image.Image, pts):
    mask = Image.new("L", dst.size)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    dst.paste(source, (0, 0), mask)


pristine = Image.open(SRC / "pristine_cell.png").convert("RGBA")
paint2, paint3, paint4 = (painting(f"stage{i}") for i in (2, 3, 4))


def stage_one() -> Image.Image:
    im = pristine.copy()
    # Surgically exchange mechanical regions with the painted damaged source.
    patch(im, paint2, [(61, 150), (76, 137), (101, 131), (121, 140),
                       (127, 158), (113, 178), (119, 199), (84, 201), (65, 179)])
    patch(im, paint2, [(185, 46), (204, 43), (224, 55), (226, 76),
                       (212, 95), (193, 92), (180, 75)])
    patch(im, paint2, [(98, 262), (118, 254), (132, 268), (133, 295),
                       (115, 315), (98, 301)])
    d = ImageDraw.Draw(im)
    d.line([(68, 155), (83, 151), (87, 157), (96, 155), (102, 163)], fill=INK, width=2)
    d.line([(187, 53), (199, 67), (207, 70), (212, 82)], fill=INK, width=2)
    # A genuinely missing bucket tooth leaves the dark axle exposed.
    cut_poly(im, [(102, 277), (108, 269), (118, 273), (116, 290), (109, 295)])
    return im


def stage_two() -> Image.Image:
    im = paint2.copy()
    # Snap the dust vent crown back inside its original right-hand footprint.
    cut_poly(im, [(326, 43), (336, 40), (344, 47), (345, 66),
                  (337, 65), (330, 58)])
    d = ImageDraw.Draw(im)
    d.line([(323, 64), (327, 61), (332, 66), (339, 63)], fill=INK, width=3)
    d.line([(324, 62), (330, 60), (335, 62)], fill=(190, 171, 131, 255), width=1)
    return im


def stage_three() -> Image.Image:
    return paint3.copy()


def stage_four() -> Image.Image:
    im = paint4.copy()
    # Open two last-heart voids through the generated plates and wheel cage.
    cut_poly(im, [(77, 177), (91, 166), (104, 171), (111, 185),
                  (104, 195), (92, 193), (85, 202)])
    cut_poly(im, [(253, 277), (270, 267), (280, 275), (276, 298),
                  (266, 308), (251, 302)])
    cut_poly(im, [(88, 132), (105, 125), (130, 139), (133, 159),
                  (118, 173), (95, 168), (83, 155)])
    cut_poly(im, [(237, 140), (254, 128), (272, 138), (286, 160),
                  (276, 184), (255, 178), (241, 163)])
    cut_poly(im, [(198, 43), (221, 38), (239, 49), (235, 72),
                  (218, 80), (201, 69)])
    return im


BASES = [stage_one(), stage_two(), stage_three(), stage_four()]


def variant(base: Image.Image, stage: int, b: int) -> Image.Image:
    im = base.copy()
    d = ImageDraw.Draw(im)
    # White machinery flicker and one loose strand, kept inside the hull.
    core = (173, 196) if stage >= 3 else (174, 287)
    radius = (5 if stage >= 3 else 2) + b
    d.ellipse((core[0]-radius, core[1]-radius, core[0]+radius, core[1]+radius),
              fill=WHITE if b == 0 else (231, 219, 190, 255))
    if b:
        d.line([(277, 181), (281, 188), (278, 197), (284, 204)],
               fill=(38, 39, 41, 255), width=2)
        d.point((282, 207), fill=PALE)
        d.point((283, 210), fill=PALE)
    return clean_palette(im)


body_cells = [[variant(BASES[s], s + 1, b) for b in range(2)] for s in range(4)]
body_sheet = Image.new("RGBA", (768, 1536))
for s in range(4):
    for b in range(2):
        body_sheet.alpha_composite(body_cells[s][b], (b * CELL, s * CELL))
body_sheet = clean_palette(body_sheet)
body_sheet.save(OUT / "Desert_damage.png")


smoke_source = Image.open(SRC / "smoke_candidate.png").convert("RGBA")
arc_source = Image.open(SRC / "arcs_candidate.png").convert("RGBA")


def smoke_crop(box, size):
    a = smoke_source.crop(box).resize(size, R)
    q = np.array(clean_palette(a))
    q[:, :, 3] = np.where(q[:, :, 3] >= 80, np.maximum(q[:, :, 3], 205), 0)
    return Image.fromarray(q, "RGBA")


smoke_left = smoke_crop((32, 0, 595, 675), (112, 134))
smoke_right = smoke_crop((620, 0, 1230, 690), (121, 139))
steam = smoke_crop((100, 535, 565, 1160), (74, 102))


def smoke_frame(k):
    im = Image.new("RGBA", (CELL, CELL))
    shift = (0, 2, 4, 3, 1, -1)[k]
    im.alpha_composite(smoke_left, (55 + shift, 33 - shift))
    im.alpha_composite(smoke_right, (211 - shift, 33 + shift // 2))
    im.alpha_composite(steam, (267 + shift, 85 - shift // 2))
    return im


def generated_flare():
    # Use one painted burst as the endpoint of the hand-routed arc network.
    crop = arc_source.crop((55, 62, 326, 333)).resize((27, 27), R)
    a = np.array(crop)
    lum = a[:, :, :3].max(axis=2)
    alpha = np.where((a[:, :, 3] >= 100) & (lum >= 170), 220, 0).astype("uint8")
    white = np.zeros((27, 27, 4), dtype="uint8")
    white[:, :, :3] = (255, 241, 200)
    white[:, :, 3] = alpha
    return Image.fromarray(white, "RGBA")


flare = generated_flare()
ANCHORS = [((96, 163), (169, 197)), ((288, 161), (236, 210)),
           ((321, 99), (274, 177)), ((132, 265), (211, 227))]


def electric_frame(k):
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    rng = random.Random(811 + k)
    for n in range(2 + (k % 2)):
        start, end = ANCHORS[(k + n) % len(ANCHORS)]
        points = [start]
        for j in range(1, 7):
            t = j / 7
            points.append((round(start[0]*(1-t)+end[0]*t+rng.randrange(-8, 9)),
                           round(start[1]*(1-t)+end[1]*t+rng.randrange(-8, 9))))
        points.append(end)
        d.line(points, fill=(150, 135, 108, 145), width=5, joint="curve")
        d.line(points, fill=PALE, width=3, joint="curve")
        d.line(points, fill=WHITE, width=2, joint="curve")
        for x, y in (start, end):
            d.line((x - 5, y, x + 5, y), fill=PALE, width=2)
            d.line((x, y - 5, x, y + 5), fill=WHITE, width=2)
    if k in (1, 4):
        im.alpha_composite(flare, (90 if k == 1 else 262, 147 if k == 1 else 156))
    return im


SMOKES = [smoke_frame(k) for k in range(6)]
ARCS = [electric_frame(k) for k in range(6)]
fx_sheet = Image.new("RGBA", (2304, 768))
for k in range(6):
    fx_sheet.alpha_composite(SMOKES[k], (k * CELL, 0))
    fx_sheet.alpha_composite(ARCS[k], (k * CELL, CELL))
fx_sheet = clean_palette(fx_sheet)
fx_sheet.save(OUT / "Desert_damage_fx.png")


def flash(im, center, length):
    d = ImageDraw.Draw(im)
    x, y = center
    for n in range(10):
        angle = (2 * math.pi * n / 10) + 0.1
        x2, y2 = round(x + length * math.cos(angle)), round(y + length * math.sin(angle))
        d.line((x, y, x2, y2), fill=PALE, width=3)
        d.line((x, y, x2, y2), fill=WHITE, width=1)
    d.ellipse((x-13, y-13, x+13, y+13), fill=WHITE)


death = []
first = body_cells[3][0].copy()
flash(first, (175, 195), 72)
flash(first, (100, 178), 25)
flash(first, (283, 174), 25)
death.append(first)
split = painting("death_split", (20, 19, 365, 365))
flash(split, (179, 192), 64)
death.append(split)
fireball = painting("death_fireball", (24, 24, 360, 360))
death.append(fireball)
peak = painting("death_fireball", (9, 9, 375, 375))
death.append(peak)
ring = painting("death_ring", (16, 16, 368, 368))
death.append(ring)
fade = painting("death_ring", (37, 36, 347, 348))
q = np.array(fade)
q[:, :, 3] = (q[:, :, 3].astype("uint16") * 132 // 255).astype("uint8")
fade = Image.fromarray(q, "RGBA")
death.append(fade)
death_sheet = Image.new("RGBA", (2304, 384))
for k, im in enumerate(death):
    death_sheet.alpha_composite(clean_palette(im), (k * CELL, 0))
death_sheet = clean_palette(death_sheet)
death_sheet.save(OUT / "Desert_death.png")


def on_bg(im):
    bg = Image.new("RGBA", (CELL, CELL), "#0b0b1a")
    bg.alpha_composite(im)
    return bg.convert("RGB").resize((768, 768), R)


preview = Image.new("RGB", (5 * 768, 768), "#0b0b1a")
preview.paste(on_bg(pristine), (0, 0))
for s in range(4):
    cell = body_cells[s][0].copy()
    cell.alpha_composite(SMOKES[2])
    cell.alpha_composite(ARCS[3])
    preview.paste(on_bg(cell), ((s + 1) * 768, 0))
preview.save(OUT / "preview.png")

gif = []
for k in range(12):
    cell = body_cells[3][k // 6].copy()
    cell.alpha_composite(SMOKES[k % 6])
    cell.alpha_composite(ARCS[k % 6])
    gif.append(on_bg(cell))
gif[0].save(OUT / "preview.gif", save_all=True, append_images=gif[1:],
            duration=100, loop=0, optimize=False, disposal=2)

death_preview = Image.new("RGB", (6 * 768, 768), "#0b0b1a")
for k, im in enumerate(death):
    death_preview.paste(on_bg(im), (k * 768, 0))
death_preview.save(OUT / "Desert_death_preview.png")
print("wrote Desert damage, FX, death and previews")
