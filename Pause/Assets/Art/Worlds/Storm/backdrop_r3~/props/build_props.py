"""Build the Storm living props: graded 8-frame strips (transparent) in out2/bd_storm_prop_<name>/r1_<k>.png + meta.json.
PixelLab stills (painted, styled on Storm's own landmark atlas) get scripted lights (Frost's ground-light grammar: amber
beacons blink, windows flicker, cyan pulses, a white strobe, fires flicker) and a soft cast shadow on the deck; the vortex
and the lightning cell are cut from the ground paint itself (scripted.py). No pink / magenta anywhere (checked)."""
import os, sys, json, math
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.ndimage import gaussian_filter
import scripted
OUT = scripted.OUT
GROUND = np.array([.14, .19, .245])
AMBER = np.array([1, .74, .32]); GOLD = np.array([1, .84, .45]); CYAN = np.array([.5, .93, 1]); WHITE = np.array([.9, .95, 1])
FIRE = np.array([1, .6, .25]); VIOLET = np.array([.7, .62, 1])

def load(p): return np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255
def hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; v = a[..., :3].max(-1); mn = a[..., :3].min(-1); c = v - mn
    s = np.where(v > 0, c / np.maximum(v, 1e-6), 0); cc = np.maximum(c, 1e-6)
    h = np.where(v == r, ((g - b) / cc) % 6, np.where(v == g, (b - r) / cc + 2, (r - g) / cc + 4)) * 60
    return np.where(c > 1e-6, h, 0), s, v
def pink(a):
    h, s, v = hsv(a); return int(((a[..., 3] > .05) & (s > .25) & (v > .3) & (h >= 285) & (h <= 345)).sum())
def over(dst, src):
    sa = src[..., 3:4]; da = dst[..., 3:4]; oa = sa + da * (1 - sa)
    rgb = (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
    return np.concatenate([rgb, oa], -1)
def glow_layer(mask, col, sig, amp):
    g = gaussian_filter(mask.astype(float), sig); g = np.clip(g / max(g.max(), 1e-6) * amp, 0, 1)
    return np.dstack([np.broadcast_to(col, mask.shape + (3,)), g])
def comps(m, maxa=60, mina=1):
    lab, n = ndi.label(m, np.ones((3, 3))); out = []
    for i in range(1, n + 1):
        c = lab == i
        if mina <= c.sum() <= maxa: out.append(c)
    return out
def place(a, cell, fit=.82, dy=-.04):
    """crop to the drawing, fit it in a cell x cell canvas (room for its shadow down-right)"""
    bb = Image.fromarray((a[..., 3] * 255).astype(np.uint8)).getbbox(); a = a[bb[1]:bb[3], bb[0]:bb[2]]
    h, w = a.shape[:2]; s = min(1, cell * fit / max(h, w))
    if s < 1:
        pm = a.copy(); pm[..., :3] *= pm[..., 3:]
        im = Image.fromarray((pm * 255 + .5).astype(np.uint8), 'RGBA').resize((max(1, int(w * s)), max(1, int(h * s))), Image.LANCZOS)
        pm = np.asarray(im).astype(float) / 255; pm[..., :3] /= np.maximum(pm[..., 3:], 1e-6); a = np.clip(pm, 0, 1); h, w = a.shape[:2]
    o = np.zeros((cell, cell, 4)); y0 = int((cell - h) / 2 + dy * cell); x0 = int((cell - w) / 2 - .03 * cell)
    o[y0:y0 + h, x0:x0 + w] = a; return o
def grade(a, lm, target=.6, haze=.1, cloud_fix=True):
    h, s, v = hsv(a); al = a[..., 3] > .5; body = al & ~lm
    k = min(1.8, target / max(np.percentile(v[body], 90), 1e-3))
    rgb = a[..., :3] * k
    vv = rgb.max(-1, keepdims=True); soft = np.where(vv > .62, .62 + (vv - .62) * .35, vv)
    rgb = rgb * np.where(vv > 0, soft / np.maximum(vv, 1e-6), 0)
    rgb = rgb * (1 - haze) + GROUND * haze
    if cloud_fix:   # near-white puffs the generator drew -> the deck's own slate-blue cloud tone
        cl = (s < .18) & (v > .55) & ~lm
        L = rgb.mean(-1, keepdims=True); rgb = np.where(cl[..., None], L * np.array([.86, .95, 1.12]), rgb)
    o = a.copy(); o[..., :3] = np.where(lm[..., None], a[..., :3], np.clip(rgb, 0, 1)); return o
def shadow(a, d=(.04, .055), sig=5, alpha=.42):
    c = a.shape[0]; m = gaussian_filter(a[..., 3], sig)
    m = np.roll(m, (int(d[1] * c), int(d[0] * c)), (0, 1)) * alpha
    return np.dstack([np.broadcast_to(np.array([.02, .03, .06]), m.shape + (3,)), m])

COL = dict(amber=AMBER, cyan=CYAN, white=WHITE, green=np.array([.48, 1, .58]), gold=GOLD, violet=VIOLET)
HULL = dict(rust=(.62, .36, .22), ochre=(.78, .62, .3), olive=(.46, .53, .32), navy=(.3, .4, .62), teal=(.26, .52, .54),
            cream=(.82, .8, .72), orange=(.88, .48, .2), sand=(.72, .64, .5), sky=(.55, .7, .85), steel=None)
def tint(g, body, hull, st):
    if isinstance(hull, str): hull = HULL[hull]
    if hull is None or st <= 0: return g
    col = np.array(hull); h, s, v = hsv(g)
    L = g[..., :3] @ np.array([.3, .5, .2])
    w = np.clip((v - .07) / .1, 0, 1) * np.clip((.85 - v) / .2, 0, 1) * st     # painted panels, not the blacks or glints
    c = L[..., None] * col / (col @ np.array([.3, .5, .2]))
    o = g.copy(); m = (w * body)[..., None]; o[..., :3] = np.clip(g[..., :3] * (1 - m) + c * m, 0, 1); return o
def hsv2rgb(h, s, v):
    h = (h % 360) / 60; i = np.floor(h).astype(int) % 6; f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    r = np.choose(i, [v, q, p, p, t, v]); g = np.choose(i, [t, v, v, q, p, p]); b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], -1)
