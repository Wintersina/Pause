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


# The aloft pieces are DRAWN under a cool haze tint (StormPropsTuning.AloftTint): it lifts blue against red, which turns a red lamp
# or band magenta. Any texel pink AS DRAWN loses the channel that makes it so (red side: blue down to the tinted green; violet side:
# red down to the tinted green), so it stays red / blue on screen.
TINT = np.array([.80, .84, .93])


def tint_safe(a):
    x = a.astype(np.float64) / 255
    t = x[..., :3] * TINT
    r, g, b = t[..., 0], t[..., 1], t[..., 2]
    mx, mn = t.max(-1), t.min(-1)
    d = np.maximum(mx - mn, 1e-9)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0)
    bad = (x[..., 3] > 0) & (s > .2) & (mx > .25) & (h >= 280) & (h <= 356)
    red = bad & (h >= 318)
    vio = bad & (h < 318)
    x[..., 2] = np.where(red, np.minimum(x[..., 2], x[..., 1] * TINT[1] / TINT[2]), x[..., 2])
    x[..., 0] = np.where(vio, np.minimum(x[..., 0], x[..., 1] * TINT[1] / TINT[0]), x[..., 0])
    a[..., :3] = np.clip(x[..., :3] * 255 + .5, 0, 255).astype(np.uint8)
    return int(bad.sum())


# ---- BLEND (user 2026-11-28: "all props should be blended in into the world correctly such that it makes sense and doesnt look
# out of place"): every ground prop is graded toward the cool cloud deck (v1-v4 mid tiles average rgb .14 .18 .24), keeps its lamps
# unscaled, gets a soft contact shadow cast down-right (the deck and the props are lit from the top-left), a feathered silhouette and a
# cloud-vapour skirt round its base where it meets the deck. The cell (a lit cloud tower) and the vortex (a cloud whirl) are the deck
# already; the crashed gunship has its own painted cloud skirt (graded and shadowed, no extra skirt). Aloft pieces get the grade only,
# lighter (they hang nearer the camera, above the deck's haze).
DECK = np.array([.14, .18, .24])
DECK_NORM = DECK / (DECK @ [.2126, .7152, .0722])
MIST = np.array([.40, .46, .56])
SHADOW = np.array([.03, .045, .07])
BLEND = {   # kind: (grade, shadow alpha, skirt alpha)
    "refinery": (1, .6, .6), "mast": (1, .55, .5), "wreck": (1, .6, .75), "gunship": (1, .5, 0), "tug": (1, .6, .6),
    "rods": (1, .6, .6), "buoys": (1, .55, .6), "skimmer": (1, .6, .6),
    "aerostat": (.6, 0, 0), "convoy": (.6, 0, 0), "rays": (.6, 0, 0), "jelly": (.3, 0, 0),
}
LAMP_LO, LAMP_HI = .62, .78   # texels this bright (max channel) keep their colour: lamps and blinking lights stay unscaled


def blur(x, r):
    # separable gaussian, zero outside the cell
    k = int(np.ceil(3 * r))
    t = np.arange(-k, k + 1, dtype=np.float32)
    w = np.exp(-.5 * (t / max(r, 1e-3)) ** 2)
    w /= w.sum()
    y = np.apply_along_axis(lambda v: np.convolve(v, w, "same"), 0, x.astype(np.float32))
    return np.apply_along_axis(lambda v: np.convolve(v, w, "same"), 1, y)


def blend_cell(px, kind):
    if kind not in BLEND:
        return px
    g, sh, sk = BLEND[kind]
    a = px.astype(np.float32) / 255
    rgb, al = a[..., :3], a[..., 3]
    n = px.shape[0] / 128.0
    # grade: luminance carried on the deck's hue, then a little atmospheric pull to the deck; lamps kept
    lum = rgb @ np.array([.2126, .7152, .0722], np.float32)
    cool = lum[..., None] * DECK_NORM
    graded = rgb * (1 - .42 * g) + cool * (.42 * g)
    graded = graded * (1 - .14 * g) + DECK * 1.3 * (.14 * g)
    graded *= 1 - .08 * g
    keep = np.clip((rgb.max(-1) - LAMP_LO) / (LAMP_HI - LAMP_LO), 0, 1)[..., None]
    rgb = np.clip(graded * (1 - keep) + rgb * keep, 0, 1)
    if sh <= 0 and sk <= 0:
        out = np.dstack([rgb, al])
        return (out * 255 + .5).astype(np.uint8)
    # feather: a hair of softness on the cut-out silhouette
    al = .6 * al + .4 * np.minimum(al, blur(al, .6 * n))
    H, W = al.shape
    ys = np.nonzero(al.max(1) > .1)[0]
    top, bot = (ys[0], ys[-1]) if len(ys) else (0, H - 1)
    # the base: the lowest 18 % of the piece fades into a cloud-vapour skirt
    yy = np.arange(H, dtype=np.float32)[:, None]
    band = np.clip((yy - (bot - .22 * (bot - top))) / max(1., .22 * (bot - top)), 0, 1) ** .8
    skirt = np.clip(blur(al, 2.8 * n) * 1.5, 0, 1) * band * sk
    # contact shadow: the silhouette, softened, cast down-right onto the deck
    dx, dy = int(round(3 * n)), int(round(4 * n))
    sil = np.zeros_like(al)
    sil[dy:, dx:] = al[:H - dy, :W - dx]
    shadow = blur(sil, 2.5 * n) * sh
    # composite (premultiplied): shadow under the prop, the skirt over its base
    pr = SHADOW * shadow[..., None]
    pa = shadow
    pr = rgb * al[..., None] + pr * (1 - al[..., None])
    pa = al + pa * (1 - al)
    mist_a = np.clip(skirt * (.55 + .45 * al), 0, 1)
    pr = MIST * mist_a[..., None] + pr * (1 - mist_a[..., None])
    pa = mist_a + pa * (1 - mist_a)
    pa = pa * (1 - .35 * band * sk)          # the very base thins into the deck (feathered mount)
    pr = pr * (1 - .35 * band * sk)[..., None]
    rgb = np.where(pa[..., None] > 1e-4, pr / np.maximum(pa[..., None], 1e-4), 0)
    out = np.dstack([np.clip(rgb, 0, 1), np.clip(pa, 0, 1)])
    return (out * 255 + .5).astype(np.uint8)


def pack(group):
    cells = []
    for kind, n, cell in group:
        for k in range(n):
            strip = np.array(Image.open(HERE / "strips" / f"{kind}_{k}.png").convert("RGBA"))
            assert strip.shape[:2] == (cell, cell * FRAMES), (kind, k, strip.shape)
            if "--raw" not in sys.argv:
                for f in range(FRAMES):
                    strip[:, f * cell:(f + 1) * cell] = blend_cell(strip[:, f * cell:(f + 1) * cell], kind)
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
        if name == "aloft":
            arr = np.array(sheet)
            print("aloft: tint-safe", tint_safe(arr), "texels")
            sheet = Image.fromarray(arr, "RGBA")
        sheet.save(OUT / f"{name}.png", optimize=True)
        (OUT / f"{name}.json").write_text(json.dumps(js, indent=1) + "\n")
        print("wrote", name, sheet.size, len(js["sprites"]), "cells")


if __name__ == "__main__":
    main()
