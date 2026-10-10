"""Assemble the selected image-generated Verdant damage and FX candidates.

Run from the repository root with `python3 Pause/Assets/Art/Bosses/VerdantDamage~/src~/build.py`.
All resampling is nearest-neighbour; the input candidate PNGs are retained here.
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
ROOT = HERE.parents[5]
CELL = 384
BG = (11, 11, 26, 255)
NEAREST = Image.Resampling.NEAREST
CHOSEN = ["candidate_1_2.png", "candidate_2_2.png", "candidate_3_1.png", "candidate_4_1.png"]


def alpha_bbox(im, threshold=16):
    return im.getchannel("A").point(lambda v: 255 if v >= threshold else 0).getbbox()


def clean_rgba(im, alpha_floor=17):
    arr = np.array(im.convert("RGBA"), dtype=np.uint8)
    arr[arr[:, :, 3] < alpha_floor] = 0
    arr[:, :, 3] = np.where(arr[:, :, 3] > 0, 255, 0).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def remove_red_band(im):
    """Move saturated 345-15 degree pixels to brass orange or thorn magenta."""
    arr = np.array(im.convert("RGBA"), dtype=np.uint8)
    rgb = arr[:, :, :3].astype(np.float32) / 255
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    delta = mx - mn
    hue = np.zeros_like(mx)
    sel = (delta > 0) & (mx == r)
    hue[sel] = ((g[sel] - b[sel]) / delta[sel]) % 6
    sel = (delta > 0) & (mx == g)
    hue[sel] = (b[sel] - r[sel]) / delta[sel] + 2
    sel = (delta > 0) & (mx == b)
    hue[sel] = (r[sel] - g[sel]) / delta[sel] + 4
    hue = (hue * 60) % 360
    red = (arr[:, :, 3] > 0) & (sat > 0.5) & ((hue >= 343) | (hue <= 17))
    chroma = mx * sat
    low = mx - chroma
    orange = red & (g >= b)
    magenta = red & ~orange
    # HSV 35 degrees / 310 degrees, preserving candidate value and saturation.
    # These targets stay clear of the red band after 8-bit rounding.
    arr[:, :, 0][orange] = np.round(mx[orange] * 255).astype(np.uint8)
    arr[:, :, 1][orange] = np.round((low[orange] + chroma[orange] * (35 / 60)) * 255).astype(np.uint8)
    arr[:, :, 2][orange] = np.round(low[orange] * 255).astype(np.uint8)
    arr[:, :, 0][magenta] = np.round(mx[magenta] * 255).astype(np.uint8)
    arr[:, :, 1][magenta] = np.round(low[magenta] * 255).astype(np.uint8)
    arr[:, :, 2][magenta] = np.round((low[magenta] + chroma[magenta] * (50 / 60)) * 255).astype(np.uint8)
    near_black = red & (mx * 255 < 16)
    arr[:, :, 0][near_black] = 5
    arr[:, :, 1][near_black] = 6
    arr[:, :, 2][near_black] = 12
    return Image.fromarray(arr, "RGBA")


def register_body(name):
    im = Image.open(HERE / name).convert("RGBA").resize((CELL, CELL), NEAREST)
    im = clean_rgba(im)
    box = alpha_bbox(im)
    # Each generated edit shifts the outer tips slightly. Register the visible
    # painted body to the original's solid-alpha envelope, leaving actual holes.
    target = (25, 48, 355, 375)
    crop = im.crop(box).resize((target[2] - target[0], target[3] - target[1]), NEAREST)
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.alpha_composite(crop, target[:2])
    return remove_red_band(canvas)


def flicker(body, stage):
    arr = np.array(body, dtype=np.uint8)
    yy, xx = np.indices((CELL, CELL))
    r, g, b, a = [arr[:, :, i] for i in range(4)]
    core = ((xx - 190) / 53) ** 2 + ((yy - 194) / 75) ** 2 < 1
    lit = core & (a > 0) & (g > r * 1.30) & (g > b * 1.15) & (g > 76)
    # A small breath in the unstable core; strongest in the last row.
    factor = [0.83, 0.77, 0.66, 0.54][stage]
    for ch in (0, 1, 2):
        arr[:, :, ch][lit] = np.clip(arr[:, :, ch][lit] * factor, 0, 255).astype(np.uint8)
    result = Image.fromarray(arr, "RGBA")
    d = ImageDraw.Draw(result)
    # The hanging sap moves by two pixels between A and B.
    sx, sy = [(108, 290), (112, 315), (102, 327), (270, 316)][stage]
    d.line([(sx, sy), (sx - 1, sy + 4 + stage)], fill=(91, 166, 12, 255), width=1)
    d.rectangle((sx - 2, sy + 5 + stage, sx, sy + 6 + stage), fill=(182, 238, 37, 255))
    return result


def loose_shard(body, stage, offset):
    result = body.copy()
    x, y = [(105, 169), (104, 170), (303, 179), (306, 174)][stage]
    x += offset
    d = ImageDraw.Draw(result)
    d.polygon([(x, y), (x + 5, y + 2), (x + 2, y + 6)], fill=(31, 63, 4, 255))
    d.line([(x, y), (x + 5, y + 2)], fill=(160, 111, 49, 255), width=1)
    return result


def smoke_source(index):
    im = Image.open(HERE / f"fx_candidate_{index}.png").convert("RGBA").resize((CELL, CELL), NEAREST)
    im = clean_rgba(im, 30)
    arr = np.array(im, dtype=np.uint8)
    yy, xx = np.indices((CELL, CELL))
    # Preserve the generated puffs but open a generous sightline to the core.
    core = ((xx - 190) / 57) ** 2 + ((yy - 202) / 75) ** 2 < 1
    arr[:, :, 3][core] = 0
    # Keep the emissions near the existing shoulders, never over the lower body.
    arr[:, :, 3][yy > 315] = 0
    return Image.fromarray(arr, "RGBA")


def shift_image(im, dx, dy):
    out = Image.new("RGBA", (CELL, CELL))
    out.alpha_composite(im, (dx, dy))
    return out


def mix_rgba(a, b, weight):
    aa = np.array(a, dtype=np.float32)
    bb = np.array(b, dtype=np.float32)
    # Straight-alpha crossfade for the puffs; keep generated pixel contours.
    cc = aa * (1 - weight) + bb * weight
    cc[:, :, 3] = np.maximum(aa[:, :, 3] * (1 - weight), bb[:, :, 3] * weight)
    cc[:, :, 3] = np.where(cc[:, :, 3] > 24, cc[:, :, 3], 0)
    return Image.fromarray(np.clip(cc, 0, 255).astype(np.uint8), "RGBA")


def smoke_frames():
    a, b = smoke_source(1), smoke_source(2)
    weights = [0.0, 0.22, 0.58, 0.80, 0.52, 0.12]
    lifts = [0, -2, -4, -5, -3, -1]
    frames = []
    for i, (w, lift) in enumerate(zip(weights, lifts)):
        fx = mix_rgba(shift_image(a, 0, lift), shift_image(b, 0, lift - 1), w)
        draw = ImageDraw.Draw(fx)
        for sx, sy in [(111, 158), (279, 174)]:
            sx += (i % 3) - 1
            draw.ellipse((sx - 3, sy - 3, sx + 3, sy + 3), fill=(49, 169, 75, 230))
            draw.rectangle((sx - 1, sy - 1, sx + 1, sy + 1), fill=(170, 240, 153, 255))
        edge = np.array(fx, dtype=np.uint8)
        edge[:3, :, 3] = 0
        edge[-3:, :, 3] = 0
        edge[:, :3, 3] = 0
        edge[:, -3:, 3] = 0
        fx = Image.fromarray(edge, "RGBA")
        frames.append(remove_red_band(fx))
    return frames


def electrical_source(index):
    im = Image.open(HERE / f"fx_candidate_{index}.png").convert("RGBA").resize((CELL, CELL), NEAREST)
    arr = np.array(im, dtype=np.uint8)
    r, g, b, a = [arr[:, :, i].astype(np.float32) for i in range(4)]
    # Select only the hot centers of image-generated discharges. Candidate 4
    # contains a ghost of the hull, so use spark regions near the shoulders.
    emerald = (g > 205) & (g > r * 1.32) & (g > b * 1.14)
    white = (r > 182) & (g > 210) & (b > 150)
    active = (a > 160) & (emerald | white)
    yy, xx = np.indices((CELL, CELL))
    left = ((xx - 103) / 39) ** 2 + ((yy - 154) / 55) ** 2 < 1
    right = ((xx - 280) / 43) ** 2 + ((yy - 154) / 57) ** 2 < 1
    crown = ((xx - 192) / 53) ** 2 + ((yy - 87) / 24) ** 2 < 1
    active &= left | right | crown
    active &= yy < 230
    # Broad luminous hull patches are not lightning. Retain only the thin
    # image-generated filaments and star edges (5x5 local occupancy test).
    padded = np.pad(active.astype(np.uint8), 2)
    density = sum(padded[dy:dy + CELL, dx:dx + CELL] for dy in range(5) for dx in range(5))
    active &= density <= 12
    mask = Image.fromarray(np.where(active, 255, 0).astype(np.uint8), "L")
    # Arc fringe and bright inner pixels are drawn from the generated contour.
    fringe = mask.filter(ImageFilter.MaxFilter(3))
    out = Image.new("RGBA", (CELL, CELL))
    out.paste((20, 120, 72, 85), (0, 0, CELL, CELL), fringe)
    out.paste((184, 255, 199, 255), (0, 0, CELL, CELL), mask)
    return out


def electrical_frames():
    a, b = electrical_source(3), electrical_source(4)
    # Distinct, erratic discharge routes supplement the selected generated arcs.
    routes = [
        [[(77, 169), (100, 153), (95, 127), (138, 111), (145, 81)], [(290, 182), (265, 160), (288, 125), (252, 103)]],
        [[(97, 226), (123, 193), (148, 200), (164, 153)], [(279, 174), (307, 145), (275, 126), (283, 79)]],
        [[(101, 127), (129, 108), (145, 141), (173, 128)], [(291, 239), (266, 206), (291, 183), (272, 144)]],
        [[(80, 206), (116, 175), (103, 139), (130, 105), (148, 72)], [(277, 193), (245, 163), (272, 133), (249, 95)]],
        [[(107, 262), (125, 217), (106, 183), (141, 164)], [(281, 136), (255, 124), (276, 95), (249, 73)]],
        [[(85, 174), (105, 145), (103, 127), (137, 108)], [(293, 192), (270, 168), (288, 135), (257, 101)]],
    ]
    frames = []
    for i in range(6):
        base = a if i % 2 == 0 else b
        fx = shift_image(base, [0, 2, -2, 1, -1, 0][i], [0, -1, 1, 0, -2, 0][i])
        d = ImageDraw.Draw(fx)
        for route in routes[i]:
            d.line(route, fill=(26, 135, 84, 180), width=5, joint="curve")
            d.line(route, fill=(175, 255, 205, 255), width=2, joint="curve")
            d.point(route[1:-1], fill=(250, 255, 240, 255))
        for x, y in ([(107, 151), (277, 146)] if i in (1, 4) else [(108 + (i * 13) % 18, 165), (272 - (i * 9) % 20, 133)]):
            rad = 10 if i in (1, 4) else 6
            d.line((x - rad, y, x + rad, y), fill=(205, 255, 215, 255), width=2)
            d.line((x, y - rad, x, y + rad), fill=(205, 255, 215, 255), width=2)
            d.point((x, y), fill=(255, 255, 244, 255))
        frames.append(fx)
    return frames


def make_sheet(frames, cols, rows):
    im = Image.new("RGBA", (CELL * cols, CELL * rows))
    for i, tile in enumerate(frames):
        im.alpha_composite(tile, ((i % cols) * CELL, (i // cols) * CELL))
    return im


def preview(pristine, damage, smoke, arcs):
    labels = ["PRISTINE", "4 HEARTS", "3 HEARTS", "2 HEARTS", "1 HEART"]
    bg = Image.new("RGBA", (CELL * 5, CELL + 30), BG)
    draw = ImageDraw.Draw(bg)
    for i in range(5):
        tile = pristine if i == 0 else damage[(i - 1) * 2]
        if i:
            tile = Image.alpha_composite(tile, smoke[(i + 1) % 6])
            tile = Image.alpha_composite(tile, arcs[(i * 2) % 6])
        bg.alpha_composite(tile, (i * CELL, 30))
        draw.text((i * CELL + 12, 9), labels[i], fill=(205, 225, 207, 255))
    bg.resize((bg.width * 2, bg.height * 2), NEAREST).save(OUT / "preview.png")
    gif = []
    for i in range(6):
        frame = Image.new("RGBA", (CELL, CELL), BG)
        frame.alpha_composite(damage[6 + i % 2])
        frame.alpha_composite(smoke[i])
        frame.alpha_composite(arcs[i])
        gif.append(frame.resize((CELL * 2, CELL * 2), NEAREST).convert("RGB"))
    gif[0].save(OUT / "preview.gif", save_all=True, append_images=gif[1:], duration=110, loop=0, disposal=2, optimize=False)


def main():
    atlas = Image.open(ROOT / "Pause/Assets/Art/Resources/Bosses/Verdant.png").convert("RGBA")
    pristine = atlas.crop((0, 0, CELL, CELL))
    bodies = [register_body(name) for name in CHOSEN]
    damage = [cell for stage, body in enumerate(bodies)
              for cell in (loose_shard(body, stage, 0), flicker(loose_shard(body, stage, 1), stage))]
    smoke = smoke_frames()
    arcs = electrical_frames()
    make_sheet(damage, 2, 4).save(OUT / "Verdant_damage.png")
    make_sheet(smoke + arcs, 6, 2).save(OUT / "Verdant_damage_fx.png")
    preview(pristine, damage, smoke, arcs)
    print("wrote Verdant_damage.png, Verdant_damage_fx.png, preview.png, preview.gif")


if __name__ == "__main__":
    main()
