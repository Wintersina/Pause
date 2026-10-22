#!/usr/bin/env python3
"""Draws Turtle_rotor.png: a 256x128 sheet, left cell = 3-blade rotor (slate blades, cream tips,
amber hub dot), right cell = faint motion-blur disc (alpha ~0.25). One sheet pixel = one hull-cell
pixel at scale 1.0 (rotor radius 48 px). Crisp pixels, no outline, no anti-aliasing.
Usage: python3 make_rotor.py   (writes ../Turtle_rotor.png)"""
import math, os
from PIL import Image

CELL, R = 128, 48
SLATE, SLATE_HI, CREAM, AMBER = (92, 106, 126, 255), (140, 156, 178, 255), (240, 228, 188, 255), (242, 169, 59, 255)
DISC = (214, 222, 232, 64)


def blade(img, cx, cy, ang):
    ca, sa = math.cos(ang), math.sin(ang)
    for py in range(CELL):
        for px in range(CELL):
            dx, dy = px + .5 - cx, py + .5 - cy
            u, v = dx * ca + dy * sa, -dx * sa + dy * ca   # u along the blade
            if u < 4.0 or u > R:
                continue
            half = 8.0 - 3.0 * (u / R)                       # tapers 8 -> 5
            if abs(v) > half:
                continue
            col = CREAM if u > R - 14 else (SLATE_HI if v < -half + 2.0 else SLATE)
            img.putpixel((px, py), col)


def main():
    img = Image.new("RGBA", (CELL * 2, CELL), (0, 0, 0, 0))
    c = CELL / 2
    for k in range(3):
        blade(img, c, c, math.radians(90 + 120 * k))
    for py in range(CELL):                                  # hub: amber dot
        for px in range(CELL):
            if (px + .5 - c) ** 2 + (py + .5 - c) ** 2 <= 6.5 ** 2:
                img.putpixel((px, py), AMBER)
    for py in range(CELL):                                  # blur disc, static
        for px in range(CELL):
            if (px + .5 - c) ** 2 + (py + .5 - c) ** 2 <= (R - .5) ** 2:
                img.putpixel((CELL + px, py), DISC)
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Turtle_rotor.png")
    img.save(out)
    print("wrote", os.path.normpath(out))


if __name__ == "__main__":
    main()
