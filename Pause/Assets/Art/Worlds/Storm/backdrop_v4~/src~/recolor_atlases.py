"""Storm backdrop v4: recolour the gray v3 piece atlases into the colourful storm palette.

The geometry, cells, alpha and the JSON files stay byte-identical (drop-in), only RGB changes:
luminance -> a hue/saturation ramp (indigo shadows, electric-blue and teal mid tones, amber sun-lit
rims, white-hot lightning). No blur, no resampling, one source pixel -> one output pixel.

Usage: python3 recolor_atlases.py <src Backdrop3 dir> <out dir>
"""
from __future__ import annotations

import colorsys
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def hsv_to_rgb(h, s, v):
    """Vectorised HSV (h in degrees) -> RGB float 0..1."""
    h = (h % 360.0) / 60.0
    i = np.floor(h).astype(np.int32) % 6
    f = h - np.floor(h)
    p = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))
    r = np.choose(i, [v, q, p, p, t, v])
    g = np.choose(i, [t, v, v, q, p, p])
    b = np.choose(i, [p, p, t, v, v, q])
    return np.stack((r, g, b), axis=-1)


def lerp(x, xs, ys):
    return np.interp(x, xs, ys)


def shifted(a, dx, dy):
    out = np.roll(a, (dy, dx), axis=(0, 1))
    return out


def recolor(rgba: np.ndarray, kind: str, cell: int = 256) -> np.ndarray:
    a = rgba[:, :, 3]
    rgb = rgba[:, :, :3].astype(np.float32) / 255.0
    mx = rgb.max(2)
    mn = rgb.min(2)
    lum = rgb @ np.array([.299, .587, .114], dtype=np.float32)
    v_src = np.maximum(mx, lum)
    H, W = a.shape

    # local lighting: rims facing the low sun (right / below) are warm, the rest cool
    blur = (shifted(lum, 2, 0) + shifted(lum, -2, 0) + shifted(lum, 0, 2) + shifted(lum, 0, -2) + lum) / 5.0
    detail = lum - blur
    # up-left light from lightning, sun from lower right: pixels brighter than their upper-left neighbour face the sun
    facing = lum - shifted(lum, 2, 2)
    warm = np.clip(facing * 5.0 + detail * 2.0, 0, 1) * np.clip((lum - .18) / .3, 0, 1)

    if kind == "landmarks":
        hue_pts = ([0, .12, .3, .55, .8, 1.0], [256, 244, 228, 208, 206, 214])
        sat_pts = ([0, .12, .3, .55, .85, 1.0], [.70, .70, .62, .50, .30, .08])
        val_gain = 1.08
        warm_amount = .85
    elif kind == "sites":
        hue_pts = ([0, .12, .3, .55, .8, 1.0], [258, 248, 234, 216, 208, 212])
        sat_pts = ([0, .12, .3, .55, .85, 1.0], [.75, .72, .62, .55, .38, .12])
        val_gain = 1.1
        warm_amount = .55
    elif kind == "weather":
        hue_pts = ([0, .15, .35, .6, .85, 1.0], [266, 254, 240, 224, 212, 212])
        sat_pts = ([0, .15, .35, .6, .85, 1.0], [.66, .66, .58, .45, .28, .12])
        val_gain = 1.3
        warm_amount = .6
    elif kind == "lightning":
        hue_pts = ([0, .2, .45, .7, .9, 1.0], [252, 240, 226, 216, 212, 210])
        sat_pts = ([0, .2, .45, .7, .9, 1.0], [.70, .64, .55, .45, .25, .05])
        val_gain = 1.0
        warm_amount = .35
    else:
        raise ValueError(kind)

    h = lerp(v_src, *hue_pts)
    s = lerp(v_src, *sat_pts)
    # deep-shadow plum-teal variety: a slow spatial hue wobble keeps flat areas from reading as one tone
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    h = h + 10.0 * np.sin(xx / 37.0 + yy / 53.0) * (1 - v_src)
    v = np.clip(v_src * val_gain, 0, 1)

    # warm sun rims: amber towards the bright edges
    wh = 38.0 + 8.0 * np.sin(xx / 29.0)
    amt = warm * warm_amount * np.clip((v_src - .25) / .35, 0, 1) * (1 - np.clip((v_src - .72) / .2, 0, 1))
    base_rgb = hsv_to_rgb(h, np.clip(s, 0, 1), v)
    warm_rgb = hsv_to_rgb(wh, np.full_like(wh, .78), np.minimum(1.0, v * 1.25 + .05))
    base_rgb = base_rgb * (1 - amt[..., None]) + warm_rgb * amt[..., None]

    # small isolated bright specks on structures = lit windows / work lamps: amber, a few cyan
    if kind in ("landmarks", "sites"):
        speck = (lum > .55) & (blur < lum - .12) & (a > 200)
        sel = (np.sin(xx * 12.9898 + yy * 78.233) * 43758.5453) % 1.0
        amber = speck & (sel < .6)
        cyan = speck & (sel >= .6)
        am = hsv_to_rgb(np.full_like(h, 40.0), np.full_like(h, .72), np.full_like(h, .97))
        cy = hsv_to_rgb(np.full_like(h, 192.0), np.full_like(h, .5), np.full_like(h, .97))
        base_rgb = np.where(amber[..., None], am, base_rgb)
        base_rgb = np.where(cyan[..., None], cy, base_rgb)

    out = base_rgb
    res = rgba.copy()
    res[:, :, :3] = np.clip(np.rint(out * 255), 0, 255).astype(np.uint8)
    # fully transparent texels keep their original (invisible) colour channels
    res[a == 0, :3] = rgba[a == 0, :3]
    return res


def main() -> None:
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    dst.mkdir(parents=True, exist_ok=True)
    for name, kind in (("landmarks", "landmarks"), ("sites", "sites"), ("weather", "weather"),
                       ("lightning", "lightning"), ("wx_ambient", "weather")):
        im = Image.open(src / f"{name}.png").convert("RGBA")
        out = recolor(np.asarray(im), kind)
        Image.fromarray(out, "RGBA").save(dst / f"{name}.png")
        print("recoloured", name, im.size)


if __name__ == "__main__":
    main()
