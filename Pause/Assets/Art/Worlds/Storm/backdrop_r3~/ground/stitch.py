"""r3 stitch: one LARGE-period seamless tile per layout, quilted (0 credits) from the approved r2_3 paint.
Install tile 896 x 4096 drawn at TILE ZOOM .82 of the view's width = two of the old .41 columns side by side, so the
texel density is unchanged (~1 texel per screen px, like Frost's 448 px at .41) but the ground repeats every 886 px across
(wider than 4/5 of the screen) and every ~4050 px down (1.7 screens): no repeat inside any one screen.
The large-scale guide is a NEW wind-sheared belt field (warped, broken, with eddy cells) instead of r2_3's 3-turn shear,
so there is no regular diagonal stamp; local detail is quilted only from r2_3's own pixels (palette/realism kept)."""
import sys, os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, map_coordinates
R2 = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'r2')
sys.path.insert(0, R2)
from common import resize_lin, save
import synth2
SP = '/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl'
OW, OH = 896, 4096
v = int(sys.argv[1]); seed = int(sys.argv[2]) if len(sys.argv) > 2 else 7
out = sys.argv[3] if len(sys.argv) > 3 else f'{SP}/storm/r3/tiles/v{v}_mid.png'
os.makedirs(os.path.dirname(out), exist_ok=True)
full = np.asarray(Image.open(f'{SP}/out2/bd_storm_v{v}_mid/r2_3.png').convert('RGB')).astype(float) / 255
src = resize_lin(full, 448, 2688)                      # the approved paint at its drawn density
rng = np.random.default_rng(500 + 31 * v + seed)

def tnoise(shape, kmin, kmax, beta, r, aniso=(1, 1)):
    """toroidal band-limited noise, unit std"""
    H, W = shape
    ky = np.fft.fftfreq(H) * H / aniso[0]; kx = np.fft.fftfreq(W) * W / aniso[1]
    K = np.hypot(ky[:, None] * (W / H), kx[None, :])    # in cycles per canvas WIDTH
    amp = np.where((K >= kmin) & (K <= kmax), 1 / np.maximum(K, 1e-6) ** beta, 0)
    f = amp * (r.standard_normal((H, W)) + 1j * r.standard_normal((H, W)))
    z = np.real(np.fft.ifft2(f)); return (z - z.mean()) / (z.std() + 1e-9)

yy, xx = np.mgrid[0:OH, 0:OW].astype(float)
# belt phase: (a, b) integer turns across / down keep it seamless; heavy low-freq warp makes belts wander, merge, break
a, b = (1, 5) if v != 2 else (1, 4)
warp = 1.6 * tnoise((OH, OW), .6, 2.2, 1.2, rng) + .5 * tnoise((OH, OW), 2, 5, 1, rng)
phase = 2 * np.pi * (a * xx / OW + b * yy / OH) + warp
belts = np.sin(phase)
belts = np.sign(belts) * np.abs(belts) ** .7
envelope = .55 + .45 * np.tanh(1.3 * tnoise((OH, OW), .5, 2, 1, rng))       # belts fade in and out
cells = tnoise((OH, OW), 1.5, 9, 1.4, rng)                                   # cloud masses / clearings
# a few eddy cells: local swirl of the cell field (domain rotation), toroidal by construction (wrapped distance)
swx, swy = xx.copy(), yy.copy()
for _ in range(rng.integers(3, 6)):
    cx, cy, R = rng.uniform(0, OW), rng.uniform(0, OH), rng.uniform(140, 260)
    dx = (xx - cx + OW / 2) % OW - OW / 2; dy = (yy - cy + OH / 2) % OH - OH / 2
    d = np.hypot(dx, dy); ang = rng.choice([-1, 1]) * 2.4 * np.exp(-(d / R) ** 2)
    c, s = np.cos(ang), np.sin(ang)
    swx += (c * dx - s * dy) - dx; swy += (s * dx + c * dy) - dy
cells = map_coordinates(cells, [swy % OH, swx % OW], order=1, mode='grid-wrap')
belts = map_coordinates(belts * envelope, [swy % OH, swx % OW], order=1, mode='grid-wrap')
field = .6 * belts + .55 * cells
field = gaussian_filter(field, 10, mode='wrap')
# colour by quantile from r2_3's own low frequencies (same palette, same exposure distribution)
srcpad = np.pad(src, ((0, 208), (0, 208), (0, 0)), mode='wrap')
low = gaussian_filter(src, (24, 24, 0), mode='wrap').reshape(-1, 3)
lum = low @ np.array([.3, .5, .2]); order = np.argsort(lum); lows = low[order]
nb = 256; edges = np.linspace(0, len(lows), nb + 1).astype(int)
lut = np.stack([lows[edges[i]:edges[i + 1]].mean(0) for i in range(nb)])
q = np.argsort(np.argsort(field.ravel())) / field.size
guide = lut[np.clip((q * nb).astype(int), 0, nb - 1)].reshape(OH, OW, 3)
guide = gaussian_filter(guide, (24, 24, 0), mode='wrap')
canvas = np.zeros((OH, OW, 3)); filled = np.zeros((OH, OW), bool)
pos = [(y, x) for y in range(0, OH, 128) for x in range(0, OW, 128)]
res, f, used = synth2.run(srcpad, canvas, filled, pos, P=208, B=80, guide=guide, guide_w=1.6, seed=seed + v,
                          sig=(0, 1, 2, 4, 8, 12), need=.99, reuse_pen=.9)
# same exposure as r2_3 (per channel), and erase any residual brightness step at the wraps
res = np.clip(res * (src.mean((0, 1)) / res.mean((0, 1))), 0, 1)
def feather_wrap(a, R=24):
    a = a.copy()
    for axis in (1, 0):
        t = a if axis == 1 else np.swapaxes(a, 0, 1)
        d = gaussian_filter(t[:, 0] - t[:, -1], (3, 0))
        w = (1 - np.arange(R) / R)[None, :, None] * .5
        t[:, :R] -= d[:, None] * w; t[:, -R:] += d[:, None] * w[:, ::-1]
        a = t if axis == 1 else np.swapaxes(t, 0, 1)
    return np.clip(a, 0, 1)
res = feather_wrap(res)
save(res, out); save(guide, out.replace('.png', '_guide.png'))
print('done', v, out)
