#!/usr/bin/env python3
"""Readability of the Tide elite hulls with NO outline: ReadabilitySweep renders enemies and shots but not elite ships, so this measures the hover
cell the sweep's way (WCAG luminance, 3:1 clear of the local backdrop) against backdrop patches taken from the sweep's Tide frames.
  python3 readability.py <sweep dir with tide-v*-enemy-p*.png> <strips dir>"""
import sys, glob
import numpy as np
from PIL import Image
sweep, strips = sys.argv[1], sys.argv[2]


def lum(rgb):
    c = rgb / 255.0
    c = np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
    return .2126 * c[..., 0] + .7152 * c[..., 1] + .0722 * c[..., 2]


# backdrop patches: 120 px squares from every frame, away from the centred sprites (the corners and the top band)
patches = []
for f in sorted(glob.glob(f"{sweep}/tide-v*-shot-p0.png")):
    im = np.asarray(Image.open(f).convert("RGB"))
    h, w = im.shape[:2]
    for (y, x) in ((10, 10), (10, w - 130), (h // 2, 10), (h // 2, w - 130), (h - 130, 10), (h - 130, w - 130)):
        patches.append(lum(im[y:y + 120, x:x + 120].astype(float)).mean())
patches = np.array(patches)
print("backdrop luminance: min %.3f  median %.3f  max %.3f (%d patches)" % (patches.min(), np.median(patches), patches.max(), len(patches)))
for n in "riptide bathyscaphe trawler thunderfin pearl_warden".split():
    cell = np.asarray(Image.open(f"{strips}/tide_elite_{n}.png").convert("RGBA"))[:, 3 * 384:4 * 384].astype(float)
    solid = cell[..., 3] >= 128
    L = lum(cell[..., :3])[solid]
    res = []
    for b in (patches.min(), np.percentile(patches, 25), np.median(patches), np.percentile(patches, 75), patches.max()):
        ratio = (np.maximum(L, b) + .05) / (np.minimum(L, b) + .05)
        res.append((ratio >= 3).mean())
    # edge ring: outermost 3 px of the silhouette
    from PIL import ImageFilter
    inner = np.asarray(Image.fromarray((solid * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(7))) > 0
    er = solid & ~inner
    Le = lum(cell[..., :3])[er]
    ering = [((np.maximum(Le, b) + .05) / (np.minimum(Le, b) + .05) >= 3).mean() for b in (patches.min(), np.median(patches), patches.max())]
    print("%-13s hull mean lum %.3f | 3:1-clear share vs backdrop min/p25/med/p75/max: %s | edge ring vs min/med/max: %s" % (
        n, L.mean(), " ".join("%.2f" % r for r in res), " ".join("%.2f" % r for r in ering)))
