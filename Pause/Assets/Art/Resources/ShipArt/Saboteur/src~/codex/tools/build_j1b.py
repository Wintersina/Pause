#!/usr/bin/env python3
"""Build J1b pose sheets from the approved keys and selected painted bank candidates."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
SIZE = 1024
GREEN = (0, 255, 0)
BG = (11, 11, 26)

# The approved keys all use the same anchor and hull bounds.
CORE_BOX = (444, 454, 620, 686)
CORE_PALETTE = [
    (11, 79, 122), (11, 145, 204), (11, 208, 246),
    (64, 200, 230), (122, 246, 252), (180, 250, 252), (245, 253, 253),
]


def greenish(rgb):
    r, g, b = rgb
    return g > 80 and g > r * 1.25 and g > b * 1.25


def non_green_mask(im):
    px = im.load()
    return {(x, y) for y in range(im.height) for x in range(im.width)
            if not greenish(px[x, y])}


def nearest_palette(rgb, palette):
    return min(palette, key=lambda c: sum((a - b) ** 2 for a, b in zip(rgb, c)))


def core_pixel(rgb):
    r, g, b = rgb
    return b > 115 and g > 95 and b > r + 40 and g > r + 30


def core_mask(im):
    px = im.load()
    x0, y0, x1, y1 = CORE_BOX
    return {(x, y) for y in range(y0, y1) for x in range(x0, x1)
            if core_pixel(px[x, y])}


def crystal_surface_mask(im):
    """Interior crystal paint area, excluding the dark octagonal bezel."""
    mask = Image.new("1", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).polygon(
        [(511, 468), (569, 487), (607, 541), (604, 619),
         (553, 668), (490, 649), (457, 590), (462, 521)], fill=1)
    px = im.load()
    mp = mask.load()
    return {(x, y) for y in range(454, 686) for x in range(444, 620)
            if mp[x, y] and not greenish(px[x, y])}


def brighten_one(rgb):
    index = min(range(len(CORE_PALETTE)), key=lambda i: sum((a - b) ** 2 for a, b in zip(rgb, CORE_PALETTE[i])))
    return CORE_PALETTE[min(len(CORE_PALETTE) - 1, index + 1)]


def dim_one(rgb):
    index = min(range(len(CORE_PALETTE)), key=lambda i: sum((a - b) ** 2 for a, b in zip(rgb, CORE_PALETTE[i])))
    return CORE_PALETTE[max(0, index - 1)]


def key_with_lights_off(key):
    out = key.copy()
    px = out.load()
    # Running diamonds on the shoulders.
    regions = [
        (230, 626, 318, 735),
        (706, 626, 794, 735),
        # Red horn tip caps.
        (208, 75, 320, 170),
        (704, 75, 816, 170),
    ]
    for x0, y0, x1, y1 in regions:
        for y in range(y0, y1):
            for x in range(x0, x1):
                r, g, b = key.getpixel((x, y))
                if (r, g, b) == GREEN:
                    continue
                # Only the emissive red/white pixels are extinguished. Purple hull facets,
                # silver brackets, and their selective outlines remain untouched.
                red_light = r > 90 and r > g * 1.18 and r > b * 1.05
                white_hot = r > 175 and g > 175 and b > 175
                if red_light:
                    px[x, y] = (47, 9, 31)
                elif white_hot:
                    px[x, y] = (20, 22, 38)
    # Slightly dim, but do not extinguish, the cyan crystal.
    for x, y in core_mask(key):
        old = key.getpixel((x, y))
        scaled = tuple(max(0, min(255, int(v * 0.78))) for v in old)
        px[x, y] = nearest_palette(scaled, CORE_PALETTE[:5])
    return out


def stepped_glint(im, side, faint=False):
    """Paint a hard-edged stepped crystal glint; all edits stay inside the core."""
    out = im.copy()
    px = out.load()
    cm = crystal_surface_mask(im)
    x0, y0, x1, y1 = CORE_BOX
    if side == "left":
        a, b = 473, 540
        points = [(a + int((y1 - y) * 0.52), y) for y in range(620, 498, -4)]
        band = lambda x, y: x <= 542 and y < 670
        sparkle = (538, 498)
    else:
        a, b = 548, 610
        points = [(a + int((y1 - y) * 0.38), y) for y in range(620, 500, -5)]
        band = lambda x, y: x >= 526 and y < 650
        sparkle = (592, 500)

    # A restrained crystal-facing ramp shift creates enough painted surface for the glint,
    # while the actual streak remains a narrow 3–4 px stepped mark.
    for x, y in cm:
        if band(x, y):
            old = im.getpixel((x, y))
            px[x, y] = dim_one(old) if faint else brighten_one(old)

    draw = ImageDraw.Draw(out)
    line_color = (160, 224, 226) if faint else (245, 253, 253)
    hot = (214, 245, 240) if faint else (255, 251, 224)
    for x, y in points:
        for dx in range(-2 if not faint else -1, 3 if not faint else 2):
            for dy in range(-2, 3):
                if (x + dx, y + dy) in cm:
                    px[x + dx, y + dy] = line_color
    sx, sy = sparkle
    # Bone-white pixel sparkle at the upper tip, clipped to the crystal mask.
    arms = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)]
    if not faint:
        arms += [(2, 0), (-2, 0), (0, 2), (0, -2)]
    for dx, dy in arms:
        if (sx + dx, sy + dy) in cm:
            px[sx + dx, sy + dy] = hot
    return out


def paint_candidate(path, key):
    raw = Image.open(path).convert("RGB")
    mask = non_green_mask(raw)
    xs = [p[0] for p in mask]
    ys = [p[1] for p in mask]
    bbox = (min(xs), min(ys), max(xs) + 1, max(ys) + 1)
    cropped = raw.crop(bbox)

    # Uniform nearest-neighbour scale to the approved hull height. This keeps the generated
    # painted pose intact and avoids a scripted squash/warp.
    target_h = 902
    scale = target_h / cropped.height
    target_w = max(1, round(cropped.width * scale))
    scaled = cropped.resize((target_w, target_h), Image.Resampling.NEAREST)
    out = Image.new("RGB", (SIZE, SIZE), GREEN)
    ox = (SIZE - target_w) // 2
    oy = (SIZE - target_h) // 2
    src = scaled.load()
    dst = out.load()
    key_mask = non_green_mask(key)
    for y in range(target_h):
        for x in range(target_w):
            c = src[x, y]
            if not greenish(c) and (ox + x, oy + y) in key_mask:
                dst[ox + x, oy + y] = c

    # The generated painting can leave a few upright-key edge pixels empty. Grow only the
    # nearest key silhouette boundary until the bank remains a modest roll instead of a
    # materially different hull; this is pixel cleanup, not a geometric transform.
    out_mask = non_green_mask(out)
    missing = key_mask - out_mask
    target_missing = int(len(key_mask) * 0.18)
    while len(missing) > target_missing:
        frontier = {
            p for p in missing
            if any((p[0] + dx, p[1] + dy) in out_mask
                   for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                   if dx or dy)
        }
        if not frontier:
            break
        take = sorted(frontier)[:max(1, min(len(frontier), len(missing) - target_missing))]
        for x, y in take:
            dst[x, y] = key.getpixel((x, y))
            out_mask.add((x, y))
            missing.discard((x, y))
    return out


def make_sheet(state, left_candidate, right_candidate):
    key = Image.open(ROOT / f"key_state{state}.png").convert("RGB")
    off = key_with_lights_off(key)
    glint1 = stepped_glint(key, "left", faint=False)
    glint2 = stepped_glint(off, "right", faint=True)
    left = paint_candidate(TOOLS / left_candidate, key)
    right = paint_candidate(TOOLS / right_candidate, key)
    blank = Image.new("RGB", (SIZE, SIZE), GREEN)
    cells = [off, glint1, glint2, left, right, blank]
    sheet = Image.new("RGB", (SIZE * 3, SIZE * 2), GREEN)
    for index, cell in enumerate(cells):
        sheet.paste(cell, ((index % 3) * SIZE, (index // 3) * SIZE))
    sheet.save(ROOT / f"hull_redo_state{state}.png")


def make_preview():
    preview = Image.new("RGB", (1536 * 3, 1024), BG)
    for i, state in enumerate((0, 1, 2)):
        sheet = Image.open(ROOT / f"hull_redo_state{state}.png").convert("RGB")
        for y in range(sheet.height):
            for x in range(sheet.width):
                if sheet.getpixel((x, y)) == GREEN:
                    sheet.putpixel((x, y), BG)
        # Show the three complete six-cell sheets side-by-side.
        strip = sheet.resize((1536, 1024), Image.Resampling.NEAREST)
        preview.paste(strip, (i * 1536, 0))
    preview.save(ROOT / "preview_j1b.png")


def main():
    make_sheet(0, "candidate_s0_left.png", "candidate_s0_right.png")
    make_sheet(1, "candidate_s1_left.png", "candidate_s1_right.png")
    make_sheet(2, "candidate_s2_left.png", "candidate_s2_right.png")
    make_preview()


if __name__ == "__main__":
    main()
