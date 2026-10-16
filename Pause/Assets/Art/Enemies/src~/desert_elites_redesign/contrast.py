#!/usr/bin/env python3
"""Value contrast of every Desert elite against the three Desert backdrops (Backdrop3 v1..v3 mid tiles), no outline involved.
For each ship's hover cell: the share of its opaque pixels whose WCAG contrast against the tile's mean luminance is >= 3:1 ('stand out'),
and the share that sits within 1.5:1 of it ('melts in'); the worst tile per ship is reported. Old strips (git origin/master) for comparison.
  python3 contrast.py [old strips dir]"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

H = Path(__file__).resolve().parent
ART = H.parents[2]
KEYS = "dune_stalker caravan_hauler glass_cannon lens_matriarch mirror_bastion scorpion_walker".split()


def lin(c):
    c = c / 255.0
    return np.where(c <= .03928, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def lum(rgb):
    r = lin(rgb[..., 0]); g = lin(rgb[..., 1]); b = lin(rgb[..., 2])
    return .2126 * r + .7152 * g + .0722 * b


def measure(strip):
    im = np.array(Image.open(strip).convert("RGBA"))
    h = im.shape[0]
    cell = im[:, 3 * h:4 * h]
    m = cell[..., 3] > 200
    L = lum(cell[..., :3].astype(float))[m]
    res = []
    for v in (1, 2, 3):
        t = np.array(Image.open(ART / f"Backgrounds/Resources/Worlds/Desert/Backdrop3/v{v}/mid.png").convert("RGB")).astype(float)
        hh, ww = t.shape[:2]
        c = t[hh // 4:3 * hh // 4, ww // 4:3 * ww // 4]
        bl = lum(c)
        mean = bl.mean(); lo, hi = np.percentile(bl, 5), np.percentile(bl, 95)
        def cr(a, b):
            a, b = np.maximum(a, b), np.minimum(a, b)
            return (a + .05) / (b + .05)
        ratio = cr(L, mean)
        res.append((v, float((ratio >= 3).mean()), float((ratio < 1.5).mean()), float((np.minimum(cr(L, lo), cr(L, hi)) >= 1.5).mean())))
    return res


def main():
    old = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    for k in KEYS:
        name = f"desert_elite_{k}.png"
        for label, p in (("old", old / name if old else None), ("new", ART / "Resources/Elites/Desert" / name)):
            if p is None or not p.exists():
                continue
            r = measure(p)
            worst = min(r, key=lambda t: t[1])
            print(f"{k:16s} {label}: stand-out(>=3:1 vs mean) " + " ".join(f"v{v}={s*100:4.0f}%" for v, s, _, _ in r) + f"   melts-in(<1.5:1) worst {max(t[2] for t in r)*100:3.0f}%")


main()
