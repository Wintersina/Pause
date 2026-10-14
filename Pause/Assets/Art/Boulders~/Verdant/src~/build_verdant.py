#!/usr/bin/env python3
"""Compose Verdant boulder cells from selected high-resolution imagegen paintings.

All painted material originates in the generated candidates. Destruction chunks
retain their source pixels; the script only cuts, moves, and lights their seams.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CELL = 768
SOURCES = ["stump_candidate_a.png", "root_candidate_a.png", "arch_candidate_b.png"]
WIDTHS = [666, 664, 668]
OUTLINE = np.array([228, 255, 232], np.uint8)
SAP = np.array([184, 255, 196], np.uint8)
CORE = np.array([239, 255, 240], np.uint8)


def erode(mask, n=1):
    result = mask.copy()
    for _ in range(n):
        old = result
        result = old.copy()
        result[1:] &= old[:-1]
        result[:-1] &= old[1:]
        result[:, 1:] &= old[:, :-1]
        result[:, :-1] &= old[:, 1:]
    return result


def opaque_bbox(mask):
    yy, xx = np.where(mask)
    return int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1


def prep(path, target_width, fill_hollow=False):
    raw = Image.open(HERE / path).convert("RGBA")
    data = np.array(raw)
    # The model leaves faint transparent fringe pixels; crop to actual paint.
    box = opaque_bbox(data[..., 3] > 128)
    raw = raw.crop(box)
    w, h = raw.size
    out_h = round(h * target_width / w)
    assert w >= target_width and h >= out_h, "never enlarge generated paint"
    raw = raw.resize((target_width, out_h), Image.Resampling.LANCZOS)
    art = np.array(raw)
    op = art[..., 3] >= 128
    # Preserve high-frequency paint and suppress the model's overly light tan.
    rgb = art[..., :3].astype(np.float32)
    rgb[..., 0] *= .70
    rgb[..., 1] *= .80
    rgb[..., 2] *= .76
    rgb = np.minimum(rgb, 158)
    art[..., :3] = rgb.astype(np.uint8)
    # Even near-black antialias remnants must obey the no-red rule.
    r = art[..., 0].astype(np.float32)
    g = art[..., 1].astype(np.float32)
    b = art[..., 2].astype(np.float32)
    redish = (r > g) & (r > b) & (np.abs(g - b) <= .26 * (r - np.minimum(g, b)))
    art[..., 1][redish] = np.maximum(g[redish], np.ceil(r[redish] * .78)).astype(np.uint8)
    art[..., 3] = op.astype(np.uint8) * 255
    cell = np.zeros((CELL, CELL, 4), np.uint8)
    x = (CELL - target_width) // 2
    y = (CELL - out_h) // 2
    cell[y:y + out_h, x:x + target_width] = art
    if fill_hollow:
        # A transparent tunnel would contradict this object's solid cover role.
        # Use the model-painted roof grain as a dim recess behind the arch.
        cavity_image = Image.new("L", (CELL, CELL))
        cavity_draw = ImageDraw.Draw(cavity_image)
        cavity_draw.polygon([(223, 448), (245, 394), (291, 356), (356, 343),
                             (428, 351), (486, 379), (541, 440), (522, 495),
                             (477, 532), (407, 544), (325, 532), (255, 501)], fill=255)
        cavity = (np.array(cavity_image) > 0) & (cell[..., 3] == 0)
        yy, xx = np.indices((CELL, CELL))
        donor = cell[np.clip(yy - 125, 0, CELL - 1), xx, :3].astype(np.float32)
        donor_alt = cell[np.clip(yy - 180, 0, CELL - 1), xx, :3].astype(np.float32)
        donor = np.where((donor.sum(axis=2) < 12)[..., None], donor_alt, donor)
        recess = np.clip(donor * .42 + np.array([6, 12, 8]), 0, 95).astype(np.uint8)
        cell[cavity, :3] = recess[cavity]
        cell[cavity, 3] = 255
    # The outermost three actual cell pixels form one continuous pale contour.
    mask = cell[..., 3] == 255
    rim = mask & ~erode(mask, 3)
    cell[rim, :3] = OUTLINE
    return cell


def crack_paths(v):
    if v == 0:
        return [[(236, 279), (310, 327), (366, 384), (428, 431), (496, 486)],
                [(375, 241), (390, 302), (366, 384), (341, 453), (294, 510)],
                [(527, 292), (460, 344), (428, 431), (458, 526)]]
    if v == 1:
        return [[(233, 262), (297, 340), (351, 386), (420, 436), (498, 504)],
                [(424, 239), (403, 303), (351, 386), (321, 481), (274, 527)],
                [(533, 337), (466, 365), (420, 436), (431, 511)]]
    return [[(228, 301), (304, 334), (362, 360), (449, 376), (529, 431)],
            [(375, 260), (369, 313), (362, 360), (321, 401), (264, 452)],
            [(510, 304), (449, 376), (479, 428), (522, 454)]]


def light_cracks(base, v, deep=False):
    a = base.copy()
    mask = a[..., 3] == 255
    canvas = Image.new("L", (CELL, CELL))
    draw = ImageDraw.Draw(canvas)
    for path in crack_paths(v):
        draw.line(path, fill=255, width=7 if deep else 3, joint="curve")
    lit = (np.array(canvas) > 0) & erode(mask, 5)
    a[lit, :3] = SAP if deep else np.array([112, 199, 129], np.uint8)
    core_canvas = Image.new("L", (CELL, CELL))
    core_draw = ImageDraw.Draw(core_canvas)
    for path in crack_paths(v):
        core_draw.line(path, fill=255, width=2 if deep else 1, joint="curve")
    core = (np.array(core_canvas) > 0) & erode(mask, 5)
    a[core, :3] = CORE if deep else SAP
    return a


SEEDS = [(202, 309), (329, 281), (463, 275), (563, 355),
         (481, 467), (358, 491), (222, 455)]


def pieces(base, v):
    yy, xx = np.mgrid[:CELL, :CELL]
    seeds = [(x + (v - 1) * 8, y + ((v + i) % 3 - 1) * 7)
             for i, (x, y) in enumerate(SEEDS)]
    # Distort the partition space so fractures follow knotted wood grain.
    wx = xx + 21 * np.sin(yy * .028 + v) + 9 * np.sin(yy * .086 + xx * .012)
    wy = yy + 17 * np.sin(xx * .026 - v) + 7 * np.sin(xx * .092 - yy * .014)
    ds = np.stack([((wx - x) * .85) ** 2 + ((wy - y) * 1.15) ** 2
                   for x, y in seeds])
    ids = np.argmin(ds, axis=0)
    solid = base[..., 3] == 255
    return [solid & (ids == i) for i in range(7)]


def shift_into(dst, src, mask, dx, dy, rim=False):
    region = np.zeros_like(src)
    region[mask] = src[mask]
    if rim:
        edge = mask & ~erode(mask, 3)
        yy, xx = np.indices(mask.shape)
        core = (mask & ~erode(mask, 1)) & ((xx * 7 + yy * 11) % 5 == 0)
        region[edge, :3] = SAP
        region[core, :3] = CORE
    x0, x1 = max(0, dx), min(CELL, CELL + dx)
    y0, y1 = max(0, dy), min(CELL, CELL + dy)
    sx0, sx1 = max(0, -dx), min(CELL, CELL - dx)
    sy0, sy1 = max(0, -dy), min(CELL, CELL - dy)
    block = region[sy0:sy1, sx0:sx1]
    target = dst[y0:y1, x0:x1]
    target[block[..., 3] > 0] = block[block[..., 3] > 0]


def add_chips(dst, source, seed, count, max_span, spread):
    rng = np.random.default_rng(seed)
    solid = source[..., 3] == 255
    ys, xs = np.where(erode(solid, 8))
    if len(xs) == 0:
        return
    for _ in range(count):
        j = int(rng.integers(0, len(xs)))
        sx, sy = int(xs[j]), int(ys[j])
        w = int(rng.integers(max(5, max_span // 3), max_span + 1))
        h = int(rng.integers(max(5, max_span // 4), max_span // 2 + 1))
        x0, y0 = max(0, sx - w // 2), max(0, sy - h // 2)
        x1, y1 = min(CELL, x0 + w), min(CELL, y0 + h)
        chip = source[y0:y1, x0:x1].copy()
        ch, cw = chip.shape[:2]
        # Pixel-sharp tapered bark chips cut from the same painted surface.
        yy, xx = np.mgrid[:ch, :cw]
        taper = np.abs(yy - ch / 2) / max(1, ch / 2) + .30 * np.abs(xx - cw / 2) / max(1, cw / 2)
        chip[taper > 1, 3] = 0
        tx = int(np.clip(sx + rng.integers(-spread, spread + 1) - cw // 2, 4, CELL - cw - 4))
        ty = int(np.clip(sy + rng.integers(-spread, spread + 1) - ch // 2, 4, CELL - ch - 4))
        block = dst[ty:ty + ch, tx:tx + cw]
        present = chip[..., 3] > 0
        block[present] = chip[present]


def add_organic_debris(dst, seed, leaves, spores, spread):
    rng = np.random.default_rng(seed)
    overlay = Image.new("RGBA", (CELL, CELL))
    draw = ImageDraw.Draw(overlay)
    for _ in range(leaves):
        x = int(np.clip(384 + rng.integers(-spread, spread + 1), 10, CELL - 10))
        y = int(np.clip(384 + rng.integers(-spread, spread + 1), 10, CELL - 10))
        length = int(rng.integers(9, 23))
        angle = rng.uniform(0, 2 * np.pi)
        u = np.array([np.cos(angle), np.sin(angle)])
        n = np.array([-u[1], u[0]])
        center = np.array([x, y])
        tip = center + u * length * .7
        heel = center - u * length * .4
        wide = int(rng.integers(3, 7))
        poly = [tuple(np.rint(heel).astype(int)),
                tuple(np.rint(center + n * wide).astype(int)),
                tuple(np.rint(tip).astype(int)),
                tuple(np.rint(center - n * wide).astype(int))]
        draw.polygon(poly, fill=(54, 100, 46, 255))
        draw.line([tuple(np.rint(heel).astype(int)), tuple(np.rint(tip).astype(int))],
                  fill=(112, 164, 89, 255), width=1)
    for _ in range(spores):
        x = int(np.clip(384 + rng.integers(-spread, spread + 1), 10, CELL - 10))
        y = int(np.clip(384 + rng.integers(-spread, spread + 1), 10, CELL - 10))
        for dx, dy, radius in ((0, 0, 3), (-7, 3, 2), (5, -5, 2), (8, 5, 1)):
            draw.ellipse((x + dx - radius, y + dy - radius,
                          x + dx + radius, y + dy + radius),
                         fill=(153, 204, 163, 255))
    layer = np.array(overlay)
    present = layer[..., 3] > 0
    dst[present] = layer[present]


def sap_burst(dst, v):
    overlay = Image.new("RGBA", (CELL, CELL))
    draw = ImageDraw.Draw(overlay)
    for path in crack_paths(v):
        draw.line(path, fill=(92, 192, 107, 255), width=5, joint="curve")
        draw.line(path, fill=(184, 255, 196, 255), width=2, joint="curve")
    layer = np.array(overlay)
    present = layer[..., 3] > 0
    dst[present] = layer[present]


def assemble_break(base, v):
    frames = [light_cracks(base, v)]
    parts = pieces(base, v)
    center = np.array([384, 384])
    seeds = np.array(SEEDS)
    vectors = seeds - center
    lengths = np.sqrt((vectors ** 2).sum(axis=1))[:, None]
    vectors = vectors / np.maximum(lengths, 1)
    for stage in (1, 2, 3):
        out = np.zeros_like(base)
        if stage == 2:
            sap_burst(out, v)
        for i, p in enumerate(parts):
            distance = [0, 5, 44, 82][stage] + (i % 3 - 1) * [0, 2, 8, 13][stage]
            dx, dy = np.rint(vectors[i] * distance).astype(int)
            if stage == 3:
                yy, xx = np.indices(p.shape)
                bend = xx + .4 * yy + 18 * np.sin(yy * .034 + i)
                pivot = SEEDS[i][0] + .4 * SEEDS[i][1]
                half_a = erode(p & (bend < pivot - 3), 2)
                half_b = erode(p & (bend > pivot + 3), 2)
                fragments = [(half_a, -7, -8), (half_b, 8, 7)]
            else:
                fragments = [(p, 0, 0)]
            for fragment, extra_x, extra_y in fragments:
                if not fragment.any():
                    continue
                x0, y0, x1, y1 = opaque_bbox(fragment)
                fx = int(np.clip(dx + extra_x, 4 - x0, CELL - 4 - x1))
                fy = int(np.clip(dy + extra_y, 4 - y0, CELL - 4 - y1))
                shift_into(out, base, fragment, fx, fy, rim=stage >= 2)
        if stage == 1:
            out = light_cracks(out, v, deep=True)
            add_chips(out, base, 100 + v, 10, 16, 12)
        elif stage == 2:
            add_chips(out, base, 200 + v, 42, 19, 76)
            add_organic_debris(out, 250 + v, 8, 7, 170)
        else:
            add_chips(out, base, 300 + v, 90, 22, 125)
            add_organic_debris(out, 350 + v, 21, 15, 235)
        frames.append(out)
    last = np.zeros_like(base)
    add_chips(last, base, 400 + v, 180, 31, 205)
    add_organic_debris(last, 450 + v, 38, 25, 280)
    frames.append(last)
    return frames


def main():
    intact = Image.new("RGBA", (CELL * 3, CELL))
    breaking = Image.new("RGBA", (CELL * 5, CELL * 3))
    for v, (name, width) in enumerate(zip(SOURCES, WIDTHS)):
        base = prep(name, width, fill_hollow=v == 2)
        intact.paste(Image.fromarray(base), (v * CELL, 0))
        frames = assemble_break(base, v)
        for f, frame in enumerate(frames):
            breaking.paste(Image.fromarray(frame), (f * CELL, v * CELL))
        op = base[..., 3] > 0
        box = opaque_bbox(op)
        print(v, name, "bbox", box, "size", (box[2] - box[0], box[3] - box[1]),
              "areas", [round((f[..., 3] > 0).sum() / op.sum(), 3) for f in frames])
    intact.save(OUT / "boulder.png")
    breaking.save(OUT / "break.png")


if __name__ == "__main__":
    main()
