"""Ember soft painted weather (r1): the old weather.png sprite names, repainted procedurally (no generation): soft billowing ash banks and
wisps lit from the upper left, low warm heat-haze mist, diagonal ash gusts, dark smoke palls, a drift of orange embers. Cells 256 x 256,
translucency baked (banks <= 80/255 like the old sheet), warm ash palette (never pink), edges feathered to 0.  python3 weather_build.py"""
import json, os, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
D = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '../../../Backgrounds/Resources/Worlds/Ember/Backdrop3'))
N = 256
def fbm(r, octs=((3, 1), (6, .55), (12, .3), (24, .16), (48, .08))):
    z = np.zeros((N, N))
    for sig_div, amp in octs:
        z += amp * gaussian_filter(r.standard_normal((N, N)), N / sig_div / 2.2)
    return (z - z.mean()) / (z.std() + 1e-9)
def edge(power=1.6, sx=1.0, sy=1.0):
    y, x = np.mgrid[0:N, 0:N] / (N - 1) * 2 - 1
    return np.clip(1 - (np.abs(x * sx) ** power + np.abs(y * sy) ** power), 0, 1) ** .8
def lit(m, r, dark, light):
    """shade a density field m (0..1) lit from the upper left: lighter where the field rises toward the upper-left"""
    gy, gx = np.gradient(gaussian_filter(m, 3))
    s = np.clip(.5 - (gx + gy) * 45, 0, 1)[..., None]
    col = np.array(dark) * (1 - s) + np.array(light) * s
    return col
def bank(seed, amax=.31, dark=(.30, .23, .19), light=(.70, .56, .44), th=.0, k=1.0):
    r = np.random.default_rng(seed); z = fbm(r, ((2, 1), (4, .6), (8, .28), (16, .1)))
    t = np.clip((z - th) / 1.4, 0, 1); m = t * t * (3 - 2 * t) * edge(1.8) * k
    m = gaussian_filter(m, 2.0)
    a = np.clip(m * amax * 1.5, 0, amax)
    return np.dstack([lit(m, r, dark, light), a])
def wisp(seed):
    r = np.random.default_rng(seed); z = fbm(r, ((2, 1), (5, .7), (11, .4), (22, .2)))
    m = np.clip((z + .1) / 1.8, 0, 1) ** 1.2 * edge(2.2, 1, 1.5); m = gaussian_filter(m, 2.0)
    return np.dstack([lit(m, r, (.36, .28, .22), (.70, .57, .45)), np.clip(m * .55, 0, .27)])
def mist(seed):
    r = np.random.default_rng(seed); z = fbm(r, ((2, 1), (4, .5), (9, .2)))
    m = np.clip((z + .5) / 2.2, 0, 1) * edge(1.4)
    m = gaussian_filter(m, 3)
    col = np.dstack([np.full((N, N), .46), np.full((N, N), .30), np.full((N, N), .20)]) * (.85 + .3 * np.clip(m, 0, 1))[..., None]
    return np.dstack([col, np.clip(m * .30, 0, .22)])
