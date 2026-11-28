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


# ---- BLEND (user 2026-11-28: "all props should be blended in into the world correctly such that it makes sense and doesnt look
# out of place"). Before the pink guard, every cell is: graded toward the ice palette (luminance carried on the ice's hue -- the
# v1-v4 mid tiles' ice measures rgb .16 .22 .25 as painted -- plus a little atmospheric pull to it; the drawing's lamps and lit
# windows, max channel >= LAMP_HI, keep their colour unscaled), its cut-out silhouette feathered by a hair, and -- for the props
# that stand on the ice -- a soft contact shadow cast down-right (the ground is lit from the top-left) and a snow drift banked
# round the lowest part of the piece. The props in open water (whale, orca, narwhal, icebreaker) already carry their wake / ripple
# rings: grade and feather only. The crashed starship has its own ground-level crash scar: grade, feather and a light shadow.
import numpy as np

ICE = np.array([.16, .22, .25])
ICE_NORM = ICE / (ICE @ [.2126, .7152, .0722])
SNOW = np.array([.25, .31, .34])
SHADOW = np.array([.01, .03, .06])
BLEND = {   # kind: (grade, shadow alpha, drift alpha)
    "station": (1, .5, .55), "lab": (1, .5, .55), "plane": (1, .5, .55), "snowcat": (1, .45, .5), "radar": (1, .5, .55),
    "fishcamp": (1, .4, .45), "flares": (1, .4, .45), "tanker": (1, .45, .6), "bears": (.8, .35, .4), "seals": (.8, .35, .4),
    "sonde": (1, .45, .5), "heli": (1, .5, .6), "starship": (1, .3, 0),
    "whale": (.6, 0, 0), "orca": (.6, 0, 0), "narwhal": (.6, 0, 0), "icebreaker": (.8, 0, 0),
}
LAMP_LO, LAMP_HI = .56, .72


def blur(x, r):
    k = int(np.ceil(3 * r))
    t = np.arange(-k, k + 1, dtype=np.float32)
    w = np.exp(-.5 * (t / max(r, 1e-3)) ** 2)
    w /= w.sum()
    y = np.apply_along_axis(lambda v: np.convolve(v, w, "same"), 0, x.astype(np.float32))
    return np.apply_along_axis(lambda v: np.convolve(v, w, "same"), 1, y)


def blend_cell(px, kind):
    g, sh, dr = BLEND[kind]
    a = px.astype(np.float32) / 255
    rgb, al = a[..., :3], a[..., 3]
    n = px.shape[0] / 128.0
    lum = rgb @ np.array([.2126, .7152, .0722], np.float32)
    graded = rgb * (1 - .32 * g) + lum[..., None] * ICE_NORM * (.32 * g)
    graded = graded * (1 - .10 * g) + ICE * (.10 * g)
    keep = np.clip((rgb.max(-1) - LAMP_LO) / (LAMP_HI - LAMP_LO), 0, 1)[..., None]
    rgb = np.clip(graded * (1 - keep) + rgb * keep, 0, 1)
    # feather: no hard cut-out edge
    al = .45 * al + .55 * np.minimum(al, blur(al, .8 * n))
    H, W = al.shape
    pr = rgb * al[..., None]
    pa = al.copy()
    if sh > 0:
        dx, dy = int(round(3 * n)), int(round(3 * n))
        sil = np.zeros_like(al)
        sil[dy:, dx:] = al[:H - dy, :W - dx]
        shadow = blur(sil, 2.2 * n) * sh
        pr = pr + SHADOW * shadow[..., None] * (1 - pa[..., None])
        pa = pa + shadow * (1 - pa)
    if dr > 0:
        ys = np.nonzero(al.max(1) > .1)[0]
        top, bot = (ys[0], ys[-1]) if len(ys) else (0, H - 1)
        yy = np.arange(H, dtype=np.float32)[:, None]
        band = np.clip((yy - (bot - .2 * (bot - top))) / max(1., .2 * (bot - top)), 0, 1) ** .8
        # the drift: snow piled against the base and spilling a little past it
        drift = np.clip(blur(al, 2.4 * n) * 1.6, 0, 1) * band * dr
        da = np.clip(drift * (.5 + .5 * al), 0, 1)
        pr = SNOW * da[..., None] + pr * (1 - da[..., None])
        pa = da + pa * (1 - da)
    rgb = np.where(pa[..., None] > 1e-4, pr / np.maximum(pa[..., None], 1e-4), 0)
    out = np.dstack([np.clip(rgb, 0, 1), np.clip(pa, 0, 1)])
    return (out * 255 + .5).astype(np.uint8)


def blend_strip(img, name):
    if "--raw" in sys.argv:
        return img
    a = np.array(img)
    c = a.shape[0]
    for f in range(a.shape[1] // c):
        a[:, f * c:(f + 1) * c] = blend_cell(a[:, f * c:(f + 1) * c], name)
    return Image.fromarray(a, "RGBA").copy()


def build():
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sprites = []
    for i, (name, v) in enumerate(STRIPS):
        b = base(name, v)
        strip = Image.open(HERE / "strips" / f"{b}.png").convert("RGBA")
        assert strip.size == (CELL * FRAMES, CELL), (b, strip.size)
        strip = blend_strip(strip, name)
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
        strip = blend_strip(strip, name)
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
