"""Storm 'eye of the storm' set-piece atlas: eye.png (1024x1024 RGBA, one cell 'storm_eye') + eye.json.

The painting (eye_a.png, image-generated 1254 px) is cropped 1:1 (no resampling) to its centre 1024x1024 and its
outer arms are feathered into transparency on a circle with a pixel-stepped (ordered-dither, 6 levels) alpha ramp,
so it lies on the cloud sea without an edge, outline or box.

Usage: python3 build_eye.py <eye_a.png> <out dir>
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

src, out = Path(sys.argv[1]), Path(sys.argv[2])
a = np.asarray(Image.open(src).convert("RGB"))
h, w = a.shape[:2]
y0, x0 = (h - 1024) // 2, (w - 1024) // 2
rgb = a[y0:y0 + 1024, x0:x0 + 1024]
yy, xx = np.mgrid[0:1024, 0:1024].astype(np.float32)
r = np.hypot(xx - 511.5, yy - 511.5)
R0, R1 = 330.0, 505.0                       # opaque inside R0, gone at R1
ramp = np.clip((R1 - r) / (R1 - R0), 0, 1)
ramp = ramp * ramp * (3 - 2 * ramp)
levels = 6
bayer = np.array([[0, 32, 8, 40, 2, 34, 10, 42], [48, 16, 56, 24, 50, 18, 58, 26], [12, 44, 4, 36, 14, 46, 6, 38],
                  [60, 28, 52, 20, 62, 30, 54, 22], [3, 35, 11, 43, 1, 33, 9, 41], [51, 19, 59, 27, 49, 17, 57, 25],
                  [15, 47, 7, 39, 13, 45, 5, 37], [63, 31, 55, 23, 61, 29, 53, 21]], np.float32) / 64.0
d = np.tile(bayer, (128, 128))
q = np.floor(ramp * levels + d * .999) / levels            # dithered, pixel-stepped alpha
alpha = np.clip(np.rint(q * 255), 0, 255).astype(np.uint8)
alpha[r >= R1] = 0
alpha[ramp >= .999] = 255
rgba = np.dstack([rgb, alpha])
rgba[alpha == 0, :3] = 0
out.mkdir(parents=True, exist_ok=True)
Image.fromarray(rgba, "RGBA").save(out / "eye.png")
(out / "eye.json").write_text(json.dumps({"sprites": [{"n": "storm_eye", "x": 0, "y": 0, "w": 1024, "h": 1024}]}, indent=2) + "\n")
print("eye atlas built")
