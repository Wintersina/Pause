#!/usr/bin/env python3
"""Soot halo around the wreck damage (UFO, row 2 only), run ONCE after the final build + patch.

ShipDamageTest wants the critical row to repaint >= 20% of the hull (the Codex wreck repaints ~19%). The wreck's
burning rim, torn panels and cracked canopy get a soot scorch around them: every row-2 pixel within 7 px of a damage
pixel (row 2 vs row 0 of the SAME cell, so it sits on the same panels in every cell and every skin) is darkened,
strongest next to the damage and fading out. Alpha is untouched.

    python3 soot.py          (hull sheet + the four skins; NOT idempotent: rebuild with build_restyle.py first if you rerun)
"""
import io
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.normpath(os.path.join(HERE, ".."))
C = 256
REACH = 7


def dilate(m):
    o = m.copy()
    o[1:] |= m[:-1]; o[:-1] |= m[1:]; o[:, 1:] |= m[:, :-1]; o[:, :-1] |= m[:, 1:]
    o[1:, 1:] |= m[:-1, :-1]; o[:-1, :-1] |= m[1:, 1:]; o[1:, :-1] |= m[:-1, 1:]; o[:-1, 1:] |= m[1:, :-1]
    return o


def soot(a):
    a = a.copy()
    for c in range(8):
        x0 = c * C
        r0 = a[0:C, x0:x0 + C].astype(int)
        r2 = a[2 * C:3 * C, x0:x0 + C].astype(int)
        al = r2[..., 3] > 0
        layer = (np.abs(r2[..., :3] - r0[..., :3]).sum(-1) > 60) & al
        reach = layer
        out = a[2 * C:3 * C, x0:x0 + C].astype(np.float32)
        for k in range(1, REACH + 1):
            nxt = dilate(reach)
            ring = nxt & ~reach & al
            f = 1.0 - 0.5 * (1 - (k - 1) / REACH)
            out[ring, :3] *= f
            reach = nxt
        a[2 * C:3 * C, x0:x0 + C] = np.clip(out, 0, 255).astype(np.uint8)
    return a


def process(path, is_bytes):
    im = Image.open(path).convert("RGBA")
    a = soot(np.array(im))
    res = Image.fromarray(a)
    if is_bytes:
        buf = io.BytesIO()
        res.save(buf, "PNG", optimize=True)
        open(path, "wb").write(buf.getvalue())
    else:
        res.save(path, optimize=True)
    print("soot ->", os.path.basename(path))


if __name__ == "__main__":
    process(os.path.join(SHIP, "UFO.png"), False)
    for n in ("Night", "Sodium", "Bone", "Abductor"):
        process(os.path.join(SHIP, "UFO_%s.bytes" % n), True)
