#!/usr/bin/env python3
"""Crop the wired Space sun DISC sheets (RGBA32 on phones, never compressed) to the drawn cell's visible box.

The approved sheet sun_<palette>.png is 2048 x 1024: two 1024 cells, of which SpaceSunLayer draws ONE
(warm: cell 0, violet: cell 1) and the disc fills only ~78 % of it, so most of the 8.4 MB RGBA32 texture is
clear pixels. This keeps the drawn pixels byte for byte and drops the clear ones: the output is the drawn
cell's alpha bounding box plus a Margin of clear pixels; SpaceSunTuning.DiscCrop carries the box so the
sprite's pivot puts the disc exactly where the full cell put it (the cell centre stays the sun's centre).

    python3 scripts/crop_sun_disc.py --check                 verify the wired files against the constants
    python3 scripts/crop_sun_disc.py --from <dir-with-originals>   crop the 2048 x 1024 originals in place

--check proves pixel identity: every pixel of the wired file equals the original's sub-rectangle and every
pixel the crop dropped (the rest of the drawn cell) is fully clear (alpha 0), and prints the placement
constants (x, y bottom-up, w, h in the cell's 1024 frame) that SpaceSun.cs must carry."""
import argparse, os, sys
import numpy as np
from PIL import Image

DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Pause", "Assets", "Art", "Backgrounds",
                   "Resources", "Worlds", "Space", "Backdrop")
CELL = 1024
MARGIN = 4
DRAWN_CELL = {"warm": 0, "violet": 1}     # SpaceSunTuning.VariantWarm / VariantViolet
F = 0


def chk(what, ok, extra=""):
    global F
    print(("PASS  " if ok else "FAIL  ") + what + ("  [%s]" % extra if extra else ""))
    F += 0 if ok else 1


def box(alpha_cell):
    ys, xs = np.nonzero(alpha_cell)
    x0 = max(0, xs.min() - MARGIN); x1 = min(CELL, xs.max() + 1 + MARGIN)
    yt = max(0, ys.min() - MARGIN); yb = min(CELL, ys.max() + 1 + MARGIN)      # top-down rows
    if (x1 - x0) % 2: x1 = min(CELL, x1 + 1)
    if (yb - yt) % 2: yb = min(CELL, yb + 1)
    return x0, yt, x1, yb


def crop(src_dir):
    for p, cell in DRAWN_CELL.items():
        src = os.path.join(src_dir, "sun_%s.png" % p)
        im = Image.open(src).convert("RGBA")
        if im.size != (2 * CELL, CELL): chk(src + " is 2048x1024", False, str(im.size)); continue
        a = np.array(im)
        c = a[:, cell * CELL:(cell + 1) * CELL]
        x0, yt, x1, yb = box(c[..., 3])
        out = Image.fromarray(c[yt:yb, x0:x1])
        dst = os.path.join(DIR, "sun_%s.png" % p)
        out.save(dst, optimize=True)
        print("cropped", dst, "to", out.size, "x", x0, "y(bottom-up)", CELL - yb, "w", x1 - x0, "h", yb - yt)


def check(src_dir):
    for p, cell in DRAWN_CELL.items():
        wired = np.array(Image.open(os.path.join(DIR, "sun_%s.png" % p)).convert("RGBA"))
        h, w = wired.shape[:2]
        chk("sun_%s: wired disc is smaller than the 1024 cell" % p, w < CELL and h < CELL, "%dx%d" % (w, h))
        chk("sun_%s: the crop keeps a clear margin (alpha 0 on all four edges)" % p,
            wired[0, :, 3].max() == 0 and wired[-1, :, 3].max() == 0 and wired[:, 0, 3].max() == 0 and wired[:, -1, 3].max() == 0)
        if not src_dir: continue
        src = os.path.join(src_dir, "sun_%s.png" % p)
        if not os.path.exists(src): chk("original " + src + " exists", False); continue
        a = np.array(Image.open(src).convert("RGBA"))
        c = a[:, cell * CELL:(cell + 1) * CELL]
        x0, yt, x1, yb = box(c[..., 3])
        chk("sun_%s: wired size equals the drawn cell's box" % p, (w, h) == (x1 - x0, yb - yt), "%dx%d vs %dx%d" % (w, h, x1 - x0, yb - yt))
        if (w, h) == (x1 - x0, yb - yt):
            chk("sun_%s: every wired pixel equals the original's" % p, bool(np.array_equal(wired, c[yt:yb, x0:x1])))
        outside = c[..., 3].copy(); outside[yt:yb, x0:x1] = 0
        chk("sun_%s: every dropped pixel of the drawn cell is clear (alpha 0)" % p, int(outside.max()) == 0)
        print("  SpaceSunTuning.DiscCrop(%s): x=%d y=%d w=%d h=%d  (y bottom-up, in the cell's 1024 frame)" % (p, x0, CELL - yb, x1 - x0, yb - yt))
        print("  RGBA32 bytes: %d -> %d" % (2 * CELL * CELL * 4, w * h * 4))


ap = argparse.ArgumentParser()
ap.add_argument("--from", dest="src", help="directory holding the original 2048x1024 sun_<palette>.png sheets")
ap.add_argument("--check", action="store_true")
o = ap.parse_args()
if not o.src and not o.check: ap.error("--from <dir> and/or --check")
if o.src and not o.check: crop(o.src)
if o.check: check(o.src)
print("RESULT:", "FAIL (%d)" % F if F else "PASS")
sys.exit(1 if F else 0)
