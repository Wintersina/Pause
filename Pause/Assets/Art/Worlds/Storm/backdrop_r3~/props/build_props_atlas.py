#!/usr/bin/env python3
"""Storm's living props: Backdrop3/props.{png,json} (ground kinds) and Backdrop3/aloft.{png,json} (aloft kinds).

User-approved 2026-11-28 ("storm stuff approved with latest version go storm"): the multi-colour liveries of
pl/storm/r3/build_props2.py, 8-frame horizontal strips (lights baked into the frames). Two depths (user: "some of these
items ... can be closer and larger in atmosphere level"):
  props  (pinned to the ground, StormProps.cs): refinery 0-3, mast 0-3, wreck 0-3 (sinking into the deck), vortex 0-2,
         cell 0-2 (lightning inside a cloud tower), gunship 0-3 (the approved gunship as a crashed derelict sunk in the deck,
         gunship_wreck.py). 128 px cells (vortex 160, gunship 176): drawn ~1:1 on a 1080 px phone.
  aloft  (closer to the camera, StormProps.cs): aerostat 0-3 (tethered), convoy 0-2 (moored to buoys: moor.py; the moored skimmer 0-3 is a GROUND prop beside the buoy chains; user
         rule 2026-11-28: nothing flies around that the player cannot shoot), jelly 0-6. 160 px cells (convoy 240)
         drawn 1.2 - 1.45x with a baked depth haze (a slight blur), so the art holds at the larger size.
Every cell sits in a 4 px transparent gutter (ASTC blocks and bilinear sampling never reach a neighbour). Sprites are
named <kind>_<variant>_<frame 00..07>, BackdropAtlas.Frames("<kind>_<variant>") returns a strip.

    python3 build_props_atlas.py --src <dir with bd_storm_prop_<kind>/r1_<k>.png>   # (re)cut strips/ from the masters
    python3 build_props_atlas.py                                                    # pack strips/ into the two atlases
NO PINK: every texel with hue 280-356 (s > .12) is pulled to 276 (blue-violet); the masters measure 0 px already.
Keep the kinds / variants in step with StormPropsTuning (StormProps.cs).
"""
import json, sys, colorsys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[3] / "Backgrounds" / "Resources" / "Worlds" / "Storm" / "Backdrop3"
FRAMES, GUT = 8, 4
GROUND = [("refinery", 4, 128), ("mast", 4, 128), ("wreck", 4, 128), ("vortex", 3, 160), ("cell", 3, 128), ("gunship", 4, 176), ("tug", 4, 176), ("rods", 4, 128), ("buoys", 4, 128), ("skimmer", 4, 128)]
ALOFT = [("aerostat", 4, 160), ("convoy", 3, 240), ("jelly", 7, 160), ("rays", 4, 160)]
HAZE = .55          # aloft: gaussian blur radius in atlas px (depth haze)
WIDTH = 2048


def depink(a):
    rgb = a[..., :3] / 255.0
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = np.maximum(mx - mn, 1e-9)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0)
    bad = (a[..., 3] > 0) & (s > .12) & (h >= 280) & (h < 356)
    for y, x in zip(*np.nonzero(bad)):
        rr, gg, bb = colorsys.hsv_to_rgb(276 / 360, s[y, x], mx[y, x])
        a[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
    return int(bad.sum())


def cut(src):
    (HERE / "strips").mkdir(exist_ok=True)
    for group, aloft in ((GROUND, False), (ALOFT, True)):
        for kind, n, cell in group:
            for k in range(n):
                im = Image.open(Path(src) / f"bd_storm_prop_{kind}" / f"r1_{k}.png").convert("RGBA")
                N = im.height
                fr = []
                for f in range(FRAMES):
                    c = im.crop((f * N, 0, f * N + N, N))
                    # premultiplied resample (no dark fringes)
                    a = np.asarray(c).astype(np.float32) / 255
                    a[..., :3] *= a[..., 3:]
                    p = Image.fromarray((a * 255 + .5).astype(np.uint8), "RGBA").resize((cell, cell), Image.LANCZOS)
                    if aloft:
                        p = p.filter(ImageFilter.GaussianBlur(HAZE))
                    q = np.asarray(p).astype(np.float32) / 255
                    q[..., :3] = np.where(q[..., 3:] > 1e-4, q[..., :3] / np.maximum(q[..., 3:], 1e-4), 0)
                    fr.append(Image.fromarray((np.clip(q, 0, 1) * 255 + .5).astype(np.uint8), "RGBA"))
                strip = Image.new("RGBA", (cell * FRAMES, cell))
                for f, c in enumerate(fr):
                    strip.paste(c, (f * cell, 0))
                strip.save(HERE / "strips" / f"{kind}_{k}.png", optimize=True)
    print("cut strips into", HERE / "strips")


def pack(group):
    cells = []
    for kind, n, cell in group:
        for k in range(n):
            strip = np.array(Image.open(HERE / "strips" / f"{kind}_{k}.png").convert("RGBA"))
            assert strip.shape[:2] == (cell, cell * FRAMES), (kind, k, strip.shape)
            depink(strip)
            for f in range(FRAMES):
                cells.append((f"{kind}_{k}_{f:02d}", strip[:, f * cell:(f + 1) * cell]))
    # shelf pack, tallest first keeps the shelves full; cells of one size stay in order
    cells.sort(key=lambda c: -c[1].shape[0])
    x = y = shelf = 0
    rects = []
    for name, px in cells:
        s = px.shape[0] + 2 * GUT
        if x + s > WIDTH:
            x, y, shelf = 0, y + shelf, 0
        rects.append((name, px, x + GUT, y + GUT))
        x += s
        shelf = max(shelf, s)
    H = y + shelf
    H = (H + 3) // 4 * 4
    W = max(r[2] + r[1].shape[1] + GUT for r in rects)
    W = (W + 3) // 4 * 4
    sheet = np.zeros((H, W, 4), np.uint8)
    sprites = []
    for name, px, x, y in rects:
        h, w = px.shape[:2]
        sheet[y:y + h, x:x + w] = px
        sprites.append({"n": name, "x": x, "y": H - y - h, "w": w, "h": h})
    sprites.sort(key=lambda s: s["n"])
    return Image.fromarray(sheet, "RGBA"), {"sheetW": W, "sheetH": H, "sprites": sprites}


def main():
    if "--src" in sys.argv:
        cut(sys.argv[sys.argv.index("--src") + 1])
    for name, group in (("props", GROUND), ("aloft", ALOFT)):
        sheet, js = pack(group)
        sheet.save(OUT / f"{name}.png", optimize=True)
        (OUT / f"{name}.json").write_text(json.dumps(js, indent=1) + "\n")
        print("wrote", name, sheet.size, len(js["sprites"]), "cells")


if __name__ == "__main__":
    main()
