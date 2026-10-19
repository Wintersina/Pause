# Recolours Verdant_attackshots.png (reads src~/Verdant_attackshots_orig.png) so the shots read over the dark jungle backdrop:
# a bright, saturated chartreuse -> pollen-white ramp keyed to the old luminance (no outline, no dark rim: the old baked keyline
# becomes body colour). Frame b (right column) differs from frame a: a white sap glint rides to a different place along the
# shot and the body shifts a little toward yellow, so the 2-frame flicker (BossArt.ShotTicks) is a visible shimmer.
# Alpha, cell grid and anchors untouched.
import sys, numpy as np
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGBA')).astype(float)
out = a.copy()
stops = [(0, (96, 205, 8)), (.45, (176, 252, 24)), (.8, (236, 255, 120)), (1, (255, 255, 232))]
shift = [(0, 0, 0), (28, 4, -10)]            # frame b: a touch more yellow
glint = [.28, .72]                           # frame a / b: where the sap glint sits along the shot (0 = tail, 1 = tip)
for r in range(a.shape[0] // 128):
    for f in range(2):
        y0, x0 = r * 128, f * 128
        cell = a[y0:y0 + 128, x0:x0 + 128]
        rgb, al = cell[..., :3] / 255, cell[..., 3]
        Y = .299 * rgb[..., 0] + .587 * rgb[..., 1] + .114 * rgb[..., 2]
        t = np.clip((Y - .04) / .70, 0, 1)
        col = np.stack([np.interp(t, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
        col = col + np.array(shift[f])
        m = al > 8
        if m.any():
            ys = np.where(m.any(1))[0]; top, bot = ys[0], ys[-1]
            yy = (np.arange(128)[:, None] - top) / max(1, bot - top)
            g = np.exp(-((yy - glint[f]) / .09) ** 2) * (.85 if r == 0 else .6)
            col = col * (1 - g[..., None]) + 255 * g[..., None]
        out[y0:y0 + 128, x0:x0 + 128, :3] = np.clip(col, 0, 255)
Image.fromarray(out.round().astype(np.uint8), 'RGBA').save(dst)
