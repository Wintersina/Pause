"""Checks for VoltViper_Storm.bytes: alpha == stock, binary, no enclosed transparent pixels, hit-flash column and
per-column damage (pixels where stock row r differs from stock row 0 by >90 stay the stock+recolour pixel, i.e. the
chevron layer never paints there, so rows 1/2 of a column share the same chevron geometry outside damage)."""
import os
import numpy as np
from PIL import Image

D = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
st = np.array(Image.open(os.path.join(D, "VoltViper.png")).convert("RGBA")).astype(int)
sm = np.array(Image.open(os.path.join(D, "VoltViper_Storm.bytes")).convert("RGBA")).astype(int)
assert (st[..., 3] == sm[..., 3]).all(), "alpha differs from stock"
assert set(np.unique(sm[..., 3])) <= {0, 255}, "alpha not binary"
# enclosed transparent: transparent components not touching the border
t = sm[..., 3] == 0
for r in range(3):
    for c in range(9):
        m = t[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256]
        seen = np.zeros_like(m)
        stack = [(0, 0)]
        for y in range(256):
            for x in (0, 255):
                stack += [(y, x), (x, y)]
        while stack:
            y, x = stack.pop()
            if 0 <= y < 256 and 0 <= x < 256 and m[y, x] and not seen[y, x]:
                seen[y, x] = True
                stack += [(y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)]
        enc = int((m & ~seen).sum())
        assert enc == 0, ("enclosed transparent", r, c, enc)
# hit-flash column identical to the committed base
base = np.array(Image.open(os.path.join(D, "src~", "storm_base.png")).convert("RGBA")).astype(int)
assert (base[:, 2048:] == sm[:, 2048:]).all(), "hit-flash changed"
# damage consistent: where stock row r differs strongly from row 0, Storm has no chevron amber/cream/shade paint
AM = {(0xFF, 0xB0, 0x1C), (0xFF, 0xF0, 0xC4), (0xC8, 0x6A, 0x0C)}
bad = 0
for c in range(8):
    for r in (1, 2):
        s0 = st[0:256, c * 256:(c + 1) * 256]
        sr = st[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256]
        dm = np.abs(sr[..., :3] - s0[..., :3]).sum(2) > 90
        cell = sm[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256]
        for y, x in zip(*np.nonzero(dm)):
            if tuple(cell[y, x, :3]) in AM and tuple(sr[y, x, :3]) not in AM:
                bad += 1
assert bad == 0, bad
print("storm checks ok")
