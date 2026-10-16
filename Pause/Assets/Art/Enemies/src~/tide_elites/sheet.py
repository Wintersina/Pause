#!/usr/bin/env python3
"""Preview: strips on the Tide sea colour. python3 sheet.py <out.png> <scale> <strip.png>...   (scale 0.5 = a cell at 192 px)"""
import sys
from PIL import Image
out, scale, strips = sys.argv[1], float(sys.argv[2]), sys.argv[3:]
BG = (16, 52, 84, 255)
rows = []
for s in strips:
    im = Image.open(s).convert("RGBA")
    bg = Image.new("RGBA", im.size, BG); bg.alpha_composite(im)
    rows.append(bg.resize((round(im.width * scale), round(im.height * scale)), Image.Resampling.LANCZOS if scale < 1 else Image.Resampling.NEAREST))
W = max(r.width for r in rows)
sheet = Image.new("RGBA", (W, sum(r.height for r in rows)), BG)
y = 0
for r in rows:
    sheet.alpha_composite(r, (0, y)); y += r.height
sheet.convert("RGB").save(out)
