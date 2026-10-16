#!/usr/bin/env python3
"""Before / after sheets (old on top, new below) on the Tide sea colour, 1x (a cell at 192 px, all seven cells) and 3x (the hover cell at 576 px).
  python3 before_after.py <old strips dir> <new strips dir> <out dir>"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
old, new, out = (Path(a) for a in sys.argv[1:4])
NAMES = "riptide bathyscaphe trawler thunderfin pearl_warden".split()
BG = (16, 52, 84, 255)


def load(d, n):
    return Image.open(d / f"tide_elite_{n}.png").convert("RGBA")


def over(im):
    b = Image.new("RGBA", im.size, BG); b.alpha_composite(im); return b


def sheet(cell_px, cells, name):
    rows = []
    for n in NAMES:
        for d in (old, new):
            im = load(d, n)
            parts = [im.crop((c * 384, 0, c * 384 + 384, 384)) for c in cells]
            row = Image.new("RGBA", (cell_px * len(cells), cell_px), BG)
            for i, p in enumerate(parts):
                row.alpha_composite(over(p).resize((cell_px, cell_px), Image.Resampling.LANCZOS if cell_px < 384 else Image.Resampling.NEAREST), (i * cell_px, 0))
            rows.append(row)
    W = rows[0].width
    s = Image.new("RGBA", (W, cell_px * len(rows) + 6 * len(NAMES)), (6, 20, 34, 255))
    y = 0
    for i, r in enumerate(rows):
        s.alpha_composite(r, (0, y)); y += cell_px + (6 if i % 2 else 0)
    s.convert("RGB").save(out / name)


out.mkdir(parents=True, exist_ok=True)
sheet(192, range(7), "tide_elites_before_after_1x.png")
s3 = Image.new("RGBA", (576 * 5, 576 * 2 + 6), BG)
for i, n in enumerate(NAMES):
    for j, d in enumerate((old, new)):
        c = load(d, n).crop((3 * 384, 0, 4 * 384, 384))
        s3.alpha_composite(over(c).resize((576, 576), Image.Resampling.NEAREST), (i * 576, j * (576 + 6)))
s3.convert("RGB").save(out / "tide_elites_before_after_3x.png")
