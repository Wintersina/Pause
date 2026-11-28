"""Storm props, MULTI-COLOUR redo (user 2026-11-28: real liveries, not single tints, not rainbow).
Base drawings: PixelLab repaints of the approved designs (raw/mc_0.png, made with the old raw as reference) already carry a
real multi-part livery (base + 1-2 accents + lights). Fleet variants re-assign the LIVERY colours part by part: each rule
picks one painted part by hue + saturation + value (orange band, yellow rails, teal stripe, red panels ...) and repaints
it to another believable livery colour (value / shading kept); steel, rust, soot, cream and the lamps are never touched.
Lamps: amber windows flicker, red aviation beacons blink, green / red nav lights steady, white strobes, cyan intakes pulse.
Jellies: an analogous glow gradient (core -> rim -> tentacle tips). Lightning: blue-white / violet-white / faint cyan
shifting subtly across the piece and over time."""
import os, sys, json, math, numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.ndimage import gaussian_filter
sys.path.insert(0, '.')
import build_props as bp, scripted
from build_props import hsv, hsv2rgb, over, glow_layer, comps, place, grade, with_shadow, depink, load, pink
OUT = scripted.OUT
RED = np.array([1, .2, .13]); GREEN = np.array([.35, 1, .45]); AMBER = bp.AMBER; WHITE = bp.WHITE; CYAN = bp.CYAN

def part(a, lo, hi, smin=.35, vmin=.35, vmax=1.01):
    h, s, v = hsv(a); hh = (h - lo) % 360 <= (hi - lo) % 360
    return (a[..., 3] > .4) & hh & (s >= smin) & (v >= vmin) & (v <= vmax)
def repaint(a, m, hue=None, sat=None, val=1.0, protect=None):
    """repaint part m: new hue (None = keep), saturation (None = keep, else absolute target mixed with the original's
    variation), value x val. Edges softened so the paint follows the drawing's shading."""
    if protect is not None: m = m & ~protect
    h, s, v = hsv(a); w = gaussian_filter(m.astype(float), .7); w = np.clip(w * 1.4, 0, 1) * (a[..., 3] > .3)
    nh = h if hue is None else np.full_like(h, hue)
    ns = s if sat is None else np.clip(sat * (.75 + .5 * s), 0, 1)
    nv = np.clip(v * val, 0, 1)
    rgb = hsv2rgb(nh, ns, nv); o = a.copy(); o[..., :3] = a[..., :3] * (1 - w[..., None]) + rgb * w[..., None]; return o
def lamps(a):
    """small bright lamp blobs, by colour (found on the base drawing; never repainted)"""
    h, s, v = hsv(a); al = a[..., 3] > .5; bright = al & (v > .78)
    k = {}
    k['amber'] = comps(bright & (s > .45) & (h >= 28) & (h <= 58), 14)
    k['red'] = comps(bright & (s > .55) & ((h <= 12) | (h >= 356)), 14)
    k['green'] = comps(bright & (s > .45) & (h >= 85) & (h <= 160), 14)
    k['cyan'] = comps(al & (v > .6) & (s > .3) & (h >= 165) & (h <= 205), 160)
    k['white'] = comps(bright & (s < .15) & (v > .93), 6)
    k['fire'] = comps(bright & (s > .5) & (h >= 10) & (h < 28), 60)
    return k
