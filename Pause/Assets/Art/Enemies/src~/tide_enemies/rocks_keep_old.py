#!/usr/bin/env python3
"""The two Tide rocks the user likes as they were: Brain Coral (violet-grey, brass fittings) and Spine Urchin (dark indigo, spines, yellow core).
A by-material recolour of the original teal sources (hull hue-rotated with its luminance ramp intact, brass kept, glow accent), with NO thrusters or
flames: they are rocks. Writes flying_src/<rock>_flying.png. (Staghorn and Islet are Codex redesigns: rocks_codex.sh.)"""
import math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

H = Path(__file__).resolve().parent / "flying_src"
# hull hue / glow hue (degrees). Hull = the original teal+olive material, hue-rotated with its luminance ramp intact;
# brass stays brass (copper/gold second material); bright polyps/cores take the accent glow; greys stay gunmetal.
ZONES = {
    "tide_rock_brain":    dict(hull=265, glow=48),   # violet coral, brass fittings, sun-yellow polyps
    "tide_rock_staghorn": dict(hull=18,  glow=205, warm=.3),  # coral-orange branches, electric-blue polyp tips
    "tide_rock_urchin":   dict(hull=228, glow=46),   # deep indigo-blue shell, sun-yellow core
    "tide_rock_islet":    dict(hull=205, glow=24),   # steel-blue slab, tangerine glow
}


def recolour(im, hull, glow, warm=0.0):
    rgba = np.asarray(im.convert("RGBA"))
    hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
    h = hsv[:, :, 0] * 360 / 255; s = hsv[:, :, 1] / 255; v = hsv[:, :, 2] / 255
    alpha = rgba[:, :, 3] > 8
    teal = alpha & (s > .15) & (h >= 60) & (h < 210)
    glowy = teal & (v > .74)
    hullm = teal & ~glowy
    brass = alpha & (s > .15) & ((h < 60) | (h >= 330))
    nh, ns, nv = h.copy(), s.copy(), v.copy()
    nh[hullm] = hull; ns[hullm] = np.clip(s[hullm] * 1.1 + .22, 0, .9); nv[hullm] = np.clip(v[hullm] * 1.12 + .03, 0, 1)
    nh[glowy] = glow; ns[glowy] = .78; nv[glowy] = 1.0
    nh[brass] = np.clip(h[brass], 18 if not warm else 14, 44 if not warm else 30); ns[brass] = np.clip(s[brass] * 1.1 + .1 + warm, 0, .95); nv[brass] = np.clip(v[brass] * (1.1 + warm) + .03, 0, 1)
    out = np.dstack((nh * 255 / 360, ns * 255, nv * 255)).clip(0, 255).astype(np.uint8)
    rgb = np.asarray(Image.fromarray(out, "HSV").convert("RGB")).copy()
    keep = ~(hullm | glowy | brass)
    rgb[keep] = rgba[:, :, :3][keep]
    return Image.fromarray(np.dstack((rgb, rgba[:, :, 3])), "RGBA")


for n, z in ZONES.items():
    if n not in ("tide_rock_brain", "tide_rock_urchin"):
        continue
    im = Image.open(H / f"{n}_in.png").convert("RGBA")
    out = recolour(im, **z)
    out.save(H / f"{n}_flying.png")
    print(n, out.size)
