# Procedural reentry plasma for Pause's planetfall: a bow-shock cap on the ship's
# leading (top) face, a sheath wrapping the hull and a turbulent tapering wake
# trailing below. Ship flies UP the screen (nose up), the air streams DOWN.
#
# Cell layout = the installed one: 512 x 1024 per cell, one row, the ship's
# pivot at (256, 338) (PlanetfallDef.entryShipPx), a clear (alpha < 40) opening
# 148 px wide, 80 up / 84 down from the pivot (ArtLoads test), the same in
# every cell (entryHoleX all 256).
#
# usage: python3 fxgen.py <world|all> <frames> <out_tag> [variant]
import sys, os, math
import numpy as np
from PIL import Image
from scipy import ndimage

CW, CH = 512, 1024
PX, PY = 256.0, 338.0
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'out2')

# ---------------------------------------------------------------- palettes
# heat LUT stops (T, rgb): the ablation wake, universal orange -> red; the cap
# tint is the shock-layer gas colour near white, per world.
CLASSIC = [(0.00, (200, 45, 12)), (0.14, (222, 64, 14)), (0.28, (240, 96, 20)), (0.42, (252, 130, 34)),
           (0.56, (255, 178, 70)), (0.70, (255, 216, 130)), (0.84, (255, 243, 205)), (1.00, (255, 254, 246))]
W = {
    'frost':   dict(cap=(205, 232, 255), lut=CLASSIC, halo=(170, 215, 255), sparks=(255, 190, 110), heat=1.0),
    'verdant': dict(cap=(238, 255, 205), lut=[(0.00, (205, 55, 12)), (0.14, (225, 78, 14)), (0.28, (242, 112, 20)), (0.42, (252, 150, 34)),
                    (0.56, (255, 190, 64)), (0.70, (255, 224, 120)), (0.84, (248, 250, 205)), (1.00, (252, 255, 244))],
                    halo=(225, 250, 170), sparks=(255, 200, 90), heat=1.0),
    'ember':   dict(cap=(255, 244, 214), lut=[(0.00, (175, 14, 10)), (0.14, (205, 26, 14)), (0.28, (230, 52, 16)), (0.42, (248, 98, 24)),
                    (0.56, (255, 150, 52)), (0.70, (255, 205, 115)), (0.84, (255, 240, 200)), (1.00, (255, 253, 245))],
                    halo=(255, 190, 120), sparks=(255, 150, 60), heat=1.12),
    'tide':    dict(cap=(212, 255, 238), lut=[(0.00, (205, 85, 14)), (0.14, (225, 105, 18)), (0.28, (240, 130, 24)), (0.42, (252, 162, 40)),
                    (0.56, (255, 200, 80)), (0.70, (250, 228, 140)), (0.84, (236, 250, 220)), (1.00, (248, 255, 250))],
                    halo=(170, 245, 220), sparks=(255, 225, 140), heat=1.0),
    'storm':   dict(cap=(212, 224, 255), lut=[(0.00, (215, 110, 50)), (0.14, (228, 132, 62)), (0.28, (240, 158, 80)), (0.42, (248, 188, 110)),
                    (0.56, (252, 214, 150)), (0.70, (246, 234, 205)), (0.84, (234, 242, 255)), (1.00, (248, 251, 255))],
                    halo=(170, 195, 255), sparks=(225, 235, 255), heat=1.05),
    'desert':  dict(cap=(255, 247, 222), lut=[(0.00, (200, 100, 30)), (0.14, (218, 120, 38)), (0.28, (232, 145, 50)), (0.42, (240, 170, 70)),
                    (0.56, (250, 205, 115)), (0.70, (252, 228, 165)), (0.84, (255, 246, 218)), (1.00, (255, 253, 244))],
                    halo=(255, 225, 160), sparks=(255, 230, 170), heat=1.0),
}

def lut(stops, T):
    xs = np.array([s[0] for s in stops]); cs = np.array([s[1] for s in stops], float) / 255.
    T = np.clip(T, 0, 1)
    return np.stack([np.interp(T, xs, cs[:, k]) for k in range(3)], -1)

