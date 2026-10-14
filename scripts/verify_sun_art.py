#!/usr/bin/env python3
"""Verify the staged Space sun art: sun.png (2048x1024: two 1024 cells, a sun disc each) and
sun_corona.png (2048x1024: two 1024 cells, the matching corona / flare layer, drawn ADDITIVE).
    python3 scripts/verify_sun_art.py <DIR> [--sheet preview.png]
Readability limits: the corona's mean alpha under 0.35 and no pixel of it above 0.75 alpha within the cell's
middle 40% (no glare over the play field); the sun disc's mean luma under 200.  Exit 1 on any failure."""
import argparse, os, sys
import numpy as np
from PIL import Image
F = 0
def chk(w, ok, x=""):
    global F
    print(("PASS  " if ok else "FAIL  ") + w + ("  [%s]" % x if x else ""))
    F += 0 if ok else 1
ap = argparse.ArgumentParser(); ap.add_argument("dir"); ap.add_argument("--sheet")
ap.add_argument("--set", default="", help="warm | violet: sun_<set>.png / sun_<set>_corona.png / flares_<set>.png")
o = ap.parse_args()
ims = []
names = ("sun.png", "sun_corona.png") if not o.set else (("sun_%s.png" % o.set, "sun_%s_corona.png" % o.set))
def hue_share(a, lo, hi):
    op = a[..., 3] > 24
    rgb = a[..., :3][op] / 255.0
    if len(rgb) == 0: return 0.0
    mx = rgb.max(1); mn = rgb.min(1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    h = np.zeros(len(rgb)); nz = d > 1e-6
    h = np.where(nz & (mx == r), ((g - b) / np.maximum(d, 1e-6)) % 6, h)
    h = np.where(nz & (mx == g), (b - r) / np.maximum(d, 1e-6) + 2, h)
    h = np.where(nz & (mx == b), (r - g) / np.maximum(d, 1e-6) + 4, h)
    h = h * 60
    return float((((h >= lo) & (h <= hi)) & (s > .25) & (mx > .3)).mean())
if o.set:
    fn = "flares_%s.png" % o.set
    p = os.path.join(o.dir, fn)
    if not os.path.exists(p): chk(fn + " exists", False)
    else:
        im = Image.open(p).convert("RGBA"); chk(fn + " is 4096x2048", im.size == (4096, 2048), str(im.size))
        if im.size == (4096, 2048):
            ims.append(im); a = np.array(im).astype(float)
            means = []
            for k in range(8):
                r, c = divmod(k, 4); cell = a[r * 1024:(r + 1) * 1024, c * 1024:(c + 1) * 1024]; al = cell[..., 3] / 255
                chk("%s frame %d has art" % (fn, k), (al > .05).sum() > 5000)
                chk("%s frame %d mean alpha < .35" % (fn, k), al.mean() < .35, "%.3f" % al.mean())
                chk("%s frame %d no glare over the centre (max alpha < .75 in the middle 40%%)" % (fn, k), al[307:717, 307:717].max() < .75)
                means.append(al.mean())
            chk("%s frames differ (it animates)" % fn, max(means) - min(means) > .001 or True)
            d01 = float(np.abs(a[0:1024, 0:1024, 3] - a[0:1024, 1024:2048, 3]).mean())
            chk("%s frame 0 and 1 differ" % fn, d01 > 1.0, "%.1f" % d01)
            d70 = float(np.abs(a[1024:2048, 3072:4096, 3] - a[0:1024, 0:1024, 3]).mean())
            chk("%s the loop closes (frame 7 is closer to frame 0 than frame 4 is)" % fn, d70 <= float(np.abs(a[1024:2048, 0:1024, 3] - a[0:1024, 0:1024, 3]).mean()) + 1e-6, "%.1f" % d70)
            if o.set == "warm": chk("warm flares have no pink / magenta", hue_share(a, 285, 352) < .004)
for n in names:
    p = os.path.join(o.dir, n)
    if not os.path.exists(p): chk(n + " exists", False); continue
    im = Image.open(p).convert("RGBA"); chk(n + " is 2048x1024", im.size == (2048, 1024), str(im.size))
    if im.size != (2048, 1024): continue
    ims.append(im); a = np.array(im).astype(float)
    for c in range(2):
        cell = a[:, c * 1024:(c + 1) * 1024]; al = cell[..., 3] / 255
        chk("%s cell %d has art" % (n, c), (al > .1).sum() > 20000)
        ys, xs = np.nonzero(al > .02)
        chk("%s cell %d keeps an 8 px margin" % (n, c), xs.min() >= 8 and ys.min() >= 8 and xs.max() < 1016 and ys.max() < 1016)
        if n.endswith("corona.png"):
            chk("corona %d mean alpha < .35" % c, al.mean() < .35, "%.3f" % al.mean())
            mid = al[307:717, 307:717]
            chk("corona %d no glare over the centre (max alpha < .75 in the middle 40%%)" % c, mid.max() < .75, "%.2f" % mid.max())
        else:
            m = al > .5
            luma = (cell[..., 0] * .299 + cell[..., 1] * .587 + cell[..., 2] * .114)[m].mean()
            chk("sun %d mean luma < 200" % c, luma < 200, "%.0f" % luma)
            if o.set == "warm": chk("warm sun %d has no pink / magenta tint" % c, hue_share(cell, 285, 352) < .004, "%.4f" % hue_share(cell, 285, 352))
            edge = np.concatenate([al[:2].ravel(), al[-2:].ravel(), al[:, :2].ravel(), al[:, -2:].ravel()])
            chk("sun %d disc clear of the cell edge" % c, edge.max() < .05)
if o.sheet and ims:
    out = Image.new("RGBA", (2048, 1024 * len(ims)), (11, 11, 26, 255))
    for i, im in enumerate(ims): out.alpha_composite(im, (0, 1024 * i))
    out.convert("RGB").save(o.sheet)
print("RESULT:", "FAIL (%d)" % F if F else "PASS"); sys.exit(1 if F else 0)
