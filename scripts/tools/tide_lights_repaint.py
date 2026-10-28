#!/usr/bin/env python3
"""Repaint Tide's blinking lamps (lights.png: beacon_mint, beacon_pink, strobe_white) as plain
glow points: a small core and a stepped round halo, no lantern frame, cage, box or ring.
Cells (256x256), names and the centre anchor are unchanged; window_lights is left untouched.
Usage: tide_lights_repaint.py <Backdrop3 dir>"""
import json, sys, math
from PIL import Image

# name -> (core, glow, halo colours, halo radius in cell pixels)
LAMPS = {
    "beacon_mint":  ((226, 255, 246), (104, 232, 204), (38, 150, 138), 34),
    "beacon_pink":  ((255, 240, 204), (255, 190, 84), (196, 118, 30), 34),   # an amber navigation lamp (no pink outside Space)
    "strobe_white": ((244, 255, 255), (186, 232, 240), (84, 160, 184), 38),
}
FRAMES = [.22, .55, 1.0, .55]   # dark, rising, lit, fading (peak vs dimmest well over 4x)
# Real per-texel resolution (BackdropResolutionTest rejects block upscales): a smooth radial falloff plus a
# +-1 level hash dither, so neighbouring texels differ.

def mix(a, b, t): return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
def hsh(x, y): return ((x * 73856093) ^ (y * 19349663)) % 3 - 1

def lamp(name, k):
    core, glow, halo, R = LAMPS[name]
    img = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    px = img.load()
    for y in range(256):
        for x in range(256):
            r = math.hypot(x + .5 - 128, y + .5 - 128)
            if r > R: continue
            if r <= 3.5: col, a = core, 1.0
            elif r <= 9: t = (r - 3.5) / 5.5; col, a = mix(core, glow, t), 1.0 - .25 * t
            else:
                t = (r - 9) / (R - 9)
                a = .75 * (1 - t) ** 2.2
                col = mix(glow, halo, min(1, t * 1.4))
            a *= k
            if a <= .012: continue
            d = hsh(x, y) if a < .9 else 0
            px[x, y] = tuple(max(0, min(255, int(c) + d)) for c in col) + (max(1, min(255, int(255 * a) + d)),)
    return img

d = sys.argv[1]
atlas = Image.open(d + "/lights.png").convert("RGBA")
for s in json.load(open(d + "/lights.json"))["sprites"]:
    for name in LAMPS:
        if s["n"].startswith(name + "_"):
            k = FRAMES[int(s["n"][-2:])]
            top = 1024 - s["y"] - s["h"]            # json y counts from the bottom
            box = (s["x"], top, s["x"] + s["w"], top + s["h"])
            atlas.paste(Image.new("RGBA", (s["w"], s["h"]), (0, 0, 0, 0)), box)
            atlas.alpha_composite(lamp(name, k), (s["x"], top))
atlas.save(d + "/lights.png")
