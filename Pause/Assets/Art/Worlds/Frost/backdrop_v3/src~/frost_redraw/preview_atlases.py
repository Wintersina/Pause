#!/usr/bin/env python3
"""Old | new preview per Frost atlas, over the Frost night ground colour, alpha as stored.

    python3 preview_atlases.py <new Backdrop3 dir or default> <out dir>   ->  <out>/atlas_<name>.png
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
NEW = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
OUT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(".")
NAMES = ["landmarks", "sites", "docks", "smoke", "steam", "fire_lights", "beacons", "aurora", "weather", "wx_ambient", "wx_hazard"]
BG = (22, 34, 52, 255)


def panel(p, W):
    im = Image.open(p).convert("RGBA")
    s = W / im.width
    im = im.resize((W, int(im.height * s)), Image.NEAREST)
    bg = Image.new("RGBA", im.size, BG)
    # 256-px cell grid lines (in the scaled image)
    out = Image.alpha_composite(bg, im)
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for n in NAMES:
        a, b = panel(HERE / "old" / f"{n}.png", 900), panel(NEW / f"{n}.png", 900)
        h = max(a.height, b.height)
        c = Image.new("RGBA", (1830, h + 28), (10, 10, 14, 255))
        c.paste(a, (10, 28)); c.paste(b, (920, 28))
        d = ImageDraw.Draw(c)
        d.text((10, 8), f"{n}: old", fill=(230, 230, 230, 255)); d.text((920, 8), f"{n}: new", fill=(230, 230, 230, 255))
        c.convert("RGB").save(OUT / f"atlas_{n}.png")
        print(OUT / f"atlas_{n}.png")


if __name__ == "__main__":
    main()
