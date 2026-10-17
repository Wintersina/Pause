#!/usr/bin/env python3
"""Pose sheets (sheets/<name>.png from Codex) -> seven-cell strips with REAL painted poses.

Cell 0 = the current idle master (kept), 1-3 = painted idle B-D, 4-5 = painted tell warm-up / peak, 6 = painted hit recoil + flash.
Every pose is keyed off the #00FF40 background, cleaned to hard alpha, scaled by ONE factor per sheet (idle-pose area -> master area,
nearest-neighbour only) and registered on the master's pivot (bbox centre x, alpha centroid y), so there is no jitter. Poses that would
come within 6 px of the cell edge are shrunk (rare). Output: strips/<name>.png (also lists metrics).

    python3 build_frames.py [name ...]  [--keep-idle]   # --keep-idle: cells 1-3 stay the existing scripted idle cells
"""
import sys
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import ships

ROOT = HERE.parents[4]
RES = ROOT / "Assets/Art/Resources/Enemies"
OUT = HERE / "strips"
MARGIN = 6
FLASH = {"desert": (255, 241, 200), "ember": (255, 214, 130)}


def key_alpha(rgb):
    """Alpha from the #00FF40 key: distance based, then hard threshold; also removes green-spill fringe pixels."""
    a = rgb.astype(int)
    d = np.abs(a - np.array([0, 255, 64])).sum(-1)
    fringe = (a[..., 1] > a[..., 0] + 60) & (a[..., 1] > a[..., 2] + 60) & (a[..., 1] > 150)   # near-key greens
    return (d > 150) & ~fringe


def label_sheet(mask):
    """Coarse (3 px) 8-connected labelling over the WHOLE sheet; returns per-pixel pose index (-1 = none) by component centroid, so a wing
    tip that crosses a grid line still belongs to its own pose. Components < 1.5% of their pose's largest are dropped as specks."""
    h, w = mask.shape
    g = 3
    gh, gw = h // g, w // g
    coarse = mask[:gh * g, :gw * g].reshape(gh, g, gw, g).any((1, 3))
    d = coarse.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            d |= np.roll(np.roll(coarse, dy, 0), dx, 1)
    lab = np.zeros((gh, gw), int); n = 0; comps = []
    for y, x in zip(*np.where(d)):
        if lab[y, x]:
            continue
        n += 1; lab[y, x] = n; q = deque([(y, x)]); pts = []
        while q:
            cy, cx = q.popleft()
            if coarse[cy, cx]:
                pts.append((cy, cx))
            for ny in (cy - 1, cy, cy + 1):
                for nx in (cx - 1, cx, cx + 1):
                    if 0 <= ny < gh and 0 <= nx < gw and d[ny, nx] and not lab[ny, nx]:
                        lab[ny, nx] = n; q.append((ny, nx))
        if pts:
            p = np.array(pts)
            comps.append((n, len(pts), int(p[:, 0].mean() * g * 2 // h), int(p[:, 1].mean() * g * 3 // w)))
    pose = {}
    for n, size, r, c in comps:
        pose.setdefault((min(r, 1), min(c, 2)), []).append((n, size))
    keep = {}
    for k, lst in pose.items():
        big = max(sz for _, sz in lst)
        keep[k] = [n for n, sz in lst if sz >= .005 * big]
    out = {}
    for k, ns in keep.items():
        m = np.isin(lab, ns)
        full = np.kron(m, np.ones((g, g), bool))
        f = np.zeros_like(mask); f[:full.shape[0], :full.shape[1]] = full
        out[k] = mask & f
    return out


def extract(sheet, poses_mask, r, c):
    m = poses_mask[(r, c)]
    ys, xs = np.where(m)
    if not len(xs):
        raise SystemExit(f"empty pose r{r} c{c}")
    rgba = np.dstack((sheet, np.where(m, 255, 0).astype(np.uint8)))
    return Image.fromarray(rgba, "RGBA").crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def pivot(im):
    a = np.asarray(im)[..., 3] > 0
    ys, xs = np.where(a)
    return (xs.min() + xs.max()) / 2, ys.mean(), a.sum()


def place(pose, scale, cell, piv, dy=0):
    w = max(1, round(pose.width * scale)); h = max(1, round(pose.height * scale))
    for _ in range(40):
        im = pose.resize((w, h), Image.NEAREST)
        px, py, _ = pivot(im)
        x0 = round(piv[0] - px); y0 = round(piv[1] - py + dy)
        if x0 >= MARGIN and y0 >= MARGIN and x0 + w <= cell - MARGIN and y0 + h <= cell - MARGIN:
            out = Image.new("RGBA", (cell, cell)); out.alpha_composite(im, (x0, y0)); return out
        w = max(1, round(w * .97)); h = max(1, round(h * .97))
    raise SystemExit("cannot fit pose inside the margin")


def flash(im, tint, amt=.13):
    a = np.asarray(im).astype(np.float32).copy()
    s = a[..., 3] > 0
    a[s, :3] = a[s, :3] * (1 - amt) + np.array(tint, np.float32) * amt
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def build(name, keep_idle=False):
    spec = ships.SHIPS[name]; cell = spec["cell"]
    old = Image.open(RES / f"{name}.png").convert("RGBA")
    cells = [old.crop((i * cell, 0, (i + 1) * cell, cell)) for i in range(7)]
    sheet = np.asarray(Image.open(HERE / "sheets" / f"{name}.png").convert("RGB"))
    pm = label_sheet(key_alpha(sheet))
    poses = {(r, c): extract(sheet, pm, r, c) for r in range(2) for c in range(3)}
    piv0 = pivot(cells[0])
    areas = [pivot(poses[(0, c)])[2] for c in range(3)] + [pivot(cells[0])[2] * 0]   # idle poses only
    scale = (piv0[2] / np.median(areas[:3])) ** .5
    # hit uses the master pivot shifted up 5 px: the painted pose already shows the knock-back, this adds a little push
    new = list(cells)
    if not keep_idle:
        for c in range(3):
            new[1 + c] = place(poses[(0, c)], scale, cell, piv0)
    new[4] = place(poses[(1, 0)], scale, cell, piv0)
    new[5] = place(poses[(1, 1)], scale, cell, piv0)
    new[6] = flash(place(poses[(1, 2)], scale, cell, piv0, dy=-5), FLASH[spec["world"]])
    strip = Image.new("RGBA", (cell * 7, cell))
    for i, c in enumerate(new):
        strip.alpha_composite(c, (i * cell, 0))
    return strip, scale


def main():
    OUT.mkdir(exist_ok=True)
    keep = "--keep-idle" in sys.argv
    names = [a for a in sys.argv[1:] if not a.startswith("--")] or [n for n in ships.SHIPS if (HERE / "sheets" / f"{n}.png").exists()]
    for n in names:
        s, sc = build(n, keep)
        s.save(OUT / f"{n}.png", optimize=True)
        print(n, s.size, "scale %.3f" % sc)


if __name__ == "__main__":
    main()
