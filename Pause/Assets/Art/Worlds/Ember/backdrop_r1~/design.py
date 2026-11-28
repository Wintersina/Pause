"""Ember r1 DESIGNED WORLD (user: props must sit in sensible places, settlements with roads, no scatter): per layout, a fixed plan of
CLUSTERS along one main road that runs the whole tile (periodic), painted PADS (levelled ash clearings) and ROADS in Ember's own ground,
lava flows out of the volcano, and the prop list (kind, x, y in tile texels).  python3 design.py v1 [v2 v3 v4]
writes tiles/v<n>D_mid.png (the painted tile), tiles/v<n>D_plan.json (placements, pads, roads, flows), ref/plan_v<n>.png (overlay)"""
import sys, json, heapq, math
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, distance_transform_edt as edt, uniform_filter, binary_dilation, label
sys.path.insert(0, '.')
import terrain
HR = terrain.H
TW, TH = 896, 4096; S = 1080 * .98 / 896; ALT = .49 / .41
PX = dict(geoplant=190, foundry=180, miningrig=170, orehauler=190, bunker=160, quarry=170, stilts=170, domes=180, crawler=140, columns=160,
          springs=160, vents=150, starship=300, volcano=350, caldera=360, spires=330)
SZ = {k: v * ALT / S for k, v in PX.items()}                      # drawn cell width in tile texels
LAVA = ('stilts', 'crawler', 'orehauler')
NATURAL = ('volcano', 'caldera', 'spires', 'columns')
def hub(k): return k
TEMPLATES = {      # offsets in texels from the cluster anchor; the hub (road junction) is the anchor
 'outpost':    [('bunker', 0, 0), ('domes', -215, 55), ('geoplant', 225, 20), ('vents', 20, 215)],
 'geothermal': [('springs', 0, 0), ('springs', 205, 70), ('vents', -30, 210), ('geoplant', 235, -150)],
 'works':      [('foundry', 0, 0), ('quarry', 225, 10), ('miningrig', 105, 225)],
 'rocks':      [('columns', 0, 0), ('spires', 250, 40)],
 'volcano':    [('volcano', 0, 0), ('springs', -250, 270), ('vents', 150, 300)],
 'caldera':    [('caldera', 0, 0), ('columns', 230, 280)],
 'starship':   [('starship', 0, 0)],
}
PLANS = {   # slot order down the tile (y), anchor x alternates left / right
 1: ['outpost', 'volcano', 'lavamine', 'works', 'starship'],
 2: ['outpost', 'geothermal', 'works', 'starship'],          # a city of plates: no volcano / caldera on it
 3: ['outpost', 'volcano', 'geothermal', 'works', 'starship'],
 4: ['outpost', 'volcano', 'lavamine', 'works', 'starship'],
}
def pen(t, x, y, r):
    """terrain penalty of a disc (tile texels): relief + lava + near-C"""
    cx, cy = int(x // terrain.CELL), int(y // terrain.CELL) % t['R']; rc = max(1, int(r // terrain.CELL))
    ys = np.arange(cy - rc, cy + rc + 1) % t['R']; xs = np.clip(np.arange(cx - rc, cx + rc + 1), 0, t['C'] - 1)
    sub_rel = t['relb'][np.ix_(ys, xs)]; sub_l = t['lava'][np.ix_(ys, xs)]
    return float(sub_rel.mean() * 10 + sub_l.mean() * 12 + (sub_l > .3).mean() * 20)
def plan(v, seed=5):
    t = terrain.read(f'{HR}tiles/v{v}A_mid.png'); r = np.random.default_rng(seed + v)
    slots = PLANS[v]; n = len(slots); gap = TH / n; out = []; hubs = []
    used = []            # (x, y, rad)
    def free(x, y, rad): return all((x - a) ** 2 + (y - b) ** 2 > (rad + c) ** 2 * .6 for a, b, c in used)
    for i, kind in enumerate(slots):
        yc = gap * (i + .5); best = None
        xs = [260, 400, 560, 660] if i % 2 == 0 else [640, 520, 380, 260]
        if kind == 'lavamine':
            W = t['lava'] > .35; dt = edt(W) * terrain.CELL
            lab, k = label(dt >= 14)
            ys0, ys1 = int((yc - gap * .45) / 8) % t['R'], int((yc + gap * .45) / 8)
            cands = [(dt[y % t['R'], x], x * 8 + 4, (y % t['R']) * 8 + 4) for y in range(ys0, ys1) for x in range(16, t['C'] - 16) if dt[y % t['R'], x] >= 14]
            if not cands: slots[i] = kind = 'works'
            else:
                cands.sort(reverse=True); top = cands[:70]; best3 = None
                for d0, sx, sy in top[:30]:
                    pts_ = [(sx, sy)]; sc_ = -d0 * .5
                    for nm, lo, hi in (('crawler', 150, 250), ('orehauler', 170, 280)):
                        pick = None
                        for d, x, y in top:
                            dd = [math.hypot(x - a, y - b) for a, b in pts_]
                            if min(dd) >= lo and max(dd) <= hi + 90 and 130 < x < 770: pick = (x, y, d); break
                        if pick is None: sc_ += 400; continue
                        pts_.append((pick[0], pick[1])); sc_ += math.hypot(pick[0] - sx, pick[1] - sy) * .3 - pick[2] * .3
                    if len(pts_) == 3 and (best3 is None or sc_ < best3[0]): best3 = (sc_, pts_)
                pts = best3[1] if best3 else [(top[0][1], top[0][2])]
                sx, sy = pts[0]
                names = ['stilts', 'crawler', 'orehauler'][:len(pts)]
                for (x, y), nm in zip(pts, names): out.append(dict(kind=nm, x=int(x), y=int(y), cluster=i))
                bestl = None
                for dx in range(-380, 381, 20):
                    for dy in range(-380, 381, 20):
                        x, y = sx + dx, sy + dy
                        if not (150 < x < 750) or any((x - a) ** 2 + (y - b) ** 2 < 215 ** 2 for a, b in pts): continue
                        sc = pen(t, x, y, SZ['miningrig'] * .38) + abs(math.hypot(dx, dy) - 250) * .006
                        if bestl is None or sc < bestl[0]: bestl = (sc, x, y)
                out.append(dict(kind='miningrig', x=int(bestl[1]), y=int(bestl[2]), cluster=i))
                hubs.append((bestl[1], bestl[2] + 40)); used += [(p['x'], p['y'], SZ[p['kind']] * .4) for p in out if p['cluster'] == i]
                continue
        tpl = [p for p in TEMPLATES[kind] if not (kind == 'works' and p[0] == 'miningrig' and 'lavamine' in PLANS[v])]; rad = max(max(abs(dx), abs(dy)) for _, dx, dy in tpl) + 100
        for x in xs:
            for dy in range(-120, 121, 40):
                ya = yc + dy; sc = 0; ok = True
                for nm, dx, dy2 in tpl:
                    px, py = x + dx, ya + dy2
                    if not (SZ[nm] * .45 + 20 < px < TW - SZ[nm] * .45 - 20): ok = False; break
                    sc += pen(t, px, py, SZ[nm] * (.34 if nm not in NATURAL else .2)) * (1.0 if nm not in NATURAL else .25)
                    if nm == 'volcano': sc += pen(t, px, py, SZ[nm] * .42) * 2.5 + 40 * (uniform_filter(t['lava'], int(SZ[nm] * .5 / 8)).__getitem__((int(py // 8) % t['R'], int(px // 8))) > .08)
                    if not free(px, py, SZ[nm] * .4): ok = False; break
                if ok and (best is None or sc < best[0]): best = (sc, x, ya)
        if best is None: best = (0, 400, yc)
        _, ax, ay = best
        for nm, dx, dy in tpl:
            out.append(dict(kind=nm, x=int(ax + dx), y=int(ay + dy), cluster=i)); used.append((ax + dx, ay + dy, SZ[nm] * .4))
        hubs.append((ax + (60 if kind in ('outpost', 'works') else 0), ay + (110 if kind in ('outpost', 'works') else 60)))
    seen = {}
    for p in out:
        k = (p['cluster'], p['kind']); seen[k] = seen.get(k, 0) + 1
        p['opt'] = bool(p['kind'] in ('vents', 'columns') or (p['kind'] == 'springs' and seen[k] > 1))   # absent on some passes: its pad stays, an empty lot
    return t, out, hubs, slots

# ------------------------------------------------------------------------------------------------ roads (A* on the cost grid, periodic in y)
def astar(cost, a, b):
    R, C = cost.shape; (ay, ax), (by, bx) = a, b
    ay %= R; by %= R
    dist = {(ay, ax): 0.0}; prev = {}; pq = [(0.0, ay, ax)]
    nb = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)]
    while pq:
        d, y, x = heapq.heappop(pq)
        if (y, x) == (by, bx): break
        if d > dist.get((y, x), 1e18): continue
        for dy, dx in nb:
            ny, nx = (y + dy) % R, x + dx
            if not (2 <= nx < C - 2): continue
            step = cost[ny, nx] * (1.414 if dy and dx else 1.0)
            nd = d + step
            if nd < dist.get((ny, nx), 1e18):
                dist[(ny, nx)] = nd; prev[(ny, nx)] = (y, x); heapq.heappush(pq, (nd, ny, nx))
    path = [(by, bx)]
    while path[-1] != (ay, ax): path.append(prev[path[-1]])
    return path[::-1]
def to_poly(path, R, y_ref):
    """cell path -> smoothed polyline in texels, unwrapped near y_ref (no jumps across the tile seam)"""
    pts = []; off = 0; py = None
    for y, x in path:
        if py is not None:
            if y - py > R // 2: off -= R
            elif py - y > R // 2: off += R
        py = y; pts.append(((x + .5) * terrain.CELL, (y + off + .5) * terrain.CELL))
    P = np.array(pts)
    if len(P) > 6:
        k = 3; Q = P.copy()
        for c in range(2): Q[:, c] = np.convolve(np.pad(P[:, c], k, mode='edge'), np.ones(2 * k + 1) / (2 * k + 1), mode='valid')
        Q[0], Q[-1] = P[0], P[-1]; P = Q
    return P
def stampfield(T, t, rng, mode='ash'):
    """a tile-sized texture of Ember's OWN flat ash (or lava): feathered stamps of real patches from the tile, painted over each other"""
    h, w = T.shape[:2]
    R, C = t['R'], t['C']
    def cm(m): return m[:R * 8, :C * 8].reshape(R, 8, C, 8).mean((1, 3))
    if mode == 'ash':
        ok = (t['relb'] < np.quantile(t['relb'], .45)) & (uniform_filter(t['lava'], 5) < .02) & (cm(t['s']) < .34) & (cm(t['v']) > .30) & (cm(t['v']) < .62)
    else:
        ok = (uniform_filter((t['lava'] > .35).astype(float), 3) > .6) & (uniform_filter((t['lava'] > .8).astype(float), 3) < .5)
    ys, xs = np.nonzero(ok)
    print(mode, 'stamp sources', len(ys), 'mean colour', T[ok.repeat(8, 0).repeat(8, 1)[:h, :w]].mean(0).round(2) if len(ys) else None)
    if len(ys) < 20: ok = t['relb'] < np.quantile(t['relb'], .5); ys, xs = np.nonzero(ok)
    P = 40; win = np.outer(np.hanning(P + 2)[1:-1], np.hanning(P + 2)[1:-1])[..., None] ** .6
    mean = T[ok.repeat(8, 0).repeat(8, 1)[:h, :w]].mean(0) if len(ys) else np.array([.35, .3, .26])
    base = np.broadcast_to(mean, (h, w, 3)).copy()
    n = int(h * w / (P * P) * 5)
    for _ in range(n):
        j = rng.integers(len(ys)); cy, cx = ys[j] * 8 + 4, xs[j] * 8 + 4
        y0, x0 = int(np.clip(cy - P // 2, 0, h - P)), int(np.clip(cx - P // 2, 0, w - P)); patch = T[y0:y0 + P, x0:x0 + P]
        ty, tx = int(rng.integers(0, h - P)), int(rng.integers(0, w - P))
        base[ty:ty + P, tx:tx + P] = base[ty:ty + P, tx:tx + P] * (1 - win) + patch * win
    return base
def draw_mask(shape, polys, width, feather=2.2, rng=None, jag=1.5):
    im = Image.new('L', (shape[1], shape[0]), 0); d = ImageDraw.Draw(im)
    for P in polys:
        pts = [(float(x), float((y) % shape[0])) for x, y in P]
        for k in range(len(pts) - 1):
            if abs(pts[k + 1][1] - pts[k][1]) > shape[0] // 2: continue
            w = width(k / max(1, len(pts) - 1)) if callable(width) else width
            d.line([pts[k], pts[k + 1]], fill=255, width=int(round(w)))
            d.ellipse((pts[k][0] - w / 2, pts[k][1] - w / 2, pts[k][0] + w / 2, pts[k][1] + w / 2), fill=255)
    m = np.asarray(im).astype(float) / 255
    if rng is not None and jag:
        from terrain import gaussian_filter as gf
        nz = gaussian_filter(rng.standard_normal(shape), 3); nz = nz / (nz.std() + 1e-9)
        m = gaussian_filter(m, feather) + nz * jag * .05 * (gaussian_filter(m, feather) > .05)
    else: m = gaussian_filter(m, feather)
    return np.clip(m * 1.15 - .08, 0, 1)
def ellipse_mask(shape, cx, cy, rx, ry, feather, rng):
    h, w = shape; y0, y1 = int(max(0, cy - ry - 40)), int(min(h, cy + ry + 40)); x0, x1 = int(max(0, cx - rx - 40)), int(min(w, cx + rx + 40))
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
    nz = gaussian_filter(rng.standard_normal((y1 - y0, x1 - x0)), 7); nz = nz / (nz.std() + 1e-9)
    d = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2) + nz * .07
    m = np.clip((1 - d) * min(rx, ry) / feather, 0, 1)
    return (y0, y1, x0, x1), m
def offset_poly(P, d):
    Q = np.array(P, float); tg = np.gradient(Q, axis=0); n = np.stack([-tg[:, 1], tg[:, 0]], 1); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-6)
    return Q + n * d
def blob(shape, pts_r, feather, rng, jag=.06):
    """union of discs (x, y, r) as a soft mask"""
    h, w = shape; m = np.zeros(shape)
    for x, y, r in pts_r:
        y0, y1, x0, x1 = int(max(0, y - r - 40)), int(min(h, y + r + 40)), int(max(0, x - r - 40)), int(min(w, x + r + 40))
        if y1 <= y0 or x1 <= x0: continue
        yy, xx = np.mgrid[y0:y1, x0:x1].astype(float); nz = gaussian_filter(rng.standard_normal((y1 - y0, x1 - x0)), 8); nz /= (nz.std() + 1e-9)
        d = np.hypot((xx - x) / 1.0, (yy - y) / .78) / r + nz * jag
        m[y0:y1, x0:x1] = np.maximum(m[y0:y1, x0:x1], np.clip((1 - d) * r / feather, 0, 1))
    return m
def paint(v, seed=5):
    t, props, hubs, slots = plan(v, seed); rng = np.random.default_rng(seed * 7 + v)
    T = t['T'].copy(); h, w = T.shape[:2]; R, C = t['R'], t['C']
    ash = stampfield(T, t, rng, 'ash')
    rb = t['relb'] / np.quantile(t['relb'], .9)
    cost = 1 + 9 * np.clip(rb, 0, 1.5) + 90 * (t['lavab'] > .05) + 140 * (t['lava'] > .3)
    roads = []; spur = []
    hubc = [(int(y // 8), int(x // 8)) for x, y in hubs]
    for i in range(len(hubc)):
        a_, b_ = hubc[i], hubc[(i + 1) % len(hubc)]
        if i == len(hubc) - 1: b_ = (b_[0] + R, b_[1])
        roads.append(to_poly(astar(cost, a_, (b_[0], b_[1])), R, hubs[i][1]))
    built = [p for p in props if p['kind'] not in NATURAL and p['kind'] != 'starship']
    for p in built:
        hubp = hubc[p['cluster']] if p['cluster'] < len(hubc) else None
        tgt = (int(p['y'] // 8), int(p['x'] // 8))
        if hubp is None or math.hypot(tgt[0] - hubp[0], tgt[1] - hubp[1]) < 5: continue
        spur.append(to_poly(astar(cost, hubp, tgt), R, p['y']))
    wrap = lambda Ps: [q for P in Ps for q in (P, P + [0, h], P - [0, h])]
    # ---- PLAINS: each settlement stands in a levelled ash plain (soft edge), so it never sits on jagged rock or crack fields
    disc = [(p['x'], p['y'], SZ[p['kind']] * .56) for p in built if p['kind'] not in LAVA] + [(hx, hy, 70) for hx, hy in hubs]
    plain = np.zeros((h, w))
    for dy in (0, h, -h):
        plain = np.maximum(plain, blob((h, w), [(x, y + dy, r) for x, y, r in disc if -200 < y + dy < h + 200], 30, rng))
    plain = gaussian_filter(plain, 3)
    # ---- the VOLCANO'S SKIRT: its base is blended into the terrain: the ground's own dark rock under a wide feathered skirt, ash fans
    #      running out downslope, never over painted structures (the volcano is only placed on open ground)
    def cm(m): return m[:R * 8, :C * 8].reshape(R, 8, C, 8).mean((1, 3))
    okr = (t['lavab'] < .02) & (cm(t['v']) < .30) & (t['relb'] > np.quantile(t['relb'], .35))
    if okr.sum() < 40: okr = (t['lavab'] < .03) & (t['relb'] > np.quantile(t['relb'], .3))
    rys, rxs = np.nonzero(okr); rock = np.broadcast_to(T[okr.repeat(8, 0).repeat(8, 1)[:h, :w]].mean(0), (h, w, 3)).copy(); Pp = 40
    winr = np.outer(np.hanning(Pp + 2)[1:-1], np.hanning(Pp + 2)[1:-1])[..., None] ** .6
    for p in props:
        if p['kind'] != 'volcano': continue
        s_ = SZ['volcano']; vy = p['y'] + s_ * .12
        for dy in (0, h, -h):
            sk = blob((h, w), [(p['x'], vy + dy, s_ * .60)], 70, rng, .10)
            if sk.max() <= 0: continue
            ys0, ys1 = int(max(0, vy + dy - s_)), int(min(h, vy + dy + s_)); xs0, xs1 = int(max(0, p['x'] - s_)), int(min(w, p['x'] + s_))
            for _ in range(int((ys1 - ys0) * (xs1 - xs0) / (Pp * Pp) * 6)):
                j = rng.integers(len(rys)); cy, cx = rys[j] * 8 + 4, rxs[j] * 8 + 4
                y0, x0 = int(np.clip(cy - Pp // 2, 0, h - Pp)), int(np.clip(cx - Pp // 2, 0, w - Pp))
                ty, tx = int(rng.integers(ys0, max(ys0 + 1, ys1 - Pp))), int(rng.integers(xs0, max(xs0 + 1, xs1 - Pp)))
                rock[ty:ty + Pp, tx:tx + Pp] = rock[ty:ty + Pp, tx:tx + Pp] * (1 - winr) + T[y0:y0 + Pp, x0:x0 + Pp] * winr
            k3 = (sk * .92)[..., None]; out_sk = k3
            T_sk = T * (1 - k3) + rock * k3
            # ash fans: three lobes of the packed ash running downslope (+y) from the foot, feathered
            fan = np.zeros((h, w))
            for ang in (70, 90, 112):
                for L_, wd in ((s_ * .5, 38), (s_ * .38, 26)):
                    x1 = p['x'] + math.cos(math.radians(ang)) * L_; y1 = vy + dy + s_ * .20 + math.sin(math.radians(ang)) * L_
                    fan = np.maximum(fan, draw_mask((h, w), [np.array([[p['x'], vy + dy + s_ * .18], [x1, y1]])], lambda u, wd=wd: wd * (1 - .7 * u), 12, rng, 2.5) * .55)
            f3 = fan[..., None]
            T_sk = T_sk * (1 - f3) + ash * 1.02 * f3
            T = T_sk
    T = T * (1 - plain[..., None] * .0) + 0
    # ---- roads: packed ash, crisp edge, shoulder, wheel ruts
    rm = np.zeros((h, w)); rim = np.zeros((h, w)); ruts = np.zeros((h, w))
    for Ps, wd in ((roads, 24), (spur, 16)):
        for P in Ps:
            q = wrap([P]); rm = np.maximum(rm, draw_mask((h, w), q, lambda u, wd=wd: wd, 1.1, rng, .8))
            rim = np.maximum(rim, draw_mask((h, w), q, lambda u, wd=wd: wd + 7, 2.0, rng, .8))
            for off in (-wd * .24, wd * .24):
                ruts = np.maximum(ruts, draw_mask((h, w), wrap([offset_poly(P, off)]), lambda u: 2.4, .7, rng, .0))
    pm = np.zeros((h, w)); pads = []
    for p in built:
        if p['kind'] in LAVA: continue
        s_ = SZ[p['kind']]; cx, cy = p['x'], p['y'] + s_ * .05
        pm = np.maximum(pm, blob((h, w), [(cx, cy, s_ * .50)], 3.5, rng, .035)); pads.append(dict(kind=p['kind'], x=cx, y=cy, r=s_ * .5))
    # ---- compose: plain (flat ash, 78 % so some of the ground's own texture stays), pad (dark levelled pad with a packed-gravel rim), road
    pl3 = plain[..., None] * .8
    out = T * (1 - pl3) + ash * pl3
    shoulder = np.clip(rim - rm, 0, 1)[..., None]
    out = out * (1 - shoulder * .35) + ash * .78 * shoulder * .35          # a darker packed shoulder either side of the road
    road_c = ash * 1.28
    m3 = np.clip(rm, 0, 1)[..., None]; out = out * (1 - m3) + road_c * m3
    out = out * (1 - ruts[..., None] * m3 * .55) + (road_c * .74) * (ruts[..., None] * m3 * .55)
    pe = np.clip(pm, 0, 1)
    inner = gaussian_filter(pe, 4) > .98                                    # the pad's flat middle
    padc = ash * .66 + np.array([.012, .010, .010])
    rimb = np.clip(pe - np.clip((gaussian_filter(pe, 3.5) - .5) * 4, 0, 1), 0, 1)
    from scipy.ndimage import binary_erosion
    core = binary_erosion(pe > .5, iterations=5)
    edge = ((pe > .5) & ~core).astype(float); edge = gaussian_filter(edge, 1.0)[..., None]
    p3 = pe[..., None]
    out = out * (1 - p3) + padc * p3
    out = out * (1 - edge * .8) + ash * 1.3 * edge * .8                      # the lighter packed-gravel kerb round the pad
    outer = np.clip(gaussian_filter(pe, 2.5) - pe, 0, 1)[..., None]
    out = out * (1 - outer * .9 * .5) + out * .55 * outer * .9 * .5          # its short cast shadow
    # pebbles along the kerbs and the road shoulders
    ed = (gaussian_filter(np.maximum(rm, pe), 1.5) > .3) & (gaussian_filter(np.maximum(rm, pe), 1.5) < .7)
    yy, xx = np.nonzero(ed & (rng.random((h, w)) < .05))
    for y, x in zip(yy, xx):
        rr = rng.uniform(.9, 2.2); y0, y1, x0, x1 = int(max(0, y - 3)), int(min(h, y + 4)), int(max(0, x - 3)), int(min(w, x + 4))
        gy, gx = np.mgrid[y0:y1, x0:x1]; mm = ((gx - x) ** 2 + (gy - y) ** 2 <= rr * rr)[..., None]
        col = np.array([.16, .13, .11]) if rng.random() < .65 else np.array([.46, .40, .33])
        out[y0:y1, x0:x1] = np.where(mm, col, out[y0:y1, x0:x1])
    # ---- the volcano's lava flows: A* downhill-ish (cheapest ground) to a pool, never across a pad or road
    lavatex = stampfield(T, t, rng, 'lava'); flows = []
    blockc = cost.copy(); bm = np.maximum(np.maximum(rm, pe), plain > .3)
    blockc += 400 * (bm[:R * 8, :C * 8].reshape(R, 8, C, 8).mean((1, 3)) > .15)
    pools = np.array([[x, y] for x, y in zip(*np.nonzero(edt(t['lava'] > .35) * 8 >= 22))[::-1]]) if False else None
    for p in props:
        if p['kind'] != 'volcano': continue
        sx, sy = p['x'] - 20, p['y'] + SZ['volcano'] * .30
        # target: the lava pool 700-1100 texels further down the tile (a real one), else a painted one
        dtp = edt(t['lava'] > .35) * 8; best = None
        for yy_ in range(int((sy + 650) / 8), int((sy + 1150) / 8), 3):
            for xx_ in range(10, C - 10, 3):
                if dtp[yy_ % R, xx_] >= 14 and not bm[(yy_ % R) * 8 + 4, xx_ * 8 + 4] > .15:
                    sc = abs(xx_ * 8 - sx) * .3 + abs(yy_ * 8 - sy - 850) * .1
                    if best is None or sc < best[0]: best = (sc, yy_, xx_)
        ty, tx = (best[1], best[2]) if best else (int((sy + 850) / 8), int(min(max(sx / 8 + 20, 20), C - 20)))
        path = astar(blockc * (1 + 0 * cost), (int(sy // 8), int(sx // 8)), (ty, tx))
        P = to_poly(path, R, sy)
        # the cooled / dull head: wide + hot near the vent, the pool at its end
        flows.append(P.tolist())
        n = len(P); u = np.linspace(0, 1, n)
        fm = draw_mask((h, w), [P], lambda uu: 24 - 8 * uu, 1.6, rng, 1.5)
        hotk = np.clip(1.15 - u * .8, .35, 1)
        im = Image.new('L', (w, h), 0); dd = ImageDraw.Draw(im)
        for k in range(n - 1): dd.line([tuple(P[k]), tuple(P[k + 1])], fill=int(255 * hotk[k]), width=26)
        hot = gaussian_filter(np.asarray(im).astype(float) / 255, 2.0)
        edgec = np.clip(gaussian_filter(fm, 4) * 1.3 - fm, 0, 1)[..., None]
        out = out * (1 - edgec * .55)                                           # dark crust bank
        lt = lavatex * (.62 + .5 * hot[..., None])
        fmm = np.clip(fm * 1.25, 0, 1)[..., None]
        out = out * (1 - fmm) + lt * fmm
        pool = blob((h, w), [(P[-1][0], P[-1][1] % h, 46)], 6, rng, .12)
        pool = np.maximum(pool, blob((h, w), [(P[-1][0], P[-1][1] % h + h, 46)], 6, rng, .12)[:, :]) if P[-1][1] > h - 100 else pool
        pc = pool[..., None]; out = out * (1 - .5 * np.clip(gaussian_filter(pool, 6) * 1.4 - pool, 0, 1)[..., None]); out = out * (1 - pc) + lavatex * 1.05 * pc
    out = np.clip(out, 0, 1)
    return t, props, hubs, roads, spur, pads, flows, out
def run(v):
    t, props, hubs, roads, spur, pads, flows, out = paint(v)
    Image.fromarray((out * 255 + .5).astype(np.uint8)).save(f'{HR}tiles/v{v}D_mid.png')
    json.dump(dict(props=props, hubs=[list(map(float, h)) for h in hubs], roads=[r.tolist() for r in roads], spurs=[r.tolist() for r in spur], pads=pads, flows=flows),
              open(f'{HR}tiles/v{v}D_plan.json', 'w'))
    # overlay
    im = Image.fromarray((out * 255).astype(np.uint8)).resize((448, 2048)); d = ImageDraw.Draw(im)
    for p in props:
        r_ = SZ[p['kind']] * .25 / 2; c = (255, 255, 0) if p['kind'] not in LAVA else (0, 255, 255)
        d.ellipse((p['x'] / 2 - r_, p['y'] / 2 - r_, p['x'] / 2 + r_, p['y'] / 2 + r_), outline=c, width=2); d.text((p['x'] / 2 - 18, p['y'] / 2 - 6), p['kind'][:6], fill=(255, 255, 255))
    im.save(f'{HR}ref/plan_v{v}.png'); print('v', v, 'painted', len(props), 'props', len(roads), 'main legs', len(spur), 'spurs')
if __name__ == '__main__':
    for a in sys.argv[1:]: run(int(a[1]))