def animate(base, L, n=8, seed=0, strobe=(), fires=False, nav_steady=True):
    rng = np.random.RandomState(seed)
    lm = np.zeros(base.shape[:2], bool)
    for kk in L:
        for c in L[kk]: lm |= c
    g = grade(base, lm, target=.62, haze=.08, cloud_fix=False)
    # the highest red lamps are aviation beacons (blink); low red / green ones are nav lights (steady)
    reds = L['red']; tops = []
    if reds:
        ys = [np.nonzero(c)[0].mean() for c in reds]; t0 = min(ys); H = base.shape[0]
        tops = [c for c, y in zip(reds, ys) if y <= t0 + .12 * H]
    navs = [c for c in reds if not any(c is t for t in tops)]
    fl = {id(c): rng.randint(n) for c in L['amber']}
    def dot(y, x, r=1.0):
        yy, xx = np.mgrid[0:base.shape[0], 0:base.shape[1]]; return (yy - y) ** 2 + (xx - x) ** 2 <= r * r
    frames = []
    for i in range(n):
        f = g.copy(); gl = np.zeros(base.shape[:2] + (4,))
        for c in L['amber']:
            k = {0: .35, 1: .8}.get((i - fl[id(c)]) % n, 1.0); f[c, :3] = AMBER * k
            gl = over(gl, glow_layer(c, AMBER, 1.6, .35 * k))
        for c in tops:
            on = i in (0, 1); f[c, :3] = RED if on else RED * .3
            if on: gl = over(gl, glow_layer(c, RED, 2.2, .7))
        for c in navs:
            f[c, :3] = RED; gl = over(gl, glow_layer(c, RED, 1.6, .45))
        for c in L['green']:
            f[c, :3] = GREEN; gl = over(gl, glow_layer(c, GREEN, 1.6, .45))
        for c in L['cyan']:
            k = .7 + .3 * math.cos(2 * math.pi * i / n); f[c, :3] = np.minimum(1, CYAN * k + .05)
            gl = over(gl, glow_layer(c, CYAN, 1.8, .35 * k))
        for c in L['white'] + [dot(y, x) for (y, x) in strobe]:
            on = i == 4; f[c, :3] = WHITE if on else WHITE * .4; f[c, 3] = 1
            if on: gl = over(gl, glow_layer(c, WHITE, 2.6, .85))
        if fires:
            for c in L['fire']:
                k = rng.uniform(.6, 1); f[c, :3] = bp.FIRE * k + .08; gl = over(gl, glow_layer(c, bp.FIRE, 2.5, .5 * k))
        frames.append(over(f, gl))
    return frames, {k: len(v) for k, v in L.items()}

# ---- fleet liveries: (part selector, repaint) per variant -------------------------------------------------------------
# selectors: hue range + min sat / value, so painted parts are picked and rust / soot (low value) and cream (low sat) are not
ORANGE = (8, 30, .5, .5); YELLOW = (30, 60, .35, .3); TEAL = (160, 200, .35, .3); NAVY = (200, 245, .3, .15)
REDP = (350, 10, .45, .3); GREENP = (90, 160, .3, .2); REDDISH = (350, 14, .3, .25)
LIVERY = {
 'refinery': [('as painted: white tanks with an international-orange band, safety-yellow rails, colour-coded pipes', []),
              ('corporate teal band, yellow rails', [(ORANGE, dict(hue=183, sat=.62, val=.9))]),
              ('navy band, white-painted rails', [(ORANGE, dict(hue=218, sat=.6, val=.7)), (YELLOW, dict(sat=.08, val=1.1))]),
              ('safety-yellow band, orange rails', [(ORANGE, dict(hue=46, sat=.8, val=1.05)), (YELLOW, dict(hue=22, sat=.85))])],
 'skimmer':  [('as painted: navy hull, yellow stripe, orange engine pods', []),
              ('dark green hull, yellow stripe, grey pods', [(NAVY, dict(hue=150, sat=.45)), ((8, 30, .45, .15), dict(sat=.08, val=.85))]),
              ('charcoal hull, orange stripe, yellow pods', [(NAVY, dict(sat=.08)), (YELLOW, dict(hue=22, sat=.85)), ((8, 30, .45, .15), dict(hue=46, sat=.8))]),
              ('corporate teal hull, white stripe, orange pods', [(NAVY, dict(hue=186, sat=.5)), (YELLOW, dict(sat=.06, val=1.15))])],
 'aerostat': [('as painted: red/cream, teal/orange, navy/yellow envelopes', []),
              ('navy/cream, red/cream, green/gold', [(REDP, dict(hue=220, sat=.6, val=.8)), (TEAL, dict(hue=3, sat=.75)), (ORANGE, dict(sat=.12, val=1.1)),
                                                     (NAVY, dict(hue=145, sat=.55, val=1.05)), (YELLOW, dict(hue=44, sat=.7))]),
              ('orange/charcoal, red/gold, green/white', [(REDP, dict(hue=24, sat=.85)), (TEAL, dict(sat=.08, val=.55)), (NAVY, dict(hue=2, sat=.75, val=1.1)),
                                                          (YELLOW, dict(hue=44, sat=.75)), (ORANGE, dict(hue=140, sat=.55))]),
              ('green/white, navy/gold, red/cream', [(REDP, dict(hue=140, sat=.5)), (TEAL, dict(hue=222, sat=.6, val=.75)), (ORANGE, dict(hue=44, sat=.75)),
                                                     (NAVY, dict(hue=3, sat=.75, val=1.15))])],
 'wreck':    [('as painted: faded red / cream stripes, green gondola', []),
              ('faded navy / cream, grey gondola', [(REDDISH, dict(hue=215, sat=.35, val=.85)), (GREENP, dict(sat=.08))]),
              ('faded ochre / cream, red gondola', [(REDDISH, dict(hue=40, sat=.45)), (GREENP, dict(hue=4, sat=.5))]),
              ('faded green / cream, navy gondola', [(REDDISH, dict(hue=140, sat=.3, val=.9)), (GREENP, dict(hue=218, sat=.4))])],
 'mast':     [('as painted: red / white aviation sections, yellow-black hazard pontoon', []),
              ('international orange / white, yellow-black pontoon', [(REDDISH, dict(hue=18, sat=.85))]),
              ('red / white, orange-black pontoon', [(YELLOW, dict(hue=24, sat=.85))]),
              ('galvanised grey mast, yellow-black pontoon', [(REDDISH, dict(sat=.06, val=.95))])],
 'cargo':    [('as painted: cream envelope, teal band, orange nose, mixed containers', []),
              ('cream, navy band, yellow nose', [(TEAL, dict(hue=220, sat=.6, val=.75)), (ORANGE, dict(hue=46, sat=.8))]),
              ('cream, red band, charcoal nose', [(TEAL, dict(hue=3, sat=.7, val=.9)), (ORANGE, dict(sat=.06, val=.5))]),
              ('cream, green band, orange nose', [(TEAL, dict(hue=140, sat=.5, val=.85))])],
}
def livery(a, rules, protect):
    o = a.copy()
    sel = [(part(a, *s), r) for s, r in rules]          # select on the ORIGINAL drawing, so rules never chain
    for m, r in sel: o = repaint(o, m, protect=protect, **r)
    return o

