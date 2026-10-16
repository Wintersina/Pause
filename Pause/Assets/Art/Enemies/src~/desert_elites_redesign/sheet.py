#!/usr/bin/env python3
"""Review sheets. python3 sheet.py <strip dir> <out png> [--scale N] [--cell 3|all] [--bg sand|dark] [keys...]
One row per ship: the 7 flight cells (or just the hover cell). Used for the vetting sheets and the before/after sheets."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

KEYS = "dune_stalker caravan_hauler glass_cannon lens_matriarch mirror_bastion scorpion_walker".split()
BG = {"sand": (205, 184, 138), "dark": (11, 11, 26), "glass": (236, 228, 200)}


def row(path, scale, cells, bg):
    im = Image.open(path).convert("RGBA")
    h = im.height
    n = im.width // h
    pick = range(n) if cells == "all" else [int(cells)]
    sz = 384 * scale // 2
    out = Image.new("RGBA", (sz * len(pick), sz), bg + (255,))
    for i, c in enumerate(pick):
        t = im.crop((c * h, 0, c * h + h, h))
        if sz != h:
            t = t.resize((sz, sz), Image.Resampling.NEAREST if sz > h else Image.Resampling.LANCZOS)
        out.alpha_composite(t, (i * sz, 0))
    return out


def main():
    a = sys.argv[1:]
    scale, cells, bg = 2, "all", "sand"
    for opt in ("--scale", "--cell", "--bg"):
        if opt in a:
            i = a.index(opt); v = a[i + 1]; del a[i:i + 2]
            if opt == "--scale": scale = int(v)
            elif opt == "--cell": cells = v
            else: bg = v
    d, out, keys = Path(a[0]), a[1], a[2:] or KEYS
    rows = []
    for k in keys:
        p = d / f"desert_elite_{k}.png"
        if p.exists():
            rows.append(row(p, scale, cells, BG[bg]))
    W = max(r.width for r in rows)
    sheet = Image.new("RGBA", (W, sum(r.height for r in rows)), BG[bg] + (255,))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y)); y += r.height
    sheet.convert("RGB").save(out)
    print(out, sheet.size)


main()
