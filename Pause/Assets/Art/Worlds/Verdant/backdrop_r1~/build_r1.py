"""Verdant r1 install (2026-11-28, user-approved): the .49-altitude ground, the soft painted weather and the living-props atlases, written
into Resources/Worlds/Verdant/Backdrop3.

  python3 build_r1.py [SP] [ground|weather|props|guard]   SP: a folder holding verdant/r1 (tiles, props/meta.json) and out2/bd_verdant_* (default: ./src,
                                                         the approved inputs committed beside this script; ground needs SP/verdant/r1/tiles, the PixelLab quilts)

  v<n>/mid.png     the quilted ground tile (v1 / v2 / v3 option A; v4 the redo test tile, interim until the user approves it), 896 x 4096 RGBA
  v<n>/sky.png     the same ground at half size (448 x 2048): the layer under the opaque mid, the far layer's texture, the Codex thumbnail
  v<n>/mask.txt    its 64 x 292 affinity grid (W water, B built, O open, C canopy), as built
  weather.png      the soft painted redo under the existing sprite names (256 px cells, box-filtered from the 512 paintings):
                   cloud_bank_00..03 <- bank; cloud_wisp_00..03 <- rain (rain curtains drift as wisps); mist_00..02 <- mist;
                   pollen_00 / 01 <- pollen 0 / 1; spore_00 <- pollen 3 (small); smokepall_* stay (the wildfires' dark smoke)
  wx_hazard.png    row 0 (the spore_gust FRONT, a rolling wall) <- the humid front (6 frames, 1024 x 730, scaled to the 512 cell, centred); row 1
                   (the updraft column) and row 2 (the warnings) untouched
  v<n>/props.png + props.json   the living props of that layout: 11 kinds x 4 variants x 8 frames + the beast walkers, each strip cut to its
                   kind's union box (all variants and frames) at its drawn size on a 1080 px screen at the .49 altitude (PX), the cell's centre the
                   sprite's pivot; the strips were blended once already (palette pull, feathered feet, contact shadow, lamps untouched), so
                   here only a TONE-MATCH to the layout's own mid tile (a small pull toward its ground colour, highlights eased over its bright
                   value; v4 a cool night grade); frames that are identical share one rect. Pink-free with a margin round WorldCue.IsPink.
"""
import sys, os, json, glob, re, shutil, numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SP = sys.argv[1] if len(sys.argv) > 1 and os.path.isdir(sys.argv[1]) else os.path.join(HERE, 'src')
D = os.path.normpath(os.path.join(HERE, '../../../Backgrounds/Resources/Worlds/Verdant/Backdrop3'))
R1 = f'{SP}/verdant/r1'; OUT = f'{SP}/out2'
PICK = {1: 'A', 2: 'A', 3: 'A', 4: 'A'}
# drawn px on a 1080 px screen at the .49 altitude (install_plan.txt B / composite.py GROUND); the landforms' cells are 512, the rest 256
PX = dict(crashplane=180, outpost=190, terraces=200, flowers=150, beasts=200, stilt=190, ropebridge=210, waterfall=400, karst=420, ridge=560, starship=430)
WALK_PX = 96 * 200 // 256          # the beast walkers are cut from the 200 px herd: the 96 px cell at the herd's scale
KINDS = tuple(PX)
SHEET_W = 4096
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
    from PIL import Image
    pm = a.copy(); pm[..., :3] *= pm[..., 3:4]
    out = np.zeros((h, w, 4))
    for c in range(4):
        out[..., c] = np.asarray(Image.fromarray(pm[..., c].astype(np.float32), 'F').resize((w, h), Image.LANCZOS))
    out = np.clip(out, 0, 1)
    al = out[..., 3:4]
    out[..., :3] = np.where(al > 1e-4, out[..., :3] / np.maximum(al, 1e-4), 0)
    return np.clip(out, 0, 1)


