#!/usr/bin/env python3
"""Bakes Space's drifting-asteroid atlas from Codex's art (read-only inputs).

Inputs  (Codex, untouched): asteroid_00..02.png (512x512), asteroid_fx.png/.json
Output: asteroid_drift.png/.json     rocks + crack masks (bilinear, like the rest of the backdrop)
        asteroid_drift_fx.png/.json  smoke stages + sparks (point-filtered at runtime: hard pixels)
        (BackdropAtlas manifests, y bottom-up)
        and the crack hot-spot table for SpaceAsteroidDrift.cs (stdout).

Cells
  rock{i}_00..03      asteroid i pre-shrunk to 256 / 128 / 64 / 32 px (premultiplied box
                      filter, alpha hardened at 50%): drawn near 1:1 so the rock stays
                      crisp without mipmaps (the 512 original is used above 256).
  crack{i}_00..03     the glowing crack pixels only (saturated magenta / pink),
                      at 256 / 128 / 64 / 32 px (the 256 mask also covers the 512 rock), colour normalised to full value,
                      alpha hard 0/1 -- the additive blink mask.
  puff{i}x{P}_00..03  the smoke of Codex's asteroidfx{i}_00..03 flipbook cells (grey
                      pixels only, the pink spark cut away), shrunk to P = 64 / 32 / 16 px
                      and hardened to two alpha steps: four pixel-art smoke stages,
                      in three sizes so a puff is drawn near 1:1 (point-filtered).
  spark_00 / spark_01 Codex's pink spark (asteroidfx0_02) as a 7 px hard cross and a
                      3 px core.
"""
import sys, json, colorsys
import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
PUFF_PX = (64, 32, 16)  # smoke stage copies: a puff picks the one nearest its drawn size
PAD = 6         # >= the 6x6 ASTC block: no compressed block mixes two cells


def load(path):
    return np.asarray(Image.open(path).convert('RGBA')).astype(np.float32) / 255.0


def crack_mask(im):
    rgb, a = im[..., :3], im[..., 3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # hue in degrees
    h = np.zeros_like(mx)
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, h)
    h = np.where(mx == g, (b - r) / d + 2, h)
    h = np.where(mx == b, (r - g) / d + 4, h)
    h = h * 60.0
    return (a > 0.5) & (h >= 280) & (h <= 340) & (sat >= 0.45) & (mx >= 0.35)


