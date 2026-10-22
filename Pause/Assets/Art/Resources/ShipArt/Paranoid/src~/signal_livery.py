#!/usr/bin/env python3
"""Paranoid "Signal" skin: an EXPLICIT diagonal livery composited on the stock hull (no Codex repaint).

    python3 signal_livery.py            (run after build_restyle.py / overlay_paranoid.py / patch_restyle.py)

For all 27 cells (+ the hit-flash column, copied unchanged from the existing skin file):
  * the band is a straight 45-degree strip (upper-left to lower-right) of constant perpendicular width through the disc
    centre, defined ONCE in the idle cell and mapped into every column with the same geometry the damage overlay uses
    (bob = integer shift, bank = the 8-landmark warp);
  * every stock hull pixel inside the band is repainted (an orange ramp driven by the pixel's own light/dark, so plate
    seams, rivets and rim light keep their shading and the band is one solid strip) EXCEPT the gem + its frame
    (an ellipse around the canopy), cyan glass, red lights / glowing tears and every pixel that differs from the
    intact cell (damage stays on top);
  * a thin cream line (2 px) runs along both straight edges of the band, a darker orange line just inside it;
  * alpha is the stock alpha, exactly.
"""
import io
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import overlay_paranoid  # noqa: E402,F401  (patches damage_overlay's landmarks for the disc-and-pods shape)
import damage_overlay as do  # noqa: E402
import shiplib  # noqa: E402

CELL = 256
CX, CY = 127.5, 128.0          # disc centre in the idle cell
WIDTH = 42.0                   # perpendicular width of the band (about a quarter of the ~165 px disc)
CREAM_W = 2.0                  # cream edge line, px
DARK_W = 2.0                   # darker orange line just inside it
RAMP = [(0.00, (0x4A, 0x20, 0x0C)), (0.25, (0xA9, 0x48, 0x1A)), (0.55, (0xF2, 0x86, 0x2B)),
        (0.85, (0xFF, 0xB4, 0x3C)), (1.00, (0xFF, 0xE0, 0x9A))]
CREAM = (0xF4, 0xEA, 0xD4)
DARK = (0xA9, 0x48, 0x1A)


def band_distance():
    """signed perpendicular distance of every idle-cell pixel from the band's centre line (45 deg, descending right)."""
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    return ((xx - CX) - (yy - CY)) / np.sqrt(2.0)


def to_pose(field, mode):
    if mode[0] == "shift":
        return np.roll(np.roll(field, mode[2], 0), mode[1], 1)
    sx, sy = mode[1], mode[2]
    xi = np.floor(sx + 0.5).astype(int)
    yi = np.floor(sy + 0.5).astype(int)
    ok = (xi >= 0) & (xi < CELL) & (yi >= 0) & (yi < CELL)
    out = np.full(field.shape, 1e3)
    out[ok] = field[yi[ok], xi[ok]]
    return out


def ramp(t):
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    for (t0, c0), (t1, c1) in zip(RAMP[:-1], RAMP[1:]):
        m = (t >= t0) & (t <= t1)
        f = ((t - t0) / (t1 - t0))[m][:, None]
        out[m] = np.array(c0) * (1 - f) + np.array(c1) * f
    return out


def livery_cell(cell, dist, keep, base):
    """cell: the stock cell to paint; keep: ellipse field (<1 = around the gem, left alone); base: the intact stock cell
    of the same column (pixels that differ from it are damage and stay on top)."""
    out = cell.copy()
    rgb = cell[..., :3].astype(int)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    al = cell[..., 3] > 0
    cyan = (b > r + 40) & (g > r + 40) & (b > 120)                    # gem glass and its glow
    reddish = (r > 120) & (r > g * 1.3)                               # lights, embers, glowing tears
    damage = np.abs(rgb - base[..., :3].astype(int)).sum(-1) > 40
    inside = np.abs(dist) <= WIDTH / 2
    band = al & inside & (keep >= 1.0) & ~cyan & ~reddish & ~damage
    lum = (0.299 * r + 0.587 * g + 0.114 * b)
    t = (lum - 25.0) / (200.0 - 25.0)
    col = ramp(t)
    out[band, :3] = col[band].astype(np.uint8)
    edge = np.abs(dist) > WIDTH / 2 - CREAM_W
    dark = (np.abs(dist) > WIDTH / 2 - CREAM_W - DARK_W) & ~edge
    out[band & dark, :3] = DARK
    out[band & edge, :3] = CREAM
    return out


def main():
    key = "Paranoid"
    stock = np.array(Image.open(shiplib.hull_path(key)).convert("RGBA"))
    sig_path = shiplib.skin_path(key, "Signal")
    old = np.array(Image.open(io.BytesIO(open(sig_path, "rb").read())).convert("RGBA"))
    geo, _ = do.stock_geometry(stock)
    dist0 = band_distance()
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    keep0 = ((xx - CX) / 21.0) ** 2 + ((yy - CY) / 33.0) ** 2      # the gem glass stays clear (the band runs behind it)
    out = np.zeros_like(stock)
    for r in range(3):
        for c in range(9):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            if c == 8:
                out[reg] = old[reg]                       # hit flash: unchanged
                continue
            d = to_pose(dist0, geo[c])
            k = to_pose(keep0, geo[c])
            out[reg] = livery_cell(stock[reg], d, k, stock[0:CELL, c * CELL:(c + 1) * CELL])
    assert (out[..., 3] == stock[..., 3]).all()
    buf = io.BytesIO()
    Image.fromarray(out).save(buf, "PNG", optimize=True)
    open(sig_path, "wb").write(buf.getvalue())
    print("wrote", sig_path)


if __name__ == "__main__":
    main()
