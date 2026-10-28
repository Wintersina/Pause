#!/usr/bin/env python3
"""Repaint Tide's blinking lamps (lights.png: beacon_mint, beacon_pink, strobe_white) as plain
glow points: a small core and a stepped round halo, no lantern frame, cage, box or ring.
Cells (256x256), names and the centre anchor are unchanged; window_lights is left untouched.
Usage: tide_lights_repaint.py <Backdrop3 dir>"""
import json, sys, math
from PIL import Image

B = 4                       # pixel-art block, in cell pixels
# name -> (core, glow, halo colours, halo radius in blocks)
LAMPS = {
    "beacon_mint":  ((226, 255, 246), (104, 232, 204), (38, 150, 138), 8),
    "beacon_pink":  ((255, 240, 204), (255, 190, 84), (196, 118, 30), 8),   # an amber navigation lamp (no pink outside Space)
    "strobe_white": ((244, 255, 255), (186, 232, 240), (84, 160, 184), 9),
}
FRAMES = [.14, .5, 1.0, .5]     # dark, rising, lit, fading (peak vs dimmest well over 4x)

def lamp(name, k):
    core, glow, halo, R = LAMPS[name]
    n = 256 // B
    img = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    px = img.load()
    c = n / 2 - .5
    for gy in range(n):
        for gx in range(n):
            r = math.hypot(gx - c, gy - c)
            if r > R: continue
            if r <= 1.1: col, a = core, 1.0
            elif r <= 2.6: col, a = glow, .8
            else:
                t = 1 - (r - 2.6) / (R - 2.6)
                a = round(t * t * 5) / 5 * .55          # stepped, radially monotone
                col = halo if t < .55 else glow
                if a <= 0: continue
            if a * k <= .02: continue
            for y in range(B):
                for x in range(B):
                    px[gx * B + x, gy * B + y] = col + (int(255 * min(1, a * k)),)
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
