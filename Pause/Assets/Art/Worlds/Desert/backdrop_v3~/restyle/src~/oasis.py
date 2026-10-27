"""Pixel-art oasis stamps drawn on the 4 px cell grid (after the pixel pass), so they match the tile's pixel size exactly.
Each oasis: a small irregular pocket of teal water with 3 tones (deep, mid, glint) and a wet-sand rim, a few dark-green date palms
seen from above (star-shaped crowns with light tips and a hard cast shadow to the lower right of the key light), reeds and scrub dots.
No outlines. Placed only where the tile is smooth sand (wadi bends / mesa feet) and never in the central lane."""
import numpy as np

WATER = [(20, 74, 82), (30, 100, 104), (48, 130, 124), (120, 184, 164)]      # deep, mid, light, glint
WET = [(96, 72, 44), (140, 108, 66)]                                          # dark wet rim, damp sand
PALM = [(20, 52, 30), (34, 80, 42), (58, 112, 54), (104, 156, 76)]            # shadow, body, lit, tip
SCRUB = [(40, 70, 36), (74, 100, 48)]


def pick_sites(cells: np.ndarray, n: int, rng, rad=17, lane=(.30, .70)):
    """cells: (h, w, 3) float; choose n centres: smooth (low local variance), outside the lane, mutually far apart (torus)."""
    h, w = cells.shape[:2]
    lum = cells.mean(-1)
    gx = np.abs(lum - np.roll(lum, 1, 1)); gy = np.abs(lum - np.roll(lum, 1, 0))
    t = gx + gy
    from numpy.lib.stride_tricks import sliding_window_view
    k = (rad | 1)
    pad = np.pad(t, k // 2, mode="wrap")
    win = sliding_window_view(pad, (k, k)).mean((-1, -2))
    xs = (np.arange(w) + .5) / w
    ok = ((xs < lane[0] - .06) | (xs > lane[1] + .06))[None, :] & (np.arange(h)[:, None] > 10) & (np.arange(h)[:, None] < h - 10)
    score = np.where(ok, win, 1e9)
    sites = []
    yy, xx = np.mgrid[0:h, 0:w]
    for i in range(n):
        band = (yy >= i * h // n + 12) & (yy < (i + 1) * h // n - 12)
        left = (i % 2 == 0)
        side = ((xx / w < lane[0] - .06) & (xx / w > .09)) if left else ((xx / w > lane[1] + .06) & (xx / w < .91))
        sc = np.where(band & side & (ok), win, 1e9)
        y, x = np.unravel_index(np.argmin(sc), sc.shape)
        sites.append((int(y), int(x)))
    return sites


def stamp(cells: np.ndarray, cy: int, cx: int, rng, size=1.0):
    """Draw one oasis centred on cell (cy, cx) into cells (uint8 h, w, 3); returns the footprint cell mask (water + palms + rim)."""
    h, w = cells.shape[:2]
    R = int(round(11 * size))
    yy, xx = np.mgrid[-R - 6:R + 7, -R - 6:R + 7]
    ang = np.arctan2(yy, xx)
    wob = 1 + .22 * np.sin(ang * 2 + rng.random() * 6) + .14 * np.sin(ang * 3 + rng.random() * 6) + .08 * np.sin(ang * 5 + rng.random() * 6)
    squash = .72 + rng.random() * .2
    d = np.sqrt(xx ** 2 + (yy / squash) ** 2) / (R * wob)
    out = {}

    def put(py, px, col):
        out[(py % h, px % w)] = col
    foot = np.zeros((h, w), bool)
    # wet-sand apron, damp ring, water
    for iy in range(yy.shape[0]):
        for ix in range(yy.shape[1]):
            dv = d[iy, ix]
            y, x = cy + yy[iy, ix], cx + xx[iy, ix]
            if dv < .62:
                tone = 0 if dv > .5 else (1 if dv > .22 or rng.random() < .35 else 2)
                if dv < .5 and (iy + 2 * ix) % 7 == 0 and dv < .4 and yy[iy, ix] < 0:
                    tone = 2                                   # sparse light ripples on the upper (lit) half
                if dv < .5 and (ix - iy) % 11 == 0 and xx[iy, ix] < -1 and yy[iy, ix] < 0 and dv < .35:
                    tone = 3                                   # glint
                put(y, x, WATER[tone]); foot[y % h, x % w] = True
            elif dv < .78:
                put(y, x, WET[0] if dv < .70 else WET[1]); foot[y % h, x % w] = True
            elif dv < .98 and rng.random() < .55 * (1.05 - dv) * 2:
                put(y, x, WET[1]); foot[y % h, x % w] = True
    # palms: crowns on the shore ring, 3-5, each a 5-arm star of fronds with a hard shadow down-right
    n = int(rng.integers(4, 7))
    a0 = rng.random() * 6.28
    palms = []
    for i in range(n):
        a = a0 + i * 6.28 / n + rng.uniform(-.5, .5)
        r = R * (.78 + rng.random() * .2)
        palms.append((cy + int(round(np.sin(a) * r * squash)), cx + int(round(np.cos(a) * r)), int(rng.integers(4, 6))))
    for py, px, L in palms:
        for dy, dx in [(1, 1), (2, 1), (1, 2), (2, 2), (3, 2), (2, 3)]:                  # cast shadow
            if (py + dy) % h and True:
                out.setdefault(((py + dy) % h, (px + dx) % w), SCRUB[0] if False else (52, 42, 26))
        phase = rng.random() * 6.28
        for arm in range(6):
            a = phase + arm * 6.28 / 6
            for s in range(0, L + 1):
                y = py + int(round(np.sin(a) * s * .85)); x = px + int(round(np.cos(a) * s))
                col = PALM[3] if s == L else PALM[2] if (s >= 2 and np.cos(a) + np.sin(a) < .3) else PALM[1]
                put(y, x, col)
                if s <= 1: put(y + 1 if s else y, x, PALM[1])
        put(py, px, PALM[0]); foot[py % h, px % w] = True
        for dy in range(-L, L + 1):
            for dx in range(-L, L + 1): foot[(py + dy) % h, (px + dx) % w] = True
    # reeds and scrub: sparse green dots hugging the rim, and a few stalks
    for _ in range(int(14 * size)):
        a = rng.random() * 6.28
        r = R * (.62 + rng.random() * .5)
        y = cy + int(round(np.sin(a) * r * squash)); x = cx + int(round(np.cos(a) * r))
        if (y % h, x % w) in out and out[(y % h, x % w)] in WATER: continue
        put(y, x, SCRUB[int(rng.integers(0, 2))]); foot[y % h, x % w] = True
        if rng.random() < .5: put(y - 1, x, SCRUB[1])
    for (y, x), c in out.items():
        cells[y, x] = c
    return foot


def add_oases(rgb: np.ndarray, cell: int = 4, n: int = 4, seed: int = 11, sites=None):
    """rgb: pixelated (H, W, 3) uint8 on the cell grid. Returns (rgb with oases, footprint mask at full res, sites)."""
    rng = np.random.default_rng(seed)
    h, w = rgb.shape[0] // cell, rgb.shape[1] // cell
    cells = rgb[::cell, ::cell].copy()
    foot = np.zeros((h, w), bool)
    if sites is None:
        sites = pick_sites(cells.astype(float), n, rng)
    for i, (cy, cx) in enumerate(sites):
        foot |= stamp(cells, cy, cx, rng, size=[1.0, .85, 1.1, .9, 1.0][i % 5])
    out = np.repeat(np.repeat(cells, cell, 0), cell, 1)
    return out, np.repeat(np.repeat(foot, cell, 0), cell, 1), sites
