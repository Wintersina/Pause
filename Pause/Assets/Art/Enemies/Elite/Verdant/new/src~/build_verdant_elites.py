"""Build the Verdant elite sprite deliverables from four painted imagegen masters.

Run from any directory with Pillow and NumPy installed. All resampling of source
art and articulated pieces uses nearest-neighbour sampling.
"""

from __future__ import annotations

import colorsys
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
CELL = 192
INK = (6, 12, 10, 255)
GREEN = (70, 239, 135, 255)
PALE = (199, 255, 191, 255)
AMBER = (239, 190, 81, 255)
SPECS = {
    "timber_hauler": {
        "size": (128, 108), "core": (96, 117), "core_r": (8, 12),
        "engines": [(73, 136), (96, 146), (118, 136)],
        "parts": [
            ([(29, 38), (68, 38), (79, 71), (72, 84), (52, 84), (28, 76)], (64, 77), "left"),
            ([(124, 38), (163, 38), (164, 76), (140, 84), (120, 84), (113, 71)], (128, 77), "right"),
        ],
        "angles": {0: (7, -7), 1: (4, -4), 2: (0, 0), 3: (-3, 3), 4: (7, 0), 5: (0, -7), 6: (0, 0)},
        "muzzles": [(62, 65), (130, 65)],
    },
    "thornlash": {
        "size": (126, 136), "core": (96, 65), "core_r": (8, 11),
        "engines": [(86, 134), (106, 134)],
        "parts": [
            ([(27, 65), (79, 65), (83, 94), (62, 112), (26, 111)], (76, 80), "front left leg"),
            ([(26, 111), (78, 108), (84, 133), (51, 174), (25, 170)], (75, 117), "rear left leg"),
            ([(113, 65), (165, 65), (166, 111), (130, 112), (109, 94)], (116, 80), "front right leg"),
            ([(108, 108), (166, 111), (167, 170), (141, 174), (108, 133)], (117, 117), "rear right leg"),
            ([(52, 19), (84, 19), (88, 68), (73, 74), (54, 68)], (82, 67), "left whip"),
            ([(108, 19), (140, 19), (138, 68), (119, 74), (104, 68)], (110, 67), "right whip"),
        ],
        "angles": {0: (5, -4, -5, 4, 8, -8), 1: (3, -2, -3, 2, 4, -4),
                   2: (0, 0, 0, 0, -2, 2), 3: (-3, 3, 3, -3, -4, 4),
                   4: (7, -6, 2, -2, 8, 2), 5: (2, -2, 7, -6, -2, -8),
                   6: (4, 1, -4, -1, 6, -6)},
        "muzzles": [(67, 68), (125, 68)],
    },
    "sporebloom": {
        "size": (112, 138), "core": (96, 77), "core_r": (19, 22),
        "engines": [(82, 158), (110, 158)],
        "parts": [
            ([(36, 26), (79, 27), (76, 72), (79, 125), (58, 139), (34, 116)], (77, 108), "left petal"),
            ([(113, 27), (156, 26), (158, 116), (134, 139), (113, 125), (116, 72)], (115, 108), "right petal"),
        ],
        "angles": {0: (7, -7), 1: (4, -4), 2: (0, 0), 3: (-3, 3), 4: (7, 0), 5: (0, -7), 6: (5, -5)},
        "muzzles": [(49, 89), (143, 89), (96, 68)],
    },
    "leafblade": {
        "size": (102, 142), "core": (96, 52), "core_r": (7, 10),
        "engines": [(87, 158), (105, 158)],
        "parts": [
            ([(38, 21), (75, 21), (84, 69), (82, 113), (53, 142), (38, 129)], (76, 105), "left blade"),
            ([(117, 21), (154, 21), (154, 129), (139, 142), (110, 113), (108, 69)], (116, 105), "right blade"),
        ],
        "angles": {0: (8, -8), 1: (5, -5), 2: (2, -2), 3: (-3, 3), 4: (8, -1), 5: (1, -8), 6: (5, -5)},
        "muzzles": [(96, 29), (50, 120), (142, 120)],
    },
}


