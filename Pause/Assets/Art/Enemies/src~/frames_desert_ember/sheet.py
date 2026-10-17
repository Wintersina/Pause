#!/usr/bin/env python3
"""Before/after contact sheet: python3 sheet.py out.png cellpx old_strip_dir name...  (after = ./strips)"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
out, cw, old = sys.argv[1], int(sys.argv[2]), Path(sys.argv[3])
names = sys.argv[4:]
rows = []
for n in names:
    for lab, p in (("before", old / f"{n}.png"), ("after", HERE / "strips" / f"{n}.png")):
        im = Image.open(p).convert("RGBA"); h = im.height
        rows.append((n + " " + lab, [im.crop((i * h, 0, (i + 1) * h, h)) for i in range(7)]))
S = Image.new("RGBA", (130 + cw * 7, (cw + 2) * len(rows)), (40, 40, 48, 255)); d = ImageDraw.Draw(S)
for r, (lab, cs) in enumerate(rows):
    d.text((2, r * (cw + 2) + 2), lab, fill=(255, 255, 255, 255))
    for k, c in enumerate(cs):
        S.alpha_composite(c.resize((cw, cw), Image.NEAREST), (130 + k * cw, r * (cw + 2)))
S.convert("RGB").save(out)
