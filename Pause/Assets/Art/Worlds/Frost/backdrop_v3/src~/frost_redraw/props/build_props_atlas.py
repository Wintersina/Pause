#!/usr/bin/env python3
"""Frost's living props atlas: Backdrop3/props.png + props.json.

The approved props are 8-cell strips of 128 px cells, already graded to the landmark rules (forms value p90 ~.46, lamps capped
at .92, no pink as drawn): round 1 (2026-11-28) strips/<name>.png, one drawing each; round 2 (props_more, 2026-11-28: snowcat,
radar, fishcamp, flares, tanker, bears, narwhal, sonde, and the crashed heli -- four colour / pose variants each -- and seals, one) strips/<name>_<v>.png;
the crashed STARSHIP (user-approved 2026-11-28, replacing the belly-landed airlift: "massive in the backdrop as a prop") is four
8-cell strips of 256 px cells (CELLS). This only lays them out: the 128 px strips two to a row (frames left to right), in NAMES
order, variants in order, then the 256 px strips one to a row. A one-drawing prop's
cells are <name>_00 .. <name>_07, a variant's <name>_<v>_00 .. _07, so BackdropAtlas.Frames(FrostPropsTuning.Strip(kind, v))
returns its strip. The manifest is the landmarks' Unity-rect JSON (n, x, y, w, h; y measured from the BOTTOM of the sheet).
The sheet is 2048 x 3840 (43 strips of 128 px cells in 22 rows, then 4 of 256 px; WorldBackdropImport.IsFrostProps never clamps it).

    python3 build_props_atlas.py            # writes Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/props.{png,json}
    python3 build_props_atlas.py --check    # verifies the installed sheet matches the strips

NO PINK (WorldCue.IsPink: hue 285-352, saturation > .25, value > .3 -- the hostile cue outside Space). The strips pass as
painted, but the icebreaker's dark crimson hull (hue 344-349, value < .3) is lifted past value .3 by the Frost grade (x1.95)
and then reads pink: every coloured texel with a hue in PINK_HUES is turned to HULL_HUE (a true red; the grade keeps hue).

Keep NAMES / VARIANTS in step with FrostPropsTuning.Names / Variants (FrostProps.cs).
"""
import json
import sys
from pathlib import Path

import colorsys

from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = Path(sys.argv[sys.argv.index("--out") + 1]) if "--out" in sys.argv else HERE.parents[5] / "Backgrounds" / "Resources" / "Worlds" / "Frost" / "Backdrop3"
NAMES = ["whale", "orca", "station", "lab", "icebreaker", "plane",
         "snowcat", "radar", "fishcamp", "flares", "tanker", "bears", "narwhal", "sonde", "seals", "heli", "starship"]
VARIANTS = [1, 1, 1, 1, 1, 1, 4, 4, 4, 4, 4, 4, 4, 4, 1, 4, 4]
CELLS = {"starship": 256}                   # per-kind cell size (FrostPropsTuning.Cell); the rest 128
CELL, FRAMES, PER_ROW = 128, 8, 2
STRIPS = [(n, v) for n, k in zip(NAMES, VARIANTS) for v in range(k) if n not in CELLS]
BIG = [(n, v) for n, k in zip(NAMES, VARIANTS) for v in range(k) if n in CELLS]
W = CELL * FRAMES * PER_ROW
SMALL_H = CELL * ((len(STRIPS) + PER_ROW - 1) // PER_ROW)
H = SMALL_H + sum(CELLS[n] for n, _ in BIG)
assert all(CELLS[n] * FRAMES == W for n, _ in BIG)


def base(name, v):
    return name if VARIANTS[NAMES.index(name)] == 1 else f"{name}_{v}"
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
    for i, (name, v) in enumerate(STRIPS):
        b = base(name, v)
        strip = Image.open(HERE / "strips" / f"{b}.png").convert("RGBA")
        assert strip.size == (CELL * FRAMES, CELL), (b, strip.size)
        depink(strip)
        r, x0 = i // PER_ROW, (i % PER_ROW) * CELL * FRAMES
        sheet.paste(strip, (x0, r * CELL))
        for f in range(FRAMES):
            sprites.append({"n": f"{b}_{f:02d}", "x": x0 + f * CELL, "y": H - (r + 1) * CELL, "w": CELL, "h": CELL})
    top = SMALL_H
    for name, v in BIG:
        b, c = base(name, v), CELLS[name]
        strip = Image.open(HERE / "strips" / f"{b}.png").convert("RGBA")
        assert strip.size == (c * FRAMES, c), (b, strip.size)
        depink(strip)
        sheet.paste(strip, (0, top))
        for f in range(FRAMES):
            sprites.append({"n": f"{b}_{f:02d}", "x": f * c, "y": H - top - c, "w": c, "h": c})
        top += c
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
