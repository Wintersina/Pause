"""Build Desert ground tiles from the selected image-generated paintings.

Only nearest-neighbour resizing is used.  The short edge cross-fade makes the
paintings periodic before the material ramps are applied; it is a colour
transition between corresponding edge pixels, not a spatial blur.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
SOURCE = {1: "candidate_1.png", 2: "candidate_2_alt.png", 3: "candidate_3.png", 4: "candidate_4.png"}
ROLES = ("sky", "far", "mid")
TARGET = {
    1: {"sky": .400, "far": .445, "mid": .490},
    2: {"sky": .400, "far": .445, "mid": .490},
    3: {"sky": .400, "far": .445, "mid": .490},
    4: {"sky": .320, "far": .365, "mid": .410},
}


def periodic(a: np.ndarray, margin_x: int = 22, margin_y: int = 28) -> np.ndarray:
    """Match corresponding left/right and top/bottom samples exactly."""
    a = a.astype(np.float32).copy()
    h, w = a.shape[:2]
    for k in range(margin_x):
        t = k / (margin_x - 1)
        l, r = a[:, k].copy(), a[:, w - 1 - k].copy()
        mean = (l + r) * .5
        a[:, k] = mean * (1 - t) + l * t
        a[:, w - 1 - k] = mean * (1 - t) + r * t
    for k in range(margin_y):
        t = k / (margin_y - 1)
        l, r = a[k].copy(), a[h - 1 - k].copy()
        mean = (l + r) * .5
        a[k] = mean * (1 - t) + l * t
        a[h - 1 - k] = mean * (1 - t) + r * t
    a[:, -1] = a[:, 0]
    a[-1] = a[0]
    return a


def rgb_to_hsv(rgb: np.ndarray):
    rgb = rgb.astype(np.float32) / 255
    hi = rgb.max(2)
    lo = rgb.min(2)
    d = hi - lo
    s = np.divide(d, hi, out=np.zeros_like(d), where=hi > 0)
    h = np.zeros_like(hi)
    mask = d > 1e-6
    r, g, b = rgb.transpose(2, 0, 1)
    rm = mask & (hi == r)
    gm = mask & (hi == g) & ~rm
    bm = mask & ~rm & ~gm
    h[rm] = ((g[rm] - b[rm]) / d[rm]) % 6
    h[gm] = (b[gm] - r[gm]) / d[gm] + 2
    h[bm] = (r[bm] - g[bm]) / d[bm] + 4
    return h * 60, s, hi


def hsv_to_rgb(h: np.ndarray, s: np.ndarray, v: np.ndarray) -> np.ndarray:
    hh = h / 60
    sector = np.floor(hh).astype(np.int16) % 6
    f = hh - np.floor(hh)
    p = v * (1 - s)
    q = v * (1 - f * s)
    t = v * (1 - (1 - f) * s)
    out = np.zeros((*h.shape, 3), np.float32)
    for i, channels in enumerate(((v, t, p), (q, v, p), (p, v, t),
                                  (p, q, v), (t, p, v), (v, p, q))):
        m = sector == i
        out[m] = np.stack([channel[m] for channel in channels], axis=1)
    return np.uint8(np.clip(np.rint(out * 255), 0, 255))


def load_source(variant: int) -> np.ndarray:
    im = Image.open(SRC / SOURCE[variant]).convert("RGB")
    w, h = im.size
    crop_w = h // 2
    im = im.crop(((w - crop_w) // 2, 0, (w + crop_w) // 2, h))
    im = im.resize((256, 512), Image.Resampling.NEAREST)
    return periodic(np.asarray(im))


def material_masks(variant: int, sh: np.ndarray, ss: np.ndarray, lum: np.ndarray):
    shadow = lum <= np.percentile(lum, 20 if variant in (1, 4) else 15)
    copper = np.zeros_like(shadow)
    glass = np.zeros_like(shadow)
    patina = np.zeros_like(shadow)
    if variant == 2:
        # Only the most readable oxidised cliff/metal highlights carry copper hue.
        score = np.where(((sh < 37) | (sh > 350)) & (ss > .21), ss * (.4 + lum), 0)
        copper = score > np.quantile(score, .89)
    if variant == 3:
        # The source's glass scars are nearly neutral, so take a few of their
        # hard crack edges rather than requiring an already green source hue.
        edge = np.maximum(np.abs(lum - np.roll(lum, 1, 0)),
                          np.abs(lum - np.roll(lum, 1, 1)))
        score = edge * (1 - .4 * lum)
        glass = score > np.quantile(score, .982)
        y, x = np.indices(glass.shape)
        patina = (score > np.quantile(score, .994)) & (((x * 3 + y) % 4) == 0)
        glass &= ~patina
    return shadow, copper, glass, patina


def shade(source: np.ndarray, variant: int, role: str) -> np.ndarray:
    sh, ss, lum = rgb_to_hsv(np.uint8(source))
    shadow, copper, glass, patina = material_masks(variant, sh, ss, lum)
    # Spatial variation inherited from the painted source; six broad material
    # tone bands remain visible, with a little within-band texture retained.
    lo, hi = np.percentile(lum, [2, 98])
    q = np.clip((lum - lo) / max(hi - lo, .01), 0, 1)
    q = q ** .88
    bands = np.interp(q, [0, .16, .34, .55, .74, .90, 1],
                      [.12, .21, .29, .37, .44, .51, .55])
    v = .76 * bands + .24 * (.12 + .43 * q)
    target = TARGET[variant][role]
    v *= target / np.percentile(v, 90)
    v = np.clip(v, .095, .60)
    # Roles shift their compositions, so their parallax remains legible.
    # Their material and surface structures all come from the same painting.
    h = 43.5 + np.clip((sh - 43) * .025, -1.2, 1.2)
    s = np.clip(.255 + .06 * (ss - .30), .20, .312)
    if variant == 4:
        h[:] = 226 + np.clip((sh - 230) * .015, -2, 2)
        s[:] = .20
    h[shadow] = 238
    s[shadow] = .36
    v[shadow] = np.minimum(v[shadow], .29 if variant != 4 else .255)
    h[copper] = 25
    s[copper] = .49
    v[copper] = np.minimum(v[copper], .56)
    h[glass] = 166
    s[glass] = .17
    h[patina] = 147
    s[patina] = .25
    # Thin crest lights / salt glints only, sampled from the painting's peaks.
    crest = (q > .992) & ~shadow & ~copper & ~glass & ~patina
    if variant in (1, 4):
        crest &= (np.maximum.reduce([np.abs(lum - np.roll(lum, 1, 0)),
                                      np.abs(lum - np.roll(lum, 1, 1))]) > .055)
    h[crest] = 44
    s[crest] = .17
    v[crest] = .66 if variant != 4 else .52
    out = hsv_to_rgb(h, s, v)
    # Round-off after the HSV conversion cannot change the already matched
    # periodic edge samples, but set them explicitly as a final exact guard.
    out[:, -1] = out[:, 0]
    out[-1] = out[0]
    return out


def flow_from(source: np.ndarray, variant: int) -> np.ndarray:
    _, _, lum = rgb_to_hsv(np.uint8(source))
    gx = np.abs(lum - np.roll(lum, 1, 1))
    gy = np.abs(lum - np.roll(lum, 1, 0))
    strength = np.maximum(gx, gy)
    threshold = np.quantile(strength, .946 if variant != 3 else .955)
    mask = strength > threshold
    y, x = np.indices(mask.shape)
    # Broken, delicate streaks: the exact edge shape comes from painted
    # dune ripples / canyon floor / salt cracks, with sparse gaps for motion.
    keep = ((x * 17 + y * 31 + x * y * 3 + variant * 19) % 11) < 9
    mask &= keep
    mask[:2] = False; mask[-2:] = False; mask[:, :2] = False; mask[:, -2:] = False
    rgba = np.zeros((*mask.shape, 4), np.uint8)
    hue = 44 if variant != 4 else 45
    sat = .19 if variant != 3 else .15
    val = .55 if variant != 4 else .47
    base = hsv_to_rgb(np.full(mask.shape, hue, np.float32),
                      np.full(mask.shape, sat, np.float32),
                      np.full(mask.shape, val, np.float32))
    rgba[:, :, :3][mask] = base[mask]
    rgba[:, :, 3][mask] = np.uint8(34 + 42 * np.clip(strength[mask] / .3, 0, 1))
    if variant == 2:
        # Three broken ribbons hug the generated canyon's winding floor.
        for y in range(8, 504):
            if y % 49 >= 32: continue
            centre = 130 + 25 * math.sin(y / 70) + 13 * math.sin(y / 31)
            for offset in (-5, 0, 5):
                x = int(centre + offset)
                rgba[y, x, :3] = (119, 113, 98)
                rgba[y, x, 3] = 31
    if variant == 4:
        # Cold wind veils remain thinner than the dune crest traces.
        for start, xpos in ((54, 35), (236, 161), (394, 48)):
            for step in range(91):
                y = start + step
                if step % 29 >= 22: continue
                x = int(xpos + step * .63 + 3 * math.sin(step / 11))
                if 2 < x < 253 and 2 < y < 509:
                    rgba[y, x, :3] = (112, 110, 108)
                    rgba[y, x, 3] = 28
    return rgba


def upscale(a: np.ndarray) -> Image.Image:
    mode = "RGBA" if a.shape[2] == 4 else "RGB"
    im = Image.fromarray(a, mode)
    return im.resize((512, 1024), Image.Resampling.NEAREST).convert("RGBA")


def main():
    for variant in range(1, 5):
        base = load_source(variant)
        out_dir = ROOT / f"v{variant}"
        out_dir.mkdir(exist_ok=True)
        for role, shift in zip(ROLES, ((36, 71), (13, 29), (0, 0))):
            shifted = np.roll(base, shift, axis=(0, 1))
            # Rolling a periodic source gives a periodic source, except at
            # the shifted cut; repeat the edge match after each offset.
            shifted = periodic(shifted, 12, 15)
            image = upscale(shade(shifted, variant, role))
            if variant == 4 and role == "mid":
                # Dim 1px sun-white work lamps on scattered buried ruins.
                pix = image.load()
                for x, y in ((68, 102), (392, 238), (110, 580), (414, 700), (84, 878)):
                    pix[x, y] = (143, 136, 115, 255)
                    pix[x - 1, y] = (92, 89, 80, 255)
            image.save(out_dir / f"{role}.png", optimize=True)
        flow = upscale(flow_from(base, variant))
        flow.save(out_dir / "flow.png", optimize=True)
    print("Built 16 tiles from image-generated source paintings.")


if __name__ == "__main__":
    main()
