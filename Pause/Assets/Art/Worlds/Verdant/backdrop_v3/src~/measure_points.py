#!/usr/bin/env python3
"""Measures, on the pixels, where every Verdant ambient loop attaches.

Writes points.json (a JsonUtility list, read at runtime by VerdantAmbientCatalog / GroundPlacement
as a TextAsset): for each piece cell of landmarks / pipes / fires / sites

    box      opaque bounding box (x0, y0, x1, y1), cell px from the top-left
    emit     [loop, x, y, scale] ambient loops at MEASURED points
    ends     pipe-end points (flange faces), pipes only

and for each loop its own anchor (the point of its 256 cell placed ON the
emitter point): the plume / flame base for rising loops (bottom-centre of
the opaque art, which the run C art aligned to (128, 236)), the lamp head for
beacons, the spout for the sap leak.

How the points are found (no eyeballing):
  stacks  the column-top profile t(x) of the opaque art; a "spire" is a run
          of columns rising > 18 px above the median top of its neighbours
          (+-30 px). A spire >= 7 px wide, >= 5 px wide at its very top, whose top 10 rows are rust / grey
          (not foliage green) is a chimney: the emitter is the centre of its
          mouth (the dark ellipse just under the rim; rim + 3 px if none).
          A narrower spire is a mast: its lamp knob (brightest warm pixel in
          its top 8 rows) carries a beacon.
  fires   "hot" pixels (hue 8-50 deg, sat > .55, value in the top 15% of the
          piece's orange pixels, >= .4), labelled into
          connected blobs; flames sit on the hottest pixel of the biggest
          blobs (burn fronts: three points spread along the front's
          principal axis, each snapped to the hottest pixel within 8 px).
  leaks   the lime sap (g > .55, g - r > .25, g - b > .3): the rupture is
          the top of the biggest sap blob.
  domes   the apex of each dome = top of the pale-green glass blobs.
  pipes   each nominal end from the run B manifest is pushed out along its
          axis to the last opaque pixel of the flange.

    python3 src~/measure_points.py <atlas dir (Resources/Worlds/Verdant/Backdrop3)> <out json> [--debug DIR]
(the run B manifest, src~/manifest.json, gives the nominal pipe ends)
"""
import sys, os, json, colorsys
import numpy as np
from PIL import Image, ImageDraw

def cells(base, atlas):
    im = np.asarray(Image.open(os.path.join(base, atlas + '.png')).convert('RGBA')).astype(np.float64) / 255
    H = im.shape[0]
    out = {}
    for s in json.load(open(os.path.join(base, atlas + '.json')))['sprites']:
        y0 = H - s['y'] - s['h']
        out[s['n']] = im[y0:y0 + s['h'], s['x']:s['x'] + s['w']]
    return out

def hsv(c):
    mx = c[..., :3].max(-1); mn = c[..., :3].min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, s, mx

def box(c):
    ys, xs = np.nonzero(c[..., 3] > .5)
    return [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]

def label(mask):
    # 4-connected components (small images: plain flood fill)
    lab = np.zeros(mask.shape, np.int32); n = 0
    H, W = mask.shape
    for y, x in zip(*np.nonzero(mask)):
        if lab[y, x]: continue
        n += 1; stack = [(y, x)]; lab[y, x] = n
        while stack:
            cy, cx = stack.pop()
            for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and not lab[ny, nx]:
                    lab[ny, nx] = n; stack.append((ny, nx))
    return lab, n

def spires(c):
    A = c[..., 3] > .5
    W = A.shape[1]
    t = np.array([np.argmax(A[:, x]) if A[:, x].any() else 999 for x in range(W)])
    rise = np.zeros(W)
    for x in range(W):
        nb = [t[i] for i in range(max(0, x - 30), min(W, x + 31)) if abs(i - x) > 9 and t[i] < 999]
        if t[x] < 999 and nb: rise[x] = np.median(nb) - t[x]
    on = rise > 18
    out = []
    x = 0
    while x < W:
        if not on[x]: x += 1; continue
        x1 = x
        while x1 + 1 < W and on[x1 + 1]: x1 += 1
        top = t[x:x1 + 1].min()
        cols = [i for i in range(x, x1 + 1) if t[i] <= top + 2]
        out.append(dict(x0=x, x1=x1, top=int(top), cx=float(np.mean(cols)), w=x1 - x + 1, topw=len(cols)))
        x = x1 + 1
    return out

def greenish(c, sp):
    patch = c[sp['top']:sp['top'] + 10, max(0, sp['x0'] - 3):sp['x1'] + 4]
    a = patch[..., 3] > .5
    if a.sum() < 2: return True
    return patch[..., 1][a].mean() > patch[..., 0][a].mean() * 1.12

