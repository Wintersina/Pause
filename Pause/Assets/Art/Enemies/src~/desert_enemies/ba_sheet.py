#!/usr/bin/env python3
"""Before/after sheet of the seven Desert enemies (cell 0 of each strip): top = sand originals, bottom = colour repaint,
on a sand tile, at 1x and 3x.  python3 ba_sheet.py out_dir"""
import sys
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
KEYS = ["alien", "chaser", "big", "fighter_1", "fighter_2", "fighter_3", "fighter_4"]
out = Path(sys.argv[1])
def cell(path, k):
    im = Image.open(path).convert("RGBA")
    s = im.height
    return im.crop((0, 0, s, s))
for scale in (1, 3):
    W = sum(256 if k == "big" else 192 for k in KEYS) * scale
    sh = Image.new("RGBA", (W, 256 * 2 * scale), (176, 150, 108, 255))
    x = 0
    for k in KEYS:
        b = cell(HERE / "strips/original_sand" / f"desert_{k}.png", k) if (HERE / "strips/original_sand").exists() else None
        before = Image.open(HERE / "original_sand" / f"desert_{k}_key.png").convert("RGBA")
        after = Image.open(HERE / f"desert_{k}_key.png").convert("RGBA")
        w = before.width * scale
        for row, im in enumerate((before, after)):
            sh.alpha_composite(im.resize((w, w), Image.NEAREST), (x, row * 256 * scale))
        x += w
    sh.save(out / f"ba_{scale}x.png")
