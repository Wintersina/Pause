"""Ember r1 install (2026-11-28, user-approved): the PixelLab-repainted ground at the .49 altitude (TileZoom .98), its class and context
grids, the living-props atlases, the starship crash scar and the soft painted weather, written into Resources/Worlds/Ember/Backdrop3.

  python3 build_r1.py [SP] [ground|props|weather|scar]   SP: a folder holding ember/r1/tiles and out2/bd_ember_* (default: ./src)

  v<n>/mid.png  the stitched tile (896 x 4096 RGBA; exposed down to the shot line)   v<n>/mask.txt  W lava / C rough crust / O open (v2: B slabs)
  v<n>/ctx.txt  context flags per cell, one hex digit: 1 flat, 2 rocky, 4 shore (beside lava), 8 open
  v<n>/props.png + props.json  <kind>_<variant>_<frame> cut to the kind's union box at its drawn size on a 1080 px screen
  scar.png + scar.json  the crashed starship's molten scar (scar_0..3)   weather.png  soft painted redo under the old sprite names
"""
import sys, os, json, glob, re, shutil, numpy as np
from PIL import Image
from scipy.ndimage import uniform_filter, binary_dilation

SP = sys.argv[1] if len(sys.argv) > 1 and os.path.isdir(sys.argv[1]) else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src')
HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.normpath(os.path.join(HERE, '../../../Backgrounds/Resources/Worlds/Ember/Backdrop3'))
R1 = f'{SP}/ember/r1'; OUT = f'{SP}/out2'
PICK = {1: 'D', 2: 'D', 3: 'D', 4: 'D'}      # D = the designed tile (pads, roads, lava flows painted into the A tile: ember/r1/design.py)
PX = dict(geoplant=190, foundry=180, miningrig=170, orehauler=190, bunker=160, quarry=170, stilts=170, domes=180, crawler=140, columns=160,
          springs=160, vents=150, starship=300, volcano=350, caldera=360, spires=330)
GROUND_ZOOM, APPROVED_ZOOM = .49, .41
ALT = GROUND_ZOOM / APPROVED_ZOOM
PX = {k: int(round(p * ALT)) for k, p in PX.items()}
MOUNTAINS = ('volcano', 'caldera', 'spires')
STILL = MOUNTAINS + ('columns',)
SHEET_W = 2048
PAD = 2


def is_pink(rgb, wide=False):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = m & (mx == r); gg = m & (mx == g) & ~rr; bb = m & ~rr & ~gg
    h[rr] = (60 * ((g - b)[rr] / d[rr])) % 360
    h[gg] = 60 * ((b - r)[gg] / d[gg]) + 120
    h[bb] = 60 * ((r - g)[bb] / d[bb]) + 240
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    if wide: return (h >= 280) & (h <= 356) & (s > .2) & (mx > .28)         # a margin round the band (8-bit / ASTC drift)
    return (h >= 285) & (h <= 352) & (s > .25) & (mx > .3)


def resize_premul(a, w, h):
    """RGBA float (0..1) resized with premultiplied alpha (no dark fringes)"""
    pm = a.copy(); pm[..., :3] *= pm[..., 3:4]
    out = np.zeros((h, w, 4))
    for c in range(4):
        out[..., c] = np.asarray(Image.fromarray(pm[..., c].astype(np.float32), 'F').resize((w, h), Image.LANCZOS))
    out = np.clip(out, 0, 1)
    al = out[..., 3:4]
    out[..., :3] = np.where(al > 1e-4, out[..., :3] / np.maximum(al, 1e-4), 0)
    return np.clip(out, 0, 1)


def night(a, mountain):
    """v4 (the night side): the props drawn darker, lamps / fires / lava (v > .8, s > .25) keep full strength; mountains' aprons feathered"""
    rgb = a[..., :3]; v = rgb.max(-1); s = (v - rgb.min(-1)) / np.maximum(v, 1e-3)
    lit = np.clip((v - .8) / .15, 0, 1) * np.clip((s - .25) / .3, 0, 1)
    if mountain: lit[:] = 0
    d = rgb * np.array([.72, .68, .68])
    o = a.copy(); o[..., :3] = d * (1 - lit[..., None]) + rgb * lit[..., None]
    if mountain: o[..., 3] = o[..., 3] ** 1.5
    return o


