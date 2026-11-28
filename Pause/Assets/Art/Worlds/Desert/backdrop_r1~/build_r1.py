"""Desert r1 install (2026-11-28, user-approved): the stitched .41-altitude ground, its context grid, the living-props atlases and the
soft painted weather, written into Resources/Worlds/Desert/Backdrop3.

  python3 build_r1.py [SP] [ground|props|weather]   SP: a folder holding desert/r1/tiles and out2/bd_desert_* (default: ./src, the approved inputs)

  v<n>/mid.png  the picked stitched tile (v1 B, v2 A; v3 A golden-hour amber, v4 A moonlit blue-grey: REGRADED), 896 x 4096 RGBA. A REGRADED tile (same size, same mask) is
                swapped in by dropping it over v<n>/mid.png and rerunning `build_r1.py ground`: nothing in code knows its colours
  v<n>/grade.json  the layout's grade as DATA (DesertTuning.HazeOf): "haze", the pale dust of the ceiling / wisps over it --
                the approved khaki / salt greys on v1 / v2 (HAZE), else derived from the tile itself (a pale, desaturated veil)
  v<n>/mask.txt its 64 x 292 class grid (W T N C / O M C), as built
  v<n>/ctx.txt  the same grid's CONTEXT flags, one hex digit a cell (GroundMask.ParseContext): 1 flat, 2 rocky, 4 shore, 8 open --
                composite.py classes(): open = unpainted ground; flat = the flattest 55% of it by local relief; rocky = cells beside
                painted rock / works (N, M, or open within 2 cells of C); shore = within 8 cells of painted water (v2 / v3)
  v<n>/props.png + props.json   the props of that layout (DesertPropsTuning: skiffs only where there is a shore), each strip
                <kind>_<variant>_<frame> cut to its kind's union box (all variants and frames) at its drawn size on a 1080 px screen
                (px), the cell's centre the sprite's pivot; v4 baked with the night grade (lamps keep their colour)
  weather.png / dust.png / dust_2.png   the soft painted redo under the old sprite names, cells box-filtered to 256
"""
import sys, os, json, glob, re, shutil, numpy as np
from PIL import Image
from scipy.ndimage import uniform_filter, binary_dilation

SP = sys.argv[1] if len(sys.argv) > 1 and os.path.isdir(sys.argv[1]) else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src')
HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.normpath(os.path.join(HERE, '../../../Backgrounds/Resources/Worlds/Desert/Backdrop3'))
R1 = f'{SP}/desert/r1'; OUT = f'{SP}/out2'
PICK = {1: 'B', 2: 'A', 3: 'A', 4: 'A'}   # the user's picks (2026-11-28): v1 B, v2 A; v3 / v4 A until their regrades are approved
OPEN = {1: 'WTN', 2: 'OM', 3: 'WN', 4: 'WN'}   # unpainted ground (N: zone-flagged sand, open too)
# drawn px on a 1080 px screen (install_plan.txt B / F); the mountains' cells are 512, the rest 256
PX = dict(oasis=150, caravan=110, crawler=110, rig=112, solar=120, station=180, wind=120, ruins=124, wreck=140, camp=104, balloon=190,
          zeppelin=250, dig=180, skiffs=120, spaceport=210, observatory=190, market=210, mesa=340, ridge=360, hoodoo=320, volcano=350)
# the user moved every world to the .49 altitude (2026-11-28, later): the props are cut at their approved .41 px x ALT
# (DesertPropsTuning.AltitudeK = DesertTuning.GroundZoom / .41), so they stay the same share of the ground
GROUND_ZOOM, APPROVED_ZOOM = .49, .41
ALT = GROUND_ZOOM / APPROVED_ZOOM
PX = {k: int(round(p * ALT)) for k, p in PX.items()}
MOUNTAINS = ('mesa', 'ridge', 'hoodoo', 'volcano')
SHORE_ONLY = ('skiffs',)
SHORE_LAYOUTS = (2, 3)
SHEET_W = 2048
PAD = 2


