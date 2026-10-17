#!/usr/bin/env python3
"""Before / after sheets (old 192 px strip on top, new 384 px below, on the world's backdrop colour) for the three rebuilt elites.
  1x : every cell at 192 px (old: its 7 cells, new: its 10)      3x : the hover cell at 576 px, old vs new, plus damaged cells
  python3 before_after.py <out dir>"""
import sys
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
OUT = Path(sys.argv[1]); OUT.mkdir(parents=True, exist_ok=True)
SHIPS = [("frost_elite_cryo_siren", "Frost", (22, 40, 74)), ("frost_elite_rimebreaker", "Frost", (22, 40, 74)), ("verdant_elite_resin_warden", "Verdant", (18, 52, 34))]


def cells(im, size):
    n = im.width // im.height
    return [im.crop((i * im.height, 0, (i + 1) * im.height, im.height)) for i in range(n)]


def fit(c, px, bg):
    b = Image.new("RGBA", c.size, bg + (255,)); b.alpha_composite(c)
    return b.resize((px, px), Image.Resampling.LANCZOS if px < c.width else Image.Resampling.NEAREST)


rows = []
for key, world, bg in SHIPS:
    old = Image.open(HERE / "old" / f"{key}.png").convert("RGBA")
    new = Image.open(ART / "Resources/Elites" / world / f"{key}.png").convert("RGBA")
    for strip in (old, new):
        cs = cells(strip, 0)
        row = Image.new("RGBA", (192 * 10, 192), bg + (255,))
        for i, c in enumerate(cs):
            row.alpha_composite(fit(c, 192, bg), (i * 192, 0))
        rows.append(row)
sheet = Image.new("RGB", (1920, 192 * len(rows) + 6 * 3), (6, 10, 18))
y = 0
for i, r in enumerate(rows):
    sheet.paste(r.convert("RGB"), (0, y)); y += 192 + (6 if i % 2 else 0)
sheet.save(OUT / "elites_before_after_1x.png")

big = Image.new("RGB", (576 * 4, 576 * 3 + 12), (6, 10, 18))
for r, (key, world, bg) in enumerate(SHIPS):
    old = Image.open(HERE / "old" / f"{key}.png").convert("RGBA")
    new = Image.open(ART / "Resources/Elites" / world / f"{key}.png").convert("RGBA")
    oc, nc = cells(old, 0), cells(new, 0)
    pics = [oc[3].resize((384, 384), Image.Resampling.LANCZOS), nc[3], oc[6].resize((384, 384), Image.Resampling.LANCZOS), nc[9]]
    for i, p in enumerate(pics):
        big.paste(fit(p, 576, bg).convert("RGB"), (i * 576, r * (576 + 6)))
big.save(OUT / "elites_before_after_3x.png")
print("ok", OUT)
