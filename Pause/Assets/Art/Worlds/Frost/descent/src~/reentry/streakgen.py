# Speed streaks for the planetfall side strips (1024 x 2048, tiles vertically;
# Planetfall.Streak shows the outer 30% of each side, additive, tinted
# Lerp(cue, white, .35), scrolling DOWN ~1.5 tiles/s). Air rushing past a
# ship climbing up the screen: motion-blurred droplets / vapour, each line
# brightest at its LEADING (bottom) end and fading up its tail; thin soft
# lines plus a few wide faint vapour ribbons; denser toward the screen edges.
# usage: python3 streakgen.py <world|all> <tag> [seed]
import sys, os, math
import numpy as np
from PIL import Image

TW, TH = 1024, 2048
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'out2')
TINT = {   # near white, a whisper of the world (the code tints it with the cue again)
    'frost': (225, 240, 255), 'verdant': (240, 255, 225), 'ember': (255, 236, 215),
    'tide': (228, 255, 246), 'storm': (230, 236, 255), 'desert': (255, 246, 228),
}

def gen(world, seed=5):
    r = np.random.default_rng(seed)
    acc = np.zeros((TH, TW))          # additive light (premultiplied, grey)
    yy_full = np.arange(TH)

    def put(x, y_head, length, sigma, bright, kind):
        # draw with vertical wrap: the line occupies rows y_head-length .. y_head+4
        x0, x1 = int(max(0, x - 4 * sigma - 2)), int(min(TW, x + 4 * sigma + 3))
        if x1 <= x0: return
        xs = np.arange(x0, x1, dtype=float)
        rows = np.arange(int(y_head - length - 6), int(y_head + 8))
        t = np.clip((y_head - rows) / length, -1, 1)          # 0 head .. 1 tail end
        if kind == 'line':
            along = np.where(t >= 0, (1 - t) ** 1.7 * np.clip(t * length / 18.0, 0, 1) ** .5, 0) + np.where(t < 0, np.exp(-(t * length / 4.0) ** 2) * 0, 0)
            wid = sigma * (0.45 + 0.55 * (1 - np.clip(t, 0, 1)))  # tapers to the tail
        else:
            ph = r.uniform(0, 6.28)
            along = np.where(t >= 0, np.sin(np.pi * np.clip(t, 0, 1)) ** .8 * (.65 + .35 * np.sin(t * 9 + ph)), 0)
            wid = sigma * (0.7 + 0.3 * np.sin(np.pi * np.clip(t, 0, 1)))
        drift = (np.clip(t, 0, 1) * length) * r.uniform(-.012, .012)   # a hair of slant
        prof = np.exp(-((xs[None, :] - x - drift[:, None]) / wid[:, None]) ** 2)
        val = bright * along[:, None] * prof
        rr = np.mod(rows, TH)
        np.add.at(acc, (rr[:, None], np.arange(x0, x1)[None, :]), val)

    def edge_x():
        # density rises toward both outer edges (what the strips show)
        while True:
            x = r.uniform(0, TW)
            e = abs(x - TW / 2) / (TW / 2)
            if r.random() < .12 + .88 * e ** 1.6: return x
    # vapour ribbons: torn cloud wisps whipping past (the main read)
    for _ in range(46):
        put(edge_x(), r.uniform(0, TH), r.uniform(500, 1500), r.uniform(8, 26), r.uniform(.05, .13), 'vapour')
    # soft speed lines: long comet tails, brightest toward the leading (bottom) end
    for _ in range(120):
        L = r.uniform(300, 1100)
        put(edge_x(), r.uniform(0, TH), L, r.uniform(1.2, 2.6), r.uniform(.12, .42), 'line')
    # a few brighter droplets / ice motes with short tails
    for _ in range(30):
        put(edge_x(), r.uniform(0, TH), r.uniform(60, 180), r.uniform(1.4, 2.4), r.uniform(.35, .7), 'line')
    a = 1 - np.exp(-acc * 1.25)                # soft saturation
    tint = np.array(TINT[world]) / 255.
    # hot cores whiter, tails take the tint
    core = np.clip(acc - .6, 0, 1)[..., None]
    rgb = tint[None, None, :] * (1 - core) + core
    img = np.concatenate([np.clip(rgb, 0, 1), a[..., None]], -1)
    return img

if __name__ == '__main__':
    worlds = list(TINT) if sys.argv[1] == 'all' else [sys.argv[1]]
    tag = sys.argv[2]; seed = int(sys.argv[3]) if len(sys.argv) > 3 else 5
    for i, w in enumerate(worlds):
        img = gen(w, seed + 31 * i)
        p = f'{OUT}/bd_{w}_entry_streaks/{tag}.png'
        os.makedirs(os.path.dirname(p), exist_ok=True)
        Image.fromarray(np.round(img * 255).astype(np.uint8), 'RGBA').save(p, optimize=True)
        print(w, p, flush=True)
