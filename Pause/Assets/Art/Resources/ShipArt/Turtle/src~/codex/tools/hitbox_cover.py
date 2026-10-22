"""Estimate of ShipHitboxTest's hull coverage: baked hull polygon area / idle silhouette area (the test divides by the
'main body' area, which excludes thin parts, so this is a conservative lower bound)."""
import json
import os
import struct
import numpy as np
from PIL import Image

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
b = open(os.path.join(S, "Turtle_hitbox.bytes"), "rb").read()
magic, margin, count = struct.unpack_from("<fii", b, 0)[0:3] if False else (b[:4], *struct.unpack_from("<fi", b, 4))
off = 12
_id, n = struct.unpack_from("<ii", b, off)
off += 8
pts = [struct.unpack_from("<ff", b, off + 8 * i) for i in range(n)]
d = json.load(open(os.path.join(S, "Turtle_data.json")))
ppu = d["rect"]["ppu"]
p = np.array(pts) * ppu
area = 0.5 * abs(np.dot(p[:, 0], np.roll(p[:, 1], -1)) - np.dot(p[:, 1], np.roll(p[:, 0], -1)))
al = np.array(Image.open(os.path.join(S, "Turtle.png")).convert("RGBA"))[:256, :256, 3] > 0
print("hull points", n, "polygon area px", round(area), "silhouette px", int(al.sum()), "cover of silhouette", round(area / al.sum(), 3))
