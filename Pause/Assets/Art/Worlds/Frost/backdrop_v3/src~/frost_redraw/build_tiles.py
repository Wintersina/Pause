#!/usr/bin/env python3
"""Frost ground tiles from the approved PixelLab drawings (224 x 448 pixel art, top-down, scrolling vertically).

    python3 build_tiles.py [--out <Resources/Worlds/Frost/Backdrop3>] [v1/sky v1/far ...]

Installs (overwrites the PNG bytes in place, the .meta files are untouched):
    v1/sky v1/far v1/mid v1/flow  v2/far v2/mid      (sources/<v>_<layer>.png; the old tiles are in old/)
The other v2 layers and v3 / v4 are not part of this redraw.

Per layer, all in the 224-wide source grid (one source pixel = one 4 x 4 cell of the 896 x 1792 tile):
  1. CROP   v1_mid is a 224 x 224 drawing letterboxed in white: its content band is cut out and repeated twice
            down the tile (period 896 px) after step 2. Everything else uses the full 224 x 448.
  2. SEAM   the last O rows are replaced, column by column below a minimum-error path, by the top rows mirrored
            (out[h-1-i] = src[i]), so the bottom row equals the top row and the wrap reads as a reflection
            through row 0. The path runs where the original and the mirror agree best (hard cut, no blur).
  3. GRADE  HSV-value remap, hue and saturation kept: v' = hi * v^g, fitted so the layer's median and p90 land
            on the painted targets below (the art is painted dark and lifted ~1.95x at draw time by
            BackdropGrade / FrostTuning.Brightness; the targets keep WorldBackdropTest's as-drawn caps, the
            lane readability rule and HudButtonsTest with margin). Saturated red / pink is turned to amber.
            flow: colours graded the same way and alpha scaled to FLOW_ALPHA (straight alpha, never 255).
  4. PIXEL  <= 96 colours (the drawings have 23-43, so this is a no-op guard), nearest 4x to 896 x 1792 (4 px
            cells, the Desert pixel-pass grid), then a per-texel grain: every opaque texel's RGB moves by the same
            -1 / 0 / +1 (seeded), so no 2x2 / 3x3 / 4x4 block is uniform (BackdropResolutionTest rejects nearest
            upscales at >= .97) while the look stays the crisp 4 px pixel art. The grain of the last row copies
            the first, so the wrap stays exact.
check_tiles.py holds the installed files to the ported test rules; ground_masks.py (here) regenerates v1 / v2 mask.txt.
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
DEFAULT_OUT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
W, H, CELL = 896, 1792, 4
COLORS = 96
SEAM_ROWS = {448: 40, 224: 24}            # mirror band height (source rows) per block height
# painted HSV-value targets (median, p90, max) of the opaque texels; drawn ~1.8x brighter (mid lift 1.95)
TARGET = {
    "v1/sky": (.100, .175, .26),
    "v1/far": (.120, .220, .30),
    "v1/mid": (.150, .250, .34),
    "v2/far": (.120, .220, .30),
    "v2/mid": (.135, .245, .34),
}
FLOW_TARGET = {"v1/flow": (.30, .40, .42)}   # value of the wisps' own colour (median, p90, max)
FLOW_ALPHA = .55                              # their peak straight alpha
GAMMA_RANGE = (.6, 2.6)


def load(name):
    return np.asarray(Image.open(HERE / "sources" / name).convert("RGBA")).copy()


def content_rows(a):
    """Rows of a letterboxed drawing that hold content (not the white pad)."""
    ink = (a[..., :3].min(-1) < 250) & (a[..., 3] > 0)
    rows = np.where(ink.any(1))[0]
    return rows.min(), rows.max() + 1


def min_cut(err):
    """err (O, L): row r[i] per column minimising the summed error, |r[i] - r[i-1]| <= 1."""
    O, L = err.shape
    cost = np.empty_like(err, dtype=np.float64)
    cost[:, 0] = err[:, 0]
    for i in range(1, L):
        p = cost[:, i - 1]
        best = np.minimum(p, np.minimum(np.concatenate(([np.inf], p[:-1])), np.concatenate((p[1:], [np.inf]))))
        cost[:, i] = err[:, i] + best
    r = np.empty(L, np.int64)
    r[-1] = int(np.argmin(cost[:, -1]))
    for i in range(L - 2, -1, -1):
        j = r[i + 1]
        lo, hi = max(0, j - 1), min(O, j + 2)
        r[i] = lo + int(np.argmin(cost[lo:hi, i]))
    return r


def seamless(a):
    """Bottom band -> the top rows mirrored, below a min-error path: a[h-1] == a[0] afterwards."""
    h = a.shape[0]
    O = SEAM_ROWS[h]
    band = a[h - O:].astype(np.float32)
    mirror = a[:O][::-1].astype(np.float32)            # mirror[j] is src[O-1-j]: band row j -> out row h-O+j
    err = np.abs(band - mirror)
    err = err[..., :3].sum(-1) * (band[..., 3] + mirror[..., 3] + 1) / 511 + np.abs(band[..., 3] - mirror[..., 3])
    k = np.ones(3) / 3
    err = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)
    # keep the cut away from the band's first rows (room to blend) and force the last row to the mirror
    err[:4] += 1e6
    r = min_cut(err)
    use = np.arange(O)[:, None] >= r[None, :]
    out = a.copy()
    out[h - O:] = np.where(use[..., None], a[:O][::-1], a[h - O:])
    out[h - 1] = a[0]
    return out


def value(rgb):
    return rgb.max(-1)


def fit_curve(v, med_t, p90_t):
    """v' = hi * v^g through (median -> med_t, p90 -> p90_t)."""
    m, p = np.median(v), np.percentile(v, 90)
    m, p = max(m, 1e-3), max(p, 1e-3)
    if abs(np.log(p / m)) < 1e-3:
        g = GAMMA_RANGE[1]
    else:
        g = np.log(p90_t / med_t) / np.log(p / m)
    g = float(np.clip(g, *GAMMA_RANGE))
    hi = med_t / m ** g                                # the lane's median is the binding rule: hit it exactly
    return g, hi