def painted_master(name: str, size: tuple[int, int]) -> Image.Image:
    src = Image.open(SRC / f"{name}_master.png").convert("RGBA")
    a = src.getchannel("A").point(lambda n: 255 if n > 32 else 0)
    src = src.crop(a.getbbox()).resize(size, Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.alpha_composite(src, ((CELL - size[0]) // 2, (CELL - size[1]) // 2))
    # Remove fringe pixels, keep the source's natural soft alpha immediately at the edge.
    ar = np.array(canvas)
    ar[ar[:, :, 3] < 12] = 0
    # Enemy red is reserved for the player. Redirect any red pixels in the
    # generated painting into the local brass material hue, preserving value.
    for y, x in zip(*np.where(ar[:, :, 3] >= 32)):
        rr, gg, bb = (int(n) / 255 for n in ar[y, x, :3])
        h, s, v = colorsys.rgb_to_hsv(rr, gg, bb)
        if s > 0.5 and (h * 360 >= 345 or h * 360 <= 15):
            ar[y, x, :3] = [round(c * 255) for c in colorsys.hsv_to_rgb(34 / 360, min(s, .48), v)]
        elif s > .45 and 60 <= h * 360 <= 130:
            # The generated paintings leaned yellow-green. Move their saturated
            # greens into Verdant's emerald band; retain brass, moss and value.
            emerald_hue = 135 + (h * 360 - 60) * 20 / 70
            ar[y, x, :3] = [round(c * 255) for c in colorsys.hsv_to_rgb(emerald_hue / 360, s, v)]
    # A selective upper-left rim catches the silhouette against jungle floor.
    solid = ar[:, :, 3] > 190
    edge = solid & (~np.pad(solid[:, :-1], ((0, 0), (1, 0))) | ~np.pad(solid[:-1, :], ((1, 0), (0, 0))))
    inner_rim = solid & ~edge & (np.pad(edge[:, :-1], ((0, 0), (1, 0))) | np.pad(edge[:-1, :], ((1, 0), (0, 0))))
    ar[inner_rim, :3] = (ar[inner_rim, :3].astype(np.float32) * .78 + np.array((192, 228, 169)) * .22).astype(np.uint8)
    ar[edge, :3] = (ar[edge, :3].astype(np.float32) * .43 + np.array((192, 228, 169)) * .57).astype(np.uint8)
    return Image.fromarray(ar, "RGBA")


def concept_image(name: str) -> Image.Image:
    original = Image.open(SRC / f"{name}_master.png").convert("RGBA")
    hsv = np.array(original.convert("RGB").convert("HSV"))
    hue = hsv[:, :, 0].astype(np.float32) * 360 / 255
    sat = hsv[:, :, 1]
    visible = np.array(original.getchannel("A")) > 8
    # Include a small buffer around the forbidden band for HSV round-trips.
    red = visible & (sat > 114) & ((hue >= 335) | (hue <= 25))
    hsv[:, :, 0][red] = round(34 / 360 * 255)
    hsv[:, :, 1][red] = np.minimum(hsv[:, :, 1][red], 122)
    lime = visible & (sat > 114) & (hue >= 60) & (hue <= 130)
    hsv[:, :, 0][lime] = np.rint((135 + (hue[lime] - 60) * 20 / 70) / 360 * 255).astype(np.uint8)
    fixed = Image.fromarray(hsv, "HSV").convert("RGB").convert("RGBA")
    fixed.putalpha(original.getchannel("A"))
    return fixed


def articulated(base: Image.Image, spec: dict, frame: int) -> Image.Image:
    remaining = base.copy()
    moved = []
    for (poly, pivot, _), angle in zip(spec["parts"], spec["angles"][frame]):
        mask = Image.new("L", (CELL, CELL))
        ImageDraw.Draw(mask).polygon(poly, fill=255)
        part = base.copy()
        part.putalpha(Image.fromarray(np.minimum(np.array(base.getchannel("A")), np.array(mask)), "L"))
        ra = np.array(remaining)
        ra[np.array(mask) > 0, 3] = 0
        remaining = Image.fromarray(ra, "RGBA")
        moved.append(part.rotate(angle, resample=Image.Resampling.NEAREST, center=pivot))
    # Moving parts are behind the uncut central body; cuts occur in the hinge shadows.
    output = Image.new("RGBA", (CELL, CELL))
    for part in moved:
        output.alpha_composite(part)
    output.alpha_composite(remaining)
    if frame in (4, 5):
        output = output.rotate(3 if frame == 4 else -3, Image.Resampling.NEAREST, center=(96, 96))
    return output


def light_region(im: Image.Image, center: tuple[int, int], radii: tuple[int, int], factor: float) -> Image.Image:
    data = np.array(im)
    yy, xx = np.ogrid[:CELL, :CELL]
    rr = ((xx - center[0]) / radii[0]) ** 2 + ((yy - center[1]) / radii[1]) ** 2
    strength = np.clip(1 - rr, 0, 1)[..., None]
    rgb = data[:, :, :3].astype(np.float32)
    rgb *= 1 + (factor - 1) * strength
    data[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(data, "RGBA")


def engines(im: Image.Image, positions: list[tuple[int, int]], frame: int) -> Image.Image:
    result = im.copy()
    for x, y in positions:
        result = light_region(result, (x, y), (8, 9), {0: .24, 1: .43, 2: 1.2, 3: 1.0, 4: 1.0, 5: 1.0, 6: .7}[frame])
    if frame not in (2, 3, 4, 5):
        return result
    draw = ImageDraw.Draw(result)
    length = 15 if frame == 2 else 8
    sway = 2 if frame == 4 else -2 if frame == 5 else 0
    for x, y in positions:
        draw.polygon([(x - 4, y + 1), (x + 4, y + 1), (x + sway + 2, y + length - 3),
                      (x + sway, y + length), (x + sway - 2, y + length - 3)], fill=(28, 164, 91, 210))
        draw.polygon([(x - 2, y + 1), (x + 2, y + 1), (x + sway, y + length - 4)], fill=(231, 223, 137, 230))
        draw.line((x, y + 1, x + sway, y + length - 5), fill=PALE, width=1)
    return result


def damaged(im: Image.Image, name: str) -> Image.Image:
    out = im.copy()
    d = ImageDraw.Draw(out)
    locations = {"timber_hauler": [(70, 95), (82, 99), (89, 109)],
                 "thornlash": [(99, 75), (105, 85), (110, 93)],
                 "sporebloom": [(96, 68), (103, 80), (110, 85)],
                 "leafblade": [(97, 85), (102, 99), (107, 113)]}[name]
    d.line(locations, fill=INK, width=3, joint="curve")
    d.line(locations, fill=(71, 240, 147, 255), width=1, joint="curve")
    for i, (x, y) in enumerate(locations):
        d.line((x + 2, y - 2, x + 5 + i, y - 5), fill=AMBER, width=1)
        d.point((x + 7, y - 6), fill=PALE)
    return out


def flight_frames(name: str, spec: dict, base: Image.Image) -> list[Image.Image]:
    frames = []
    for i in range(7):
        im = articulated(base, spec, i)
        im = light_region(im, spec["core"], spec["core_r"], [.37, .65, 1.25, 1.0, 1.12, .9, .7][i])
        im = engines(im, spec["engines"], i)
        if i == 6:
            im = damaged(im, name)
        frames.append(im)
    return frames


def crack_and_sparks(im: Image.Image, center: tuple[int, int], frame: int) -> Image.Image:
    out = im.copy()
    d = ImageDraw.Draw(out)
    cx, cy = center
    if frame == 0:
        d.ellipse((cx - 14, cy - 14, cx + 14, cy + 14), fill=(49, 226, 121, 145))
        d.ellipse((cx - 7, cy - 7, cx + 7, cy + 7), fill=(218, 255, 194, 220))
    else:
        for i in range(11 if frame == 1 else 18):
            a = (i * 137.5 + 11) * math.pi / 180
            r = (12 + i % 4 * 4) if frame == 1 else (20 + i % 5 * 5)
            x = round(cx + math.cos(a) * r)
            y = round(cy + math.sin(a) * r)
            col = (84, 242, 150, 255) if i % 3 else AMBER
            d.line((x, y, x + round(math.cos(a) * 4), y + round(math.sin(a) * 4)), fill=col, width=1)
            d.point((x + 3, y - 2), fill=PALE)
    return out


def death_frames(base: Image.Image, spec: dict) -> list[Image.Image]:
    cx, cy = spec["core"]
    first = crack_and_sparks(light_region(base, (cx, cy), (29, 29), 1.75), (cx, cy), 0)
    frames = [first]
    for stage in (1, 2):
        out = Image.new("RGBA", (CELL, CELL))
        # Painted material planes fracture into independently drifting debris.
        offsets = [(-5, -5), (5, -5), (-5, 5), (5, 5)] if stage == 1 else [(-10, -10), (10, -10), (-10, 10), (10, 10)]
        if stage == 2:
            base_stage = base.resize((160, 160), Image.Resampling.NEAREST)
            t = Image.new("RGBA", (CELL, CELL))
            t.alpha_composite(base_stage, (16, 16))
        else:
            t = base
        for j, (dx, dy) in enumerate(offsets):
            x0 = 0 if j % 2 == 0 else 96
            y0 = 0 if j < 2 else 96
            frag = t.crop((x0, y0, x0 + 96, y0 + 96))
            frag = frag.rotate(((-1) ** j) * (3 if stage == 1 else 8), Image.Resampling.NEAREST)
            out.alpha_composite(frag, (x0 + dx, y0 + dy))
        # Scatter chips sampled from the painted hull: these keep the debris in
        # the ship's own bark, brass and emerald materials.
        for j in range(15 if stage == 1 else 25):
            theta = (j * 2.399963 + .35)
            sx = max(20, min(167, round(cx + math.cos(theta) * (12 + j % 6 * 4))))
            sy = max(20, min(167, round(cy + math.sin(theta) * (10 + j % 5 * 5))))
            chip = base.crop((sx - 3, sy - 3, sx + 4, sy + 4))
            drift = (9 + j % 5 * 3) if stage == 1 else (20 + j % 6 * 4)
            px = max(17, min(168, round(sx + math.cos(theta) * drift)))
            py = max(17, min(168, round(sy + math.sin(theta) * drift)))
            out.alpha_composite(chip.rotate(j * 17, Image.Resampling.NEAREST), (px, py))
        out = light_region(out, (cx, cy), (18, 18), 1.35 if stage == 1 else .75)
        out = crack_and_sparks(out, (cx, cy), stage)
        frames.append(out)
    return frames


def save_strip(frames: list[Image.Image], path: Path) -> None:
    strip = Image.new("RGBA", (CELL * len(frames), CELL))
    for i, frame in enumerate(frames):
        strip.alpha_composite(frame, (i * CELL, 0))
    strip.save(path)


def main() -> None:
    all_flight = []
    for name, spec in SPECS.items():
        key = "verdant_elite_" + name
        base = painted_master(name, spec["size"])
        flight = flight_frames(name, spec, base)
        save_strip(flight, ROOT / f"{key}.png")
        save_strip(death_frames(base, spec), ROOT / f"{key}_death.png")
        # Keep the full painted generation as the hero concept, with the same
        # Verdant hue correction used in the playable strip.
        concept_image(name).save(ROOT / f"{key}_concept.png")
        all_flight.append(flight)

    reference = Image.open(ROOT.parent.parent.parent.parent / "Resources/Elites/Verdant/verdant_elite_resin_warden.png").convert("RGBA")
    contact = Image.new("RGBA", (CELL * 7 * 2, CELL * 5 * 2))
    for row, flight in enumerate(all_flight):
        strip = Image.new("RGBA", (CELL * 7, CELL))
        for col, frame in enumerate(flight):
            strip.alpha_composite(frame, (col * CELL, 0))
        contact.alpha_composite(strip.resize((strip.width * 2, strip.height * 2), Image.Resampling.NEAREST), (0, row * CELL * 2))
    contact.alpha_composite(reference.resize((reference.width * 2, reference.height * 2), Image.Resampling.NEAREST), (0, CELL * 4 * 2))
    contact.save(ROOT / "contact_sheet.png")

    anim = []
    for frame in (3, 4, 3, 5):
        pane = Image.new("RGBA", (CELL * 2, CELL * 4 * 2))
        for row, flight in enumerate(all_flight):
            pane.alpha_composite(flight[frame].resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST), (0, row * CELL * 2))
        anim.append(pane)
    anim[0].save(ROOT / "preview.gif", save_all=True, append_images=anim[1:], duration=[200, 150, 200, 150], loop=0, disposal=2, transparency=0)


if __name__ == "__main__":
    main()