def depink(path):
    """grey every texel inside WorldCue.IsPink (8-bit rounding can push a graded edge texel back into the band); idempotent"""
    im = np.asarray(Image.open(path).convert('RGBA')).copy()
    a = im.astype(float) / 255
    pk = is_pink(a[..., :3]) & (a[..., 3] > 0)
    if pk.any():
        g = np.floor(im[..., :3][pk].astype(float).mean(-1, keepdims=True)).astype(np.uint8)
        im[..., :3][pk] = g
        Image.fromarray(im).save(path, optimize=True)
    return int(pk.sum())


def warm_fix(path):
    """a hot red that drifted into WorldCue.IsPink (hue 285-352, s > .25, v > .3) is turned to red-orange (hue 8) at its own s / v: lava and glows never go pink"""
    im = np.asarray(Image.open(path).convert('RGBA')).copy(); a = im.astype(float) / 255
    pk = is_pink(a[..., :3], wide=True) & (a[..., 3] > 0)
    if pk.any():
        rgb = a[..., :3][pk]; mx = rgb.max(-1); mn = rgb.min(-1)
        h = np.full(mx.shape, 8 / 60.); s_ = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
        i = 0; f = h; p = mx * (1 - s_); q = mx * (1 - f * s_); t = mx * (1 - (1 - f) * s_)
        out = np.stack([mx, t, p], -1)
        im[..., :3][pk] = np.clip(np.round(out * 255), 0, 255).astype(np.uint8); Image.fromarray(im).save(path, optimize=True)
    return int(pk.sum())


def guard(path, low=.18):
    """ASTC drifts a texel's hue / value a few steps, so a texel just outside WorldCue.IsPink (hue 285-352, s > .25, v > .3) can
    come out of the importer inside it (CI read 2 / 6 such texels in the v1 / v2 props). Keep a margin round the band: any texel
    (any alpha) with hue 270-360, saturation > .2 and value > `low` is moved out -- 340-360 (the reds of the lamps' rims) turned
    to pure red, the rest (dusky violet shadow) brought to saturation .18 at its own value. Idempotent."""
    im = np.asarray(Image.open(path).convert('RGBA')).copy()
    a = im[..., :3].astype(float) / 255
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); d = mx - mn
    h = np.zeros_like(mx); m = d > 1e-6
    rr = m & (mx == r); gg = m & (mx == g) & ~rr; bb = m & ~rr & ~gg
    h[rr] = (60 * ((g - b)[rr] / d[rr])) % 360
    h[gg] = 60 * ((b - r)[gg] / d[gg]) + 120
    h[bb] = 60 * ((r - g)[bb] / d[bb]) + 240
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    sel = (h >= 270) & (s > .2) & (mx > low) & (im[..., 3] > 0)
    if not sel.any(): return 0
    red = sel & (h >= 340)
    vio = sel & ~red
    out = a.copy()
    # red: hue 0 at the same value / saturation -> (v, v(1-s), v(1-s))
    out[red] = np.stack([mx[red], (mx * (1 - s))[red], (mx * (1 - s))[red]], -1)
    # violet: pull every channel toward the value so the saturation is .18 (hue kept)
    k = (.18 / s[vio])[:, None]
    out[vio] = mx[vio][:, None] - (mx[vio][:, None] - a[vio]) * k
    im[..., :3] = np.clip(np.round(out * 255), 0, 255).astype(np.uint8)
    Image.fromarray(im).save(path, optimize=True)
    return int(sel.sum())


# ---------------------------------------------------------------- ground
GRADE = 'grade'
EXPOSURE = {1: .78, 2: .78, 3: .80, 4: .90}


