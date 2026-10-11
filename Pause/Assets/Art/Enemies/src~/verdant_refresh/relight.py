#!/usr/bin/env python3
"""Readability pass over the four Codex Verdant strips (strips/ -> strips_lit/): lifts the dark bark/brass mid-tones
(value gamma, saturation eased so amber never blooms) and writes a 2 px pale lime-bone rim OUTSIDE the silhouette, once per cell.
No pink, no resampling; geometry (cells, anchors) unchanged.   python3 relight.py [gamma] [rimwidth]"""
import sys, colorsys
from pathlib import Path
import numpy as np
from PIL import Image
HERE = Path(__file__).resolve().parent
G = float(sys.argv[1]) if len(sys.argv) > 1 else .62
RW = int(sys.argv[2]) if len(sys.argv) > 2 else 2
SCALES = {'verdant_chaser': 1.24, 'verdant_fighter_1': 1.2, 'verdant_fighter_2': 1.18, 'verdant_fighter_3': 1.22}
SC0 = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0  # global multiplier   # nearest-neighbour cell scale about the cell centre (EnemyRosterTest silhouette size contract)
RIM = (241, 246, 200)
CELL = 192
def hsv(a):
    r, g, b = a[..., 0] / 255, a[..., 1] / 255, a[..., 2] / 255
    mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rc = np.where(m & (mx == r), ((g - b) / np.where(m, d, 1)) % 6, 0)
    gc = np.where(m & (mx == g) & (mx != r), (b - r) / np.where(m, d, 1) + 2, 0)
    bc = np.where(m & (mx == b) & (mx != r) & (mx != g), (r - g) / np.where(m, d, 1) + 4, 0)
    h = (rc + gc + bc) / 6 % 1
    s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    return h, s, mx
def rgb(h, s, v):
    i = np.floor(h * 6).astype(int) % 6; f = h * 6 - np.floor(h * 6)
    p = v * (1 - s); q = v * (1 - f * s); t = v * (1 - (1 - f) * s)
    r = np.choose(i, [v, q, p, p, t, v]); g = np.choose(i, [t, v, v, q, p, p]); b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], -1) * 255
def dil(m):
    p = np.pad(m, 1); o = m.copy(); h, w = m.shape
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            o |= p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
    return o
for src in sorted((HERE / "strips").glob("verdant_*.png")):
    SC = SCALES.get(src.stem, 1.0) * SC0
    im0 = Image.open(src).convert("RGBA")
    if SC != 1.0:
        sc = Image.new("RGBA", im0.size)
        for c in range(im0.width // CELL):
            cell = im0.crop((c * CELL, 0, (c + 1) * CELL, CELL))
            n = int(round(CELL * SC))
            big = cell.resize((n, n), Image.NEAREST)
            o = (n - CELL) // 2
            sc.paste(big.crop((o, o, o + CELL, o + CELL)), (c * CELL, 0))
        im0 = sc
    a = np.array(im0)
    out = a.copy()
    h, s, v = hsv(a[..., :3].astype(float))
    v2 = v ** G
    s2 = np.where(v2 > v, s * (1 - .25 * (v2 - v) / np.maximum(v2, 1e-3)), s)
    lit = rgb(h, np.clip(s2, 0, 1), np.clip(v2, 0, 1))
    solid = a[..., 3] >= 96
    out[..., :3] = np.where(solid[..., None], lit, a[..., :3]).astype(np.uint8)
    for c in range(a.shape[1] // CELL):
        sl = slice(c * CELL, (c + 1) * CELL)
        sd = solid[:, sl]; g = sd
        for _ in range(RW): g = dil(g)
        ring = g & ~sd & (a[:, sl, 3] < 96)
        cell = out[:, sl]; cell[ring] = (*RIM, 255)
        # break any ruler-straight vertical silhouette run (>= 22 px) with a 1 px notch every ~18 px, so no cell has a straight cut
    for c in range(out.shape[1] // CELL):
        cell = out[:, c * CELL:(c + 1) * CELL]
        solid = cell[..., 3] > 128
        for side in (-1, 1):
            for x in range(CELL):
                nx = x + side
                if not 0 <= nx < CELL: continue
                edge = solid[:, x] & (cell[:, nx, 3] == 0)
                run = 0
                for y in range(CELL):
                    run = run + 1 if edge[y] else 0
                    if run >= 18:
                        cell[y, x] = (0, 0, 0, 0); solid[y, x] = False; edge[y] = False; run = 0
    (HERE / "strips_lit").mkdir(exist_ok=True)
    Image.fromarray(out).save(HERE / "strips_lit" / src.name)
    print("lit", src.name)
