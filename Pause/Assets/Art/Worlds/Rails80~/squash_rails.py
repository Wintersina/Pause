"""Squash every reinforced rail to 80% of its width (user: "squash the rails down to 80%").

Horizontal-only LANCZOS on PREMULTIPLIED rgba (no colour bleeds out of transparent texels,
no fringe halo), height kept, the whole canvas squashed so every silhouette fraction
(WorldPainter.RailBounds: u = px / width) stays where it was. Alpha is clamped (a binary-alpha source -- Tide, Storm -- stays binary, cut at .5), texels whose
alpha rounds to 0 are zeroed rgb (premultiply-safe), and colours are un-premultiplied.
Originals: src~/<World>/<file>.png (725 px wide). Re-run from the originals only.
Usage: python3 squash_rails.py <Assets dir>
"""
import sys, os, shutil, glob
import numpy as np
from PIL import Image

SCALE = .8
assets = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
for live in sorted(glob.glob(os.path.join(assets, "Art/Resources/Worlds/*/rail_*_wide_v1.png"))):
    world = os.path.basename(os.path.dirname(live))
    src = os.path.join(here, "src~", world, os.path.basename(live))
    if not os.path.exists(src):
        os.makedirs(os.path.dirname(src), exist_ok=True)
        shutil.copy2(live, src)
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    nw = int(round(w * SCALE))
    a = np.asarray(im).astype(np.float64) / 255.
    pm = a.copy(); pm[..., :3] *= a[..., 3:4]
    chans = [np.asarray(Image.fromarray(pm[..., c].astype(np.float32), "F").resize((nw, h), Image.LANCZOS)) for c in range(4)]
    out = np.stack(chans, -1).astype(np.float64)
    al = np.clip(out[..., 3], 0, 1)
    rgb = np.clip(out[..., :3], 0, None)
    rgb = np.minimum(rgb, al[..., None])               # premultiplied rgb never exceeds alpha
    a8 = np.round(al * 255)
    srcA = np.asarray(im)[..., 3]
    if np.isin(srcA, (0, 255)).all():                  # a hard-edged (binary alpha) source stays hard-edged
        a8 = np.where(al >= .5, 255., 0.)
    safe = np.where(a8 > 0, al, 1)
    col = np.where(a8[..., None] > 0, rgb / safe[..., None], 0)
    res = np.dstack([np.round(np.clip(col, 0, 1) * 255), a8]).astype(np.uint8)
    Image.fromarray(res, "RGBA").save(live, optimize=True)
    print(world, (w, h), "->", (nw, h))
