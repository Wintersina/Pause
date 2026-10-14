"""Verdant elites at true high resolution: build_verdant_elites.py's recipe on a 384 px cell (Z = 2) with a premultiplied area-averaging
downsample of the same four painted masters (the old build used NEAREST), the articulated parts / banks / death fragments rotated
bicubically (premultiplied) instead of nearest, and the old 192-space drawing code scaled through elite_hd.ZDraw. Writes ../../hd~/.
(The Resin Warden has no master and is not rebuilt.)
    python3 build_verdant_hd.py [timber_hauler thornlash sporebloom leafblade]
"""
from __future__ import annotations
import colorsys, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ELITE = HERE.parents[2]                         # Art/Enemies/Elite
ART = HERE.parents[4]                           # Art
sys.path.insert(0, str(ELITE / "src~"))
import elite_hd as E
from elite_hd import Z, CELL, ZDraw

OUT = ELITE / "Verdant" / "hd~"
INK = (6, 12, 10, 255); PALE = (199, 255, 191, 255); AMBER = (239, 190, 81, 255)
SPECS = {
    "timber_hauler": {
        "size": (128, 108), "core": (96, 117), "core_r": (8, 12), "engines": [(73, 136), (96, 146), (118, 136)],
        "parts": [([(29, 38), (68, 38), (79, 71), (72, 84), (52, 84), (28, 76)], (64, 77)), ([(124, 38), (163, 38), (164, 76), (140, 84), (120, 84), (113, 71)], (128, 77))],
        "angles": {0: (7, -7), 1: (4, -4), 2: (0, 0), 3: (-3, 3), 4: (7, 0), 5: (0, -7), 6: (0, 0)}, "crop": (60, 60, 132, 132)},
    "thornlash": {
        "size": (126, 136), "core": (96, 65), "core_r": (8, 11), "engines": [(86, 134), (106, 134)],
        "parts": [([(27, 65), (79, 65), (83, 94), (62, 112), (26, 111)], (76, 80)), ([(26, 111), (78, 108), (84, 133), (51, 174), (25, 170)], (75, 117)),
                  ([(113, 65), (165, 65), (166, 111), (130, 112), (109, 94)], (116, 80)), ([(108, 108), (166, 111), (167, 170), (141, 174), (108, 133)], (117, 117)),
                  ([(52, 19), (84, 19), (88, 68), (73, 74), (54, 68)], (82, 67)), ([(108, 19), (140, 19), (138, 68), (119, 74), (104, 68)], (110, 67))],
        "angles": {0: (5, -4, -5, 4, 8, -8), 1: (3, -2, -3, 2, 4, -4), 2: (0, 0, 0, 0, -2, 2), 3: (-3, 3, 3, -3, -4, 4), 4: (7, -6, 2, -2, 8, 2), 5: (2, -2, 7, -6, -2, -8), 6: (4, 1, -4, -1, 6, -6)},
        "crop": (60, 30, 132, 102)},
    "sporebloom": {
        "size": (112, 138), "core": (96, 77), "core_r": (19, 22), "engines": [(82, 158), (110, 158)],
        "parts": [([(36, 26), (79, 27), (76, 72), (79, 125), (58, 139), (34, 116)], (77, 108)), ([(113, 27), (156, 26), (158, 116), (134, 139), (113, 125), (116, 72)], (115, 108))],
        "angles": {0: (7, -7), 1: (4, -4), 2: (0, 0), 3: (-3, 3), 4: (7, 0), 5: (0, -7), 6: (5, -5)}, "crop": (60, 40, 132, 112)},
    "leafblade": {
        "size": (102, 142), "core": (96, 52), "core_r": (7, 10), "engines": [(87, 158), (105, 158)],
        "parts": [([(38, 21), (75, 21), (84, 69), (82, 113), (53, 142), (38, 129)], (76, 105)), ([(117, 21), (154, 21), (154, 129), (139, 142), (110, 113), (108, 69)], (116, 105))],
        "angles": {0: (8, -8), 1: (5, -5), 2: (2, -2), 3: (-3, 3), 4: (8, -1), 5: (1, -8), 6: (5, -5)}, "crop": (66, 20, 126, 80)},
}
LOC = {"timber_hauler": [(70, 95), (82, 99), (89, 109)], "thornlash": [(99, 75), (105, 85), (110, 93)], "sporebloom": [(96, 68), (103, 80), (110, 85)], "leafblade": [(97, 85), (102, 99), (107, 113)]}