def haze(v, T):
    """the layout's veil: the tile's mean washed toward a warm ash grey (never red / rose / purple); the night side darker"""
    m = T.reshape(-1, 3).mean(0); grey = .34 if v == 4 else .52
    c = np.array([grey * 1.06, grey * .96, grey * .86]) * .7 + m / max(m.max(), 1e-3) * grey * .3
    return [round(float(x), 3) for x in c]


def ground():
    for v, o in PICK.items():
        src = f'{R1}/tiles/v{v}{o}'
        Image.open(src + '_mid.png').convert('RGBA').save(f'{D}/v{v}/mid.png', optimize=True)
        _im = np.asarray(Image.open(f'{D}/v{v}/mid.png').convert('RGBA')).astype(float) / 255
        _im[..., :3] *= EXPOSURE[v]        # readability (CI: lane body contrast 1.7-1.9 < 2.0): the day grounds drawn darker, lava kept as the accent
        Image.fromarray((np.clip(_im, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(f'{D}/v{v}/mid.png', optimize=True)
        n = warm_fix(f'{D}/v{v}/mid.png')
        if n: print(f'  v{v}/mid.png: {n} pink texels turned red-orange')
        shutil.copy(src + '_mask.txt', f'{D}/v{v}/mask.txt'); shutil.copy(src + '_ctx.txt', f'{D}/v{v}/ctx.txt')
        T = np.asarray(Image.open(f'{D}/v{v}/mid.png').convert('RGB')).astype(float) / 255
        gp = f'{D}/v{v}/{GRADE}.json'
        g = {'haze': haze(v, T)}
        with open(gp, 'w') as f: json.dump(g, f)
        shot_line(v); warm_fix(f'{D}/v{v}/mid.png')
        print(f'v{v}{o} installed')


# The SHOT LINE (DesertTuning.ShotLine): a shot's white-hot core (linear luminance ~1) must stand 3:1 over the ground it crosses
# (RailMineThemedTest / HostileProjectileTest: >= 16 px at 3:1 against the patch behind it), so the lit ground stays under
# (1 + .05) / 3 - .05 = .30 everywhere and under the line in all but its brightest 1 %. CI: the beam over v3 amber (32 px patches
# p99 .306) stood out 0 px, over v1 B (.352) 16 px, over v2 A (.220) 91. A tile over the line is exposed down in linear light
# (hue and saturation kept, after the context grid is read from the painting) until its p99 sits on it; grade.json keeps the factor.
SHOT_LINE = .24
SHOT_PATCH = 32


def patch_p99(rgb):
    lin = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    y = lin @ np.array([.2126, .7152, .0722])
    h, w = y.shape; P = SHOT_PATCH
    return float(np.percentile(y[:h // P * P, :w // P * P].reshape(h // P, P, w // P, P).mean((1, 3)), 99))


def shot_line(v):
    """expose v<n>/mid.png down to the shot line (idempotent: a tile on or under it is left alone)"""
    path = f'{D}/v{v}/mid.png'
    im = np.asarray(Image.open(path).convert('RGBA')).astype(float) / 255
    p = patch_p99(im[..., :3])
    if p <= SHOT_LINE + .002: return 1.0
    k = SHOT_LINE / p
    lin = np.where(im[..., :3] <= .04045, im[..., :3] / 12.92, ((im[..., :3] + .055) / 1.055) ** 2.4) * k
    srgb = np.where(lin <= .0031308, lin * 12.92, 1.055 * np.power(np.maximum(lin, 0), 1 / 2.4) - .055)
    im[..., :3] = srgb
    Image.fromarray((np.clip(im, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(path, optimize=True)
    gp = f'{D}/v{v}/{GRADE}.json'
    g = json.load(open(gp)) if os.path.exists(gp) else {}
    g['exposure'] = round(g.get('exposure', 1.0) * k, 4)
    with open(gp, 'w') as f: json.dump(g, f)
    print(f'v{v}/mid.png: patch p99 {p:.3f} -> {patch_p99(srgb):.3f} (linear x {k:.3f})')
    return k


# ---------------------------------------------------------------- props
def strips(kind):
    fs = sorted(glob.glob(f'{OUT}/bd_ember_prop_{kind}/r1_*.png'), key=lambda p: int(re.findall(r'r1_(\d+)', p)[0]))
    out = []
    for p in fs:
        a = np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255
        N = a.shape[0]; n = a.shape[1] // N
        frames = [a[:, i * N:(i + 1) * N] for i in range(n)]
        frames = [frames[(i // 2) * 2] for i in range(n)]      # texture budget: 4 unique cells a strip, each held 250 ms (lamps keep their 1 Hz blink)
        out.append(frames)
    return out


LAVA_KINDS_SET = ('orehauler', 'stilts', 'crawler')


def kinds_for(v):
    return [k for k in PX if not (v in (2, 3) and k in LAVA_KINDS_SET)]      # v3's lava is thin threads: no lava pool for the pool kinds


SCALED = {}
def scaled_cells(kind):
    """every cell of `kind` scaled to its drawn px (square px x px, not yet cut)"""
    if kind not in SCALED:
        S = strips(kind); px = PX[kind]
        SCALED[kind] = [[resize_premul(f, px, px) for f in fr] for fr in S]
    return SCALED[kind]


def union_cut(scaled, px):
    """cut a kind's cells to the union box of all its variants / frames (+1 px clear); the pivot is the cell centre's place in it"""
    al = np.zeros((px, px), bool)
    for fr in scaled:
        for f in fr: al |= f[..., 3] > 2 / 255
    ys, xs = np.nonzero(al)
    x0, x1 = max(0, xs.min() - 1), min(px, xs.max() + 2); y0, y1 = max(0, ys.min() - 1), min(px, ys.max() + 2)
    cells = [[f[y0:y1, x0:x1] for f in fr] for fr in scaled]
    return cells, ((px / 2 - x0) / (x1 - x0), (y1 - px / 2) / (y1 - y0))


# ---------------------------------------------------------------- BLEND (user 2026-11-28: every prop must sit IN the world)
LAVA_KINDS = ('orehauler', 'stilts', 'crawler')           # stand in / over lava: heat, not a scorch apron
BLEND = os.environ.get('EMBER_BLEND', '1') != '0'
from scipy.ndimage import gaussian_filter, binary_dilation as _bd


def ground_stats(v):
    T = np.asarray(Image.open(f'{D}/v{v}/mid.png').convert('RGB')).astype(float) / 255
    L = T @ np.array([.3, .59, .11]); m = L < np.quantile(L, .55)               # the ground itself, not its lava
    return T[m].mean(0), T[L < np.quantile(L, .3)].mean(0)


def blend_(f, v, kind, gmean, gdark, vi, fi):
    """fit one cell to its layout's ground: (1) its palette drawn toward the ground's own tint and value (non-lamp texels only: lamps,
    lava and fires keep their colour), (2) a contact shadow: the foot darkened, (3) a SCORCH / ASH apron round the foot in the ground's
    own dark (a soft feathered stain with a few ash flecks), so the base never reads as a cut-out; the lava kinds get a warm heat
    bloom instead, the mountains' aprons are already feathered"""
    a = f[..., 3]; rgb = f[..., :3].copy()
    mx = rgb.max(-1); mn = rgb.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-3)
    hot = np.clip((mx - .62) / .2, 0, 1) * np.clip((sat - .35) / .25, 0, 1)          # lamps / lava / fires
    lum = (rgb @ np.array([.3, .59, .11]))[..., None]
    tone = gmean / max(float(gmean @ np.array([.3, .59, .11])), 1e-3)
    k = .16 if kind not in MOUNTAINS else .10
    tinted = rgb * (1 - k) + (lum * tone) * k
    rgb = tinted * (1 - hot[..., None]) + rgb * hot[..., None]
    h, w = a.shape; ys, xs = np.nonzero(a > .3)
    if len(ys):
        y0, y1 = ys.min(), ys.max(); foot = np.clip((np.arange(h)[:, None] - (y1 - .22 * (y1 - y0))) / (.22 * (y1 - y0) + 1e-3), 0, 1)
        rgb = rgb * (1 - .16 * foot[..., None] * (1 - hot[..., None]) * (1 if kind not in MOUNTAINS else .5))
    out = np.dstack([rgb, a])
    if kind in MOUNTAINS or kind in LAVA_KINDS:
        if kind in LAVA_KINDS:                                                               # heat bloom: a faint warm halo, under the sprite
            r = np.random.RandomState(7 + vi * 5)
            hb = np.clip(gaussian_filter(_bd(a > .3, iterations=3).astype(float), 3.5) * .12 - a, 0, 1); hb[hb < .03] = 0
            out = over_straight(out, np.dstack([np.broadcast_to(np.array([.62, .26, .08]), (h, w, 3)), hb]))
        return out
    return foot_feather(out)


def foot_feather(o, F=18):
    """the drawing's own dark rock FOOT (scorched basalt round the building, painted by the generator) fades out over F texels toward the
    outline, so it dissolves into the pad / ground under it instead of reading as a cut-out; the structure itself keeps its edge"""
    from scipy.ndimage import distance_transform_edt as edt
    a = o[..., 3]; rgb = o[..., :3]; mx = rgb.max(-1); mn = rgb.min(-1); s = (mx - mn) / np.maximum(mx, 1e-3)
    dark = gaussian_filter(((mx < .34) & (s < .55)).astype(float), 2.5) > .55
    d = edt(a > .3)
    k = np.where(dark, np.clip(d / F, 0, 1) ** 1.3, 1.0)
    o = o.copy(); o[..., 3] = a * k; return o


def over_straight(top, bot, under_it=False):
    """straight-alpha composite; under_it: `bot` is the layer UNDER `top` (the second argument order of the call site) ... both orders handled"""
    if under_it: top, bot = top, bot
    at, ab = top[..., 3:4], bot[..., 3:4]; ao = at + ab * (1 - at)
    rgb = np.where(ao > 1e-4, (top[..., :3] * at + bot[..., :3] * ab * (1 - at)) / np.maximum(ao, 1e-4), 0)
    return np.dstack([rgb, ao[..., 0]])


def shelf(items, width):
    x = y = PAD; row = 0; place = []
    for key, f in items:
        h, w = f.shape[:2]
        if x + w + PAD > width: x = PAD; y += row + PAD; row = 0
        place.append((key, x, y)); x += w + PAD; row = max(row, h)
    return place, (y + row + PAD + 3) // 4 * 4


def pack(v, out=None, blend=True):
    """every UNIQUE cell once (a strip's frames that only differ where its lamps blink are often identical: they share one rect)"""
    uniq = {}; names = []
    gmean, gdark = ground_stats(v)
    for k in kinds_for(v):
        sc = scaled_cells(k); px = PX[k]
        sc = [[blend_(f, v, k, gmean, gdark, vi, fi) if (blend and BLEND) else f for fi, f in enumerate(fr)] for vi, fr in enumerate(sc)]
        sc = [[night(f, k in MOUNTAINS) if v == 4 else f for f in fr] for fr in sc]
        cells, pivot = union_cut(sc, px)
        for vi, fr in enumerate(cells):
            for fi, f in enumerate(fr):
                f = f.copy()
                pk = is_pink(f[..., :3]) & (f[..., 3] > 0)          # (resampling ringing on faint edge texels only: greyed)
                if pk.any(): g = f[..., :3].mean(-1, keepdims=True); f[..., :3] = np.where(pk[..., None], g, f[..., :3])
                key = f.tobytes()
                if key not in uniq: uniq[key] = f
                names.append((f'{k}_{vi}_{fi:02d}', key, pivot))
    items = sorted(uniq.items(), key=lambda t: -t[1].shape[0])
    for width in (2048, 4096):
        place, H = shelf(items, width)
        if H <= 4096: break
    at = {key: (x, y) for key, x, y in place}
    sheet = np.zeros((H, width, 4))
    for key, f in items:
        x, y = at[key]; h, w = f.shape[:2]; sheet[y:y + h, x:x + w] = f
    sprites = []
    for n, key, pv in names:
        x, y = at[key]; h, w = uniq[key].shape[:2]
        sprites.append(dict(n=n, x=x, y=H - y - h, w=w, h=h, px=round(pv[0], 5), py=round(pv[1], 5)))
    pink = int((is_pink(sheet[..., :3]) & (sheet[..., 3] > 0)).sum())
    od = out or f'{D}/v{v}'; os.makedirs(od, exist_ok=True)
    Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA').save(f'{od}/props.png', optimize=True)
    depink(f'{od}/props.png')        # (8-bit rounding of a faint edge texel can land in the pink band: WorldCue.IsPink, any alpha)
    guard(f'{od}/props.png')         # and a margin round the band, which the ASTC import would otherwise cross
    json.dump(dict(sheetW=width, sheetH=H, sprites=sprites), open(f'{od}/props.json', 'w'), indent=0)
    print(f'v{v} props: {len(sprites)} sprites, {len(uniq)} unique cells, {width} x {H}, ASTC 4x4 {width * H / 1048576:.2f} MB, pink {pink}')


# ---------------------------------------------------------------- weather
def box256(a):
    im = Image.fromarray((a * 255 + .5).astype(np.uint8), 'RGBA')
    pm = np.asarray(im).astype(float) / 255; pm[..., :3] *= pm[..., 3:4]
    out = np.asarray(Image.fromarray((pm * 255 + .5).astype(np.uint8), 'RGBA').resize((256, 256), Image.BOX)).astype(float) / 255
    al = out[..., 3:4]; out[..., :3] = np.where(al > 1e-3, out[..., :3] / np.maximum(al, 1e-3), 0)
    return np.clip(out, 0, 1)


def load(p): return np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255


def frames_of(p):
    a = load(p); N = a.shape[0]
    return [a[:, i * N:(i + 1) * N] for i in range(a.shape[1] // N)]


def repack(atlas, cells):
    js = json.load(open(f'{D}/{atlas}.json'))
    old = np.asarray(Image.open(f'{D}/{atlas}.png').convert('RGBA')).astype(float) / 255
    H = old.shape[0]; sheet = old.copy()
    for s in js['sprites']:
        if s['n'] not in cells: continue
        c = cells[s['n']]
        y = H - s['y'] - s['h']
        sheet[y:y + s['h'], s['x']:s['x'] + s['w']] = box256(c) if c.shape[0] != 256 else c
    Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA').save(f'{D}/{atlas}.png', optimize=True)
    pk = is_pink(sheet[..., :3]) & (sheet[..., 3] > .02)
    print(f'{atlas}: replaced {sum(1 for s in js["sprites"] if s["n"] in cells)} cells, pink {int(pk.sum())}')


def thin(a, k):
    o = a.copy(); o[..., 3] *= k; return o


WEATHER_VALUE = .88


def dim(a, k):
    o = a.copy(); o[..., :3] *= k
    return o


def weather():
    raise SystemExit('weather: see weather_build.py')


if __name__ == '__main__':
    what = [a for a in sys.argv[1:] if a in ('ground', 'props', 'weather', 'guard', 'shotline')] or ['ground', 'props']
    if 'shotline' in what:
        for v in (1, 2, 3, 4): shot_line(v)
    if 'guard' in what:
        for v in (1, 2, 3, 4): print(f'v{v} props guard: {guard(f"{D}/v{v}/props.png")} texels moved out of the pink margin')
    if 'ground' in what: ground()
    if 'props' in what:
        for v in (1, 2, 3, 4): pack(v)
