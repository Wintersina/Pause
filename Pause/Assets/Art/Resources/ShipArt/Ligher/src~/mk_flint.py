"""Flint = stock copper hull + three crisp bone-white chevrons (V shapes, point up) composited explicitly.
Codex's own chevrons came out small and smudged, so the livery is drawn here, pixel-clean, on the final stock sheet.
Run AFTER build_restyle + patch_restyle + damage_overlay --skins (it overwrites Ligher_Flint.bytes from Ligher.png).
Geometry (256 px cell, idle): chevron apexes at y = 166, 181, 196; half-widths 19, 17, 15 px; each arm drops 10 px
over its half-width and is 4 px thick; colours #F4EAD4 (lit) / #B9AE98 (right half, in shade).
Other cells: bob columns shift by the bob; bank columns follow the hull's bbox centre and width ratio.
In damaged rows a chevron pixel is only drawn where the stock row pixel equals the intact one (damage wins)."""
import os
import numpy as np
from PIL import Image
D = os.path.dirname(os.path.abspath(__file__))
S = os.path.join(D, '..')
CELL = 256
sheet = np.array(Image.open(os.path.join(S, 'Ligher.png')).convert('RGBA'))
out = sheet.copy()
LIT, SHADE = (0xF4, 0xEA, 0xD4), (0xB9, 0xAE, 0x98)
BOB = {1: -4, 2: -4, 5: 4}
CH = [(166, 19), (181, 17), (196, 15)]


def bbox_x(c):
    a = sheet[0:CELL, c * CELL:(c + 1) * CELL, 3] > 0
    xs = np.nonzero(a.any(axis=0))[0]
    return xs.min(), xs.max()


b0 = bbox_x(0)
cx0 = (b0[0] + b0[1]) / 2.0
w0 = b0[1] - b0[0] + 1
for c in range(8):
    bc = bbox_x(c)
    cxc = (bc[0] + bc[1]) / 2.0
    sx = (bc[1] - bc[0] + 1) / w0
    dy = BOB.get(c, 0)
    for r in range(3):
        cell = out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
        intact = sheet[0:CELL, c * CELL:(c + 1) * CELL]
        cur = sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
        same = (cur == intact).all(axis=2)
        for y0, hw in CH:
            hwc = max(2, int(round(hw * sx)))
            for dx in range(-hwc, hwc + 1):
                drop = int(round(abs(dx) * 10.0 / hwc))
                x = int(np.floor(cxc + 0.5)) + dx
                col = SHADE if dx > 0 and c != 6 or (c == 6 and dx > 2) else LIT
                for t in range(4):
                    y = y0 + dy + drop + t
                    if 0 <= x < CELL and 0 <= y < CELL and cur[y, x, 3] > 0 and same[y, x]:
                        cell[y, x, :3] = col
im = Image.fromarray(out)
im.save(os.path.join(S, 'Ligher_Flint.bytes'), format='PNG', optimize=True)
print('Flint written')
