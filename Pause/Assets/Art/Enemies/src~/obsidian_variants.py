"""Ember Obsidian Shard colour variants (user, Oct 25: "make obsidian shards multi colors ... they are hard to notice").

Deterministic palette remaps of the ORIGINAL violet-black shard (variant 0 is that file, untouched):
  Art/Resources/Enemies/ember_rock_obsidian.png            1344x192, seven 192 px cells (idle x4, tell x2, hit)
  Art/Resources/Enemies/Death/ember_rock_obsidian.png       576x192, three 192 px cells
-> Art/Resources/Enemies/Variants/ember_rock_obsidian_<name>.png and Variants/Death/ember_rock_obsidian_<name>.png.

Silhouette, alpha, frames and layout are untouched; only RGB moves. The warm lava veins / crack glow / death burst (hue < 50 or > 340,
saturated) stay exactly as drawn, so every shard keeps the Ember cue. The cool glass body and its ice-white edges are re-hued: the body
keeps its darkness (obsidian) but is saturated enough to read as the variant's colour, the lit facets and edges are lifted.
No variant lands in the pink band (hue 280..352): vermilion sits at 14 deg and its hue wobble is clipped to +-14.

usage: obsidian_variants.py ASSETS_DIR     (ASSETS_DIR = .../Pause/Assets)
Names / hues here are mirrored by ObsidianShards.cs (Names) and pinned by ObsidianShardTest.
"""
import sys, os
import numpy as np
from PIL import Image

# name -> (target hue deg, body saturation floor, value gain, edge lift)
VARIANTS = {
    'teal':       (178, .62, 1.10, .20),
    'jade':       (128, .58, 1.12, .20),
    'acid':       (80,  .80, 1.50, .35),
    'vermilion':  (14,  .88, 1.55, .35),
}
BODY_HUE = 225.0       # the original blade's mean glass hue
MAXWOBBLE = 14.0       # deg: keeps vermilion (14) out of the pink band (280..352) and every variant on its own hue
SPREAD = .35           # how much of the original's hue wobble (+-60 deg) survives

def rgb2hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h * 60.0, s, mx

def hsv2rgb(h, s, v):
    h = (h % 360.0) / 60.0
    c = v * s; x = c * (1 - np.abs(h % 2 - 1)); m = v - c
    z = np.zeros_like(h)
    sec = h.astype(int) % 6
    r = np.select([sec == 0, sec == 1, sec == 2, sec == 3, sec == 4, sec == 5], [c, x, z, z, x, c])
    g = np.select([sec == 0, sec == 1, sec == 2, sec == 3, sec == 4, sec == 5], [x, c, c, x, z, z])
    b = np.select([sec == 0, sec == 1, sec == 2, sec == 3, sec == 4, sec == 5], [z, z, x, c, c, x])
    return np.stack([r + m, g + m, b + m], -1)

def remap(img, hue, sfloor, vgain, lift):
    a = np.asarray(img.convert('RGBA'), dtype=np.uint8).copy()
    rgb = a[..., :3].astype(np.float64) / 255.0
    h, s, v = rgb2hsv(rgb)
    warm = ((h < 50) | (h > 340)) & (s > .30)      # lava veins, crack glow, burst, damage rings: left alone
    dh = (h - BODY_HUE + 180) % 360 - 180
    nh = hue + np.clip(np.clip(dh, -60, 60) * SPREAD, -MAXWOBBLE, MAXWOBBLE)
    ice = (s < .28) & (v > .55)                    # the white glass edges and glints
    body = ~warm
    ns = np.where(ice, .38, np.clip(np.maximum(s, sfloor), 0, .95))
    # darkness is kept; mid and bright facets are lifted so the shard reads against the backdrop
    nv = np.where(v > .35, np.clip(v * vgain + lift * (v - .35), 0, 1), np.clip(v * (1 + (vgain - 1) * .6), 0, 1))
    out = hsv2rgb(nh, ns, nv)
    res = np.where(body[..., None], out, rgb)
    a[..., :3] = np.round(np.clip(res, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(a, 'RGBA')

def main(assets):
    base = os.path.join(assets, 'Art', 'Resources', 'Enemies')
    jobs = [('ember_rock_obsidian.png', 'Variants'), (os.path.join('Death', 'ember_rock_obsidian.png'), os.path.join('Variants', 'Death'))]
    for src, dst in jobs:
        im = Image.open(os.path.join(base, src))
        os.makedirs(os.path.join(base, dst), exist_ok=True)
        for name, spec in VARIANTS.items():
            out = remap(im, *spec)
            assert out.size == im.size
            assert (np.asarray(out)[..., 3] == np.asarray(im.convert('RGBA'))[..., 3]).all()
            out.save(os.path.join(base, dst, 'ember_rock_obsidian_%s.png' % name))

if __name__ == '__main__':
    main(sys.argv[1])
