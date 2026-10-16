#!/usr/bin/env python3
"""Hull readability without an outline: share of each elite's hover-cell pixels that are 3:1 (WCAG) clear of the Storm backdrops
(sky + mid layers of every variant, opaque pixels), measured against the backdrop's luminance at its 15th / 50th / 85th percentile.
Also the old strips (git) for comparison: python3 contrast.py [old]"""
import sys, io, subprocess
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
BD = ART / "Backgrounds/Resources/Worlds/Storm/Backdrop3"
NAMES = "skyhook_harrier refinery_tender rod_dancer anvil_cloud static_bastion".split()


def lin(c):
    c = c / 255.0
    return np.where(c <= .03928, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def lum(rgb):
    return .2126 * lin(rgb[..., 0]) + .7152 * lin(rgb[..., 1]) + .0722 * lin(rgb[..., 2])


bg = []
for v in ("v1", "v2", "v3"):
    for layer in ("sky", "mid", "far"):
        p = BD / v / f"{layer}.png"
        if p.exists():
            a = np.asarray(Image.open(p).convert("RGBA").resize((256, 256)))
            m = a[..., 3] > 200
            bg.append(lum(a[..., :3].astype(float))[m])
bg = np.concatenate(bg)
qs = np.percentile(bg, [15, 50, 85])
print("backdrop luminance p15/p50/p85:", np.round(qs, 3))
old = len(sys.argv) > 1
for n in NAMES:
    p = ART / f"Resources/Elites/Storm/storm_elite_{n}.png"
    if old:
        data = subprocess.run(["git", "show", f"origin/master:Pause/Assets/Art/Resources/Elites/Storm/storm_elite_{n}.png"], capture_output=True, cwd=HERE.parents[5]).stdout
        im = Image.open(io.BytesIO(data))
    else:
        im = Image.open(p)
    a = np.asarray(im.convert("RGBA").crop((1152, 0, 1536, 384)))
    m = a[..., 3] > 200
    L = lum(a[..., :3].astype(float))[m]
    res = []
    for q in qs:
        c = (np.maximum(L, q) + .05) / (np.minimum(L, q) + .05)
        res.append((c >= 3).mean())
    print(f"{n:18s} hull median lum {np.median(L):.3f}  share >=3:1 vs p15/p50/p85: " + " / ".join(f"{r:.0%}" for r in res))
