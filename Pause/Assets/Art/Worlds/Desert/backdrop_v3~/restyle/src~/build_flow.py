"""Desert painted-ground overlay ('flow'): a transparent 1024x2048 strip of wind-blown grit streaks and a few sun glints,
seamless on both axes (every mark wraps). One per variant.

Usage: python3 build_flow.py <variant: salt|dunes> <out flow.png>
"""
import sys
import numpy as np
from PIL import Image

W, H = 1024, 2048
KIND = sys.argv[1]
rng = np.random.default_rng(7 if KIND == "salt" else 11)
img = np.zeros((H, W, 4), np.float32)
PAL = {"salt": [(236, 228, 206), (214, 200, 170), (190, 226, 224)],
       "dunes": [(232, 206, 150), (214, 178, 112), (240, 226, 190)]}[KIND]
LEAN = .22          # wind-blown grit drifts sideways (dy per dx), not like rain


def put(x, y, col, a):
    x %= W; y %= H
    if a > img[y, x, 3]:
        img[y, x] = (*col, a)


for _ in range(90 if KIND == "salt" else 110):
    x0, y0 = int(rng.integers(0, W)), int(rng.integers(0, H))
    ln = int(rng.integers(24, 90))
    col = PAL[int(rng.integers(0, 3)) if rng.random() < .25 else int(rng.integers(0, 2))]
    amax = float(rng.uniform(45, 100))
    thick = 2 if rng.random() < .8 else 3
    for i in range(ln):
        a = amax * min(1, i / 10, (ln - i) / 14)          # fade in / out along the streak
        for t in range(thick):
            put(x0 + i, y0 + int(i * LEAN) + t, col, a)
for _ in range(330 if KIND == "salt" else 160):       # glints (salt crystals / sun on sand)
    x, y = int(rng.integers(0, W)), int(rng.integers(0, H))
    col = (255, 250, 232)
    a = float(rng.uniform(110, 210))
    for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        put(x + dx, y + dy, col, a)
    if rng.random() < .3:
        for d in (-2, -1, 2, 3):
            put(x + d, y, col, a * .45); put(x, y + d, col, a * .45)
out = img.copy()
out[..., :3] = np.where(img[..., 3:4] > 0, img[..., :3], 0)
Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA").save(sys.argv[2])