def lit(c, sp):
    # rusted metal, not a charred trunk
    patch = c[sp['top']:sp['top'] + 12, sp['x0']:sp['x1'] + 1]
    a = patch[..., 3] > .5
    return a.any() and patch[..., :3].max(-1)[a].mean() > .15

def dedupe(pts, d):
    out = []
    for p in pts:
        if all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 >= d * d for q in out): out.append(p)
    return out

def chimney(c, sp):
    h, s, v = hsv(c)
    x0, x1, top = sp['x0'], sp['x1'], sp['top']
    patch = c[top:top + 10, x0:x1 + 1]
    a = patch[..., 3] > .5
    if a.sum() < 8: return None
    r, g = patch[..., 0][a].mean(), patch[..., 1][a].mean()
    green = g > r * 1.12
    if sp['w'] < 7 or sp.get('topw', 9) < 5 or green: return None     # a chimney has a wide mouth, a mast a point
    # the mouth: dark pixels under the rim
    cx = int(round(sp['cx']))
    ys = [y for y in range(top, top + 14) if c[y, cx, 3] > .5 and v[y, cx] < .2]
    y = (ys[0] + ys[-1]) / 2 if ys else top + 3
    return [round(sp['cx'], 1), round(float(y), 1)]

def lamp(c, sp):
    h, s, v = hsv(c)
    x0, x1, top = max(0, sp['x0'] - 3), sp['x1'] + 3, sp['top']
    best = None
    for y in range(top, top + 9):
        for x in range(x0, min(c.shape[1], x1 + 1)):
            if c[y, x, 3] < .5: continue
            warm = (h[y, x] < 60 or h[y, x] > 340) and s[y, x] > .35
            sc = v[y, x] + (.3 if warm else 0)
            if best is None or sc > best[0]: best = (sc, x, y)
    return [float(best[1]), float(best[2])] if best else [round(sp['cx'], 1), float(top)]

def hot_mask(c):
    h, s, v = hsv(c)
    orange = (c[..., 3] > .5) & (h > 8) & (h < 50) & (s > .55)
    thr = max(.4, np.percentile(v[orange], 85)) if orange.any() else 1.1
    return orange & (v >= thr - 1e-6), v

def hottest_near(v, m, x, y, r=8):
    best = None
    for yy in range(max(0, int(y) - r), min(v.shape[0], int(y) + r + 1)):
        for xx in range(max(0, int(x) - r), min(v.shape[1], int(x) + r + 1)):
            if m[yy, xx] and (best is None or v[yy, xx] > best[0]): best = (v[yy, xx], xx, yy)
    return [float(best[1]), float(best[2])] if best else [float(x), float(y)]

def fire_points(c, n, front):
    m, v = hot_mask(c)
    if front:
        ys, xs = np.nonzero(m)
        w = v[m] + .05
        mx, my = np.average(xs, weights=w), np.average(ys, weights=w)
        cov = np.cov(np.stack([xs - mx, ys - my]), aweights=w)
        ev, evec = np.linalg.eigh(cov)
        ax = evec[:, -1]; L = np.sqrt(ev[-1]) * 1.3
        return [hottest_near(v, m, mx + ax[0] * k * L, my + ax[1] * k * L) for k in (-1, 0, 1)]
    lab, k = label(m)
    blobs = sorted(range(1, k + 1), key=lambda i: -(lab == i).sum())[:n]
    pts = []
    for i in blobs:
        if (lab == i).sum() < 6: continue
        ys, xs = np.nonzero(lab == i)
        j = np.argmax(v[ys, xs])
        pts.append([float(xs[j]), float(ys[j])])
    return pts

def sap_point(c):
    g, r, b = c[..., 1], c[..., 0], c[..., 2]
    m = (c[..., 3] > .5) & (g > .45) & (g - r > .2) & (g - b > .25)
    lab, k = label(m)
    keep = np.isin(lab, [i for i in range(1, k + 1) if (lab == i).sum() >= 10])
    if not keep.any(): return None
    ys, xs = np.nonzero(keep)
    top, bot = ys.min(), ys.max()
    return [float(xs[ys <= top + 2].mean()), float(top)], [float(xs[ys >= bot - 2].mean()), float(bot)]

def dome_points(c):
    h, s, v = hsv(c)
    m = (c[..., 3] > .5) & (h > 60) & (h < 110) & (v > .38) & (s < .6)
    lab, k = label(m)
    pts = []
    for i in sorted(range(1, k + 1), key=lambda i: -(lab == i).sum())[:3]:
        if (lab == i).sum() < 150: continue
        ys, xs = np.nonzero(lab == i)
        top = ys.min()
        pts.append([float(xs[ys <= top + 2].mean()), float(top)])
    return pts

