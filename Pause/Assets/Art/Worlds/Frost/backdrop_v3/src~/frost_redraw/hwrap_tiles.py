#!/usr/bin/env python3
"""Make the installed Frost ground tiles seamless ACROSS (left edge against right edge), in place.

    python3 hwrap_tiles.py [<Resources/Worlds/Frost/Backdrop3>] [--check] [v1/mid v2/flow ...]

Why: the ground is drawn from higher up (FrostTuning.GroundZoom < 1), so a tile no longer spans the view: BackdropTile lays
copies side by side as well as stacked, and every copy's right edge meets the next copy's left edge. build_tiles.py only
made the tiles seamless down the scroll (top row vs bottom row), so the left / right edges met with a hard break.

How (the same two-cut quilt build_tiles.seamless() does down the tile, turned sideways): a 2 x O column strip
src[:, k : k + 2O] found elsewhere in the same tile (the k with the least error against both bands) is spliced over the
wrap. Its first half replaces the right band (cut in along a minimum-error path, so the tile's own columns are kept where
they already agree), its second half the left band, so the tile's last column and its first column are columns that were
neighbours in the drawing: the wrap is invisible. The cuts follow the art's pixel cells (2 px for the HD mid / flow, 4 px
for the sky / far), so nothing is resampled or blurred, and each path is CYCLIC (its first and last rows are at most one
cell apart), so the vertical wrap stays exactly as seamless as before (check_tiles.py: top vs bottom row).

Run it after build_tiles.py (which rebuilds the tiles from the drawings, seamless down only), then frost_redraw/
flat_masks.py (the masks are read off the final mid). --check only reports each tile's left / right seam.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
DEFAULT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
LAYERS = ("sky", "far", "mid", "flow")
BAND = 8                 # the band is 1/BAND of the tile's width on each side
# A wrap counts as seamless when its premultiplied mean |left column - right column| (check_tiles.py's vertical seam
# measure, turned sideways) is no worse than the p90 of the tile's own neighbouring columns across a cell boundary.


def cell_of(v, layer):
    """2 px cells for the HD redraws (sources_hd), 4 px for the rest (build_tiles.load)."""
    return 2 if (HERE / "sources_hd" / f"v{v}_{layer}.png").exists() else 4


def hseam(a):
    px = a.astype(np.float64) / 255
    l, r = px[:, 0], px[:, -1]
    pl, pr = l[:, :3] * l[:, 3:], r[:, :3] * r[:, 3:]
    return float((np.abs(pl - pr).sum(-1) + np.abs(l[:, 3] - r[:, 3])).sum() / (px.shape[0] * 4))


def adjacent(a, cell):
    """(median, p90) of the seam measure between neighbouring columns across every cell boundary inside the tile."""
    px = a.astype(np.float64) / 255
    vals = []
    for c in range(cell, px.shape[1], cell):
        l, r = px[:, c - 1], px[:, c]
        pl, pr = l[:, :3] * l[:, 3:], r[:, :3] * r[:, 3:]
        vals.append((np.abs(pl - pr).sum(-1) + np.abs(l[:, 3] - r[:, 3])).sum() / (px.shape[0] * 4))
    return float(np.median(vals)), float(np.percentile(vals, 90))


def cyclic_path(err):
    """Minimum-cost top-to-bottom path through err (rows x cols), one column per row, steps of at most one column, with its
    last row at most one column from its first (the tile repeats vertically). Columns 0 and cols - 1 are never cut on."""
    rows, cols = err.shape
    big = 1e18
    best_cost, best_path = big, None
    inner = np.arange(1, cols - 1)
    for s in inner:
        cost = np.full(cols, big)
        cost[s] = err[0, s]
        back = np.zeros((rows, cols), np.int16)
        for r in range(1, rows):
            left = np.concatenate([[big], cost[:-1]])
            right = np.concatenate([cost[1:], [big]])
            stack = np.stack([left, cost, right])
            k = stack.argmin(0)
            cost = stack[k, np.arange(cols)] + err[r]
            cost[0] = cost[-1] = big
            back[r] = np.arange(cols) + k - 1
        ends = [c for c in (s - 1, s, s + 1) if 1 <= c <= cols - 2]
        e = min(ends, key=lambda c: cost[c])
        if cost[e] < best_cost:
            best_cost = cost[e]
            path = np.zeros(rows, int)
            path[-1] = e
            for r in range(rows - 1, 0, -1):
                path[r - 1] = back[r, path[r]]
            best_path = path
    return best_path, best_cost


def quilt(a, cell):
    """a: H x W x 4 uint8 -> a copy seamless across (see the module doc)."""
    H, W = a.shape[:2]
    hc, wc = H // cell, W // cell
    A = a.reshape(hc, cell, wc, cell, 4).astype(np.float64).mean((1, 3)) / 255
    A = np.concatenate([A[..., :3] * A[..., 3:], A[..., 3:]], -1)       # premultiplied: transparent texels agree
    O = wc // BAND
    med, p90 = adjacent(a, cell)
    best = None
    for k in range(O, wc - 3 * O + 1):
        e1 = ((A[:, wc - O:] - A[:, k:k + O]) ** 2).sum(-1)              # right band vs the strip's first half
        e2 = ((A[:, :O] - A[:, k + O:k + 2 * O]) ** 2).sum(-1)           # left band vs its second half
        score = e1.mean() + e2.mean()
        # the new wrap is the pixel pair at the strip's middle: never one of the tile's busiest column pairs
        pair = hseam(a[:, (k + O) * cell - 1:(k + O) * cell + 1])
        if pair > med:
            score += 1e3 * (pair - med)
        if best is None or score < best[0]:
            best = (score, k, e1, e2)
    _, k, e1, e2 = best
    p1, _ = cyclic_path(e1)          # right band: the tile's own columns left of p1, the strip's from p1 on
    p2, _ = cyclic_path(e2)          # left band: the strip's columns left of p2, the tile's own from p2 on
    out = a.copy()
    for r in range(hc):
        ys = slice(r * cell, (r + 1) * cell)
        c0 = p1[r]
        out[ys, (wc - O + c0) * cell:] = a[ys, (k + c0) * cell:(k + O) * cell]
        c1 = p2[r]
        out[ys, :c1 * cell] = a[ys, (k + O) * cell:(k + O + c1) * cell]
    return out, k, O


def main():
    args = sys.argv[1:]
    check_only = "--check" in args
    args = [x for x in args if x != "--check"]
    base = Path(args[0]) if args and not args[0].startswith("v") else DEFAULT
    picks = [x for x in args if x.startswith("v") and "/" in x]
    todo = picks or [f"v{v}/{l}" for v in (1, 2, 3, 4) for l in LAYERS]
    bad = 0
    for t in todo:
        v, layer = int(t[1]), t.split("/")[1]
        p = base / f"{t}.png"
        a = np.asarray(Image.open(p).convert("RGBA")).copy()
        cell = cell_of(v, layer)
        before = hseam(a)
        med, p90 = adjacent(a, cell)
        if check_only:
            ok = before <= p90 + 1e-4
            print(f"{t}: left / right seam {before:.4f} (its own cell boundaries: median {med:.4f}, p90 {p90:.4f})" +
                  ("" if ok else "  NOT seamless across"))
            bad += not ok
            continue
        if before <= med:
            print(f"{t}: already seamless across ({before:.4f} <= median {med:.4f})")
            continue
        out, k, O = quilt(a, cell)
        Image.fromarray(out, "RGBA").save(p)
        print(f"{t}: left / right seam {before:.4f} -> {hseam(out):.4f} (strip at cell {k}, band {O} cells)")
    sys.exit(bad)


if __name__ == "__main__":
    main()
