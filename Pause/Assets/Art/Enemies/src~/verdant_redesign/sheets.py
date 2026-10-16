#!/usr/bin/env python3
"""Review sheets for the Verdant redesign (jungle-green tile).

  python3 sheets.py strips OUT.png name...   every cell of each strip, 1x (one row per enemy)
  python3 sheets.py flap OUT.png name...     idle cells 0-3 at 2x side by side (the wing-beat cycle), one row per enemy
  python3 sheets.py ba OUT.png SCALE name... before (old strip cell 0 from git ref OLD_REF) on top, new below, SCALE x, one column per enemy
"""
import io, os, subprocess, sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
BG = (22, 48, 30, 255)
OLD_REF = os.environ.get("OLD_REF", "origin/master")


def cells(strip):
    h = strip.height
    return [strip.crop((i * h, 0, (i + 1) * h, h)) for i in range(strip.width // h)]


def tile(im, scale=1):
    bg = Image.new("RGBA", im.size, BG); bg.alpha_composite(im)
    return bg.resize((im.width * scale, im.height * scale), Image.NEAREST)


def new(name):
    return Image.open(HERE / "strips" / f"{name}.png").convert("RGBA")


def old(name):
    data = subprocess.check_output(["git", "-C", str(REPO), "show", f"{OLD_REF}:Pause/Assets/Art/Resources/Enemies/{name}.png"])
    return Image.open(io.BytesIO(data)).convert("RGBA")


def main():
    mode, out = sys.argv[1], sys.argv[2]
    if mode == "strips":
        rows = [tile(new(n)) for n in sys.argv[3:]]
        sh = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), BG)
        y = 0
        for r in rows:
            sh.paste(r, (0, y)); y += r.height
    elif mode == "flap":
        rows = []
        for n in sys.argv[3:]:
            cs = cells(new(n))[:4]
            rows.append(tile(Image.fromarray(__import__("numpy").concatenate([__import__("numpy").asarray(c) for c in cs], axis=1), "RGBA"), 2))
        sh = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), BG)
        y = 0
        for r in rows:
            sh.paste(r, (0, y)); y += r.height
    elif mode == "ba":
        scale = int(sys.argv[3]); names = sys.argv[4:]
        cs = [(tile(cells(old(n))[0], scale), tile(cells(new(n))[0], scale)) for n in names]
        w = max(max(a.width, b.width) for a, b in cs); h = max(a.height for a, b in cs)
        sh = Image.new("RGBA", (w * len(cs), h * 2), BG)
        for i, (a, b) in enumerate(cs):
            sh.paste(a, (i * w + (w - a.width) // 2, (h - a.height) // 2))
            sh.paste(b, (i * w + (w - b.width) // 2, h + (h - b.height) // 2))
    sh.convert("RGB").save(out)
    print(out, sh.size)




def all_sheets():
    """python3 sheets.py all   -> before_after_1x / 3x_ships / 3x_rocks and flap_cycles next to this script"""
    ships = ["verdant_chaser", "verdant_fighter_1", "verdant_fighter_2", "verdant_fighter_3", "verdant_fighter_4", "verdant_big"]
    rocks = ["verdant_rock_pod", "verdant_rock_spore", "verdant_rock_knot", "verdant_rock_vine"]
    h = str(HERE) + "/"
    for argv in (["ba", h + "before_after_1x.png", "1"] + ships + rocks, ["ba", h + "before_after_3x_ships.png", "3"] + ships,
                 ["ba", h + "before_after_3x_rocks.png", "3"] + rocks, ["flap", h + "flap_cycles.png"] + ships + rocks):
        sys.argv = ["sheets.py"] + argv
        main()


if __name__ == "__main__":
    all_sheets() if sys.argv[1:] == ["all"] else main()
