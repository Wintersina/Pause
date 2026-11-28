#!/usr/bin/env python3
"""Frost mask.txt v2: WHERE THE GROUND IS FLAT (GroundPlacement: 64 x 128 cells of 14 px, row 0 = top, W B O C).

    python3 flat_masks.py [<Resources/Worlds/Frost/Backdrop3>] [--debug DIR]

Reads the 896 x 1792 masters (tiles_hd/v*/mid.png: the installed tiles are those halved, install_tiles.py) and writes
mask.txt into the installed folders.

Why: the first masks (ground_masks.py, 32 x 64 cells of 28 px) counted every textured light surface as land (C: "ice
sheet / pack / ridge"), and the landmarks / launch sites were allowed on it -- so a slab landed on cliff faces, on the
broken floe rubble between the plates, across gorges and crevasse rims (user, 2026-11-27: "still placed randomly over
cliffs and stuff"). The redrawn mid tiles are isometric: the FLAT surfaces are the lit tops of the ice plates / snow flats /
glacier plateaus (light, smooth); the cliff faces under them are vertical stripes (high local contrast), the leads and
crevasses dark. So this mask is read off the drawing at its own 2 px cell size:

    top   a 2 px cell whose 7-cell neighbourhood is light (mean luma > LIGHT[v]) and smooth (luma std < SMOOTH), opened by a
          disc of r 3 cells (no thin slivers: the lit rims of cliff faces, the tips of ridges, little floes), then closed by
          r 5 (the hairline cracks and mottling between two plates of one sheet are still one flat surface)
    W     a 14 px mask cell more than half dark (mean luma < DARK[v]): open leads, harbour basins, gorges, crevasses
    O     FLAT OPEN GROUND: at least TOP_SHARE of the cell is top, and no warm painted prop is in or next to it (lamps,
          braziers, crates: ground_masks.mark_props, grown by one cell)
    B     those props, and ground_masks.py's built class (quays, roads, plant) where it is not flat ground
    C     everything else that is not water: rubble, cliff faces, ridges, slopes -- nothing stands on it any more

FrostTuning's rules then put every landmark / launch site with its whole footprint AND a rim around it (PieceRule.rim) on O.
--debug writes, per variant, the tile with the classes over it (yellow O, green C, blue W, red B).
"""
import importlib.util
import os
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as nd

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
DEFAULT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
MASTERS = HERE / "tiles_hd"                  # the 896 x 1792 mid tiles the masks are read off (install_tiles.py halves them)

_spec = importlib.util.spec_from_file_location("frost_redraw_ground_masks", HERE / "ground_masks.py")
gmr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gmr)
gm = gmr.gm

COLS, ROWS = 64, 128
CELL = 2                                     # the HD mid's pixel cell
LIGHT = {1: .17, 2: .15, 3: .15, 4: .15}     # mean luma of a flat lit top (as painted: the tiles are lifted ~1.95 at draw time)
SMOOTH = .06                                 # luma std of a flat top over 7 cells
DARK = {1: .07, 2: .08, 3: .05, 4: .06}      # water / crevasse
TOP_SHARE = .5


def disc(r):
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    return x * x + y * y <= r * r


def top_map(lum, v):
    L = lum.reshape(lum.shape[0] // CELL, CELL, lum.shape[1] // CELL, CELL).mean((1, 3))
    w = lambda x, s: nd.uniform_filter(x, s, mode="wrap")
    mean = w(L, 7)
    std = np.sqrt(np.maximum(w(L * L, 7) - mean ** 2, 0))
    top = (mean > LIGHT[v]) & (std < SMOOTH)
    p = 8
    t = np.pad(top, p, mode="wrap")
    t = nd.binary_opening(t, disc(3))
    t = nd.binary_closing(t, disc(5))
    top = t[p:-p, p:-p]
    return top, mean


def cells(a, rows, cols):
    """Share of each mask cell that is True."""
    return a.reshape(rows, a.shape[0] // rows, cols, a.shape[1] // cols).mean((1, 3))


def built(v, mid_rgb):
    """(props, plant): the cells holding warm painted props (lamps, braziers, crates: ground_masks.mark_props on this grid,
    already grown by one cell), and ground_masks.py's built class (32 x 64, quays / roads / plant) on this grid."""
    cfg = dict(gm.VARIANTS[v])
    cfg.update(gmr.OVERRIDES.get(v, {}))
    small = mid_rgb.resize((512, 1024), Image.LANCZOS)
    with tempfile.TemporaryDirectory() as td:
        p = os.path.join(td, "mid.png")
        small.save(p)
        _, grid = gm.classify(p, cfg)
    plant = np.kron(grid == gm.CLASSES.index("B"), np.ones((2, 2), bool))
    rows, _ = gmr.mark_props(mid_rgb, ["C" * COLS for _ in range(ROWS)])
    props = np.array([[c == "B" for c in r] for r in rows])
    return props, plant


def classify(v, mid):
    a = np.asarray(mid.convert("RGB")).astype(np.float64) / 255
    lum = a @ np.array([.299, .587, .114])
    top, mean = top_map(lum, v)
    top_share = cells(top, ROWS, COLS)
    dark_share = cells(mean < DARK[v], ROWS, COLS)
    props, plant = built(v, mid)
    g = np.full((ROWS, COLS), "C")
    g[dark_share > .5] = "W"
    flat = (top_share >= TOP_SHARE) & ~props
    g[flat] = "O"
    # painted plant stays B where it is not a flat lit surface (a quay plaza or a snow flat the old classifier swept into its
    # built share is flat ground; a tank roof is flat too, but far too small for any footprint and its rim: FrostTuning)
    g[props | (plant & ~flat & (g != "W"))] = "B"
    return g, top


def main():
    args = sys.argv[1:]
    dbg = None
    if "--debug" in args:
        i = args.index("--debug")
        dbg = Path(args[i + 1])
        del args[i:i + 2]
    base = Path(args[0]) if args else DEFAULT
    for v in (1, 2, 3, 4):
        g, top = classify(v, Image.open(MASTERS / f"v{v}" / "mid.png"))
        (base / f"v{v}" / "mask.txt").write_text("\n".join("".join(r) for r in g) + "\n")
        print(f"v{v} " + " ".join(f"{c} {(g == c).mean():.2f}" for c in "WBOC"))
        if dbg:
            dbg.mkdir(parents=True, exist_ok=True)
            im = np.asarray(Image.open(MASTERS / f"v{v}" / "mid.png").convert("RGB")).astype(np.float64)
            im = np.minimum(255, im * 2.0)
            col = {"W": (0, 120, 255), "B": (255, 50, 0), "O": (255, 255, 0), "C": (0, 200, 120)}
            over = np.zeros_like(im)
            for k, c in col.items():
                over[np.kron(g == k, np.ones((14, 14), bool))] = c
            Image.fromarray((im * .62 + over * .38).astype(np.uint8)).save(dbg / f"flatmask_v{v}.png")


if __name__ == "__main__":
    main()
