#!/usr/bin/env python3
"""Ship size audit for Pause: silhouette + detail legibility in world units and screen px."""
import json, glob, os, sys, math, re
import numpy as np
from PIL import Image

ROOT = sys.argv[1]            # .../Pause/Assets
OUT = sys.argv[2]             # scratch dir
PXU_BIG, PXU_SMALL = 1080 / 7.44, 720 / 7.44   # screen px per world unit (width-fitted camera, 7.44 u wide)
PLAYER_U = 0.58 * 1.35        # flown hull, longest side, world units (gameS1)
ART = ROOT + '/Art/Resources/'

FRAME = {'R': .66, 'B': 1.3, 'F': .9, 'C': .95, 'A': .67}
# frameScale overrides (EnemyRoster.Scaled): parse from source
src = open(ROOT + '/Scripts/Gameplay/Enemies/EnemyRoster.cs').read()
roster = {}
for m in re.finditer(r'(Scaled\()?(Floating\()?Def\("([a-z0-9_]+)", "([^"]+)", ([RBFCAM]), (\d), (\d)', src):
    roster[m.group(3)] = dict(name=m.group(4), role=m.group(5), world=int(m.group(6)))
# frameScale values: look for the closing ", <num>f)" of Scaled(...) blocks
scales = {}
for m in re.finditer(r'Scaled\(Def\("([a-z0-9_]+)".*?\)\s*,\s*([0-9.]+)f\)', src, re.S):
    scales[m.group(1)] = float(m.group(2))

def load(p):
    return np.array(Image.open(p).convert('RGBA'))

def cell(a, i, h):
    return a[:, i * h:(i + 1) * h]

def bbox(alpha, thr=40):
    ys, xs = np.where(alpha > thr)
    if len(xs) == 0: return None
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1

def rgb2hsv(rgb):
    rgb = rgb.astype(np.float32) / 255
    mx = rgb.max(-1); mn = rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    return s, mx

def components(mask):
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    out = []
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not seen[y, x]:
                st = [(y, x)]; seen[y, x] = True; n = 0
                while st:
                    cy, cx = st.pop(); n += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True; st.append((ny, nx))
                out.append(n)
    return out

def analyse(c, world_cell):
    """c: RGBA cell array (h,h,4); world_cell: world units across the whole cell."""
    h = c.shape[0]
    a = c[..., 3]
    bb = bbox(a)
    if bb is None: return None
    x0, y0, x1, y1 = bb
    w, hh = x1 - x0, y1 - y0
    upx = world_cell / h                       # world units per art px
    long_u = max(w, hh) * upx
    sc_big, sc_small = upx * PXU_BIG, upx * PXU_SMALL   # screen px per art px
    solid = a > 128
    # outline: boundary pixels vs one ring inside
    er = solid.copy()
    er[1:, :] &= solid[:-1, :]; er[:-1, :] &= solid[1:, :]; er[:, 1:] &= solid[:, :-1]; er[:, :-1] &= solid[:, 1:]
    bound = solid & ~er
    er2 = er.copy()
    er2[1:, :] &= er[:-1, :]; er2[:-1, :] &= er[1:, :]; er2[:, 1:] &= er[:, :-1]; er2[:, :-1] &= er[:, 1:]
    ring2 = er & ~er2
    lum = (0.299 * c[..., 0] + 0.587 * c[..., 1] + 0.114 * c[..., 2])
    ol = float(lum[bound].mean()) if bound.any() else 0
    il = float(lum[ring2].mean()) if ring2.any() else 0
    deep = float(lum[er2].mean()) if er2.any() else 0
    pale = (ol - deep) > 28 and (ol - il) > 10
    # accent features inside the silhouette
    s, v = rgb2hsv(c[..., :3])
    acc = er2 & (((v > .82) & (s > .45)) | ((v > .93) & (s < .25)))
    comps = [n for n in components(acc) if n >= 3]
    comps.sort(reverse=True)
    diam = lambda n: 2 * math.sqrt(n / math.pi)
    top = comps[:3]
    key_small = [diam(n) * sc_small for n in top]
    lost_small = sum(1 for n in comps if diam(n) * sc_small < 3)
    return dict(w_u=w * upx, h_u=hh * upx, long_u=long_u, long_big=long_u * PXU_BIG, long_small=long_u * PXU_SMALL,
                fill=max(w, hh) / h, sc_big=sc_big, sc_small=sc_small, pale=bool(pale), ol=ol, il=il,
                area_u=float(solid.sum()) * upx * upx,
                n_acc=len(comps), top_diam_big=[diam(n) * sc_big for n in top], top_diam_small=key_small,
                lost_small=lost_small, outline_px_small=sc_small)

