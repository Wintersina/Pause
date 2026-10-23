#!/usr/bin/env python3
"""Damage OVERLAY pipeline for a restyled hull (restyle-ship skill, step "overlay").

Idea: damage must sit on the SAME physical panels in every cell of a damage row. Instead of
repainting every frame, ONE damage layer per state is taken from the idle cell (column 0) and mapped
onto the intact cell of every other column by a landmark transform, then composited with alpha
clipped to the intact alpha (binary alpha kept, nothing outside the silhouette).

    python3 damage_overlay.py <Ship> [--skins] [--preview DIR]

  layer_s  = pixels of  sheet[row s, col 0]  that differ from  sheet[row 0, col 0]  (RGBA, binary alpha)
  col 1,2,5 (bobs)       : same geometry, integer translation (measured from the intact alphas)
  col 3,4 (glints)       : identical geometry
  col 6,7 (banks)        : piecewise-affine warp through 9 landmarks found on the STOCK intact cells
                           (nose, wing tips, canopy, wing slab lights, nozzle glows)
  col 8 (hit flash)      : flat bone-white frame: dark (cavity) layer pixels become ink, nothing else
Skins reuse the stock landmarks (same geometry) and take their own idle damage cell as the layer, so
skin damage matches stock cell for cell. Column 0 of every row is never touched.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402
CELL = 256
INK = (6, 8, 20)
# landmark search windows (fractions of ship height / px from the canopy x); a ship overrides them with a
# "landmarks" dict in its src~/restyle.json (red_y, red_x, orange_x)
LM = {"red_y": (0.55, 0.85), "red_x": (25, 85), "orange_x": (0, 40), "orange_amber": 0}
NAMES = ["N", "L", "R", "C", "SL", "SR", "NL", "NR"]
TRIS = [("N", "L", "SL"), ("N", "SL", "C"), ("C", "SL", "NL"), ("SL", "L", "NL"),
        ("N", "R", "SR"), ("N", "SR", "C"), ("C", "SR", "NR"), ("SR", "R", "NR"), ("C", "NL", "NR")]


def cell(a, r, c):
    return a[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]


def landmarks(cl):
    """Physical landmarks of an INTACT stock cell (rgba uint8): x, y of each."""
    rgb = cl[..., :3].astype(int)
    al = cl[..., 3] > 0
    ys, xs = np.where(al)
    top = ys.min()
    nose = (xs[ys == top].mean(), float(top))
    left = ys[xs == xs.min()].mean(), xs.min()
    right = ys[xs == xs.max()].mean(), xs.max()
    h = ys.max() - ys.min()
    yy = np.arange(CELL)[:, None]
    xx = np.arange(CELL)[None, :]
    cyan = al & (rgb[..., 0] < 120) & (rgb[..., 1] > 150) & (rgb[..., 2] > 170)
    cyan &= (yy < ys.min() + 0.40 * h) & (yy > ys.min() + 0.08 * h) & (abs(xx - nose[0]) < 30)
    cy, cx = np.where(cyan)
    canopy = (cx.mean(), cy.mean())
    cxm = canopy[0]
    redish = al & (rgb[..., 0] > 150) & (rgb[..., 0] > rgb[..., 1] * 2) & (rgb[..., 0] > rgb[..., 2] * 2)
    red = redish & (yy > ys.min() + LM["red_y"][0] * h) & (yy < ys.min() + LM["red_y"][1] * h) & (abs(xx - cxm) > LM["red_x"][0]) & (abs(xx - cxm) < LM["red_x"][1])
    orange = redish & (yy > ys.min() + 0.9 * h) & (abs(xx - cxm) < LM["orange_x"][1]) & (abs(xx - cxm) >= LM["orange_x"][0])
    if LM["orange_amber"]:  # amber (not red) nozzle glow
        orange = al & (rgb[..., 0] > 120) & (rgb[..., 1] > 50) & (rgb[..., 2] < 110) & (rgb[..., 0] > rgb[..., 2] * 1.8) & (rgb[..., 0] > rgb[..., 1] * 1.15) \
            & (yy > ys.min() + 0.82 * h) & (abs(xx - cxm) < LM["orange_x"][1]) & (abs(xx - cxm) >= LM["orange_x"][0])
        for sel in (xx < cxm, xx >= cxm):  # keep only each nozzle's lowest rows (a foreshortened pod ends higher)
            side = orange & sel
            if side.any():
                low = np.where(side)[0].max()
                orange &= ~(sel & (yy < low - 8))
    return nose, (left[1], left[0]), (right[1], right[0]), canopy, red, orange, cxm


def side_blobs(mask, cx, minsize):
    """centroid of all mask pixels left / right of x=cx (landmark glows are fragmented)"""
    out = []
    for sel in (np.arange(CELL)[None, :] < cx, np.arange(CELL)[None, :] >= cx):
        ys, xs = np.where(mask & sel)
        if len(xs) < minsize:
            return None
        out.append((xs.mean(), ys.mean()))
    return out


def stock_landmarks(cl):
    nose, left, right, canopy, red, orange, cx = landmarks(cl)
    slabs = side_blobs(red, cx, 12)
    noz = side_blobs(orange, cx, 6)
    if slabs is None or noz is None:
        return None
    return dict(N=nose, L=left, R=right, C=canopy, SL=slabs[0], SR=slabs[1], NL=noz[0], NR=noz[1])


def affine_fit(src, dst):
    A = np.c_[np.array(src, float), np.ones(len(src))]
    M, *_ = np.linalg.lstsq(A, np.array(dst, float), rcond=None)
    return M.T  # 2x3, dst = M @ [x y 1]


def build_maps(lm_idle, lm_pose):
    """For every DEST pixel of the pose cell, the source coordinate (x, y) in the idle cell."""
    inv = affine_fit([lm_pose[n] for n in NAMES], [lm_idle[n] for n in NAMES])
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    sx = inv[0, 0] * xx + inv[0, 1] * yy + inv[0, 2]
    sy = inv[1, 0] * xx + inv[1, 1] * yy + inv[1, 2]
    for t in TRIS:
        d = np.array([lm_pose[n] for n in t], float)
        s = np.array([lm_idle[n] for n in t], float)
        det = (d[1, 0] - d[0, 0]) * (d[2, 1] - d[0, 1]) - (d[2, 0] - d[0, 0]) * (d[1, 1] - d[0, 1])
        if abs(det) < 1e-6:
            continue
        px, py = xx - d[0, 0], yy - d[0, 1]
        l1 = (px * (d[2, 1] - d[0, 1]) - py * (d[2, 0] - d[0, 0])) / det
        l2 = (py * (d[1, 0] - d[0, 0]) - px * (d[1, 1] - d[0, 1])) / det
        l0 = 1 - l1 - l2
        inside = (l0 >= -1e-9) & (l1 >= -1e-9) & (l2 >= -1e-9)
        sx[inside] = l0[inside] * s[0, 0] + l1[inside] * s[1, 0] + l2[inside] * s[2, 0]
        sy[inside] = l0[inside] * s[0, 1] + l1[inside] * s[1, 1] + l2[inside] * s[2, 1]
    return sx, sy


def best_shift(a_idle, a_pose, rng=8):
    best = (10 ** 9, 0, 0)
    for dy in range(-rng, rng + 1):
        for dx in range(-rng, rng + 1):
            s = np.roll(np.roll(a_idle, dy, 0), dx, 1)
            d = int((s != a_pose).sum())
            if d < best[0]:
                best = (d, dx, dy)
    return best[1], best[2]


def sample(layer, sx, sy):
    xi = np.floor(sx + 0.5).astype(int)
    yi = np.floor(sy + 0.5).astype(int)
    ok = (xi >= 0) & (xi < CELL) & (yi >= 0) & (yi < CELL)
    out = np.zeros(layer.shape, np.uint8)
    out[ok] = layer[yi[ok], xi[ok]]
    return out


def make_layer(idle_dmg, idle_intact, thr=24, mask=None):
    if mask is not None:  # skins: the STOCK damage footprint, the skin's own colours
        out = np.zeros_like(idle_dmg)
        out[mask] = idle_dmg[mask]
        out[mask, 3] = 255
        return out
    diff = np.abs(idle_dmg[..., :3].astype(int) - idle_intact[..., :3].astype(int)).sum(-1)
    m = (diff > thr) & (idle_intact[..., 3] > 0)
    # close 1 px gaps so cavities are solid
    g = m.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        g |= np.roll(np.roll(m, dy, 0), dx, 1) & np.roll(np.roll(m, -dy, 0), -dx, 1)
    m = g & (idle_intact[..., 3] > 0)
    out = np.zeros_like(idle_dmg)
    out[m] = idle_dmg[m]
    out[m, 3] = 255
    return out


def overlay_sheet(sheet, geo, rows=(1, 2), masks=None):
    """sheet: rgba uint8 (768 x 2304). geo: dict col -> ('shift', dx, dy) or ('warp', sx, sy). Returns new sheet."""
    out = sheet.copy()
    for r in rows:
        layer = make_layer(cell(sheet, r, 0), cell(sheet, 0, 0), mask=None if masks is None else masks[r])
        # skins: column 0 too (their Codex palette sheets carry stray shading differences outside the damage)
        for c in range(0 if masks is not None else 1, 9):
            intact = cell(sheet, 0, c)
            mode = geo[c]
            if mode[0] == "shift":
                lay = np.roll(np.roll(layer, mode[2], 0), mode[1], 1)
            else:
                lay = sample(layer, mode[1], mode[2])
            alpha = intact[..., 3] > 0
            dst = intact.copy()
            m = (lay[..., 3] > 0) & alpha
            if c == 8:
                dark = m & (lay[..., :3].astype(int).max(-1) < 90)
                dst[dark, :3] = INK
            else:
                dst[m, :3] = lay[m, :3]
            dst[..., 3] = intact[..., 3]
            out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = dst
    return out


def stock_geometry(sheet):
    """per-column geometry from the stock intact row"""
    idle = cell(sheet, 0, 0)
    ai = idle[..., 3] > 0
    lm0 = stock_landmarks(idle)
    assert lm0, "stock idle landmarks not found"
    geo = {0: ("shift", 0, 0)}
    for c in (1, 2, 3, 4, 5, 8):
        dx, dy = best_shift(ai, cell(sheet, 0, c)[..., 3] > 0)
        geo[c] = ("shift", dx, dy)
    marks = {0: lm0}
    for c in (6, 7):
        lm = stock_landmarks(cell(sheet, 0, c))
        assert lm, "bank landmarks not found in col %d" % c
        sx, sy = build_maps(lm0, lm)
        geo[c] = ("warp", sx, sy)
        marks[c] = lm
    return geo, marks


def load(path):
    return np.array(Image.open(path).convert("RGBA"))


def main():
    key = shiplib.resolve(sys.argv[1])
    rj = os.path.join(shiplib.src_dir(key), "restyle.json")
    if os.path.exists(rj):
        import json
        for k, v in json.load(open(rj)).get("landmarks", {}).items():
            LM[k] = tuple(v) if isinstance(v, (list, tuple)) else v
    prev = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    stock_p = shiplib.hull_path(key)
    stock = load(stock_p)
    geo, marks = stock_geometry(stock)
    print("shifts", {c: g[1:] for c, g in geo.items() if g[0] == "shift"})
    masks = {r: make_layer(cell(stock, r, 0), cell(stock, 0, 0))[..., 3] > 0 for r in (1, 2)}
    targets = [(stock_p, stock)]
    if "--skins" in sys.argv:
        d = shiplib.ship_dir(key)
        for n in sorted(os.listdir(d)):
            if n.startswith(key + "_") and n.endswith(".bytes") and not n.endswith(("_shield.bytes", "_hitbox.bytes")):
                targets.append((os.path.join(d, n), load(os.path.join(d, n))))
    for path, sh in targets:
        res = overlay_sheet(sh, geo, masks=None if path == stock_p else masks)
        assert (res[..., 3] == sh[..., 3]).all(), "alpha changed " + path
        Image.fromarray(res).save(path if path.endswith(".png") else path, format="PNG")
        print("overlaid", os.path.basename(path))
        if prev:
            os.makedirs(prev, exist_ok=True)
            name = os.path.basename(path).split(".")[0]
            bg = Image.new("RGBA", (res.shape[1], res.shape[0]), (14, 20, 36, 255))
            bg.alpha_composite(Image.fromarray(res))
            bg.convert("RGB").save(os.path.join(prev, name + "_overlay.png"))


if __name__ == "__main__":
    main()
