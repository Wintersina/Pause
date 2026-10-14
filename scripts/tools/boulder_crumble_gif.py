#!/usr/bin/env python3
"""Crumble preview: python3 scripts/tools/boulder_crumble_gif.py <dir> [cell] -> <dir>/crumble.gif (3 variants side by side, 5 break frames)."""
import sys
from PIL import Image
d = sys.argv[1]; cell = int(sys.argv[2]) if len(sys.argv) > 2 else 768
k = Image.open(d + "/break.png").convert("RGBA")
sc = 0.5
frames = []
for f in range(5):
    im = Image.new("RGBA", (cell * 3, cell), (11, 11, 26, 255))
    for v in range(3):
        im.alpha_composite(k.crop((f * cell, v * cell, (f + 1) * cell, (v + 1) * cell)), (v * cell, 0))
    frames.append(im.resize((int(cell * 3 * sc), int(cell * sc)), Image.LANCZOS).convert("P", palette=Image.ADAPTIVE))
frames[0].save(d + "/crumble.gif", save_all=True, append_images=frames[1:], duration=[500, 300, 200, 200, 400], loop=0)