def lit(a, beacons=(), strobes=(), extra_amber=(), cyan_pulse=True, fires=False, seed=0, n=8, target=.6, lamp='amber', win_c='amber', glow_c='cyan', hull=None, st=.45):
    """beacons / strobes: (y, x) points in the cell to add a lamp at (the generator's own amber lamps are found too)"""
    h, s, v = hsv(a); al = a[..., 3] > .5
    amber = al & (s > .38) & (v > .55) & (h >= 24) & (h <= 60)
    cyan = al & (s > .3) & (v > .5) & (h >= 165) & (h <= 205)
    fire = al & (s > .45) & (v > .5) & (h >= 8) & (h < 24)
    A = comps(amber, 40); Cy = comps(cyan, 120); Fi = comps(fire, 80) if fires else []
    lm = np.zeros(a.shape[:2], bool)
    for c in A + Cy + Fi: lm |= c
    g = grade(a, lm, target); g = tint(g, (a[..., 3] > .5) & ~lm, hull, st)
    LA, WI, GC = COL[lamp], COL[win_c], COL[glow_c]
    rng = np.random.RandomState(seed)
    # the highest generator lamps blink as beacons, the rest are windows
    if A:
        ys = [np.nonzero(c)[0].mean() for c in A]; top = min(ys)
        bl = [c for c, y in zip(A, ys) if y <= top + 4][:2]
    else: bl = []
    win = [c for c in A if not any(c is b for b in bl)]
    flick = {id(c): rng.randint(n) for c in win}
    def dot(y, x, r=1.2):
        yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]; return (yy - y) ** 2 + (xx - x) ** 2 <= r * r
    frames = []
    for i in range(n):
        f = g.copy(); glow = np.zeros(a.shape[:2] + (4,))
        for c in win:
            k = {0: .35, 1: .8}.get((i - flick[id(c)]) % n, 1.0)
            f[c, :3] = np.minimum(1, WI * (.75 + .25 * k)) * k
        for c in bl + [dot(y, x) for (y, x) in beacons]:
            on = i in (0, 1)
            f[c, :3] = LA if on else LA * .3; f[c, 3] = 1
            if on: glow = over(glow, glow_layer(c, LA, 2.2, .75))
        for (y, x) in strobes:
            c = dot(y, x, 1.0); f[c, :3] = WHITE if i == 4 else WHITE * .35; f[c, 3] = 1
            if i == 4: glow = over(glow, glow_layer(c, WHITE, 2.6, .9))
        for c in Cy:
            k = .7 + .3 * math.cos(2 * math.pi * i / n)
            f[c, :3] = np.minimum(1, GC * k + .05); glow = over(glow, glow_layer(c, GC, 1.8, .35 * k))
        for c in Fi:
            k = rng.uniform(.6, 1.0)
            f[c, :3] = FIRE * k + .08; glow = over(glow, glow_layer(c, FIRE, 2.5, .5 * k))
        frames.append(over(f, glow))
    return frames, dict(beacons=len(bl) + len(beacons), windows=len(win), cyan=len(Cy), fires=len(Fi), strobes=len(strobes))

