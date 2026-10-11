#!/usr/bin/env python3
"""Ember colour pass: the refreshed strips were too white/cream/grey. A deterministic HSV remap (grids, alpha, anchors,
outlines untouched) pushes the palette to hot fiery orange-red:

  dark steel / basalt (v < .5)   -> deep ember hulls: hue 17-21, saturation .45-.80 (never true red: hue stays >= 16)
  mid tones                      -> lava orange: hue 20-27, saturation up to .95
  bright low-saturation (cream rims, pale plating) -> pale orange / ember glow rim (hue ~24), not white
  glow pixels already saturated  -> kept orange, pushed hotter; the hottest ones (v > .9) go yellow-orange (hue 32)
  only the very brightest, near-white pixels (v >= .96, s < .25) stay white-hot (small cores)

    python3 recolor.py            # reads ../ember_refresh/strips + ../ember_elites_refresh/strips, writes ./out/{enemies,elites}
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
SRC = HERE.parent
OUT = HERE / "out"


def rgb2hsv(a):
    r, g, b = a[..., 0] / 255., a[..., 1] / 255., a[..., 2] / 255.
    mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rc = m & (mx == r); gc = m & (mx == g) & ~rc; bc = m & ~rc & ~gc
    h[rc] = ((g - b)[rc] / d[rc]) % 6
    h[gc] = (b - r)[gc] / d[gc] + 2
    h[bc] = (r - g)[bc] / d[bc] + 4
    h = h * 60.0
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def hsv2rgb(h, s, v):
    h = (h % 360) / 60.0
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p = v * (1 - s); q = v * (1 - f * s); t = v * (1 - (1 - f) * s)
    r = np.choose(i, [v, q, p, p, t, v]); g = np.choose(i, [t, v, v, q, p, p]); b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], -1) * 255


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def keep_off_red(rgba, min_hue=17.5):
    """Player-red guard (hue 345..15 at saturation > .5 must stay < 2% of the texels): after 8-bit rounding, dark ember tints can land
    a few degrees low. Raise the green channel of any such texel just enough to reach `min_hue` (a tiny change, hue only)."""
    a = np.asarray(rgba).astype(np.float64).copy()
    h, s, v = rgb2hsv(a[..., :3])
    bad = (a[..., 3] > 0) & (s > .45) & ((h < min_hue) | (h > 340))
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = np.maximum(r, np.maximum(g, b))
    red_dom = bad & (r >= g) & (r >= b)
    need = b + (r - b) * (min_hue + .8) / 60.0
    a[..., 1] = np.where(red_dom, np.minimum(r, np.maximum(g, np.ceil(need))), g)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def recolor(img, heat=1.0):
    """Hue/saturation remap only (VALUE untouched, so every detail, outline and the matte look survive). A value-driven fire ramp:
    darks (steel/basalt/outline) get a deep ember-red tint (hue 17-18), mid tones go lava orange (hue 21-23, strongly saturated),
    lights go hot orange-yellow (hue 26-30, softer saturation) instead of cream/white; already-saturated pixels (glows, brass,
    lava seams) keep their own hue pulled into 17-32 and get hotter. Only near-white cores (v >= .96, s < .25) stay white-hot.
    Blend strength follows how unsaturated a pixel was, so material differences (brass vs basalt vs steel) survive."""
    a = np.asarray(img.convert("RGBA")).astype(np.float64)
    h, s, v = rgb2hsv(a[..., :3])
    solid = a[..., 3] > 0
    k_lo = 1 - smooth(v, .30, .45)                 # 1 in the darks
    k_hi = smooth(v, .62, .80)                     # 1 in the lights
    k_mid = np.clip(1 - k_lo - k_hi, 0, 1)
    h_t = k_lo * 19.0 + k_mid * 21.0 + k_hi * 25.0
    s_t = k_lo * .74 + k_mid * .96 + k_hi * .80
    # the pull toward the fire ramp: strong for everything, a little weaker for already-saturated pixels (they keep more of their own hue)
    w = (.96 - .12 * smooth(s, .45, .8)) * heat
    dh = ((h_t - h + 180) % 360) - 180
    h_fin = h + dh * w
    h_fin = np.where(s < .08, h_t, h_fin)
    s_fin = s + (s_t - s) * w
    s_fin = np.where(s >= .45, np.maximum(s_fin, np.clip(s * 1.10, 0, .98)), s_fin)
    white = (v >= .96) & (s < .25)
    s_fin = np.where(white, s * .6, s_fin)
    # heat: lift the mid and light tones (a gamma on value that leaves the darks and the outlines alone) so the orange reads as fire, not bronze
    lift = smooth(v, .18, .40)
    v_fin = np.where(white, v, v * (1 - lift) + (v ** (.70 if heat >= 1 else 1.0)) * lift)
    out = hsv2rgb(h_fin, np.clip(s_fin, 0, .98), np.clip(v_fin, 0, 1))
    res = a.copy()
    res[..., :3] = np.where(solid[..., None], out, a[..., :3])
    return keep_off_red(Image.fromarray(np.clip(res, 0, 255).astype(np.uint8), "RGBA"))


def main():
    for kind, folder in (("enemies", SRC / "ember_refresh" / "strips"), ("elites", SRC / "ember_elites_refresh" / "strips")):
        (OUT / kind).mkdir(parents=True, exist_ok=True)
        for f in sorted(folder.glob("ember_*.png")):
            recolor(Image.open(f)).save(OUT / kind / f.name)
            print(kind, f.name)


if __name__ == "__main__":
    main()