def gust(seed):
    r = np.random.default_rng(seed); z = fbm(r, ((2, 1), (4, .6), (8, .3)))
    yy, xx = np.mgrid[0:N, 0:N]
    u = (xx * .86 + yy * .5); v = (-xx * .5 + yy * .86)           # diagonal streaks: noise stretched along one axis
    st = gaussian_filter(r.standard_normal((N, N)), (N / 200, N / 6))
    st = (st - st.mean()) / st.std()
    ang = np.rad2deg(np.arctan2(.5, .86))
    from scipy.ndimage import rotate
    st = rotate(gaussian_filter(r.standard_normal((N * 2, N * 2)), (N / 220, N / 5)), -30, reshape=False)[N // 2:N // 2 + N, N // 2:N // 2 + N]
    st = (st - st.mean()) / (st.std() + 1e-9)
    m = np.clip((st + .1) / 2.2, 0, 1) * edge(2.0, 1, 1.2) * np.clip((z + 1.2) / 2.4, 0, 1)
    m = gaussian_filter(m, .9)
    return np.dstack([lit(m, r, (.36, .28, .22), (.66, .54, .42)), np.clip(m * .42, 0, .28)])
def pall(seed):
    r = np.random.default_rng(seed); z = fbm(r, ((2.5, 1), (5, .8), (10, .45), (20, .2)))
    m = np.clip((z + .1) / 1.7, 0, 1) ** .9 * edge(1.5); m = gaussian_filter(m, 1.5)
    return np.dstack([lit(m, r, (.16, .12, .10), (.36, .27, .21)), np.clip(m * .46, 0, .31)])
def embers(seed):
    r = np.random.default_rng(seed); a = np.zeros((N, N)); col = np.zeros((N, N, 3))
    drift = np.zeros((N, N))
    for _ in range(34):
        x, y = r.uniform(18, N - 18, 2); s = r.uniform(1.2, 2.6); b = r.uniform(.45, 1.0)
        yy, xx = np.ogrid[0:N, 0:N]; d2 = (xx - x) ** 2 + (yy - y) ** 2
        g = np.exp(-d2 / (2 * s * s)) * b; drift = np.maximum(drift, g)
        # a short upward-right trail
        for t in range(1, 6):
            drift = np.maximum(drift, np.exp(-((xx - x - t * 1.4) ** 2 + (yy - y + t * 2.2) ** 2) / (2 * (s * .8) ** 2)) * b * (1 - t / 6) * .5)
    m = np.clip(drift, 0, 1) * edge(3, 1, 1)
    hot = np.clip((m - .5) * 2, 0, 1)[..., None]
    col = np.array([.86, .38, .08]) * (1 - hot) + np.array([.92, .66, .22]) * hot          # orange to amber, never pink; held under the shots
    col = np.broadcast_to(col, (N, N, 3))
    return np.dstack([col, np.clip(m * .85, 0, .8)])
CELLS = {}
for i in range(4): CELLS[f'cloud_bank_{i:02d}'] = bank(100 + i, th=-.1 + .15 * (i % 2), k=1.0)
for i in range(4): CELLS[f'cloud_wisp_{i:02d}'] = wisp(200 + i)
for i in range(3): CELLS[f'mist_{i:02d}'] = mist(300 + i)
for i in range(3): CELLS[f'ashgust_{i:02d}'] = gust(400 + i)
for i in range(2): CELLS[f'smokepall_{i:02d}'] = pall(500 + i)
CELLS['embers_00'] = embers(600)
def fit(c, w, h):
    if c.shape[0] == h and c.shape[1] == w: return c
    pm = c.copy(); pm[..., :3] *= pm[..., 3:]
    o = np.dstack([np.asarray(Image.fromarray(pm[..., i].astype(np.float32), 'F').resize((w, h), Image.BOX)) for i in range(4)])
    o[..., :3] = np.where(o[..., 3:] > 1e-4, o[..., :3] / np.maximum(o[..., 3:], 1e-4), 0); return np.clip(o, 0, 1)


if __name__ == '__main__':
    js = json.load(open(f'{D}/weather.json')); old = np.asarray(Image.open(f'{D}/weather.png').convert('RGBA')).astype(float) / 255
    H = old.shape[0]; sheet = old.copy()
    for s in js['sprites']:
        if s['n'] not in CELLS: continue
        y = H - s['y'] - s['h']; c = fit(CELLS[s['n']], s['w'], s['h'])
        sheet[y:y + s['h'], s['x']:s['x'] + s['w']] = np.clip(c, 0, 1)
    Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA').save(f'{D}/weather.png', optimize=True)
    print('weather.png:', sum(1 for s in js['sprites'] if s['n'] in CELLS), 'cells replaced')
    # preview
    bg = Image.new('RGBA', (1024, 1024), (60, 44, 36, 255)); bg.alpha_composite(Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA'))
    bg.convert('RGB').save('/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl/ember/r1/ref/weather_sheet.png')