def with_shadow(frames, **kw):
    sh = shadow(frames[0], **kw); return [over(sh, f) for f in frames]

def jelly(a, n=8, seed=0, hue=None):
    h, s, v = hsv(a); al = a[..., 3] > .05
    gm = al & (s > .25) & (v > .42) & (h >= 170) & (h <= 275)
    g = grade(a, gm, target=.55, haze=.06, cloud_fix=False)
    src = a.copy(); hc = CYAN * .6 + VIOLET * .4
    if hue is not None:
        src[gm, :3] = hsv2rgb(np.full(gm.sum(), hue), s[gm], v[gm]); hc = hsv2rgb(np.array(hue), np.array(.55), np.array(1.))
    frames = []
    for i in range(n):
        k = .5 + .5 * math.cos(2 * math.pi * i / n)          # one slow breath per loop
        f = g.copy(); f[gm, :3] = np.clip(src[gm, :3] * (.75 + .45 * k), 0, 1)
        halo = glow_layer(gm, hc, 5, .38 * (.4 + .6 * k))
        frames.append(over(halo, f) if False else over(f, halo * np.array([1, 1, 1, .75])))
    return frames, dict(glow_px=int(gm.sum()))

def convoy(a, cell=384, n=8, liveries=(('steel', 'amber'), ('ochre', 'white'), ('navy', 'green'))):
    """three freighters in echelon with faint contrails, running lights chasing down the line"""
    bb = Image.fromarray((a[..., 3] * 255).astype(np.uint8)).getbbox(); s0 = a[bb[1]:bb[3], bb[0]:bb[2]]
    sz = int(cell * .3); h, w = s0.shape[:2]; sc = sz / max(h, w)
    pm = s0.copy(); pm[..., :3] *= pm[..., 3:]
    im = Image.fromarray((pm * 255 + .5).astype(np.uint8), 'RGBA').resize((int(w * sc), int(h * sc)), Image.LANCZOS)
    rot = im.rotate(-28, resample=Image.BICUBIC, expand=True)
    ship = np.asarray(rot).astype(float) / 255; ship[..., :3] /= np.maximum(ship[..., 3:], 1e-6); ship = np.clip(ship, 0, 1)
    SH = []
    for hull, lamp in liveries:
        fo, info = lit(ship, seed=3, target=.62, hull=None if hull == 'steel' else hull, lamp=lamp, win_c=lamp)
        SH.append([with_shadow([f], d=(.06, .08), sig=3, alpha=.4)[0] for f in fo])
    H, W = ship.shape[:2]; frames = []
    pos = [(int(cell * .08), int(cell * .5)), (int(cell * .36), int(cell * .3)), (int(cell * .62), int(cell * .1))]
    for i in range(n):
        o = np.zeros((cell, cell, 4))
        for j, (y, x) in enumerate(pos):
            # contrail: a soft fading streak behind (down-left of) each ship
            t = np.zeros((cell, cell)); cy, cx = y + H * .62, x + W * .38
            for k in range(60):
                yy, xx = int(cy + k * 1.6), int(cx - k * .85)
                if 0 <= yy < cell and 0 <= xx < cell: t[yy, xx] = (1 - k / 60) ** 1.5
            t = gaussian_filter(t, 1.6) * 3.2
            o = over(o, np.dstack([np.broadcast_to(np.array([.75, .82, .92]), t.shape + (3,)), np.clip(t, 0, .28)]))
            f = SH[j % len(SH)][(i - j * 2) % n]
            yy0, xx0 = max(0, y), max(0, x); hh, ww = min(H, cell - yy0), min(W, cell - xx0)
            o[yy0:yy0 + hh, xx0:xx0 + ww] = over(o[yy0:yy0 + hh, xx0:xx0 + ww], f[:hh, :ww])
        frames.append(o)
    return frames, info

