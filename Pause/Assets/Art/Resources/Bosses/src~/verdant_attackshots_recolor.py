# Recolours Verdant_attackshots.png (reads src~/Verdant_attackshots_orig.png) so the green/brown shots hold >=3:1 against the dark jungle
# backdrop: luminance remapped onto a lime -> pollen -> cream ramp, the baked dark keyline kept dark. Alpha, grid and anchors untouched.
import sys, numpy as np
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGBA')).astype(float)
rgb, al = a[..., :3] / 255, a[..., 3:]
Y = .299*rgb[..., 0] + .587*rgb[..., 1] + .114*rgb[..., 2]
stops = [(0, (150, 226, 72)), (.45, (226, 250, 150)), (1, (255, 255, 240))]
lo_, hi_ = .20, .75
t = np.clip((Y - lo_) / (hi_ - lo_), 0, 1)
out = np.zeros_like(rgb)
for ch in range(3):
    out[..., ch] = np.interp(t, [s[0] for s in stops], [s[1][ch] for s in stops]) / 255
dark = np.array([14, 36, 16]) / 255
k = np.clip((Y - .08) / .14, 0, 1)[..., None]       # keyline stays dark, blends into the body
out = dark * (1 - k) + out * k
res = np.concatenate([out * 255, al], -1).round().clip(0, 255).astype(np.uint8)
Image.fromarray(res, 'RGBA').save(dst)