# ---- jellies: analogous glow gradients ------------------------------------------------------------------------------------
JELLY = [('cyan core -> teal rim -> green tips', 190, 172, 140, None),
         ('blue-white core -> blue rim -> violet tips', 205, 222, 262, None),
         ('gold-green core -> green rim -> teal tips, gold rim spots', 88, 138, 175, 44),
         ('cyan core -> blue rim -> blue-violet tips, pale gold spots', 192, 212, 245, 48)]
def jelly_mc(a, scheme, bell_frac, n=8):
    name, hA, hB, hC, spot = scheme
    h, s, v = hsv(a); al = a[..., 3] > .05
    ys, xs = np.nonzero(al); y0, y1 = ys.min(), ys.max(); yb = y0 + bell_frac * (y1 - y0)
    bell = al & (np.arange(a.shape[0])[:, None] <= yb)
    cy = y0 + .45 * (yb - y0); cx = xs[ys <= yb].mean(); R = max(1, (xs[ys <= yb].max() - xs[ys <= yb].min()) / 2)
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    r = np.clip(np.hypot((xx - cx) / R, (yy - cy) / (R * .8)), 0, 1)
    t = np.clip((yy - yb) / max(1, y1 - yb), 0, 1)
    hue = np.where(bell, hA + (hB - hA) * r ** 1.3, hB + (hC - hB) * t ** .8)
    glowm = al & (s > .2) & (v > .35)
    tint_body = al & ~glowm
    o = a.copy()
    o[..., :3] = np.where(glowm[..., None], hsv2rgb(hue, np.clip(s * 1.0, 0, .72), v), o[..., :3])
    # the translucent body takes a little of the same hue so the bell and arms read as one creature
    o[..., :3] = np.where(tint_body[..., None], o[..., :3] * .8 + hsv2rgb(hue, np.full_like(s, .45), v) * .2, o[..., :3])
    spots = []
    if spot is not None:
        rim = bell & (r > .7) & (v > .6)
        sp = comps(rim & (s < .5), 6)
        spots = sp[::2]
        for c in spots: o[c, :3] = hsv2rgb(np.array(spot), np.array(.65), np.array(1.))
    g = grade(o, glowm, target=.55, haze=.05, cloud_fix=False)
    frames = []
    for i in range(n):
        k = .5 + .5 * math.cos(2 * math.pi * i / n)
        f = g.copy(); f[glowm, :3] = np.clip(o[glowm, :3] * (.75 + .45 * k), 0, 1)
        core = glowm & bell & (r < .55)
        halo = glow_layer(core, hsv2rgb(np.array(hA), np.array(.5), np.array(1.)), 6, .32 * (.4 + .6 * k))
        f = over(f, halo)
        for c in spots: f = over(f, glow_layer(c, hsv2rgb(np.array(spot), np.array(.6), np.array(1.)), 1.4, .5 * k))
        frames.append(f)
    return frames, dict(spots=len(spots), scheme=name)

