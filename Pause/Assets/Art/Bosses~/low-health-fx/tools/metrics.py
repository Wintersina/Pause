import sys, glob, os
import numpy as np
from PIL import Image
R = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Resources/'
C = 384
NAMES = ["tell0_0", "tell0_1", "tell1_0", "tell1_1", "tell2_0", "tell2_1", "fire"]
FR = [5, 6, 7, 8, 10, 11, 9]

def erode(m, k):
    out = m.copy()
    for _ in range(k):
        o = out.copy()
        o[1:] &= out[:-1]; o[:-1] &= out[1:]; o[:, 1:] &= out[:, :-1]; o[:, :-1] &= out[:, 1:]
        out = o
    return out

def dilate(m, k):
    return ~erode(~m, k)

def stats(dmg, pose):
    da, pa = dmg[:, :, 3], pose[:, :, 3]
    D, P = da > 40, pa > 40
    outside = int((D & ~dilate(P, 12)).sum())
    inner = erode(D, 6)
    translucent = int(((da > 8) & (da < 200) & inner).sum())
    band = D & ~erode(D, 3) & P
    col = np.abs(dmg[:, :, :3].astype(int) - pose[:, :, :3].astype(int)).max(axis=2)
    edge_noise = float(((col > 60) & band).sum() / max(1, band.sum()))
    return outside, translucent, edge_noise

if __name__ == '__main__':
    base = sys.argv[1]   # dir with <key>_<pose>_stage<N>.png
    for k in sys.argv[2:]:
        body = np.asarray(Image.open(R + 'Bosses/%s.png' % k).convert('RGBA'))
        for st in (1, 2, 3, 4):
            tot = [0, 0, 0.0]; n = 0
            for nm, f in zip(NAMES, FR):
                p = base + '/%s_%s_stage%d.png' % (k.lower(), nm, st)
                if not os.path.exists(p):
                    continue
                d = np.asarray(Image.open(p).convert('RGBA'))
                pose = body[(f // 5) * C:(f // 5 + 1) * C, (f % 5) * C:(f % 5 + 1) * C]
                o, t, e = stats(d, pose)
                tot[0] += o; tot[1] += t; tot[2] += e; n += 1
            if n:
                print(k, 'stage', st, 'n', n, 'outside', tot[0], 'translucent_inner', tot[1], 'edge_noise_avg %.3f' % (tot[2] / n))
