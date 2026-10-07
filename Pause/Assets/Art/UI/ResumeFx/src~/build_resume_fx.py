#!/usr/bin/env python3
"""Sprites for the resume slow-mo indicator (ResumeFx / ResumeFxView).

  python3 build_resume_fx.py        (needs Pillow)

Outputs into Art/Resources/ResumeFx (imported point-filtered, uncompressed,
ppu 100 by Editor/Importers/ResumeFxArtImporter.cs):

  resume_vignette.png  90x200 full-screen frame, stretched to the camera view.
      Transparent middle; a stepped, Bayer-dithered MAGENTA rim down the
      left/right edges with CYAN pixels in its gaps (a chromatic "time is
      thick" edge). Sides only, easing off towards the top (the HUD's) and
      the bottom (where the thumb rests). The outer ~6% sits under the rails.
  resume_streak.png    4x40 speed streak, pivot at the bottom (its head).
      BONE head, CYAN core fading up the tail in hard steps, a MAGENTA fringe
      on the left and a TEAL one on the right. Stretched vertically from a
      short dash (slow) to a long line (back up to speed).

Neon pixel art: hard alpha steps + ordered dither, no smooth gradients.
"""
import math
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(ASSETS, "Art", "Resources", "ResumeFx")

MAGENTA = (0xFF, 0x2E, 0x88)
MAGENTA_SH = (0x86, 0x12, 0x5A)
CYAN = (0x6E, 0xF2, 0xEE)
TEAL = (0x1F, 0xB5, 0xB9)
BONE = (0xF4, 0xEA, 0xD4)

BAYER4 = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def dither(level, x, y):
    """True when an ordered-dither cell at (x, y) is lit for 0..1 level."""
    return (BAYER4[y % 4][x % 4] + 0.5) / 16.0 < level


def vignette():
    w, h = 90, 200
    side = 18.0                   # rim depth in texels on the left/right
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    for y in range(h):
        # strongest mid-height, easing off towards the HUD and the thumb
        vert = 0.55 + 0.45 * math.sin(math.pi * (y + 0.5) / h)
        for x in range(w):
            cx = x + 0.5
            # depth into the rim: 0 at its inner edge, 1 at the screen edge
            t = max(0.0, side - min(cx, w - cx)) / side
            if t <= 0.0:
                continue
            if t < 0.35:
                if dither(0.18 * vert, x, y):
                    px[x, y] = MAGENTA + (110,)
            elif t < 0.6:
                if dither(0.45 * vert, x, y):
                    px[x, y] = MAGENTA + (130,)
            elif t < 0.85:
                if dither(0.8 * vert, x, y):
                    px[x, y] = MAGENTA + (155,)
                elif dither(0.5, y, x):
                    px[x, y] = CYAN + (90,)
            else:
                px[x, y] = MAGENTA_SH + (185,)
    return img


def streak():
    w, h = 4, 40
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    head = 4
    for y in range(h):
        # y = 0 is the top (tail end), y = h-1 the bottom (head)
        from_head = h - 1 - y
        if from_head < head:
            px[1, y] = BONE + (255,)
            px[2, y] = BONE + (255,)
            px[0, y] = MAGENTA + (200,)
            px[3, y] = TEAL + (200,)
            continue
        k = 1.0 - (from_head - head) / float(h - head)   # 1 near head .. 0 at tail
        step = 1.0 if k > 0.75 else 0.75 if k > 0.5 else 0.5 if k > 0.25 else 0.25
        a = int(235 * step)
        for x in (1, 2):
            if step >= 0.5 or dither(step * 2, x, y):
                px[x, y] = CYAN + (a,)
        if k > 0.5:
            px[0, y] = MAGENTA + (int(a * 0.6),)
            px[3, y] = TEAL + (int(a * 0.6),)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    vignette().save(os.path.join(OUT, "resume_vignette.png"))
    streak().save(os.path.join(OUT, "resume_streak.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
