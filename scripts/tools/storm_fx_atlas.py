#!/usr/bin/env python3
"""Storm boulder effects atlas (Art/Resources/Boulders/Storm/fx.png, 1024 x 1024, full resolution, pixel-hard dithered alpha, no blur).

Layout (image px, origin top-left; CoverStormFx slices it): wisps 4 x 512x256 at (0,0) (512,0) (0,256) (512,256); lightning arcs 3 x 256x256
at y=512 x=0/256/512; cloud shadow 256x128 at (768,512); rain 4 x 16x128 at y=768 x=0/32/64/96; hail 3 x 32x32 at (160,768) (208,768) (256,768).
Colours are the Storm backdrop's cloud graphite and the storm-white / pale-yellow cue.  Deterministic (seeded).  No pink."""
import numpy as np
from PIL import Image
rng = np.random.default_rng(20261014)
BAYER = np.array([[0, 32, 8, 40, 2, 34, 10, 42], [48, 16, 56, 24, 50, 18, 58, 26], [12, 44, 4, 36, 14, 46, 6, 38], [60, 28, 52, 20, 62, 30, 54, 22],
                  [3, 35, 11, 43, 1, 33, 9, 41], [51, 19, 59, 27, 49, 17, 57, 25], [15, 47, 7, 39, 13, 45, 5, 37], [63, 31, 55, 23, 61, 29, 53, 21]]) / 64.0
TONES = np.array([(30, 33, 42), (52, 57, 70), (84, 91, 106), (124, 132, 148)], np.uint8)   # the Storm cloud deck's graphite ramp
atlas = np.zeros((1024, 1024, 4), np.uint8)

def fbm(h, w, oct=6, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(oct):
        g = max(2, 2 ** (o + 2))
        small = r.random((g, int(g * w / h) + 2)).astype(np.float32)
        out += amp * np.array(Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
        tot += amp; amp *= .55
    return out / tot

def put(img, x, y):
    h, w = img.shape[:2]
    atlas[y:y + h, x:x + w] = img

def wisp(seed):
    h, w = 256, 512
    n = fbm(h, w, 6, seed)
    yy, xx = np.mgrid[0:h, 0:w]
    rad = ((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2
    cov = np.clip(n * 2.3 - .55 - rad * .95, 0, 1)                     # puffy, ragged, fading toward the rim
    gy, gx = np.gradient(n)
    light = np.clip(.5 - (gx + gy) * 14, 0, 1)                          # lit from the upper left like the rock
    tone = np.clip((light * 2.2 + n * 1.3 - .9 + (1 - cov) * -.6) * 1.4, 0, 3.999).astype(int)
    a = (cov * 1.15 > BAYER[yy % 8, xx % 8]).astype(np.uint8) * 255   # pixel-hard dithered edge
    a = np.where(cov > .42, 255, a).astype(np.uint8)
    rgb = TONES[tone]
    return np.dstack([rgb, a])

def bolt(seed):
    r = np.random.default_rng(seed)
    S = 256
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = im.load()
    def line(p, q, wd, col):
        n = int(max(abs(q[0] - p[0]), abs(q[1] - p[1])) * 1.5) + 1
        for i in range(n + 1):
            x = p[0] + (q[0] - p[0]) * i / n; y = p[1] + (q[1] - p[1]) * i / n
            for dx in range(-wd, wd + 1):
                for dy in range(-wd, wd + 1):
                    if dx * dx + dy * dy <= wd * wd + 1 and 0 <= int(x) + dx < S and 0 <= int(y) + dy < S:
                        px[int(x) + dx, int(y) + dy] = col
    pts = [(r.integers(90, 170), 8)]
    while pts[-1][1] < S - 14:
        pts.append((int(np.clip(pts[-1][0] + r.integers(-34, 35), 14, S - 14)), pts[-1][1] + int(r.integers(14, 30))))
    for wd, col in ((3, (150, 168, 200, 255)), (2, (222, 230, 246, 255)), (1, (255, 252, 214, 255))):   # outer storm-blue, white, pale-yellow core
        for a, b in zip(pts, pts[1:]): line(a, b, wd, col)
    for k in range(2):                                                   # two thin branches
        i = int(r.integers(1, len(pts) - 2)); p = pts[i]
        d = 1 if k == 0 else -1
        q = p
        for _ in range(3):
            nq = (int(np.clip(q[0] + d * r.integers(12, 26), 6, S - 6)), q[1] + int(r.integers(8, 18)))
            line(q, nq, 1, (222, 230, 246, 255)); q = nq
    return np.array(im)

# wisps
for i, (x, y) in enumerate(((0, 0), (512, 0), (0, 256), (512, 256))): put(wisp(11 + i * 7), x, y)
# arcs
for i in range(3): put(bolt(5 + i * 13), i * 256, 512)
# cloud shadow: a dark dithered ellipse
h, w = 128, 256
yy, xx = np.mgrid[0:h, 0:w]
rad = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2)
cov = np.clip(1.15 - rad * 1.2, 0, 1)
sh = np.zeros((h, w, 4), np.uint8); sh[..., :3] = (8, 10, 16); sh[..., 3] = ((cov > BAYER[yy % 8, xx % 8]) * 255).astype(np.uint8)
put(sh, 768, 512)
# rain streaks: a thin pale line, brighter toward the head, 1-2 px wide
for i in range(4):
    s = np.zeros((128, 16, 4), np.uint8)
    for yv in range(128):
        t = yv / 127
        for dx in range(2 if i % 2 else 1):
            s[yv, 6 + dx + (i // 2)] = (190, 200, 220, int(40 + 215 * t ** 1.5))
    put(s, i * 32, 768)
# hail: small pale pellets
for i in range(3):
    s = np.zeros((32, 32, 4), np.uint8)
    yy, xx = np.mgrid[0:32, 0:32]
    r_ = 6 + i * 1.5
    m = (xx - 15.5) ** 2 + (yy - 15.5) ** 2 <= r_ * r_
    lit = (xx + yy) < 31
    s[m] = (200, 210, 228, 255); s[m & ~lit] = (132, 142, 160, 255); s[m & (xx + yy < 24)] = (240, 245, 255, 255)
    put(s, 160 + 48 * i, 768)
Image.fromarray(atlas).save("Pause/Assets/Art/Resources/Boulders/Storm/fx.png")
Image.fromarray(atlas).save("Pause/Assets/Art/Boulders~/Storm/fx_atlas.png")
print("fx atlas written")
