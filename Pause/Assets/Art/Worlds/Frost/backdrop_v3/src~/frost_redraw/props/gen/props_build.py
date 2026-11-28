# Build graded frame strips + in-game-scale GIF previews for the Frost living props.
import os, glob, colorsys, math, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
os.chdir(os.path.dirname(os.path.abspath(__file__)))
B3 = '/Users/sina/Developer/Pause-worktrees/enemy-art/Pause/Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3'
OUT = 'out2'; PV = 'props'
LIFT, SAT, SHADE = 1.95, 1.05, .92
WATER = np.array([14, 26, 42]) / 255.

def load(p): return np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255.
def toim(a): return Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
def hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; v = a[..., :3].max(-1); mn = a[..., :3].min(-1); c = v - mn
    s = np.where(v > 0, c / np.maximum(v, 1e-6), 0)
    h = np.zeros_like(v); m = c > 1e-6
    rc = np.where(m & (v == r), ((g - b) / np.maximum(c, 1e-6)) % 6, 0)
    gc = np.where(m & (v == g) & (v != r), (b - r) / np.maximum(c, 1e-6) + 2, 0)
    bc = np.where(m & (v == b) & (v != r) & (v != g), (r - g) / np.maximum(c, 1e-6) + 4, 0)
    h = (rc + gc + bc) * 60
    return h, s, v

# ---------- lights ----------
def lights(a, maxarea=40):
    h, s, v = hsv(a); al = a[..., 3] > .5
    red = al & (s > .55) & (v > .55) & ((h < 14) | (h > 354))
    amber = al & (s > .4) & (v > .62) & (h >= 22) & (h <= 58)
    teal = al & (s > .35) & (v > .55) & (h >= 160) & (h <= 200)
    out = {}
    for k, m in (('red', red), ('amber', amber), ('teal', teal)):
        lab, n = ndi.label(m, np.ones((3, 3)))
        comps = [lab == i for i in range(1, n + 1)]
        out[k] = [c for c in comps if c.sum() <= maxarea]
    return out

# ---------- grade: forms like the landmarks (value p90 ~.45 painted), lights kept bright ----------
def grade_factor(a, lightmask):
    h, s, v = hsv(a); m = (a[..., 3] > .5) & ~lightmask
    p90 = np.percentile(v[m], 90) if m.any() else 1
    return min(1., .46 / p90)
def grade(a, k, lightmask, tint=None):
    o = a.copy(); rgb = o[..., :3] * k
    v = rgb.max(-1, keepdims=True)
    soft = np.where(v > .5, .5 + (v - .5) * .35, v)            # roll the whites off
    rgb = rgb * np.where(v > 0, soft / np.maximum(v, 1e-6), 0)
    if tint is not None: rgb = rgb * np.array(tint)
    lm = lightmask[..., None]
    o[..., :3] = np.where(lm, np.minimum(a[..., :3], .92), rgb)
    return o
def pinkcheck(a):
    h, s, v = hsv(a); return int(((a[..., 3] > 0) & (s > .25) & (v > .3) & (h >= 285) & (h <= 352)).sum())

def halo(a, mask, col, alpha=.45, it=1):
    """soft glow ring round a lamp, drawn under it"""
    d = ndi.binary_dilation(mask, np.ones((3, 3)), iterations=it) & ~mask
    o = a.copy(); add = d & (o[..., 3] < .99) | d
    o[add, :3] = o[add, :3] * (1 - alpha) + np.array(col) * alpha
    o[add, 3] = np.maximum(o[add, 3], alpha)
    return o

# ---------- water creatures ----------
def split_water(a, spout_rows=None):
    h, s, v = hsv(a); al = a[..., 3] > .1
    white = al & (v > .62) & (s < .35)
    body = al & ~white
    # white bits enclosed by the body (eye / saddle patches) are body
    lab, n = ndi.label(white, np.ones((3, 3)))
    for i in range(1, n + 1):
        c = lab == i; ring = ndi.binary_dilation(c, np.ones((3, 3))) & ~c
        if (body & ring).sum() >= .7 * ring.sum(): body |= c; white &= ~c
    spout = np.zeros_like(white)
    if spout_rows is not None:
        y0, y1, x0, x1 = spout_rows
        seed = np.zeros_like(white); seed[y0:y1, x0:x1] = True; seed &= white
        lab, n = ndi.label(white, np.ones((3, 3)))
        ids = set(np.unique(lab[seed])) - {0}
        spout = np.isin(lab, list(ids))
        spout = ndi.binary_dilation(spout, np.ones((3, 3)), iterations=2) & al & ~(white & ~spout)
        body &= ~spout
    foam = white & ~spout
    return body, spout, foam
def layer(a, m):
    o = a.copy(); o[..., 3] = np.where(m, a[..., 3], 0); return o
