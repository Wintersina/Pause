"""Post-build repair of Codex skin artefacts (run after build_restyle.py Turtle, before the bake):
Night / Bone carry a semi-transparent orange rectangle around the centre lamp and orange speckles left over from the
stock armour. On the shell (not the lamp, red lights, nozzle or the damage layer) every orange-tinted pixel is
re-filled from its non-orange neighbours, iteratively. Damage pixels (cells that differ from the intact row 0 cell of
the same column) are never touched."""
import io
import os
import sys
import numpy as np
from PIL import Image

D = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
CELL = 256
NAVY = (0.66, 0.72, 1.12)
BONE = (1.0, 0.97, 0.88)


def orange(rgb):
    r, g, b = [rgb[..., i].astype(int) for i in range(3)]
    mx = np.maximum(np.maximum(r, g), b)
    mn = np.minimum(np.minimum(r, g), b)
    sat = (mx - mn) / np.maximum(mx, 1)
    return (r >= g * 0.92) & (g > b) & (sat > 0.2) & (r - b > 22)


def lamp_centre(stock_cell):
    """centre of the amber lamp (bright warm pixels in the middle window of the STOCK cell) or None."""
    w = stock_cell[110:175, 96:160].astype(int)
    m = (w[..., 0] > 235) & (w[..., 1] > 150) & (w[..., 2] < 170) & (w[..., 3] > 0)
    if m.sum() < 3:
        return None
    ys, xs = np.nonzero(m)
    return xs.mean() + 96, ys.mean() + 110


def fix(path):
    a = np.array(Image.open(path).convert("RGBA"))
    stock = np.array(Image.open(os.path.join(D, "Turtle.png")).convert("RGBA"))
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    total = 0
    for c in range(8):
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = a[reg]
            sc = stock[reg]
            al = cell[..., 3] > 0
            shell = (np.abs(xx - 128) < 64) & (yy > 40) & (yy < 226) & al
            bad = orange(cell[..., :3]) & shell
            lc = lamp_centre(stock[0:CELL, c * CELL:(c + 1) * CELL])
            if lc is not None:
                bad &= ((xx - lc[0]) ** 2 + (yy - lc[1]) ** 2) > 5 ** 2
            if r:      # never the damage layer (where the stock sheet's damaged cell differs from its intact cell)
                dm = (np.abs(sc[..., :3].astype(int) - stock[0:CELL, c * CELL:(c + 1) * CELL, :3].astype(int)).sum(2) > 30)
                for _ in range(2):
                    n = dm.copy()
                    n[1:] |= dm[:-1]; n[:-1] |= dm[1:]; n[:, 1:] |= dm[:, :-1]; n[:, :-1] |= dm[:, 1:]
                    dm = n
                bad &= ~dm
            total += int(bad.sum())
            # recolour (not fill): keep each pixel's luminance, make it indigo-navy / bone-grey like its neighbours
            lum = (0.3 * cell[..., 0] + 0.59 * cell[..., 1] + 0.11 * cell[..., 2]).astype(float)
            mul = NAVY if "Night" in path else BONE
            for ch in range(3):
                cell[..., ch][bad] = np.clip(lum[bad] * mul[ch], 0, 255).astype(np.uint8)
            a[reg] = cell
    buf = io.BytesIO()
    Image.fromarray(a).save(buf, "PNG", optimize=True)
    open(path, "wb").write(buf.getvalue())
    print(os.path.basename(path), "repaired px:", total)


for n in sys.argv[1:]:
    fix(os.path.join(D, f"Turtle_{n}.bytes"))
