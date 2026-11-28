#!/usr/bin/env python3
"""Ember's starship MOLTEN SCAR decals: Backdrop3/scar.png + scar.json (+ scar_meta.json with the constants for the C#).
Procedural (no generation), one decal per starship variant (scar_0..3, different noise): a long gouged FURROW trailing from the
impact down-left along the wreck's lava stream (~1.9 hull lengths), a scorched halo, black rubble banked at its sides, a molten
core and crack web that is hottest at the impact and cools to dull red / black along the furrow, radial fracture cracks round the
impact, hull debris chips, soot. Feathered to zero alpha inside the decal (no rectangle). Glows kept orange / yellow (never pink)
and held under the shot line (peak value .78).
Geometry in the starship's 256 px cell px (x right, y DOWN from the top-left): impact CENTRE, trail direction U, decal box X0,Y0,SPAN."""
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, distance_transform_edt
HERE = Path(__file__).resolve().parent
OUT = Path(sys.argv[sys.argv.index('--out') + 1]) if '--out' in sys.argv else HERE.parents[2] / 'Backgrounds' / 'Resources' / 'Worlds' / 'Ember' / 'Backdrop3'
N = 768; X0, Y0, SPAN = -230.0, 20.0, 560.0; K = N / SPAN
CENTRE = np.array([135., 125.]); U = np.array([-.77, .64]); U /= np.hypot(*U); HL = 180.0; LEN = HL * 1.9
def noise(r, sig, n=N):
    z = gaussian_filter(r.standard_normal((n, n)), sig); return (z - z.mean()) / (z.std() + 1e-9)
def decal(seed):
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:N, 0:N].astype(float); px = X0 + xx / K; py = Y0 + yy / K          # cell px
    d = np.stack([px - CENTRE[0], py - CENTRE[1]], -1)
    t = (d @ U) / LEN                                                                    # 0 at the impact, 1 at the far end
    perp = np.abs(d[..., 0] * -U[1] + d[..., 1] * U[0])                                  # distance from the axis, cell px
    warp = noise(r, 9) * 4.0 + noise(r, 3) * 1.5
    width = (30 * (1 - .55 * np.clip(t, 0, 1)) + warp)
    along = np.clip(1 - np.maximum(t - .6, 0) / .4, 0, 1) * np.clip((t + .12) / .18, 0, 1)   # tail fade; short lead-in behind the impact
    fur = np.clip(1 - perp / np.maximum(width, 1), 0, 1) * (t > -.12) * along
    rad = np.hypot(d[..., 0], d[..., 1])
    halo = np.clip(1 - perp / (width * 2.1 + 6), 0, 1) * (t > -.25) * np.clip(1 - np.maximum(t - .7, 0) / .3, 0, 1)
    halo = np.maximum(halo, np.clip(1 - rad / 90, 0, 1) * .8)
    halo = gaussian_filter(halo, 2.5)
    # fracture web: ridged noise, wide near the impact, narrow far
    rid = 1 - np.abs(noise(r, 3.2)); rid2 = 1 - np.abs(noise(r, 1.8))
    reach = np.clip(1 - rad / 125, 0, 1) + np.clip(1 - perp / (width * 1.6 + 4), 0, 1) * along * .9
    cr = (np.maximum(rid, rid2 * .9) > .885) * np.clip(reach, 0, 1)
    cr = gaussian_filter(cr.astype(float), .7)
    heat = np.clip(1 - np.maximum(t, 0) * 1.25, 0, 1) * np.clip(.35 + fur, 0, 1)            # hottest at the impact
    core = np.clip(fur * 1.7 - .35, 0, 1) * (.45 + .55 * heat) * np.clip(.75 + .35 * noise(r, 2), 0, 1.2)
    # colours (RGB 0..1)
    dark = np.array([.075, .058, .05]); rub = np.array([.17, .145, .13]); red = np.array([.70, .20, .06]); org = np.array([.80, .42, .10]); yel = np.array([.80, .66, .22])
    rgb = np.zeros((N, N, 3)); a = np.zeros((N, N))
    # scorched halo
    a = np.maximum(a, halo * .62); rgb[:] = dark
    # banked rubble speckle at the furrow's flanks
    flank = np.clip(1 - np.abs(perp - width * 1.25) / (width * .55 + 3), 0, 1) * along * (t > -.1)
    sp = noise(r, .9) > .85
    rub_m = np.clip(flank * (.35 + .65 * sp), 0, 1)
    rgb = rgb * (1 - rub_m[..., None]) + rub * rub_m[..., None]; a = np.maximum(a, rub_m * .9)
    # furrow floor: scorched basalt
    fl = np.clip(fur * 1.5, 0, 1); rgb = rgb * (1 - fl[..., None]) + np.array([.05, .04, .04]) * fl[..., None]; a = np.maximum(a, fl * .92)
    # crack web and molten core: red -> orange -> yellow by heat
    glow = np.clip(np.maximum(cr * (.45 + .55 * heat), core), 0, 1)
    col = red[None, None] * (1 - np.clip(glow * 1.6 - .2, 0, 1)[..., None]) + org[None, None] * np.clip(glow * 1.6 - .2, 0, 1)[..., None]
    hot = np.clip((glow - .6) / .4, 0, 1)[..., None]; col = col * (1 - hot) + yel * hot
    rgb = rgb * (1 - glow[..., None]) + col * glow[..., None]; a = np.maximum(a, glow * .95)
    # debris chips along the furrow
    ch = np.zeros((N, N), bool)
    for _ in range(70):
        tt = r.uniform(.05, .85); off = r.normal(0, width.mean() * .6)
        p = CENTRE + U * LEN * tt + np.array([-U[1], U[0]]) * off
        cx, cy = (p[0] - X0) * K, (p[1] - Y0) * K; rr = r.uniform(1.5, 4)
        ch |= (xx - cx) ** 2 + (yy - cy) ** 2 < rr * rr
    rgb = np.where(ch[..., None], np.array([.30, .27, .25]), rgb); a = np.maximum(a, ch * .9)
    # soot smear beyond the impact (up-right: behind the bow)
    # feather everything to zero inside the box
    edge = np.minimum(np.minimum(xx, N - 1 - xx), np.minimum(yy, N - 1 - yy)); fe = np.clip(edge / 60, 0, 1) ** 1.3
    a = np.clip(a * fe, 0, 1)
    # peak value held under the shot line
    v = rgb.max(-1, keepdims=True); rgb = rgb * np.minimum(1, .78 / np.maximum(v, 1e-3))
    return np.dstack([rgb, a])