# ---------------------------------------------------------------- noise
def field(h, w, sx, sy, seed, alpha=1.6):
    """Smooth random field, unit variance, anisotropic: features ~sx wide, ~sy long."""
    r = np.random.default_rng(seed)
    ky = np.fft.fftfreq(h)[:, None]; kx = np.fft.fftfreq(w)[None, :]
    k = np.sqrt((kx * sx) ** 2 + (ky * sy) ** 2)
    amp = np.exp(-(k * 2.2) ** 2) + .35 * np.exp(-(k * .9) ** 2) * (k > 0.05)
    amp = 1.0 / (1 + (k * 4) ** alpha) * np.exp(-(k * 1.2) ** 2)
    amp[0, 0] = 0
    z = np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((h, w)))).real
    return (z - z.mean()) / (z.std() + 1e-9)

def sample(f, xs, ys):
    return ndimage.map_coordinates(f, [np.mod(ys, f.shape[0]), np.mod(xs, f.shape[1])], order=1, mode='grid-wrap')

def flowed(f, xs, ys, u, dist, evo=None):
    """Noise advected DOWN by `dist` px per loop, seamless in u (two-phase crossfade)."""
    p1 = u % 1.0; p2 = (u + .5) % 1.0
    w1 = 1 - abs(2 * p1 - 1); w2 = 1 - w1
    a = sample(f, xs, ys - dist * p1)
    b = sample(f, xs + 97.0, ys - dist * p2 + 311.0)
    return (w1 * a + w2 * b) / np.sqrt(w1 * w1 + w2 * w2)

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

# ---------------------------------------------------------------- geometry
Y, X = np.mgrid[0:CH, 0:CW].astype(float)
DX = X - PX

def hole_half(y):
    """The opening's half width at row y: the Paragon-class arrowhead hull
    (nose tip at 266, narrow nose to 311, wings flaring to +-71 at 353..365,
    engines to 380) grown by a few px, then rounded off behind the engines.
    Rows 338 (+-68) and the centre column (258 .. 412) meet the ArtLoads
    opening test (147 +-14 wide, up/down within 14)."""
    y = np.asarray(y, float)
    pts = [(250, 0), (258, 3), (270, 10), (290, 18), (306, 27), (314, 40), (320, 58), (338, 68.5), (353, 77),
           (368, 77), (376, 62), (384, 40), (398, 30), (412, 4), (416, 0)]
    xs = [p[0] for p in pts]; ws = [p[1] for p in pts]
    h = np.interp(y, xs, ws, left=0, right=0)
    return h

HOLE = np.abs(X - PX) < hole_half(Y)
HULL = np.abs(X - PX) < np.interp(Y, [250, 258, 270, 290, 306, 314, 320, 338, 353, 366, 372, 377, 381], [0, 3, 10, 18, 27, 40, 58, 66, 72, 72, 62, 30, 0], left=0, right=0)
D_HULL = ndimage.distance_transform_edt(~HULL) - ndimage.distance_transform_edt(HULL)
D_OUT = ndimage.distance_transform_edt(~HOLE)
D_IN = ndimage.distance_transform_edt(HOLE)
D = D_OUT - D_IN
# how much the hull's surface nearest a pixel faces the oncoming air (up):
# the upward component of the outward normal of the (smoothed) envelope
_Ds = ndimage.gaussian_filter(D, 5)
_gy, _gx = np.gradient(_Ds)
_n = np.sqrt(_gx ** 2 + _gy ** 2) + 1e-6
FACING = np.clip(-_gy / _n, 0, 1)                    # 1 faces straight up
NOSE = np.exp(-(((X - PX) / 34.0) ** 2 + ((Y - 262.0) / 40.0) ** 2))   # the stagnation region
DX = X - PX

