"""Assemble dark Frost cover boulders from image-generated 1254px paintings.

The only resampling is a Lanczos reduction into 768px cells. Break chunks are
cuts of those finished cells, translated at integer pixel offsets.
"""
from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 768
PAINTINGS = (
    ("mountain_revision_candidate_1.png", 690),
    ("slab_revision_candidate_1.png", 650),
    ("arch_revision_candidate_1.png", 690),
)
CYAN = np.array((191, 243, 255, 255), np.uint8)
CORE = np.array((234, 252, 255, 255), np.uint8)
BAYER = np.array(((0, 8, 2, 10), (12, 4, 14, 6),
                  (3, 11, 1, 9), (15, 7, 13, 5))) / 16.0


def erode(mask, n):
    for _ in range(n):
        old = mask
        mask = old.copy()
        mask[1:] &= old[:-1]
        mask[:-1] &= old[1:]
        mask[:, 1:] &= old[:, :-1]
        mask[:, :-1] &= old[:, 1:]
    return mask


def dilate(mask, n):
    for _ in range(n):
        old = mask
        mask = old.copy()
        mask[1:] |= old[:-1]
        mask[:-1] |= old[1:]
        mask[:, 1:] |= old[:, :-1]
        mask[:, :-1] |= old[:, 1:]
    return mask


