#!/usr/bin/env python3
"""Tide weather atlas: no cloud is cut flat. Slivers that Codex's sheet slicing left on a cell's edge (flat top, a long
straight opaque run) are dropped whole by connected component; a cloud whose own edge is cut flat gets that edge re-scalloped
into 4 px puffs like the rest of the cloud. Mirrors TideCloudCutTest. Run: python3 fix_weather_cuts.py <weather.png> <weather.json>"""
import sys, json, numpy as np
from PIL import Image
from scipy import ndimage as ndi
BORDER, FLAT = 8, 20
png, js = sys.argv[1], sys.argv[2]
a = np.array(Image.open(png).convert('RGBA')); H = a.shape[0]
rng = np.random.RandomState(7)

def run(v):
    best = cur = 0
    for x in v:
        cur = cur + 1 if x else 0
        best = max(best, cur)
    return best

def edges(m):
    ys, xs = np.where(m)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    return y0, y1, x0, x1, {'t': run(m[y0]), 'b': run(m[y1]), 'l': run(m[:, x0]), 'r': run(m[:, x1])}

def scallop(cell, comp, side):
    """re-cut the flat 'bottom' edge of comp (cell rotated so the flat side is down): below a line 32 px above the old edge a
    pixel survives only inside one of a row of round puffs (radius 10..24, centres snapped to 4 px, spaced about a radius
    apart), so the underside ends in rounded puffs of different sizes like the cloud's other edges"""
    y0, y1, x0, x1, _ = edges(comp)
    cols = np.where(comp[y1])[0]
    xa, xb = cols.min(), cols.max()
    yy, xx = np.mgrid[0:cell.shape[0], 0:cell.shape[1]]
    keep = np.zeros(comp.shape, bool)
    x = xa - 8
    while x <= xb + 8:
        r = int(rng.randint(3, 7)) * 4 - 2
        cy = (y1 - 6 - int(rng.randint(0, 4)) * 4) - r
        keep |= ((xx - x) ** 2 + (yy - cy) ** 2) <= r * r
        x += int(r * 0.9) // 4 * 4 + 4
    zone = comp & (yy > y1 - 32) & (xx >= xa - 8) & (xx <= xb + 8)
    cell[zone & ~keep, 3] = 0
    return cell

d = json.load(open(js))
for s in d['sprites']:
    iy = H - s['y'] - s['h']; ix = s['x']
    cell = a[iy:iy + s['h'], ix:ix + s['w']].copy()
    for _ in range(200):
        lab, n = ndi.label(cell[:, :, 3] > BORDER, structure=np.ones((3, 3)))
        changed = False
        for i in range(1, n + 1):
            m = lab == i
            if m.sum() < 10: continue
            y0, y1, x0, x1, e = edges(m)
            side = max(e, key=e.get)
            margin = y0 <= 16
            if m.sum() < 1500 and margin:
                cell[m, 3] = 0          # speck of a neighbouring cloud on the cell margin
                changed = True
                break
            if e[side] <= FLAT: continue
            if (m.sum() < 14000 and side in 'tlr') or m.sum() < 700:
                cell[m, 3] = 0          # fragment of a neighbouring cloud: drop whole
            else:
                k = {'b': 0, 't': 2, 'r': 1, 'l': 3}[side]
                rc = np.rot90(cell, k).copy(); rm = np.rot90(m, k).copy()
                rc = scallop(rc, rm, side)
                cell = np.rot90(rc, -k).copy()
            changed = True
            break
        if not changed: break
    # drop specks left behind
    lab, n = ndi.label(cell[:, :, 3] > BORDER, structure=np.ones((3, 3)))
    for i in range(1, n + 1):
        if (lab == i).sum() < 24: cell[lab == i, 3] = 0
    a[iy:iy + s['h'], ix:ix + s['w']] = cell
Image.fromarray(a).save(png)
