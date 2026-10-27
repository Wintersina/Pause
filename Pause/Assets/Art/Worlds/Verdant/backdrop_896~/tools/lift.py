"""Whole-tile lift of crushed blacks (no per-lane shaping): v' = floor + (1-floor)*v**gamma, hue and saturation kept.
usage: lift.py <in> <out>"""
import sys, numpy as np
from pathlib import Path
from PIL import Image
sys.path.insert(0, str(Path(__file__).parent))
from build_tiles import rgb2hsv, hsv2rgb
P = {"1": (.07, .9), "2": (.07, .9), "3": (.07, .9), "4": (.13, .8)}
i, o = Path(sys.argv[1]), Path(sys.argv[2])
for v, (fl, g) in P.items():
    (o / f"v{v}").mkdir(parents=True, exist_ok=True)
    for r in ("sky", "far", "mid", "flow"):
        im = Image.open(i / f"v{v}" / f"{r}.png").convert("RGBA"); a = np.asarray(im)
        if r == "flow": Image.fromarray(a).save(o / f"v{v}" / f"{r}.png"); continue
        h, s, val = rgb2hsv(a[..., :3].astype(np.float32) / 255)
        v2 = fl + (1 - fl) * val ** g
        rgb = np.clip(np.rint(hsv2rgb(h, s, v2) * 255), 0, 255).astype(np.uint8)
        rgb[:, -1] = rgb[:, 0]; rgb[-1] = rgb[0]
        Image.fromarray(np.dstack([rgb, a[..., 3]]), "RGBA").save(o / f"v{v}" / f"{r}.png")