def no_red(rgb):
    """Frost has no red / pink / magenta: a saturated texel with hue in [300, 20) turns amber (hue 32), value kept."""
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    with np.errstate(invalid="ignore", divide="ignore"):
        hue = np.where(mx == r, ((g - b) / np.where(d > 0, d, 1)) % 6,
                       np.where(mx == g, (b - r) / np.where(d > 0, d, 1) + 2, (r - g) / np.where(d > 0, d, 1) + 4)) * 60
    sat = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    bad = (d > 0) & (sat > .35) & ((hue >= 300) | (hue < 20))
    if bad.any():
        # amber at the same value and saturation: r = mx, b = mn, g = mn + d * 32 / 60
        out = rgb.copy()
        out[..., 0] = np.where(bad, mx, r)
        out[..., 1] = np.where(bad, mn + d * 32 / 60, g)
        out[..., 2] = np.where(bad, mn, b)
        return out, int(bad.sum())
    return rgb, 0


def grade(a, target, alpha=None):
    rgb = a[..., :3].astype(np.float64) / 255
    op = a[..., 3] > 0
    v = value(rgb)
    med_t, p90_t, cap = target
    g, hi = fit_curve(v[op], med_t, p90_t)
    nv = np.minimum(hi * np.power(v, g), cap)
    rgb = rgb * (nv / np.maximum(v, 1e-6))[..., None]
    rgb, n_red = no_red(rgb)
    out = a.copy()
    out[..., :3] = np.clip(np.rint(rgb * 255), 0, 255).astype(np.uint8)
    if alpha is not None:
        out[..., 3] = np.clip(np.rint(a[..., 3].astype(np.float64) * alpha), 0, 254).astype(np.uint8)
        out[out[..., 3] == 0] = 0
    return out, dict(gamma=round(g, 3), hi=round(float(hi), 3), red_fixed=n_red)


def limit_colors(a):
    flat = a.reshape(-1, 4)
    uniq = np.unique(flat, axis=0)
    if len(uniq) <= COLORS:
        return a, len(uniq)
    op = a[..., 3] > 0
    q = Image.fromarray(a[..., :3]).quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    out = a.copy()
    out[..., :3] = np.asarray(q.convert("RGB"))
    out[~op] = 0
    return out, COLORS


def pixel_pass(a, seed):
    big = np.repeat(np.repeat(a, CELL, 0), CELL, 1).astype(np.int16)
    rng = np.random.default_rng(seed)
    grain = rng.integers(-1, 2, size=big.shape[:2]).astype(np.int16)
    grain[-1] = grain[0]
    op = big[..., 3] > 0
    big[..., :3] += (grain * op)[..., None]
    big = np.clip(big, 0, 255).astype(np.uint8)
    big[~op] = 0
    return big


def build(key):
    v, layer = key.split("/")
    a = load(f"{v}_{layer}.png")
    if key == "v1/mid":
        y0, y1 = content_rows(a)
        a = a[y0:y1]
        if a.shape[0] != 224:
            raise SystemExit(f"v1_mid content band is {a.shape[0]} rows, expected 224")
    a = seamless(a)
    if layer == "flow":
        a, info = grade(a, FLOW_TARGET[key], FLOW_ALPHA)
    else:
        a[..., 3] = 255
        a, info = grade(a, TARGET[key])
    if a.shape[0] == 224:
        a = np.concatenate([a, a], 0)
    a, ncol = limit_colors(a)
    assert a.shape[:2] == (H // CELL, W // CELL), a.shape
    out = pixel_pass(a, seed=sum(map(ord, key)) * 7919)
    info["colors"] = ncol
    return out, info


ALL = ["v1/sky", "v1/far", "v1/mid", "v1/flow", "v2/far", "v2/mid"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("layers", nargs="*", default=ALL)
    args = ap.parse_args()
    out = Path(args.out)
    for key in args.layers:
        img, info = build(key)
        dst = out / f"{key}.png"
        dst.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(img, "RGBA").save(dst, optimize=True)
        print(f"{key}: {img.shape[1]}x{img.shape[0]} -> {dst}  {info}")


if __name__ == "__main__":
    main()
