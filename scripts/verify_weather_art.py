#!/usr/bin/env python3
"""Verify one world's weather / hazard art (docs/world-weather-hazards.md section 12).

    python3 scripts/verify_weather_art.py <DIR> --world Storm [--sheet preview.png]

<DIR> holds:
    hazard.png   3072 x 1536  6 columns x 3 rows of 512 px cells (transparent)
                   row 0  hazard A loop, 6 frames      row 1  hazard B loop, 6 frames
                   row 2  cells 0-1 hazard A WARNING, cells 2-3 hazard B WARNING, cells 4-5 free (may be empty)
    ambient.png  2048 x 512   8 columns x 2 rows of 256 px cells: 4 particle kinds x 4 variants (row-major, kind = cell // 4)

A FRONT (a wall that sweeps across the lane) must tile horizontally: its left and right alpha columns agree.
Which hazard is a front is given with --fronts A,B (e.g. --fronts B).  Prints PASS / FAIL, exits 1 on a failure.
--sheet writes a contact sheet of the art on dark #0b0b1a (no labels, no mock UI).
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


def hsv_stats(a):
    op = a[..., 3] > 24
    rgb = a[..., :3][op] / 255.0
    if len(rgb) == 0:
        return 0.0, 0.0, 0
    mx = rgb.max(1); mn = rgb.min(1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    h = np.zeros(len(rgb))
    nz = d > 1e-6
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    h = np.where(nz & (mx == r), ((g - b) / np.maximum(d, 1e-6)) % 6, h)
    h = np.where(nz & (mx == g), (b - r) / np.maximum(d, 1e-6) + 2, h)
    h = np.where(nz & (mx == b), (r - g) / np.maximum(d, 1e-6) + 4, h)
    h = h * 60
    pink = (h >= 285) & (h <= 352) & (s > .25) & (mx > .3)
    red = ((h <= 12) | (h >= 348)) & (s > .6) & (mx > .35)
    return float(pink.mean()), float(red.mean()), len(rgb)


def block_upscale(cellpx):
    """Largest share of opaque texels equal to the first texel of their k x k block (k = 2..4, aligned phase and diagonal shifts)."""
    h, w = cellpx.shape[:2]; op = cellpx[..., 3] > 24; worst = 0.0
    for k in (2, 3, 4):
        for ph in range(k):
            ys = np.arange(ph, h - k + 1); xs = np.arange(ph, w - k + 1)
            by = ((ys - ph) % k)[:, None]; bx = ((xs - ph) % k)[None, :]
            Y = ys[:, None] - by; X = xs[None, :] - bx
            first = cellpx[Y, X]; cur = cellpx[ys[:, None], xs[None, :]]
            same = np.all(first[..., :3] == cur[..., :3], axis=-1) & (np.abs(first[..., 3].astype(int) - cur[..., 3].astype(int)) < 3)
            m = op[ys[:, None], xs[None, :]]
            if m.sum() > 0: worst = max(worst, float((same & m).sum() / m.sum()))
    return worst


def cells(a, size, cols, rows):
    for r in range(rows):
        for c in range(cols):
            yield r, c, a[r * size:(r + 1) * size, c * size:(c + 1) * size]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("--world", required=True)
    ap.add_argument("--fronts", default=""); ap.add_argument("--sheet")
    o = ap.parse_args()
    space = o.world.lower() == "space"
    fronts = [x for x in o.fronts.split(",") if x]
    sheets = []
    for name, size, cols, rows, cell in (("hazard.png", (3072, 1536), 6, 3, 512), ("ambient.png", (2048, 512), 8, 2, 256)):
        p = os.path.join(o.dir, name)
        if not os.path.exists(p):
            check(name + " exists", False); continue
        im = Image.open(p).convert("RGBA")
        check(name + " is %dx%d" % size, im.size == size, "got %dx%d" % im.size)
        if im.size != size:
            continue
        a = np.array(im); sheets.append(im)
        pink, red, n = hsv_stats(a)
        check(name + " has art", n > 2000, "%d px" % n)
        check(name + " no pink outside Space", space or pink < .004, "pink share %.4f" % pink)
        check(name + " no red", red < .01, "red share %.4f" % red)
        a8 = a[..., 3]
        mid = float(((a8 > 8) & (a8 < 247)).sum()) / max(1, int((a8 > 8).sum()))
        print("INFO  %s soft-alpha share %.2f" % (name, mid))
        for r, c, cellpx in cells(a, cell, cols, rows):
            filled = int((cellpx[..., 3] > 24).sum())
            used = (name == "hazard.png" and (r < 2 or (r == 2 and c < 4))) or (name == "ambient.png")
            if not used:
                continue
            check("%s cell r%d c%d is filled" % (name, r, c), filled > (150 if name == "ambient.png" else 1500), "%d px" % filled)
            ys, xs = np.nonzero(cellpx[..., 3] > 24)
            if len(xs) == 0:
                continue
            cu8 = cellpx.astype(np.uint8)
            if filled >= (100 if name == "ambient.png" else 400):
                bu = block_upscale(cu8)
                check("%s cell r%d c%d is true resolution (no 2x/3x/4x block upscale)" % (name, r, c), bu < .97, "worst block share %.2f" % bu)
            m = 6
            inside = xs.min() >= m and ys.min() >= m and xs.max() < cell - m and ys.max() < cell - m
            fronts_row = name == "hazard.png" and ((r == 0 and "A" in fronts) or (r == 1 and "B" in fronts))
            if not fronts_row:
                check("%s cell r%d c%d keeps a %d px margin" % (name, r, c, m), inside)
            else:
                l = cellpx[:, 0, 3].astype(int); rr = cellpx[:, -1, 3].astype(int)
                check("%s front r%d c%d tiles horizontally" % (name, r, c), float(np.abs(l - rr).mean()) < 14,
                      "edge diff %.1f" % float(np.abs(l - rr).mean()))
    if o.sheet and sheets:
        W = max(s.size[0] for s in sheets); H = sum(s.size[1] for s in sheets)
        out = Image.new("RGBA", (W, H), (11, 11, 26, 255)); y = 0
        for s in sheets:
            out.alpha_composite(s, (0, y)); y += s.size[1]
        out.convert("RGB").save(o.sheet); print("wrote", o.sheet)
    print("RESULT:", "FAIL (%d)" % FAIL if FAIL else "PASS")
    sys.exit(1 if FAIL else 0)


main()
