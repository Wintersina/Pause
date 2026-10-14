#!/usr/bin/env python3
"""Re-grade a world's boulder sheets to sit in its backdrop's value range (docs/cover-boulders.md section 13).

    python3 scripts/tools/boulder_grade.py <dir> --body "0:0,60:26,150:70,200:105,235:200,255:255" --sat 0.75 --tint 40,52,66 --tint-amt .12 --rim 0.88

Body pixels: luma through the piecewise curve (chroma scaled with it, saturation x --sat, nudged --tint-amt toward the tint colour).
The outermost 3 px (the pale readability rim) and any glow above luma 235 only get the gentle --rim gain, so the thin pale edge and the crack
light stay.  The pristine originals are kept in <dir>/src~/orig_boulder.png / orig_break.png (the first run) and every run starts from them."""
import argparse, os, shutil
import numpy as np
from PIL import Image
ap = argparse.ArgumentParser()
ap.add_argument("dir")
ap.add_argument("--body", required=True)
ap.add_argument("--sat", type=float, default=.8)
ap.add_argument("--tint", default="40,52,66")
ap.add_argument("--tint-amt", type=float, default=.1)
ap.add_argument("--rim", type=float, default=.9)
ap.add_argument("--rim-px", type=int, default=3)
a = ap.parse_args()
pts = [tuple(float(v) for v in p.split(":")) for p in a.body.split(",")]
xs, ys = zip(*pts)
tint = np.array([float(v) for v in a.tint.split(",")])
os.makedirs(a.dir + "/src~", exist_ok=True)
for name in ("boulder", "break"):
    orig = "%s/src~/orig_%s.png" % (a.dir, name)
    if not os.path.exists(orig):
        shutil.copy("%s/%s.png" % (a.dir, name), orig)
    im = np.array(Image.open(orig).convert("RGBA")).astype(np.float32)
    al = im[..., 3] > 128
    er = al.copy()
    for _ in range(a.rim_px):
        n = er.copy()
        n[1:, :] &= er[:-1, :]; n[:-1, :] &= er[1:, :]; n[:, 1:] &= er[:, :-1]; n[:, :-1] &= er[:, 1:]
        er = n
    ring = al & ~er
    rgb = im[..., :3]
    l = rgb @ np.array([.299, .587, .114], np.float32)
    l2 = np.interp(l, xs, ys).astype(np.float32)
    gain = l2 / np.maximum(l, 1)
    g = gain[..., None]
    grey = l[..., None]
    out = grey * g + (rgb - grey) * g * a.sat            # keep luma ratio, scale chroma
    tl = float(tint @ np.array([.299, .587, .114]))
    out = out * (1 - a.tint_amt) + tint * (l2[..., None] / max(tl, 1)) * a.tint_amt
    rim = np.clip(rgb * a.rim, 0, 255)
    glow = (l >= 235) & ~ring
    use_rim = (ring | glow)[..., None]
    res = np.where(use_rim, np.where(glow[..., None], rgb, rim), out)
    res = np.clip(res, 0, 255)
    im[..., :3] = res
    Image.fromarray(im.astype(np.uint8)).save("%s/%s.png" % (a.dir, name))
print("graded", a.dir)