res = {'enemies': {}, 'elites': {}, 'mines': {}, 'bosses': {}}

# enemies
for key, r in sorted(roster.items()):
    if r['role'] == 'M':
        continue
    p = ART + 'Enemies/%s.png' % key
    if not os.path.exists(p):
        continue
    a = load(p)
    h = a.shape[0]
    role = r['role']
    wc = FRAME[role] * scales.get(key, 1.0)
    best = None
    for i in range(4):
        m = analyse(cell(a, i, h), wc)
        if m and (best is None or m['long_u'] > best['long_u']): best = m
    if best:
        best.update(name=r['name'], role=role, world=r['world'], cell_world=wc, cell_px=h, frameScale=scales.get(key, 1.0))
        res['enemies'][key] = best

# elites
for f in sorted(glob.glob(ROOT + '/Art/Resources/Elites/Defs/*.json')):
    d = json.load(open(f))
    key = d['key']
    wname = d['world'].capitalize()
    p = ART + 'Elites/%s/%s.png' % (wname, key)
    if not os.path.exists(p):
        print('missing', p); continue
    a = load(p); h = a.shape[0]
    cells = d.get('cells')
    idx = (cells or {}).get('flight') or [0, 1, 2, 3]
    best = None
    for i in idx:
        m = analyse(cell(a, i, h), d['cellWorldSize'])
        if m and (best is None or m['long_u'] > best['long_u']): best = m
    best.update(name=d['displayName'], world=d['world'], cell_world=d['cellWorldSize'], cell_px=h, hullRadius=d['hullRadius'],
                brain=d['brain'], attack=d['attack'])
    res['elites'][key] = best

# mines (cell 0 of the strip, drawn MineWidth)
for p in sorted(glob.glob(ART + 'Enemies/Mines/*.png')):
    a = load(p)
    h = a.shape[1] // 4 if a.shape[0] < a.shape[1] else a.shape[0]
    cw = min(a.shape[0], a.shape[1] // 4)
    c = a[:cw, :cw]
    bb = bbox(c[..., 3])
    if bb:
        w, hh = bb[2] - bb[0], bb[3] - bb[1]
        res['mines'][os.path.basename(p)] = dict(cell=cw, bbox=[int(w), int(hh)], shape=list(a.shape[:2]))

# bosses
for p in sorted(glob.glob(ART + 'Bosses/*.png')):
    n = os.path.basename(p)
    if any(k in n for k in ('_card', '_shots', '_fx', '_death', '_damage', 'warning')): continue
    a = load(p)
    cw = 384
    c = a[:cw, :cw]
    bb = bbox(c[..., 3])
    m = analyse(c, 3.3)
    res['bosses'][n] = dict(size=list(a.shape[:2]), m=m)

# player reference
a = load(ART + 'ShipArt/Hulls/Dove.png')
c = a[:, :768]
bb = bbox(c[..., 3])
res['player'] = dict(bbox=[int(v) for v in bb], long_u=PLAYER_U, long_big=PLAYER_U * PXU_BIG, long_small=PLAYER_U * PXU_SMALL)
json.dump(res, open(OUT + '/measure.json', 'w'), indent=1)
print({k: len(v) for k, v in res.items()})
