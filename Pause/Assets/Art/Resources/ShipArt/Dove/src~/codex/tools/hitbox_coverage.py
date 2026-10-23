#!/usr/bin/env python3
"""Parse Dove_hitbox.bytes (HBX1: magic, margin, count, id, hull path, shield path) and measure how much of the idle
hull alpha the hull polygon covers (pivot assumed at the rect centre; also prints the polygon/alpha area ratio)."""
import json
import os
import struct
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
DOVE = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
b = open(os.path.join(DOVE, "Dove_hitbox.bytes"), "rb").read()
magic, margin, count, ident = struct.unpack_from("<4sfii", b, 0)
off = 16
def path(off):
    n = struct.unpack_from("<i", b, off)[0]
    pts = np.array(struct.unpack_from("<%df" % (2 * n), b, off + 4)).reshape(n, 2)
    return pts, off + 4 + 8 * n
hull, off = path(off)
shield, off = path(off)
d = json.load(open(os.path.join(DOVE, "Dove_data.json")))
rect = d["rect"]
ppu = rect["ppu"]
alpha = np.array(Image.open(os.path.join(DOVE, "Dove.png")).convert("RGBA"))[:256, :256, 3] > 0
ys, xs = np.nonzero(alpha)
print("magic", magic, "id", ident, "hull pts", len(hull), "shield pts", len(shield), "rect", rect)
best = None
# polygon is in world units from the sprite pivot; try the pivot at the centre of the rect
cx = rect["x"] + rect["w"] / 2
cy = rect["y"] + rect["h"] / 2          # rect y counted from the bottom
img = Image.new("L", (256, 256), 0)
H = 256
pts = [(cx + p[0] * ppu, (H - cy) - p[1] * ppu) for p in hull]
ImageDraw.Draw(img).polygon(pts, fill=1)
poly = np.array(img) > 0
cov = (poly & alpha).sum() / alpha.sum()
print(f"hull polygon covers {cov:.1%} of the idle hull alpha ({alpha.sum()} px); polygon area / alpha area {poly.sum() / alpha.sum():.2f}")
