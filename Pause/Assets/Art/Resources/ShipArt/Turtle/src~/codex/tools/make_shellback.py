"""Shellback = the special CHEVRON livery skin, composited explicitly (Codex painted dark patches instead of V shapes).
Run AFTER build_restyle.py Turtle: reads the final stock sheet Turtle.png, draws crisp slate "^" chevrons on the idle
cell, moves them with every cell (bob = shift, bank = the same affine map as the damage layer) and hides them where the
damage layer of that cell lies (so the chevrons look cracked / missing under damage). Writes Turtle_Shellback.bytes.
Chevron geometry (256 cell): three centre chevrons apex y 150 / 172 / 194, half width 46 / 38 / 30; flank chevrons at
x 84 and 172, apex y 96, half width 11. Slate #2C2D40, top edge #5A5C78, lower edge #1A1A28."""
import io
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.join(HERE, "..", "..", "..")
CELL = 256
BODY, HI, LO = (0x2C, 0x2D, 0x40), (0x5A, 0x5C, 0x78), (0x1A, 0x1A, 0x28)


def chevron(dr, cx, y0, hw, d, t):
    pts = [(cx, y0), (cx + hw, y0 + d), (cx + hw, y0 + d + t), (cx, y0 + t), (cx - hw, y0 + d + t), (cx - hw, y0 + d)]
    dr.polygon(pts, fill=BODY + (255,))
    # crisp 1 px edges: lighter on the two top arms, darker along the two lower arms
    dr.line([(cx - hw, y0 + d), (cx, y0), (cx + hw, y0 + d)], fill=HI + (255,), width=1)
    dr.line([(cx - hw, y0 + d + t), (cx, y0 + t), (cx + hw, y0 + d + t)], fill=LO + (255,), width=1)


def layer():
    im = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    for y0, hw in ((150, 46), (172, 38), (194, 30)):
        chevron(dr, 128, y0, hw, int(hw * 0.6), 8)
    chevron(dr, 84, 96, 11, 7, 5)
    chevron(dr, 172, 96, 11, 7, 5)
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
