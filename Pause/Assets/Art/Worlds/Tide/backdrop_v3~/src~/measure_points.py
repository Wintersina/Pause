#!/usr/bin/env python3
"""Measures, on the pixels, where every Tide ambient loop attaches.

Writes points.json (a JsonUtility list, read at runtime by TideAmbientCatalog / TideDirector):
for each piece cell of landmarks / pipes / fires / sites

    box      opaque bounding box (x0, y0, x1, y1), cell px from the top-left
    emit     [loop, x, y, scale] ambient loops at MEASURED points
    ends     pipe-end points (flange faces), pipes only

and, once run C is installed (smoke, flames, surf, leaks, lights atlases), each loop's own anchor
(the point of its 256 cell placed ON the emitter point; default (128, 236)). Absent loops get no
anchor entry and the catalog skips them (TideAmbientCatalog.Present).

How the points are found (no eyeballing; the run B manifest's emitter points are nominal and only
seed the pipe ends):
  stacks   the column-top profile t(x) of the opaque art; a "spire" is a run of columns rising
           > 14 px above the median top of its neighbours (+-30 px). The highest spires of a
           derrick / refinery column carry smoke at their top (centre of the top columns, +3 px:
           inside the mouth); a flare stack carries a flare AND smoke on its open mouth.
  masts    a lighthouse lantern / mast head carries a beacon: the brightest lamp-coloured pixel
           blob (bright green or blue-violet, 4..80 px) among the highest spires.
  vents    a bubbling vent's point is the centroid of its bright mint glow inside the crater ring
           nearest the cell centre; a crawler's the nearest opaque pixel to its nominal exhaust.
  eyes     a whirlpool's eye: centroid of the darkest pixels near the centre of its opaque box.
  leaks    a pipe leak's mouth is the pipe-side end of its painted spray blob (bright mint, low
           opacity-free: v > .55, g > r + .1) -- the blob's point nearest the pipe body.
  pipes    each nominal end from the run B manifest is pushed out along its axis to the last
           opaque pixel of the flange.

    python3 src~/measure_points.py <atlas dir (Resources/Worlds/Tide/Backdrop3)> <out json> [--debug DIR]
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


def top_of(c):
    A = c[..., 3] > .5
    ty = int(np.argmax(A.any(1)))
    return float(np.nonzero(A[ty])[0].mean()), float(ty)

def tip(c, sp):
    """The smoke point of a spire: centre of its top columns, 3 px inside (a derrick / column mouth)."""
    return [round(sp['cx'], 1), float(sp['top'] + 3)]

def lamps(c, y_max=None):
    """Bright lamp knobs (green / mint or blue-violet blobs, 4..80 px), top first."""
    h, s, v = hsv(c)
    A = c[..., 3] > .5
    green = (h > 120) & (h < 190)
    blue = (h > 200) & (h < 265)
    m = A & (v > .45) & (s > .5) & (green | blue)
    lab, k = label(m)
    out = []
    for i in range(1, k + 1):
        ys, xs = np.nonzero(lab == i)
        if not 3 <= len(xs) <= 80: continue
        w = v[ys, xs]
        out.append([float(np.average(xs, weights=w)), float(np.average(ys, weights=w))])
    out.sort(key=lambda p: p[1])
    return dedupe([p for p in out if y_max is None or p[1] <= y_max], 24)

def snap_opaque(c, p, r=12):
    A = c[..., 3] > .5
    ys, xs = np.nonzero(A)
    j = np.argmin((xs - p[0]) ** 2 + (ys - p[1]) ** 2)
    return [float(xs[j]), float(ys[j])]

def vent_centre(c):
    """Centroid of the bright mint glow within 32 px of the cell's centre (the crater's mouth)."""
    h, s, v = hsv(c)
    yy, xx = np.mgrid[0:c.shape[0], 0:c.shape[1]]
    m = (c[..., 3] > .5) & (v > .6) & (h > 120) & (h < 190) & ((xx - 130) ** 2 + (yy - 130) ** 2 < 32 ** 2)
    if not m.any(): return None
    ys, xs = np.nonzero(m)
    return [round(float(xs.mean()), 1), round(float(ys.mean()), 1)]

def eye(c):
    A = c[..., 3] > .5
    b = box(c)
    cx, cy = (b[0] + b[2]) / 2, (b[1] + b[3]) / 2
    lum = c[..., :3] @ np.array([.299, .587, .114])
    yy, xx = np.mgrid[0:c.shape[0], 0:c.shape[1]]
    m = A & (lum < .06) & ((xx - cx) ** 2 + (yy - cy) ** 2 < 40 ** 2)
    if not m.any(): return [round(cx, 1), round(cy, 1)]
    ys, xs = np.nonzero(m)
    return [round(float(xs.mean()), 1), round(float(ys.mean()), 1)]

def spray_mouth(c, name):
    """The pipe-side end of a leak's painted spray blob: the bright mint pixels right of the pipe."""
    h, s, v = hsv(c)
    m = (c[..., 3] > .4) & (v > .55) & (c[..., 1] > c[..., 0] + .1) & (h > 130) & (h < 190)
    m[:, :118] = False
    m[:75] = False
    m[115:] = False      # the break sits on the pipe axis: ignore the spray's lower fan
    lab, k = label(m)
    if k == 0: return None
    big = max(range(1, k + 1), key=lambda i: (lab == i).sum())
    ys, xs = np.nonzero(lab == big)
    if len(xs) < 40: return None
    x0 = xs.min()
    sel = xs <= x0 + 6
    return [round(float(xs[sel].mean()), 1), round(float(ys[sel].mean()), 1)]

