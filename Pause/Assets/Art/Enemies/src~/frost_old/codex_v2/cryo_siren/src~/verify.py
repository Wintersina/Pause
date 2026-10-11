"""Validate and report the Cryo Siren seven-cell art contract."""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
PATH = ROOT / "frost_elite_cryo_siren.png"
SIDE = 192
NAMES = ("landed", "grounded", "ignition", "hover", "bank L", "bank R", "damaged")


def measure() -> None:
    image = Image.open(PATH)
    rgba = image.convert("RGBA")
    pixels = np.asarray(rgba)
    print(f"file: {PATH.name} | size: {image.width}x{image.height} | mode: {image.mode}")
    print("cell  state       margins L/T/R/B   bbox x0,y0,x1,y1  colours  red%   pink%  centroid x,y")
    measurements = []
    for i, name in enumerate(NAMES):
        a = pixels[:, i * SIDE:(i + 1) * SIDE]
        alpha = a[:, :, 3]
        ys, xs = np.nonzero(alpha)
        if not len(xs):
            raise AssertionError(f"cell {i} is empty")
        box = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
        margins = (box[0], box[1], SIDE - box[2], SIDE - box[3])
        rgb = a[alpha > 0, :3]
        colours = len(np.unique(rgb, axis=0))
        hsv = np.array([colorsys.rgb_to_hsv(*(p / 255)) for p in rgb])
        h, s = hsv[:, 0] * 360, hsv[:, 1]
        red = (((h <= 15) | (h >= 345)) & (s >= .5)).mean() * 100
        pink = (((h >= 290) & (h < 345)) & (s >= .35)).mean() * 100
        center = (float(xs.mean()), float(ys.mean()))
        measurements.append((margins, box, colours, red, pink, center))
        print(f"{i:>2}    {name:<9}   {margins!s:<17} {box!s:<19} {colours:>5}  "
              f"{red:5.3f}  {pink:6.3f}  {center[0]:6.2f},{center[1]:6.2f}")

    ref = np.array(measurements[3][5])
    for i in (4, 5):
        delta = np.array(measurements[i][5]) - ref
        print(f"hover -> bank {i}: dx={delta[0]:+.2f} px, dy={delta[1]:+.2f} px, "
              f"distance={np.linalg.norm(delta):.2f} px")

    hover = pixels[:, 3 * SIDE:4 * SIDE]
    hull_ys, hull_xs = np.nonzero((hover[:, :, 3] > 0) & (np.indices((SIDE, SIDE))[0] <= 164))
    hull_box = (int(hull_xs.min()), int(hull_ys.min()),
                int(hull_xs.max() + 1), int(hull_ys.max() + 1))
    print(f"cell 3 hull bbox (jets excluded): {hull_box}, "
          f"size={hull_box[2] - hull_box[0]}x{hull_box[3] - hull_box[1]} px")
    print("cell 3 anchors (x,y | RGBA):")
    for name, (x, y) in (("forward cryo-orb muzzle", (96, 62)),
                         ("left nozzle", (90, 155)),
                         ("right nozzle", (102, 155)),
                         ("dish core", (96, 79))):
        print(f"  {name:<24} ({x:>3},{y:>3}) | {tuple(int(v) for v in hover[y, x])}")

    assert image.size == (1344, 192) and image.mode == "RGBA"
    assert all(min(m[0]) >= 16 for m in measurements), "margin under 16 px"
    assert all(m[2] >= 1000 for m in measurements), "too few colours"
    assert all(m[3] < .01 and m[4] < .01 for m in measurements), "red or pink pixels"
    assert all(np.linalg.norm(np.array(measurements[i][5]) - ref) <= 4 for i in (4, 5))
    assert all(hover[y, x, 3] == 255 for x, y in ((96, 62), (90, 155), (102, 155)))
    print("PASS: contract, anchors, margins, registration, colour density, hue exclusions")


if __name__ == "__main__":
    measure()
