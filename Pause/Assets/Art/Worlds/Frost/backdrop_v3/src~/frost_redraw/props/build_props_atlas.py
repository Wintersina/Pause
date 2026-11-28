#!/usr/bin/env python3
"""Frost's living props atlas: Backdrop3/props.png + props.json.

The six approved props (round 1, user-approved 2026-11-28; no seals) are 8-cell strips of 128 px cells in strips/<name>.png,
already graded to the landmark rules by gen/props_build.py (forms value p90 ~.46, lamps capped at .92, no pink). This only
lays them out: one prop per row, top to bottom in NAMES order, frames left to right, named <name>_00 .. <name>_07 so
BackdropAtlas.Frames(name) returns the strip. The manifest is the landmarks' Unity-rect JSON (n, x, y, w, h; y measured from
the BOTTOM of the sheet). The sheet is 1024 x 768 (six rows; no empty rows paid for on phones).

    python3 build_props_atlas.py            # writes Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/props.{png,json}
    python3 build_props_atlas.py --check    # verifies the installed sheet matches the strips

NO PINK (WorldCue.IsPink: hue 285-352, saturation > .25, value > .3 -- the hostile cue outside Space). The strips pass as
painted, but the icebreaker's dark crimson hull (hue 344-349, value < .3) is lifted past value .3 by the Frost grade (x1.95)
and then reads pink: every coloured texel with a hue in PINK_HUES is turned to HULL_HUE (a true red; the grade keeps hue).

Keep NAMES in step with FrostPropsTuning.Names (FrostProps.cs).
"""
import json
import sys
from pathlib import Path

import colorsys

from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[5] / "Backgrounds" / "Resources" / "Worlds" / "Frost" / "Backdrop3"
NAMES = ["whale", "orca", "station", "lab", "icebreaker", "plane"]
CELL, FRAMES = 128, 8
W, H = CELL * FRAMES, CELL * len(NAMES)
PINK_HUES = (280.0, 357.0)
HULL_HUE = 358.0


def depink(img):
    px = img.load()
    n = 0
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > .12 and PINK_HUES[0] <= h * 360 < PINK_HUES[1]:
                r2, g2, b2 = colorsys.hsv_to_rgb(HULL_HUE / 360, s, v)
                px[x, y] = (round(r2 * 255), round(g2 * 255), round(b2 * 255), a)
                n += 1
    return n


def build():
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sprites = []
    for r, name in enumerate(NAMES):
        strip = Image.open(HERE / "strips" / f"{name}.png").convert("RGBA")
        assert strip.size == (W, CELL), (name, strip.size)
        depink(strip)
        sheet.paste(strip, (0, r * CELL))
        for f in range(FRAMES):
            sprites.append({"n": f"{name}_{f:02d}", "x": f * CELL, "y": H - (r + 1) * CELL, "w": CELL, "h": CELL})
    return sheet, {"sprites": sprites}


def main():
    sheet, js = build()
    if "--check" in sys.argv:
        old = Image.open(OUT / "props.png").convert("RGBA")
        same = old.size == sheet.size and old.tobytes() == sheet.tobytes()
        same &= json.loads((OUT / "props.json").read_text()) == js
        print("props atlas up to date" if same else "props atlas DIFFERS from the strips")
        sys.exit(0 if same else 1)
    sheet.save(OUT / "props.png", optimize=True)
    (OUT / "props.json").write_text(json.dumps(js, indent=2) + "\n")
    print("wrote", OUT / "props.png", sheet.size, len(js["sprites"]), "cells")


if __name__ == "__main__":
    main()