def is_pink(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = m & (mx == r); gg = m & (mx == g) & ~rr; bb = m & ~rr & ~gg
    h[rr] = (60 * ((g - b)[rr] / d[rr])) % 360
    h[gg] = 60 * ((b - r)[gg] / d[gg]) + 120
    h[bb] = 60 * ((r - g)[bb] / d[bb]) + 240
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
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
    """v4: the props drawn dark and moon-cold (composite.py night()); lamps / fires (v > .8, s > .25) keep full strength. The
    mountains' pale sand aprons are feathered further (alpha^1.8) so no light halo sits on the dark ground."""
    rgb = a[..., :3]; v = rgb.max(-1); s = (v - rgb.min(-1)) / np.maximum(v, 1e-3)
    lit = np.clip((v - .8) / .15, 0, 1) * np.clip((s - .25) / .3, 0, 1)
    if mountain: lit[:] = 0
    # moonlit BLUE-GREY (coordinator 2026-11-28: not purple): most of the colour taken out toward grey, then a cool moon tint
    luma = (rgb @ np.array([.3, .59, .11]))[..., None]
    d = (rgb * .35 + luma * .65) * np.array([.40, .44, .50])
    o = a.copy(); o[..., :3] = d * (1 - lit[..., None]) + rgb * lit[..., None]
    if mountain: o[..., 3] = o[..., 3] ** 1.8
    return o


def golden(a):
    """v3 (golden-hour amber ground): the props and mountains under a warm late-afternoon light (lamps / fires keep their colour)"""
    rgb = a[..., :3]; v = rgb.max(-1); s = (v - rgb.min(-1)) / np.maximum(v, 1e-3)
    lit = np.clip((v - .8) / .15, 0, 1) * np.clip((s - .25) / .3, 0, 1)
    d = np.clip(rgb * np.array([1.03, .96, .86]), 0, 1)
    o = a.copy(); o[..., :3] = d * (1 - lit[..., None]) + rgb * lit[..., None]
    return o


# ---------------------------------------------------------------- the v3 / v4 regrade (user-approved 2026-11-28)
# The ground tiles of v3 (golden-hour amber) and v4 (moonlit blue-grey) are the approved regrades of v3A / v4A (pl/desert/regrade:
# hue recentred, saturation scaled, a value curve; water, palms and campfires kept). The layers under them (sky, flow) take the same
# grade once (`build_r1.py regrade_layers`, recorded in grade.json "layers" so a rerun never grades twice), and the weather a tint.
REGRADED = {3: 'v3A_amber', 4: 'v4A_moon'}
LAYER_GRADE = {3: dict(name='amber', hue=.075, spread=.5, sat=.95, vmul=1.0, vgamma=1.0),
               4: dict(name='moon', hue=.60, spread=.3, sat=.40, vmul=1.25, vgamma=.95)}
WEATHER = {3: [1.0, .93, .82], 4: [.80, .86, .98]}   # wisps, mist, gusts, grit, dust devils (DesertTuning.WeatherTintOf)


def _hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; mx = a.max(-1); mn = a.min(-1); d = mx - mn + 1e-9
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6
    return h, np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0), mx


