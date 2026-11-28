#!/usr/bin/env python3
"""Regenerate v1 / v2 mask.txt (GroundPlacement: 32 x 64 cells, row 0 = top, W B O C) from the redrawn mid tiles.

    python3 ground_masks.py [<Resources/Worlds/Frost/Backdrop3>] [--debug DIR]

The method is Frost's own (../ground_masks.py: colour + edge statistics ranked by percentile per variant, majority
vote per cell, 3x3 mode filter), unchanged; that script measures a 512 x 1024 tile in 16 px cells, so the 896 x 1792 mid
is resized to 512 x 1024 (Lanczos) first, which gives the same 32 x 64 grid and the same blur radii per cell.
The per-variant rules are the original ones (OVERRIDES is the hook to retune them); v3 / v4 are not touched.
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


def main():
    args = [a for a in sys.argv[1:]]
    dbg = None
    if "--debug" in args:
        i = args.index("--debug")
        dbg = args[i + 1]
        del args[i:i + 2]
    base = Path(args[0]) if args else DEFAULT
    for v in (1, 2, 3):
        cfg = dict(gm.VARIANTS[v])
        cfg.update(OVERRIDES.get(v, {}))
        mid = Image.open(base / f"v{v}" / "mid.png").convert("RGB").resize((512, 1024), Image.LANCZOS)
        with tempfile.TemporaryDirectory() as td:
            p = os.path.join(td, "mid.png")
            mid.save(p)
            im, grid = gm.classify(p, cfg)
        assert grid.shape == (64, 32), grid.shape
        (base / f"v{v}" / "mask.txt").write_text("\n".join("".join(gm.CLASSES[c] for c in row) for row in grid) + "\n")
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