def source_cell(filename, width):
    source = Image.open(SRC / filename).convert("RGBA")
    original = np.asarray(source)
    ys, xs = np.nonzero(original[..., 3] > 8)
    crop = source.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1,
                        int(ys.max()) + 1))
    height = round(width * crop.height / crop.width)
    assert width < crop.width and height < crop.height and height <= 729
    painted = crop.resize((width, height), Image.Resampling.LANCZOS)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(painted, ((CELL - width) // 2, (CELL - height) // 2))
    a = np.array(cell)
    mask = a[..., 3] >= 92

    # Bring the image-generated painting to the measured Frost landmark ramp:
    # median ~43 and upper snow masses ~71 luma, preserving source microtexture.
    rgb = a[..., :3].astype(np.float32)
    lum = rgb @ np.array((.299, .587, .114), np.float32)
    desired = np.minimum(88, 13 + .42 * lum - .00005 * lum * lum)
    factor = desired / np.maximum(lum, 1)
    rgb *= factor[..., None]
    rgb[..., 0] = np.minimum(rgb[..., 0], rgb[..., 1] * .96)
    rgb[..., 2] = np.minimum(rgb[..., 2], rgb[..., 1] * 1.30)
    a[..., :3] = np.uint8(np.clip(rgb, 0, 255))

    # The last 75 cell pixels contain the generated pack-ice skirt. Fade its
    # outer floes through an ordered pixel dither; the solid mountain remains
    # opaque, including all of its broad front face.
    yy, xx = np.indices((CELL, CELL))
    if filename.startswith("mountain"):
        # The large pale glacier foot otherwise balances the lit left peak.
        # A gentle directional wash restores the upper-left key light.
        wash = 18 * (CELL - xx - yy) / CELL
        a[..., :3] = np.uint8(np.clip(a[..., :3].astype(np.float32)
                                        + wash[..., None], 0, 255))
    low = np.nonzero(mask)[0].max()
    skirt_start = low - 76
    skirt = (yy > skirt_start) & mask
    strength = np.clip((low - yy + 1) / 77, 0, 1)
    keep = strength > BAYER[yy % 4, xx % 4]
    mask[skirt] &= keep[skirt]
    ring = mask & ~erode(mask, 2) & (yy < skirt_start)
    a[ring, :3] = np.uint8(a[ring, :3].astype(np.float32) * .90)
    a[..., 3] = np.where(mask, 255, 0).astype(np.uint8)
    a[~mask, :3] = 0
    return a


def crack_paths(v):
    return (
        [[(345, 180), (365, 221), (350, 273), (377, 325), (359, 376),
          (388, 426), (370, 480), (399, 527), (384, 586)],
         [(377, 325), (418, 339), (446, 367)],
         [(370, 480), (335, 497), (318, 525)]],
        [[(348, 127), (359, 189), (342, 245), (370, 300), (350, 355),
          (379, 408), (354, 467), (385, 527), (374, 592), (393, 651)],
         [(350, 355), (313, 375), (293, 411)],
         [(354, 467), (413, 479), (449, 509)]],
        [[(360, 98), (379, 156), (363, 220), (390, 273), (371, 335),
          (397, 393), (378, 458), (400, 518), (384, 581), (401, 648)],
         [(371, 335), (331, 355), (305, 390)],
         [(397, 393), (435, 426), (468, 451)]],
    )[v]


def add_cracks(base, v, stage):
    out = base.copy()
    body = base[..., 3] > 128
    glow = Image.new("L", (CELL, CELL))
    core = Image.new("L", (CELL, CELL))
    gd, cd = ImageDraw.Draw(glow), ImageDraw.Draw(core)
    paths = crack_paths(v)
    if stage == 0:
        paths = [paths[0][2:8], paths[1][:2]]
    for path in paths:
        gd.line(path, fill=255, width=2 if stage == 0 else 6, joint="curve")
        cd.line(path, fill=255, width=1 if stage == 0 else 2, joint="curve")
    g = (np.asarray(glow) > 0) & body
    c = (np.asarray(core) > 0) & body
    out[g] = CYAN
    out[c] = CORE
    return out


def piece_labels(v, fine):
    if fine:
        seeds = [(x, y) for y in (175, 325, 470, 605)
                 for x in (150, 305, 460, 615)]
    else:
        seeds = [(211, 266), (405, 240), (555, 290), (182, 464),
                 (364, 425), (558, 482), (268, 610), (468, 616)]
    rng = random.Random(5601 + v + 30 * fine)
    seeds = [(x + rng.randrange(-17, 18), y + rng.randrange(-15, 16))
             for x, y in seeds]
    yy, xx = np.indices((CELL, CELL))
    best = np.full((CELL, CELL), np.inf, np.float32)
    labels = np.zeros((CELL, CELL), np.uint8)
    for i, (x, y) in enumerate(seeds):
        dist = ((xx - x) ** 2 + (yy - y) ** 2).astype(np.float32)
        take = dist < best
        labels[take] = i
        best[take] = dist[take]
    return labels


def split(base, v, stage):
    mask = base[..., 3] > 128
    labels = piece_labels(v, stage == 3)
    edge = np.zeros(mask.shape, bool)
    edge[1:] |= labels[1:] != labels[:-1]
    edge[:-1] |= labels[:-1] != labels[1:]
    edge[:, 1:] |= labels[:, 1:] != labels[:, :-1]
    edge[:, :-1] |= labels[:, :-1] != labels[:, 1:]
    edge &= mask
    # The burst's cut faces carry white-cyan fissure light. Its seams are thin
    # relative to the big, source-painted ice faces.
    lit = base.copy()
    if stage == 2:
        glow = dilate(edge, 3) & mask
        lit[glow, :3] = (116, 185, 205)
        lit[edge] = CORE
    elif stage == 1:
        lit[edge, :3] = (98, 145, 160)
    elif stage == 3:
        lit[edge, :3] = (117, 176, 193)
    out = np.zeros_like(base)
    yy, xx = np.nonzero(mask)
    cx, cy = xx.mean(), yy.mean()
    rng = random.Random(1481 + v * 23 + stage)
    for i in range(int(labels.max()) + 1):
        piece = mask & (labels == i)
        if stage == 3:
            piece &= erode(piece, 2)
        py, px = np.nonzero(piece)
        if len(px) == 0:
            continue
        vx, vy = px.mean() - cx, py.mean() - cy
        norm = max(1, math.hypot(vx, vy))
        move = {1: 4, 2: 36, 3: 62}[stage]
        dx = round(move * vx / norm + rng.randrange(-4, 5))
        dy = round(move * vy / norm + rng.randrange(-4, 5))
        dx = min(max(dx, 4 - int(px.min())), 763 - int(px.max()))
        dy = min(max(dy, 4 - int(py.min())), 763 - int(py.max()))
        out[py + dy, px + dx] = lit[py, px]
    # Keep the deep natural crevasse visible before the rupture.
    if stage == 1:
        out = add_cracks(out, v, 1)
    if stage >= 2:
        dust = 70 if stage == 2 else 220
        for _ in range(dust):
            x, y = rng.randrange(65, 701), rng.randrange(66, 701)
            if out[y, x, 3] == 0:
                out[y, x] = (78, 111, 130, 255) if rng.random() < .85 else CYAN
    return out


def drifting_debris(base, v):
    out = np.zeros_like(base)
    mask = base[..., 3] > 128
    sy, sx = np.nonzero(mask)
    rng = random.Random(4310 + v)
    target = int(mask.sum() * .15)
    for _ in range(850):
        if np.count_nonzero(out[..., 3]) >= target:
            break
        k = rng.randrange(len(sx))
        x, y = int(sx[k]), int(sy[k])
        r = rng.randrange(7, 19)
        x0, y0, x1, y1 = x-r, y-r, x+r+1, y+r+1
        if min(x0, y0) < 4 or max(x1, y1) > 763:
            continue
        wedge = Image.new("1", (2*r+1, 2*r+1))
        ImageDraw.Draw(wedge).polygon(((r, 0), (0, 2*r), (2*r, 2*r)), fill=1)
        valid = np.asarray(wedge, bool) & (base[y0:y1, x0:x1, 3] > 128)
        if valid.sum() < 12:
            continue
        dx, dy = rng.randrange(-82, 83), rng.randrange(-86, 87)
        dx = min(max(dx, 4-x0), 763-x1)
        dy = min(max(dy, 4-y0), 763-y1)
        dst = out[y0+dy:y1+dy, x0+dx:x1+dx]
        dst[valid] = base[y0:y1, x0:x1][valid]
    for _ in range(800):
        x, y = rng.randrange(48, 719), rng.randrange(58, 710)
        if out[y, x, 3] == 0:
            out[y, x] = (64, 89, 106, rng.choice((64, 96, 128, 160)))
    return out


def main():
    for name in ("boulder.png", "break.png"):
        if (ROOT / name).exists():
            raise SystemExit(f"Refusing to overwrite {ROOT / name}")
    intact = [source_cell(*spec) for spec in PAINTINGS]
    boulder = np.zeros((CELL, CELL*3, 4), np.uint8)
    breaks = np.zeros((CELL*3, CELL*5, 4), np.uint8)
    for v, base in enumerate(intact):
        boulder[:, v*CELL:(v+1)*CELL] = base
        frames = (add_cracks(base, v, 0), split(base, v, 1),
                  split(base, v, 2), split(base, v, 3),
                  drifting_debris(base, v))
        for f, frame in enumerate(frames):
            breaks[v*CELL:(v+1)*CELL, f*CELL:(f+1)*CELL] = frame
    Image.fromarray(boulder).save(ROOT / "boulder.png")
    Image.fromarray(breaks).save(ROOT / "break.png")


if __name__ == "__main__":
    main()
