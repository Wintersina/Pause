"""Shellback = the special livery skin, composited explicitly: four HELICOPTER PADS (cream ring + bold H on a slate-navy
disc, thin slate rim) on the stock hull, one on each side of the shell: top (128,38) between the red tail light and the
canopy gem, bottom (128,218) below the lower scute, left (82,150) and right (174,150) on the side plating (idle cell
px, radius 11). Run AFTER build_restyle.py Turtle: reads the final stock sheet Turtle.png, draws the pads on the idle
cell, moves them with every cell (bob = shift, bank = the same affine map as the damage layer) and hides them where the
damage layer of that cell lies. Writes Turtle_Shellback.bytes."""
import io
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.join(HERE, "..", "..", "..")
CELL = 256
BODY, HI, LO = (0x2C, 0x2D, 0x40), (0x5A, 0x5C, 0x78), (0x1A, 0x1A, 0x28)


CREAM, SAND = (0xF4, 0xEA, 0xD4), (0xE2, 0xBE, 0x84)
PADS = [(128, 38), (128, 218), (82, 150), (174, 150)]       # top, bottom, left, right (idle cell px)
R = 11


def helipad(dr, cx, cy):
    """flat round pad: slate-navy disc, cream ring, a thin slate outer edge, a bold cream H (pixel-crisp)."""
    dr.ellipse([cx - R - 1, cy - R - 1, cx + R + 1, cy + R + 1], fill=HI + (255,))
    dr.ellipse([cx - R, cy - R, cx + R, cy + R], fill=CREAM + (255,))
    dr.ellipse([cx - R + 2, cy - R + 2, cx + R - 2, cy + R - 2], fill=BODY + (255,))
    for dx in (-4, 3):                                       # the two uprights of the H, 2 px wide
        dr.rectangle([cx + dx - 1, cy - 6, cx + dx, cy + 6], fill=CREAM + (255,))
    dr.rectangle([cx - 4, cy - 1, cx + 3, cy], fill=CREAM + (255,))        # crossbar
    dr.line([(cx - 6, cy + R - 3), (cx + 5, cy + R - 3)], fill=LO + (255,), width=1)   # lower shade under the ring


def layer():
    im = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    for cx, cy in PADS:
        helipad(dr, cx, cy)
    return np.array(im)


def extent(al, band=None):
    rows = al if band is None else al[band[0]:band[1]]
    xs = np.nonzero(rows.any(axis=0))[0]
    return xs.min(), xs.max() + 1


def vext(al):
    ys = np.nonzero(al.any(axis=1))[0]
    return ys.min(), ys.max() + 1


def main():
    stock = np.array(Image.open(os.path.join(SHIP, "Turtle.png")).convert("RGBA"))
    out = stock.copy()
    lay = layer()
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    idle_al = stock[0:CELL, 0:CELL, 3] > 0
    for c in range(8):
        ci = stock[0:CELL, c * CELL:(c + 1) * CELL]
        ac = ci[..., 3] > 0
        if c in (6, 7):
            l0, r0 = extent(idle_al, (100, 150))
            lc, rc = extent(ac, (100, 150))
            t0, b0 = vext(idle_al)
            tc, bc = vext(ac)
            xk = l0 + (xx - lc) * ((r0 - l0) / (rc - lc))
            yk = t0 + (yy - tc) * ((b0 - t0) / (bc - tc))
        else:
            dy = {1: -4, 2: -4, 5: 4}.get(c, 0)
            xk, yk = xx, yy - dy
        xi = np.clip(np.round(xk).astype(int), 0, CELL - 1)
        yi = np.clip(np.round(yk).astype(int), 0, CELL - 1)
        L = lay[yi, xi]
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = stock[reg].copy()
            use = (L[..., 3] > 0) & ac
            # shell interior only: keep 4 px away from the silhouette edge
            inner = ac.copy()
            for _ in range(4):
                n = inner.copy()
                n[1:] &= inner[:-1]; n[:-1] &= inner[1:]; n[:, 1:] &= inner[:, :-1]; n[:, :-1] &= inner[:, 1:]
                inner = n
            use &= inner
            if r:
                dm = np.abs(cell[..., :3].astype(int) - ci[..., :3].astype(int)).sum(2) > 30
                n = dm.copy()
                n[1:] |= dm[:-1]; n[:-1] |= dm[1:]; n[:, 1:] |= dm[:, :-1]; n[:, :-1] |= dm[:, 1:]
                use &= ~n
            cell[use, :3] = L[use][:, :3]
            out[reg] = cell
    buf = io.BytesIO()
    Image.fromarray(out).save(buf, "PNG", optimize=True)
    open(os.path.join(SHIP, "Turtle_Shellback.bytes"), "wb").write(buf.getvalue())
    assert (out[..., 3] == stock[..., 3]).all()
    print("Shellback written")


main()