def depink(a):
    """WorldCue.IsPink guard: any pixel in hue 285-352 with s > .25, v > .3 is pulled to violet 278 (never seen as pink)"""
    h, s, v = hsv(a); m = (a[..., 3] > 0) & (s > .2) & (v > .25) & (h >= 280) & (h <= 356)
    if m.any():
        o = a.copy(); o[m, :3] = hsv2rgb(np.full(m.sum(), 276.), s[m], v[m]); return o
    return a
def strip(frames, path): scripted.strip(frames, path)
LIT_MS = [125] * 8
def strobe_top(a):
    ys, xs = np.nonzero(a[..., 3] > .5); y = ys.min(); return [(y + 1, int(xs[ys <= y + 1].mean()))]
if __name__ == '__main__':
    M = {}
    R = lambda n, t: load(f'{OUT}/bd_storm_prop_{n}/raw/{t}_0.png')
    def save(name, k, frames, ms, info, note):
        frames = [depink(f) for f in frames]
        p = f'{OUT}/bd_storm_prop_{name}/r1_{k}.png'; strip(frames, p)
        M[f'{name}_r1_{k}'] = dict(frames=len(frames), cell=frames[0].shape[0], ms=ms, pink_px=max(pink(f) for f in frames), note=note, **info)
        print(name, k, M[f'{name}_r1_{k}'], flush=True)
    which = sys.argv[1:]
    want = lambda k: not which or k in which
    jobs = json.load(open('props/jobs.json'))
    for j in jobs:
        if not want(j['name']): continue
        a0 = R(j['name'], j['raw'])
        for vi, var in enumerate(j['variants']):
            k = j.get('k0', 0) + vi; note = f"{j['note']} | colour: {var}"
            if j['kind'] == 'lit':
                a = place(a0, 256, fit=j.get('fit', .82))
                st = strobe_top(a) if j.get('strobe_top') else []
                fr, info = lit(a, strobes=st, fires=j.get('fires', False), seed=j.get('seed', 0) + vi, target=j.get('target', .6),
                               lamp=var.get('lamp', 'amber'), win_c=var.get('win', var.get('lamp', 'amber')), glow_c=var.get('glow', 'cyan'),
                               hull=var.get('hull'), st=var.get('st', .45))
                save(j['name'], k, with_shadow(fr, alpha=j.get('shadow', .42)), LIT_MS, info, note)
            elif j['kind'] == 'jelly':
                a = place(a0, 256, fit=j.get('fit', .86))
                fr, info = jelly(a, hue=var.get('hue')); fr = with_shadow(fr, d=(.07, .1), sig=9, alpha=.22)
                save(j['name'], k, fr, [180] * 8, info, note)
            elif j['kind'] == 'convoy':
                fr, info = convoy(a0, liveries=[tuple(x) for x in var['liveries']]); save(j['name'], k, fr, LIT_MS, info, note)
    VOR = [('violet', (.72, .66, 1.0)), ('blue-white', (.82, .9, 1.0)), ('cyan', (.5, .9, 1.0))]
    if want('vortex'):
        for i, (nm, c) in enumerate(VOR):
            save('vortex', i, scripted.vortex(1, 1500, 420, seed=1, glow=c), scripted.VORTEX_MS, {}, f'storm-eye spiral cut from v1 paint, eyewall lightning {nm}; spin slowly in game')
        save('vortex', 3, scripted.vortex(3, 2600, 300, turns=.9, eye=.1, seed=2), scripted.VORTEX_MS, {}, 'looser night-side spiral from v3 paint, violet')
    CEL = [('violet', (.62, .58, 1.0), (.9, .93, 1.0)), ('blue-white', (.6, .72, 1.0), (.92, .96, 1.0)), ('cyan', (.4, .8, .95), (.85, 1.0, 1.0))]
    if want('cell'):
        for i, (nm, lo, hi) in enumerate(CEL):
            save('cell', i, scripted.cell(1 + (i % 2), 900 if i % 2 == 0 else 2000, 500 if i % 2 == 0 else 300, seed=i, lo=lo, hi=hi),
                 scripted.CELL_MS, {}, f'lightning inside a cloud tower (additive light overlay), {nm}')
    mp = 'props/meta.json'; old = json.load(open(mp)) if os.path.exists(mp) else {}
    old.update(M); json.dump(old, open(mp, 'w'), indent=1)
