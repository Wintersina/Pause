"""Verdant's LIT (arming) mine atlas, rebuilt from the clean Verdant row of rail_mines_neon.png (user, 2026-10-13: "the green mine still turns into
a pink glow when it is about to shoot"). The old lit atlas was the Codex-painted hot-pink repaint with its pink pixels remapped to green: the
remap left a posterised, washed-out mint blob with salmon / rust flecks and a hard vertical clip, which read as a pinkish glow on the mine.

Now: the lit frame IS the original Verdant frame (same drawing, same brown wood, same vines), with its luminous green pixels pushed toward a
bright green-white and a soft green bloom added around them, kept inside the frame's own rect so nothing crosses into a neighbour. Nothing here
can produce a pink pixel: the bloom is hue 125, the boost keeps each pixel's own hue, the wood is untouched.
usage: python3 build_verdant_lit.py <rail_mines_neon.png> <dest rail_mines_neon_verdant_lit.png>
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

ROW_Y, ROW_H = 594, 298
RECTS = [(0, 320), (320, 613), (613, 922), (922, 1254)]   # the Verdant row's frame columns (RailMineArt.Rects)
BLOOM = {0: 0.0, 1: .55, 2: .85, 3: .0}                      # only the arming frames (waking, charging) glow harder; idle / burst stay original

src = Image.open(sys.argv[1]).convert("RGBA").crop((0, ROW_Y, 1254, ROW_Y + ROW_H))
a = np.array(src).astype(np.float32) / 255
rgb, al = a[..., :3], a[..., 3]
mx, mn = rgb.max(2), rgb.min(2)
d = mx - mn
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
# a luminous green pixel: green is the max channel, bright and clearly coloured (the core, vein light, glow rings), not the brown wood
lum = (g >= r * 1.05) & (g >= b) & (mx > .55) & (al > .05)
w = lum.astype(np.float32)
out = a.copy()
for c, (x0, x1) in enumerate(RECTS):
    k = BLOOM[c]
    if k <= 0: continue
    sl = (slice(None), slice(x0, x1))
    m = w[sl]
    # boost: lift the lit pixels toward green-white (their own hue kept: only value / whiteness move)
    white = np.clip((mx[sl] - .55) / .45, 0, 1) * .55 * k
    for ch in range(3):
        base = out[sl][..., ch]
        lifted = base + (1 - base) * white
        out[sl][..., ch] = np.where(m > 0, lifted, base)
    # bloom: a soft green halo (hue 125) from the luminous mask, clipped to the frame rect
    img = Image.fromarray((m * 255).astype(np.uint8))
    halo = np.array(img.filter(ImageFilter.GaussianBlur(9))).astype(np.float32) / 255
    halo = np.clip(halo * 1.8, 0, 1) * k * .75
    cur = out[sl]
    ha = halo * (1 - cur[..., 3])            # only fill where the frame is not already opaque
    col = np.array([.45, 1.0, .38], np.float32)
    na = np.clip(cur[..., 3] + ha, 0, 1)
    for ch in range(3):
        cur[..., ch] = np.where(na > 0, (cur[..., ch] * cur[..., 3] + col[ch] * ha) / np.maximum(na, 1e-4), cur[..., ch])
    cur[..., 3] = na
    out[sl] = cur
Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8), "RGBA").save(sys.argv[2])
print("wrote", sys.argv[2])
