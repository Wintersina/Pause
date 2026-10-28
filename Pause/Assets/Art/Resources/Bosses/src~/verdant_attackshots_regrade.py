# Regrades Verdant_attackshots.png from src~/Verdant_attackshots_orig.png (2026-10-27, "the three jungle shots are hard to see").
#
# Why the old file read badly: (1) the bright jungle backdrops (mint mist, mean luminance ~.35) leave only a near-black shape able to hold 3:1,
# and the old shots were lime / mid-green on lime / mid-green (the thorn was brown-black but small); (2) the two frames of every shot were
# drawn 40-44 px apart inside their 128 px cells (the drawing sat right of centre in frame a and left of centre in frame b), so the shot
# jumped ~0.2 u sideways twelve times a second: the "wiggle". Both are fixed here, no outline or border added:
#   * every drawing is re-centred on its cell's centre (the hit centre) and grown 1.15-1.3x about it (the hitbox is untouched);
#   * frame b has EXACTLY frame a's silhouette: only the hot core flows one step down the shot (the pulse), so nothing shifts;
#   * value structure: a near-black body (L < .03) carrying a saturated hot core that runs amber -> yellow -> near-white; thorn amber,
#     spore acid yellow-lime (the world's cue), gourd orange. No pink anywhere.
import sys
import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGBA')).astype(float)
out = np.zeros_like(a)
GROW = {0: 1.3, 1: 1.25, 2: 1.15}   # per shot: the thorn is the smallest drawing
DOWN = 3          # frame b: the core flows this many px down the shot

ink = {0: (7, 9, 6), 1: (6, 12, 7), 2: (8, 11, 6)}
hot = {   # a short saturated edge into a bright yellow-cream core: mid tones are the ones a bright backdrop swallows, so the ramp climbs fast
    0: [(0, (255, 110, 10)), (.12, (255, 175, 30)), (.4, (255, 225, 90)), (1, (255, 252, 205))],   # thorn: amber -> cream
    1: [(0, (120, 200, 10)), (.12, (190, 245, 30)), (.4, (240, 255, 110)), (1, (255, 255, 225))],  # spore: acid lime-yellow
    2: [(0, (255, 90, 10)), (.12, (255, 150, 25)), (.4, (255, 215, 80)), (1, (255, 250, 200))],    # gourd: orange flame
}
# fraction of the shot's luminance range (by rank) that is hot core; the rest is the dark body
HOT_SHARE = {0: .38, 1: .42, 2: .42}


def edge_dist(m, n=7):
    d = np.zeros(m.shape); cur = m.copy()
    for i in range(n):
        d += cur
        p = np.pad(cur, 1)
        cur = cur & p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:]
    return d


def grow(arr, s):
    """scale a 128x128 float array about the cell centre (bilinear)."""
    im = Image.fromarray(arr.astype(np.float32), 'F')
    big = im.resize((int(round(128 * s)),) * 2, Image.BICUBIC)
    b = np.asarray(big)
    o = (b.shape[0] - 128) // 2
    return b[o:o + 128, o:o + 128]


for r in range(3):
    cell = a[r * 128:(r + 1) * 128, 0:128]
    al = cell[..., 3]
    m0 = al > 40
    rgb = cell[..., :3] / 255
    Y = .299 * rgb[..., 0] + .587 * rgb[..., 1] + .114 * rgb[..., 2]
    # re-centre on the mass centroid (x) and the bbox middle (y)
    ys, xs = np.where(m0)
    sx = 64 - int(round(np.average(xs, weights=Y[m0] + .05)))
    sy = 64 - int(round((ys.min() + ys.max()) / 2))
    Y = np.roll(np.roll(Y, sx, 1), sy, 0); al = np.roll(np.roll(al, sx, 1), sy, 0)
    Yg = grow(Y, GROW[r]); Ag = grow(al, GROW[r])
    m = Ag > 110
    ed = edge_dist(m)
    # normalised luminance rank inside the silhouette, the outer rim of pixels pushed toward the dark body
    def tmap(Yf, shift=0):
        Yf = np.roll(Yf, shift, 0) if shift else Yf
        v = Yf[m]
        lo, hi = np.percentile(v, 8), np.percentile(v, 99)
        t = np.clip((Yf - lo) / max(1e-3, hi - lo), 0, 1)
        t = t - np.clip(1 - (ed - 1) / 2.0, 0, 1) * .45          # the outer two pixels fall to the body
        return np.clip(t, 0, 1)
    for f in range(2):
        t = tmap(Yg, DOWN * f)
        cut = np.percentile(t[m], 100 * (1 - HOT_SHARE[r])) - (.05 if f else 0)
        h = np.clip((t - cut) / max(1e-3, 1 - cut), 0, 1)           # 0 = body, 1 = hottest
        body = np.array(ink[r]) + (t[..., None] * np.array([14, 16, 8]))   # slight form shading inside the dark body
        stops = hot[r]
        hc = np.stack([np.interp(h, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
        # a sharp step into the hot core (pixel art: two or three tones, no soft airbrush)
        hot_on = (t > cut)[..., None]
        col = np.where(hot_on, hc, body)
        # the frame-b pulse: the hottest pixels go whiter
        if f:
            w = np.clip((h - .55) / .45, 0, 1)[..., None] * (t > cut)[..., None]
            col = col * (1 - .35 * w) + np.array([255, 255, 225]) * .35 * w
        px = np.zeros((128, 128, 4))
        px[..., :3] = np.clip(col, 0, 255)
        px[..., 3] = np.where(m, 255, 0)
        px[~m, :3] = 0
        out[r * 128:(r + 1) * 128, f * 128:(f + 1) * 128] = px
Image.fromarray(out.round().astype(np.uint8), 'RGBA').save(dst)