def out_half(y):
    """The sheath's outer half width: a slender bow shock standing ~14 px off
    the nose tip and riding just outside the wing tips, then the wake
    tapering to its end."""
    s = np.clip(y - 238.0, 0, None)
    shock = np.sqrt(2 * 16.0 * s + (s * .70) ** 2) + 8 * (s > 0)
    yend, ymax = 990.0, 405.0
    t = np.clip((yend - y) / (yend - ymax), 0, 1)
    taper = 118.0 * t ** .9
    w = np.minimum(shock, taper)
    return w * (y >= 238)

# ---------------------------------------------------------------- one cell
def cell(world, u, F, variant):
    P = W[world]
    hot = P['heat']
    cap_c = np.array(P['cap'])[None, None, :] / 255.
    nA = flowed(F['A'], X, Y, u, 260.0)     # long filaments, streaming down
    nB = flowed(F['B'], X, Y, u, 180.0)     # blobs
    nC = flowed(F['C'], X, Y, u, 340.0)     # fine flicker
    th = 2 * np.pi * u
    nE = sample(F['E'], X + 40 * np.cos(th), Y + 40 * np.sin(th))   # boiling (loops)
    down = np.clip((Y - 380.0) / 600.0, 0, 1)
    sway = down ** 1.3 * (20 * np.sin(th + Y / 140.0) + 10 * nB)
    dx = DX - sway
    grow = np.clip((Y - 270) / 140, 0, 1)
    ow = out_half(Y) * (1 + .14 * nA * grow + .05 * nE)
    ow = np.maximum(np.maximum(ow, 1e-3), np.where(Y < 420, hole_half(Y) + 16 * (hole_half(Y) > 0), 1e-3))
    r = np.where(Y < 238, 9.0, np.abs(dx) / ow)
    capd = np.clip(D, 0, None)
    along = np.clip((Y - 300.0) / 660.0, 0, 1)
    layer = 1 - sstep(.66, 1.0, r)
    if variant == 0:
        rim = sstep(-1.0, 2.5, D)
    else:
        rim = np.ones_like(D)
    face = np.maximum(FACING * np.exp(-capd / 30.0), NOSE)
    # temperature
    t_face = layer * (.30 + .85 * face) * np.exp(-capd / 26.0)            # the shock layer on the leading faces
    t_side = layer * np.exp(-capd / 34.0) * (1 - along) ** 1.25 * .80     # the sheath streaming back
    core = np.exp(-(dx / (ow * (.38 + .30 * along) + 1)) ** 2) * sstep(372, 430, Y) * (1 - along) ** 1.5 * .78
    shock_line = np.exp(-((r - .84) / .08) ** 2) * sstep(.25, .9, face + NOSE) * .35
    T = np.maximum.reduce([t_face, t_side, core]) + shock_line
    turb = .80 + .28 * nA + .16 * nC + .10 * nE
    T = T * np.clip(turb, .2, 1.6) * (1 - .40 * r ** 2) * hot
    # the leading faces are white-hot whatever the turbulence does
    T = np.maximum(T, layer * np.clip(face, 0, 1) * np.exp(-capd / 16.0) * 1.05 * hot)
    filaments = np.clip(.64 + .34 * nA + .22 * nB + .12 * nC, 0, 1.4)
    dens = layer * rim * filaments
    dens *= (1 - sstep(.55, 1.0, along) * (.25 + .75 * sstep(.2, .9, r + .3 * nB)))
    a = np.clip(dens * (.04 + 1.45 * np.clip(T, 0, 2) ** 1.15), 0, 1)
    # the leading faces are solid plasma
    a = np.maximum(a, layer * rim * np.clip(face, 0, 1) * np.exp(-capd / 18.0) * .9)
    if variant == 0:   # keep the opening's edge crisp enough for the test (alpha >= 40 right outside it)
        a = np.maximum(a, rim * sstep(-1, 3, D) * (1 - sstep(4, 10, D)) * .26 * (Y >= 252))
    col = lut(P['lut'], T)
    capw = np.clip((face * 1.1) * np.exp(-capd / 22.0), 0, 1)[..., None] * .65
    # the shock-layer tint: multiply (keeps the white-hot brightness), never a grey mix
    col = col * (1 - capw + capw * cap_c / cap_c.max())

    # under the hull: variant 0 clear (alpha < 40/255: a faint glow through
    # the hull's gaps only), variant 1 the sheath continues (hot near the nose)
    hole_t = np.clip((.40 + .60 * np.maximum(NOSE, sstep(330, 270, Y) * .7)) * (.9 + .12 * nC), 0, 1.05) * hot
    c_in = lut(P['lut'], hole_t)
    cw_in = (np.maximum(NOSE, .4 * sstep(330, 270, Y)) * .6)[..., None]
    c_in = c_in * (1 - cw_in) + cap_c * cw_in
    if variant == 0:
        a_in = .145 * np.exp(-D_IN / 6.0)
        c_in = c_in * .3 + np.array([1, .97, .9])[None, None, :] * .7
    else:
        a_in = np.clip(.82 + .12 * nA, .6, .95)
    k_in = HOLE.astype(float) if variant == 0 else (1 - sstep(-6, 1.5, D_HULL))
    col = col * (1 - k_in[..., None]) + c_in * k_in[..., None]
    a = a * (1 - k_in) + a_in * k_in

    pm = col * a[..., None]
    # glow: the plasma's own light, blurred, plus a tinted glow ahead of the leading faces
    halo_pm = ndimage.gaussian_filter(pm, (16, 16, 0)) * .55
    halo_a = ndimage.gaussian_filter(a, 16) * .45
    src = a * capw[..., 0] / .65 * sstep(350, 300, Y)
    capglow = np.clip(ndimage.gaussian_filter(src, 24) * 2.0, 0, .6) * (D > 0)
    hc = np.array(P['halo'])[None, None, :] / 255.
    ha = np.clip(halo_a + capglow, 0, 1)
    hpm = halo_pm + hc * capglow[..., None]
    out_pm = pm + hpm * (1 - a)[..., None]
    out_a = a + ha * (1 - a)
    sp_a, sp_c = sparks(world, u, F['seed'])
    out_pm = sp_c * sp_a[..., None] + out_pm * (1 - sp_a)[..., None]
    out_a = sp_a + out_a * (1 - sp_a)
    if variant == 0:   # nothing may build past alpha 40 inside the opening
        lim = np.where(HOLE, .148, 1.0)
        f = np.minimum(1, lim / np.maximum(out_a, 1e-6))
        out_pm = out_pm * f[..., None]; out_a = out_a * f
    rgb = np.clip(out_pm / np.maximum(out_a, 1e-6)[..., None], 0, 1)
    return rgb, np.clip(out_a, 0, 1)

