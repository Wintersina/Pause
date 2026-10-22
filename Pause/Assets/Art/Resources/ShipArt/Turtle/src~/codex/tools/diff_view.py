"""Saves a diff overview: key_state1 / key_state2 vs key_state0 as red masks over the dim intact pose, with a
256-cell coordinate grid every 32 cell px (cell px = 128 + (x - ax) * scale)."""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

d = os.path.dirname(os.path.abspath(__file__)) + "/.."
k = [np.array(Image.open(f"{d}/key_state{i}.png").convert("RGB")).astype(int) for i in range(3)]
green = (k[0][..., 1] > 200) & (k[0][..., 0] < 60) & (k[0][..., 2] < 60)
hull = ~green
ys, xs = np.nonzero(hull)
bw = xs.max() - xs.min() + 1
scale = 174 / bw
ax, ay = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
panels = []
for i in (1, 2):
    m = (np.abs(k[i] - k[0]).sum(2) > 40) & hull
    img = (k[0] * .35).astype(np.uint8)
    img[m] = (255, 40, 40)
    im = Image.fromarray(img)
    dr = ImageDraw.Draw(im)
    for c in range(0, 257, 32):
        px = (c - 128) / scale + ax
        py = (c - 128) / scale + ay
        dr.line([(px, 0), (px, 1023)], fill=(0, 160, 255), width=1)
        dr.line([(0, py), (1023, py)], fill=(0, 160, 255), width=1)
        dr.text((px + 2, 2), str(c), fill=(255, 255, 0))
        dr.text((2, py + 2), str(c), fill=(255, 255, 0))
    panels.append(im)
    print("state", i, "changed hull %", round(100 * m.sum() / hull.sum(), 1))
out = Image.new("RGB", (2048, 1024))
out.paste(panels[0], (0, 0))
out.paste(panels[1], (1024, 0))
out.save(sys.argv[1])
