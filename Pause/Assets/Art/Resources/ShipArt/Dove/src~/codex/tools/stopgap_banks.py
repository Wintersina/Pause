"""Dove bank cells (sheet columns 6 = bank left, 7 = bank right) built from the clean idle cell, no image generation.
Codex's painted banks twisted the hull, so every bank cell is the idle cell (column 0) of the SAME row and the SAME
sheet (stock and each skin) squeezed about the canopy axis (near side x0.80, far side x0.95), shifted 6 px in the bank direction, with the near
(leaning) side shaded slightly darker. Wings, nose, nozzle, damage and skin colours are the idle ones (the damage
therefore sits on the same panels, the Courier split stays a clean left/right division, and no colour leaks in from
another skin). Alpha is the same transform of the identical idle alpha, so it stays binary and equal in every row/skin.
Run AFTER build_restyle.py (which rewrites the sheets):

    python3 stopgap_banks.py
"""
import io
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DOVE = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CELL = 256
K_NEAR = 0.80         # near (leaning) side squeezed toward the axis (game convention, ShipDamageTable.Posed)
K_FAR = 0.95
SHIFT = 6             # whole ship shifted toward the bank
SHADE = 0.88          # darkest multiplier at the outer edge of the near side


def bank_cell(idle, direction):
    """direction -1 = bank left, +1 = bank right."""
    xs = np.arange(CELL)
    off = xs - 127.5 - SHIFT * direction              # offset from the shifted axis in the output
    near = off * direction > 0
    src = np.round(127.5 + off / np.where(near, K_NEAR, K_FAR)).astype(int)
    ok = (src >= 0) & (src < CELL)
    out = np.zeros_like(idle)
    out[:, ok] = idle[:, src[ok]]
    d = (xs - 127.5) * direction
    f = 1 - (1 - SHADE) * np.clip(d / 110.0, 0, 1)
    out[..., :3] = np.clip(out[..., :3].astype(float) * f[None, :, None] + 0.5, 0, 255).astype(np.uint8)
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


process(os.path.join(DOVE, "Dove.png"), False)
for n in ("Night", "Sodium", "Bone", "Courier"):
    process(os.path.join(DOVE, f"Dove_{n}.bytes"), True)
print("banks rebuilt")