def sparks(world, u, seed):
    r = np.random.default_rng(seed + 7)
    n = 22 if world != 'ember' else 40
    a = np.zeros((CH, CW)); c = np.zeros((CH, CW, 3))
    base = np.array(W[world]['sparks']) / 255.
    if not len(W[world]['sparks']): return np.zeros((CH, CW)), np.zeros((CH, CW, 3))
    for i in range(n):
        side = r.choice([-1, 1])
        x0 = PX + side * r.uniform(40, 120)
        y0 = r.uniform(330, 470)
        L = r.uniform(380, 620)               # its fall over one loop... in px
        ph = r.random()
        loops = r.choice([1, 1, 2])
        p = (u * loops + ph) % 1.0
        y = y0 + p * L
        x = x0 + side * p * r.uniform(-30, 25) - side * p * p * 30 + 8 * math.sin(2 * math.pi * (p + ph))
        life = math.sin(math.pi * p) ** .7
        bright = r.uniform(.5, 1.0) * life
        ln = r.uniform(5, 14) * (1 + p)
        wd = r.uniform(1.2, 2.2)
        x0b, x1b = int(max(0, x - 6)), int(min(CW, x + 7)); y0b, y1b = int(max(0, y - ln - 4)), int(min(CH, y + 6))
        if x1b <= x0b or y1b <= y0b: continue
        yy, xx = np.mgrid[y0b:y1b, x0b:x1b].astype(float)
        t = np.clip((y - yy) / ln, 0, 1)
        d = np.abs(xx - x)
        m = np.exp(-(d / wd) ** 2) * (1 - t) ** 1.5 * (yy <= y + 1.5) * np.exp(-np.clip(yy - y, 0, None) ** 2 / 2)
        m *= bright
        hotc = base * (1 - t[..., None] * .35)
        aa = a[y0b:y1b, x0b:x1b]
        c[y0b:y1b, x0b:x1b] = hotc * m[..., None] + c[y0b:y1b, x0b:x1b] * (1 - m[..., None])
        a[y0b:y1b, x0b:x1b] = m + aa * (1 - m)
    c = np.where(a[..., None] > 1e-6, c / np.maximum(a, 1e-6)[..., None], 0)
    return np.clip(a, 0, 1) * np.logical_not(HOLE), np.clip(c, 0, 1)

