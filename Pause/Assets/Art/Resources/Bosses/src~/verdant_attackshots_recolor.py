# Recolours Verdant_attackshots.png from src~/Verdant_attackshots_orig.png: neon chartreuse on the edges and glints, painted
# detail kept inside. The ORIGINAL luminance drives a value ramp (deep jungle green -> leaf green -> neon chartreuse), so the
# vine strands / thorns, spore puffs and sap-drop highlight + bubbles keep their form; an edge weight (distance to the
# silhouette) lifts the outer pixels toward neon so the shape separates from the dark canopy. No outline, no rim, no pink/amber.
# Frame b (right column) shifts a little toward yellow and moves a white sap glint, so the 2-frame flicker still shimmers.
import sys, numpy as np
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGBA')).astype(float)
out = a.copy()
stops = [(0, (18, 66, 13)), (.28, (46, 134, 15)), (.55, (120, 220, 18)), (.8, (190, 255, 40)), (1, (232, 255, 130))]
shift = [(0, 0, 0), (22, 4, -8)]
glint = [.28, .72]
def edge_dist(m, n=6):
    d = np.zeros(m.shape); cur = m.copy()
    for i in range(n):
        d += cur
        p = np.pad(cur, 1)
        cur = cur & p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:]
    return d    # 0 outside, 1 = outermost pixel ... n = deep inside
for r in range(a.shape[0] // 128):
    for f in range(2):
        y0, x0 = r * 128, f * 128
        cell = a[y0:y0 + 128, x0:x0 + 128]
        rgb, al = cell[..., :3] / 255, cell[..., 3]
        Y = .299 * rgb[..., 0] + .587 * rgb[..., 1] + .114 * rgb[..., 2]
        m = al > 40
        t = np.clip((Y - .07) / .56, 0, 1) ** 1.15
        e = np.clip(1 - (edge_dist(m) - 1) / 2.6, 0, 1)        # 1 on the outer ring of pixels, 0 from ~4 px in
        t = np.clip(t * .88 + e * .3, 0, 1)
        col = np.stack([np.interp(t, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
        col = col + np.array(shift[f])
        if m.any():
            ys = np.where(m.any(1))[0]; top, bot = ys[0], ys[-1]
            yy = (np.arange(128)[:, None] - top) / max(1, bot - top)
            g = np.exp(-((yy - glint[f]) / .07) ** 2) * (.4 if r == 0 else .22) * m
            col = col * (1 - g[..., None]) + np.array([245, 255, 190]) * g[..., None]
        # the ORIGINAL drawing's own dark keyline pixels (silhouette-edge pixels it painted dark) go back under the neon, as deep
        # jungle green (their own value, hue pulled off the old brown/amber): not an added border, the art's baked edge
        p = np.pad(al, 1, constant_values=0)
        border = (al >= 200) & ((p[1:-1, :-2] < 40) | (p[1:-1, 2:] < 40) | (p[:-2, 1:-1] < 40) | (p[2:, 1:-1] < 40))
        keep = border & (Y < .32)
        k = np.clip(Y / .32, 0, 1)[..., None]
        dk = np.array([10, 40, 10]) + k * np.array([22, 52, 10])
        col = np.where(keep[..., None], dk, col)
        out[y0:y0 + 128, x0:x0 + 128, :3] = np.clip(col, 0, 255)
Image.fromarray(out.round().astype(np.uint8), 'RGBA').save(dst)