SMOKERS = ('oilrig', 'refineryrig')
MASTED = ('lighthouse', 'crane_gantry', 'mooring_buoys', 'tanker', 'trawlerbarge', 'bridge_section', 'pump_platform', 'sunken_city', 'oilrig', 'refineryrig')

def main():
    base, out = sys.argv[1], sys.argv[2]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    man = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'manifest.json')))
    hints = {p['name']: p for p in man['run_b']['pieces']}
    data = dict(pieces={}, loops={})
    lm = cells(base, 'landmarks'); pp = cells(base, 'pipes'); ff = cells(base, 'fires'); ss = cells(base, 'sites')
    for name, c in lm.items():
        e = []
        sp = sorted(spires(c), key=lambda s: s['top'])
        if name == 'refineryrig_00':
            # the vent stub on the first dome: the highest opaque pixels left of x = 110 (columns within 2 px of the top)
            A = c[..., 3] > .5
            t = np.array([np.argmax(A[:, x]) if A[:, x].any() else 999 for x in range(110)])
            top = t.min()
            e.append(['smoke_a', round(float(np.mean(np.nonzero(t <= top + 1)[0])), 1), float(top + 3), .8])
        elif name == 'refineryrig_01':
            for i, t in enumerate(sp[:2]): e.append(['smoke_a' if i == 0 else 'smoke_b'] + tip(c, t) + [1.0 if i == 0 else .85])
        elif name in ('oilrig_00', 'oilrig_02', 'pump_platform_00') and sp:
            e.append(['smoke_a'] + tip(c, sp[0]) + [.8])      # a derrick's flare boom tip
        if name.startswith(MASTED):
            ys, xs = np.nonzero(c[..., 3] > .5)
            lim = ys.min() + .5 * (ys.max() - ys.min())
            for p in lamps(c, lim)[:2]:
                if all((p[0] - q[1]) ** 2 + (p[1] - q[2]) ** 2 > 18 ** 2 for q in e):
                    e.append(['beacon_mint' if len([1 for q in e if q[0].startswith('beacon')]) == 0 else 'beacon_amber'] + p + [.6])
        if name.startswith('lighthouse'):
            e = [q for q in e if not q[0].startswith('beacon')]
            l = lamps(c)
            tx, ty = top_of(c)
            p = min(l, key=lambda q: q[1]) if l else [tx, ty + 4]
            e.append(['beacon_mint'] + p + [.9])
        data['pieces'][name] = dict(atlas='landmarks', box=box(c), emit=e[:4])
    for name, c in pp.items():
        e = []
        if name.startswith('pipe_leak'):
            p = spray_mouth(c, name)
            if p:
                e.append(['steam_vent'] + p + [.8])
                e.append(['pipe_bubbles'] + p + [.6])
        elif name == 'pumphouse_00':
            sp = [s for s in spires(c, 10) if s['w'] >= 2]
            if sp:
                s = min(sp, key=lambda q: q['top'])
                e.append(['steam_vent', round(s['cx'], 1), float(s['top']) + 1, .6])
        ends = [pipe_end(c, [q['x'], q['y']]) for q in hints.get(name, {}).get('ends', [])]
        data['pieces'][name] = dict(atlas='pipes', box=box(c), emit=e, ends=ends)
    for name, c in ff.items():
        e = []
        if name.startswith('bubbling_vent'):
            p = vent_centre(c) or snap_opaque(c, [131, 131])
            e.append(['bubble_stream'] + p + [.9])
            e.append(['vent_gas'] + p + [.6])
        elif name.startswith('flare_stack'):
            sp = [s for s in spires(c, 10) if chimney(c, s)]
            if sp:
                s = max(sp, key=lambda q: q['topw'])
                p = chimney(c, s)
                e.append(['flare'] + p + [.7])
                e.append(['smoke_a'] + p + [.8])
        elif name.startswith('whirlpool'):
            e.append(['whirlpool'] + eye(c) + [.9])
        elif name.startswith('wreck_hull'):
            pass
        data['pieces'][name] = dict(atlas='fires', box=box(c), emit=e[:4])
    for name, c in ss.items():
        data['pieces'][name] = dict(atlas='sites', box=box(c), emit=[])
    # run C loop anchors, for every run C atlas that is installed
    loops = ['smoke_a', 'smoke_b', 'flare', 'vent_gas', 'steam_vent', 'pipe_bubbles', 'bubble_stream', 'oil_drip',
             'beacon_mint', 'beacon_amber', 'strobe_white', 'whirlpool']
    for atlas in ('smoke', 'flames', 'surf', 'leaks', 'lights'):
        if not os.path.exists(os.path.join(base, atlas + '.json')): continue
        a = cells(base, atlas)
        for loop in loops:
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
        fh.write('{\n "note": "MEASURED by Art/Worlds/Tide/backdrop_v3~/src~/measure_points.py -- do not edit by hand",\n')
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
                bg = Image.new('RGBA', im.size, (70, 70, 90, 255)); bg.alpha_composite(im); im = bg
                d = ImageDraw.Draw(im)
                for e in data['pieces'][n]['emit']:
                    x, y = e[1] * 1.5, e[2] * 1.5
                    col = (255, 255, 255, 255) if 'smoke' in e[0] or e[0] in ('flare', 'steam_vent') else (255, 255, 0, 255) if 'beacon' in e[0] else (0, 255, 255, 255)
                    d.line([(x - 8, y), (x + 8, y)], fill=col); d.line([(x, y - 8), (x, y + 8)], fill=col)
                for q in data['pieces'][n].get('ends', []):
                    d.ellipse([q[0] * 1.5 - 4, q[1] * 1.5 - 4, q[0] * 1.5 + 4, q[1] * 1.5 + 4], outline=(255, 0, 255, 255))
                sheet.alpha_composite(im, ((i % 4) * 384, (i // 4) * 384))
            sheet.save(os.path.join(dbg, 'points_' + atlas + '.png'))

if __name__ == '__main__':
    main()