def fields(seed):
    return dict(seed=seed,
                A=field(1024, 512, 14, 70, seed + 1),   # long filaments
                B=field(1024, 512, 40, 90, seed + 2),   # blobs
                C=field(1024, 512, 6, 22, seed + 3),    # fine
                E=field(1024, 512, 30, 30, seed + 4))   # boiling

def is_pink(rgb, a):
    import colorsys
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    h = np.zeros_like(mx)
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return (a > 8 / 255.) & (s > .25) & (mx > .3) & (h >= 285) & (h <= 352)

def depink(rgb, a):
    m = is_pink(rgb, a)
    if m.any():
        g = rgb[m].mean(-1, keepdims=True)
        rgb[m] = g + (rgb[m] - g) * .35   # pull the stray mixes toward grey (s < .25)
    return rgb

def strip(world, frames, seed=11, variant=0):
    F = fields(seed)
    img = np.zeros((CH, CW * frames, 4))
    for f in range(frames):
        rgb, a = cell(world, f / frames, F, variant)
        rgb = depink(rgb, a)
        if f == 0:
            # one wake strength for every world (the seeds differ): scale the
            # wake's opacity to a common mean, fixed from the first cell
            reg = a[420:820, 176:336]
            gain = float(np.clip(.30 / max(reg.mean(), 1e-3), .75, 1.6))
        wake = sstep(345.0, 405.0, Y) * (~HOLE if variant == 0 else 1)
        a = np.clip(a * (1 + (gain - 1) * wake), 0, 1)
        img[:, f * CW:(f + 1) * CW, :3] = rgb; img[:, f * CW:(f + 1) * CW, 3] = a
    return img

def save(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(np.round(np.clip(img, 0, 1) * 255).astype(np.uint8), 'RGBA').save(path, optimize=True)

def check(img, frames):
    rgb, a = img[..., :3], img[..., 3]
    pink = int(is_pink(rgb, a).sum())
    A = (a * 255).round().astype(int)
    spans = []
    for i in range(frames):
        cx, cy = i * CW + 256, int(PY)
        def run(dx, dy):
            n = 0
            while n < 200:
                x, y = cx + dx * (n + 1), cy + dy * (n + 1)
                if x < 0 or y < 0 or x >= A.shape[1] or y >= A.shape[0] or A[y, x] >= 40: break
                n += 1
            return n
        spans.append((run(-1, 0), run(1, 0), run(0, -1), run(0, 1)))
    ok = all(abs(l - r) <= 12 and abs(l + r - 147) <= 14 and abs(u - d) <= 14 for l, r, u, d in spans)
    return pink, spans, ok

if __name__ == '__main__':
    worlds = list(W) if sys.argv[1] == 'all' else [sys.argv[1]]
    frames = int(sys.argv[2]); tag = sys.argv[3]
    for w in worlds:
        img = strip(w, frames, seed=11 + 13 * list(W).index(w), variant=int(sys.argv[4]) if len(sys.argv) > 4 else 0)
        pink, spans, ok = check(img, frames)
        save(img, f'{OUT}/bd_{w}_entry_fx/{tag}.png')
        print(w, img.shape[1], 'x', img.shape[0], 'pink px', pink, 'opening ok', ok, spans[:2], flush=True)