def box_down(im, f):
    """premultiplied box filter by integer factor f"""
    H, W = im.shape[:2]
    pm = im.copy()
    pm[..., :3] *= pm[..., 3:4]
    pm = pm.reshape(H // f, f, W // f, f, 4).mean(axis=(1, 3))
    a = pm[..., 3:4]
    rgb = np.where(a > 1e-4, pm[..., :3] / np.maximum(a, 1e-4), 0)
    return np.concatenate([rgb, a], -1)


def harden(im, cut=0.5):
    o = im.copy()
    o[..., 3] = (o[..., 3] >= cut).astype(np.float32)
    o[..., :3] *= o[..., 3:4]
    return o


def mask_level(im, m, size):
    """crack mask image at `size`: a cell is a crack if any crack pixel lands in it
    for the 512 level; below that, if >= 25% of its block is crack."""
    f = 512 // size
    rgb = im[..., :3]
    mx = np.maximum(rgb.max(-1, keepdims=True), 1e-6)
    col = np.where(m[..., None], rgb / mx, 0)          # full-value crack colour
    if f == 1:
        a = m.astype(np.float32)
        return np.concatenate([col * a[..., None], a[..., None]], -1)
    blk = m.reshape(size, f, size, f).mean(axis=(1, 3))
    c = col.reshape(size, f, size, f, 3).sum(axis=(1, 3)) / np.maximum(blk[..., None] * f * f, 1e-6)
    a = (blk >= 0.25).astype(np.float32)
    c = c / np.maximum(c.max(-1, keepdims=True), 1e-6)
    return np.concatenate([c * a[..., None], a[..., None]], -1)


def hot_spots(m, im, count=3):
    """the brightest crack clusters: their crack pixel nearest the cluster centre,
    as fractions of the sprite width from its centre (x right, y up)."""
    from collections import deque
    H, W = m.shape
    # coarse grid clusters: 16 px blocks containing crack pixels, flood-filled
    g = 16
    blk = m.reshape(H // g, g, W // g, g).sum(axis=(1, 3))
    seen = np.zeros_like(blk, dtype=bool)
    clusters = []
    for by in range(blk.shape[0]):
        for bx in range(blk.shape[1]):
            if blk[by, bx] < 6 or seen[by, bx]:
                continue
            q = deque([(by, bx)]); seen[by, bx] = True; cells = []
            while q:
                y, x = q.popleft(); cells.append((y, x))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        yy, xx = y + dy, x + dx
                        if 0 <= yy < blk.shape[0] and 0 <= xx < blk.shape[1] and not seen[yy, xx] and blk[yy, xx] >= 6:
                            seen[yy, xx] = True; q.append((yy, xx))
            clusters.append(cells)
    val = im[..., :3].max(-1)
    scored = []
    for cells in clusters:
        pts = []
        for (y, x) in cells:
            ys, xs = np.nonzero(m[y * g:(y + 1) * g, x * g:(x + 1) * g])
            pts += [(y * g + yy, x * g + xx) for yy, xx in zip(ys, xs)]
        pts = np.array(pts)
        bright = val[pts[:, 0], pts[:, 1]]
        # the hottest part of the cluster: its brightest quarter
        hot = pts[bright >= np.quantile(bright, 0.75)]
        cy, cx = hot.mean(0)
        k = np.argmin(((hot - [cy, cx]) ** 2).sum(1))
        scored.append((len(pts) * float(bright.mean()), hot[k]))
    scored.sort(key=lambda s: -s[0])
    picked = []
    for score, (y, x) in scored:
        if any((y - py) ** 2 + (x - px) ** 2 < 70 ** 2 for py, px in picked):
            continue
        picked.append((y, x))
        if len(picked) == count:
            break
    return [((x + 0.5 - W / 2) / W, (H / 2 - (y + 0.5)) / W) for y, x in picked], picked


cells = []      # (name, RGBA float image)
spots = []
for i in range(3):
    im = load(f"{src}/asteroid_{i:02d}.png")
    m = crack_mask(im)
    for k, size in enumerate((256, 128, 64, 32)):
        cells.append((f"rock{i}_{k:02d}", harden(box_down(im, 512 // size))))
    for k, size in enumerate((256, 128, 64, 32)):
        cells.append((f"crack{i}_{k:02d}", mask_level(im, m, size)))
    fr, px = hot_spots(m, im)
    spots.append((fr, px, int(m.sum())))

# smoke stages + spark from Codex's asteroid_fx flipbook
fx = load(f"{src}/asteroid_fx.png")
fxm = json.load(open(f"{src}/asteroid_fx.json"))
rects = {s['n']: s for s in fxm['sprites']}
FH = fx.shape[0]


def fx_cell(name):
    r = rects[name]
    top = FH - r['y'] - r['h']
    return fx[top:top + r['h'], r['x']:r['x'] + r['w']]


for i in range(3):
    for s in range(4):
        c = fx_cell(f"asteroidfx{i}_{s:02d}")
        rgb, a = c[..., :3], c[..., 3]
        mx, mn = rgb.max(-1), rgb.min(-1)
        sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
        smoke = (a > 0.01) & (rgb[..., 0] < rgb[..., 2])     # blue-grey smoke, not the pink spark
        ys, xs = np.nonzero(smoke)
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        side = max(y1 - y0, x1 - x0)
        cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
        crop = np.zeros((side, side, 4), np.float32)
        sy0, sx0 = cy - side // 2, cx - side // 2
        for yy in range(side):
            for xx in range(side):
                Y, X = sy0 + yy, sx0 + xx
                if 0 <= Y < 256 and 0 <= X < 256 and smoke[Y, X]:
                    crop[yy, xx] = c[Y, X]
        for P in PUFF_PX:
            img = Image.fromarray((crop * 255).astype(np.uint8), 'RGBA').resize((P, P), Image.BOX)
            p = np.asarray(img).astype(np.float32) / 255
            an = p[..., 3] / max(p[..., 3].max(), 1e-6)
            alpha = np.where(an >= 0.45, 1.0, np.where(an >= 0.18, 0.5, 0.0))
            # the later stages thin out as the smoke disperses: stage 2 loses its
            # half-alpha fringe to a checker, stage 3 is broken up throughout
            yy, xx = np.mgrid[0:P, 0:P]
            checker = ((yy + xx) & 1) == 0
            if s == 2:
                alpha = np.where((alpha == 0.5) & checker, 0.0, alpha)
            if s == 3:
                alpha = np.where(alpha == 0.5, 0.0, np.where(checker & (alpha == 1.0) & (an < 0.8), 0.5, alpha))
            lum = p[..., :3].mean(-1)
            lum = lum / max(lum.max(), 1e-6)
            # baked at 3/4 of the smoke's light grey (the runtime tint, near 1,
            # restores most of it): the stored atlas stays under the backdrop
            # brightness ceiling (WorldBackdropTest, AtlasMaxLuminance)
            v = 0.75 * (0.78 + 0.22 * lum)
            out_c = np.stack([v, v, v, alpha], -1)
            out_c[..., :3] *= (alpha > 0)[..., None]
            cells.append((f"puff{i}x{P}_{s:02d}", out_c))

sc = fx_cell("asteroidfx0_02")
rgb, a = sc[..., :3], sc[..., 3]
mx, mn = rgb.max(-1), rgb.min(-1)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
pink = (a > 0.05) & (rgb[..., 0] > rgb[..., 2]) & (sat > 0.4)
ys, xs = np.nonzero(pink)
cy, cx = int(round(ys.mean())), int(round(xs.mean()))
spark_col = rgb[pink & (a > 0.8)].mean(0)
spark_col = spark_col / spark_col.max()
for name, n, arm in (("spark_00", 7, 3), ("spark_01", 3, 1)):
    s = np.zeros((n, n, 4), np.float32)
    mid = n // 2
    for k in range(-arm, arm + 1):
        s[mid, mid + k] = [*spark_col, 1]
        s[mid + k, mid] = [*spark_col, 1]
    s[mid, mid] = [1, 1, 1, 1]          # white-hot centre pixel
    cells.append((name, s))

def pack(cells, name, W):
    """shelf-pack, tallest first; PAD >= the ASTC block so no block mixes two cells"""
    order = sorted(cells, key=lambda c: (-c[1].shape[0], c[0]))
    x = y = shelf = 0
    placed = {}
    for n, img in order:
        h, w = img.shape[:2]
        if x + w > W:
            x = 0; y += shelf + PAD; shelf = 0
        placed[n] = (x, y, w, h)
        x += w + PAD
        shelf = max(shelf, h)
    Htot = y + shelf
    H = 1
    while H < Htot:
        H *= 2
    atlas = np.zeros((H, W, 4), np.float32)
    for n, img in cells:
        x, y, w, h = placed[n]
        atlas[y:y + h, x:x + w] = img
    Image.fromarray((np.clip(atlas, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(f"{out}/{name}.png", optimize=True)
    manifest = {"sprites": [{"n": n, "x": placed[n][0], "y": H - placed[n][1] - placed[n][3],
                             "w": placed[n][2], "h": placed[n][3]} for n, _ in cells]}
    with open(f"{out}/{name}.json", "w") as f:
        json.dump(manifest, f, indent=1)
    print(name, W, H)


pack([c for c in cells if c[0].startswith(("rock", "crack"))], "asteroid_drift", 1024)
pack([c for c in cells if c[0].startswith(("puff", "spark"))], "asteroid_drift_fx", 512)
for i, (fr, px, n) in enumerate(spots):
    print(f"asteroid_{i:02d}: {n} crack px; hot spots (px) {px}")
    print("    { " + ", ".join(f"new Vector2({x:.3f}f, {y:.3f}f)" for x, y in fr) + " },")
