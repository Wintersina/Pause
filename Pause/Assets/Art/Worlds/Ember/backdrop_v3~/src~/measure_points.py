#!/usr/bin/env python3
"""Measures, on the pixels, where every Ember ambient loop attaches.

Writes points.json (a JsonUtility list, read at runtime by EmberAmbientCatalog /
GroundPlacement as a TextAsset): for each piece cell of landmarks / pipes / fires / sites

    box      opaque bounding box (x0, y0, x1, y1), cell px from the top-left
    emit     [loop, x, y, scale] ambient loops at MEASURED points
    ends     pipe-end points (flange faces), pipes only

and for each loop its own anchor (the point of its 256 cell placed ON the
emitter point): the plume / flame base for the rising loops, the lantern foot
of a beacon.

How the points are found (no eyeballing; the run B manifest's emitter points
are nominal and are NOT used):
  stacks   the column-top profile t(x) of the opaque art; a "spire" is a run
           of columns rising > 14 px above the median top of its neighbours
           (+-30 px). A spire >= 4 px wide, >= 3 px wide at its very top is
           a chimney (or >= 8 px wide at any top): the smoke point is the centre of its mouth (the dark
           ellipse just under the rim; rim + 3 px if none). A narrower spire
           is a mast: its lamp knob (brightest warm pixel in its top rows)
           carries a beacon.
  rims     a cooling tower's rim is wider than any spire: the point is the
           centroid of the hot (lava-lit) pixels inside its top 30 rows.
  vents    a lava fountain's mouth is where its column meets the crater: the
           first row below the column's top at which the opaque run through
           the column's axis is > 2.2 x the column's width, centred on the
           column. A scar / pool / bed has its mouth at the centroid of its
           biggest hot blob (hue 8-55 deg, sat > .5, value in the top 15% of
           the piece's orange pixels).
  leaks    a venting pipe's mouth is the bottom-centre of its grey steam
           blob; a lava-spilling pipe's is the top of its biggest hot blob.
  pipes    each nominal end from the run B manifest is pushed out along its
           axis to the last opaque pixel of the flange.

    python3 src~/measure_points.py <atlas dir (Resources/Worlds/Ember/Backdrop3)> <out json> [--debug DIR]
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
        out[s['n']] = clean(im[y0:y0 + s['h'], s['x']:s['x'] + s['w']])
    return out

MIN_PIECE_PX = 50

def clean(c):
    """Drops detached specks (stray pixels, cut-off scraps of a neighbour at the cell's edge)
    that would otherwise be measured as spires: opaque components under MIN_PIECE_PX are cleared."""
    A = c[..., 3] > .5
    lab, k = label(A)
    c = c.copy()
    sizes = [(lab == i).sum() for i in range(1, k + 1)]
    if not sizes: return c
    keep = max(MIN_PIECE_PX, .04 * max(sizes))      # scraps of a neighbouring cell's art, stray sparks
    for i in range(1, k + 1):
        if sizes[i - 1] < keep: c[lab == i] = 0
    return c

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


def spires(c, rise_min=14):
    A = c[..., 3] > .5
    W = A.shape[1]
    t = np.array([np.argmax(A[:, x]) if A[:, x].any() else 999 for x in range(W)])
    rise = np.zeros(W)
    for x in range(W):
        nb = [t[i] for i in range(max(0, x - 30), min(W, x + 31)) if abs(i - x) > 9 and t[i] < 999]
        if t[x] < 999 and nb: rise[x] = np.median(nb) - t[x]
    on = rise > rise_min
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

def dedupe(pts, d):
    out = []
    for p in pts:
        if all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 >= d * d for q in out): out.append(p)
    return out

def chimney(c, sp):
    """Centre of a stack's mouth, or None for a mast (too narrow)."""
    h, s, v = hsv(c)
    if (sp['w'] < 4 or sp['topw'] < 3) and sp['w'] < 8: return None    # a mast, not a stack
    top = sp['top']
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
    orange = (c[..., 3] > .5) & (h > 8) & (h < 55) & (s > .5)
    thr = max(.4, np.percentile(v[orange], 85)) if orange.any() else 1.1
    return orange & (v >= thr - 1e-6), v

def blobs(m, v, n):
    lab, k = label(m)
    ids = sorted(range(1, k + 1), key=lambda i: -(lab == i).sum())[:n]
    out = []
    for i in ids:
        if (lab == i).sum() < 6: continue
        ys, xs = np.nonzero(lab == i)
        w = v[ys, xs] + .05
        out.append(dict(cx=float(np.average(xs, weights=w)), cy=float(np.average(ys, weights=w)),
                        top=(float(xs[ys == ys.min()].mean()), float(ys.min())),
                        bot=(float(xs[ys == ys.max()].mean()), float(ys.max())), n=int((lab == i).sum())))
    return out

def snap(m, x, y, r=6):
    """The nearest hot pixel to (x, y): a point on the lava, not beside it."""
    ys, xs = np.nonzero(m)
    if len(xs) == 0: return [float(x), float(y)]
    j = np.argmin((xs - x) ** 2 + (ys - y) ** 2)
    return [float(xs[j]), float(ys[j])]

def rim_points(c):
    """Cooling towers: the centroid of each lava-lit rim interior (one per tower)."""
    m, v = hot_mask(c)
    A = c[..., 3] > .5
    # a tower's rim: hot pixels inside the topmost 30 rows of its own column band
    t = np.array([np.argmax(A[:, x]) if A[:, x].any() else 999 for x in range(A.shape[1])])
    sel = np.zeros_like(m)
    ys, xs = np.nonzero(m)
    for y, x in zip(ys, xs):
        if y <= t[max(0, x - 40):x + 41].min() + 30: sel[y, x] = True
    lab, k = label(sel)
    out = []
    for i in sorted(range(1, k + 1), key=lambda i: -(lab == i).sum()):
        if (lab == i).sum() < 120: continue
        yy, xx = np.nonzero(lab == i)
        out.append([round(float(xx.mean()), 1), round(float(yy.mean()), 1)])
    return sorted(out)[:2]

def fountain_mouth(c):
    """Where a lava column meets its crater: the centre of the crater's hot ring, under the column.

    The column's axis x is the centroid of the orange pixels in the top 35% of the art; the ring is
    the hot pixels within 60 px of that axis below the art's vertical middle; the mouth is their
    centroid (the ring is symmetric about the column, so its centroid is the crater's centre)."""
    A = c[..., 3] > .5
    h, s, v = hsv(c)
    orange = A & (h > 8) & (h < 55) & (s > .5) & (v > .35)
    ys, xs = np.nonzero(A)
    y0, y1 = ys.min(), ys.max()
    top = orange.copy(); top[int(y0 + .35 * (y1 - y0)):] = False
    if not top.any(): return None
    cx = float(np.nonzero(top)[1].mean())
    m, hv = hot_mask(c)
    ring = m.copy(); ring[:int((y0 + y1) / 2)] = False
    ring[:, :int(cx) - 60] = False; ring[:, int(cx) + 61:] = False
    if not ring.any(): return None
    ry, rx = np.nonzero(ring)
    return [round(float(rx.mean()), 1), round(float(ry.mean()), 1)]

def steam_mouth(c):
    """Bottom-centre of the grey steam blob over a vent stub."""
    h, s, v = hsv(c)
    m = (c[..., 3] > .5) & (s < .22) & (v > .45)
    lab, k = label(m)
    if k == 0: return None
    big = max(range(1, k + 1), key=lambda i: (lab == i).sum())
    ys, xs = np.nonzero(lab == big)
    if len(xs) < 80: return None
    bot = ys.max()
    return [round(float(xs[ys >= bot - 3].mean()), 1), float(bot)]

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

def loop_anchor(c):
    """Foot of a loop: bottom-centre of the opaque art (stray sparks below the body ignored)."""
    A = c[..., 3] > .3
    ys, xs = np.nonzero(A)
    rows = np.nonzero((A.sum(1) >= 6))[0]
    if len(rows) == 0: return None      # a blank / near-blank frame (a beacon's dark phase)
    bot = rows.max()
    sel = (ys >= bot - 20) & (ys <= bot)
    return [float(xs[sel].mean()), float(bot)]

STACKERS = ('forgetower', 'refinery', 'kiln', 'forgewell', 'smelter')
MASTED = ('forgetower', 'refinery', 'derrick', 'viaduct', 'smelter', 'kiln', 'forgewell', 'coolingtower')

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
        stacks = sorted([(s, chimney(c, s)) for s in sp if chimney(c, s)], key=lambda t: -t[0]['topw'])
        masts = [lamp(c, s) for s in sp if chimney(c, s) is None]
        A = c[..., 3] > .5
        ty = int(np.argmax(A.any(1))); tx = float(np.nonzero(A[ty])[0].mean())
        masts = dedupe(masts, 14)
        smokes = []
        if name.startswith('coolingtower'):
            for p in rim_points(c): smokes.append(['smoke_b'] + p + [1.0])
        elif name.startswith(STACKERS):
            for i, (s, p) in enumerate(stacks[:2]): smokes.append(['smoke_a' if i == 0 else 'smoke_b'] + p + [1.0 if i == 0 else .85])
        e += smokes
        if name.startswith(MASTED):
            tips = sorted(masts, key=lambda q: q[1])
            if not tips and not smokes: tips = [[tx, float(ty)]]
            names = ['beacon_amber', 'beacon_magenta']
            for i, p in enumerate(tips[:2]): e.append([names[i]] + p + [.6])
            if name.startswith(('derrick', 'viaduct')) and len(tips) > 1: e.append(['strobe_white'] + tips[1] + [.5])
        data['pieces'][name] = dict(atlas='landmarks', box=box(c), emit=e[:4])
    for name, c in pp.items():
        e = []
        if name == 'pipe_leak_00':
            p = steam_mouth(c)
            if p: e.append(['steam_vent'] + p + [.8])
        elif name == 'pipe_leak_01':
            m, v = hot_mask(c)
            b = blobs(m, v, 1)
            if b: e.append(['pipe_drip'] + snap(m, *b[0]['bot']) + [.6])
        elif name == 'pipe_lava_00':
            m, v = hot_mask(c)
            b = blobs(m, v, 1)
            if b: e.append(['lava_bubble'] + snap(m, b[0]['cx'], b[0]['cy']) + [.5])
        elif name == 'pumphouse_00':
            sp = [s for s in spires(c, 10) if s['w'] >= 4]
            for s in sp[:1]: e.append(['steam_vent', round(s['cx'], 1), float(s['top']) + 1, .6])
        ends = [pipe_end(c, q) for q in hints.get(name, {}).get('pipe_end_points_px_from_top_left', [])]
        data['pieces'][name] = dict(atlas='pipes', box=box(c), emit=e, ends=ends)
    for name, c in ff.items():
        e = []
        m, v = hot_mask(c)
        bl = blobs(m, v, 3)
        if name.startswith('lavafountain'):
            p = fountain_mouth(c) or (snap(m, bl[0]['cx'], bl[0]['cy']) if bl else None)
            if p:
                e.append(['eruptsmoke_a'] + p + [.8])
                e.append(['ember_rain'] + p + [.6])
        elif name.startswith('eruptionscar'):
            if bl:
                p = snap(m, bl[0]['cx'], bl[0]['cy'])
                e.append(['fountain'] + p + [.5])
                e.append(['eruptsmoke_a'] + p + [.7])
        elif name.startswith('lavapool'):
            for b in bl[:2]: e.append(['lava_bubble'] + snap(m, b['cx'], b['cy']) + [.5])
            if bl: e.append(['ember_rain'] + snap(m, bl[0]['cx'], bl[0]['cy']) + [.5])
        elif name.startswith('flarestack'):
            sp = [s for s in spires(c, 10) if chimney(c, s)]
            if sp:
                s = max(sp, key=lambda q: q['topw'])
                e.append(['flare'] + chimney(c, s) + [.6])
        elif name.startswith(('coalbed', 'scorched')):
            if bl:
                p = snap(m, bl[0]['cx'], bl[0]['cy'])
                e.append(['eruptsmoke_b'] + p + [.7])
                e.append(['ember_rain'] + p + [.5])
            if len(bl) > 1: e.append(['lava_bubble'] + snap(m, bl[1]['cx'], bl[1]['cy']) + [.4])
        else:   # firebreak, lavachannel: lava runs
            for b in bl[:2]: e.append(['lava_bubble'] + snap(m, b['cx'], b['cy']) + [.5])
            if bl: e.append(['ember_rain'] + snap(m, bl[-1]['cx'], bl[-1]['cy']) + [.5])
        data['pieces'][name] = dict(atlas='fires', box=box(c), emit=e[:4])
    for name, c in ss.items():
        data['pieces'][name] = dict(atlas='sites', box=box(c), emit=[])
    atl = {'smoke': cells(base, 'smoke'), 'lavafire': cells(base, 'lavafire'), 'eruption': cells(base, 'eruption'),
           'leaks': cells(base, 'leaks'), 'lights': cells(base, 'lights')}
    loops = ['smoke_a', 'smoke_b', 'flare', 'fountain', 'eruptsmoke_a', 'eruptsmoke_b', 'steam_vent', 'pipe_drip',
             'ember_rain', 'lava_bubble', 'beacon_amber', 'beacon_magenta', 'strobe_white']
    for loop in loops:
        for a in atl.values():
            frames = [a[k] for k in sorted(a) if k.startswith(loop + '_')]
            if frames:
                pts = np.array([q for q in (loop_anchor(f) for f in frames) if q is not None])
                data['loops'][loop] = dict(anchor=[round(float(pts[:, 0].mean()), 1), round(float(pts[:, 1].mean()), 1)],
                                           spread=round(float(np.abs(pts - pts.mean(0)).max()), 1), kind='base')
    flat = dict(
        pieces=[dict(name=n, atlas=p['atlas'], box=p['box'],
                     emit=[dict(loop=e[0], x=round(e[1], 1), y=round(e[2], 1), scale=e[3]) for e in p['emit']],
                     ends=[dict(x=q[0], y=q[1]) for q in p.get('ends', [])]) for n, p in data['pieces'].items()],
        loops=[dict(name=n, x=l['anchor'][0], y=l['anchor'][1], spread=l['spread'], kind=l['kind']) for n, l in data['loops'].items()])
    with open(out, 'w') as fh:
        fh.write('{\n "note": "MEASURED by Art/Worlds/Ember/backdrop_v3~/src~/measure_points.py -- do not edit by hand",\n')
        fh.write(' "pieces": [\n' + ',\n'.join('  ' + json.dumps(p) for p in flat['pieces']) + '\n ],\n')
        fh.write(' "loops": [\n' + ',\n'.join('  ' + json.dumps(l) for l in flat['loops']) + '\n ]\n}\n')
    for n, p in data['pieces'].items():
        if p['emit'] or p.get('ends'): print(n, p['box'], p['emit'], p.get('ends', ''))
    for n, l in data['loops'].items(): print('loop', n, l)
    if dbg:
        for atlas, cs in (('landmarks', lm), ('pipes', pp), ('fires', ff)):
            sheet = Image.new('RGBA', (4 * 384, 4 * 384), (70, 70, 90, 255))
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