# ---- lightning: realistic electric colours shifting across the piece and over time -----------------------------------
EL = dict(bw=np.array([.84, .91, 1.]), vw=np.array([.78, .74, 1.]), cy=np.array([.66, .92, 1.]))
def elec_field(N, seed, mix, i, n=8):
    rng = np.random.default_rng(seed)
    w = [gaussian_filter(rng.standard_normal((N, N)), N / 6, mode='wrap') for _ in range(3)]
    ph = 2 * np.pi * i / n
    w = [x * mix[j] + .6 * np.sin(ph + j * 2.1) * x.std() for j, x in enumerate(w)]   # weights drift a little each frame
    e = np.exp(np.stack(w, -1) / (np.std(np.stack(w)) + 1e-6) * 1.2); e /= e.sum(-1, keepdims=True)
    return e[..., 0:1] * EL['bw'] + e[..., 1:2] * EL['vw'] + e[..., 2:3] * EL['cy'] * .9
def vortex_mc(v, cy, cx, seed, mix, **kw):
    fr = scripted.vortex(v, cy, cx, seed=seed, glow=(1, 1, 1), **kw)
    base = scripted.vortex(v, cy, cx, seed=seed, glow=(0, 0, 0), **kw)
    out = []
    for i, (f, b) in enumerate(zip(fr, base)):
        lit_ = f[..., :3] - b[..., :3]; col = elec_field(f.shape[0], seed, mix, i)
        o = f.copy(); o[..., :3] = np.clip(b[..., :3] + lit_ * col, 0, 1); out.append(o)
    return out
def cell_mc(v, cy, cx, seed, mix):
    fr = scripted.cell(v, cy, cx, seed=seed, lo=(1, 1, 1), hi=(1, 1, 1)); out = []
    for i, f in enumerate(fr):
        col = elec_field(f.shape[0], seed + 7, mix, i)
        hot = np.clip(f[..., 3:] * 2, 0, 1)
        o = f.copy(); o[..., :3] = col * (1 - .4 * hot) + .4 * hot; out.append(o)
    return out

def convoy_mc(ships, L, cell=384, n=8):
    seq = []
    for b in ships:
        fr, _ = animate(b, L, seed=len(seq)); seq.append(fr)
    def shrink(f):
        bb = Image.fromarray((f[..., 3] * 255).astype(np.uint8)).getbbox(); f = f[bb[1]:bb[3], bb[0]:bb[2]]
        h, w = f.shape[:2]; sc = cell * .3 / max(h, w); pm = f.copy(); pm[..., :3] *= pm[..., 3:]
        im = Image.fromarray((pm * 255 + .5).astype(np.uint8), 'RGBA').resize((int(w * sc), int(h * sc)), Image.LANCZOS).rotate(-28, resample=Image.BICUBIC, expand=True)
        o = np.asarray(im).astype(float) / 255; o[..., :3] /= np.maximum(o[..., 3:], 1e-6); return np.clip(o, 0, 1)
    seq = [[with_shadow([shrink(f)], d=(.06, .08), sig=3, alpha=.4)[0] for f in fr] for fr in seq]
    H, W = seq[0][0].shape[:2]; out = []
    pos = [(int(cell * .08), int(cell * .5)), (int(cell * .36), int(cell * .3)), (int(cell * .62), int(cell * .1))]
    for i in range(n):
        o = np.zeros((cell, cell, 4))
        for j, (y, x) in enumerate(pos):
            t = np.zeros((cell, cell)); cy, cx = y + H * .62, x + W * .38
            for k in range(60):
                yy, xx = int(cy + k * 1.6), int(cx - k * .85)
                if 0 <= yy < cell and 0 <= xx < cell: t[yy, xx] = (1 - k / 60) ** 1.5
            t = gaussian_filter(t, 1.6) * 3.2
            o = over(o, np.dstack([np.broadcast_to(np.array([.75, .82, .92]), t.shape + (3,)), np.clip(t, 0, .28)]))
            f = seq[j][(i - j * 2) % n]; hh, ww = min(f.shape[0], cell - y), min(f.shape[1], cell - x)
            o[y:y + hh, x:x + ww] = over(o[y:y + hh, x:x + ww], f[:hh, :ww])
        out.append(o)
    return out
