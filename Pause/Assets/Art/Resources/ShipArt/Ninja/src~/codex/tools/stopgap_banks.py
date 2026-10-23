#!/usr/bin/env python3
"""Ninja spins and never banks in game, so cells 6 and 7 are a stopgap from the idle key pose.

Writes hull_redo_state{0,1,2}.png = hull_redo_v1_state{N}.png (cells 0, 1, 2, 5 kept: lights off, glints)
with cells 3 (bank left) and 4 (bank right) rebuilt from key_state{N}.png: the idle star squeezed ~5% in
width, shifted ~3 px (cell px at 256) toward the lean, the near half darkened a little. The damage is the
key's own, so it sits on the same panels. Usage: python3 stopgap_banks.py
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
C = os.path.dirname(HERE)
N = 1024


def is_green(a):
    v = a.astype(np.int16)
    return (v[..., 1] > 100) & (v[..., 1] > v[..., 0] * 1.55) & (v[..., 1] > v[..., 2] * 1.4) \
        & (v[..., 1] - v[..., 0] > 65) & (v[..., 1] - v[..., 2] > 65)


def bank(key_rgb, side):
    a = np.array(key_rgb)
    hull = ~is_green(a)
    rgba = np.dstack([a, hull.astype(np.uint8) * 255])
    im = Image.fromarray(rgba)
    w = int(round(N * 0.95))
    im = im.resize((w, N), Image.NEAREST)
    canvas = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    shift = 12 * side                      # ~3 px at the 256 cell
    canvas.alpha_composite(im, ((N - w) // 2 + shift, 0))
    arr = np.array(canvas)
    near = np.zeros((N, N), bool)
    if side < 0:
        near[:, :N // 2 + shift] = True
    else:
        near[:, N // 2 + shift:] = True
    m = near & (arr[..., 3] > 0)
    arr[m, :3] = (arr[m, :3].astype(np.float32) * 0.86).astype(np.uint8)
    out = np.zeros((N, N, 3), np.uint8)
    out[:] = (0, 255, 0)
    solid = arr[..., 3] > 0
    out[solid] = arr[solid, :3]
    return Image.fromarray(out)


for st in range(3):
    grid = Image.open(os.path.join(C, "hull_redo_v1_state%d.png" % st)).convert("RGB")
    key = Image.open(os.path.join(C, "key_state%d.png" % st)).convert("RGB")
    grid.paste(bank(key, -1), (0, N))          # cell 3 = row 1, col 0
    grid.paste(bank(key, +1), (N, N))          # cell 4 = row 1, col 1
    grid.save(os.path.join(C, "hull_redo_state%d.png" % st))
    print("wrote hull_redo_state%d.png" % st)
