#!/usr/bin/env python3
"""Keys Codex's magenta-background 3x3 sheets into transparent 9-frame strips.
usage: process.py [key ...]  ->  ../Resources/Fusion/<key>.png (209 px cells, one row).
Only keying / despill / slicing / 2x box downsample: every pixel of art is Codex's."""
import sys, os
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Resources", "Fusion")
# despill per effect: what the magenta fringe is replaced with (cool / green / warm / violet)
MODE = {"vacuum": "cool", "lifeline": "cool", "bloom": "green", "nova": "cool", "overclock": "amber", "stasis": "cool",
        "duststorm": "violet", "singularity": "bone", "featherstorm": "bone", "shellburst": "amber", "overheal": "green", "aftershock": "cool"}
def key(img, mode):
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    if mode == "violet":
        m = np.minimum(R, B) - G - np.maximum(0, np.abs(R - B) - 0)  # key is R~B high with G~0; violet has B > R
        m = np.minimum(R, B) - G
        m = m * (np.minimum(R, B) / np.maximum(1, np.maximum(R, B)))          # balanced R/B = magenta
        m = np.where((R > 200) & (B > 200) & (G < 90), 255 - G, m * .5)
    else:
        m = np.minimum(R, B) - G
    alpha = 1 - np.clip((m - 70) / 110, 0, 1)
    out = a.copy()
    if mode == "cool":
        out[..., 0] = np.minimum(R, G)
    elif mode == "green":
        out[..., 0] = np.minimum(R, G); out[..., 2] = np.minimum(B, G)
    elif mode == "bone":   # warm white: no pink spill
        out[..., 0] = np.minimum(R, G + 14); out[..., 2] = np.minimum(B, G + 10)
    elif mode == "amber":  # amber / gold: pull red down toward the green so orange-red cannot read pink or red
        out[..., 0] = np.minimum(R, G * 1.75 + 25); out[..., 2] = np.minimum(B, G * .6)
    elif mode == "warm":
        out[..., 2] = np.minimum(B, np.maximum(G, 40 + .6 * G) if False else G + 30)
        spill = np.clip(np.minimum(R, B) - G, 0, None)
        out[..., 0] = R - spill * .0
    else:  # violet: pull red below blue so the fringe reads violet not pink
        out[..., 0] = np.minimum(R, B * .72)
    out[..., 3:] = 0
    rgba = np.dstack([np.clip(out[..., :3], 0, 255), alpha * 255]).astype(np.uint8)
    rgba[alpha < .05] = 0
    return Image.fromarray(rgba, "RGBA")
def main(keys):
    os.makedirs(OUT, exist_ok=True)
    for k in keys:
        src = Image.open(os.path.join(HERE, "raw", k + ".png"))
        w, h = src.size
        cw, ch = w // 3, h // 3
        rgba = key(src, MODE[k])
        cell = 209
        strip = Image.new("RGBA", (cell * 9, cell))
        for i in range(9):
            c = rgba.crop(((i % 3) * cw, (i // 3) * ch, (i % 3 + 1) * cw, (i // 3 + 1) * ch)).resize((cell, cell), Image.BOX)
            strip.paste(c, (i * cell, 0))
        strip.save(os.path.join(OUT, k + ".png"), optimize=True)
        print(k, strip.size)
if __name__ == "__main__":
    main(sys.argv[1:])
