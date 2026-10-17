import numpy as np, sys
from fxlib import *
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'
import os
os.makedirs(W + 'out', exist_ok=True)

def plumes(cell):
    a = cell[:, :, 3]
    s = gap_split(a, 150, 260)
    return s

def mirror_replace(cell, src, keep_side):
    """Replace the OTHER plume of `cell` by the horizontal mirror of src's keep_side plume."""
    s_c = plumes(cell)
    s_s = plumes(src)
    out = np.zeros_like(cell)
    srcL, srcR = src.copy(), src.copy()
    srcL[:, s_s:] = 0
    srcR[:, :s_s] = 0
    curL, curR = cell.copy(), cell.copy()
    curL[:, s_c:] = 0
    curR[:, :s_c] = 0
    if keep_side == 'right':      # rebuild the left plume from a mirrored right plume
        keep = curR
        donor = srcR
        tgt_old = curL
    else:
        keep = curL
        donor = srcL
        tgt_old = curR
    bx0, by0, bx1, by1 = bbox(donor[:, :, 3])
    ox0, oy0, ox1, oy1 = bbox(tgt_old[:, :, 3])
    m = donor[:, ::-1].copy()                       # mirror about the cell centre
    mx0, mx1 = C - 1 - bx1, C - 1 - bx0             # mirrored bbox x range
    dx = ((ox0 + ox1) - (mx0 + mx1)) // 2           # centre it on the old plume
    new = shifted(m, dx, 0)
    # composite (the plumes do not overlap)
    out = keep.copy()
    mask = new[:, :, 3] > 0
    out[mask] = new[mask]
    return out

def sanity(name, cell):
    print(name, 'straight', longest_straight(cell[:, :, 3]))

def run_frost():
    fx = load(W + 'orig/Frost_damage_fx.png')
    out = fx.copy()
    cells = [cell_of(fx, 0, c).copy() for c in range(6)]
    for c in range(6):
        new = mirror_replace(cells[c], cells[(c + 2) % 6], 'right')
        new = declip_plumes(new, depth=16, minrun=14)
        out[0:C, c * C:(c + 1) * C] = new
    return out

def run_desert():
    fx = load(W + 'orig/Desert_damage_fx.png')
    out = fx.copy()
    cells = [cell_of(fx, 0, c).copy() for c in range(6)]
    for c in range(6):
        new = mirror_replace(cells[c], cells[(c + 2) % 6], 'left')
        new = declip_plumes(new, depth=24, minrun=10)
        out[0:C, c * C:(c + 1) * C] = new
    return out

def run_storm():
    fx = load(W + 'orig/Storm_damage_fx.png')
    out = fx.copy()
    for c in range(6):
        new = declip_plumes(cell_of(fx, 0, c).copy(), depth=24, minrun=10)
        out[0:C, c * C:(c + 1) * C] = new
    return out

def run_verdant():
    fx = load(W + 'orig/Verdant_damage_fx.png')
    out = fx.copy()
    c0 = cell_of(fx, 0, 0).copy()
    c1 = cell_of(fx, 0, 1).copy()
    c1[c1[:, :, 3] < 100] = 0                      # the ghost smudge layer
    c0, c1 = mirror_replace(c0, c1, 'right'), mirror_replace(c1, c0, 'right')   # the left plumes are clipped / cross-faded
    frames = [c0, c1,
              shifted(c0, 1, -2), shifted(c1, -1, -2),
              shifted(c0, 2, -3), shifted(c1, -2, -3)]
    # a second drift pass keeps the 5 -> 0 wrap small
    frames[4] = shifted(c0, 1, -1)
    frames[5] = shifted(c1, -1, -1)
    frames[2] = shifted(c0, 0, 1)
    frames[3] = shifted(c1, 0, 1)
    for c in range(6):
        out[0:C, c * C:(c + 1) * C] = declip_plumes(frames[c], depth=14, minrun=14)
    return out

if __name__ == '__main__':
    for k, fn in (('Frost', run_frost), ('Desert', run_desert), ('Storm', run_storm), ('Verdant', run_verdant)):
        if len(sys.argv) > 1 and k not in sys.argv[1:]:
            continue
        o = fn()
        save(o, W + 'out/%s_smoke.png' % k)
        for c in range(6):
            sanity(k + str(c), cell_of(o, 0, c))
