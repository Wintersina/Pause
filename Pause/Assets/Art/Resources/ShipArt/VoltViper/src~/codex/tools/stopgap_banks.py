"""Stopgap VoltViper bank cells (sheet columns 6 = bank left, 7 = bank right), no image generation.
Both Codex bank passes were rejected, so each bank cell is built from the clean idle cell (column 0) of the same
row and sheet: silhouette narrowed 5% about the cell centre, shifted 3 px in the bank direction, the near
(leaning) side shaded slightly darker. The wings stay the idle wings. Alpha comes from the same transform on the
identical idle alpha of every row/skin, so it stays binary and equal across damage rows and skins.
Also refreshes rect / ppu / nozzles in ../../data.json from the sheet (run patch_restyle.py afterwards).

    python3 stopgap_banks.py <Resources/ShipArt dir>
"""
import io
import json
import os
import sys

import numpy as np
from PIL import Image

art = sys.argv[1]
CELL = 256
SQUEEZE = 0.95
SHIFT = 3
SHADE = 0.86          # darkest multiplier at the outer edge of the near side
WORLD_W = 243 / 867.8571


def bank_cell(idle, direction):
    """direction -1 = bank left, +1 = bank right."""
    a = idle
    xs = np.arange(CELL)
    src = np.round(127.5 + (xs - SHIFT * direction - 127.5) / SQUEEZE).astype(int)
    ok = (src >= 0) & (src < CELL)
    out = np.zeros_like(a)
    out[:, ok] = a[:, src[ok]]
    # shade the near side (the side the ship leans toward), strongest at the outer edge
    d = (xs - 127.5) * direction                      # > 0 on the near side
    f = 1 - (1 - SHADE) * np.clip(d / 110.0, 0, 1)
    rgb = out[..., :3].astype(float) * f[None, :, None]
    out[..., :3] = np.clip(rgb + 0.5, 0, 255).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    return out


def process(path, as_bytes):
    im = Image.open(path)
    im.load()
    a = np.array(im.convert("RGBA"))
    assert a.shape == (768, 2304, 4)
    for r in range(3):
        idle = a[r * CELL:(r + 1) * CELL, 0:CELL]
        for col, direction in ((6, -1), (7, 1)):
            a[r * CELL:(r + 1) * CELL, col * CELL:(col + 1) * CELL] = bank_cell(idle, direction)
    out = Image.fromarray(a)
    if as_bytes:
        buf = io.BytesIO()
        out.save(buf, format="PNG", optimize=True)
        open(path, "wb").write(buf.getvalue())
    else:
        out.save(path, optimize=True)
    return a


hull_path = os.path.join(art, "Hulls", "VoltViper.png")
hull = process(hull_path, False)
for n in ("Night", "Sodium", "Bone", "Storm"):
    process(os.path.join(art, "Skins", f"VoltViper_{n}.bytes"), True)

# rect / ppu / nozzles (same rules as build_restyle.measure; nozzle lips are the idle ones of the first pass)
al = hull[..., 3]
x0 = y0 = 10 ** 6
x1 = y1 = -1
for s in range(3):
    for c in range(9):
        ys, xs = np.nonzero(al[s * CELL:(s + 1) * CELL, c * CELL:(c + 1) * CELL])
        x0, y0 = min(x0, int(xs.min())), min(y0, int(ys.min()))
        x1, y1 = max(x1, int(xs.max()) + 1), max(y1, int(ys.max()) + 1)
x0, y0, x1, y1 = max(0, x0 - 1), max(0, y0 - 1), min(CELL, x1 + 1), min(CELL, y1 + 1)
w, h = x1 - x0, y1 - y0
dj = os.path.join(art, "Hulls", "src~", "voltviper", "data.json")
data = json.load(open(dj))
data["rect"] = [x0, CELL - y1, w, h]
data["ppu"] = round(w / WORLD_W, 4)
lips = [(106, 219, 0.72), (148, 219, 0.72)]
data["nozzles"] = [[round(px + .5 - x0, 1), round(y1 - (py + .5), 1), k] for px, py, k in lips]
json.dump(data, open(dj, "w"))
print("rect", data["rect"], "ppu", data["ppu"], "nozzles", data["nozzles"])