def painted_master(name, size):
    src = E.fit_to_master(Image.open(HERE / f"{name}_master.png").convert("RGBA"), 33, size, name)
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.alpha_composite(src, ((CELL - src.width) // 2, (CELL - src.height) // 2))
    ar = np.array(canvas)
    ar[ar[:, :, 3] < 12] = 0
    for y, x in zip(*np.where(ar[:, :, 3] >= 32)):
        rr, gg, bb = (int(n) / 255 for n in ar[y, x, :3])
        h, s, v = colorsys.rgb_to_hsv(rr, gg, bb)
        if s > 0.5 and (h * 360 >= 345 or h * 360 <= 15):
            ar[y, x, :3] = [round(c * 255) for c in colorsys.hsv_to_rgb(34 / 360, min(s, .48), v)]
        elif s > .45 and 60 <= h * 360 <= 130:
            emerald_hue = 135 + (h * 360 - 60) * 20 / 70
            ar[y, x, :3] = [round(c * 255) for c in colorsys.hsv_to_rgb(emerald_hue / 360, s, v)]
    solid = ar[:, :, 3] > 190
    up = E.shift_mask(solid, 0, Z); left = E.shift_mask(solid, Z, 0)
    edge = solid & (~left | ~up)
    inner_rim = solid & ~edge & (E.shift_mask(edge, Z, 0) | E.shift_mask(edge, 0, Z))
    ar[inner_rim, :3] = (ar[inner_rim, :3].astype(np.float32) * .78 + np.array((192, 228, 169)) * .22).astype(np.uint8)
    ar[edge, :3] = (ar[edge, :3].astype(np.float32) * .43 + np.array((192, 228, 169)) * .57).astype(np.uint8)
    return Image.fromarray(ar, "RGBA")


def articulated(base, spec, frame):
    remaining = base.copy(); moved = []
    for (poly, pivot), angle in zip(spec["parts"], spec["angles"][frame]):
        mask = Image.new("L", (CELL, CELL))
        ImageDraw.Draw(mask).polygon([(x * Z, y * Z) for x, y in poly], fill=255)
        part = base.copy()
        part.putalpha(Image.fromarray(np.minimum(np.array(base.getchannel("A")), np.array(mask)), "L"))
        ra = np.array(remaining); ra[np.array(mask) > 0, 3] = 0
        remaining = Image.fromarray(ra, "RGBA")
        moved.append(E.rotate(part, angle, pivot))
    output = Image.new("RGBA", (CELL, CELL))
    for part in moved: output.alpha_composite(part)
    output.alpha_composite(remaining)
    if frame in (4, 5): output = E.rotate(output, 3 if frame == 4 else -3, (96, 96))
    return output


def light_region(im, center, radii, factor):
    data = np.array(im)
    yy, xx = E.zgrid()
    rr = ((xx - center[0]) / radii[0]) ** 2 + ((yy - center[1]) / radii[1]) ** 2
    strength = np.clip(1 - rr, 0, 1)[..., None]
    rgb = data[:, :, :3].astype(np.float32) * (1 + (factor - 1) * strength)
    data[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(data, "RGBA")


def engines(im, positions, frame):
    result = im.copy()
    for x, y in positions:
        result = light_region(result, (x, y), (8, 9), {0: .24, 1: .43, 2: 1.2, 3: 1.0, 4: 1.0, 5: 1.0, 6: .7}[frame])
    if frame not in (2, 3, 4, 5): return result
    draw = ZDraw(result)
    length = 15 if frame == 2 else 8
    sway = 2 if frame == 4 else -2 if frame == 5 else 0
    for x, y in positions:
        draw.polygon([(x - 4, y + 1), (x + 4, y + 1), (x + sway + 2, y + length - 3), (x + sway, y + length), (x + sway - 2, y + length - 3)], fill=(28, 164, 91, 210))
        draw.polygon([(x - 2, y + 1), (x + 2, y + 1), (x + sway, y + length - 4)], fill=(231, 223, 137, 230))
        draw.line((x, y + 1, x + sway, y + length - 5), fill=PALE, width=1)
    return result


def damaged(im, name):
    out = im.copy(); d = ZDraw(out)
    loc = LOC[name]
    d.line(loc, fill=INK, width=3, joint="curve"); d.line(loc, fill=(71, 240, 147, 255), width=1, joint="curve")
    for i, (x, y) in enumerate(loc):
        d.line((x + 2, y - 2, x + 5 + i, y - 5), fill=AMBER, width=1); d.point((x + 7, y - 6), fill=PALE)
    return out


def flight_frames(name, spec, base):
    frames = []
    for i in range(7):
        im = articulated(base, spec, i)
        im = light_region(im, spec["core"], spec["core_r"], [.37, .65, 1.25, 1.0, 1.12, .9, .7][i])
        im = engines(im, spec["engines"], i)
        if i == 6: im = damaged(im, name)
        frames.append(im)
    return frames


def crack_and_sparks(im, center, frame):
    out = im.copy(); d = ZDraw(out); cx, cy = center
    if frame == 0:
        d.ellipse((cx - 14, cy - 14, cx + 14, cy + 14), fill=(49, 226, 121, 145)); d.ellipse((cx - 7, cy - 7, cx + 7, cy + 7), fill=(218, 255, 194, 220))
    else:
        for i in range(11 if frame == 1 else 18):
            a = (i * 137.5 + 11) * math.pi / 180
            r = (12 + i % 4 * 4) if frame == 1 else (20 + i % 5 * 5)
            x = round(cx + math.cos(a) * r); y = round(cy + math.sin(a) * r)
            col = (84, 242, 150, 255) if i % 3 else AMBER
            d.line((x, y, x + round(math.cos(a) * 4), y + round(math.sin(a) * 4)), fill=col, width=1)
            d.point((x + 3, y - 2), fill=PALE)
    return out


def death_frames(base, spec):
    cx, cy = spec["core"]
    frames = [crack_and_sparks(light_region(base, (cx, cy), (29, 29), 1.75), (cx, cy), 0)]
    h = 96 * Z
    for stage in (1, 2):
        out = Image.new("RGBA", (CELL, CELL))
        offsets = [(-5, -5), (5, -5), (-5, 5), (5, 5)] if stage == 1 else [(-10, -10), (10, -10), (-10, 10), (10, 10)]
        if stage == 2:
            t = Image.new("RGBA", (CELL, CELL)); t.alpha_composite(E.premult_resize(base, (160 * Z, 160 * Z)), (16 * Z, 16 * Z))
        else:
            t = base
        for j, (dx, dy) in enumerate(offsets):
            x0 = 0 if j % 2 == 0 else h; y0 = 0 if j < 2 else h
            frag = t.crop((x0, y0, x0 + h, y0 + h))
            frag = E.rotate(frag, ((-1) ** j) * (3 if stage == 1 else 8), (48, 48))
            out.alpha_composite(frag, (x0 + dx * Z, y0 + dy * Z))
        for j in range(15 if stage == 1 else 25):
            theta = (j * 2.399963 + .35)
            sx = max(20, min(167, round(cx + math.cos(theta) * (12 + j % 6 * 4))))
            sy = max(20, min(167, round(cy + math.sin(theta) * (10 + j % 5 * 5))))
            chip = base.crop(((sx - 3) * Z, (sy - 3) * Z, (sx + 4) * Z, (sy + 4) * Z))
            drift = (9 + j % 5 * 3) if stage == 1 else (20 + j % 6 * 4)
            px = max(17, min(168, round(sx + math.cos(theta) * drift))); py = max(17, min(168, round(sy + math.sin(theta) * drift)))
            chip = chip.resize((chip.width, chip.height))
            canvas = Image.new("RGBA", (CELL, CELL)); canvas.alpha_composite(chip, (px * Z, py * Z))
            # chip.rotate(j*17) turned the chip about its own centre
            ccx, ccy = (px * Z + chip.width / 2) / Z, (py * Z + chip.height / 2) / Z
            out.alpha_composite(E.rotate(canvas, j * 17, (ccx, ccy)))
        out = light_region(out, (cx, cy), (18, 18), 1.35 if stage == 1 else .75)
        frames.append(crack_and_sparks(out, (cx, cy), stage))
    return frames


def build(name):
    spec = SPECS[name]; key = "verdant_elite_" + name
    base = painted_master(name, spec["size"])
    flight = flight_frames(name, spec, base)
    E.emit(OUT, key, E.paste_strip(flight), E.paste_strip(death_frames(base, spec)), ART / "Resources/Elites/Defs", ELITE / "Verdant" / f"{key}.png", spec["crop"])


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for n in (sys.argv[1:] or list(SPECS)): build(n)