def _rgb(h, s, v):
    i = np.floor(h * 6) % 6; f = h * 6 - np.floor(h * 6); p = v * (1 - s); q = v * (1 - f * s); t = v * (1 - (1 - f) * s)
    out = np.zeros(h.shape + (3,))
    for k, (R, G, B) in enumerate([(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]):
        m = i == k; out[m, 0] = R[m]; out[m, 1] = G[m]; out[m, 2] = B[m]
    return out


def regrade_file(path, hue, spread, sat, vmul, vgamma, **_):
    a = np.asarray(Image.open(path).convert('RGBA')).astype(float) / 255
    h, s, v = _hsv(a[..., :3]); ang = h * 2 * np.pi
    c = (np.arctan2((np.sin(ang) * s).sum(), (np.cos(ang) * s).sum()) / (2 * np.pi)) % 1
    h2 = (hue + (((h - c + .5) % 1) - .5) * spread) % 1
    out = _rgb(h2, np.clip(s * sat, 0, 1), np.clip((v ** vgamma) * vmul, 0, 1))
    pk = is_pink(out); out[pk] = out[pk].mean(-1, keepdims=True)
    a[..., :3] = out
    Image.fromarray((a * 255 + .5).astype(np.uint8)).save(path, optimize=True)
    print(f'  {os.path.relpath(path, D)} graded ({int(pk.sum())} pink texels greyed)')


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


def regrade_layers():
    for v, g in LAYER_GRADE.items():
        gp = f'{D}/v{v}/grade.json'
        cur = json.load(open(gp)) if os.path.exists(gp) else {}
        if cur.get('layers') == g['name']: continue
        for layer in ('sky', 'flow'): regrade_file(f'{D}/v{v}/{layer}.png', **g)
        for layer in ('sky', 'flow'): depink(f'{D}/v{v}/{layer}.png')
        cur['layers'] = g['name']; json.dump(cur, open(gp, 'w'))


# ---------------------------------------------------------------- ground
GRADE = 'grade'
HAZE = {1: [.70, .64, .52], 2: [.72, .71, .74]}        # the approved round-5 hazes of the day layouts


def haze(v, T):
    """the layout's pale dust veil: its approved haze, else the tile's mean colour washed toward a pale grey (70% grey, at most .12
    saturation, only a sand or a cool sky hue: a red / rose / purple tile gives a neutral grey); the night side (v4) a darker veil"""
    if v in HAZE: return HAZE[v]
    m = T.reshape(-1, 3).mean(0); grey = .40 if v == 4 else .70
    c = grey * .7 + m / max(m.max(), 1e-3) * grey * .3
    mx, mn = c.max(), c.min()
    if mx > 0 and (mx - mn) / mx > .12: c = mx - (mx - c) * (.12 * mx / (mx - mn))
    import colorsys
    hue = colorsys.rgb_to_hsv(*c)[0] * 360
    if not (20 <= hue <= 70 or 190 <= hue <= 240): c[:] = c.mean()      # only a sand or a sky tint: never red / rose / purple
    return [round(float(x), 3) for x in c]


def ground():
    for v, o in PICK.items():
        src = f'{R1}/tiles/v{v}{o}'
        rg = f'{SP}/desert/regrade/{REGRADED[v]}.png' if v in REGRADED else ''
        if rg and os.path.exists(rg): Image.open(rg).convert('RGBA').save(f'{D}/v{v}/mid.png', optimize=True)
        elif os.path.exists(src + '_mid.png'): Image.open(src + '_mid.png').convert('RGBA').save(f'{D}/v{v}/mid.png', optimize=True)
        n = depink(f'{D}/v{v}/mid.png')
        if n: print(f'  v{v}/mid.png: {n} pink texels greyed')
        im = Image.open(f'{D}/v{v}/mid.png')        # (the installed tile is the source of record)
        shutil.copy(src + '_mask.txt', f'{D}/v{v}/mask.txt')
        M = np.array([list(l.strip()) for l in open(src + '_mask.txt') if l.strip()])
        T = np.asarray(im.convert('RGB')).astype(float) / 255
        R, C = M.shape; cell = T.shape[0] // R
        L = T @ np.array([.3, .5, .2])
        rel = L[:R * cell, :C * cell].reshape(R, cell, C, cell).std((1, 3))
        rel = uniform_filter(rel, 9, mode='wrap')                     # relief over ~a prop's footprint, so flat ground comes in patches
        openm = np.isin(M, list(OPEN[v]))
        flat = openm & (rel <= np.quantile(rel[openm], .65))
        rocky = (np.isin(M, ['N', 'M']) | (openm & binary_dilation(M == 'C', iterations=4))) & (M != 'C')
        wat = ((T[..., 1] > T[..., 0] + .1) & (T[..., 2] > T[..., 0] + .05) & (T[..., 1] > .35) & (v in SHORE_LAYOUTS))[:R * cell, :C * cell]
        wat = wat.reshape(R, cell, C, cell).mean((1, 3)) > .06
        shore = binary_dilation(wat, iterations=8) & (M != 'C') & ~wat
        bits = flat * 1 + rocky * 2 + shore * 4 + openm * 8
        with open(f'{D}/v{v}/ctx.txt', 'w') as f:
            for r in range(R): f.write(''.join('%x' % b for b in bits[r]) + '\n')
        gp = f'{D}/v{v}/{GRADE}.json'
        g = json.load(open(gp)) if os.path.exists(gp) else {}
        g['haze'] = haze(v, T)
        if v in WEATHER: g['weather'] = WEATHER[v]
        with open(gp, 'w') as f: json.dump(g, f)
        print(f'v{v}{o}: {C}x{R}, flat {flat.mean():.2f} rocky {rocky.mean():.2f} shore {shore.mean():.2f} open {openm.mean():.2f}')


# ---------------------------------------------------------------- props
def strips(kind):
    fs = sorted(glob.glob(f'{OUT}/bd_desert_prop_{kind}/r1_*.png'), key=lambda p: int(re.findall(r'r1_(\d+)', p)[0]))
    out = []
    for p in fs:
        a = np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255
        N = a.shape[0]; n = a.shape[1] // N
        frames = [a[:, i * N:(i + 1) * N] for i in range(n)]
        if kind in MOUNTAINS: frames = frames[:1]        # all eight frames are the same still painting
        out.append(frames)
    return out


def kinds_for(v):
    return [k for k in PX if (k not in SHORE_ONLY or v in SHORE_LAYOUTS)]


CUT = {}
def cut(kind):
    """every cell of `kind` scaled to its drawn px and cut to the kind's union box (+1 px clear)"""
    if kind in CUT: return CUT[kind]
    S = strips(kind); N = S[0][0].shape[0]; px = PX[kind]
    scaled = [[resize_premul(f, px, px) for f in fr] for fr in S]
    al = np.zeros((px, px), bool)
    for fr in scaled:
        for f in fr: al |= f[..., 3] > 2 / 255
    ys, xs = np.nonzero(al)
    x0, x1 = max(0, xs.min() - 1), min(px, xs.max() + 2); y0, y1 = max(0, ys.min() - 1), min(px, ys.max() + 2)
    cells = [[f[y0:y1, x0:x1] for f in fr] for fr in scaled]
    pivot = ((px / 2 - x0) / (x1 - x0), (y1 - px / 2) / (y1 - y0))
    CUT[kind] = (cells, pivot)
    return CUT[kind]


def shelf(items, width):
    x = y = PAD; row = 0; place = []
    for key, f in items:
        h, w = f.shape[:2]
        if x + w + PAD > width: x = PAD; y += row + PAD; row = 0
        place.append((key, x, y)); x += w + PAD; row = max(row, h)
    return place, (y + row + PAD + 3) // 4 * 4


def pack(v):
    """every UNIQUE cell once (a strip's frames that only differ where its lamps blink are often identical: they share one rect)"""
    uniq = {}; names = []
    for k in kinds_for(v):
        cells, pivot = cut(k)
        for vi, fr in enumerate(cells):
            for fi, f in enumerate(fr):
                if v == 4: f = night(f, k in MOUNTAINS)
                elif v == 3: f = golden(f)
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
    Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA').save(f'{D}/v{v}/props.png', optimize=True)
    depink(f'{D}/v{v}/props.png')        # (8-bit rounding of a faint edge texel can land in the pink band: WorldCue.IsPink, any alpha)
    guard(f'{D}/v{v}/props.png')         # and a margin round the band, which the ASTC import would otherwise cross
    json.dump(dict(sheetW=width, sheetH=H, sprites=sprites), open(f'{D}/v{v}/props.json', 'w'), indent=0)
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
    bank = [load(f'{OUT}/bd_desert_wx_bank/r1_{i}.png') for i in range(4)]
    mist = [load(f'{OUT}/bd_desert_wx_mist/r1_{i}.png') for i in range(4)]
    gust = [load(f'{OUT}/bd_desert_wx_gust/r1_{i}.png') for i in range(4)]
    w = {f'cloud_bank_{i:02d}': bank[i] for i in range(4)}
    w.update({f'cloud_wisp_{i:02d}': gust[i] for i in range(4)})
    w.update({'mist_00': mist[0], 'mist_01': mist[1], 'mist_02': mist[2], 'gust_00': gust[0], 'gust_01': gust[2],
              'pall_00': thin(mist[3], .7), 'pall_01': thin(np.ascontiguousarray(mist[1][:, ::-1]), .7)})
    # the soft redo is painted paler than the old sheet: held to Desert's cloud ceiling as drawn (WorldBackdropTest, <= .80)
    w = {k: dim(a, WEATHER_VALUE) for k, a in w.items()}
    repack('weather', w)
    devil = frames_of(f'{OUT}/bd_desert_wx_devil/r1_0.png')
    repack('dust', {f'dustdevil_{i:02d}': devil[i] for i in range(8)})
    # the grit gust: the gust painting rolled along itself, cross-faded so frame 8 == frame 0
    g = box256(gust[1]); grit = {}
    for i in range(8):
        sh = int(round(i * 256 / 8))
        a = np.roll(g, sh, axis=1); b = np.roll(g, sh - 256, axis=1)
        ex = np.ones(256); fade = np.clip(np.minimum(np.arange(256), 255 - np.arange(256)) / 48, 0, 1)
        f = a.copy(); f[..., 3] *= fade[None, :]
        grit[f'grit_gust_{i:02d}'] = f
    repack('dust_2', grit)


def worms():
    """the worm sheets (2048 px, 256 / 512 px cells) are drawn at DesertTuning.PieceScale since r1, about half their pixels: stored
    at 1024 (premultiplied resample; their JSON keeps sheetW / sheetH 2048, so BackdropAtlas scales the rects). Idempotent."""
    for n in ('worm', 'worm_giant'):
        p = f'{D}/{n}.png'
        a = np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255
        if a.shape[1] <= 1024: continue
        o = resize_premul(a, a.shape[1] // 2, a.shape[0] // 2)
        Image.fromarray((o * 255 + .5).astype(np.uint8), 'RGBA').save(p, optimize=True)
        print(f'{n}: {a.shape[1]} -> {o.shape[1]} px')


if __name__ == '__main__':
    what = [a for a in sys.argv[1:] if a in ('ground', 'props', 'weather', 'regrade_layers', 'worms', 'guard')] or ['ground', 'regrade_layers', 'props', 'weather', 'worms']
    if 'guard' in what:
        for v in (1, 2, 3, 4): print(f'v{v} props guard: {guard(f"{D}/v{v}/props.png")} texels moved out of the pink margin')
    if 'worms' in what: worms()
    if 'ground' in what: ground()
    if 'regrade_layers' in what: regrade_layers()
    if 'props' in what:
        for v in (1, 2, 3, 4): pack(v)
    if 'weather' in what: weather()
