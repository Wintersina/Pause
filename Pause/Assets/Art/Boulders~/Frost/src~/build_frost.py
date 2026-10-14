"""Compose Frost cover-boulder art from the selected high-resolution imagegen paintings.

All painted pixels originate in the 1254 px generated candidates. They are
downsampled once with Lanczos to the 768 px delivery cells. Break chunks are
integer-translated cuts of those cells; no painted content is enlarged.
"""
from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 768
SELECTED = [
    ("mountain_candidate_1.png", (690, 580)),
    ("slab_candidate_1.png", (620, 690)),
    ("arch_candidate_2.png", (675, 685)),
]
CYAN = np.array([191, 243, 255, 255], dtype=np.uint8)
CORE = np.array([234, 252, 255, 255], dtype=np.uint8)
RIM = np.array([230, 246, 255, 255], dtype=np.uint8)


def erode(mask, n=1):
    m = mask.copy()
    for _ in range(n):
        out = m.copy()
        out[1:] &= m[:-1]
        out[:-1] &= m[1:]
        out[:, 1:] &= m[:, :-1]
        out[:, :-1] &= m[:, 1:]
        m = out
    return m


def source_to_cell(name, target):
    im = Image.open(SRC / name).convert("RGBA")
    ar = np.array(im)
    ys, xs = np.nonzero(ar[..., 3] > 120)
    crop = im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    # Both target axes are smaller than the imagegen source axes.
    assert target[0] < crop.width and target[1] < crop.height
    small = crop.resize(target, Image.Resampling.LANCZOS)
    bg = Image.new("RGBA", (CELL, CELL))
    bg.alpha_composite(small, ((CELL - target[0]) // 2, (CELL - target[1]) // 2))
    a = np.array(bg)
    mask = a[..., 3] >= 145
    # Neutralize generated cobalt fringe and incidental warm metal; preserve
    # the fine tonal variation in the original painting.
    rgb = a[..., :3].astype(np.float32)
    gray = .299 * rgb[..., 0] + .587 * rgb[..., 1] + .114 * rgb[..., 2]
    blended = .62 * rgb + .38 * gray[..., None]
    b = np.maximum(blended[..., 2], blended[..., 1] * 1.035)
    g = np.maximum(blended[..., 1], blended[..., 0] * 1.075)
    r = np.minimum(blended[..., 0], g * .935)
    # A dark scenery ramp with a few genuine snow-white ridges.
    out = np.stack((r * .74, g * .83, b * .88), axis=-1)
    out = np.clip(out, 0, 240).astype(np.uint8)
    a[..., :3] = out
    a[..., 3] = np.where(mask, 255, 0).astype(np.uint8)
    a[~mask, :3] = 0
    ring = mask & ~erode(mask, 3)
    a[ring] = RIM
    return a


def crack_lines(variant):
    # Coordinates sit on the boulder's broad body, with one principal crevasse
    # and small ice-branch cracks. Every line is jagged and vertically weighted.
    return [
        [[(350, 203), (363, 245), (352, 279), (378, 317), (366, 365), (390, 402),
          (376, 448), (393, 489), (381, 538), (397, 572)],
         [(378, 317), (411, 330), (428, 355), (445, 360)],
         [(376, 448), (341, 466), (329, 485)]],
        [[(346, 133), (356, 193), (342, 241), (363, 291), (352, 343), (372, 390),
          (357, 452), (379, 510), (368, 568), (383, 645)],
         [(352, 343), (316, 362), (296, 394)],
         [(357, 452), (409, 462), (438, 489)]],
        [[(365, 160), (385, 205), (369, 260), (393, 309), (379, 365), (397, 417),
          (382, 471), (399, 524), (386, 588), (401, 651)],
         [(379, 365), (337, 382), (310, 410)],
         [(397, 417), (435, 443), (459, 459)]],
    ][variant]


def cracks(base, variant, stage):
    out = base.copy()
    mask = base[..., 3] != 0
    lines = crack_lines(variant)
    if stage == 0:
        lines = [lines[0][2:8], lines[1][:2]]
        widths = (3, 1)
    else:
        widths = (8, 3)
    glow = Image.new("L", (CELL, CELL))
    core = Image.new("L", (CELL, CELL))
    dg, dc = ImageDraw.Draw(glow), ImageDraw.Draw(core)
    for line in lines:
        dg.line(line, fill=255, width=widths[0], joint="curve")
        dc.line(line, fill=255, width=widths[1], joint="curve")
    g = (np.array(glow) > 0) & mask
    c = (np.array(core) > 0) & mask
    out[g] = CYAN
    out[c] = CORE
    return out


def seeds_for(variant, small=False):
    if small:
        pts = [(x, y) for y in (210, 335, 460, 580) for x in (165, 305, 455, 600)]
        pts += [(365, 145), (370, 650)]
    else:
        pts = [(225, 275), (410, 230), (540, 300), (180, 465),
               (350, 430), (545, 500), (365, 620)]
    rng = random.Random(8901 + variant + int(small) * 34)
    return [(x + rng.randint(-22, 22), y + rng.randint(-18, 18)) for x, y in pts]


def labels_from_seeds(seeds):
    yy, xx = np.mgrid[:CELL, :CELL]
    dist = np.full((CELL, CELL), 1e12, dtype=np.float32)
    labels = np.zeros((CELL, CELL), dtype=np.uint8)
    for i, (x, y) in enumerate(seeds):
        d = (xx - x) ** 2 + (yy - y) ** 2
        take = d < dist
        labels[take] = i
        dist[take] = d[take]
    return labels


def inner_seams(labels, mask):
    edge = np.zeros_like(mask)
    edge[1:] |= labels[1:] != labels[:-1]
    edge[:-1] |= labels[:-1] != labels[1:]
    edge[:, 1:] |= labels[:, 1:] != labels[:, :-1]
    edge[:, :-1] |= labels[:, :-1] != labels[:, 1:]
    edge &= mask
    wide = edge.copy()
    for _ in range(3):
        t = wide.copy()
        t[1:] |= wide[:-1]
        t[:-1] |= wide[1:]
        t[:, 1:] |= wide[:, :-1]
        t[:, :-1] |= wide[:, 1:]
        wide = t & mask
    return wide, edge


def translated_pieces(base, variant, stage):
    mask = base[..., 3] != 0
    labels = labels_from_seeds(seeds_for(variant, small=stage == 3))
    wide, core = inner_seams(labels, mask)
    lit = base.copy()
    lit[wide] = CYAN
    lit[core] = CORE
    out = np.zeros_like(base)
    n = int(labels.max()) + 1
    ys, xs = np.nonzero(mask)
    cx, cy = xs.mean(), ys.mean()
    rng = random.Random(2149 + variant * 29 + stage)
    for i in range(n):
        piece = (labels == i) & mask
        py, px = np.nonzero(piece)
        if len(px) == 0:
            continue
        pcx, pcy = px.mean(), py.mean()
        vx, vy = pcx - cx, pcy - cy
        norm = max(1, math.hypot(vx, vy))
        strength = {1: 7, 2: 35, 3: 54}[stage]
        dx = int(round(strength * vx / norm + rng.randint(-5, 5)))
        dy = int(round(strength * vy / norm + rng.randint(-5, 5)))
        dx = min(max(dx, 4 - int(px.min())), 764 - int(px.max()))
        dy = min(max(dy, 4 - int(py.min())), 764 - int(py.max()))
        destx, desty = px + dx, py + dy
        out[desty, destx] = lit[py, px]
    # Snow and ice spray behind the moving chunks. Hard, single-pixel facets.
    rng = random.Random(718 + variant * 31 + stage)
    count = 0 if stage == 1 else (75 if stage == 2 else 170)
    for _ in range(count):
        x = rng.randrange(85, 684)
        y = rng.randrange(100, 692)
        if out[y, x, 3] != 0:
            continue
        size = rng.choice((1, 2, 2, 3))
        c = (168, 204, 220, 255) if rng.random() < .75 else (210, 235, 245, 255)
        out[y:y + size, x:x + size] = c
    return out


def final_debris(base, variant):
    out = np.zeros_like(base)
    mask = base[..., 3] != 0
    sy, sx = np.nonzero(mask)
    rng = random.Random(1310 + variant)
    target = int(mask.sum() * .115)
    tries = 0
    while (out[..., 3] != 0).sum() < target and tries < 900:
        tries += 1
        k = rng.randrange(len(sx))
        x, y = int(sx[k]), int(sy[k])
        radius = rng.randint(8, 19)
        # Cut a triangular shard from this exact boulder's surface.
        wedge = Image.new("1", (radius * 2 + 1, radius * 2 + 1))
        d = ImageDraw.Draw(wedge)
        d.polygon([(radius, 0), (0, radius * 2), (radius * 2, radius * 2)], fill=1)
        wedge = np.array(wedge, dtype=bool)
        x0, y0 = x - radius, y - radius
        x1, y1 = x + radius + 1, y + radius + 1
        if x0 < 0 or y0 < 0 or x1 > CELL or y1 > CELL:
            continue
        src = base[y0:y1, x0:x1]
        valid = wedge & (src[..., 3] != 0)
        if valid.sum() < 20:
            continue
        dx = rng.randint(-75, 75)
        dy = rng.randint(-85, 80)
        dx = min(max(dx, 4 - x0), 764 - (x1 - 1))
        dy = min(max(dy, 4 - y0), 764 - (y1 - 1))
        dst = out[y0 + dy:y1 + dy, x0 + dx:x1 + dx]
        dst[valid] = src[valid]
        # Just one small icy glint per shard, without a soft glow.
        gy, gx = np.nonzero(valid)
        if len(gx):
            mid = len(gx) // 3
            dst[gy[mid], gx[mid]] = CYAN
    for _ in range(380):
        x, y = rng.randrange(74, 695), rng.randrange(72, 699)
        if out[y, x, 3] == 0:
            out[y, x] = (196, 220, 232, 255)
    return out


def main():
    for name in ("boulder.png", "break.png"):
        if (ROOT / name).exists():
            raise SystemExit(f"Refusing to overwrite {ROOT / name}")
    intact = [source_to_cell(*entry) for entry in SELECTED]
    boulder = np.zeros((CELL, CELL * 3, 4), dtype=np.uint8)
    breaking = np.zeros((CELL * 3, CELL * 5, 4), dtype=np.uint8)
    for v, base in enumerate(intact):
        boulder[:, v * CELL:(v + 1) * CELL] = base
        frames = [cracks(base, v, 0), cracks(base, v, 1),
                  translated_pieces(base, v, 2),
                  translated_pieces(base, v, 3), final_debris(base, v)]
        for f, frame in enumerate(frames):
            breaking[v * CELL:(v + 1) * CELL, f * CELL:(f + 1) * CELL] = frame
    Image.fromarray(boulder).save(ROOT / "boulder.png")
    Image.fromarray(breaking).save(ROOT / "break.png")
    print("wrote", ROOT / "boulder.png", ROOT / "break.png")


if __name__ == "__main__":
    main()
