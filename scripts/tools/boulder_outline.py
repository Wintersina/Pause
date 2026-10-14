#!/usr/bin/env python3
"""One-sided pale outline for a boulder that needs to be seen (Storm; docs/cover-boulders.md section 13).

    python3 scripts/tools/boulder_outline.py <dir> --color 222,230,242 --px 4 [--bottom 0.0]

Rim = the outer --px pixels of the SOLID silhouette (alpha > 240; the fading base skirt is never outlined). Per pixel the weight is by the outward
normal: 1 on the top and sides, falling to --bottom (default .0) where the edge faces down, so the rim fades out along the base and does not look cut out.
Starts from the pristine src~/pre_outline_{boulder,break}.png (kept on the first run), so it is repeatable."""
import argparse, os, shutil
import numpy as np
from PIL import Image, ImageFilter
ap = argparse.ArgumentParser()
ap.add_argument("dir"); ap.add_argument("--color", default="222,230,242"); ap.add_argument("--px", type=int, default=4)
ap.add_argument("--bottom", type=float, default=.0); ap.add_argument("--strength", type=float, default=.9)
a = ap.parse_args()
col = np.array([float(v) for v in a.color.split(",")], np.float32)
os.makedirs(a.dir + "/src~", exist_ok=True)
CELL = 768
for name in ("boulder", "break"):
    orig = "%s/src~/pre_outline_%s.png" % (a.dir, name)
    if not os.path.exists(orig):
        shutil.copy("%s/%s.png" % (a.dir, name), orig)
    im = np.array(Image.open(orig).convert("RGBA")).astype(np.float32)
    H, W = im.shape[:2]
    solid = im[..., 3] > 240
    er = solid.copy()
    for _ in range(a.px):
        n = er.copy(); n[1:, :] &= er[:-1, :]; n[:-1, :] &= er[1:, :]; n[:, 1:] &= er[:, :-1]; n[:, :-1] &= er[:, 1:]; er = n
    ring = solid & ~er
    # outward normal from the blurred silhouette (screen y down in the array)
    blur = np.array(Image.fromarray((solid * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(a.px * 2.2))).astype(np.float32) / 255
    gy, gx = np.gradient(blur)
    mag = np.sqrt(gx * gx + gy * gy) + 1e-6
    up = np.clip(gy / mag, -1, 1)          # gradient of "solid" points inward; the OUTWARD normal is -grad, so an edge facing up has gy > 0
    w = np.clip(a.bottom + (1 - a.bottom) * (.5 + .5 * up * 1.6), 0, 1)   # up-facing 1, sides ~.5-.9, down-facing ~a.bottom
    w = np.where(up > -.2, np.maximum(w, .75), w)                          # sides stay readable
    t = (ring * w * a.strength)[..., None]
    im[..., :3] = im[..., :3] * (1 - t) + col * t
    Image.fromarray(np.clip(im, 0, 255).astype(np.uint8)).save("%s/%s.png" % (a.dir, name))
print("outlined", a.dir)
