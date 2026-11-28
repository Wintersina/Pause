#!/usr/bin/env python3
"""Frost ground tiles from the approved PixelLab drawings (224 x 448 pixel art, top-down, scrolling vertically).

    python3 build_tiles.py [--out <Resources/Worlds/Frost/Backdrop3>] [v1/sky v1/far ...]

Installs (overwrites the PNG bytes in place, the .meta files are untouched):
    v1/sky v1/far v1/mid v1/flow  v2/far v2/mid      (sources/<v>_<layer>.png; the old tiles are in old/)
The other v2 layers and v3 / v4 are not part of this redraw.

Per layer, all in the 224-wide source grid (one source pixel = one 4 x 4 cell of the 896 x 1792 tile):
  1. CROP   a 224 x 224 drawing letterboxed in white (none now: v1_mid is the full-height r1_2; v3_mid's 415-row band is lengthened by extend()) would have its band cut
            out and repeated twice down the tile after step 2. flow: the faintest wisps are dropped to FLOW_COVER.
  2. SEAM   two-cut quilt (seamless()): a 2 x O row strip found elsewhere in the same drawing is spliced over the
            wrap, cut in and out along minimum-error paths (hard cuts where the pieces already agree: no mirror, no
            blur), so the last row and the first row are rows that were neighbours in the drawing.
  3. GRADE  HSV-value remap, hue and saturation kept: v' = hi * v^g, fitted so the layer's median and p90 land
            on the painted targets below (the art is painted dark and lifted ~1.95x at draw time by
            BackdropGrade / FrostTuning.Brightness; the targets keep WorldBackdropTest's as-drawn caps, the
            lane readability rule and HudButtonsTest with margin). Saturated red / pink is turned to amber.
            flow: colours graded the same way and alpha scaled to FLOW_ALPHA (straight alpha, never 255).
  4. PIXEL  <= 96 colours (the drawings have 23-43, so this is a no-op guard), nearest 4x to 896 x 1792 (4 px
            cells, the Desert pixel-pass grid), then a per-texel grain: every opaque texel's RGB moves by the same
            -1 / 0 / +1 (seeded), so no 2x2 / 3x3 / 4x4 block is uniform (BackdropResolutionTest rejects nearest
            upscales at >= .97) while the look stays the crisp 4 px pixel art. The tile is rolled down by half a
            cell (2 px) so its top and bottom rows are the two halves of one cell row, and the grain of the last
            row copies the first: WorldBackdropTest's seam (top row vs bottom row) measures ~0.
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
SEAM_ROWS = {448: 28, 224: 12}            # quilt band height (source rows) per block height
DETAIL_WEIGHT = .5                        # quilt: cost of repeating a busy strip vs a plain one
ROLL = CELL // 2                          # output rows the tile is rolled by: the wrap falls inside one 4 px cell
# painted HSV-value targets (median, p90, max) of the opaque texels; drawn ~1.8x brighter (mid lift 1.95)
TARGET = {
    "v1/sky": (.110, .185, .27),
    "v1/far": (.140, .250, .32),
    "v1/mid": (.160, .280, .35),
    "v2/far": (.140, .250, .32),
    "v2/mid": (.165, .290, .36),
    "v3/far": (.140, .250, .32),
    "v3/mid": (.160, .280, .35),
    "v4/far": (.140, .250, .32),
}
FLOW_TARGET = {"v1/flow": (.30, .40, .42), "v2/flow": (.30, .40, .42), "v3/flow": (.30, .40, .42)}   # value of the wisps' own colour (median, p90, max)
FLOW_COVER = .245                             # share of flow texels that may show (skill verifier: <= .25)
FLOW_ALPHA = .55                              # their peak straight alpha
GAMMA_RANGE = (.6, 1.4)              # flatter than .6 washes the darks out, steeper than 1.4 crushes them


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


def pair_err(A, B):
    """Per-texel disagreement of two RGBA row stacks (alpha-weighted colour + alpha)."""
    A = A.astype(np.float32)
    B = B.astype(np.float32)
    e = np.abs(A[..., :3] - B[..., :3]).sum(-1) * (A[..., 3] + B[..., 3] + 1) / 511 + np.abs(A[..., 3] - B[..., 3])
    k = np.ones(3) / 3
    return np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, e)


def cut(err, lo, hi):
    """Min-error path restricted to rows [lo, hi) of the band; returns (rows, cost)."""
    e = err.copy()
    e[:lo] += 1e6
    e[hi:] += 1e6
    r = min_cut(e)
    return r, float(err[r, np.arange(err.shape[1])].sum())


def seamless(a):
    """Two-cut quilt: a strip src[k : k + 2O] found elsewhere in the drawing is spliced in at the wrap.

    Its upper half replaces the bottom band (cut in below a min-error path where it agrees with the original), its
    lower half replaces the top band (cut out above a min-error path); the wrap itself then joins src[k+O-1] to
    src[k+O], two rows that were neighbours in the drawing. No mirror, no blend: every join is a hard cut along the
    texels where the two pieces already agree. k is the offset with the least total cut error plus a penalty for
    the strip's own detail (a strip of plain ice or water repeats unnoticed, a building does not), searched at least
    3 O rows away from both bands so the repeated strip never sits next to its original.
    """
    h = a.shape[0]
    O = SEAM_ROWS[h]
    bottom, top = a[h - O:], a[:O]
    best = None
    f = a.astype(np.float32)
    detail = (np.abs(np.diff(f[..., :3], axis=0)).sum(-1) * (f[1:, :, 3] + 1) / 256).sum(1)   # per row pair
    for k in range(3 * O, h - 5 * O):
        e1 = pair_err(bottom, a[k:k + O])            # bottom band: original above the cut, patch below
        e2 = pair_err(a[k + O:k + 2 * O], top)       # top band: patch continuation above the cut, original below
        penalty = DETAIL_WEIGHT * detail[k:k + 2 * O - 1].sum() / (2 * O)
        quick = e1.min(0).sum() + e2.min(0).sum() + penalty
        if best is not None and quick >= best[0]:
            continue
        r1, c1 = cut(e1, 2, O)                       # >= 2 original rows kept at the band's top; last row is patch
        r2, c2 = cut(e2, 1, O - 1)                   # row 0 is patch continuation; last row original
        if best is None or c1 + c2 + penalty < best[0]:
            best = (c1 + c2 + penalty, k, r1, r2)
    _, k, r1, r2 = best
    rows = np.arange(O)[:, None]
    out = a.copy()
    out[h - O:] = np.where((rows >= r1[None, :])[..., None], a[k:k + O], bottom)
    out[:O] = np.where((rows < r2[None, :])[..., None], a[k + O:k + 2 * O], top)
    return out, k


def extend(a, h_out, O=12):
    """A drawing band shorter than the tile (v3_mid: 415 of 448 rows between white pads) is lengthened by quilting:
    a strip src[k : k + n] (n = the missing rows) is appended below it, joined along a min-error cut over an O-row
    overlap where src[k - O : k] agrees with the band's bottom; k is the offset with the least cut error plus the
    detail penalty, at least 3 O away from the band's ends. The tile's wrap is then made seamless as usual."""
    h = a.shape[0]
    n = h_out - h
    if n <= 0:
        return a, None
    f = a.astype(np.float32)
    detail = (np.abs(np.diff(f[..., :3], axis=0)).sum(-1) * (f[1:, :, 3] + 1) / 256).sum(1)
    bottom = a[h - O:]
    best = None
    for k in range(3 * O + O, h - n - 3 * O):
        e = pair_err(bottom, a[k - O:k])
        penalty = DETAIL_WEIGHT * detail[k - O:k + n - 1].sum() / (n + O)
        if best is not None and e.min(0).sum() + penalty >= best[0]:
            continue
        r, c = cut(e, 2, O)
        if best is None or c + penalty < best[0]:
            best = (c + penalty, k, r)
    _, k, r = best
    rows = np.arange(O)[:, None]
    joint = np.where((rows >= r[None, :])[..., None], a[k - O:k], bottom)
    return np.concatenate([a[:h - O], joint, a[k:k + n]], 0), k


def trim_flow(a, cover):
    """Drop the faintest wisps (connected pieces, least visible first: size x mean value) until <= cover of texels show."""
    from scipy import ndimage
    op = a[..., 3] > 0
    lab, n = ndimage.label(op, structure=np.ones((3, 3)))
    if op.mean() <= cover or n == 0:
        return a
    ids = np.arange(1, n + 1)
    size = ndimage.sum(op, lab, ids)
    val = ndimage.mean(a[..., :3].max(-1).astype(np.float64), lab, ids)
    out = a.copy()
    left = op.sum()
    for i in np.argsort(size * val):
        if left <= cover * op.size:
            break
        out[lab == ids[i]] = 0
        left -= size[i]
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
    big = np.roll(np.repeat(np.repeat(a, CELL, 0), CELL, 1), ROLL, 0).astype(np.int16)
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
    if layer != "flow":
        y0, y1 = content_rows(a)
        if y1 - y0 < a.shape[0]:                   # a letterboxed square drawing: its band, repeated twice
            a = a[y0:y1]
            if a.shape[0] > 224:                   # a nearly full-height drawing between white pads: quilt it to 448
                a, ek = extend(a, 448)
                print(f"{key}: content rows {y0}..{y1} lengthened to 448 by a strip from row {ek}")
            elif a.shape[0] != 224:
                raise SystemExit(f"{key}: content band is {a.shape[0]} rows, expected 224 or 448")
    a, k = seamless(a)
    if layer == "flow":
        a = trim_flow(a, FLOW_COVER)
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
    info["quilt_k"] = k
    return out, info


ALL = ["v1/sky", "v1/far", "v1/mid", "v1/flow", "v2/far", "v2/mid", "v2/flow", "v3/far", "v3/mid", "v3/flow", "v4/far"]


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