def strip(name, k, frames, ms, info, note, M):
    frames = [depink(f) for f in frames]
    p = f'{OUT}/bd_storm_prop_{name}/r1_{k}.png'; scripted.strip(frames, p)
    # pink audit AFTER the game's grade too (Storm draws its ground pieces at lift StormTuning.Brightness x VariantBrightness <= 1)
    M[f'{name}_r1_{k}'] = dict(frames=len(frames), cell=frames[0].shape[0], ms=ms, pink_px=max(pink(f) for f in frames), note=note, **info)
    print(name, k, M[f'{name}_r1_{k}']['pink_px'], note, flush=True)
if __name__ == '__main__':
    import glob
    which = sys.argv[1:]; want = lambda k: not which or k in which
    M = {}
    for name in LIVERY:
        if not want(name) and not (name == 'cargo' and want('convoy')): continue
        a0 = place(load(f'{OUT}/bd_storm_prop_{name}/raw/mc_0.png'), 256, fit=.84)
        L = lamps(a0); prot = np.zeros(a0.shape[:2], bool)
        for kk in L:
            for c in L[kk]: prot |= ndi.binary_dilation(c, iterations=1)
        bases = [livery(a0, rules, prot) for _, rules in LIVERY[name]]
        if name == 'cargo':
            for f in glob.glob(f'{OUT}/bd_storm_prop_convoy/r1_*.png'): os.remove(f)
            # convoy: three freighters of one line, each in a fleet livery; variants = different fleet mixes
            for k, combo in enumerate([(0, 1, 2), (1, 1, 1), (3, 0, 2)]):
                fr = convoy_mc([bases[c] for c in combo], L); strip('convoy', k, fr, bp.LIT_MS, {}, 'freighter line: ' + ' / '.join(LIVERY['cargo'][c][0] for c in combo), M)
            continue
        for f in glob.glob(f'{OUT}/bd_storm_prop_{name}/r1_*.png'): os.remove(f)
        for k, (b, (desc, _)) in enumerate(zip(bases, LIVERY[name])):
            fr, info = animate(b, L, seed=k, fires=(name == 'wreck'))
            strip(name, k, with_shadow(fr, alpha=.4), bp.LIT_MS, info, desc, M)
    if want('jelly'):
        for f in glob.glob(f'{OUT}/bd_storm_prop_jelly/r1_*.png'): os.remove(f)
        k = 0
        for raw, bf in (('r2', .48), ('r1', .62)):
            a = place(load(f'{OUT}/bd_storm_prop_jelly/raw/{raw}_0.png'), 256, fit=.86)
            for sc in (JELLY if raw == 'r2' else JELLY[:3]):
                fr, info = jelly_mc(a, sc, bf); fr = with_shadow(fr, d=(.07, .1), sig=9, alpha=.2)
                strip('jelly', k, fr, [180] * 8, info, f'{raw} design: {sc[0]}', M); k += 1
    if want('vortex'):
        for k, (mix, v, cy, cx, kw) in enumerate([((1, .6, .3), 1, 1500, 420, {}), ((.5, 1, .3), 1, 1500, 420, {}),
                                                    ((.8, .4, .8), 3, 2600, 300, dict(turns=.9, eye=.1))]):
            strip('vortex', k, vortex_mc(v, cy, cx, k + 1, mix, **kw), scripted.VORTEX_MS, {}, f'eyewall lightning, blue-white/violet-white/cyan mix {mix}', M)
    if want('cell'):
        for k, (mix, v, cy, cx) in enumerate([((1, .7, .3), 1, 900, 500), ((.5, 1, .4), 2, 2000, 300), ((.8, .5, .9), 1, 900, 500)]):
            strip('cell', k, cell_mc(v, cy, cx, k, mix), scripted.CELL_MS, {}, f'lightning in a cloud tower, electric mix {mix}', M)
    mp = 'props/meta.json'; old = json.load(open(mp)) if os.path.exists(mp) else {}
    old = {k: v for k, v in old.items() if k.split('_r1_')[0] not in {n.split('_r1_')[0] for n in M}}
    old.update(M); json.dump(old, open(mp, 'w'), indent=1)