def box_premul(a, w, h):
    pm = a.copy(); pm[..., :3] *= pm[..., 3:4]
    out = np.stack([np.asarray(Image.fromarray(pm[..., c].astype(np.float32), 'F').resize((w, h), Image.BOX)) for c in range(4)], -1)
    out = np.clip(out, 0, 1)
    al = out[..., 3:4]
    out[..., :3] = np.where(al > 1e-3, out[..., :3] / np.maximum(al, 1e-3), 0)
    return np.clip(out, 0, 1)


def load(p): return np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255


def save(a, p): Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(p, optimize=True)


def depink(path):
    """grey every texel inside WorldCue.IsPink (any alpha); idempotent"""
    im = np.asarray(Image.open(path).convert('RGBA')).copy()
    pk = is_pink(im[..., :3].astype(float) / 255) & (im[..., 3] > 0)
    if pk.any():
        g = np.floor(im[..., :3][pk].astype(float).mean(-1, keepdims=True)).astype(np.uint8)
        im[..., :3][pk] = g
        Image.fromarray(im).save(path, optimize=True)
    return int(pk.sum())


def guard(path, low=.25):
    """ASTC drifts a texel's hue / value a few steps, so a texel just outside WorldCue.IsPink (hue 285-352, s > .25, v > .3) can come out
    of the importer inside it. Keep a margin round the band: any texel (any alpha) with hue 280-360, saturation > .22 and value > `low` is
    moved out -- 340-360 to pure red, the rest brought to saturation .20 at its own value. Idempotent."""
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
    sel = (h >= 280) & (s > .22) & (mx > low) & (im[..., 3] > 0)
    if not sel.any(): return 0
    red = sel & (h >= 340); vio = sel & ~red
    out = a.copy()
    out[red] = np.stack([mx[red], (mx * (1 - s))[red], (mx * (1 - s))[red]], -1)
    k = (.20 / s[vio])[:, None]
    out[vio] = mx[vio][:, None] - (mx[vio][:, None] - a[vio]) * k
    im[..., :3] = np.clip(np.round(out * 255), 0, 255).astype(np.uint8)
    Image.fromarray(im).save(path, optimize=True)
    return int(sel.sum())


# ---------------------------------------------------------------- ground
def ground():
    for v, o in PICK.items():
        src = f'{R1}/tiles/v{v}{o}'
        if not os.path.exists(src + '_mid.png'): print(f'  v{v}: no {src}_mid.png, kept as installed'); continue
        Image.open(src + '_mid.png').convert('RGBA').save(f'{D}/v{v}/mid.png', optimize=True)
        shutil.copy(src + '_mask.txt', f'{D}/v{v}/mask.txt')
        n = depink(f'{D}/v{v}/mid.png')
        print(f'v{v}{o}: mid.png installed' + (f' ({n} pink texels greyed)' if n else ''))


