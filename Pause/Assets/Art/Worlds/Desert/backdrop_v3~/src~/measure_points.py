#!/usr/bin/env python3
"""Measures, on the pixels, where every Desert ambient loop attaches.

Writes points.json (a JsonUtility list, read at runtime by DesertAmbientCatalog / DesertDirector):
for each piece cell of landmarks / pipes / fires / sites

    box      opaque bounding box (x0, y0, x1, y1), cell px from the top-left
    emit     [loop, x, y, scale] ambient loops at MEASURED points
    ends     pipe-end points (flange faces), pipes only

and each loop's own anchor (the point of its 256 cell placed ON the emitter), from the run C
manifest (verify.py holds every frame to it).

How the points are found (no eyeballing; the run B manifest carries no usable emitter points):
  stacks   the column-top profile t(x) of the opaque art; a "spire" is a run of columns rising
           > 14 px above the median top of its neighbours (+-30 px). Smoke / steam / strobes sit
           3 px inside the top of the highest spires (a stack mouth, a mast tip, a derrick head).
  craters  a well's / vent's hollow: centroid of the darkest opaque pixels near the cell centre.
  centres  the opaque box's centre (window rows, glass glints, mirage).
  crests   the highest opaque point of a dune ridge.
  pipes    each nominal end from the run B manifest is pushed out along its axis to the last
           opaque pixel of the flange.

    python3 src~/measure_points.py <atlas dir (Resources/Worlds/Desert/Backdrop3)> <out json> [--debug DIR]
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

def snap_opaque(c, p, r=12):
    A = c[..., 3] > .5
    ys, xs = np.nonzero(A)
    j = np.argmin((xs - p[0]) ** 2 + (ys - p[1]) ** 2)
    return [float(xs[j]), float(ys[j])]

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


def eye(c, r=40):
    A = c[..., 3] > .5
    b = box(c)
    cx, cy = (b[0] + b[2]) / 2, (b[1] + b[3]) / 2
    lum = c[..., :3] @ np.array([.299, .587, .114])
    yy, xx = np.mgrid[0:c.shape[0], 0:c.shape[1]]
    near = A & ((xx - cx) ** 2 + (yy - cy) ** 2 < r ** 2)
    if not near.any(): return [round(cx, 1), round(cy, 1)]
    thr = np.percentile(lum[near], 15)
    m = near & (lum <= thr)
    ys, xs = np.nonzero(m)
    return [round(float(xs.mean()), 1), round(float(ys.mean()), 1)]

def centre(c):
    b = box(c)
    return [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2]

def stack_points(c, n=2, rise=14):
    sp = sorted(spires(c, rise), key=lambda s: s['top'])
    return [tip(c, s) for s in sp[:n]], sp

STACKED = {
    # name -> list of (loop, which spire, scale)
    'refinery_hull_00': [('smoke_a', 0, .9), ('heat_shimmer', 1, .8)],
    'refinery_hull_01': [('smoke_a', 0, .9), ('smoke_b', 1, .7)],
    'ribcage_gantry_00': [('beacon_white', 0, .7)],
    'ribcage_gantry_01': [('beacon_white', 0, .7)],
    'drill_head_tower': [('strobe_white', 0, .8), ('steam_vent', 1, .6)],
    'solar_array_skeleton_00': [('strobe_white', 0, .6)],
    'solar_array_skeleton_01': [('strobe_white', 0, .6)],
    'crawler_caravan_00': [('smoke_b', 1, .6), ('beacon_pale_gold', 0, .7)],
    'crawler_caravan_01': [('smoke_b', 1, .6), ('beacon_pale_gold', 0, .7)],
    'radio_mast': [('beacon_white', 0, .9)],
    'collapsed_dome_cluster': [('smoke_b', 0, .8)],
    'bucket_wheel_excavator': [('strobe_white', 0, .8), ('heat_shimmer', 0, .8)],
    'pump_well_station': [('steam_vent', 0, .7), ('beacon_pale_gold', 1, .6)],
    'pump_house': [('steam_vent', 0, .6), ('work_lamp_flicker', 1, .6)],
    'lone_crawler': [('smoke_b', 0, .6), ('work_lamp_flicker', 0, .5)],
}
CENTRED = {
    'refinery_hull_00': [('window_lights', .8)], 'refinery_hull_01': [('window_lights', .8)],
    'solar_array_skeleton_00': [('glass_sparkle', .9)], 'solar_array_skeleton_01': [('glass_sparkle', .9)],
    'sunken_cargo_crawler': [('window_lights', .8)],
    'fused_glass_crater_rim': [('glass_sparkle', 1.0), ('mirage_glint', .9)],
    'fulgurite_00': [('glass_sparkle', .9)], 'fulgurite_01': [('glass_sparkle', .9)],
}
CRATERS = {
    'dry_well_00': [('smoke_b', .6)], 'dry_well_01': [('smoke_b', .6)],
    'heat_vent_00': [('heat_vent', .9), ('smoke_b', .45)], 'heat_vent_01': [('heat_vent', .9), ('smoke_b', .45)],
}

def main():
    base, out = sys.argv[1], sys.argv[2]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    here = os.path.dirname(os.path.abspath(__file__))
    top = json.load(open(os.path.join(here, '..', 'manifest.json')))
    hints = {s['name']: s for s in top['run_b']['sprites']}
    data = dict(pieces={}, loops={})
    lm = cells(base, 'landmarks'); pp = cells(base, 'pipes'); ff = cells(base, 'fires'); ss = cells(base, 'sites')
    for atlas, cs in (('landmarks', lm), ('pipes', pp), ('fires', ff)):
        for name, c in cs.items():
            e = []
            if name in STACKED:
                _, sp = stack_points(c, 4)
                for loop, k, sc in STACKED[name]:
                    if k < len(sp): e.append([loop] + tip(c, sp[k]) + [sc])
                    elif k > 0 and sp: e.append([loop] + tip(c, sp[0]) + [sc * .8])
                if name == 'radio_mast' and not sp:
                    p = top_of(c); e.append(['beacon_white', p[0], p[1] + 3, .9])
            for loop, sc in CENTRED.get(name, []):
                e.append([loop] + centre(c) + [sc])
            for loop, sc in CRATERS.get(name, []):
                e.append([loop] + eye(c) + [sc])
            if name == 'dune_ridge':
                p = top_of(c)
                e.append(['sandplume', p[0], p[1] + 12, .7])
            ends = []
            if atlas == 'pipes':
                hh = hints.get(name)
                if hh and hh.get('pipe_ends'):
                    ends = [pipe_end(c, q['xy']) for q in hh['pipe_ends']]
            data['pieces'][name] = dict(atlas=atlas, box=box(c), emit=e[:4], ends=ends)
    for name, c in ss.items():
        data['pieces'][name] = dict(atlas='sites', box=box(c), emit=[], ends=[])
    for l in top['run_c']['loops'] if 'run_c' in top else json.load(open(os.path.join(here, '..', 'manifest_c.json')))['run_c']['loops']:
        stem = l['atlas'][:-4]
        if not os.path.exists(os.path.join(base, stem + '.json')): continue
        data['loops'][l['name']] = dict(anchor=[float(l['anchor_px_top_left'][0]), float(l['anchor_px_top_left'][1])], spread=0.0,
                                        kind='base' if l['anchor_px_top_left'][1] > 200 else 'centre')
    flat = dict(
        pieces=[dict(name=n, atlas=p['atlas'], box=p['box'],
                     emit=[dict(loop=e[0], x=round(e[1], 1), y=round(e[2], 1), scale=e[3]) for e in p['emit']],
                     ends=[dict(x=q[0], y=q[1]) for q in p.get('ends', [])]) for n, p in data['pieces'].items()],
        loops=[dict(name=n, x=l['anchor'][0], y=l['anchor'][1], spread=l['spread'], kind=l['kind']) for n, l in data['loops'].items()])
    with open(out, 'w') as fh:
        fh.write('{\n "note": "MEASURED by Art/Worlds/Desert/backdrop_v3~/src~/measure_points.py -- do not edit by hand",\n')
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
                    col = (255, 255, 255, 255) if 'smoke' in e[0] or e[0] in ('steam_vent',) else (255, 255, 0, 255) if 'beacon' in e[0] or 'strobe' in e[0] else (0, 255, 255, 255)
                    d.line([(x - 8, y), (x + 8, y)], fill=col); d.line([(x, y - 8), (x, y + 8)], fill=col)
                for q in data['pieces'][n].get('ends', []):
                    d.ellipse([q[0] * 1.5 - 4, q[1] * 1.5 - 4, q[0] * 1.5 + 4, q[1] * 1.5 + 4], outline=(255, 0, 255, 255))
                sheet.alpha_composite(im, ((i % 4) * 384, (i // 4) * 384))
            sheet.save(os.path.join(dbg, 'points_' + atlas + '.png'))

if __name__ == '__main__':
    main()