def scale_about(a, s, cx, cy, sy=None):
    sy = sy or s; H, W = a.shape[:2]
    im = toim(a); nw, nh = max(1, int(W * s)), max(1, int(H * sy))
    r = im.resize((nw, nh), Image.NEAREST); c = Image.new('RGBA', (W, H))
    c.paste(r, (int(cx - cx * s), int(cy - cy * sy)), r)
    return np.asarray(c).astype(float) / 255.
def over(dst, src):
    sa = src[..., 3:4]; da = dst[..., 3:4]; oa = sa + da * (1 - sa)
    rgb = (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
    return np.concatenate([rgb, oa], -1)
def fade(a, al): o = a.copy(); o[..., 3] *= al; return o
def submerge(body, vis):
    o = body.copy(); o[..., :3] = o[..., :3] * vis + WATER * (1 - vis)
    o[..., 3] *= min(1, .25 + .75 * vis) if vis > .05 else 0; return o
def creature(a, spout_rows, sched, k=None):
    body, spout, foam = split_water(a, spout_rows)
    L = lights(a); lm = np.zeros(a.shape[:2], bool)
    k = k or grade_factor(a, lm); g = grade(a, k, lm)
    B, S, F = layer(g, body), layer(g, spout), layer(g, foam)
    if spout.any():   # the spout hid part of the head: close the notch with the body's own dark tone
        yy = np.arange(body.shape[0])[:, None]; byc = np.nonzero(body)[0].mean()
        hole = ndi.binary_closing(body, np.ones((3, 3)), iterations=9) & ~body & (yy < byc) & ~foam
        bm = np.median(g[body][:, :3], 0); B[hole, :3] = bm; B[hole, 3] = 1
    ys, xs = np.nonzero(body); cx, cy = xs.mean(), ys.mean()
    sy_, sx_ = np.nonzero(spout) if spout.any() else (np.array([cy]), np.array([cx]))
    base_y = sy_.max(); scx = sx_.mean()
    frames = []
    for bv, ss, sa, fs, fa in sched:
        f = np.zeros_like(a)
        if fa > 0: f = over(f, fade(scale_about(F, fs, cx, cy), fa))
        if bv > 0: f = over(f, submerge(B, bv))
        if sa > 0 and ss > 0:
            top = sy_.min(); hgt = base_y - top + 1
            if ss <= 1:   # the column rises out of the blowhole: reveal its lower part, nudged down
                cut = base_y - ss * hgt; Sx = S.copy(); Sx[:int(cut)] = 0
                sh = int((1 - ss) * hgt * .5)
                if sh: Sx = np.concatenate([np.zeros_like(Sx[:sh]), Sx[:-sh]], 0)
            else:          # the cloud spreads and drifts as it fades
                Sx = scale_about(S, ss, scx, base_y, ss)
            f = over(f, fade(Sx, sa))
        frames.append(f)
    return frames

# ---------- lit structures ----------
def lit(a, seed=0, blink=(0, 1), flicker=3, n=8, tint=None, strobe=None, add_beacon=None):
    a = a.copy()
    if add_beacon is not None:   # a lamp on a mast tip the generator left dark
        y, x = add_beacon; a[y:y + 2, x:x + 2] = (.95, .2, .12, 1)
    L = lights(a)
    if L['red']:                 # only the topmost red lamps blink (a red hull is not a beacon)
        ymin = min(np.nonzero(c)[0].mean() for c in L['red'])
        L['red'] = [c for c in L['red'] if np.nonzero(c)[0].mean() <= ymin + 6 and c.sum() <= 24]
    H, _, _ = hsv(a)
    L['amber'] = [c for c in L['amber'] if 2 <= c.sum() <= 16 and H[c].mean() > 30] or L['amber']
    lm = np.zeros(a.shape[:2], bool)
    for k in L:
        for c in L[k]: lm |= c
    k = grade_factor(a, lm); g = grade(a, k, lm, tint)
    rng = np.random.RandomState(seed)
    win = L['amber'][rng.randint(len(L['amber']))] if L['amber'] else None
    frames = []
    for i in range(n):
        f = g.copy()
        for c in L['red']:
            if i in blink:
                f = halo(f, c, (1, .3, .15), .35, 4); f = halo(f, c, (1, .35, .2), .7, 2)
                core = ndi.binary_dilation(c, np.ones((3, 3))); f[core, :3] = (1, .42, .3); f[core, 3] = 1
            else: f[c, :3] = a[c, :3] * .35
        if win is not None:
            dim = {flicker: .25, flicker + 1: .8, flicker + 2: .45}.get(i, 1)
            f[win, :3] = a[win, :3] * dim
        for c in L['teal']:
            f[c, :3] = np.minimum(.92, a[c, :3] * (.75 + .25 * math.cos(2 * math.pi * i / n)))
        if strobe is not None and i == strobe[0]:
            y, x = strobe[1]; m = np.zeros(a.shape[:2], bool); m[y - 1:y + 1, x - 1:x + 1] = True
            f = halo(f, m, (.9, .95, 1), .6); f[m] = (.95, .97, 1, 1)
        frames.append(f)
    return frames, L

def seq(paths, tint=None, blinkfrom=None):
    fr = [load(p) for p in paths]; L = lights(fr[0]); lm = np.zeros(fr[0].shape[:2], bool)
    for kk in L:
        for c in L[kk]: lm |= c
    k = grade_factor(fr[0], lm)
    return [grade(f, k, lights_mask(f), tint) for f in fr]
def lights_mask(a):
    L = lights(a); m = np.zeros(a.shape[:2], bool)
    for k in L:
        for c in L[k]: m |= c
    return m

# ---------- previews ----------
def curve(v, k): return 1 - np.power(np.clip(1 - v, 0, 1), k)
def lift(a, k=LIFT, sat=SAT, shade=1.):
    o = a.copy(); v = o[..., :3].max(-1, keepdims=True)
    rgb = o[..., :3] * np.where(v > 1e-4, curve(v, k) / np.maximum(v, 1e-4), 0)
    l = rgb.mean(-1, keepdims=True); rgb = np.clip(l + (rgb - l) * sat, 0, 1) * shade
    o[..., :3] = rgb; return o
GROUND = {}
def ground(var):
    if var not in GROUND:
        g = lift(load(f'{B3}/{var}/mid.png'), shade=SHADE); g[..., 3] = 1
        m = [l.rstrip('\n') for l in open(f'{B3}/{var}/mask.txt') if l.strip()]
        GROUND[var] = (g, np.array([list(l) for l in m]))
    return GROUND[var]
def spot(var, cls, size, nth=0):
    g, m = ground(var); k = int(math.ceil(size / 7)) + 1; H, W = m.shape; best = []
    ok = np.isin(m, list(cls))
    for y in range(4, H - k - 4):
        for x in range(2, W - k - 2):
            best.append((ok[y:y + k, x:x + k].mean(), -abs(y - H / 2), y, x))
    best.sort(reverse=True)
    picks = []
    for b in best:
        if all(abs(b[2] - p[2]) > 12 for p in picks): picks.append(b)
        if len(picks) > nth: break
    s, _, y, x = picks[nth % len(picks)]
    return (x + k / 2) * 7, (y + k / 2) * 7, s
def shrink(f, size):
    im = toim(f); w, h = im.size; sc = size / max(w, h)
    # premultiplied box-ish minification, as a mipmapped bilinear sprite draws
    a = np.asarray(im).astype(float) / 255.; pm = a.copy(); pm[..., :3] *= pm[..., 3:4]
    r = np.asarray(Image.fromarray((pm * 255).astype(np.uint8), 'RGBA').resize((max(1, round(w * sc)), max(1, round(h * sc))), Image.BOX)).astype(float) / 255.
    r[..., :3] = r[..., :3] / np.maximum(r[..., 3:4], 1e-4); return np.clip(r, 0, 1)
def preview(name, frames, durs, var, cls, size, out, nth=0, label=''):
    g, _ = ground(var); cx, cy, score = spot(var, cls, size, nth)
    win, Z = 240, 3; zw = 80
    gifs = []
    for f in frames:
        p = lift(shrink(f, size)); h, w = p.shape[:2]
        G = g.copy(); x0, y0 = int(cx - w / 2), int(cy - h / 2)
        G[y0:y0 + h, x0:x0 + w] = over(G[y0:y0 + h, x0:x0 + w], p)
        ax = int(np.clip(cx - win / 2, 0, 448 - win)); ay = int(np.clip(cy - win / 2, 0, 896 - win))
        left = toim(G[ay:ay + win, ax:ax + win])
        zx = int(np.clip(cx - zw / 2, 0, 448 - zw)); zy = int(np.clip(cy - zw / 2, 0, 896 - zw))
        right = toim(G[zy:zy + zw, zx:zx + zw]).resize((zw * Z, zw * Z), Image.NEAREST)
        c = Image.new('RGB', (win + 8 + zw * Z, max(win, zw * Z) + 16), (12, 14, 20))
        c.paste(left.convert('RGB'), (0, 16)); c.paste(right.convert('RGB'), (win + 8, 16))
        ImageDraw.Draw(c).text((3, 2), f'{label}  1:1 phone px (zoom .41)   |   3x', fill=(200, 210, 220))
        gifs.append(c)
    gifs[0].save(out, save_all=True, append_images=gifs[1:], duration=durs, loop=0, disposal=2)
    return gifs, score
def strip(frames, out):
    h, w = frames[0].shape[:2]; c = Image.new('RGBA', (w * len(frames), h))
    for i, f in enumerate(frames): c.paste(toim(f), (i * w, 0))
    c.save(out)
