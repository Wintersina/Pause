"""Scripted (0-credit) Storm props cut from the approved cloud paint itself: the storm-eye vortex and the lightning-lit
cloud cell. Both are made of the ground's own pixels / light, so they cannot drift from its look."""
import numpy as np, os
from PIL import Image
from scipy.ndimage import gaussian_filter, map_coordinates
OUT = '/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl/out2'
def tile(v): return np.asarray(Image.open(f'tiles/v{v}_mid.png').convert('RGB')).astype(float) / 255
def strip(frames, path):
    c = len(frames); h, w = frames[0].shape[:2]; s = np.concatenate(frames, 1)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray((np.clip(s, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(path)

def vortex(v, cy, cx, N=320, turns=1.15, eye=.11, seed=0, glow=(.72, .66, 1.0)):
    """A spiral storm cell: a disc of the tile wound into a log-spiral round a dark eye; the eyewall lit violet-white by
    lightning in some frames. RGBA, alpha feathered so the arms dissolve into the deck. The game spins it slowly."""
    T = tile(v); H, W = T.shape[:2]; R = N / 2
    yy, xx = np.mgrid[0:N, 0:N].astype(float) - R + .5
    r = np.hypot(xx, yy) / R; th = np.arctan2(yy, xx)
    wind = turns * 2 * np.pi * (1 - np.clip(r, 0, 1)) ** 1.6          # inner rings wound tightest
    rs = r * R * (1.0 + .6 * r) + 6 * np.sin(5 * th + 3 * r)                                         # sample a slightly larger area (arms stretched)
    sx = cx + rs * np.cos(th + wind); sy = cy + rs * np.sin(th + wind) * 1.0
    rgb = np.stack([map_coordinates(T[..., i], [sy % H, sx % W], order=1, mode='grid-wrap') for i in range(3)], -1)
    L = rgb @ np.array([.3, .5, .2])
    # spiral arm contrast: brighten along two log-spiral arms, darken between, deepen the eye
    arm = .5 + .5 * np.cos(2 * (th + wind * .9) )
    arm = gaussian_filter(arm, 2)
    k = (.9 + .2 * arm) * np.clip((r - eye * .6) / (eye * 1.4), .25, 1) ** .8
    rgb = np.clip(rgb * k[..., None], 0, 1)
    eyewall = np.exp(-((r - eye * 1.6) / (eye * .9)) ** 2)
    alpha = np.clip((1 - r) / .6, 0, 1) ** 1.6
    frames = []
    sched = [0, 0, .15, 1, .45, .1, .6, .2]      # lightning in the eyewall (two pulses .375 s apart at 125 ms -> see durs)
    rng = np.random.default_rng(seed)
    sector = np.exp(-(((th - rng.uniform(-np.pi, np.pi) + np.pi) % (2 * np.pi) - np.pi) / .9) ** 2)
    for s in sched:
        g = s * eyewall * (.35 + .65 * sector) * np.clip(L * 3, 0, 1) ** .7
        f = np.clip(rgb + g[..., None] * np.array(glow) * .9, 0, 1)
        frames.append(np.dstack([f, alpha]))
    return frames

def cell(v, cy, cx, N=256, seed=0, lo=(.62, .58, 1.0), hi=(.9, .93, 1.0)):
    """Lightning lighting a cloud tower from inside: a LIGHT overlay (draw additively, like Storm's glow layer) whose
    shape is the cloud's own texture, so the clump under it lights up rather than a flat blob. Frames = flash envelope."""
    T = tile(v); H, W = T.shape[:2]
    ys = (np.arange(N) + cy - N // 2) % H; xs = (np.arange(N) + cx - N // 2) % W
    c = T[np.ix_(ys, xs)]; L = c @ np.array([.3, .5, .2])
    yy, xx = np.mgrid[0:N, 0:N].astype(float) - N / 2
    rng = np.random.default_rng(seed)
    fall = np.exp(-(xx ** 2 + (yy * 1.15) ** 2) / (2 * (N * .2) ** 2))
    tex = np.clip((L - np.percentile(L, 35)) / (np.percentile(L, 99) - np.percentile(L, 35)), 0, 1) ** 1.2
    light = np.clip(.55 * tex + .45 * gaussian_filter(tex, 10), 0, 1) * fall
    light /= light.max(); light = light ** .7
    hot = light ** 2.2
    col = np.array(lo)[None, None] * (1 - hot[..., None]) + np.array(hi)[None, None] * hot[..., None]
    frames = []
    for s in [0, .2, 1, .55, .25, .75, .3, .08]:   # durs make the two peaks >= .45 s apart (StormTuning.MinPulseGap .4)
        frames.append(np.dstack([col, np.clip(light * s * .95, 0, 1)]))
    return frames
CELL_MS = [1700, 90, 140, 200, 420, 130, 260, 400]
VORTEX_MS = [1500, 900, 90, 150, 220, 380, 160, 600]