def pipe_end(c, p, cx=128, cy=128):
    x, y = p
    A = c[..., 3] > .5
    dx = (x - cx); dy = (y - cy)
    if abs(dx) >= abs(dy):
        step = 1 if dx > 0 else -1
        row = int(round(y)); xx = int(round(x))
        while 0 <= xx + step < 256 and A[row, xx + step]: xx += step
        return [float(xx), float(y)]
    step = 1 if dy > 0 else -1
    col = int(round(x)); yy = int(round(y))
    while 0 <= yy + step < 256 and A[yy + step, col]: yy += step
    return [float(x), float(yy)]

def loop_anchor(c, kind):
    A = c[..., 3] > .3
    ys, xs = np.nonzero(A)
    if kind == 'base':
        # robust: stray embers below the body are ignored (95th percentile
        # of the opaque rows), x = mean of the body's lowest 20 rows
        rows = np.nonzero((A.sum(1) >= 6))[0]
        bot = rows.max()
        sel = (ys >= bot - 20) & (ys <= bot)
        return [float(xs[sel].mean()), float(bot)]
    if kind == 'lamp':
        h, s, v = hsv(c)
        A = c[..., 3] > .1
        sel = A & (v >= np.percentile(v[A], 97))
        yy, xx = np.nonzero(sel)
        return [float(xx.mean()), float(yy.mean())]
    if kind == 'spout':
        return sap_point(c)

