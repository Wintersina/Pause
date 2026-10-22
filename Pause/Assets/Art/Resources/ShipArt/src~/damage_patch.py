"""Damage-layer composition for ships whose Codex job paints damage for the IDLE pose only (Turtle).

compose(img, cfg): img = sheet whose row r column 0 holds the key pose of damage state r (the intact row 0 holds
all nine intact cells). For rows 1-2 every column 1..7 = the intact cell of that column with the idle damage LAYER
stamped on: layer = pixels of the idle damaged cell (col 0) that differ from the intact idle cell, restricted to the
hand-placed `damage_zones` ellipses (state 2 = zones s1 + s2) and grown one pixel. The layer follows the cell:
bob columns = integer shift, bank columns = affine map of the idle cell onto the banked cell measured from the
intact alphas (shell width from the middle band rows cfg["bank_band"], nose/tail for y). Column 8 and column 0 are
left alone; alpha is the intact alpha exactly. So the damage sits on the same physical panels in every cell.
"""
import numpy as np
from PIL import Image

CELL = 256
COLS = 9


def _grow(m):
    n = m.copy()
    n[1:] |= m[:-1]
    n[:-1] |= m[1:]
    n[:, 1:] |= m[:, :-1]
    n[:, :-1] |= m[:, 1:]
    return n


def _extent(alpha, band=None):
    rows = alpha if band is None else alpha[band[0]:band[1]]
    xs = np.nonzero(rows.any(axis=0))[0]
    return xs.min(), xs.max() + 1


def _vext(alpha):
    ys = np.nonzero(alpha.any(axis=1))[0]
    return ys.min(), ys.max() + 1


def _ellipses(zones):
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
    m = np.zeros((CELL, CELL), bool)
    for cx, cy, rx, ry in zones:
        m |= ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1.0
    return m


def compose(img, cfg):
    z = cfg["damage_zones"]
    band = cfg.get("bank_band", [100, 150])
    a = np.array(img)
    out = a.copy()
    cells = lambda r, c: a[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
    idle = cells(0, 0)
    a0 = idle[..., 3] > 0
    zone1 = _ellipses(z["s1"])
    zone2 = zone1 | _ellipses(z["s2"])
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
    for st, zone in ((1, zone1), (2, zone2)):
        key = cells(st, 0)
        diff = (np.abs(key[..., :3].astype(int) - idle[..., :3].astype(int)).sum(2) > 30) & a0
        layer = _grow(diff & zone) & a0
        for c in range(1, COLS - 1):
            ci = cells(0, c)
            ac = ci[..., 3] > 0
            if c in (6, 7):
                l0, r0 = _extent(a0, band)
                lc, rc = _extent(ac, band)
                t0, b0 = _vext(a0)
                tc, bc = _vext(ac)
                # target pixel (x, y) <- idle pixel
                xk = l0 + (xx - lc) * ((r0 - l0) / (rc - lc))
                yk = t0 + (yy - tc) * ((b0 - t0) / (bc - tc))
            else:
                dy = {1: -4, 2: -4, 5: 4}.get(c, 0)
                xk, yk = xx, yy - dy
            xi = np.clip(np.round(xk).astype(int), 0, CELL - 1)
            yi = np.clip(np.round(yk).astype(int), 0, CELL - 1)
            use = layer[yi, xi] & ac
            cell = ci.copy()
            cell[use] = key[yi, xi][use]
            cell[..., 3] = ci[..., 3]
            out[st * CELL:(st + 1) * CELL, c * CELL:(c + 1) * CELL] = cell
    return Image.fromarray(out)
