#!/usr/bin/env python3
"""Design-language outline pass (docs/ship-scale-and-lockon-analysis.md, 3.3 rule 3): a thin pale, world-tinted outline on the
silhouettes that lack one, but only on ONE SIDE of each ship with a soft fade across its middle line, so there is no hard cut and
no ring all round (the user rejected full rings: Glow Jelly, Singularity Hauler). The side per ship is in modes.json (default
bottom; top | right | none exist; a full ring is not allowed).

Method as the Tide pass (Art/Enemies/src~/tide_border): written only into (almost) transparent pixels touching the silhouette, once
per cell; the drawing itself is untouched, grids / anchors / registration identical. No pink. Elites 1 px, enemies 2 px (drawn at
0.33-0.55 screen px per art px a 1 px outline cannot render).

  python3 add_border.py --init     copy the live strips of every listed ship into ./original/ (once; they are the source)
  python3 add_border.py            write ./staged/ (a preview, untracked) from ./original/ (nothing else changes)
  python3 add_border.py --wire     write the live strips in Resources from ./original/ (re-runnable)
  python3 add_border.py --check    only verify (interior untouched, no pink outside Space)
  --only key,key  restrict to some ships;  --res <Resources dir>  (default: this tree's)
"""
import sys, json, shutil, colorsys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = Path(sys.argv[sys.argv.index("--res") + 1]) if "--res" in sys.argv else HERE.parents[2] / "Resources"
ONLY = set(sys.argv[sys.argv.index("--only") + 1].split(",")) if "--only" in sys.argv else None
SOLID = 96
TONE = {  # pale, inside each world's palette, never pink
    "frost": (221, 243, 255), "space": (207, 224, 255), "storm": (214, 226, 246), "tide": (210, 244, 230),
    "desert": (246, 226, 176), "ember": (255, 226, 184), "verdant": (228, 246, 184),
}
MODES = ("bottom", "top", "right", "none")   # never "full": no ship is outlined all the way round


def dilate(mask):
    out = mask.copy()
    p = np.pad(mask, 1)
    h, w = mask.shape
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx or dy:
                out |= p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
    return out


def ramp(solid, mode, fade):
    """Alpha (0..255) of the outline per pixel of one cell: 255 on the outlined side, 0 on the other, a smoothstep of `fade` x the
    silhouette's extent across its middle line. Image y grows downwards: 'bottom' = larger y."""
    h, w = solid.shape
    ys, xs = np.where(solid)
    if len(ys) == 0:
        return np.zeros((h, w))
    if mode == "right":
        lo, hi, n = xs.min(), xs.max(), w
        t = np.arange(n)[None, :].repeat(h, axis=0).astype(float)
    else:
        lo, hi, n = ys.min(), ys.max(), h
        t = np.arange(n)[:, None].repeat(w, axis=1).astype(float)
    mid, half = (lo + hi) / 2.0, max(1.0, (hi - lo) * fade / 2.0)
    u = np.clip((t - (mid - half)) / (2 * half), 0, 1)
    if mode == "top":
        u = 1 - u
    return 255 * u * u * (3 - 2 * u)


def border(strip, width, colour, cell, mode, fade):
    a = strip[:, :, 3]
    out = strip.copy()
    for c in range(a.shape[1] // cell):
        sl = slice(c * cell, (c + 1) * cell)
        solid = a[:, sl] >= SOLID
        grown = solid
        for _ in range(width):
            grown = dilate(grown)
        ring = grown & ~solid & (a[:, sl] < SOLID)
        cellpx = out[:, sl]
        assert mode in ("bottom", "top", "right"), mode
        alpha = ramp(solid, mode, fade)
        cellpx[ring, :3] = colour
        cellpx[ring, 3] = np.maximum(cellpx[ring, 3], alpha[ring].astype(np.uint8))
    return out


def plan():
    """[(kind, key, world, mode)] for every ship that gets an outline."""
    lang = json.load(open(HERE / "lacking.json"))
    cfg = json.load(open(HERE / "modes.json"))
    ships = {k: v for k, v in cfg["ships"].items()}
    out = []
    keys = {("elites" if "_elite_" in k else "enemies", k): w for kind in lang for k, w in lang[kind].items()}
    for k in ships:
        keys.setdefault(("elites" if "_elite_" in k else "enemies", k), k.split("_")[0])
    for (kind, key), world in sorted(keys.items()):
        mode = ships.get(key, cfg["default"])
        assert mode in MODES, (key, mode)
        if mode == "none" or (ONLY and key not in ONLY):
            continue
        out.append((kind, key, world, mode))
    return out, cfg["fade"]


def live_path(kind, key, world):
    return RES / "Elites" / world.capitalize() / f"{key}.png" if kind == "elites" else RES / "Enemies" / f"{key}.png"


def is_pink(rgb):
    h, s, v = colorsys.rgb_to_hsv(*(np.array(rgb) / 255.0))
    return s > .35 and v > .4 and .78 < h < .96


def main():
    items, fade = plan()
    (HERE / "original").mkdir(exist_ok=True)
    for kind, key, world, mode in items:
        orig = HERE / "original" / f"{key}.png"
        live = live_path(kind, key, world)
        if "--init" in sys.argv:
            if not orig.exists():
                shutil.copy(live, orig)
            continue
        src = np.array(Image.open(orig).convert("RGBA"))
        cell = src.shape[0]
        res = border(src, 1 if kind == "elites" and cell <= 192 else 2, TONE[world], cell, mode, fade)   # (an HD 384 px elite cell is drawn at ~0.9 screen px per art px: 2 px)
        solid = src[:, :, 3] >= SOLID
        assert (res[solid] == src[solid]).all(), key
        if world != "space":
            new = (res != src).any(axis=2)   # only what the pass wrote
            assert not any(is_pink(p[:3]) for p in res[new][::7]), key
        if "--check" in sys.argv:
            continue
        if "--wire" in sys.argv:
            Image.fromarray(res).save(live)
        else:   # a preview only: nothing live changes
            dst = HERE / "staged" / kind / f"{key}.png"
            dst.parent.mkdir(parents=True, exist_ok=True)
            Image.fromarray(res).save(dst)
    print(len(items), "ships,", sorted({m for *_, m in items}))


if __name__ == "__main__":
    main()
