#!/usr/bin/env python3
"""Check Verdant strip colour exclusions over opaque body pixels."""
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
FAILED = False


def check(path):
    global FAILED
    im = Image.open(path).convert('RGBA')
    rgba = np.asarray(im)
    hsv = np.asarray(im.convert('RGB').convert('HSV')).astype(float)
    opaque = rgba[..., 3] > 128
    n = int(opaque.sum())
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255

    def count(mask):
        return int(np.count_nonzero(mask & opaque))

    red = count(((h < 16) | (h >= 345)) & (s > .3))
    pink = count((h >= 305) & (h < 345) & (s > .3))
    violet = count((h >= 250) & (h < 305) & (s > .3))
    teal = count((h >= 160) & (h < 250) & (s > .45))
    amber = count((h >= 30) & (h <= 48) & (s > .45))
    leaf = count((((h >= 64) & (h <= 76)) | ((h >= 98) & (h <= 125))) & (s > .45) & (v > .45))
    bad = red or pink or violet or teal or amber > n*.01 or leaf > n*.12
    FAILED |= bool(bad)
    print(f'{path.name}: opaque={n}, red={red}, pink={pink}, violet={violet}, '
          f'teal={teal}, amber={100*amber/n:.3f}%, bright leaf={100*leaf/n:.3f}% '
          f'{"FAIL" if bad else "PASS"}')


for name in ('verdant_fighter_2', 'verdant_fighter_3'):
    check(HERE / 'strips' / f'{name}.png')
raise SystemExit(1 if FAILED else 0)
