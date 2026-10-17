import sys, colorsys
sys.path.insert(0, '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg')
from common import *
TARGETS['space'] = ['eventide_bastion', 'orbit_reaver', 'rift_lancer', 'singularity_hauler']
bad = 0
for w, n, key in keys():
    d = defn(key); c = d['cells']; fl = c['flight'][0]; dm = c['damaged']
    im, h, cs = strip(respath(w, key))
    f = np.asarray(cs[fl]).astype(int); g = np.asarray(cs[dm]).astype(int)
    nt = min(z['y'] for z in d['nozzles']); hr = nt - 2
    sa = f[..., 3] >= 96; sb = g[..., 3] >= 96
    A, B = sa[:hr], sb[:hr]
    iou = (A & B).sum() / max(1, (A | B).sum())
    diff = np.abs(f[:hr, :, :3] - g[:hr, :, :3]).max(-1) > 35
    ch = ((A != B) | (A & B & diff)).sum() / max(1, A.sum())
    rgb = g[nt + 4:, :, :3]; sat = rgb.max(-1) - rgb.min(-1)
    flame = ((g[nt + 4:, :, 3] >= 200) & (sat > 90)).sum()
    ys, xs = np.nonzero(sb)
    marg = min(xs.min(), ys.min(), 383 - xs.max(), 383 - ys.max())
    pink = 0
    for (y, x) in zip(*np.nonzero(sb))[::1] if False else []: pass
    hsv = np.array([colorsys.rgb_to_hsv(*(p / 255)) for p in g[sb][::3, :3]]) if sb.any() else np.zeros((1, 3))
    pk = ((hsv[:, 1] > .35) & (hsv[:, 2] > .4) & (hsv[:, 0] > .78) & (hsv[:, 0] < .96)).sum() * 3
    hf = np.array([colorsys.rgb_to_hsv(*(p / 255)) for p in f[sa][::3, :3]])
    pf = ((hf[:, 1] > .35) & (hf[:, 2] > .4) & (hf[:, 0] > .78) & (hf[:, 0] < .96)).sum() * 3
    print('   flight pink~', pf)
    pk = max(0, pk - pf)
    ok = iou >= .85 and ch >= .04 and flame >= 120 and marg >= 6 and (w == 'space' or pk <= 8)
    if not ok: bad += 1
    print(f'{key:34s} iou {iou:.3f} chg {ch*100:5.1f}% flame {flame:4d} margin {marg:3d} pink~{pk} {"OK" if ok else "FAIL"}')
print('fails', bad)
