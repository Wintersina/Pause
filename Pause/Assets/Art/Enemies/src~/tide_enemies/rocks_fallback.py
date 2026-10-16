#!/usr/bin/env python3
"""Fallback (Codex quota exhausted): multi-accent recolour + hover thrusters for the four Tide hazard rocks.
Writes flying_src/<rock>_flying.png from the *_in.png sources. Replace by Codex repaints via flying_all.sh when quota returns."""
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


def thruster(d, x, y, r, flame):
    d.polygon([(x - r, y), (x + r, y), (x + r * .6, y + r * 1.4), (x - r * .6, y + r * 1.4)], fill=(60, 66, 92, 255), outline=(14, 18, 34, 255))
    d.rectangle((x - r, y - r * .5, x + r, y), fill=(255, 196, 40, 255), outline=(14, 18, 34, 255))
    for k, col in enumerate(((45, 156, 255, 255), (170, 225, 255, 255), (255, 255, 255, 255))):
        s = 1 - k * .28
        d.polygon([(x - r * .7 * s, y + r * 1.4), (x + r * .7 * s, y + r * 1.4), (x, y + r * 1.4 + flame * s)], fill=col)


def add_thrusters(im, name):
    ys, xs = np.where(np.asarray(im.getchannel("A")) > 100)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    w, hgt = x1 - x0, y1 - y0
    pad = int(hgt * .3)
    cv = Image.new("RGBA", (im.width, im.height + pad), (0, 0, 0, 0))
    cv.alpha_composite(im, (0, 0))
    d = ImageDraw.Draw(cv)
    r = max(10, int(w * .045)); fl = int(hgt * .16)
    if name == "tide_rock_staghorn":
        pts = [(x0 + w * .38, y1 - hgt * .14), (x0 + w * .62, y1 - hgt * .14)]
    elif name == "tide_rock_urchin":
        pts = [(x0 + w * .30, y1 - hgt * .20), (x0 + w * .5, y1 - hgt * .12), (x0 + w * .70, y1 - hgt * .20)]
    else:
        pts = [(x0 + w * .22, y1 - hgt * .20), (x0 + w * .42, y1 - hgt * .12), (x0 + w * .62, y1 - hgt * .12), (x0 + w * .80, y1 - hgt * .20)]
    for px, py in pts:
        thruster(d, int(px), int(py), r, fl)
    return cv


for n, z in ZONES.items():
    im = Image.open(H / f"{n}_in.png").convert("RGBA")
    out = add_thrusters(recolour(im, **z), n)
    out.save(H / f"{n}_flying.png")
    print(n, out.size)
