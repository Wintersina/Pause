#!/usr/bin/env python3
"""Report the user-specified forbidden-hue shares over opaque strip pixels."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build import hsv_arrays  # noqa: E402

failed = False
for path in sorted((Path(__file__).resolve().parent / 'strips').glob('*.png')):
    a = np.asarray(Image.open(path).convert('RGBA'))
    hue, sat, val = hsv_arrays(a[:, :, :3])
    body = a[:, :, 3] > 128
    total = int(body.sum())
    magenta = int((body & (hue >= 305) & (hue <= 345) & (sat > .3)).sum())
    red = int((body & ((hue >= 345) | (hue <= 15)) & (sat > .5)).sum())
    amber = int((body & (hue >= 30) & (hue <= 48) & (sat > .45)).sum())
    pickup_green = int((body & (hue >= 78) & (hue <= 90) & (sat > .7)).sum())
    neon = int((body & (hue >= 64) & (hue <= 76) & (sat > .45) & (val > .7)).sum())
    okay = magenta == 0 and red == 0 and amber / total <= .01 and pickup_green / total <= .01 and neon / total <= .12
    failed |= not okay
    print(f'{path.name}: opaque={total}, magenta={magenta} ({magenta/total:.3%}), '
          f'red={red} ({red/total:.3%}), amber={amber} ({amber/total:.3%}), '
          f'pickup-green={pickup_green} ({pickup_green/total:.3%}), '
          f'bright-lime={neon} ({neon/total:.3%}) -> {"PASS" if okay else "FAIL"}')
sys.exit(1 if failed else 0)
