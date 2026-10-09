#!/usr/bin/env python3
"""Contact sheet of strips for the user's review (open it, give an honest critique).

  python3 contact_sheet.py OUT.png STRIP.png [STRIP.png ...] [--scale 2] [--bg 0b0b1a] [--backdrop MID_TILE.png]

One row per strip, at --scale (default 2x, nearest-neighbour) on dark #0b0b1a, a thin grid line at every cell
boundary (cell side = strip height; death strips are 3 cells, idle 7), the file name above each row.
--backdrop lays the strips over a tile of the world's mid layer instead (the readability check: does it pop?).
Rows wider than 2400 px are split. Needs Pillow.
"""
import sys, argparse, os
from PIL import Image, ImageDraw

ap = argparse.ArgumentParser()
ap.add_argument("out"); ap.add_argument("strips", nargs="+")
ap.add_argument("--scale", type=int, default=2); ap.add_argument("--bg", default="0b0b1a")
ap.add_argument("--backdrop")
o = ap.parse_args()
bg = tuple(int(o.bg[i:i + 2], 16) for i in (0, 2, 4))
ims = [Image.open(p).convert("RGBA") for p in o.strips]
W = max(i.width for i in ims) * o.scale
rows = [(os.path.basename(p), i.resize((i.width * o.scale, i.height * o.scale), Image.NEAREST)) for p, i in zip(o.strips, ims)]
H = sum(r.height + 18 for _, r in rows)
sheet = Image.new("RGBA", (W, H), bg + (255,))
if o.backdrop:
    t = Image.open(o.backdrop).convert("RGBA")
    for y in range(0, H, t.height):
        for x in range(0, W, t.width): sheet.paste(t, (x, y))
d = ImageDraw.Draw(sheet)
y = 0
for name, r in rows:
    d.text((4, y + 3), name, fill=(200, 200, 220, 255)); y += 18
    sheet.alpha_composite(r, (0, y))
    side = r.height
    for x in range(side, r.width, side): d.line([(x, y), (x, y + r.height)], fill=(60, 60, 90, 255))
    y += r.height
sheet.convert("RGB").save(o.out)
print("wrote", o.out, sheet.size)
