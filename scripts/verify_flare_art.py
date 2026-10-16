#!/usr/bin/env python3
"""Verify the Space solar-flare sheets (docs/weather-overhaul.md).

    python3 scripts/verify_flare_art.py <DIR> [--sheet preview.png]

<DIR> holds hazard.png (3072x1536, 6x3 cells of 512: rows 0/1 flare A/B loops, row 2 warnings in cells 0-3) and ambient.png (2048x512, 256 cells).
Each flare cell is a vertical band: leading edge at x ~ 400, tail to the left, vertically seamless.
"""
import argparse, os, sys
import numpy as np
from PIL import Image

FAIL = 0


def check(what, ok, extra=""):
    global FAIL
    print(("PASS  " if ok else "FAIL  ") + what + (("  [" + extra + "]") if extra else ""))
    if not ok:
        FAIL += 1


def red_share(a):
    op = a[..., 3] > 24
    rgb = a[..., :3][op] / 255.0
    if len(rgb) == 0:
        return 0.0
    mx = rgb.max(1); mn = rgb.min(1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    h = np.where(d > 1e-6, np.where(mx == r, ((g - b) / np.maximum(d, 1e-6)) % 6, np.where(mx == g, (b - r) / np.maximum(d, 1e-6) + 2, (r - g) / np.maximum(d, 1e-6) + 4)), 0) * 60
    return float((((h <= 8) | (h >= 352)) & (s > .7) & (mx > .5)).mean())


def block_upscale(c):
    h, w = c.shape[:2]; op = c[..., 3] > 24; worst = 0.0
    for k in (2, 3, 4):
        ys = np.arange(0, h - k + 1); xs = np.arange(0, w - k + 1)
        by = (ys % k)[:, None]; bx = (xs % k)[None, :]
        first = c[ys[:, None] - by, xs[None, :] - bx]; cur = c[ys[:, None], xs[None, :]]
        same = np.all(first[..., :3] == cur[..., :3], axis=-1)
        m = op[ys[:, None], xs[None, :]]
        if m.sum():
            worst = max(worst, float((same & m).sum() / m.sum()))
    return worst


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("dir"); ap.add_argument("--sheet")
    o = ap.parse_args()
    sheets = []
    p = os.path.join(o.dir, "hazard.png")
    if not os.path.exists(p):
        check("hazard.png exists", False)
    else:
        im = Image.open(p).convert("RGBA"); sheets.append(im)
        check("hazard.png is 3072x1536", im.size == (3072, 1536), "%dx%d" % im.size)
        if im.size == (3072, 1536):
            a = np.array(im)
            check("no red", red_share(a) < .01, "%.4f" % red_share(a))
            for r in range(3):
                for c in range(6):
                    if r == 2 and c > 3:
                        continue
                    cell = a[r * 512:(r + 1) * 512, c * 512:(c + 1) * 512]
                    n = int((cell[..., 3] > 24).sum())
                    check("r%d c%d is filled" % (r, c), n > (2500 if r == 2 else 9000), "%d px" % n)
                    if r < 2:
                        seam = float(np.abs(cell[0, :, 3].astype(int) - cell[-1, :, 3].astype(int)).mean())
                        check("r%d c%d is vertically seamless" % (r, c), seam < 14, "alpha diff %.1f" % seam)
                        colsum = (cell[..., 3] > 200).sum(0)
                        lead = int(np.argmax(colsum))
                        check("r%d c%d leading edge near x=400" % (r, c), 370 <= lead <= 430, "x=%d" % lead)
                        tail = cell[:, :, 3].astype(float)[:, :380]
                        check("r%d c%d tail is semi-transparent (mean alpha %.2f)" % (r, c, tail[tail > 8].mean() / 255 if (tail > 8).any() else 0), (tail[tail > 8].mean() / 255 if (tail > 8).any() else 1) <= .6)
                        bu = block_upscale(cell)
                        check("r%d c%d is true resolution (no block upscale)" % (r, c), bu < .97, "%.2f" % bu)
    p = os.path.join(o.dir, "ambient.png")
    if not os.path.exists(p):
        check("ambient.png exists", False)
    else:
        im = Image.open(p).convert("RGBA"); sheets.append(im)
        check("ambient.png is 2048x512", im.size == (2048, 512))
        if im.size == (2048, 512):
            a = np.array(im)
            check("ambient no red", red_share(a) < .01)
            for r in range(2):
                for c in range(8):
                    n = int((a[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256, 3] > 24).sum())
                    check("ambient r%d c%d filled" % (r, c), n > 150, "%d px" % n)
    if o.sheet and sheets:
        W = max(s.size[0] for s in sheets); H = sum(s.size[1] for s in sheets)
        out = Image.new("RGBA", (W, H), (11, 11, 26, 255)); y = 0
        for s in sheets:
            out.alpha_composite(s, (0, y)); y += s.size[1]
        out.convert("RGB").save(o.sheet); print("wrote", o.sheet)
    print("RESULT:", "FAIL (%d)" % FAIL if FAIL else "PASS")
    sys.exit(1 if FAIL else 0)


main()
