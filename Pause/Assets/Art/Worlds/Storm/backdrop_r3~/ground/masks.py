"""Storm r3 ground masks (GroundMask format, row 0 = top): 64 x 292 cells of 14 px on the 896 x 4096 tile.
O = lit CLOUD TOPS (raised bright masses: refineries, masts, lightning cells stand / glow here),
W = dark LANES and gaps between the masses (skimmers, wrecks, creatures, convoys sink / fly here),
C = everything else (textured mid deck). Read off the drawing: smoothed luminance quantiles, majority-cleaned."""
import sys, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, median_filter
def mask(v):
    a = np.asarray(Image.open(f'tiles/v{v}_mid.png').convert('RGB')).astype(float) / 255
    L = a @ np.array([.3, .5, .2])
    big = gaussian_filter(L, 18, mode='wrap')
    H, W = L.shape; R, C = 292, 64
    ys = ((np.arange(R) + .5) * H / R).astype(int); xs = ((np.arange(C) + .5) * W / C).astype(int)
    g = big[np.ix_(ys, xs)]
    lo, hi = np.quantile(g, .30), np.quantile(g, .74)
    k = np.where(g <= lo, 0, np.where(g >= hi, 2, 1))
    k = median_filter(k, size=3, mode='wrap')
    return np.array(list('WCO'))[k]
if __name__ == '__main__':
    for v in (1, 2, 3, 4):
        m = mask(v); open(f'tiles/v{v}_mask.txt', 'w').write('\n'.join(''.join(r) for r in m) + '\n')
        print(v, {c: round(float((m == c).mean()), 2) for c in 'OCW'})