def sky():
    """v<n>/sky.png (the layer under the opaque mid and the texture the far layer reuses; the Codex / menu thumbnails read it too) is the new
    ground itself at half size (448 x 2048, premultiplied LANCZOS with the rows wrapped round, so it stays seamless vertically) -- the old
    896 x 1792 paintings were brighter than the repainted ground and showed the retired layout"""
    for v in (1, 2, 3, 4):
        t = load(f'{D}/v{v}/mid.png'); t[..., 3] = 1
        pad = 16
        w = np.concatenate([t[-pad:], t, t[:pad]], 0)
        o = resize_premul(w, t.shape[1] // 2, w.shape[0] // 2)[pad // 2:-pad // 2]
        save(o, f'{D}/v{v}/sky.png'); print(f'v{v}/sky.png {o.shape[1]} x {o.shape[0]}')


# ---------------------------------------------------------------- weather
GAIN_BANK, GAIN = .50, .55      # tuned like Tide's (.6 / .85): the ceiling's lift 2.1 and alpha thickening multiply the cloud, so the atlas AS DRAWN stays under WorldBackdropTest's .80 (measured .772)


def gain(a, k):
    o = a.copy(); o[..., :3] *= k; return o


def wx(name, i): return load(f'{OUT}/bd_verdant_wx_{name}/r1_{i}.png')


def weather():
    js = json.load(open(f'{D}/weather.json')); sheet = load(f'{D}/weather.png'); H = sheet.shape[0]
    cells = {}
    for i in range(4):
        cells[f'cloud_bank_{i:02d}'] = gain(wx('bank', i), GAIN_BANK)
        cells[f'cloud_wisp_{i:02d}'] = gain(wx('rain', i), GAIN)
    for i in range(3): cells[f'mist_{i:02d}'] = gain(wx('mist', i), GAIN)
    cells['pollen_00'] = wx('pollen', 0); cells['pollen_01'] = wx('pollen', 1)
    cells['spore_00'] = wx('pollen', 3)
    for s in js['sprites']:
        if s['n'] not in cells: continue
        c = box_premul(cells[s['n']], s['w'], s['h']); y = H - s['y'] - s['h']
        sheet[y:y + s['h'], s['x']:s['x'] + s['w']] = c
    save(sheet, f'{D}/weather.png')
    pk = is_pink(sheet[..., :3]) & (sheet[..., 3] > .02)
    print(f'weather.png: {len(cells)} cells replaced, pink {int(pk.sum())}')
    # the humid front: wx_hazard row 0, 6 frames (1024 x 730) scaled to the 512 cell and centred (Tide's build_weather.py)
    hz = Image.open(f'{D}/wx_hazard.png').convert('RGBA'); P = 512
    fl = Image.open(f'{OUT}/bd_verdant_wx_front/r1_0.png').convert('RGBA'); w6 = fl.width // 6
    for c in range(6):
        f = np.asarray(fl.crop((c * w6, 0, (c + 1) * w6, fl.height))).astype(float) / 255
        h = max(8, int(round(fl.height * P / w6)))
        cell = np.zeros((P, P, 4)); y0 = (P - h) // 2
        cell[y0:y0 + h] = resize_premul(f, P, h)
        hz.paste(Image.new('RGBA', (P, P), (0, 0, 0, 0)), (c * P, 0))
        hz.paste(Image.fromarray((cell * 255 + .5).astype(np.uint8), 'RGBA'), (c * P, 0))
    hz.save(f'{D}/wx_hazard.png', optimize=True)
    print(f'wx_hazard.png: row 0 replaced, pink {depink(f"{D}/wx_hazard.png")}')
    for p in ('weather', 'wx_hazard'): guard(f'{D}/{p}.png')


# ---------------------------------------------------------------- props
GROUND = {}


def ground_stats(v):
    """the layout's ground colour (median) and bright value (p90), from the installed tile"""
    if v in GROUND: return GROUND[v]
    T = load(f'{D}/v{v}/mid.png')[::4, ::4, :3].reshape(-1, 3)
    val = T.max(-1)
    GROUND[v] = dict(color=np.median(T, 0), bright=float(np.percentile(val, 90)))
    return GROUND[v]


PULL = {1: .05, 2: .05, 3: .05, 4: .10}
KNEE = 1.35                                 # highlights over the ground's bright value x KNEE are eased
NIGHT = np.array([.72, .82, .88])          # v4: a cool, darker grade (the night ground's lane is ~.8 x the day's); the ground layer's own variant lift darkens the rest


def lamps(rgb):
    v = rgb.max(-1); s = (v - rgb.min(-1)) / np.maximum(v, 1e-3)
    return np.clip((v - .8) / .15, 0, 1) * np.clip((s - .25) / .3, 0, 1)


def tone(a, v):
    """a small pull toward the layout's ground colour, highlights eased over its bright value; lamps (v > .8, s > .25) untouched; v4 cool"""
    g = ground_stats(v); rgb = a[..., :3]; lit = lamps(rgb)[..., None]
    o = rgb * (1 - PULL[v]) + g['color'] * PULL[v]
    if v == 4: o = o * NIGHT
    val = o.max(-1, keepdims=True); k = min(1.0, g['bright'] * KNEE)
    o = np.where(val > k, o * ((k + (val - k) * .5) / np.maximum(val, 1e-4)), o)
    out = a.copy(); out[..., :3] = np.clip(o * (1 - lit) + rgb * lit, 0, 1)
    return out


def strips(kind):
    fs = sorted(glob.glob(f'{OUT}/bd_verdant_prop_{kind}/r1_*.png'), key=lambda p: int(re.findall(r'r1_(\d+)', p)[0]))
    out = []
    for p in fs:
        a = load(p); N = a.shape[0]
        out.append([a[:, i * N:(i + 1) * N] for i in range(a.shape[1] // N)])
    return out


CUT = {}


def cut(kind, px):
    """every cell of `kind` scaled to its drawn px and cut to the kind's union box (+1 px clear)"""
    if kind in CUT: return CUT[kind]
    S = strips(kind)
    P = px
    scaled = [[resize_premul(f, px, px) for f in fr] for fr in S]
    al = np.zeros((P, P), bool)
    for fr in scaled:
        for f in fr: al |= f[..., 3] > 2 / 255
    ys, xs = np.nonzero(al)
    x0, x1 = max(0, xs.min() - 1), min(P, xs.max() + 2); y0, y1 = max(0, ys.min() - 1), min(P, ys.max() + 2)
    cells = [[f[y0:y1, x0:x1] for f in fr] for fr in scaled]
    pivot = ((P / 2 - x0) / (x1 - x0), (y1 - P / 2) / (y1 - y0))
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
    """every UNIQUE cell once (frames that only differ where a lamp blinks, or not at all, share one rect)"""
    uniq = {}; names = []
    jobs = [(k, PX[k], k) for k in KINDS] + [('beastwalk', WALK_PX, 'beastwalk')]
    for kind, px, key in jobs:
        cells, pivot = cut(kind, px)
        for vi, fr in enumerate(cells):
            for fi, f in enumerate(fr):
                f = tone(f, v).copy()
                pk = is_pink(f[..., :3]) & (f[..., 3] > 0)
                if pk.any(): f[..., :3] = np.where(pk[..., None], f[..., :3].mean(-1, keepdims=True), f[..., :3])
                u8 = (np.clip(f, 0, 1) * 255 + .5).astype(np.uint8)
                h = u8.tobytes()
                if h not in uniq: uniq[h] = u8
                names.append((f'{key}_{vi}_{fi:02d}', h, pivot))
    items = sorted(uniq.items(), key=lambda t: -t[1].shape[0])
    for width in (2048, SHEET_W):
        place, H = shelf(items, width)
        if H <= 4096: break
    at = {k: (x, y) for k, x, y in place}
    sheet = np.zeros((H, width, 4), np.uint8)
    for k, f in items:
        x, y = at[k]; h, w = f.shape[:2]; sheet[y:y + h, x:x + w] = f
    sprites = []
    for n, k, pv in names:
        x, y = at[k]; h, w = uniq[k].shape[:2]
        sprites.append(dict(n=n, x=x, y=H - y - h, w=w, h=h, px=round(pv[0], 5), py=round(pv[1], 5)))
    Image.fromarray(sheet, 'RGBA').save(f'{D}/v{v}/props.png', optimize=True)
    pink = depink(f'{D}/v{v}/props.png'); moved = guard(f'{D}/v{v}/props.png')
    json.dump(dict(sheetW=width, sheetH=H, sprites=sprites), open(f'{D}/v{v}/props.json', 'w'), indent=0)
    print(f'v{v} props: {len(sprites)} sprites, {len(uniq)} unique cells, {width} x {H}, ASTC 6x6 {width * H / 1048576 * 16 / 36:.2f} MB / 4x4 {width * H / 1048576:.2f} MB, pink {pink}, margin {moved}')


if __name__ == '__main__':
    what = [a for a in sys.argv[1:] if a in ('ground', 'sky', 'weather', 'props', 'guard')] or ['sky', 'weather', 'props']
    if 'ground' in what: ground()
    if 'sky' in what: sky()
    if 'weather' in what: weather()
    if 'props' in what:
        for v in (1, 2, 3, 4): pack(v)
    if 'guard' in what:
        for v in (1, 2, 3, 4): print(f'v{v} props guard: {guard(f"{D}/v{v}/props.png")} texels moved out of the pink margin')