def main():
    base, out = sys.argv[1], sys.argv[2]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    man = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'manifest.json')))
    hints = {p['name']: p for p in man['run_b']['pieces']}
    data = dict(pieces={}, loops={})
    lm = cells(base, 'landmarks'); pp = cells(base, 'pipes'); ff = cells(base, 'fires'); ss = cells(base, 'sites')
    for name, c in lm.items():
        e = []
        sp = spires(c)
        stacks = [p for p in (chimney(c, s) for s in sp) if p]
        masts = [lamp(c, s) for s in sp if chimney(c, s) is None and s['w'] < 7 and not greenish(c, s)]
        A = c[..., 3] > .5
        ty = int(np.argmax(A.any(1))); tx = float(np.nonzero(A[ty])[0].mean())
        top = dict(x0=int(tx) - 2, x1=int(tx) + 2, top=ty, cx=tx, w=5)
        if not greenish(c, top) and not any(abs(chimney_pt[0] - tx) < 8 for chimney_pt in stacks): masts.append(lamp(c, top))
        masts = dedupe(masts, 14)
        if name.startswith('refinery') or name == 'silo_00':
            for i, p in enumerate(stacks[:3]): e.append(['smoke_a' if i == 0 else 'smoke_b'] + p + [1.0 if i == 0 else .85])
        if name.startswith(('tower', 'relay', 'derrick', 'silo', 'refinery', 'dome')):
            for i, p in enumerate(sorted(masts, key=lambda q: q[1])[:2]):
                e.append(['beacon_lime' if i == 0 else 'beacon_magenta'] + p + [.6])
        if name == 'dome_00':
            for p in dome_points(c)[:2]: e.append(['spore_burst'] + p + [.8])
        data['pieces'][name] = dict(atlas='landmarks', box=box(c), emit=e, spires=sp)
    for name, c in pp.items():
        e = []
        if name.startswith('pipe_leak'):
            p = sap_point(c)
            if p:
                e.append(['steam_vent'] + p[0] + [.55])     # vapour off the rupture
                e.append(['leak_sap'] + p[1] + [.6])        # sap dripping where the stream lands
        ends = [pipe_end(c, q) for q in hints.get(name, {}).get('pipe_end_points_px_from_top_left', [])]
        if name == 'pumphouse_00':
            sp = [s for s in spires(c) if s['w'] >= 5]
            for s in sp[:1]: e.append(['steam_vent', round(s['cx'], 1), float(s['top']), .6])
        data['pieces'][name] = dict(atlas='pipes', box=box(c), emit=e, ends=ends)
    for name, c in ff.items():
        e = []
        front = name.startswith('burnfront') or name.startswith('firebreak')
        pts = fire_points(c, 3, front) if front else dedupe(fire_points(c, 4, front), 25)
        # burnt industry: its widest chimney smokes (charred trunks are narrow)
        wide = sorted((sp for sp in spires(c) if sp['w'] >= 9 and sp['topw'] >= 8), key=lambda sp: -sp['topw'])
        stacks = [p for p in (chimney(c, sp) for sp in wide) if p]
        for p in stacks[:1]: e.append(['smoke_a'] + p + [.9])
        if front:
            for i, p in enumerate(pts): e.append(['flame_patch' if i != 1 else 'flame_front'] + p + [.55 if i == 1 else .5])
            if pts: e.append(['wildsmoke_a'] + pts[0] + [.9])
        elif name.startswith(('burnpatch', 'burntforest', 'scorched')):
            for p in pts[:2]: e.append(['flame_patch'] + p + [.45])
            if pts: e.append(['wildsmoke_b'] + pts[0] + [.8])
        else:   # coal beds glow and smoke thinly
            for p in pts[:1]: e.append(['flame_patch'] + p + [.35]); e.append(['wildsmoke_b'] + p + [.6])
        if pts and len(e) < 4: e.append(['ember_rain'] + pts[-1] + [.6])
        data['pieces'][name] = dict(atlas='fires', box=box(c), emit=e[:4])
    for name, c in ss.items():
        data['pieces'][name] = dict(atlas='sites', box=box(c), emit=[])
    kinds = {'smoke_a': 'base', 'smoke_b': 'base', 'flame_front': 'base', 'flame_patch': 'base',
             'wildsmoke_a': 'base', 'wildsmoke_b': 'base', 'steam_vent': 'base', 'ember_rain': 'base',
             'spore_burst': 'base', 'leak_sap': 'base', 'beacon_lime': 'lamp', 'beacon_magenta': 'lamp'}
    atl = {'smoke': cells(base, 'smoke'), 'wildfire': cells(base, 'wildfire'), 'firesmoke': cells(base, 'firesmoke'),
           'leaks': cells(base, 'leaks'), 'lights': cells(base, 'lights')}
    for loop, kind in kinds.items():
        for a in atl.values():
            frames = [a[k] for k in sorted(a) if k.startswith(loop + '_')]
            if frames:
                if kind == 'lamp':   # the lit frame (the off frames are dark)
                    frames = [max(frames, key=lambda f: (f[..., :3].max(-1) * f[..., 3]).sum())]
                pts = np.array([q for q in (loop_anchor(f, kind) for f in frames) if q is not None])
                if len(pts) == 0: print('NO ANCHOR', loop); continue
                data['loops'][loop] = dict(anchor=[round(float(pts[:, 0].mean()), 1), round(float(pts[:, 1].mean()), 1)],
                                           spread=round(float(np.abs(pts - pts.mean(0)).max()), 1), kind=kind)
    for p in data['pieces'].values(): p.pop('spires', None)
    # JsonUtility-friendly layout: lists of flat objects
    flat = dict(
        pieces=[dict(name=n, atlas=p['atlas'], box=p['box'],
                     emit=[dict(loop=e[0], x=round(e[1], 1), y=round(e[2], 1), scale=e[3]) for e in p['emit']],
                     ends=[dict(x=q[0], y=q[1]) for q in p.get('ends', [])]) for n, p in data['pieces'].items()],
        loops=[dict(name=n, x=l['anchor'][0], y=l['anchor'][1], spread=l['spread'], kind=l['kind']) for n, l in data['loops'].items()])
    with open(out, 'w') as fh:
        fh.write('{\n "note": "MEASURED by Art/Worlds/Verdant/backdrop_v3/src~/measure_points.py -- do not edit by hand",\n')
        fh.write(' "pieces": [\n' + ',\n'.join('  ' + json.dumps(p) for p in flat['pieces']) + '\n ],\n')
        fh.write(' "loops": [\n' + ',\n'.join('  ' + json.dumps(l) for l in flat['loops']) + '\n ]\n}\n')
    for n, p in data['pieces'].items():
        if p['emit'] or p.get('ends'): print(n, p['box'], p['emit'], p.get('ends', ''))
    for n, l in data['loops'].items(): print('loop', n, l)
    if dbg:
        for atlas, cs in (('landmarks', lm), ('pipes', pp), ('fires', ff)):
            sheet = Image.new('RGBA', (4 * 384, 4 * 384), (255, 0, 255, 255))
            for i, (n, c) in enumerate(cs.items()):
                im = Image.fromarray((c * 255).astype(np.uint8)).resize((384, 384), Image.NEAREST)
                d = ImageDraw.Draw(im)
                for e in data['pieces'][n]['emit']:
                    x, y = e[1] * 1.5, e[2] * 1.5
                    col = (255, 255, 255, 255) if 'smoke' in e[0] else (0, 255, 255, 255)
                    d.line([(x - 8, y), (x + 8, y)], fill=col); d.line([(x, y - 8), (x, y + 8)], fill=col)
                for q in data['pieces'][n].get('ends', []):
                    d.ellipse([q[0] * 1.5 - 4, q[1] * 1.5 - 4, q[0] * 1.5 + 4, q[1] * 1.5 + 4], outline=(255, 255, 0, 255))
                sheet.alpha_composite(im, ((i % 4) * 384, (i // 4) * 384))
            sheet.save(os.path.join(dbg, 'points_' + atlas + '.png'))

if __name__ == '__main__':
    main()
