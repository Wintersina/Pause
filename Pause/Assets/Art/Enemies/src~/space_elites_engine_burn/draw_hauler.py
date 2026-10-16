#!/usr/bin/env python3
"""masters/singularity_hauler.png, drawn in code in the same simple, bold language as the three Codex masters (Codex's usage limit
was hit before its hauler render): a thick ring holding a magenta core, bolted to a blocky hull with four engine bells.
Written on the #00FF40 key like the other masters (nose up, no flames)."""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

HERE = Path(__file__).resolve().parent
N = 96
OUT = (22, 16, 44)
STEEL = [(54, 52, 78), (80, 76, 112), (112, 108, 148)]
VIOLET = [(70, 40, 130), (110, 64, 190), (150, 100, 235)]
AMBER, DARKAMB = (255, 190, 40), (60, 40, 20)
CYAN, MAG, MAGL = (90, 225, 255), (255, 70, 215), (255, 170, 240)

im = Image.new("RGBA", (N, N))
d = ImageDraw.Draw(im)


def rect(x0, y0, x1, y1, c):
    d.rectangle((x0, y0, x1, y1), fill=c + (255,))


# grapple arms (behind the ring)
for sx in (1, -1):
    def X(x): return 48 + sx * (x - 48) if sx > 0 else 48 - (x - 48) - 1
    rect(min(X(70), X(82)), 14, max(X(70), X(82)), 50, STEEL[1])
    rect(min(X(70), X(73)), 14, max(X(70), X(73)), 50, STEEL[2])
    rect(min(X(70), X(82)), 46, max(X(70), X(82)), 54, AMBER)
    rect(min(X(72), X(80)), 49, max(X(72), X(80)), 52, DARKAMB)
# hull block
d.polygon([(26, 36), (70, 36), (80, 56), (74, 66), (22, 66), (16, 56)], fill=STEEL[1] + (255,))
d.polygon([(26, 36), (48, 36), (48, 66), (22, 66), (16, 56)], fill=STEEL[0] + (255,))
rect(30, 42, 38, 62, VIOLET[1]); rect(58, 42, 66, 62, VIOLET[1])
rect(30, 42, 32, 62, VIOLET[2]); rect(58, 42, 60, 62, VIOLET[2])
for i in range(7):   # amber hazard stripe
    d.polygon([(26 + i * 6, 56), (30 + i * 6, 56), (28 + i * 6, 61), (24 + i * 6, 61)], fill=(AMBER if i % 2 == 0 else DARKAMB) + (255,))
# engine bells
for cx in (28, 40, 56, 68):
    rect(cx - 5, 66, cx + 4, 74, STEEL[0]); rect(cx - 5, 66, cx + 4, 67, STEEL[2]); rect(cx - 4, 71, cx + 3, 74, (10, 8, 20))
# ring
d.ellipse((26, 2, 70, 46), fill=VIOLET[1] + (255,))
d.ellipse((26, 2, 70, 46), outline=VIOLET[2] + (255,), width=2)
d.ellipse((32, 8, 64, 40), outline=VIOLET[0] + (255,), width=2)
# clear the ring's hole then draw the dark well + core
hole = Image.new("L", (N, N), 0)
ImageDraw.Draw(hole).ellipse((33, 9, 63, 39), fill=255)
a = np.array(im); h = np.array(hole) > 0
a[h] = (26, 18, 52, 255)
im = Image.fromarray(a)
d = ImageDraw.Draw(im)
d.ellipse((38, 14, 58, 34), fill=MAG + (255,))
d.ellipse((41, 17, 55, 31), fill=MAGL + (255,))
d.ellipse((45, 20, 51, 26), fill=(255, 250, 255, 255))
for (x, y) in ((48, 4), (48, 44), (28, 24), (68, 24)):   # ring lights
    d.rectangle((x - 2, y - 1, x + 1, y + 1), fill=CYAN + (255,))
# dark 1 px outline round the silhouette
al = np.array(im)[..., 3] > 0
p = np.pad(al, 1); g = al.copy()
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1):
        g |= p[1 + dy:1 + dy + N, 1 + dx:1 + dx + N]
arr = np.array(im)
ring = g & ~al
arr[ring] = OUT + (255,)
big = Image.fromarray(arr).resize((N * 8, N * 8), Image.Resampling.NEAREST)
bg = Image.new("RGBA", big.size, (0, 255, 64, 255))
bg.alpha_composite(big)
bg.convert("RGB").save(HERE / "masters" / "singularity_hauler.png")
print("ok")
