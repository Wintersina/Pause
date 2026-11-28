#!/usr/bin/env python3
"""Install the Frost ground tiles at the size they are SEEN at: tiles_hd/ (896 x 1792, 2 px art cells) -> 448 x 896.

    python3 install_tiles.py [<Resources/Worlds/Frost/Backdrop3>] [--check]

Why: Frost is seen from high up (FrostTuning.GroundZoom .41): a tile is drawn 0.41 x the view's width x 1.02 overscan,
~450 screen px on a 1080 px wide phone, so the 896 px tile was minified ~2x. Point sampled (the backdrop has no mipmaps,
and Unity keeps a non-power-of-two mipmapped texture uncompressed: AndroidTextureDiet), that skips every other texel and
the ground shimmers as it scrolls. Halved here with a proper filter instead, each texel is one 2 px art cell of the drawing
(the sky / far's 4 px cells two texels), ~1 texel per screen pixel, drawn BILINEAR (WorldBackdropImport.IsFrostHdTile):
nothing to alias on any phone, and a quarter of the memory.

How: every 2 x 2 block averaged in LINEAR light (sRGB decoded, averaged, re-encoded), alpha premultiplied so transparent
texels of the flow strip add no colour, then un-premultiplied. The 2 x 2 blocks line up with the art's 2 px cells (and
build_tiles rolled the tile by half a 4 px cell, so the sky / far halve cleanly too). The vertical and across seams stay
seamless (the masters are: build_tiles.py, hwrap_tiles.py; a wrap the halving roughened is re-quilted, seamless()),
the per-texel grain averages to a gentler one.

Pipeline: build_tiles.py -> hwrap_tiles.py on tiles_hd/ (the 896 masters) -> flat_masks.py tiles_hd (the masks are read
off the 896 mid) -> this. check_tiles.py checks the installed result.
"""
import sys
from pathlib import Path

import importlib.util

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
MASTERS = HERE / "tiles_hd"
DEFAULT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
LAYERS = ("sky", "far", "mid", "flow")
_spec = importlib.util.spec_from_file_location("frost_hwrap", HERE / "hwrap_tiles.py")
hw = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(hw)
SIZE = (448, 896)


def to_lin(c):
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def to_srgb(c):
    return np.where(c <= .0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - .055)


def phase(a, axis):
    """0 or 1: the offset at which the 2 x 2 blocks fall inside the art's cells along `axis` (the one whose block pairs
    differ least). build_tiles rolled the tiles by half a cell so their top and bottom rows are one cell row: there the
    cells start at row 1, and the block holding the wrap is the last row with the first."""
    g = a[..., :3].astype(np.float64).mean(-1)
    d = np.abs(np.diff(g, axis=axis))
    even = d.take(np.arange(0, d.shape[axis], 2), axis).mean()   # pairs (0,1), (2,3) ...
    odd = d.take(np.arange(1, d.shape[axis], 2), axis).mean()    # pairs (1,2), (3,4) ...
    return 0 if even <= odd else 1


def halve(a):
    """Box-halve on the art's own cell grid (rolled onto it first: the drawing moves by one master pixel, ~half a screen pixel)."""
    py, px_ = phase(a, 0), phase(a, 1)
    a = np.roll(a, (-py, -px_), (0, 1))
    px = a.astype(np.float64) / 255
    rgb, al = to_lin(px[..., :3]), px[..., 3:]
    pre = rgb * al
    H, W = px.shape[:2]
    blk = lambda x: x.reshape(H // 2, 2, W // 2, 2, x.shape[-1]).mean((1, 3))
    pre, al = blk(pre), blk(al)
    rgb = np.where(al > 1e-6, pre / np.maximum(al, 1e-6), 0)
    out = np.concatenate([to_srgb(np.clip(rgb, 0, 1)), al], -1)
    return np.clip(np.round(out * 255), 0, 255).astype(np.uint8)


SEAM_FIX = .016          # a halved tile whose wrap (down or across) measures over this is re-quilted (WorldBackdropTest: .02)


def seamless(a):
    """The halved tile with its wraps checked: halving puts two different art cells either side of a wrap that was one cell
    row in the master, and where that row is busy (floe edges) the wrap can measure over WorldBackdropTest's seam. Such a
    wrap is re-quilted at the new size (hwrap_tiles.quilt, cell 1: a strip of the tile's own rows spliced over it along
    cyclic minimum-error cuts), down the tile by turning it on its side."""
    for axis in (0, 1):
        t = np.swapaxes(a, 0, 1) if axis == 0 else a          # quilt() mends the left / right wrap
        if hw.hseam(t) > SEAM_FIX:
            t, _, _ = hw.quilt(np.ascontiguousarray(t), 1)
            a = np.ascontiguousarray(np.swapaxes(t, 0, 1)) if axis == 0 else t
    return a


def main():
    args = sys.argv[1:]
    check = "--check" in args
    args = [x for x in args if x != "--check"]
    base = Path(args[0]) if args else DEFAULT
    bad = 0
    for v in (1, 2, 3, 4):
        for layer in LAYERS:
            src = np.asarray(Image.open(MASTERS / f"v{v}" / f"{layer}.png").convert("RGBA"))
            want = seamless(halve(src))
            dst = base / f"v{v}" / f"{layer}.png"
            if check:
                have = np.asarray(Image.open(dst).convert("RGBA"))
                ok = have.shape == want.shape and np.abs(have.astype(int) - want).max() <= 1
                print(f"v{v}/{layer}: {'installed from its master' if ok else 'NOT the halved master'}")
                bad += not ok
                continue
            Image.fromarray(want, "RGBA").save(dst)
            print(f"v{v}/{layer}: {src.shape[1]} x {src.shape[0]} -> {want.shape[1]} x {want.shape[0]}")
    sys.exit(bad)


if __name__ == "__main__":
    main()