def boxes():
    out = []
    for tt, hs in ((.14, 59), (.38, 53), (.62, 46), (.86, 40)):
        p = CENTRE + U * LEN * tt
        out.append([round(float(p[0] - 128), 1), round(float(128 - p[1]), 1), hs])          # cell px from the cell centre, y up
    return out
if __name__ == '__main__':
    cells = [decal(11 + 7 * i) for i in range(4)]
    W = 2 * N; Hh = 2 * N; sheet = np.zeros((Hh, W, 4)); sprites = []
    for i, c in enumerate(cells):
        x, y = (i % 2) * N, (i // 2) * N; sheet[y:y + N, x:x + N] = c
        sprites.append(dict(n=f'scar_{i}', x=x, y=Hh - y - N, w=N, h=N, px=.5, py=.5))
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA').save(OUT / 'scar.png', optimize=True)
    json.dump(dict(sheetW=W, sheetH=Hh, sprites=sprites), open(OUT / 'scar.json', 'w'), indent=0)
    cx, cy = (X0 + SPAN / 2) - 128, 128 - (Y0 + SPAN / 2)
    meta = dict(ScarSpan=SPAN, ScarTexels=N, ScarCentre=[round(cx, 1), round(cy, 1)], ScarBelow=round((Y0 + SPAN * .72 - 128) / 256, 2), ScarAbove=round((128 - (Y0 + 40)) / 256, 2),
                ScarBoxes=boxes(), ScarMargin=.06, note='cell px from the starship cell centre, y up; one decal per variant: scar_<variant>')
    json.dump(meta, open('/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl/ember/r1/scar_meta.json', 'w'), indent=1)
    print(meta)
