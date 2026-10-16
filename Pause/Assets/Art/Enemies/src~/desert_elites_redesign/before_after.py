#!/usr/bin/env python3
"""Before / after sheets (old strips on top, new below) at 1x (the 384 px cell as drawn) and 3x (the hover cell, 3x the native cell is too big,
so 1x of each ship's hover cell at 3x of the half-size game view is not needed: 'x3' here = hover cell magnified to 3 / 2 of native).
  python3 before_after.py <out dir> [old strips dir]     old strips default: git show origin/master (run from the repo root)"""
import subprocess, sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
NEW = HERE.parents[2] / "Resources/Elites/Desert"
KEYS = "dune_stalker caravan_hauler glass_cannon lens_matriarch mirror_bastion scorpion_walker".split()
SAND, DARK = (205, 184, 138), (11, 11, 26)
REPO = HERE.parents[5]


def old_strip(k, cache):
    p = cache / f"desert_elite_{k}.png"
    if not p.exists():
        data = subprocess.run(["git", "-C", str(REPO), "show", f"origin/master:Pause/Assets/Art/Resources/Elites/Desert/desert_elite_{k}.png"], capture_output=True, check=True).stdout
        p.write_bytes(data)
    return Image.open(p).convert("RGBA")


def cells(im, which, size):
    h = im.height
    out = []
    for c in which:
        t = im.crop((c * h, 0, c * h + h, h)).resize((size, size), Image.Resampling.NEAREST if size > h else Image.Resampling.LANCZOS)
        out.append(t)
    return out


def sheet(which, size, bg, olds, path):
    cols = len(which)
    W, H = size * cols, size * len(KEYS) * 2
    im = Image.new("RGBA", (W, H), bg + (255,))
    for r, k in enumerate(KEYS):
        for j, (strip) in enumerate((olds[k], Image.open(NEW / f"desert_elite_{k}.png").convert("RGBA"))):
            for i, t in enumerate(cells(strip, which, size)):
                im.alpha_composite(t, (i * size, (r * 2 + j) * size))
    im.convert("RGB").save(path)
    print(path, im.size)


def hover_sheet(keys, size, bg, olds, path):
    """one column per ship: old on top, new below, the hover cell"""
    im = Image.new("RGBA", (size * len(keys), size * 2), bg + (255,))
    for c, k in enumerate(keys):
        for j, strip in enumerate((olds[k], Image.open(NEW / f"desert_elite_{k}.png").convert("RGBA"))):
            im.alpha_composite(cells(strip, [3], size)[0], (c * size, j * size))
    im.convert("RGB").save(path)
    print(path, im.size)


def main():
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    cache = out / "old"; cache.mkdir(exist_ok=True)
    olds = {k: old_strip(k, cache) for k in KEYS}
    sheet(range(7), 192, SAND, olds, out / "before_after_all_cells_sand.png")      # every cell, half native
    hover_sheet(KEYS, 384, SAND, olds, out / "before_after_1x_sand.png")           # 1x = the native 384 px cell
    hover_sheet(KEYS, 384, DARK, olds, out / "before_after_1x_dark.png")
    hover_sheet(KEYS[:3], 1152, SAND, olds, out / "before_after_3x_sand_a.png")    # 3x
    hover_sheet(KEYS[3:], 1152, SAND, olds, out / "before_after_3x_sand_b.png")


main()
