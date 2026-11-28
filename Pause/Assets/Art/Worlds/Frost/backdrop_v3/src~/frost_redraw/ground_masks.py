#!/usr/bin/env python3
"""Regenerate v1 / v2 mask.txt (GroundPlacement: 32 x 64 cells, row 0 = top, W B O C) from the redrawn mid tiles.

    python3 ground_masks.py [<Resources/Worlds/Frost/Backdrop3>] [--debug DIR]

The method is Frost's own (../ground_masks.py: colour + edge statistics ranked by percentile per variant, majority
vote per cell, 3x3 mode filter), unchanged; that script measures a 512 x 1024 tile in 16 px cells, so the 896 x 1792 mid
is resized to 512 x 1024 (Lanczos) first, which gives the same 32 x 64 grid and the same blur radii per cell.
The per-variant rules are the original ones (OVERRIDES is the hook to retune them); v3 / v4 are not touched.

PAINTED PROPS (mark_props, after the vote): the colour statistics read the redrawn tiles' small painted props -- v1's crate
stacks, tent and lamp posts on the floes, v4's braziers and supply crates on the plateaus -- as plain sheet / plateau (C),
so the planner would stand a landmark or a launch site right on top of them. Every cell holding a cluster of their warm
lit pixels (amber lamps, fire, orange crate bands: never in the blue-grey ice) is marked B, grown by one cell for the
prop's unlit body. --props-only applies just that pass to the mask.txt already there (the masks are otherwise left as
checked against the tiles).
"""
import importlib.util
import os
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True     # no __pycache__ in the Unity tree
HERE = Path(__file__).resolve().parent
DEFAULT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"

spec = importlib.util.spec_from_file_location("frost_ground_masks", HERE.parent / "ground_masks.py")
gm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gm)

# Per-variant overrides of ../ground_masks.py's class shares. None: tried for v1 (built = dark_tex + lum, bshare .12 / .16)
# and it marks the gorge rims, not the domed fort, so the original rules stand.
OVERRIDES = {}

PROP_MIN = 6        # warm pixels (of the 896 x 1792 tile) that make a cell hold a painted prop


PROP_VARIANTS = (1, 4)   # the ice-field tiles with lone painted props; v2's quays / v3's plant are lit everywhere and the
                         # classifier already marks them B (marking every lit window there leaves too little land)


def mark_props(mid_rgb, grid):
    """B on every cell with a painted prop (warm lit pixels), grown by one cell; grid rows x cols, row 0 = top."""
    a = np.asarray(mid_rgb.convert("RGB")).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    warm = (r > g + 12) & (r > b + 25) & (r > 60)
    rows, cols = len(grid), len(grid[0])
    cnt = np.zeros((rows, cols), int)
    ys, xs = np.nonzero(warm)
    np.add.at(cnt, ((ys * rows) // a.shape[0], (xs * cols) // a.shape[1]), 1)
    hot = cnt >= PROP_MIN
    grown = hot.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            grown |= np.roll(np.roll(hot, dy, 0), dx, 1) & (np.arange(cols)[None, :] - dx >= 0) & (np.arange(cols)[None, :] - dx < cols)
    return ["".join("B" if grown[y, x] else grid[y][x] for x in range(cols)) for y in range(rows)], int(grown.sum())


def main():
    args = [a for a in sys.argv[1:]]
    dbg = None
    if "--debug" in args:
        i = args.index("--debug")
        dbg = args[i + 1]
        del args[i:i + 2]
    props_only = "--props-only" in args
    if props_only:
        args.remove("--props-only")
    base = Path(args[0]) if args else DEFAULT
    if props_only:
        for v in PROP_VARIANTS:
            path = base / f"v{v}" / "mask.txt"
            grid = [l for l in path.read_text().split("\n") if l]
            grid, n = mark_props(Image.open(base / f"v{v}" / "mid.png"), grid)
            path.write_text("\n".join(grid) + "\n")
            print(f"v{v}: {n} prop cells B")
        return
    for v in (1, 2, 3, 4):
        cfg = dict(gm.VARIANTS[v])
        cfg.update(OVERRIDES.get(v, {}))
        mid = Image.open(base / f"v{v}" / "mid.png").convert("RGB").resize((512, 1024), Image.LANCZOS)
        with tempfile.TemporaryDirectory() as td:
            p = os.path.join(td, "mid.png")
            mid.save(p)
            im, grid = gm.classify(p, cfg)
        assert grid.shape == (64, 32), grid.shape
        rows = ["".join(gm.CLASSES[c] for c in row) for row in grid]
        if v in PROP_VARIANTS:
            rows, _ = mark_props(Image.open(base / f"v{v}" / "mid.png"), rows)
        (base / f"v{v}" / "mask.txt").write_text("\n".join(rows) + "\n")
        print(f"v{v}", " ".join(f"{gm.CLASSES[k]} {(grid == k).mean():.2f}" for k in range(4)))
        if dbg:
            os.makedirs(dbg, exist_ok=True)
            cols = np.array([[0, 120, 255], [255, 60, 0], [255, 255, 0], [0, 200, 120]]) / 255.
            big = np.kron(grid, np.ones((gm.CELL, gm.CELL), int))
            lifted = np.minimum(1, im * 2.2)
            ov = lifted * .55 + cols[big] * .45
            Image.fromarray((np.concatenate([lifted, ov], 1) * 255).astype(np.uint8)).save(os.path.join(dbg, f"mask_v{v}.png"))


if __name__ == "__main__":
    main()
