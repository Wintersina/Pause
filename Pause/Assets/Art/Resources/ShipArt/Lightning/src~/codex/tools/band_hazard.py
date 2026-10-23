#!/usr/bin/env python3
"""Lightning Hazard skin = the finished STOCK sheet with one explicit diagonal band composited on the armour.

Not a Codex repaint: the band is a hard-edged geometric mask (45 degrees, lower left to upper right, sharp
stair-stepped edges), mapped through the same bob (y shift) / bank (x stretch) mapping as the damage zones so it
sits on the same physical parts in every cell. Armour pixels (yellow plating and the red wing bolts) inside the
band are recoloured onto a gunmetal ramp by their own luminance (panel seams, rivets and rim lights survive);
canopy, nozzles, lights, damage (scorch, cavities, sparks) and the hit-flash column are untouched, so damage
sits on top of the band. Alpha is the stock alpha. Run after damage_overlay.py has finished the stock sheet:

    python3 band_hazard.py
"""
import colorsys
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CELL = 256
CENTRE = 262        # band centre line: x + y = CENTRE in idle-cell pixels (45 degrees, lower left to upper right)
HALF = 31           # half width in x + y units  (perpendicular width = 2 * HALF / sqrt(2) = ~44 px)
SHADOW, BASE, LIGHT = (0x1A, 0x1A, 0x28), (0x2C, 0x2D, 0x40), (0x5A, 0x5C, 0x78)


def band_mask(col, bbox0, bbox_c):
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
    dy = {1: -4, 2: -4, 5: 4}.get(col, 0)
    yk = yy - dy
    if col in (6, 7):
        kw = bbox0[1] - bbox0[0] + 1
        bw = bbox_c[1] - bbox_c[0] + 1
        xk = bbox0[0] + (xx - bbox_c[0]) * (kw / bw)
    else:
        xk = xx
    return np.abs(np.floor(xk + 0.5) + np.floor(yk + 0.5) - CENTRE) <= HALF


def armour(rgb):
    """yellow plating (hue 38-68 deg) and the red bolts, as a bool mask"""
    r, g, b = [rgb[..., i].astype(float) / 255 for i in range(3)]
    mx, mn = np.maximum(np.maximum(r, g), b), np.minimum(np.minimum(r, g), b)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    yellow = (h >= 38) & (h <= 68) & (s > 0.45) & (v > 0.28)
    red = ((h <= 12) | (h >= 348)) & (s > 0.6) & (v > 0.45)
    return yellow | red


def ramp(lum):
    t = np.clip((lum - 50.0) / (250.0 - 50.0), 0, 1)[..., None]
    sh, ba, li = [np.array(c, float) for c in (SHADOW, BASE, LIGHT)]
    lo = sh + (ba - sh) * np.clip(t / 0.5, 0, 1)
    hi = ba + (li - ba) * np.clip((t - 0.5) / 0.5, 0, 1)
    return np.where(t < 0.5, lo, hi)


def main():
    st = np.array(Image.open(os.path.join(SHIP, "Lightning.png")).convert("RGBA"))
    out = st.copy()
    ys, xs = np.where(st[0:CELL, 0:CELL, 3] > 0)
    b0 = (xs.min(), xs.max())

    def bbox(c):
        xs_ = np.where(st[0:CELL, c * CELL:(c + 1) * CELL, 3].any(axis=0))[0]
        return (xs_.min(), xs_.max())
    for c in range(8):          # column 8 = hit flash, stays bone-white
        m = band_mask(c, b0, bbox(c))
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = st[reg]
            sel = m & (cell[..., 3] > 0) & armour(cell[..., :3])
            lum = cell[..., 0] * 0.3 + cell[..., 1] * 0.59 + cell[..., 2] * 0.11
            col = ramp(lum)
            o = out[reg]
            o[sel, :3] = col[sel].astype(np.uint8)
            out[reg] = o
    assert (out[..., 3] == st[..., 3]).all()
    Image.fromarray(out).save(os.path.join(SHIP, "Lightning_Hazard.bytes"), format="PNG")
    print("Hazard written; band pixels recoloured in row 0 col 0:",
          int(((out[0:CELL, 0:CELL, :3] != st[0:CELL, 0:CELL, :3]).any(-1)).sum()))


if __name__ == "__main__":
    main()
